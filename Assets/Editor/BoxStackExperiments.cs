using System;
using System.Collections.Generic;
using System.IO;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BoxStackExperiments
{
    [Serializable] public sealed class Trial
    {
        public string difficulty,reason,path;public int seed;public bool passed;public BoxStackFeatures features;public StabilityReport settlement;
    }
    [Serializable] public sealed class Report {public Trial[] trials;}
    [MenuItem("Tools/Structures/Generate Box Stack Samples")]
    public static void Run()
    {
        Directory.CreateDirectory("results/box-stacks/candidates");Directory.CreateDirectory("structures/box-stacks");var trials=new List<Trial>();
        foreach(BoxDifficulty difficulty in Enum.GetValues(typeof(BoxDifficulty)))for(int seed=0;seed<20;seed++)
        {
            var trial=new Trial {difficulty=difficulty.ToString(),seed=seed};
            try
            {
                var doc=BoxStackGenerator.Generate(difficulty,seed);trial.features=doc.generator.features;
                File.WriteAllText("results/box-stacks/candidates/"+difficulty+"-"+seed+".json",StructureJson.Serialize(doc));
                trial.settlement=BoxStackSettlement.Settle(doc,out var settled);trial.passed=trial.settlement.passed;trial.reason=trial.settlement.reason;
                if(trial.passed)
                {
                    trial.features=settled.generator.features;settled.validation=trial.settlement;settled.validation.inputHash=StructureJson.Hash(settled);
                    trial.path="structures/box-stacks/"+difficulty.ToString().ToLowerInvariant()+"-"+seed+".json";
                    File.WriteAllText(trial.path,StructureJson.Serialize(settled));
                }
            }
            catch(Exception e){trial.passed=false;trial.reason=e.GetType().Name+":"+e.Message;}
            trials.Add(trial);Debug.Log("BOX_STACK_TRIAL "+JsonUtility.ToJson(trial));
        }
        File.WriteAllText("results/box-stacks/trials.json",JsonUtility.ToJson(new Report {trials=trials.ToArray()},true));
        Debug.Log("BOX_STACK_SAMPLES_COMPLETE count="+trials.Count);
    }
    [MenuItem("Tools/Structures/Build Box Smoke Scenes")]
    public static void BuildSmokeScenes()
    {
        foreach(bool stairs in new[] {false,true})
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var body=StaticStructureLoader.Load(BoxStackGenerator.Smoke(stairs),Vector3.zero,Quaternion.identity);
            body.gameObject.AddComponent<BoxClimbSmokeController>().Configure(body,stairs);
            var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/StaticStructurePhysics.physicMaterial");
            if(material==null)throw new InvalidOperationException("Generate Static JSON Demo first to create its explicit material");
            foreach(var c in body.GetComponentsInChildren<Collider>())c.sharedMaterial=material;
            var resources=body.GetComponent<StructureResources>();foreach(var m in resources.Materials)UnityEngine.Object.DestroyImmediate(m);resources.Materials.Clear();
            var camera=new GameObject("Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(3,3,-4);camera.transform.LookAt(new Vector3(0,1,.8f));
            var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(40,-30,0);
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/Box"+(stairs?"Steps":"Single")+"Smoke.unity");
        }
        AssetDatabase.SaveAssets();Debug.Log("BOX_SMOKE_SCENES_OK");
    }
    public static void Prepare(){BuildSmokeScenes();Run();}
}
