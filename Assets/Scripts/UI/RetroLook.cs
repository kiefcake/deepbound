using UnityEngine;

namespace Deepbound.UI
{
    public class RetroLook : MonoBehaviour
    {
        private static readonly int OnId = Shader.PropertyToID("_RetroOn"), ResId = Shader.PropertyToID("_RetroRes"), LevelsId = Shader.PropertyToID("_RetroLevels"), TintId = Shader.PropertyToID("_RetroTint"), BrightId = Shader.PropertyToID("_Bright");

        public const float Tint = 0.18f;

        public static readonly float[] Levels = { 26f, 20f, 15f };

        private int lastW, lastH;
        private bool? texturesPoint;
        private readonly System.Collections.Generic.Dictionary<Texture, FilterMode> filters = new System.Collections.Generic.Dictionary<Texture, FilterMode>();

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

                if (on && !CharacterSelect.Open && !Wardrobe.Open) Settings.ApplyGraphics();
            }
            Shader.SetGlobalFloat(OnId, on ? 1f : 0f);
            Shader.SetGlobalFloat(TintId, Tint);
            SetTexturesPoint(on);
            float s = Settings.RetroScale;
            Shader.SetGlobalVector(ResId, new Vector4(Mathf.Round(Screen.width * s), Mathf.Round(Screen.height * s), 0f, 0f));
            Shader.SetGlobalFloat(LevelsId, Levels[Settings.RetroSize]);

            Shader.SetGlobalFloat(BrightId, Mathf.Exp(-Settings.Brightness * 1.2f));
        }
    }
}
