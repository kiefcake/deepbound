using System.Collections.Generic;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Places the small life of a level (see <see cref="Critter"/>): rats along the walls of rooms,
    /// bats under the high ceilings, ravens on the camp's walls and road. Local and deterministic per
    /// floor seed so a party sees roughly the same creatures.
    /// </summary>
    public static class AmbientLife
    {
        public static void Populate(Transform parent, DungeonLayout layout)
        {
            var rng = new System.Random(layout.Seed ^ 0x5a7);
            int rats = 0, bats = 0;
            foreach (var room in layout.Rooms)
            {
                if (room.Kind == RoomKind.Boss) continue;
                var half = room.Size / 2f - new Vector2(0.9f, 0.9f);
                var rect = new Rect(room.Center.x - half.x, room.Center.z - half.y, half.x * 2f, half.y * 2f);
                int n = rng.Next(0, 3);
                for (int i = 0; i < n && rats < 16; i++, rats++)
                {
                    var c = Critter.Make(parent, Critter.Kind.Rat, room.Center);
                    c.Bounds = rect;
                    c.Floor = room.Center.y + 0.01f;
                }
                // The Drowned Vaults leak (and the Ossuary, a little).
                int leaks = layout.Depth == 1 ? 3 : layout.Depth == 0 ? 1 : 0;
                for (int i = 0; i < leaks; i++)
                {
                    var at = new Vector3(Mathf.Lerp(rect.xMin, rect.xMax, (float)rng.NextDouble()), room.Center.y + room.Height - 0.2f, Mathf.Lerp(rect.yMin, rect.yMax, (float)rng.NextDouble()));
                    var go = new GameObject("Drip");
                    go.transform.SetParent(parent, false);
                    go.transform.position = at;
                    go.AddComponent<Drip>().Floor = room.Center.y;
                }
                if (room.Height >= 7f && bats < 10)
                {
                    int b = 2 + rng.Next(0, 3);
                    for (int i = 0; i < b; i++, bats++)
                    {
                        var c = Critter.Make(parent, Critter.Kind.Bat, room.Center + Vector3.up * (room.Height - 1.5f));
                        c.Bounds = rect;
                        c.LoopHeight = room.Center.y + room.Height - 1.6f - (float)rng.NextDouble() * 1.2f;
                        c.LoopRadius = Mathf.Min(half.x, half.y) * (0.4f + (float)rng.NextDouble() * 0.4f);
                    }
                }
            }
        }

        /// <summary>Ravens of Castle Ravenhold: on the battlements and pecking about the road.</summary>
        public static void Ravens(Transform parent, Vector3 origin, float wallRadius, float wallHeight)
        {
            var perches = new List<Vector3>();
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f + 0.13f;
                perches.Add(origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (wallRadius - 0.5f) + Vector3.up * wallHeight);
            }
            // Down on the cobbles of the road, wherever there is nothing in the way (never in a brazier).
            foreach (var g in new[] { new Vector3(0.3f, 0f, 2.6f), new Vector3(-0.4f, 0f, 8.8f), new Vector3(0.5f, 0f, -10.5f), new Vector3(-0.6f, 0f, -4.2f), new Vector3(1.1f, 0f, 11.5f) })
            {
                var p = origin + g;
                if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
                if (Physics.CheckSphere(p + Vector3.up * 0.45f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                perches.Add(p);
            }
            var arr = perches.ToArray();
            int[] start = { 0, 3, 7, 10, 14, 16 };
            foreach (int s in start)
            {
                var c = Critter.Make(parent, Critter.Kind.Raven, arr[s % arr.Length]);
                c.Perches = arr;
            }
        }
    }
}
