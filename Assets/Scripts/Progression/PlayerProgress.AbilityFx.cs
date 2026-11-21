using Deepbound.Art;
using Deepbound.Combat;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// Shared effects for Ysolde's ability options (2026-11-20 rework): one set of RPCs every kit can call through
    /// <see cref="Classes.AbilityKit"/>, so the many small choices do not each need their own.
    /// </summary>
    public partial class PlayerProgress
    {
        public enum FxKind : byte { Fire, Frost, Holy, Poison, Shadow, Earth, Storm, Blood, Water, Steel }

        public static Color FxColor(FxKind k) => k switch
        {
            FxKind.Fire => new Color(1f, 0.5f, 0.15f),
            FxKind.Frost => new Color(0.55f, 0.85f, 1f),
            FxKind.Holy => new Color(1f, 0.85f, 0.45f),
            FxKind.Poison => new Color(0.55f, 1f, 0.35f),
            FxKind.Shadow => new Color(0.6f, 0.4f, 0.95f),
            FxKind.Earth => new Color(0.75f, 0.62f, 0.45f),
            FxKind.Storm => new Color(0.7f, 0.85f, 1f),
            FxKind.Blood => new Color(0.85f, 0.1f, 0.08f),
            FxKind.Water => new Color(0.35f, 0.65f, 1f),
            _ => new Color(0.9f, 0.9f, 0.95f),
        };

        /// <summary>A burst of <paramref name="kind"/> at <paramref name="at"/>, <paramref name="radius"/> wide.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void AbilityBurstRpc(Vector3 at, float radius, FxKind kind)
        {
            var c = FxColor(kind);
            var floor = Vfx.Floor(at);
            if (radius > 0.5f) Vfx.Ring(floor + Vector3.up * 0.05f, radius, c, 0.45f);
            Vfx.Embers(at + Vector3.up * 0.4f, Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 6, 22), c, 3f + radius, 0.06f, 0.7f);
            Vfx.Flash(at + Vector3.up * 0.8f, c, 3f, Mathf.Max(3f, radius * 2f), 0.35f);
            switch (kind)
            {
                case FxKind.Fire: Vfx.Fireball(at + Vector3.up * 0.5f, Mathf.Max(0.8f, radius * 0.5f), c); Sfx.Play(Sound.Fire, at, 0.8f, 0.9f); break;
                case FxKind.Frost: Vfx.IceSpikes(floor, Mathf.Max(1f, radius * 0.7f), 10); Sfx.Play(Sound.Frost, at, 0.8f, 0.9f); break;
                // 2026-11-21 (distinct abilities): each kind in its own shape, the same language as the class kits (Art/ClassFx).
                case FxKind.Earth:
                    Vfx.Smoke(floor, 3, radius * 0.4f, 1.4f); Sfx.Play(Sound.Slam, at, 0.8f, 0.9f);
                    ClassFx.Cracks(floor, radius, 7, c, 3f); ClassFx.Rubble(floor, 8, 3.5f); ClassFx.Shock(floor, radius, c, 0.7f, 0.35f);
                    break;
                case FxKind.Poison:
                    Vfx.Smoke(floor + Vector3.up * 0.4f, 3, radius * 0.4f, 1.8f); Sfx.Play(Sound.Slime, at, 0.6f, 1.1f);
                    var spokes = new System.Collections.Generic.List<Vector3>();
                    for (int i = 0; i < 5; i++) spokes.Add(floor + Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward * radius * 0.8f);
                    ClassFx.Veins(floor, spokes, 2.2f, 0.3f); ClassFx.Flies(null, floor, Mathf.Min(1.2f, radius * 0.5f), 1.1f, 2.5f, 8);
                    break;
                case FxKind.Water: Sfx.Play(Sound.Splash, at, 0.8f, 0.9f); break;
                case FxKind.Storm: Sfx.Play(Sound.Shatter, at, 0.6f, 1.6f); break;
                case FxKind.Blood: Sfx.Play(Sound.Hit, at, 0.8f, 0.7f); ClassFx.FlagCrater(floor, radius, 0.7f); break;
                case FxKind.Shadow:
                    Vfx.Smoke(floor + Vector3.up * 0.3f, 2, radius * 0.3f, 1.2f); Sfx.Play(Sound.Shade, at, 0.5f, 1.2f);
                    ClassFx.CrossCut(at + Vector3.up * 0.8f, Mathf.Clamp(radius * 0.8f, 1f, 2.6f));
                    break;
                case FxKind.Holy:
                    Sfx.Play(Sound.Revive, at, 0.6f, 1.3f);
                    ClassFx.HolyCross(at + Vector3.up * 0.9f, Mathf.Clamp(radius * 0.6f, 1f, 2.2f), new Color(2.6f, 2.2f, 1.4f, 1f), 0.6f, Vector3.up * 0.4f);
                    ClassFx.Shock(floor, radius, c, 0.5f, 0.4f);
                    break;
                case FxKind.Steel:
                    Sfx.Play(Sound.Revive, at, 0.6f, 1.3f);
                    ClassFx.Glint(at + Vector3.up * 0.6f, c, 1.2f, 0.2f, 45f); ClassFx.Shards(at + Vector3.up * 0.6f, Vector3.up, 8, c * 2.2f, 5f);
                    break;
                default: Sfx.Play(Sound.Revive, at, 0.6f, 1.3f); break;
            }
        }

        /// <summary>A warning circle the party can read, then (optionally) the strike lands where it said.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void AbilityCircleRpc(Vector3 at, float radius, float seconds, FxKind kind)
        {
            var c = FxColor(kind);
            Telegraph.Show(at, radius, seconds, null, new Color(c.r, c.g, c.b, 0.24f));
        }

        /// <summary>A lane on the floor from <paramref name="origin"/> along <paramref name="dir"/>.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void AbilityLaneRpc(Vector3 origin, Vector3 dir, float length, float width, float seconds, FxKind kind)
        {
            var c = FxColor(kind);
            Telegraph.ShowLine(origin, dir, length, width, seconds, null, new Color(c.r, c.g, c.b, 0.24f));
        }

        /// <summary>A streak of light from one point to another (a chain, a leap, a thrown shield).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void AbilityLinkRpc(Vector3 from, Vector3 to, FxKind kind)
        {
            var c = FxColor(kind);
            int n = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(from, to) * 0.8f), 2, 12);
            for (int i = 0; i <= n; i++) Vfx.Embers(Vector3.Lerp(from, to, i / (float)n), 1, c, 0.8f, 0.07f, 0.5f);
            // A band of the kind's light along it (2026-11-21), and a glint where it lands.
            ClassFx.Tether(from, to, new Color(c.r * 2.2f, c.g * 2.2f, c.b * 2.2f, 0.85f), 0.16f, 0.4f);
            ClassFx.Glint(to, c, 0.8f, 0.18f, 30f);
            if (kind == FxKind.Storm) LightningBolt.Spawn(from, new[] { to });
        }

        /// <summary>The Wardweaver's shards (2026-11-20): each swallows one blow (up to <see cref="Classes.PathTriads.ShardSoak"/> of max health).</summary>
        public readonly NetworkVariable<byte> WardShards = new NetworkVariable<byte>();
        private float nextShardFx;

        /// <summary>Everyone: the shards circle the delver (a few glints a frame, cheap).</summary>
        private void TickWardShardFx()
        {
            int n = WardShards.Value;
            if (n == 0 || Time.time < nextShardFx) return;
            nextShardFx = Time.time + 0.05f;
            var cam = Art.ViewCamera.Current;
            if (cam != null && (cam.transform.position - transform.position).sqrMagnitude > 900f) return;
            for (int i = 0; i < n; i++)
            {
                float a = Time.time * 2.2f + i * Mathf.PI * 2f / n;
                var at = transform.position + new Vector3(Mathf.Cos(a) * 0.9f, 1.1f + Mathf.Sin(Time.time * 3f + i) * 0.15f, Mathf.Sin(a) * 0.9f);
                Vfx.Speck(at, new Color(0.6f, 0.85f, 1f));
            }
        }

        /// <summary>A word over something (FROZEN, SWITCH, ECHO...).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void AbilityTextRpc(Vector3 at, FixedString32Bytes text, FxKind kind) => UI.GameHud.ShowCombo(at, text.ToString(), FxColor(kind));
    }
}
