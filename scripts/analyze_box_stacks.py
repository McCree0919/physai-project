"""Archive box-stack candidates, settlement metrics and feature distributions."""
from pathlib import Path
import json,collections,csv,shutil,statistics
root=Path(__file__).resolve().parents[1];data=root/'docs/data/box-stacks';data.mkdir(parents=True,exist_ok=True)
trials=json.loads((root/'results/box-stacks/trials.json').read_text())['trials'];assert len(trials)==60
shutil.copy2(root/'results/box-stacks/trials.json',data/'trials.json')
features=['constructionLevels','levels','coarseMinimumTransitions','maxRise','maxVerticalFace','maxUntoppedRise','overhangCount','maxOverhang','maxGap','exposedTopEdges','totalHeight']
summary={}
inputs={(r['difficulty'],r['seed']):json.loads((root/'results/box-stacks/candidates'/f"{r['difficulty']}-{r['seed']}.json").read_text())['generator']['features'] for r in trials}
(data/'input_features.json').write_text(json.dumps([dict(difficulty=r['difficulty'],seed=r['seed'],features=inputs[r['difficulty'],r['seed']])for r in trials],indent=2)+'\n',encoding='utf-8')
for grade in ['Easy','Standard','Hard']:
 rows=[r for r in trials if r['difficulty']==grade];accepted=[r for r in rows if r['passed']];assert len(rows)==20
 values={name:[inputs[r['difficulty'],r['seed']][name]for r in rows] for name in features}
 metrics=[m for r in accepted for m in r['settlement']['boxes']]
 summary[grade]={'attempts':20,'passed':len(accepted),'passRate':len(accepted)/20,'reasons':dict(collections.Counter(r['reason']for r in rows if not r['passed'])),'features':{k:{'min':min(v),'max':max(v),'mean':statistics.mean(v),'histogram':dict(collections.Counter(str(round(x,3))for x in v))}for k,v in values.items()},'settledFeatures':{name:{'min':min(r['features'][name]for r in accepted),'max':max(r['features'][name]for r in accepted)}for name in features},'settlement':{'maxDisplacement':max(m['displacement']for m in metrics),'maxSpeed':max(m['speed']for m in metrics),'maxAngle':max(m['angle']for m in metrics),'sleepingBodies':sum(m['sleeping']for m in metrics),'bodies':len(metrics)}}
 for r in rows:
  if not r['passed']:
   folder=data/'rejected';folder.mkdir(exist_ok=True)
   shutil.copy2(root/'results/box-stacks/candidates'/f"{grade}-{r['seed']}.json",folder/f"{grade.lower()}-{r['seed']}.json")
(data/'summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
with (data/'features.csv').open('w',newline='',encoding='utf-8')as f:
 writer=csv.writer(f);writer.writerow(['difficulty','seed','passed','reason',*['input_'+k for k in features],*['settled_'+k for k in features]])
 for r in trials:writer.writerow([r['difficulty'],r['seed'],r['passed'],r['reason'],*[inputs[r['difficulty'],r['seed']][k]for k in features],*[r['features'][k]if r['passed']else '' for k in features]])
print(json.dumps(summary,indent=2))
