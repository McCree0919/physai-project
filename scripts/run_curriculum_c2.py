"""Curriculum C2 (docs/LAYER3_C2_DESIGN.md): 2 seeds x 8M steps, CPU, 4 Unity instances, one seed after the other, each initialized from
the C0+C1 8M checkpoint of the same seed (--initialize-from; mlagents 1.1 loads <run>/Climb/checkpoint.pt, which the runner checks is
tensor-identical to the final Climb-<steps>.pt, restarts the step count at 0, and loads policy, normalizer, critic and optimizer state).
Everything as climb_curriculum_c0c1.yaml except the structure pools (structures/curriculum-v2/pools.json, weighted sub-pools c2-A..D).

Early stops agreed with the user (section 4.1), ground starts only, per seed:
  1 not learning: at summary step >= 2M, if any of c2-B, c2-C, c2-D has 0 ground-start successes in the last 500k steps.
  2 forgetting  : at every 1M steps, if the two-step or c2-A ground-start success rate over the last 500k steps is below 85%.
Both read the runtime episode CSVs (the step window is mapped to wall time through TensorBoard summary times) and TensorBoard
(Climb/Pool/<pool>/GroundSuccess over the same summary steps); a seed is stopped only when both sources meet the condition, and every check
is logged with both numbers. --smoke runs one short instance (1 env, SMOKE_EPISODES episodes, no early stops) and checks initialization, pool shares, spawns and logs.
Re-running resumes an unfinished seed (--resume, no re-initialization) and skips finished or early-stopped ones."""
import argparse
import csv
import datetime
import glob
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'results/climb-curriculum-c2'
LIBRARY = ROOT/'results/climb-snapshots-v2/library.jsonl'
POOLS = ROOT/'structures/curriculum-v2/pools.json'
PLAYER = ROOT/'Builds/ClimbCurriculumC2/Climb.exe'
PLAYER_MANIFEST = OUT/'player-sha256.txt'
CONFIG, PREFIX, BASE_PORT = 'config/climb_curriculum_c2.yaml', 'climb_curriculum_c2', 7600
SEEDS = [2026, 2027]
INIT = {2026: 'climb_curriculum_c0c1_s2026', 2027: 'climb_curriculum_c0c1_s2027'}
MAX_STEPS, WINDOW, LEARN_CHECK, FORGET_EVERY, FORGET_MIN = 8_000_000, 500_000, 2_000_000, 1_000_000, .85
TALL, KEEP = ('c2-B', 'c2-C', 'c2-D'), ('two-step', 'c2-A')
POLL_SECONDS = 120
SMOKE_EPISODES, SMOKE_SECONDS, SMOKE_SHARE_TOLERANCE = 2000, 2*3600, .03  # 2000 episodes: share SE <= 0.010, the +-0.03 tolerance is >= 3 SE


def log(message):
    line = datetime.datetime.now().isoformat(timespec='seconds')+' '+message
    with (OUT/'runner.log').open('a', encoding='utf-8') as stream:stream.write(line+'\n')
    print(line, flush=True)


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def checkpoints(run_id):
    path = ROOT/'results'/run_id/'run_logs/training_status.json'
    if not path.exists():return []
    return [c for c in json.loads(path.read_text())['Climb']['checkpoints'] if Path(ROOT/c['file_path']).exists()]


def trained(run_id):
    return (ROOT/'results'/run_id/'Climb.onnx').exists() and any(c['steps'] >= MAX_STEPS-10_000 for c in checkpoints(run_id))


def check_initialization(init_run):
    """checkpoint.pt (what --initialize-from loads) must equal the final checkpoint of the C0+C1 run, tensor by tensor."""
    import torch
    status = json.loads((ROOT/'results'/init_run/'run_logs/training_status.json').read_text())['Climb']['final_checkpoint']
    final = ROOT/'results'/init_run/'Climb'/f"Climb-{status['steps']}.pt";default = ROOT/'results'/init_run/'Climb/checkpoint.pt'

    def flat(d, p=''):
        for k, v in (d.items() if isinstance(d, dict) else enumerate(d)):
            if isinstance(v, (dict, list, tuple)):yield from flat(v, f'{p}{k}/')
            else:yield f'{p}{k}', v
    a, b = dict(flat(torch.load(default, map_location='cpu'))), dict(flat(torch.load(final, map_location='cpu')))
    same = a.keys() == b.keys() and all(torch.equal(a[k], b[k]) if torch.is_tensor(a[k]) else a[k] == b[k] for k in a)
    if not same:raise RuntimeError(f'{default} differs from {final}; refusing to initialize from an unexpected checkpoint')
    return dict(initRun=init_run, finalCheckpoint=str(final.relative_to(ROOT)), steps=status['steps'], entries=len(a), checkpointSha256=sha(default), finalSha256=sha(final))


class Monitor:
    def __init__(self, run_id, runtime):
        self.run_id, self.runtime, self.seen = run_id, runtime, set()

    def scalars(self):
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
        data = {}
        for f in sorted(glob.glob(str(ROOT/'results'/self.run_id/'Climb/events.out*'))):  # a resumed run writes a second events file
            accumulator = EventAccumulator(f, size_guidance={'scalars': 0});accumulator.Reload()
            for tag in accumulator.Tags()['scalars']:data.setdefault(tag, []).extend((e.step, e.wall_time, e.value) for e in accumulator.Scalars(tag))
        return {k: sorted(v) for k, v in data.items()}

    def episodes(self):
        rows = []
        for f in glob.glob(str(self.runtime/'*.csv')):
            with open(f, encoding='utf-8', newline='') as stream:rows.extend(r for r in csv.DictReader(stream) if r.get('pool') is not None)
        return rows

    @staticmethod
    def rates(rows):
        out = {}
        for pool in ('two-step',)+KEEP[1:]+TALL:
            for spawn in ('ground', 'demo'):
                sel = [r for r in rows if r['pool'] == pool and (r['spawn'] == 'ground') == (spawn == 'ground')]
                if sel:out[f'{pool}/{spawn}'] = dict(n=len(sel), level1=sum(r['level1'] == '1' for r in sel), level2=sum(r['level2'] == '1' for r in sel), success=sum(r['success'] == '1' for r in sel))
        return out

    def window(self, data, rows, step):
        """Ground-start episodes and TensorBoard GroundSuccess summaries of each pool within summary steps (step-WINDOW, step]."""
        clock = data.get('Climb/Success', [])
        inside = [w for s, w, _ in clock if step-WINDOW < s <= step]
        if not inside:return None
        start_ms = (min(inside)-(clock[1][1]-clock[0][1] if len(clock) > 1 else 0))*1000  # include the summary period before the first one
        end_ms = max(inside)*1000
        out = {}
        for pool in KEEP+TALL:
            sel = [r for r in rows if r['pool'] == pool and r['spawn'] == 'ground' and start_ms < int(r['unix_ms']) <= end_ms]
            tb = [v for s, _, v in data.get(f'Climb/Pool/{pool}/GroundSuccess', []) if step-WINDOW < s <= step]
            out[pool] = dict(csvEpisodes=len(sel), csvSuccesses=sum(r['success'] == '1' for r in sel), csvRate=round(sum(r['success'] == '1' for r in sel)/len(sel), 4) if sel else None,
                             tbSummaries=len(tb), tbMean=round(sum(tb)/len(tb), 4) if tb else None, tbMax=max(tb) if tb else None)
        return out

    def once(self, key, message):
        if key not in self.seen:self.seen.add(key);log(message)

    def poll(self, early_stops=True):
        """Returns early-stop evidence when an agreed condition holds in both sources, else None."""
        try:data = self.scalars()
        except Exception as error:log(f'MONITOR {self.run_id} read failed {error!r}');return None
        clock = data.get('Climb/Success', [])
        if not clock:return None
        step = clock[-1][0]
        bucket = step//500_000
        if bucket and ('status', bucket) not in self.seen:
            def mean(tag, n=20):
                values = [v for _, _, v in data.get(tag, [])][-n:]
                return round(sum(values)/len(values), 4) if values else None
            pools = {t[len('Climb/Pool/'):]: mean(t) for t in data if t.startswith('Climb/Pool/')}
            self.once(('status', bucket), f'STATUS {self.run_id} step={step} success={mean("Climb/Success")} level1={mean("Climb/Level1Rate")} '
                      f'twoStepShare={mean("Climb/Curriculum/TwoStepShare")} demoActual={mean("Climb/Spawn/ActualDemoFraction")} '
                      f'pools(last20)={json.dumps(pools, sort_keys=True)} episodeRates={json.dumps(self.rates(self.episodes()))}')
        if not early_stops:return None
        rows = None
        if step >= LEARN_CHECK and 'learn' not in self.seen:
            rows = self.episodes();w = self.window(data, rows, step)
            if w:
                zero = [p for p in TALL if w[p]['csvEpisodes'] and w[p]['csvSuccesses'] == 0 and not (w[p]['tbMax'] or 0) > 0]
                evidence = dict(check='not-learning', runId=self.run_id, summaryStep=step, window=WINDOW, pools=w, triggered=zero)
                self.once('learn', f'EARLY_STOP_CHECK {json.dumps(evidence)}')
                if zero:return evidence
        for k in range(1, step//FORGET_EVERY+1):
            if ('forget', k) in self.seen or step < k*FORGET_EVERY:continue
            rows = rows if rows is not None else self.episodes();w = self.window(data, rows, step)
            if not w:continue
            low = [p for p in KEEP if w[p]['csvRate'] is not None and w[p]['csvRate'] < FORGET_MIN and w[p]['tbMean'] is not None and w[p]['tbMean'] < FORGET_MIN]
            evidence = dict(check='forgetting', at=k*FORGET_EVERY, runId=self.run_id, summaryStep=step, window=WINDOW, pools=w, triggered=low)
            self.once(('forget', k), f'EARLY_STOP_CHECK {json.dumps(evidence)}')
            if low:return evidence
        return None


def kill_tree(pid):
    subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def smoke_check(run_id, folder, rows):
    """Stop conditions of the smoke run (user, 2026-11-11): exceptions, rejected structure swaps, sub-pool shares off by more than
    SMOKE_SHARE_TOLERANCE, demonstration starts outside the training two-step, and episodes whose structure is not in their pool."""
    pools = json.loads(POOLS.read_text(encoding='utf-8'));stage = pools['stages'][0];two = pools['twoStep']['id']
    total = sum(q['weight'] for q in stage['pools'])
    expected = {'two-step': pools['twoStepShare'], **{q['pool']: (1-pools['twoStepShare'])*q['weight']/total for q in stage['pools']}}
    members = {'two-step': {two}, **{q['pool']: {e['id'] for e in q['structures']} for q in stage['pools']}}
    failures = []
    shares = {k: dict(expected=round(v, 4), observed=round(sum(r['pool'] == k for r in rows)/len(rows), 4)) for k, v in expected.items()}
    for k, v in shares.items():
        if abs(v['observed']-v['expected']) > SMOKE_SHARE_TOLERANCE:failures.append(f'share {k} {v["observed"]} vs {v["expected"]}')
    unknown = sorted({r['pool'] for r in rows}-set(expected))
    if unknown:failures.append(f'unknown pools {unknown}')
    outside = [r for r in rows if r['pool'] in members and r['structure'] not in members[r['pool']]]
    if outside:failures.append(f'{len(outside)} episodes on a structure outside their pool, e.g. {outside[0]["structure"]}/{outside[0]["pool"]}')
    demo = [r for r in rows if r['spawn'] != 'ground']
    bad_demo = [r for r in demo if r['pool'] != 'two-step' or r['structure'] != two]
    if bad_demo:failures.append(f'{len(bad_demo)} demonstration starts outside the two-step, e.g. {bad_demo[0]["structure"]}')
    logs = sorted(glob.glob(str(ROOT/'results'/run_id/'run_logs/Player-*.log')))+[str(folder/'console.txt')]
    errors = []
    for f in logs:
        for line in open(f, encoding='utf-8', errors='replace'):
            if 'Exception' in line or 'error CS' in line or 'Traceback' in line:errors.append(f'{Path(f).name}: {line.strip()[:300]}')
    if not any('Player-' in f for f in logs):failures.append('no player log')
    if errors:failures.append(f'{len(errors)} exception lines, first: {errors[0]}')
    return dict(ok=not failures, failures=failures, episodes=len(rows), shares=shares, demoStarts=len(demo), demoOnTwoStep=len(demo)-len(bad_demo),
                structuresSeen={k: len({r['structure'] for r in rows if r['pool'] == k}) for k in expected}, logsScanned=logs, exceptionLines=errors[:20])


def train(seed, index, smoke=False):
    run_id = f'{PREFIX}_smoke_s{seed}' if smoke else f'{PREFIX}_s{seed}';folder = OUT/run_id;folder.mkdir(parents=True, exist_ok=True)
    if not smoke and trained(run_id):log(f'SKIP train {run_id} (complete)');return
    if (folder/'early_stop.json').exists():log(f'SKIP train {run_id} (early-stopped)');return
    resume = (ROOT/'results'/run_id).exists()
    if smoke and resume:raise RuntimeError(f'{run_id} exists; use a new smoke run-id')
    command = [sys.executable, str(ROOT/'scripts/climb_train_worker.py'), '--train-device', 'cpu', '--train-record', str(folder/'device.jsonl'),
               CONFIG, '--env', str(PLAYER.relative_to(ROOT)), '--run-id', run_id, '--seed', str(seed), '--num-envs', '1' if smoke else '4',
               '--base-port', str(BASE_PORT+index*10+(5 if smoke else 0)), '--no-graphics', '--env-args', '--climb-snapshot-library='+str(LIBRARY),
               '--climb-curriculum='+str(POOLS), '--climb-output='+str(folder/'runtime')]
    if resume:command.insert(command.index('--env-args'), '--resume')
    else:
        init = check_initialization(INIT[seed]);(folder/'initialization.json').write_text(json.dumps(init, indent=2)+'\n', encoding='utf-8')
        command.insert(command.index('--env-args'), '--initialize-from='+INIT[seed])
        log(f'INIT {run_id} {json.dumps(init)}')
    (folder/'command.json').write_text(json.dumps(command, indent=2)+'\n', encoding='utf-8')
    log(f'START train {run_id} resume={resume} smoke={smoke}')
    started = datetime.datetime.now();monitor = Monitor(run_id, folder/'runtime');stopped = None
    with (folder/'console.txt').open('a', encoding='utf-8') as stream:
        process = subprocess.Popen(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        while process.poll() is None:
            if smoke:
                time.sleep(20)
                rows = monitor.episodes()
                if len(rows) >= SMOKE_EPISODES:kill_tree(process.pid);process.wait();break
                if time.time()-started.timestamp() > SMOKE_SECONDS:kill_tree(process.pid);process.wait();raise RuntimeError(f'smoke run did not produce {SMOKE_EPISODES} episodes in {SMOKE_SECONDS} s')
                continue
            time.sleep(POLL_SECONDS);stopped = monitor.poll()
            if stopped:
                (folder/'early_stop.json').write_text(json.dumps(stopped, indent=2)+'\n', encoding='utf-8')
                log(f'EARLY_STOP {run_id}: {stopped["check"]} {stopped["triggered"]}; stopping this seed')
                kill_tree(process.pid);process.wait();break
    if smoke:
        rows = monitor.episodes()
        log(f'SMOKE {run_id} episodes={len(rows)} rates={json.dumps(monitor.rates(rows))} pools={json.dumps({p: sum(r["pool"] == p for r in rows) for p in sorted({r["pool"] for r in rows})})} '
            f'spawns={json.dumps({s: sum(r["spawn"] == s for r in rows) for s in sorted({r["spawn"] for r in rows})})}')
        check = smoke_check(run_id, folder, rows);(folder/'smoke_check.json').write_text(json.dumps(check, indent=2)+'\n', encoding='utf-8')
        log(f'SMOKE_CHECK {run_id} ok={check["ok"]} failures={json.dumps(check["failures"])} shares={json.dumps(check["shares"])}')
        if not check['ok']:raise RuntimeError(f'smoke check failed: {check["failures"]}')
        return
    if not stopped:monitor.poll()
    seconds = (datetime.datetime.now()-started).total_seconds()
    with (folder/'timing.jsonl').open('a', encoding='utf-8') as stream:stream.write(json.dumps(dict(started=started.isoformat(), seconds=seconds, exitCode=process.returncode, resume=resume, earlyStop=bool(stopped)))+'\n')
    if stopped:return
    if not trained(run_id):raise RuntimeError(f'{run_id} did not reach {MAX_STEPS} steps (exit {process.returncode}); see {folder}/console.txt')
    log(f'DONE train {run_id} {seconds:.0f}s exit={process.returncode}')


def main():
    p = argparse.ArgumentParser();p.add_argument('--smoke', action='store_true');a = p.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    for path in (LIBRARY, POOLS, PLAYER, PLAYER_MANIFEST):
        if not path.exists():raise FileNotFoundError(path)
    for line in PLAYER_MANIFEST.read_text().splitlines():  # every file of the build: the learning code lives in Climb_Data/Managed
        if line.strip():
            digest, name = line.split(' ', 1)
            if sha(PLAYER.parent/name.lstrip('*')) != digest:raise RuntimeError(f'player build file {name} does not match the recorded hash')
    log(f'RUNNER start pid={os.getpid()} smoke={a.smoke} config={CONFIG} library_sha256={sha(LIBRARY)} pools_sha256={sha(POOLS)} player_sha256={sha(PLAYER)}')
    for index, seed in enumerate(SEEDS[:1] if a.smoke else SEEDS):train(seed, index, a.smoke)
    log('SMOKE_COMPLETE' if a.smoke else 'ALL_TRAINING_COMPLETE')


if __name__ == '__main__':
    try:main()
    except Exception as error:
        log('FAILED '+repr(error));raise
