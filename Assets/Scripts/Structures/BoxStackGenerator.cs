using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAI.Structures
{
    public enum BoxDifficulty { Easy, Standard, Hard }
    [Serializable] public sealed class BoxStackFeatures
    {
        public int levels,constructionLevels,overhangCount,exposedTopEdges,coarseMinimumTransitions;
        public float maxRise,maxVerticalFace,maxUntoppedRise,maxOverhang,maxGap,totalHeight;
    }
    public static class BoxPresets
    {
        public static readonly string[] Ids={"Step","Tall","Wide"};
        public static Vector3 Size(string id)
        {
            switch(id){case "Step":return new Vector3(1.2f,.5f,1.2f);case "Tall":return new Vector3(1.2f,1,1.2f);case "Wide":return new Vector3(1.8f,.5f,1.2f);default:throw new ArgumentException("Unknown box preset "+id);}
        }
        public static float Mass(string id) => id=="Step"?15:25;
        public static bool Matches(string id,float[] size)
        {
            if(!Ids.Contains(id))return false;
            foreach(float scale in new[] {.95f,1f,1.05f})
            {
                Vector3 basis=Size(id)*scale;basis=new Vector3(Mathf.Round(basis.x*20)/20,Mathf.Round(basis.y*20)/20,Mathf.Round(basis.z*20)/20);
                if(Mathf.Abs(size[1]-basis.y)<.001f&&((Mathf.Abs(size[0]-basis.x)<.001f&&Mathf.Abs(size[2]-basis.z)<.001f)||(Mathf.Abs(size[0]-basis.z)<.001f&&Mathf.Abs(size[2]-basis.x)<.001f)))return true;
            }
            return false;
        }
    }
    public static class BoxStackGenerator
    {
        static float Grid(float value) => Mathf.Round(value/.05f)*.05f;
        public static StructureDocument Smoke(bool twoSteps)
        {
            var doc=Empty("smoke-"+(twoSteps?"stairs":"single"));var list=new List<ObjectSpec>(doc.objects);
            if(twoSteps)
            {
                list.Add(Box("lower","Wide",new Vector3(0,.25f,.9f),1,true));
                list.Add(Box("upper","Step",new Vector3(0,.75f,1.2f)));
            }
            else list.Add(Box("single","Tall",new Vector3(0,.5f,.6f)));
            doc.objects=list.ToArray();SetGoal(doc);doc.generator.features=BoxStackGeometry.Measure(doc);return doc;
        }
        public static StructureDocument Generate(BoxDifficulty difficulty,int seed)
        {
            var random=new System.Random(seed);var doc=Empty("stack-"+difficulty+"-"+seed);
            doc.generator.difficulty=difficulty.ToString();doc.generator.seed=seed;
            var list=new List<ObjectSpec>(doc.objects);
            if(difficulty==BoxDifficulty.Easy)
            {
                if(seed%2==0)
                {
                    float scale=Scale(random);var size=Scaled("Tall",scale);
                    list.Add(Box("base","Tall",new Vector3(0,size.y*.5f,size.z*.5f),scale));
                }
                else
                {
                    list.Add(Box("base","Wide",new Vector3(0,.25f,.9f),1,true));
                    list.Add(Box("step","Step",new Vector3(0,.75f,1.2f)));
                }
            }
            else
            {
                int levels=difficulty==BoxDifficulty.Standard?2+seed%2:3+seed%2;
                float y=0,z=0,x=0;
                for(int i=0;i<levels;i++)
                {
                    string preset=i==1?"Tall":"Step";float scale=Scale(random);Vector3 size=Scaled(preset,scale);
                    if(i>0)
                    {
                        z+=difficulty==BoxDifficulty.Standard?.05f*(1+seed%3):.15f+.05f*(seed%4);
                        if(difficulty==BoxDifficulty.Hard&&i==2)x+=.15f;
                    }
                    list.Add(Box("tier"+i,preset,new Vector3(x,y+size.y*.5f,z+size.z*.5f),scale));y+=size.y;
                }
                if(difficulty==BoxDifficulty.Hard||seed%3==0)
                {
                    float gap=difficulty==BoxDifficulty.Hard?.20f+.05f*(seed%5):.10f+.05f*(seed%3);
                    float width=list[1].size[0];
                    list.Add(Box("island","Tall",new Vector3(width*.5f+gap+.6f,.5f,.6f)));
                }
            }
            doc.objects=list.ToArray();SetGoal(doc);doc.generator.features=BoxStackGeometry.Measure(doc);StructureJson.Validate(doc);return doc;
        }
        static float Scale(System.Random r) => new[] {.95f,1f,1.05f}[r.Next(3)];
        static Vector3 Scaled(string id,float scale)
        {var s=BoxPresets.Size(id)*scale;return new Vector3(Grid(s.x),Grid(s.y),Grid(s.z));}
        public static ObjectSpec Box(string id,string preset,Vector3 center,float scale=1,bool rotate=false)
        {
            Vector3 s=Scaled(preset,scale);if(rotate)s=new Vector3(s.z,s.y,s.x);
            var result=StaticWallGenerator.Box(id,center,s,true);result.presetId=preset;result.settleAsDynamic=true;result.massKg=BoxPresets.Mass(preset);return result;
        }
        static StructureDocument Empty(string id)
        {
            var doc=new StructureDocument {schemaVersion=1,id=id,bodyProfile="layer1-validated",units=new Units {length="m",mass="kg",time="s"},
                generator=new GeneratorInfo {name="boxStack",version=1,difficulty="Smoke"},
                materials=new[] {new MaterialSpec {id="lowFriction",staticFriction=.15f,dynamicFriction=.15f,frictionCombine="Average",bounceCombine="Average",bounciness=0}},
                objects=new[] {StaticWallGenerator.Box("ground",new Vector3(0,-.1f,2),new Vector3(12,.2f,12),false)},
                start=new StartSpec {torso=StaticWallGenerator.Pose(new Vector3(0,.4f,-.4f)),leftHand=StaticWallGenerator.Pose(new Vector3(-.3f,1.15f,-.15f)),rightHand=StaticWallGenerator.Pose(new Vector3(.3f,.95f,-.15f))}};
            return doc;
        }
        public static void SetGoal(StructureDocument doc)
        {
            var top=doc.objects.Where(o=>o.settleAsDynamic).OrderByDescending(o=>o.pose.Position.y+o.size[1]*.5f).First();
            doc.goal=new GoalSpec {frame="structure",center=new[] {top.pose.Position.x,top.pose.Position.y+top.size[1]*.5f+.6f,top.pose.Position.z},size=new[] {.6f,.4f,.6f},dwellSeconds=.3f,maxTorsoSpeed=.5f};
        }
    }
    public static class BoxStackGeometry
    {
        static Bounds BoundsOf(ObjectSpec o)
        {
            var r=o.pose.Rotation;Vector3 s=StructureJson.Vector(o.size);
            var x=r*Vector3.right*s.x;var y=r*Vector3.up*s.y;var z=r*Vector3.forward*s.z;
            return new Bounds(o.pose.Position,new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)));
        }
        public static List<Vector3> Edges(StructureDocument d)
        {
            var edges=new List<Vector3>();
            foreach(var o in d.objects.Where(o=>o.graspable))
            {
                var b=BoundsOf(o);
                var points=new[] {new Vector3(b.center.x,b.max.y,b.min.z),new Vector3(b.center.x,b.max.y,b.max.z),new Vector3(b.min.x,b.max.y,b.center.z),new Vector3(b.max.x,b.max.y,b.center.z)};
                foreach(var p in points)
                {
                    var clearance=new Bounds(p+Vector3.up*.115f,new Vector3(.2f,.22f,.2f));bool clear=true;
                    foreach(var other in d.objects)if(other!=o&&BoundsOf(other).Intersects(clearance)){clear=false;break;}
                    if(clear)edges.Add(p);
                }
            }
            return edges;
        }
        public static bool Reachable(StructureDocument d)
            => MinimumTransitions(d)>=0;
        public static int MinimumTransitions(StructureDocument d)
        {
            var edges=Edges(d);edges.Insert(0,d.start.torso.Position-Vector3.up*.4f);var distance=Enumerable.Repeat(int.MaxValue,edges.Count).ToArray();distance[0]=0;
            for(int pass=0;pass<edges.Count;pass++)for(int i=0;i<edges.Count;i++)if(distance[i]!=int.MaxValue)for(int j=1;j<edges.Count;j++)
                if(Mathf.Abs(edges[j].y-edges[i].y)<=1.5f&&Vector3.Distance(edges[j],edges[i])<=1.8f)distance[j]=Math.Min(distance[j],distance[i]+1);
            float top=StructureJson.Vector(d.goal.center).y-.6f;
            int best=int.MaxValue;for(int i=0;i<edges.Count;i++)if(Mathf.Abs(edges[i].y-top)<.08f)best=Math.Min(best,distance[i]);return best==int.MaxValue?-1:best;
        }
        public static BoxStackFeatures Measure(StructureDocument d)
        {
            var boxes=d.objects.Where(o=>o.settleAsDynamic).Select(BoundsOf).ToArray();var heights=boxes.Select(b=>Mathf.Round(b.max.y*20)/20).Distinct().OrderBy(x=>x).ToArray();
            var f=new BoxStackFeatures {levels=heights.Length,totalHeight=heights.Last(),maxVerticalFace=boxes.Max(b=>b.size.y),exposedTopEdges=Edges(d).Count};
            var depths=new int[boxes.Length];
            foreach(int i in Enumerable.Range(0,boxes.Length).OrderBy(i=>boxes[i].min.y))
            {
                depths[i]=1;
                for(int j=0;j<boxes.Length;j++)if(j!=i&&boxes[j].center.y<boxes[i].center.y&&Mathf.Abs(boxes[j].max.y-boxes[i].min.y)<.06f
                    &&boxes[j].min.x<boxes[i].max.x&&boxes[j].max.x>boxes[i].min.x&&boxes[j].min.z<boxes[i].max.z&&boxes[j].max.z>boxes[i].min.z)
                    depths[i]=Math.Max(depths[i],depths[j]+1);
            }
            int highest=Enumerable.Range(0,boxes.Length).OrderByDescending(i=>boxes[i].max.y).First();f.constructionLevels=depths[highest];
            float previous=0;foreach(float h in heights){f.maxRise=Mathf.Max(f.maxRise,h-previous);previous=h;}
            previous=0;foreach(float h in Edges(d).Select(p=>p.y).Distinct().OrderBy(h=>h)){f.maxUntoppedRise=Mathf.Max(f.maxUntoppedRise,h-previous);previous=h;}
            f.coarseMinimumTransitions=MinimumTransitions(d);
            foreach(var b in boxes.Where(b=>b.min.y>.05f))
            {
                var supports=boxes.Where(a=>Mathf.Abs(a.max.y-b.min.y)<.06f&&a.min.x<b.max.x&&a.max.x>b.min.x&&a.min.z<b.max.z&&a.max.z>b.min.z).ToArray();
                if(supports.Length==0)continue;
                float protrusion=supports.Min(a=>Mathf.Max(0,a.min.x-b.min.x,b.max.x-a.max.x,a.min.z-b.min.z,b.max.z-a.max.z));
                if(protrusion>.025f){f.overhangCount++;f.maxOverhang=Mathf.Max(f.maxOverhang,protrusion);}
            }
            for(int i=0;i<boxes.Length;i++)for(int j=i+1;j<boxes.Length;j++)
                if(Mathf.Abs(boxes[i].min.y-boxes[j].min.y)<.05f)
                {float gap=Mathf.Max(boxes[i].min.x-boxes[j].max.x,boxes[j].min.x-boxes[i].max.x);if(gap>0)f.maxGap=Mathf.Max(f.maxGap,gap);}
            return f;
        }
    }
}
