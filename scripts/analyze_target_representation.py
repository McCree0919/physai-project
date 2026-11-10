"""Summarize and archive the incremental-vs-absolute evaluations (per seed, then mean and between-seed spread)."""
import csv
import json
from pathlib import Path
import shutil
import statistics
from climb_eval_metrics import load, quantile, summarize

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT/'results/climb-target-representation'
OUT = ROOT/'docs/data/climb-target-representation'
MODES = ['incremental', 'absolute']
SEEDS = [2026, 2027, 2028]
LABELS = ['1M', '2.5M', '5M']
KEYS = {
    'gripEpisodeFraction': lambda m: m['gripEpisodeFraction'],
    'legalContactRate': lambda m: m['legalContactRate'],
    'legalContactEpisodeFraction': lambda m: m['legalContactEpisodeFraction'],
    'level1Rate': lambda m: m['level1Rate'],
    'level2Rate': lambda m: m['level2Rate'],
    'successRate': lambda m: m['successRate'],
    'maximumTorsoYMean': lambda m: m['maximumTorsoY']['mean'],
    'maximumTorsoYP95': lambda m: m['maximumTorsoY']['p95'],
    'commandOuterFraction': lambda m: m['commandOuterFraction'],
    'gripCommandRadiusMean': lambda m: m['grip']['commandRadiusMean'],
    'gripCommandOuterFraction': lambda m: m['grip']['commandOuterFraction'],
    'gripTorsoUpForceMeanN': lambda m: m['grip']['torsoUpForceMeanN'],
    'gripTorsoUpForceEpisodeP50N': lambda m: (m['grip']['torsoUpForceEpisodes'] or {}).get('p50'),
    'gripTorsoUpForcePositiveEpisodeFraction': lambda m: (m['grip']['torsoUpForceEpisodes'] or {}).get('positiveFraction'),
    'gripHandBelowShoulderFraction': lambda m: m['grip']['handBelowShoulderFraction'],
    'grippedSecondsMean': lambda m: m['grippedSecondsMean'],
}


def main():
    rows, aggregate = [], {}
    for mode in MODES:
        for seed in SEEDS:
            run_id = f'climb_target_{mode}_s{seed}'
            for label in LABELS:
                folder = SOURCE/'eval'/run_id/label
                if not (folder/'checkpoint/report.json').exists():continue
                report, runtime = load(folder/'checkpoint')
                info = json.loads((folder/'checkpoint.json').read_text())
                metrics = summarize(report['episodes'])
                # Grip-period vertical force on the torso (-F_y; positive lifts): per-episode tick-weighted means, gripping episodes only.
                ups = [e['physics']['gripTorsoUpForceSum']/e['physics']['gripHandTicks'] for e in report['episodes'] if e['physics']['gripHandTicks']]
                metrics['grip']['torsoUpForceEpisodes'] = dict(n=len(ups), p05=quantile(ups, .05), p50=quantile(ups, .5), p95=quantile(ups, .95), positiveFraction=sum(u > 0 for u in ups)/len(ups)) if ups else None
                row = dict(mode=mode, seed=seed, label=label, steps=info['steps'], modelSha256=info['sha256'], **{k: f(metrics) for k, f in KEYS.items()})
                rows.append(row)
                destination = OUT/'eval'/run_id/label;destination.mkdir(parents=True, exist_ok=True)
                (destination/'metrics.json').write_text(json.dumps(dict(checkpoint=info, metrics=metrics), indent=2)+'\n', encoding='utf-8')
                shutil.copy2(runtime, destination/'runtime.csv')
                flat = []
                for e in report['episodes']:
                    p = e['physics']
                    flat.append(dict(arena=p['arena'], episode=p['episode'], reason=e['terminal']['reason'], level1=e['terminal']['level1'], level2=e['terminal']['level2'], success=e['terminal']['success'],
                                     gripSuccesses=p['gripSuccesses'], legalContactTicks=p['legalContactTicks'], freeHandTicks=p['freeHandTicks'], maximumTorsoY=p['maximumTorsoY'],
                                     grippedSeconds=p['grippedSeconds'], commandOuterFraction=e['commandTargets']['outerFraction'], gripHandTicks=p['gripHandTicks'],
                                     gripCommandRadiusMean=p['gripCommandRadiusSum']/p['gripHandTicks'] if p['gripHandTicks'] else '', gripTorsoUpForceMeanN=p['gripTorsoUpForceSum']/p['gripHandTicks'] if p['gripHandTicks'] else ''))
                with (destination/'episodes.csv').open('w', newline='', encoding='utf-8') as stream:
                    writer = csv.DictWriter(stream, fieldnames=list(flat[0]));writer.writeheader();writer.writerows(flat)
            timing = SOURCE/run_id/'timing.jsonl'
            for name in ['timing.jsonl', 'device.jsonl', 'command.json']:
                if (SOURCE/run_id/name).exists():(OUT/'runs'/run_id).mkdir(parents=True, exist_ok=True);shutil.copy2(SOURCE/run_id/name, OUT/'runs'/run_id/name)
            status = ROOT/'results'/run_id/'run_logs/training_status.json'
            if status.exists():shutil.copy2(status, OUT/'runs'/run_id/'training_status.json')
    for mode in MODES:
        for label in LABELS:
            group = [r for r in rows if r['mode'] == mode and r['label'] == label]
            if not group:continue
            entry = {'seeds': len(group)}
            for key in KEYS:
                values = [r[key] for r in group if r[key] is not None]
                entry[key] = dict(mean=statistics.mean(values), sd=statistics.stdev(values) if len(values) > 1 else None, min=min(values), max=max(values), n=len(values)) if values else None
            aggregate[f'{mode}/{label}'] = entry
    OUT.mkdir(parents=True, exist_ok=True)
    if rows:
        with (OUT/'per_seed.csv').open('w', newline='', encoding='utf-8') as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]));writer.writeheader();writer.writerows(rows)
    (OUT/'summary.json').write_text(json.dumps(dict(perSeed=rows, aggregate=aggregate), indent=2)+'\n', encoding='utf-8')
    if (SOURCE/'runner.log').exists():shutil.copy2(SOURCE/'runner.log', OUT/'runner.log')
    print(json.dumps(aggregate, indent=1))


if __name__ == '__main__':main()
