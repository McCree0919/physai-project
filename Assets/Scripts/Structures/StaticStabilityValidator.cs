using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAI.Structures
{
    [Serializable] public sealed class StabilityReport
    {
        public bool passed;
        public string reason,inputHash,unityVersion,scope="static-world-anchors";
        public int staticObjects,dynamicObjects,simulationSteps;
        public float simulatedSeconds,fixedDeltaTime,maxPenetration;
    }
    public static class StaticStabilityValidator
    {
        // Static supports are world anchors by definition. This does not certify free boxes.
        public static StabilityReport Check(StructureDocument doc)
        {
            StructureJson.Validate(doc);
            var report=new StabilityReport {inputHash=StructureJson.Hash(doc),unityVersion=Application.unityVersion,staticObjects=doc.objects.Length,fixedDeltaTime=Time.fixedDeltaTime};
            Scene scene;
#if UNITY_EDITOR
            if(!Application.isPlaying)scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            else
#endif
                scene=SceneManager.CreateScene("StructureValidation_"+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject root=null;
            try
            {
                root=new GameObject("ValidationOnly");SceneManager.MoveGameObjectToScene(root,scene);
                var resources=root.AddComponent<StructureResources>();
                var boxes=StaticStructureLoader.BuildGeometry(doc,root.transform,resources);
                Physics.SyncTransforms();
                for(int i=0;i<boxes.Count;i++)for(int j=i+1;j<boxes.Count;j++)
                {
                    if(Penetration(boxes[i],boxes[j],out float depth))
                    {
                        report.maxPenetration=Mathf.Max(report.maxPenetration,depth);
                        if(depth>.002f){report.reason="overlap: "+doc.objects[i].id+" / "+doc.objects[j].id;return report;}
                    }
                }
                // Check the three spawn bodies and goal torso clearances, not only supports.
                var probes=new[] {
                    Probe(root.transform,"TorsoStart",PrimitiveType.Cube,doc.start.torso.Position,new Vector3(.6f,.8f,.35f),doc.start.torso.Rotation),
                    Probe(root.transform,"LeftStart",PrimitiveType.Sphere,doc.start.leftHand.Position,Vector3.one*.2f,doc.start.leftHand.Rotation),
                    Probe(root.transform,"RightStart",PrimitiveType.Sphere,doc.start.rightHand.Position,Vector3.one*.2f,doc.start.rightHand.Rotation),
                    Probe(root.transform,"GoalCenter",PrimitiveType.Cube,StructureJson.Vector(doc.goal.center),new Vector3(.6f,.8f,.35f),Quaternion.identity)
                };
                Physics.SyncTransforms();
                foreach(var probe in probes)foreach(var box in boxes)
                    if(Penetration(probe,box,out float depth)&&depth>.002f){report.reason="clearance: "+probe.name+" / "+box.name;return report;}
                for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)
                    if(Penetration(probes[i],probes[j],out float depth)&&depth>.002f){report.reason="spawn bodies overlap";return report;}
                foreach(var probe in probes)probe.gameObject.SetActive(false);
                var positions=new Vector3[boxes.Count];var rotations=new Quaternion[boxes.Count];
                for(int i=0;i<boxes.Count;i++){positions[i]=boxes[i].transform.position;rotations[i]=boxes[i].transform.rotation;}
                var physics=scene.GetPhysicsScene();
                if(!physics.IsValid()||physics==Physics.defaultPhysicsScene)throw new InvalidOperationException("Validation requires an isolated physics scene");
                int steps=Mathf.CeilToInt(3f/report.fixedDeltaTime);
                for(int step=0;step<steps;step++)
                {
                    physics.Simulate(report.fixedDeltaTime);report.simulationSteps++;
                    for(int i=0;i<boxes.Count;i++)
                        if(Vector3.Distance(boxes[i].transform.position,positions[i])>.0001f||Quaternion.Angle(boxes[i].transform.rotation,rotations[i])>.01f)
                        {report.reason="static anchor moved: "+boxes[i].name;return report;}
                }
                report.simulatedSeconds=report.simulationSteps*report.fixedDeltaTime;
                report.passed=true;report.reason="static anchors unchanged; geometry and spawn/goal center clearances passed; no dynamic stability claim";
                return report;
            }
            finally
            {
                if(root!=null)
                {
                    // Dispose transient assets now, including synchronous editor validation.
                    var resources=root.GetComponent<StructureResources>();
                    foreach(var m in resources.Materials)UnityEngine.Object.DestroyImmediate(m);resources.Materials.Clear();
                    UnityEngine.Object.DestroyImmediate(root);
                }
#if UNITY_EDITOR
                if(!Application.isPlaying)UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                else
#endif
                    SceneManager.UnloadSceneAsync(scene);
            }
        }
        static Collider Probe(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 size,Quaternion rotation)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=rotation;go.transform.localScale=size;
            var collider=go.GetComponent<Collider>();collider.sharedMaterial=parent.GetComponent<StructureResources>().Materials[0];return collider;
        }
        static bool Penetration(Collider a,Collider b,out float depth) => Physics.ComputePenetration(a,a.transform.position,a.transform.rotation,b,b.transform.position,b.transform.rotation,out _,out depth);
        public static StabilityReport SaveVerified(string path,StructureDocument doc)
        {
            var report=Check(doc);if(!report.passed)throw new InvalidDataException("Save rejected: "+report.reason);
            doc.validation=report;
            string text=StructureJson.Serialize(doc);StructureJson.Parse(text);
            string absolute=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(absolute));
            string temporary=absolute+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {File.WriteAllText(temporary,text,new System.Text.UTF8Encoding(false));if(File.Exists(absolute))File.Replace(temporary,absolute,null);else File.Move(temporary,absolute);}
            finally {if(File.Exists(temporary))File.Delete(temporary);}
            return report;
        }
    }
}
