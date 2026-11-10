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

public sealed class StaticStructureTests
{
    readonly List<GameObject> roots=new List<GameObject>();
    float oldTimeScale;SimulationMode oldSimulation;
    [SetUp] public void Setup(){oldTimeScale=Time.timeScale;oldSimulation=Physics.simulationMode;}
    [TearDown] public void Cleanup()
    {
        foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);roots.Clear();
        Time.timeScale=oldTimeScale;Physics.simulationMode=oldSimulation;
    }
    static StructureDocument Baseline() => StaticWallGenerator.Generate(StaticDifficulty.Easy,0);
    [Test] public void JsonRoundTripPreservesGeometryAndRejectsUnknownVersionAndFields()
    {
        var doc=Baseline();string json=StructureJson.Serialize(doc);
        Assert.AreEqual(StructureJson.Hash(doc),StructureJson.Hash(StructureJson.Parse(json)));
        Assert.AreEqual(7,StructureJson.Parse(json).objects.Length);
        Assert.Throws<InvalidDataException>(()=>StructureJson.Parse(json.Replace("\"schemaVersion\": 1","\"schemaVersion\": 99")));
        Assert.Throws<InvalidDataException>(()=>StructureJson.Parse(json.Replace("\"schemaVersion\"","\"unknownVersion\"")));
        Assert.Throws<InvalidDataException>(()=>StructureJson.Parse("{\"id\":\"a\",\"id\":\"b\"}"));
        Assert.Throws<InvalidDataException>(()=>StructureJson.Parse("{}"));
    }
    [TestCase("dynamic")][TestCase("kinematic")]
    public void DeferredMotionIsRejected(string motion)
    {var doc=Baseline();doc.objects[1].motion=motion;doc.objects[1].massKg=10;Assert.Throws<InvalidDataException>(()=>StructureJson.Validate(doc));}
    [TestCase("duplicate")][TestCase("size")][TestCase("material")][TestCase("rotation")][TestCase("start")][TestCase("nan")][TestCase("count")]
    public void InvalidInputsAreRejected(string kind)
    {
        var d=Baseline();
        switch(kind)
        {
            case "duplicate":d.objects[1].id=d.objects[0].id;break;
            case "size":d.objects[1].size[0]=0;break;
            case "material":d.objects[1].materialId="missing";break;
            case "rotation":d.objects[1].pose.rotation=new[] {0f,0f,0f,0f};break;
            case "start":d.start.leftHand.position[0]=10;break;
            case "nan":d.objects[1].size[0]=float.NaN;break;
            case "count":d.objects=new ObjectSpec[21];break;
        }
        Assert.Throws<InvalidDataException>(()=>StructureJson.Validate(d));
    }
    [Test] public void SettlingGateRejectsOverlapAndForgedStampBeforeOverwriting()
    {
        string directory=Path.Combine(Application.temporaryCachePath,"structure-tests-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"wall.json");
        try
        {
            var d=Baseline();var report=StaticStabilityValidator.SaveVerified(path,d);
            Assert.IsTrue(report.passed,report.reason);Assert.AreEqual(0,report.dynamicObjects);Assert.GreaterOrEqual(report.simulatedSeconds,3);
            string before=File.ReadAllText(path);
            StaticStabilityValidator.SaveVerified(path,d);Assert.AreEqual(before,File.ReadAllText(path));
            d.objects[2].pose=d.objects[1].pose;
            Assert.IsFalse(StaticStabilityValidator.Check(d).passed);
            Assert.Throws<InvalidDataException>(()=>StaticStabilityValidator.SaveVerified(path,d));
            Assert.AreEqual(before,File.ReadAllText(path));
            Assert.Throws<InvalidDataException>(()=>StaticStructureLoader.Load(d,Vector3.zero,Quaternion.identity));
            Debug.Log("STRUCTURE_SAVE_GATE passed: 3s local simulation, overlap rejected, old file preserved, forged receipt rejected");
        }
        finally {Directory.Delete(directory,true);}
    }
    [Test] public void SpawnAndGoalPenetrationAreRejected()
    {
        var d=Baseline();d.goal.center=d.objects[5].pose.position;
        Assert.IsFalse(StaticStabilityValidator.Check(d).passed);
        d=Baseline();d.start.torso.position[2]=.45f;d.start.leftHand.position[2]=.45f;d.start.rightHand.position[2]=.45f;
        Assert.IsFalse(StaticStabilityValidator.Check(d).passed);
    }
    [Test] public void DifficultyPoolsAreSeededAndLimitedToWitnessedCells()
    {
        foreach(StaticDifficulty level in Enum.GetValues(typeof(StaticDifficulty)))for(int seed=0;seed<30;seed++)
        {
            var d=StaticWallGenerator.Generate(level,seed);var other=StaticWallGenerator.Generate(level,seed);
            Assert.AreEqual(StructureJson.Hash(d),StructureJson.Hash(other));
            Assert.IsTrue(StaticStabilityValidator.Check(d).passed,level+" seed="+seed);
            Assert.LessOrEqual(d.objects.Length,20);
        }
        Assert.Throws<InvalidDataException>(()=>StaticWallGenerator.GenerateAt(.25f,.20f));
        Assert.Throws<InvalidDataException>(()=>StaticWallGenerator.GenerateAt(1.05f,.40f));
    }
    [UnityTest] public IEnumerator SavedJsonClimbsIn16ArenasWithUnchangedLayer1Controller()
    {
        Physics.simulationMode=SimulationMode.FixedUpdate;Time.timeScale=25;
        var controllers=new List<ClimberScriptController>();
        var doc=StructureJson.Parse(StructureJson.Serialize(Baseline()));
        for(int i=0;i<16;i++)
        {
            var b=StaticStructureLoader.Load(doc,new Vector3(100+(i%4)*7,0,100+(i/4)*7),Quaternion.Euler(0,(i%3-1)*15,0),true);
            roots.Add(b.gameObject);controllers.Add(b.GetComponent<ClimberScriptController>());
            Assert.AreEqual(3,b.GetComponentsInChildren<Rigidbody>().Length);
            foreach(var c in b.GetComponentsInChildren<Collider>())Assert.IsNotNull(c.sharedMaterial,c.name);
            Assert.Less(Vector3.Distance(b.GetBodyState().Position,doc.start.torso.Position),.0001f);
        }
        float deadline=Time.realtimeSinceStartup+25;
        while(Time.realtimeSinceStartup<deadline)
        {
            bool all=true;foreach(var c in controllers)all&=c.Completed;if(all)break;
            yield return new WaitForFixedUpdate();
        }
        foreach(var c in controllers)
        {
            var b=c.GetComponent<ClimberBody>();
            Debug.Log($"STRUCTURE_CLIMB arena={c.name} complete={c.Completed} handovers={c.Handovers} seconds={c.Elapsed:F3} torso={b.GetBodyState().Position} peakN={Mathf.Max(b.GetHandState(HandId.Left).PeakConstraintLoad,b.GetHandState(HandId.Right).PeakConstraintLoad):F6}");
            Assert.IsTrue(c.Completed,c.name);Assert.GreaterOrEqual(c.Handovers,3);Assert.IsTrue(b.GetComponent<ClimberGoal>().Contains(b.GetBodyState()));
        }
    }
    [UnityTest] public IEnumerator ParameterizedGeneratedJsonUsesUnchangedPrepPolicy()
    {
        Physics.simulationMode=SimulationMode.FixedUpdate;Time.timeScale=25;
        var drivers=new List<StructureScriptDriver>();
        for(int i=0;i<8;i++)
        {
            var doc=i<4?StaticWallGenerator.Generate(StaticDifficulty.Easy,i+1):StaticWallGenerator.GenerateAt(i<6?.60f:1f,i%2==0?.20f:.80f,i);
            doc=StructureJson.Parse(StructureJson.Serialize(doc));
            var b=StaticStructureLoader.Load(doc,new Vector3(100+(i%4)*7,0,200+(i/4)*7),Quaternion.Euler(0,i%2==0?-15:15,0),true);
            roots.Add(b.gameObject);drivers.Add(b.GetComponent<StructureScriptDriver>());Assert.IsNotNull(drivers[i]);
        }
        float deadline=Time.realtimeSinceStartup+25;
        while(Time.realtimeSinceStartup<deadline)
        {
            bool all=true;foreach(var driver in drivers)all&=driver.Policy.Done;if(all)break;
            yield return new WaitForFixedUpdate();
        }
        foreach(var driver in drivers)
        {
            var p=driver.Policy;Debug.Log($"STRUCTURE_PARAMETER_CLIMB arena={driver.name} success={p.Success} reason={p.Reason} handovers={p.Handovers} seconds={p.Elapsed:F3}");
            Assert.IsTrue(p.Success,p.Reason+" "+p.Detail);Assert.GreaterOrEqual(p.Handovers,3);
        }
    }
#if UNITY_EDITOR
    [UnityTest] public IEnumerator GeneratedDemoReloadsWithPersistentMaterialsAndClimbs()
    {
        Physics.simulationMode=SimulationMode.FixedUpdate;Time.timeScale=25;
        const string path="Assets/Scenes/StaticStructureDemo.unity";
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Additive));
        var scene=SceneManager.GetSceneByPath(path);
        try
        {
            ClimberScriptController controller=null;
            foreach(var root in scene.GetRootGameObjects())
            {
                root.transform.position+=new Vector3(200,0,200);
                foreach(var collider in root.GetComponentsInChildren<Collider>())Assert.IsNotNull(collider.sharedMaterial,collider.name);
                if(root.TryGetComponent(out ClimberScriptController found))controller=found;
            }
            Assert.IsNotNull(controller);Physics.SyncTransforms();
            float deadline=Time.realtimeSinceStartup+20;
            while(!controller.Completed&&Time.realtimeSinceStartup<deadline)yield return new WaitForFixedUpdate();
            Assert.IsTrue(controller.Completed);Assert.GreaterOrEqual(controller.Handovers,3);
            Debug.Log("STRUCTURE_SAVED_DEMO_PASS seconds="+controller.Elapsed.ToString("F3"));
        }
        finally
        {
            foreach(var root in scene.GetRootGameObjects())root.SetActive(false);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
#endif
}
