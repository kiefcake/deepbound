using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// One particle, as a tiny GameObject: drifts, grows or shrinks, and — if it glows —
    /// fades its emission out over its life. Cheap enough for the dozens an effect needs, and
    /// it keeps every effect in the same baked materials as the rest of the game.
    /// </summary>
    public class Puff : MonoBehaviour
    {
        public Vector3 Velocity;
        public float Gravity, Drag = 1f, Life = 1f, StartScale = 1f, EndScale = 0f;
        public Color Glow;
        public bool Glowing;
        /// <summary>A see-through puff (Mat.Smoke): its colour's alpha thins out over its life.</summary>
        public Color Fade;
        public bool Fading;
        private Renderer rend;
        private float age;

        private void Start() => rend = GetComponent<Renderer>();

        /// <summary>Reused from the pool: start a fresh life.</summary>
        public void Restart()
        {
            age = 0f;
            rend = GetComponent<Renderer>();
        }

        private void Update()
        {
            age += Time.deltaTime;
            float k = Mathf.Clamp01(age / Life);
            Velocity += Vector3.down * Gravity * Time.deltaTime;
            Velocity *= Mathf.Exp(-Drag * Time.deltaTime);
            transform.position += Velocity * Time.deltaTime;
            transform.localScale = Vector3.one * Mathf.Lerp(StartScale, EndScale, k);
            if (Glowing && rend != null) ArtLibrary.SetGlow(rend, Glow * (1f - k) * (1f - k));
            if (Fading && rend != null) ArtLibrary.Tint(rend, new Color(Fade.r, Fade.g, Fade.b, Fade.a * Mathf.Sin(Mathf.Min(1f, k * 3f) * Mathf.PI * 0.5f) * (1f - k)));
            if (age >= Life) Vfx.Recycle(this);
        }
    }
}
