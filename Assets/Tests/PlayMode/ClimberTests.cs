using System;
using System.Collections.Generic;
using System.Collections;
using NUnit.Framework;
using PhysicalAI.Climber;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public sealed class ClimberTests
{
    readonly List<ClimberBody> bodies=new List<ClimberBody>();
    readonly List<GameObject> extras=new List<GameObject>();
    PhysicsMaterial material;
    SimulationMode previous;
    float previousTimeScale;
    [SetUp] public void Setup()
    {
        previous=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;
        previousTimeScale=Time.timeScale;
        material=new PhysicsMaterial("TestExplicit") {staticFriction=.15f,dynamicFriction=.15f,frictionCombine=PhysicsMaterialCombine.Average,bounciness=0};
    }
    [TearDown] public void Cleanup()
    {
        foreach(var body in bodies) if(body!=null) UnityEngine.Object.DestroyImmediate(body.gameObject);
        bodies.Clear();foreach(var go in extras)if(go!=null)UnityEngine.Object.DestroyImmediate(go);extras.Clear();
        UnityEngine.Object.DestroyImmediate(material);Physics.simulationMode=previous;Time.timeScale=previousTimeScale;
    }
    ClimberBody Create(int index=0,bool controller=false)
    {
        var b=ClimberArenaFactory.Create("TestArena_"+index,new Vector3(100+(index%4)*5,0,100+(index/4)*5),material,controller);
        b.enabled=false;bodies.Add(b);
        var c=b.GetComponent<ClimberScriptController>();if(c!=null)c.enabled=false;
        Physics.SyncTransforms();return b;
    }
    void Step(int count=1,bool controllers=false)
    {
        for(int step=0;step<count;step++)
        {
            if(controllers)foreach(var b in bodies)if(b.gameObject.activeInHierarchy)b.GetComponent<ClimberScriptController>().Step(.02f);
            foreach(var b in bodies)if(b.gameObject.activeInHierarchy)b.PhysicsStep(.02f);
            Physics.Simulate(.02f);Physics.SyncTransforms();
        }
    }
    void Reset(ClimberBody b,Vector3 torso,Vector3 left,Vector3 right)
    {
        b.ResetBody(new BodyReset {Torso=torso,LeftHand=left,RightHand=right,Rotation=Quaternion.identity});Physics.SyncTransforms();
    }
    void HangFixture(ClimberBody b,float angle,float length=.67f)
    {
        Vector3 torso=new Vector3(0,3.15f,-.4f);
        Vector3 shoulder=torso+new Vector3(-.3f,.28f,0);
        Vector3 delta=new Vector3(Mathf.Sin(angle*Mathf.Deg2Rad),Mathf.Cos(angle*Mathf.Deg2Rad),0)*length;
        Vector3 left=shoulder+delta;
        ClimberArenaFactory.Box("Fixture",b.transform,left-Vector3.up*.15f,new Vector3(.3f,.1f,.3f),material,true);
        Reset(b,torso,left,torso+new Vector3(.3f,.63f,0));
        b.SetGrab(HandId.Left,true);Step();Assert.IsTrue(b.GetHandState(HandId.Left).Grabbed,"Fixture grip");
        b.SetHandTarget(HandId.Right,new Vector3(0,.35f,0),1);
    }
    [Test] public void GeometryMaterialsAndRadialLimit()
    {
        var b=Create();Assert.AreEqual(10,b.Torso.mass);Assert.AreEqual(1,b.Hand(0).mass);
        foreach(var c in b.GetComponentsInChildren<Collider>())Assert.NotNull(c.sharedMaterial,c.name);
        Assert.IsTrue(float.IsPositiveInfinity(b.Arm(0).breakForce));
        b.Torso.isKinematic=true;
        foreach(var c in b.GetComponentsInChildren<Collider>())if(c.attachedRigidbody==null)c.enabled=false;
        foreach(var direction in new[]{Vector3.right,Vector3.up,new Vector3(1,1,1).normalized})
        {
            var s=b.GetHandState(HandId.Left);
            b.SetHandTarget(HandId.Left,direction*10,1);
            for(int step=0;step<100;step++) {b.Hand(0).AddForce(direction*1000);Step();}
            Debug.Log($"LAYER1_LIMIT direction={direction} measured={b.GetHandState(HandId.Left).ArmLength:F5}");
            Assert.LessOrEqual(b.GetHandState(HandId.Left).ArmLength,.92f,"Radial distance "+direction);
            Assert.Greater(b.GetHandState(HandId.Left).ArmLength,.88f,"Limit must actually be loaded");
        }
        Reset(b,new Vector3(0,.4f,-.4f),new Vector3(-.3f,.95f,-.05f),new Vector3(.3f,.95f,-.05f));
        Assert.Less(b.GetBodyState().Velocity.magnitude,1e-5f);
        Assert.Less(b.GetHandState(HandId.Left).Velocity.magnitude,1e-5f);
        Assert.AreEqual(Vector3.zero,b.Hand(0).angularVelocity);
    }
    [TestCase(0f)] [TestCase(45f)] public void LiftThresholdSweep(float angle)
    {
        float finalHigh=0;
        foreach(float u in new[]{.1f,.25f,.35f,.5f,.7f})
        {
            var b=Create(bodies.Count);
            HangFixture(b,angle);
            var h=b.GetHandState(HandId.Left);
            Vector3 direction=(h.Position-h.ShoulderPosition).normalized;
            b.SetHandTarget(HandId.Left,direction*.18f,u);
            float initial=b.GetBodyState().Position.y;float maxForce=0,maxLength=0;
            for(int i=0;i<25;i++) {Step();maxForce=Mathf.Max(maxForce,b.GetHandState(HandId.Left).CommandForce.magnitude);maxLength=Mathf.Max(maxLength,b.GetHandState(HandId.Left).ArmLength);}
            float rise=b.GetBodyState().Position.y-initial;
            Debug.Log($"LAYER1_LIFT angle={angle} u={u:F2} rise={rise:F4} maxForceN={maxForce:F2} arm={maxLength:F4}");
            Assert.LessOrEqual(maxForce,u*450+.02f);Assert.Less(maxLength,.92f);
            if(u==.1f) Assert.Less(rise,.02f,"Subthreshold must not lift");
            if(u==.7f) finalHigh=rise;
            // Keep prior fixtures from participating in later sweeps.
            b.gameObject.SetActive(false);
        }
        Assert.Greater(finalHigh,.08f,"u=.7 must lift, not just support");
    }
    [Test] public void SingleHandHoldAndFiveSecondFreeHandJitter()
    {
        var b=Create();HangFixture(b,0,.89f);
        b.SetHandTarget(HandId.Left,new Vector3(0,.85f,0),0); // legal passive straight-arm suspension
        b.SetHandTarget(HandId.Right,new Vector3(0,.35f,0),1);
        Step(150);
        bool slept=MeasureJitter(b,"natural",false);
        if(slept)
        {
            Debug.Log("LAYER1_JITTER_INVALID natural phase included sleeping bodies; superseded by keep-awake measurement.");
            MeasureJitter(b,"keepAwake",true);
        }
        var before=b.GetHandState(HandId.Right).Position;
        b.SetHandTarget(HandId.Right,new Vector3(.15f,.55f,0),1);Step(100);
        Assert.Greater(Vector3.Distance(before,b.GetHandState(HandId.Right).Position),.1f);
        Assert.IsTrue(b.GetHandState(HandId.Left).Grabbed);
    }
    bool MeasureJitter(ClimberBody b,string phase,bool keepAwake)
    {
        var rigidbodies=new[]{b.Hand(0),b.Hand(1),b.Torso};
        var thresholds=new float[3];
        for(int i=0;i<3;i++) {thresholds[i]=rigidbodies[i].sleepThreshold;if(keepAwake){rigidbodies[i].sleepThreshold=0;rigidbodies[i].WakeUp();}}
        var left0=b.GetHandState(HandId.Left).Position;var right0=b.GetHandState(HandId.Right).Position;var torso0=b.GetBodyState().Position;
        Vector3 relative0=right0-b.GetHandState(HandId.Right).ShoulderPosition;
        Vector3 min=Vector3.one*float.PositiveInfinity,max=Vector3.one*float.NegativeInfinity;
        Vector3 worldMin=min,worldMax=max;
        Vector3 forceMin=min,forceMax=max;
        float magnitudeMin=float.PositiveInfinity,magnitudeMax=float.NegativeInfinity;
        int sleepingSamples=0;
        try
        {
            for(int i=0;i<250;i++)
            {
                if(keepAwake)foreach(var rb in rigidbodies)rb.WakeUp();
                Step();var left=b.GetHandState(HandId.Left);var h=b.GetHandState(HandId.Right);var trunk=b.GetBodyState();
                Vector3 p=h.Position-h.ShoulderPosition;
                bool sl=rigidbodies[0].IsSleeping(),sr=rigidbodies[1].IsSleeping(),st=rigidbodies[2].IsSleeping();
                if(sl||sr||st)sleepingSamples++;
                min=Vector3.Min(min,p);max=Vector3.Max(max,p);
                worldMin=Vector3.Min(worldMin,h.Position);worldMax=Vector3.Max(worldMax,h.Position);
                forceMin=Vector3.Min(forceMin,h.CommandForce);forceMax=Vector3.Max(forceMax,h.CommandForce);
                magnitudeMin=Mathf.Min(magnitudeMin,h.CommandForce.magnitude);magnitudeMax=Mathf.Max(magnitudeMax,h.CommandForce.magnitude);
                Debug.Log($"LAYER1_JITTER_SAMPLE phase={phase} t={(i+1)*.02f:F2} sleepL={sl} sleepR={sr} sleepT={st} L_delta_mm={((left.Position-left0)*1000).ToString("F4")} R_delta_mm={((h.Position-right0)*1000).ToString("F4")} T_delta_mm={((trunk.Position-torso0)*1000).ToString("F4")} R_relative_delta_mm={((p-relative0)*1000).ToString("F4")} R_PD_N={h.CommandForce.ToString("F6")}");
                Assert.IsTrue(left.Grabbed);Assert.IsFalse(h.Grabbed,"Measured hand must remain free");Assert.Less(left.ArmLength,.92f);
            }
            Debug.Log($"LAYER1_JITTER_SUMMARY phase={phase} seconds=5 samples=250 sleepingSamples={sleepingSamples} valid={sleepingSamples==0} relativeP2P_mm={((max-min)*1000).ToString("F4")} arenaP2P_mm={((worldMax-worldMin)*1000).ToString("F4")} relativeEnvelope_mm={(max-min).magnitude*1000:F4} PD_vectorP2P_N={(forceMax-forceMin).ToString("F6")} PD_magnitude_min_N={magnitudeMin:F6} PD_magnitude_max_N={magnitudeMax:F6} PD_magnitudeP2P_N={magnitudeMax-magnitudeMin:F6} peakConstraint_N={b.GetHandState(HandId.Left).PeakConstraintLoad:F4}");
            if(keepAwake)Assert.AreEqual(0,sleepingSamples,"Keep-awake measurement must have no sleeping sample");
            if(sleepingSamples==0)Assert.Less((max-min).magnitude,.02f);
        }
        finally {for(int i=0;i<3;i++)rigidbodies[i].sleepThreshold=thresholds[i];}
        return sleepingSamples>0;
    }
    [Test] public void StaticTopEdgeSideLayerArenaAndRotatedBox()
    {
        var b=Create();HangFixture(b,0);
        var surface=b.transform.Find("Fixture").GetComponent<ClimberSurface>();var box=surface.GetComponent<BoxCollider>();
        Assert.IsTrue(b.CheckCandidate(0,box,surface).Allowed);
        var h=b.GetHandState(HandId.Left);
        Reset(b,new Vector3(0,3.15f,-.4f),h.Position+new Vector3(.23f,-.04f,0),new Vector3(.3f,3.8f,-.4f));
        Assert.IsTrue(b.CheckCandidate(0,box,surface).Allowed,"Geometric top edge");
        Reset(b,new Vector3(0,3.15f,-.4f),h.Position+new Vector3(.24f,-.15f,0),new Vector3(.3f,3.8f,-.4f));
        Assert.IsFalse(b.CheckCandidate(0,box,surface).Allowed,"Vertical side");
        Reset(b,new Vector3(0,3.15f,-.4f),h.Position-Vector3.up*.3f,new Vector3(.3f,3.8f,-.4f));
        Assert.IsFalse(b.CheckCandidate(0,box,surface).Allowed,"Underside");
        Reset(b,new Vector3(0,3.15f,-.4f),h.Position,new Vector3(.3f,3.8f,-.4f));
        box.gameObject.layer=0;Assert.AreEqual("Layer",b.CheckCandidate(0,box,surface).Reason);box.gameObject.layer=8;
        surface.Arena=null;Assert.AreEqual("OtherArena",b.CheckCandidate(0,box,surface).Reason);surface.Arena=b.transform;
        b.Hand(0).linearVelocity=Vector3.up*2;Assert.AreEqual("TooFast",b.CheckCandidate(0,box,surface).Reason);b.Hand(0).linearVelocity=Vector3.zero;
        box.transform.localRotation=Quaternion.Euler(0,0,90);Physics.SyncTransforms();
        Vector3 point,normal;ClimberSurface.TopPoint(box,box.transform.position+Vector3.up,Vector3.up,out point,out normal);
        Assert.Greater(Vector3.Dot(normal,Vector3.up),.99f);
        Vector3 local=b.transform.InverseTransformPoint(point+normal*.1f);
        Reset(b,new Vector3(0,3.15f,-.4f),local,new Vector3(.3f,3.8f,-.4f));
        Assert.IsTrue(b.CheckCandidate(0,box,surface).Allowed,"New top after tipping");
    }
    [Test] public void GrabCooldownAndFiniteGripStrength()
    {
        var b=Create();HangFixture(b,0);
        Assert.AreEqual(900,b.Grip(0).breakForce);Assert.AreEqual(90,b.Grip(0).breakTorque);
        b.SetGrab(HandId.Left,false);b.SetGrab(HandId.Left,true);Step(4);
        Assert.IsFalse(b.GetHandState(HandId.Left).Grabbed);Assert.Greater(b.GetHandState(HandId.Left).Cooldown,0);
    }
    [Test] public void GripBreaksButShoulderDoesNot()
    {
        var b=Create();HangFixture(b,0);
        b.Hand(0).AddForce(Vector3.down*100,ForceMode.Impulse);Step(2);
        Assert.IsFalse(b.GetHandState(HandId.Left).Grabbed,"900 N grip must break under impact");
        Assert.NotNull(b.Arm(0));Assert.IsTrue(float.IsPositiveInfinity(b.Arm(0).breakForce));
        Step(20);Assert.IsFalse(b.GetHandState(HandId.Left).Grabbed,"No automatic rearming after break");
        Debug.Log("LAYER1_BREAK gripBroken=True shoulderIntact=True regrab=False");
    }
    [Test] public void BothHandsPressOverTopEdge()
    {
        var b=Create();Reset(b,new Vector3(0,1.5f,-.4f),new Vector3(-.3f,2.1f,.02f),new Vector3(.3f,2.1f,.02f));
        b.SetGrab(HandId.Left,true);b.SetGrab(HandId.Right,true);Step();
        Assert.IsTrue(b.GetHandState(HandId.Left).Grabbed);Assert.IsTrue(b.GetHandState(HandId.Right).Grabbed);
        var controller=b.gameObject.AddComponent<ClimberScriptController>();controller.Configure(b,b.GetComponent<ClimberGoal>());controller.enabled=false;controller.BeginTopPress();
        var goal=b.GetComponent<ClimberGoal>();var region=new Bounds(goal.Center,goal.Size);
        Assert.IsFalse(goal.Contains(new BodyState {Position=new Vector3(0,1.75f,-.18f)}),"Highest rung is not arrival");
        Assert.IsFalse(goal.Contains(new BodyState {Position=new Vector3(0,2.5f,-.01f)}),"High but outside platform edge is not arrival");
        bool entered=false;float firstHeight=0;var minimumForce=new[]{float.PositiveInfinity,float.PositiveInfinity};var maximumForce=new float[2];
        var minimumEffort=new[]{float.PositiveInfinity,float.PositiveInfinity};var maximumEffort=new float[2];
        Debug.Log($"LAYER1_PRESS_REGION center={goal.Center.ToString("F4")} size={goal.Size.ToString("F4")} topSurfaceY=2.0000 frontEdgeZ=0.0000");
        for(int i=0;i<1000 && !controller.Completed;i++)
        {
            Step(1,true);var state=b.GetBodyState();Assert.AreEqual(2,state.GrabbedHands);
            var left=b.GetHandState(HandId.Left);var right=b.GetHandState(HandId.Right);
            var hs=new[]{left,right};
            for(int h=0;h<2;h++){minimumForce[h]=Mathf.Min(minimumForce[h],hs[h].CommandForce.magnitude);maximumForce[h]=Mathf.Max(maximumForce[h],hs[h].CommandForce.magnitude);minimumEffort[h]=Mathf.Min(minimumEffort[h],hs[h].Effort);maximumEffort[h]=Mathf.Max(maximumEffort[h],hs[h].Effort);}
            Debug.Log($"LAYER1_PRESS_SAMPLE t={(i+1)*.02f:F2} torso={state.Position.ToString("F4")} speed={state.Velocity.magnitude:F4} effortL={left.Effort:F4} effortR={right.Effort:F4} forceL_N={left.CommandForce.ToString("F4")} forceR_N={right.CommandForce.ToString("F4")} normL_N={left.CommandForce.magnitude:F4} normR_N={right.CommandForce.magnitude:F4} geometricInside={region.Contains(state.Position)} validGoal={goal.Contains(state)}");
            if(!entered && region.Contains(state.Position))
            {
                entered=true;firstHeight=state.Position.y;
                Bounds trunk=b.Torso.GetComponent<Collider>().bounds;
                var min=b.transform.InverseTransformPoint(trunk.min);
                Debug.Log($"LAYER1_PRESS_FIRST_ENTRY t={(i+1)*.02f:F2} torso={state.Position.ToString("F6")} minTorsoY={min.y:F6} minTorsoZ={min.z:F6} effortL={left.Effort:F4} effortR={right.Effort:F4} forceL_N={left.CommandForce.ToString("F4")} forceR_N={right.CommandForce.ToString("F4")}");
                Assert.GreaterOrEqual(min.y,2f,"Torso bottom must clear top surface in this fixture");
                Assert.Greater(min.z,0f,"Torso must physically pass platform front edge in this fixture");
            }
        }
        var final=b.GetBodyState();var fl=b.GetHandState(HandId.Left);var fr=b.GetHandState(HandId.Right);
        Debug.Log($"LAYER1_PRESS_SUMMARY firstEntryY={firstHeight:F6} finalPosition={final.Position.ToString("F6")} finalSpeed={final.Velocity.magnitude:F6} effortL_range={minimumEffort[0]:F4},{maximumEffort[0]:F4} effortR_range={minimumEffort[1]:F4},{maximumEffort[1]:F4} forceL_norm_range_N={minimumForce[0]:F4},{maximumForce[0]:F4} forceR_norm_range_N={minimumForce[1]:F4},{maximumForce[1]:F4} finalEffortL={fl.Effort:F4} finalEffortR={fr.Effort:F4} finalForceL_N={fl.CommandForce.ToString("F4")} finalForceR_N={fr.CommandForce.ToString("F4")}");
        Assert.IsTrue(entered);
        Debug.Log($"LAYER1_PRESS pos={b.GetBodyState().Position} stage={controller.Stage}");
        Assert.IsTrue(controller.Completed,"Press-up goal");
    }
    [TestCase(1,false)] [TestCase(16,false)] [TestCase(16,true)] public void ScriptedHandoversAndArrival(int count,bool rotateArenas)
    {
        for(int i=0;i<count;i++)
        {
            var b=Create(i,true);
            if(rotateArenas)b.transform.rotation=Quaternion.Euler(0,i%2==0?30:-30,0);
        }
        Physics.SyncTransforms();
        for(int step=0;step<2500;step++)
        {
            Step(1,true);bool all=true;
            foreach(var b in bodies) all &= b.GetComponent<ClimberScriptController>().Completed;
            if(all)break;
        }
        foreach(var b in bodies)
        {
            var c=b.GetComponent<ClimberScriptController>();
            Debug.Log($"LAYER1_RESULT arena={b.name} pass={c.Completed} handovers={c.Handovers} stage={c.Stage} position={b.GetBodyState().Position} yaw={b.GetBodyState().Rotation.eulerAngles.y} seconds={c.Elapsed:F2} left={b.GetHandState(HandId.Left).Position} right={b.GetHandState(HandId.Right).Position} reasonL={b.GetGripCandidate(HandId.Left).Reason} reasonR={b.GetGripCandidate(HandId.Right).Reason} {c.DiagnosticState} Rshoulder={b.GetHandState(HandId.Right).ShoulderPosition} Rtarget={b.GetHandState(HandId.Right).TargetOffset} Rforce={b.GetHandState(HandId.Right).CommandForce}");
            Assert.IsTrue(c.Completed,b.name+" stuck at "+c.Stage);Assert.GreaterOrEqual(c.Handovers,3);
        }
    }
#if UNITY_EDITOR
    [UnityTest] public IEnumerator GeneratedSceneLoadsAndRunsWithNormalFixedUpdate()
    {
        float oldScale=Time.timeScale;Time.timeScale=0;
        var scene=EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/ClimberTraining.unity",new LoadSceneParameters(LoadSceneMode.Additive));
        // Unity completes this load on the next frame; freeze physics while awaiting it.
        yield return null;
        var loaded=new List<ClimberScriptController>();
        var roots=scene.GetRootGameObjects();
        foreach(var root in roots)
        {
            root.transform.position+=new Vector3(200,0,200);
            var c=root.GetComponent<ClimberScriptController>();if(c!=null)loaded.Add(c);
        }
        Assert.AreEqual(16,loaded.Count);Physics.SyncTransforms();
        Physics.simulationMode=SimulationMode.FixedUpdate;Time.timeScale=5;
        try
        {
            float deadline=Time.realtimeSinceStartup+25;
            bool all=false;
            while(!all && Time.realtimeSinceStartup<deadline)
            {
                yield return new WaitForFixedUpdate();
                all=true;foreach(var c in loaded)all &= c.Completed;
            }
            foreach(var c in loaded)
            {
                Debug.Log($"LAYER1_SAVED_SCENE arena={c.name} pass={c.Completed} handovers={c.Handovers} seconds={c.Elapsed:F2}");
                Assert.IsTrue(c.Completed,c.name);Assert.GreaterOrEqual(c.Handovers,3);
            }
        }
        finally
        {
            Time.timeScale=oldScale;Physics.simulationMode=SimulationMode.Script;
            foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
#endif
}
