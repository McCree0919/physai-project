"""Diagnostic re-test of v2 Hard for the C0+C1 checkpoints (docs/LAYER3_CURRICULUM_C0C1.md §6.3, LAYER3_C2_DESIGN §5 item 5).
Same protocol as scripts/run_curriculum_eval.py (deterministic ONNX actions, ground starts, one-stage v2-hard pool, first 20 episodes per
structure by start order), on the diagnostic player Builds/ClimbDiagnosticsC2 (the training scene plus the passive ClimbDiagnosticsObserver
in every arena; hash-checked against results/hard-diagnostics/player-sha256.txt), with --climb-diagnostic-output.
The observer writes one JSON line per finished episode per arena; the runtime CSV one row per end per arena, both in order. They are
joined by (arena, ordinal) and the join is verified per episode: observer episode index = ordinal, and observed physics samples / 50 Hz
equals the CSV sim_seconds within 0.1 s. Writes results/hard-diagnostics/<label>/<set>/report.json; analysis: analyze_hard_diagnostics.py.
--set v2-easy-tall (LAYER3_C2_REPORT §4, control for the Hard re-test): the v2 Easy structures whose effective first step is a single box of
~1.0 m (first_step.csv: firstStand >= 0.9, not stacked; 13 structures), same protocol and player."""
import argparse
import csv
import collections
import json
from pathlib import Path
import time
import player_guard
import numpy as np
import onnxruntime as ort
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel
import run_curriculum_eval as base

ROOT = base.ROOT
OUT = ROOT/'results/hard-diagnostics'
PLAYER = ROOT/'Builds/ClimbDiagnosticsC2/ClimbDiagnostics.exe'
PLAYER_MANIFEST = OUT/'player-sha256.txt'
MODELS = {k: base.MODELS[k] for k in ('c0c1_s2026', 'c0c1_s2027')}
FIRST_STEP = ROOT/'docs/data/curriculum-c0c1-eval/first_step.csv'


def pool_file(eval_set):
    if eval_set == 'v2-hard':return base.pool_file('v2-hard')
    assert eval_set == 'v2-easy-tall', eval_set
    _, easy = base.pool_file('v2-easy')
    first = {r['id']: r for r in csv.DictReader(open(FIRST_STEP, encoding='utf-8'))}
    items = [e for e in easy if float(first[e['id']]['firstStand']) >= .9 and first[e['id']]['firstStacked'] != '1']
    assert len(items) == 13, len(items)
    c1 = json.loads(base.CURRICULUM.read_text());folder = OUT/'pools';folder.mkdir(parents=True, exist_ok=True);path = folder/f'{eval_set}.json'
    text = json.dumps(dict(version=1, root='../../..', rule='diagnostic pool v2-easy-tall; not used for training', twoStepShare=0.0, twoStep=c1['twoStep'],
                           stages=[dict(name='eval', pool='eval', structures=items)], validation=[]), indent=2)+'\n'
    if not path.exists() or path.read_text() != text:path.write_text(text, encoding='utf-8')
    return path, items


def observer_rows(folder):
    out = {}
    for f in sorted((folder/'diagnostics').glob('*.jsonl')):
        out[f.stem] = [json.loads(line) for line in f.read_text(encoding='utf-8').splitlines() if line.strip()]
    return out


def join(rows, observed):
    """Attach to every CSV row its observer episode (same arena, same ordinal); returns rows with 'observer' and the join check counts."""
    ordinal = collections.Counter();checks = collections.Counter()
    for r in rows:
        i = ordinal[r['arena']];ordinal[r['arena']] += 1
        episodes = observed.get(r['arena'], [])
        if i >= len(episodes):checks['missing'] += 1;continue
        o = episodes[i]
        ok = o['episode'] == i and abs(o['physicsSamples']/50-float(r['sim_seconds'])) <= .1
        checks['ok' if ok else 'mismatch'] += 1
        r['observer'] = o if ok else None
    return checks


def run(label, model, EVAL_SET):
    folder = OUT/label/EVAL_SET
    if (folder/'report.json').exists():print('SKIP', label, flush=True);return
    folder.mkdir(parents=True, exist_ok=True)
    path, entries = pool_file(EVAL_SET);ids = {e['id'] for e in entries}
    args = ['--climb-output='+str(folder/'runtime'), '--climb-diagnostic-output='+str(folder/'diagnostics'), '--climb-curriculum='+str(path)]
    params = dict(base.ENV_PARAMS, curriculum_stage=0.0)
    channel = EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=100)
    parameters = EnvironmentParametersChannel()
    for name, value in params.items():parameters.set_float_parameter(name, value)
    stats = StatsSideChannel()
    options = ort.SessionOptions();options.intra_op_num_threads = 1;options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model), sess_options=options, providers=['CPUExecutionProvider'])
    (folder/'command.json').write_text(json.dumps(dict(player=str(PLAYER), args=args, model=str(model), envParams=params), indent=2)+'\n', encoding='utf-8')
    started = time.time();env = UnityEnvironment(file_name=str(PLAYER), base_port=7700, worker_id=0, no_graphics=True, timeout_wait=300,
                                                 additional_args=args+['-logFile', str(folder/'player.log')], side_channels=[channel, stats, parameters])
    chosen, steps = None, 0
    try:
        env.reset();behavior = next(iter(env.behavior_specs))
        while True:
            decisions, _ = env.get_steps(behavior)
            if len(decisions):
                continuous, discrete = session.run(['deterministic_continuous_actions', 'deterministic_discrete_actions'], {'obs_0': decisions.obs[0].astype(np.float32), 'action_masks': np.ones((len(decisions), 4), np.float32)})
                env.set_actions(behavior, ActionTuple(continuous=continuous, discrete=discrete.astype(np.int32)))
            env.step();stats.get_and_reset_stats();steps += 1
            if steps % 50 == 0:
                chosen = base.select(base.read_rows(folder), ids)
                if chosen:
                    # The observer flushes an episode at the next physics step of its arena; step on until every chosen one is written.
                    for _ in range(20):env.step()
                    break
            if time.time()-started > 4*3600:raise RuntimeError(f'{label} did not complete in 4 h')
    finally:
        env.close()
        player_guard.reap(f'{label} {EVAL_SET}')  # a player that did not exit with the environment
    rows = base.read_rows(folder);observed = observer_rows(folder)
    checks = join(rows, observed)
    chosen = base.select(rows, ids)
    episodes = [r for v in chosen.values() for r in v]
    keyed = {(r['arena'], r['unix_ms']): r for r in rows}
    for e in episodes:e['observer'] = keyed[e['arena'], e['unix_ms']].get('observer')
    assert all(r['spawn'] == 'ground' for r in episodes)
    missing = sum(e['observer'] is None for e in episodes)
    if missing:raise RuntimeError(f'{missing} chosen episodes have no verified observer record ({dict(checks)})')
    summary = dict(label=label, evalSet=EVAL_SET, model=str(model), modelSha256=base.sha(model), playerSha256=base.sha(PLAYER), envParams=params, args=args,
                   structures=len(chosen), episodes=len(episodes), rowsTotal=len(rows), joinChecks=dict(checks), poolSha256=base.sha(path), wallSeconds=time.time()-started,
                   success=sum(r['success'] == '1' for r in episodes)/len(episodes))
    (folder/'report.json').write_text(json.dumps(dict(summary=summary, episodes=episodes), indent=2)+'\n', encoding='utf-8')
    print(json.dumps(summary), flush=True)


def main():
    for line in PLAYER_MANIFEST.read_text().splitlines():
        if line.strip():
            digest, name = line.split(' ', 1)
            assert base.sha(PLAYER.parent/name.lstrip('*')) == digest, f'player build file {name} does not match the recorded hash'
    p = argparse.ArgumentParser();p.add_argument('--set', default='v2-hard', choices=['v2-hard', 'v2-easy-tall']);a = p.parse_args()
    for label, model in MODELS.items():run(label, ROOT/model, a.set)


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
