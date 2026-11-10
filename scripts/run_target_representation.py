"""Sequential incremental-vs-absolute PPO comparison: 2 modes x 3 seeds x 5M steps, CPU, 4 Unity instances,
then deterministic 200-episode evaluation of the ~1M/2.5M/5M checkpoints. Re-running resumes: finished
trainings and evaluations are skipped, an unfinished training continues with --resume."""
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'results/climb-target-representation'
MODES = {'incremental': 0, 'absolute': 1}
SEEDS = [2026, 2027, 2028]
TARGETS = {'1M': 1_000_000, '2.5M': 2_500_000, '5M': 5_000_000}
MAX_STEPS = 5_000_000


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


def train(mode, seed, index):
    run_id = f'climb_target_{mode}_s{seed}';folder = OUT/run_id;folder.mkdir(parents=True, exist_ok=True)
    if trained(run_id):log(f'SKIP train {run_id} (complete)');return run_id
    resume = (ROOT/'results'/run_id).exists()
    command = [sys.executable, str(ROOT/'scripts/climb_train_worker.py'), '--train-device', 'cpu', '--train-record', str(folder/'device.jsonl'),
               f'config/climb_target_{mode}.yaml', '--env', 'Builds/Climb/Climb.exe', '--run-id', run_id, '--seed', str(seed), '--num-envs', '4',
               '--base-port', str(7000+index*10), '--no-graphics', '--env-args', '--climb-output='+str(folder/'runtime')]
    if resume:command.append('--resume')
    (folder/'command.json').write_text(json.dumps(command, indent=2)+'\n', encoding='utf-8')
    log(f'START train {run_id} resume={resume}')
    started = datetime.datetime.now()
    with (folder/'console.txt').open('a', encoding='utf-8') as stream:result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
    seconds = (datetime.datetime.now()-started).total_seconds()
    with (folder/'timing.jsonl').open('a', encoding='utf-8') as stream:stream.write(json.dumps(dict(started=started.isoformat(), seconds=seconds, exitCode=result.returncode, resume=resume))+'\n')
    if not trained(run_id):raise RuntimeError(f'{run_id} did not reach {MAX_STEPS} steps (exit {result.returncode}); see {folder}/console.txt')
    log(f'DONE train {run_id} {seconds:.0f}s exit={result.returncode}')
    return run_id


def evaluate(run_id, mode):
    available = checkpoints(run_id)
    for label, target in TARGETS.items():
        chosen = min(available, key=lambda c: abs(c['steps']-target))
        folder = OUT/'eval'/run_id/label
        if (folder/'checkpoint/report.json').exists():log(f'SKIP eval {run_id} {label}');continue
        folder.mkdir(parents=True, exist_ok=True)
        model = ROOT/chosen['file_path']
        (folder/'checkpoint.json').write_text(json.dumps(dict(label=label, target=target, steps=chosen['steps'], model=str(model), sha256=hashlib.sha256(model.read_bytes()).hexdigest(), targetMode=MODES[mode]), indent=2)+'\n', encoding='utf-8')
        command = [sys.executable, str(ROOT/'scripts/climb_behavior_diagnostics.py'), '--episodes', '200', '--modes', 'checkpoint', '--model', str(model),
                   '--target-mode', str(MODES[mode]), '--output', str(folder), '--worker-id', '41']
        log(f'START eval {run_id} {label} steps={chosen["steps"]}')
        with (folder/'console.txt').open('w', encoding='utf-8') as stream:result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
        if result.returncode != 0:raise RuntimeError(f'eval {run_id} {label} failed; see {folder}/console.txt')
        log(f'DONE eval {run_id} {label}')


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    log('RUNNER start pid=%d' % __import__('os').getpid())
    index = 0
    # Interleave modes within each seed so slow machine drift affects both groups alike.
    for seed in SEEDS:
        for mode in MODES:
            run_id = train(mode, seed, index);evaluate(run_id, mode);index += 1
    subprocess.run([sys.executable, str(ROOT/'scripts/analyze_target_representation.py')], cwd=ROOT,
                   stdout=(OUT/'analysis.txt').open('w', encoding='utf-8'), stderr=subprocess.STDOUT)
    log('ALL_COMPLETE')


if __name__ == '__main__':
    try:main()
    except Exception as error:
        log('FAILED '+repr(error));raise
