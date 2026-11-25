using UnityEngine;

namespace Deepbound.Art
{
    public class CloakSway : MonoBehaviour
    {
        private Transform body;
        private Vector3 lastPos;
        private float lastYaw, pitch, pitchVel, roll, rollVel, seed;
        private bool primed;

        private void Start()
        {
            body = transform.parent;
            seed = Random.value * 30f;
        }

        private void LateUpdate()
        {
            if (body == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var pos = body.position;
            float yaw = body.eulerAngles.y;
            if (!primed) { lastPos = pos; lastYaw = yaw; primed = true; return; }
            var v = (pos - lastPos) / dt;
            float turn = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastPos = pos; lastYaw = yaw;
            if (v.sqrMagnitude > 900f) v = Vector3.zero;

            var local = body.InverseTransformDirection(v);
            float stir = (Mathf.PerlinNoise(Time.time * 0.5f, seed) - 0.5f) * 4f;

            float wantPitch = Mathf.Clamp(local.z * 4.5f - v.y * 1.5f, -3f, 38f) + stir;
            float wantRoll = Mathf.Clamp(local.x * 3.5f + turn * 0.03f, -20f, 20f);

            pitchVel += ((wantPitch - pitch) * 60f - pitchVel * 9f) * dt;
            pitch += pitchVel * dt;
            rollVel += ((wantRoll - roll) * 50f - rollVel * 8f) * dt;
            roll += rollVel * dt;
            pitch = Mathf.Clamp(pitch, -4f, 45f);
            transform.localRotation = Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
