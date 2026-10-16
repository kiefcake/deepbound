using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>A view-model bowstring: two thin segments laid from each limb tip to the nock on the draw hand, every frame.</summary>
    public class BowString : MonoBehaviour
    {
        public Transform TipTop, TipBottom, Nock, Upper, Lower;

        private void LateUpdate()
        {
            if (Nock == null) return;
            Lay(Upper, TipTop);
            Lay(Lower, TipBottom);
        }

        private void Lay(Transform seg, Transform tip)
        {
            if (seg == null || tip == null) return;
            var a = tip.position; var b = Nock.position;
            var d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            seg.position = (a + b) * 0.5f;
            seg.rotation = Quaternion.LookRotation(d / len, transform.up);
            var parentScale = seg.parent != null ? seg.parent.lossyScale : Vector3.one;
            seg.localScale = new Vector3(1f / Mathf.Max(parentScale.x, 1e-4f), 1f / Mathf.Max(parentScale.y, 1e-4f), len / Mathf.Max(parentScale.z, 1e-4f));
        }
    }
}
