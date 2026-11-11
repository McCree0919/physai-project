"""Editor-mode deterministic evaluation (LAYER3_ZERO_SHOT.md). Sets (ClimbZeroShot.BuildScene):
  zero-shot  : training two-step (control arena 0) + every frozen structure in structures/zero-shot-v1; a fixed number of episodes per arena.
  training16 : the 16 identical training two-step arenas; the first N completed episodes overall, as climb_behavior_diagnostics.py does.
The scene runs in Editor play mode (ClimbZeroShot.PlayInEditor) because a standalone player cannot be built in this worktree;
actions, observations and the observer join are the same as climb_behavior_diagnostics.py. Re-running skips finished models."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import subprocess
import threading
import time
import numpy as np
import onnxruntime as ort
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

ROOT = Path(__file__).resolve().parents[1]
UNITY = Path(r'C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe')
MANIFEST = ROOT/'structures/zero-shot-v1/manifest.json'
MODELS = {'s2026_5M': 'results/climb_height_shaping_s2026/Climb/Climb-5000009.onnx', 's2027_5M': 'results/climb_height_shaping_s2027/Climb/Climb-5000007.onnx'}
ENV_PARAMS = {'target_mode': 0.0, 'height_reward_scale': 0.2, 'height_reward_cap': 0.2}


def run(label, model, folder, eval_set, per_arena=None, total=None):
    folder.mkdir(parents=True, exist_ok=True)
    arenas = 16 if eval_set == 'training16' else 1+len(json.loads(MANIFEST.read_text())['structures'])
    channel = EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=100)
    parameters = EnvironmentParametersChannel()
    for name, value in ENV_PARAMS.items():parameters.set_float_parameter(name, value)
    stats = StatsSideChannel()
    options = ort.SessionOptions();options.intra_op_num_threads = 1;options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(model), sess_options=options, providers=['CPUExecutionProvider'])
    command = [str(UNITY), '-batchmode', '-nographics', '-projectPath', str(ROOT), '-executeMethod', 'ClimbZeroShot.PlayInEditor', '-logFile', str(folder/'unity.log'),
               '--climb-eval-set='+eval_set, '--climb-output='+str(folder/'runtime'), '--climb-diagnostic-output='+str(folder/'observer')]
    (folder/'command.json').write_text(json.dumps(dict(unity=command, model=str(model), envParams=ENV_PARAMS, evalSet=eval_set, perArena=per_arena, totalEpisodes=total), indent=2)+'\n', encoding='utf-8')
    # Python is the gRPC server: start the editor, then listen; the editor connects once play mode starts.
    unity = subprocess.Popen(command, cwd=ROOT)
    def watchdog():
        # A failed handshake makes the editor log exceptions every physics step; stop it before the log fills the disk.
        while unity.poll() is None:
            time.sleep(10)
            if (folder/'unity.log').exists() and (folder/'unity.log').stat().st_size > 300_000_000:print('WATCHDOG unity.log > 300 MB; killing editor', flush=True);unity.kill()
    threading.Thread(target=watchdog, daemon=True).start()
    started = time.time();active, counts, completed, seen = {}, collections.Counter(), [], set()
    def done():
        if total is not None:return len(completed) >= total
        return len(seen) == arenas and all(counts[a] >= per_arena for a in seen)
    env = UnityEnvironment(file_name=None, base_port=5004, worker_id=0, timeout_wait=900, side_channels=[channel, stats, parameters])
    try:
        env.reset();behavior = next(iter(env.behavior_specs))
        while True:
            decisions, terminal = env.get_steps(behavior)
            for aid in terminal.agent_id:
                aid = int(aid);state = active.pop(aid);state['return'] += float(terminal[aid].reward)
                keep = len(completed) < total if total is not None else counts[aid] < per_arena
                if keep:state.update(agentId=aid, episode=int(counts[aid]));completed.append(state)
                counts[aid] += 1
            for aid in decisions.agent_id:
                aid = int(aid);seen.add(aid);state = active.setdefault(aid, {'return': 0.0, 'decisions': 0});state['return'] += float(decisions[aid].reward)
            if done():
                # One more cycle so the passive observer flushes the last terminal records.
                if len(decisions):env.set_actions(behavior, ActionTuple(continuous=np.zeros((len(decisions), 6), np.float32), discrete=np.zeros((len(decisions), 2), np.int32)))
                env.step();break
            if len(decisions):
                continuous, discrete = session.run(['deterministic_continuous_actions', 'deterministic_discrete_actions'], {'obs_0': decisions.obs[0].astype(np.float32), 'action_masks': np.ones((len(decisions), 4), np.float32)})
                for aid in decisions.agent_id:active[int(aid)]['decisions'] += 1
                env.set_actions(behavior, ActionTuple(continuous=continuous, discrete=discrete.astype(np.int32)))
            env.step();stats.get_and_reset_stats()
    finally:
        env.close()
        try:unity.wait(timeout=300)
        except subprocess.TimeoutExpired:unity.kill()
    observer = {}
    for path in (folder/'observer').glob('*.jsonl'):
        for line in path.read_text().splitlines():
            record = json.loads(line);observer[record['agentId'], record['episode']] = record
    assert len({k[0] for k in observer}) == arenas, f'observer saw {len({k[0] for k in observer})} of {arenas} arenas'
    for row in completed:row['physics'] = observer[row['agentId'], row['episode']]
    if total is not None:assert len(completed) == total
    else:assert len(completed) == arenas*per_arena and all(n == per_arena for n in collections.Counter(r['physics']['arena'] for r in completed).values())
    summary = dict(label=label, model=str(model), modelSha256=hashlib.sha256(Path(model).read_bytes()).hexdigest(), evalSet=eval_set, arenas=arenas, episodesPerArena=per_arena,
                   totalEpisodes=total, envParams=ENV_PARAMS, manifestSha256=hashlib.sha256(MANIFEST.read_bytes()).hexdigest() if eval_set == 'zero-shot' else None,
                   wallSeconds=time.time()-started, unityExit=unity.returncode)
    (folder/'report.json').write_text(json.dumps(dict(summary=summary, episodes=completed), indent=2)+'\n', encoding='utf-8')
    print(json.dumps(summary), flush=True)


def main():
    p = argparse.ArgumentParser();p.add_argument('--out', default=str(ROOT/'results/climb-zero-shot'))
    p.add_argument('--eval-set', default='zero-shot', choices=['zero-shot', 'training16'])
    p.add_argument('--episodes-per-arena', type=int, default=20);p.add_argument('--total-episodes', type=int, help='first N completed episodes overall instead of a per-arena quota')
    p.add_argument('--only', choices=list(MODELS))
    args = p.parse_args()
    for label, model in MODELS.items():
        if args.only and label != args.only:continue
        folder = Path(args.out)/label
        if (folder/'report.json').exists():print('SKIP', label, flush=True);continue
        run(label, ROOT/model, folder, args.eval_set, None if args.total_episodes else args.episodes_per_arena, args.total_episodes)


if __name__ == '__main__':main()
