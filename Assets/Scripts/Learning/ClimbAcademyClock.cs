using Unity.MLAgents;
using UnityEngine;

namespace PhysicalAI.Learning
{
    [DefaultExecutionOrder(-200)]
    public sealed class ClimbAcademyClock : MonoBehaviour
    {
        bool quitting;
        void Awake(){Academy.Instance.AutomaticSteppingEnabled=false;Application.quitting+=Quit;}
        void Quit(){quitting=true;}
        void FixedUpdate(){Academy.Instance.EnvironmentStep();}
        void OnDestroy(){Application.quitting-=Quit;if(!quitting&&Academy.IsInitialized)Academy.Instance.AutomaticSteppingEnabled=true;}
    }
}
