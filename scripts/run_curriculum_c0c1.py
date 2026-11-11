"""Curriculum C0+C1 feasibility check (LAYER3_DESIGN §17): 2 seeds x 8M steps, CPU, 4 Unity instances, run one seed after the other.
Everything as climb_height_shaping.yaml except the structure distribution (--climb-curriculum pools, fixed schedule C0 3M then C1 5M).
A monitor polls TensorBoard and the runtime episode CSVs and logs per-pool rates every 500k steps, the C0 check when C1 starts, and the
early stop agreed with the user: 2M steps into C1, if no ground-start episode on a C1 structure has reached Level 1, that seed's training
is stopped and reported (next experiment: record a state library on new structures). Evaluation is a separate step.
Re-running resumes an unfinished seed and skips finished or early-stopped ones."""
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
OUT = ROOT/'results/climb-curriculum-c0c1'
LIBRARY = ROOT/'results/climb-snapshots-v2/library.jsonl'
POOLS = ROOT/'structures/curriculum-v1/pools.json'
PLAYER = ROOT/'Builds/ClimbCurriculum/Climb.exe'
CONFIG, PREFIX, BASE_PORT = 'config/climb_curriculum_c0c1.yaml', 'climb_curriculum_c0c1', 7300
SEEDS = [2026, 2027]
MAX_STEPS, C0_STEPS, EARLY_STOP_C1_STEPS = 8_000_000, 3_000_000, 2_000_000
POLL_SECONDS = 120


def log(message):
    line = datetime.datetime.now().isoformat(timespec='seconds')+' '+message
    with (OUT/'runner.log').open('a', encoding='utf-8') as stream:stream.write(line+'\n')
    print(line, flush=True)


def checkpoints(run_id):
    path = ROOT/'results'/run_id/'run_logs/training_status.json'
    if not path.exists():return []
    return [c for c in json.loads(path.read_text())['Climb']['checkpoints'] if Path(ROOT/c['file_path']).exists()]


def trained(run_id):
    return (ROOT/'results'/run_id/'Climb.onnx').exists() and any(c['steps'] >= MAX_STEPS-10_000 for c in checkpoints(run_id))


class Monitor:
    def __init__(self, run_id, runtime):
        self.run_id, self.runtime, self.seen, self.c1Start = run_id, runtime, set(), None

    def scalars(self):
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
        files = sorted(glob.glob(str(ROOT/'results'/self.run_id/'Climb/events.out*')))
        data = {}
        for f in files:  # a resumed run writes a second events file
            accumulator = EventAccumulator(f, size_guidance={'scalars': 0});accumulator.Reload()
            for tag in accumulator.Tags()['scalars']:data.setdefault(tag, []).extend((e.step, e.value) for e in accumulator.Scalars(tag))
        return {k: sorted(v) for k, v in data.items()}

    def episodes(self):
        rows = []
        for f in glob.glob(str(self.runtime/'*.csv')):
            with open(f, encoding='utf-8', newline='') as stream:rows.extend(r for r in csv.DictReader(stream) if r.get('pool') is not None)
        return rows

    @staticmethod
    def rates(rows):
        out = {}
        for pool in ('two-step', 'c0', 'c1'):
            for spawn in ('ground', 'demo'):
                sel = [r for r in rows if r['pool'] == pool and (r['spawn'] == 'ground') == (spawn == 'ground')]
                if sel:out[f'{pool}/{spawn}'] = dict(n=len(sel), level1=sum(r['level1'] == '1' for r in sel), level2=sum(r['level2'] == '1' for r in sel), success=sum(r['success'] == '1' for r in sel))
        return out

    def once(self, key, message):
        if key not in self.seen:self.seen.add(key);log(message)

    def poll(self):
        """Returns an early-stop evidence dict when the agreed condition holds, else None."""
        try:data = self.scalars()
        except Exception as error:log(f'MONITOR {self.run_id} read failed {error!r}');return None
        stage = data.get('Climb/Curriculum/Stage', [])
        if not stage:return None
        step = stage[-1][0]
        if self.c1Start is None:
            c1 = [s for s, v in stage if v > 0]
            if c1:
                self.c1Start = c1[0];rows = self.episodes()
                self.once('c0check', f'C0_CHECK {self.run_id} C1 first seen at summary step={self.c1Start} episodeRates={json.dumps(self.rates(rows))}')
        bucket = step//500_000
        if bucket and ('status', bucket) not in self.seen:
            def mean(tag, n=20):
                values = [v for _, v in data.get(tag, [])][-n:]
                return round(sum(values)/len(values), 4) if values else None
            pools = {t[len('Climb/Pool/'):]: mean(t) for t in data if t.startswith('Climb/Pool/')}
            self.once(('status', bucket), f'STATUS {self.run_id} step={step} stage(last20)={mean("Climb/Curriculum/Stage")} success={mean("Climb/Success")} '
                      f'level1={mean("Climb/Level1Rate")} twoStepShare={mean("Climb/Curriculum/TwoStepShare")} demoActual={mean("Climb/Spawn/ActualDemoFraction")} '
                      f'pools(last20)={json.dumps(pools, sort_keys=True)} episodeRates={json.dumps(self.rates(self.episodes()))}')
        if self.c1Start is not None and step >= self.c1Start+EARLY_STOP_C1_STEPS and 'earlycheck' not in self.seen:
            rows = [r for r in self.episodes() if r['pool'] == 'c1' and r['spawn'] == 'ground']
            level1 = sum(r['level1'] == '1' for r in rows)
            tb = [v for s, v in data.get('Climb/Pool/c1/GroundLevel1Rate', []) if s >= self.c1Start]
            evidence = dict(runId=self.run_id, summaryStep=step, c1Start=self.c1Start, c1GroundEpisodes=len(rows), c1GroundLevel1=level1,
                            tensorboardMaxC1GroundLevel1Rate=max(tb) if tb else None, tensorboardSummaries=len(tb))
            self.once('earlycheck', f'EARLY_STOP_CHECK {self.run_id} {json.dumps(evidence)}')
            if rows and level1 == 0 and not any(v > 0 for v in tb):return evidence
        return None


def kill_tree(pid):
    subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def train(seed, index):
    run_id = f'{PREFIX}_s{seed}';folder = OUT/run_id;folder.mkdir(parents=True, exist_ok=True)
    if trained(run_id):log(f'SKIP train {run_id} (complete)');return
    if (folder/'early_stop.json').exists():log(f'SKIP train {run_id} (early-stopped)');return
    resume = (ROOT/'results'/run_id).exists()
    command = [sys.executable, str(ROOT/'scripts/climb_train_worker.py'), '--train-device', 'cpu', '--train-record', str(folder/'device.jsonl'),
               CONFIG, '--env', str(PLAYER.relative_to(ROOT)), '--run-id', run_id, '--seed', str(seed), '--num-envs', '4',
               '--base-port', str(BASE_PORT+index*10), '--no-graphics', '--env-args', '--climb-snapshot-library='+str(LIBRARY),
               '--climb-curriculum='+str(POOLS), '--climb-output='+str(folder/'runtime')]
    if resume:command.append('--resume')
    (folder/'command.json').write_text(json.dumps(command, indent=2)+'\n', encoding='utf-8')
    log(f'START train {run_id} resume={resume}')
    started = datetime.datetime.now();monitor = Monitor(run_id, folder/'runtime');stopped = None
    with (folder/'console.txt').open('a', encoding='utf-8') as stream:
        process = subprocess.Popen(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        while process.poll() is None:
            time.sleep(POLL_SECONDS);stopped = monitor.poll()
            if stopped:
                (folder/'early_stop.json').write_text(json.dumps(stopped, indent=2)+'\n', encoding='utf-8')
                log(f'EARLY_STOP {run_id}: no ground-start Level 1 on C1 structures within {EARLY_STOP_C1_STEPS} C1 steps; stopping this seed')
                kill_tree(process.pid);process.wait();break
    if not stopped:monitor.poll()
    seconds = (datetime.datetime.now()-started).total_seconds()
    with (folder/'timing.jsonl').open('a', encoding='utf-8') as stream:stream.write(json.dumps(dict(started=started.isoformat(), seconds=seconds, exitCode=process.returncode, resume=resume, earlyStop=bool(stopped)))+'\n')
    if stopped:return
    if not trained(run_id):raise RuntimeError(f'{run_id} did not reach {MAX_STEPS} steps (exit {process.returncode}); see {folder}/console.txt')
    log(f'DONE train {run_id} {seconds:.0f}s exit={process.returncode}')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for path in (LIBRARY, POOLS, PLAYER):
        if not path.exists():raise FileNotFoundError(path)
    digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    log(f'RUNNER start pid={os.getpid()} config={CONFIG} library_sha256={digest(LIBRARY)} pools_sha256={digest(POOLS)} player_sha256={digest(PLAYER)}')
    for index, seed in enumerate(SEEDS):train(seed, index)
    log('ALL_TRAINING_COMPLETE')


if __name__ == '__main__':
    try:main()
    except Exception as error:
        log('FAILED '+repr(error));raise
