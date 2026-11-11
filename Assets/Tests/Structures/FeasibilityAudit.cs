using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using UnityEngine;
using UnityEngine.TestTools;

// Feasibility audit (LAYER3_FEASIBILITY_AUDIT.md): for every frozen evaluation structure, the geometry check and whether the generalized
// scripted controller completes it. Reference upper bound only; the result never filters training or evaluation structures.
public sealed class FeasibilityAudit
{
    readonly List<GameObject> roots=new List<GameObject>();
    float scale;SimulationMode simulation;
    [SetUp] public void Setup(){scale=Time.timeScale;simulation=Physics.simulationMode;}
    [TearDown] public void Cleanup(){foreach(var r in roots)if(r!=null)UnityEngine.Object.DestroyImmediate(r);roots.Clear();Time.timeScale=scale;Physics.simulationMode=simulation;}

    // swap: geometry goes in through StructureSwap (frozen settled document, as curriculum training does) instead of the loader's settlement.
    IEnumerator Climb(List<(string set,string id,string difficulty,StructureDocument doc)> items,List<string> rows,bool swap=false)
    {
        Time.timeScale=25;Physics.simulationMode=SimulationMode.FixedUpdate;
        const int Batch=30;
        for(int start=0;start<items.Count;start+=Batch)
        {
            var batch=items.Skip(start).Take(Batch).ToList();var runs=new List<(int index,BoxStackScriptController controller,FeasibilityReport report)>();
            for(int i=0;i<batch.Count;i++)
            {
                var body=StaticStructureLoader.Load(swap?BoxStackGenerator.Smoke(false):batch[i].doc,new Vector3(600+(i%6)*14,0,600+(i/6)*14),Quaternion.identity);body.name="Audit_"+batch[i].id;roots.Add(body.gameObject);
                if(swap)StructureSwap.Apply(body,batch[i].doc,SwapMaterial);
                var controller=body.gameObject.AddComponent<BoxStackScriptController>();controller.Configure(body,BoxStackScriptController.Route(batch[i].doc));
                runs.Add((i,controller,BoxStackFeasibility.Check(batch[i].doc)));
            }
            Physics.SyncTransforms();float deadline=Time.realtimeSinceStartup+300;
            while(runs.Any(r=>!r.controller.Completed&&!r.controller.Failed)&&Time.realtimeSinceStartup<deadline)yield return new WaitForFixedUpdate();
            foreach(var (index,c,report) in runs)
            {
                var item=batch[index];string F(float v)=>v.ToString("F3",CultureInfo.InvariantCulture);
                rows.Add(string.Join(",",item.set,item.id,item.difficulty,report.passed?1:0,report.reason,string.Join("|",report.route),
                    report.surfaces.Count(s=>s.required),c.Completed?1:0,c.Failed?1:0,c.TierReached,c.TierCount,c.Stage,F(c.Elapsed)));
            }
            foreach(var r in roots)if(r!=null)UnityEngine.Object.DestroyImmediate(r);roots.Clear();
        }
    }

    [UnityTest] public IEnumerator GeneralizedControllerClimbsSmokeLayouts()
    {
        var rows=new List<string>();
        var items=new List<(string,string,string,StructureDocument)>();
        for(int i=0;i<4;i++){items.Add(("smoke","two-step-"+i,"Smoke",BoxStackGenerator.Smoke(true)));items.Add(("smoke","single-"+i,"Smoke",BoxStackGenerator.Smoke(false)));}
        yield return Climb(items,rows);
        Assert.AreEqual(8,rows.Count(r=>r.Split(',')[7]=="1"),string.Join("\n",rows));
    }

    static PhysicsMaterial swapMaterial;
    static PhysicsMaterial SwapMaterial => swapMaterial??(swapMaterial=new PhysicsMaterial("StaticStructurePhysics") {staticFriction=.15f,dynamicFriction=.15f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Average});

    [UnityTest] public IEnumerator GeneralizedControllerClimbsTheSwappedFrozenTwoStep()
    {
        var doc=StructureJson.Parse(File.ReadAllText("structures/curriculum-v1/two-step.json"));var rows=new List<string>();
        yield return Climb(Enumerable.Range(0,4).Select(i=>("swap","two-step-"+i,"Smoke",doc)).ToList(),rows,true);
        Assert.AreEqual(4,rows.Count(r=>r.Split(',')[7]=="1"),string.Join("\n",rows));
    }

    // Solvability check before curriculum training (AGENTS.md): the scripted controller on every C0/C1 structure, swapped in as training
    // does. Reference only; nothing is filtered by the outcome.
    [UnityTest,Explicit("Audit; run by name")]
    public IEnumerator AuditCurriculumPools()
    {
        if(Environment.GetEnvironmentVariable("CLIMB_RUN_DIAGNOSTICS")!="1")Assert.Ignore("Set CLIMB_RUN_DIAGNOSTICS=1 to run this audit");
        var file=JsonUtility.FromJson<PoolsView>(File.ReadAllText("structures/curriculum-v1/pools.json"));
        var items=new List<(string set,string id,string difficulty,StructureDocument doc)> {("two-step",file.twoStep.id,"Control",StructureJson.Parse(File.ReadAllText(file.twoStep.path)))};
        foreach(var stage in file.stages)foreach(var e in stage.structures)items.Add((stage.pool,e.id,"Easy",StructureJson.Parse(File.ReadAllText(e.path))));
        foreach(var e in file.validation)items.Add(("c1-validation",e.id,"Easy",StructureJson.Parse(File.ReadAllText(e.path))));
        var rows=new List<string>();yield return Climb(items,rows,true);
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","results","curriculum-audit"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"audit.csv"),"set,id,difficulty,feasible,reason,route,requiredStanding,controllerSuccess,controllerFailed,tierReached,tierCount,stage,seconds\n"+string.Join("\n",rows)+"\n");
        Debug.Log($"CURRICULUM_AUDIT_COMPLETE rows={rows.Count} folder={folder}");
        Assert.AreEqual(items.Count,rows.Count);
    }
    [Serializable] sealed class PoolsView {public EntryView twoStep;public StageView[] stages;public EntryView[] validation;}
    [Serializable] sealed class StageView {public string pool;public EntryView[] structures;}

    [UnityTest,Explicit("Audit; run by name")]
    public IEnumerator AuditFrozenEvaluationSets()
    {
        if(Environment.GetEnvironmentVariable("CLIMB_RUN_DIAGNOSTICS")!="1")Assert.Ignore("Set CLIMB_RUN_DIAGNOSTICS=1 to run this audit");
        var items=new List<(string set,string id,string difficulty,StructureDocument doc)> {
            ("control","training-two-step","Control",BoxStackGenerator.Smoke(true)),("control","single-tall","Control",BoxStackGenerator.Smoke(false))};
        void Manifest(string set,string path)
        {
            var m=JsonUtility.FromJson<ManifestView>(File.ReadAllText(path));
            foreach(var e in m.structures)items.Add((set,e.id,e.difficulty,StructureJson.Parse(File.ReadAllText(e.path))));
        }
        Manifest("zero-shot-v1","structures/zero-shot-v1/manifest.json");Manifest("v2-eval","structures/box-stacks-v2/eval/manifest.json");
        var rows=new List<string>();yield return Climb(items,rows);
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"..","results","feasibility-audit"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"audit.csv"),"set,id,difficulty,feasible,reason,route,requiredStanding,controllerSuccess,controllerFailed,tierReached,tierCount,stage,seconds\n"+string.Join("\n",rows)+"\n");
        Debug.Log($"FEASIBILITY_AUDIT_COMPLETE rows={rows.Count} folder={folder}");
        Assert.AreEqual(items.Count,rows.Count);
    }
    [Serializable] sealed class ManifestView {public EntryView[] structures;}
    [Serializable] sealed class EntryView {public string id,difficulty,path;}
}
