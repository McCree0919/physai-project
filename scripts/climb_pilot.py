"""Wall-timed PPO calibration. Instruments installed trainer in-process without editing it."""
import argparse
import datetime
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
import ctypes


def rss_mib():
    if os.name!='nt':return None
    class Counters(ctypes.Structure):
        _fields_=[('cb',ctypes.c_ulong),('faults',ctypes.c_ulong),*[(name,ctypes.c_size_t)for name in ['peak_working','working','peak_pool','pool','peak_nonpaged','nonpaged','pagefile','peak_pagefile']]]
    counters=Counters();counters.cb=ctypes.sizeof(counters)
    kernel=ctypes.windll.kernel32;kernel.GetCurrentProcess.restype=ctypes.c_void_p
    api=ctypes.windll.psapi.GetProcessMemoryInfo;api.argtypes=[ctypes.c_void_p,ctypes.POINTER(Counters),ctypes.c_ulong]
    if not api(kernel.GetCurrentProcess(),ctypes.byref(counters),counters.cb):raise ctypes.WinError()
    return counters.working/2**20


def main():
    parser=argparse.ArgumentParser(add_help=False)
    parser.add_argument('--pilot-seconds',type=float,default=660)
    parser.add_argument('--pilot-profile',default='results/climb-pilot-profile')
    args,remaining=parser.parse_known_args()
    from mlagents.trainers.ppo.trainer import PPOTrainer
    from mlagents.trainers.learn import main as learn_main
    import torch
    folder=Path(args.pilot_profile).resolve();folder.mkdir(parents=True,exist_ok=True)
    writer=(folder/'measurements.jsonl').open('w',buffering=1,encoding='utf-8')
    lock=threading.Lock();state={'start':None,'trainer':None,'last_update_end':None,'updates':0,'stopped_by_budget':False}
    done=threading.Event()
    def write(kind,**values):
        record={'kind':kind,'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),**values}
        with lock:writer.write(json.dumps(record)+'\n')
    def elapsed():return time.perf_counter()-state['start'] if state['start'] else 0
    def gpu():
        if not torch.cuda.is_initialized():return {}
        return dict(cuda_allocated_MiB=torch.cuda.memory_allocated()/2**20,cuda_reserved_MiB=torch.cuda.memory_reserved()/2**20,cuda_peak_allocated_MiB=torch.cuda.max_memory_allocated()/2**20)
    original_advance=PPOTrainer.advance;original_update=PPOTrainer._update_policy
    def advance(self):
        if state['start'] is None:
            state['start']=time.perf_counter();state['trainer']=self
            write('start',steps=int(self.get_step),duration=args.pilot_seconds,torch=torch.__version__,device=torch.cuda.get_device_name(0))
        if elapsed()>=args.pilot_seconds:
            state['stopped_by_budget']=True;write('budget_stop',seconds=elapsed(),steps=int(self.get_step));raise KeyboardInterrupt
        return original_advance(self)
    def update(self):
        torch.cuda.synchronize();start=time.perf_counter();buffer=int(self.update_buffer.num_experiences)
        collection=start-(state['last_update_end'] or state['start'])
        try:return original_update(self)
        finally:
            torch.cuda.synchronize();end=time.perf_counter();state['last_update_end']=end;state['updates']+=1
            write('update',seconds=elapsed(),steps=int(self.get_step),update_seconds=end-start,collection_seconds=collection,buffer=buffer,**gpu())
    PPOTrainer.advance=advance;PPOTrainer._update_policy=update
    def monitor():
        while not done.wait(5):
            if not state['start']:continue
            values={}
            try:
                creation=getattr(subprocess,'CREATE_NO_WINDOW',0)
                result=subprocess.check_output(['nvidia-smi','--query-gpu=memory.used,utilization.gpu','--format=csv,noheader,nounits'],text=True,creationflags=creation,timeout=3).strip().split(',')
                values={'device_total_used_MiB':float(result[0]),'device_utilization_percent':float(result[1])}
            except Exception as e:values={'nvidia_smi_error':str(e)}
            trainer=state['trainer'];write('sample',seconds=elapsed(),steps=int(trainer.get_step),rss_MiB=rss_mib(),**gpu(),**values)
    thread=threading.Thread(target=monitor,daemon=True);thread.start()
    sys.argv=['mlagents-learn',*remaining]
    try:learn_main()
    finally:
        done.set();thread.join(timeout=5)
        write('finish',seconds=elapsed(),steps=int(state['trainer'].get_step)if state['trainer']else 0,updates=state['updates'],budget_stop=state['stopped_by_budget'],**gpu())
        writer.close()


if __name__=='__main__':main()
