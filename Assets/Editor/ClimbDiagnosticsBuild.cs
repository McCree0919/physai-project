using System.IO;
using PhysicalAI.Learning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ClimbDiagnosticsBuild
{
    public static void Build()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/ClimbTraining.unity",OpenSceneMode.Single);int count=0;
        foreach(var root in scene.GetRootGameObjects())if(root.GetComponent<ClimberAgent>()!=null){root.AddComponent<ClimbDiagnosticsObserver>();count++;}
        if(count!=16)throw new BuildFailedException("Expected exactly 16 unchanged training arenas");
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/ClimbDiagnostics.unity");Directory.CreateDirectory("Builds/ClimbDiagnostics");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[] {"Assets/Scenes/ClimbDiagnostics.unity"},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/ClimbDiagnostics/ClimbDiagnostics.exe"});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Diagnostic build failed");
        Debug.Log("CLIMB_DIAGNOSTIC_BUILD_OK");
    }
}
