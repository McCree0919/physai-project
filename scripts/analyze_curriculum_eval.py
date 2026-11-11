"""Analysis of results/curriculum-c0c1-eval (docs/LAYER3_CURRICULUM_C0C1.md). Standard library only.
Unit = structure: s_i = success mean of its 20 deterministic ground-start episodes; a set's score is mean(s_i), structures equally weighted.
95% intervals: cluster bootstrap (10000 resamples of whole clusters, all structures of a drawn cluster kept), percentile method.
Only structures that pass the geometry check (BoxStackFeasibility, from the audits) are scored. The scripted controller (one run per
structure, same audits) is given next to each set as a reference upper bound, with the same intervals.
Writes docs/data/curriculum-c0c1-eval/{summary.json,structures.csv,episodes.csv}."""
import collections
import csv
import json
from pathlib import Path
import random
import statistics

ROOT = Path(__file__).resolve().parents[1]
EVAL = ROOT/'results/curriculum-c0c1-eval'
OUT = ROOT/'docs/data/curriculum-c0c1-eval'
SETS = {'c1-val': 'c1-validation', 'v2-easy': 'v2-eval', 'v2-standard': 'v2-eval', 'v2-hard': 'v2-eval'}
BOOT = 10000
FEATURES = ['constructionLevels', 'levels', 'overhangCount', 'maxRise', 'maxOverhang', 'maxGap', 'totalHeight']


def audit():
    rows = {}
    for path, kind in ((ROOT/'results/curriculum-audit/audit.csv', 'curriculum'), (ROOT/'results/feasibility-audit/audit.csv', 'v2')):
        with open(path, encoding='utf-8', newline='') as stream:
            for r in csv.DictReader(stream):rows[r['set'], r['id']] = r
    return rows


def features():
    out = {}
    for manifest in ('structures/box-stacks-v2/train/manifest.json', 'structures/box-stacks-v2/eval/manifest.json'):
        for e in json.loads((ROOT/manifest).read_text())['structures']:out[e['id']] = e['features']
    return out


def bootstrap(scores, clusters, seed=1, paired=None):
    """scores: id -> value; clusters: id -> cluster. Percentile 95% interval of the structure mean over cluster resamples."""
    groups = collections.defaultdict(list)
    for sid in scores:groups[clusters[sid]].append(sid)
    keys = sorted(groups);rng = random.Random(seed);means = []
    for _ in range(BOOT):
        drawn = [sid for _ in keys for sid in groups[keys[rng.randrange(len(keys))]]]
        means.append(sum(scores[s] for s in drawn)/len(drawn))
    means.sort()
    return [means[int(.025*BOOT)], means[int(.975*BOOT)-1]]


def stage(r):
    if r['success'] == '1':return 'success'
    if r['level1'] != '1':return 'below-level1'
    if r['level2'] != '1':return 'level1-not-level2'
    return 'level2-not-goal'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    audits, feats = audit(), features()
    pools = {s: json.loads((EVAL/'pools'/f'{s}.json').read_text())['stages'][0]['structures'] for s in SETS}
    cluster = {e['id']: e['cluster'] for s in pools for e in pools[s]}
    summary, structure_rows, episode_rows = dict(bootstrapResamples=BOOT, sets={}, twoStep={}), [], []
    scores = {}
    for model_dir in sorted(p for p in EVAL.iterdir() if p.is_dir() and p.name != 'pools'):
        label = model_dir.name
        two = model_dir/'two-step/report.json'
        if two.exists():
            rep = json.loads(two.read_text());eps = rep['episodes']
            summary['twoStep'][label] = dict(episodes=len(eps), success=sum(r['success'] == '1' for r in eps)/len(eps), level1=sum(r['level1'] == '1' for r in eps)/len(eps),
                                             level2=sum(r['level2'] == '1' for r in eps)/len(eps), reasons=dict(collections.Counter(r['reason'] for r in eps)),
                                             modelSha256=rep['summary']['modelSha256'], playerSha256=rep['summary']['playerSha256'])
            for r in eps:episode_rows.append(dict(model=label, set='two-step', structure='training-two-step', arena=r['arena'], success=r['success'], level1=r['level1'], level2=r['level2'], reason=r['reason'], decisions=r['decisions'], sim_seconds=r['sim_seconds']))
        for eval_set, audit_set in SETS.items():
            path = model_dir/eval_set/'report.json'
            if not path.exists():continue
            rep = json.loads(path.read_text());by = collections.defaultdict(list)
            for r in rep['episodes']:by[r['structure']].append(r)
            ids = [e['id'] for e in pools[eval_set]]
            assert set(by) == set(ids) and all(len(by[i]) == 20 for i in ids), (label, eval_set)
            feasible = [i for i in ids if audits[audit_set, i]['feasible'] == '1']
            s = {i: sum(r['success'] == '1' for r in by[i])/20 for i in feasible}
            scores[label, eval_set] = s
            eps = [r for i in feasible for r in by[i]]
            stages = collections.Counter(stage(r) for r in eps);reasons = collections.Counter(r['reason'] for r in eps if r['success'] != '1')
            scripted = {i: float(audits[audit_set, i]['controllerSuccess']) for i in feasible}
            entry = dict(structures=len(ids), feasible=len(feasible), excludedByGeometry=len(ids)-len(feasible), clusters=len({cluster[i] for i in feasible}),
                         episodes=len(eps), mean=statistics.mean(s.values()), ci95=bootstrap(s, cluster),
                         structuresAll20=sum(v == 1 for v in s.values()), structuresZero=sum(v == 0 for v in s.values()),
                         level1=sum(r['level1'] == '1' for r in eps)/len(eps), fallOrOutOfBounds=sum(r['reason'] in ('Fall', 'OutOfBounds') for r in eps)/len(eps),
                         failureStages=dict(stages), failureReasons=dict(reasons),
                         scripted=dict(mean=statistics.mean(scripted.values()), ci95=bootstrap(scripted, cluster), completed=int(sum(scripted.values()))),
                         modelSha256=rep['summary']['modelSha256'], playerSha256=rep['summary']['playerSha256'], poolSha256=rep['summary']['poolSha256'],
                         droppedOutsideSet=rep['summary']['droppedOutsideSet'])
            # Shape features of structures the policy does not always complete, by bins.
            bins = {}
            for f in ('constructionLevels', 'overhangCount'):
                g = collections.defaultdict(list)
                for i in feasible:g[int(feats[i][f])].append(s[i])
                bins[f] = {str(k): dict(n=len(v), mean=statistics.mean(v)) for k, v in sorted(g.items())}
            for f, edges in (('maxRise', [.6, .8, 1.0]), ('totalHeight', [1.5, 2.0, 2.5]), ('maxOverhang', [.01, .3, .5])):
                g = collections.defaultdict(list)
                for i in feasible:
                    v = feats[i][f];k = sum(v >= e for e in edges);g[k].append(s[i])
                names = [f'<{edges[0]}']+[f'{a}-{b}' for a, b in zip(edges, edges[1:])]+[f'>={edges[-1]}']
                bins[f] = {names[k]: dict(n=len(v), mean=statistics.mean(v)) for k, v in sorted(g.items())}
            entry['byFeature'] = bins
            # Policy vs scripted controller per structure.
            entry['vsScripted'] = dict(collections.Counter(f"policy{'>=0.5' if s[i] >= .5 else '<0.5'}/scripted{int(scripted[i])}" for i in feasible))
            summary['sets'].setdefault(eval_set, {})[label] = entry
            for i in ids:
                st = collections.Counter(stage(r) for r in by[i])
                structure_rows.append(dict(model=label, set=eval_set, structure=i, cluster=cluster[i], feasible=audits[audit_set, i]['feasible'],
                                           scripted=audits[audit_set, i]['controllerSuccess'], success=sum(r['success'] == '1' for r in by[i]), episodes=20,
                                           belowLevel1=st['below-level1'], level1NotLevel2=st['level1-not-level2'], level2NotGoal=st['level2-not-goal'],
                                           reasons='|'.join(f'{k}:{v}' for k, v in sorted(collections.Counter(r['reason'] for r in by[i] if r['success'] != '1').items())),
                                           **{f: round(feats[i][f], 4) for f in FEATURES}))
                for r in by[i]:episode_rows.append(dict(model=label, set=eval_set, structure=i, arena=r['arena'], success=r['success'], level1=r['level1'], level2=r['level2'], reason=r['reason'], decisions=r['decisions'], sim_seconds=r['sim_seconds']))
    # Paired curriculum - baseline, same seed, same structures (cluster bootstrap of the per-structure difference).
    summary['paired'] = {}
    for eval_set in ('c1-val', 'v2-easy'):
        for seed in ('s2026', 's2027'):
            a, b = scores.get((f'c0c1_{seed}', eval_set)), scores.get((f'height_{seed}_5M', eval_set))
            if a and b:
                d = {i: a[i]-b[i] for i in a}
                summary['paired'][f'{eval_set}/{seed}'] = dict(curriculum=statistics.mean(a.values()), baseline=statistics.mean(b.values()), difference=statistics.mean(d.values()),
                                                               ci95=bootstrap(d, cluster), curriculumBetter=sum(v > 0 for v in d.values()), baselineBetter=sum(v < 0 for v in d.values()))
    (OUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    for name, rows in (('structures.csv', structure_rows), ('episodes.csv', episode_rows)):
        with open(OUT/name, 'w', encoding='utf-8', newline='') as stream:
            w = csv.DictWriter(stream, fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    print(json.dumps(summary, indent=1)[:20000])


if __name__ == '__main__':main()
