using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>A thrown flask as you see it: a spinning bottle on an arc, then the splash. Visual only.</summary>
    public class FlaskFlight : MonoBehaviour
    {
        private Vector3 from, to;
        private float born, flight;
        private Flasks.Kind kind;

        public static void Spawn(Flasks.Kind kind, Vector3 from, Vector3 to, float flight)
        {
            var go = new GameObject("Flask");
            go.transform.position = from;
            var f = go.AddComponent<FlaskFlight>();
            f.from = from;
            f.to = to;
            f.flight = flight;
            f.kind = kind;
            f.born = Time.time;
            var c = Flasks.ColorOf(kind);
            MeshKit.Part(go.transform, "Bottle", MeshKit.Lathe("flask", new[]
            {
                new Vector2(0f, 0f), new Vector2(0.09f, 0.02f), new Vector2(0.1f, 0.1f), new Vector2(0.04f, 0.18f), new Vector2(0.035f, 0.26f), new Vector2(0f, 0.27f),
            }, 8), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(go.GetComponentInChildren<Renderer>(), c * 1.3f);
        }

        private void Update()
        {
            float t = (Time.time - born) / flight;
            if (t >= 1f) { Splash(); Destroy(gameObject); return; }
            var p = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 2.2f;
            transform.position = p;
            transform.Rotate(540f * Time.deltaTime, 0f, 300f * Time.deltaTime);
        }

        private void Splash()
        {
            var c = Flasks.ColorOf(kind);
            Sfx.Play(Sound.Shatter, to + Vector3.up * 0.3f, 0.8f, 1.3f);
            switch (kind)
            {
                case Flasks.Kind.Fire:
                    Sfx.Play(Sound.Fire, to + Vector3.up, 1f, 0.8f);
                    Telegraph.Show(to, 3.2f, 4f, null, new Color(1f, 0.35f, 0.08f, 0.35f));
                    for (int i = 0; i < 6; i++) Vfx.Fireball(to + Random.insideUnitSphere * 1.6f + Vector3.up * 0.4f, 0.9f, c);
                    Vfx.Embers(to, 18, c, 4f, 0.06f, 1.2f);
                    break;
                case Flasks.Kind.Frost:
                    Sfx.Play(Sound.Frost, to + Vector3.up, 1f, 1f);
                    Vfx.IceSpikes(to, 3.6f, 16);
                    Vfx.Ring(to, 3.6f, c, 0.45f);
                    break;
                default:
                    Sfx.Play(Sound.Revive, to + Vector3.up, 1f, 1.2f);
                    Vfx.Flash(to + Vector3.up, c, 8f, 9f, 0.5f);
                    Vfx.Ring(to, 4f, c, 0.5f);
                    Vfx.Embers(to + Vector3.up * 0.5f, 16, c, 3f, 0.05f, 1f);
                    break;
            }
        }
    }
}
