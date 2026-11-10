using System.IO;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StaticStructuresEditor
{
    [MenuItem("Tools/Structures/Generate Static Examples")]
    public static void GenerateExamples()
    {
        Directory.CreateDirectory("structures");
        StaticStabilityValidator.SaveVerified("structures/easy-baseline.json",StaticWallGenerator.Generate(StaticDifficulty.Easy,0));
        foreach(StaticDifficulty difficulty in System.Enum.GetValues(typeof(StaticDifficulty)))
            StaticStabilityValidator.SaveVerified("structures/"+difficulty.ToString().ToLowerInvariant()+"-seed42.json",StaticWallGenerator.Generate(difficulty,42));
        Debug.Log("STATIC_STRUCTURES_EXAMPLES_OK");
    }
    [MenuItem("Tools/Structures/Build Static JSON Demo")]
    public static void BuildDemo()
    {
        GenerateExamples();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var body=StaticStructureLoader.LoadFile("structures/easy-baseline.json",Vector3.zero,Quaternion.identity,true);
        const string materialPath="Assets/Materials/StaticStructurePhysics.physicMaterial";
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
        if(material==null){material=new PhysicsMaterial("StaticStructurePhysics") {staticFriction=.15f,dynamicFriction=.15f,frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Average,bounciness=0};AssetDatabase.CreateAsset(material,materialPath);}
        foreach(var collider in body.GetComponentsInChildren<Collider>())collider.sharedMaterial=material;
        var resources=body.GetComponent<StructureResources>();
        foreach(var transient in resources.Materials)Object.DestroyImmediate(transient);resources.Materials.Clear();
        var camera=new GameObject("Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(3,3,-5);camera.transform.LookAt(new Vector3(0,1.5f,.2f));
        var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(40,-30,0);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/StaticStructureDemo.unity");AssetDatabase.SaveAssets();
        Debug.Log("STATIC_STRUCTURES_DEMO_OK");
    }
}
