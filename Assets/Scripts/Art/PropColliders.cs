using UnityEngine;

namespace Deepbound.Art
{
    public static class PropColliders
    {
        private static readonly string[] SkipMaterials = { "Glow", "Flame", "Telegraph", "Water", "Smoke" };
        private static readonly string[] SkipNames = { "Shaft", "Mote", "Water", "Rug", "Puddle", "Cobweb", "Chain", "Banner", "Beam", "Ring", "Ember", "Smoke", "Pennant", "Rune", "Torch", "Flame", "Candle", "Halo", "Glow", "Light", "Dust", "Bedroll", "Roof" };

        public static int Add(Transform root)
        {
            if (root == null) return 0;
            Physics.SyncTransforms();
            int added = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<Collider>() != null) continue;

                if (r.GetComponentInParent<World.SwingDoor>() != null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mat = r.sharedMaterial != null ? r.sharedMaterial.name : "";
                bool skip = false;
                foreach (var m in SkipMaterials) if (mat.Contains(m)) { skip = true; break; }
                if (!skip) foreach (var n in SkipNames) if (r.name.Contains(n)) { skip = true; break; }
                if (skip) continue;

                var world = r.bounds;
                float height = world.size.y, foot = Mathf.Max(world.size.x, world.size.z), thin = Mathf.Min(world.size.x, world.size.z);
                if (height < 0.3f) continue;
                if (foot < 0.25f && height < 1.5f) continue;
                if (thin < 0.08f && height < 1.5f) continue;

                if (Physics.Raycast(world.center, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore) && world.min.y - hit.point.y > 1.3f) continue;

                var box = r.gameObject.AddComponent<BoxCollider>();
                var b = mf.sharedMesh.bounds;
                box.center = b.center;
                box.size = b.size;
                added++;
            }
            return added;
        }
    }
}
