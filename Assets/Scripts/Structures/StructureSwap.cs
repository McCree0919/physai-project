using System.Collections.Generic;
using PhysicalAI.Climber;
using UnityEngine;

namespace PhysicalAI.Structures
{
    // Runtime structure swap for curriculum training (LAYER3_DESIGN §17): replaces an arena's static geometry and goal with a frozen,
    // already settled document. No settlement runs here; the frozen set's settlement check and sha256 are what make the document trusted.
    // The result matches StaticStructureLoader + ClimbTrainingBuilder.PrepareEnvironment: same object names, layers, surfaces and one
    // shared physics material, so snapshot geometry hashes of the training two-step stay valid.
    public static class StructureSwap
    {
        public const int SurfaceLayer=8,StaticLayer=9;
        public static void Apply(ClimberBody arena,StructureDocument doc,PhysicsMaterial material)
        {
            if(material==null)throw new System.ArgumentNullException(nameof(material),"Every structure collider needs an explicit physics material");
            var old=new List<GameObject>();
            foreach(Transform child in arena.transform)if(child.gameObject.activeSelf&&child.GetComponent<Rigidbody>()==null)old.Add(child.gameObject);
            // Inactive colliders leave the physics scene at once; the deferred Destroy only frees the objects.
            foreach(var item in old){item.SetActive(false);if(Application.isPlaying)Object.Destroy(item);else Object.DestroyImmediate(item);}
            foreach(var o in doc.objects)
            {
                var collider=ClimberArenaFactory.Box(o.id,arena.transform,o.pose.Position,StructureJson.Vector(o.size),material,o.graspable);
                collider.transform.localRotation=o.pose.Rotation;
                if(!o.graspable)collider.gameObject.layer=StaticLayer;
            }
            var goal=arena.GetComponent<ClimberGoal>();goal.Center=StructureJson.Vector(doc.goal.center);goal.Size=StructureJson.Vector(doc.goal.size);
            Physics.SyncTransforms();
        }
    }
}
