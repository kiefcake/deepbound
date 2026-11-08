using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// SPILLED LAMP OIL (2026-11-08, dungeon interactables: "oil spills + torches"). A broken oil jar
    /// (<see cref="Breakable.Kind.OilJar"/>) leaves a dark slick on the floor. Any fire lights it: a scorch on anything
    /// standing in it (Mage fire, the Ember Rune, Embertide, meteors...), a powder barrel, a tipped fire bowl
    /// (<see cref="Breakable.Kind.FireBowl"/>), or a burning hand torch carried through it. Lit, it burns for
    /// <see cref="BurnSeconds"/>: everything in it - the dead AND you - burns every half second, and a lit slick lights
    /// any other slick touching it. The rim glows orange while it burns, so the danger reads at a glance.
    /// </summary>
    public class OilSlick : NetworkBehaviour
    {
        public static readonly List<OilSlick> All = new List<OilSlick>();
        public const float Radius = 2.4f, BurnSeconds = 9f, Tick = 0.5f, SpreadDelay = 0.6f;
        /// <summary>Per tick: enemies lose this share of their max health (bosses a fifth of it), at least a flat floor.</summary>
        public const float EnemyShare = 0.05f, PlayerShare = 0.045f;

        public readonly NetworkVariable<bool> Lit = new NetworkVariable<bool>();
        private float litAt = -100f, nextTick, spreadAt = -1f;
        private ulong litBy = ulong.MaxValue;
        private Transform pool, rim;
        private Light glow;
        private float nextFlame, nextTorchCheck;

        public bool Burning => Lit.Value;
        public bool Contains(Vector3 p, float slack = 0f)
        {
            var d = p - transform.position;
            return Mathf.Abs(d.y) < 1.6f && new Vector2(d.x, d.z).magnitude <= Radius + slack;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            BuildVisual();
            Lit.OnValueChanged += (_, on) => { if (on) LitFx(); };
            if (Lit.Value) LitFx();
        }

        public override void OnNetworkDespawn() { All.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }

        // ---- Server ----

        /// <summary>Server: fire touches this slick.</summary>
        public void ServerIgnite(ulong by)
        {
            if (!IsServer || Lit.Value) return;
            Lit.Value = true;
            litAt = Time.time;
            litBy = by;
            nextTick = Time.time + 0.2f;
            spreadAt = Time.time + SpreadDelay;
        }

        /// <summary>Server: light every unlit slick that <paramref name="at"/> stands in (within <paramref name="slack"/>).</summary>
        public static void ServerIgniteAt(Vector3 at, ulong by, float slack = 0f)
        {
            foreach (var s in All.ToArray())
                if (s != null && s.IsSpawned && !s.Lit.Value && s.Contains(at, slack)) s.ServerIgnite(by);
        }

        /// <summary>Server: a burning slick (or other fire) is at <paramref name="at"/>?</summary>
        public static bool BurningAt(Vector3 at)
        {
            foreach (var s in All) if (s != null && s.Lit.Value && s.Contains(at)) return true;
            return false;
        }

        private void Update()
        {
            if (Lit.Value) LocalFire();
            if (IsServer && !Lit.Value && Time.time >= nextTorchCheck)
            {
                nextTorchCheck = Time.time + 0.25f;
                foreach (var c in Carryable.All)
                    if (c is HandTorch && c.Carried && Contains(c.transform.position - Vector3.up * 1.1f, 0.2f)) { ServerIgnite(c.Carrier.Value); break; }
            }
            if (!IsServer || !Lit.Value) return;
            if (Time.time - litAt > BurnSeconds) { EmberOutRpc(); NetworkObject.Despawn(); return; }
            if (spreadAt > 0f && Time.time >= spreadAt)
            {
                spreadAt = -1f;
                foreach (var other in All.ToArray())
                    if (other != null && other != this && !other.Lit.Value && Vector3.Distance(other.transform.position, transform.position) < Radius * 2f + 0.3f)
                        other.ServerIgnite(litBy);
            }
            if (Time.time < nextTick) return;
            nextTick = Time.time + Tick;
            float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
            foreach (var h in Health.All.ToArray())
            {
                if (h == null || !h.IsAlive || !Contains(h.transform.position)) continue;
                if (h.Team == Team.Enemies)
                {
                    bool boss = h.GetComponent<BossBrain>() != null;
                    float dmg = Mathf.Max(8f + 5f * tier, h.Max.Value * EnemyShare * (boss ? 0.2f : 1f));
                    h.Tag("Burning oil").ServerDamage(dmg, transform.position, litBy);
                    h.ServerScorch(1.5f, litBy);
                }
                else if (h.Team == Team.Players)
                {
                    h.Tag("Burning oil").ServerDamage(h.Max.Value * PlayerShare + h.Defense * Health.DefenseWeight, transform.position);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EmberOutRpc()
        {
            FxSprite.Scorch(transform.position + Vector3.up * 0.03f, Radius * 0.9f);
            Vfx.Smoke(transform.position + Vector3.up * 0.3f, 6, Radius * 0.6f, 2.5f);
        }

        // ---- Every peer: the pool, the flames ----

        private void BuildVisual()
        {
            // A wet black pool with a dull sheen, and a rim that only glows while it burns.
            pool = MeshKit.Part(transform, "Oil", MeshKit.Cylinder(Radius, Radius, 0.012f, 20), Mat.Dark, Vector3.up * 0.015f).transform;
            ArtLibrary.Tint(pool.GetComponent<Renderer>(), new Color(0.04f, 0.035f, 0.03f));
            pool.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rng = new System.Random((int)(NetworkObjectId * 7919));
            for (int i = 0; i < 6; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = (float)rng.NextDouble() * Radius * 0.75f;
                var sheen = MeshKit.Part(transform, "Sheen", MeshKit.Cylinder(0.18f + (float)rng.NextDouble() * 0.25f, 0.18f, 0.004f, 8), Mat.Glow,
                    new Vector3(Mathf.Cos(a) * r, 0.024f, Mathf.Sin(a) * r));
                ArtLibrary.SetGlow(sheen.GetComponent<Renderer>(), new Color(0.35f, 0.3f, 0.2f) * 0.12f);
                sheen.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            rim = MeshKit.Part(transform, "Rim", MeshKit.Cylinder(Radius + 0.05f, Radius + 0.05f, 0.01f, 24), Mat.Glow, Vector3.up * 0.01f).transform;
            ArtLibrary.SetGlow(rim.GetComponent<Renderer>(), new Color(1f, 0.45f, 0.1f) * 0.9f);
            rim.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rim.gameObject.SetActive(false);
        }

        private void LitFx()
        {
            if (rim != null) rim.gameObject.SetActive(true);
            if (pool != null) ArtLibrary.Tint(pool.GetComponent<Renderer>(), new Color(0.12f, 0.05f, 0.02f));
            if (glow == null)
            {
                glow = new GameObject("OilFire").AddComponent<Light>();
                glow.transform.SetParent(transform, false);
                glow.transform.localPosition = Vector3.up * 0.9f;
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.55f, 0.2f);
                glow.range = Radius * 3f;
                glow.shadows = LightShadows.None;
            }
            Sfx.Play(Sound.Fire, transform.position + Vector3.up * 0.5f, 1f, 0.8f);
            Vfx.Flash(transform.position + Vector3.up * 0.6f, new Color(1f, 0.55f, 0.2f), 4f, Radius * 3f, 0.4f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 1.4f, "OIL ALIGHT", new Color(1f, 0.6f, 0.25f));
        }

        private void LocalFire()
        {
            if (glow != null) glow.intensity = 2.2f + Mathf.PerlinNoise(Time.time * 6f, NetworkObjectId) * 1.4f;
            if (Application.isBatchMode || Time.time < nextFlame) return;
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > 45f * 45f) return;
            nextFlame = Time.time + 0.07f;
            var off = Random.insideUnitCircle * Radius * 0.9f;
            var at = transform.position + new Vector3(off.x, 0.15f, off.y);
            FxSprite.Billboard(Random.value < 0.5f ? "fire_01" : "flame_03", at, Vector3.up * Random.Range(1.2f, 2.2f), Random.Range(0.5f, 0.8f),
                Random.Range(0.5f, 0.8f), Random.Range(0.9f, 1.4f), new Color(1f, 0.55f, 0.2f, 0.9f), Random.Range(-60f, 60f));
        }
    }
}
