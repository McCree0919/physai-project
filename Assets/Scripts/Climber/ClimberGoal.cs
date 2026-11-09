using UnityEngine;
namespace PhysicalAI.Climber
{
    public sealed class ClimberGoal : MonoBehaviour
    {
        public Vector3 Center=new Vector3(0,2.6f,.6f), Size=new Vector3(.8f,.4f,.6f);
        public bool Contains(BodyState state)
        {
            Vector3 p=state.Position-Center;
            return Mathf.Abs(p.x)<=Size.x*.5f && Mathf.Abs(p.y)<=Size.y*.5f && Mathf.Abs(p.z)<=Size.z*.5f && state.Velocity.magnitude<.5f;
        }
        void OnDrawGizmos()
        {
            Matrix4x4 old=Gizmos.matrix;
            Gizmos.color=Color.green;Gizmos.matrix=transform.localToWorldMatrix;Gizmos.DrawWireCube(Center,Size);
            Gizmos.matrix=old;
        }
    }
}
