"""Deterministic checkpoint episodes versus independent uniform-action episodes (defaults: 200 each, pilot model)."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import numpy as np
import onnxruntime as ort
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.environment_parameters_channel import EnvironmentParametersChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

ROOT=Path(__file__).resolve().parents[1]

def distribution(values):
    values=np.asarray(values,dtype=np.float64)
    norms=np.linalg.norm(values,axis=1)
    return {'samples':len(values),'normMin':float(norms.min()),'normMean':float(norms.mean()),'normQuantiles':np.quantile(norms,[.05,.5,.95]).tolist(),'outerFraction':float(np.mean(norms>=.84)),'innerFraction':float(np.mean(norms<=.16)),'axisMean':values.mean(0).tolist(),'axisMin':values.min(0).tolist(),'axisMax':values.max(0).tolist(),'normHistogram':np.histogram(norms,bins=np.linspace(0,.9,19))[0].tolist()}

def run(mode,episodes,model,root,target_mode=None,worker_id=31):
    folder=Path(root)/mode;folder.mkdir(parents=True,exist_ok=True)
    channel=EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=100)
    parameters=EnvironmentParametersChannel()
    if target_mode is not None:parameters.set_float_parameter('target_mode',float(target_mode))
    stats=StatsSideChannel();rng=np.random.default_rng(2026)
    options=ort.SessionOptions();options.intra_op_num_threads=1;options.inter_op_num_threads=1
    session=ort.InferenceSession(str(model),sess_options=options,providers=['CPUExecutionProvider'])if mode=='checkpoint'else None
    env=UnityEnvironment(file_name=str(ROOT/'Builds/ClimbDiagnostics/ClimbDiagnostics.exe'),seed=2026,worker_id=worker_id,no_graphics=True,log_folder=str(folder),side_channels=[channel,stats,parameters],additional_args=['--climb-output='+str(folder/'runtime'),'--climb-diagnostic-output='+str(folder/'observer')])
    active={};counts=collections.Counter();completed=[];targets_by_key={}
    try:
        env.reset();behavior=next(iter(env.behavior_specs))
        for iteration in range(20000):
            decisions,terminal=env.get_steps(behavior)
            for aid in terminal.agent_id:
                aid=int(aid);state=active.pop(aid);state['return']+=float(terminal[aid].reward);key=(aid,int(counts[aid]));counts[aid]+=1
                if len(completed)<episodes:
                    targets_by_key[key]=state.pop('targets');state.update(agentId=key[0],episode=key[1]);grip=state.pop('gripTargets');state['gripCommandTargets']=distribution(grip)if grip else None;state['commandTargets']=distribution(targets_by_key[key]);completed.append(state)
            # A terminal and the next episode's decision can share the same persistent Agent ID.
            for aid in decisions.agent_id:
                aid=int(aid);state=active.setdefault(aid,{'return':0,'targets':[],'gripTargets':[],'requestOutputs':0,'continuousClipped':0,'continuousCount':0})
                obs=decisions[aid].obs[0];state['return']+=float(decisions[aid].reward)
                state['targets'].extend([obs[19:22]*.9,obs[45:48]*.9])
                # Hand blocks start at 13 and 39; Grabbed flag at +14. Decision-time command targets of gripping hands.
                state['gripTargets'].extend([obs[13+26*h+6:13+26*h+9]*.9 for h in range(2) if obs[13+26*h+14]>.5])
            if len(completed)>=episodes:
                # Resume one decision cycle so the passive observer can flush all terminal records.
                if len(decisions):env.set_actions(behavior,ActionTuple(continuous=np.zeros((len(decisions),6),np.float32),discrete=np.zeros((len(decisions),2),np.int32)))
                env.step();break
            if len(decisions):
                if session:
                    continuous,discrete=session.run(['deterministic_continuous_actions','deterministic_discrete_actions'],{'obs_0':decisions.obs[0].astype(np.float32),'action_masks':np.ones((len(decisions),4),np.float32)})
                    discrete=discrete.astype(np.int32)
                else:continuous=rng.uniform(-1,1,(len(decisions),6)).astype(np.float32);discrete=rng.integers(0,2,(len(decisions),2),dtype=np.int32)
                for row,aid in enumerate(decisions.agent_id):
                    state=active[int(aid)];state['requestOutputs']+=int(discrete[row].sum());state['continuousClipped']+=int(np.sum(np.abs(continuous[row])>=1));state['continuousCount']+=6
                env.set_actions(behavior,ActionTuple(continuous=continuous,discrete=discrete))
            env.step();stats.get_and_reset_stats()
        assert len(completed)==episodes
    finally:env.close()
    observer={}
    for path in (folder/'observer').glob('*.jsonl'):
        for line in path.read_text().splitlines():
            record=json.loads(line);observer[record['agentId'],record['episode']]=record
    assert len({key[0]for key in observer})==16
    for row in completed:row['physics']=observer[row['agentId'],row['episode']]
    alltargets=[v for values in targets_by_key.values()for v in values];reasons=collections.Counter()
    for row in completed:reasons.update(dict(zip(row['physics']['rejectionReasons'],row['physics']['rejectionCounts'])))
    summary={'mode':mode,'episodes':episodes,'seed':2026,'model':str(model)if session else None,'targetMode':target_mode,'modelSha256':hashlib.sha256(model.read_bytes()).hexdigest()if session else None,'commandTargets':distribution(alltargets),'episodesWithGrip':sum(r['physics']['gripSuccesses']>0 for r in completed),'episodesAboveFirstTorsoHeight':sum(r['physics']['maximumTorsoY']>=.9 for r in completed),'gripSuccessTotal':sum(r['physics']['gripSuccesses']for r in completed),'requestOutputsTotal':sum(r['requestOutputs']for r in completed),'rejectionReasons':dict(reasons)}
    report={'summary':summary,'episodes':completed};(folder/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(summary),flush=True)

def main():
    p=argparse.ArgumentParser();p.add_argument('--episodes',type=int,default=200)
    p.add_argument('--model',default=str(ROOT/'results/climb_pilot_s2026/Climb.onnx'));p.add_argument('--modes',nargs='+',default=['checkpoint','random'],choices=['checkpoint','random'])
    p.add_argument('--output',default=str(ROOT/'results/climb-diagnostics'));p.add_argument('--target-mode',type=int,choices=[0,1]);p.add_argument('--worker-id',type=int,default=31)
    args=p.parse_args()
    for mode in args.modes:run(mode,args.episodes,Path(args.model),args.output,args.target_mode,args.worker_id)

if __name__=='__main__':main()
