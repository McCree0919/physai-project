"""Run standalone, no-graphics preparation experiments (never invokes a trainer)."""
import argparse
import json
import subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

def run(mode,folder,count,seconds=12,warmup=3,height=.25,spacing=.4):
    output=ROOT/'results/layer2'/folder
    output.mkdir(parents=True,exist_ok=True)
    revision=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    command=[str(ROOT/'Builds/Layer2Prep/Layer2Prep.exe'),'-batchmode','-nographics','-logFile',str(output/'player.log'),
             '--prep-mode',mode,'--prep-count',str(count),'--prep-output',str(output),'--prep-revision',revision,
             '--prep-seconds',str(seconds),'--prep-warmup',str(warmup),'--prep-height',str(height),'--prep-spacing',str(spacing)]
    (output/'command.json').write_text(json.dumps(command,indent=2),encoding='utf-8')
    result=subprocess.run(command,cwd=ROOT,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=1800 if mode=='scan' else 180)
    if result.returncode:
        raise RuntimeError(f'{mode} failed: exit {result.returncode}; see {output}/player.log')
    metadata=json.loads((output/'metadata.json').read_text())
    print(json.dumps(metadata),flush=True)
    return metadata

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode',choices=['validate','scan','throughput','all'])
    parser.add_argument('--repeats',type=int,default=3)
    parser.add_argument('--seconds',type=float,default=12)
    args=parser.parse_args()
    if args.mode in ('validate','all'):run('validate','validate',8)
    if args.mode in ('scan','all'):run('scan','scan',8)
    if args.mode in ('throughput','all'):
        # Deliberately sequential: concurrent runs would contaminate CPU timings.
        for count in (16,32,64):
            for repeat in range(args.repeats):
                run('throughput',f'throughput/{count}_{repeat}',count,args.seconds)

if __name__=='__main__':main()
