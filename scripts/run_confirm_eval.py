"""One-shot layer 3 confirmation run (docs/LAYER3_CONFIRMATION_PREREG.md). Same protocol and code as scripts/run_c2_eval.py
(scripts/run_curriculum_eval.py: deterministic ONNX actions, ground starts, one-stage eval pool, per structure the first 20 episodes by start
order, two-step = first 200 episodes), on the C2 player Builds/ClimbCurriculumC2/Climb.exe (hash-checked against
results/climb-curriculum-c2/player-sha256.txt).

Sets: two-step (training two-step, §16 regression) and confirm-easy / confirm-standard / confirm-hard (structures/box-stacks-v3-confirm,
every structure its own cluster). Models: only the final checkpoints of climb_curriculum_c2_s2026 / s2027, file hashes as pre-registered.

Write-once: the run writes results/confirm-eval/started.json first and refuses to start if it exists, so the set is run once. A model/set
that fails technically is not retried by this script; the failure is reported to the user. --prepare writes the pool files only (no player)
and prints their hashes for the pre-registration. Analysis: scripts/analyze_confirm_eval.py."""
import argparse
import json
import time
import run_curriculum_eval as base

ROOT = base.ROOT
OUT = ROOT/'results/confirm-eval'
PLAYER = ROOT/'Builds/ClimbCurriculumC2/Climb.exe'
PLAYER_MANIFEST = ROOT/'results/climb-curriculum-c2/player-sha256.txt'
MANIFEST = ROOT/'structures/box-stacks-v3-confirm/manifest.json'
SETS = ['two-step', 'confirm-easy', 'confirm-standard', 'confirm-hard']
MODELS = {'c2_s2026': 'results/climb_curriculum_c2_s2026/Climb/Climb-8000057.onnx', 'c2_s2027': 'results/climb_curriculum_c2_s2027/Climb/Climb-8000027.onnx'}
MODEL_SHA256 = {'c2_s2026': 'bb08ab192e15bb80e01344672b408ac7b5590cc194064d7a21a2e73cc21f0134',
                'c2_s2027': '32e71adee65e4d90acdf718911394204d3c5b46ec87ccf9c05ba1987718955d2'}


def pool_file(eval_set):
    """Confirmation tiers as one-stage curriculum files (version 1, twoStepShare 0) that ClimbCurriculum.Load verifies."""
    tier = eval_set.split('-')[1].capitalize()
    entries = [dict(id=e['id'], path=e['path'], sha256=e['sha256'], cluster=e['id']) for e in json.loads(MANIFEST.read_text())['structures'] if e['difficulty'] == tier]
    c1 = json.loads(base.CURRICULUM.read_text())
    folder = OUT/'pools';folder.mkdir(parents=True, exist_ok=True);path = folder/f'{eval_set}.json'
    document = dict(version=1, root='../../..', rule=f'confirmation pool {eval_set}; not used for training or model selection', twoStepShare=0.0, twoStep=c1['twoStep'],
                    stages=[dict(name='eval', pool='eval', structures=entries)], validation=[])
    text = json.dumps(document, indent=2)+'\n'
    if not path.exists():path.write_text(text, encoding='utf-8')
    assert path.read_text() == text, f'{path} differs from the pre-registered pool'
    return path, entries


base.OUT, base.PLAYER, base.pool_file = OUT, PLAYER, pool_file


def main():
    p = argparse.ArgumentParser();p.add_argument('--prepare', action='store_true');a = p.parse_args()
    if a.prepare:
        for s in SETS[1:]:path, entries = pool_file(s);print(s, len(entries), base.sha(path))
        for k, m in MODELS.items():print(k, m, base.sha(ROOT/m))
        return
    for k, m in MODELS.items():assert base.sha(ROOT/m) == MODEL_SHA256[k], f'{m} is not the pre-registered checkpoint'
    for line in PLAYER_MANIFEST.read_text().splitlines():
        if line.strip():
            digest, name = line.split(' ', 1)
            assert base.sha(PLAYER.parent/name.lstrip('*')) == digest, f'player build file {name} does not match the recorded hash'
    marker = OUT/'started.json'
    if marker.exists():raise SystemExit(f'{marker} exists: the confirmation set has been run; it is run once (LAYER3_CONFIRMATION_PREREG.md)')
    OUT.mkdir(parents=True, exist_ok=True)
    marker.write_text(json.dumps(dict(started=time.strftime('%Y-%m-%dT%H:%M:%S'), models=MODELS, sets=SETS), indent=2)+'\n', encoding='utf-8')
    for label in MODELS:
        for eval_set in SETS:base.run(label, ROOT/MODELS[label], eval_set)


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
