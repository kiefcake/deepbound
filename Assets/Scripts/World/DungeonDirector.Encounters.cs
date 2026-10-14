using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// Set-piece encounters, layered on the director's floor loop:
    ///
    /// <list type="bullet">
    /// <item><b>Sealed boss arena.</b> The boss waits, dormant, until every standing member of
    ///   the party is inside its room (or someone hits it). Then the gates slam, its name is
    ///   announced, and nobody leaves until it falls.</item>
    /// <item><b>Ambush room.</b> One room per floor looks empty. Reach its middle and the gates
    ///   drop and the dead claw up out of the floor in two waves — the second led by an
    ///   elite. Break the ambush and a chest rises where it began.</item>
    /// </list>
    /// </summary>
    public partial class DungeonDirector
    {
        [Header("Encounters")]
        [SerializeField] private NetworkObject portcullisPrefab;
        [SerializeField] private NetworkObject leverPrefab;
        [SerializeField] private NetworkObject fountainPrefab;
        [SerializeField] private NetworkObject sarcophagusPrefab;

        private class Vault
        {
            public DungeonLayout.Room Room;
            public readonly List<Portcullis> Gates = new List<Portcullis>();
            public readonly List<Lever> Levers = new List<Lever>();
            public bool Open;
        }
        private Vault vault;
        private readonly Dictionary<PlayerLife, float> rescuedAt = new Dictionary<PlayerLife, float>();

        private readonly List<Portcullis> bossGates = new List<Portcullis>();
        private bool bossSealed;

        private class Ambush
        {
            public DungeonLayout.Room Room;
            public readonly List<Portcullis> Gates = new List<Portcullis>();
            public readonly List<EnemyBrain> Alive = new List<EnemyBrain>();
            public int Wave; // 0 = waiting, 1-2 = fighting, 3 = broken
        }
        private Ambush ambush;

        // Read-only views for the smoke test and debugging.
        public DungeonLayout.Room AmbushRoom => ambush?.Room;
        public int AmbushWave => ambush?.Wave ?? -1;
        public bool BossSealed => bossSealed;

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void ResetEncounters()
        {
            bossGates.Clear();
            bossSealed = false;
            ambush = null;
            vault = null;
            foreach (var l in FindObjectsByType<Lever>(FindObjectsSortMode.None))
                if (l.NetworkObject.IsSpawned) l.NetworkObject.Despawn();
            foreach (var f in FindObjectsByType<Fountain>(FindObjectsSortMode.None))
                if (f.NetworkObject.IsSpawned) f.NetworkObject.Despawn();
            foreach (var s in FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None))
                if (s.NetworkObject.IsSpawned) s.NetworkObject.Despawn();
            foreach (var g in FindObjectsByType<Portcullis>(FindObjectsSortMode.None))
            {
                if (g.NetworkObject.IsSpawned) g.NetworkObject.Despawn();
            }
        }

        /// <summary>Choose the ambush room (before enemies spawn, so it stays empty) and hang gates.</summary>
        private void SetupEncounters(DungeonLayout layout)
        {
            if (portcullisPrefab == null) return;
            foreach (var g in SpawnGates(layout.BossRoom)) bossGates.Add(g);

            // A combat room at least two steps from the start, so the first fight is not an ambush.
            var rng = new System.Random(layout.Seed ^ 0xA3B);
            var candidates = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && r.Id >= 2 &&
                (r.Feature == RoomFeature.None || r.Feature == RoomFeature.GreatHall));
            if (candidates.Count == 0) return;
            ambush = new Ambush { Room = candidates[rng.Next(candidates.Count)] };
            ambush.Gates.AddRange(SpawnGates(ambush.Room));
        }

        private bool IsAmbushRoom(DungeonLayout.Room room) => ambush != null && ambush.Room == room;

        /// <summary>Sarcophagi in crypts, the vault's gate, levers and treasure, and the fountain.</summary>
        private void SetupFeatures(DungeonLayout layout)
        {
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            var theme = DungeonTheme.ForDepth(layout.Depth);
            foreach (var room in layout.Rooms)
            {
                switch (room.Feature)
                {
                    case RoomFeature.Crypt when sarcophagusPrefab != null:
                        foreach (var spot in room.FeatureSpots)
                        {
                            var face = room.Center - spot;
                            face.y = 0f;
                            var obj = Spawn(sarcophagusPrefab, spot, Quaternion.LookRotation(face));
                            bool brute = true, necro = true; // crypts hold the rank and file
                            var occupant = PickEnemy(layout.Depth, theme, ref brute, ref necro, out int _);
                            obj.GetComponent<Sarcophagus>().ServerLoad(occupant, players, layout.Depth);
                        }
                        break;

                    case RoomFeature.Vault when leverPrefab != null && portcullisPrefab != null:
                        vault = new Vault { Room = room };
                        vault.Gates.AddRange(SpawnGates(room));
                        SetGates(vault.Gates, true);
                        var host = room.VaultHost ?? room;
                        foreach (var spot in room.FeatureSpots)
                        {
                            var face = host.Center - spot;
                            face.y = 0f;
                            vault.Levers.Add(Spawn(leverPrefab, spot, Quaternion.LookRotation(face)).GetComponent<Lever>());
                        }
                        if (chestPrefab != null)
                        {
                            foreach (float s in new[] { -1f, 1f })
                            {
                                var chest = Spawn(chestPrefab, room.Center + new Vector3(s * 2f, 0f, 0f), Quaternion.Euler(0f, s > 0 ? -90f : 90f, 0f));
                                chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                            }
                        }
                        break;

                    case RoomFeature.Fountain when fountainPrefab != null && room.FeatureSpots.Count > 0:
                        Spawn(fountainPrefab, room.FeatureSpots[0], Quaternion.identity);
                        break;
                }
            }
        }

        private void TickVault()
        {
            if (vault == null || vault.Open || vault.Levers.Count == 0) return;
            foreach (var l in vault.Levers) if (l == null || !l.Pulled.Value) return;
            vault.Open = true;
            foreach (var l in vault.Levers) l.ServerLock();
            SetGates(vault.Gates, false);
            TitleCardRpc("THE VAULT OPENS", "Treasure lies within");
        }

        /// <summary>
        /// Anyone who falls (into a chasm, or through the world) climbs back out at the
        /// nearest doorway of the room they fell in, a fifth of their health the poorer.
        /// </summary>
        private void TickFalls()
        {
            if (Layout == null) return;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.transform.position.y > -3f) continue;
                if (rescuedAt.TryGetValue(p, out var t) && Time.time - t < 2f) continue;
                rescuedAt[p] = Time.time;

                var room = Layout.RoomAt(p.transform.position, 2f) ?? Layout.StartRoom;
                var safe = room.Center;
                for (int side = 0; side < 4; side++)
                {
                    if (!room.Doors[side]) continue;
                    var d = DungeonLayout.Dir[side];
                    bool alongX = side == 0 || side == 2;
                    safe = room.Center + new Vector3(d.x, 0f, d.y) * ((alongX ? room.Size.y : room.Size.x) / 2f - 1.5f);
                    break;
                }
                p.Health.ServerDamage(p.Health.Max.Value * 0.2f, p.transform.position);
                p.GetComponent<NetworkPlayer>().RescueRpc(safe + Vector3.up * 0.1f);
            }
        }

        private List<Portcullis> SpawnGates(DungeonLayout.Room room)
        {
            var gates = new List<Portcullis>();
            for (int side = 0; side < 4; side++)
            {
                if (!room.Doors[side]) continue;
                var d = DungeonLayout.Dir[side];
                bool alongX = side == 0 || side == 2;
                float half = (alongX ? room.Size.y : room.Size.x) / 2f;
                var at = room.Center + new Vector3(d.x, 0f, d.y) * (half + 0.4f);
                var obj = Instantiate(portcullisPrefab, at, alongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f));
                obj.Spawn(destroyWithScene: true);
                gates.Add(obj.GetComponent<Portcullis>());
            }
            return gates;
        }

        private static void SetGates(List<Portcullis> gates, bool closed)
        {
            foreach (var g in gates) if (g != null) g.Closed.Value = closed;
        }

        // ------------------------------------------------------------------
        // Ticking (server, while exploring)
        // ------------------------------------------------------------------

        private void TickEncounters()
        {
            TickBossArena();
            TickAmbush();
            TickVault();
            TickFalls();
        }

        private void TickBossArena()
        {
            if (boss == null || Layout == null) return;
            if (bossSealed)
            {
                if (boss.IsDead) SetGates(bossGates, false);
                return;
            }

            // Wake when the whole standing party is inside, or when someone starts the fight early.
            bool allIn = AllStandingInside(Layout.BossRoom, 1f);
            if (allIn || !boss.Dormant)
            {
                bossSealed = true;
                boss.Dormant = false;
                SetGates(bossGates, true);
                TitleCardRpc(boss.DisplayName.ToUpperInvariant(), "Guardian of " + DungeonTheme.ForDepth(Layout.Depth).Name);
            }
        }

        private void TickAmbush()
        {
            if (ambush == null || ambush.Wave >= 3) return;
            var room = ambush.Room;

            if (ambush.Wave == 0)
            {
                float trigger = Mathf.Min(room.Size.x, room.Size.y) / 2f - 2.5f;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var d = p.transform.position - room.Center;
                    d.y = 0f;
                    if (d.magnitude > trigger) continue;
                    SetGates(ambush.Gates, true);
                    TitleCardRpc("AMBUSH!", "Survive the dead");
                    SpawnWave(1);
                    return;
                }
                return;
            }

            ambush.Alive.RemoveAll(e => e == null || e.IsDead);
            if (ambush.Alive.Count > 0) return;

            if (ambush.Wave == 1)
            {
                SpawnWave(2);
                return;
            }

            // Broken: open up and reward.
            ambush.Wave = 3;
            SetGates(ambush.Gates, false);
            if (chestPrefab != null)
            {
                var chest = Spawn(chestPrefab, room.Center, Quaternion.Euler(0f, 180f, 0f));
                chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
            }
            TitleCardRpc("AMBUSH BROKEN", "A chest rises from the floor");
        }

        private void SpawnWave(int wave)
        {
            ambush.Wave = wave;
            var room = ambush.Room;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            int count = Mathf.RoundToInt((wave == 1 ? 5 : 3) * Difficulty.SpawnCount(players) * Difficulty.Level.SpawnMultiplier);
            var theme = DungeonTheme.ForDepth(Layout.Depth);
            bool brute = false, necro = true; // no necromancers in ambushes: they would raise more
            var points = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                var p = room.Center + new Vector3(Random.Range(-1f, 1f) * (room.Size.x / 2f - 2f), 0f, Random.Range(-1f, 1f) * (room.Size.y / 2f - 2f));
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
                points[i] = p;

                var prefab = PickEnemy(Layout.Depth, theme, ref brute, ref necro, out int _);
                var obj = Instantiate(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                obj.Spawn(destroyWithScene: true);
                var brain = obj.GetComponent<EnemyBrain>();
                brain.ServerScale(players, Layout.Depth);
                // The second wave is led by an elite.
                if (wave == 2 && i == 0) brain.ServerMakeElite();
                brain.ServerAggroNearest();
                ambush.Alive.Add(brain);
                spawned.Add(obj);
            }
            RiseFxRpc(points);
        }

        private static bool AllStandingInside(DungeonLayout.Room room, float margin)
        {
            int standing = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value) continue;
                standing++;
                if (!room.Contains(p.transform.position, -margin)) return false;
            }
            return standing > 0;
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        [Rpc(SendTo.ClientsAndHost)]
        private void TitleCardRpc(string title, string subtitle)
        {
            UI.GameHud.ShowTitleCard(title, subtitle);
            Art.Sfx.Play2D(Art.Sound.Summon, 0.8f);
        }

        /// <summary>The dead climbing out of the floor: a dark circle and a spray of bone at each spot.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void RiseFxRpc(Vector3[] points)
        {
            foreach (var p in points)
            {
                Telegraph.Show(p, 1.1f, 0.4f, null, new Color(0.15f, 0.05f, 0.2f, 0.6f));
                Art.BoneBurst.Spawn(p - Vector3.up * 0.6f, 0.9f, false, Art.Mat.Rubble);
            }
            if (points.Length > 0) Art.Sfx.Play(Art.Sound.BoneBreak, points[0], 1f, 0.7f);
        }
    }
}
