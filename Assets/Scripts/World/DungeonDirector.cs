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
    public enum RunPhase : byte
    {
        Exploring = 0,
        FloorCleared = 1,  // boss dead, descending soon
        Wiped = 2,         // everyone downed, run over
        RunComplete = 3,   // final boss dead
    }

    /// <summary>
    /// Runs the dungeon: which floor you are on, when it is cleared, when the party has
    /// wiped. One per session, spawned by the server when hosting starts.
    ///
    /// <para>
    /// Floors are synced as a single (seed, depth) pair. Each peer rebuilds the geometry
    /// locally from it (see <see cref="DungeonLayout"/>); only the server bakes a navmesh
    /// and spawns enemies, which reach clients as ordinary networked objects.
    /// </para>
    ///
    /// The run is three floors (design doc Q6, decided 2026-10-13: three, then extend).
    /// Clearing a floor's boss descends to the next; clearing the third ends the run. A
    /// wipe restarts from floor one — nothing earned inside a run survives it.
    /// </summary>
    public class DungeonDirector : NetworkBehaviour
    {
        public const int FloorsPerRun = 3;
        private const float DescendDelay = 8f, WipeDelay = 6f, CompleteDelay = 14f;

        [SerializeField] private NetworkObject skeletonPrefab;
        [SerializeField] private NetworkObject bossPrefab;

        /// <summary>x = seed, y = depth (0-based floor index).</summary>
        public readonly NetworkVariable<Vector2Int> Floor = new NetworkVariable<Vector2Int>(new Vector2Int(0, -1));
        public readonly NetworkVariable<RunPhase> Phase = new NetworkVariable<RunPhase>(RunPhase.Exploring);
        /// <summary>Server time the current phase transition fires, for HUD countdowns.</summary>
        public readonly NetworkVariable<double> PhaseEndsAt = new NetworkVariable<double>();

        public static DungeonDirector Instance { get; private set; }

        /// <summary>The floor currently built on this peer.</summary>
        public static DungeonLayout Layout { get; private set; }

        /// <summary>Raised on every peer after the local geometry for a new floor exists.</summary>
        public static event Action<DungeonLayout> FloorBuilt;

        private GameObject built;
        private Vector2Int builtFloor = new Vector2Int(0, -1);
        private readonly List<NetworkObject> spawned = new List<NetworkObject>();
        private BossBrain boss;
        private double phaseTimer;
        private bool bossSeen;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Floor.OnValueChanged += (_, now) => BuildLocal(now);

            if (IsServer)
            {
                StartFloor(0);
            }
            else if (Floor.Value.y >= 0)
            {
                BuildLocal(Floor.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (built != null) Destroy(built);
            Layout = null;
        }

        // ------------------------------------------------------------------
        // Floors
        // ------------------------------------------------------------------

        private void StartFloor(int depth)
        {
            foreach (var obj in spawned)
            {
                if (obj != null && obj.IsSpawned) obj.Despawn();
            }
            spawned.Clear();
            // Boss minions are not tracked individually; sweep up anything left.
            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain.NetworkObject.IsSpawned) brain.NetworkObject.Despawn();
            }
            // Unclaimed loot does not follow you down: pick it up or lose it.
            foreach (var drop in FindObjectsByType<LootDrop>(FindObjectsSortMode.None))
            {
                if (drop.NetworkObject.IsSpawned) drop.NetworkObject.Despawn();
            }

            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            Phase.Value = RunPhase.Exploring;
            Floor.Value = new Vector2Int(seed, depth);

            // NetworkVariable callbacks do not fire for the writer's own change on every
            // NGO version, so build explicitly; BuildLocal ignores a repeat.
            BuildLocal(Floor.Value);

            BakeNavMesh();
            SpawnEnemies(Layout);

            foreach (var p in PlayerLife.All)
            {
                // A new run (floor 1) wipes everything the last one granted.
                if (depth == 0 && p.TryGetComponent<PlayerProgress>(out var progress) && progress.Stats != null) progress.ServerResetRun();
                p.ServerRevive(1f);
            }

            Debug.Log("[Director] Floor " + (depth + 1) + " started, seed " + seed + ", " + spawned.Count + " enemies.");
        }

        private void BuildLocal(Vector2Int floor)
        {
            if (floor.y < 0 || floor == builtFloor) return;
            builtFloor = floor;

            if (built != null) Destroy(built);
            Layout = DungeonLayout.Generate(floor.x, floor.y);
            built = DungeonBuilder.Build(Layout);

            // The floor geometry must exist in the physics scene before anyone is placed
            // on it, or a teleported player falls through the not-yet-registered collider.
            Physics.SyncTransforms();
            FloorBuilt?.Invoke(Layout);
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

            foreach (var room in layout.Rooms)
            {
                foreach (var point in room.EnemySpawns)
                {
                    var p = point;
                    if (NavMesh.SamplePosition(point, out var hit, 3f, NavMesh.AllAreas)) p = hit.position;
                    var obj = Instantiate(skeletonPrefab, p, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                    obj.Spawn(destroyWithScene: true);
                    obj.GetComponent<EnemyBrain>().ServerScale(players, layout.Depth);
                    spawned.Add(obj);
                }
            }

            var spawn = layout.BossSpawn;
            var bossObj = Instantiate(bossPrefab, spawn, Quaternion.LookRotation(layout.BossRoom.Center - spawn + Vector3.back * 0.01f));
            bossObj.Spawn(destroyWithScene: true);
            boss = bossObj.GetComponent<BossBrain>();
            boss.SetMinionPrefab(skeletonPrefab);
            boss.ServerScaleBoss(players, layout.Depth);
            spawned.Add(bossObj);
            bossSeen = true;
        }

        // ------------------------------------------------------------------
        // Run state
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!IsServer || Layout == null) return;
            double now = NetworkManager.ServerTime.Time;

            switch (Phase.Value)
            {
                case RunPhase.Exploring:
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
                        int next = Phase.Value == RunPhase.FloorCleared ? Floor.Value.y + 1 : 0;
                        bossSeen = false;
                        StartFloor(next);
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
