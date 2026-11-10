using PhysicalAI.Climber;
using PhysicalAI.Demo;
using PhysicalAI.Structures;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// DemoShowcase: 16 arenas of existing, already-validated structures, each climbed by its existing scripted controller.
// Rung walls use only (rise, spacing) cells that were 8/8 in the Layer 2 scan (docs/data/layer2/scan_cells.csv).
public static class DemoShowcaseBuilder
{
    public const string ScenePath="Assets/Scenes/DemoShowcase.unity";
    const float Spacing=10;
    struct Entry {public DemoStructure Kind;public float Height,Gap;public Entry(DemoStructure k,float h=0,float g=0){Kind=k;Height=h;Gap=g;}}
    static readonly Entry[] Layout={
        new Entry(DemoStructure.RungWall,.20f,.40f),new Entry(DemoStructure.SingleBox),new Entry(DemoStructure.RungWall,.25f,.60f),new Entry(DemoStructure.TwoStepBoxes),
        new Entry(DemoStructure.RungWall,.30f,.50f),new Entry(DemoStructure.RungWall,.35f,.30f),new Entry(DemoStructure.RungWall,.40f,.20f),new Entry(DemoStructure.RungWall,.40f,.60f),
        new Entry(DemoStructure.RungWall,.45f,.45f),new Entry(DemoStructure.SingleBox),new Entry(DemoStructure.RungWall,.50f,.35f),new Entry(DemoStructure.TwoStepBoxes),
        new Entry(DemoStructure.RungWall,.55f,.80f),new Entry(DemoStructure.RungWall,.60f,.50f),new Entry(DemoStructure.RungWall,.20f,.70f),new Entry(DemoStructure.RungWall,.25f,.40f)};

    [MenuItem("Tools/Demo/Build DemoShowcase Scene")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(!Application.isBatchMode&&!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        var physics=AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/Materials/StaticStructurePhysics.physicMaterial");
        if(physics==null)throw new System.InvalidOperationException("Missing Assets/Materials/StaticStructurePhysics.physicMaterial (Tools/Structures/Build Static JSON Demo)");
        var palette=DemoVisuals.Materials();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for(int i=0;i<Layout.Length;i++)
        {
            var e=Layout[i];var position=new Vector3((i%4)*Spacing,0,(i/4)*Spacing);
            StructureDocument doc;string title,details;float labelY;
            switch(e.Kind)
            {
                case DemoStructure.RungWall:
                    doc=StaticWallGenerator.GenerateAt(e.Height,e.Gap);title="Rung wall";
                    details=$"rise {e.Height:F2} m  |  spacing {e.Gap:F2} m  |  top {1+4*e.Height:F2} m";labelY=1+4*e.Height+1.3f;break;
                case DemoStructure.SingleBox:
                    doc=BoxStackGenerator.Smoke(false);title="Single box";details="Tall 1.20 x 1.00 x 1.20 m";labelY=2.6f;break;
                default:
                    doc=BoxStackGenerator.Smoke(true);title="Two-step boxes";details="Wide 0.50 m + Step 0.50 m  (tops 0.50 / 1.00 m)";labelY=2.6f;break;
            }
            var start=new BodyReset {Torso=doc.start.torso.Position,LeftHand=doc.start.leftHand.Position,RightHand=doc.start.rightHand.Position,Rotation=Quaternion.identity};
            var body=StaticStructureLoader.Load(doc,position,Quaternion.identity,false);
            body.name=$"Arena_{i:00}_{e.Kind}";
            // Explicit shared physics material (same 0.15 friction as the document's), as the existing scene builders do.
            foreach(var c in body.GetComponentsInChildren<Collider>())c.sharedMaterial=physics;
            var resources=body.GetComponent<StructureResources>();foreach(var m in resources.Materials)Object.DestroyImmediate(m);resources.Materials.Clear();
            DemoVisuals.Dress(body,palette,true);
            var text=new GameObject(DemoVisuals.Prefix+"Label");text.transform.SetParent(body.transform,false);text.transform.localPosition=new Vector3(0,labelY,.4f);
            var mesh=text.AddComponent<TextMesh>();mesh.font=font;mesh.fontSize=64;mesh.characterSize=.03f;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;mesh.color=Color.white;
            text.GetComponent<MeshRenderer>().sharedMaterial=font.material;text.AddComponent<DemoBillboard>();
            body.gameObject.AddComponent<DemoArena>().Configure(e.Kind,e.Height,e.Gap,title,details,body,mesh,start);
            mesh.text=title+"\n"+details;
        }
        var floor=DemoVisuals.Visual("Floor",PrimitiveType.Plane,null,new Vector3(1.5f*Spacing,0,1.5f*Spacing+1),Quaternion.identity,Vector3.one*7,palette.Ground);
        floor.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        var cam=new GameObject("Camera",typeof(Camera),typeof(AudioListener));cam.tag="MainCamera";cam.transform.position=new Vector3(15,20,-20);cam.transform.LookAt(new Vector3(15,0,15));
        cam.AddComponent<DemoCamera>();
        DemoVisuals.Lighting();
        new GameObject("DemoHud").AddComponent<DemoHud>().Heading="PHYSICAL AI CLIMBING  |  16 arenas, existing scripted controllers, unchanged body and physics";
        EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
        Debug.Log("DEMO_SHOWCASE_BUILT arenas="+Layout.Length);
    }
}
