using PhysicalAI.Climber;
using UnityEngine;

namespace PhysicalAI.Demo
{
    // Visual only: reads the public hand state and swaps hand materials (green while gripping).
    public sealed class DemoHandColors : MonoBehaviour
    {
        [SerializeField] ClimberBody body;
        [SerializeField] Renderer left,right;
        [SerializeField] Material leftFree,rightFree,gripped;
        public void Configure(ClimberBody value,Renderer leftHand,Renderer rightHand,Material leftMaterial,Material rightMaterial,Material grip)
        {body=value;left=leftHand;right=rightHand;leftFree=leftMaterial;rightFree=rightMaterial;gripped=grip;}
        void LateUpdate()
        {
            if(body==null)return;
            Paint(left,body.GetHandState(HandId.Left).Grabbed?gripped:leftFree);
            Paint(right,body.GetHandState(HandId.Right).Grabbed?gripped:rightFree);
        }
        static void Paint(Renderer r,Material m){if(r!=null&&r.sharedMaterial!=m)r.sharedMaterial=m;}
    }
}
