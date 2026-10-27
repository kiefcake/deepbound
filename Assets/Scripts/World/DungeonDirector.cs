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
        /// <summary>
        /// Ten floors in five acts of two (one theme each): the first floor of an act ends at a Warden,
        /// the second at the act's own boss; the last act is the Abyss, and the Unsung waits on floor 10.
        /// </summary>
        public const int FloorsPerRun = 10;
        public const int FinalDepth = FloorsPerRun - 1;
        public const int ActCount = 5;
        /// <summary>The first floor of the Abyss act (floors 9 and 10).</summary>
        public const int AbyssDepth = 8;

        /// <summary>Which act (and theme) a floor belongs to: 0 Ossuary, 1 Drowned Vaults, 2 Hollow Throne, 3 Black Cathedral, 4 Abyss.</summary>
        public static int Act(int depth) => Mathf.Clamp(Mathf.Max(0, depth) / 2, 0, ActCount - 1);
        /// <summary>The first floor of an act ends at a Warden; the second at the act's boss.</summary>
        public static bool IsWardenFloor(int depth) => depth % 2 == 0;
        /// <summary>
        /// How deep a floor is for scaling (health, damage, gold, traps): half a step per floor, so ten
        /// floors climb about as far as the old five did, and nothing runs away on floor 10.
        /// </summary>
        public static float Tier(int depth) => Mathf.Max(0, depth) * 0.5f;

        /// <summary>Seal fragments the party has taken from champions this run (bit per act 0-3).</summary>
        public readonly NetworkVariable<byte> Seals = new NetworkVariable<byte>();
        public const byte AllSeals = 0b1111;
        public const int SealTotal = 4;
        public int SealCount => (Seals.Value & 1) + ((Seals.Value >> 1) & 1) + ((Seals.Value >> 2) & 1) + ((Seals.Value >> 3) & 1);
        /// <summary>Each Seal left unbroken makes the Unsung this much tougher.</summary>
        public const float UnsungPerMissingSeal = 0.15f;

        /// <summary>Tests: spawn an Entling at a spot, scaled for this floor.</summary>
        public EnemyBrain ServerDebugSpawnEntling(Vector3 at)
        {
            if (entlingPrefab == null) return null;
            if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
            var obj = Spawn(entlingPrefab, at, Quaternion.identity);
            var b = obj.GetComponent<EnemyBrain>();
            b.ServerScale(Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count), Layout != null ? Layout.Depth : 0);
            spawned.Add(obj);
            return b;
        }

        /// <summary>Server: a champion fell on floor <paramref name="depth"/>; each of the first four acts carries a fragment of the Seal.</summary>
        public void ServerSealFound(int depth)
        {
            int act = Act(depth);
            if (!IsServer || depth < 0 || act > 3) return;
            byte bit = (byte)(1 << act);
            if ((Seals.Value & bit) != 0) return;
            Seals.Value |= bit;
            SealRpc(SealCount);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SealRpc(int count)
        {
            UI.GameHud.ShowTitleCard("A FRAGMENT OF THE SEAL  (" + count + " / " + SealTotal + ")", count >= SealTotal ? "All four. Whatever sleeps in the Abyss will wake weak." : "The champion carried it without knowing. Each one weakens what waits at the bottom");
            Art.Sfx.Play2D(Art.Sound.Relic, 0.9f);
            Art.Sfx.Play2D(Art.Sound.Summon, 0.5f);
        }
        // Long enough to read the run summary before returning to camp.
        private const float DescendDelay = 8f, WipeDelay = 14f, CompleteDelay = 32f, BoardingDelay = 3f;
        private const int LobbyDepth = -1;

        [SerializeField] private NetworkObject skeletonPrefab;
        [SerializeField] private NetworkObject archerPrefab;
        [SerializeField] private NetworkObject ghoulPrefab;
        [SerializeField] private NetworkObject brutePrefab;
        [SerializeField] private NetworkObject necromancerPrefab;
        [SerializeField] private NetworkObject wraithPrefab;
        [SerializeField] private NetworkObject shieldbearerPrefab;
        /// <summary>One Warden per act, at the end of the act's first floor.</summary>
        [SerializeField] private NetworkObject[] wardenPrefabs;
        /// <summary>Entlings: from floor 3 down, they root you to the spot until killed.</summary>
        [SerializeField] private NetworkObject entlingPrefab;
        /// <summary>Orbweavers: from the first act's second floor, they conjure bullet-pattern orbs.</summary>
        [SerializeField] private NetworkObject orbweaverPrefab;
        [SerializeField] private NetworkObject mimicPrefab;
        [SerializeField] private NetworkObject hoarderPrefab;
        [SerializeField] private NetworkObject gargoylePrefab;
        [SerializeField] private NetworkObject mournerPrefab;
        [SerializeField] private NetworkObject reliquaryPrefab;
        [SerializeField] private NetworkObject lorePagePrefab;

        /// <summary>Server (tests): a Mourner here, facing <paramref name="face"/>.</summary>
        public EnemyBrain ServerDebugSpawnMourner(Vector3 at, Vector3 face)
        {
            if (mournerPrefab == null) return null;
            // Onto the navmesh, or it spawns without an agent and cannot move at all.
            if (NavMesh.SamplePosition(at, out var onMesh, 4f, NavMesh.AllAreas)) at = onMesh.position;
            var obj = Spawn(mournerPrefab, at, Quaternion.LookRotation(face));
            var b = obj.GetComponent<EnemyBrain>();
            b.ServerScale(1, 0);
            return b;
        }
        [SerializeField] private NetworkObject plaguebearerPrefab, frostWightPrefab, hexerPrefab;
        /// <summary>The Black Cathedral's choristers (act 4 on, the Abyss too).</summary>
        [SerializeField] private NetworkObject acolytePrefab;
        [SerializeField] private NetworkObject shaftCatPrefab;

        /// <summary>Tests: the cat, here.</summary>
        public ShaftCat ServerDebugShaftCat(Vector3 at) => shaftCatPrefab == null ? null : Spawn(shaftCatPrefab, at, Quaternion.identity).GetComponent<ShaftCat>();
        /// <summary>The Abyss's floating eyes (the last act).</summary>
        [SerializeField] private NetworkObject abyssEyePrefab;
        /// <summary>Bone Pipers (2026-10-21): from the first act's second floor, they play bullet patterns.</summary>
        [SerializeField] private NetworkObject bonePiperPrefab;
        [SerializeField] private NetworkObject cantorPrefab;
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

        [SerializeField] private NetworkObject arenaEnginePrefab;
        private float nextSnuff;

        /// <summary>Server: the arena's quirk (2026-10-20): healing pylons, ballistas, a chandelier, sanctum braziers.</summary>
        private void SpawnArenaQuirk(DungeonLayout layout, int players)
        {
            var room = layout.BossRoom;
            if (room == null) return;
            var c = room.Center;
            int entry = 0;
            for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
            var toDoor = new Vector3(DungeonLayout.Dir[entry].x, 0f, DungeonLayout.Dir[entry].y);
            var across = new Vector3(-toDoor.z, 0f, toDoor.x);
            switch (room.Quirk)
            {
                case BossQuirk.Pylons when breakablePrefab != null:
                {
                    int n = players >= 3 ? 4 : 3;
                    for (int i = 0; i < n; i++)
                    {
                        float a = (i * 360f / n + 30f) * Mathf.Deg2Rad;
                        var at = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 11.5f;
                        if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
                        SpawnBreakable(Breakable.Kind.HealPylon, at);
                    }
                    break;
                }
                case BossQuirk.Ballistas when arenaEnginePrefab != null:
                    foreach (float s in new[] { -1f, 1f })
                        SpawnEngine(ArenaEngine.Kind.Ballista, c + toDoor * 11f + across * (s * 11f), c, Vector3.zero);
                    break;
                case BossQuirk.Chandelier when arenaEnginePrefab != null:
                    SpawnEngine(ArenaEngine.Kind.Winch, c + across * (room.Size.x / 2f - 1.4f), c, c);
                    break;
                case BossQuirk.Sanctum when arenaEnginePrefab != null:
                    foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
                        SpawnEngine(ArenaEngine.Kind.Brazier, c + new Vector3(sx * 10.5f, 0f, sz * 10.5f), c, Vector3.zero);
                    break;
            }
        }

        private ArenaEngine SpawnEngine(ArenaEngine.Kind kind, Vector3 at, Vector3 face, Vector3 target)
        {
            if (NavMesh.SamplePosition(at, out var hit, 2.5f, NavMesh.AllAreas)) at = hit.position;
            var look = face - at;
            look.y = 0f;
            var obj = Instantiate(arenaEnginePrefab, at, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);
            var e = obj.GetComponent<ArenaEngine>();
            obj.Spawn(destroyWithScene: true);
            e.Type.Value = kind;
            e.Target.Value = target;
            spawned.Add(obj);
            return e;
        }

        /// <summary>Tests: a healing pylon at a spot.</summary>
        public void ServerDebugPylon(Vector3 at) { if (breakablePrefab != null) SpawnBreakable(Breakable.Kind.HealPylon, at); }

        /// <summary>Tests: an arena device at a spot.</summary>
        public ArenaEngine ServerDebugEngine(ArenaEngine.Kind kind, Vector3 at, Vector3 target) => arenaEnginePrefab != null ? SpawnEngine(kind, at, at + Vector3.forward, target) : null;

        /// <summary>Server: the sanctum's boss puts out a burning brazier every so often.</summary>
        private void TickSanctum()
        {
            if (boss == null || boss.IsDead || boss.Dormant || Time.time < nextSnuff) return;
            nextSnuff = Time.time + 22f;
            var lit = ArenaEngine.All.FindAll(e => e != null && e.Type.Value == ArenaEngine.Kind.Brazier && e.Lit.Value);
            if (lit.Count > 0) lit[UnityEngine.Random.Range(0, lit.Count)].ServerSnuff();
        }

        /// <summary>A walk of 3-4 linked combat rooms (not the ambush room), as navmesh points at their centres, or null.</summary>
        private System.Collections.Generic.List<Vector3> PatrolRoute(DungeonLayout layout, System.Random rng)
        {
            bool Walkable(DungeonLayout.Room r) => r.Kind == RoomKind.Combat && !IsAmbushRoom(r) && r.Feature != RoomFeature.Chasm;
            var pool = layout.Rooms.FindAll(r => Walkable(r) && r.Id >= 2);
            if (pool.Count == 0) return null;
            var at = pool[rng.Next(pool.Count)];
            var rooms = new System.Collections.Generic.List<DungeonLayout.Room> { at };
            for (int step = 0; step < 3; step++)
            {
                var next = new System.Collections.Generic.List<DungeonLayout.Room>();
                foreach (var link in layout.Links)
                {
                    var other = link.A == at.Id ? layout.Rooms[link.B] : link.B == at.Id ? layout.Rooms[link.A] : null;
                    if (other != null && Walkable(other) && !rooms.Contains(other)) next.Add(other);
                }
                if (next.Count == 0) break;
                at = next[rng.Next(next.Count)];
                rooms.Add(at);
            }
            if (rooms.Count < 2) return null;
            var route = new System.Collections.Generic.List<Vector3>();
            foreach (var r in rooms)
            {
                var c = r.Center + Vector3.up * r.RaisedAt(r.Center);
                if (NavMesh.SamplePosition(c, out var hit, 6f, NavMesh.AllAreas)) route.Add(hit.position);
            }
            return route.Count >= 2 ? route : null;
        }

        /// <summary>Server: a patrol of skeletons led by an elite, walking <paramref name="route"/> back and forth.</summary>
        public System.Collections.Generic.List<EnemyBrain> ServerSpawnPatrol(System.Collections.Generic.List<Vector3> route, int count)
        {
            var band = new System.Collections.Generic.List<EnemyBrain>();
            if (Layout == null || skeletonPrefab == null || route == null || route.Count < 2) return band;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            var points = route.ToArray();
            var heading = points[1] - points[0];
            heading.y = 0f;
            var rot = heading.sqrMagnitude > 0.01f ? Quaternion.LookRotation(heading) : Quaternion.identity;
            EnemyBrain leader = null;
            for (int i = 0; i < count; i++)
            {
                // A loose column: the leader in front, the rest two by two behind it.
                var offset = i == 0 ? Vector3.zero : rot * new Vector3(((i - 1) % 2 == 0 ? -1f : 1f) * 1.1f, 0f, -1.6f * ((i + 1) / 2));
                var at = points[0] + offset;
                if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
                var obj = Spawn(skeletonPrefab, at, rot);
                var brain = obj.GetComponent<EnemyBrain>();
                brain.ServerScale(players, Layout.Depth);
                if (i == 0) { brain.ServerMakeElite(); leader = brain; }
                brain.ServerSetPatrol(points, i == 0 ? null : leader, offset);
                spawned.Add(obj);
                band.Add(brain);
            }
            return band;
        }

        /// <summary>Server: like <see cref="ServerSpawnPack"/>, returning the brains it raised (the proving bell waits on them).</summary>
        public System.Collections.Generic.List<EnemyBrain> ServerSpawnPackList(Vector3 around, int count, bool elite)
        {
            int before = spawned.Count;
            ServerSpawnPack(around, count, elite);
            var list = new System.Collections.Generic.List<EnemyBrain>();
            for (int i = before; i < spawned.Count; i++) if (spawned[i] != null && spawned[i].TryGetComponent<EnemyBrain>(out var b)) list.Add(b);
            return list;
        }

        /// <summary>Server: a pack of the floor's dead around a point (the Wheel's cruel turn).</summary>
        public void ServerSpawnPack(Vector3 around, int count, bool elite, bool champion = false)
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
                if (champion && i == 0) brain.ServerMakeChampion();
                else if (elite && i == 0) brain.ServerMakeElite();
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
        [SerializeField] private NetworkObject phoenixPrefab;

        /// <summary>Server: the Tome of the Phoenix (one bird per player at a time). False if it cannot come.</summary>
        public bool ServerSummonPhoenix(Progression.PlayerProgress who)
        {
            if (!IsServer || phoenixPrefab == null || who == null) return false;
            foreach (var p in Player.Phoenix.All) if (p != null && p.Summoner.Value == who.NetworkObjectId) return false;
            var obj = Instantiate(phoenixPrefab, who.transform.position + Vector3.up * Player.Phoenix.OrbitHeight, Quaternion.identity);
            obj.Spawn(destroyWithScene: true);
            obj.GetComponent<Player.Phoenix>().ServerInit(who);
            return true;
        }
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

        /// <summary>Thermal Shock: frost and fire meet in a crack of steam, pale blue shards and orange sparks.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ThermalShockFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Shatter, at, 1f, 0.85f);
            Art.Sfx.Play(Art.Sound.Fire, at, 0.6f, 1.5f);
            Art.Vfx.Ring(at + Vector3.down * 0.8f, 2.2f, new Color(1f, 0.55f, 0.85f), 0.45f);
            Art.Vfx.Embers(at, 10, new Color(0.65f, 0.88f, 1f), 6f, 0.07f, 0.6f);
            Art.Vfx.Embers(at, 10, new Color(1f, 0.55f, 0.2f), 6f, 0.06f, 0.6f);
            Art.Vfx.Smoke(at, 3, 1.2f, 1f);
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
        public void ServerDropEmbers(Vector3 at, int amount) => ServerDropLoot(at, LootKind.Embers, Mathf.RoundToInt(amount * OmenEmbers * EventEmbers));

        /// <summary>Server: one loot drop of a given kind at a point.</summary>
        /// <summary>
        /// Server: one drop. It lands on walkable floor on this side of any wall between it and
        /// <paramref name="from"/> (its source, default: where it was asked for); <paramref name="settle"/> false
        /// leaves it exactly where it was put (a niche's plinth).
        /// </summary>
        public void ServerDropLoot(Vector3 at, LootKind kind, int amount, Vector3? from = null, bool settle = true)
        {
            if (lootPrefab == null) return;
            var place = settle ? DropSpots.Settle(from ?? at, at) : at + Vector3.up * 0.05f;
            var obj = Instantiate(lootPrefab, place, Quaternion.identity);
            var drop = obj.GetComponent<LootDrop>();
            drop.Kind.Value = kind;
            drop.Amount.Value = amount;
            obj.Spawn(destroyWithScene: true);
        }
        [SerializeField] private NetworkObject trapPrefab;
        [SerializeField] private NetworkObject lootPrefab;
        /// <summary>One boss per floor, by depth: the Gravewarden, the Tide Lich, the Hollow King, the Pale Cardinal.</summary>
        [SerializeField] private NetworkObject[] bossPrefabs;

        /// <summary>The rank-and-file prefabs (not bosses or Wardens), as network objects.</summary>
        private System.Collections.Generic.IEnumerable<NetworkObject> AllEnemyPrefabNetworkObjects() => new[] { skeletonPrefab, archerPrefab, ghoulPrefab, brutePrefab, necromancerPrefab, wraithPrefab,
                shieldbearerPrefab, hoarderPrefab, gargoylePrefab, plaguebearerPrefab, frostWightPrefab, hexerPrefab, entlingPrefab, orbweaverPrefab, acolytePrefab, abyssEyePrefab, bonePiperPrefab, cantorPrefab };

        /// <summary>Every enemy prefab the director can spawn (balance report).</summary>
        public System.Collections.Generic.IEnumerable<EnemyBrain> AllEnemyPrefabs()
        {
            var all = new System.Collections.Generic.List<NetworkObject> { skeletonPrefab, archerPrefab, ghoulPrefab, brutePrefab, necromancerPrefab, wraithPrefab,
                shieldbearerPrefab, mimicPrefab, hoarderPrefab, gargoylePrefab, plaguebearerPrefab, frostWightPrefab, hexerPrefab, mournerPrefab, entlingPrefab, orbweaverPrefab, acolytePrefab, abyssEyePrefab, bonePiperPrefab, cantorPrefab };
            if (bossPrefabs != null) all.AddRange(bossPrefabs);
            if (wardenPrefabs != null) all.AddRange(wardenPrefabs);
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

        /// <summary>
        /// The party's loot rule, set by the host at camp: shared (the default; gold and embers picked up
        /// are split evenly across the party) or finders keepers.
        /// </summary>
        public readonly NetworkVariable<bool> FindersKeepers = new NetworkVariable<bool>();

        /// <summary>
        /// The Reliquary reached the waystone this floor: the floor's guardian (boss or Warden) wakes
        /// HALLOWED, at <see cref="HallowedHealth"/> of its health and taking more damage all fight.
        /// </summary>
        public readonly NetworkVariable<bool> Hallowed = new NetworkVariable<bool>();
        public const float HallowedHealth = 0.75f, HallowedVulnerability = 1.1f;
        public static bool ShareLoot => Instance == null || !Instance.FindersKeepers.Value;

        /// <summary>Host → server: change the loot rule (at camp).</summary>
        [Rpc(SendTo.Server)]
        public void SetLootRuleRpc(bool findersKeepers)
        {
            if (Phase.Value != RunPhase.Lobby) return;
            FindersKeepers.Value = findersKeepers;
        }

        /// <summary>Host → server: change difficulty. Only honoured in the lobby.</summary>
        [Rpc(SendTo.Server)]
        public void SetDifficultyRpc(byte index)
        {
            if (Phase.Value != RunPhase.Lobby || RunInProgress) return;
            int i = Mathf.Clamp(index, 0, Difficulty.Settings.Length - 1);
            if (i >= Difficulty.TormentIndex && !Difficulty.TormentUnlocked) i = Difficulty.TormentIndex - 1;
            DifficultyIndex.Value = (byte)i;
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
            if (IsServer) Player.PlayerVoice.ServerHeard += ServerNoise;
            if (IsServer) Combat.Health.ServerAnyDamaged += ServerHitMark;
            Instance = this;
            Floor.OnValueChanged += (_, now) => BuildLocal(now);
            Phase.OnValueChanged += (_, now) =>
            {
                if (now == RunPhase.FloorCleared && Layout != null && built != null) Art.ExitPortal.Spawn(ExitPosition, built.transform);
            };

            if (IsServer) EnterLobby();
            else BuildLocal(Floor.Value);
        }

        /// <summary>Server: a noise (a shout, the Reliquary's hymn) wakes the dead within earshot, statues included. Bosses ignore it.</summary>
        public void ServerNoise(Combat.Health who, Vector3 at, float radius)
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
            if (woke > 0) Debug.Log("[Noise] at " + at.ToString("F0") + " alerted " + woke);
        }

        /// <summary>Server: a player's hit landed on an enemy - tell them, so their crosshair can say so.</summary>
        private static void ServerHitMark(Combat.Health target, float lost, ulong attackerId)
        {
            if (target == null || target.Team != Combat.Team.Enemies || attackerId == ulong.MaxValue) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) || obj == null) return;
            if (obj.TryGetComponent<PlayerProgress>(out var who)) who.HitMarkRpc(target.Current.Value <= 0f, lost >= target.Max.Value * 0.2f);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Combat.Health.ServerAnyDamaged -= ServerHitMark;
            if (IsServer) Player.PlayerVoice.ServerHeard -= ServerNoise;
            if (Instance == this) Instance = null;
            if (built != null) Destroy(built);
            Layout = null;
        }

        // ------------------------------------------------------------------
        // Transitions (server)
        // ------------------------------------------------------------------

        /// <param name="keepRun">True between floors: the party keeps everything and heals up.</param>
        /// <summary>Server (developer menu): back to camp, keeping the run.</summary>
        public void ServerDebugCamp() { if (IsServer) EnterLobby(keepRun: true); }

        /// <summary>Server (developer menu): spawn one of the rank and file by name at a spot.</summary>
        public EnemyBrain ServerDebugSpawn(string kind, Vector3 at, bool elite)
        {
            if (!IsServer) return null;
            NetworkObject prefab = kind switch
            {
                "skeleton" => skeletonPrefab, "archer" => archerPrefab, "ghoul" => ghoulPrefab, "brute" => brutePrefab,
                "necromancer" => necromancerPrefab, "wraith" => wraithPrefab, "shieldbearer" => shieldbearerPrefab, "gargoyle" => gargoylePrefab,
                "plaguebearer" => plaguebearerPrefab, "frostwight" => frostWightPrefab, "hexer" => hexerPrefab, "mourner" => mournerPrefab,
                "entling" => entlingPrefab, "orbweaver" => orbweaverPrefab, "mimic" => mimicPrefab, "hoarder" => hoarderPrefab, "acolyte" => acolytePrefab, "eye" => abyssEyePrefab, "piper" => bonePiperPrefab, "cantor" => cantorPrefab, _ => null,
            };
            if (prefab == null) return null;
            if (NavMesh.SamplePosition(at, out var hit, 4f, NavMesh.AllAreas)) at = hit.position;
            var obj = Spawn(prefab, at, Quaternion.identity);
            var b = obj.GetComponent<EnemyBrain>();
            b.ServerScale(Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count), Layout != null ? Layout.Depth : 0);
            if (elite) b.ServerMakeElite();
            b.ServerAggroNearest();
            spawned.Add(obj);
            return b;
        }

        public static readonly string[] DebugSpawnKinds = { "skeleton", "archer", "ghoul", "brute", "necromancer", "wraith", "shieldbearer", "gargoyle", "plaguebearer", "frostwight", "hexer", "mourner", "entling", "mimic", "hoarder", "orbweaver", "acolyte", "eye", "piper", "cantor" };

        private void EnterLobby(bool keepRun = false)
        {
            ClearSpawned();
            ClearSettledContract();
            ApplyOmen(false);
            ClearFloorEvent();
            if (!keepRun) { NextDepth.Value = 0; Seals.Value = 0; }
            foreach (var p in PlayerLife.All) if (p != null) p.Seat.Value = -1;
            if (CarriageRide.Instance != null) CarriageRide.Instance.Park();
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
                if (p.TryGetComponent<PlayerProgress>(out var blessed)) { blessed.ServerClearBlessing(); blessed.ServerSetMirrorCursed(false); }
                if (keepRun && p.TryGetComponent<PlayerProgress>(out var sore)) sore.ServerEaseGrudges();
                if (!keepRun && p.TryGetComponent<PlayerProgress>(out var progress) && progress.Stats != null) progress.ServerResetRun();
                p.ServerRevive(1f);
                p.Health.ServerSetFraction(1f);
            }
            Debug.Log("[Director] Lobby");
        }

        [SerializeField] private NetworkObject diceTablePrefab;
        [SerializeField] private NetworkObject catPrefab;
        [SerializeField] private NetworkObject slotMachinePrefab;
        [SerializeField] private NetworkObject blackjackPrefab;
        [SerializeField] private NetworkObject cofferPrefab;
        [SerializeField] private NetworkObject gongPrefab;
        [SerializeField] private NetworkObject shrinePrefab;
        [SerializeField] private NetworkObject rangeTargetPrefab;
        [SerializeField] private NetworkObject folkPrefab;

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
            Put(slotMachinePrefab, Lobby.SlotSpot, Lobby.TentFacing);
            Put(blackjackPrefab, Lobby.BlackjackSpot, Vector3.back);
            Put(catPrefab, Lobby.Origin + Lobby.S(0.6f, 0f, -8f), Vector3.forward);
            Put(gongPrefab, Lobby.GongSpot, Lobby.GongFacing);
            Put(shrinePrefab, Lobby.ShrineSpot, Vector3.left);
            Put(omenStonePrefab, Lobby.OmenSpot, Vector3.left);
            // The camp's people get bodies that can be struck (and remember it).
            if (folkPrefab != null)
                foreach (var (id, at) in Lobby.FolkBodies)
                {
                    var folk = Instantiate(folkPrefab, at, Quaternion.identity);
                    folk.GetComponent<CampFolk>().Who.Value = (byte)id;
                    folk.Spawn(destroyWithScene: true);
                    spawned.Add(folk);
                }
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
        public const float DepartSeconds = CarriageRide.RideSeconds;
        private float departAt = -1f;

        /// <summary>Server: the carriage goes. A beam of light takes the party first (a short cutscene), then the floor starts.</summary>
        public void ServerBoard()
        {
            if (!IsServer || departAt > 0f) return;
            departAt = Time.time + DepartSeconds;
            DepartRpc();
        }

        private void ServerFinishBoarding()
        {
            departAt = -1f;
            if (RunInProgress) StartFloor(NextDepth.Value);
            else ServerStartRun();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DepartRpc()
        {
            // The carriage rolls out through the gate with the party on its tailgate; the screen fades as the wood closes in.
            if (CarriageRide.Instance != null) CarriageRide.Instance.Depart();
            Art.Sfx.Play2D(Art.Sound.Creak, 0.9f);
            Art.Sfx.Play2D(Art.Sound.Step, 0.6f);
            UI.GameHud.BeginDeparture(DepartSeconds);
        }

        /// <summary>Server: board the carriage and start floor one. Also used by the smoke test.</summary>
        public void ServerStartRun()
        {
            if (!IsServer) return;
            RunStartedAt.Value = NetworkManager.ServerTime.Time;
            Classes.Combos.ResetRun();
            // A lone delver has nobody to drag them up (2026-10-23, the solo loop): one set of Smelling Salts to start.
            if (Player.PlayerLife.All.Count == 1)
                foreach (var pl in Player.PlayerLife.All)
                    if (pl != null && pl.TryGetComponent<Progression.PlayerProgress>(out var solo) && !solo.Salts.Value)
                    {
                        solo.Salts.Value = true;
                        ServerFeedAll("Corwin presses a twist of Smelling Salts into your hand: \"Nobody to carry you out. Take these.\"", new Color(0.85f, 0.8f, 0.6f));
                    }
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

            int seed = FloorSeed(depth);
            Phase.Value = RunPhase.Exploring;
            PhaseEndsAt.Value = 0;
            Floor.Value = new Vector2Int(seed, depth);
            foreach (var p in PlayerLife.All) if (p != null) p.Seat.Value = -1;
            Hallowed.Value = false;
            ResetTally();
            ResetRoomClears();
            RollFloorEvent(depth);

            // NetworkVariable callbacks do not fire for the writer's own change on every
            // NGO version, so build explicitly; BuildLocal ignores a repeat.
            BuildLocal(Floor.Value);
            // The Tallyman's clock, sized to the floor just built.
            TallyArriveAfter.Value = TallyBaseSeconds + TallyPerRoom * Layout.Rooms.Count + (AnyoneHolds(Relic.TallymansLedger) ? TallyLedgerDelay : 0f)
                - (DifficultyIndex.Value >= Difficulty.TormentIndex ? Difficulty.TormentTallySooner : 0f);

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

            if (depth == AbyssDepth) AbyssArrivalRpc(SealCount);
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
            foreach (var o in FindObjectsByType<OathPlates>(FindObjectsSortMode.None))
            {
                if (o.NetworkObject.IsSpawned) o.NetworkObject.Despawn();
            }
            foreach (var t in FindObjectsByType<Tallyman>(FindObjectsSortMode.None))
            {
                if (t.NetworkObject.IsSpawned) t.NetworkObject.Despawn();
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
            Art.TheSilence.Cancel();

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
            // No little islands on prop tops (tables, crates, plinths): an enemy put there at spawn could never
            // walk off, and paths begun beside one started on it (2026-10-22, autoplay: enemies "left" no one could reach).
            surface.minRegionArea = 6f;
            surface.BuildNavMesh();
        }

        private void SpawnEnemies(DungeonLayout layout)
        {
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);

            float eliteChance = Mathf.Min(0.6f, Difficulty.EliteChance(layout.Depth, players) * OmenElites * EventElites);
            var theme = DungeonTheme.ForDepth(layout.Depth);

            foreach (var room in layout.Rooms)
            {
                if (room.EnemySpawns.Count == 0 || IsAmbushRoom(room)) continue;

                // More players, more bodies: scale the room's head count, cycling through its
                // spawn points with a little jitter for the extras.
                int count = Mathf.Max(2, Mathf.RoundToInt(room.EnemySpawns.Count * Difficulty.RoomCrowd * Difficulty.SpawnCount(players) * Difficulty.Level.SpawnMultiplier * OmenCrowd * EventCrowd));
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
                        NoteSpawnRoom(brain, room);
                    }
                }
            }

            // One champion per floor: a named elite in a combat room, guarding a hoard.
            var candidates = new System.Collections.Generic.List<EnemyBrain>();
            foreach (var o in spawned)
                if (o != null && o.TryGetComponent<EnemyBrain>(out var cb) && !(cb is BossBrain) && !(cb is HoarderBrain) && !(cb is WraithBrain) && !(cb is GargoyleBrain) && !(cb is MournerBrain) &&
                    layout.RoomAt(o.transform.position)?.Kind == RoomKind.Combat) candidates.Add(cb);
            if (candidates.Count > 0) candidates[UnityEngine.Random.Range(0, candidates.Count)].ServerMakeChampion();

            // From the second floor, a patrol or two walks between the combat rooms (never in tests).
            if (!TestMode && layout.Depth >= 1)
            {
                var prng = new System.Random(layout.Seed ^ 0x9a7e);
                int patrols = (prng.NextDouble() < 0.75 ? 1 : 0) + (layout.Depth >= 5 && prng.NextDouble() < 0.5 ? 1 : 0) + EventPatrols;
                for (int i = 0; i < patrols; i++)
                {
                    var route = PatrolRoute(layout, prng);
                    if (route != null) ServerSpawnPatrol(route, 3 + (players > 2 ? 1 : 0));
                }
            }

            // From the second floor down, some of the statues by the walls are not statues.
            if (gargoylePrefab != null && Act(layout.Depth) >= 1)
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

            // From the second floor, a Mourner or two waits by a wall, weeping. Never in tests (it hunts whoever looks away).
            if (mournerPrefab != null && Act(layout.Depth) >= 1 && !TestMode)
            {
                var mrng = new System.Random(layout.Seed ^ 0x3e0a);
                int placed = 0;
                foreach (var room in layout.Rooms)
                {
                    if (placed >= 2) break;
                    if (room.Kind != RoomKind.Combat || IsAmbushRoom(room) || mrng.NextDouble() > 0.3) continue;
                    var mat = WallSpot(room, mrng);
                    var mface = room.Center - mat;
                    mface.y = 0f;
                    if (NavMesh.SamplePosition(mat, out var mhit, 2f, NavMesh.AllAreas)) mat = mhit.position;
                    var mobj = Spawn(mournerPrefab, mat, Quaternion.LookRotation(mface));
                    mobj.GetComponent<EnemyBrain>().ServerScale(players, layout.Depth);
                    spawned.Add(mobj);
                    placed++;
                }
            }

            // The peddler, if one set up shop on this floor, can be struck (and remembers it) too.
            if (folkPrefab != null && layout.PeddlerSpot.HasValue)
            {
                var pd = Instantiate(folkPrefab, layout.PeddlerSpot.Value, Quaternion.identity);
                pd.GetComponent<CampFolk>().Who.Value = (byte)FolkId.Peddler;
                pd.Spawn(destroyWithScene: true);
                spawned.Add(pd);
            }

            // Now and then the castle cat is down here, asleep in a quiet side room (never in tests).
            if (shaftCatPrefab != null && !TestMode && layout.Depth >= 1 && UnityEngine.Random.value < ShaftCat.SpawnChance)
            {
                var quiet = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side && !r.Special && !r.Peaceful && r.Feature == RoomFeature.None);
                if (quiet.Count > 0)
                {
                    var crng = new System.Random(layout.Seed ^ 0xca7);
                    var room = quiet[crng.Next(quiet.Count)];
                    var cat = room.Center + new Vector3((float)crng.NextDouble() * 4f - 2f, 0f, (float)crng.NextDouble() * 4f - 2f);
                    if (NavMesh.SamplePosition(cat, out var chit, 2f, NavMesh.AllAreas)) cat = chit.position;
                    spawned.Add(Spawn(shaftCatPrefab, cat, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)));
                }
            }

            // Every floor: one journal page on a lectern, by a side-room wall (a combat room's if there are none).
            if (lorePagePrefab != null)
            {
                var prng = new System.Random(layout.Seed ^ 0x10e);
                var pageRooms = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side && r.Feature != RoomFeature.Vault && r.Feature != RoomFeature.Fountain);
                if (pageRooms.Count == 0) pageRooms = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && !IsAmbushRoom(r));
                if (pageRooms.Count > 0)
                {
                    var room = pageRooms[prng.Next(pageRooms.Count)];
                    var pat = WallSpot(room, prng);
                    var pface = room.Center - pat;
                    pface.y = 0f;
                    if (NavMesh.SamplePosition(pat, out var phit, 2f, NavMesh.AllAreas)) pat = phit.position;
                    var pobj = Instantiate(lorePagePrefab, pat, Quaternion.LookRotation(-pface));
                    pobj.GetComponent<LorePage>().Index.Value = (byte)UI.Journal.PickFor(Act(layout.Depth), prng);
                    pobj.Spawn(destroyWithScene: true);
                    spawned.Add(pobj);
                }
            }

            // Most floors: a Reliquary in a side room, to haul to the waystone (always in tests).
            if (reliquaryPrefab != null && (TestMode || UnityEngine.Random.value < 0.6f))
            {
                // A plain side room, at its centre; failing that, against the wall of one without a fountain or vault.
                var sides = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side && r.Feature == RoomFeature.None);
                bool atWall = sides.Count == 0;
                if (atWall) sides = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side && r.Feature != RoomFeature.Vault && r.Feature != RoomFeature.Fountain);
                if (atWall && sides.Count == 0) sides = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && !IsAmbushRoom(r));
                if (sides.Count > 0)
                {
                    var room = sides[UnityEngine.Random.Range(0, sides.Count)];
                    var rat = atWall ? WallSpot(room, new System.Random(layout.Seed ^ 0x71c)) : room.Center + new Vector3(0.8f, 0f, 0.8f);
                    if (NavMesh.SamplePosition(rat, out var rhit, 3f, NavMesh.AllAreas)) rat = rhit.position;
                    Spawn(reliquaryPrefab, rat, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
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
            int act = Act(layout.Depth);
            var bossPrefab = IsWardenFloor(layout.Depth) && wardenPrefabs != null && act < wardenPrefabs.Length && wardenPrefabs[act] != null
                ? wardenPrefabs[act] : bossPrefabs[Mathf.Clamp(act, 0, bossPrefabs.Length - 1)];
            var bossObj = Instantiate(bossPrefab, spawn, Quaternion.LookRotation(layout.BossRoom.Center - spawn + Vector3.back * 0.01f));
            bossObj.Spawn(destroyWithScene: true);
            boss = bossObj.GetComponent<BossBrain>();
            // The Lich raises archers from the water; everyone else raises swordsmen. Wardens call their act's dead.
            if (boss is WardenBrain)
                boss.SetMinionPrefab(act == 1 && frostWightPrefab != null ? frostWightPrefab : act == 2 && shieldbearerPrefab != null ? shieldbearerPrefab
                    : act == 3 && wraithPrefab != null ? wraithPrefab : act == 4 && plaguebearerPrefab != null ? plaguebearerPrefab : skeletonPrefab);
            else
                boss.SetMinionPrefab(boss is TideLichBrain && archerPrefab != null ? archerPrefab
                    : (boss is CardinalBrain || boss is UnsungBrain) && wraithPrefab != null ? wraithPrefab : skeletonPrefab);
            boss.SetGuardPrefab(boss is UnsungBrain && mournerPrefab != null ? mournerPrefab : shieldbearerPrefab);
            boss.ServerScaleBoss(players, layout.Depth);
            // Every Seal the party failed to break makes the thing at the bottom stronger.
            if (boss is UnsungBrain)
            {
                int missing = SealTotal - SealCount;
                if (missing > 0) boss.Health.ServerSetMax(boss.Health.Max.Value * (1f + UnsungPerMissingSeal * missing), refill: true);
            }
            boss.Dormant = portcullisPrefab != null; // waits on its throne until the party arrives
            spawned.Add(bossObj);
            bossSeen = true;
            SpawnArenaQuirk(layout, players);
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
            if (altarPrefab != null && (TestMode || rng.NextDouble() < 0.5))
            {
                var combat = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat);
                if (combat.Count > 0) altarRoom = combat[rng.Next(combat.Count)];
            }

            DungeonLayout.Room wheelRoom = null;
            if (fateWheelPrefab != null && (rng.NextDouble() < 0.55 || TestMode))
            {
                var ambushRoom = portcullisPrefab != null ? PickAmbushRoom(layout) : null;
                var pool = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && r != altarRoom && r != ambushRoom && r.Feature != RoomFeature.Chasm && r.Feature != RoomFeature.Crypt);
                if (pool.Count > 0) wheelRoom = pool[rng.Next(pool.Count)];
            }

            foreach (var room in layout.Rooms)
            {
                if (room.Kind == RoomKind.Side && chestPrefab != null && !room.Special)
                {
                    int entry = 0;
                    for (int s = 0; s < 4; s++) if (room.Doors[s]) entry = s;
                    var back = -new Vector3(DungeonLayout.Dir[entry].x, 0f, DungeonLayout.Dir[entry].y);
                    // The back wall's distance depends on which way the room runs (it used to always use its depth).
                    float toBack = (entry == 0 || entry == 2 ? room.Size.y : room.Size.x) / 2f - 1.5f;
                    var chestRot = Quaternion.LookRotation(-back);
                    var chestAt = room.Center + back * toBack;
                    // Not in front of a hollow wall on the back wall: the chest sealed the niche once (the navmesh check).
                    int backSide = (entry + 2) % 4;
                    var backHole = room.HoleOn(backSide);
                    if (backHole != null)
                    {
                        bool alongX = backSide == 0 || backSide == 2;
                        float wallHalf = (alongX ? room.Size.x : room.Size.y) / 2f;
                        float clear = WallHole.Width / 2f + 1.8f;
                        float t = backHole.Along + (backHole.Along > 0f ? -clear : clear);
                        if (Mathf.Abs(t) > wallHalf - 1.2f) t = backHole.Along + (backHole.Along > 0f ? clear : -clear);
                        t = Mathf.Clamp(t, -wallHalf + 1.2f, wallHalf - 1.2f);
                        chestAt += alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t);
                    }
                    var chest = Spawn(chestPrefab, DropSpots.ClearForChest(chestAt, chestRot, room), chestRot);
                    // Some chests are hungry. Fewer on the first floor.
                    if (mimicPrefab != null && !TestMode && !room.Peaceful && rng.NextDouble() < (layout.Depth == 0 ? 0.12 : 0.2)) chest.GetComponent<Chest>().Mimic.Value = true;
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
                if (Act(layout.Depth) == 2 && room.Kind == RoomKind.Combat)
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
                // Rooms too (2026-10-20): a row of two or three pressure plates across a plain fight
                // room's floor, off the doorways, in about half of them. Lure the dead across.
                foreach (var room in layout.Rooms)
                {
                    if (TestMode || room.Kind != RoomKind.Combat || room.Peaceful) continue;
                    var f = room.Feature;
                    if (f != RoomFeature.None && f != RoomFeature.GreatHall && f != RoomFeature.Gallery && f != RoomFeature.Ritual && f != RoomFeature.Library) continue;
                    if (rng.NextDouble() > 0.55) continue;
                    var shelves = DungeonLayout.LibraryShelves(room);
                    int rows = room.Size.x * room.Size.y > 600f && rng.NextDouble() < 0.5 ? 2 : 1;
                    for (int r = 0; r < rows; r++)
                    {
                        bool alongX = rng.NextDouble() < 0.5;
                        var start = room.Center + new Vector3((float)(rng.NextDouble() - 0.5) * (room.Size.x - 9f), 0f, (float)(rng.NextDouble() - 0.5) * (room.Size.y - 9f));
                        int count = 2 + rng.Next(2);
                        for (int k = 0; k < count; k++)
                        {
                            var at = start + (alongX ? Vector3.right : Vector3.forward) * (k * 2.7f);
                            if (!room.Contains(at, -3f)) continue;
                            if (shelves.Exists(q => { var g = q; g.xMin -= 1f; g.yMin -= 1f; g.xMax += 1f; g.yMax += 1f; return g.Contains(new Vector2(at.x, at.z)); })) continue;
                            if (Vector3.Distance(new Vector3(at.x, 0f, at.z), new Vector3(room.Center.x, 0f, room.Center.z)) < 2.5f) continue;
                            if (!NavMesh.SamplePosition(at, out var hit, 1.2f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y) > 0.3f) continue;
                            Spawn(trapPrefab, hit.position + Vector3.up * 0.005f, Quaternion.identity);
                        }
                    }
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
            int act = Act(depth);
            float tier = Tier(depth);
            float brute = brutePrefab != null && !hasBrute ? 0.06f + 0.03f * tier : 0f;
            float necro = necromancerPrefab != null && !hasNecro && act >= 1 ? 0.06f + 0.03f * tier : 0f;
            float ghoul = ghoulPrefab != null ? 0.22f : 0f;
            float archer = archerPrefab != null ? theme.ArcherChance : 0f;
            float wraith = wraithPrefab != null ? 0.05f + 0.05f * tier : 0f;
            float r0 = UnityEngine.Random.value;
            if (r0 < wraith) return wraithPrefab;
            // Entlings come in from the second act, in pairs sometimes.
            float entling = entlingPrefab != null && act >= 1 ? 0.05f + 0.02f * tier : 0f;
            if (UnityEngine.Random.value < entling) { pack = UnityEngine.Random.value < 0.35f ? 2 : 1; return entlingPrefab; }

            float roll = UnityEngine.Random.value;
            if ((roll -= brute) < 0f) { hasBrute = true; return brutePrefab; }
            if ((roll -= necro) < 0f) { hasNecro = true; return necromancerPrefab; }
            if ((roll -= ghoul) < 0f) { pack = 2; return ghoulPrefab; }
            if ((roll -= archer) < 0f) return archerPrefab;
            // The afflicters: plague from the first floor, frost and hexes deeper down.
            float plague = plaguebearerPrefab != null ? 0.06f + 0.02f * tier : 0f;
            float frost = frostWightPrefab != null && act >= 1 ? 0.07f + 0.02f * tier : 0f;
            float hex = hexerPrefab != null && act >= 1 ? 0.04f + 0.02f * tier : 0f;
            float orb = orbweaverPrefab != null && depth >= 1 ? 0.04f + 0.015f * tier : 0f;
            if ((roll -= orb) < 0f) return orbweaverPrefab;
            float piper = bonePiperPrefab != null && depth >= 1 ? 0.04f + 0.012f * tier : 0f;
            if ((roll -= piper) < 0f) return bonePiperPrefab;
            // The Throne and below: Cantors hush the party's abilities.
            float cantor = cantorPrefab != null && act >= 2 ? 0.04f + 0.01f * tier : 0f;
            if ((roll -= cantor) < 0f) return cantorPrefab;
            // The Black Cathedral sings: choristers from act 4, warding the dead around them.
            float choir = acolytePrefab != null && act >= 3 ? 0.08f + 0.01f * tier : 0f;
            if ((roll -= choir) < 0f) return acolytePrefab;
            // The Abyss stares back.
            float eye = abyssEyePrefab != null && act >= 4 ? 0.12f : 0f;
            if ((roll -= eye) < 0f) return abyssEyePrefab;
            if ((roll -= plague) < 0f) return plaguebearerPrefab;
            if ((roll -= frost) < 0f) return frostWightPrefab;
            if ((roll -= hex) < 0f) return hexerPrefab;
            float shield = shieldbearerPrefab != null && act >= 1 ? 0.1f + 0.03f * tier : shieldbearerPrefab != null ? 0.05f : 0f;
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
                    if (departAt > 0f)
                    {
                        if (Time.time >= departAt) ServerFinishBoarding();
                        break;
                    }
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
                    TickTally(now);
                    if (PartyWiped())
                    {
                        EnterPhase(RunPhase.Wiped, WipeDelay);
                    }
                    else if (bossSeen && (boss == null || boss.IsDead))
                    {
                        ContractOnFloorCleared();
                        // No timer: a portal opens where the boss stood, and the party leaves when everyone steps in.
                        ExitFinal.Value = Floor.Value.y >= FinalDepth;
                        EnterPhase(RunPhase.FloorCleared, 0f);
                        phaseTimer = double.MaxValue;
                        PhaseEndsAt.Value = 0;
                        // The cleared floor is safe: pick everyone up.
                        foreach (var p in PlayerLife.All)
                        {
                            if (p.Downed.Value) p.ServerRevive(0.5f);
                        }
                    }
                    break;

                case RunPhase.FloorCleared:
                    // Everyone at the portal starts a short countdown; anyone stepping out cancels it.
                    if (PlayersAtExit() == PlayerLife.All.Count && PlayerLife.All.Count > 0)
                    {
                        if (PhaseEndsAt.Value <= 0) PhaseEndsAt.Value = now + ExitDelay;
                        else if (now >= PhaseEndsAt.Value && !ascending)
                        {
                            // The grave lift takes up its chains: everyone rises into the dark, then camp.
                            ascending = true;
                            AscendRpc();
                            StartCoroutine(AfterAscent());
                        }
                    }
                    else if (PhaseEndsAt.Value > 0 && !ascending) PhaseEndsAt.Value = 0;
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

        private bool ascending;

        [Rpc(SendTo.ClientsAndHost)]
        private void AscendRpc()
        {
            Art.ExitPortal.Ascend();
            UI.GameHud.BeginFadeOut(Art.ExitPortal.AscendSeconds);
        }

        private System.Collections.IEnumerator AfterAscent()
        {
            yield return new WaitForSeconds(Art.ExitPortal.AscendSeconds + 0.2f);
            ascending = false;
            if (Phase.Value != RunPhase.FloorCleared) yield break;
            if (ExitFinal.Value) EnterPhase(RunPhase.RunComplete, CompleteDelay);
            else
            {
                NextDepth.Value = Floor.Value.y + 1;
                EnterLobby(keepRun: true);
            }
        }

        /// <summary>After the boss: the last floor's portal ends the run; the others go back to camp.</summary>
        public readonly NetworkVariable<bool> ExitFinal = new NetworkVariable<bool>();
        public const float ExitRadius = 3.2f, ExitDelay = 3f;

        /// <summary>Where the way out opens: the boss's throne spot.</summary>
        public static Vector3 ExitPosition => Layout != null ? Layout.LiftSpot : Vector3.zero;

        /// <summary>Arriving in the Abyss: how many Seals the party broke on the way down.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void AbyssArrivalRpc(int seals)
        {
            Progression.Deeds.Add(Progression.Deeds.Deed.SawAbyss);
            UI.GameHud.ShowTitleCard(seals >= SealTotal ? "THE SEALS ARE BROKEN" : "THE ABYSS  (" + seals + " / " + SealTotal + " SEALS)",
                seals >= SealTotal ? "Below the hymn, something wakes weak and afraid" : "Every Seal you left whole is strength for what waits on the tenth floor");
            Art.Sfx.Play2D(Art.Sound.Slam, 1f);
        }

        public static int PlayersAtExit()
        {
            int n = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var d = p.transform.position - ExitPosition;
                d.y = 0f;
                if (d.magnitude <= ExitRadius) n++;
            }
            return n;
        }

        /// <summary>Server (tests): bring everyone to the portal.</summary>
        public void ServerDebugGatherAtExit()
        {
            int i = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var at = ExitPosition + Quaternion.Euler(0f, i++ * 90f, 0f) * Vector3.forward * 1.2f + Vector3.up * 0.1f;
                p.GetComponent<NetworkPlayer>().RescueRpc(at);
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
                if (p != null && p.Seat.Value >= 0) n++;
            }
            return n;
        }

        private static bool EveryoneAboard() => PlayerLife.All.Count > 0 && PlayersAboard() == PlayerLife.All.Count;

        private static bool PartyWiped()
        {
            if (PlayerLife.All.Count == 0) return false;
            foreach (var p in PlayerLife.All)
            {
                if (!p.Downed.Value || p.GettingUp) return false;
            }
            return true;
        }
    }
}
