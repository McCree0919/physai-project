using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PhysicalAI.Structures
{
    [Serializable] public sealed class StructureDocument
    {
        [JsonProperty(Required=Required.Always)] public int schemaVersion;
        [JsonProperty(Required=Required.Always)] public string id;
        [JsonProperty(Required=Required.Always)] public Units units;
        [JsonProperty(Required=Required.Always)] public string bodyProfile;
        public GeneratorInfo generator;
        [JsonProperty(Required=Required.Always)] public MaterialSpec[] materials;
        [JsonProperty(Required=Required.Always)] public ObjectSpec[] objects;
        [JsonProperty(Required=Required.Always)] public StartSpec start;
        [JsonProperty(Required=Required.Always)] public GoalSpec goal;
        public StabilityReport validation;
    }
    [Serializable] public sealed class Units { public string length,mass,time; }
    [Serializable] public sealed class GeneratorInfo
    {
        public string name,difficulty; public int version,seed; public WallParameters parameters;
        [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public BoxStackFeatures features;
    }
    [Serializable] public sealed class WallParameters { public float heightGap,lateralGap; }
    [Serializable] public sealed class MaterialSpec
    {
        public string id,frictionCombine,bounceCombine;
        public float staticFriction,dynamicFriction,bounciness;
    }
    [Serializable] public sealed class PoseSpec
    {
        [JsonProperty(Required=Required.Always)] public float[] position;
        [JsonProperty(Required=Required.Always)] public float[] rotation;
        [JsonIgnore] public Vector3 Position => StructureJson.Vector(position);
        [JsonIgnore] public Quaternion Rotation => new Quaternion(rotation[0],rotation[1],rotation[2],rotation[3]);
    }
    [Serializable] public sealed class ObjectSpec
    {
        public string id,type,motion,materialId,surfacePolicy;
        [JsonProperty(Required=Required.Always)] public float[] size;
        [JsonProperty(Required=Required.Always)] public PoseSpec pose;
        [JsonProperty(Required=Required.AllowNull)] public float? massKg;
        public bool graspable;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public bool settleAsDynamic;
        public bool movable;
        [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public string presetId;
    }
    [Serializable] public sealed class StartSpec { public PoseSpec torso,leftHand,rightHand; }
    [Serializable] public sealed class GoalSpec
    {
        public string frame;
        public float[] center,size;
        public float dwellSeconds,maxTorsoSpeed;
    }

    public static class StructureJson
    {
        static readonly JsonSerializerSettings Settings=new JsonSerializerSettings {
            MissingMemberHandling=MissingMemberHandling.Error,TypeNameHandling=TypeNameHandling.None,
            MaxDepth=32,DateParseHandling=DateParseHandling.None,FloatParseHandling=FloatParseHandling.Double
        };
        public static StructureDocument Parse(string json)
        {
            if(json==null||json.Length>2*1024*1024)throw new InvalidDataException("JSON missing or exceeds 2 MiB");
            try
            {
                using(var reader=new JsonTextReader(new StringReader(json)) {MaxDepth=32,DateParseHandling=DateParseHandling.None})
                {
                    var token=JToken.Load(reader,new JsonLoadSettings {DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
                    if(reader.Read())throw new InvalidDataException("Trailing JSON content");
                    if(token.Type!=JTokenType.Object)throw new InvalidDataException("Structure must be an object");
                    var result=token.ToObject<StructureDocument>(JsonSerializer.Create(Settings));
                    Validate(result);return result;
                }
            }
            catch(JsonException error){throw new InvalidDataException("Invalid structure JSON: "+error.Message,error);}
        }
        public static string Serialize(StructureDocument doc) {Validate(doc);return JsonConvert.SerializeObject(doc,Formatting.Indented,Settings);}
        public static string Hash(StructureDocument doc)
        {
            var token=JObject.FromObject(doc,JsonSerializer.Create(Settings));token.Remove("validation");
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(token.ToString(Formatting.None)))).Replace("-","").ToLowerInvariant();
        }
        public static string HashWithoutMetadata(StructureDocument doc)
        {
            var token=JObject.FromObject(doc,JsonSerializer.Create(Settings));
            token.Remove("validation");token.Remove("generator");token.Remove("id");
            return token.ToString(Formatting.None);
        }
        public static Vector3 Vector(float[] values) => new Vector3(values[0],values[1],values[2]);
        static void Need(bool valid,string path) {if(!valid)throw new InvalidDataException(path);}
        static bool Finite(float value) => !float.IsNaN(value)&&!float.IsInfinity(value);
        static void Vec(float[] v,string path,bool size=false)
        {Need(v!=null&&v.Length==3,path+": expected 3 numbers");Need(v.All(x=>Finite(x)&&Mathf.Abs(x)<=100),path+": finite values within +/-100 m required");if(size)Need(v.All(x=>x>=.1f&&x<=20),path+": sizes must be 0.10..20 m");}
        static void Pose(PoseSpec pose,string path)
        {
            Need(pose!=null,path+": required");Vec(pose.position,path+".position");
            Need(pose.rotation!=null&&pose.rotation.Length==4&&pose.rotation.All(Finite),path+".rotation: four finite numbers required");
            Need(Mathf.Abs(pose.rotation.Sum(x=>x*x)-1)<.001f,path+".rotation: normalized quaternion required");
        }
        public static void Validate(StructureDocument doc)
        {
            Need(doc!=null,"document required");Need(doc.schemaVersion==1,"schemaVersion: only 1 supported");
            Need(!string.IsNullOrWhiteSpace(doc.id)&&doc.id.Length<=100,"id required, max 100 characters");
            Need(doc.units!=null&&doc.units.length=="m"&&doc.units.mass=="kg"&&doc.units.time=="s","units: m/kg/s required");
            Need(doc.bodyProfile=="layer1-validated","bodyProfile: unknown profile");
            Need(doc.materials!=null&&doc.materials.Length>0&&doc.materials.Length<=20,"materials: expected 1..20");
            var mats=new HashSet<string>();
            foreach(var m in doc.materials)
            {
                Need(m!=null&&!string.IsNullOrWhiteSpace(m.id)&&mats.Add(m.id),"materials: null/duplicate ID");
                Need(Finite(m.staticFriction)&&Finite(m.dynamicFriction)&&m.staticFriction==.15f&&m.dynamicFriction==.15f&&m.bounciness==0&&m.frictionCombine=="Average"&&m.bounceCombine=="Average","materials."+m.id+": validated 0.15/Average/zero bounce profile required");
            }
            Need(doc.objects!=null&&doc.objects.Length>0&&doc.objects.Length<=32,"objects: expected 1..32");
            var ids=new HashSet<string>();
            foreach(var o in doc.objects)
            {
                Need(o!=null&&!string.IsNullOrWhiteSpace(o.id)&&ids.Add(o.id),"objects: null/duplicate ID");
                string p="objects."+o.id;Need(o.type=="box",p+".type: only box supported");
                Need(o.motion=="static"&&!o.movable,p+": runtime dynamic/kinematic deferred");
                Need(o.settleAsDynamic ? o.massKg.HasValue&&Finite(o.massKg.Value)&&o.massKg>=10&&o.massKg<=30 : o.massKg==null,p+": settlement boxes require mass 10..30 kg; legacy static anchors require null");
                Vec(o.size,p+".size",true);Pose(o.pose,p+".pose");Need(mats.Contains(o.materialId??""),p+".materialId: missing reference");
                if(o.settleAsDynamic)Need(BoxPresets.Matches(o.presetId,o.size)&&o.massKg==BoxPresets.Mass(o.presetId),p+": unknown preset, scale or mass");
                Need(!o.graspable||o.surfacePolicy=="autoBoxTop",p+".surfacePolicy: autoBoxTop required");
            }
            Need(doc.start!=null,"start required");Pose(doc.start.torso,"start.torso");Pose(doc.start.leftHand,"start.leftHand");Pose(doc.start.rightHand,"start.rightHand");
            var rotation=doc.start.torso.Rotation;
            Need(Quaternion.Angle(rotation,doc.start.leftHand.Rotation)<.01f&&Quaternion.Angle(rotation,doc.start.rightHand.Rotation)<.01f,"start: shared orientation required by public ResetBody interface");
            var torso=doc.start.torso.Position;
            Need(Vector3.Distance(torso+rotation*new Vector3(-.3f,.28f,0),doc.start.leftHand.Position)<=.9f,"start.leftHand: exceeds arm length");
            Need(Vector3.Distance(torso+rotation*new Vector3(.3f,.28f,0),doc.start.rightHand.Position)<=.9f,"start.rightHand: exceeds arm length");
            Need(doc.goal!=null&&doc.goal.frame=="structure","goal.frame: structure required");Vec(doc.goal.center,"goal.center");Vec(doc.goal.size,"goal.size",true);
            Need(doc.goal.dwellSeconds==.3f&&doc.goal.maxTorsoSpeed==.5f,"goal: existing 0.30 s / 0.50 m/s contract required");
            if(doc.generator!=null)
            {
                Need((doc.generator.name=="rungWall"&&doc.generator.version==1)||(doc.generator.name=="boxStack"&&(doc.generator.version==1||doc.generator.version==BoxStackGeneratorV2.Version)),"generator: unsupported name/version");
                if(doc.generator.name=="rungWall")
                {
                    var p=doc.generator.parameters;Need(p!=null&&Finite(p.heightGap)&&p.heightGap>=.15f&&p.heightGap<=1.0001f&&Finite(p.lateralGap)&&p.lateralGap>=.2f&&p.lateralGap<=.8001f,"generator.parameters: outside scanned range");
                }
            }
        }
    }
}
