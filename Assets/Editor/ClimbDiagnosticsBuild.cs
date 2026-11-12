using System.IO;
using System.Linq;
using PhysicalAI.Learning;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ClimbDiagnosticsBuild
{
    const string Training="Assets/Scenes/ClimbTraining.unity",Diagnostics="Assets/Scenes/ClimbDiagnostics.unity";

    // Unchanged earlier entry point: regenerate the diagnostic scene, then build it to Builds/ClimbDiagnostics.
    public static void Build()
    {
        PrepareScene();
        BuildPlayer("Builds/ClimbDiagnostics/ClimbDiagnostics.exe");
    }

    // The training scene's 16 arenas, each with the passive ClimbDiagnosticsObserver. Run where the scene is committed (the worktree), so a
    // player build elsewhere only reads committed scenes.
    public static void PrepareScene()
    {
        var scene=EditorSceneManager.OpenScene(Training,OpenSceneMode.Single);int count=0;
        foreach(var root in scene.GetRootGameObjects())if(root.GetComponent<ClimberAgent>()!=null){root.AddComponent<ClimbDiagnosticsObserver>();count++;}
        if(count!=16)throw new BuildFailedException("Expected exactly 16 unchanged training arenas");
        EditorSceneManager.SaveScene(scene,Diagnostics);
        Debug.Log("CLIMB_DIAGNOSTIC_SCENE_OK");
    }

    // C2 (LAYER3_C2_DESIGN §5): build the committed diagnostic scene without re-saving it, into its own folder so the earlier diagnostic
    // player stays reproducible.
    public static void BuildCommitted(string location)
    {
        var scene=EditorSceneManager.OpenScene(Diagnostics,OpenSceneMode.Single);
        var agents=scene.GetRootGameObjects().Where(r=>r.GetComponent<ClimberAgent>()!=null).ToList();
        if(agents.Count!=16||agents.Any(r=>r.GetComponent<ClimbDiagnosticsObserver>()==null))throw new BuildFailedException("Committed diagnostic scene must hold 16 arenas, each with an observer");
        BuildPlayer(location);
    }

    static void BuildPlayer(string location)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(location));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[] {Diagnostics},target=BuildTarget.StandaloneWindows64,locationPathName=location});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Diagnostic build failed");
        Debug.Log("CLIMB_DIAGNOSTIC_BUILD_OK "+location);
    }
}
