"""Strict control for the C0+C1 curriculum (docs/LAYER3_CURRICULUM_C0C1.md §7): 2 seeds x 8M steps on the training two-step only, in the
same curriculum player (post-ResetBody-fix), with config/climb_twostep_8m.yaml = climb_curriculum_c0c1.yaml without the curriculum block.
Same seeds, workers, device and snapshot library as scripts/run_curriculum_c0c1.py; no --climb-curriculum, hence no structure swaps and
no early stop. Logs a STATUS line every 500k steps. Re-running resumes an unfinished seed and skips finished ones."""
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
OUT = ROOT/'results/climb-twostep-8m'
LIBRARY = ROOT/'results/climb-snapshots-v2/library.jsonl'
PLAYER = ROOT/'Builds/ClimbCurriculum/Climb.exe'
CONFIG, PREFIX, BASE_PORT = 'config/climb_twostep_8m.yaml', 'climb_twostep_8m', 7500
SEEDS = [2026, 2027]
MAX_STEPS, POLL_SECONDS = 8_000_000, 120


def log(message):
    line = datetime.datetime.now().isoformat(timespec='seconds')+' '+message
    with (OUT/'runner.log').open('a', encoding='utf-8') as stream:stream.write(line+'\n')
    print(line, flush=True)


def trained(run_id):
    path = ROOT/'results'/run_id/'run_logs/training_status.json'
    if not path.exists() or not (ROOT/'results'/run_id/'Climb.onnx').exists():return False
    return any(c['steps'] >= MAX_STEPS-10_000 for c in json.loads(path.read_text())['Climb']['checkpoints'])


def status(run_id, seen):
    from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    data = {}
    for f in sorted(glob.glob(str(ROOT/'results'/run_id/'Climb/events.out*'))):
        a = EventAccumulator(f, size_guidance={'scalars': 0});a.Reload()
        for tag in a.Tags()['scalars']:data.setdefault(tag, []).extend((e.step, e.value) for e in a.Scalars(tag))
    steps = data.get('Climb/Success', [])
    if not steps:return
    step = max(s for s, _ in steps);bucket = step//500_000
    if not bucket or bucket in seen:return
    seen.add(bucket)
    def mean(tag, n=20):
        values = [v for _, v in sorted(data.get(tag, []))][-n:]
        return round(sum(values)/len(values), 4) if values else None
    log(f'STATUS {run_id} step={step} success={mean("Climb/Success")} level1={mean("Climb/Level1Rate")} level2={mean("Climb/Level2Rate")} '
        f'demoActual={mean("Climb/Spawn/ActualDemoFraction")} rejected={mean("Climb/Spawn/Rejected")}')


def train(seed, index):
    run_id = f'{PREFIX}_s{seed}';folder = OUT/run_id;folder.mkdir(parents=True, exist_ok=True)
    if trained(run_id):log(f'SKIP train {run_id} (complete)');return
    resume = (ROOT/'results'/run_id).exists()
    command = [sys.executable, str(ROOT/'scripts/climb_train_worker.py'), '--train-device', 'cpu', '--train-record', str(folder/'device.jsonl'),
               CONFIG, '--env', str(PLAYER.relative_to(ROOT)), '--run-id', run_id, '--seed', str(seed), '--num-envs', '4',
               '--base-port', str(BASE_PORT+index*10), '--no-graphics', '--env-args', '--climb-snapshot-library='+str(LIBRARY),
               '--climb-output='+str(folder/'runtime')]
    if resume:command.append('--resume')
    (folder/'command.json').write_text(json.dumps(command, indent=2)+'\n', encoding='utf-8')
    log(f'START train {run_id} resume={resume}')
    started = datetime.datetime.now();seen = set()
    with (folder/'console.txt').open('a', encoding='utf-8') as stream:
        process = subprocess.Popen(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        while process.poll() is None:
            time.sleep(POLL_SECONDS)
            try:status(run_id, seen)
            except Exception as error:log(f'MONITOR {run_id} read failed {error!r}')
    status(run_id, seen)
    seconds = (datetime.datetime.now()-started).total_seconds()
    with (folder/'timing.jsonl').open('a', encoding='utf-8') as stream:stream.write(json.dumps(dict(started=started.isoformat(), seconds=seconds, exitCode=process.returncode, resume=resume))+'\n')
    if not trained(run_id):raise RuntimeError(f'{run_id} did not reach {MAX_STEPS} steps (exit {process.returncode}); see {folder}/console.txt')
    log(f'DONE train {run_id} {seconds:.0f}s exit={process.returncode}')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for path in (LIBRARY, PLAYER):
        if not path.exists():raise FileNotFoundError(path)
    digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
    log(f'RUNNER start pid={os.getpid()} config={CONFIG} library_sha256={digest(LIBRARY)} player_sha256={digest(PLAYER)}')
    for index, seed in enumerate(SEEDS):train(seed, index)
    log('ALL_TRAINING_COMPLETE')


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    try:main()
    except Exception as error:
        log('FAILED '+repr(error));raise
