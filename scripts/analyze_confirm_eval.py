"""Analysis of the one-shot confirmation run (scripts/run_confirm_eval.py) against LAYER3_DESIGN.md §16, as pre-registered in
docs/LAYER3_CONFIRMATION_PREREG.md. Unit = structure (mean success of its 20 deterministic ground-start episodes), structures equally
weighted, each structure its own cluster; 95% interval by cluster bootstrap (10000 resamples, seed 1; analyze_curriculum_eval.bootstrap).
Only structures that pass the geometry check (results/confirm-audit/audit.csv, feasible=1) are scored; the excluded count and the scripted
controller completions (on scored and excluded structures) are reported. Each seed is checked separately.
Writes docs/data/confirm/{summary.json,structures.csv}."""
import collections
import csv
import json
import statistics
from pathlib import Path
import analyze_curriculum_eval as prior

ROOT = Path(__file__).resolve().parents[1]
EVAL = ROOT/'results/confirm-eval'
OUT = ROOT/'docs/data/confirm'
TIERS = {'confirm-easy': (.80, .70), 'confirm-standard': (.60, .50), 'confirm-hard': (.30, .20)}  # §16: mean, 95% lower bound
SEEDS = ('s2026', 's2027')


def main():
    with open(ROOT/'results/confirm-audit/audit.csv', encoding='utf-8', newline='') as stream:audit = {r['id']: r for r in csv.DictReader(stream) if r['set'] == 'confirm'}
    summary = dict(bootstrapResamples=prior.BOOT, sets={}, twoStep={}, acceptance={});rows = []
    for seed in SEEDS:
        label = f'c2_{seed}';checks = {}
        rep = json.loads((EVAL/label/'two-step/report.json').read_text());eps = rep['episodes']
        t = summary['twoStep'][label] = dict(episodes=len(eps), success=sum(r['success'] == '1' for r in eps)/len(eps), fallOrOutOfBounds=sum(r['reason'] in ('Fall', 'OutOfBounds') for r in eps)/len(eps),
                                            reasons=dict(collections.Counter(r['reason'] for r in eps)), modelSha256=rep['summary']['modelSha256'], playerSha256=rep['summary']['playerSha256'])
        checks['two-step'] = dict(value=t['success'], rule='>=0.90 (200 episodes)', passed=t['success'] >= .90)
        for eval_set, (lo_mean, lo_ci) in TIERS.items():
            rep = json.loads((EVAL/label/eval_set/'report.json').read_text())
            ids = [e['id'] for e in json.loads((EVAL/'pools'/f'{eval_set}.json').read_text())['stages'][0]['structures']]
            by = collections.defaultdict(list)
            for r in rep['episodes']:by[r['structure']].append(r)
            assert set(by) == set(ids) and all(len(by[i]) == 20 for i in ids), (label, eval_set)
            feasible = [i for i in ids if audit[i]['feasible'] == '1'];excluded = [i for i in ids if i not in feasible]
            s = {i: sum(r['success'] == '1' for r in by[i])/20 for i in feasible};e_ = [r for i in feasible for r in by[i]]
            e = summary['sets'].setdefault(eval_set, {})[label] = dict(
                structures=len(ids), feasible=len(feasible), excludedByGeometry=excluded, scriptedOnExcluded=sum(audit[i]['controllerSuccess'] == '1' for i in excluded),
                episodes=len(e_), mean=statistics.mean(s.values()), ci95=prior.bootstrap(s, {i: i for i in s}), structuresAll20=sum(v == 1 for v in s.values()), structuresZero=sum(v == 0 for v in s.values()),
                level1=sum(r['level1'] == '1' for r in e_)/len(e_), fallOrOutOfBounds=sum(r['reason'] in ('Fall', 'OutOfBounds') for r in e_)/len(e_),
                failureStages=dict(collections.Counter(prior.stage(r) for r in e_ if r['success'] != '1')), failureReasons=dict(collections.Counter(r['reason'] for r in e_ if r['success'] != '1')),
                scripted=dict(completed=sum(audit[i]['controllerSuccess'] == '1' for i in feasible), of=len(feasible)),
                vsScripted=dict(collections.Counter(f"policy{'>=0.5' if s[i] >= .5 else '<0.5'}/scripted{audit[i]['controllerSuccess']}" for i in feasible)),
                modelSha256=rep['summary']['modelSha256'], playerSha256=rep['summary']['playerSha256'], poolSha256=rep['summary']['poolSha256'], droppedOutsideSet=rep['summary']['droppedOutsideSet'])
            checks[eval_set] = dict(value=e['mean'], low=e['ci95'][0], n=len(feasible), rule=f'>={lo_mean:.2f} and lower >={lo_ci:.2f}', passed=e['mean'] >= lo_mean and e['ci95'][0] >= lo_ci)
            checks[f'{eval_set} fall+out'] = dict(value=e['fallOrOutOfBounds'], rule='<=0.05', passed=e['fallOrOutOfBounds'] <= .05)
            for i in ids:
                st = collections.Counter(prior.stage(r) for r in by[i])
                rows.append(dict(model=label, set=eval_set, structure=i, feasible=audit[i]['feasible'], scripted=audit[i]['controllerSuccess'], success=sum(r['success'] == '1' for r in by[i]), episodes=20,
                                 belowLevel1=st['below-level1'], level1NotLevel2=st['level1-not-level2'], level2NotGoal=st['level2-not-goal'],
                                 reasons='|'.join(f'{k}:{v}' for k, v in sorted(collections.Counter(r['reason'] for r in by[i] if r['success'] != '1').items()))))
        summary['acceptance'][seed] = dict(checks=checks, passed=all(c['passed'] for c in checks.values()))
    summary['allSeedsPassed'] = all(summary['acceptance'][s]['passed'] for s in SEEDS)
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    with open(OUT/'structures.csv', 'w', encoding='utf-8', newline='') as stream:
        w = csv.DictWriter(stream, fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    print(json.dumps(dict(acceptance=summary['acceptance'], allSeedsPassed=summary['allSeedsPassed']), indent=1))


if __name__ == '__main__':main()
