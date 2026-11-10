"""Demonstration state initialisation only (incremental, everything else as climb_target_incremental.yaml):
2 seeds x 5M steps, CPU, 4 Unity instances, then the target-representation evaluation protocol (deterministic,
200 ground-spawn episodes at the ~1M/2.5M/5M checkpoints). While each run trains, a monitor polls TensorBoard and
logs when Climb/RewardProgress first becomes non-zero, an ALERT if it is still zero after 1M steps, the spawn mix and
quarantine shares, and the first Level 1 reached from a ground spawn. Re-running resumes like run_target_representation."""
import csv
import datetime
import glob
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'results/climb-demo-init'
LIBRARY = ROOT/'results/climb-snapshots-v2/library.jsonl'
SEEDS = [2026, 2027]
TARGETS = {'1M': 1_000_000, '2.5M': 2_500_000, '5M': 5_000_000}
MAX_STEPS = 5_000_000
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
    """Reads the run's TensorBoard events and runtime CSVs; logs milestones once."""
    def __init__(self, run_id, runtime):
        self.run_id, self.runtime, self.seen = run_id, runtime, set()
        self.firstProgress = self.firstGroundLevel1 = None

    def scalars(self):
        from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
        files = glob.glob(str(ROOT/'results'/self.run_id/'Climb/events.out*'))
        if not files:return {}
        accumulator = EventAccumulator(files[-1], size_guidance={'scalars': 0});accumulator.Reload()
        return {tag: [(e.step, e.value) for e in accumulator.Scalars(tag)] for tag in accumulator.Tags()['scalars']}

    def ground(self):
        rows = [r for f in glob.glob(str(self.runtime/'*.csv')) for r in csv.DictReader(open(f, encoding='utf-8'))]
        ground = [r for r in rows if r.get('spawn') == 'ground']
        return len(rows), len(ground), sum(r['level1'] == '1' for r in ground), sum(r['level2'] == '1' for r in ground), sum(r['success'] == '1' for r in ground)

    def once(self, key, message):
        if key not in self.seen:self.seen.add(key);log(message)

    def poll(self):
        try:data = self.scalars()
        except Exception as error:log(f'MONITOR {self.run_id} read failed {error!r}');return
        progress = data.get('Climb/RewardProgress', [])
        if not progress:return
        step = progress[-1][0]
        nonzero = [(s, v) for s, v in progress if v > 0]
        if nonzero and self.firstProgress is None:
            self.firstProgress = nonzero[0];self.once('progress', f'FIRST_NONZERO_REWARD_PROGRESS {self.run_id} step={nonzero[0][0]} value={nonzero[0][1]:.5f}')
        if step >= 1_000_000 and not nonzero:self.once('alert1M', f'ALERT {self.run_id} RewardProgress still zero after 1M steps (latest summary step={step})')
        total, ground, level1, level2, success = self.ground()
        if level1 and self.firstGroundLevel1 is None:
            self.firstGroundLevel1 = step;self.once('groundL1', f'FIRST_GROUND_SPAWN_LEVEL1 {self.run_id} near step={step} groundEpisodes={ground}')
        bucket = step//500_000
        if bucket and ('status', bucket) not in self.seen:
            def mean(tag, n=20):
                values = [v for _, v in data.get(tag, [])][-n:]
                return sum(values)/len(values) if values else float('nan')
            quarantine = {t.split('/')[-1]: round(mean(t), 4) for t in data if t.startswith('Climb/Spawn/QuarantineFraction/')}
            alert = ' QUARANTINE_ALERT>1%' if any(v > .01 for v in quarantine.values()) else ''
            self.once(('status', bucket), f'STATUS {self.run_id} step={step} rewardProgress(last20)={mean("Climb/RewardProgress"):.4f} level1Rate={mean("Climb/Level1Rate"):.3f} '
                      f'level2Rate={mean("Climb/Level2Rate"):.3f} success={mean("Climb/Success"):.3f} demoDrawn={mean("Climb/Spawn/DemoFraction"):.3f} '
                      f'demoActual={mean("Climb/Spawn/ActualDemoFraction"):.3f} quarantine={quarantine}{alert} '
                      f'groundEpisodes={ground}/{total} groundL1={level1} groundL2={level2} groundSuccess={success}')


def train(seed, index):
    run_id = f'climb_demo_init_s{seed}';folder = OUT/run_id;folder.mkdir(parents=True, exist_ok=True)
    if trained(run_id):log(f'SKIP train {run_id} (complete)');return run_id
    resume = (ROOT/'results'/run_id).exists()
    command = [sys.executable, str(ROOT/'scripts/climb_train_worker.py'), '--train-device', 'cpu', '--train-record', str(folder/'device.jsonl'),
               'config/climb_demo_init.yaml', '--env', 'Builds/Climb/Climb.exe', '--run-id', run_id, '--seed', str(seed), '--num-envs', '4',
               '--base-port', str(7100+index*10), '--no-graphics', '--env-args', '--climb-snapshot-library='+str(LIBRARY), '--climb-output='+str(folder/'runtime')]
    if resume:command.append('--resume')
    (folder/'command.json').write_text(json.dumps(command, indent=2)+'\n', encoding='utf-8')
    log(f'START train {run_id} resume={resume}')
    started = datetime.datetime.now();monitor = Monitor(run_id, folder/'runtime')
    with (folder/'console.txt').open('a', encoding='utf-8') as stream:
        process = subprocess.Popen(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        while process.poll() is None:
            time.sleep(POLL_SECONDS);monitor.poll()
    monitor.poll()
    seconds = (datetime.datetime.now()-started).total_seconds()
    with (folder/'timing.jsonl').open('a', encoding='utf-8') as stream:stream.write(json.dumps(dict(started=started.isoformat(), seconds=seconds, exitCode=process.returncode, resume=resume))+'\n')
    if monitor.firstProgress is None:log(f'NOTE {run_id} RewardProgress was zero for the whole run')
    if not trained(run_id):raise RuntimeError(f'{run_id} did not reach {MAX_STEPS} steps (exit {process.returncode}); see {folder}/console.txt')
    log(f'DONE train {run_id} {seconds:.0f}s exit={process.returncode}')
    return run_id


def evaluate(run_id):
    available = checkpoints(run_id)
    for label, target in TARGETS.items():
        chosen = min(available, key=lambda c: abs(c['steps']-target))
        folder = OUT/'eval'/run_id/label
        if (folder/'checkpoint/report.json').exists():log(f'SKIP eval {run_id} {label}');continue
        folder.mkdir(parents=True, exist_ok=True)
        model = ROOT/chosen['file_path']
        (folder/'checkpoint.json').write_text(json.dumps(dict(label=label, target=target, steps=chosen['steps'], model=str(model), sha256=hashlib.sha256(model.read_bytes()).hexdigest(), targetMode=0), indent=2)+'\n', encoding='utf-8')
        # Diagnostics player has no snapshot library: all evaluation episodes are normal ground spawns.
        command = [sys.executable, str(ROOT/'scripts/climb_behavior_diagnostics.py'), '--episodes', '200', '--modes', 'checkpoint', '--model', str(model),
                   '--target-mode', '0', '--output', str(folder), '--worker-id', '41']
        log(f'START eval {run_id} {label} steps={chosen["steps"]}')
        with (folder/'console.txt').open('w', encoding='utf-8') as stream:result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode != 0:raise RuntimeError(f'eval {run_id} {label} failed; see {folder}/console.txt')
        log(f'DONE eval {run_id} {label}')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    if not LIBRARY.exists():raise FileNotFoundError(LIBRARY)
    log('RUNNER start pid=%d library=%s sha256=%s' % (__import__('os').getpid(), LIBRARY, hashlib.sha256(LIBRARY.read_bytes()).hexdigest()))
    for index, seed in enumerate(SEEDS):
        run_id = train(seed, index);evaluate(run_id)
    log('ALL_COMPLETE')


if __name__ == '__main__':
    try:main()
    except Exception as error:
        log('FAILED '+repr(error));raise
