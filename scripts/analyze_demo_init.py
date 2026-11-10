"""Summarize and archive the demonstration-initialisation runs: deterministic ground-spawn evaluations (same metrics as
analyze_target_representation, with the incremental s2026/s2027 runs of that experiment as baseline), the training-time
RewardProgress / Level timeline from TensorBoard, and training episodes split by spawn source from the runtime CSVs."""
import argparse
import collections
import csv
import glob
import json
from pathlib import Path
import shutil
from analyze_target_representation import KEYS
from climb_eval_metrics import load, summarize

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'results/climb-demo-init'
BASELINE = ROOT/'docs/data/climb-target-representation/per_seed.csv'
OUT = ROOT/'docs/data/climb-demo-init'
SEEDS = [2026, 2027]
LABELS = ['1M', '2.5M', '5M']
PREFIX = 'climb_demo_init'
TAGS = ['Climb/RewardProgress', 'Climb/RewardHeight', 'Climb/Level1Rate', 'Climb/Level2Rate', 'Climb/Success', 'Climb/MaximumTorsoHeight', 'Environment/Cumulative Reward',
        'Climb/Spawn/DemoFraction', 'Climb/Spawn/ActualDemoFraction', 'Climb/Spawn/Rejected', 'Climb/Spawn/Missing', 'Climb/Spawn/ObservationMismatch']


def timeline(run_id):
    from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    accumulator = EventAccumulator(glob.glob(str(ROOT/'results'/run_id/'Climb/events.out*'))[-1], size_guidance={'scalars': 0});accumulator.Reload()
    tags = accumulator.Tags()['scalars']
    series = {t: [(e.step, e.value) for e in accumulator.Scalars(t)] for t in tags if t in TAGS or t.startswith('Climb/Spawn/')}
    progress = series.get('Climb/RewardProgress', [])
    first = next(((s, v) for s, v in progress if v > 0), None)
    def window(tag, low, high):
        values = [v for s, v in series.get(tag, []) if low <= s < high]
        return sum(values)/len(values) if values else None
    windows = {f'{low/1e6:g}-{high/1e6:g}M': {t.split('/', 1)[1]: window(t, low, high) for t in series}
               for low, high in [(0, 1_000_000), (1_000_000, 2_500_000), (2_500_000, 4_000_000), (4_000_000, 5_100_000)]}
    return dict(firstNonzeroRewardProgress=dict(step=first[0], value=first[1]) if first else None,
                zeroAfter1M=all(v <= 0 for s, v in progress if s <= 1_000_000) and bool(progress), windows=windows), series


def training_episodes(run_id):
    rows = [r for f in glob.glob(str(SOURCE/run_id/'runtime/*.csv')) for r in csv.DictReader(open(f, encoding='utf-8'))]
    groups = collections.defaultdict(list)
    for r in rows:groups['ground' if r['spawn'] == 'ground' else r['spawn'].rsplit(':', 1)[-1]].append(r)
    def stats(g):
        n = len(g);return dict(episodes=n, level1=sum(r['level1'] == '1' for r in g)/n, level2=sum(r['level2'] == '1' for r in g)/n, success=sum(r['success'] == '1' for r in g)/n,
                               reasons=dict(collections.Counter(r['reason'] for r in g)))
    # Ground spawns in time order (unix_ms) split into fifths: does the full task improve during training?
    ground = sorted(groups.get('ground', []), key=lambda r: int(r['unix_ms']))
    fifths = [stats(ground[i*len(ground)//5:(i+1)*len(ground)//5]) for i in range(5)] if len(ground) >= 5 else []
    return dict(total=len(rows), demoShare=1-len(groups.get('ground', []))/len(rows) if rows else None, bySpawn={k: stats(v) for k, v in sorted(groups.items())}, groundByFifth=fifths)


SOURCES = ['ground', 'S1a', 'S1b', 'S2', 'S3']
WINDOWS = [(0, 1_000_000), (1_000_000, 2_500_000), (2_500_000, 4_000_000), (4_000_000, 5_100_000)]


def snapshot_starts():
    """Snapshot id -> hands gripping and level flags at the start (from the library itself)."""
    starts = {}
    for line in open(SOURCE.parent/'climb-snapshots-v2/library.jsonl', encoding='utf-8'):
        s = json.loads(line);starts[s['id']] = dict(held=sum(bool(h['held']) for h in s['body']['hands']), level1=bool(s['task']['level1']), level2=bool(s['task']['level2']))
    return starts


def source_of(spawn):
    return 'ground' if spawn == 'ground' else spawn.rsplit(':', 1)[-1]


def by_source(episodes, starts):
    """Rates for one evaluation; new grips exclude grips restored at the start (the observer counts them once)."""
    n = len(episodes)
    def start(e):
        spawn = e['terminal']['spawn'];return starts[spawn.split(':')[1]] if spawn != 'ground' else dict(held=0, level1=False, level2=False)
    return dict(episodes=n, newGrip=sum(e['physics']['gripSuccesses']-start(e)['held'] > 0 for e in episodes)/n,
                heldGrip=sum(e['physics']['gripHandTicks'] > 0 for e in episodes)/n,
                level1=sum(e['terminal']['level1'] == '1' for e in episodes)/n, level2=sum(e['terminal']['level2'] == '1' for e in episodes)/n,
                success=sum(e['terminal']['success'] == '1' for e in episodes)/n,
                level1AtStart=sum(start(e)['level1'] for e in episodes)/n, level2AtStart=sum(start(e)['level2'] for e in episodes)/n,
                maximumTorsoYMean=sum(e['physics']['maximumTorsoY'] for e in episodes)/n, reasons=dict(collections.Counter(e['terminal']['reason'] for e in episodes)))


def evaluation_by_source(run_id, starts):
    result = {}
    for label in LABELS:
        result[label] = {}
        for source in SOURCES:
            folder = SOURCE/'eval'/run_id/label if source == 'ground' else SOURCE/'eval_stage'/run_id/label/source
            if not (folder/'checkpoint/report.json').exists():continue
            evaluation, _ = load(folder/'checkpoint')
            episodes = evaluation['episodes'];mismatched = sum(source_of(e['terminal']['spawn']) != source for e in episodes)
            result[label][source] = dict(by_source([e for e in episodes if source_of(e['terminal']['spawn']) == source], starts), mismatchedSpawn=mismatched)
    return result


def baselines_by_source(starts):
    """Policy-free baselines per snapshot stage (run_demo_init_baselines.py), same metrics as the trained policy."""
    result = {}
    for stage in SOURCES[1:]:
        for policy in ['hold', 'random']:
            folder = SOURCE/'baseline_stage'/stage/policy
            if not (folder/'report.json').exists():continue
            evaluation, _ = load(folder)
            episodes = evaluation['episodes']
            result.setdefault(policy, {})[stage] = dict(by_source([e for e in episodes if source_of(e['terminal']['spawn']) == stage], starts),
                                                        mismatchedSpawn=sum(source_of(e['terminal']['spawn']) != stage for e in episodes))
    return result


def training_by_source(run_id, series, starts):
    """Training episodes by start source within step windows; unix_ms is mapped to trainer steps via TensorBoard wall times.
    Grip is not logged per training episode, so training trends cover levels and success only."""
    from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    accumulator = EventAccumulator(glob.glob(str(ROOT/'results'/run_id/'Climb/events.out*'))[-1], size_guidance={'scalars': 0});accumulator.Reload()
    clock = [(e.wall_time*1000, e.step) for e in accumulator.Scalars('Environment/Cumulative Reward')]
    def step(ms):
        for (t0, s0), (t1, s1) in zip(clock, clock[1:]):
            if ms <= t1:return s0+(s1-s0)*max(0, ms-t0)/(t1-t0) if t1 > t0 else s1
        return clock[-1][1]
    rows = [r for f in glob.glob(str(SOURCE/run_id/'runtime/*.csv')) for r in csv.DictReader(open(f, encoding='utf-8'))]
    table = {}
    for low, high in WINDOWS:
        key = f'{low/1e6:g}-{min(high, 5_000_000)/1e6:g}M';table[key] = {}
        window = [r for r in rows if low <= step(int(r['unix_ms'])) < high]
        for source in SOURCES:
            g = [r for r in window if source_of(r['spawn']) == source]
            if not g:continue
            table[key][source] = dict(episodes=len(g), level1=sum(r['level1'] == '1' for r in g)/len(g), level2=sum(r['level2'] == '1' for r in g)/len(g),
                                      success=sum(r['success'] == '1' for r in g)/len(g),
                                      level1AtStart=sum(starts[r['spawn'].split(':')[1]]['level1'] for r in g)/len(g) if source != 'ground' else 0.0)
    return table


def main():
    global SOURCE, OUT, PREFIX
    p = argparse.ArgumentParser();p.add_argument('--source', default=str(SOURCE));p.add_argument('--out', default=str(OUT));p.add_argument('--run-prefix', default=PREFIX)
    args = p.parse_args();SOURCE, OUT, PREFIX = Path(args.source), Path(args.out), args.run_prefix
    rows, report = [], {}
    starts = snapshot_starts()
    for seed in SEEDS:
        run_id = f'{PREFIX}_s{seed}'
        summary, series = timeline(run_id)
        summary['trainingEpisodes'] = training_episodes(run_id)
        summary['trainingBySource'] = training_by_source(run_id, series, starts)
        summary['evaluationBySource'] = evaluation_by_source(run_id, starts)
        report[run_id] = summary
        (OUT/'runs'/run_id).mkdir(parents=True, exist_ok=True)
        (OUT/'runs'/run_id/'tensorboard_scalars.json').write_text(json.dumps(series)+'\n', encoding='utf-8')
        for name in ['timing.jsonl', 'device.jsonl', 'command.json']:
            if (SOURCE/run_id/name).exists():shutil.copy2(SOURCE/run_id/name, OUT/'runs'/run_id/name)
        status = ROOT/'results'/run_id/'run_logs/training_status.json'
        if status.exists():shutil.copy2(status, OUT/'runs'/run_id/'training_status.json')
        for label in LABELS:
            folder = SOURCE/'eval'/run_id/label
            if not (folder/'checkpoint/report.json').exists():continue
            evaluation, runtime = load(folder/'checkpoint')
            info = json.loads((folder/'checkpoint.json').read_text())
            metrics = summarize(evaluation['episodes'])
            ups = [e['physics']['gripTorsoUpForceSum']/e['physics']['gripHandTicks'] for e in evaluation['episodes'] if e['physics']['gripHandTicks']]
            metrics['grip']['torsoUpForceEpisodes'] = dict(n=len(ups), p50=sorted(ups)[len(ups)//2], positiveFraction=sum(u > 0 for u in ups)/len(ups)) if ups else None
            rows.append(dict(mode=PREFIX.removeprefix('climb_'), seed=seed, label=label, steps=info['steps'], modelSha256=info['sha256'], **{k: f(metrics) for k, f in KEYS.items()}))
            destination = OUT/'eval'/run_id/label;destination.mkdir(parents=True, exist_ok=True)
            (destination/'metrics.json').write_text(json.dumps(dict(checkpoint=info, metrics=metrics), indent=2)+'\n', encoding='utf-8')
            shutil.copy2(runtime, destination/'runtime.csv')
    baseline = [dict(r, mode='incremental_baseline') for r in csv.DictReader(open(BASELINE, encoding='utf-8')) if r['mode'] == 'incremental' and int(r['seed']) in SEEDS]
    OUT.mkdir(parents=True, exist_ok=True)
    with (OUT/'per_seed.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]) if rows else list(baseline[0]));writer.writeheader();writer.writerows(rows+baseline)
    baselines = baselines_by_source(starts)
    (OUT/'summary.json').write_text(json.dumps(dict(runs=report, evaluations=rows, baseline=baseline, stageBaselines=baselines), indent=2)+'\n', encoding='utf-8')
    for name in ['runner.log', 'stage_eval.log', 'baselines.log']:
        if (SOURCE/name).exists():shutil.copy2(SOURCE/name, OUT/name)
    for run_id, v in [('stage baselines', None)]+list(report.items()):
        print(run_id)
        for title, table in ([(policy, {'-': stages}) for policy, stages in baselines.items()] if v is None else [('training', v['trainingBySource']), ('evaluation', v['evaluationBySource'])]):
            for window, sources in table.items():
                for source, s in sources.items():
                    print(f"  {title:10} {window:8} {source:6} n={s['episodes']:5} " + ' '.join(f"{k}={s[k]:.3f}" for k in ['newGrip', 'heldGrip', 'level1', 'level2', 'success', 'level1AtStart'] if k in s))
    print(json.dumps(dict(runs={k: {x: v[x] for x in ['firstNonzeroRewardProgress', 'zeroAfter1M']} | {'demoShare': v['trainingEpisodes']['demoShare'], 'bySpawn': v['trainingEpisodes']['bySpawn'], 'groundByFifth': v['trainingEpisodes']['groundByFifth']} for k, v in report.items()},
                     evaluations=[{k: r[k] for k in ['seed', 'label', 'level1Rate', 'level2Rate', 'successRate', 'gripEpisodeFraction', 'legalContactRate', 'maximumTorsoYMean', 'gripTorsoUpForceMeanN']} for r in rows]), indent=1))


if __name__ == '__main__':main()
