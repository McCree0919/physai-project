using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhysicalAI.Climber;
using Unity.MLAgents;
using UnityEngine;

namespace PhysicalAI.Learning
{
    // Chooses the stage of each demonstration spawn. Report receives every finished episode's outcome
    // (stage "ground" for normal spawns): reserved for success-rate-driven mixing; the first version ignores it.
    public interface IClimbSpawnSchedule
    {
        string SampleStage(System.Random random);
        void Report(string stage,bool success,bool level1,bool level2);
    }

    public sealed class FixedSpawnSchedule : IClimbSpawnSchedule
    {
        // Within demonstration spawns: S1 35% (half pre-grip, half grip just established), S2 35%, S3 30%.
        public static readonly float[] DefaultWeights={.175f,.175f,.35f,.30f};
        public readonly float[] Weights;
        public FixedSpawnSchedule(params float[] weights)
        {
            if(weights==null||weights.Length!=ClimbSnapshotStages.All.Length||weights.Any(w=>w<0||float.IsNaN(w)||float.IsInfinity(w))||weights.Sum()<=0)
                throw new ArgumentException("Spawn weights must be "+ClimbSnapshotStages.All.Length+" finite non-negative values with a positive sum");
            Weights=(float[])weights.Clone();
        }
        public static FixedSpawnSchedule FromEnvironment(EnvironmentParameters p) => new FixedSpawnSchedule(
            p.GetWithDefault("spawn_weight_s1a",DefaultWeights[0]),p.GetWithDefault("spawn_weight_s1b",DefaultWeights[1]),
            p.GetWithDefault("spawn_weight_s2",DefaultWeights[2]),p.GetWithDefault("spawn_weight_s3",DefaultWeights[3]));
        public string SampleStage(System.Random random)
        {
            double u=random.NextDouble()*Weights.Sum();
            for(int i=0;i<Weights.Length;i++){u-=Weights[i];if(u<0)return ClimbSnapshotStages.All[i];}
            for(int i=Weights.Length-1;i>=0;i--)if(Weights[i]>0)return ClimbSnapshotStages.All[i];
            throw new InvalidOperationException("No positive spawn weight");
        }
        public void Report(string stage,bool success,bool level1,bool level2){}
    }

    public sealed class SnapshotCandidate
    {
        public ClimbStateSnapshot state;
        public float time,torsoSpeed,lastGripChange=-1,lastGripCreated=-1;
        public int grabbedHands,step;
        public bool handAboveTop,level1,level2;
        public object expert; // Optional copy of an expert's internal state (consistency tests only).
    }

    public static class ClimbSnapshotStages
    {
        public const string PreGrip="S1a",GripEstablished="S1b",Level1Pull="S2",Level1To2="S3";
        public static readonly string[] All={PreGrip,GripEstablished,Level1Pull,Level1To2};
        public const float PreGripWindow=.5f,GripWindow=.5f,ContactGuard=.04f,AboveTopDistance=.3f,StaticSpeed=.05f,StaticHold=.5f,StaticWeight=.2f;

        // Any free hand above a graspable top face (layer-limited, same arena), i.e. about to descend onto it.
        public static bool HandAboveTop(ClimberBody body,RaycastHit[] buffer)
        {
            var arena=body.transform;
            for(int i=0;i<2;i++)
            {
                var h=body.GetHandState((HandId)i);if(h.Grabbed)continue;
                if(ClimbLocalGeometry.Cast(arena,arena.TransformPoint(h.Position),-arena.up,AboveTopDistance,buffer,out var hit)
                   &&hit.collider.GetComponent<ClimberSurface>()!=null&&Vector3.Dot(hit.normal,arena.up)>=.7071f)return true;
            }
            return false;
        }

        // Stage windows by trajectory events (LAYER3_SNAPSHOT_DESIGN 6.2); unlabeled candidates are dropped.
        // level1To2End: first contact with the second level or success (ClimbTrajectoryTracker.Level1To2End); -1 if never reached.
        public static List<SnapshotCandidate> Label(IEnumerable<SnapshotCandidate> candidates,float firstGrip,float level1Time,float level1To2End)
        {
            var result=new List<SnapshotCandidate>();
            foreach(var c in candidates)
            {
                float t=c.time;
                if(c.lastGripCreated>=0&&t-c.lastGripCreated<ContactGuard)continue; // Skip the high-impulse contact instant.
                string stage=null;
                if(firstGrip>=0&&t>=firstGrip-PreGripWindow&&t<firstGrip&&c.grabbedHands==0&&c.handAboveTop)stage=PreGrip;
                else if(firstGrip>=0&&t>=firstGrip&&t<=firstGrip+GripWindow&&c.grabbedHands>0&&!c.level1)stage=GripEstablished;
                else if(firstGrip>=0&&level1Time>=0&&t>firstGrip+GripWindow&&t<level1Time)stage=Level1Pull;
                else if(level1Time>=0&&level1To2End>=0&&t>=level1Time&&t<level1To2End)stage=Level1To2;
                if(stage==null)continue;
                c.state.stage=stage;
                c.state.weight=c.torsoSpeed<StaticSpeed&&(c.lastGripChange<0||t-c.lastGripChange>=StaticHold)?StaticWeight:1;
                result.Add(c);
            }
            return result;
        }
    }

    // Per-trajectory event tracking shared by the recorder player and the consistency tests.
    // Observe at the start of each physics step, before the Agent pre-step; capture on decision boundaries.
    public sealed class ClimbTrajectoryTracker
    {
        public readonly List<SnapshotCandidate> Candidates=new List<SnapshotCandidate>();
        // Level2ContactTime: first grip after level 1 on a surface whose top is >=0.2m (the progress step) above the level-1 support.
        // GoalTime: first goal-region entry after level 1. Success resets the Agent within the same step, so it is never observed directly.
        public float FirstGrip=-1,Level1Time=-1,Level2Time=-1,Level2ContactTime=-1,GoalTime=-1;
        public float Level1To2End => new[] {Level2ContactTime,Level2Time,GoalTime}.Where(v=>v>=0).DefaultIfEmpty(-1).Min();
        float lastGripChange=-1,lastGripCreated=-1;
        Collider level1Support;
        readonly bool[] held=new bool[2];
        readonly RaycastHit[] hits=new RaycastHit[16];
        public SnapshotCandidate Observe(ClimberAgent agent,bool capture,string id,string trajectory,string source)
        {
            var body=agent.GetComponent<ClimberBody>();float t=agent.Elapsed;var b=body.GetBodyState();
            for(int i=0;i<2;i++)
            {
                bool grabbed=body.GetHandState((HandId)i).Grabbed;
                if(grabbed!=held[i])
                {
                    lastGripChange=t;held[i]=grabbed;
                    if(grabbed)
                    {
                        lastGripCreated=t;var surface=body.GetCapturedSurface((HandId)i);
                        if(Level1Time>=0&&Level2ContactTime<0&&surface!=null&&level1Support!=null&&surface.bounds.max.y>=level1Support.bounds.max.y+.2f)Level2ContactTime=t;
                    }
                }
            }
            if(b.GrabbedHands>0&&FirstGrip<0)FirstGrip=t;
            if(agent.Level1&&Level1Time<0){Level1Time=t;level1Support=agent.CurrentSupport;}
            if(agent.Level2&&Level2Time<0)Level2Time=t;
            if(Level1Time>=0&&GoalTime<0&&agent.GetComponent<ClimberGoal>().Contains(b))GoalTime=t;
            if(!capture)return null;
            var c=new SnapshotCandidate {time=t,torsoSpeed=b.Velocity.magnitude,grabbedHands=b.GrabbedHands,handAboveTop=ClimbSnapshotStages.HandAboveTop(body,hits),
                level1=agent.Level1,level2=agent.Level2,lastGripChange=lastGripChange,lastGripCreated=lastGripCreated};
            c.state=agent.CaptureState(id,"",trajectory,source);
            Candidates.Add(c);return c;
        }
        public List<SnapshotCandidate> Label() => ClimbSnapshotStages.Label(Candidates,FirstGrip,Level1Time,Level1To2End);
    }

    // Stage -> trajectory -> snapshots. Sampling: uniform over trajectories that still have usable snapshots of the stage,
    // then by snapshot weight (static frames down-weighted), so long trajectories do not dominate.
    public sealed class ClimbSnapshotLibrary
    {
        readonly Dictionary<string,List<List<ClimbStateSnapshot>>> stages=new Dictionary<string,List<List<ClimbStateSnapshot>>>();
        readonly Dictionary<string,string> quarantined=new Dictionary<string,string>();
        public int Count {get;private set;}
        public IReadOnlyDictionary<string,string> Quarantined => quarantined;

        public static ClimbSnapshotLibrary FromSnapshots(IEnumerable<ClimbStateSnapshot> snapshots)
        {
            var library=new ClimbSnapshotLibrary();var ids=new HashSet<string>();
            foreach(var group in snapshots.GroupBy(s=>(s.stage,s.trajectory)))
            {
                if(Array.IndexOf(ClimbSnapshotStages.All,group.Key.stage)<0)throw new InvalidDataException("Unknown snapshot stage "+group.Key.stage);
                var list=group.ToList();
                foreach(var s in list){if(string.IsNullOrEmpty(s.id)||!ids.Add(s.id))throw new InvalidDataException("Missing or duplicate snapshot id "+s.id);if(!(s.weight>0))throw new InvalidDataException("Non-positive weight "+s.id);}
                if(!library.stages.TryGetValue(group.Key.stage,out var trajectories))library.stages[group.Key.stage]=trajectories=new List<List<ClimbStateSnapshot>>();
                trajectories.Add(list);library.Count+=list.Count;
            }
            return library;
        }
        // One JsonUtility-serialized ClimbStateSnapshot per line.
        public static ClimbSnapshotLibrary Load(string path) =>
            FromSnapshots(File.ReadLines(path).Where(line=>line.Trim().Length>0).Select(line=>JsonUtility.FromJson<ClimbStateSnapshot>(line)).ToList());

        static bool commandLineChecked;static ClimbSnapshotLibrary commandLine;
        public static ClimbSnapshotLibrary FromCommandLine()
        {
            if(commandLineChecked)return commandLine;
            commandLineChecked=true;
            foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith("--climb-snapshot-library="))
            {
                commandLine=Load(arg.Substring("--climb-snapshot-library=".Length));
                Debug.Log($"CLIMB_SNAPSHOT_LIBRARY snapshots={commandLine.Count} stages={string.Join(",",commandLine.stages.Select(p=>p.Key+":"+p.Value.Sum(t=>t.Count)))}");
            }
            return commandLine;
        }

        public int StageCount(string stage) => stages.TryGetValue(stage,out var t)?t.Sum(list=>list.Count(s=>!quarantined.ContainsKey(s.id))):0;

        public ClimbStateSnapshot Sample(string stage,System.Random random)
        {
            if(!stages.TryGetValue(stage,out var trajectories))return null;
            var usable=trajectories.Select(list=>list.Where(s=>!quarantined.ContainsKey(s.id)).ToList()).Where(list=>list.Count>0).ToList();
            if(usable.Count==0)return null;
            var chosen=usable[random.Next(usable.Count)];
            double u=random.NextDouble()*chosen.Sum(s=>s.weight);
            foreach(var s in chosen){u-=s.weight;if(u<0)return s;}
            return chosen[chosen.Count-1];
        }
        public void Quarantine(string id,string reason){if(!quarantined.ContainsKey(id))quarantined.Add(id,reason);}

        public const float QuarantineAlert=.01f;public const int QuarantineLogInterval=1000;
        int draws;
        public int StageTotal(string stage) => stages.TryGetValue(stage,out var t)?t.Sum(list=>list.Count):0;
        // Quarantined share of a stage's snapshots; above 1% the stage's demonstration starts deviate from the configured mix.
        public float QuarantineFraction(string stage){int total=StageTotal(stage);return total==0?0:1-StageCount(stage)/(float)total;}
        // Called once per demonstration draw (shared by all Agents of the process); logs every QuarantineLogInterval draws.
        public void NoteDraw()
        {
            if(++draws%QuarantineLogInterval!=0)return;
            var parts=ClimbSnapshotStages.All.Select(s=>$"{s}={StageTotal(s)-StageCount(s)}/{StageTotal(s)}({QuarantineFraction(s):P2}){(QuarantineFraction(s)>QuarantineAlert?" ALERT>1%":"")}");
            string line=$"CLIMB_SNAPSHOT_QUARANTINE draws={draws} "+string.Join(" ",parts);
            if(ClimbSnapshotStages.All.Any(s=>QuarantineFraction(s)>QuarantineAlert))Debug.LogWarning(line);else Debug.Log(line);
        }

        // FNV-1a: stable across processes, unlike string.GetHashCode.
        public static int StableSeed(string name,int seed)
        {
            unchecked
            {
                uint hash=2166136261;
                foreach(char ch in name+"#"+seed){hash^=ch;hash*=16777619;}
                return (int)(hash&0x7fffffff);
            }
        }
    }
}
