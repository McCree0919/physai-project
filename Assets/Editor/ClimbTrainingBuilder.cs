using System.IO;
using PhysicalAI.Climber;
using PhysicalAI.Structures;
using PhysicalAI.Learning;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ClimbTrainingBuilder
{
    [MenuItem("Tools/Climb/Build Two-Step Training Scene")]
    public static void Generate()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("ClimbAcademyClock").AddComponent<ClimbAcademyClock>();
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/StaticStructurePhysics.physicMaterial");
        for(int i=0;i<16;i++)
        {
            var body=StaticStructureLoader.Load(BoxStackGenerator.Smoke(true),new Vector3((i%4)*14,0,(i/4)*14),Quaternion.identity);
            body.name="ClimbArena_"+i;PrepareEnvironment(body);
            foreach(var collider in body.GetComponentsInChildren<Collider>())collider.sharedMaterial=material;
            var resources=body.GetComponent<StructureResources>();foreach(var transient in resources.Materials)Object.DestroyImmediate(transient);resources.Materials.Clear();
            AttachAgent(body);
        }
        var camera=new GameObject("Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(3,3,-4);camera.transform.LookAt(new Vector3(0,1,1));
        var light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(40,-30,0);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/ClimbTraining.unity");AssetDatabase.SaveAssets();Debug.Log("CLIMB_SCENE_OK arenas=16 obs=327 continuous=6 discrete=2,2 memory=false");
    }
    public static void PrepareEnvironment(ClimberBody body)
    {
        foreach(var c in body.GetComponentsInChildren<Collider>())if(c.attachedRigidbody==null)c.gameObject.layer=c.GetComponent<ClimberSurface>()!=null?8:9;
    }
    public static ClimberAgent AttachAgent(ClimberBody body)
    {
        var behavior=body.gameObject.AddComponent<BehaviorParameters>();behavior.BehaviorName="Climb";
        behavior.BrainParameters.VectorObservationSize=ClimberAgent.ObservationCount;behavior.BrainParameters.NumStackedVectorObservations=1;
        behavior.BrainParameters.ActionSpec=new ActionSpec(6,new[] {2,2});behavior.BehaviorType=BehaviorType.Default;
        var agent=body.gameObject.AddComponent<ClimberAgent>();
        var requester=body.gameObject.AddComponent<DecisionRequester>();requester.DecisionPeriod=5;requester.DecisionStep=0;requester.TakeActionsBetweenDecisions=false;
        return agent;
    }
    public static void Build()
    {
        Generate();Directory.CreateDirectory("Builds/Climb");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[] {"Assets/Scenes/ClimbTraining.unity"},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/Climb/Climb.exe"});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Climb build failed");
        Debug.Log("CLIMB_BUILD_OK");
    }
    // Curriculum player (LAYER3_DESIGN §17): the committed ClimbTraining scene as is (no regeneration, so a build leaves the checkout
    // unchanged); structures are swapped at runtime from --climb-curriculum=<pools.json>.
    public static void BuildCurriculum()
    {
        Directory.CreateDirectory("Builds/ClimbCurriculum");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[] {"Assets/Scenes/ClimbTraining.unity"},target=BuildTarget.StandaloneWindows64,locationPathName="Builds/ClimbCurriculum/Climb.exe"});
        if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new BuildFailedException("Climb curriculum build failed");
        Debug.Log("CLIMB_CURRICULUM_BUILD_OK");
    }
}
