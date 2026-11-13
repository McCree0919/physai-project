"""C2 checkpoint curve on the v2 evaluation set (scripts/run_c2_curve.py; final point from results/curriculum-c2-eval). Per seed, point and
tier: structure-weighted mean, 95% cluster-bootstrap interval (10000, seed 1) and whether the §16 tier rule would hold at that point.
Report only, never used to select a checkpoint. Writes docs/data/c2-curve/summary.json and prints a table."""
import json
import statistics
from pathlib import Path
import analyze_curriculum_eval as prior
import run_c2_curve as curve

ROOT = Path(__file__).resolve().parents[1]
RULE = {'v2-easy': (.80, .70), 'v2-standard': (.60, .50), 'v2-hard': (.30, .20)}


def main():
    v2 = {e['id']: e for e in json.loads((ROOT/'structures/box-stacks-v2/eval/manifest.json').read_text())['structures']}
    labels = {k: (k, ROOT/'results/c2-curve') for k in curve.points()}
    labels.update({f'c2_s{s}_8M': (f'c2_s{s}', ROOT/'results/curriculum-c2-eval') for s in ('2026', '2027')})
    out = {}
    for name, (label, folder) in sorted(labels.items(), key=lambda kv: (kv[0][:7], int(kv[0].split('_')[-1][:-1]))):
        for eval_set, (m, lo) in RULE.items():
            rep = json.loads((folder/label/eval_set/'report.json').read_text())
            by = {}
            for r in rep['episodes']:by.setdefault(r['structure'], []).append(r['success'] == '1')
            assert all(len(v) == 20 for v in by.values()) and all(i in v2 for i in by) and len(by) == 60
            s = {i: sum(v)/20 for i, v in by.items()};ci = prior.bootstrap(s, {i: i for i in s});mean = statistics.mean(s.values())
            eps = rep['episodes']
            out.setdefault(name, {})[eval_set] = dict(mean=mean, ci95=ci, rule16=mean >= m and ci[0] >= lo, fallOrOutOfBounds=sum(r['reason'] in ('Fall', 'OutOfBounds') for r in eps)/len(eps),
                                                    model=rep['summary']['model'], modelSha256=rep['summary']['modelSha256'])
    dst = ROOT/'docs/data/c2-curve';dst.mkdir(parents=True, exist_ok=True);(dst/'summary.json').write_text(json.dumps(out, indent=2)+'\n', encoding='utf-8')
    for name, sets in out.items():
        print(name.ljust(14), '  '.join(f"{k[3:]}={v['mean']:.3f}[{v['ci95'][0]:.3f},{v['ci95'][1]:.3f}]{'' if v['rule16'] else '*'}" for k, v in sets.items()))


if __name__ == '__main__':main()
