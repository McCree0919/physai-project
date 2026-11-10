"""Summarize a completed calibration run; no policy or environment changes."""
import csv
import datetime
import json
from pathlib import Path
import shutil
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

ROOT=Path(__file__).resolve().parents[1]
RUN='climb_pilot_s2026'

def main():
    out=ROOT/'docs/data/climb-pilot';out.mkdir(parents=True,exist_ok=True)
    images=ROOT/'docs/images';images.mkdir(exist_ok=True)
    source=ROOT/'results/climb-pilot-profile-s2026/measurements.jsonl'
    records=[json.loads(line)for line in source.read_text().splitlines()if line.strip()]
    start=next(r for r in records if r['kind']=='start');stop=next(r for r in records if r['kind']=='budget_stop');finish=next(r for r in records if r['kind']=='finish')
    assert finish['budget_stop'] and stop['seconds']>=660
    samples=[r for r in records if r['kind']=='sample'];updates=[r for r in records if r['kind']=='update'and r['seconds']>=60 and r['seconds']<=stop['seconds']]
    series=sorted([dict(seconds=0,steps=start['steps']),*samples,stop],key=lambda r:r['seconds'])
    seconds=np.array([r['seconds']for r in series]);steps=np.array([r['steps']for r in series])
    step60=float(np.interp(60,seconds,steps));duration=stop['seconds']-60;rate=(stop['steps']-step60)/duration
    update_times=np.array([r['update_seconds']for r in updates]);collection=np.array([r['collection_seconds']for r in updates])
    wall_start=datetime.datetime.fromisoformat(start['utc']).timestamp()
    episodes=[]
    for p in (ROOT/'results/climb-pilot-runtime-s2026').glob('episodes-*.csv'):
        episodes.extend(list(csv.DictReader(p.open())))
    assert episodes
    for r in episodes:r['seconds']=int(r['unix_ms'])/1000-wall_start
    def outcome(rows):
        return dict(episodes=len(rows),meanReward=float(np.mean([float(r['reward'])for r in rows])),level1Rate=float(np.mean([int(r['level1'])for r in rows])),level2Rate=float(np.mean([int(r['level2'])for r in rows])),successRate=float(np.mean([int(r['success'])for r in rows])))
    time_bins=[]
    for low in range(0,660,60):
        rows=[r for r in episodes if low<=r['seconds']<low+60]
        if rows:time_bins.append(dict(startSeconds=low,**outcome(rows)))
    from collections import Counter
    summary=dict(runId=RUN,seed=2026,arenas=16,structures=1,normalBottomStarts=True,memory=False,behavioralCloning=False,demonstrationStateInitialization=False,trainingSeconds=stop['seconds'],warmupSeconds=60,measurementSeconds=duration,totalTrainerSteps=stop['steps'],warmupTrainerStepInterpolated=step60,measuredDecisionsPerSecond=rate,measuredUpdates=len(updates),updateSecondsP50=float(np.median(update_times)),updateSecondsP95=float(np.percentile(update_times,95)),nonUpdateWallSecondsP50=float(np.median(collection)),updateWallTotal=float(np.sum(update_times)),cudaAllocatedPeakMiB=max(r.get('cuda_peak_allocated_MiB',0)for r in records),cudaReservedPeakMiB=max(r.get('cuda_reserved_MiB',0)for r in records),deviceTotalUsedFirstMiB=samples[0].get('device_total_used_MiB'),deviceTotalUsedPeakMiB=max(r.get('device_total_used_MiB',0)for r in samples),rssPeakMiB=max(r.get('rss_MiB',0)for r in samples),outcomes=outcome(episodes),endReasons=dict(Counter(r['reason']for r in episodes)),minuteOutcomes=time_bins,estimate5MMinutes=5e6/rate/60,estimate20MMinutes=2e7/rate/60)
    (out/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
    shutil.copy2(source,out/'measurements.jsonl');shutil.copy2(ROOT/'results/climb-preflight/report.json',out/'preflight.json');shutil.copy2(ROOT/'results/climb-tests.xml',out/'tests.xml')
    with(out/'episodes.csv').open('w',newline='',encoding='utf-8')as f:
        w=csv.DictWriter(f,fieldnames=list(episodes[0]));w.writeheader();w.writerows(episodes)
    accumulator=EventAccumulator(str(ROOT/'results'/RUN/'Climb'),size_guidance={'scalars':0});accumulator.Reload()
    scalars={tag:[dict(step=e.step,value=e.value,wall_time=e.wall_time)for e in accumulator.Scalars(tag)]for tag in accumulator.Tags()['scalars']}
    (out/'tensorboard_scalars.json').write_text(json.dumps(scalars,indent=2)+'\n',encoding='utf-8')
    fig,axs=plt.subplots(3,1,figsize=(10,10))
    reward_tag=next(tag for tag in scalars if tag.endswith('Cumulative Reward'))
    for ax,tag in [(axs[0],reward_tag)]:
        events=scalars[tag];ax.plot([(e['wall_time']-wall_start)/60 for e in events],[e['value']for e in events]);ax.set_ylabel('Mean episode reward');ax.set_ylim(-1.15,-1.05);ax.ticklabel_format(axis='y',useOffset=False);ax.grid(alpha=.2)
    for suffix,label in [('Climb/Level1Rate','Stable support >= 0.45 m'),('Climb/Level2Rate','Stable support >= 0.95 m')]:
        tag=next(t for t in scalars if t.endswith(suffix));events=scalars[tag]
        axs[1].plot([(e['wall_time']-wall_start)/60 for e in events],[e['value']for e in events],label=label)
    axs[1].set_ylabel('Completed episode fraction');axs[1].set_ylim(-.03,1.03);axs[1].legend();axs[1].grid(alpha=.2)
    widths=np.diff(seconds);rates=np.diff(steps)/np.maximum(widths,1e-9)
    axs[2].plot(seconds[1:]/60,rates);axs[2].set_ylabel('Trainer decisions / wall sec');axs[2].grid(alpha=.2)
    for ax in axs:ax.axvline(1,color='gray',linestyle='--',label='Warmup end');ax.set_xlabel('Wall minutes from trainer start')
    fig.suptitle('Two-step PPO calibration: seed 2026, 16 arenas, no memory or demonstrations');fig.tight_layout();fig.savefig(images/'climb_pilot_curves.png',dpi=150);plt.close(fig)
    print(json.dumps(summary,indent=2))

if __name__=='__main__':main()
