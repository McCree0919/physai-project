using UnityEngine;
namespace PhysicalAI.Climber
{
    public sealed class ClimberSurface : MonoBehaviour
    {
        public Transform Arena;
        public static void TopPoint(BoxCollider box,Vector3 hand,Vector3 up,out Vector3 point,out Vector3 normal)
        {
            Vector3[] axes={Vector3.right,Vector3.up,Vector3.forward};
            int best=0;float sign=1,score=-1;
            for(int axis=0;axis<3;axis++)
            {
                float dot=Vector3.Dot(box.transform.TransformDirection(axes[axis]),up);
                if(Mathf.Abs(dot)>score) {score=Mathf.Abs(dot);best=axis;sign=Mathf.Sign(dot);}
            }
            Vector3 local=box.transform.InverseTransformPoint(hand)-box.center;
            Vector3 half=box.size*.5f;
            for(int a=0;a<3;a++) local[a]=a==best?sign*half[a]:Mathf.Clamp(local[a],-half[a],half[a]);
            point=box.transform.TransformPoint(box.center+local);
            normal=box.transform.TransformDirection(axes[best]*sign).normalized;
        }
    }
}
