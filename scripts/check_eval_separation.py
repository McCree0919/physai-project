"""Train/eval separation check for the layer 3 §16 evaluation (2026-11-12). Standard library only.
Independent of the C# freeze: for every structure of the C2 training pools (A-D) and of the C0/C1 pools, the smallest grid distance
(0.05 m units, max over box bounds after canonical ordering) to any v2 eval structure with the same support graph, original or x-mirrored,
across all difficulties. Linked = distance <= 1 (the §11 / generator-v2 rule). Also lists near-duplicate pairs inside the eval set
(all difficulties). Prints a JSON summary."""
import collections
import json
from pathlib import Path
from analyze_zero_shot import boxes, graph

ROOT = Path(__file__).resolve().parents[1]


def variants(key):
    bs = boxes(key);mirror = [(-b[3], b[1], b[2], -b[0], b[4], b[5]) for b in bs]
    return [graph(bs), graph(mirror)]


def distance(va, vb):
    ds = [max(abs(x-y) for p, q in zip(ca, cb) for x, y in zip(p, q)) for ca, ga in va for cb, gb in vb if ga == gb]
    return min(ds) if ds else None


def main():
    ev = json.loads((ROOT/'structures/box-stacks-v2/eval/manifest.json').read_text())['structures']
    train = {e['id']: e for e in json.loads((ROOT/'structures/box-stacks-v2/train/manifest.json').read_text())['structures']}
    c2 = json.loads((ROOT/'structures/curriculum-v2/pools.json').read_text())
    c1 = json.loads((ROOT/'structures/curriculum-v1/pools.json').read_text())
    sets = {'c2-train': [e['id'] for q in c2['stages'][0]['pools'] for e in q['structures']],
            'c0c1-train': sorted({e['id'] for s in c1['stages'] for e in s['structures']})}
    EV = [(e['id'], e['difficulty'], variants(e['geometryKey'])) for e in ev]
    out = {}
    for name, ids in sets.items():
        mins, linked, unmatched = [], [], 0
        for i in ids:
            ds = [d for d in (distance(variants(train[i]['geometryKey']), v) for _, _, v in EV) if d is not None]
            if not ds:unmatched += 1;continue
            mins.append(min(ds))
            if min(ds) <= 1:linked.append(i)
        out[name] = dict(structures=len(ids), linkedToEval=linked, noSharedSupportGraph=unmatched, minDistance=min(mins), evalIdsInPool=sorted(set(ids) & {e['id'] for e in ev}),
                         histogram={str(k): v for k, v in sorted(collections.Counter(min(m, 10) for m in mins).items())})
    pairs = [(a[0], b[0], a[1], b[1], d) for k, a in enumerate(EV) for b in EV[k+1:] for d in [distance(a[2], b[2])] if d is not None and d <= 1]
    out['evalNearDuplicatePairs'] = dict(count=len(pairs), byTiers={f'{a}-{b}': n for (a, b), n in collections.Counter((p[2], p[3]) for p in pairs).items()},
                                         withinTier=sum(p[2] == p[3] for p in pairs), identical=sum(p[4] == 0 for p in pairs), pairs=pairs)
    print(json.dumps(out, indent=1))


if __name__ == '__main__':main()
