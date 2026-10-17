using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>Owns a mesh combined at runtime (see <see cref="MeshKit.MergeParts"/>) and frees it with the object.</summary>
    public class MergedMesh : MonoBehaviour
    {
        public Mesh Mesh;

        private void OnDestroy()
        {
            if (Mesh != null) Destroy(Mesh);
        }
    }
}
