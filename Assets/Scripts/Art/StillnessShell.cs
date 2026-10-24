using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Grave Stillness on a player: a shell of grey marble closes round them while it lasts, then cracks and falls
    /// away. Client-side only. From inside, the shell's faces point away and are not drawn, so the drinker still sees
    /// out (the HUD greys their view instead).
    /// </summary>
    public class StillnessShell : MonoBehaviour
    {
        private Transform target;
        private float until, born;

        public static void Spawn(Transform target, float seconds)
        {
            if (target == null) return;
            var go = new GameObject("StillnessShell");
            var s = go.AddComponent<StillnessShell>();
            s.target = target;
            s.until = Time.time + seconds;
            s.born = Time.time;
            var shell = MeshKit.Part(go.transform, "Shell", MeshKit.Ball(0.62f, 14, 10), Mat.Marble, Vector3.up * 0.95f, Vector3.zero, new Vector3(1f, 1.65f, 1f));
            ArtLibrary.Tint(shell.GetComponent<Renderer>(), new Color(0.72f, 0.74f, 0.8f));
            for (int i = 0; i < 6; i++)
            {
                // A few raised cracks of pale light so it reads as a spell, not a boulder.
                var crack = MeshKit.Part(go.transform, "Crack" + i, MeshKit.Box(new Vector3(0.03f, 0.5f, 0.02f)), Mat.Glow,
                    Vector3.up * (0.6f + (i % 3) * 0.3f) + Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * 0.62f, new Vector3(0f, i * 60f, (i % 2 == 0 ? 18f : -14f)));
                ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), new Color(0.8f, 0.85f, 1f) * 0.9f);
            }
            Sfx.Play(Sound.Shatter, target.position + Vector3.up, 0.6f, 0.5f);
        }

        private void Update()
        {
            if (target == null) { Destroy(gameObject); return; }
            transform.position = target.position;
            // Closes over a quarter of a second, holds, then cracks apart.
            float k = Mathf.Clamp01((Time.time - born) / 0.25f);
            transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
            if (Time.time >= until)
            {
                var at = target.position + Vector3.up;
                Sfx.Play(Sound.BoneBreak, at, 0.8f, 0.6f);
                for (int i = 0; i < 3; i++) BoneBurst.Spawn(at + Vector3.up * (i - 1) * 0.5f, 0.6f, false, Mat.Marble);
                Destroy(gameObject);
            }
        }
    }
}
