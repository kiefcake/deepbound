using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Feeds the retro pass (<c>Shaders/Retro.shader</c>, a full-screen renderer feature) its globals every frame: on or off,
    /// the pixel grid (the small picture's size) and the colour steps per channel. When the window changes size the render
    /// scale is worked out again, so a pixel stays the same size on any screen. Booted on load, survives scene loads.
    /// </summary>
    public class RetroLook : MonoBehaviour
    {
        private static readonly int OnId = Shader.PropertyToID("_RetroOn"), ResId = Shader.PropertyToID("_RetroRes"), LevelsId = Shader.PropertyToID("_RetroLevels"), TintId = Shader.PropertyToID("_RetroTint"), BrightId = Shader.PropertyToID("_Bright");
        /// <summary>How far greys and browns are pulled toward the gothic ramp (saturated colours much less).</summary>
        public const float Tint = 0.18f;
        /// <summary>Colour steps per channel for Fine / Classic / Chunky: the smaller the picture, the fewer the colours.</summary>
        public static readonly float[] Levels = { 26f, 20f, 15f };

        private int lastW, lastH;
        private bool? texturesPoint;
        private readonly System.Collections.Generic.Dictionary<Texture, FilterMode> filters = new System.Collections.Generic.Dictionary<Texture, FilterMode>();

        /// <summary>
        /// Retro texels: every texture on the baked world materials goes to point filtering (crisp brick and flagstone
        /// texels, mips kept against shimmer); back to what they were when the look is turned off.
        /// </summary>
        private void SetTexturesPoint(bool point)
        {
            if (texturesPoint == point) return;
            texturesPoint = point;
            foreach (var mat in Resources.LoadAll<Material>("Materials"))
            {
                if (mat == null || mat.name == "Retro") continue;
                foreach (int id in mat.GetTexturePropertyNameIDs())
                {
                    var tex = mat.GetTexture(id);
                    if (tex == null) continue;
                    if (!filters.ContainsKey(tex)) filters[tex] = tex.filterMode;
                    tex.filterMode = point ? FilterMode.Point : filters[tex];
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("RetroLook");
            DontDestroyOnLoad(go);
            go.AddComponent<RetroLook>();
        }

        private void LateUpdate()
        {
            bool on = Settings.Retro;
            if (Screen.width != lastW || Screen.height != lastH)
            {
                lastW = Screen.width;
                lastH = Screen.height;
                // The preview screens hold the scale at 1 while they are open; they re-apply on closing.
                if (on && !CharacterSelect.Open && !Wardrobe.Open) Settings.ApplyGraphics();
            }
            Shader.SetGlobalFloat(OnId, on ? 1f : 0f);
            Shader.SetGlobalFloat(TintId, Tint);
            SetTexturesPoint(on);
            float s = Settings.RetroScale;
            Shader.SetGlobalVector(ResId, new Vector4(Mathf.Round(Screen.width * s), Mathf.Round(Screen.height * s), 0f, 0f));
            Shader.SetGlobalFloat(LevelsId, Levels[Settings.RetroSize]);
            // Brightness (2026-11-07, the user: "brightness option does nothing": the LiftGammaGain volume never showed): a gamma
            // exponent in the retro pass, which runs whether the look is on or off. +0.4 -> x0.62 (brighter), -0.3 -> x1.43.
            Shader.SetGlobalFloat(BrightId, Mathf.Exp(-Settings.Brightness * 1.2f));
        }
    }
}
