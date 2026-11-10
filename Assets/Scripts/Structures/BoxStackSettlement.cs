using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicalAI.Structures
{
    [Serializable] public sealed class BoxSettleMetrics
    {
        public string id;public float displacement,angle,speed,angularSpeed,windowDrift,windowAngle;public bool sleeping;
    }
    public static class BoxStackSettlement
    {
        public static StabilityReport Check(StructureDocument doc) => Settle(doc,out _);
        public static StabilityReport Settle(StructureDocument input,out StructureDocument settled)
        {
            StructureJson.Validate(input);settled=StructureJson.Parse(StructureJson.Serialize(input));
            var report=new StabilityReport {inputHash=StructureJson.Hash(input),unityVersion=Application.unityVersion,scope="dynamic-settle-then-static",fixedDeltaTime=Time.fixedDeltaTime};
            Scene scene;
#if UNITY_EDITOR
            if(!Application.isPlaying)scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();else
#endif
                scene=SceneManager.CreateScene("BoxSettle_"+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var root=new GameObject("SettlementOnly");SceneManager.MoveGameObjectToScene(root,scene);
            var resources=root.AddComponent<StructureResources>();
            try
            {
                var colliders=StaticStructureLoader.BuildGeometry(input,root.transform,resources);
                var bodies=new List<Rigidbody>();var indices=new List<int>();
                for(int i=0;i<colliders.Count;i++)if(input.objects[i].settleAsDynamic)
                {
                    var body=colliders[i].gameObject.AddComponent<Rigidbody>();body.mass=input.objects[i].massKg.Value;
                    body.linearDamping=.1f;body.angularDamping=.5f;body.solverIterations=16;body.solverVelocityIterations=8;
                    body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;bodies.Add(body);indices.Add(i);
                }
                report.dynamicObjects=bodies.Count;report.staticObjects=colliders.Count-bodies.Count;
                Physics.SyncTransforms();
                for(int i=0;i<colliders.Count;i++)for(int j=i+1;j<colliders.Count;j++)
                    if(Physics.ComputePenetration(colliders[i],colliders[i].transform.position,colliders[i].transform.rotation,colliders[j],colliders[j].transform.position,colliders[j].transform.rotation,out _,out float depth)&&depth>.005f)
                    {report.reason="InitialPenetration:"+input.objects[i].id+"/"+input.objects[j].id;return report;}
                var physics=scene.GetPhysicsScene();if(physics==Physics.defaultPhysicsScene)throw new InvalidOperationException("Cannot settle in default physics scene");
                var windowP=new Vector3[bodies.Count];var windowQ=new Quaternion[bodies.Count];
                float stable=0;int steps=Mathf.CeilToInt(10/report.fixedDeltaTime);
                for(int step=0;step<steps;step++)
                {
                    physics.Simulate(report.fixedDeltaTime);report.simulationSteps++;
                    bool quiet=true;
                    for(int i=0;i<bodies.Count;i++)
                    {
                        var b=bodies[i];
                        if(b.linearVelocity.magnitude>=.02f||b.angularVelocity.magnitude>=.05f)quiet=false;
                        if(stable>0&&(Vector3.Distance(b.position,windowP[i])>=.01f||Quaternion.Angle(b.rotation,windowQ[i])>=1))quiet=false;
                    }
                    if(!quiet)stable=0;
                    else
                    {
                        if(stable==0)for(int i=0;i<bodies.Count;i++){windowP[i]=bodies[i].position;windowQ[i]=bodies[i].rotation;}
                        stable+=report.fixedDeltaTime;
                    }
                    report.simulatedSeconds=report.simulationSteps*report.fixedDeltaTime;
                    if(report.simulatedSeconds>=3&&stable>=1)break;
                }
                report.boxes=new BoxSettleMetrics[bodies.Count];
                for(int i=0;i<bodies.Count;i++)
                {
                    var b=bodies[i];var original=input.objects[indices[i]];var o=settled.objects[indices[i]];
                    var m=new BoxSettleMetrics {id=o.id,displacement=Vector3.Distance(b.position,original.pose.Position),angle=Quaternion.Angle(b.rotation,original.pose.Rotation),speed=b.linearVelocity.magnitude,angularSpeed=b.angularVelocity.magnitude,sleeping=b.IsSleeping(),windowDrift=Vector3.Distance(b.position,windowP[i]),windowAngle=Quaternion.Angle(b.rotation,windowQ[i])};report.boxes[i]=m;
                    o.pose=new PoseSpec {position=new[] {b.position.x,b.position.y,b.position.z},rotation=new[] {b.rotation.x,b.rotation.y,b.rotation.z,b.rotation.w}};
                }
                if(stable<1){report.reason="UnsettledAfter10Seconds";return report;}
                if(report.boxes.Any(m=>m.displacement>.05f||m.angle>5)){report.reason="CollapsedOrRearranged";return report;}
                if(bodies.Any(b=>b.position.y<-.1f||Mathf.Abs(b.position.x)>6||b.position.z < -4||b.position.z>8)){report.reason="OutOfBounds";return report;}
                // Validate settled geometry without accidentally re-entering the dynamic pass.
                BoxStackGenerator.SetGoal(settled);settled.generator.features=BoxStackGeometry.Measure(settled);
                if(!BoxStackGeometry.Reachable(settled)){report.reason="CoarseReachability";return report;}
                var frozen=StructureJson.Parse(StructureJson.Serialize(settled));
                foreach(var o in frozen.objects){o.settleAsDynamic=false;o.massKg=null;}
                var geometry=StaticStabilityValidator.Check(frozen,.005f);report.maxPenetration=geometry.maxPenetration;
                if(!geometry.passed){report.reason="SettledGeometry:"+geometry.reason;return report;}
                report.passed=true;report.reason="Dynamic settlement and coarse geometry passed; runtime boxes frozen static";return report;
            }
            finally
            {
                foreach(var m in resources.Materials)UnityEngine.Object.DestroyImmediate(m);resources.Materials.Clear();UnityEngine.Object.DestroyImmediate(root);
#if UNITY_EDITOR
                if(!Application.isPlaying)UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);else
#endif
                    SceneManager.UnloadSceneAsync(scene);
            }
        }
    }
}
