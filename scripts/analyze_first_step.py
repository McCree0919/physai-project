"""Read-only diagnosis of first-level failures (docs/LAYER3_CURRICULUM_C0C1.md §7). Standard library only; no simulation.
Per v2 eval structure: the effective first step = top of the first standing surface on the geometry check's route (feasibility.route[1]),
its free tread depth/width, whether that surface is a box resting on another box (stacked, with the set-back of its front face), the
distance of the front-most box face from the spawn torso (z = -0.4), and the effective rises along the route. Joined with the evaluation
(level-1 episodes = 20 - belowLevel1, success) from docs/data/curriculum-c0c1-eval/structures.csv.
Writes docs/data/curriculum-c0c1-eval/first_step.csv and prints group means."""
import collections
import csv
import json
from pathlib import Path
import statistics

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT/'docs/data/curriculum-c0c1-eval'
SPAWN_Z = -.4
MODELS = ['c0c1_s2026', 'c0c1_s2027', 'height_s2026_5M', 'height_s2027_5M', 'twostep8m_s2026', 'twostep8m_s2027']


def geometry(entry):
    f = entry['feasibility'];surfaces = {s['id']: s for s in f['surfaces']}
    tops = [0.0]+[surfaces[s]['top'] for s in f['route'][1:]]
    rises = [b-a for a, b in zip(tops, tops[1:])]
    doc = json.loads((ROOT/entry['path']).read_text());boxes = {o['id']: o for o in doc['objects'] if o.get('settleAsDynamic')}
    first = boxes[f['route'][1]];p, s = first['pose']['position'], first['size'];bottom, front = p[1]-s[1]/2, p[2]-s[2]/2
    below = [q for q in boxes.values() if q is not first and abs(q['pose']['position'][1]+q['size'][1]/2-bottom) < .05
             and abs(q['pose']['position'][0]-p[0]) < (q['size'][0]+s[0])/2]
    setback = front-(below[0]['pose']['position'][2]-below[0]['size'][2]/2) if below else None
    nearest = min(o['pose']['position'][2]-o['size'][2]/2 for o in boxes.values())
    return dict(firstStand=surfaces[f['route'][1]]['top'], firstTreadDepth=surfaces[f['route'][1]]['freeDepth'], firstTreadWidth=surfaces[f['route'][1]]['freeWidth'],
                firstStacked=int(bool(below)), firstSetback=setback, frontDistance=nearest-SPAWN_Z, standingSteps=len(rises),
                rises='|'.join(f'{r:.2f}' for r in rises), tallSteps=sum(r >= .9 for r in rises), exposedTopEdges=entry['features']['exposedTopEdges'])


def main():
    evals = {(r['model'], r['structure']): r for r in csv.DictReader(open(DATA/'structures.csv', encoding='utf-8'))}
    rows = []
    for e in json.loads((ROOT/'structures/box-stacks-v2/eval/manifest.json').read_text())['structures']:
        g = geometry(e);row = dict(id=e['id'], difficulty=e['difficulty'], **g)
        for m in MODELS:
            r = evals.get((m, e['id']))
            row[m+'_level1'] = '' if r is None else 20-int(r['belowLevel1']);row[m+'_success'] = '' if r is None else int(r['success'])
        row['scripted'] = int(float(evals['c0c1_s2026', e['id']]['scripted']));rows.append(row)
    with open(DATA/'first_step.csv', 'w', encoding='utf-8', newline='') as stream:
        w = csv.DictWriter(stream, fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)

    def group(name, key):
        g = collections.defaultdict(list)
        for r in rows:g[(r['difficulty'], key(r))].append(r)
        print('==', name)
        for k in sorted(g):
            v = g[k];out = dict(n=len(v))
            for m in MODELS[:2]:out[m+' L1'] = round(statistics.mean(r[m+'_level1'] for r in v)/20, 2);out[m+' succ'] = round(statistics.mean(r[m+'_success'] for r in v)/20, 2)
            out['scripted'] = round(statistics.mean(r['scripted'] for r in v), 2)
            for f in ('firstTreadDepth', 'firstTreadWidth', 'frontDistance', 'exposedTopEdges'):out[f] = round(statistics.mean(r[f] for r in v), 3)
            print(k, out)
    first = lambda r: '~1.0' if r['firstStand'] >= .9 else '0.5'
    group('effective first step', first)
    group('effective first step x stacked', lambda r: first(r)+('/stacked' if r['firstStacked'] else '/single'))
    hard = [r for r in rows if r['difficulty'] == 'Hard']
    for m in MODELS[:2]:
        for k in ('0.5', '~1.0'):
            sel = [r for r in hard if first(r) == k]
            print(m, 'Hard', k, 'n', len(sel), 'level1 0/20:', sum(r[m+'_level1'] == 0 for r in sel), '20/20:', sum(r[m+'_level1'] == 20 for r in sel))
    print('frontDistance range', min(r['frontDistance'] for r in rows), max(r['frontDistance'] for r in rows))
    print('stacked set-backs (Hard, ~1.0):', sorted(round(r['firstSetback'], 2) for r in hard if r['firstStacked'] and first(r) == '~1.0'))


if __name__ == '__main__':main()
