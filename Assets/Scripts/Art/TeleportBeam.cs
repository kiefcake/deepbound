using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The descent: a column of light comes down on a player, motes stream up it, and they are
    /// taken; on arrival the same column sets them down and fades. Local only, follows the player.
    /// </summary>
    public class TeleportBeam : MonoBehaviour
    {
        private float born, life;
        private bool arriving;
        private Transform column, core;
        private Light glow;
        private float nextMote;
        private static readonly Color Holy = new Color(1f, 0.9f, 0.65f);

        /// <summary>A beam on <paramref name="who"/>: departing (it grows) or arriving (it fades).</summary>
        public static void On(Transform who, bool arrive, float seconds)
        {
            if (who == null) return;
            var go = new GameObject(arrive ? "ArrivalBeam" : "DepartureBeam");
            go.transform.SetParent(who, false);
            var b = go.AddComponent<TeleportBeam>();
            b.born = Time.time;
            b.life = seconds;
            b.arriving = arrive;
            b.Build();
            Destroy(go, seconds + 0.2f);
        }

        private void Build()
        {
            column = MeshKit.Part(transform, "Column", MeshKit.Cylinder(1f, 1f, 1f, 24), Mat.Telegraph, Vector3.zero).transform;
            ArtLibrary.Tint(column.GetComponent<Renderer>(), new Color(1f, 0.92f, 0.7f, 0.25f));
            core = MeshKit.Part(transform, "Core", MeshKit.Cylinder(1f, 1f, 1f, 16), Mat.Glow, Vector3.zero).transform;
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), Holy * 1.2f);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = new GameObject("BeamLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 2f;
            glow.color = Holy;
            glow.range = 8f;
            glow.shadows = LightShadows.None;
            Update();
        }

        private void Update()
        {
            float k = Mathf.Clamp01((Time.time - born) / life);
            // Departing: the column narrows onto you and brightens. Arriving: it starts bright and lifts away.
            float strength = arriving ? 1f - k : Mathf.SmoothStep(0.2f, 1f, k);
            float radius = arriving ? Mathf.Lerp(0.7f, 1.4f, k) : Mathf.Lerp(1.6f, 0.75f, k);
            float height = 40f;
            column.localScale = new Vector3(radius, height, radius);
            core.localScale = new Vector3(radius * 0.25f, height, radius * 0.25f);
            ArtLibrary.Tint(column.GetComponent<Renderer>(), new Color(1f, 0.92f, 0.7f, 0.28f * strength));
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), Holy * (1.6f * strength));
            glow.intensity = 4f * strength;
            if (Time.time >= nextMote)
            {
                nextMote = Time.time + 0.05f;
                var p = transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(0f, radius * 0.9f);
                Vfx.Embers(p + Vector3.up * Random.Range(0f, 1.2f), 1, Holy, arriving ? 1.5f : 3.5f, 0.05f, 0.9f);
            }
        }
    }
}
