using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PhysicalAI.Learning;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Zero-shot evaluation (LAYER3_ZERO_SHOT.md): freeze generated box stacks never used in training, build one arena per structure
// (plus the training two-step as control) with the unchanged Agent/observer, and run it in Editor play mode for a Python driver.
// Selection never looks at any controller or policy outcome: generation order, exact-geometry dedupe, training exclusion, settlement only.
public static class ClimbZeroShot
{
    public const string Folder="structures/zero-shot-v1",ScenePath="Assets/Scenes/ClimbZeroShot.unity",ControlId="control-training-two-step";
    public const int PerDifficulty=30,FirstSeed=10000,SeedBudget=2000;
    const float Spacing=14;
    [Serializable] public sealed class Entry
    {
        public string id,difficulty,path,sha256,geometryKey;public int seed;public BoxStackFeatures features;public float maxSettleDisplacement;
    }
    [Serializable] public sealed class Skip {public string difficulty,reason;public int seed;}
    [Serializable] public sealed class Manifest
    {
        public string rule,unityVersion,trainingGeometryKey;public int generatorVersion,firstSeed,seedBudget,perDifficulty;
        public Entry[] structures;public Skip[] skipped;
    }

    // Axis-aligned box bounds on the 0.05 m grid, sorted; ids, order, masses and goal (derived from geometry) are ignored.
    public static string GeometryKey(StructureDocument d)
    {
        int Q(float v)=>Mathf.RoundToInt(v/.05f);
        return string.Join(";",d.objects.Where(o=>o.settleAsDynamic).Select(o=>
        {
            Vector3 p=o.pose.Position,s=o.pose.Rotation*StructureJson.Vector(o.size);s=new Vector3(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));
            Vector3 lo=p-s*.5f,hi=p+s*.5f;return $"{Q(lo.x)},{Q(lo.y)},{Q(lo.z)},{Q(hi.x)},{Q(hi.y)},{Q(hi.z)}";
        }).OrderBy(x=>x,StringComparer.Ordinal));
    }
    static string Sha256(string text){using(var h=SHA256.Create())return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(b=>b.ToString("x2")));}

    [MenuItem("Tools/Climb/Freeze Zero-Shot Evaluation Set")]
    public static void Freeze()
    {
        if(Directory.Exists(Folder)&&Directory.GetFiles(Folder,"*.json").Length>0)throw new InvalidOperationException(Folder+" is frozen; delete it explicitly to regenerate");
        Directory.CreateDirectory(Folder);
        string training=GeometryKey(BoxStackGenerator.Smoke(true));
        var entries=new List<Entry>();var skipped=new List<Skip>();var seen=new Dictionary<string,string>();
        foreach(BoxDifficulty difficulty in Enum.GetValues(typeof(BoxDifficulty)))
        {
            int accepted=0;
            for(int seed=FirstSeed;seed<FirstSeed+SeedBudget&&accepted<PerDifficulty;seed++)
            {
                StructureDocument doc;
                try{doc=BoxStackGenerator.Generate(difficulty,seed);}
                catch(Exception e){skipped.Add(new Skip {difficulty=difficulty.ToString(),seed=seed,reason="Generate:"+e.GetType().Name});continue;}
                string key=GeometryKey(doc);
                if(key==training){skipped.Add(new Skip {difficulty=difficulty.ToString(),seed=seed,reason="TrainingStructure"});continue;}
                if(seen.TryGetValue(key,out string first)){skipped.Add(new Skip {difficulty=difficulty.ToString(),seed=seed,reason="DuplicateOf:"+first});continue;}
                string id=difficulty.ToString().ToLowerInvariant()+"-"+seed;seen[key]=id;
                var report=BoxStackSettlement.Settle(doc,out var settled);
                if(!report.passed){skipped.Add(new Skip {difficulty=difficulty.ToString(),seed=seed,reason="Settlement:"+report.reason});continue;}
                settled.validation=report;settled.validation.inputHash=StructureJson.Hash(settled);
                string path=Folder+"/"+id+".json",json=StructureJson.Serialize(settled);File.WriteAllText(path,json);
                entries.Add(new Entry {id=id,difficulty=difficulty.ToString(),seed=seed,path=path,sha256=Sha256(json),geometryKey=key,features=settled.generator.features,
                    maxSettleDisplacement=report.boxes.Max(b=>b.displacement)});
                accepted++;
            }
            Debug.Log($"ZERO_SHOT_FREEZE {difficulty} accepted={accepted} seedsTried={skipped.Count(s=>s.difficulty==difficulty.ToString())+accepted}");
        }
        var manifest=new Manifest {rule="seeds "+FirstSeed+".. in order per difficulty; skip exact duplicates (0.05 m grid bounds key), the training two-step and failed settlements; first "+PerDifficulty+" kept; no controller or policy outcome used",
            unityVersion=Application.unityVersion,trainingGeometryKey=training,generatorVersion=1,firstSeed=FirstSeed,seedBudget=SeedBudget,perDifficulty=PerDifficulty,
            structures=entries.ToArray(),skipped=skipped.ToArray()};
        File.WriteAllText(Folder+"/manifest.json",JsonUtility.ToJson(manifest,true));
        Debug.Log("ZERO_SHOT_FREEZE_COMPLETE structures="+entries.Count);
    }

    // Evaluation sets: "zero-shot" (default) = arena 0 is the training two-step (generated exactly as ClimbTrainingBuilder does), then every
    // frozen structure in manifest order; "training16" = the 16 identical training two-step arenas of ClimbTraining/ClimbDiagnostics.
    public const string SetArgument="--climb-eval-set=";
    static string EvalSet()
    {
        foreach(string arg in Environment.GetCommandLineArgs())if(arg.StartsWith(SetArgument))return arg.Substring(SetArgument.Length);
        return "zero-shot";
    }
    public static int BuildScene()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("ClimbAcademyClock").AddComponent<ClimbAcademyClock>();
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/StaticStructurePhysics.physicMaterial");
        if(material==null)throw new InvalidOperationException("Missing Assets/Materials/StaticStructurePhysics.physicMaterial");
        string set=EvalSet();var docs=new List<(string id,StructureDocument doc)>();
        if(set=="training16")for(int i=0;i<16;i++)docs.Add(("training-two-step",BoxStackGenerator.Smoke(true)));
        else if(set=="zero-shot")
        {
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder+"/manifest.json"));
            docs.Add((ControlId,BoxStackGenerator.Smoke(true)));
            foreach(var e in manifest.structures)
            {
                string json=File.ReadAllText(e.path);if(Sha256(json)!=e.sha256)throw new InvalidDataException("Manifest hash mismatch: "+e.path);
                docs.Add((e.id,StructureJson.Parse(json)));
            }
        }
        else throw new ArgumentException("Unknown evaluation set "+set);
        for(int i=0;i<docs.Count;i++)
        {
            var body=StaticStructureLoader.Load(docs[i].doc,new Vector3((i%8)*Spacing,0,(i/8)*Spacing),Quaternion.identity);
            body.name=$"ZS{i:000}_{docs[i].id}";ClimbTrainingBuilder.PrepareEnvironment(body);
            foreach(var c in body.GetComponentsInChildren<Collider>())c.sharedMaterial=material;
            var resources=body.GetComponent<StructureResources>();foreach(var transient in resources.Materials)UnityEngine.Object.DestroyImmediate(transient);resources.Materials.Clear();
            ClimbTrainingBuilder.AttachAgent(body);body.gameObject.AddComponent<ClimbDiagnosticsObserver>();
        }
        var camera=new GameObject("Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(3,3,-4);camera.transform.LookAt(new Vector3(0,1,1));
        var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(40,-30,0);
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("ZERO_SHOT_SCENE_OK set="+set+" arenas="+docs.Count);return docs.Count;
    }

    // Batchmode entry (no -quit): build the scene, enter play mode; the Python driver connects on the editor port and quits it when done.
    // Editor startup requests a recompile ("Define symbols changed") a few seconds in; entering play mode before it finishes reloads the
    // domain mid-play and the Academy re-handshakes. So play starts only after 10 s without compiling. Hooks survive reloads via session flags.
    const string PlayFlag="ClimbZeroShot.PlayInEditor",PendingFlag="ClimbZeroShot.PlayPending";
    const double QuietSeconds=10;
    static double quietSince;
    public static void PlayInEditor()
    {
        BuildScene();SessionState.SetBool(PlayFlag,true);SessionState.SetBool(PendingFlag,true);Hook();
    }
    [InitializeOnLoadMethod] static void Hook()
    {
        if(SessionState.GetBool(PlayFlag,false)){EditorApplication.playModeStateChanged-=OnPlayMode;EditorApplication.playModeStateChanged+=OnPlayMode;}
        if(SessionState.GetBool(PendingFlag,false)){quietSince=EditorApplication.timeSinceStartup;EditorApplication.update-=WaitThenPlay;EditorApplication.update+=WaitThenPlay;}
    }
    static void WaitThenPlay()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating){quietSince=EditorApplication.timeSinceStartup;return;}
        if(EditorApplication.timeSinceStartup-quietSince<QuietSeconds)return;
        EditorApplication.update-=WaitThenPlay;SessionState.EraseBool(PendingFlag);Debug.Log("ZERO_SHOT_ENTER_PLAY");EditorApplication.EnterPlaymode();
    }
    static void OnPlayMode(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredEditMode)return;
        SessionState.EraseBool(PlayFlag);Debug.Log("ZERO_SHOT_PLAY_FINISHED");EditorApplication.Exit(0);
    }
}
