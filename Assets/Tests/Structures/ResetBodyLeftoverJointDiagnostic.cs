using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Climber.Layer2Prep;
using PhysicalAI.Structures;
using UnityEngine;
using UnityEngine.TestTools;

// Diagnostic only (LAYER3_SNAPSHOT_DESIGN.md §8.4-8.5, LAYER3_RESETBODY_DIAGNOSTIC.md): ResetBody releases grips with breakForce=0 and a deferred Destroy, so the old FixedJoint
// still exists in the physics step that immediately follows the reset. Conditions per site:
//   gripped       reset while a hand holds (the Agent's case when an episode ends while gripping);
//   releasedFirst SetGrab(false) on both hands, wait for the next frame (joints destroyed), then reset from the same place;
//   free          reset before any grip.
// Records torso/hand displacement from the reset pose and velocity over the first 10 steps, and whether each hand has an Allowed grip
// candidate (a read-only query). Explicit: excluded from the regular suite. Writes results/resetbody-joint-diagnostic/; changes no body,
// physics or controller code.
public sealed class ResetBodyLeftoverJointDiagnostic
{
    public const int PerCondition=50,TrackSteps=10,StepsPerFrame=5;
    public static readonly string[] Conditions={"gripped","releasedFirst","free"};
    readonly List<GameObject> roots=new List<GameObject>();
    float scale,capture;SimulationMode simulation;
    [SetUp] public void Setup(){scale=Time.timeScale;capture=Time.captureDeltaTime;simulation=Physics.simulationMode;}
    [TearDown] public void Cleanup()
    {
        foreach(var root in roots)if(root!=null)Object.DestroyImmediate(root);roots.Clear();
        Time.timeScale=scale;Time.captureDeltaTime=capture;Physics.simulationMode=simulation;
    }
    [UnityTest,Explicit("Diagnostic; run by name")]
    public IEnumerator Reset200TimesAcrossSites()
    {
        // The command-line runner executes [Explicit] tests when whole assemblies are selected; require an opt-in as well.
        if(System.Environment.GetEnvironmentVariable("CLIMB_RUN_DIAGNOSTICS")!="1")Assert.Ignore("Set CLIMB_RUN_DIAGNOSTICS=1 to run this diagnostic");
        // Several physics steps per frame, as in training (time_scale 100), so the deferred Destroy lands at a frame end as it does there.
        Physics.simulationMode=SimulationMode.FixedUpdate;Time.timeScale=1;Time.captureDeltaTime=StepsPerFrame*Time.fixedDeltaTime;
        var probes=new List<ResetJointProbe>();
        void Add(string name,ResetJointProbe.Kind kind,StructureDocument doc,float yaw,float h=0,float g=0)
        {
            var body=StaticStructureLoader.Load(doc,new Vector3(400+probes.Count*20,0,400),Quaternion.Euler(0,yaw,0));body.name=name;roots.Add(body.gameObject);
            var start=new BodyReset {Torso=doc.start.torso.Position,LeftHand=doc.start.leftHand.Position,RightHand=doc.start.rightHand.Position,Rotation=doc.start.torso.Rotation};
            var probe=body.gameObject.AddComponent<ResetJointProbe>();probe.Configure(kind,body,start,h,g,1000+probes.Count);probes.Add(probe);
        }
        Add("TwoStep_yaw0",ResetJointProbe.Kind.TwoStep,BoxStackGenerator.Smoke(true),0);
        Add("TwoStep_yaw15",ResetJointProbe.Kind.TwoStep,BoxStackGenerator.Smoke(true),15);
        Add("SingleBox_yaw-15",ResetJointProbe.Kind.SingleBox,BoxStackGenerator.Smoke(false),-15);
        Add("RungWall_h0.30_s0.50",ResetJointProbe.Kind.RungWall,StaticWallGenerator.GenerateAt(.30f,.50f),0,.30f,.50f);
        float deadline=Time.realtimeSinceStartup+1200;
        while(probes.Any(p=>!p.Done)&&Time.realtimeSinceStartup<deadline)yield return null;
        var rows=probes.SelectMany(p=>p.Rows).ToList();
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","results","resetbody-joint-diagnostic"));Directory.CreateDirectory(folder);
        var csv=new StringBuilder(ResetJointProbe.Row.Header+"\n");foreach(var r in rows)csv.Append(r.Csv()).Append('\n');
        File.WriteAllText(Path.Combine(folder,"trials.csv"),csv.ToString());
        foreach(var p in probes)Debug.Log($"RESET_DIAG_SITE {p.name} "+string.Join(" ",Conditions.Select(c=>c+"="+p.Count(c)))+$" restarts={p.Restarts}");
        Debug.Log($"RESET_DIAG_COMPLETE rows={rows.Count} fixedDeltaTime={Time.fixedDeltaTime} stepsPerFrame={StepsPerFrame} folder={folder}");
        Assert.IsTrue(probes.All(p=>p.Done),"Diagnostic did not collect every reset before the deadline");
    }
}

// Runs at the Academy clock's order (-200), where ClimberAgent.OnEpisodeBegin calls ResetBody in training, before the body's own step.
[DefaultExecutionOrder(-200)]
public sealed class ResetJointProbe : MonoBehaviour
{
    public enum Kind {TwoStep,SingleBox,RungWall}
    public sealed class Row
    {
        public static readonly string Header="site,trial,condition,handsGrabbed,jointsAtReset,"+
            string.Join(",",Enumerable.Range(1,ResetBodyLeftoverJointDiagnostic.TrackSteps).Select(k=>$"torsoDisp{k},leftDisp{k},rightDisp{k}"))+
            ",torsoSpeed1,leftSpeed1,rightSpeed1,leftAllowedSteps,rightAllowedSteps,heldLeft,heldRight";
        public string Site,Condition;public int Trial,HandsGrabbed,JointsAtReset,LeftAllowed,RightAllowed;public bool HeldLeft,HeldRight;
        public readonly List<float> Disp=new List<float>();public float TorsoSpeed1,LeftSpeed1,RightSpeed1;
        public string Csv()
        {
            string F(float v)=>v.ToString("R",CultureInfo.InvariantCulture);
            return string.Join(",",new[] {Site,Trial.ToString(),Condition,HandsGrabbed.ToString(),JointsAtReset.ToString()}.Concat(Disp.Select(F))
                .Concat(new[] {F(TorsoSpeed1),F(LeftSpeed1),F(RightSpeed1),LeftAllowed.ToString(),RightAllowed.ToString(),HeldLeft?"1":"0",HeldRight?"1":"0"}));
        }
    }
    public readonly List<Row> Rows=new List<Row>();
    public int Restarts;
    public int Count(string condition) => Rows.Count(r=>r.Condition==condition);
    public bool Done => ResetBodyLeftoverJointDiagnostic.Conditions.All(c=>Count(c)>=ResetBodyLeftoverJointDiagnostic.PerCondition);
    Kind kind;ClimberBody body;BodyReset start;float rungHeight,rungGap;System.Random random;
    BoxClimbSmokeController box;PrepWall wall;PrepClimbController rung;Rigidbody torso,left,right;
    int ticks,climbSteps,waitAfter,waited,releaseFrame=-1,tracked;string plan;bool stopped,controlling;
    Row pending;Vector3 torso0,left0,right0;
    public void Configure(Kind value,ClimberBody climber,BodyReset spawn,float h,float g,int seed)
    {
        kind=value;body=climber;start=spawn;rungHeight=h;rungGap=g;random=new System.Random(seed);
        foreach(var r in body.GetComponentsInChildren<Rigidbody>())
        {if(r.name=="Torso")torso=r;else if(r.name=="LeftHand")left=r;else if(r.name=="RightHand")right=r;}
        Begin();
    }
    void Begin()
    {
        // Unmeasured episode start from a free state; the existing scripted controller climbs until the planned reset moment.
        if(box!=null){box.enabled=false;Destroy(box);box=null;}
        if(kind==Kind.RungWall){if(wall==null)wall=new PrepWall(body,rungHeight,rungGap);wall.Reset(-.01f);rung=new PrepClimbController(wall);}
        else {body.ResetBody(start);box=gameObject.AddComponent<BoxClimbSmokeController>();box.Configure(body,kind==Kind.TwoStep);}
        Physics.SyncTransforms();ticks=climbSteps=waited=0;releaseFrame=-1;controlling=true;
        var open=ResetBodyLeftoverJointDiagnostic.Conditions.Where(c=>Count(c)<ResetBodyLeftoverJointDiagnostic.PerCondition).ToArray();
        plan=open.Length==0?null:open[random.Next(open.Length)];
        waitAfter=plan=="free"?random.Next(5,40):random.Next(0,60);
    }
    int Joints() => body.GetComponentsInChildren<FixedJoint>(true).Count(j=>j!=null);
    void FixedUpdate()
    {
        if(pending!=null){Track();return;}
        if(stopped)return;
        if(plan==null){stopped=true;return;}
        if(controlling&&kind==Kind.RungWall&&!rung.Done&&++ticks%5==0)rung.Step(Time.fixedDeltaTime*5);
        climbSteps++;var state=body.GetBodyState();bool gripping=state.GrabbedHands>0;
        bool controllerDone=kind==Kind.RungWall?rung.Done:box.Completed||box.Failed;
        if(releaseFrame>=0){if(Time.frameCount!=releaseFrame)ResetNow(0);return;}
        if(plan=="free")
        {
            if(gripping){Restarts++;Begin();}else if(climbSteps>=waitAfter)ResetNow(0);
            return;
        }
        if(gripping)waited++;
        if(gripping&&waited>=waitAfter)
        {
            if(plan=="gripped")ResetNow(state.GrabbedHands);
            else
            {
                // Release now; the deferred Destroy of both grip joints completes at this frame's end, before the reset.
                controlling=false;if(box!=null)box.enabled=false;
                body.SetGrab(HandId.Left,false);body.SetGrab(HandId.Right,false);releaseFrame=Time.frameCount;
            }
        }
        else if((controllerDone&&!gripping)||climbSteps>4000){Restarts++;Begin();}
    }
    void ResetNow(int grabbed)
    {
        // Same call and order as the Agent; the controller is disabled so nothing commands the body afterwards.
        controlling=false;if(box!=null)box.enabled=false;
        pending=new Row {Site=name,Condition=plan,Trial=Rows.Count,HandsGrabbed=grabbed,JointsAtReset=Joints()};
        body.ResetBody(start);Physics.SyncTransforms();
        torso0=torso.position;left0=left.position;right0=right.position;tracked=0;
    }
    void Track()
    {
        // Called once per physics step after the reset step's simulation (before the body's own step).
        tracked++;pending.Disp.Add(Vector3.Distance(torso.position,torso0));pending.Disp.Add(Vector3.Distance(left.position,left0));pending.Disp.Add(Vector3.Distance(right.position,right0));
        if(tracked==1){pending.TorsoSpeed1=torso.linearVelocity.magnitude;pending.LeftSpeed1=left.linearVelocity.magnitude;pending.RightSpeed1=right.linearVelocity.magnitude;}
        if(body.GetGripCandidate(HandId.Left).Allowed)pending.LeftAllowed++;
        if(body.GetGripCandidate(HandId.Right).Allowed)pending.RightAllowed++;
        pending.HeldLeft|=body.GetHandState(HandId.Left).Grabbed;pending.HeldRight|=body.GetHandState(HandId.Right).Grabbed;
        if(tracked<ResetBodyLeftoverJointDiagnostic.TrackSteps)return;
        Rows.Add(pending);pending=null;Begin();
    }
}
