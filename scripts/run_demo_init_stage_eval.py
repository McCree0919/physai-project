"""Per-start-source evaluation of the demonstration-initialisation checkpoints: after run_demo_init.py finishes (never
concurrently with training), evaluate the 1M/2.5M/5M checkpoints of each seed from every snapshot stage S1a/S1b/S2/S3
(200 deterministic episodes each, all starting from that stage). Ground starts are run_demo_init.py's own evaluation.
Re-running skips finished evaluations."""
import argparse
import datetime
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'results/climb-demo-init'
LIBRARY = ROOT/'results/climb-snapshots-v2/library.jsonl'
SEEDS = [2026, 2027]
LABELS = ['1M', '2.5M', '5M']
STAGES = ['S1a', 'S1b', 'S2', 'S3']
PREFIX, ENV_PARAMS, CONFIG, BASE_PORT = 'climb_demo_init', [], None, None


def configure():
    """Defaults reproduce the demo-init experiment; LAYER3_HEIGHT_SHAPING reuses the protocol with its own config/output/run prefix."""
    global OUT, PREFIX, ENV_PARAMS, CONFIG, BASE_PORT
    p = argparse.ArgumentParser()
    p.add_argument('--out', default=str(OUT));p.add_argument('--run-prefix', default=PREFIX);p.add_argument('--config', default=CONFIG)
    p.add_argument('--base-port', type=int, default=BASE_PORT)
    p.add_argument('--env-param', action='append', default=[], metavar='NAME=VALUE', help='also set for every evaluation (must match the training YAML)')
    args = p.parse_args()
    OUT, PREFIX, ENV_PARAMS, CONFIG, BASE_PORT = Path(args.out), args.run_prefix, args.env_param, args.config, args.base_port


def env_args():
    return [x for e in ENV_PARAMS for x in ('--env-param', e)]


def log(message):
    line = datetime.datetime.now().isoformat(timespec='seconds')+' '+message
    with (OUT/'stage_eval.log').open('a', encoding='utf-8') as stream:stream.write(line+'\n')
    print(line, flush=True)


def main():
    configure()
    log('STAGE_EVAL waiting for run_demo_init ALL_COMPLETE')
    while True:
        text = (OUT/'runner.log').read_text(encoding='utf-8') if (OUT/'runner.log').exists() else ''
        if 'FAILED' in text:log('STAGE_EVAL aborted: main runner FAILED');return
        if 'ALL_COMPLETE' in text:break
        time.sleep(60)
    for seed in SEEDS:
        run_id = f'{PREFIX}_s{seed}'
        for label in LABELS:
            info = json.loads((OUT/'eval'/run_id/label/'checkpoint.json').read_text())
            for stage in STAGES:
                folder = OUT/'eval_stage'/run_id/label/stage
                if (folder/'checkpoint/report.json').exists():log(f'SKIP {run_id} {label} {stage}');continue
                folder.mkdir(parents=True, exist_ok=True);(folder/'checkpoint.json').write_text(json.dumps(dict(info, spawnStage=stage), indent=2)+'\n', encoding='utf-8')
                command = [sys.executable, str(ROOT/'scripts/climb_behavior_diagnostics.py'), '--episodes', '200', '--modes', 'checkpoint', '--model', info['model'],
                           '--target-mode', '0', '--output', str(folder), '--worker-id', '43', '--snapshot-library', str(LIBRARY), '--spawn-stage', stage]+env_args()
                log(f'START {run_id} {label} {stage}')
                with (folder/'console.txt').open('w', encoding='utf-8') as stream:result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
                if result.returncode != 0:raise RuntimeError(f'{run_id} {label} {stage} failed; see {folder}/console.txt')
                log(f'DONE {run_id} {label} {stage}')
    log('STAGE_EVAL_COMPLETE')


if __name__ == '__main__':
    try:main()
    except Exception as error:
        log('STAGE_EVAL_FAILED '+repr(error));raise
