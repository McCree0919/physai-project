using UnityEngine;
namespace PhysicalAI.Climber
{
    public static class ClimberArenaFactory
    {
        public static ClimberBody Create(string name,Vector3 position,PhysicsMaterial material,bool addController=true)
        {
            var arena=new GameObject(name);arena.transform.position=position;
            var goal=arena.AddComponent<ClimberGoal>();
            var torso=BodyPart("Torso",PrimitiveType.Cube,arena.transform,new Vector3(0,.4f,-.4f),new Vector3(.6f,.8f,.35f),10,material);
            var left=BodyPart("LeftHand",PrimitiveType.Sphere,arena.transform,new Vector3(-.3f,1.15f,-.05f),Vector3.one*.2f,1,material);
            var right=BodyPart("RightHand",PrimitiveType.Sphere,arena.transform,new Vector3(.3f,.95f,-.05f),Vector3.one*.2f,1,material);
            var body=arena.AddComponent<ClimberBody>();body.Configure(torso,left,right);
            Box("Ground",arena.transform,new Vector3(0,-.1f,0),new Vector3(3,.2f,3),material,false);
            // Alternating static ledges: +0.25 m in y, 0.40 m laterally.
            for(int i=0;i<4;i++)
                Box("Rung"+i,arena.transform,new Vector3(i%2==0?-.2f:.2f,.95f+i*.25f,-.075f),new Vector3(.5f,.1f,.25f),material,true);
            Box("TopPlatform",arena.transform,new Vector3(0,1.9f,.75f),new Vector3(2,.2f,1.5f),material,true);
            // Wall behind the ledges; torso must actually clear the platform edge.
            Box("Wall",arena.transform,new Vector3(0,.9f,.45f),new Vector3(2,1.8f,.15f),material,false);
            if(addController)
            {
                var controller=arena.AddComponent<ClimberScriptController>();controller.Configure(body,goal);
            }
            return body;
        }
        static Rigidbody BodyPart(string name,PrimitiveType primitive,Transform parent,Vector3 position,Vector3 size,float mass,PhysicsMaterial material)
        {
            var go=GameObject.CreatePrimitive(primitive);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;
            go.GetComponent<Collider>().sharedMaterial=material;
            var rb=go.AddComponent<Rigidbody>();rb.mass=mass;rb.linearDamping=.1f;rb.angularDamping=mass>1?1:.5f;
            rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.solverIterations=16;rb.solverVelocityIterations=8;
            return rb;
        }
        public static BoxCollider Box(string name,Transform parent,Vector3 position,Vector3 size,PhysicsMaterial material,bool grabbable)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;
            var box=go.GetComponent<BoxCollider>();box.sharedMaterial=material;
            if(grabbable) {go.layer=8;go.AddComponent<ClimberSurface>().Arena=parent;}
            return box;
        }
    }
}
