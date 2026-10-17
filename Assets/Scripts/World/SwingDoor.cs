using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A pair of old plank doors in a doorway: they swing into the room when anyone (player or dead)
    /// comes near and creak shut a few seconds after the last of them has gone. Purely visual and
    /// local: no collider, so they never block a path, a swing or a shot - they just make rooms
    /// rooms again, closing off the view and the light until you push through.
    /// </summary>
    public class SwingDoor : MonoBehaviour
    {
        private Transform left, right;
        private Vector3 closedL, closedR, inward;
        private float open, want, nextCheck, lastNear = -10f;
        private bool wasOpen;
        private const float Reach = 3.8f;

        public static void Build(Transform parent, Vector3 center, Vector3 tangent, Vector3 inward, float width, float height)
        {
            var root = new GameObject("Door").transform;
            root.SetParent(parent, false);
            root.position = center;
            var d = root.gameObject.AddComponent<SwingDoor>();
            d.inward = inward;
            d.closedL = tangent;
            d.closedR = -tangent;
            d.left = Leaf(root, center - tangent * width / 2f, tangent, width / 2f, height, true);
            d.right = Leaf(root, center + tangent * width / 2f, -tangent, width / 2f, height, false);
        }

        private static Transform Leaf(Transform root, Vector3 hinge, Vector3 across, float w, float h, bool left)
        {
            var pivot = new GameObject(left ? "LeafL" : "LeafR").transform;
            pivot.SetParent(root, true);
            pivot.position = hinge;
            pivot.rotation = Quaternion.LookRotation(across);
            float leafW = w - 0.04f;
            MeshKit.Part(pivot, "Planks", MeshKit.Box(new Vector3(0.07f, h - 0.1f, leafW)), Mat.Wood, new Vector3(0f, (h - 0.1f) / 2f, leafW / 2f));
            foreach (float y in new[] { 0.45f, h * 0.5f, h - 0.6f })
                MeshKit.Part(pivot, "Strap", MeshKit.Box(new Vector3(0.09f, 0.08f, leafW * 0.9f)), Mat.Steel, new Vector3(0f, y, leafW * 0.47f));
            MeshKit.Part(pivot, "Ring", MeshKit.Cylinder(0.07f, 0.07f, 0.02f, 10), Mat.Steel, new Vector3(0.06f, h * 0.48f, leafW - 0.22f), new Vector3(0f, 0f, 90f), new Vector3(1f, 1f, 0.35f));
            MeshKit.MergeParts(pivot);
            return pivot;
        }

        private bool AnyoneNear()
        {
            var at = transform.position;
            foreach (var h in Health.All)
            {
                if (h == null || !h.IsAlive || (h.Team != Team.Players && h.Team != Team.Enemies)) continue;
                var d = h.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude < Reach * Reach) return true;
            }
            return false;
        }

        private void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.15f;
                if (AnyoneNear()) lastNear = Time.time;
                want = Time.time - lastNear < 3f ? 1f : 0f;
            }
            // Thrown open quickly (someone is coming through); drifting shut slowly.
            open = Mathf.MoveTowards(open, want, Time.deltaTime * (want > open ? 3.2f : 0.9f));
            bool isOpen = open > 0.5f;
            if (isOpen != wasOpen)
            {
                wasOpen = isOpen;
                var cam = ViewCamera.Current;
                if (cam != null && (cam.transform.position - transform.position).sqrMagnitude < 20f * 20f)
                    Sfx.Play(isOpen ? Sound.Creak : Sound.Step, transform.position + Vector3.up * 1.5f, isOpen ? 0.35f : 0.5f, isOpen ? Random.Range(0.85f, 1.1f) : 0.55f);
            }
            float k = open * open * (3f - 2f * open);
            // Each leaf swings from across the doorway round to face into the room (a little past square).
            left.rotation = Quaternion.LookRotation(Vector3.Slerp(closedL, Quaternion.AngleAxis(Vector3.SignedAngle(closedL, inward, Vector3.up) * 1.05f, Vector3.up) * closedL, k));
            right.rotation = Quaternion.LookRotation(Vector3.Slerp(closedR, Quaternion.AngleAxis(Vector3.SignedAngle(closedR, inward, Vector3.up) * 1.05f, Vector3.up) * closedR, k));
        }
    }
}
