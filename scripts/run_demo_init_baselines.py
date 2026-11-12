"""Policy-free baselines from every snapshot stage, to separate what the snapshot state gives for free from what the trained
policy adds: 'hold' (zero target change, keep each hand's current grip state) and 'random' (uniform actions), 200 episodes
each per stage S1a/S1b/S2/S3. Runs only after run_demo_init_stage_eval.py completes (never concurrently with training)."""
import argparse
import datetime
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'results/climb-demo-init'
LIBRARY = ROOT/'results/climb-snapshots-v2/library.jsonl'
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
POLICIES = ['hold', 'random']


def log(message):
    line = datetime.datetime.now().isoformat(timespec='seconds')+' '+message
    with (OUT/'baselines.log').open('a', encoding='utf-8') as stream:stream.write(line+'\n')
    print(line, flush=True)


def main():
    configure()
    log('BASELINES waiting for STAGE_EVAL_COMPLETE')
    while True:
        text = (OUT/'stage_eval.log').read_text(encoding='utf-8') if (OUT/'stage_eval.log').exists() else ''
        if 'STAGE_EVAL_FAILED' in text or 'aborted' in text:log('BASELINES aborted: stage evaluation did not complete');return
        if 'STAGE_EVAL_COMPLETE' in text:break
        time.sleep(60)
    for stage in STAGES:
        for policy in POLICIES:
            folder = OUT/'baseline_stage'/stage
            if (folder/policy/'report.json').exists():log(f'SKIP {stage} {policy}');continue
            folder.mkdir(parents=True, exist_ok=True)
            command = [sys.executable, str(ROOT/'scripts/climb_behavior_diagnostics.py'), '--episodes', '200', '--modes', policy, '--target-mode', '0',
                       '--output', str(folder), '--worker-id', '45', '--snapshot-library', str(LIBRARY), '--spawn-stage', stage]+env_args()
            log(f'START {stage} {policy}')
            with (folder/f'console_{policy}.txt').open('w', encoding='utf-8') as stream:result = subprocess.run(command, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
            if result.returncode != 0:raise RuntimeError(f'{stage} {policy} failed; see {folder}/console_{policy}.txt')
            log(f'DONE {stage} {policy}')
    log('BASELINES_COMPLETE')


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    try:main()
    except Exception as error:
        log('BASELINES_FAILED '+repr(error));raise
