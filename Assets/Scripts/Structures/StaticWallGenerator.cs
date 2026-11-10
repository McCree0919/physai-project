using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PhysicalAI.Structures
{
    public enum StaticDifficulty { Easy, Standard, Extended }
    public static class StaticWallGenerator
    {
        // Parameter bands describe geometry, never a scripted-controller success whitelist.
        public static StructureDocument Generate(StaticDifficulty difficulty,int seed)
        {
            if(difficulty==StaticDifficulty.Easy&&seed==0){var baseline=GenerateAt(.25f,.4f);baseline.generator.difficulty="Easy";baseline.validation.inputHash=StructureJson.Hash(baseline);return baseline;}
            var random=new System.Random(seed);
            float height,spacing;
            switch(difficulty)
            {
                // Platform underside is top-0.20 m; lower gaps intersect the last rung.
                case StaticDifficulty.Easy:height=.20f+.05f*random.Next(3);spacing=.20f+.05f*random.Next(13);break;
                case StaticDifficulty.Standard:height=.35f+.05f*random.Next(6);spacing=.20f+.05f*random.Next(13);break;
                case StaticDifficulty.Extended:height=.65f+.05f*random.Next(8);spacing=.20f+.05f*random.Next(13);break;
                default:throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
            var doc=GenerateAt(height,spacing,seed);doc.generator.difficulty=difficulty.ToString();doc.validation.inputHash=StructureJson.Hash(doc);return doc;
        }
        public static StructureDocument GenerateAt(float height,float spacing,int seed=0)
        {
            if(float.IsNaN(height)||float.IsInfinity(height)||float.IsNaN(spacing)||float.IsInfinity(spacing)
                ||height<.15f||height>1f||spacing<.2f||spacing>.8f)
                throw new InvalidDataException("Wall parameters must be finite: height 0.15..1.00 m, spacing 0.20..0.80 m");
            // Necessary, deliberately coarse two-arm reach bound between adjacent top faces.
            // It is not a trajectory, IK, collision-free path, or scripted-success proof.
            float lateralClearance=Mathf.Max(0,spacing-.5f);
            if(Mathf.Sqrt(height*height+lateralClearance*lateralClearance)>2*.9f)
                throw new InvalidDataException("Adjacent top faces exceed the coarse two-arm span");
            var objects=new List<ObjectSpec>();
            objects.Add(Box("ground",new Vector3(0,-.1f,0),new Vector3(3,.2f,3),false));
            for(int i=0;i<4;i++)objects.Add(Box("rung"+i,new Vector3(i%2==0?-spacing*.5f:spacing*.5f,.95f+i*height,-.075f),new Vector3(.5f,.1f,.25f),true));
            float top=1+4*height;
            objects.Add(Box("platform",new Vector3(0,top-.1f,.75f),new Vector3(2,.2f,1.5f),true));
            objects.Add(Box("wall",new Vector3(0,(top-.2f)*.5f,.45f),new Vector3(2,top-.2f,.15f),false));
            var doc=new StructureDocument {
                schemaVersion=1,id="wall-"+seed,bodyProfile="layer1-validated",units=new Units {length="m",mass="kg",time="s"},
                generator=new GeneratorInfo {name="rungWall",version=1,seed=seed,difficulty="CustomGeometry",parameters=new WallParameters {heightGap=height,lateralGap=spacing}},
                materials=new[] {new MaterialSpec {id="lowFriction",staticFriction=.15f,dynamicFriction=.15f,frictionCombine="Average",bounciness=0,bounceCombine="Average"}},
                objects=objects.ToArray(),start=new StartSpec {torso=Pose(new Vector3(0,.4f,-.4f)),leftHand=Pose(new Vector3(-.3f,1.15f,-.05f)),rightHand=Pose(new Vector3(.3f,.95f,-.05f))},
                goal=new GoalSpec {frame="structure",center=new[] {0,top+.6f,.6f},size=new[] {.8f,.4f,.6f},dwellSeconds=.3f,maxTorsoSpeed=.5f}
            };
            StructureJson.Validate(doc);
            var stability=StaticStabilityValidator.Check(doc);
            if(!stability.passed)throw new InvalidDataException("Generated structure rejected: "+stability.reason);
            doc.validation=stability;return doc;
        }
        public static PoseSpec Pose(Vector3 p) => new PoseSpec {position=new[] {p.x,p.y,p.z},rotation=new[] {0f,0f,0f,1f}};
        public static ObjectSpec Box(string id,Vector3 p,Vector3 size,bool grab) => new ObjectSpec {
            id=id,type="box",motion="static",massKg=null,materialId="lowFriction",surfacePolicy=grab?"autoBoxTop":null,graspable=grab,
            size=new[] {size.x,size.y,size.z},pose=Pose(p)
        };
        public static bool IsLegacyRoute(StructureDocument doc)
        {
            if(doc.generator==null||Mathf.Abs(doc.generator.parameters.heightGap-.25f)>.0001f||Mathf.Abs(doc.generator.parameters.lateralGap-.4f)>.0001f)return false;
            // Metadata alone cannot make arbitrary geometry safe for a hard-coded controller.
            return StructureJson.HashWithoutMetadata(doc)==StructureJson.HashWithoutMetadata(GenerateAt(.25f,.4f));
        }
        public static bool IsParametricRoute(StructureDocument doc)
        {
            if(doc.generator==null)return false;
            try
            {
                var p=doc.generator.parameters;
                return StructureJson.HashWithoutMetadata(doc)==StructureJson.HashWithoutMetadata(GenerateAt(p.heightGap,p.lateralGap));
            }
            catch(InvalidDataException){return false;}
        }
    }
}
