using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhysicalAI.Climber;
using Unity.MLAgents;
using UnityEngine;

namespace PhysicalAI.Learning
{
    [Serializable] public sealed class ClimbTaskSnapshot
    {
        public int targetMode;public Vector3[] commandTargets={Vector3.zero,Vector3.zero};
        public float elapsed,goalStable,supportStable,groundStable,brokenTime=-1,highestSupport,maximumHeight;
        // Supports are stored by stable structure object id; runtime instance ids never leave the process.
        public string supportKey="";public string[] paidSupportKeys=new string[0];
        public int decisions,progressEvents;public bool level1,level2;public bool[] previousGrip={false,false};
    }
    [Serializable] public sealed class ClimbStateSnapshot
    {
        public const int CurrentVersion=1;
        public int version=CurrentVersion;
        public string id="",stage="",trajectory="",source="",geometryHash="";
        public float weight=1;
        public BodySnapshot body;public ClimbTaskSnapshot task;
        public float[] observation; // 327 values at capture, used only to verify a restore; never fed to the policy from here.
    }

    public sealed partial class ClimberAgent
    {
        public const float SnapshotObservationTolerance=1e-4f,DefaultDemoFraction=.5f;
        // Assigned by tests; otherwise loaded once per process from --climb-snapshot-library=<jsonl>. Null keeps normal spawns only.
        public ClimbSnapshotLibrary SnapshotLibrary;
        public IClimbSpawnSchedule SpawnSchedule;
        public string SpawnSource {get;private set;}="ground";
        public bool Level1 => level1;
        public bool Level2 => level2;
        public bool Active => active;
        public BodyReset Spawn => spawn;
        // Current stable-support surface (null when unsupported); read-only, for trajectory labeling.
        public Collider CurrentSupport => supportId==0?null:Surfaces().Values.FirstOrDefault(c=>c.GetInstanceID()==supportId);
        string spawnStage;System.Random spawnRandom;
        Dictionary<string,Collider> surfaces;

        Dictionary<string,Collider> Surfaces()
        {
            if(surfaces!=null)return surfaces;
            var map=new Dictionary<string,Collider>();
            foreach(var c in GetComponentsInChildren<Collider>())
            {
                if(c.attachedRigidbody!=null||c.isTrigger)continue;
                // StaticStructureLoader names each geometry object by its structure document id.
                if(map.ContainsKey(c.gameObject.name))throw new InvalidOperationException("Duplicate static collider id "+c.gameObject.name);
                map.Add(c.gameObject.name,c);
            }
            return surfaces=map;
        }
        Collider Resolve(string key) => key!=null&&Surfaces().TryGetValue(key,out var c)?c:null;
        string KeyOf(Collider c)
        {
            if(c==null)return "";
            if(Resolve(c.gameObject.name)!=c)throw new InvalidOperationException("Not a static surface of this arena: "+c.gameObject.name);
            return c.gameObject.name;
        }
        string KeyOfInstance(int id)
        {
            if(id==0)return "";
            foreach(var pair in Surfaces())if(pair.Value.GetInstanceID()==id)return pair.Key;
            throw new InvalidOperationException("Unknown support instance "+id);
        }

        // Arena-local static geometry and goal, quantized to 0.1 mm / 1e-4; independent of arena translation and yaw.
        public string GeometryHash()
        {
            var ci=CultureInfo.InvariantCulture;var text=new StringBuilder();
            void F(float value)=>text.Append(Mathf.RoundToInt(value*1e4f).ToString(ci)).Append('|');
            void V(Vector3 value){F(value.x);F(value.y);F(value.z);}
            var inverse=Quaternion.Inverse(transform.rotation);
            foreach(var pair in Surfaces().OrderBy(p=>p.Key,StringComparer.Ordinal))
            {
                var c=pair.Value;var rotation=inverse*c.transform.rotation;if(rotation.w<0)rotation=new Quaternion(-rotation.x,-rotation.y,-rotation.z,-rotation.w);
                text.Append(pair.Key).Append('|').Append(c.GetType().Name).Append('|').Append(c.gameObject.layer).Append('|').Append(c.GetComponent<ClimberSurface>()!=null).Append('|');
                V(transform.InverseTransformPoint(c.transform.position));F(rotation.x);F(rotation.y);F(rotation.z);F(rotation.w);V(c.transform.lossyScale);
                if(c is BoxCollider box){V(box.center);V(box.size);}
            }
            V(goal.Center);V(goal.Size);
            using(var sha=SHA256.Create())
            {
                var hex=new StringBuilder(64);foreach(byte b in sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())))hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        public ClimbStateSnapshot CaptureState(string id="",string stage="",string trajectory="",string source="")
        {
            if(!active)throw new InvalidOperationException("Cannot snapshot a terminated episode");
            var s=new ClimbStateSnapshot {id=id,stage=stage,trajectory=trajectory,source=source,geometryHash=GeometryHash()};
            s.body=body.CaptureSnapshot(KeyOf);
            // An external expert commands the body directly; its last projected request is the equivalent Agent command target.
            if(ExternalCommand)for(int i=0;i<2;i++)targets[i]=s.body.hands[i].requested;
            s.task=new ClimbTaskSnapshot {targetMode=(int)ActiveTargetMode,commandTargets=new[] {targets[0],targets[1]},
                elapsed=elapsed,goalStable=goalStable,supportStable=supportStable,groundStable=groundStable,brokenTime=brokenTime,
                highestSupport=highestSupport,maximumHeight=maximumHeight,supportKey=KeyOfInstance(supportId),
                paidSupportKeys=paidSupports.Select(KeyOfInstance).OrderBy(k=>k,StringComparer.Ordinal).ToArray(),
                decisions=decisions,progressEvents=progressEvents,level1=level1,level2=level2,previousGrip=new[] {previousGrip[0],previousGrip[1]}};
            s.observation=BuildObservations();
            return s;
        }

        // Null when restorable into this arena and target mode; never mutates state.
        public string ValidateState(ClimbStateSnapshot s)
        {
            if(s==null||s.body==null||s.task==null)return "Malformed";
            var t=s.task;
            if(t.commandTargets==null||t.commandTargets.Length!=2||t.previousGrip==null||t.previousGrip.Length!=2||t.paidSupportKeys==null)return "Malformed";
            if(s.version!=ClimbStateSnapshot.CurrentVersion)return "Version";
            if(s.geometryHash!=GeometryHash())return "Geometry";
            if(t.targetMode!=(int)ActiveTargetMode)return "TargetMode";
            if(t.elapsed<0||t.elapsed>=60||t.decisions<0||t.decisions>=600)return "Budget";
            foreach(var q in t.commandTargets)if(float.IsNaN(q.x+q.y+q.z)||float.IsInfinity(q.x+q.y+q.z))return "Nonfinite";
            if(!string.IsNullOrEmpty(t.supportKey)&&Resolve(t.supportKey)==null)return "UnknownSupport";
            foreach(var key in t.paidSupportKeys)if(Resolve(key)==null)return "UnknownSupport";
            return body.ValidateSnapshot(s.body,Resolve);
        }

        // Episode management only (called at an episode boundary). Throws without changing state when invalid.
        public void RestoreState(ClimbStateSnapshot s)
        {
            string reason=ValidateState(s);
            if(reason!=null)throw new ArgumentException("Climb snapshot rejected: "+reason);
            body.RestoreSnapshot(s.body,Resolve);
            var t=s.task;
            for(int i=0;i<2;i++){targets[i]=t.commandTargets[i];previousGrip[i]=t.previousGrip[i];}
            elapsed=t.elapsed;goalStable=t.goalStable;supportStable=t.supportStable;groundStable=t.groundStable;brokenTime=t.brokenTime;
            highestSupport=t.highestSupport;maximumHeight=t.maximumHeight;decisions=t.decisions;progressEvents=t.progressEvents;level1=t.level1;level2=t.level2;
            supportId=string.IsNullOrEmpty(t.supportKey)?0:Resolve(t.supportKey).GetInstanceID();
            paidSupports.Clear();foreach(var key in t.paidSupportKeys)paidSupports.Add(Resolve(key).GetInstanceID());
            // This episode's return starts at zero: earlier progress stays in the ledger (never repaid) but is not re-credited.
            progressReward=rewardTime=rewardTerminal=0;active=true;
            Physics.SyncTransforms();
        }

        public float ObservationError(ClimbStateSnapshot s,out int index)
        {
            var now=BuildObservations();index=-1;
            if(s.observation==null||s.observation.Length!=now.Length)return float.PositiveInfinity;
            float worst=0;
            for(int i=0;i<now.Length;i++){float d=Mathf.Abs(now[i]-s.observation[i]);if(d>worst){worst=d;index=i;}}
            return worst;
        }

        // "--mlagents-port <n>" as passed by mlagents_envs (Academy.k_PortCommandLineFlag); "" in the editor and tests.
        static string WorkerPort()
        {
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="--mlagents-port")return args[i+1];
            return "";
        }

        void SnapshotSpawn()
        {
            SpawnSource="ground";spawnStage=null;
            var library=SnapshotLibrary??ClimbSnapshotLibrary.FromCommandLine();
            if(library==null)return;
            var parameters=Academy.Instance.EnvironmentParameters;var recorder=Academy.Instance.StatsRecorder;
            float fraction=parameters.GetWithDefault("spawn_demo_fraction",DefaultDemoFraction);
            // Arena names repeat in every worker process: the worker's port (base port + worker id) keeps the draws independent.
            if(spawnRandom==null)spawnRandom=new System.Random(ClimbSnapshotLibrary.StableSeed(name+"#"+WorkerPort(),(int)parameters.GetWithDefault("spawn_seed",0)));
            if(SpawnSchedule==null)SpawnSchedule=FixedSpawnSchedule.FromEnvironment(parameters);
            bool demo=spawnRandom.NextDouble()<fraction;
            recorder.Add("Climb/Spawn/DemoFraction",demo?1:0); // Drawn share; ActualDemoFraction below is the share that restored.
            if(!demo){recorder.Add("Climb/Spawn/ActualDemoFraction",0);return;}
            string stage=SpawnSchedule.SampleStage(spawnRandom);
            library.NoteDraw();
            var s=library.Sample(stage,spawnRandom);
            bool restored=false;
            try
            {
                if(s==null){recorder.Add("Climb/Spawn/Missing",1);Debug.LogWarning("CLIMB_SNAPSHOT_MISSING "+stage);return;}
                string reason=ValidateState(s);
                if(reason!=null)
                {
                    // Quarantined and counted, never silently reused; this episode keeps its normal start.
                    library.Quarantine(s.id,reason);recorder.Add("Climb/Spawn/Rejected",1);Debug.LogWarning($"CLIMB_SNAPSHOT_REJECTED {s.id} {reason}");return;
                }
                RestoreState(s);restored=true;
            }
            finally
            {
                // Per-draw stage quarantine share (TensorBoard averages per summary) and the demo share actually achieved.
                recorder.Add("Climb/Spawn/QuarantineFraction/"+stage,library.QuarantineFraction(stage));
                recorder.Add("Climb/Spawn/ActualDemoFraction",restored?1:0);
            }
            float error=ObservationError(s,out int index);
            if(error>SnapshotObservationTolerance){recorder.Add("Climb/Spawn/ObservationMismatch",1);Debug.LogWarning($"CLIMB_SNAPSHOT_OBSERVATION {s.id} index={index} error={error}");}
            spawnStage=s.stage;SpawnSource="snapshot:"+s.id+":"+s.stage;
        }
    }
}
