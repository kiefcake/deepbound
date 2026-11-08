using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>Keeps the tour's stand-in for the player's carried light (Art/PresenceLight) ahead of the tour camera.</summary>
    public class TourFollow : MonoBehaviour
    {
        public Camera Cam;

        private void LateUpdate()
        {
            if (Cam == null) return;
            var eye = Cam.transform.position;
            var fwd = Cam.transform.forward;
            float d = Art.PresenceLight.Ahead;
            if (Physics.Raycast(eye, fwd, out var hit, d + 0.4f, ~0, QueryTriggerInteraction.Ignore)) d = Mathf.Max(1.4f, hit.distance - 0.4f);
            transform.position = eye + fwd * d + Vector3.up * 0.3f;
        }
    }
}
