using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        [SerializeField] private NetworkObject oilSlickPrefab, chandelierPrefab, vigilCandlePrefab, prisonerPrefab, hearthPrefab;

        public HearthBrazier ServerSpawnHearth(Vector3 at)
        {
            if (hearthPrefab == null) return null;
            var obj = Instantiate(hearthPrefab, at, Quaternion.identity);
            obj.Spawn(destroyWithScene: true);
            return obj.GetComponent<HearthBrazier>();
        }
        private byte nextVigil;

        public readonly NetworkVariable<uint> CampKeeps = new NetworkVariable<uint>();

        public readonly NetworkVariable<byte> Weather = new NetworkVariable<byte>();

        private static bool PlainEnough(DungeonLayout.Room r) =>
            r.Feature == RoomFeature.None || r.Feature == RoomFeature.GreatHall || r.Feature == RoomFeature.Crypt || r.Feature == RoomFeature.Ritual || r.Feature == RoomFeature.Lightless;

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

            foreach (var room in layout.Rooms)
                if (Centrepieces.Of(room) == Centrepieces.Kind.Hearth) ServerSpawnHearth(room.Center + Vector3.up * 0.12f);
            foreach (var room in combat)
            {
                if (breakablePrefab != null && !TestMode && rng.NextDouble() < 0.35)
                {
                    int jars = 2 + rng.Next(2);
                    for (int i = 0; i < jars; i++) SpawnBreakable(Breakable.Kind.OilJar, WallSpot(room, rng));
                    if (rng.NextDouble() < 0.75) SpawnBreakable(Breakable.Kind.FireBowl, WallSpot(room, rng));
                }

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

            if (vigilCandlePrefab != null && combat.Count > 0 && !TestMode && (rng.NextDouble() < (act == 3 ? 0.85 : 0.45)))
            {
                var room = combat[rng.Next(combat.Count)];
                byte id = nextVigil++;
                for (int i = 0; i < VigilCandle.PerVigil; i++)
                {
                    float sx = (i % 2 == 0 ? -1f : 1f), sz = (i < 2 ? -1f : 1f);
                    var at = DropSpots.Stand(room.Center, room.Center + new Vector3(sx * (room.Size.x / 2f - 3f), 0f, sz * (room.Size.y / 2f - 3f)), 0.3f, 2f);
                    var obj = Instantiate(vigilCandlePrefab, at, Quaternion.identity);
                    obj.GetComponent<VigilCandle>().Vigil.Value = id;
                    obj.Spawn(destroyWithScene: true);
                }
            }

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
