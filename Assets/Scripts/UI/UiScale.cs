using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Everything on screen is laid out in a virtual space and scaled to the window (2026-11-01, the user: "scale minimap
    /// size with resolution, as well as all other things"; it used to be raw pixels, so 1440p and 4K got a tiny HUD). 1080
    /// lines is 1:1. <see cref="UiSkin.Apply"/> sets the matrix at the top of every OnGUI, so all drawing and IMGUI mouse
    /// positions go through it: layout code reads <see cref="W"/> and <see cref="H"/> rather than Screen.width and height,
    /// and world-to-screen labels use <see cref="World"/>. Real pixels stay only where the OS or renderer needs them
    /// (the retro pass, the pad's cursor warp).
    /// </summary>
    public static class UiScale
    {
        public const float ReferenceHeight = 1080f;
        /// <summary>Below this the type stops being readable; a small window keeps at least this much.</summary>
        public const float MinAuto = 0.75f;
        /// <summary>The menus are built for this many virtual lines: no setting may push them off the screen.</summary>
        public const float MinVirtualHeight = 720f;

        /// <summary>Real pixels per virtual pixel.</summary>
        public static float For(float screenHeight, float userScale)
        {
            float auto = Mathf.Max(MinAuto, screenHeight / ReferenceHeight);
            return Mathf.Max(0.1f, Mathf.Min(auto * userScale, screenHeight / MinVirtualHeight));
        }

        public static float Factor => For(Screen.height, Settings.UiScale);
        /// <summary>The window in virtual pixels.</summary>
        public static float W => Screen.width / Factor;
        public static float H => Screen.height / Factor;

        /// <summary>Called by <see cref="UiSkin.Apply"/>.</summary>
        public static void Begin()
        {
            float f = Factor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(f, f, 1f));
        }

        /// <summary>A world point in virtual pixels (z is still the distance; y counts from the bottom, like WorldToScreenPoint's).</summary>
        public static Vector3 World(Camera cam, Vector3 world)
        {
            var p = cam.WorldToScreenPoint(world);
            float f = Factor;
            return new Vector3(p.x / f, p.y / f, p.z);
        }

        /// <summary>
        /// GUIUtility.RotateAroundPivot takes its pivot in unscaled space, so under a scale it swings around the wrong
        /// point: this one composes under the current matrix instead. Save and restore GUI.matrix around it as usual.
        /// </summary>
        public static void Rotate(float degrees, Vector2 pivot)
        {
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Vector2 at = GUIUtility.GUIToScreenPoint(pivot);
            GUI.matrix = keep * Matrix4x4.Translate(at) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-at);
        }
    }
}
