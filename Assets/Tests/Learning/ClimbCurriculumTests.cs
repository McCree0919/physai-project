using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Learning;
using PhysicalAI.Structures;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;
using Object=UnityEngine.Object;

// LAYER3_DESIGN §17: runtime structure swaps from the frozen curriculum pools.
public sealed class ClimbCurriculumTests
{
    const string Pools="structures/curriculum-v1/pools.json",Library="results/climb-snapshots-v2/library.jsonl";
    readonly List<GameObject> roots=new List<GameObject>();bool automatic;SimulationMode mode;
    [SetUp] public void Setup(){automatic=Academy.Instance.AutomaticSteppingEnabled;Academy.Instance.AutomaticSteppingEnabled=false;mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;}
    [TearDown] public void Cleanup(){foreach(var root in roots)Object.DestroyImmediate(root);roots.Clear();Academy.Instance.AutomaticSteppingEnabled=automatic;Physics.simulationMode=mode;}

    // Same arena as ClimbTrainingBuilder: training two-step at a scene arena position (the body configuration hash includes joint anchors,
    // whose last float bits depend on the arena position), layers 8/9, one shared physics material.
    ClimberAgent Create(PhysicsMaterial material)
    {
        var body=StaticStructureLoader.Load(BoxStackGenerator.Smoke(true),new Vector3((roots.Count%4)*14,0,(roots.Count/4)*14),Quaternion.identity);roots.Add(body.gameObject);
        foreach(var c in body.GetComponentsInChildren<Collider>())if(c.attachedRigidbody==null){c.gameObject.layer=c.GetComponent<ClimberSurface>()!=null?8:9;c.sharedMaterial=material;}
        var behavior=body.gameObject.AddComponent<BehaviorParameters>();behavior.BrainParameters.VectorObservationSize=327;behavior.BrainParameters.ActionSpec=new ActionSpec(6,new[] {2,2});behavior.BehaviorType=BehaviorType.HeuristicOnly;
        var agent=body.gameObject.AddComponent<ClimberAgent>();Physics.SyncTransforms();return agent;
    }
    static PhysicsMaterial Material() => new PhysicsMaterial("StaticStructurePhysics") {staticFriction=.15f,dynamicFriction=.15f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Average};
    static List<Collider> Static(ClimberAgent a) => a.GetComponentsInChildren<Collider>().Where(c=>c.attachedRigidbody==null).ToList();

    [Test] public void PoolsAreFrozenDisjointAndFromTheTrainingPoolOnly()
    {
        var file=JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(Pools));var curriculum=ClimbCurriculum.Load(Pools);
        Assert.AreEqual(.25f,curriculum.TwoStepShare);Assert.AreEqual(2,curriculum.StageCount);
        var train=file.stages.SelectMany(s=>s.structures).ToList();
        Assert.IsTrue(train.Concat(file.validation).All(e=>e.path.StartsWith("structures/box-stacks-v2/train/")),"curriculum structures come only from the v2 training pool");
        Assert.IsEmpty(file.validation.Select(e=>e.cluster).Intersect(train.Select(e=>e.cluster)),"validation clusters never train");
        Assert.AreEqual(12,file.stages[0].structures.Length,"C0: Easy single boxes");
        Assert.IsTrue(curriculum.Stage(0).All(d=>d.objects.Count(o=>o.settleAsDynamic)==1));
        Assert.IsTrue(curriculum.Stage(1).All(d=>d.objects.Count(o=>o.settleAsDynamic)==2));
        Assert.AreEqual(281,file.stages[1].structures.Length+file.validation.Length,"C1: all Easy two-level stacks, split by cluster");
    }

    [Test] public void RuntimeSwapReproducesTheTrainingTwoStepAndItsSnapshotHash()
    {
        var curriculum=ClimbCurriculum.Load(Pools);var material=Material();var agent=Create(material);var body=agent.GetComponent<ClimberBody>();
        string scene=agent.GeometryHash();
        string recorded=JsonUtility.FromJson<ClimbStateSnapshot>(File.ReadLines(Library).First(l=>l.Trim().Length>0)).geometryHash;
        Assert.AreEqual(recorded,scene,"test arena matches the recorded library geometry");
        var swapped=Create(material);
        foreach(var doc in new[] {curriculum.Stage(1)[0],curriculum.Stage(0)[0],curriculum.TwoStep})StructureSwap.Apply(swapped.GetComponent<ClimberBody>(),doc,material);
        // GeometryHash caches its surface map; a fresh agent component reads the swapped geometry.
        Object.DestroyImmediate(swapped);var fresh=roots.Last().AddComponent<ClimberAgent>();
        Assert.AreEqual(scene,fresh.GeometryHash(),"swapping back to the frozen two-step restores the scene geometry exactly");
        Assert.IsTrue(Static(fresh).All(c=>c.sharedMaterial==material),"one explicit physics material on every structure collider");
    }

    [Test] public void EpisodesSwapStructuresAndDemonstrationsStayOnTheTwoStep()
    {
        var curriculum=ClimbCurriculum.Load(Pools);var material=Material();var agent=Create(material);
        // Snapshots captured in this arena: the recorded library's body configuration hash belongs to the scene-deserialized bodies
        // (checked in the player smoke run); the geometry hash, which the swap affects, is checked against the library above.
        agent.OnEpisodeBegin();
        agent.SnapshotLibrary=ClimbSnapshotLibrary.FromSnapshots(ClimbSnapshotStages.All.Select(s=>agent.CaptureState("own-"+s,s,"own-"+s,"test")).ToList());
        agent.Curriculum=curriculum;agent.CurriculumStage=1;
        var held=new HashSet<string>(JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(Pools)).validation.Select(e=>e.id));
        var byId=curriculum.Stage(1).Concat(new[] {curriculum.TwoStep}).ToDictionary(d=>d.id);
        int twoStep=0,demo=0;var seen=new HashSet<string>();
        for(int episode=0;episode<400;episode++)
        {
            agent.OnEpisodeBegin();Physics.SyncTransforms();
            var doc=byId[agent.StructureId];seen.Add(doc.id);
            Assert.IsFalse(held.Contains(doc.id),"validation structure drawn for training");
            Assert.AreEqual(agent.StructurePool==ClimbCurriculum.TwoStepPool,doc==curriculum.TwoStep);
            var colliders=Static(agent).Where(c=>c.gameObject.activeInHierarchy).ToList();
            CollectionAssert.AreEquivalent(doc.objects.Select(o=>o.id),colliders.Select(c=>c.gameObject.name),"exactly the drawn structure, no leftover boxes");
            Assert.IsTrue(colliders.All(c=>c.sharedMaterial==material&&c.gameObject.layer==(c.GetComponent<ClimberSurface>()!=null?8:9)));
            Assert.AreEqual(StructureJson.Vector(doc.goal.center),agent.GetComponent<ClimberGoal>().Center);
            if(agent.StructurePool==ClimbCurriculum.TwoStepPool)twoStep++;
            if(agent.SpawnSource!="ground"){demo++;Assert.AreEqual(ClimbCurriculum.TwoStepPool,agent.StructurePool,"demonstration start only on the two-step");}
            else Assert.Less(agent.GetComponent<ClimberBody>().GetBodyState().Position.y,.5f,"ground start at the spawn");
        }
        Assert.That(twoStep,Is.InRange(70,130),"two-step share ~25%");Assert.That(demo,Is.InRange(twoStep/4,twoStep*3/4),"~50% demonstration starts on the two-step");
        Assert.Greater(seen.Count,100,"episodes draw many C1 structures");
    }

    [Test] public void WithoutACurriculumStageTheSceneStructureIsKept()
    {
        var agent=Create(Material());agent.Curriculum=ClimbCurriculum.Load(Pools);string before=agent.GeometryHash();
        for(int i=0;i<5;i++)agent.OnEpisodeBegin();
        Assert.AreEqual("",agent.StructureId);Assert.AreEqual(before,agent.GeometryHash());
    }
}
