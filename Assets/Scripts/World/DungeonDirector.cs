using System;
using System.Collections.Generic;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.AI.Navigation;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>APPEND ONLY: synced as a byte.</summary>
    public enum RunPhase : byte
    {
        Exploring = 0,
        FloorCleared = 1,  // boss dead, descending soon
        Wiped = 2,         // everyone downed, run over
        RunComplete = 3,   // final boss dead
        Lobby = 4,         // at the Last Lantern, between runs
    }

    /// <summary>
    /// Runs the session: the lobby, which floor you are on, when it is cleared, when the
    /// party has wiped. One per session, spawned by the server when hosting starts.
    ///
    /// <para>
    /// Where the party is gets synced as one (nonce, depth) pair. Depth -1 is the lobby;
    /// 0 and up are floors, and for floors the nonce is the generation seed. Each peer
    /// rebuilds the geometry locally from it (see <see cref="DungeonLayout"/> and
    /// <see cref="Lobby"/>); only the server bakes a navmesh and spawns enemies, which
    /// reach clients as ordinary networked objects.
    /// </para>
    ///
    /// The loop (design doc section 6): lobby → board the carriage → three floors, each
    /// ended by a boss → back to the lobby. A wipe also returns to the lobby. Nothing
    /// earned inside a run survives it.
    /// </summary>
    public class DungeonDirector : NetworkBehaviour
    {
        public const int FloorsPerRun = 3;
        private const float DescendDelay = 8f, WipeDelay = 6f, CompleteDelay = 12f, BoardingDelay = 3f;
        private const int LobbyDepth = -1;

        [SerializeField] private NetworkObject skeletonPrefab;
        [SerializeField] private NetworkObject archerPrefab;
        [SerializeField] private NetworkObject ghoulPrefab;
        [SerializeField] private NetworkObject brutePrefab;
        [SerializeField] private NetworkObject necromancerPrefab;

        [Header("Dungeon objects")]
        [SerializeField] private NetworkObject chestPrefab;
        [SerializeField] private NetworkObject altarPrefab;
        [SerializeField] private NetworkObject breakablePrefab;
        [SerializeField] private NetworkObject trapPrefab;
        [SerializeField] private NetworkObject lootPrefab;
        /// <summary>One boss per floor, by depth: the Gravewarden, the Tide Lich, the Hollow King.</summary>
        [SerializeField] private NetworkObject[] bossPrefabs;
        [SerializeField] private NetworkObject dummyPrefab;

        /// <summary>x = nonce (the seed, for floors), y = depth; -1 is the lobby.</summary>
        public readonly NetworkVariable<Vector2Int> Floor = new NetworkVariable<Vector2Int>(new Vector2Int(0, int.MinValue));
        public readonly NetworkVariable<RunPhase> Phase = new NetworkVariable<RunPhase>(RunPhase.Lobby);
        /// <summary>Server time the pending transition fires (descend, return, depart); 0 if none.</summary>
        public readonly NetworkVariable<double> PhaseEndsAt = new NetworkVariable<double>();

        public static DungeonDirector Instance { get; private set; }

        /// <summary>The floor currently built on this peer, or null in the lobby.</summary>
        public static DungeonLayout Layout { get; private set; }

        public static bool InLobby => Instance != null && Instance.Floor.Value.y == LobbyDepth;

        /// <summary>
        /// Raised on every peer after new local geometry — a floor or the lobby — exists.
        /// The argument changes every time, so listeners can tell builds apart.
        /// </summary>
        public static event Action<int> ArenaBuilt;
        private static int arenaCounter;

        /// <summary>Where a given player should stand right now.</summary>
        public static Vector3 SpawnFor(ulong clientId) => Layout != null ? Layout.PlayerSpawnFor(clientId) : Lobby.SpawnFor(clientId);

        /// <summary>A point to face when spawning: the lobby fire, or the floor's start room centre.</summary>
        public static Vector3 SpawnLookTarget => Layout != null ? Layout.StartRoom.Center : Lobby.Origin;

        public static int ArenaId => arenaCounter;

        private GameObject built;
        private Vector2Int builtFloor = new Vector2Int(int.MinValue, int.MinValue);
        private readonly List<NetworkObject> spawned = new List<NetworkObject>();
        private BossBrain boss;
        private double phaseTimer;
        private bool bossSeen;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Floor.OnValueChanged += (_, now) => BuildLocal(now);

            if (IsServer) EnterLobby();
            else BuildLocal(Floor.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (built != null) Destroy(built);
            Layout = null;
        }

        // ------------------------------------------------------------------
        // Transitions (server)
        // ------------------------------------------------------------------

        private void EnterLobby()
        {
            ClearSpawned();
            Phase.Value = RunPhase.Lobby;
            PhaseEndsAt.Value = 0;
            Floor.Value = new Vector2Int(UnityEngine.Random.Range(1, int.MaxValue), LobbyDepth);
            BuildLocal(Floor.Value);

            if (dummyPrefab != null)
            {
                foreach (var spot in Lobby.DummySpots)
                {
                    var dummy = Instantiate(dummyPrefab, spot, Quaternion.Euler(0f, 90f, 0f));
                    dummy.Spawn(destroyWithScene: true);
                    spawned.Add(dummy);
                }
            }

            // Back at camp, the run is over: wipe its power and pick everyone up.
            foreach (var p in PlayerLife.All)
            {
                if (p.TryGetComponent<PlayerProgress>(out var progress) && progress.Stats != null) progress.ServerResetRun();
                p.ServerRevive(1f);
            }
            Debug.Log("[Director] Lobby");
        }

        /// <summary>Server: board the carriage and start floor one. Also used by the smoke test.</summary>
        public void ServerStartRun()
        {
            if (IsServer) StartFloor(0);
        }

        /// <summary>Server, debugging and screenshots only: jump straight to a floor.</summary>
        public void ServerDebugStartFloor(int depth)
        {
            if (IsServer) StartFloor(depth);
        }

        private void StartFloor(int depth)
        {
            ClearSpawned();

            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            Phase.Value = RunPhase.Exploring;
            PhaseEndsAt.Value = 0;
            Floor.Value = new Vector2Int(seed, depth);

            // NetworkVariable callbacks do not fire for the writer's own change on every
            // NGO version, so build explicitly; BuildLocal ignores a repeat.
            BuildLocal(Floor.Value);

            BakeNavMesh();
            SpawnObjects(Layout);
            SpawnEnemies(Layout);

            foreach (var p in PlayerLife.All)
            {
                p.ServerRevive(1f);
                if (p.TryGetComponent<PlayerProgress>(out var prog)) prog.ServerNewFloor();
            }

            Debug.Log("[Director] Floor " + (depth + 1) + " started, seed " + seed + ", " + spawned.Count + " enemies.");
        }

        private void ClearSpawned()
        {
            foreach (var obj in spawned)
            {
                if (obj != null && obj.IsSpawned) obj.Despawn();
            }
            spawned.Clear();
            boss = null;
            bossSeen = false;

            // Boss minions are not tracked individually; sweep up anything left.
            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain.NetworkObject.IsSpawned) brain.NetworkObject.Despawn();
            }
            foreach (var i in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            {
                if (i.NetworkObject.IsSpawned) i.NetworkObject.Despawn();
            }
            foreach (var b in FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            {
                if (b.NetworkObject.IsSpawned) b.NetworkObject.Despawn();
            }
            foreach (var t in FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None))
            {
                if (t.NetworkObject.IsSpawned) t.NetworkObject.Despawn();
            }
            // Unclaimed loot does not follow you down: pick it up or lose it.
            foreach (var drop in FindObjectsByType<LootDrop>(FindObjectsSortMode.None))
            {
                if (drop.NetworkObject.IsSpawned) drop.NetworkObject.Despawn();
            }
        }

        // ------------------------------------------------------------------
        // Local build (every peer)
        // ------------------------------------------------------------------

        private void BuildLocal(Vector2Int floor)
        {
            if (floor.y == int.MinValue || floor == builtFloor) return;
            builtFloor = floor;

            if (built != null) Destroy(built);
            Combat.Arrows.Clear();

            if (floor.y == LobbyDepth)
            {
                Layout = null;
                built = Lobby.Build();
            }
            else
            {
                Layout = DungeonLayout.Generate(floor.x, floor.y);
                built = DungeonBuilder.Build(Layout);
            }

            // The geometry must exist in the physics scene before anyone is placed on it,
            // or a teleported player falls through the not-yet-registered collider.
            Physics.SyncTransforms();
            arenaCounter++;
            ArenaBuilt?.Invoke(arenaCounter);
        }

        private void BakeNavMesh()
        {
            var surface = built.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        private void SpawnEnemies(DungeonLayout layout)
        {
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

            float eliteChance = Difficulty.EliteChance(layout.Depth, players);
            var theme = DungeonTheme.ForDepth(layout.Depth);

            foreach (var room in layout.Rooms)
            {
                if (room.EnemySpawns.Count == 0) continue;

                // More players, more bodies: scale the room's head count, cycling through its
                // spawn points with a little jitter for the extras.
                int count = Mathf.RoundToInt(room.EnemySpawns.Count * Difficulty.SpawnCount(players));
                bool hasBrute = false, hasNecro = false;

                for (int i = 0; i < count; i++)
                {
                    var p = room.EnemySpawns[i % room.EnemySpawns.Count];
                    if (i >= room.EnemySpawns.Count) p += new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0f, UnityEngine.Random.Range(-1.5f, 1.5f));

                    var prefab = PickEnemy(layout.Depth, theme, ref hasBrute, ref hasNecro, out int pack);
                    for (int k = 0; k < pack; k++)
                    {
                        var at = p + (k > 0 ? new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)) : Vector3.zero);
                        if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
                        var obj = Instantiate(prefab, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                        obj.Spawn(destroyWithScene: true);
                        var brain = obj.GetComponent<EnemyBrain>();
                        brain.ServerScale(players, layout.Depth);
                        if (brain is NecromancerBrain necro) necro.SetMinionPrefab(skeletonPrefab);
                        if (UnityEngine.Random.value < eliteChance) brain.ServerMakeElite();
                        spawned.Add(obj);
                    }
                }
            }

            var spawn = layout.BossSpawn;
            var bossPrefab = bossPrefabs[Mathf.Clamp(layout.Depth, 0, bossPrefabs.Length - 1)];
            var bossObj = Instantiate(bossPrefab, spawn, Quaternion.LookRotation(layout.BossRoom.Center - spawn + Vector3.back * 0.01f));
            bossObj.Spawn(destroyWithScene: true);
            boss = bossObj.GetComponent<BossBrain>();
            // The Lich raises archers from the water; everyone else raises swordsmen.
            boss.SetMinionPrefab(boss is TideLichBrain && archerPrefab != null ? archerPrefab : skeletonPrefab);
            boss.ServerScaleBoss(players, layout.Depth);
            spawned.Add(bossObj);
            bossSeen = true;
        }

        /// <summary>
        /// The things in a floor that are not monsters: a chest in every side room, urns and
        /// powder barrels along room walls, spike traps in some corridors, and sometimes a
        /// blood altar. Placed after the navmesh bake so they never cut holes in it.
        /// </summary>
        private void SpawnObjects(DungeonLayout layout)
        {
            if (breakablePrefab == null) return;
            var rng = new System.Random(layout.Seed ^ 0x5eed);
            DungeonLayout.Room altarRoom = null;
            if (altarPrefab != null && rng.NextDouble() < 0.5)
            {
                var combat = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat);
                if (combat.Count > 0) altarRoom = combat[rng.Next(combat.Count)];
            }

            foreach (var room in layout.Rooms)
            {
                if (room.Kind == RoomKind.Side && chestPrefab != null)
                {
                    int entry = 0;
                    for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
                    var back = -new Vector3(DungeonLayout.Dir[entry].x, 0f, DungeonLayout.Dir[entry].y);
                    var chest = Spawn(chestPrefab, room.Center + back * (room.Size.y / 2f - 1.5f), Quaternion.LookRotation(-back));
                    chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                }

                if (room == altarRoom)
                {
                    Spawn(altarPrefab, room.Center + new Vector3(0f, 0f, room.Size.y / 2f - 1.8f), Quaternion.Euler(0f, 180f, 0f));
                }

                if (room.Kind == RoomKind.Boss || room.Kind == RoomKind.Start) continue;

                int urns = 2 + rng.Next(3);
                for (int i = 0; i < urns; i++) SpawnBreakable(Breakable.Kind.Urn, WallSpot(room, rng));
                if (room.Kind == RoomKind.Combat && rng.NextDouble() < 0.6)
                {
                    int barrels = 1 + rng.Next(2);
                    for (int i = 0; i < barrels; i++) SpawnBreakable(Breakable.Kind.PowderBarrel, WallSpot(room, rng));
                }
            }

            if (trapPrefab != null)
            {
                foreach (var link in layout.Links)
                {
                    var a = layout.Rooms[link.A];
                    var b = layout.Rooms[link.B];
                    if (a.Kind == RoomKind.Boss || b.Kind == RoomKind.Boss || a.Kind == RoomKind.Start) continue;
                    if (rng.NextDouble() < 0.4) Spawn(trapPrefab, layout.CorridorMidpoint(link) + Vector3.up * 0.005f, Quaternion.identity);
                }
            }
        }

        private NetworkObject Spawn(NetworkObject prefab, Vector3 at, Quaternion rot)
        {
            var obj = Instantiate(prefab, at, rot);
            obj.Spawn(destroyWithScene: true);
            return obj;
        }

        private void SpawnBreakable(Breakable.Kind kind, Vector3 at)
        {
            var obj = Instantiate(breakablePrefab, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            var b = obj.GetComponent<Breakable>();
            b.Type.Value = kind;
            b.SetLootPrefab(lootPrefab);
            obj.Spawn(destroyWithScene: true);
        }

        /// <summary>A spot about a metre in from a random wall, clear of the doorway.</summary>
        private static Vector3 WallSpot(DungeonLayout.Room room, System.Random rng)
        {
            for (int tries = 0; tries < 8; tries++)
            {
                int side = rng.Next(4);
                var d = DungeonLayout.Dir[side];
                bool alongX = side == 0 || side == 2;
                float half = (alongX ? room.Size.x : room.Size.y) / 2f - 1.2f;
                float t = (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                if (room.Doors[side] && Mathf.Abs(t) < DungeonLayout.CorridorWidth) continue;
                float off = (alongX ? room.Size.y : room.Size.x) / 2f - 1.0f;
                return room.Center + new Vector3(d.x, 0f, d.y) * off + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
            }
            return room.Center + new Vector3(room.Size.x / 2f - 1.2f, 0f, room.Size.y / 2f - 1.2f);
        }

        /// <summary>
        /// The roster, by depth. Floor 1 is mostly swordsmen and ghouls to teach the basics;
        /// deeper floors bring archers, brutes and necromancers. At most one brute and one
        /// necromancer per room — they are the room's centrepiece, not its wallpaper.
        /// </summary>
        private NetworkObject PickEnemy(int depth, DungeonTheme theme, ref bool hasBrute, ref bool hasNecro, out int pack)
        {
            pack = 1;
            float brute = brutePrefab != null && !hasBrute ? 0.06f + 0.03f * depth : 0f;
            float necro = necromancerPrefab != null && !hasNecro && depth >= 1 ? 0.06f + 0.03f * depth : 0f;
            float ghoul = ghoulPrefab != null ? 0.22f : 0f;
            float archer = archerPrefab != null ? theme.ArcherChance : 0f;

            float roll = UnityEngine.Random.value;
            if ((roll -= brute) < 0f) { hasBrute = true; return brutePrefab; }
            if ((roll -= necro) < 0f) { hasNecro = true; return necromancerPrefab; }
            if ((roll -= ghoul) < 0f) { pack = 2; return ghoulPrefab; }
            if ((roll -= archer) < 0f) return archerPrefab;
            return skeletonPrefab;
        }

        // ------------------------------------------------------------------
        // Run state (server)
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!IsServer || Instance != this) return;
            double now = NetworkManager.ServerTime.Time;

            switch (Phase.Value)
            {
                case RunPhase.Lobby:
                    // Everyone in the boarding circle starts a short countdown; anyone
                    // stepping out cancels it. One player can start a solo run.
                    if (EveryoneAboard())
                    {
                        if (PhaseEndsAt.Value <= 0) PhaseEndsAt.Value = now + BoardingDelay;
                        else if (now >= PhaseEndsAt.Value) StartFloor(0);
                    }
                    else if (PhaseEndsAt.Value > 0)
                    {
                        PhaseEndsAt.Value = 0;
                    }
                    break;

                case RunPhase.Exploring:
                    if (Layout == null) break;
                    if (PartyWiped())
                    {
                        EnterPhase(RunPhase.Wiped, WipeDelay);
                    }
                    else if (bossSeen && (boss == null || boss.IsDead))
                    {
                        bool last = Floor.Value.y >= FloorsPerRun - 1;
                        EnterPhase(last ? RunPhase.RunComplete : RunPhase.FloorCleared, last ? CompleteDelay : DescendDelay);
                        // The cleared floor is safe: pick everyone up.
                        foreach (var p in PlayerLife.All)
                        {
                            if (p.Downed.Value) p.ServerRevive(0.5f);
                        }
                    }
                    break;

                default:
                    if (now >= phaseTimer)
                    {
                        if (Phase.Value == RunPhase.FloorCleared) StartFloor(Floor.Value.y + 1);
                        else EnterLobby();
                    }
                    break;
            }
        }

        private void EnterPhase(RunPhase phase, float delay)
        {
            Phase.Value = phase;
            phaseTimer = NetworkManager.ServerTime.Time + delay;
            PhaseEndsAt.Value = phaseTimer;
            Debug.Log("[Director] " + phase);
        }

        public static int PlayersAboard()
        {
            int n = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p != null && Lobby.InBoardingCircle(p.transform.position)) n++;
            }
            return n;
        }

        private static bool EveryoneAboard() => PlayerLife.All.Count > 0 && PlayersAboard() == PlayerLife.All.Count;

        private static bool PartyWiped()
        {
            if (PlayerLife.All.Count == 0) return false;
            foreach (var p in PlayerLife.All)
            {
                if (!p.Downed.Value) return false;
            }
            return true;
        }
    }
}
