using PhysicalAI.Climber;
using UnityEngine;

namespace PhysicalAI.Structures
{
    [DefaultExecutionOrder(-100)]
    public sealed class BoxClimbSmokeController : MonoBehaviour
    {
        [SerializeField] ClimberBody body;
        [SerializeField] bool twoSteps;
        readonly int[] approach=new int[2];
        int tier;float stable,wait;
        Vector3 anchor;
        bool inside;
        public string Stage {get;private set;}="Seek";
        public bool Completed {get;private set;}
        public bool Failed {get;private set;}
        public float Elapsed {get;private set;}
        public int Tier => tier;
        public void Configure(ClimberBody value,bool stairs){body=value;twoSteps=stairs;anchor=body.GetBodyState().Position;}
        void Start(){if(body!=null)anchor=body.GetBodyState().Position;}
        void FixedUpdate()
        {
            if(body==null||Completed||Failed)return;
            float dt=Time.fixedDeltaTime;Elapsed+=dt;
            var b=body.GetBodyState();
            if(Elapsed>60||b.Position.y<-.2f){Failed=true;Debug.Log("BOX_SMOKE_FAILURE "+Snapshot());return;}
            float top=twoSteps?(tier==0?.5f:1f):1f;
            float front=tier==0?0:.6f;
            if(Stage=="Seek")
            {
                for(int i=0;i<2;i++)
                {
                    var id=(HandId)i;var hand=body.GetHandState(id);
                    if(hand.Grabbed){Hold(id,anchor);continue;}
                    var point=new Vector3(i==0?-.3f:.3f,top+.1f,front+.02f);
                    Vector3 target=point;
                    if(approach[i]==0)
                    {
                        target=new Vector3(point.x,point.y+.10f,front-.20f);
                        if(hand.Position.y>point.y+.05f&&Mathf.Abs(hand.Position.x-point.x)<.04f&&hand.Position.z<front-.15f)approach[i]=1;
                    }
                    else if(approach[i]==1)
                    {
                        target+=Vector3.up*.10f;
                        if(hand.Position.z>front-.015f)approach[i]=2;
                    }
                    body.SetHandTarget(id,Quaternion.Inverse(b.Rotation)*(target-hand.ShoulderPosition),1);
                    body.SetGrab(id,approach[i]==2&&Vector3.Distance(hand.Position,point)<.04f);
                }
                if(body.GetBodyState().GrabbedHands==2){Stage="Press";inside=false;stable=0;}
                return;
            }
            if(Stage=="Release")
            {
                body.SetGrab(HandId.Left,false);body.SetGrab(HandId.Right,false);wait+=dt;
                if(wait>=.35f){tier++;approach[0]=approach[1]=0;anchor=b.Position;Stage="Seek";}
                return;
            }
            if(b.Position.y>=top+.43f)inside=true;
            float inward=twoSteps&&tier==0?.30f:.55f;
            var desired=new Vector3(0,top+.53f,front+(inside?inward:-.48f));
            Hold(HandId.Left,desired);Hold(HandId.Right,desired);
            bool arrived=twoSteps&&tier==0?b.Position.y>=top+.40f&&b.Position.z>.20f&&b.Velocity.magnitude<.5f:body.GetComponent<ClimberGoal>().Contains(b);
            stable=arrived?stable+dt:0;
            if(stable>=.3f)
            {
                if(twoSteps&&tier==0){Stage="Release";wait=0;}
                else {Completed=true;Stage="Arrived";Debug.Log("BOX_SMOKE_PASS "+Snapshot());}
            }
        }
        void Hold(HandId id,Vector3 desiredTorso)
        {
            var h=body.GetHandState(id);if(!h.Grabbed)return;
            var b=body.GetBodyState();Vector3 shoulder=Quaternion.Inverse(b.Rotation)*(h.ShoulderPosition-b.Position);
            body.SetHandTarget(id,h.Position-desiredTorso-shoulder,.7f);
        }
        public string Snapshot()
        {
            var b=body.GetBodyState();var l=body.GetHandState(HandId.Left);var r=body.GetHandState(HandId.Right);
            return $"arena={name} stairs={twoSteps} stage={Stage} tier={tier} seconds={Elapsed:F3} torso={b.Position:F5} velocity={b.Velocity:F5} L={l.Position:F5} Lv={l.Velocity:F5} Lt={l.TargetOffset:F5} Lgrip={l.Grabbed} Lreject={body.GetGripCandidate(HandId.Left).Reason} R={r.Position:F5} Rv={r.Velocity:F5} Rt={r.TargetOffset:F5} Rgrip={r.Grabbed} Rreject={body.GetGripCandidate(HandId.Right).Reason}";
        }
    }
}
