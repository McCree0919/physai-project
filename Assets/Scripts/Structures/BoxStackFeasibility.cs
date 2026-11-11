using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAI.Structures
{
    // Body-derived geometry limits (LAYER3_GENERATOR_V2.md §2). Values mirror ClimberArenaFactory/ClimberBody/ClimberAgent;
    // ClimberGeometryTests asserts they match the live body.
    public static class ClimberGeometry
    {
        public const float TorsoWidth=.60f,TorsoHeight=.80f,TorsoDepth=.35f;   // torso box x, y, z
        public const float ShoulderHeight=.28f,MaxHandOffset=.85f,HandRadius=.10f; // shoulder above torso centre; SetHandTarget clamp; hand sphere
        public const float YawToleranceDegrees=5,PositionTolerance=.025f,HeadroomMargin=.05f;
        static float Cos => Mathf.Cos(YawToleranceDegrees*Mathf.Deg2Rad);
        static float Sin => Mathf.Sin(YawToleranceDegrees*Mathf.Deg2Rad);
        // Stable support (ClimberAgent.TrySupport) needs all five rays (centre and (+/-0.30, +/-0.175)) on one top face, i.e. the whole
        // footprint; rotation X/Z is locked so tipping is not the binding limit. Footprint under the yaw tolerance plus +/-PositionTolerance:
        public static float MinTreadDepth => TorsoDepth*Cos+TorsoWidth*Sin+2*PositionTolerance;   // 0.451 m (strict geometric minimum 0.35)
        public static float MinTreadWidth => TorsoWidth*Cos+TorsoDepth*Sin+2*PositionTolerance;   // 0.678 m (strict 0.60)
        public static float Headroom => TorsoHeight+HeadroomMargin;                                // 0.85 m
        // Highest graspable edge above a standing surface: shoulder at torso half-height + 0.28; hand within 0.85 m of the shoulder while the
        // edge is at least torso half-depth + hand radius in front of the shoulder.
        public static float Reach => TorsoHeight*.5f+ShoulderHeight+Mathf.Sqrt(MaxHandOffset*MaxHandOffset-Mathf.Pow(TorsoDepth*.5f+HandRadius,2)); // 1.484 m
        // Standing shoulder height above the surface; an edge point is graspable from a standing spot when it lies within MaxHandOffset of
        // either shoulder (torso centre +/-0.30 m in x). The body cannot walk: on the ground it stands only at its spawn position (the
        // audit probe shows the torso does not leave the spawn point before the hands hold something).
        public static float StandingShoulderHeight => TorsoHeight*.5f+ShoulderHeight; // 0.68 m
        public const float ShoulderHalfWidth=.30f;
    }

    [Serializable] public sealed class SurfaceFeasibility
    {
        public string id;public float top,freeDepth,freeWidth;public bool standable,reachable,required;
    }
    [Serializable] public sealed class FeasibilityReport
    {
        public bool passed,coarseReachable;public string reason;public float minTreadDepth,minTreadWidth,reach;
        public string[] route;public SurfaceFeasibility[] surfaces;
    }

    // Geometry-only feasibility (no controller, no policy): can the body stand on a chain of top faces from the ground to the goal box,
    // each next face's grip edge within reach of a standing spot on the previous one? Used for audit and as the success-rate denominator,
    // never to filter generated structures.
    public static class BoxStackFeasibility
    {
        sealed class Face {public string Id;public Bounds Box;public float Top;public List<Vector2> Spots=new List<Vector2>();}
        static Bounds BoundsOf(ObjectSpec o)
        {
            var r=o.pose.Rotation;Vector3 s=StructureJson.Vector(o.size);
            var x=r*Vector3.right*s.x;var y=r*Vector3.up*s.y;var z=r*Vector3.forward*s.z;
            return new Bounds(o.pose.Position,new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)));
        }
        public static FeasibilityReport Check(StructureDocument d)
        {
            float depth=ClimberGeometry.MinTreadDepth,width=ClimberGeometry.MinTreadWidth,head=ClimberGeometry.Headroom,reach=ClimberGeometry.Reach;
            var report=new FeasibilityReport {minTreadDepth=depth,minTreadWidth=width,reach=reach,coarseReachable=BoxStackGeometry.Reachable(d)};
            var boxes=d.objects.Where(o=>o.settleAsDynamic).Select(o=>(o.id,b:BoundsOf(o))).ToList();
            var faces=new List<Face>();
            foreach(var (id,b) in boxes)
            {
                // Torso footprint (with tolerance) placements on the 0.05 m grid that lie inside this top face and under no other box within the headroom.
                var f=new Face {Id=id,Box=b,Top=b.max.y};
                for(float x=b.min.x+width*.5f;x<=b.max.x-width*.5f+1e-4f;x+=.05f)for(float z=b.min.z+depth*.5f;z<=b.max.z-depth*.5f+1e-4f;z+=.05f)
                {
                    var foot=new Bounds(new Vector3(x,f.Top+head*.5f,z),new Vector3(width-.002f,head-.002f,depth-.002f));
                    if(!boxes.Any(o=>o.id!=id&&o.b.Intersects(foot)))f.Spots.Add(new Vector2(x,z));
                }
                faces.Add(f);
            }
            var edges=BoxStackGeometry.Edges(d);
            // Ground: the body cannot walk, so its only ground stance is the spawn position.
            var start=d.start.torso.Position;var ground=new Face {Id="ground",Top=0};ground.Spots.Add(new Vector2(start.x,start.z));
            float shoulderY=ClimberGeometry.StandingShoulderHeight,arm=ClimberGeometry.MaxHandOffset,half=ClimberGeometry.ShoulderHalfWidth;
            bool Reaches(Face from,Face to)
            {
                if(to.Spots.Count==0)return false;
                foreach(var e in edges)
                {
                    if(Mathf.Abs(e.y-to.Top)>.01f||e.x<to.Box.min.x-.01f||e.x>to.Box.max.x+.01f||e.z<to.Box.min.z-.01f||e.z>to.Box.max.z+.01f)continue;
                    if(e.y<=from.Top+.01f||e.y-from.Top>reach)continue;
                    foreach(var s in from.Spots)
                    {
                        float dy=e.y-(from.Top+shoulderY),dz=e.z-s.y;
                        float dx=Mathf.Min(Mathf.Abs(e.x-(s.x-half)),Mathf.Abs(e.x-(s.x+half)));
                        if(dx*dx+dy*dy+dz*dz<=arm*arm)return true;
                    }
                }
                return false;
            }
            var goalBox=boxes.OrderByDescending(o=>o.b.max.y).First().id;
            List<string> Path(string skip)
            {
                var previous=new Dictionary<Face,Face> {{ground,null}};var queue=new Queue<Face>();queue.Enqueue(ground);
                while(queue.Count>0)
                {
                    var f=queue.Dequeue();
                    if(f.Id==goalBox){var path=new List<string>();for(var p=f;p!=null;p=previous[p])path.Insert(0,p.Id);return path;}
                    foreach(var next in faces)if(next.Id!=skip&&!previous.ContainsKey(next)&&Reaches(f,next)){previous[next]=f;queue.Enqueue(next);}
                }
                return null;
            }
            var route=Path(null);
            var reachableIds=new HashSet<string>();
            {
                var seen=new HashSet<Face> {ground};var queue=new Queue<Face>();queue.Enqueue(ground);
                while(queue.Count>0){var f=queue.Dequeue();foreach(var n in faces)if(!seen.Contains(n)&&Reaches(f,n)){seen.Add(n);reachableIds.Add(n.Id);queue.Enqueue(n);}}
            }
            report.surfaces=faces.Select(f=>new SurfaceFeasibility {id=f.Id,top=f.Top,standable=f.Spots.Count>0,reachable=reachableIds.Contains(f.Id),
                freeDepth=f.Spots.Count>0?f.Spots.Max(s=>s.y)-f.Spots.Min(s=>s.y)+depth:0,freeWidth=f.Spots.Count>0?f.Spots.Max(s=>s.x)-f.Spots.Min(s=>s.x)+width:0}).ToArray();
            report.route=route?.ToArray()??new string[0];
            if(route==null)
            {
                var stuck=faces.Where(f=>!reachableIds.Contains(f.Id)).OrderBy(f=>f.Top).FirstOrDefault();
                report.reason=stuck==null?"GoalUnreachable":(stuck.Spots.Count==0?$"NoStandingSpot:{stuck.Id}":$"OutOfReach:{stuck.Id}");return report;
            }
            // A face is required when the goal becomes unreachable without it.
            foreach(var s in report.surfaces)s.required=s.id!=goalBox&&route.Contains(s.id)&&Path(s.id)==null;
            report.passed=report.coarseReachable;report.reason=report.passed?"StandingChainToGoal":"CoarseReachability";return report;
        }
    }
}
