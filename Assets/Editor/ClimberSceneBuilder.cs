using PhysicalAI.Climber;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ClimberSceneBuilder
{
    [MenuItem("Tools/Build Layer 1 Climber Arenas")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        if(!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        if(!AssetDatabase.IsValidFolder("Assets/Materials"))AssetDatabase.CreateFolder("Assets","Materials");
        const string path="Assets/Materials/ClimberPhysics.physicMaterial";
        var material=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if(material==null){material=new PhysicsMaterial("ClimberPhysics");AssetDatabase.CreateAsset(material,path);}
        material.staticFriction=material.dynamicFriction=.15f;material.frictionCombine=PhysicsMaterialCombine.Average;material.bounciness=0;material.bounceCombine=PhysicsMaterialCombine.Average;
        EditorUtility.SetDirty(material);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var bodies=new ClimberBody[16];
        for(int i=0;i<16;i++)
        {
            bodies[i]=ClimberArenaFactory.Create("ClimberArena_"+i,new Vector3((i%4)*5,0,(i/4)*5),material);
            bodies[i].GetComponent<ClimberScriptController>().enabled=false;
        }
        var controls=new GameObject("ManualControls").AddComponent<ClimberManualController>();controls.Configure(bodies);
        var camera=new GameObject("Camera",typeof(Camera),typeof(AudioListener));camera.tag="MainCamera";
        camera.transform.position=new Vector3(7.5f,14,-12);camera.transform.rotation=Quaternion.Euler(35,0,0);camera.GetComponent<Camera>().farClipPlane=100;
        camera.AddComponent<ClimberFollowCamera>().Configure(controls);
        var light=new GameObject("Light",typeof(Light));light.GetComponent<Light>().type=LightType.Directional;light.transform.rotation=Quaternion.Euler(50,-30,0);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/ClimberTraining.unity");AssetDatabase.SaveAssets();
        Debug.Log("LAYER1_SCENE_GENERATED 16 static arenas");
    }
}
