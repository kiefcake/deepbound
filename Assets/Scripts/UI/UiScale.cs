using UnityEngine;

namespace Deepbound.UI
{
    public static class UiScale
    {
        public const float ReferenceHeight = 1080f;

        public const float MinAuto = 0.75f;

        public const float MinVirtualHeight = 720f;

        public static float For(float screenHeight, float userScale)
        {
            float auto = Mathf.Max(MinAuto, screenHeight / ReferenceHeight);
            return Mathf.Max(0.1f, Mathf.Min(auto * userScale, screenHeight / MinVirtualHeight));
        }

        public static float Factor => For(Screen.height, Settings.UiScale);

        public static float W => Screen.width / Factor;
        public static float H => Screen.height / Factor;

        public static void Begin()
        {
            float f = Factor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(f, f, 1f));
        }

        public static Vector3 World(Camera cam, Vector3 world)
        {
            var p = cam.WorldToScreenPoint(world);
            float f = Factor;
            return new Vector3(p.x / f, p.y / f, p.z);
        }

        public static void Rotate(float degrees, Vector2 pivot)
        {
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Vector2 at = GUIUtility.GUIToScreenPoint(pivot);
            GUI.matrix = keep * Matrix4x4.Translate(at) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-at);
        }
    }
}
