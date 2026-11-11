using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhysicalAI.Structures
{
    // Generator v2 (LAYER3_GENERATOR_V2.md): the same Step/Tall/Wide presets users build with, but every placement parameter is drawn
    // independently (no seed-modulo coupling). Each level's box sits on the previous one, set back by that level's tread depth.
    // Hard constraint: every tread the body must stand on is at least ClimberGeometry.MinTreadDepth deep; the only narrower treads are
    // optional Hard "ledges" that can be skipped (combined rise within reach), so they are never required.
    // Physical settlement is still checked separately; nothing here looks at any controller or policy outcome.
    public static class BoxStackGeneratorV2
    {
        public const int Version=2;
        const float Grid=.05f;
        static float Snap(float v) => Mathf.Round(v/Grid)*Grid;
        static float Uniform(System.Random r,float lo,float hi)
        {
            int a=Mathf.RoundToInt(lo/Grid),b=Mathf.RoundToInt(hi/Grid);return r.Next(a,b+1)*Grid;
        }
        sealed class Level {public string Preset;public float Scale;public bool Rotate;public Vector3 Size,Center;public float Tread;}
        static Vector3 SizeOf(string preset,float scale,bool rotate){var s=BoxStackGenerator.Scaled(preset,scale);return rotate?new Vector3(s.z,s.y,s.x):s;}

        public static StructureDocument Generate(BoxDifficulty difficulty,int seed)
        {
            var r=new System.Random(seed);
            var doc=BoxStackGenerator.Empty("stack2-"+difficulty.ToString().ToLowerInvariant()+"-"+seed);
            doc.generator.version=Version;doc.generator.difficulty=difficulty.ToString();doc.generator.seed=seed;
            int count=difficulty==BoxDifficulty.Easy?1+r.Next(2):difficulty==BoxDifficulty.Standard?2+r.Next(2):3+r.Next(3);
            float lateral=difficulty==BoxDifficulty.Hard?.30f:.20f; // upper-level x offset bound (also kept within the lower top face)
            float minTread=Mathf.Ceil(ClimberGeometry.MinTreadDepth/Grid-1e-4f)*Grid; // 0.50 m on the 0.05 m grid
            // Hard: optionally one skippable narrow ledge (decided up front, kept only if the combined rise stays within reach).
            int ledge=difficulty==BoxDifficulty.Hard&&r.NextDouble()<.5?r.Next(count-1):-1;
            var levels=new List<Level>();
            for(int i=0;i<count;i++)
            {
                Level level=null;
                for(int attempt=0;attempt<40&&level==null;attempt++)
                {
                    string preset=Preset(r,difficulty,i);float scale=new[] {.95f,1f,1.05f}[r.Next(3)];bool rotate=preset=="Wide"&&r.Next(2)==0;
                    var size=SizeOf(preset,scale,rotate);
                    if(i==0){level=new Level {Preset=preset,Scale=scale,Rotate=rotate,Size=size,Center=new Vector3(0,size.y*.5f,size.z*.5f)};break;}
                    var below=levels[i-1];float belowMinZ=below.Center.z-below.Size.z*.5f,belowMaxZ=below.Center.z+below.Size.z*.5f;
                    // Support: the upper box's centre stays at least 0.10 m inside the lower top face.
                    float maxTread=Snap(below.Size.z-size.z*.5f-.10f);
                    bool narrow=i-1==ledge&&attempt<20; // after 20 failed draws the ledge becomes a normal tread
                    float lo=narrow?.10f:minTread,hi=narrow?Mathf.Min(.40f,maxTread):Mathf.Min(.80f,maxTread);
                    if(hi<lo)continue;
                    float tread=Uniform(r,lo,hi);
                    if(narrow)
                    {
                        // Skippable only: from the stance below the ledge (the spawn point when the ledge is the first level, else the back of
                        // the tread below, torso half-depth with tolerance from the ledge riser), this box's front edge must be within arm reach
                        // of a shoulder (BoxStackFeasibility's test).
                        float stance=i-1==0?belowMinZ-doc.start.torso.Position.z:ClimberGeometry.MinTreadDepth*.5f;
                        float rise=below.Size.y+size.y-ClimberGeometry.StandingShoulderHeight,back=stance+tread,side=ClimberGeometry.ShoulderHalfWidth;
                        if(side*side+rise*rise+back*back>ClimberGeometry.MaxHandOffset*ClimberGeometry.MaxHandOffset)continue;
                    }
                    float dx=Mathf.Min(lateral,Mathf.Max(0,below.Size.x*.5f-.10f));float x=below.Center.x+(dx>0?Uniform(r,-dx,dx):0);
                    level=new Level {Preset=preset,Scale=scale,Rotate=rotate,Size=size,Center=new Vector3(x,below.Center.y+below.Size.y*.5f+size.y*.5f,belowMinZ+tread+size.z*.5f)};
                    below.Tread=tread;
                }
                if(level==null)break; // no supported placement for this level: the structure ends one level lower
                levels.Add(level);
            }
            var list=new List<ObjectSpec>(doc.objects);
            for(int i=0;i<levels.Count;i++){var l=levels[i];list.Add(BoxStackGenerator.Box("level"+i,l.Preset,l.Center,l.Scale,l.Rotate));}
            // Side island (distractor or alternative hold): Standard 30 %, Hard 60 %, gap 0.10-0.40 m from the base box.
            double islandChance=difficulty==BoxDifficulty.Standard?.3:difficulty==BoxDifficulty.Hard?.6:0;
            if(r.NextDouble()<islandChance)
            {
                string preset=r.Next(2)==0?"Tall":"Step";float scale=new[] {.95f,1f,1.05f}[r.Next(3)];var size=SizeOf(preset,scale,false);
                float side=r.Next(2)==0?-1:1,gap=Uniform(r,.10f,.40f);
                // Gap measured from the outermost level that overlaps the island's height range.
                float edge=levels.Where(l=>l.Center.y-l.Size.y*.5f<size.y).Max(l=>side>0?l.Center.x+l.Size.x*.5f:-(l.Center.x-l.Size.x*.5f));
                list.Add(BoxStackGenerator.Box("island",preset,new Vector3(side*(edge+gap+size.x*.5f),size.y*.5f,levels[0].Size.z*.5f+Uniform(r,-.2f,.2f)),scale,false));
            }
            doc.objects=list.ToArray();BoxStackGenerator.SetGoal(doc);doc.generator.features=BoxStackGeometry.Measure(doc);StructureJson.Validate(doc);return doc;
        }
        static string Preset(System.Random r,BoxDifficulty difficulty,int i)
        {
            // Easy: no Tall above the first level (single faces up to ~1 m only from the ground). Standard/Hard: any preset at any level.
            if(difficulty==BoxDifficulty.Easy&&i>0)return r.Next(2)==0?"Step":"Wide";
            return BoxPresets.Ids[r.Next(BoxPresets.Ids.Length)];
        }
    }
}
