"""§7.5 ONNX vs training-side consistency check (docs/LAYER3_CONFIRMATION_PREREG.md §4), for the two C2 final checkpoints.

1. Observations: one guarded run of the C2 player per model (Builds/ClimbCurriculumC2, hash-checked) on the training two-step and on the v2
   Standard dev set, actions from the ONNX deterministic outputs as in the evaluation; every decision observation is stored (up to
   MAX_OBS per set). The confirmation set is not used.
2. Training side: mlagents 1.1.0 SimpleActor (PPO actor kwargs: conditional_sigma False, tanh_squash False), network settings of
   config/climb_curriculum_c2.yaml with deterministic=True, state dict 'Policy' of the .pt saved with the same step as the ONNX, strict
   load. Actions via get_action_and_stats (the trainer path: distribution mean / argmax, then env_action = clamp(-3, 3)/3).
3. Compare with onnxruntime (CPU, as the evaluation) on the recorded observations and on the same observations with N(0,1) noise added
   (normalizer stress). Pass: continuous max |diff| <= 1e-4; no discrete disagreement where the training-side logit margin is >= 1e-4.
   Also: two ONNX passes are bitwise identical.
Writes results/onnx-consistency/<model>/{obs.npz,report.json} and docs/data/confirm/onnx_consistency.json."""
import json
import time
import numpy as np
import onnxruntime as ort
import torch
from mlagents_envs.base_env import ActionSpec, ActionTuple, DimensionProperty, ObservationSpec, ObservationType
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel
from mlagents.trainers.settings import NetworkSettings
from mlagents.trainers.torch_entities.networks import SimpleActor
from mlagents.torch_utils import set_torch_config
from mlagents.trainers.settings import TorchSettings
import player_guard
import run_c2_eval as c2
import run_curriculum_eval as base

ROOT = base.ROOT
OUT = ROOT/'results/onnx-consistency'
MODELS = {'c2_s2026': 'results/climb_curriculum_c2_s2026/Climb/Climb-8000057', 'c2_s2027': 'results/climb_curriculum_c2_s2027/Climb/Climb-8000027'}
SETS, MAX_OBS, STEPS, TOL = ['two-step', 'v2-standard'], 4000, 400, 1e-4


def collect(label, model, eval_set):
    args = []
    params = dict(base.ENV_PARAMS)
    if eval_set != 'two-step':
        path, _ = c2.pool_file(eval_set);args.append('--climb-curriculum='+str(path));params['curriculum_stage'] = 0.0
    folder = OUT/label/eval_set;folder.mkdir(parents=True, exist_ok=True);args.append('--climb-output='+str(folder/'runtime'))
    channel = EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=100)
    parameters = EnvironmentParametersChannel()
    for k, v in params.items():parameters.set_float_parameter(k, v)
    stats = StatsSideChannel()
    session = ort.InferenceSession(model, providers=['CPUExecutionProvider'])
    env = UnityEnvironment(file_name=str(c2.PLAYER), base_port=7400, worker_id=0, no_graphics=True, timeout_wait=300,
                           additional_args=args+['-logFile', str(folder/'player.log')], side_channels=[channel, stats, parameters])
    obs = []
    try:
        env.reset();behavior = next(iter(env.behavior_specs))
        for _ in range(STEPS):
            d, _ = env.get_steps(behavior)
            if len(d):
                o = d.obs[0].astype(np.float32);obs.append(o)
                cont, disc = session.run(['deterministic_continuous_actions', 'deterministic_discrete_actions'], {'obs_0': o, 'action_masks': np.ones((len(d), 4), np.float32)})
                env.set_actions(behavior, ActionTuple(continuous=cont, discrete=disc.astype(np.int32)))
            env.step();stats.get_and_reset_stats()
    finally:
        env.close();player_guard.reap(f'onnx-consistency {label} {eval_set}')
    return np.concatenate(obs)[:MAX_OBS]


def actor(pt):
    set_torch_config(TorchSettings(device='cpu'));torch.set_default_device('cpu')  # training ran with torch_settings.device cpu
    settings = NetworkSettings(normalize=True, hidden_units=256, num_layers=2, deterministic=True)
    spec = ObservationSpec(shape=(327,), dimension_property=(DimensionProperty.NONE,), observation_type=ObservationType.DEFAULT, name='obs_0')
    a = SimpleActor([spec], settings, ActionSpec(continuous_size=6, discrete_branches=(2, 2)), conditional_sigma=False, tanh_squash=False)
    a.load_state_dict(torch.load(pt, map_location='cpu', weights_only=False)['Policy'], strict=True);a.eval()
    return a


def compare(a, session, x):
    masks = np.ones((len(x), 4), np.float32)
    with torch.no_grad():
        _, run_out, _ = a.get_action_and_stats([torch.from_numpy(x)], masks=torch.from_numpy(masks))
        enc, _ = a.network_body([torch.from_numpy(x)])
        logits = [b(enc).numpy() for b in a.action_model._discrete_distribution.branches]
    tc, td = run_out['env_action'].continuous, run_out['env_action'].discrete
    oc, od = session.run(['deterministic_continuous_actions', 'deterministic_discrete_actions'], {'obs_0': x, 'action_masks': masks})
    oc2, od2 = session.run(['deterministic_continuous_actions', 'deterministic_discrete_actions'], {'obs_0': x, 'action_masks': masks})
    margin = np.stack([np.abs(l[:, 1]-l[:, 0]) for l in logits], 1)
    dis = od.astype(np.int64) != td.astype(np.int64)
    return dict(n=len(x), continuousMaxAbsDiff=float(np.abs(oc-tc).max()), continuousMeanAbsDiff=float(np.abs(oc-tc).mean()), continuousSaturatedShare=float((np.abs(tc) >= 1-1e-6).mean()),
                discreteDisagreements=int(dis.sum()), discreteDisagreementsAboveMargin=int((dis & (margin >= TOL)).sum()), minLogitMargin=float(margin.min()),
                onnxRepeatIdentical=bool(np.array_equal(oc, oc2) and np.array_equal(od, od2)))


def main():
    for line in c2.PLAYER_MANIFEST.read_text().splitlines():
        if line.strip():
            digest, name = line.split(' ', 1)
            assert base.sha(c2.PLAYER.parent/name.lstrip('*')) == digest, f'player build file {name} does not match the recorded hash'
    result = dict(tolerance=TOL, torch=torch.__version__, onnxruntime=ort.__version__, models={})
    for label, stem in MODELS.items():
        onnx, pt = str(ROOT/(stem+'.onnx')), str(ROOT/(stem+'.pt'))
        path = OUT/label/'obs.npz'
        if path.exists():data = dict(np.load(path))
        else:
            data = {s: collect(label, onnx, s) for s in SETS};path.parent.mkdir(parents=True, exist_ok=True);np.savez_compressed(path, **data)
        a = actor(pt);session = ort.InferenceSession(onnx, providers=['CPUExecutionProvider']);rng = np.random.default_rng(0);checks = {}
        for s, x in data.items():
            checks[s] = compare(a, session, x)
            checks[s+'+noise'] = compare(a, session, (x+rng.standard_normal(x.shape)).astype(np.float32))
        passed = all(c['continuousMaxAbsDiff'] <= TOL and c['discreteDisagreementsAboveMargin'] == 0 and c['onnxRepeatIdentical'] for c in checks.values())
        result['models'][label] = dict(onnx=stem+'.onnx', onnxSha256=base.sha(onnx), pt=stem+'.pt', ptSha256=base.sha(pt), checks=checks, passed=passed)
        (OUT/label/'report.json').write_text(json.dumps(result['models'][label], indent=2)+'\n', encoding='utf-8')
    result['passed'] = all(m['passed'] for m in result['models'].values());result['time'] = time.strftime('%Y-%m-%dT%H:%M:%S')
    dst = ROOT/'docs/data/confirm/onnx_consistency.json';dst.parent.mkdir(parents=True, exist_ok=True);dst.write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(result, indent=1))


if __name__ == '__main__':
    player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
