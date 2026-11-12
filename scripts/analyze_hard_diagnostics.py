"""Analysis of results/hard-diagnostics (run_hard_diagnostics.py). Standard library only.
Per C0+C1 checkpoint, v2 Hard episodes grouped by the effective first step (docs/data/curriculum-c0c1-eval/first_step.csv: first standing
surface on the geometry route ~0.5 m, ~1.0 m single box, ~1.0 m two stacked boxes) and by outcome (never reached level 1 / reached it):
grip attempts (rising edges of a hand's grab request), grip successes, rejected request ticks with their reasons, cooldown and re-arm
blocking, legal-contact availability, maximum torso height (overall and while gripping) and time to first grip. Per-structure means are
averaged with structures equally weighted, as in the evaluation. Sets: v2-hard (60 structures) and the control v2-easy-tall (13 Easy
structures whose first step is a single ~1.0 m box). Writes docs/data/hard-diagnostics/{summary.json,episodes.csv}."""
import collections
import csv
import json
from pathlib import Path
import statistics

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'results/hard-diagnostics'
OUT = ROOT/'docs/data/hard-diagnostics'
FIRST = ROOT/'docs/data/curriculum-c0c1-eval/first_step.csv'
FIELDS = ['requestRisingEdges', 'gripSuccesses', 'rejectedHandTicks', 'cooldownBlockedTicks', 'rearmBlockedTicks', 'requestedHandTicks', 'legalContactTicks', 'freeHandTicks',
          'maximumTorsoY', 'maximumGripTorsoY', 'firstGripSeconds', 'grippedSeconds', 'physicsSamples']


def group(row):
    if float(row['firstStand']) < .9:return '0.5m'
    return '~1.0m stacked' if row['firstStacked'] == '1' else '~1.0m single'


def stats(episodes):
    """Structure-weighted means of the per-episode fields, plus pooled rejection-reason shares."""
    by = collections.defaultdict(list)
    for e in episodes:by[e['structure']].append(e)
    out = dict(structures=len(by), episodes=len(episodes))
    if not episodes:return out
    for f in FIELDS:
        per = [statistics.mean(float(e['observer'][f]) for e in v) for v in by.values()]
        out[f] = round(statistics.mean(per), 4) if per else None
    gripped = [e for e in episodes if e['observer']['gripSuccesses'] > 0]
    out['episodesWithAnyGrip'] = round(len(gripped)/len(episodes), 4) if episodes else None
    out['gripSuccessPerAttempt'] = round(sum(e['observer']['gripSuccesses'] for e in episodes)/max(1, sum(e['observer']['requestRisingEdges'] for e in episodes)), 4)
    out['legalContactShare'] = round(sum(e['observer']['legalContactTicks'] for e in episodes)/max(1, sum(e['observer']['freeHandTicks'] for e in episodes)), 4)
    reasons = collections.Counter()
    for e in episodes:
        for k, v in zip(e['observer']['rejectionReasons'] or [], e['observer']['rejectionCounts'] or []):reasons[k] += v
    total = sum(reasons.values())
    out['rejectionReasons'] = {k: dict(ticks=v, share=round(v/total, 4)) for k, v in reasons.most_common()}
    out['reasons'] = dict(collections.Counter(e['reason'] for e in episodes))
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    first = {r['id']: r for r in csv.DictReader(open(FIRST, encoding='utf-8'))}
    summary, rows = {}, []
    for folder in sorted(p for p in SOURCE.iterdir() if p.is_dir() and p.name != 'pools'):
        for eval_set, count in (('v2-hard', 60), ('v2-easy-tall', 13)):
            if not (folder/eval_set/'report.json').exists():continue
            rep = json.loads((folder/eval_set/'report.json').read_text());eps = rep['episodes']
            assert len(eps) == count*20 and all(e['observer'] for e in eps)
            entry = dict(modelSha256=rep['summary']['modelSha256'], playerSha256=rep['summary']['playerSha256'], joinChecks=rep['summary']['joinChecks'], groups={})
            for g in ('0.5m', '~1.0m single', '~1.0m stacked'):
                sel = [e for e in eps if group(first[e['structure']]) == g]
                if sel:entry['groups'][g] = dict(all=stats(sel), belowLevel1=stats([e for e in sel if e['level1'] != '1']), reachedLevel1=stats([e for e in sel if e['level1'] == '1']))
            summary.setdefault(folder.name, {})[eval_set] = entry
            for e in eps:
                o = e['observer']
                rows.append(dict(model=folder.name, set=eval_set, structure=e['structure'], group=group(first[e['structure']]), arena=e['arena'], success=e['success'], level1=e['level1'], level2=e['level2'],
                                 reason=e['reason'], sim_seconds=e['sim_seconds'], **{f: o[f] for f in FIELDS},
                                 rejections='|'.join(f'{k}:{v}' for k, v in zip(o['rejectionReasons'] or [], o['rejectionCounts'] or []))))
    (OUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    with open(OUT/'episodes.csv', 'w', encoding='utf-8', newline='') as stream:
        w = csv.DictWriter(stream, fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
    print(json.dumps(summary, indent=1)[:15000])


if __name__ == '__main__':main()
