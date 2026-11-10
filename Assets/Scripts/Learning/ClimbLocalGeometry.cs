using UnityEngine;
using PhysicalAI.Climber;

namespace PhysicalAI.Learning
{
    public static class ClimbLocalGeometry
    {
        public const int Mask=(1<<8)|(1<<9);
        public static bool Cast(Transform arena,Vector3 origin,Vector3 direction,float length,RaycastHit[] buffer,out RaycastHit nearest)
        {
            int count=Physics.RaycastNonAlloc(origin,direction,buffer,length,Mask,QueryTriggerInteraction.Ignore);
            nearest=default;float distance=float.PositiveInfinity;
            for(int i=0;i<count;i++)if(buffer[i].collider.transform.IsChildOf(arena)&&buffer[i].distance<distance)
            {nearest=buffer[i];distance=nearest.distance;}
            return distance<float.PositiveInfinity;
        }
        public static bool Clear(Transform arena,Collider support,Vector3 point)
        {
            Vector3 up=arena.up;
            var hits=Physics.OverlapCapsule(point+up*(GripClearance.SurfaceGap+GripClearance.Radius),point+up*(GripClearance.SurfaceGap+GripClearance.Height-GripClearance.Radius),GripClearance.Radius,Mask,QueryTriggerInteraction.Ignore);
            foreach(var c in hits)if(c!=support&&c.transform.IsChildOf(arena))return false;
            return true;
        }
    }
}
