"""§7.4 checkpoint curve of C2 on the v2 evaluation set only (docs/LAYER3_CONFIRMATION_PREREG.md §5). Report only: no checkpoint is selected
from it; the confirmation run uses the final checkpoints regardless. Same protocol, code and player as scripts/run_c2_eval.py.
Points: for each seed, the saved checkpoint closest to 1M, 2M, ..., 7M C2 steps (steps restart at 0 after --initialize-from the C0+C1
8M checkpoint); the 8M point is the final checkpoint, already evaluated in results/curriculum-c2-eval (not re-run).
Writes results/c2-curve/<label>/<set>/report.json; analysis: scripts/analyze_c2_curve.py."""
import re
import run_c2_eval as c2
import run_curriculum_eval as base

ROOT = base.ROOT
OUT = ROOT/'results/c2-curve'
SETS = ['v2-easy', 'v2-standard', 'v2-hard']


def points():
    out = {}
    for seed in ('2026', '2027'):
        files = {int(re.search(r'Climb-(\d+)\.onnx$', f.name).group(1)): f for f in (ROOT/f'results/climb_curriculum_c2_s{seed}/Climb').glob('Climb-*.onnx')}
        for k in range(1, 8):
            step = min(files, key=lambda s: abs(s-k*1_000_000));assert abs(step-k*1_000_000) < 1000, (seed, k, step)
            out[f'c2_s{seed}_{k}M'] = files[step]
    return out


base.OUT = OUT  # the v2 tier pools are written under OUT/pools by run_curriculum_eval.pool_file (same entries as for C2 acceptance)


def main():
    for line in c2.PLAYER_MANIFEST.read_text().splitlines():
        if line.strip():
            digest, name = line.split(' ', 1)
            assert base.sha(c2.PLAYER.parent/name.lstrip('*')) == digest, f'player build file {name} does not match the recorded hash'
    for label, model in points().items():
        for eval_set in SETS:base.run(label, model, eval_set)


if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
