using System.IO;
using PhysicalAI.Climber.Layer2Prep;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class Layer2PrepBuild
{
    public static void Build()
    {
        const string materialPath="Assets/Materials/Layer2PrepPhysics.physicMaterial";
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
        if(material==null){material=new PhysicsMaterial("Layer2PrepPhysics");AssetDatabase.CreateAsset(material,materialPath);}
        material.staticFriction=material.dynamicFriction=.15f;material.frictionCombine=PhysicsMaterialCombine.Average;material.bounceCombine=PhysicsMaterialCombine.Average;material.bounciness=0;
        EditorUtility.SetDirty(material);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("Layer2PrepRunner").AddComponent<PrepExperimentRunner>().Material=material;
        const string path="Assets/Scenes/Layer2Prep.unity";
        EditorSceneManager.SaveScene(scene,path);AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Builds/Layer2Prep");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{path},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/Layer2Prep/Layer2Prep.exe",options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Layer2Prep build failed");
        Debug.Log("LAYER2_PREP_BUILD_OK");
    }
}
