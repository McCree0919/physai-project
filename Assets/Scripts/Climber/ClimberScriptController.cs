using UnityEngine;
namespace PhysicalAI.Climber
{
    [DefaultExecutionOrder(-100)]
    public sealed class ClimberScriptController : MonoBehaviour
    {
        [SerializeField] ClimberBody body;
        [SerializeField] ClimberGoal goal;
        public bool Completed {get;private set;}
        public int Handovers {get;private set;}
        public string Stage {get;private set;}="Climb";
        public float Elapsed {get;private set;}
        public string DiagnosticState => $"next={next} phases={reachPhase[0]},{reachPhase[1]}";
        int next;
        float stable;
        bool topLeft,topRight;
        bool clearedEdge;
        readonly int[] reachPhase=new int[2];
        readonly Vector3[] reachGoal={Vector3.one*999,Vector3.one*999};
        public void Configure(ClimberBody value,ClimberGoal target) {body=value;goal=target;}
        public void RestartController()
        {
            next=0;stable=0;topLeft=topRight=clearedEdge=Completed=false;
            Handovers=0;Elapsed=0;Stage="Climb";
            for(int i=0;i<2;i++){reachPhase[i]=0;reachGoal[i]=Vector3.one*999;}
        }
        internal void BeginTopPress() {next=4;topLeft=topRight=true;}
        void FixedUpdate() { Step(Time.fixedDeltaTime); }
        internal void Step(float dt)
        {
            if(body==null || Completed) return;
            Elapsed+=dt;
            var state=body.GetBodyState();
            if(next<4)
            {
                int moving=next%2;
                HandId id=(HandId)moving;
                Vector3 point=new Vector3(moving==0?-.2f:.2f,1.1f+next*.25f,-.18f);
                // Raise torso using the current support before the next reach.
                float supportY=next==0?.4f:1.1f+(next-1)*.25f-.28f-.15f;
                HoldSupport((HandId)(1-moving),new Vector3(0,supportY,-.5f));
                SeekGrab(id,point);
                if(body.GetHandState(id).Grabbed && Vector3.Distance(body.GetHandState(id).Position,point)<.05f)
                {
                    Debug.Log($"LAYER1_GRIP {name} rung={next} hand={id} position={body.GetHandState(id).Position}");
                    if(next>0) {Handovers++;body.SetGrab((HandId)(1-moving),false);}
                    next++;
                }
                return;
            }
            Stage="TopReach";
            if(!topLeft)
            {
                HoldSupport(HandId.Right,new Vector3(0,1.50f,-.5f));
                var point=new Vector3(-.3f,2.1f,.02f);
                SeekGrab(HandId.Left,point);
                topLeft=body.GetHandState(HandId.Left).Grabbed && Vector3.Distance(body.GetHandState(HandId.Left).Position,point)<.05f;
                if(!topLeft)return;
            }
            if(!topRight)
            {
                HoldSupport(HandId.Left,new Vector3(0,1.90f,-.55f));
                var point=new Vector3(.3f,2.1f,.02f);
                SeekGrab(HandId.Right,point);
                topRight=body.GetHandState(HandId.Right).Grabbed && Vector3.Distance(body.GetHandState(HandId.Right).Position,point)<.05f;
                if(!topRight)return;
            }
            Stage="PressUp";
            if(state.Position.y>=2.43f)clearedEdge=true;
            Vector3 desired=new Vector3(0,2.53f,clearedEdge?.55f:-.48f);
            HoldSupport(HandId.Left,desired);HoldSupport(HandId.Right,desired);
            stable=goal.Contains(state)?stable+dt:0;
            if(stable>=.3f)
            {
                Completed=true;Stage="Arrived";
                Debug.Log($"LAYER1_ARENA_PASS {name} handovers={Handovers} seconds={Elapsed:F2} torso={state.Position} peakN={body.GetHandState(HandId.Left).PeakConstraintLoad:F2},{body.GetHandState(HandId.Right).PeakConstraintLoad:F2}");
            }
        }
        void Reach(HandId id,Vector3 point)
        {
            var b=body.GetBodyState();var h=body.GetHandState(id);
            int i=(int)id;
            if(Vector3.Distance(reachGoal[i],point)>.001f) {reachGoal[i]=point;reachPhase[i]=0;}
            // Persistent waypoints avoid dropping early onto the vertical front face.
            switch(reachPhase[i])
            {
                case 0:
                    if(h.Position.z<point.z-.165f) reachPhase[i]=1;
                    point=new Vector3(h.Position.x,h.Position.y,point.z-.2f);break;
                case 1:
                    if(h.Position.y>point.y+.04f && Mathf.Abs(h.Position.x-point.x)<.04f) reachPhase[i]=2;
                    point=new Vector3(point.x,point.y+.08f,point.z-.2f);break;
                case 2:
                    if(h.Position.z>point.z-.025f) reachPhase[i]=3;
                    point+=Vector3.up*.08f;break;
            }
            body.SetHandTarget(id,Quaternion.Inverse(b.Rotation)*(point-h.ShoulderPosition),1);
        }
        void SeekGrab(HandId id,Vector3 point)
        {
            Reach(id,point);
            body.SetGrab(id,Vector3.Distance(body.GetHandState(id).Position,point)<.05f);
        }
        void HoldSupport(HandId id,Vector3 desiredTorso)
        {
            var h=body.GetHandState(id);if(!h.Grabbed) return;
            var b=body.GetBodyState();
            Vector3 shoulderBodyLocal=Quaternion.Inverse(b.Rotation)*(h.ShoulderPosition-b.Position);
            // A support target is a desired BODY-frame pose, not a world target
            // that silently follows and cancels every current yaw deviation.
            body.SetHandTarget(id,h.Position-desiredTorso-shoulderBodyLocal,.7f);
        }
    }
}
