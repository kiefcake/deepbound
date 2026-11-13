using System.Collections.Generic;
using Deepbound.Enemies;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// Ways in, and rooms that do not stay won (2026-11-12, the user):
    /// <list type="bullet">
    /// <item>"enemies not spawn out of the air ... climb out of the walls, or out of the floor ... or summoned through
    /// portals": <see cref="SpawnEntering"/> gives anything raised mid-fight a way in (<see cref="EnemyBrain.Entrance"/>).</item>
    /// <item>"more chaos ... a large group of very weak skeletons erupting from the walls, once a room is cleared it
    /// should still pose a threat" (and, asked: "more common and dangerous the deeper"): a cleared combat room may be
    /// ARMED (<see cref="EruptChance"/>, 20% on the first floor to 75%); some seconds later, the next time someone stands in
    /// it, the walls shake, then a swarm of lesser skeletons (<see cref="EnemyBrain.Lesser"/>) claws out of them all
    /// round the room (<see cref="EruptCount"/>), with full-strength ones among them deeper down. From the fifth floor a
    /// room can erupt twice. Never after the guardian falls; never by chance in a test.</item>
    /// </list>
    /// </summary>
    public partial class DungeonDirector
    {
        public static float EruptChance(int depth) => Mathf.Min(0.75f, 0.2f + 0.08f * depth);
        public static int EruptCount(int depth, int players) => Mathf.RoundToInt((6f + 1.5f * depth) * (1f + 0.2f * (Mathf.Clamp(players, 1, 4) - 1)));
        /// <summary>Full-strength skeletons among a swarm: none above the fourth floor, then one per two floors.</summary>
        public static int EruptHeavies(int depth) => depth < 3 ? 0 : (depth - 1) / 2;
        public const float EruptWarn = 1.6f;

        private readonly Dictionary<int, float> eruptArmed = new Dictionary<int, float>();
        private readonly Dictionary<int, int> eruptCount = new Dictionary<int, int>();
        private readonly List<(int room, float at)> eruptPending = new List<(int, float)>();
        /// <summary>Server, tests: swarms raised this floor.</summary>
        public int Eruptions { get; private set; }

        private void ResetEruptions()
        {
            eruptArmed.Clear();
            eruptCount.Clear();
            eruptPending.Clear();
            Eruptions = 0;
            ResetBreachPatrols();
            if (IsServer) ChokedJets.Value = 0;
        }

        /// <summary>Server: a combat room was just won; it may not stay quiet.</summary>
        private void MaybeArmEruption(DungeonLayout.Room room)
        {
            NoteRoomCleared(room);
            if (TestMode || Layout == null || room == null || room.Kind != RoomKind.Combat) return;
            if (Random.value < EruptChance(Layout.Depth)) eruptArmed[room.Id] = Time.time + Random.Range(10f, 30f);
        }

        private void TickEruptions()
        {
            if (Layout == null) return;
            TickBreachPatrols();
            for (int i = eruptPending.Count - 1; i >= 0; i--)
            {
                if (Time.time < eruptPending[i].at) continue;
                var id = eruptPending[i].room;
                eruptPending.RemoveAt(i);
                if (id >= 0 && id < Layout.Rooms.Count) ServerErupt(Layout.Rooms[id]);
            }
            if (eruptArmed.Count == 0 || Time.frameCount % 15 != 0) return;
            int found = -1;
            foreach (var kv in eruptArmed)
            {
                if (Time.time < kv.Value) continue;
                var room = Layout.Rooms[kv.Key];
                foreach (var p in Player.PlayerLife.All)
                    if (p != null && !p.Downed.Value && room.Contains(p.transform.position, -1.5f)) { found = kv.Key; break; }
                if (found >= 0) break;
            }
            if (found < 0) return;
            eruptArmed.Remove(found);
            ServerWarnEruption(Layout.Rooms[found]);
        }

        /// <summary>Server: the walls shake now and the swarm comes <see cref="EruptWarn"/> s later.</summary>
        public void ServerWarnEruption(DungeonLayout.Room room)
        {
            eruptPending.Add((room.Id, Time.time + EruptWarn));
            EruptWarnRpc(room.Center, room.Size, (byte)SwarmOf(Layout.Depth));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EruptWarnRpc(Vector3 center, Vector2 size, byte swarm)
        {
            var me = Player.NetworkPlayer.Local;
            if (me == null || Vector3.Distance(me.transform.position, center) > 30f) return;
            Art.Sfx.Play(Art.Sound.Slam, center, 1f, 0.45f);
            Art.Sfx.Play(Art.Sound.Rattle, center, 1f, 0.6f);
            var dust = new Color(0.45f, 0.42f, 0.38f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f;
                var p = PerimeterPoint(center, size, a) + Vector3.up * Random.Range(0.8f, 2.4f);
                Art.Vfx.Embers(p, 5, dust, 1.5f, 0.07f, 1.4f);
            }
            UI.GameHud.Feed(SwarmWarning((SwarmKind)swarm), new Color(0.85f, 0.75f, 0.6f));
            UI.Tips.OfferNow("eruption", "A WON ROOM DOES NOT STAY WON: the walls shudder, then a swarm of lesser dead claws out of them. They are frail - cut through, or get to a doorway and hold it. Deeper down it happens more, and worse.");
        }

        private static Vector3 PerimeterPoint(Vector3 c, Vector2 size, float a)
        {
            float hx = size.x / 2f, hz = size.y / 2f, per = 2f * (size.x + size.y), d = Mathf.Repeat(a, 1f) * per;
            if (d < size.x) return c + new Vector3(-hx + d, 0f, hz);
            d -= size.x;
            if (d < size.y) return c + new Vector3(hx, 0f, hz - d);
            d -= size.y;
            if (d < size.x) return c + new Vector3(hx - d, 0f, -hz);
            d -= size.x;
            return c + new Vector3(-hx, 0f, -hz + d);
        }

        /// <summary>
        /// SWARMS BY ACT (2026-11-13, the user: "Crypts erupt with skeletons, deeper acts with ghouls clawing up through the
        /// floor, the holy acts with acolytes stepping out of rifts. Same mechanic, different flavour."): what comes, and how.
        /// </summary>
        public enum SwarmKind : byte { Bones = 0, Ghouls = 1, Mixed = 2, Choir = 3, Abyss = 4 }
        public static SwarmKind SwarmOf(int depth) => (SwarmKind)Act(depth);

        public static string SwarmWarning(SwarmKind k) => k switch
        {
            SwarmKind.Ghouls => "The floor heaves. Something is digging up through it.",
            SwarmKind.Mixed => "Stone and earth both crack. They come from everywhere.",
            SwarmKind.Choir => "Rifts tear open in the air. A choir steps through.",
            SwarmKind.Abyss => "The room splits open on every side.",
            _ => "The walls shudder. Something is coming through them.",
        };

        /// <summary>Server: the swarm comes into <paramref name="room"/> its act's way, and goes for you.</summary>
        public int ServerErupt(DungeonLayout.Room room)
        {
            if (Layout == null || skeletonPrefab == null || room == null) return 0;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            var kind = SwarmOf(Layout.Depth);
            int count = EruptCount(Layout.Depth, players), heavies = EruptHeavies(Layout.Depth);
            if (kind == SwarmKind.Ghouls) count = Mathf.Max(4, Mathf.RoundToInt(count * 0.7f)); // ghouls hit harder
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                // Which creature, and which way in.
                var way = EnemyBrain.EntranceKind.Wall;
                var prefab = skeletonPrefab;
                switch (kind)
                {
                    case SwarmKind.Ghouls: way = EnemyBrain.EntranceKind.Floor; if (ghoulPrefab != null) prefab = ghoulPrefab; break;
                    case SwarmKind.Mixed: if (i % 2 == 0 && ghoulPrefab != null) { way = EnemyBrain.EntranceKind.Floor; prefab = ghoulPrefab; } break;
                    case SwarmKind.Choir: way = EnemyBrain.EntranceKind.Portal; if (i < 2 && acolytePrefab != null) prefab = acolytePrefab; break;
                    case SwarmKind.Abyss:
                        way = (EnemyBrain.EntranceKind)(1 + i % 3);
                        if (way == EnemyBrain.EntranceKind.Floor && ghoulPrefab != null) prefab = ghoulPrefab;
                        else if (way == EnemyBrain.EntranceKind.Portal && i < 3 && acolytePrefab != null) prefab = acolytePrefab;
                        break;
                }
                Vector3 at, from;
                Quaternion rot;
                if (way == EnemyBrain.EntranceKind.Wall)
                {
                    if (!RandomWallPoint(room, out var wall, out var outward)) continue;
                    at = wall - outward * 1.1f;
                    from = wall;
                    rot = Quaternion.LookRotation(-outward);
                }
                else
                {
                    // Up through the floor, or out of a rift, anywhere in the inner part of the room.
                    var off = new Vector3(Random.Range(-0.38f, 0.38f) * room.Size.x, 0f, Random.Range(-0.38f, 0.38f) * room.Size.y);
                    at = room.Center + off;
                    from = at;
                    var face = room.Center - at; face.y = 0f;
                    rot = face.sqrMagnitude > 0.1f ? Quaternion.LookRotation(face) : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                }
                at.y = room.Center.y + room.RaisedAt(at);
                if (NavMesh.SamplePosition(at, out var hit, 1.5f, NavMesh.AllAreas)) at = hit.position;
                if (way != EnemyBrain.EntranceKind.Wall) from = at;
                bool lesser = i >= heavies;
                var obj = Instantiate(prefab, at, rot);
                var brain = obj.GetComponent<EnemyBrain>();
                brain.PrepareEntrance(way, from);
                brain.Lesser.Value = lesser;
                obj.Spawn(destroyWithScene: true);
                brain.ServerScale(players, Layout.Depth);
                if (lesser) brain.ServerMakeLesser();
                else brain.NoLoot = true;
                brain.ServerAggroNearest();
                spawned.Add(obj);
                made++;
            }
            Eruptions++;
            // Deep down a room can do it again.
            eruptCount.TryGetValue(room.Id, out int n);
            eruptCount[room.Id] = n + 1;
            if (!TestMode && Layout.Depth >= 4 && n == 0 && Random.value < 0.5f) eruptArmed[room.Id] = Time.time + Random.Range(25f, 50f);
            return made;
        }

        // ---------------------------------------------------------------- BREACH PATROLS

        /// <summary>
        /// BREACH PATROLS (2026-11-13, the user: "On deeper floors, a wall you've already passed cracks open and a small hunting
        /// party follows your trail through cleared rooms. Backtracking for loot gets risky."). From <see cref="BreachDepth"/>,
        /// every <see cref="BreachEvery"/> seconds or so a cleared combat room nobody is standing near breaks open
        /// (<see cref="ServerBreach"/>) and its party HUNTS (<see cref="EnemyBrain.ServerMakeHunter"/>): no post, no leash.
        /// </summary>
        public const int BreachDepth = 4;
        public const float BreachFirst = 110f, BreachEvery = 120f, BreachClear = 18f;
        public static int BreachCap(int depth) => depth < BreachDepth ? 0 : depth < 6 ? 2 : depth < 8 ? 3 : 4;
        public static int BreachSize(int depth, int players) => Mathf.RoundToInt((3 + depth / 3) * (1f + 0.25f * (Mathf.Clamp(players, 1, 4) - 1)));

        private readonly List<int> clearedRooms = new List<int>();
        private float nextBreachAt;
        public int BreachPatrols { get; private set; }

        private void ResetBreachPatrols()
        {
            clearedRooms.Clear();
            BreachPatrols = 0;
            nextBreachAt = Time.time + BreachFirst;
        }

        private void NoteRoomCleared(DungeonLayout.Room room)
        {
            if (room != null && room.Kind == RoomKind.Combat && !clearedRooms.Contains(room.Id)) clearedRooms.Add(room.Id);
        }

        private void TickBreachPatrols()
        {
            if (TestMode || Layout == null || Layout.Depth < BreachDepth || BreachPatrols >= BreachCap(Layout.Depth)) return;
            if (Phase.Value != RunPhase.Exploring || BossSealed || Time.time < nextBreachAt || Time.frameCount % 30 != 0) return;
            DungeonLayout.Room pick = null;
            foreach (var id in clearedRooms)
            {
                var r = Layout.Rooms[id];
                float nearest = float.MaxValue;
                foreach (var p in Player.PlayerLife.All)
                    if (p != null && !p.Downed.Value) nearest = Mathf.Min(nearest, Vector3.Distance(p.transform.position, r.Center));
                if (nearest > BreachClear + Mathf.Max(r.Size.x, r.Size.y) * 0.5f && nearest < 75f) { pick = r; break; }
            }
            nextBreachAt = Time.time + BreachEvery * Random.Range(0.75f, 1.2f) / (1f + 0.1f * (Layout.Depth - BreachDepth));
            if (pick != null) ServerBreach(pick);
        }

        /// <summary>Server: <paramref name="room"/>'s wall cracks open and a hunting party comes out after you (tests call this).</summary>
        public int ServerBreach(DungeonLayout.Room room)
        {
            if (Layout == null || room == null || skeletonPrefab == null) return 0;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            int n = BreachSize(Layout.Depth, players), made = 0;
            // One wall point; the party files out of it.
            if (!RandomWallPoint(room, out var wall, out var outward)) return 0;
            for (int i = 0; i < n; i++)
            {
                var prefab = i == 0 && archerPrefab != null ? archerPrefab : i % 3 == 2 && ghoulPrefab != null ? ghoulPrefab : skeletonPrefab;
                var side = Vector3.Cross(Vector3.up, outward) * ((i % 3) - 1) * 1.1f;
                var at = wall - outward * (1.1f + 0.8f * (i / 3)) + side;
                at.y = room.Center.y + room.RaisedAt(at);
                if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
                var obj = Instantiate(prefab, at, Quaternion.LookRotation(-outward));
                var brain = obj.GetComponent<EnemyBrain>();
                brain.PrepareEntrance(EnemyBrain.EntranceKind.Wall, wall + side);
                obj.Spawn(destroyWithScene: true);
                brain.ServerScale(players, Layout.Depth);
                brain.NoLoot = i > 0;
                brain.ServerMakeHunter();
                spawned.Add(obj);
                made++;
            }
            BreachPatrols++;
            BreachRpc(wall);
            return made;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BreachRpc(Vector3 at)
        {
            Art.Sfx.Play2D(Art.Sound.Slam, 0.35f);
            Art.Sfx.Play(Art.Sound.Rattle, at, 1f, 0.7f);
            UI.GameHud.Feed("Somewhere behind you a wall breaks open. Something is following your trail.", new Color(0.9f, 0.6f, 0.45f));
            UI.Tips.OfferNow("breach", "BREACH PATROLS: deep down, a room you have already cleared can break open behind you, and a hunting party follows your trail. Going back for loot is never quite safe.");
        }

        // ---------------------------------------------------------------- RIFTS

        /// <summary>Server, tests: a rift calling skeletons at <paramref name="at"/>.</summary>
        public SoulRift ServerDebugRift(Vector3 at) => ServerOpenRift(at, Quaternion.identity, skeletonPrefab);

        /// <summary>Server: a necromancer tears a <see cref="SoulRift"/> open at <paramref name="at"/>, calling <paramref name="through"/>.</summary>
        public static SoulRift ServerOpenRift(Vector3 at, Quaternion rot, NetworkObject through)
        {
            var d = Instance;
            if (d == null || !d.IsServer || d.soulRiftPrefab == null || through == null) return null;
            if (NavMesh.SamplePosition(at, out var hit, 2.5f, NavMesh.AllAreas)) at = hit.position;
            var obj = Instantiate(d.soulRiftPrefab, at, rot);
            var rift = obj.GetComponent<SoulRift>();
            rift.Prepare(through, Mathf.Max(1, d.NetworkManager.ConnectedClientsIds.Count), Layout != null ? Layout.Depth : 0);
            obj.Spawn(destroyWithScene: true);
            d.spawned.Add(obj);
            return rift;
        }

        /// <summary>A point on one of <paramref name="room"/>'s walls, clear of its doorways and corners.</summary>
        private static bool RandomWallPoint(DungeonLayout.Room room, out Vector3 wall, out Vector3 outward)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                int s = Random.Range(0, 4);
                if (TryWallPoint(room, s, Random.Range(-1f, 1f), out wall, out outward)) return true;
            }
            wall = room.Center; outward = Vector3.forward;
            return false;
        }

        /// <summary>The point on side <paramref name="side"/> (DungeonLayout.Dir order) at <paramref name="along"/> -1..1 of its
        /// usable length, or false if that lands in a doorway.</summary>
        private static bool TryWallPoint(DungeonLayout.Room room, int side, float along, out Vector3 wall, out Vector3 outward)
        {
            var d = DungeonLayout.Dir[side];
            outward = new Vector3(d.x, 0f, d.y);
            float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
            bool zSide = d.y != 0;
            float half = (zSide ? hx : hz) - 1.2f;
            float a = Mathf.Clamp(along, -1f, 1f) * Mathf.Max(0f, half);
            wall = room.Center + (zSide ? new Vector3(a, 0f, d.y * (hz + 0.2f)) : new Vector3(d.x * (hx + 0.2f), 0f, a));
            if (half <= 0.5f) return false;
            if (room.Doors[side] && Mathf.Abs(a) < 2.6f) return false;
            return true;
        }

        /// <summary>
        /// Server: how an enemy about to appear at <paramref name="at"/> comes in: out of the nearest wall (within 4 m, not a
        /// doorway; the body then stands a pace inside it, facing the room), through a rift, or up through the floor.
        /// </summary>
        public EnemyBrain.EntranceKind PickEntrance(ref Vector3 at, ref Quaternion rot, out Vector3 from, float wallShare, float portalShare)
        {
            from = at;
            float r = Random.value;
            var room = Layout != null ? Layout.RoomAt(at) : null;
            if (room != null && r < wallShare)
            {
                // The nearest side that has room for it.
                int best = -1; float bestD = 4f;
                for (int s = 0; s < 4; s++)
                {
                    var dd = DungeonLayout.Dir[s];
                    float dist = dd.y != 0 ? room.Center.z + dd.y * room.Size.y / 2f - at.z : room.Center.x + dd.x * room.Size.x / 2f - at.x;
                    dist = Mathf.Abs(dist);
                    if (dist < bestD) { bestD = dist; best = s; }
                }
                if (best >= 0)
                {
                    var dd = DungeonLayout.Dir[best];
                    float half = (dd.y != 0 ? room.Size.x : room.Size.y) / 2f - 1.2f;
                    float along = dd.y != 0 ? at.x - room.Center.x : at.z - room.Center.z;
                    if (half > 0.5f && TryWallPoint(room, best, along / half, out var wall, out var outward))
                    {
                        from = wall;
                        var inside = wall - outward * 1.1f;
                        inside.y = at.y;
                        if (NavMesh.SamplePosition(inside, out var hit, 1.5f, NavMesh.AllAreas)) inside = hit.position;
                        at = inside;
                        rot = Quaternion.LookRotation(-outward);
                        return EnemyBrain.EntranceKind.Wall;
                    }
                }
            }
            if (r >= wallShare && r < wallShare + portalShare) return EnemyBrain.EntranceKind.Portal;
            return EnemyBrain.EntranceKind.Floor;
        }

        /// <summary>Server: instantiate, give it a way in (<see cref="PickEntrance"/>), spawn.</summary>
        public NetworkObject SpawnEntering(NetworkObject prefab, Vector3 at, Quaternion rot, float wallShare = 0.45f, float portalShare = 0.2f)
        {
            var kind = PickEntrance(ref at, ref rot, out var from, wallShare, portalShare);
            var obj = Instantiate(prefab, at, rot);
            if (obj.TryGetComponent<EnemyBrain>(out var b)) b.PrepareEntrance(kind, from);
            obj.Spawn(destroyWithScene: true);
            return obj;
        }

        /// <summary>For the brains (a necromancer's raised, a guardian's summons): the director's way in, or a plain spawn.</summary>
        public static NetworkObject ServerSpawnEntering(NetworkObject prefab, Vector3 at, Quaternion rot, float wallShare, float portalShare)
        {
            if (Instance != null && Instance.IsServer) return Instance.SpawnEntering(prefab, at, rot, wallShare, portalShare);
            var obj = Instantiate(prefab, at, rot);
            obj.Spawn(destroyWithScene: true);
            return obj;
        }
    }
}
