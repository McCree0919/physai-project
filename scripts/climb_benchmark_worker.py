"""Five-minute resource-only PPO benchmark; never edits installed trainer or task."""
import argparse
import datetime
import json
from pathlib import Path
import sys
import threading
import time
from climb_pilot import rss_mib

def main():
    parser=argparse.ArgumentParser(add_help=False)
    parser.add_argument('--bench-output',required=True);parser.add_argument('--bench-device',required=True,choices=['cpu','cuda:0']);parser.add_argument('--bench-seconds',type=float,default=300)
    args,remaining=parser.parse_known_args()
    from mlagents.trainers.ppo.trainer import PPOTrainer
    from mlagents.trainers.learn import main as learn_main
    import torch
    # ML-Agents 1.1's CPU switch does not undo PyTorch 2.x's earlier global CUDA default.
    # Match the explicitly requested resource; assert actual actor and critic devices below.
    torch.set_default_device(args.bench_device)
    use_cuda=args.bench_device.startswith('cuda')
    folder=Path(args.bench_output);folder.mkdir(parents=True,exist_ok=True)
    writer=(folder/'measurements.jsonl').open('w',encoding='utf-8',buffering=1)
    state={'start':None,'trainer':None,'last_update':None,'updates':0,'budget':False};lock=threading.Lock();done=threading.Event()
    def elapsed():return time.perf_counter()-state['start']if state['start']else 0
    def write(kind,**data):
        with lock:writer.write(json.dumps(dict(kind=kind,utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),**data))+'\n')
    def memory():
        if not use_cuda:return {}
        return dict(cuda_allocated_MiB=torch.cuda.memory_allocated()/2**20,cuda_reserved_MiB=torch.cuda.memory_reserved()/2**20,cuda_peak_allocated_MiB=torch.cuda.max_memory_allocated()/2**20)
    advance_original=PPOTrainer.advance;update_original=PPOTrainer._update_policy
    def advance(self):
        if state['start']is None:
            state['start']=time.perf_counter();state['trainer']=self
            devices=set(str(p.device)for p in self.policy.actor.parameters())
            if hasattr(self.optimizer,'critic'):devices.update(str(p.device)for p in self.optimizer.critic.parameters())
            assert devices=={args.bench_device},devices
            write('start',steps=int(self.get_step),duration=args.bench_seconds,parameter_devices=sorted(devices),torch_threads=torch.get_num_threads())
        if elapsed()>=args.bench_seconds:
            state['budget']=True;write('budget_stop',seconds=elapsed(),steps=int(self.get_step));raise KeyboardInterrupt
        return advance_original(self)
    def update(self):
        if use_cuda:torch.cuda.synchronize()
        start=time.perf_counter();buffer=int(self.update_buffer.num_experiences)
        collection=start-(state['last_update']or state['start'])
        try:return update_original(self)
        finally:
            if use_cuda:torch.cuda.synchronize()
            end=time.perf_counter();state['last_update']=end;state['updates']+=1
            write('update',seconds=elapsed(),steps=int(self.get_step),update_seconds=end-start,collection_seconds=collection,buffer=buffer,**memory())
    PPOTrainer.advance=advance;PPOTrainer._update_policy=update
    def monitor():
        while not done.wait(5):
            if state['trainer']:write('sample',seconds=elapsed(),steps=int(state['trainer'].get_step),rss_MiB=rss_mib(),**memory())
    thread=threading.Thread(target=monitor,daemon=True);thread.start();sys.argv=['mlagents-learn',*remaining]
    try:learn_main()
    finally:
        done.set();thread.join(timeout=5);write('finish',seconds=elapsed(),steps=int(state['trainer'].get_step)if state['trainer']else 0,updates=state['updates'],budget_stop=state['budget']);writer.close()

if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
