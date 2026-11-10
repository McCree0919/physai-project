using System;
using System.Collections.Generic;
using System.IO;
using PhysicalAI.Climber;
using UnityEngine;

namespace PhysicalAI.Structures
{
    public static class StaticStructureLoader
    {
        public static ClimberBody LoadFile(string path,Vector3 position,Quaternion rotation,bool scripted=false)
            => Load(StructureJson.Parse(File.ReadAllText(path)),position,rotation,scripted);
        public static ClimberBody Load(StructureDocument doc,Vector3 position,Quaternion rotation,bool scripted=false)
        {
            // Never trust a validation stamp from an imported document.
            var report=StaticStabilityValidator.Check(doc);
            if(!report.passed)throw new InvalidDataException("Structure rejected: "+report.reason);
            var material=CreateMaterial(doc.materials[0]);
            ClimberBody body=null;
            try
            {
                body=ClimberArenaFactory.Create(doc.id,position,material,false);body.transform.rotation=rotation;
                var resources=body.gameObject.AddComponent<StructureResources>();resources.Materials.Add(material);
                var old=new List<GameObject>();
                foreach(Transform child in body.transform)if(child.GetComponent<Rigidbody>()==null)old.Add(child.gameObject);
                foreach(var item in old){item.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(item);else UnityEngine.Object.DestroyImmediate(item);}
                BuildGeometry(doc,body.transform,resources);
                var goal=body.GetComponent<ClimberGoal>();goal.Center=StructureJson.Vector(doc.goal.center);goal.Size=StructureJson.Vector(doc.goal.size);
                body.ResetBody(new BodyReset {Torso=doc.start.torso.Position,LeftHand=doc.start.leftHand.Position,RightHand=doc.start.rightHand.Position,Rotation=doc.start.torso.Rotation});
                if(scripted)
                {
                    if(!StaticWallGenerator.IsParametricRoute(doc))throw new InvalidDataException("Existing controllers require the canonical four-rung route");
                    if(StaticWallGenerator.IsLegacyRoute(doc))body.gameObject.AddComponent<ClimberScriptController>().Configure(body,goal);
                    else body.gameObject.AddComponent<StructureScriptDriver>().Configure(body,doc.generator.parameters.heightGap,doc.generator.parameters.lateralGap);
                }
                Physics.SyncTransforms();return body;
            }
            catch
            {
                if(body!=null){body.gameObject.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(body.gameObject);else UnityEngine.Object.DestroyImmediate(body.gameObject);}
                else {if(Application.isPlaying)UnityEngine.Object.Destroy(material);else UnityEngine.Object.DestroyImmediate(material);}
                throw;
            }
        }
        public static List<BoxCollider> BuildGeometry(StructureDocument doc,Transform parent,StructureResources resources)
        {
            var mats=new Dictionary<string,PhysicsMaterial>();
            foreach(var spec in doc.materials){var mat=CreateMaterial(spec);resources.Materials.Add(mat);mats.Add(spec.id,mat);}
            var result=new List<BoxCollider>();
            foreach(var o in doc.objects)
            {
                var collider=ClimberArenaFactory.Box(o.id,parent,o.pose.Position,StructureJson.Vector(o.size),mats[o.materialId],o.graspable);
                collider.transform.localRotation=o.pose.Rotation;result.Add(collider);
            }
            return result;
        }
        static PhysicsMaterial CreateMaterial(MaterialSpec spec) => new PhysicsMaterial(spec.id) {
            staticFriction=spec.staticFriction,dynamicFriction=spec.dynamicFriction,bounciness=spec.bounciness,
            frictionCombine=PhysicsMaterialCombine.Average,bounceCombine=PhysicsMaterialCombine.Average
        };
    }
}
