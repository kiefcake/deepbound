using UnityEngine;

namespace Deepbound.Art
{
    public class MergedMesh : MonoBehaviour
    {
        public Mesh Mesh;

        private void OnDestroy()
        {
            if (Mesh != null) Destroy(Mesh);
        }
    }
}
