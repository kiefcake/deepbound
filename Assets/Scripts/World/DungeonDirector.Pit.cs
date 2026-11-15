using System.Collections.Generic;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE CINDER PIT (2026-11-14, the user: "try to recreate a new floor copying the abyss" - the classic lava-and-demons
    /// dungeon reached through a rare portal, made our own). On floors 5-8 an EMBER RIFT (<see cref="EmberRift"/>) may burn in a
    /// quiet room ( <see cref="RiftChance"/>): step in and the party abandons the floor and drops into the Pit at the same depth -
    /// red-black stone, pools of lava that burn the living (never the dead), charred dead with ember eyes, and VHAROTH, the
    /// Ember Archon, at the bottom (<see cref="VharothBrain"/>). Clear it and the road goes on as if the floor were done.
    /// A Pit floor is marked by a NEGATIVE seed (normal and daily seeds are always positive), so every peer knows from
    /// <see cref="Floor"/> alone - no extra sync, no ordering race.
    /// </summary>
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject pitBossPrefab;
        [SerializeField] private NetworkObject riftPrefab;
        public const float RiftChance = 0.22f;
        public const int RiftMinDepth = 4, RiftMaxDepth = 7;
        /// <summary>Tests: a rift on every floor in range.</summary>
        public static bool ForceRift;

        /// <summary>Any peer: this floor is the Cinder Pit.</summary>
        public static bool InPit => Instance != null && !InLobby && Instance.Floor.Value.x < 0;

        // ---- Lava: pools placed from the seed on every peer, burning players who stand in them ----

        public struct LavaPool { public Vector3 At; public float Radius; }
        public static readonly List<LavaPool> Lava = new List<LavaPool>();
        public const float LavaDps = 0.09f;

        /// <summary>Any peer: the Pit's pools for <paramref name="layout"/> (deterministic), and their looks under <paramref name="parent"/>.</summary>
        public static void BuildLava(DungeonLayout layout, Transform parent)
        {
            Lava.Clear();
            if (layout == null || layout.Seed >= 0) return;
            var rng = new System.Random(layout.Seed ^ 0x7A1A);
            foreach (var r in layout.Rooms)
            {
                if (r.Kind == RoomKind.Start || r.Kind == RoomKind.Boss) continue;
                int n = r.Kind == RoomKind.Combat ? 1 + rng.Next(2) : rng.Next(2);
                for (int i = 0; i < n; i++)
                {
                    float rad = 1.4f + (float)rng.NextDouble() * 1.3f;
                    float hx = r.Size.x / 2f - rad - 2.2f, hz = r.Size.y / 2f - rad - 2.2f;
                    if (hx < 1f || hz < 1f) continue;
                    var at = r.Center + new Vector3(((float)rng.NextDouble() * 2f - 1f) * hx, 0.03f, ((float)rng.NextDouble() * 2f - 1f) * hz);
                    // Keep the middle walkable, and the pools apart.
                    if (new Vector2(at.x - r.Center.x, at.z - r.Center.z).magnitude < 3f) continue;
                    bool clash = false;
                    foreach (var p in Lava) if (Vector3.Distance(p.At, at) < p.Radius + rad + 1.5f) clash = true;
                    if (clash) continue;
                    Lava.Add(new LavaPool { At = at, Radius = rad });
                }
            }
            if (parent == null || Application.isBatchMode) return;
            foreach (var p in Lava)
            {
                var pool = new GameObject("LavaPool").transform;
                pool.SetParent(parent, false);
                pool.position = p.At;
                var crust = Art.MeshKit.Part(pool, "Crust", Art.MeshKit.Cylinder(p.Radius + 0.35f, p.Radius + 0.25f, 0.06f, 20), Art.Mat.Rubble, Vector3.zero);
                Art.ArtLibrary.Tint(crust.GetComponent<Renderer>(), new Color(0.12f, 0.07f, 0.05f));
                var melt = Art.MeshKit.Part(pool, "Melt", Art.MeshKit.Cylinder(p.Radius, p.Radius, 0.08f, 20), Art.Mat.Glow, Vector3.up * 0.01f);
                Art.ArtLibrary.SetGlow(melt.GetComponent<Renderer>(), new Color(1f, 0.38f, 0.08f) * 1.8f);
                melt.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                for (int k = 0; k < 4; k++)
                {
                    var a = k * 1.7f;
                    var blob = Art.MeshKit.Part(pool, "Skin", Art.MeshKit.Cylinder(p.Radius * 0.3f, p.Radius * 0.28f, 0.09f, 8), Art.Mat.Rubble, new Vector3(Mathf.Cos(a) * p.Radius * 0.5f, 0.02f, Mathf.Sin(a) * p.Radius * 0.45f));
                    Art.ArtLibrary.Tint(blob.GetComponent<Renderer>(), new Color(0.2f, 0.08f, 0.04f));
                }
                var light = new GameObject("LavaLight").AddComponent<Light>();
                light.transform.SetParent(pool, false);
                light.transform.localPosition = Vector3.up * 0.8f;
                light.type = LightType.Point; light.color = new Color(1f, 0.42f, 0.12f); light.range = p.Radius * 3.2f + 2f; light.intensity = 2.4f; light.shadows = LightShadows.None;
                pool.gameObject.AddComponent<Art.LavaBubbles>().Radius = p.Radius;
            }
        }

        public static bool InLava(Vector3 at)
        {
            foreach (var p in Lava)
            {
                var d = at - p.At; d.y = 0f;
                if (d.magnitude < p.Radius && at.y < p.At.y + 0.6f) return true;
            }
            return false;
        }

        private float nextLavaTick;

        /// <summary>Server: the lava burns whoever stands in it.</summary>
        private void TickLava()
        {
            if (Lava.Count == 0 || !InPit || Time.time < nextLavaTick) return;
            nextLavaTick = Time.time + 0.5f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable || !InLava(p.transform.position)) continue;
                p.Health.ServerDamage(p.Health.Max.Value * LavaDps * 0.5f, p.transform.position + Vector3.down, ulong.MaxValue);
                LavaBurnRpc(p.transform.position);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LavaBurnRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Fire, at, 0.5f, 1.3f);
            Art.Vfx.Embers(at + Vector3.up * 0.2f, 5, new Color(1f, 0.45f, 0.12f), 2.5f, 0.05f, 0.5f);
        }

        // ---- The rift ----

        /// <summary>Server, with the floor's objects: maybe an ember rift in a quiet room.</summary>
        private void MaybeSpawnRift(DungeonLayout layout)
        {
            if (riftPrefab == null || InPit || layout.Depth < RiftMinDepth || layout.Depth > RiftMaxDepth) return;
            if (!ForceRift && (TestMode || Practice.Value || DailyRun.Value || Random.value > RiftChance)) return;
            var quiet = layout.Rooms.FindAll(r => QuietForRemains(layout, r, RoomKind.Side) && !IsAmbushRoom(r));
            if (quiet.Count == 0) quiet = layout.Rooms.FindAll(r => QuietForRemains(layout, r, RoomKind.Combat) && !IsAmbushRoom(r));
            if (quiet.Count == 0) return;
            var room = quiet[Random.Range(0, quiet.Count)];
            var at = UnityEngine.AI.NavMesh.SamplePosition(room.Center, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : room.Center;
            Spawn(riftPrefab, at, Quaternion.identity);
            Debug.Log("[Director] an ember rift burns in room " + room.Id);
        }

        /// <summary>Server: someone stepped into the rift - the party drops into the Pit, this floor abandoned.</summary>
        public void ServerEnterPit(string who)
        {
            if (Phase.Value != RunPhase.Exploring || InPit || BossSealed) return;
            PitEntryRpc(who);
            StartCoroutine(AfterPitEntry(Floor.Value.y));
        }

        private System.Collections.IEnumerator AfterPitEntry(int depth)
        {
            yield return new WaitForSeconds(1.8f);
            StartFloor(depth, pit: true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PitEntryRpc(string who)
        {
            UI.GameHud.BeginFadeOut(1.7f);
            Art.Sfx.Play2D(Art.Sound.Fire, 0.9f);
            UI.GameHud.Feed(who + " steps into the ember rift. The floor gives way to fire.", new Color(1f, 0.55f, 0.25f));
        }

        /// <summary>Tests: into the Pit now, at this depth.</summary>
        public void ServerDebugPit() => StartFloor(Mathf.Max(RiftMinDepth, Floor.Value.y), pit: true);
    }
}
