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
        FloorCleared = 1,  // boss dead, returning to camp soon
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
    public partial class DungeonDirector : NetworkBehaviour
    {
        public const int FloorsPerRun = 4;
        // Long enough to read the run summary before returning to camp.
        private const float DescendDelay = 8f, WipeDelay = 14f, CompleteDelay = 18f, BoardingDelay = 3f;
        private const int LobbyDepth = -1;

        [SerializeField] private NetworkObject skeletonPrefab;
        [SerializeField] private NetworkObject archerPrefab;
        [SerializeField] private NetworkObject ghoulPrefab;
        [SerializeField] private NetworkObject brutePrefab;
        [SerializeField] private NetworkObject necromancerPrefab;
        [SerializeField] private NetworkObject wraithPrefab;
        [SerializeField] private NetworkObject shieldbearerPrefab;
        [SerializeField] private NetworkObject mimicPrefab;
        [SerializeField] private NetworkObject hoarderPrefab;
        [SerializeField] private NetworkObject gargoylePrefab;
        [SerializeField] private NetworkObject plaguebearerPrefab, frostWightPrefab, hexerPrefab;
        [SerializeField] private NetworkObject fateWheelPrefab;
        [SerializeField] private NetworkObject bountyNoticePrefab;
        [SerializeField] private NetworkObject omenStonePrefab;

        /// <summary>Server: a Splitting elite breaks apart into two lesser skeletons.</summary>
        public void ServerSpawnSplit(Vector3 at, int players, int depth)
        {
            if (skeletonPrefab == null) return;
            var points = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                var p = at + Quaternion.Euler(0f, i * 180f + 90f, 0f) * Vector3.forward * 1.1f;
                if (NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) p = hit.position;
                var obj = Spawn(skeletonPrefab, p, Quaternion.identity);
                var brain = obj.GetComponent<EnemyBrain>();
                brain.NoLoot = true;
                brain.ServerScale(players, depth);
                brain.ServerAggroNearest();
                spawned.Add(obj);
                points[i] = p;
            }
            RiseFxRpc(points);
        }

        /// <summary>Server: a pack of the floor's dead around a point (the Wheel's cruel turn).</summary>
        public void ServerSpawnPack(Vector3 around, int count, bool elite)
        {
            if (Layout == null) return;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            var theme = DungeonTheme.ForDepth(Layout.Depth);
            bool brute = true, necro = true;
            var points = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < count; i++)
            {
                var at = around + Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward * 2.5f;
                if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
                var obj = Spawn(PickEnemy(Layout.Depth, theme, ref brute, ref necro, out int _), at, Quaternion.identity);
                var brain = obj.GetComponent<EnemyBrain>();
                brain.ServerScale(players, Layout.Depth);
                if (brain is NecromancerBrain nb) nb.SetMinionPrefab(skeletonPrefab);
                if (elite && i == 0) brain.ServerMakeElite();
                spawned.Add(obj);
                points.Add(at);
            }
            RiseFxRpc(points.ToArray());
        }

        /// <summary>Server: a mimic chest was reached for — it becomes the Mimic, and bites.</summary>
        public void ServerSpringMimic(Chest chest, Combat.Health victim)
        {
            if (mimicPrefab == null || Layout == null) return;
            var obj = Spawn(mimicPrefab, chest.transform.position, chest.transform.rotation);
            var brain = obj.GetComponent<MimicBrain>();
            brain.ServerScale(Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count), Layout.Depth);
            spawned.Add(obj);
            chest.NetworkObject.Despawn();
            brain.ServerAmbush(victim);
        }

        [Header("Dungeon objects")]
        [SerializeField] private NetworkObject chestPrefab;
        [SerializeField] private NetworkObject altarPrefab;
        [SerializeField] private NetworkObject breakablePrefab;
        [SerializeField] private NetworkObject anvilPrefab;
        [SerializeField] private NetworkObject gearDropPrefab;

        /// <summary>A class combo fired: float its name over the spot for everyone.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ComboFxRpc(Vector3 at, byte kind, bool first)
        {
            var k = (Classes.ComboKind)kind;
            UI.GameHud.ShowCombo(at + Vector3.up * 0.6f, Classes.Combos.Label(k), Classes.Combos.Color(k));
            if (first) UI.GameHud.Feed("COMBO  " + Classes.Combos.Explain(k), Classes.Combos.Color(k));
            if (k == Classes.ComboKind.Shatter)
            {
                Art.Sfx.Play(Art.Sound.Shatter, at, 0.9f, 1.15f);
                Art.Vfx.Embers(at, 16, new Color(0.6f, 0.9f, 1f), 6f, 0.07f, 0.7f);
                Art.Vfx.IceSpikes(Art.Vfx.Ground(at), 1.2f, 6);
            }
            else if (k == Classes.ComboKind.Exposed || k == Classes.ComboKind.WardBroken)
                Art.Vfx.Embers(at, 8, Classes.Combos.Color(k), 3f, 0.05f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void EmberBurstFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Fire, at + Vector3.up, 0.8f, 1.2f);
            Art.Vfx.Fireball(at + Vector3.up * 0.5f, 2f, new Color(1f, 0.5f, 0.15f));
            Art.Vfx.Embers(at, 10, new Color(1f, 0.5f, 0.15f), 5f, 0.06f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void AegisBurstFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Revive, at + Vector3.up, 0.8f, 1.6f);
            Art.Vfx.Ring(at, Classes.Combos.AegisBurstRadius, new Color(1f, 0.85f, 0.45f), 0.35f);
            Art.Vfx.Embers(at + Vector3.up, 14, new Color(1f, 0.85f, 0.45f), 5f, 0.06f, 0.6f);
            Art.Vfx.Flash(at + Vector3.up, new Color(1f, 0.85f, 0.45f), 5f, 6f, 0.3f);
        }

        /// <summary>Server: a small pile of embers (Ember Locket).</summary>
        public void ServerDropEmbers(Vector3 at, int amount) => ServerDropLoot(at, LootKind.Embers, Mathf.RoundToInt(amount * OmenEmbers));

        /// <summary>Server: one loot drop of a given kind at a point.</summary>
        public void ServerDropLoot(Vector3 at, LootKind kind, int amount)
        {
            if (lootPrefab == null) return;
            var obj = Instantiate(lootPrefab, at + Vector3.up * 0.05f, Quaternion.identity);
            var drop = obj.GetComponent<LootDrop>();
            drop.Kind.Value = kind;
            drop.Amount.Value = amount;
            obj.Spawn(destroyWithScene: true);
        }
        [SerializeField] private NetworkObject trapPrefab;
        [SerializeField] private NetworkObject lootPrefab;
        /// <summary>One boss per floor, by depth: the Gravewarden, the Tide Lich, the Hollow King, the Pale Cardinal.</summary>
        [SerializeField] private NetworkObject[] bossPrefabs;

        /// <summary>Every enemy prefab the director can spawn (balance report).</summary>
        public System.Collections.Generic.IEnumerable<EnemyBrain> AllEnemyPrefabs()
        {
            var all = new System.Collections.Generic.List<NetworkObject> { skeletonPrefab, archerPrefab, ghoulPrefab, brutePrefab, necromancerPrefab, wraithPrefab,
                shieldbearerPrefab, mimicPrefab, hoarderPrefab, gargoylePrefab, plaguebearerPrefab, frostWightPrefab, hexerPrefab };
            if (bossPrefabs != null) all.AddRange(bossPrefabs);
            foreach (var p in all) if (p != null && p.TryGetComponent<EnemyBrain>(out var b)) yield return b;
        }
        [SerializeField] private NetworkObject dummyPrefab;

        /// <summary>x = nonce (the seed, for floors), y = depth; -1 is the lobby.</summary>
        public readonly NetworkVariable<Vector2Int> Floor = new NetworkVariable<Vector2Int>(new Vector2Int(0, int.MinValue));
        public readonly NetworkVariable<RunPhase> Phase = new NetworkVariable<RunPhase>(RunPhase.Lobby);
        /// <summary>Server time the pending transition fires (descend, return, depart); 0 if none.</summary>
        public readonly NetworkVariable<double> PhaseEndsAt = new NetworkVariable<double>();

        /// <summary>Index into <see cref="Difficulty.Settings"/>. The host picks it at camp.</summary>
        public readonly NetworkVariable<byte> DifficultyIndex = new NetworkVariable<byte>();

        /// <summary>
        /// The floor the carriage takes the party to next. 0 means no run is under way (a
        /// fresh start); after each boss the party comes back to camp with this advanced.
        /// </summary>
        public readonly NetworkVariable<int> NextDepth = new NetworkVariable<int>();

        /// <summary>A run is in progress (the party is at camp between floors).</summary>
        public bool RunInProgress => NextDepth.Value > 0;

        /// <summary>The depth gear, prices and attunement use: the floor you are on, or the next one from camp.</summary>
        public static int GearDepth => Instance == null ? 0 : InLobby ? Instance.NextDepth.Value : Mathf.Max(0, Instance.Floor.Value.y);

        /// <summary>The merchant's stall, only at camp.</summary>
        public static Vector3? MerchantPosition => InLobby ? Lobby.MerchantSpot : Layout?.PeddlerSpot;

        /// <summary>Host → server: change difficulty. Only honoured in the lobby.</summary>
        [Rpc(SendTo.Server)]
        public void SetDifficultyRpc(byte index)
        {
            if (Phase.Value != RunPhase.Lobby || RunInProgress) return;
            DifficultyIndex.Value = (byte)Mathf.Clamp(index, 0, Difficulty.Settings.Length - 1);
        }

        /// <summary>Server time the current run began (boarding the carriage), for the summary.</summary>
        public readonly NetworkVariable<double> RunStartedAt = new NetworkVariable<double>();

        /// <summary>Server time the run ended (wipe or victory), frozen for the summary screen.</summary>
        public readonly NetworkVariable<double> RunEndedAt = new NetworkVariable<double>();

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
            if (IsServer) GearDrop.Prefab = gearDropPrefab;
            if (IsServer) Player.PlayerVoice.ServerHeard += ServerOnShout;
            Instance = this;
            Floor.OnValueChanged += (_, now) => BuildLocal(now);

            if (IsServer) EnterLobby();
            else BuildLocal(Floor.Value);
        }

        /// <summary>Server: a shout wakes the dead within earshot (statues included). Bosses ignore it.</summary>
        private void ServerOnShout(Combat.Health who, Vector3 at, float radius)
        {
            int woke = 0;
            foreach (var h in Combat.Health.All)
            {
                if (h == null || h.Team != Combat.Team.Enemies || !h.IsAlive) continue;
                if (Vector3.Distance(h.transform.position, at) > radius) continue;
                var brain = h.GetComponent<Enemies.EnemyBrain>();
                if (brain == null || brain is Enemies.BossBrain) continue;
                brain.ServerHear(who);
                woke++;
            }
            if (woke > 0) Debug.Log("[Voice] shout at " + at.ToString("F0") + " alerted " + woke);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Player.PlayerVoice.ServerHeard -= ServerOnShout;
            if (Instance == this) Instance = null;
            if (built != null) Destroy(built);
            Layout = null;
        }

        // ------------------------------------------------------------------
        // Transitions (server)
        // ------------------------------------------------------------------

        /// <param name="keepRun">True between floors: the party keeps everything and heals up.</param>
        private void EnterLobby(bool keepRun = false)
        {
            ClearSpawned();
            ClearSettledContract();
            ApplyOmen(false);
            if (!keepRun) NextDepth.Value = 0;
            Phase.Value = RunPhase.Lobby;
            PhaseEndsAt.Value = 0;
            Floor.Value = new Vector2Int(UnityEngine.Random.Range(1, int.MaxValue), LobbyDepth);
            Debug.Log("[Director] Camp" + (keepRun ? " (between floors, next " + (NextDepth.Value + 1) + ")" : ""));
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

            SpawnCampStations();

            // Between floors the party keeps its run and rests; after a run, everything goes.
            foreach (var p in PlayerLife.All)
            {
                if (p.TryGetComponent<PlayerProgress>(out var blessed)) blessed.ServerClearBlessing();
                if (!keepRun && p.TryGetComponent<PlayerProgress>(out var progress) && progress.Stats != null) progress.ServerResetRun();
                p.ServerRevive(1f);
                p.Health.ServerSetFraction(1f);
            }
            Debug.Log("[Director] Lobby");
        }

        [SerializeField] private NetworkObject diceTablePrefab;
        [SerializeField] private NetworkObject cofferPrefab;
        [SerializeField] private NetworkObject gongPrefab;
        [SerializeField] private NetworkObject shrinePrefab;
        [SerializeField] private NetworkObject rangeTargetPrefab;

        private void SpawnCampStations()
        {
            void Put(NetworkObject prefab, Vector3 at, Vector3 face)
            {
                if (prefab == null) return;
                var obj = Instantiate(prefab, at, Quaternion.LookRotation(face));
                obj.Spawn(destroyWithScene: true);
                spawned.Add(obj);
            }
            Put(anvilPrefab, Lobby.AnvilSpot, Lobby.AnvilFacing);
            Put(diceTablePrefab, Lobby.DiceSpot, Lobby.TentFacing);
            Put(cofferPrefab, Lobby.CofferSpot, Lobby.TentFacing);
            Put(gongPrefab, Lobby.GongSpot, Lobby.GongFacing);
            Put(shrinePrefab, Lobby.ShrineSpot, Vector3.left);
            Put(omenStonePrefab, Lobby.OmenSpot, Vector3.left);
            // The bounty board: three different contracts for the next floor.
            if (bountyNoticePrefab != null)
            {
                var kinds = new System.Collections.Generic.List<byte> { 1, 2, 3, 4, 5 };
                for (int i = 0; i < 3; i++)
                {
                    int k = UnityEngine.Random.Range(0, kinds.Count);
                    var at = Lobby.BountySpot + Lobby.BountyFacing * 0.12f + Vector3.Cross(Vector3.up, Lobby.BountyFacing) * ((i - 1) * 0.85f);
                    var obj = Instantiate(bountyNoticePrefab, at, Quaternion.LookRotation(Lobby.BountyFacing));
                    obj.GetComponent<BountyNotice>().Kind.Value = kinds[k];
                    kinds.RemoveAt(k);
                    obj.Spawn(destroyWithScene: true);
                    spawned.Add(obj);
                }
            }
            if (rangeTargetPrefab != null)
            {
                for (int i = 0; i < Lobby.TargetSpots.Length; i++)
                {
                    // Targets face the shooting line (west); they slide along their rails (north-south).
                    var obj = Instantiate(rangeTargetPrefab, Lobby.TargetSpots[i], Quaternion.LookRotation(Vector3.left));
                    var t = obj.GetComponent<RangeTarget>();
                    t.Phase.Value = i * 2.1f;
                    t.Speed.Value = 0.6f + i * 0.35f;
                    obj.Spawn(destroyWithScene: true);
                    spawned.Add(obj);
                }
            }
        }

        /// <summary>Server: the carriage leaves — for the next floor of this run, or a new run.</summary>
        public void ServerBoard()
        {
            if (!IsServer) return;
            if (RunInProgress) StartFloor(NextDepth.Value);
            else ServerStartRun();
        }

        /// <summary>Server: board the carriage and start floor one. Also used by the smoke test.</summary>
        public void ServerStartRun()
        {
            if (!IsServer) return;
            RunStartedAt.Value = NetworkManager.ServerTime.Time;
            Classes.Combos.ResetRun();
            StartFloor(0);
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
            SetupEncounters(Layout);
            SetupFeatures(Layout);
            SetupHoles(Layout);
            ApplyOmen(true);
            SpawnEnemies(Layout);
            BeginContract(Layout.Depth);

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
            ResetEncounters();

            // Boss minions are not tracked individually; sweep up anything left.
            foreach (var brain in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
            {
                if (brain.NetworkObject.IsSpawned) brain.NetworkObject.Despawn();
            }
            foreach (var i in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            {
                if (i.NetworkObject.IsSpawned) i.NetworkObject.Despawn();
            }
            foreach (var a in FindObjectsByType<Anvil>(FindObjectsSortMode.None))
            {
                if (a.NetworkObject.IsSpawned) a.NetworkObject.Despawn();
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
            Combat.Health.FreezesWhenChilled = h => InWater(h.transform.position);
            if (!bonesHooked)
            {
                bonesHooked = true;
                Combat.Health.ServerAnyDamaged += (t, dealt, from) =>
                {
                    if (t != null && !t.IsAlive && t.Team == Combat.Team.Enemies && Instance == this && !pileFor.Contains(t.NetworkObjectId))
                    {
                        pileFor.Add(t.NetworkObjectId);
                        OnKillForBones(t);
                        ContractOnKill(t);
                    }
                    else if (t != null && !t.IsAlive && t.Team == Combat.Team.Players && Instance == this) ContractOnKill(t);
                };
            }
            Combat.Health.FrozenFx = h => FrozenFxRpc(h.Center);
            var surface = built.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
        }

        private void SpawnEnemies(DungeonLayout layout)
        {
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

            float eliteChance = Difficulty.EliteChance(layout.Depth, players) * OmenElites;
            var theme = DungeonTheme.ForDepth(layout.Depth);

            foreach (var room in layout.Rooms)
            {
                if (room.EnemySpawns.Count == 0 || IsAmbushRoom(room)) continue;

                // More players, more bodies: scale the room's head count, cycling through its
                // spawn points with a little jitter for the extras.
                int count = Mathf.RoundToInt(room.EnemySpawns.Count * Difficulty.SpawnCount(players) * Difficulty.Level.SpawnMultiplier * OmenCrowd);
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

            // One champion per floor: a named elite in a combat room, guarding a hoard.
            var candidates = new System.Collections.Generic.List<EnemyBrain>();
            foreach (var o in spawned)
                if (o != null && o.TryGetComponent<EnemyBrain>(out var cb) && !(cb is BossBrain) && !(cb is HoarderBrain) && !(cb is WraithBrain) && !(cb is GargoyleBrain) &&
                    layout.RoomAt(o.transform.position)?.Kind == RoomKind.Combat) candidates.Add(cb);
            if (candidates.Count > 0) candidates[UnityEngine.Random.Range(0, candidates.Count)].ServerMakeChampion();

            // From the second floor down, some of the statues by the walls are not statues.
            if (gargoylePrefab != null && layout.Depth >= 1)
            {
                var grng = new System.Random(layout.Seed ^ 0x6a76);
                foreach (var room in layout.Rooms)
                {
                    if (room.Kind != RoomKind.Combat || IsAmbushRoom(room) || grng.NextDouble() > 0.4) continue;
                    var gat = WallSpot(room, grng);
                    var face = room.Center - gat;
                    face.y = 0f;
                    if (NavMesh.SamplePosition(gat, out var ghit, 2f, NavMesh.AllAreas)) gat = ghit.position;
                    var gobj = Spawn(gargoylePrefab, gat, Quaternion.LookRotation(face));
                    gobj.GetComponent<EnemyBrain>().ServerScale(players, layout.Depth);
                    spawned.Add(gobj);
                }
            }

            // Now and then, a Bone Hoarder in one of the combat rooms: a chase for gold.
            var combatRooms = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && !IsAmbushRoom(r) && r.EnemySpawns.Count > 0);
            if (hoarderPrefab != null && combatRooms.Count > 0 && UnityEngine.Random.value < 0.45f)
            {
                var room = combatRooms[UnityEngine.Random.Range(0, combatRooms.Count)];
                var at = room.EnemySpawns[UnityEngine.Random.Range(0, room.EnemySpawns.Count)];
                if (NavMesh.SamplePosition(at, out var hhit, 3f, NavMesh.AllAreas)) at = hhit.position;
                var hobj = Spawn(hoarderPrefab, at, Quaternion.identity);
                hobj.GetComponent<EnemyBrain>().ServerScale(players, layout.Depth);
                spawned.Add(hobj);
            }

            var spawn = layout.BossSpawn;
            var bossPrefab = bossPrefabs[Mathf.Clamp(layout.Depth, 0, bossPrefabs.Length - 1)];
            var bossObj = Instantiate(bossPrefab, spawn, Quaternion.LookRotation(layout.BossRoom.Center - spawn + Vector3.back * 0.01f));
            bossObj.Spawn(destroyWithScene: true);
            boss = bossObj.GetComponent<BossBrain>();
            // The Lich raises archers from the water; everyone else raises swordsmen.
            boss.SetMinionPrefab(boss is TideLichBrain && archerPrefab != null ? archerPrefab
                : boss is CardinalBrain && wraithPrefab != null ? wraithPrefab : skeletonPrefab);
            boss.SetGuardPrefab(shieldbearerPrefab);
            boss.ServerScaleBoss(players, layout.Depth);
            boss.Dormant = portcullisPrefab != null; // waits on its throne until the party arrives
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

            DungeonLayout.Room wheelRoom = null;
            if (fateWheelPrefab != null && (rng.NextDouble() < 0.55 || TestMode))
            {
                var pool = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && r != altarRoom && r.Feature != RoomFeature.Chasm && r.Feature != RoomFeature.Crypt);
                if (pool.Count > 0) wheelRoom = pool[rng.Next(pool.Count)];
            }

            foreach (var room in layout.Rooms)
            {
                if (room.Kind == RoomKind.Side && chestPrefab != null)
                {
                    int entry = 0;
                    for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
                    var back = -new Vector3(DungeonLayout.Dir[entry].x, 0f, DungeonLayout.Dir[entry].y);
                    var chest = Spawn(chestPrefab, room.Center + back * (room.Size.y / 2f - 1.5f), Quaternion.LookRotation(-back));
                    // Some chests are hungry. Fewer on the first floor.
                    if (mimicPrefab != null && !TestMode && rng.NextDouble() < (layout.Depth == 0 ? 0.12 : 0.2)) chest.GetComponent<Chest>().Mimic.Value = true;
                    chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                }

                if (room == wheelRoom)
                {
                    // Against a wall with no door, facing in; failing that, across a corner.
                    bool placed = false;
                    for (int side = 0; side < 4 && !placed; side++)
                    {
                        if (room.Doors[side] || room.HoleOn(side) != null || room.Balconies.Contains(side)) continue;
                        var d = DungeonLayout.Dir[side];
                        var outward = new Vector3(d.x, 0f, d.y);
                        float half = (side == 0 || side == 2 ? room.Size.y : room.Size.x) / 2f;
                        Spawn(fateWheelPrefab, room.Center + outward * (half - 0.7f), Quaternion.LookRotation(-outward));
                        placed = true;
                    }
                    if (!placed)
                    {
                        var corner = room.Center + new Vector3(room.Size.x / 2f - 2.2f, 0f, room.Size.y / 2f - 2.2f);
                        var face = room.Center - corner;
                        face.y = 0f;
                        Spawn(fateWheelPrefab, corner, Quaternion.LookRotation(face));
                    }
                }

                if (room == altarRoom)
                {
                    Spawn(altarPrefab, room.Center + new Vector3(0f, 0f, room.Size.y / 2f - 1.8f), Quaternion.Euler(0f, 180f, 0f));
                }

                if (room.Kind == RoomKind.Boss || room.Kind == RoomKind.Start) continue;

                int urns = 2 + rng.Next(3);
                for (int i = 0; i < urns; i++) SpawnBreakable(Breakable.Kind.Urn, WallSpot(room, rng));
                if (layout.Depth == 2 && room.Kind == RoomKind.Combat)
                    for (int i = 0; i < 2; i++) SpawnBreakable(Breakable.Kind.SoulLantern, WallSpot(room, rng));
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

        private bool bonesHooked;
        private readonly System.Collections.Generic.HashSet<ulong> pileFor = new System.Collections.Generic.HashSet<ulong>();

        [Rpc(SendTo.ClientsAndHost)]
        private void FrozenFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Frost, at, 1f, 0.8f);
            Art.Vfx.IceSpikes(at - Vector3.up * 0.9f, 0.9f, 8);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.8f, "FROZEN", new Color(0.6f, 0.9f, 1f));
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
            float wraith = wraithPrefab != null ? 0.05f + 0.05f * depth : 0f;
            float r0 = UnityEngine.Random.value;
            if (r0 < wraith) return wraithPrefab;

            float roll = UnityEngine.Random.value;
            if ((roll -= brute) < 0f) { hasBrute = true; return brutePrefab; }
            if ((roll -= necro) < 0f) { hasNecro = true; return necromancerPrefab; }
            if ((roll -= ghoul) < 0f) { pack = 2; return ghoulPrefab; }
            if ((roll -= archer) < 0f) return archerPrefab;
            // The afflicters: plague from the first floor, frost and hexes deeper down.
            float plague = plaguebearerPrefab != null ? 0.06f + 0.02f * depth : 0f;
            float frost = frostWightPrefab != null && depth >= 1 ? 0.07f + 0.02f * depth : 0f;
            float hex = hexerPrefab != null && depth >= 1 ? 0.04f + 0.02f * depth : 0f;
            if ((roll -= plague) < 0f) return plaguebearerPrefab;
            if ((roll -= frost) < 0f) return frostWightPrefab;
            if ((roll -= hex) < 0f) return hexerPrefab;
            float shield = shieldbearerPrefab != null && depth >= 1 ? 0.1f + 0.03f * depth : shieldbearerPrefab != null ? 0.05f : 0f;
            if ((roll -= shield) < 0f) return shieldbearerPrefab;
            return skeletonPrefab;
        }

        // ------------------------------------------------------------------
        // Run state (server)
        // ------------------------------------------------------------------

        private void Update()
        {
            UpdateTideVisual();
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
                        else if (now >= PhaseEndsAt.Value) ServerBoard();
                    }
                    else if (PhaseEndsAt.Value > 0)
                    {
                        PhaseEndsAt.Value = 0;
                    }
                    break;

                case RunPhase.Exploring:
                    if (Layout == null) break;
                    TickEncounters();
                    if (PartyWiped())
                    {
                        EnterPhase(RunPhase.Wiped, WipeDelay);
                    }
                    else if (bossSeen && (boss == null || boss.IsDead))
                    {
                        ContractOnFloorCleared();
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
                        if (Phase.Value == RunPhase.FloorCleared)
                        {
                            NextDepth.Value = Floor.Value.y + 1;
                            EnterLobby(keepRun: true);
                        }
                        else EnterLobby();
                    }
                    break;
            }
        }

        private void EnterPhase(RunPhase phase, float delay)
        {
            if (phase == RunPhase.Wiped || phase == RunPhase.RunComplete) RunEndedAt.Value = NetworkManager.ServerTime.Time;
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
