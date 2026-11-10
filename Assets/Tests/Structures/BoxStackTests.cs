using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public sealed class BoxStackTests
{
    readonly List<GameObject> roots=new List<GameObject>();
    float scale;SimulationMode simulation;
    [SetUp] public void Setup(){scale=Time.timeScale;simulation=Physics.simulationMode;}
    [TearDown] public void Cleanup()
    {
        foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);roots.Clear();Time.timeScale=scale;Physics.simulationMode=simulation;
    }
    [Test] public void DynamicSettlementRoundTripAndStaticRuntimeLoad()
    {
        var doc=BoxStackGenerator.Smoke(true);var report=BoxStackSettlement.Settle(doc,out var settled);
        Assert.IsTrue(report.passed,report.reason);Assert.AreEqual(2,report.dynamicObjects);Assert.GreaterOrEqual(report.simulatedSeconds,3);
        Assert.AreEqual(2,settled.generator.features.constructionLevels);
        foreach(var box in report.boxes){Assert.LessOrEqual(box.displacement,.05f);Assert.Less(box.speed,.02f);}
        var parsed=StructureJson.Parse(StructureJson.Serialize(settled));
        var body=StaticStructureLoader.Load(parsed,new Vector3(100,0,100),Quaternion.identity);roots.Add(body.gameObject);
        Assert.AreEqual(3,body.GetComponentsInChildren<Rigidbody>().Length,"Only the character remains dynamic");
        foreach(var o in parsed.objects)Assert.IsFalse(o.movable);
        string path=Path.Combine(Application.temporaryCachePath,"box-smoke-"+Guid.NewGuid()+".json");
        try{StaticStabilityValidator.SaveVerified(path,parsed);Assert.IsTrue(StructureJson.Parse(File.ReadAllText(path)).validation.passed);}finally{if(File.Exists(path))File.Delete(path);}
    }
    [Test] public void UnsupportedBoxCollapsesAndCannotSave()
    {
        var doc=BoxStackGenerator.Smoke(false);doc.objects[1].pose.position[1]+=1;
        var report=BoxStackSettlement.Check(doc);Assert.IsFalse(report.passed);StringAssert.Contains("Collapsed",report.reason);
    }
    [Test] public void CoarseReachRejectsTwoMetreVerticalFaceWithoutScript()
    {
        var doc=BoxStackGenerator.Smoke(false);doc.objects[1].size[1]=2;doc.objects[1].pose.position[1]=1;BoxStackGenerator.SetGoal(doc);
        Assert.IsFalse(BoxStackGeometry.Reachable(doc));
    }
    [Test] public void CoveredContactRejectedButExposedPartAccepted()
    {
        var mat=new PhysicsMaterial("ExplicitTest") {staticFriction=.15f,dynamicFriction=.15f};
        var body=ClimberArenaFactory.Create("Occlusion",new Vector3(100,0,100),mat,false);roots.Add(body.gameObject);
        var support=ClimberArenaFactory.Box("Support",body.transform,new Vector3(0,2.75f,.6f),new Vector3(1.2f,.5f,1.2f),mat,true);
        var blocker=ClimberArenaFactory.Box("Upper",body.transform,new Vector3(-.3f,3.25f,.2f),new Vector3(.4f,.5f,.4f),mat,false);
        body.ResetBody(new BodyReset {Torso=new Vector3(0,2.4f,-.4f),LeftHand=new Vector3(-.3f,3.10f,.2f),RightHand=new Vector3(.3f,3.10f,.2f),Rotation=Quaternion.identity});Physics.SyncTransforms();
        Assert.AreEqual("TopOccluded",body.GetGripCandidate(HandId.Left).Reason);
        Assert.IsTrue(body.GetGripCandidate(HandId.Right).Allowed,body.GetGripCandidate(HandId.Right).Reason);
        UnityEngine.Object.DestroyImmediate(blocker.gameObject);Physics.SyncTransforms();Assert.IsTrue(body.GetGripCandidate(HandId.Left).Allowed);
        foreach(var c in body.GetComponentsInChildren<Collider>())c.sharedMaterial=null;UnityEngine.Object.DestroyImmediate(mat);
    }
    [UnityTest] public IEnumerator SingleBoxSmoke16Arenas(){return Smoke(false);}
    [UnityTest] public IEnumerator TwoStepSmoke16Arenas(){return Smoke(true);}
    IEnumerator Smoke(bool stairs)
    {
        Time.timeScale=25;Physics.simulationMode=SimulationMode.FixedUpdate;var drivers=new List<BoxClimbSmokeController>();
        for(int i=0;i<16;i++)
        {
            var body=StaticStructureLoader.Load(BoxStackGenerator.Smoke(stairs),new Vector3(100+(i%4)*8,0,200+(i/4)*8),Quaternion.Euler(0,(i%3-1)*15,0));
            body.name="BoxArena_"+i;roots.Add(body.gameObject);var driver=body.gameObject.AddComponent<BoxClimbSmokeController>();driver.Configure(body,stairs);drivers.Add(driver);
        }
        float deadline=Time.realtimeSinceStartup+35;
        while(Time.realtimeSinceStartup<deadline)
        {
            bool done=true;foreach(var d in drivers)done&=d.Completed||d.Failed;if(done)break;yield return new WaitForFixedUpdate();
        }
        int passed=0;foreach(var d in drivers){Debug.Log("BOX_SMOKE_RESULT "+d.Snapshot());if(d.Completed)passed++;}
        Assert.AreEqual(16,passed,"Failed smoke: see BOX_SMOKE_RESULT poses; body parameters must not be changed");
    }
    [TestCase(0f,false)][TestCase(.12f,false)][TestCase(.30f,true)]
    public void ClearanceRequiresWholeHandWidth(float gap,bool expected)
    {
        var root=new GameObject("ClearanceFixture");root.transform.position=new Vector3(100,0,100);roots.Add(root);
        var resources=root.AddComponent<StructureResources>();var material=new PhysicsMaterial("Fixture") {staticFriction=.15f,dynamicFriction=.15f};resources.Materials.Add(material);
        var support=ClimberArenaFactory.Box("Support",root.transform,new Vector3(0,.75f,.6f),new Vector3(1.2f,.5f,1.2f),material,true);
        ClimberArenaFactory.Box("CoverL",root.transform,new Vector3(-.25f-gap*.5f,1.25f,.6f),new Vector3(.5f,.5f,1.2f),material,true);
        ClimberArenaFactory.Box("CoverR",root.transform,new Vector3(.25f+gap*.5f,1.25f,.6f),new Vector3(.5f,.5f,1.2f),material,true);
        Physics.SyncTransforms();Assert.AreEqual(expected,GripClearance.IsClear(support,root.transform.TransformPoint(new Vector3(0,1,.2f)),root.transform));
    }
#if UNITY_EDITOR
    [UnityTest] public IEnumerator SavedBoxSmokeScenesReloadAndFinish()
    {
        Time.timeScale=25;Physics.simulationMode=SimulationMode.FixedUpdate;
        foreach(string name in new[] {"Single","Steps"})
        {
            string path="Assets/Scenes/Box"+name+"Smoke.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Additive));
            var scene=SceneManager.GetSceneByPath(path);
            try
            {
                BoxClimbSmokeController controller=null;
                foreach(var root in scene.GetRootGameObjects())
                {
                    root.transform.position+=new Vector3(300,0,300);
                    if(root.TryGetComponent(out BoxClimbSmokeController found))controller=found;
                    foreach(var c in root.GetComponentsInChildren<Collider>())Assert.IsNotNull(c.sharedMaterial,c.name);
                }
                Assert.IsNotNull(controller);Physics.SyncTransforms();float deadline=Time.realtimeSinceStartup+20;
                while(!controller.Completed&&!controller.Failed&&Time.realtimeSinceStartup<deadline)yield return new WaitForFixedUpdate();
                Debug.Log("BOX_SAVED_SCENE "+name+" "+controller.Snapshot());Assert.IsTrue(controller.Completed,controller.Snapshot());
            }
            finally{foreach(var root in scene.GetRootGameObjects())root.SetActive(false);SceneManager.UnloadSceneAsync(scene);}
        }
    }
#endif
}
