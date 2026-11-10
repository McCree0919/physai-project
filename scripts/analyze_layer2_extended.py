"""Archive the 0.60..1.00 m scan and plot it alongside the original scan."""
from pathlib import Path
import csv,json,shutil,statistics
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np
root=Path(__file__).resolve().parents[1]
source=root/'results/layer2/extended_scan';data=root/'docs/data/layer2';images=root/'docs/images'
shutil.copy2(source/'trials.csv',data/'extended_scan_trials.csv')
shutil.copy2(source/'metadata.json',data/'extended_scan_metadata.json')
new=list(csv.DictReader((source/'trials.csv').open()))
assert len(new)==936
cells={}
for row in new:cells.setdefault((float(row['height_gap']),float(row['lateral_gap'])),[]).append(row)
assert len(cells)==117 and all(len(rows)==8 for rows in cells.values())
old=list(csv.DictReader((data/'scan_trials.csv').open()))
# Keep the new run's overlap row (0.60 m); do not double-weight repeated cases.
rows=[r for r in old if float(r['height_gap'])<.60]+new
hs=sorted({float(r['height_gap'])for r in rows});ss=sorted({float(r['lateral_gap'])for r in rows})
grid=np.array([[sum(int(r['success'])for r in rows if float(r['height_gap'])==h and float(r['lateral_gap'])==s)/8 for s in ss]for h in hs])
fig,ax=plt.subplots(figsize=(11,9));im=ax.imshow(grid,origin='lower',vmin=0,vmax=1,cmap='RdYlGn',aspect='auto')
ax.set_xticks(range(len(ss)),[f'{s:.2f}'for s in ss]);ax.set_yticks(range(len(hs)),[f'{h:.2f}'for h in hs]);ax.set_xlabel('Lateral center spacing (m)');ax.set_ylabel('Rung height gap (m)');ax.set_title('Unchanged policy: extended scan to 1.00 m; 8 variants/cell')
for i in range(len(hs)):
 for j in range(len(ss)):ax.text(j,i,f'{grid[i,j]*100:.0f}%',ha='center',va='center',fontsize=7)
fig.colorbar(im,ax=ax,label='Success fraction');fig.tight_layout();fig.savefig(images/'layer2_solvability_extended.png',dpi=160)
summary=dict(trials=len(new),successes=sum(int(r['success'])for r in new),cells=len(cells),medianSeconds=statistics.median(float(r['sim_seconds'])for r in new),minSeconds=min(float(r['sim_seconds'])for r in new),maxSeconds=max(float(r['sim_seconds'])for r in new),peakConstraint=max(float(r['peak_constraint_N'])for r in new),peakActive=max(float(r['peak_active_N'])for r in new),perHeight=[])
for h in sorted({key[0]for key in cells}):
 group=[r for r in new if float(r['height_gap'])==h];summary['perHeight'].append(dict(height=h,successes=sum(int(r['success'])for r in group),trials=len(group),medianSeconds=statistics.median(float(r['sim_seconds'])for r in group)))
(data/'extended_scan_summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
print(json.dumps(summary,indent=2))
