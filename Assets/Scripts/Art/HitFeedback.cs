using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public class HitFlash : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<Color> originals = new List<Color>();

        private readonly List<bool> hadBlock = new List<bool>();
        private MaterialPropertyBlock block;
        private float flash;
        private bool cached;

        public void Flash(float strength = 1f)
        {
            if (!cached) Cache();
            flash = Mathf.Max(flash, strength);
            enabled = true;
        }

        public void Invalidate() => cached = false;

        private void Cache()
        {
            cached = true;
            renderers.Clear();
            originals.Clear();
            hadBlock.Clear();
            block ??= new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r.sharedMaterial == null || ArtLibrary.Is(r.sharedMaterial, Mat.Glow)) continue;
                r.GetPropertyBlock(block);
                var c = block.HasColor(BaseColorId) ? block.GetColor(BaseColorId)
                    : r.sharedMaterial.HasProperty(BaseColorId) ? r.sharedMaterial.GetColor(BaseColorId) : Color.white;
                renderers.Add(r);
                originals.Add(c);
                hadBlock.Add(r.HasPropertyBlock());
            }
        }

        private void LateUpdate()
        {
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 9f);
            float k = flash * flash;
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, Color.Lerp(originals[i], new Color(3f, 3f, 3f, 1f), k));
                r.SetPropertyBlock(block);
            }
            if (flash <= 0f)
            {
                for (int i = 0; i < renderers.Count; i++)
                    if (renderers[i] != null && !hadBlock[i]) renderers[i].SetPropertyBlock(null);
                enabled = false;
            }
        }
    }

    public static class BoneBurst
    {
        public static void Spawn(Vector3 at, float scale, bool gilded, string materialOverride = null)
        {
            int count = Mathf.RoundToInt(9 * Mathf.Clamp(scale, 0.8f, 2f));
            var material = materialOverride ?? (gilded ? Mat.Gold : Mat.Bone);
            for (int i = 0; i < count; i++)
            {
                var size = new Vector3(Random.Range(0.04f, 0.08f), Random.Range(0.04f, 0.07f), Random.Range(0.12f, 0.28f)) * scale;
                var go = MeshKit.Part(null, "Shard", MeshKit.Box(size), material, at + Random.insideUnitSphere * 0.3f * scale + Vector3.up * scale,
                    new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), 0f));
                var col = go.AddComponent<BoxCollider>();
                col.size = size;
                var body = go.AddComponent<Rigidbody>();
                body.mass = 0.2f;
                body.linearVelocity = (Random.insideUnitSphere + Vector3.up * 1.2f) * Random.Range(2.5f, 5f);
                body.angularVelocity = Random.insideUnitSphere * 15f;

                go.layer = 2;
                Object.Destroy(go, Random.Range(1.8f, 2.8f));
            }
        }
    }
}
