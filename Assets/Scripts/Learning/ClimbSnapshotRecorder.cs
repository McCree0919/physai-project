using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using Unity.MLAgents;
using UnityEngine;

namespace PhysicalAI.Learning
{
    // Snapshot-library player only. Before the Academy clock (-200), so it sees the post-solve state at the start of each
    // physics step. The expert drives the body; the Agent (ExternalCommand) only tracks task state and reward ledger.
    [DefaultExecutionOrder(-300)]
    public sealed class ClimbSnapshotRecorder : MonoBehaviour
    {
        public const float TorsoJitterX=.05f,TorsoJitterZ=.03f,HandJitter=.03f;
        static StreamWriter writer;static string folder;static int target=64,seed=2026,started,finished;static bool quitting;
        static readonly Dictionary<string,int> stageCounts=new Dictionary<string,int>(),rejections=new Dictionary<string,int>();
        static readonly List<string> outcomes=new List<string>();
        ClimberAgent agent;ClimberBody body;BoxClimbSmokeController expert;ClimbTrajectoryTracker tracker;System.Random random;
        int episode=-1,trajectory=-1;string trajectoryId;bool recording;

        void Awake()
        {
            agent=GetComponent<ClimberAgent>();body=GetComponent<ClimberBody>();
            if(writer!=null)return;
            folder="results/climb-snapshots";
            foreach(string arg in Environment.GetCommandLineArgs())
            {
                if(arg.StartsWith("--climb-snapshot-output="))folder=arg.Substring("--climb-snapshot-output=".Length);
                if(arg.StartsWith("--climb-snapshot-trajectories="))target=int.Parse(arg.Substring("--climb-snapshot-trajectories=".Length));
                if(arg.StartsWith("--climb-snapshot-seed="))seed=int.Parse(arg.Substring("--climb-snapshot-seed=".Length));
            }
            Directory.CreateDirectory(folder);writer=new StreamWriter(Path.Combine(folder,"library.jsonl")){AutoFlush=true};
            Time.timeScale=20;
        }
        void FixedUpdate()
        {
            if(quitting||!agent.Active)return;
            if(agent.CompletedEpisodes!=episode)
            {
                if(recording)Finish();
                episode=agent.CompletedEpisodes;
                if(started>=target){recording=false;return;}
                StartTrajectory();return;
            }
            if(!recording)return;
            int index=tracker.Candidates.Count;
            // DecisionRequester period 5, step 0: the same boundary at which training decisions are taken.
            tracker.Observe(agent,Academy.Instance.StepCount%5==0,$"{name}-t{trajectory}-{index}",trajectoryId,"BoxClimbSmokeController two-step");
        }
        void StartTrajectory()
        {
            trajectory=started++;trajectoryId=$"{name}-t{trajectory}";random=new System.Random(ClimbSnapshotLibrary.StableSeed(trajectoryId,seed));
            var spawn=agent.Spawn;float J(float range)=>(float)(random.NextDouble()*2-1)*range;
            Vector3 hand()=>new Vector3(J(HandJitter),J(HandJitter),J(HandJitter));
            // Environment management: same public reset the Agent uses, with a seeded start perturbation for diversity.
            body.ResetBody(new BodyReset {Torso=spawn.Torso+new Vector3(J(TorsoJitterX),0,J(TorsoJitterZ)),LeftHand=spawn.LeftHand+hand(),RightHand=spawn.RightHand+hand(),Rotation=spawn.Rotation});
            Physics.SyncTransforms();
            if(expert!=null){expert.enabled=false;Destroy(expert);}
            expert=gameObject.AddComponent<BoxClimbSmokeController>();expert.Configure(body,true);
            tracker=new ClimbTrajectoryTracker();recording=true;
        }
        void Finish()
        {
            recording=false;finished++;
            var labeled=tracker.Label();int kept=0;
            foreach(var c in labeled)
            {
                // Recorded in this arena; validity depends only on snapshot data and the (identical) geometry.
                string reason=agent.ValidateState(c.state);
                if(reason!=null){rejections[reason]=rejections.TryGetValue(reason,out int n)?n+1:1;continue;}
                writer.WriteLine(JsonUtility.ToJson(c.state));kept++;
                stageCounts[c.state.stage]=stageCounts.TryGetValue(c.state.stage,out int m)?m+1:1;
            }
            outcomes.Add(FormattableString.Invariant($"{{\"trajectory\":\"{trajectoryId}\",\"firstGrip\":{tracker.FirstGrip:R},\"level1Time\":{tracker.Level1Time:R},\"level2Time\":{tracker.Level2Time:R},\"level2ContactTime\":{tracker.Level2ContactTime:R},\"goalTime\":{tracker.GoalTime:R},\"level1To2End\":{tracker.Level1To2End:R},\"expertCompleted\":{(expert.Completed?"true":"false")},\"candidates\":{tracker.Candidates.Count},\"kept\":{kept}}}"));
            if(finished>=target&&!quitting){quitting=true;WriteManifest();Application.Quit();}
        }
        static void WriteManifest()
        {
            string Map(Dictionary<string,int> map)=>"{"+string.Join(",",map.OrderBy(p=>p.Key).Select(p=>$"\"{p.Key}\":{p.Value}"))+"}";
            writer.Flush();writer.Dispose();
            File.WriteAllText(Path.Combine(folder,"manifest.json"),FormattableString.Invariant($"{{\"version\":{ClimbStateSnapshot.CurrentVersion},\"seed\":{seed},\"trajectories\":{finished},\"jitter\":{{\"torsoX\":{TorsoJitterX},\"torsoZ\":{TorsoJitterZ},\"hand\":{HandJitter}}},\"stages\":{Map(stageCounts)},\"rejections\":{Map(rejections)},\"outcomes\":[{string.Join(",",outcomes)}]}}\n"));
        }
    }
}
