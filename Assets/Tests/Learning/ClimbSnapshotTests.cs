using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Learning;
using PhysicalAI.Structures;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;
using Object=UnityEngine.Object;

// LAYER3_SNAPSHOT_DESIGN section 5. Physics is stepped manually in the runtime order:
// Agent pre-step (Academy clock), expert (-100), body (0), then the PhysX solve.
public sealed class ClimbSnapshotTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly MethodInfo BodyStep=typeof(ClimberBody).GetMethod("PhysicsStep",Private);
    static readonly MethodInfo ExpertStep=typeof(BoxClimbSmokeController).GetMethod("FixedUpdate",Private);
    static readonly object[] Dt={.02f};
    // Lower box: top y=0.5, front face z=0. These are the expert's own first grip points (hand radius 0.1).
    static readonly Vector3 Torso0=new Vector3(0,.4f,-.4f),LeftEdge=new Vector3(-.3f,.6f,.02f),RightEdge=new Vector3(.3f,.6f,.02f);
    readonly List<GameObject> roots=new List<GameObject>();bool automatic;SimulationMode mode;

    [SetUp] public void Setup(){automatic=Academy.Instance.AutomaticSteppingEnabled;Academy.Instance.AutomaticSteppingEnabled=false;mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;}
    [TearDown] public void Cleanup(){foreach(var root in roots)Object.DestroyImmediate(root);roots.Clear();Academy.Instance.AutomaticSteppingEnabled=automatic;Physics.simulationMode=mode;}

    // At the document start (torso z=-0.4) heightmap cell (+0.4,+0.4) samples exactly the lower box front edge (z=0), where its
    // graspable flag (observation 302) is discontinuous; float-level arena differences flip it. Round-trip tests shift the spawn off it.
    static readonly Vector3 EdgeShift=new Vector3(0,0,-.005f);
    ClimberAgent Create(Vector3 position,Quaternion rotation,bool absolute=false,bool external=false,Vector3 shift=default)
    {
        var doc=BoxStackGenerator.Smoke(true);var body=StaticStructureLoader.Load(doc,position,rotation);roots.Add(body.gameObject);
        body.ResetBody(new BodyReset {Torso=doc.start.torso.Position+shift,LeftHand=doc.start.leftHand.Position+shift,RightHand=doc.start.rightHand.Position+shift,Rotation=Quaternion.identity});Physics.SyncTransforms();
        foreach(var c in body.GetComponentsInChildren<Collider>())if(c.attachedRigidbody==null)c.gameObject.layer=c.GetComponent<ClimberSurface>()!=null?8:9;
        var behavior=body.gameObject.AddComponent<BehaviorParameters>();behavior.BrainParameters.VectorObservationSize=327;behavior.BrainParameters.ActionSpec=new ActionSpec(6,new[] {2,2});behavior.BehaviorType=BehaviorType.HeuristicOnly;
        var agent=body.gameObject.AddComponent<ClimberAgent>();agent.TargetMode=absolute?ClimbTargetMode.Absolute:ClimbTargetMode.Incremental;agent.ExternalCommand=external;
        // Expert-driven agents are configured like the recorder player: decisions every 5 steps through the Academy.
        if(external){var requester=body.gameObject.AddComponent<DecisionRequester>();requester.DecisionPeriod=5;requester.DecisionStep=0;requester.TakeActionsBetweenDecisions=false;}
        agent.OnEpisodeBegin();Physics.SyncTransforms();return agent;
    }
    static ClimberBody Body(ClimberAgent a) => a.GetComponent<ClimberBody>();
    static void Step(ClimberAgent a,BoxClimbSmokeController expert=null)
    {
        // Expert steps run the full Academy step (as ClimbAcademyClock does at runtime): besides the Agent pre-step it sends the
        // agent info, which clears the SDK done flag. Without it every episode end after the first goes uncounted (CompletedEpisodes).
        if(expert!=null)Academy.Instance.EnvironmentStep();else a.BeforePhysics(0);
        if(expert!=null)ExpertStep.Invoke(expert,null);BodyStep.Invoke(Body(a),Dt);
        Physics.Simulate(.02f);Physics.SyncTransforms();
    }
    static void Pose(ClimberAgent a,Vector3 torso,Vector3 left,Vector3 right)
    {Body(a).ResetBody(new BodyReset {Torso=torso,LeftHand=left,RightHand=right,Rotation=Quaternion.identity});Physics.SyncTransforms();}
    // Absolute-mode action that holds a hand where it is (torso and arena unrotated relative to each other).
    static Vector3 Hold(ClimberAgent a,HandId hand){var h=Body(a).GetHandState(hand);return (h.Position-h.ShoulderPosition)/ClimberAgent.AbsoluteReach;}
    static void Run(ClimberAgent a,int steps,int grabLeft,int grabRight)
    {
        for(int k=0;k<steps;k++){if(k%5==0)a.ApplyCommand(Hold(a,HandId.Left),Hold(a,HandId.Right),grabLeft,grabRight);Step(a);}
    }
    // Representative states: free; one grip; two grips; released in cooldown; stable on level 1 with the ledger paid.
    Dictionary<string,ClimbStateSnapshot> States(ClimberAgent a,Vector3 torsoShift=default)
    {
        var states=new Dictionary<string,ClimbStateSnapshot>();
        Run(a,10,0,0);states["free"]=a.CaptureState("free");
        Pose(a,Torso0+torsoShift,LeftEdge,RightEdge);Run(a,10,1,0);Assert.IsTrue(Body(a).GetHandState(HandId.Left).Grabbed,"left grip");states["oneGrip"]=a.CaptureState("oneGrip");
        Run(a,10,1,1);Assert.AreEqual(2,Body(a).GetBodyState().GrabbedHands,"two grips");states["twoGrips"]=a.CaptureState("twoGrips");
        Run(a,5,0,1);Assert.Greater(Body(a).GetHandState(HandId.Left).Cooldown,0f,"cooldown");states["cooldown"]=a.CaptureState("cooldown");
        Pose(a,new Vector3(0,.95f,.30f),new Vector3(-.3f,.60f,.02f),new Vector3(.3f,.60f,.02f));for(int i=0;i<60;i++)a.BeforePhysics(i);
        Assert.IsTrue(a.Level1);Assert.AreEqual(1,a.ProgressEvents);states["level1"]=a.CaptureState("level1");
        return states;
    }

    // Recursive field comparison; tolerance<0 means bitwise. Skips listed paths.
    static void Compare(object x,object y,string path,float tolerance,List<string> failures,ICollection<string> skip=null)
    {
        if(skip!=null&&skip.Contains(path))return;
        if(x==null||y==null){if(!(x==null&&y==null))failures.Add(path+" null");return;}
        switch(x)
        {
            case float a:
            {
                float b=(float)y;
                if(tolerance<0?BitConverter.SingleToInt32Bits(a)!=BitConverter.SingleToInt32Bits(b):!(Mathf.Abs(a-b)<=tolerance))failures.Add($"{path} {a:R} vs {b:R}");
                return;
            }
            case Vector3 v:{var w=(Vector3)y;Compare(v.x,w.x,path+".x",tolerance,failures);Compare(v.y,w.y,path+".y",tolerance,failures);Compare(v.z,w.z,path+".z",tolerance,failures);return;}
            case Quaternion q:{var r=(Quaternion)y;Compare(q.x,r.x,path+".x",tolerance,failures);Compare(q.y,r.y,path+".y",tolerance,failures);Compare(q.z,r.z,path+".z",tolerance,failures);Compare(q.w,r.w,path+".w",tolerance,failures);return;}
            case int _:case bool _:case string _:if(!x.Equals(y))failures.Add($"{path} {x} vs {y}");return;
            case Array array:
            {
                var other=(Array)y;if(array.Length!=other.Length){failures.Add(path+" length");return;}
                for(int i=0;i<array.Length;i++)Compare(array.GetValue(i),other.GetValue(i),path+"["+i+"]",tolerance,failures,skip);
                return;
            }
        }
        foreach(var field in x.GetType().GetFields(BindingFlags.Public|BindingFlags.Instance))Compare(field.GetValue(x),field.GetValue(y),path+"."+field.Name,tolerance,failures,skip);
    }
    static void AssertSame(ClimbStateSnapshot x,ClimbStateSnapshot y,float tolerance,string label,ICollection<string> skip=null)
    {var failures=new List<string>();Compare(x,y,"",tolerance,failures,skip);Assert.IsEmpty(failures,label+": "+string.Join("; ",failures.Take(8)));}
    static readonly string[] Labels={".id",".stage",".trajectory",".source"};

    // 5.1-1
    [Test] public void SerializationRoundTripIsBitwise()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true);
        foreach(var pair in States(a))
        {
            var copy=JsonUtility.FromJson<ClimbStateSnapshot>(JsonUtility.ToJson(pair.Value));
            AssertSame(pair.Value,copy,-1,pair.Key);
        }
    }
    // 5.1-2 and 5.3: same arena; Capture -> Restore -> Capture, observation included.
    [Test] public void SameArenaStateRoundTrip()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true);
        foreach(var pair in States(a))
        {
            a.RestoreState(pair.Value);var again=a.CaptureState(pair.Key);
            AssertSame(pair.Value,again,1e-6f,pair.Key);
            Assert.LessOrEqual(a.ObservationError(pair.Value,out int index),ClimberAgent.SnapshotObservationTolerance,pair.Key+" obs "+index);
        }
    }
    // 5.1-3: translated and yawed copy at training-scale coordinates (float resolution ~4e-6 m at 42 m).
    [Test] public void CrossArenaStateRoundTrip()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true,false,EdgeShift);var b=Create(new Vector3(42,0,28),Quaternion.Euler(0,90,0),true,false,EdgeShift);
        foreach(var pair in States(a,EdgeShift))
        {
            b.RestoreState(pair.Value);var there=b.CaptureState(pair.Key);
            // Forces (up to ~300 N) are rotated twice through a non-exact 90-degree quaternion: checked separately at 1e-3 N.
            AssertSame(pair.Value,there,2e-5f,pair.Key,new[] {".observation",".body.hands[0].force",".body.hands[1].force"});
            for(int i=0;i<2;i++)Assert.LessOrEqual(Vector3.Distance(pair.Value.body.hands[i].force,there.body.hands[i].force),1e-3f,pair.Key+" force "+i);
            Assert.LessOrEqual(b.ObservationError(pair.Value,out int index),.002f,pair.Key+" obs "+index);
        }
    }
    // 5.1-4: every rejection reason throws and leaves the arena untouched.
    [Test] public void InvalidSnapshotsAreRejectedWithoutSideEffects()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true);var states=States(a);var b=Create(new Vector3(60,0,0),Quaternion.identity,true);
        var cases=new Dictionary<string,Action<ClimbStateSnapshot>>
        {
            ["Geometry"]=s=>s.geometryHash="x",["TargetMode"]=s=>s.task.targetMode=0,["BodyConfiguration"]=s=>s.body.configurationHash="x",
            ["UnknownSupport"]=s=>s.task.paidSupportKeys=new[] {"missing"},["UnknownSurface0"]=s=>s.body.hands[0].capturedSurface="missing",
            ["ArmLength0"]=s=>s.body.hands[0].position+=new Vector3(0,0,-1.5f),["Penetration"]=s=>s.body.torsoPosition=new Vector3(0,.25f,.9f),
            ["GripAnchor0"]=s=>s.body.hands[0].gripConnectedAnchor+=new Vector3(0,0,.01f),["Budget"]=s=>s.task.elapsed=61,
        };
        var before=b.CaptureState("before");
        foreach(var pair in cases)
        {
            var bad=JsonUtility.FromJson<ClimbStateSnapshot>(JsonUtility.ToJson(states["twoGrips"]));pair.Value(bad);
            Assert.AreEqual(pair.Key,b.ValidateState(bad),pair.Key);
            Assert.Throws<ArgumentException>(()=>b.RestoreState(bad),pair.Key);
            AssertSame(before,b.CaptureState("before"),-1,"untouched after "+pair.Key);
        }
    }
    // 5.2-5: first-step PD force from the restored filter/effort state equals the original's (same arena).
    [Test] public void PdForceMatchesAfterRestore()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true);
        // Live state -> one step gives the original force; restore the pre-step capture -> one step must give the same force.
        void Check(string label,int grabLeft,int grabRight)
        {
            var s=a.CaptureState(label);Run(a,1,grabLeft,grabRight);
            var reference=new[] {Body(a).GetHandState(HandId.Left).CommandForce,Body(a).GetHandState(HandId.Right).CommandForce};
            a.RestoreState(s);Run(a,1,grabLeft,grabRight);
            for(int i=0;i<2;i++)Assert.LessOrEqual(Vector3.Distance(reference[i],Body(a).GetHandState((HandId)i).CommandForce),1e-4f,label+" hand "+i);
        }
        Run(a,10,0,0);Check("free",0,0);
        Pose(a,Torso0,LeftEdge,RightEdge);Run(a,10,1,0);Check("oneGrip",1,0);
        Run(a,10,1,1);Check("twoGrips",1,1);
        Run(a,5,0,1);Check("cooldown",0,1);
    }
    // 5.2-6: cooldown, NeedsRearm and held grips keep their semantics after restore.
    [Test] public void GripSemanticsSurviveRestore()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true);var states=States(a);var b=Create(new Vector3(60,0,0),Quaternion.identity,true);
        b.RestoreState(states["cooldown"]);var left=Body(b).GetHandState(HandId.Left);Assert.IsFalse(left.Grabbed);Assert.Greater(left.Cooldown,0f);
        b.ApplyCommand(Hold(b,HandId.Left),Hold(b,HandId.Right),1,1);Step(b);Assert.IsFalse(Body(b).GetHandState(HandId.Left).Grabbed,"no grip during cooldown");
        Run(b,20,1,1);Assert.IsTrue(Body(b).GetHandState(HandId.Left).Grabbed,"grip after cooldown");

        var rearm=JsonUtility.FromJson<ClimbStateSnapshot>(JsonUtility.ToJson(states["cooldown"]));
        rearm.body.hands[0].armed=false;rearm.body.hands[0].cooldown=0;rearm.body.hands[0].wantsGrab=true;
        b.RestoreState(rearm);Assert.IsTrue(Body(b).GetHandState(HandId.Left).NeedsRearm);
        Run(b,10,1,1);Assert.IsFalse(Body(b).GetHandState(HandId.Left).Grabbed,"held 1 never bypasses rearm");
        Run(b,5,0,1);Run(b,20,1,1);Assert.IsTrue(Body(b).GetHandState(HandId.Left).Grabbed,"0 then 1 rearms");

        b.RestoreState(states["twoGrips"]);Run(b,1,1,1);
        Assert.AreEqual(2,Body(b).GetBodyState().GrabbedHands,"no break on first step");
        Assert.IsNull(b.ValidateState(b.CaptureState("after")),"anchors within tolerance after one step");
    }
    // 5.2-7: the restored ledger never repays level 1; level 2 pays once; the episode return starts at zero.
    [Test] public void RewardLedgerIsRestored()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true);var states=States(a);var b=Create(new Vector3(60,0,0),Quaternion.identity,true);
        b.RestoreState(states["level1"]);Assert.AreEqual(0,b.GetCumulativeReward(),1e-6);
        for(int i=0;i<60;i++)b.BeforePhysics(i);Assert.AreEqual(1,b.ProgressEvents);Assert.AreEqual(0,b.GetCumulativeReward(),1e-6);
        Pose(b,new Vector3(0,1.45f,1.60f),new Vector3(-.3f,1.10f,1.40f),new Vector3(.3f,1.10f,1.40f));for(int i=0;i<60;i++)b.BeforePhysics(i);
        Assert.AreEqual(2,b.ProgressEvents);Assert.AreEqual(.1f,b.GetCumulativeReward(),1e-5);
    }
    // 5.2-8: a normal reset after a snapshot episode equals a normal reset that never saw one.
    [Test] public void NormalResetAfterSnapshotIsUnchanged()
    {
        var a=Create(Vector3.zero,Quaternion.identity,true,false,EdgeShift);var states=States(a,EdgeShift);var fresh=Create(new Vector3(0,0,60),Quaternion.identity,true,false,EdgeShift);
        a.RestoreState(states["twoGrips"]);a.OnEpisodeBegin();Physics.SyncTransforms();
        Assert.AreEqual("ground",a.SpawnSource);
        AssertSame(fresh.CaptureState(),a.CaptureState(),1e-5f,"reset",new[] {".observation",".body.diagnosticTime"});
        Assert.LessOrEqual(a.ObservationError(fresh.CaptureState(),out int index),1e-4f,"obs "+index);
    }
    // Spawn mixing: fixed stage weights and per-trajectory balanced sampling with quarantine.
    [Test] public void SpawnScheduleAndLibrarySampling()
    {
        var schedule=new FixedSpawnSchedule(FixedSpawnSchedule.DefaultWeights);var random=new System.Random(1);var counts=ClimbSnapshotStages.All.ToDictionary(s=>s,s=>0);
        for(int i=0;i<20000;i++)counts[schedule.SampleStage(random)]++;
        for(int i=0;i<4;i++)Assert.AreEqual(FixedSpawnSchedule.DefaultWeights[i],counts[ClimbSnapshotStages.All[i]]/20000f,.015f,ClimbSnapshotStages.All[i]);
        Assert.Throws<ArgumentException>(()=>new FixedSpawnSchedule(1,1,1));Assert.Throws<ArgumentException>(()=>new FixedSpawnSchedule(0,0,0,0));
        var snapshots=new List<ClimbStateSnapshot>();
        for(int t=0;t<2;t++)for(int k=0;k<(t==0?9:1);k++)snapshots.Add(new ClimbStateSnapshot {id=$"t{t}-{k}",stage=ClimbSnapshotStages.Level1Pull,trajectory="t"+t});
        var library=ClimbSnapshotLibrary.FromSnapshots(snapshots);int fromShort=0;
        for(int i=0;i<4000;i++)if(library.Sample(ClimbSnapshotStages.Level1Pull,random).trajectory=="t1")fromShort++;
        Assert.AreEqual(.5f,fromShort/4000f,.03f,"trajectory-balanced");
        library.Quarantine("t1-0","test");for(int i=0;i<200;i++)Assert.AreEqual("t0",library.Sample(ClimbSnapshotStages.Level1Pull,random).trajectory);
        Assert.IsNull(library.Sample(ClimbSnapshotStages.Level1To2,random));
    }

    // 5.4: stage snapshots from expert trajectories; restored continuation vs the original continuation.
    struct Frame
    {
        public Vector3 torso,left,right,velocity;public string events;public int episodes;
        public static Frame Of(ClimberAgent a)
        {
            var body=Body(a);var b=body.GetBodyState();var l=body.GetHandState(HandId.Left);var r=body.GetHandState(HandId.Right);
            return new Frame {torso=b.Position,left=l.Position,right=r.Position,velocity=b.Velocity,episodes=a.CompletedEpisodes,
                events=$"{l.Grabbed}{r.Grabbed}{l.NeedsRearm}{r.NeedsRearm}{l.Cooldown>0}{r.Cooldown>0}{a.Level1}{a.Level2}"};
        }
        public float Distance(Frame o) => Mathf.Max(Vector3.Distance(torso,o.torso),Mathf.Max(Vector3.Distance(left,o.left),Vector3.Distance(right,o.right)));
    }
    static object CopyExpert(BoxClimbSmokeController expert)
    {
        var copy=new Dictionary<FieldInfo,object>();
        foreach(var f in typeof(BoxClimbSmokeController).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
            if(f.Name!="body"){var v=f.GetValue(expert);copy[f]=v is Array array?array.Clone():v;}
        return copy;
    }
    static void PasteExpert(BoxClimbSmokeController expert,object state)
    {foreach(var pair in (Dictionary<FieldInfo,object>)state)pair.Key.SetValue(expert,pair.Value is Array array?array.Clone():pair.Value);}
    BoxClimbSmokeController Expert(ClimberAgent a)
    {
        foreach(var old in a.GetComponents<BoxClimbSmokeController>())Object.DestroyImmediate(old);
        var expert=a.gameObject.AddComponent<BoxClimbSmokeController>();expert.Configure(Body(a),true);return expert;
    }
    sealed class Recorded {public SnapshotCandidate candidate;public List<Frame> reference;public int trajectory;}
    List<Frame> Continue(ClimberAgent a,ClimbStateSnapshot s,object expertState,int steps)
    {
        a.RestoreState(s);var expert=Expert(a);PasteExpert(expert,expertState);
        var frames=new List<Frame>();int episodes=a.CompletedEpisodes;
        for(int k=0;k<steps&&a.CompletedEpisodes==episodes;k++){frames.Add(Frame.Of(a));Step(a,expert);}
        return frames;
    }
    static (float error,int firstEventMismatch) Divergence(List<Frame> x,List<Frame> y,int steps)
    {
        float error=0;int mismatch=-1;int n=Mathf.Min(steps,Mathf.Min(x.Count,y.Count));
        for(int k=0;k<n;k++){error=Mathf.Max(error,x[k].Distance(y[k]));if(mismatch<0&&x[k].events!=y[k].events)mismatch=k;}
        return (error,mismatch);
    }
    static float VelocityError(List<Frame> x,List<Frame> y,int steps)
    {float error=0;for(int k=0;k<Mathf.Min(steps,Mathf.Min(x.Count,y.Count));k++)error=Mathf.Max(error,Vector3.Distance(x[k].velocity,y[k].velocity));return error;}

    [Test] public void StageSnapshotsContinueConsistently()
    {
        // About 1.5 S1a labels per expert trajectory (the recorder yields 186/128), so 10 trajectories for >=10 per stage.
        const int Short=50,Long=250,PerStage=10,Trajectories=10;
        var a=Create(Vector3.zero,Quaternion.identity,true,true);var b=Create(new Vector3(42,0,28),Quaternion.Euler(0,90,0),true,true);
        var spawn=a.Spawn;var recorded=new List<Recorded>();var starts=new List<BodyReset>();
        for(int t=0;t<Trajectories;t++)
        {
            var random=new System.Random(2026+t);float J(float range)=>t==0?0:(float)(random.NextDouble()*2-1)*range;
            var start=new BodyReset {Torso=spawn.Torso+new Vector3(J(.02f),0,J(.02f)),LeftHand=spawn.LeftHand+new Vector3(J(.02f),J(.02f),J(.02f)),RightHand=spawn.RightHand+new Vector3(J(.02f),J(.02f),J(.02f)),Rotation=spawn.Rotation};
            starts.Add(start);
            a.OnEpisodeBegin();Body(a).ResetBody(start);Physics.SyncTransforms();var expert=Expert(a);
            var tracker=new ClimbTrajectoryTracker();var frames=new List<Frame>();int episodes=a.CompletedEpisodes;
            for(int k=0;k<3000&&a.CompletedEpisodes==episodes;k++)
            {
                frames.Add(Frame.Of(a));
                var c=tracker.Observe(a,k%5==0,$"t{t}-{k}","t"+t,"test");
                if(c!=null){c.step=k;c.expert=CopyExpert(expert);}
                Step(a,expert);
            }
            foreach(var c in tracker.Label())recorded.Add(new Recorded {candidate=c,reference=frames.Skip(c.step).ToList(),trajectory=t});
        }
        var report=new List<string>();int longFailures=0,total=0;
        foreach(string stage in ClimbSnapshotStages.All)
        {
            var list=recorded.Where(r=>r.candidate.state.stage==stage).ToList();
            Assert.GreaterOrEqual(list.Count,PerStage,"labeled snapshots for "+stage);
            var chosen=Enumerable.Range(0,PerStage).Select(i=>list[i*list.Count/PerStage]).ToList();
            foreach(var r in chosen)
            {
                var s=r.candidate.state;total++;
                var restored=Continue(b,s,r.candidate.expert,Long);
                var perturbed=JsonUtility.FromJson<ClimbStateSnapshot>(JsonUtility.ToJson(s));perturbed.body.torsoPosition+=new Vector3(1e-6f,0,0);
                var eps=Continue(b,perturbed,r.candidate.expert,Long);
                var shortRes=Divergence(r.reference,restored,Short);
                Assert.LessOrEqual(shortRes.error,.001f,$"{s.id} short position");Assert.LessOrEqual(VelocityError(r.reference,restored,Short),.01f,$"{s.id} short velocity");
                Assert.AreEqual(-1,shortRes.firstEventMismatch,$"{s.id} short discrete events");
                var longRes=Divergence(r.reference,restored,Long);var longEps=Divergence(r.reference,eps,Long);
                bool sameOutcome=restored.Count>0&&r.reference.Count>0&&restored[Mathf.Min(restored.Count,r.reference.Count)-1].events==r.reference[Mathf.Min(restored.Count,r.reference.Count)-1].events;
                bool ok=longRes.error<=10*Mathf.Max(longEps.error,.001f)&&sameOutcome;if(!ok)longFailures++;
                report.Add($"{s.id} {stage} long={longRes.error:E2} eps={longEps.error:E2} event@{longRes.firstEventMismatch} outcome={sameOutcome}");
            }
        }
        // Solver-cache baseline on a subset: rerun the reference from its start, restore onto itself at the snapshot step.
        foreach(var r in ClimbSnapshotStages.All.SelectMany(stage=>recorded.Where(x=>x.candidate.state.stage==stage).Take(2)))
        {
            a.OnEpisodeBegin();Body(a).ResetBody(starts[r.trajectory]);Physics.SyncTransforms();var expert=Expert(a);
            for(int k=0;k<r.candidate.step;k++)Step(a,expert);
            float before=Frame.Of(a).Distance(r.reference[0]);
            var self=a.CaptureState("noop");var expertState=CopyExpert(expert);
            var noop=Continue(a,self,expertState,Long);
            report.Add($"noop {r.candidate.state.id} rerun={before:E2} long={Divergence(r.reference,noop,Long).error:E2}");
        }
        Debug.Log("CLIMB_SNAPSHOT_CONSISTENCY\n"+string.Join("\n",report));
        Assert.LessOrEqual(longFailures,(int)Math.Floor(total*.05),"long-horizon consistency\n"+string.Join("\n",report));
    }
}
