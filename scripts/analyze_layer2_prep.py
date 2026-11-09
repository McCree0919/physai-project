"""Archive measured prep data and render report figures; run in the mlagents env."""
import csv, json, shutil, statistics
from pathlib import Path
from collections import Counter
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
ROOT=Path(__file__).resolve().parents[1]
data=ROOT/'docs/data/layer2'; data.mkdir(parents=True,exist_ok=True)
images=ROOT/'docs/images'; images.mkdir(parents=True,exist_ok=True)
source=ROOT/'results/layer2'
shutil.copy2(source/'scan/trials.csv',data/'scan_trials.csv')
shutil.copy2(source/'scan/metadata.json',data/'scan_metadata.json')
rows=list(csv.DictReader((data/'scan_trials.csv').open()))
hs=sorted({float(r['height_gap']) for r in rows}); ss=sorted({float(r['lateral_gap']) for r in rows})
grid=np.array([[sum(int(r['success']) for r in rows if float(r['height_gap'])==h and float(r['lateral_gap'])==s)/8 for s in ss]for h in hs])
fig,ax=plt.subplots(figsize=(12,6)); im=ax.imshow(grid,origin='lower',vmin=0,vmax=1,cmap='RdYlGn',aspect='auto')
ax.set_xticks(range(len(ss)),[f'{x:.2f}'for x in ss]);ax.set_yticks(range(len(hs)),[f'{x:.2f}'for x in hs]);ax.set_xlabel('Lateral center spacing (m)');ax.set_ylabel('Rung height gap (m)');ax.set_title('Scripted climb success: 8 arena variants per cell (1040 trials)')
for y in range(len(hs)):
 for x in range(len(ss)):ax.text(x,y,f'{grid[y,x]*100:.0f}%',ha='center',va='center',fontsize=8,color='black')
fig.colorbar(im,ax=ax,label='Success fraction');fig.tight_layout();fig.savefig(images/'layer2_solvability.png',dpi=160);plt.close(fig)
counts=Counter(r['reason']for r in rows if r['success']=='0')
fig,ax=plt.subplots(figsize=(7,4));names=['unreachable','fall','grab_rejected','timeout'];ax.bar(names,[counts[n]for n in names]);ax.set_ylabel('Failed trials');ax.set_title('Failure labels are controller diagnostics, not physical proofs');fig.tight_layout();fig.savefig(images/'layer2_failures.png',dpi=160);plt.close(fig)
with (data/'scan_cells.csv').open('w',newline='')as f:
 w=csv.writer(f);w.writerow(['height_gap','lateral_gap','successes','trials'])
 for yi,h in enumerate(hs):
  for xi,s in enumerate(ss):w.writerow([h,s,int(grid[yi,xi]*8),8])
bench=[]
for p in sorted((source/'throughput').glob('*/metadata.json')):
 r=json.loads(p.read_text());r['trial']=p.parent.name;bench.append(r)
(data/'throughput_trials.json').write_text(json.dumps(bench,indent=2))
summary=[]
for n in [16,32,64]:
 group=[r for r in bench if r['arenas']==n];dec=[r['aggregateDecisionStepsPerSecond']for r in group];phy=[r['aggregatePhysicsStepsPerSecond']for r in group];glob=[r['globalPhysicsTicksPerSecond']for r in group]
 med=statistics.median(dec)
 summary.append(dict(arenas=n,physics=statistics.median(phy),ticks=statistics.median(glob),decisions=med,min=min(dec),max=max(dec),cv=statistics.stdev(dec)/statistics.mean(dec)*100,minutes5M=5e6/med/60,minutes20M=2e7/med/60,episodes=sum(r['episodes']for r in group),successes=sum(r['successes']for r in group)))
fig,ax=plt.subplots(figsize=(7,4));x=range(3);y=[r['decisions']for r in summary];err=np.array([[r['decisions']-r['min']for r in summary],[r['max']-r['decisions']for r in summary]])
ax.bar(x,y,yerr=err,capsize=5);ax.set_xticks(list(x),['16','32','64']);ax.set_xlabel('Arenas per standalone process');ax.set_ylabel('Aggregate decisions / wall second');ax.set_title('Headless script-only throughput; median and min/max, 3 runs');fig.tight_layout();fig.savefig(images/'layer2_throughput.png',dpi=160);plt.close(fig)
success=[r for r in rows if r['success']=='1']
report=dict(successes=len(success),trials=len(rows),failures=counts,successTimeMedian=statistics.median(float(r['sim_seconds'])for r in success),successTimeRange=[min(float(r['sim_seconds'])for r in success),max(float(r['sim_seconds'])for r in success)],maxConstraint=max(float(r['peak_constraint_N'])for r in rows),maxActive=max(float(r['peak_active_N'])for r in rows),failureStages=Counter(r['detail'].split(';')[0]for r in rows if r['success']=='0'),throughput=summary)
(data/'summary.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
