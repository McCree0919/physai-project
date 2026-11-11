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
