using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAI.Structures
{
    public sealed class StructureResources : MonoBehaviour
    {
        public readonly List<PhysicsMaterial> Materials=new List<PhysicsMaterial>();
        void OnDestroy()
        {
            foreach(var material in Materials)if(material!=null)
            {
                if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);
            }
        }
    }
}
