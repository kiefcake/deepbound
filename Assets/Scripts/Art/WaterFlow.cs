using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Moves a water surface: scrolls its ripple normal and the detail ripple in different
    /// directions (through the renderer's property block, keeping its tint). A fall scrolls fast
    /// and downward instead. Local only.
    /// </summary>
    public class WaterFlow : MonoBehaviour
    {
        public bool Falling;
        /// <summary>0..1: the Drowned Vaults' water thickening (ripples slow to a crawl).</summary>
        public static float Sluggish;
        private float clock;
        private static readonly int BaseSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int DetailSt = Shader.PropertyToID("_DetailAlbedoMap_ST");
        private Renderer rend;
        private MaterialPropertyBlock block;
        private float seed;

        private void Start()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            seed = Random.value * 10f;
        }

        private void Update()
        {
            if (rend == null || !rend.isVisible) return;
            clock += Time.deltaTime * (Falling ? 1f : 1f - 0.85f * Sluggish);
            float t = clock + seed;
            rend.GetPropertyBlock(block);
            if (Falling)
            {
                block.SetVector(BaseSt, new Vector4(1f, 1f, 0f, t * 1.1f));
                block.SetVector(DetailSt, new Vector4(1.7f, 1.7f, t * 0.05f, t * 1.6f));
            }
            else
            {
                block.SetVector(BaseSt, new Vector4(1f, 1f, t * 0.018f, t * 0.011f));
                block.SetVector(DetailSt, new Vector4(1.7f, 1.7f, -t * 0.024f, t * 0.017f));
            }
            rend.SetPropertyBlock(block);
        }
    }
}
