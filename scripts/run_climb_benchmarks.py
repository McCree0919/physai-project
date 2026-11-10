"""Six sequential 300s PPO runs; original build/task/configuration unchanged."""
import datetime
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parents[1]

def main():
    folder=ROOT/'results/climb-benchmarks';folder.mkdir(parents=True,exist_ok=True)
    manifest={'startedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'configSha256':hashlib.sha256((ROOT/'config/climb_pilot.yaml').read_bytes()).hexdigest(),'runs':[]}
    for index,(count,device)in enumerate((n,d)for n in [2,4,8]for d in ['cpu','cuda:0']):
        name=f"{device.split(':')[0]}_e{count}";out=folder/name;out.mkdir(exist_ok=True)
        run_id=f'climb_bench_{name}_s2026'
        if(ROOT/'results'/run_id).exists():raise RuntimeError('Run ID already exists: '+run_id)
        command=[sys.executable,str(ROOT/'scripts/climb_benchmark_worker.py'),'--bench-output',str(out),'--bench-device',device,'--bench-seconds','300','config/climb_pilot.yaml','--env','Builds/Climb/Climb.exe','--run-id',run_id,'--seed','2026','--num-envs',str(count),'--base-port',str(6200+index*100),'--no-graphics','--torch-device',device,'--env-args','--climb-output='+str(out/'runtime')]
        (out/'command.json').write_text(json.dumps(command,indent=2)+'\n',encoding='utf-8')
        print('START '+name+' agents='+str(count*16),flush=True)
        with(out/'console.txt').open('w',encoding='utf-8')as log:result=subprocess.run(command,cwd=ROOT,stdout=log,stderr=subprocess.STDOUT)
        records=[json.loads(line)for line in(out/'measurements.jsonl').read_text().splitlines()if line.strip()]
        stop=next((r for r in records if r['kind']=='budget_stop'),None);finish=next((r for r in records if r['kind']=='finish'),None)
        if result.returncode!=0 or not stop or not finish or not finish['budget_stop']:raise RuntimeError(f'{name} failed; see {out}/console.txt')
        manifest['runs'].append(dict(name=name,device=device,environments=count,agents=count*16,runId=run_id,exitCode=result.returncode,seconds=stop['seconds'],steps=stop['steps']))
        (folder/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
        print('DONE '+json.dumps(manifest['runs'][-1]),flush=True)
    print('BENCHMARKS_COMPLETE',flush=True)

if __name__=='__main__':main()
