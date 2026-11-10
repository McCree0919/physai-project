using System.Collections.Generic;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using PhysicalAI.Learning;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;

public sealed class ClimberAgentTests
{
    readonly List<GameObject> roots=new List<GameObject>();bool automatic;
    [SetUp] public void Setup(){automatic=Academy.Instance.AutomaticSteppingEnabled;Academy.Instance.AutomaticSteppingEnabled=false;}
    [TearDown] public void Cleanup(){foreach(var root in roots)Object.DestroyImmediate(root);roots.Clear();Academy.Instance.AutomaticSteppingEnabled=automatic;}
    ClimberAgent Create(Vector3 position,Quaternion rotation)
    {
        var doc=BoxStackGenerator.Smoke(true);var body=StaticStructureLoader.Load(doc,position,rotation);roots.Add(body.gameObject);
        // Compare equal public body states, independently of initial loader transform sync.
        body.ResetBody(new BodyReset {Torso=doc.start.torso.Position,LeftHand=doc.start.leftHand.Position,RightHand=doc.start.rightHand.Position,Rotation=Quaternion.identity});Physics.SyncTransforms();
        foreach(var c in body.GetComponentsInChildren<Collider>())if(c.attachedRigidbody==null)c.gameObject.layer=c.GetComponent<ClimberSurface>()!=null?8:9;
        var behavior=body.gameObject.AddComponent<BehaviorParameters>();behavior.BrainParameters.VectorObservationSize=327;behavior.BrainParameters.ActionSpec=new ActionSpec(6,new[] {2,2});behavior.BehaviorType=BehaviorType.HeuristicOnly;
        var agent=body.gameObject.AddComponent<ClimberAgent>();agent.OnEpisodeBegin();Physics.SyncTransforms();return agent;
    }
    [Test] public void ProjectionKeepsExistingTargetShell()
    {Assert.AreEqual(.15f,ClimberAgent.Project(Vector3.zero).magnitude,.0001f);Assert.AreEqual(.85f,ClimberAgent.Project(Vector3.one*10).magnitude,.0001f);}
    [Test] public void ObservationCountAndFiniteBounds()
    {
        var agent=Create(new Vector3(100,0,100),Quaternion.identity);var obs=agent.BuildObservations();Assert.AreEqual(327,obs.Length);Assert.AreEqual(0,agent.MaxStep);
        foreach(float v in obs){Assert.IsFalse(float.IsNaN(v)||float.IsInfinity(v));Assert.That(v,Is.InRange(-1f,1f));}
    }
    [Test] public void LocalObservationsDoNotLeakTranslationOrYaw()
    {
        var a=Create(new Vector3(100,0,100),Quaternion.identity);var b=Create(new Vector3(200,0,200),Quaternion.Euler(0,90,0));
        // Do not place a height-map ray exactly on a box edge (visibility is discontinuous).
        foreach(var agent in new[] {a,b})agent.GetComponent<ClimberBody>().ResetBody(new BodyReset {Torso=new Vector3(0,.4f,-.45f),LeftHand=new Vector3(-.3f,1.15f,-.15f),RightHand=new Vector3(.3f,.95f,-.15f),Rotation=Quaternion.identity});
        Physics.SyncTransforms();
        var x=a.BuildObservations();var y=b.BuildObservations();for(int i=0;i<x.Length;i++)Assert.AreEqual(x[i],y[i],.002f,"obs "+i);
    }
    [Test] public void GrabCommandsAreStateIntentAndDeltaIntegratedOnlyOnce()
    {
        var a=Create(new Vector3(100,0,100),Quaternion.identity);var before=a.BuildObservations();
        a.ApplyCommand(Vector3.right,Vector3.zero,1,1);var after=a.BuildObservations();
        Assert.LessOrEqual(Vector3.Distance(new Vector3(before[19],before[20],before[21]),new Vector3(after[19],after[20],after[21]))*.9f,.08001f);
        a.ApplyCommand(Vector3.zero,Vector3.zero,1,1);Assert.IsTrue(a.GetComponent<ClimberBody>().GetHandState(HandId.Left).GrabRequested);
        var held=a.BuildObservations();Assert.AreEqual(after[19],held[19],.0001f);
        a.ApplyCommand(Vector3.zero,Vector3.zero,0,0);Assert.IsFalse(a.GetComponent<ClimberBody>().GetHandState(HandId.Left).GrabRequested);
        a.ApplyCommand(Vector3.zero,Vector3.zero,1,1);Assert.IsTrue(a.GetComponent<ClimberBody>().GetHandState(HandId.Left).GrabRequested);Assert.AreEqual(4,a.Decisions);
        Assert.AreEqual(-.0008f,a.GetCumulativeReward(),.000001f);
    }
    [Test] public void StableProgressCannotBeFarmedByStayingOrReturning()
    {
        var a=Create(new Vector3(100,0,100),Quaternion.identity);var body=a.GetComponent<ClimberBody>();
        Pose(body,.95f,.30f,.60f,.02f);for(int i=0;i<60;i++)a.BeforePhysics(i);Assert.AreEqual(1,a.ProgressEvents);
        Pose(body,1.45f,1.60f,1.10f,1.40f);for(int i=0;i<60;i++)a.BeforePhysics(i);Assert.AreEqual(2,a.ProgressEvents);
        Pose(body,.95f,.30f,.60f,.02f);for(int i=0;i<60;i++)a.BeforePhysics(i);Assert.AreEqual(2,a.ProgressEvents);Assert.AreEqual(.2f,a.GetCumulativeReward(),.00001f);
    }
    static void Pose(ClimberBody body,float torsoY,float torsoZ,float handY,float handZ)
    {body.ResetBody(new BodyReset {Torso=new Vector3(0,torsoY,torsoZ),LeftHand=new Vector3(-.3f,handY,handZ),RightHand=new Vector3(.3f,handY,handZ),Rotation=Quaternion.identity});Physics.SyncTransforms();}
}
