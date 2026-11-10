using UnityEngine;

namespace PhysicalAI.Demo
{
    // Keeps an arena label facing the active camera.
    public sealed class DemoBillboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam=Camera.main;if(cam==null)return;
            transform.rotation=Quaternion.LookRotation(transform.position-cam.transform.position,Vector3.up);
        }
    }
}
