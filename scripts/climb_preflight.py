"""Zero-action packaged-interface check, not training or expert initialization."""
import json
from pathlib import Path
import numpy as np
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.base_env import ActionTuple
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.stats_side_channel import StatsSideChannel

ROOT=Path(__file__).resolve().parents[1]

def main():
    folder=ROOT/'results/climb-preflight';folder.mkdir(parents=True,exist_ok=True)
    channel=EngineConfigurationChannel();channel.set_configuration_parameters(time_scale=100)
    stats=StatsSideChannel()
    env=UnityEnvironment(file_name=str(ROOT/'Builds/Climb/Climb.exe'),no_graphics=True,worker_id=77,side_channels=[channel,stats],log_folder=str(folder),additional_args=['--climb-output='+str(folder/'runtime')])
    rewards={};completed=[]
    try:
        env.reset();name=list(env.behavior_specs)[0];spec=env.behavior_specs[name]
        assert len(spec.observation_specs)==1 and spec.observation_specs[0].shape==(327,)
        assert spec.action_spec.continuous_size==6 and list(spec.action_spec.discrete_branches)==[2,2]
        for _ in range(650):
            decisions,terminal=env.get_steps(name)
            for agent,reward in zip(decisions.agent_id,decisions.reward):rewards[int(agent)]=rewards.get(int(agent),0)+float(reward)
            for agent,reward in zip(terminal.agent_id,terminal.reward):completed.append(rewards.pop(int(agent),0)+float(reward))
            if len(decisions):assert np.isfinite(decisions.obs[0]).all() and np.max(np.abs(decisions.obs[0]))<=1.001
            stats.get_and_reset_stats()
            if len(decisions):env.set_actions(name,ActionTuple(continuous=np.zeros((len(decisions),6),np.float32),discrete=np.zeros((len(decisions),2),np.int32)))
            env.step()
        assert len(completed)>=16,completed
        assert all(abs(reward+1.12)<.002 for reward in completed),completed
        report={'behavior':name,'observations':327,'continuous':6,'discrete':[2,2],'steps':650,'completed_episodes':len(completed),'zero_action_episode_rewards':completed,'passed':True}
        (folder/'report.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report))
    finally:env.close()

if __name__ == '__main__':
    import player_guard;player_guard.install()  # leftover check, kill-on-exit job, player log cap (player_guard.py)
    main()
