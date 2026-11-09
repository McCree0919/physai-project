using UnityEngine;

namespace PhysicalAI.Climber.Layer2Prep
{
    // A new parameterized policy. Its only body operations are the public interface.
    public sealed class PrepClimbController
    {
        public bool Done {get;private set;}
        public bool Success {get;private set;}
        public string Reason {get;private set;}="running";
        public string Detail {get;private set;}="";
        public float Elapsed {get;private set;}
        public float PeakConstraint {get;private set;}
        public float PeakActive {get;private set;}
        public int Decisions {get;private set;}
        public int Handovers {get;private set;}
        public int StageIndex => next;
        readonly PrepWall wall;
        readonly ClimberBody body;
        readonly int[] phases=new int[2];
        readonly Vector3[] goals={Vector3.one*999,Vector3.one*999};
        int next;
        bool leftTop,rightTop,cleared,hadGrip;
        float stable,stageTime,unreachableTime,rejectionTime;
        string stage="Rung0";
        public PrepClimbController(PrepWall value){wall=value;body=value.Body;}
        public void Step(float dt)
        {
            if(Done)return;
            Elapsed+=dt;stageTime+=dt;Decisions++;
            var bs=body.GetBodyState();
            for(int i=0;i<2;i++)
            {
                var h=body.GetHandState((HandId)i);
                PeakConstraint=Mathf.Max(PeakConstraint,h.PeakConstraintLoad);
                PeakActive=Mathf.Max(PeakActive,h.CommandForce.magnitude);
            }
            if(bs.GrabbedHands>0)hadGrip=true;
            if(bs.Position.y<-.25f||(hadGrip&&bs.GrabbedHands==0)) {Finish(false,"fall","Support lost at "+stage);return;}
            if(Elapsed>=45) {Finish(false,"timeout",stage+"; controller budget 45 simulated seconds");return;}
            if(next<4)
            {
                var id=(HandId)(next%2);
                Vector3 point=new Vector3(next%2==0?-wall.LateralGap*.5f:wall.LateralGap*.5f,1.1f+next*wall.HeightGap,-.18f);
                float supportY=next==0?.4f:Mathf.Max(1.1f+(next-1)*wall.HeightGap-.43f,point.y-.80f);
                Hold((HandId)(1-(int)id),new Vector3(0,supportY,-.5f));
                Seek(id,point,dt);
                if(AtGrip(id,point))
                {
                    if(next>0){Handovers++;body.SetGrab((HandId)(1-(int)id),false);}
                    next++;Advance("Rung"+next);
                }
                return;
            }
            if(!leftTop)
            {
                Hold(HandId.Right,new Vector3(0,wall.Top-.50f,-.5f));
                Vector3 point=new Vector3(-.3f,wall.Top+.1f,.02f);Seek(HandId.Left,point,dt);
                leftTop=AtGrip(HandId.Left,point);
                if(!leftTop)return;
                Advance("RightTop");
            }
            if(!rightTop)
            {
                Hold(HandId.Left,new Vector3(0,wall.Top-.10f,-.55f));
                Vector3 point=new Vector3(.3f,wall.Top+.1f,.02f);Seek(HandId.Right,point,dt);
                rightTop=AtGrip(HandId.Right,point);
                if(!rightTop)return;
                Advance("Press");
            }
            if(bs.Position.y>=wall.Top+.43f)cleared=true;
            Vector3 desired=new Vector3(0,wall.Top+.53f,cleared?.55f:-.48f);
            Hold(HandId.Left,desired);Hold(HandId.Right,desired);
            stable=wall.Goal.Contains(bs)?stable+dt:0;
            if(stable>=.3f)Finish(true,"success","Arrived after "+Handovers+" handovers");
        }
        void Advance(string value){stage=value;stageTime=unreachableTime=rejectionTime=0;}
        void Finish(bool success,string reason,string detail){Done=true;Success=success;Reason=reason;Detail=detail;}
        bool AtGrip(HandId id,Vector3 p) => body.GetHandState(id).Grabbed&&Vector3.Distance(body.GetHandState(id).Position,p)<.05f;
        void Seek(HandId id,Vector3 point,float dt)
        {
            var bs=body.GetBodyState();var h=body.GetHandState(id);int i=(int)id;
            if(Vector3.Distance(goals[i],point)>.001f){goals[i]=point;phases[i]=0;}
            Vector3 target=point;
            switch(phases[i])
            {
                case 0:if(h.Position.z<point.z-.165f)phases[i]=1;target=new Vector3(h.Position.x,h.Position.y,point.z-.2f);break;
                case 1:if(h.Position.y>point.y+.04f&&Mathf.Abs(h.Position.x-point.x)<.04f)phases[i]=2;target=new Vector3(point.x,point.y+.08f,point.z-.2f);break;
                case 2:if(h.Position.z>point.z-.025f)phases[i]=3;target+=Vector3.up*.08f;break;
            }
            float requestDistance=Vector3.Distance(target,h.ShoulderPosition);
            body.SetHandTarget(id,Quaternion.Inverse(bs.Rotation)*(target-h.ShoulderPosition),1);
            bool near=Vector3.Distance(h.Position,point)<.05f;body.SetGrab(id,near);
            unreachableTime=requestDistance>.855f&&h.Velocity.magnitude<.08f?unreachableTime+dt:0;
            var candidate=body.GetGripCandidate(id);
            rejectionTime=near&&!h.Grabbed&&!candidate.Allowed?rejectionTime+dt:0;
            if(stageTime>8&&unreachableTime>=3)Finish(false,"unreachable",stage+" phase="+phases[i]+"; requested shoulder distance="+requestDistance.ToString("F4")+"; pose/policy limited, not a physical impossibility proof");
            else if(rejectionTime>=2)Finish(false,"grab_rejected",stage+" phase="+phases[i]+"; "+candidate.Reason);
        }
        void Hold(HandId id,Vector3 desiredTorso)
        {
            var h=body.GetHandState(id);if(!h.Grabbed)return;
            var bs=body.GetBodyState();Vector3 shoulder=Quaternion.Inverse(bs.Rotation)*(h.ShoulderPosition-bs.Position);
            body.SetHandTarget(id,h.Position-desiredTorso-shoulder,.7f);
        }
    }
}
