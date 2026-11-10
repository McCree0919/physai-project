"""Archive passive evaluation and resource benchmark evidence (standard library only)."""
import collections
import csv
import hashlib
import json
from pathlib import Path
import shutil
import statistics

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/data/climb-diagnostics'


def quantile(values, q):
    values = sorted(values)
    p = (len(values)-1)*q
    lo = int(p)
    return values[lo] + (values[min(lo+1,len(values)-1)]-values[lo])*(p-lo)


def stats(values):
    return dict(mean=statistics.mean(values), min=min(values), p50=quantile(values,.5), p95=quantile(values,.95), max=max(values)) if values else None


def behavior(mode):
    folder = ROOT/'results/climb-diagnostics'/mode
    report = json.loads((folder/'report.json').read_text())
    # One execution produces a process-specific CSV. Earlier aborted attempts are excluded.
    runtime = max((folder/'runtime').glob('episodes-*.csv'), key=lambda p:p.stat().st_mtime_ns)
    by_key = {}
    counters = collections.Counter()
    with runtime.open() as stream:
        for row in csv.DictReader(stream):
            arena = row['arena']; index = counters[arena]; counters[arena] += 1
            by_key[arena,index] = row
    for row in report['episodes']:
        p = row['physics']
        row['terminal'] = by_key[p['arena'],p['episode']]
    rows = report['episodes']
    s = report['summary']
    fields = ['requestRisingEdges','gripSuccesses','rejectedHandTicks','cooldownBlockedTicks','rearmBlockedTicks','maximumTorsoY','maximumGripTorsoY','grippedSeconds']
    s['perEpisode'] = {field:stats([r['physics'][field] for r in rows]) for field in fields}
    s['perEpisode']['requestOutputs'] = stats([r['requestOutputs'] for r in rows])
    s['firstGripSecondsAmongGrippingEpisodes'] = stats([r['physics']['firstGripSeconds'] for r in rows if r['physics']['firstGripSeconds']>=0])
    s['actualTargetOuterFraction'] = sum(r['physics']['actualOuterBoundary'] for r in rows)/sum(r['physics']['actualTargetSamples'] for r in rows)
    s['continuousOutputClippedFraction'] = sum(r['continuousClipped'] for r in rows)/sum(r['continuousCount'] for r in rows)
    s['terminalReasons'] = dict(collections.Counter(r['terminal']['reason'] for r in rows))
    s['level1Episodes'] = sum(int(r['terminal']['level1']) for r in rows)
    s['level2Episodes'] = sum(int(r['terminal']['level2']) for r in rows)
    s['successEpisodes'] = sum(int(r['terminal']['success']) for r in rows)
    s['gripButNoStableLevel1'] = sum(r['physics']['gripSuccesses']>0 and not int(r['terminal']['level1']) for r in rows)
    s['stableLevelThenFall'] = sum(int(r['terminal']['level1']) and r['terminal']['reason']=='Fall' for r in rows)
    s['rejectionTotal'] = sum(s['rejectionReasons'].values())
    s['rejectionFractions'] = {k:v/s['rejectionTotal'] for k,v in s['rejectionReasons'].items()}
    destination = OUT/mode;destination.mkdir(parents=True,exist_ok=True)
    (destination/'episodes.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    flat = []
    for row in rows:
        p = row['physics']; reasons = dict(zip(p['rejectionReasons'],p['rejectionCounts']))
        item = {key:p[key] for key in ['arena','agentId','episode','requestRisingEdges','gripSuccesses','rejectedHandTicks','cooldownBlockedTicks','rearmBlockedTicks','maximumTorsoY','firstGripSeconds','grippedSeconds']}
        item.update(requestOutputs=row['requestOutputs'],commandRadiusMean=row['commandTargets']['normMean'],commandOuterFraction=row['commandTargets']['outerFraction'],reason=row['terminal']['reason'],level1=row['terminal']['level1'],level2=row['terminal']['level2'])
        for reason in ['NoContact','NotTop','Occupied','TooFast','TopOccluded']:item['rejected_'+reason] = reasons.get(reason,0)
        flat.append(item)
    with (destination/'episodes.csv').open('w',newline='',encoding='utf-8') as stream:
        writer = csv.DictWriter(stream,fieldnames=list(flat[0]));writer.writeheader();writer.writerows(flat)
    shutil.copy2(runtime,destination/'runtime.csv')
    return s


def benchmarks():
    folder = ROOT/'results/climb-benchmarks'
    manifest = json.loads((folder/'manifest.json').read_text())
    summaries = []
    for run in manifest['runs']:
        source = folder/run['name']
        records = [json.loads(line) for line in (source/'measurements.jsonl').read_text().splitlines()]
        start = next(r for r in records if r['kind']=='start')
        stop = next(r for r in records if r['kind']=='budget_stop')
        samples = [dict(seconds=0,steps=start['steps'])]+[r for r in records if r['kind']=='sample']+[stop]
        left = max((r for r in samples if r['seconds']<=30),key=lambda r:r['seconds'])
        right = min((r for r in samples if r['seconds']>=30),key=lambda r:r['seconds'])
        steps30 = left['steps']+(right['steps']-left['steps'])*(30-left['seconds'])/(right['seconds']-left['seconds'])
        updates = [r for r in records if r['kind']=='update' and r['seconds']>=30]
        summary = dict(run,parameterDevices=start['parameter_devices'],torchThreads=start['torch_threads'],warmupSeconds=30,decisionStepsPerSecond=(stop['steps']-steps30)/(stop['seconds']-30),updateSeconds=stats([r['update_seconds'] for r in updates]),updatesMeasured=len(updates),updateBuffer=stats([r['buffer'] for r in updates]),allWindowStepsPerSecond=(stop['steps']-start['steps'])/stop['seconds'])
        for field in ['cuda_allocated_MiB','cuda_reserved_MiB','cuda_peak_allocated_MiB','rss_MiB']:
            values = [r[field] for r in records if field in r]
            if values:summary[field] = max(values)
        summaries.append(summary)
        destination = OUT/'benchmarks'/run['name'];destination.mkdir(parents=True,exist_ok=True)
        for name in ['measurements.jsonl','command.json','console.txt']:
            shutil.copy2(source/name,destination/name)
    shutil.copy2(folder/'manifest.json',OUT/'benchmarks/manifest.json')
    return summaries


def plot(summary):
    # Only render once all timing runs have finished, avoiding work in measured windows.
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    figure,axes = plt.subplots(2,2,figsize=(11,7))
    for mode,color in [('checkpoint','#2667a6'),('random','#d78735')]:
        data = json.loads((OUT/mode/'episodes.json').read_text())
        counts = data['summary']['commandTargets']['normHistogram']
        axes[0,0].stairs([v/sum(counts) for v in counts],[v*.05 for v in range(19)],label=mode,color=color)
        axes[0,1].hist([r['physics']['maximumTorsoY'] for r in data['episodes']],bins=[.4+i*.025 for i in range(26)],alpha=.55,label=mode,color=color)
        categories = [sum(r['physics']['gripSuccesses']==0 for r in data['episodes']),sum(r['physics']['gripSuccesses']>0 and not int(r['terminal']['level1']) for r in data['episodes']),sum(int(r['terminal']['level1']) for r in data['episodes'])]
        offset = -.18 if mode=='checkpoint' else .18
        axes[1,0].bar([i+offset for i in range(3)],categories,width=.36,label=mode,color=color)
    axes[0,0].set(xlabel='Command target radius (m)',ylabel='Fraction of hand observations',title='Accumulated target saturation')
    axes[0,0].axvline(.84,color='gray',linestyle='--');axes[0,0].legend()
    axes[0,1].set(xlabel='Maximum torso local height (m)',ylabel='Episodes',title='200 completed episodes per policy');axes[0,1].legend()
    axes[1,0].set_xticks([0,1,2],['Never gripped','Gripped, no level 1','Stable level 1']);axes[1,0].set(ylabel='Episodes',title='Failure stage');axes[1,0].legend()
    for device,color in [('cpu','#2667a6'),('cuda:0','#d78735')]:
        rows = [r for r in summary['benchmarks'] if r['device']==device]
        offset = -.18 if device=='cpu' else .18
        axes[1,1].bar([i+offset for i in range(len(rows))],[r['decisionStepsPerSecond'] for r in rows],width=.36,label=device,color=color)
    axes[1,1].set_xticks([0,1,2],['2 / 32 agents','4 / 64 agents','8 / 128 agents']);axes[1,1].set(xlabel='Unity processes / total agents',ylabel='Trainer decision steps / s',title='300s each, first 30s excluded');axes[1,1].legend()
    figure.tight_layout();destination = ROOT/'docs/images/climb_diagnostics.png';destination.parent.mkdir(exist_ok=True);figure.savefig(destination,dpi=160);plt.close(figure)


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    summary = {'behavior':{mode:behavior(mode) for mode in ['checkpoint','random']}}
    if (ROOT/'results/climb-benchmarks/manifest.json').exists():summary['benchmarks'] = benchmarks()
    summary['protectedHashes'] = {}
    for pattern in ['Assets/Scripts/Climber/*.cs','Assets/Scripts/Learning/ClimberAgent.cs','config/climb_pilot.yaml','ProjectSettings/*.asset']:
        for path in ROOT.glob(pattern):summary['protectedHashes'][str(path.relative_to(ROOT))] = hashlib.sha256(path.read_bytes()).hexdigest()
    (OUT/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    if len(summary.get('benchmarks',[]))==6:plot(summary)
    print(json.dumps(summary,indent=2))


if __name__=='__main__':main()
