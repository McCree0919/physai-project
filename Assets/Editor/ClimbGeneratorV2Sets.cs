using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEngine;

// Frozen generator-v2 sets (LAYER3_GENERATOR_V2.md §4). Evaluation: per difficulty, seeds in order, kept when settlement passes and the
// structure is not linked (LAYER3_DESIGN §11: same support graph, every box bound within 0.05 m, mirror images included) to the training
// two-step or to any kept evaluation structure, so every kept structure is its own independent cluster. Training pool: a disjoint seed
// range, kept when settlement passes, not an exact duplicate, and not linked to any evaluation structure. Feasibility is recorded for the
// audit only; no controller, policy or feasibility outcome is used to keep or drop a structure.
public static class ClimbGeneratorV2Sets
{
    public const string Root="structures/box-stacks-v2";
    public const int EvalPerDifficulty=60,TrainPerDifficulty=300,EvalFirstSeed=20000,TrainFirstSeed=100000,SeedBudget=5000;
    [Serializable] public sealed class Entry
    {
        public string id,difficulty,path,sha256,geometryKey;public int seed;public BoxStackFeatures features;public FeasibilityReport feasibility;
        public int nearestEvalUnits=-1;public string nearestEval; // training only: max box-bound difference (0.05 m units) to the closest same-graph eval structure
    }
    [Serializable] public sealed class Skip {public string difficulty,reason;public int seed;}
    [Serializable] public sealed class Manifest
    {
        public string set,rule,unityVersion;public int generatorVersion,firstSeed,perDifficulty;public float minTreadDepth,minTreadWidth,reach,headroom;
        public Entry[] structures;public Skip[] skipped;
    }

    sealed class Canon {public int[][] Boxes;public string Graph;}
    static int[][] Parse(string key) => key.Split(';').Select(b=>b.Split(',').Select(int.Parse).ToArray()).ToArray();
    static Canon Canonical(int[][] boxes)
    {
        var c=boxes.OrderBy(b=>b[1]).ThenBy(b=>b[2]).ThenBy(b=>b[0]).ThenBy(b=>b[4]).ThenBy(b=>b[5]).ThenBy(b=>b[3]).ToArray();
        var edges=new List<string>();
        for(int i=0;i<c.Length;i++)for(int j=0;j<c.Length;j++)
            if(i!=j&&Math.Abs(c[i][4]-c[j][1])<=1&&c[i][0]<c[j][3]&&c[j][0]<c[i][3]&&c[i][2]<c[j][5]&&c[j][2]<c[i][5])edges.Add(i+">"+j);
        return new Canon {Boxes=c,Graph=c.Length+"|"+string.Join(",",edges)+"|"+string.Join("",c.Select(b=>b[1]<=1?"g":"-"))};
    }
    static Canon[] Variants(string key)
    {
        var boxes=Parse(key);var mirrored=boxes.Select(b=>new[] {-b[3],b[1],b[2],-b[0],b[4],b[5]}).ToArray();
        return new[] {Canonical(boxes),Canonical(mirrored)};
    }
    // Max bound difference in grid units if a and b share a support graph (either mirror), else -1.
    static int Distance(Canon[] a,Canon[] b)
    {
        int best=-1;
        foreach(var y in b)
        {
            var x=a[0];if(x.Graph!=y.Graph)continue;
            int d=0;for(int i=0;i<x.Boxes.Length;i++)for(int k=0;k<6;k++)d=Math.Max(d,Math.Abs(x.Boxes[i][k]-y.Boxes[i][k]));
            best=best<0?d:Math.Min(best,d);
        }
        return best;
    }
    static bool Linked(Canon[] a,Canon[] b){int d=Distance(a,b);return d>=0&&d<=1;}
    static string Sha256(string text){using(var h=SHA256.Create())return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2")));}

    [MenuItem("Tools/Climb/Freeze Generator V2 Sets")]
    public static void Freeze()
    {
        if(Directory.Exists(Root)&&Directory.GetFiles(Root,"*.json",SearchOption.AllDirectories).Length>0)throw new InvalidOperationException(Root+" is frozen; delete it explicitly to regenerate");
        var training=Variants(ClimbZeroShot.GeometryKey(BoxStackGenerator.Smoke(true)));
        // Single boxes form only ~4 independent clusters (Step, Tall, Wide in two orientations); curriculum stage C0 trains on them, so they
        // are reserved for training and never enter the evaluation set (decided before any evaluation of v2 structures).
        var eval=Build("eval",EvalFirstSeed,EvalPerDifficulty,(doc,key,kept)=>
            SingleBox(doc)?"SingleBoxReservedForTraining":Linked(key,training)?"LinkedToTrainingTwoStep":kept.Where(k=>Linked(key,k.canon)).Select(k=>"LinkedTo:"+k.id).FirstOrDefault(),null);
        var evalCanon=eval.Select(e=>(e.id,canon:Variants(e.geometryKey))).ToList();
        Build("train",TrainFirstSeed,TrainPerDifficulty,(doc,key,kept)=>
            evalCanon.Where(e=>Linked(key,e.canon)).Select(e=>"LinkedToEval:"+e.id).FirstOrDefault()??(kept.Any(k=>Distance(key,k.canon)==0)?"ExactDuplicate":null),evalCanon);
        Debug.Log("GENERATOR_V2_FREEZE_COMPLETE");
    }
    static bool SingleBox(StructureDocument d) => d.objects.Count(o=>o.settleAsDynamic)==1;
    static List<Entry> Build(string set,int firstSeed,int perDifficulty,Func<StructureDocument,Canon[],List<(string id,Canon[] canon)>,string> reject,List<(string id,Canon[] canon)> evalCanon)
    {
        string folder=Root+"/"+set;Directory.CreateDirectory(folder);
        var entries=new List<Entry>();var skipped=new List<Skip>();
        foreach(BoxDifficulty difficulty in Enum.GetValues(typeof(BoxDifficulty)))
        {
            var kept=new List<(string id,Canon[] canon)>();
            for(int seed=firstSeed;seed<firstSeed+SeedBudget&&kept.Count<perDifficulty;seed++)
            {
                void Drop(string why)=>skipped.Add(new Skip {difficulty=difficulty.ToString(),seed=seed,reason=why});
                StructureDocument doc;
                try{doc=BoxStackGeneratorV2.Generate(difficulty,seed);}catch(Exception e){Drop("Generate:"+e.GetType().Name+":"+e.Message);continue;}
                string key=ClimbZeroShot.GeometryKey(doc);var canon=Variants(key);
                string why=reject(doc,canon,kept);if(why!=null){Drop(why);continue;}
                var report=BoxStackSettlement.Settle(doc,out var settled);
                if(!report.passed){Drop("Settlement:"+report.reason);continue;}
                settled.validation=report;settled.validation.inputHash=StructureJson.Hash(settled);
                string id=doc.id,path=folder+"/"+id+".json",json=StructureJson.Serialize(settled);File.WriteAllText(path,json);
                var entry=new Entry {id=id,difficulty=difficulty.ToString(),seed=seed,path=path,sha256=Sha256(json),geometryKey=key,features=settled.generator.features,
                    feasibility=BoxStackFeasibility.Check(settled)};
                if(evalCanon!=null)
                {
                    foreach(var e in evalCanon){int d=Distance(canon,e.canon);if(d>=0&&(entry.nearestEvalUnits<0||d<entry.nearestEvalUnits)){entry.nearestEvalUnits=d;entry.nearestEval=e.id;}}
                }
                entries.Add(entry);kept.Add((id,canon));
            }
            Debug.Log($"GENERATOR_V2_{set.ToUpperInvariant()} {difficulty} kept={kept.Count} skipped={skipped.Count(s=>s.difficulty==difficulty.ToString())}");
            if(set=="eval"&&kept.Count<50)Debug.LogError($"GENERATOR_V2_SHORT {difficulty}: only {kept.Count} independent evaluation structures within {SeedBudget} seeds (need >= 50)");
        }
        var manifest=new Manifest {set=set,unityVersion=Application.unityVersion,generatorVersion=BoxStackGeneratorV2.Version,firstSeed=firstSeed,perDifficulty=perDifficulty,
            minTreadDepth=ClimberGeometry.MinTreadDepth,minTreadWidth=ClimberGeometry.MinTreadWidth,reach=ClimberGeometry.Reach,headroom=ClimberGeometry.Headroom,
            rule=set=="eval"?"seeds in order; single boxes reserved for training; kept if settlement passes and not linked (same support graph, all bounds within 0.05 m, mirrors included) to the training two-step or any kept eval structure":
                "disjoint seed range; kept if settlement passes, not an exact duplicate, and not linked to any eval structure",
            structures=entries.ToArray(),skipped=skipped.ToArray()};
        File.WriteAllText(folder+"/manifest.json",JsonUtility.ToJson(manifest,true));
        return entries;
    }
}
