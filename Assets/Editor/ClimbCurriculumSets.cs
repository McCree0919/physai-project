using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhysicalAI.Learning;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEngine;

// Curriculum pools C0/C1 (LAYER3_DESIGN §17.2), taken only from the frozen v2 training pool, never from the evaluation set.
// C0: the training two-step plus the Easy single boxes. C1: the training two-step plus the Easy two-level stacks, of which 20 % of the
// clusters (same linking rule as the freeze, mirrors included) are held out as the curriculum validation set. The two-step is written
// here once, settled exactly as ClimbTrainingBuilder settles it, so runtime swaps reproduce the scene geometry and its snapshot hashes.
public static class ClimbCurriculumSets
{
    public const string Root="structures/curriculum-v1";
    public const float TwoStepShare=.25f,ValidationClusterShare=.2f;
    public const int SplitSeed=17;

    [MenuItem("Tools/Climb/Freeze Curriculum C0-C1")]
    public static void Freeze()
    {
        if(File.Exists(Root+"/pools.json"))throw new InvalidOperationException(Root+" is frozen; delete it explicitly to regenerate");
        Directory.CreateDirectory(Root);
        var manifest=JsonUtility.FromJson<ClimbGeneratorV2Sets.Manifest>(File.ReadAllText(ClimbGeneratorV2Sets.Root+"/train/manifest.json"));
        var easy=manifest.structures.Where(e=>e.difficulty=="Easy").ToList();
        int Boxes(ClimbGeneratorV2Sets.Entry e) => StructureJson.Parse(File.ReadAllText(e.path)).objects.Count(o=>o.settleAsDynamic);
        var singles=easy.Where(e=>Boxes(e)==1).ToList();var twoLevel=easy.Where(e=>Boxes(e)==2).ToList();

        var report=BoxStackSettlement.Settle(BoxStackGenerator.Smoke(true),out var twoStep);
        if(!report.passed)throw new InvalidOperationException("Training two-step settlement failed: "+report.reason);
        twoStep.validation=report;twoStep.validation.inputHash=StructureJson.Hash(twoStep);
        string twoStepJson=StructureJson.Serialize(twoStep),twoStepPath=Root+"/two-step.json";File.WriteAllText(twoStepPath,twoStepJson);

        var c0=Entries(singles);var c1=Entries(twoLevel);
        var clusters=c1.Select(e=>e.cluster).Distinct().OrderBy(c=>c,StringComparer.Ordinal).ToList();
        var random=new System.Random(SplitSeed);
        for(int i=clusters.Count-1;i>0;i--){int j=random.Next(i+1);(clusters[i],clusters[j])=(clusters[j],clusters[i]);}
        var held=new HashSet<string>(clusters.Take(Mathf.RoundToInt(clusters.Count*ValidationClusterShare)));
        var file=new ClimbCurriculumFile {version=1,root="../..",twoStepShare=TwoStepShare,
            rule=$"v2 train pool only; C0 = Easy single boxes, C1 = Easy two-level stacks; {held.Count}/{clusters.Count} C1 clusters (seed {SplitSeed}) held out for validation",
            twoStep=new ClimbCurriculumEntry {id=twoStep.id,path=twoStepPath,sha256=ClimbGeneratorV2Sets.Sha256(twoStepJson),cluster="two-step"},
            stages=new[] {new ClimbCurriculumStage {name="C0",pool="c0",structures=c0.ToArray()},new ClimbCurriculumStage {name="C1",pool="c1",structures=c1.Where(e=>!held.Contains(e.cluster)).ToArray()}},
            validation=c1.Where(e=>held.Contains(e.cluster)).ToArray()};
        File.WriteAllText(Root+"/pools.json",JsonUtility.ToJson(file,true));
        var check=ClimbCurriculum.Load(Root+"/pools.json");
        Debug.Log($"CURRICULUM_FREEZE_COMPLETE c0={c0.Count}({c0.Select(e=>e.cluster).Distinct().Count()} clusters) c1Train={file.stages[1].structures.Length}({clusters.Count-held.Count} clusters) "+
            $"c1Validation={file.validation.Length}({held.Count} clusters) twoStepShare={check.TwoStepShare}");
    }

    // ---- C2 (LAYER3_C2_DESIGN §1-§2) ----
    // Strata by the effective rises along the geometry check's route (top of each standing surface minus the previous one, ground = 0):
    // two standing levels A low-low, B tall-low, C low-tall, D tall-tall (tall = rise >= 0.9 m); E (>= 3 levels, all low) and F (>= 3 levels,
    // some tall) are recorded and kept for C3. Single boxes stay C0 content. Clusters link all non-single v2 training structures (mirrors
    // included), so no cluster straddles train and eval in any stratum. Per stratum, 30 home clusters (40 % if it has fewer than 75) are
    // held out, after forcing the C1 validation clusters into the evaluation part and the C1 training clusters into the training part.
    // Nothing here uses a controller, policy or feasibility outcome other than the geometry route.
    public const string RootC2="structures/curriculum-v2";
    public const float TallRise=.9f;
    public const int HeldOutClusters=30,SmallStratumClusters=75,ReportOnlyBelowClusters=25;
    public const float SmallStratumHeldOutShare=.4f;
    public static readonly string[] StrataC2={"A","B","C","D"};
    static readonly Dictionary<string,float> WeightsC2=new Dictionary<string,float> {{"A",.15f},{"B",.2f},{"C",.2f},{"D",.2f}};

    [Serializable] public sealed class StratumEntry {public string id,difficulty,path,sha256,cluster,homeStratum,stratum,split;public float[] rises;}
    [Serializable] public sealed class StratumSummary
    {
        public string stratum;public int structures,clusters,homeClusters,targetHeldOutClusters,forcedEvalClusters,forcedTrainClusters,trainStructures,trainClusters,evalStructures,evalClusters,excludedBridge;
        public bool reportOnly;
    }
    [Serializable] public sealed class StrataFile
    {
        public int version=1;public string rule,source,c1Pools,c1PoolsSha256;public int splitSeed;public float tallRise;
        public StratumSummary[] strata;public StratumEntry[] structures;
    }

    // Effective rises: route[0] is the ground; each later route element names a standing surface.
    public static float[] EffectiveRises(FeasibilityReport f)
    {
        var top=f.surfaces.ToDictionary(s=>s.id,s=>s.top);var tops=new List<float> {0};
        for(int i=1;i<f.route.Length;i++)tops.Add(top[f.route[i]]);
        return tops.Skip(1).Select((t,i)=>t-tops[i]).ToArray();
    }
    public static string Stratum(float[] rises)
    {
        bool Tall(float r) => r>=TallRise;
        if(rises.Length==2)return Tall(rises[0])?(Tall(rises[1])?"D":"B"):(Tall(rises[1])?"C":"A");
        if(rises.Length>=3)return rises.Any(Tall)?"F":"E";
        return "other";
    }

    [MenuItem("Tools/Climb/Freeze Curriculum C2")]
    public static void FreezeC2()
    {
        if(File.Exists(RootC2+"/pools.json")||File.Exists(RootC2+"/strata.json"))throw new InvalidOperationException(RootC2+" is frozen; delete it explicitly to regenerate");
        string c1Text=File.ReadAllText(Root+"/pools.json");var c1=JsonUtility.FromJson<ClimbCurriculumFile>(c1Text);
        var c1Validation=new HashSet<string>(c1.validation.Select(e=>e.id));var c1Train=new HashSet<string>(c1.stages.SelectMany(s=>s.structures).Select(e=>e.id));
        var manifest=JsonUtility.FromJson<ClimbGeneratorV2Sets.Manifest>(File.ReadAllText(ClimbGeneratorV2Sets.Root+"/train/manifest.json"));
        int Boxes(ClimbGeneratorV2Sets.Entry e) => StructureJson.Parse(File.ReadAllText(e.path)).objects.Count(o=>o.settleAsDynamic);
        var multi=manifest.structures.Where(e=>Boxes(e)>1).OrderBy(e=>e.id,StringComparer.Ordinal).ToList();
        if(multi.Any(e=>e.feasibility==null||e.feasibility.route==null||e.feasibility.route.Length<2))throw new InvalidOperationException("A training structure has no geometry route");
        var rises=multi.ToDictionary(e=>e.id,e=>EffectiveRises(e.feasibility));
        var stratum=multi.ToDictionary(e=>e.id,e=>Stratum(rises[e.id]));
        // C1 clusters were linked among Easy two-level stacks only; a Standard structure can bridge a C1 validation and a C1 training cluster.
        // The C1 split is kept as it was: in such a cluster, every non-C1 member is excluded from C2 (train and eval) and the clusters are
        // re-linked without them, until no cluster holds both. C1 structures are never linked directly (they were split by cluster).
        var excluded=new HashSet<string>();Dictionary<string,string> cluster;HashSet<string> forcedEval,forcedTrain;
        while(true)
        {
            var kept=multi.Where(e=>!excluded.Contains(e.id)).ToList();
            cluster=Entries(kept).ToDictionary(e=>e.id,e=>e.cluster);
            forcedEval=new HashSet<string>(kept.Where(e=>c1Validation.Contains(e.id)).Select(e=>cluster[e.id]));
            forcedTrain=new HashSet<string>(kept.Where(e=>c1Train.Contains(e.id)).Select(e=>cluster[e.id]));
            var conflict=new HashSet<string>(forcedEval.Intersect(forcedTrain));
            if(conflict.Count==0)break;
            var bridges=kept.Where(e=>conflict.Contains(cluster[e.id])&&!c1Validation.Contains(e.id)&&!c1Train.Contains(e.id)).Select(e=>e.id).ToList();
            if(bridges.Count==0)throw new InvalidOperationException("C1 validation and training structures are linked directly: "+string.Join(",",conflict));
            excluded.UnionWith(bridges);
        }
        if(c1Validation.Any(id=>!cluster.ContainsKey(id)))throw new InvalidOperationException("A C1 validation structure is missing from the v2 training pool");
        var members=multi.Where(e=>!excluded.Contains(e.id)).GroupBy(e=>cluster[e.id]).ToDictionary(g=>g.Key,g=>g.ToList());
        // Home stratum of a cluster: the stratum of its named (smallest-id) member.
        var home=members.Keys.ToDictionary(c=>c,c=>stratum[c]);

        var random=new System.Random(SplitSeed);var evalClusters=new HashSet<string>();var summaries=new List<StratumSummary>();
        foreach(string s in StrataC2)
        {
            var homes=members.Keys.Where(c=>home[c]==s).OrderBy(c=>c,StringComparer.Ordinal).ToList();
            int target=homes.Count<SmallStratumClusters?Mathf.RoundToInt(homes.Count*SmallStratumHeldOutShare):HeldOutClusters;
            var forced=homes.Where(forcedEval.Contains).ToList();
            var candidates=homes.Where(c=>!forcedEval.Contains(c)&&!forcedTrain.Contains(c)).ToList();
            for(int i=candidates.Count-1;i>0;i--){int j=random.Next(i+1);(candidates[i],candidates[j])=(candidates[j],candidates[i]);}
            evalClusters.UnionWith(forced);evalClusters.UnionWith(candidates.Take(Math.Max(0,target-forced.Count)));
            summaries.Add(new StratumSummary {stratum=s,homeClusters=homes.Count,targetHeldOutClusters=target,forcedEvalClusters=forced.Count,forcedTrainClusters=homes.Count(forcedTrain.Contains)});
        }
        string Split(ClimbGeneratorV2Sets.Entry e) => excluded.Contains(e.id)?"excluded-bridge":!StrataC2.Contains(stratum[e.id])?"reserved":evalClusters.Contains(cluster[e.id])?"eval":"train";
        var rows=multi.Select(e=>new StratumEntry {id=e.id,difficulty=e.difficulty,path=e.path,sha256=e.sha256,cluster=excluded.Contains(e.id)?"":cluster[e.id],
            homeStratum=excluded.Contains(e.id)?"":home[cluster[e.id]],stratum=stratum[e.id],split=Split(e),rises=rises[e.id]}).ToList();
        foreach(var m in summaries)
        {
            var mine=rows.Where(r=>r.stratum==m.stratum&&r.split!="excluded-bridge").ToList();
            m.excludedBridge=rows.Count(r=>r.stratum==m.stratum&&r.split=="excluded-bridge");
            m.structures=mine.Count;m.clusters=mine.Select(r=>r.cluster).Distinct().Count();
            m.trainStructures=mine.Count(r=>r.split=="train");m.trainClusters=mine.Where(r=>r.split=="train").Select(r=>r.cluster).Distinct().Count();
            m.evalStructures=mine.Count(r=>r.split=="eval");m.evalClusters=mine.Where(r=>r.split=="eval").Select(r=>r.cluster).Distinct().Count();
            m.reportOnly=m.evalClusters<ReportOnlyBelowClusters;
            if(m.trainStructures==0)throw new InvalidOperationException("Stratum "+m.stratum+" has no training structures");
        }
        foreach(string s in new[] {"E","F","other"})
        {
            var mine=rows.Where(r=>r.stratum==s&&r.split!="excluded-bridge").ToList();
            summaries.Add(new StratumSummary {stratum=s,structures=mine.Count,clusters=mine.Select(r=>r.cluster).Distinct().Count(),
                excludedBridge=rows.Count(r=>r.stratum==s&&r.split=="excluded-bridge"),reportOnly=true});
        }
        if(rows.Any(r=>c1Validation.Contains(r.id)&&r.split=="train")||rows.Any(r=>c1Train.Contains(r.id)&&r.split=="eval"))throw new InvalidOperationException("C1 split not preserved");

        Directory.CreateDirectory(RootC2);
        ClimbCurriculumEntry Entry(StratumEntry r) => new ClimbCurriculumEntry {id=r.id,path=r.path,sha256=r.sha256,cluster=r.cluster};
        var file=new ClimbCurriculumFile {version=2,root="../..",twoStepShare=TwoStepShare,twoStep=c1.twoStep,
            rule=$"v2 train pool only; C2 strata by effective rise (tall >= {TallRise} m) on the geometry route, two standing levels only; per stratum {HeldOutClusters} home clusters "+
                 $"({SmallStratumHeldOutShare*100:0}% below {SmallStratumClusters}) held out with seed {SplitSeed} after forcing C1 validation clusters to eval and C1 training clusters to train; "+
                 $"{excluded.Count} non-C1 structures that linked a C1 validation and a C1 training cluster excluded",
            stages=new[] {new ClimbCurriculumStage {name="C2",pool="c2",structures=new ClimbCurriculumEntry[0],
                pools=StrataC2.Select(s=>new ClimbCurriculumSubPool {pool="c2-"+s,weight=WeightsC2[s],structures=rows.Where(r=>r.stratum==s&&r.split=="train").Select(Entry).ToArray()}).ToArray()}},
            validation=rows.Where(r=>StrataC2.Contains(r.stratum)&&r.split=="eval").Select(Entry).ToArray()};
        File.WriteAllText(RootC2+"/pools.json",JsonUtility.ToJson(file,true));
        var strata=new StrataFile {rule=file.rule,source=ClimbGeneratorV2Sets.Root+"/train/manifest.json",c1Pools=Root+"/pools.json",c1PoolsSha256=ClimbGeneratorV2Sets.Sha256(c1Text),
            splitSeed=SplitSeed,tallRise=TallRise,strata=summaries.ToArray(),structures=rows.ToArray()};
        File.WriteAllText(RootC2+"/strata.json",JsonUtility.ToJson(strata,true));
        var check=ClimbCurriculum.Load(RootC2+"/pools.json");
        Debug.Log("CURRICULUM_C2_FREEZE_COMPLETE "+string.Join(" ",summaries.Select(m=>$"{m.stratum}: n={m.structures} clusters={m.clusters} home={m.homeClusters} "+
            $"train={m.trainStructures}({m.trainClusters}) eval={m.evalStructures}({m.evalClusters}) forcedEval={m.forcedEvalClusters} forcedTrain={m.forcedTrainClusters} excludedBridge={m.excludedBridge} reportOnly={m.reportOnly};"))+
            $" pools={string.Join(",",check.SubPools(0).Select(p=>p.Pool+":"+p.Docs.Length+"@"+p.Weight))}");
    }

    // Cluster = smallest id of the linked component, so the split does not depend on manifest order.
    static List<ClimbCurriculumEntry> Entries(List<ClimbGeneratorV2Sets.Entry> entries)
    {
        var canon=entries.Select(e=>ClimbGeneratorV2Sets.Variants(e.geometryKey)).ToList();
        var parent=Enumerable.Range(0,entries.Count).ToArray();
        int Find(int x){while(parent[x]!=x)x=parent[x]=parent[parent[x]];return x;}
        for(int i=0;i<entries.Count;i++)for(int j=i+1;j<entries.Count;j++)if(ClimbGeneratorV2Sets.Linked(canon[i],canon[j]))parent[Find(i)]=Find(j);
        var name=Enumerable.Range(0,entries.Count).GroupBy(Find).ToDictionary(g=>g.Key,g=>g.Select(i=>entries[i].id).OrderBy(id=>id,StringComparer.Ordinal).First());
        return entries.Select((e,i)=>new ClimbCurriculumEntry {id=e.id,path=e.path,sha256=e.sha256,cluster=name[Find(i)]}).ToList();
    }
}
