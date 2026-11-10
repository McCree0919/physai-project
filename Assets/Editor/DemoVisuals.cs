using System.Collections.Generic;
using PhysicalAI.Climber;
using PhysicalAI.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Demo palette: visual-only materials, highlight meshes and lighting. No colliders, rigidbodies or body parameters are touched.
public static class DemoVisuals
{
    public const string Folder="Assets/Materials/Demo",Prefix="DemoVisual_";
    public sealed class Palette {public Material Torso,LeftHand,RightHand,Gripped,Structure,Edge,Ground,Goal;}
    static readonly string[] ExistingScenes={"Assets/Scenes/BoxSingleSmoke.unity","Assets/Scenes/BoxStepsSmoke.unity","Assets/Scenes/ClimberTraining.unity"};

    public static Palette Materials()
    {
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Materials","Demo");
        var p=new Palette {
            Torso=Opaque("DemoTorso",new Color(1f,.48f,.08f)),
            LeftHand=Opaque("DemoHandLeft",new Color(.12f,.38f,1f)),
            RightHand=Opaque("DemoHandRight",new Color(.92f,.12f,.12f)),
            Gripped=Opaque("DemoHandGripped",new Color(.1f,.85f,.25f)),
            Structure=Opaque("DemoStructure",new Color(.58f,.59f,.61f)),
            Edge=Opaque("DemoGraspableEdge",new Color(1f,.84f,.05f)),
            Ground=Opaque("DemoGround",new Color(.11f,.12f,.14f)),
            Goal=Opaque("DemoGoal",new Color(.15f,.9f,.3f,.28f))};
        p.Edge.EnableKeyword("_EMISSION");p.Edge.SetColor("_EmissionColor",new Color(1f,.84f,.05f)*.6f);p.Edge.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        // Standard shader "Fade" mode for the goal volume.
        var g=p.Goal;g.SetFloat("_Mode",2);g.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);g.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);g.SetInt("_ZWrite",0);
        g.DisableKeyword("_ALPHATEST_ON");g.EnableKeyword("_ALPHABLEND_ON");g.DisableKeyword("_ALPHAPREMULTIPLY_ON");g.renderQueue=(int)RenderQueue.Transparent;
        foreach(var m in new[]{p.Edge,p.Goal})EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();return p;
    }
    static Material Opaque(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}
        m.color=color;m.SetFloat("_Glossiness",.25f);EditorUtility.SetDirty(m);return m;
    }

    // Recolours one arena and adds highlight/goal meshes (idempotent). hideGround: a shared floor replaces overlapping arena grounds.
    public static void Dress(ClimberBody body,Palette p,bool hideGround=false)
    {
        var root=body.transform;var stale=new List<GameObject>();
        foreach(Transform child in root)if(child.name.StartsWith(Prefix))stale.Add(child.gameObject);
        foreach(var item in stale)Object.DestroyImmediate(item);
        Renderer left=null,right=null;var surfaces=new List<BoxCollider>();
        foreach(Transform child in root)
        {
            var r=child.GetComponent<Renderer>();if(r==null)continue;
            switch(child.name)
            {
                case "Torso":r.sharedMaterial=p.Torso;break;
                case "LeftHand":r.sharedMaterial=p.LeftHand;left=r;break;
                case "RightHand":r.sharedMaterial=p.RightHand;right=r;break;
                case "Ground":case "ground":r.sharedMaterial=p.Ground;r.enabled=!hideGround;break;
                default:
                    r.sharedMaterial=p.Structure;
                    var box=child.GetComponent<BoxCollider>();
                    if(child.GetComponent<ClimberSurface>()!=null&&box!=null)surfaces.Add(box);
                    break;
            }
        }
        foreach(var box in surfaces)EdgeStrips(root,box,p.Edge);
        var goal=body.GetComponent<ClimberGoal>();
        if(goal!=null)Visual("Goal",PrimitiveType.Cube,root,goal.Center,Quaternion.identity,goal.Size,p.Goal);
        var colors=body.GetComponent<DemoHandColors>();if(colors==null)colors=body.gameObject.AddComponent<DemoHandColors>();
        colors.Configure(body,left,right,p.LeftHand,p.RightHand,p.Gripped);
    }
    // Thin yellow strips on the four top-face edges (the body grips any clear top point; edges are where climbs start).
    static void EdgeStrips(Transform root,BoxCollider box,Material material)
    {
        var t=box.transform;Vector3 s=Vector3.Scale(t.localScale,box.size);Vector3 c=t.localPosition+t.localRotation*Vector3.Scale(t.localScale,box.center);
        const float width=.03f,height=.012f;float y=s.y*.5f+height*.5f;
        for(int k=0;k<4;k++)
        {
            bool alongX=k<2;float sign=k%2==0?-1:1;
            Vector3 offset=alongX?new Vector3(0,y,sign*(s.z*.5f-width*.5f)):new Vector3(sign*(s.x*.5f-width*.5f),y,0);
            Vector3 size=alongX?new Vector3(s.x,height,width):new Vector3(width,height,s.z);
            Visual("Edge_"+t.name+"_"+k,PrimitiveType.Cube,root,c+t.localRotation*offset,t.localRotation,size,material);
        }
    }
    public static GameObject Visual(string name,PrimitiveType type,Transform parent,Vector3 position,Quaternion rotation,Vector3 size,Material material)
    {
        var go=GameObject.CreatePrimitive(type);go.name=Prefix+name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=rotation;go.transform.localScale=size;
        var r=go.GetComponent<Renderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;
        return go;
    }
    public static void Lighting()
    {
        Light light=null;
        foreach(var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))if(l.type==LightType.Directional){light=l;break;}
        if(light==null){light=new GameObject("Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(50,-30,0);}
        light.shadows=LightShadows.Soft;light.shadowStrength=.75f;light.intensity=1.1f;
        RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.42f,.44f,.48f);
        foreach(var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)){cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.08f,.1f);}
    }

    public static void Overlay()
    {
        if(Object.FindFirstObjectByType<DemoHud>()==null)new GameObject("DemoHud").AddComponent<DemoHud>();
        var cam=Camera.main!=null?Camera.main:Object.FindFirstObjectByType<Camera>();
        if(cam==null){cam=new GameObject("Camera").AddComponent<Camera>();cam.tag="MainCamera";}
        if(cam.GetComponent<DemoCamera>()==null)cam.gameObject.AddComponent<DemoCamera>();
    }

    [MenuItem("Tools/Demo/Apply Palette To Existing Scenes")]
    public static void ApplyToExistingScenes()
    {
        if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var p=Materials();
        foreach(string path in ExistingScenes)
        {
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
            foreach(var body in Object.FindObjectsByType<ClimberBody>(FindObjectsSortMode.None))Dress(body,p);
            Lighting();
            // The Layer 1 manual scene keeps its own control HUD and camera; the box scenes get the demo overlay.
            if(path.Contains("Box"))Overlay();
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();Debug.Log("DEMO_PALETTE_APPLIED "+string.Join(",",ExistingScenes));
    }
}
