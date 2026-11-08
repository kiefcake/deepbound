using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// DUNGEON INTERACTABLES (2026-11-08, the user: "more interactive things to do ... in the dungeons"; the plan in
    /// docs/ACTIVITIES_PLAN.md section 3). Placed once per floor on the server with their own seeded stream (so the older
    /// placements keep their seeds):
    /// <list type="bullet">
    /// <item>OIL: in some combat rooms, oil jars by the walls and a fire bowl (<see cref="Breakable"/>, <see cref="OilSlick"/>).</item>
    /// <item>CHANDELIERS: over the middle of some combat rooms, the rope to a wall cleat (<see cref="Chandelier"/>).</item>
    /// <item>CRACKED PILLARS: one or two standing clear of the walls (<see cref="Breakable.Kind.CrackedPillar"/>).</item>
    /// <item>A VIGIL: four cold candles round one combat room (<see cref="VigilCandle"/>), more likely in the Black Cathedral.</item>
    /// <item>A PRISONER: from floor 2, sometimes, in a plain side room (<see cref="PrisonerCage"/>).</item>
    /// </list>
    /// </summary>
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject oilSlickPrefab, chandelierPrefab, vigilCandlePrefab, prisonerPrefab;
        private byte nextVigil;

        /// <summary>What the camp shows: trophies and keepsakes (<see cref="CampMemory"/>), set by the host at camp.</summary>
        public readonly NetworkVariable<uint> CampKeeps = new NetworkVariable<uint>();

        /// <summary>The camp's weather this visit (<see cref="CampWeather.Kind"/>), rolled by the host on each return.</summary>
        public readonly NetworkVariable<byte> Weather = new NetworkVariable<byte>();

        /// <summary>Rooms where props on the floor would fight the room's own mechanic.</summary>
        private static bool PlainEnough(DungeonLayout.Room r) =>
            r.Feature == RoomFeature.None || r.Feature == RoomFeature.GreatHall || r.Feature == RoomFeature.Crypt || r.Feature == RoomFeature.Ritual || r.Feature == RoomFeature.Lightless;

        /// <summary>Server: a slick of oil here (lit at once if <paramref name="lit"/>).</summary>
        public OilSlick ServerSpawnOil(Vector3 at, bool lit, ulong by, bool coals = false)
        {
            if (oilSlickPrefab == null) return null;
            var spot = NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas) ? hit.position : at;
            var obj = Instantiate(oilSlickPrefab, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            obj.Spawn(destroyWithScene: true);
            var s = obj.GetComponent<OilSlick>();
            if (lit) s.ServerIgnite(by);
            return s;
        }

        private void SpawnInteractables(DungeonLayout layout)
        {
            var rng = new System.Random(layout.Seed ^ 0x1e7e);
            nextVigil = 0;
            var combat = layout.Rooms.FindAll(r => r.Kind == RoomKind.Combat && PlainEnough(r) && !r.Peaceful && !IsAmbushRoom(r));
            int act = Act(layout.Depth);
            foreach (var room in combat)
            {
                // Oil: two or three jars and, mostly, a fire bowl to light them with.
                if (breakablePrefab != null && !TestMode && rng.NextDouble() < 0.35)
                {
                    int jars = 2 + rng.Next(2);
                    for (int i = 0; i < jars; i++) SpawnBreakable(Breakable.Kind.OilJar, WallSpot(room, rng));
                    if (rng.NextDouble() < 0.75) SpawnBreakable(Breakable.Kind.FireBowl, WallSpot(room, rng));
                }
                // A chandelier over the middle, its rope to a wall.
                if (chandelierPrefab != null && !TestMode && rng.NextDouble() < 0.3 && room.Feature != RoomFeature.Lightless)
                {
                    var hang = room.Center + new Vector3((float)rng.NextDouble() * 4f - 2f, 0f, (float)rng.NextDouble() * 4f - 2f);
                    if (NavMesh.SamplePosition(hang, out var hh, 2f, NavMesh.AllAreas)) hang = hh.position;
                    var cleat = WallSpot(room, rng);
                    var face = room.Center - cleat; face.y = 0f;
                    var obj = Instantiate(chandelierPrefab, cleat, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward));
                    obj.GetComponent<Chandelier>().HangAt.Value = hang;
                    obj.Spawn(destroyWithScene: true);
                }
                // Cracked pillars, standing clear of the walls so they have somewhere to fall.
                if (breakablePrefab != null && !TestMode && rng.NextDouble() < 0.35)
                {
                    int n = 1 + (room.Size.x > 24f && room.Size.y > 24f ? rng.Next(2) : 0);
                    for (int i = 0; i < n; i++)
                    {
                        float hx = Mathf.Max(1f, room.Size.x / 2f - 4.5f), hz = Mathf.Max(1f, room.Size.y / 2f - 4.5f);
                        var at = room.Center + new Vector3(((float)rng.NextDouble() * 2f - 1f) * hx, 0f, ((float)rng.NextDouble() * 2f - 1f) * hz);
                        if (Vector3.Distance(at, room.Center) < 2.5f) at += (at - room.Center).normalized * 2.5f + Vector3.right * 0.01f;
                        if (!NavMesh.SamplePosition(at, out var ph, 1.2f, NavMesh.AllAreas)) continue;
                        SpawnBreakable(Breakable.Kind.CrackedPillar, ph.position);
                    }
                }
            }
            // One vigil a floor (often, deep in the Cathedral; never in the ambush room).
            if (vigilCandlePrefab != null && combat.Count > 0 && !TestMode && (rng.NextDouble() < (act == 3 ? 0.85 : 0.45)))
            {
                var room = combat[rng.Next(combat.Count)];
                byte id = nextVigil++;
                for (int i = 0; i < VigilCandle.PerVigil; i++)
                {
                    float sx = (i % 2 == 0 ? -1f : 1f), sz = (i < 2 ? -1f : 1f);
                    var at = room.Center + new Vector3(sx * (room.Size.x / 2f - 3f), 0f, sz * (room.Size.y / 2f - 3f));
                    if (NavMesh.SamplePosition(at, out var ch, 2.5f, NavMesh.AllAreas)) at = ch.position;
                    var obj = Instantiate(vigilCandlePrefab, at, Quaternion.identity);
                    obj.GetComponent<VigilCandle>().Vigil.Value = id;
                    obj.Spawn(destroyWithScene: true);
                }
            }
            // A prisoner, from floor 2, in a plain side room.
            if (prisonerPrefab != null && !TestMode && layout.Depth >= 1 && rng.NextDouble() < 0.35)
            {
                var sides = layout.Rooms.FindAll(r => r.Kind == RoomKind.Side && !r.Special && !r.Peaceful && r.Feature == RoomFeature.None);
                if (sides.Count > 0)
                {
                    var room = sides[rng.Next(sides.Count)];
                    var at = WallSpot(room, rng);
                    var face = room.Center - at; face.y = 0f;
                    var obj = Instantiate(prisonerPrefab, at, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward));
                    obj.Spawn(destroyWithScene: true);
                }
            }
        }

        // ---- Tests ----
        public Chandelier ServerDebugChandelier(Vector3 cleat, Vector3 hang)
        {
            if (chandelierPrefab == null) return null;
            var obj = Instantiate(chandelierPrefab, cleat, Quaternion.identity);
            obj.GetComponent<Chandelier>().HangAt.Value = hang;
            obj.Spawn(destroyWithScene: true);
            return obj.GetComponent<Chandelier>();
        }

        public Breakable ServerDebugBreakable(Breakable.Kind kind, Vector3 at)
        {
            if (breakablePrefab == null) return null;
            var obj = Instantiate(breakablePrefab, at, Quaternion.identity);
            var b = obj.GetComponent<Breakable>();
            b.Type.Value = kind;
            b.SetLootPrefab(lootPrefab);
            obj.Spawn(destroyWithScene: true);
            return b;
        }

        public List<VigilCandle> ServerDebugVigil(Vector3 centre)
        {
            var list = new List<VigilCandle>();
            if (vigilCandlePrefab == null) return list;
            byte id = (byte)(200 + nextVigil++);
            for (int i = 0; i < VigilCandle.PerVigil; i++)
            {
                var obj = Instantiate(vigilCandlePrefab, centre + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 3f, Quaternion.identity);
                obj.GetComponent<VigilCandle>().Vigil.Value = id;
                obj.Spawn(destroyWithScene: true);
                list.Add(obj.GetComponent<VigilCandle>());
            }
            return list;
        }

        public PrisonerCage ServerDebugPrisoner(Vector3 at, Quaternion facing)
        {
            if (prisonerPrefab == null) return null;
            var obj = Instantiate(prisonerPrefab, at, facing);
            obj.Spawn(destroyWithScene: true);
            return obj.GetComponent<PrisonerCage>();
        }
    }
}
