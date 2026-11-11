"""Zero-shot evaluation summary (LAYER3_ZERO_SHOT.md). Unit of analysis is the structure: per-structure rates over its 20 episodes,
then per-difficulty means with percentile bootstrap CIs resampling structures, and (more conservative) resampling the near-duplicate
clusters of LAYER3_DESIGN.md §11 (same support graph, every box bound within 0.05 m, single linkage). Also reports each arena's first
episode separately: later episodes can start with a hand pulled back to the previous grip point by the ResetBody leftover joint
(LAYER3_RESETBODY_DIAGNOSTIC.md)."""
import collections
import csv
import json
from pathlib import Path
import random
import statistics
from climb_eval_metrics import load

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'results/climb-zero-shot'
OUT = ROOT/'docs/data/climb-zero-shot'
MANIFEST = ROOT/'structures/zero-shot-v1/manifest.json'
MODELS = ['s2026_5M', 's2027_5M']
FEATURES = ['levels', 'constructionLevels', 'maxRise', 'maxVerticalFace', 'totalHeight', 'overhangCount', 'maxOverhang', 'maxGap', 'exposedTopEdges', 'coarseMinimumTransitions']
EARLY_GRIP = .12  # s; a clean ground spawn never grips this early (first episodes: 0/32 in the 5M ground evals)
BOOT = 10000


def boxes(key):
    return [tuple(int(v) for v in b.split(',')) for b in key.split(';')]


def graph(bs):
    """Canonical order and support edges (top face within one 0.05 m grid unit of the bottom face, overlapping footprints)."""
    order = sorted(range(len(bs)), key=lambda i: (bs[i][1], bs[i][2], bs[i][0], bs[i][4], bs[i][5], bs[i][3]))
    c = [bs[i] for i in order]
    edges = frozenset((i, j) for i in range(len(c)) for j in range(len(c)) if i != j and abs(c[i][4]-c[j][1]) <= 1
                      and c[i][0] < c[j][3] and c[j][0] < c[i][3] and c[i][2] < c[j][5] and c[j][2] < c[i][5])
    ground = tuple(b[1] <= 1 for b in c)
    return c, (len(c), edges, ground)


def clusters(entries):
    parent = {e['id']: e['id'] for e in entries}
    def find(x):
        while parent[x] != x:parent[x] = parent[parent[x]];x = parent[x]
        return x
    canon = {e['id']: graph(boxes(e['geometryKey'])) for e in entries}
    for a in entries:
        for b in entries:
            if a['id'] < b['id'] and a['difficulty'] == b['difficulty']:
                (ca, ga), (cb, gb) = canon[a['id']], canon[b['id']]
                if ga == gb and all(abs(x-y) <= 1 for p, q in zip(ca, cb) for x, y in zip(p, q)):parent[find(a['id'])] = find(b['id'])
    return {e['id']: find(e['id']) for e in entries}


def bootstrap(values, groups=None, seed=2026):
    """Mean and 95% percentile CI; resamples whole groups (clusters) when given."""
    rng = random.Random(seed)
    units = collections.defaultdict(list)
    for i, v in enumerate(values):units[groups[i] if groups else i].append(v)
    units = list(units.values())
    means = []
    for _ in range(BOOT):
        sample = [v for _ in units for v in rng.choice(units)]
        means.append(sum(sample)/len(sample))
    means.sort()
    return dict(mean=sum(values)/len(values), lo=means[int(.025*BOOT)], hi=means[int(.975*BOOT)-1], units=len(units))


def wilson(k, n, z=1.96):
    if n == 0:return None
    p = k/n;d = 1+z*z/n;c = p+z*z/(2*n);h = z*((p*(1-p)+z*z/(4*n))/n)**.5
    return dict(k=k, n=n, rate=p, lo=(c-h)/d, hi=(c+h)/d)


def spearman(x, y):
    def rank(v):
        order = sorted(range(len(v)), key=lambda i: v[i]);r = [0.0]*len(v);i = 0
        while i < len(v):
            j = i
            while j+1 < len(v) and v[order[j+1]] == v[order[i]]:j += 1
            for k in range(i, j+1):r[order[k]] = (i+j)/2
            i = j+1
        return r
    rx, ry = rank(x), rank(y)
    if len(set(rx)) < 2 or len(set(ry)) < 2:return None
    return statistics.correlation(rx, ry)


def main():
    manifest = json.loads(MANIFEST.read_text())
    entries = {e['id']: e for e in manifest['structures']}
    cluster = clusters(manifest['structures'])
    rows, episodes = [], []
    for model in MODELS:
        report, _ = load(SOURCE/model)
        by_arena = collections.defaultdict(list)
        for e in report['episodes']:by_arena[e['physics']['arena']].append(e)
        for arena, eps in sorted(by_arena.items()):
            sid = arena.split('_', 1)[1];eps.sort(key=lambda e: e['physics']['episode'])
            meta = entries.get(sid)
            n = len(eps);first = eps[0]['terminal']
            early = [e for e in eps[1:] if 0 <= e['physics']['firstGripSeconds'] <= EARLY_GRIP]
            row = dict(model=model, structure=sid, difficulty=meta['difficulty'] if meta else 'Control', seed=meta['seed'] if meta else '', cluster=cluster.get(sid, sid), episodes=n,
                       level1=sum(e['terminal']['level1'] == '1' for e in eps)/n, level2=sum(e['terminal']['level2'] == '1' for e in eps)/n,
                       success=sum(e['terminal']['success'] == '1' for e in eps)/n, firstSuccess=int(first['success'] == '1'), firstLevel1=int(first['level1'] == '1'),
                       firstLevel2=int(first['level2'] == '1'), earlyGripEpisodes=len(early),
                       maximumTorsoYMean=statistics.mean(e['physics']['maximumTorsoY'] for e in eps),
                       reasons=';'.join(f'{k}:{v}' for k, v in sorted(collections.Counter(e['terminal']['reason'] for e in eps).items())))
            for f in FEATURES:row[f] = meta['features'][f] if meta else None
            rows.append(row)
            for e in eps:
                episodes.append(dict(model=model, structure=sid, episode=e['physics']['episode'], level1=e['terminal']['level1'], level2=e['terminal']['level2'],
                                     success=e['terminal']['success'], reason=e['terminal']['reason'], maximumTorsoY=round(e['physics']['maximumTorsoY'], 4),
                                     firstGripSeconds=round(e['physics']['firstGripSeconds'], 3), decisions=e['terminal'].get('decisions', '')))
    summary = dict(manifestSha256=__import__('hashlib').sha256(MANIFEST.read_bytes()).hexdigest(), reports={m: json.loads((SOURCE/m/'report.json').read_text())['summary'] for m in MODELS},
                   clusters={d: len({cluster[e['id']] for e in manifest['structures'] if e['difficulty'] == d}) for d in ['Easy', 'Standard', 'Hard']}, byDifficulty={}, features={})
    for model in MODELS:
        for difficulty in ['Control', 'Easy', 'Standard', 'Hard']:
            g = [r for r in rows if r['model'] == model and r['difficulty'] == difficulty]
            if not g:continue
            key = f'{model}/{difficulty}';groups = [r['cluster'] for r in g]
            summary['byDifficulty'][key] = dict(structures=len(g), episodes=sum(r['episodes'] for r in g),
                **{m: dict(byStructure=bootstrap([r[m] for r in g]), byCluster=bootstrap([r[m] for r in g], groups)) for m in ['level1', 'level2', 'success']},
                firstEpisodeSuccess=wilson(sum(r['firstSuccess'] for r in g), len(g)),
                structuresAnySuccess=wilson(sum(r['success'] > 0 for r in g), len(g)), structuresMajoritySuccess=wilson(sum(r['success'] >= .5 for r in g), len(g)),
                earlyGripEpisodes=sum(r['earlyGripEpisodes'] for r in g))
        # Shape features: solved (success >= 0.5) versus not, within each difficulty and pooled over generated structures.
        for scope in ['Standard', 'Hard', 'Generated']:
            g = [r for r in rows if r['model'] == model and (r['difficulty'] == scope or (scope == 'Generated' and r['difficulty'] != 'Control'))]
            solved, failed = [r for r in g if r['success'] >= .5], [r for r in g if r['success'] < .5]
            summary['features'][f'{model}/{scope}'] = dict(solved=len(solved), failed=len(failed), **{f: dict(
                solvedMean=statistics.mean(r[f] for r in solved) if solved else None, failedMean=statistics.mean(r[f] for r in failed) if failed else None,
                spearmanWithSuccess=spearman([r[f] for r in g], [r['success'] for r in g])) for f in FEATURES})
    OUT.mkdir(parents=True, exist_ok=True)
    for name, data in [('per_structure.csv', rows), ('episodes.csv', episodes)]:
        with (OUT/name).open('w', newline='', encoding='utf-8') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(data[0]));writer.writeheader();writer.writerows(data)
    (OUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n', encoding='utf-8')
    for key, v in summary['byDifficulty'].items():
        print(f"{key:22} n={v['structures']:2} " + ' '.join(f"{m}={v[m]['byStructure']['mean']:.3f}[{v[m]['byStructure']['lo']:.3f},{v[m]['byStructure']['hi']:.3f}]|cl{v[m]['byCluster']['units']}[{v[m]['byCluster']['lo']:.3f},{v[m]['byCluster']['hi']:.3f}]" for m in ['level1', 'level2', 'success'])
              + f" first={v['firstEpisodeSuccess']['rate']:.3f} any={v['structuresAnySuccess']['k']} maj={v['structuresMajoritySuccess']['k']} early={v['earlyGripEpisodes']}")
    print('clusters', summary['clusters'])


if __name__ == '__main__':main()
