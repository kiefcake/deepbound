using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// EMBERBOUND (2026-11-22, elite affix 12): the elite trails fire. Every <see cref="EmberStep"/> m it walks it leaves a
    /// burning patch (<see cref="EmberRadius"/> m, <see cref="EmberSeconds"/> s) that hurts players standing in it every
    /// half second. Readable at a glance - glowing coals and rising embers on the floor, a warm light - so the counterplay
    /// is positional: kite it in a loop away from where you fight, or chill it (frost slows the trail). Embers rise off
    /// the elite itself (EnemyStatusFx).
    /// </summary>
    public partial class EnemyBrain
    {
        public const float EmberStep = 1.6f, EmberRadius = 1.15f, EmberSeconds = 4.5f, EmberShare = 0.22f;
        private struct EmberPatch { public Vector3 At; public float Until; }
        private readonly List<EmberPatch> emberPatches = new List<EmberPatch>();
        private Vector3 lastEmberAt = new Vector3(float.MaxValue, 0f, 0f);
        private float nextEmberTick;

        /// <summary>Server, for tests.</summary>
        public int EmberPatchCount => emberPatches.Count;

        /// <summary>Server, every frame from <see cref="Update"/>.</summary>
        private void ServerTickEmberbound()
        {
            if (AffixKind == EliteAffix.Emberbound && !IsDead)
            {
                var at = transform.position;
                var d = at - lastEmberAt; d.y = 0f;
                if (lastEmberAt.x == float.MaxValue) lastEmberAt = at;
                else if (d.magnitude >= EmberStep * (health.Chilled.Value ? 2f : 1f))
                {
                    lastEmberAt = at;
                    ServerDropEmber(at);
                }
            }
            if (emberPatches.Count == 0 || Time.time < nextEmberTick) return;
            nextEmberTick = Time.time + 0.5f;
            float hit = damage * EmberShare;
            for (int i = emberPatches.Count - 1; i >= 0; i--)
            {
                if (Time.time > emberPatches[i].Until) { emberPatches.RemoveAt(i); continue; }
                foreach (var v in CombatQuery.Circle(emberPatches[i].At, EmberRadius, Team.Players))
                    if (v != null && v.IsAlive) v.Tag("Emberbound").ServerDamage(hit, emberPatches[i].At + Vector3.up * 0.2f, NetworkObjectId);
            }
        }

        /// <summary>Server: leave a burning patch (public for tests).</summary>
        public void ServerDropEmber(Vector3 at)
        {
            emberPatches.Add(new EmberPatch { At = at, Until = Time.time + EmberSeconds });
            EmberPatchRpc(at, EmberSeconds);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EmberPatchRpc(Vector3 at, float seconds)
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("EmberPatch");
            go.transform.position = Vfx.Floor(at) + Vector3.up * 0.05f;
            go.AddComponent<EmberPatchFx>().Init(EmberRadius, seconds);
        }
    }

    /// <summary>The look of an Emberbound patch: a ring that warns as it lands, coals glowing in a disc, embers rising, a low warm light.</summary>
    public class EmberPatchFx : MonoBehaviour
    {
        private float radius, life, born, nextEmber;
        private Light glow;
        private static readonly Color Fire = new Color(1f, 0.45f, 0.12f);

        public void Init(float r, float seconds)
        {
            radius = r; life = seconds; born = Time.time;
            Vfx.Ring(transform.position, r, Fire, 0.35f);
            var rng = new System.Random(GetInstanceID());
            for (int i = 0; i < 7; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = (float)rng.NextDouble() * r * 0.8f;
                var coal = MeshKit.Part(transform, "Coal", MeshKit.Ball(0.07f + (float)rng.NextDouble() * 0.05f, 6, 4), Mat.Glow, new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), default, new Vector3(1f, 0.45f, 1f));
                ArtLibrary.SetGlow(coal.GetComponent<Renderer>(), Fire * (0.9f + (float)rng.NextDouble() * 0.6f));
                coal.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            glow = new GameObject("EmberLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 0.5f;
            glow.type = LightType.Point; glow.color = Fire; glow.range = r * 2.6f; glow.intensity = 1.1f; glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            float k = (Time.time - born) / Mathf.Max(0.01f, life);
            if (k >= 1f) { Destroy(gameObject); return; }
            float fade = Mathf.Clamp01((1f - k) * 4f);
            if (glow != null) glow.intensity = (1f + 0.25f * Mathf.Sin(Time.time * 11f)) * fade;
            transform.localScale = Vector3.one * (0.6f + 0.4f * Mathf.Clamp01(k * 8f)) * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((k - 0.75f) * 4f));
            if (Time.time >= nextEmber)
            {
                nextEmber = Time.time + 0.18f;
                var p = transform.position + new Vector3(Random.Range(-radius, radius) * 0.7f, 0.1f, Random.Range(-radius, radius) * 0.7f);
                Vfx.Embers(p, 2, Fire, 1.6f, 0.05f, 0.8f);
            }
        }
    }
}
