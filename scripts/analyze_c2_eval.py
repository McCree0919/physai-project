"""Analysis of results/curriculum-c2-eval (scripts/run_c2_eval.py) against the acceptance criteria of docs/LAYER3_C2_DESIGN.md §4.2.
Unit = structure (mean success of its 20 deterministic ground-start episodes), structures equally weighted; 95% intervals by cluster
bootstrap (10000 resamples, as analyze_curriculum_eval.py). Only structures that pass the geometry check are scored; the number excluded
and the scripted controller completion (one run per structure, results/curriculum-c2-eval/audit for the C2 sets,
results/feasibility-audit for v2 eval) are reported next to each set. Paired C2 - C0+C1 per seed on c2-A .. c2-D (same structures, same
player). Writes docs/data/curriculum-c2-eval/{summary.json,structures.csv}."""
import collections
import csv
import json
from pathlib import Path
import statistics
import analyze_curriculum_eval as prior

ROOT = Path(__file__).resolve().parents[1]
EVAL = ROOT/'results/curriculum-c2-eval'
OUT = ROOT/'docs/data/curriculum-c2-eval'
AUDIT_SET = {'c2-A': ('c2', 'c2-eval-A'), 'c2-B': ('c2', 'c2-eval-B'), 'c2-C': ('c2', 'c2-eval-C'), 'c2-D': ('c2', 'c2-eval-D'), 'c2-F': ('c2', 'c2-F'),
             'v2-easy': ('v2', 'v2-eval'), 'v2-standard': ('v2', 'v2-eval'), 'v2-hard': ('v2', 'v2-eval')}
SEEDS = ('s2026', 's2027')


def audits():
    out = {}
    for kind, path in (('c2', EVAL/'audit/audit.csv'), ('v2', ROOT/'results/feasibility-audit/audit.csv')):
        with open(path, encoding='utf-8', newline='') as stream:
            for r in csv.DictReader(stream):out[kind, r['set'], r['id']] = r
    return out


def score(label, eval_set, audit, rows_out):
    path = EVAL/label/eval_set/'report.json'
    if not path.exists():return None, None
    rep = json.loads(path.read_text());pool = json.loads((EVAL/'pools'/f'{eval_set}.json').read_text())['stages'][0]['structures']
    cluster = {e['id']: e['cluster'] for e in pool};ids = list(cluster)
    by = collections.defaultdict(list)
    for r in rep['episodes']:by[r['structure']].append(r)
    assert set(by) == set(ids) and all(len(by[i]) == 20 for i in ids), (label, eval_set)
    kind, audit_set = AUDIT_SET[eval_set]
    feasible = [i for i in ids if audit[kind, audit_set, i]['feasible'] == '1']
    s = {i: sum(r['success'] == '1' for r in by[i])/20 for i in feasible}
    eps = [r for i in feasible for r in by[i]]
    scripted = {i: float(audit[kind, audit_set, i]['controllerSuccess']) for i in feasible}
    entry = dict(structures=len(ids), feasible=len(feasible), excludedByGeometry=len(ids)-len(feasible), clusters=len({cluster[i] for i in feasible}), episodes=len(eps),
                 mean=statistics.mean(s.values()), ci95=prior.bootstrap(s, cluster), structuresAll20=sum(v == 1 for v in s.values()), structuresZero=sum(v == 0 for v in s.values()),
                 level1=sum(r['level1'] == '1' for r in eps)/len(eps), fallOrOutOfBounds=sum(r['reason'] in ('Fall', 'OutOfBounds') for r in eps)/len(eps),
                 failureStages=dict(collections.Counter(prior.stage(r) for r in eps if r['success'] != '1')), failureReasons=dict(collections.Counter(r['reason'] for r in eps if r['success'] != '1')),
                 scripted=dict(mean=statistics.mean(scripted.values()), completed=int(sum(scripted.values()))),
                 vsScripted=dict(collections.Counter(f"policy{'>=0.5' if s[i] >= .5 else '<0.5'}/scripted{int(scripted[i])}" for i in feasible)),
                 modelSha256=rep['summary']['modelSha256'], playerSha256=rep['summary']['playerSha256'], poolSha256=rep['summary']['poolSha256'], droppedOutsideSet=rep['summary']['droppedOutsideSet'])
    for i in ids:
        st = collections.Counter(prior.stage(r) for r in by[i])
        rows_out.append(dict(model=label, set=eval_set, structure=i, cluster=cluster[i], feasible=audit[kind, audit_set, i]['feasible'], scripted=audit[kind, audit_set, i]['controllerSuccess'],
                             success=sum(r['success'] == '1' for r in by[i]), episodes=20, belowLevel1=st['below-level1'], level1NotLevel2=st['level1-not-level2'], level2NotGoal=st['level2-not-goal'],
                             reasons='|'.join(f'{k}:{v}' for k, v in sorted(collections.Counter(r['reason'] for r in by[i] if r['success'] != '1').items()))))
    return entry, (s, cluster)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    audit = audits();rows = [];summary = dict(bootstrapResamples=prior.BOOT, sets={}, twoStep={}, paired={}, acceptance={}, auditA={})
    scores = {}
    for label in ('c2_s2026', 'c2_s2027', 'c0c1_s2026', 'c0c1_s2027'):
        two = EVAL/label/'two-step/report.json'
        if two.exists():
            eps = json.loads(two.read_text())['episodes']
            summary['twoStep'][label] = dict(episodes=len(eps), success=sum(r['success'] == '1' for r in eps)/len(eps), fallOrOutOfBounds=sum(r['reason'] in ('Fall', 'OutOfBounds') for r in eps)/len(eps),
                                             reasons=dict(collections.Counter(r['reason'] for r in eps)))
        for eval_set in AUDIT_SET:
            entry, sc = score(label, eval_set, audit, rows)
            if entry:summary['sets'].setdefault(eval_set, {})[label] = entry;scores[label, eval_set] = sc
    for seed in SEEDS:
        for eval_set in ('c2-A', 'c2-B', 'c2-C', 'c2-D'):
            a, b = scores.get((f'c2_{seed}', eval_set)), scores.get((f'c0c1_{seed}', eval_set))
            if a and b:
                d = {i: a[0][i]-b[0][i] for i in a[0]}
                summary['paired'][f'{eval_set}/{seed}'] = dict(c2=statistics.mean(a[0].values()), c0c1=statistics.mean(b[0].values()), difference=statistics.mean(d.values()), ci95=prior.bootstrap(d, a[1]),
                                                              c2Better=sum(v > 0 for v in d.values()), c0c1Better=sum(v < 0 for v in d.values()), equal=sum(v == 0 for v in d.values()))
        # §4.2, per seed.
        label = f'c2_{seed}';sets = summary['sets'];checks = {}
        for st in ('c2-B', 'c2-C', 'c2-D'):
            e = sets[st][label];checks[st] = dict(value=e['mean'], low=e['ci95'][0], rule='>=0.70 and lower >=0.60', passed=e['mean'] >= .70 and e['ci95'][0] >= .60)
        e = sets['c2-A'][label];checks['c2-A'] = dict(value=e['mean'], low=e['ci95'][0], rule='>=0.95', passed=e['mean'] >= .95)
        t = summary['twoStep'][label];checks['two-step'] = dict(value=t['success'], rule='>=0.90 (200 episodes)', passed=t['success'] >= .90)
        e = sets['v2-easy'][label];checks['v2-easy'] = dict(value=e['mean'], low=e['ci95'][0], rule='>=0.80 and lower >=0.70', passed=e['mean'] >= .80 and e['ci95'][0] >= .70)
        for st in ('c2-A', 'c2-B', 'c2-C', 'c2-D'):
            e = sets[st][label];checks[f'{st} fall+out'] = dict(value=e['fallOrOutOfBounds'], rule='<=0.05', passed=e['fallOrOutOfBounds'] <= .05)
        summary['acceptance'][seed] = dict(checks=checks, passed=all(c['passed'] for c in checks.values()))
    # Scripted controller on the A training structures that failed repeatedly in training (reference only).
    fails = json.loads((EVAL/'a-repeated-failures.json').read_text())
    for i in fails['ids']:
        r = audit['c2', 'a-repeated-failure', i]
        summary['auditA'][i] = dict(feasible=r['feasible'], reason=r['reason'], controllerSuccess=r['controllerSuccess'], tierReached=r['tierReached'], tierCount=r['tierCount'], stage=r['stage'],
                                    training={s: fails['perSeed'][s].get(i) for s in SEEDS})
    (OUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    with open(OUT/'structures.csv', 'w', encoding='utf-8', newline='') as stream:
        w = csv.DictWriter(stream, fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    print(json.dumps({k: summary[k] for k in ('acceptance', 'paired', 'twoStep')}, indent=1))


if __name__ == '__main__':main()
