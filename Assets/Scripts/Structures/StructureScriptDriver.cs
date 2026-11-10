using PhysicalAI.Climber;
using PhysicalAI.Climber.Layer2Prep;
using UnityEngine;

namespace PhysicalAI.Structures
{
    // Scheduling adapter for the unchanged prep policy. It never touches Rigidbody/Joint.
    [DefaultExecutionOrder(-100)]
    public sealed class StructureScriptDriver : MonoBehaviour
    {
        [SerializeField] ClimberBody body;
        [SerializeField] float heightGap,lateralGap;
        PrepClimbController policy;
        int ticks;
        public PrepClimbController Policy => policy;
        public void Configure(ClimberBody value,float height,float spacing)
        {body=value;heightGap=height;lateralGap=spacing;Initialize();}
        void Initialize(){policy=new PrepClimbController(new PrepWall(body,heightGap,lateralGap));ticks=0;}
        void Start(){if(policy==null)Initialize();}
        void FixedUpdate(){if(++ticks%5==0)policy.Step(Time.fixedDeltaTime*5);}
    }
}
