using UnityEngine;

namespace Deepbound.Art
{
    public class LiftHatch : MonoBehaviour
    {
        public const float OpenSeconds = 1.3f, OpenAngle = 100f, Thickness = 0.4f;

        public static LiftHatch Current { get; private set; }

        private Transform left, right;
        private float openedAt = -1f;

        public bool Opening => openedAt >= 0f;

        public static LiftHatch Build(Transform parent, Vector3 underside, float half)
        {
            var go = new GameObject("LiftHatch");
            go.transform.SetParent(parent, false);
            go.transform.position = underside;
            var hatch = go.AddComponent<LiftHatch>();
            hatch.left = hatch.Door(-1f, half);
            hatch.right = hatch.Door(1f, half);
            Current = hatch;
            return hatch;
        }

        private Transform Door(float side, float half)
        {
            var hinge = MeshKit.Pivot(transform, side < 0f ? "HingeL" : "HingeR", new Vector3(side * half, Thickness / 2f, 0f));
            float w = half - 0.02f;
            MeshKit.Part(hinge, "Slab", MeshKit.Box(new Vector3(w, Thickness, half * 2f)), Mat.StoneWall, new Vector3(-side * w / 2f, 0f, 0f));
            foreach (float z in new[] { -half * 0.6f, half * 0.6f })
                MeshKit.Part(hinge, "Strap", MeshKit.Box(new Vector3(w, 0.06f, 0.18f)), Mat.Steel, new Vector3(-side * w / 2f, -Thickness / 2f - 0.02f, z));
            MeshKit.Part(hinge, "Ring", MeshKit.Lathe("hatchRing", new[] { new Vector2(0.16f, -0.02f), new Vector2(0.2f, 0f), new Vector2(0.16f, 0.02f) }, 12, false), Mat.Steel,
                new Vector3(-side * (w - 0.35f), -Thickness / 2f - 0.05f, 0f), new Vector3(90f, 90f, 0f));
            return hinge;
        }

        public void Open()
        {
            if (openedAt >= 0f) return;
            openedAt = Time.time;
            Sfx.Play(Sound.Door, transform.position, 1f, 0.55f);
            Sfx.Play(Sound.Chain, transform.position, 0.8f, 0.7f);
            Vfx.Smoke(transform.position + Vector3.down * 0.4f, 10, 1.6f, 2.6f);
        }

        private void Update()
        {
            if (openedAt < 0f || left == null) return;
            float k = Mathf.Clamp01((Time.time - openedAt) / OpenSeconds);
            float a = OpenAngle * (1f - Mathf.Pow(1f - k, 3f));
            left.localRotation = Quaternion.Euler(0f, 0f, a);
            right.localRotation = Quaternion.Euler(0f, 0f, -a);
            if (k >= 1f) enabled = false;
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }
    }
}
