using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A worn weapon glow on a weapon with no blade edge (2026-11-22): small sparks in its colour drift up off the weapon
    /// hand now and then, instead of the solid glowing ball it used to be. Seen on other players (never in your own view).
    /// </summary>
    public class WeaponMotes : MonoBehaviour
    {
        public Color Color = Color.white;
        private float next;

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + Random.Range(0.12f, 0.22f);
            var at = transform.TransformPoint(new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(-0.85f, -0.45f), Random.Range(0.1f, 0.25f)));
            var vel = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.25f, 0.5f), Random.Range(-0.1f, 0.1f));
            FxSprite.Billboard("spark_05", at, vel, Random.Range(0.5f, 0.8f), 0.05f, 0.015f, new Color(Color.r, Color.g, Color.b, 0.75f), Random.Range(-90f, 90f));
        }
    }
}
