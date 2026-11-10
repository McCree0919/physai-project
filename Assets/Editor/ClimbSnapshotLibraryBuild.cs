using System.IO;
using PhysicalAI.Climber;
using PhysicalAI.Learning;
using PhysicalAI.Structures;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// Snapshot-library player: the 16 training arenas, unchanged, with the two-step expert driving each body,
// an ExternalCommand Agent tracking task state, and a recorder. Never used for training.
public static class ClimbSnapshotLibraryBuild
{
    [MenuItem("Tools/Climb/Build Snapshot Recorder Scene")]
    public static void Generate()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("ClimbAcademyClock").AddComponent<ClimbAcademyClock>();
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/StaticStructurePhysics.physicMaterial");
        for(int i=0;i<16;i++)
        {
            // Same construction as ClimbTrainingBuilder.Generate, so snapshot geometry hashes match the training arenas.
            var body=StaticStructureLoader.Load(BoxStackGenerator.Smoke(true),new Vector3((i%4)*14,0,(i/4)*14),Quaternion.identity);
            body.name="ClimbArena_"+i;ClimbTrainingBuilder.PrepareEnvironment(body);
            foreach(var collider in body.GetComponentsInChildren<Collider>())collider.sharedMaterial=material;
            var resources=body.GetComponent<StructureResources>();foreach(var transient in resources.Materials)Object.DestroyImmediate(transient);resources.Materials.Clear();
            var agent=ClimbTrainingBuilder.AttachAgent(body);agent.ExternalCommand=true;
            body.GetComponent<BehaviorParameters>().BehaviorType=BehaviorType.HeuristicOnly;
            body.gameObject.AddComponent<ClimbSnapshotRecorder>();
        }
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/ClimbSnapshots.unity");AssetDatabase.SaveAssets();Debug.Log("CLIMB_SNAPSHOT_SCENE_OK arenas=16");
    }
    public static void Build()
    {
        Generate();Directory.CreateDirectory("Builds/ClimbSnapshots");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[] {"Assets/Scenes/ClimbSnapshots.unity"},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/ClimbSnapshots/ClimbSnapshots.exe"});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Snapshot recorder build failed");
        Debug.Log("CLIMB_SNAPSHOT_BUILD_OK");
    }
}
