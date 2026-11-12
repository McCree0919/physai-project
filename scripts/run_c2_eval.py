"""C2 acceptance evaluation (docs/LAYER3_C2_DESIGN.md §4.2, docs/LAYER3_C2_REPORT.md §4). Same protocol and code as
scripts/run_curriculum_eval.py (deterministic ONNX actions, ground starts, one-stage eval pool, per structure the first 20 episodes by start
order, two-step = first 200 episodes), on the C2 player Builds/ClimbCurriculumC2/Climb.exe (hash-checked against
results/climb-curriculum-c2/player-sha256.txt). Every model, the C0+C1 checkpoints included, runs on this one player.

Sets:
  c2-A .. c2-D : the C2 held-out clusters of each stratum (pools.json validation, split by strata.json); acceptance.
  two-step, v2-easy : acceptance.   v2-standard, v2-hard, c2-F (reserved >=3-level stratum with a tall rise) : report only.
Models: the final checkpoints of climb_curriculum_c2_s2026/s2027 on every set; the C0+C1 8M checkpoints on c2-A .. c2-D, for the paired
comparison. Writes results/curriculum-c2-eval/<model>/<set>/report.json; analysis: scripts/analyze_c2_eval.py."""
import argparse
import json
from pathlib import Path
import run_curriculum_eval as base

ROOT = base.ROOT
OUT = ROOT/'results/curriculum-c2-eval'
PLAYER = ROOT/'Builds/ClimbCurriculumC2/Climb.exe'
PLAYER_MANIFEST = ROOT/'results/climb-curriculum-c2/player-sha256.txt'
POOLS = ROOT/'structures/curriculum-v2/pools.json'
STRATA = ROOT/'structures/curriculum-v2/strata.json'
STRATA_SETS = ['c2-A', 'c2-B', 'c2-C', 'c2-D']
MODELS = {f'c2_s{s}': base.final_onnx(f'climb_curriculum_c2_s{s}') for s in (2026, 2027)}
MODELS.update({k: base.MODELS[k] for k in ('c0c1_s2026', 'c0c1_s2027')})
PLAN = {'c2_s2026': ['two-step']+STRATA_SETS+['v2-easy', 'v2-standard', 'v2-hard', 'c2-F'], 'c2_s2027': ['two-step']+STRATA_SETS+['v2-easy', 'v2-standard', 'v2-hard', 'c2-F'],
        'c0c1_s2026': STRATA_SETS, 'c0c1_s2027': STRATA_SETS}
v1_pool_file = base.pool_file


def entries(eval_set):
    rows = {r['id']: r for r in json.loads(STRATA.read_text(encoding='utf-8'))['structures']}
    stratum = eval_set.split('-')[1]
    if stratum == 'F':return [dict(id=r['id'], path=r['path'], sha256=r['sha256'], cluster=r['cluster']) for r in rows.values() if r['stratum'] == 'F' and r['split'] == 'reserved']
    out = [e for e in json.loads(POOLS.read_text(encoding='utf-8'))['validation'] if rows[e['id']]['stratum'] == stratum]
    assert all(rows[e['id']]['split'] == 'eval' for e in out)
    return out


def pool_file(eval_set):
    """C2 sets as one-stage curriculum files (version 1, twoStepShare 0) that ClimbCurriculum.Load verifies; v2 tiers as before."""
    if not eval_set.startswith('c2-'):return v1_pool_file(eval_set)
    c1 = json.loads(base.CURRICULUM.read_text())
    folder = OUT/'pools';folder.mkdir(parents=True, exist_ok=True);path = folder/f'{eval_set}.json';items = entries(eval_set)
    document = dict(version=1, root='../../..', rule=f'evaluation pool {eval_set}; not used for training', twoStepShare=0.0, twoStep=c1['twoStep'],
                    stages=[dict(name='eval', pool='eval', structures=items)], validation=[])
    text = json.dumps(document, indent=2)+'\n'
    if not path.exists() or path.read_text() != text:path.write_text(text, encoding='utf-8')
    return path, items


# base.run reads these module globals, so the C2 player, output folder and pools apply to every run below.
base.OUT, base.PLAYER, base.pool_file = OUT, PLAYER, pool_file


def main():
    p = argparse.ArgumentParser();p.add_argument('--only');p.add_argument('--set');a = p.parse_args()
    for line in PLAYER_MANIFEST.read_text().splitlines():
        if line.strip():
            digest, name = line.split(' ', 1)
            assert base.sha(PLAYER.parent/name.lstrip('*')) == digest, f'player build file {name} does not match the recorded hash'
    for label, sets in PLAN.items():
        if a.only and label != a.only:continue
        for eval_set in sets:
            if a.set and eval_set != a.set:continue
            base.run(label, ROOT/MODELS[label], eval_set)


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
