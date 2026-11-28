using Deepbound.Loot;
using System.Collections.Generic;
using System.Linq;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        [Header("Encounters")]
        [SerializeField] private NetworkObject portcullisPrefab;
        [SerializeField] private NetworkObject leverPrefab;
        [SerializeField] private NetworkObject fountainPrefab;

        [SerializeField] private NetworkObject sideShrinePrefab;

        private void SpawnShrine(SideShrine.Kind kind, DungeonLayout.Room room, Vector3 at, int choice, int group)
        {
            var face = room.Center - at;
            face.y = 0f;
            var obj = Instantiate(sideShrinePrefab, at, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
            var s = obj.GetComponent<SideShrine>();
            obj.Spawn(destroyWithScene: true);
            s.Type.Value = kind;
            s.Choice.Value = choice;
            s.Group.Value = group;
            spawned.Add(obj);
        }

        [SerializeField] private NetworkObject oathPlatesPrefab, oathStonePrefab, handTorchPrefab;

        public HandTorch ServerDebugTorch(Vector3 at) => handTorchPrefab == null ? null : Spawn(handTorchPrefab, at, Quaternion.identity).GetComponent<HandTorch>();

        private OathPlates SpawnOath(DungeonLayout.Room room, Vector3? a = null, Vector3? b = null, Vector3? stone = null, Vector3? coffer = null)
        {
            var obj = Instantiate(oathPlatesPrefab, room != null ? room.Center : (a ?? Vector3.zero), Quaternion.identity);
            obj.Spawn(destroyWithScene: true);
            spawned.Add(obj);
            var plates = obj.GetComponent<OathPlates>();
            plates.RoomId = room != null ? room.Id : -1;
            plates.ServerSetup(a ?? room.FeatureSpots[0], b ?? room.FeatureSpots[1], coffer ?? room.FeatureSpots[3]);
            if (oathStonePrefab != null)
            {
                var s = Instantiate(oathStonePrefab, Art.Vfx.Floor(stone ?? room.FeatureSpots[2]), Quaternion.identity);
                s.Spawn(destroyWithScene: true);
                spawned.Add(s);
            }
            return plates;
        }

        public void ServerOathKept(OathPlates plates)
        {
            var at = plates.Coffer.Value;
            var room = plates.RoomId >= 0 && Layout != null && plates.RoomId < Layout.Rooms.Count ? Layout.Rooms[plates.RoomId] : Layout?.RoomAt(at, 0f);
            var face = (room != null ? room.Center : plates.transform.position) - at;
            face.y = 0f;
            var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
            if (chestPrefab != null)
            {
                var chest = Spawn(chestPrefab, room != null ? DropSpots.ClearForChest(at, rot, room) : at, rot);
                chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                spawned.Add(chest);
            }
            var front = at + rot * Vector3.forward * 1.6f;
            ServerDropLoot(front, LootKind.Sigil, 1);
            for (int i = 0; i < 4; i++) ServerDropLoot(front + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 0.9f, LootKind.Gold, 15 + 8 * (int)Tier(Floor.Value.y));
            OathsKept++;
        }

        public int OathsKept { get; private set; }

        public OathPlates ServerDebugOath(Vector3 a, Vector3 b, Vector3 coffer) =>
            oathPlatesPrefab == null ? null : SpawnOath(null, a, b, a + (b - a).normalized * -2f, coffer);

        public SideShrine ServerDebugShrine(SideShrine.Kind kind, Vector3 at, int choice = 0)
        {
            if (sideShrinePrefab == null || Layout == null) return null;
            var obj = Instantiate(sideShrinePrefab, at, Quaternion.identity);
            var s = obj.GetComponent<SideShrine>();
            obj.Spawn(destroyWithScene: true);
            s.Type.Value = kind;
            s.Choice.Value = choice;
            s.Group.Value = 9999;
            return s;
        }
        [SerializeField] private NetworkObject soulSkullPrefab;

        public void ServerSpawnSoulSkull(Player.PlayerLife who)
        {
            if (soulSkullPrefab == null || SoulSkull.For(who.NetworkObjectId) != null) return;
            var obj = Spawn(soulSkullPrefab, who.transform.position + Vector3.up * 0.05f, Quaternion.identity);
            obj.GetComponent<SoulSkull>().Soul.Value = who.NetworkObjectId;
            BledOutRpc(who.name);
        }

        public SoulSkull ServerDebugSkull(Vector3 at, ulong soulId)
        {
            if (soulSkullPrefab == null) return null;
            var skull = Spawn(soulSkullPrefab, at, Quaternion.identity).GetComponent<SoulSkull>();
            skull.Soul.Value = soulId;
            return skull;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BledOutRpc(string who)
        {
            UI.GameHud.Feed(who + " has bled out. Carry their soul skull to the waystone in the start room.", new Color(0.75f, 0.55f, 1f));
            Art.Sfx.Play2D(Art.Sound.Whisper, 0.9f);
        }
        [SerializeField] private NetworkObject sarcophagusPrefab;

        private class Vault
        {
            public DungeonLayout.Room Room;
            public readonly List<Portcullis> Gates = new List<Portcullis>();
            public readonly List<Lever> Levers = new List<Lever>();
            public readonly List<Lever> Hidden = new List<Lever>();
            public bool Open, HintGiven;
        }
        private Vault vault;

        [SerializeField] private NetworkObject crackedWallPrefab;

        private class Breach
        {
            public CrackedWall Wall;
            public DungeonLayout.Room Room;
            public WallHole Hole;
            public float Occupied, TriggerAfter;
        }
        private readonly List<Breach> breaches = new List<Breach>();

        public int BreachCount => breaches.Count;
        public void ServerDebugTriggerBreach(int i) { if (i < breaches.Count) breaches[i].Occupied = 999f; }
        public CrackedWall BreachWall(int i) => i < breaches.Count ? breaches[i].Wall : null;

        [SerializeField] private NetworkObject bargainAltarPrefab;
        [SerializeField] private NetworkObject cellarHatchPrefab;

        public BargainAltar SpawnBargain(Vector3 at, Quaternion rot, int offer)
        {
            if (bargainAltarPrefab == null) return null;
            var obj = Instantiate(bargainAltarPrefab, at, rot);
            var altar = obj.GetComponent<BargainAltar>();
            altar.Prepare((Progression.PlayerProgress.Bargain)Mathf.Clamp(offer, 0, Progression.PlayerProgress.BargainCount - 1));
            obj.Spawn(destroyWithScene: true);
            return altar;
        }

        private void SpawnCellars(DungeonLayout layout)
        {
            if (cellarHatchPrefab == null) return;
            var rng = new System.Random(layout.Seed ^ 0xce12);
            foreach (var room in layout.Rooms)
            {
                if (!room.Cellar) continue;
                var cc = room.CellarCenter;
                var hatch = Instantiate(cellarHatchPrefab, room.HatchSpot, Quaternion.identity);
                hatch.GetComponent<CellarHatch>().Prepare(CellarHatch.Mode.Hatch, cc + new Vector3(0.9f, 0f, 0.9f));
                hatch.Spawn(destroyWithScene: true);
                var rope = Instantiate(cellarHatchPrefab, cc, Quaternion.identity);
                rope.GetComponent<CellarHatch>().Prepare(CellarHatch.Mode.Rope, room.HatchSpot + new Vector3(1.4f, 0f, 1.4f));
                rope.Spawn(destroyWithScene: true);
                float half = DungeonLayout.CellarSize / 2f - 1.2f;
                if (chestPrefab != null)
                    for (int i = 0; i < 2; i++)
                    {
                        var at = cc + new Vector3(i == 0 ? -half : half, 0f, -half);
                        var chest = Spawn(chestPrefab, at, Quaternion.LookRotation(cc - at));
                        chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                    }
                int tier = Mathf.RoundToInt(Tier(layout.Depth));
                for (int g = 0; g < 5; g++) ServerDropLoot(cc + new Vector3(-half + 0.6f + g * 0.35f, 0.3f, half - 0.3f), LootKind.Gold, 10 + 5 * tier, settle: false);
                if (rng.NextDouble() < 0.5) ServerDropLoot(cc + new Vector3(half - 0.5f, 0.3f, half - 0.5f), LootKind.Relic, (int)Relics.Random(), settle: false);

                if (!TestMode && skeletonPrefab != null)
                {
                    int guards = 2 + (layout.Depth >= 4 ? 1 : 0);
                    for (int i = 0; i < guards; i++)
                    {
                        var at = DropSpots.Stand(cc, cc + new Vector3((i - 1) * 1.6f, 0f, -half + 1.4f), 0.3f, 0.9f);
                        var e = Instantiate(i == 2 && brutePrefab != null ? brutePrefab : skeletonPrefab, at, Quaternion.LookRotation(Vector3.forward));
                        e.Spawn(destroyWithScene: true);
                        var b = e.GetComponent<Enemies.EnemyBrain>();
                        b.ServerScale(Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count), layout.Depth);
                        spawned.Add(e);
                    }
                }
            }
        }

        private void SetupHoles(DungeonLayout layout)
        {
            breaches.Clear();
            if (crackedWallPrefab == null) return;
            var theme = DungeonTheme.ForDepth(layout.Depth);
            var rng = new System.Random(layout.Seed ^ 0x601e);
            foreach (var room in layout.Rooms)
            {
                foreach (var hole in room.Holes)
                {
                    var face = room.HoleFace(hole);
                    var outward = DungeonLayout.Room.HoleOutward(hole);
                    var obj = Instantiate(crackedWallPrefab, face, Quaternion.LookRotation(outward));
                    var wall = obj.GetComponent<CrackedWall>();
                    wall.Breach.Value = hole.Breach;
                    wall.StoneTint.Value = theme.StoneTint;
                    obj.Spawn(destroyWithScene: true);

                    if (hole.Breach)
                    {
                        if (IsAmbushRoom(room)) continue;
                        breaches.Add(new Breach { Wall = wall, Room = room, Hole = hole, TriggerAfter = 3f + (float)rng.NextDouble() * 5f });
                        continue;
                    }

                    if (hole.Lever) continue;
                    var back = face + outward * (0.8f + WallHole.NicheDepth - 1.4f);
                    double roll = rng.NextDouble();
                    if (roll < 0.18 && statDraughtPrefab != null)
                    {
                        SpawnDraught(room, back + outward * 0.3f);
                        continue;
                    }

                    if (roll < 0.34 && bargainAltarPrefab != null)
                    {
                        SpawnBargain(back + outward * 0.1f, Quaternion.LookRotation(-outward), rng.Next(Progression.PlayerProgress.BargainCount));
                        continue;
                    }
                    if (chestPrefab != null)
                    {
                        var chest = Spawn(chestPrefab, back - outward * 0.6f, Quaternion.LookRotation(-outward));
                        chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                    }
                    var plinth = back + outward * 0.75f + Vector3.up * 0.6f;
                    if (roll < 0.4)
                    {
                        int tier = Mathf.RoundToInt(Tier(layout.Depth));
                        for (int g = 0; g < 4; g++) ServerDropLoot(plinth + Quaternion.Euler(0f, g * 90f, 0f) * Vector3.forward * 0.35f, LootKind.Gold, 8 + 4 * tier, settle: false);
                        ServerDropEmbers(plinth, 6 + 2 * tier);
                    }
                    else if (roll < 0.62) ServerDropLoot(plinth, LootKind.Relic, (int)Relics.Random(), settle: false);
                    else ServerDropLoot(plinth, roll < 0.81 ? LootKind.Sigil : LootKind.Rune, (int)Runes.Random(), settle: false);
                }
            }
        }

        private void TickBreaches()
        {
            foreach (var b in breaches)
            {
                if (b.Wall == null || b.Wall.Triggered) continue;
                bool inRoom = false;
                foreach (var p in PlayerLife.All)
                    if (p != null && p.Targetable && b.Room.Contains(p.transform.position, -1f)) { inRoom = true; break; }
                if (!inRoom) continue;
                b.Occupied += Time.deltaTime;
                if (b.Occupied < b.TriggerAfter) continue;
                var breach = b;
                b.Wall.ServerTriggerBreach(() => SpawnBreachers(breach));
            }
        }

        private void SpawnBreachers(Breach b)
        {
            var layout = Layout;
            if (layout == null) return;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            var theme = DungeonTheme.ForDepth(layout.Depth);
            var face = b.Room.HoleFace(b.Hole);
            var outward = DungeonLayout.Room.HoleOutward(b.Hole);
            var across = Vector3.Cross(Vector3.up, outward);
            int count = Mathf.RoundToInt((2 + Tier(layout.Depth)) * Difficulty.SpawnCount(players));
            bool brute = false, necro = true;
            for (int i = 0; i < count; i++)
            {
                var prefab = PickEnemy(layout.Depth, theme, ref brute, ref necro, out int _);

                var at = face + outward * (1.6f + (i / 2) * 1.1f) + across * ((i % 2) - 0.5f) * 1.1f;
                if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
                var obj = Instantiate(prefab, at, Quaternion.LookRotation(-outward));
                obj.Spawn(destroyWithScene: true);
                var brain = obj.GetComponent<EnemyBrain>();
                brain.ServerScale(players, layout.Depth);
                brain.ServerAggroNearest();
                spawned.Add(obj);
            }
            TitleCardRpc("BREACH", "The dead break through the walls");
        }

        public bool VaultOpen => vault != null && vault.Open;

        public int VaultChestCount { get; private set; }
        private readonly Dictionary<PlayerLife, float> rescuedAt = new Dictionary<PlayerLife, float>();

        private readonly List<Portcullis> bossGates = new List<Portcullis>();
        private bool bossSealed;

        private class Ambush
        {
            public DungeonLayout.Room Room;
            public readonly List<Portcullis> Gates = new List<Portcullis>();
            public readonly List<EnemyBrain> Alive = new List<EnemyBrain>();
            public int Wave;
            public float WaveAt;

            public int Party = 1;

            public bool Opened;
        }

        private static int InAmbushTrigger(DungeonLayout.Room room)
        {
            float trigger = Mathf.Min(room.Size.x, room.Size.y) / 2f - 2.5f;
            int inside = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                var d = p.transform.position - room.Center;
                d.y = 0f;
                if (d.magnitude <= trigger) inside++;
            }
            return inside;
        }

        private static bool AnyoneStandingIn(DungeonLayout.Room room)
        {
            foreach (var p in PlayerLife.All)
                if (p != null && !p.Downed.Value && p.Health != null && p.Health.IsAlive && room.Contains(p.transform.position, 0.5f)) return true;
            return false;
        }

        public const float AmbushWaveLimit = 75f;
        private Ambush ambush;

        public DungeonLayout.Room AmbushRoom => ambush?.Room;
        public int AmbushWave => ambush?.Wave ?? -1;

        public int AmbushParty => ambush?.Party ?? 0;

        public readonly NetworkVariable<bool> BossSealedSync = new NetworkVariable<bool>();
        public bool BossSealed => IsServer ? bossSealed : BossSealedSync.Value;

        private void ResetEncounters()
        {
            ResetGimmicks();
            clearedDownAt.Clear();
            ritualRoom = null;
            ritualBraziers.Clear();
            if (IsServer) RitualActive.Value = false;
            bladeHitAt.Clear();
            bossGates.Clear();
            bossSealed = false;
            if (IsServer) BossSealedSync.Value = false;
            ambush = null;
            lair = null;
            vault = null;
            VaultChestCount = 0;
            ResetTripWires();
            leversAnnounced = 0;
            breaches.Clear();
            foreach (var w in FindObjectsByType<CrackedWall>(FindObjectsSortMode.None))
                if (w.NetworkObject.IsSpawned) w.NetworkObject.Despawn();
            foreach (var l in FindObjectsByType<Lever>(FindObjectsSortMode.None))
                if (l.NetworkObject.IsSpawned) l.NetworkObject.Despawn();
            foreach (var f in FindObjectsByType<Fountain>(FindObjectsSortMode.None))
                if (f.NetworkObject.IsSpawned) f.NetworkObject.Despawn();
            foreach (var sk in FindObjectsByType<Carryable>(FindObjectsSortMode.None))
                if (sk.NetworkObject.IsSpawned) sk.NetworkObject.Despawn();
            foreach (var s in FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None))
                if (s.NetworkObject.IsSpawned) s.NetworkObject.Despawn();
            foreach (var g in FindObjectsByType<Portcullis>(FindObjectsSortMode.None))
            {
                if (g.NetworkObject.IsSpawned) g.NetworkObject.Despawn();
            }
        }

        private void SetupEncounters(DungeonLayout layout)
        {
            if (portcullisPrefab == null) return;
            foreach (var g in SpawnGates(layout.BossRoom)) bossGates.Add(g);
            if (InTutorial) return;

            var room = PickAmbushRoom(layout);
            if (room == null || IsLairRoom(room)) return;
            ambush = new Ambush { Room = room };
            ambush.Gates.AddRange(SpawnGates(ambush.Room));
        }

        private static DungeonLayout.Room PickAmbushRoom(DungeonLayout layout)
        {
            var rng = new System.Random(layout.Seed ^ 0xA3B);
            var candidates = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && r.Id >= 2 &&
                (r.Feature == RoomFeature.None || r.Feature == RoomFeature.GreatHall || r.Feature == RoomFeature.Gallery || r.Feature == RoomFeature.SunkenCourt));
            return candidates.Count == 0 ? null : candidates[rng.Next(candidates.Count)];
        }

        private bool IsAmbushRoom(DungeonLayout.Room room) => (ambush != null && ambush.Room == room) || IsLairRoom(room);

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
                            bool brute = true, necro = true;
                            var occupant = PickEnemy(layout.Depth, theme, ref brute, ref necro, out int _);
                            obj.GetComponent<Sarcophagus>().ServerLoad(occupant, players, layout.Depth, room.Id);
                        }
                        break;

                    case RoomFeature.Vault when leverPrefab != null && portcullisPrefab != null:
                        vault = new Vault { Room = room };
                        vault.Gates.AddRange(SpawnGates(room));
                        SetGates(vault.Gates, true);

                        var lrng = new System.Random(layout.Seed ^ 0x1e7e);
                        var leverRooms = layout.Rooms.FindAll(r => r != room && r.Kind != RoomKind.Boss && r.Kind != RoomKind.Start && !IsAmbushRoom(r));
                        foreach (var lr in layout.Rooms)
                            foreach (var hole in lr.Holes)
                            {
                                if (!hole.Lever || vault.Levers.Count >= DungeonLayout.VaultLevers) continue;
                                var outward = DungeonLayout.Room.HoleOutward(hole);
                                var spot = lr.HoleFace(hole) + outward * (DungeonBuilder.WallThickness / 2f + WallHole.NicheDepth - 0.06f);
                                var hidden = Spawn(leverPrefab, spot, Quaternion.LookRotation(-outward)).GetComponent<Lever>();
                                vault.Levers.Add(hidden);
                                vault.Hidden.Add(hidden);
                                leverRooms.Remove(lr);
                            }
                        if (leverRooms.Count == 0) leverRooms = layout.Rooms.FindAll(r => r != room && r.Kind != RoomKind.Boss && r.Kind != RoomKind.Start);
                        while (vault.Levers.Count < DungeonLayout.VaultLevers && leverRooms.Count > 0)
                        {
                            var lr = PickLeverRoom(leverRooms, layout, lrng);
                            leverRooms.Remove(lr);
                            var spot = WallMount(lr, lrng, out var face);
                            vault.Levers.Add(Spawn(leverPrefab, spot, Quaternion.LookRotation(face)).GetComponent<Lever>());
                        }
                        SpawnVaultHoard(room, layout.Depth, lrng);
                        break;

                    case RoomFeature.Ritual when breakablePrefab != null:
                        ritualRoom = room;
                        ritualBraziers.Clear();
                        foreach (var spot in room.FeatureSpots)
                        {
                            var obj = Instantiate(breakablePrefab, spot, Quaternion.identity);
                            var br = obj.GetComponent<Breakable>();
                            br.Type.Value = Breakable.Kind.RitualBrazier;
                            br.SetLootPrefab(lootPrefab);
                            obj.Spawn(destroyWithScene: true);
                            ritualBraziers.Add(br);
                        }
                        RitualActive.Value = true;
                        nextRitualSummon = 0f;
                        ritualSummons = 0;
                        break;

                    case RoomFeature.Belfry when chestPrefab != null && room.FeatureSpots.Count >= 1:
                    {
                        var face = room.Center - room.FeatureSpots[0];
                        face.y = 0f;
                        var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
                        var loft = Spawn(chestPrefab, room.FeatureSpots[0], rot);
                        loft.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                        spawned.Add(loft);
                        break;
                    }
                    case RoomFeature.Lightless when handTorchPrefab != null && room.FeatureSpots.Count >= 2:
                    {
                        Spawn(handTorchPrefab, Art.Vfx.Floor(room.FeatureSpots[0]), Quaternion.identity);
                        if (chestPrefab != null)
                        {
                            var face = room.Center - room.FeatureSpots[1];
                            face.y = 0f;
                            var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
                            var dark = Spawn(chestPrefab, DropSpots.ClearForChest(room.FeatureSpots[1], rot, room), rot);
                            dark.GetComponent<Chest>().SetLootPrefab(lootPrefab);
                            spawned.Add(dark);
                        }
                        break;
                    }
                    case RoomFeature.Fountain when fountainPrefab != null && room.FeatureSpots.Count > 0:
                        Spawn(fountainPrefab, room.FeatureSpots[0], Quaternion.identity);
                        break;
                    case RoomFeature.Treasury when sideShrinePrefab != null:
                    {
                        var picks = new List<Relic>();
                        for (int tries = 0; tries < 40 && picks.Count < room.FeatureSpots.Count; tries++) { var r = Relics.Random(); if (!picks.Contains(r)) picks.Add(r); }
                        for (int k = 0; k < room.FeatureSpots.Count && k < picks.Count; k++)
                            SpawnShrine(SideShrine.Kind.Pedestal, room, room.FeatureSpots[k], (int)picks[k], room.Id);
                        break;
                    }
                    case RoomFeature.Beggar when sideShrinePrefab != null && room.FeatureSpots.Count > 0: SpawnShrine(SideShrine.Kind.Beggar, room, room.FeatureSpots[0], 0, room.Id); break;
                    case RoomFeature.Proving when sideShrinePrefab != null && room.FeatureSpots.Count > 0: SpawnShrine(SideShrine.Kind.Bell, room, room.FeatureSpots[0], 0, room.Id); break;
                    case RoomFeature.Armoury when sideShrinePrefab != null && room.FeatureSpots.Count > 0: SpawnShrine(SideShrine.Kind.Whetstone, room, room.FeatureSpots[0], 0, room.Id); break;
                    case RoomFeature.Mirror when sideShrinePrefab != null && room.FeatureSpots.Count > 0: SpawnShrine(SideShrine.Kind.Mirror, room, room.FeatureSpots[0], 0, room.Id); break;
                    case RoomFeature.Crucible when sideShrinePrefab != null && room.FeatureSpots.Count > 0: SpawnShrine(SideShrine.Kind.Crucible, room, room.FeatureSpots[0], 0, room.Id); break;
                    case RoomFeature.Oath when oathPlatesPrefab != null && room.FeatureSpots.Count >= 4: SpawnOath(room); break;
                    case RoomFeature.Statues when room.FeatureSpots.Count >= 2: SpawnPuzzle(PuzzleRoom.Kind.Watchers, room.FeatureSpots[1], room.FeatureSpots[0], room.Id); break;
                    case RoomFeature.Chimes when room.FeatureSpots.Count >= 2: SpawnPuzzle(PuzzleRoom.Kind.Chimes, room.FeatureSpots[1], room.FeatureSpots[0], room.Id); break;
                    case RoomFeature.Mirrors when room.FeatureSpots.Count >= 2: SpawnPuzzle(PuzzleRoom.Kind.Mirrors, room.FeatureSpots[1], room.FeatureSpots[0], room.Id); break;
                    case RoomFeature.Draught when statDraughtPrefab != null && room.FeatureSpots.Count > 0: SpawnDraught(room, room.FeatureSpots[0]); break;
                    case RoomFeature.Scales when room.FeatureSpots.Count >= 2: SpawnPuzzle(PuzzleRoom.Kind.Scales, room.FeatureSpots[1], room.FeatureSpots[0], room.Id); break;
                    case RoomFeature.Panoply: SetupPanoply(room, layout); break;
                }
            }
        }

        private int leversAnnounced;

        private static DungeonLayout.Room PickLeverRoom(List<DungeonLayout.Room> rooms, DungeonLayout layout, System.Random rng)
        {
            if (layout.Depth < 2) return rooms[rng.Next(rooms.Count)];
            DungeonLayout.Room best = null;
            float bestScore = float.MinValue;
            foreach (var r in rooms)
            {
                int doors = 0;
                for (int s = 0; s < 4; s++) if (r.Doors[s]) doors++;
                float score = Vector3.Distance(r.Center, layout.StartRoom.Center) / DungeonLayout.CellSize + (doors <= 1 ? 1.5f : 0f) + (r.Kind == RoomKind.Side ? 0.8f : 0f) + (float)rng.NextDouble() * 1.5f;
                if (score > bestScore) { bestScore = score; best = r; }
            }
            return best;
        }

        private enum Hoard { Gold, Embers, Rune, Relic, Sigil, Gear, Potions }

        private static readonly (Hoard kind, int fromDepth, float weight)[] HoardTable =
        {
            (Hoard.Gold, 0, 3f), (Hoard.Embers, 0, 2.5f), (Hoard.Potions, 0, 1.5f), (Hoard.Rune, 1, 1.5f),
            (Hoard.Gear, 2, 1.5f), (Hoard.Relic, 2, 1.2f), (Hoard.Sigil, 3, 1f),
        };

        public static int VaultChests(int depth, System.Random rng) => 1 + rng.Next(3) + depth / 4;

        public static int VaultExtras(int depth) => 1 + (depth + 1) / 2;

        private void SpawnVaultHoard(DungeonLayout.Room room, int depth, System.Random rng)
        {
            var spots = new List<Vector3>();
            float radius = Mathf.Min(room.Size.x, room.Size.y) / 2f - 2.6f;
            for (int ring = 0; ring < 2; ring++)
                for (int i = 0; i < 8; i++)
                {
                    float a = (i + ring * 0.5f) * Mathf.PI / 4f;
                    spots.Add(room.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring == 0 ? radius : radius * 0.5f));
                }
            for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }
            int next = 0;
            Vector3 NextSpot() => spots[next++ % spots.Count];

            VaultChestCount = chestPrefab != null ? VaultChests(depth, rng) : 0;
            for (int c = VaultChestCount; c > 0; c--)
            {
                var at = NextSpot();
                var face = room.Center - at;
                face.y = 0f;
                var rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity;
                Spawn(chestPrefab, DropSpots.ClearForChest(at, rot, room), rot).GetComponent<Chest>().SetLootPrefab(lootPrefab);
            }

            var picks = new List<Hoard>();
            int extras = VaultExtras(depth);
            if (depth >= 6) { picks.Add(Hoard.Relic); extras--; }
            float total = 0f;
            foreach (var h in HoardTable) if (depth >= h.fromDepth) total += h.weight;
            for (int e = 0; e < extras; e++)
            {
                float roll = (float)rng.NextDouble() * total;
                foreach (var h in HoardTable)
                {
                    if (depth < h.fromDepth) continue;
                    roll -= h.weight;
                    if (roll <= 0f) { picks.Add(h.kind); break; }
                }
            }

            int tier = Mathf.RoundToInt(Tier(depth));
            foreach (var kind in picks)
            {
                var at = NextSpot() + Vector3.up * 0.3f;
                switch (kind)
                {
                    case Hoard.Gold:
                        for (int g = 0; g < 4; g++) ServerDropLoot(at + Quaternion.Euler(0f, g * 90f, 0f) * Vector3.forward * 0.45f, LootKind.Gold, 14 + 8 * tier);
                        break;
                    case Hoard.Embers:
                        ServerDropEmbers(at, 12 + 6 * tier);
                        break;
                    case Hoard.Potions:
                        ServerDropLoot(at, LootKind.PotionHP, 1 + depth / 4);
                        ServerDropLoot(at + Vector3.right * 0.5f, LootKind.PotionMP, 1);
                        break;
                    case Hoard.Rune:
                        ServerDropLoot(at, LootKind.Rune, (int)Runes.Random());
                        break;
                    case Hoard.Relic:
                        ServerDropLoot(at, LootKind.Relic, (int)Relics.Random());
                        break;
                    case Hoard.Sigil:
                        ServerDropLoot(at, LootKind.Sigil, 1);
                        break;
                    case Hoard.Gear:
                        foreach (var p in PlayerLife.All)
                            if (p != null) GearDrop.ServerSpawn(Gear.Roll(depth, depth >= 5 ? 1 : 0), p.OwnerClientId, at);
                        break;
                }
            }
        }

        private static Vector3 WallMount(DungeonLayout.Room room, System.Random rng, out Vector3 face)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                int side = rng.Next(4);
                var d = DungeonLayout.Dir[side];
                bool alongX = side == 0 || side == 2;
                float half = (alongX ? room.Size.x : room.Size.y) / 2f - 1.6f;
                float t = (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                if (room.Doors[side] && Mathf.Abs(t) < DungeonLayout.CorridorWidth) continue;

                var hollow = room.HoleOn(side);
                if (hollow != null && Mathf.Abs(t - hollow.Along) < WallHole.Width / 2f + 1.4f) continue;
                var n = new Vector3(d.x, 0f, d.y);
                face = -n;
                float off = (alongX ? room.Size.y : room.Size.x) / 2f - 0.06f;
                return room.Center + n * off + (alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
            }
            face = Vector3.back;
            return room.Center + Vector3.forward * (room.Size.y / 2f - 0.06f);
        }

        private void TickVault()
        {
            if (vault == null || vault.Open || vault.Levers.Count == 0) return;
            int down = 0;
            foreach (var l in vault.Levers) if (l != null && l.Pulled.Value) down++;
            if (down > leversAnnounced)
            {
                leversAnnounced = down;
                if (down < vault.Levers.Count)
                    ServerFeedAll("A vault lever falls into place: " + down + " of " + vault.Levers.Count + ". Somewhere, a vault stirs.", new Color(1f, 0.75f, 0.35f));
                int hiddenLeft = 0;
                foreach (var l in vault.Hidden) if (l != null && !l.Pulled.Value) hiddenLeft++;
                if (!vault.HintGiven && hiddenLeft > 0 && down + hiddenLeft >= vault.Levers.Count)
                {
                    vault.HintGiven = true;
                    ServerFeedAll(hiddenLeft == 1 ? "The last lever is sealed behind a cracked wall. Listen for the draught." : "The last " + hiddenLeft + " levers are sealed behind cracked walls. Listen for the draught.", new Color(1f, 0.75f, 0.35f));
                }
            }
            if (down < vault.Levers.Count) return;
            vault.Open = true;
            foreach (var l in vault.Levers) l.ServerLock();
            SetGates(vault.Gates, false);
            TitleCardRpc("THE VAULT OPENS", "Treasure lies within");
        }

        private void TickFalls()
        {
            if (Layout == null) return;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.transform.position.y > -3f || Layout.InCellar(p.transform.position)) continue;

                var pitRoom = Layout.RoomAt(p.transform.position);
                if (pitRoom != null && pitRoom.Feature == RoomFeature.Undercroft && p.transform.position.y > -DungeonLayout.UndercroftDepth - 1.5f) continue;
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

        public const float CampFloorLimit = 6f;

        private void TickCampFalls()
        {
            if (!InLobby) return;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.transform.position.y > Lobby.Origin.y - CampFloorLimit) continue;
                if (rescuedAt.TryGetValue(p, out var t) && Time.time - t < 2f) continue;
                rescuedAt[p] = Time.time;
                p.GetComponent<NetworkPlayer>().RescueRpc(Lobby.SpawnFor(p.OwnerClientId) + Vector3.up * 0.1f);
            }
        }

        public const float UnderGate = 1.7f;
        private int ShoveUnderGates(List<Portcullis> gates, DungeonLayout.Room room)
        {
            int moved = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                foreach (var g in gates)
                {
                    if (g == null) continue;
                    var d = p.transform.position - g.transform.position; d.y = 0f;
                    if (d.magnitude > UnderGate) continue;
                    var inward = room.Center - g.transform.position; inward.y = 0f;
                    var to = g.transform.position + inward.normalized * 2.4f;
                    if (NavMesh.SamplePosition(to, out var hit, 1.5f, NavMesh.AllAreas)) to = hit.position;
                    p.GetComponent<NetworkPlayer>().ShoveRpc(to + Vector3.up * 0.1f);
                    moved++;
                    break;
                }
            }
            return moved;
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

        private void TickEncounters()
        {
            TickBossArena();
            TickAmbush();
            TickLair();
            TickRoomClears();
            TickEruptions();
            TickVault();
            TickFalls();
            TickBreaches();
            TickPendulums();
            TickRoomHazards();
            TickRitual();
            TickGimmicks();
            TickFlameJets();
            TickTripWires();
            TickHearing();
            TickContract();
        }

        private readonly Dictionary<Health, float> burnedAt = new Dictionary<Health, float>();

        public int VentBurns { get; private set; }

        private void TickFlameJets()
        {
            if (Layout == null || Layout.FlameTraps.Count == 0) return;
            double now = Pendulum.Clock;
            for (int ti = 0; ti < Layout.FlameTraps.Count; ti++)
            {
                var trap = Layout.FlameTraps[ti];
                if (!JetBurning(ti, trap.Phase, now)) continue;
                var along = Vector3.Cross(trap.Across, Vector3.up);
                foreach (var h in JetScratch())
                {
                    if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                    var d = h.transform.position - trap.At;
                    if (Mathf.Abs(Vector3.Dot(d, along)) > FlameJet.HalfWidth || Mathf.Abs(Vector3.Dot(d, trap.Across)) > DungeonLayout.CorridorWidth / 2f || Mathf.Abs(d.y) > 1.5f) continue;

                    if (burnedAt.TryGetValue(h, out float t) && Time.time - t < (h.Team == Team.Players ? FlameJet.Burn + 0.1f : 0.4f)) continue;
                    burnedAt[h] = Time.time;
                    if (h.Team == Team.Players) VentBurns++;

                    h.Unavoidable().ServerDamage(h.Team == Team.Players ? TrapBite(h) : 50f + 15f * Tier(Layout.Depth), trap.At);
                    if (!h.IsAlive && h.Team == Team.Enemies) EnvKillRpc(h.Center, 1);
                }
            }
        }

        public void ServerCheckShove(Health target, Vector3 velocity, ulong by)
        {
            if (Layout == null || target == null || !target.IsAlive) return;
            if (!target.TryGetComponent<EnemyBrain>(out var brain) || brain is BossBrain) return;
            var from = target.transform.position;
            var flat = velocity;
            flat.y = 0f;
            var to = from + flat.normalized * Mathf.Min(4f, flat.magnitude * 0.3f);
            var room = Layout.RoomAt(to, 0.5f);
            if (room != null && room.Feature == RoomFeature.Chasm && room.Pit.Contains(new Vector2(to.x, to.z)) &&
                Mathf.Abs(to.x - room.Center.x) > 1.4f && Mathf.Abs(to.z - room.Center.z) > 1.4f)
            {
                brain.ServerFallIntoAbyss(to, by);
                if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(by, out var kicker) && kicker.TryGetComponent<Progression.PlayerProgress>(out var kp)) kp.AbyssKicks.Value++;
                AbyssFxRpc(to);
                return;
            }

            if (Physics.Raycast(to + Vector3.up * 0.6f, Vector3.down, out var ground, 12f, ~0, QueryTriggerInteraction.Ignore) &&
                ground.point.y < from.y - 1.2f && NavMesh.SamplePosition(ground.point, out var land, 1.5f, NavMesh.AllAreas))
                brain.ServerFallTo(land.position, (from.y - land.position.y) * 0.06f, by);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EnvKillRpc(Vector3 at, byte how) =>
            UI.GameHud.ShowCombo(at + Vector3.up * 0.6f, how == 0 ? "SLICED" : "SCORCHED", how == 0 ? new Color(0.9f, 0.9f, 1f) : new Color(1f, 0.5f, 0.2f));

        [Rpc(SendTo.ClientsAndHost)]
        private void AbyssFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Rattle, at, 1f, 0.6f);
            UI.GameHud.ShowCombo(at + Vector3.up * 1.5f, "INTO THE ABYSS", new Color(1f, 0.45f, 0.2f));
        }

        private readonly Dictionary<Health, float> bladeHitAt = new Dictionary<Health, float>();

        private void TickPendulums()
        {
            if (Layout == null) return;
            double now = Pendulum.Clock;
            foreach (var room in Layout.Rooms)
            {
                if (room.Feature != RoomFeature.Pendulums) continue;
                bool occupied = false;
                foreach (var p in PlayerLife.All) if (p != null && room.Contains(p.transform.position, 1f)) { occupied = true; break; }
                if (!occupied) continue;
                for (int i = 0; i < room.FeatureSpots.Count; i++)
                {
                    var blade = Pendulum.BladeCenter(room, i, now);
                    foreach (var h in Health.All)
                    {
                        if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                        if ((h.Center - blade).sqrMagnitude > Pendulum.HitRadius * Pendulum.HitRadius) continue;
                        if (bladeHitAt.TryGetValue(h, out float t) && Time.time - t < 0.9f) continue;
                        bladeHitAt[h] = Time.time;
                        var push = Pendulum.SwingDirection(room, i, now);
                        if (h.Team == Team.Players)
                        {
                            h.Unavoidable().ServerDamage(TrapHurt(46f + 15f * Tier(Layout.Depth)), blade);
                            if (h.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(push * 13f + Vector3.up * 3.5f);
                        }
                        else
                        {
                            h.ServerDamage(110f + 35f * Tier(Layout.Depth), blade);
                            if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerKnockback(push * 9f);
                            if (!h.IsAlive) EnvKillRpc(h.Center, 0);
                        }
                        BladeHitFxRpc(h.Center);
                    }
                }
            }
        }

        private readonly Dictionary<int, float> pyrePhase = new Dictionary<int, float>();
        private readonly Dictionary<Health, float> hazardHitAt = new Dictionary<Health, float>();
        private float nextCoreBurn;

        private bool Occupied(DungeonLayout.Room room)
        {
            foreach (var p in PlayerLife.All) if (p != null && room.Contains(p.transform.position, 1f)) return true;
            return false;
        }

        public static float TrapHurt(float damage) => damage * Difficulty.Level.Damage;

        private static readonly System.Collections.Generic.List<Health> jetScratch = new System.Collections.Generic.List<Health>();
        private static System.Collections.Generic.List<Health> JetScratch() { jetScratch.Clear(); jetScratch.AddRange(Health.All); return jetScratch; }

        public static float TrapBite(Health h) => (h.Max.Value * TrapHealthShare + h.Defense * Health.DefenseWeight)
            * (h.TryGetComponent<Progression.PlayerProgress>(out var tp) ? tp.TrapScale : 1f);

        public const float TrapHealthShare = 1f / 3f;

        private void HazardHit(Health h, Vector3 from, float playerDamage, float enemyDamage, Vector3 push, byte how, float rehit, float pin = 0f)
        {
            if (hazardHitAt.TryGetValue(h, out float t) && Time.time - t < rehit) return;
            hazardHitAt[h] = Time.time;
            if (h.Team == Team.Players)
            {
                h.Unavoidable().ServerDamage(TrapHurt(playerDamage), from);
                if (pin > 0f && h.IsAlive && h.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerRoot(pin);
                if (push.sqrMagnitude > 0.01f && h.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(push);
            }
            else
            {
                h.ServerDamage(enemyDamage, from);
                if (push.sqrMagnitude > 0.01f && h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerKnockback(new Vector3(push.x, 0f, push.z));
                if (!h.IsAlive) EnvKillRpc(h.Center, how);
            }
        }

        private void TickRoomHazards()
        {
            if (Layout == null) return;
            double now = Pendulum.Clock;
            float tier = Tier(Layout.Depth);
            bool coreTick = Time.time >= nextCoreBurn;
            if (coreTick) nextCoreBurn = Time.time + 0.5f;
            foreach (var room in Layout.Rooms)
            {
                if (room.Feature == RoomFeature.Pyre)
                {
                    float ph = PyreFire.Phase(room, now);
                    float last = pyrePhase.TryGetValue(room.Id, out var lp) ? lp : ph;
                    pyrePhase[room.Id] = ph;
                    if (!Occupied(room)) continue;
                    var c = room.Center;
                    foreach (var h in new List<Health>(Health.All))
                    {
                        if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                        var flat = h.transform.position - c;
                        flat.y = 0f;
                        float dist = flat.magnitude;
                        if (ph < last && dist < PyreFire.EruptRadius && h.transform.position.y < c.y + 2.5f)
                            HazardHit(h, c, 62f + 20f * tier, 160f + 40f * tier, flat.normalized * 11f + Vector3.up * 3.5f, 1, 0.2f);
                        else if (coreTick && dist < PyreFire.CoreRadius)
                        {
                            if (h.Team == Team.Players) h.Unavoidable().ServerDamage(TrapHurt(12f + 4f * tier), c);
                            else { h.ServerDamage(30f + 10f * tier, c); if (!h.IsAlive) EnvKillRpc(h.Center, 1); }
                        }
                    }
                }
                else if (room.Feature == RoomFeature.Gauntlet)
                {
                    if (!Occupied(room)) continue;
                    int n = SpikeGauntlet.Rows(room);
                    for (int i = 0; i < n; i++)
                    {
                        if (!SpikeGauntlet.Up(room, i, now)) continue;
                        foreach (var h in new List<Health>(Health.All))
                        {
                            if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                            if (!SpikeGauntlet.OnRow(room, i, h.transform.position - Vector3.up * 0.05f)) continue;

                            HazardHit(h, h.transform.position, 44f + 13f * tier, 100f + 28f * tier, Vector3.up * 5f, 0, SpikeGauntlet.UpSeconds + 0.1f, 0.5f);
                        }
                    }
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BladeHitFxRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Hit, at, 1f, 0.7f);
            Art.Vfx.Embers(at, 8, new Color(1f, 0.75f, 0.45f), 5f, 0.04f, 0.4f);
        }

        public readonly NetworkVariable<bool> RitualActive = new NetworkVariable<bool>();
        private DungeonLayout.Room ritualRoom;
        private readonly List<Breakable> ritualBraziers = new List<Breakable>();
        private float nextRitualSummon;
        private int ritualSummons;

        public static bool InRitualWard(Vector3 at)
        {
            if (Instance == null || !Instance.RitualActive.Value || Layout == null) return false;
            var room = Layout.RoomAt(at, 0.5f);
            return room != null && room.Feature == RoomFeature.Ritual;
        }

        private void TickRitual()
        {
            if (!RitualActive.Value || ritualRoom == null) return;
            ritualBraziers.RemoveAll(b => b == null || !b.IsSpawned);
            if (ritualBraziers.Count == 0)
            {
                RitualActive.Value = false;
                TitleCardRpc("THE RITUAL IS BROKEN", "The dead stagger, unwarded");
                foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                    if (!e.IsDead && ritualRoom.Contains(e.transform.position, 1f)) e.ServerStun(2.2f);
                ServerDropEmbers(ritualRoom.Center + Vector3.up * 0.5f, 6);
                return;
            }

            bool occupied = false;
            foreach (var p in PlayerLife.All) if (p != null && ritualRoom.Contains(p.transform.position)) { occupied = true; break; }
            if (!occupied || ritualSummons >= 4) return;
            if (nextRitualSummon <= 0f) { nextRitualSummon = Time.time + 6f; return; }
            if (Time.time < nextRitualSummon) return;
            nextRitualSummon = Time.time + 13f;
            ritualSummons++;
            var at = ritualRoom.Center + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * 2.5f;
            if (NavMesh.SamplePosition(at, out var hit, 3f, NavMesh.AllAreas)) at = hit.position;
            var obj = Spawn(skeletonPrefab, at, Quaternion.identity);
            var brain = obj.GetComponent<EnemyBrain>();
            brain.ServerScale(Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count), Layout.Depth);
            spawned.Add(obj);
            RiseFxRpc(new[] { at });
        }

        public static float WaterSlow(Vector3 at, bool player = false)
        {
            if (Layout == null) return 1f;
            float tide = TideLevel;
            if (tide > 0.3f && at.y < tide - 0.12f) return Mathf.Lerp(1f, player ? 0.5f : 0.8f, Thickness);
            var room = Layout.RoomAt(at, 0f);
            return room != null && DungeonLayout.InCisternWater(room, at) ? 0.68f : 1f;
        }

        public static bool InWater(Vector3 at) => WaterSlow(at) < 1f;

        public static bool LandsInWater(Vector3 at)
        {
            var surface = WaterSurface(at);
            return surface.HasValue && at.y <= surface.Value + 0.25f;
        }

        public static float? WaterSurface(Vector3 at)
        {
            if (Layout == null || InLobby) return null;
            float sheet = DungeonTheme.ForDepth(Layout.Depth).WaterDepth;
            if (sheet > 0f) return Mathf.Max(sheet, TideLevel);
            var room = Layout.RoomAt(at, 0f);
            return room != null && DungeonLayout.InCisternWater(room, at) ? -0.08f : (float?)null;
        }

        private void TickBossArena()
        {
            if (boss == null || Layout == null) return;
            if (bossSealed && Layout.BossRoom != null && Layout.BossRoom.Quirk == BossQuirk.Sanctum) TickSanctum();
            if (bossSealed)
            {
                if (boss.IsDead) SetGates(bossGates, false);
                return;
            }

            bool allIn = AllStandingInside(Layout.BossRoom, 1f);
            if (allIn)
            {
                bossSealed = true;
                BossSealedSync.Value = true;
                boss.Dormant = false;
                QuestCargoAtSeal();
                SetGates(bossGates, true);
                bool hallowed = Hallowed.Value;
                if (hallowed)
                {
                    var bh = boss.GetComponent<Combat.Health>();
                    bh.ServerSetFraction(HallowedHealth);
                    bh.IncomingModifiers.Insert(0, (dmg, from) => dmg * HallowedVulnerability);
                }

                if (Layout.BossRoom.Quirk == BossQuirk.Sanctum)
                    boss.GetComponent<Combat.Health>().IncomingModifiers.Insert(0, (dmg, from) => dmg * (1f + ArenaEngine.SanctumPerBrazier * ArenaEngine.LitBraziers));
                nextSnuff = Time.time + 15f;
                string quirk = Layout.BossRoom.Quirk switch
                {
                    BossQuirk.Pylons => "  PYLONS heal it: break them.",
                    BossQuirk.Ballistas => "  BALLISTAS stand ready: loose them (F).",
                    BossQuirk.Chandelier => "  A CHANDELIER hangs over the lit circle: lure it under, cut the chain (F).",
                    BossQuirk.Sanctum => "  Light the SANCTUM braziers: each one makes it take more damage.",
                    _ => "",
                };
                boss.ServerEntrance();
                BossIntroRpc(boss.DisplayName.ToUpperInvariant(), boss.Epithet, (hallowed ? "HALLOWED by the Reliquary: it wakes at " + Mathf.RoundToInt(HallowedHealth * 100f) + "% and takes +10% damage.  " : "") + boss.Hint + quirk);
            }
            else TellWhoHoldsTheGuardian();
        }

        private void TellWhoHoldsTheGuardian()
        {
            var outside = new List<string>();
            int inside = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value) continue;
                if (Layout.BossRoom.Contains(p.transform.position, -1f)) inside++;
                else outside.Add(p.name);
            }
            if (inside == 0 || outside.Count == 0) { guardianWaitSince = -1f; return; }
            if (guardianWaitSince < 0f) { guardianWaitSince = Time.time; nextGuardianWaitNote = Time.time + GuardianWaitGrace; }
            if (Time.time < nextGuardianWaitNote) return;
            nextGuardianWaitNote = Time.time + GuardianWaitRepeat;
            ServerFeedAll("The guardian sleeps until all of you are inside. Still outside: " + string.Join(", ", outside), new Color(1f, 0.8f, 0.45f));
        }

        private const float GuardianWaitGrace = 10f, GuardianWaitRepeat = 20f;
        private float guardianWaitSince = -1f, nextGuardianWaitNote;

        private void TickAmbush()
        {
            if (ambush == null || ambush.Wave >= 3) return;
            var room = ambush.Room;

            if (ambush.Wave == 0)
            {
                int inside = InAmbushTrigger(room);
                if (inside == 0) return;
                ambush.Party = inside;
                Debug.Log("[Director] ambush sprung on " + inside + " in room " + room.Id + " at " + room.Center + " (" + string.Join(", ", PlayerLife.All.Where(p => p != null).Select(p => p.name + " " + p.transform.position)) + ")");
                ShoveUnderGates(ambush.Gates, room);
                SetGates(ambush.Gates, true);
                TitleCardRpc("AMBUSH!", "Survive the dead");
                SpawnWave(1);
                return;
            }

            if (!ambush.Opened && !AnyoneStandingIn(room))
            {
                ambush.Opened = true;
                Debug.Log("[Director] ambush gates lift: no one standing in room " + room.Id + " (" + string.Join(", ", PlayerLife.All.Where(p => p != null).Select(p => p.name + " " + p.transform.position + " downed " + p.Downed.Value)) + ")");
                SetGates(ambush.Gates, false);
                TitleCardRpc("THE GATES LIFT", "Go to them");
            }

            float waveAge = Time.time - ambush.WaveAt;
            ambush.Alive.RemoveAll(e => e == null || e.IsDead);

            if (waveAge > 8f)
                ambush.Alive.RemoveAll(e =>
                {
                    if (room.Contains(e.transform.position, 4f)) return false;
                    Debug.Log("[Director] ambusher " + e.name + " left the room at " + e.transform.position + ": it no longer holds the gates");
                    return true;
                });
            bool overdue = waveAge > AmbushWaveLimit;
            if (ambush.Alive.Count > 0 && !overdue) return;
            if (overdue && ambush.Alive.Count > 0) { ambush.Alive.Clear(); ambush.Wave = 2; }

            if (ambush.Wave == 1)
            {
                SpawnWave(2);
                return;
            }

            ambush.Wave = 3;
            SetGates(ambush.Gates, false);
            if (chestPrefab != null)
            {
                var chest = Spawn(chestPrefab, DropSpots.ClearForChest(room.Center, Quaternion.Euler(0f, 180f, 0f), room), Quaternion.Euler(0f, 180f, 0f));
                chest.GetComponent<Chest>().SetLootPrefab(lootPrefab);
            }
            TitleCardRpc("AMBUSH BROKEN", "A chest rises from the floor");
        }

        private void SpawnWave(int wave)
        {
            ambush.Wave = wave;
            ambush.WaveAt = Time.time;
            var room = ambush.Room;
            int players = Mathf.Clamp(ambush.Party, 1, Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count));
            int count = Mathf.RoundToInt((wave == 1 ? 5 : 3) * Difficulty.SpawnCount(players) * Difficulty.Level.SpawnMultiplier);
            var theme = DungeonTheme.ForDepth(Layout.Depth);
            bool brute = false, necro = true;
            var points = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                var p = DropSpots.Stand(room.Center, room.Center + new Vector3(Random.Range(-1f, 1f) * (room.Size.x / 2f - 2f), 0f, Random.Range(-1f, 1f) * (room.Size.y / 2f - 2f)), 0.3f, 1.2f);
                points[i] = p;

                var prefab = PickEnemy(Layout.Depth, theme, ref brute, ref necro, out int _);

                var obj = SpawnEntering(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), 0.6f, 0.15f);
                points[i] = obj.transform.position;
                var brain = obj.GetComponent<EnemyBrain>();
                brain.ServerScale(players, Layout.Depth);

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

        [Rpc(SendTo.ClientsAndHost)]
        private void TitleCardRpc(string title, string subtitle)
        {
            UI.GameHud.ShowTitleCard(title, subtitle);
            Art.Sfx.Play2D(Art.Sound.Summon, 0.8f);
        }

        public void ServerFeedAll(string text, Color color) => FeedAllRpc(text, color);

        [Rpc(SendTo.ClientsAndHost)]
        private void FeedAllRpc(string text, Color color) => UI.GameHud.Feed(text, color);

        [Rpc(SendTo.ClientsAndHost)]
        private void BossIntroRpc(string name, string epithet, string hint)
        {
            UI.GameHud.ShowBossIntro(name, epithet, hint);
            Art.Sfx.Play2D(Art.Sound.Slam, 0.9f);
            Art.Sfx.Play2D(Art.Sound.Summon, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RiseFxRpc(Vector3[] points)
        {
            foreach (var p in points)
            {
                Telegraph.Show(p, 1.1f, 0.4f, null, new Color(0.15f, 0.05f, 0.2f, 0.6f));
            }
            if (points.Length > 0) Art.Sfx.Play(Art.Sound.BoneBreak, points[0], 1f, 0.7f);
        }
    }
}
