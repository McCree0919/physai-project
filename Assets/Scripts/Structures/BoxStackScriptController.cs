using System.Collections.Generic;
using System.Linq;
using PhysicalAI.Climber;
using UnityEngine;

namespace PhysicalAI.Structures
{
    // BoxClimbSmokeController's Seek / Press / Release sequence with per-level parameters taken from the structure instead of the two
    // hard-coded layouts (tops 0.5/1.0 m, fronts 0/0.6 m). For the fixed two-step and single box it reduces to the same numbers.
    // Used only for the feasibility audit (LAYER3_FEASIBILITY_AUDIT.md); public body interface only, no forces or transform writes.
    [DefaultExecutionOrder(-100)]
    public sealed class BoxStackScriptController : MonoBehaviour
    {
        public struct Tier {public string Id;public float Top,Front,X,Inward;}
        ClimberBody body;ClimberGoal goal;Tier[] tiers;
        readonly int[] approach=new int[2];
        int tier;float stable,wait;Vector3 anchor;bool inside;
        public string Stage {get;private set;}="Seek";
        public bool Completed {get;private set;}
        public bool Failed {get;private set;}
        public float Elapsed {get;private set;}
        public int TierReached => tier;
        public int TierCount => tiers?.Length??0;

        // Route: the feasibility chain when there is one, otherwise every non-island box from low to high.
        public static Tier[] Route(StructureDocument d)
        {
            var report=BoxStackFeasibility.Check(d);
            var boxes=d.objects.Where(o=>o.settleAsDynamic).ToDictionary(o=>o.id);
            var ids=report.route.Length>0?report.route.Where(id=>id!="ground").ToList():boxes.Values.Where(o=>!o.id.StartsWith("island")).OrderBy(o=>o.pose.Position.y+o.size[1]*.5f).Select(o=>o.id).ToList();
            var goalZ=StructureJson.Vector(d.goal.center).z;var result=new List<Tier>();
            for(int i=0;i<ids.Count;i++)
            {
                var o=boxes[ids[i]];var c=o.pose.Position;var s=o.pose.Rotation*StructureJson.Vector(o.size);s=new Vector3(Mathf.Abs(s.x),Mathf.Abs(s.y),Mathf.Abs(s.z));
                float front=c.z-s.z*.5f,top=c.y+s.y*.5f;
                // Inward stance: middle of the tread in front of the next box (two-step tier 0: 0.30), or near the goal on the last box (0.55).
                float inward;
                if(i==ids.Count-1)inward=Mathf.Min(.55f,goalZ-front);
                else {var next=boxes[ids[i+1]];var nc=next.pose.Position;var ns=next.pose.Rotation*StructureJson.Vector(next.size);float nextFront=nc.z-Mathf.Abs(ns.z)*.5f;inward=Mathf.Clamp((nextFront-front)*.5f,.10f,.55f);}
                result.Add(new Tier {Id=o.id,Top=top,Front=front,X=c.x,Inward=inward});
            }
            return result.ToArray();
        }
        public void Configure(ClimberBody value,Tier[] route){body=value;goal=body.GetComponent<ClimberGoal>();tiers=route;anchor=body.GetBodyState().Position;}
        void FixedUpdate()
        {
            if(body==null||Completed||Failed)return;
            float dt=Time.fixedDeltaTime;Elapsed+=dt;
            var b=body.GetBodyState();
            if(Elapsed>60||b.Position.y<-.2f||tiers.Length==0){Failed=true;return;}
            var t=tiers[tier];bool last=tier==tiers.Length-1;
            if(Stage=="Seek")
            {
                for(int i=0;i<2;i++)
                {
                    var id=(HandId)i;var hand=body.GetHandState(id);
                    if(hand.Grabbed){Hold(id,anchor);continue;}
                    var point=new Vector3(t.X+(i==0?-.3f:.3f),t.Top+.1f,t.Front+.02f);
                    Vector3 target=point;
                    if(approach[i]==0)
                    {
                        target=new Vector3(point.x,point.y+.10f,t.Front-.20f);
                        if(hand.Position.y>point.y+.05f&&Mathf.Abs(hand.Position.x-point.x)<.04f&&hand.Position.z<t.Front-.15f)approach[i]=1;
                    }
                    else if(approach[i]==1)
                    {
                        target+=Vector3.up*.10f;
                        if(hand.Position.z>t.Front-.015f)approach[i]=2;
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
            if(b.Position.y>=t.Top+.43f)inside=true;
            var desired=new Vector3(t.X,t.Top+.53f,t.Front+(inside?t.Inward:-.48f));
            Hold(HandId.Left,desired);Hold(HandId.Right,desired);
            bool arrived=last?goal.Contains(b):b.Position.y>=t.Top+.40f&&b.Position.z>t.Front+t.Inward-.10f&&b.Velocity.magnitude<.5f;
            stable=arrived?stable+dt:0;
            if(stable>=.3f)
            {
                if(!last){Stage="Release";wait=0;}
                else {Completed=true;Stage="Arrived";}
            }
        }
        void Hold(HandId id,Vector3 desiredTorso)
        {
            var h=body.GetHandState(id);if(!h.Grabbed)return;
            var b=body.GetBodyState();Vector3 shoulder=Quaternion.Inverse(b.Rotation)*(h.ShoulderPosition-b.Position);
            body.SetHandTarget(id,h.Position-desiredTorso-shoulder,.7f);
        }
    }
}
