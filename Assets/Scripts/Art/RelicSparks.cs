using UnityEngine;

namespace Deepbound.Art
{
    public class RelicSparks : MonoBehaviour
    {
        public Color Color = Color.white;
        private float next;

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.12f;
            var cam = ViewCamera.Current;
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > 40f * 40f) return;
            Vfx.Mote(transform.position, Color);
        }
    }
}
