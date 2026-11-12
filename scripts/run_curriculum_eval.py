"""Player-build deterministic evaluation of the C0+C1 curriculum (docs/LAYER3_CURRICULUM_C0C1.md). Ground starts only (no snapshot
library is passed), actions from the ONNX deterministic outputs, the committed curriculum player Builds/ClimbCurriculum/Climb.exe
(hash checked against results/climb-curriculum-c0c1/player-sha256.txt), 16 arenas in one process, no C# change.

Sets:
  two-step   : the scene's training two-step (no --climb-curriculum), the first 200 completed episodes overall, as LAYER3_HEIGHT_SHAPING §6.
  c1-val     : the 27 held-out C1 validation structures          } one-stage eval pool file (twoStepShare 0) under OUT/pools; each episode
  v2-easy/standard/hard : structures/box-stacks-v2/eval by tier } draws one structure uniformly. Per structure the first 20 episodes by
                                                                  start order are kept, and the run continues until all of them have ended,
                                                                  so long (timeout) episodes cannot be under-sampled.
An episode's start is the previous end in the same arena (the runtime CSV writes one row per end, per arena, in order).
Rows of structures outside the set (the first episode of each arena can begin before the environment parameters arrive and keeps the scene
two-step) are dropped and counted. Re-running skips finished (model, set) pairs."""
import argparse
import collections
import csv
import hashlib
import json
from pathlib import Path
import subprocess
import time
import numpy as np
import onnxruntime as ort
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'results/curriculum-c0c1-eval'
PLAYER = ROOT/'Builds/ClimbCurriculum/Climb.exe'
PLAYER_MANIFEST = ROOT/'results/climb-curriculum-c0c1/player-sha256.txt'
CURRICULUM = ROOT/'structures/curriculum-v1/pools.json'
V2_EVAL = ROOT/'structures/box-stacks-v2/eval/manifest.json'
MODELS = {
    'c0c1_s2026': 'results/climb_curriculum_c0c1_s2026/Climb/Climb-8000051.onnx',
    'c0c1_s2027': 'results/climb_curriculum_c0c1_s2027/Climb/Climb-8000081.onnx',
    'height_s2026_5M': 'results/climb_height_shaping_s2026/Climb/Climb-5000009.onnx',
    'height_s2027_5M': 'results/climb_height_shaping_s2027/Climb/Climb-5000007.onnx',
}
# Strict control (docs/LAYER3_CURRICULUM_C0C1.md §8): two-step only, 8M steps, post-fix environment. Final checkpoint = the one listed as
# final_checkpoint in training_status.json (its .pt sibling in Climb/ has the same step count).
def final_onnx(run_id):
    status = json.loads((ROOT/'results'/run_id/'run_logs/training_status.json').read_text())['Climb']['final_checkpoint']
    return f"results/{run_id}/Climb/Climb-{status['steps']}.onnx"
for seed in ('2026', '2027'):
    if (ROOT/f'results/climb_twostep_8m_s{seed}/run_logs/training_status.json').exists():MODELS[f'twostep8m_s{seed}'] = final_onnx(f'climb_twostep_8m_s{seed}')
# Items 1-3 and 5 for the curriculum checkpoints; items 2-3 for the baselines, plus the two-step as a player-vs-editor check of §6.
PLAN = {'c0c1_s2026': ['two-step', 'c1-val', 'v2-easy', 'v2-standard', 'v2-hard'], 'c0c1_s2027': ['two-step', 'c1-val', 'v2-easy', 'v2-standard', 'v2-hard'],
        'height_s2026_5M': ['two-step', 'c1-val', 'v2-easy'], 'height_s2027_5M': ['two-step', 'c1-val', 'v2-easy'],
        'twostep8m_s2026': ['two-step', 'c1-val', 'v2-easy'], 'twostep8m_s2027': ['two-step', 'c1-val', 'v2-easy']}
ENV_PARAMS = {'target_mode': 0.0, 'height_reward_scale': 0.2, 'height_reward_cap': 0.2, 'spawn_seed': 0.0}
PER_STRUCTURE, TWO_STEP_TOTAL, ARENAS = 20, 200, 16


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def pool_file(eval_set):
    """Entries (id, path, sha256, cluster) of an eval set, written as a one-stage curriculum file that ClimbCurriculum.Load verifies."""
    curriculum = json.loads(CURRICULUM.read_text())
    if eval_set == 'c1-val':entries = curriculum['validation']
    else:
        tier = eval_set.split('-')[1].capitalize()
        # v2 eval: every structure is its own cluster (LAYER3_GENERATOR_V2 §4.2, 60 structures = 60 clusters per tier).
        entries = [dict(id=e['id'], path=e['path'], sha256=e['sha256'], cluster=e['id']) for e in json.loads(V2_EVAL.read_text())['structures'] if e['difficulty'] == tier]
    folder = OUT/'pools';folder.mkdir(parents=True, exist_ok=True)
    path = folder/f'{eval_set}.json'
    document = dict(version=1, root='../../..', rule=f'evaluation pool {eval_set}; not used for training', twoStepShare=0.0, twoStep=curriculum['twoStep'],
                    stages=[dict(name='eval', pool='eval', structures=entries)], validation=[])
    text = json.dumps(document, indent=2)+'\n'
    if not path.exists() or path.read_text() != text:path.write_text(text, encoding='utf-8')
    return path, entries


def read_rows(folder):
    rows = []
    for f in sorted((folder/'runtime').glob('episodes-*.csv')):
        with open(f, encoding='utf-8', newline='') as stream:rows.extend(csv.DictReader(stream))
    return rows


def select(rows, ids):
    """Per structure the first PER_STRUCTURE episodes by start; complete when every in-progress episode started later than all of them."""
    last, starts = {}, []
    for index, r in enumerate(rows):
        t = int(r['unix_ms']);starts.append((last.get(r['arena'], -1), index));last[r['arena']] = t
    by = collections.defaultdict(list)
    for (start, index) in starts:
        r = rows[index]
        if r['structure'] in ids:by[r['structure']].append((start, index))
    if len(last) < ARENAS:return None
    running = min(last.values())
    chosen = {}
    for sid in ids:
        items = sorted(by.get(sid, []))[:PER_STRUCTURE]
        if len(items) < PER_STRUCTURE or items[-1][0] >= running:return None
        chosen[sid] = [dict(rows[i], start_ms=s, structure=sid) for s, i in items]
    return chosen


def run(label, model, eval_set):
    folder = OUT/label/eval_set
    if (folder/'report.json').exists():print('SKIP', label, eval_set, flush=True);return
    folder.mkdir(parents=True, exist_ok=True)
    args = ['--climb-output='+str(folder/'runtime')]
    params = dict(ENV_PARAMS)
    ids = None
    if eval_set != 'two-step':
        path, entries = pool_file(eval_set);ids = {e['id'] for e in entries}
        args.append('--climb-curriculum='+str(path));params['curriculum_stage'] = 0.0
    channel = EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=100)
    parameters = EnvironmentParametersChannel()
    for name, value in params.items():parameters.set_float_parameter(name, value)
    stats = StatsSideChannel()
    options = ort.SessionOptions();options.intra_op_num_threads = 1;options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model), sess_options=options, providers=['CPUExecutionProvider'])
    (folder/'command.json').write_text(json.dumps(dict(player=str(PLAYER), args=args, model=str(model), envParams=params), indent=2)+'\n', encoding='utf-8')
    started = time.time();env = UnityEnvironment(file_name=str(PLAYER), base_port=7400, worker_id=0, no_graphics=True, timeout_wait=300,
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
                rows = read_rows(folder)
                if ids is None:
                    if len(rows) >= TWO_STEP_TOTAL:chosen = {'two-step': rows[:TWO_STEP_TOTAL]};break
                else:
                    chosen = select(rows, ids)
                    if chosen:break
            if time.time()-started > 4*3600:raise RuntimeError(f'{label} {eval_set} did not complete in 4 h')
    finally:env.close()
    rows = read_rows(folder)
    dropped = collections.Counter(r['structure'] or '(scene)' for r in rows if ids is not None and r['structure'] not in ids)
    episodes = [r for v in chosen.values() for r in v]
    if ids is None:assert all(r['structure'] == '' for r in episodes), 'two-step run drew curriculum structures'
    assert all(r['spawn'] == 'ground' for r in episodes)
    summary = dict(label=label, evalSet=eval_set, model=str(model), modelSha256=sha(model), playerSha256=sha(PLAYER), envParams=params, args=args,
                   structures=len(chosen) if ids else 1, episodes=len(episodes), rowsTotal=len(rows), droppedOutsideSet=dict(dropped),
                   poolSha256=sha(folder.parent.parent/'pools'/f'{eval_set}.json') if ids else None, wallSeconds=time.time()-started,
                   success=sum(r['success'] == '1' for r in episodes)/len(episodes))
    (folder/'report.json').write_text(json.dumps(dict(summary=summary, episodes=episodes), indent=2)+'\n', encoding='utf-8')
    print(json.dumps(summary), flush=True)


def main():
    p = argparse.ArgumentParser();p.add_argument('--only');p.add_argument('--set');a = p.parse_args()
    # Every file of the build, not only the executable: the learning code lives in Climb_Data/Managed/*.dll.
    for line in PLAYER_MANIFEST.read_text().splitlines():
        if not line.strip():continue
        digest, name = line.split(' ', 1)
        assert sha(PLAYER.parent/name.lstrip('*')) == digest, f'player build file {name} does not match the recorded hash'
    for label, sets in PLAN.items():
        if a.only and label != a.only:continue
        if label not in MODELS:print('SKIP', label, '(not trained yet)', flush=True);continue
        for eval_set in sets:
            if a.set and eval_set != a.set:continue
            run(label, ROOT/MODELS[label], eval_set)


if __name__ == '__main__':main()
