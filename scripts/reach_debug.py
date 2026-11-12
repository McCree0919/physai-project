"""Built-player diagnostics. Never changes the Agent, trainer, or scene settings.

Run from any directory after activating the mlagents environment:
  python scripts/reach_debug.py controller
  python scripts/reach_debug.py probes
  python scripts/reach_debug.py evaluate --model results/reach_v1/Reach.onnx
"""
import argparse
import json
from pathlib import Path

import numpy as np
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

ROOT = Path(__file__).resolve().parents[1]


def controller(observations):
    """Original diagnostic feedback controller, unchanged gains (not a learner)."""
    delta = observations[:, [0, 2]] * 10.0
    distance = np.maximum(np.linalg.norm(delta, axis=1, keepdims=True), 0.01)
    direction = delta / distance
    desired_velocity = direction * np.minimum(1.5, distance * 2.0)
    actions = (desired_velocity - observations[:, 3:5]) * 2.0 + 0.65 * direction
    return np.clip(actions, -1.0, 1.0).astype(np.float32)


def run_controller(env, name, stats):
    env.reset()
    decisions, _ = env.get_steps(name)
    initial_ids = set(map(int, decisions.agent_id))
    successes = set()
    failure_count = 0
    success_count = 0
    for iteration in range(400):
        decisions, terminal = env.get_steps(name)
        for agent_id in terminal.agent_id:
            if terminal[agent_id].reward > 0.5:
                successes.add(int(agent_id))
                success_count += 1
            else:
                failure_count += 1
        if initial_ids.issubset(successes):
            break
        if len(decisions):
            env.set_actions(name, ActionTuple(continuous=controller(decisions.obs[0])))
        env.step()
        stats.get_and_reset_stats()
    return dict(initial_agents=len(initial_ids), successful_agents=len(successes),
                all_agents_succeeded=initial_ids.issubset(successes),
                successes=success_count, failures=failure_count,
                decision_iterations=iteration)


def run_probes(env, name, stats):
    reports = []
    # Sustained saturation is a stress test, not a requirement that the agent
    # ignore its platform edge. Also measure a maximum pulse followed by braking.
    for label, value in [('small', 0.1), ('medium', 0.3), ('saturated', 1.0), ('pulse_brake', 1.0)]:
        env.reset()
        decisions, _ = env.get_steps(name)
        ids = set(map(int, decisions.agent_id))
        initial_relative = {int(aid): decisions[aid].obs[0][[0, 2]].copy() * 10.0
                            for aid in decisions.agent_id}
        ended = set()
        completed_decisions = {aid: 0 for aid in ids}
        pending = set()
        snapshots = {}
        successes = failures = 0
        peak_speed = 0.0
        # env.step() can return early for another agent's terminal event.
        # Count each original agent's completed decision intervals separately.
        for _ in range(100):
            decisions, terminal = env.get_steps(name)
            for agent_id in terminal.agent_id:
                aid = int(agent_id)
                if aid in ids and aid not in ended and completed_decisions[aid] < 10:
                    ended.add(aid)
                    pending.discard(aid)
                    successes += int(terminal[agent_id].reward > 0.5)
                    failures += int(terminal[agent_id].reward <= 0.5)
            actions = np.zeros((len(decisions), 2), dtype=np.float32)
            for index, agent_id in enumerate(decisions.agent_id):
                aid = int(agent_id)
                if aid not in ids or aid in ended:
                    continue
                if aid in pending:
                    pending.remove(aid)
                    completed_decisions[aid] += 1
                    snapshots[aid] = decisions[agent_id].obs[0].copy()
                    peak_speed = max(peak_speed, float(np.linalg.norm(snapshots[aid][3:5])))
                step = completed_decisions[aid]
                if step < 10:
                    action = value
                    if label == 'pulse_brake':
                        action = 1.0 if step == 0 else (-1.0 if step == 1 else 0.0)
                    actions[index] = action
                    pending.add(aid)
            if all(aid in ended or completed_decisions[aid] >= 10 for aid in ids):
                break
            if len(decisions):
                env.set_actions(name, ActionTuple(continuous=actions))
            env.step()
            stats.get_and_reset_stats()
        if not all(aid in ended or completed_decisions[aid] >= 10 for aid in ids):
            raise RuntimeError('Probe did not reach its per-agent decision budget')
        survivors = [aid for aid in ids if aid not in ended]
        velocities = [snapshots[aid][3:5] for aid in survivors]
        speeds = np.linalg.norm(velocities, axis=1) if velocities else np.array([])
        displacements = [np.linalg.norm(initial_relative[aid] - snapshots[aid][[0, 2]] * 10.0)
                         for aid in survivors]
        reports.append(dict(test=label, input=[value, value], decisions=10,
                            original_episodes_remaining=len(velocities),
                            speed_mean=float(speeds.mean()) if len(speeds) else None,
                            speed_min=float(speeds.min()) if len(speeds) else None,
                            speed_max=float(speeds.max()) if len(speeds) else None,
                            displacement_mean=float(np.mean(displacements)) if displacements else None,
                            peak_speed=peak_speed, successes=successes, failures=failures))
    return reports


def run_evaluation(env, name, stats, model, episodes):
    import onnx
    import onnxruntime as ort
    onnx.checker.check_model(str(model))
    session = ort.InferenceSession(str(model), providers=['CPUExecutionProvider'])
    returns = {}
    completed = []
    successes = timeouts = 0
    for _ in range(10000):
        decisions, terminal = env.get_steps(name)
        for agent_id in decisions.agent_id:
            key = int(agent_id)
            returns[key] = returns.get(key, 0.0) + float(decisions[agent_id].reward)
        # Fixed count: accept exactly the first N terminal episodes, even if
        # several agents terminate in the same environment step.
        for agent_id in terminal.agent_id:
            result = terminal[agent_id]
            key = int(agent_id)
            completed.append(returns.pop(key, 0.0) + float(result.reward))
            successes += int(result.reward > 0.5)
            timeouts += int(result.interrupted)
            if len(completed) == episodes:
                break
        if len(completed) == episodes:
            break
        if len(decisions):
            actions = session.run(['deterministic_continuous_actions'],
                                  {'obs_0': decisions.obs[0].astype(np.float32)})[0]
            env.set_actions(name, ActionTuple(continuous=actions))
        env.step()
        stats.get_and_reset_stats()
    if len(completed) != episodes:
        raise RuntimeError('Evaluation episode budget was not reached')
    return dict(episodes=len(completed), successes=successes, success_rate=successes / episodes,
                timeouts=timeouts, mean_return=float(np.mean(completed)),
                policy='deterministic_continuous_actions', onnx_valid=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['controller', 'probes', 'evaluate'])
    parser.add_argument('--env', type=Path, default=ROOT / 'Builds/Reach/Reach.exe')
    parser.add_argument('--model', type=Path)
    parser.add_argument('--episodes', type=int, default=112)
    parser.add_argument('--seed', type=int, default=2026)
    parser.add_argument('--worker-id', type=int, default=14)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.mode == 'evaluate' and (args.model is None or args.episodes <= 0):
        parser.error('evaluate requires --model and a positive --episodes')
    channel = EngineConfigurationChannel()
    channel.set_configuration_parameters(time_scale=20)
    stats = StatsSideChannel()
    env = UnityEnvironment(file_name=str(args.env.resolve()), worker_id=args.worker_id,
                           seed=args.seed, no_graphics=True, additional_args=['-logFile', 'NUL'],
                           side_channels=[channel, stats])
    try:
        env.reset()
        name = next(iter(env.behavior_specs))
        spec = env.behavior_specs[name]
        if spec.observation_specs[0].shape != (5,) or spec.action_spec.continuous_size != 2:
            raise RuntimeError('Expected 5 observations and 2 continuous actions')
        if args.mode == 'controller':
            result = run_controller(env, name, stats)
        elif args.mode == 'probes':
            result = run_probes(env, name, stats)
        else:
            result = run_evaluation(env, name, stats, args.model.resolve(), args.episodes)
        report = dict(mode=args.mode, seed=args.seed, environment=str(args.env.resolve()), result=result)
        print(json.dumps(report, indent=2), flush=True)
        if args.output:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        if args.mode == 'controller' and not result['all_agents_succeeded']:
            raise RuntimeError('Not all arena agents succeeded')
    finally:
        env.close()


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
