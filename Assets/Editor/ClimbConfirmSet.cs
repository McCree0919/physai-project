using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEngine;

// One-shot layer 3 confirmation set (docs/LAYER3_CONFIRMATION_PREREG.md, 2026-11-12). Same generator, settlement and link rule as the v2
// evaluation set (ClimbGeneratorV2Sets: same support graph, every box bound within 0.05 m, mirror images included), with a new seed range
// and stricter separation: a candidate is dropped if it is a single box, or linked to the training two-step, to any v2 evaluation
// structure, to any v2 training structure (a superset of every C0, C1 and C2 pool), or to any already kept confirmation structure of ANY
// difficulty, so the three difficulties are not linked to each other either. Feasibility is recorded only; no controller, policy or
// feasibility outcome is used to keep or drop a structure.
public static class ClimbConfirmSet
{
    public const string Root="structures/box-stacks-v3-confirm";
    public const int PerDifficulty=60,FirstSeed=300000,SeedBudget=5000;

    // Diagnostic only (writes nothing): how many Easy structures pass the separation rules (before settlement) as the seed budget grows.
    public static void CountEasyCandidates()
    {
        var reference=new List<ClimbGeneratorV2Sets.Canon[]> {ClimbGeneratorV2Sets.Variants(ClimbZeroShot.GeometryKey(BoxStackGenerator.Smoke(true)))};
        foreach(var set in new[] {"eval","train"})
            foreach(var e in JsonUtility.FromJson<ClimbGeneratorV2Sets.Manifest>(File.ReadAllText(ClimbGeneratorV2Sets.Root+"/"+set+"/manifest.json")).structures)reference.Add(ClimbGeneratorV2Sets.Variants(e.geometryKey));
        var kept=new List<ClimbGeneratorV2Sets.Canon[]>();var marks=new List<string>();
        for(int i=0;i<50000;i++)
        {
            var doc=BoxStackGeneratorV2.Generate(BoxDifficulty.Easy,FirstSeed+i);
            if(doc.objects.Count(o=>o.settleAsDynamic)!=1)
            {
                var canon=ClimbGeneratorV2Sets.Variants(ClimbZeroShot.GeometryKey(doc));
                if(!reference.Any(r=>ClimbGeneratorV2Sets.Linked(canon,r))&&!kept.Any(k=>ClimbGeneratorV2Sets.Linked(canon,k)))kept.Add(canon);
            }
            if((i+1)%5000==0)marks.Add($"{i+1}:{kept.Count}");
        }
        Debug.Log("CONFIRM_EASY_CANDIDATES seeds:independent "+string.Join(" ",marks));
    }

    [MenuItem("Tools/Climb/Freeze Layer 3 Confirmation Set")]
    public static void Freeze()
    {
        if(Directory.Exists(Root))throw new InvalidOperationException(Root+" exists; the confirmation set is frozen once");
        var reference=new List<(string id,ClimbGeneratorV2Sets.Canon[] canon)> {("training-two-step",ClimbGeneratorV2Sets.Variants(ClimbZeroShot.GeometryKey(BoxStackGenerator.Smoke(true))))};
        foreach(var set in new[] {"eval","train"})
            foreach(var e in JsonUtility.FromJson<ClimbGeneratorV2Sets.Manifest>(File.ReadAllText(ClimbGeneratorV2Sets.Root+"/"+set+"/manifest.json")).structures)
                reference.Add(("v2-"+set+":"+e.id,ClimbGeneratorV2Sets.Variants(e.geometryKey)));
        Directory.CreateDirectory(Root);
        var entries=new List<ClimbGeneratorV2Sets.Entry>();var skipped=new List<ClimbGeneratorV2Sets.Skip>();
        var kept=new List<(string id,ClimbGeneratorV2Sets.Canon[] canon)>();  // shared by all difficulties
        var shortTiers=new List<string>();
        foreach(BoxDifficulty difficulty in Enum.GetValues(typeof(BoxDifficulty)))
        {
            int count=0;
            for(int seed=FirstSeed;seed<FirstSeed+SeedBudget&&count<PerDifficulty;seed++)
            {
                void Drop(string why)=>skipped.Add(new ClimbGeneratorV2Sets.Skip {difficulty=difficulty.ToString(),seed=seed,reason=why});
                StructureDocument doc;
                try{doc=BoxStackGeneratorV2.Generate(difficulty,seed);}catch(Exception ex){Drop("Generate:"+ex.GetType().Name+":"+ex.Message);continue;}
                doc.id="confirm-"+doc.id;  // ids stay distinct from the v2 sets
                string key=ClimbZeroShot.GeometryKey(doc);var canon=ClimbGeneratorV2Sets.Variants(key);
                if(doc.objects.Count(o=>o.settleAsDynamic)==1){Drop("SingleBox");continue;}
                string linked=reference.Where(r=>ClimbGeneratorV2Sets.Linked(canon,r.canon)).Select(r=>"LinkedTo:"+r.id).FirstOrDefault()
                    ??kept.Where(k=>ClimbGeneratorV2Sets.Linked(canon,k.canon)).Select(k=>"LinkedToConfirm:"+k.id).FirstOrDefault();
                if(linked!=null){Drop(linked);continue;}
                var report=BoxStackSettlement.Settle(doc,out var settled);
                if(!report.passed){Drop("Settlement:"+report.reason);continue;}
                settled.validation=report;settled.validation.inputHash=StructureJson.Hash(settled);
                string id=settled.id,path=Root+"/"+id+".json",json=StructureJson.Serialize(settled);File.WriteAllText(path,json);
                var entry=new ClimbGeneratorV2Sets.Entry {id=id,difficulty=difficulty.ToString(),seed=seed,path=path,sha256=ClimbGeneratorV2Sets.Sha256(json),geometryKey=key,
                    features=settled.generator.features,feasibility=BoxStackFeasibility.Check(settled)};
                foreach(var r in reference){int d=ClimbGeneratorV2Sets.Distance(canon,r.canon);if(d>=0&&(entry.nearestEvalUnits<0||d<entry.nearestEvalUnits)){entry.nearestEvalUnits=d;entry.nearestEval=r.id;}}
                entries.Add(entry);kept.Add((id,canon));count++;
            }
            Debug.Log($"CONFIRM_SET {difficulty} kept={count} skipped={skipped.Count(s=>s.difficulty==difficulty.ToString())} reasons="+string.Join(",",
                skipped.Where(s=>s.difficulty==difficulty.ToString()).GroupBy(s=>s.reason.StartsWith("LinkedTo:v2-eval")?"LinkedToV2Eval":s.reason.StartsWith("LinkedTo:v2-train")?"LinkedToV2Train":
                    s.reason.StartsWith("LinkedToConfirm")?"LinkedToConfirm":s.reason.Split(':')[0]).OrderByDescending(g=>g.Count()).Select(g=>g.Key+"="+g.Count())));
            if(count<PerDifficulty)shortTiers.Add($"{difficulty}:{count}");
        }
        if(shortTiers.Count>0)throw new InvalidOperationException($"Too few confirmation structures within {SeedBudget} seeds per difficulty: {string.Join(",",shortTiers)} (need {PerDifficulty}); no manifest written");
        var manifest=new ClimbGeneratorV2Sets.Manifest {set="confirm",unityVersion=Application.unityVersion,generatorVersion=BoxStackGeneratorV2.Version,firstSeed=FirstSeed,perDifficulty=PerDifficulty,
            minTreadDepth=ClimberGeometry.MinTreadDepth,minTreadWidth=ClimberGeometry.MinTreadWidth,reach=ClimberGeometry.Reach,headroom=ClimberGeometry.Headroom,
            rule="seeds from 300000 in order; single boxes dropped; kept if settlement passes and not linked (same support graph, all bounds within 0.05 m, mirrors included) "+
                 "to the training two-step, any v2 eval or v2 train structure, or any kept confirmation structure of any difficulty; nearestEval = closest reference structure",
            structures=entries.ToArray(),skipped=skipped.ToArray()};
        File.WriteAllText(Root+"/manifest.json",JsonUtility.ToJson(manifest,true));
        Debug.Log("CONFIRM_SET_FREEZE_COMPLETE "+entries.Count);
    }
}
