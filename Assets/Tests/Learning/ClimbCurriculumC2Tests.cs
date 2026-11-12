using System;
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

// LAYER3_C2_DESIGN §1-§2: the frozen C2 strata and the weighted sub-pools of curriculum file version 2.
public sealed class ClimbCurriculumC2Tests
{
    const string Pools="structures/curriculum-v2/pools.json",Strata="structures/curriculum-v2/strata.json",C1="structures/curriculum-v1/pools.json";
    const string TrainManifest="structures/box-stacks-v2/train/manifest.json",EvalManifest="structures/box-stacks-v2/eval/manifest.json";
    static readonly Dictionary<string,float> Shares=new Dictionary<string,float> {{"two-step",.25f},{"c2-A",.15f},{"c2-B",.2f},{"c2-C",.2f},{"c2-D",.2f}};

    [Serializable] sealed class StratumEntry {public string id,difficulty,path,sha256,cluster,homeStratum,stratum,split;public float[] rises;}
    [Serializable] sealed class StratumSummary {public string stratum;public int structures,clusters,trainStructures,trainClusters,evalStructures,evalClusters,excludedBridge;public bool reportOnly;}
    [Serializable] sealed class StrataFile {public StratumSummary[] strata;public StratumEntry[] structures;}
    [Serializable] sealed class ManifestEntry {public string id,path,sha256;public FeasibilityReport feasibility;}
    [Serializable] sealed class Manifest {public ManifestEntry[] structures;}

    readonly List<GameObject> roots=new List<GameObject>();bool automatic;SimulationMode mode;
    [SetUp] public void Setup(){automatic=Academy.Instance.AutomaticSteppingEnabled;Academy.Instance.AutomaticSteppingEnabled=false;mode=Physics.simulationMode;Physics.simulationMode=SimulationMode.Script;}
    [TearDown] public void Cleanup(){foreach(var root in roots)Object.DestroyImmediate(root);roots.Clear();Academy.Instance.AutomaticSteppingEnabled=automatic;Physics.simulationMode=mode;}

    // Independent of the freeze tool: rises from the manifest's geometry route, ground = 0.
    static float[] Rises(FeasibilityReport f)
    {
        var top=f.surfaces.ToDictionary(s=>s.id,s=>s.top);float previous=0;var rises=new List<float>();
        foreach(string id in f.route.Skip(1)){rises.Add(top[id]-previous);previous=top[id];}
        return rises.ToArray();
    }
    static string Pattern(float[] r)
    {
        if(r.Length==2)return r[0]>=.9f?(r[1]>=.9f?"D":"B"):(r[1]>=.9f?"C":"A");
        return r.Length>=3?(r.Any(x=>x>=.9f)?"F":"E"):"other";
    }

    [Test] public void C2StrataAreFrozenByEffectiveRiseAndSplitByCluster()
    {
        var file=JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(Pools));var strata=JsonUtility.FromJson<StrataFile>(File.ReadAllText(Strata));
        var curriculum=ClimbCurriculum.Load(Pools);
        Assert.AreEqual(2,file.version);Assert.AreEqual(1,curriculum.StageCount);Assert.AreEqual(.25f,curriculum.TwoStepShare);
        CollectionAssert.AreEqual(new[] {"c2-A","c2-B","c2-C","c2-D"},curriculum.SubPools(0).Select(p=>p.Pool));
        CollectionAssert.AreEqual(new[] {.15f,.2f,.2f,.2f},curriculum.SubPools(0).Select(p=>p.Weight));

        var train=JsonUtility.FromJson<Manifest>(File.ReadAllText(TrainManifest)).structures.ToDictionary(e=>e.id);
        var evalIds=new HashSet<string>(JsonUtility.FromJson<Manifest>(File.ReadAllText(EvalManifest)).structures.Select(e=>e.id));
        var rows=strata.structures.ToDictionary(r=>r.id);
        foreach(var r in strata.structures)
        {
            Assert.IsTrue(train.ContainsKey(r.id)&&train[r.id].sha256==r.sha256&&r.path.StartsWith("structures/box-stacks-v2/train/"),"only the v2 training pool: "+r.id);
            var rises=Rises(train[r.id].feasibility);
            Assert.AreEqual(Pattern(rises),r.stratum,"stratum from the effective rises: "+r.id);
            CollectionAssert.AreEqual(rises,r.rises);
        }
        Assert.IsFalse(rows.Keys.Any(evalIds.Contains),"the v2 evaluation set stays out");
        Assert.AreEqual(893-12,strata.structures.Length,"every non-single training structure is recorded");

        // Pools hold exactly the training part of A-D; validation exactly their evaluation part; no cluster on both sides.
        foreach(var p in file.stages[0].pools)
            CollectionAssert.AreEquivalent(strata.structures.Where(r=>"c2-"+r.stratum==p.pool&&r.split=="train").Select(r=>r.id),p.structures.Select(e=>e.id));
        CollectionAssert.AreEquivalent(strata.structures.Where(r=>"ABCD".Contains(r.stratum)&&r.split=="eval").Select(r=>r.id),file.validation.Select(e=>e.id));
        var trainClusters=new HashSet<string>(strata.structures.Where(r=>r.split=="train").Select(r=>r.cluster));
        var evalClusters=new HashSet<string>(strata.structures.Where(r=>r.split=="eval").Select(r=>r.cluster));
        Assert.IsEmpty(evalClusters.Intersect(trainClusters),"no evaluation cluster trains");
        Assert.IsTrue(strata.structures.All(r=>(r.split=="excluded-bridge")==(r.cluster=="")));
        Assert.IsTrue(strata.structures.Where(r=>r.split=="excluded-bridge").All(r=>!file.stages[0].pools.SelectMany(p=>p.structures).Any(e=>e.id==r.id)&&!file.validation.Any(e=>e.id==r.id)));

        // C1 split preserved: every C1 validation structure evaluates in A, no C1 training structure evaluates.
        var c1=JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(C1));
        Assert.IsTrue(c1.validation.All(e=>rows[e.id].split=="eval"&&rows[e.id].stratum=="A"));
        Assert.IsTrue(c1.stages[1].structures.All(e=>rows[e.id].split=="train"));

        foreach(string s in new[] {"A","B","C","D"})
        {
            var m=strata.strata.Single(x=>x.stratum==s);
            Assert.AreEqual(30,m.evalClusters,"30 held-out clusters in "+s);Assert.IsFalse(m.reportOnly);Assert.Greater(m.trainStructures,0);
        }
        Assert.IsTrue(strata.structures.Where(r=>r.stratum=="E"||r.stratum=="F").All(r=>r.split=="reserved"),"E/F kept for C3");
    }

    [Test] public void WeightedPoolsDrawTheDesignShares()
    {
        var curriculum=ClimbCurriculum.Load(Pools);var random=new System.Random(1);const int draws=40000;
        var counts=new Dictionary<string,int>();var seen=curriculum.SubPools(0).ToDictionary(p=>p.Pool,p=>new HashSet<StructureDocument>());
        for(int i=0;i<draws;i++)
        {
            var (pool,doc)=curriculum.Sample(0,random);counts[pool]=counts.TryGetValue(pool,out int c)?c+1:1;
            if(pool==ClimbCurriculum.TwoStepPool)Assert.AreSame(curriculum.TwoStep,doc);
            else {Assert.IsTrue(curriculum.SubPools(0).Single(p=>p.Pool==pool).Docs.Contains(doc),"structure drawn from its own pool");seen[pool].Add(doc);}
        }
        foreach(var pair in Shares)Assert.AreEqual(pair.Value,counts[pair.Key]/(float)draws,.01f,pair.Key+" share");
        foreach(var p in curriculum.SubPools(0))Assert.AreEqual(p.Docs.Length,seen[p.Pool].Count,"uniform within "+p.Pool+": every structure drawn");
    }

    // Version-1 files (C0+C1 training and every evaluation pool) must keep their exact random sequence.
    [Test] public void SinglePoolStagesKeepTheVersion1Sequence()
    {
        var curriculum=ClimbCurriculum.Load(C1);
        for(int stage=0;stage<2;stage++)
        {
            var docs=curriculum.Stage(stage);var a=new System.Random(42);var b=new System.Random(42);
            for(int i=0;i<2000;i++)
            {
                var (pool,doc)=curriculum.Sample(stage,a);
                var expected=b.NextDouble()<.25?curriculum.TwoStep:docs[b.Next(docs.Count)];
                Assert.AreSame(expected,doc);Assert.AreEqual(expected==curriculum.TwoStep?"two-step":stage==0?"c0":"c1",pool);
            }
        }
    }

    [Test] public void MalformedWeightedFilesAreRejected()
    {
        var c1=JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(C1));string folder=Path.Combine(Application.temporaryCachePath,"c2-tests");Directory.CreateDirectory(folder);
        string Write(ClimbCurriculumFile f){f.root=Path.GetFullPath(".");string p=Path.Combine(folder,Guid.NewGuid()+".json");File.WriteAllText(p,JsonUtility.ToJson(f));return p;}
        ClimbCurriculumSubPool Sub(string name,float weight,int skip) => new ClimbCurriculumSubPool {pool=name,weight=weight,structures=c1.stages[1].structures.Skip(skip).Take(3).ToArray()};
        ClimbCurriculumFile File2(int version,params ClimbCurriculumStage[] stages) => new ClimbCurriculumFile {version=version,twoStepShare=.25f,twoStep=c1.twoStep,stages=stages,validation=c1.validation};
        Assert.DoesNotThrow(()=>ClimbCurriculum.Load(Write(File2(2,new ClimbCurriculumStage {name="ok",pools=new[] {Sub("x",1,0),Sub("y",2,3)}}))));
        Assert.Throws<InvalidDataException>(()=>ClimbCurriculum.Load(Write(File2(1,new ClimbCurriculumStage {name="v1",pools=new[] {Sub("x",1,0)}}))),"weighted pools need version 2");
        Assert.Throws<InvalidDataException>(()=>ClimbCurriculum.Load(Write(File2(2,new ClimbCurriculumStage {name="both",pool="z",structures=c1.stages[1].structures.Take(2).ToArray(),pools=new[] {Sub("x",1,0)}}))));
        Assert.Throws<InvalidDataException>(()=>ClimbCurriculum.Load(Write(File2(2,new ClimbCurriculumStage {name="dup",pools=new[] {Sub("x",1,0),Sub("y",1,1)}}))),"one structure in two pools");
        Assert.Throws<ArgumentException>(()=>ClimbCurriculum.Load(Write(File2(2,new ClimbCurriculumStage {name="zero",pools=new[] {Sub("x",0,0),Sub("y",1,3)}}))),"positive weights");
        Assert.Throws<ArgumentException>(()=>ClimbCurriculum.Load(Write(File2(2,new ClimbCurriculumStage {name="name",pools=new[] {Sub("two-step",1,0)}}))),"two-step name is reserved");
        var leak=c1.validation.Take(1).ToArray();
        Assert.Throws<InvalidDataException>(()=>ClimbCurriculum.Load(Write(File2(2,new ClimbCurriculumStage {name="leak",pools=new[] {new ClimbCurriculumSubPool {pool="x",weight=1,structures=leak}}}))),"validation cluster in a weighted pool");
        Directory.Delete(folder,true);
    }

    // Same arena as ClimbCurriculumTests: episodes on the C2 pools swap structures, never draw evaluation structures, and keep demonstration
    // starts on the two-step only.
    [Test] public void C2EpisodesDrawTrainingStructuresAndDemonstrationsStayOnTheTwoStep()
    {
        var doc0=BoxStackGenerator.Smoke(true);
        var body=StaticStructureLoader.Load(doc0,Vector3.zero,Quaternion.identity);roots.Add(body.gameObject);
        var material=new PhysicsMaterial("StaticStructurePhysics") {staticFriction=.15f,dynamicFriction=.15f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Average};
        foreach(var c in body.GetComponentsInChildren<Collider>())if(c.attachedRigidbody==null){c.gameObject.layer=c.GetComponent<ClimberSurface>()!=null?8:9;c.sharedMaterial=material;}
        var behavior=body.gameObject.AddComponent<BehaviorParameters>();behavior.BrainParameters.VectorObservationSize=327;behavior.BrainParameters.ActionSpec=new ActionSpec(6,new[] {2,2});behavior.BehaviorType=BehaviorType.HeuristicOnly;
        var agent=body.gameObject.AddComponent<ClimberAgent>();Physics.SyncTransforms();
        agent.OnEpisodeBegin();
        agent.SnapshotLibrary=ClimbSnapshotLibrary.FromSnapshots(ClimbSnapshotStages.All.Select(s=>agent.CaptureState("own-"+s,s,"own-"+s,"test")).ToList());
        var curriculum=ClimbCurriculum.Load(Pools);agent.Curriculum=curriculum;agent.CurriculumStage=0;
        var held=new HashSet<string>(JsonUtility.FromJson<ClimbCurriculumFile>(File.ReadAllText(Pools)).validation.Select(e=>e.id));
        var poolOf=curriculum.SubPools(0).SelectMany(p=>p.Docs.Select(d=>(d.id,p.Pool))).ToDictionary(x=>x.id,x=>x.Pool);
        var counts=new Dictionary<string,int>();
        for(int episode=0;episode<300;episode++)
        {
            agent.OnEpisodeBegin();Physics.SyncTransforms();
            Assert.IsFalse(held.Contains(agent.StructureId),"evaluation structure drawn for training");
            if(agent.StructurePool==ClimbCurriculum.TwoStepPool)Assert.AreEqual(curriculum.TwoStep.id,agent.StructureId);
            else Assert.AreEqual(poolOf[agent.StructureId],agent.StructurePool,"pool label matches the drawn structure");
            var colliders=agent.GetComponentsInChildren<Collider>().Where(c=>c.attachedRigidbody==null&&c.gameObject.activeInHierarchy).ToList();
            Assert.IsTrue(colliders.All(c=>c.sharedMaterial==material),"one explicit physics material");
            if(agent.SpawnSource!="ground")Assert.AreEqual(ClimbCurriculum.TwoStepPool,agent.StructurePool,"demonstration start only on the two-step");
            counts[agent.StructurePool]=counts.TryGetValue(agent.StructurePool,out int c)?c+1:1;
        }
        foreach(string pool in Shares.Keys)Assert.Greater(counts.TryGetValue(pool,out int n)?n:0,20,pool+" drawn");
    }
}
