using UnityEngine;

namespace PhysicalAI.Climber
{
    public static class GripClearance
    {
        public const float Radius=.10f,Height=.22f,SurfaceGap=.005f;
        public static bool IsClear(Collider support,Vector3 point,Transform arena,Rigidbody torso=null,Rigidbody left=null,Rigidbody right=null)
        {
            Vector3 up=arena.up;
            var hits=Physics.OverlapCapsule(point+up*(SurfaceGap+Radius),point+up*(SurfaceGap+Height-Radius),Radius,~0,QueryTriggerInteraction.Ignore);
            foreach(var hit in hits)
            {
                if(hit==support||!hit.transform.IsChildOf(arena))continue;
                // Body parts are not environmental occluders. Dynamic boxes must still count.
                var rb=hit.attachedRigidbody;if(rb!=null&&(rb==torso||rb==left||rb==right))continue;
                return false;
            }
            return true;
        }
    }
}
