using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// ROOM CENTREPIECES (2026-11-13, the user: "i want to see more things in the middle of rooms, currently you are
    /// sprinkling things on the sides"; and: "whatever you plan to add, it must serve a gameplay purpose, or work a
    /// decoration. braziers to light also", cover/obstacles, set-dressing landmarks). Plain combat rooms of 16 m or more get
    /// one thing in the middle, picked from the room itself (the same on every peer, no network traffic):
    /// <list type="bullet">
    /// <item><b>Hearth</b>: a cold iron brazier (the networked <see cref="HearthBrazier"/>, spawned by the director): F lights
    ///   it; lit, it lights the room, wards off frostbite, mends those who stand by it and scorches the dead pushed into it.</item>
    /// <item><b>Bell</b>: a great cracked bronze bell fallen from its frame, on its side - full cover, shots and all.</item>
    /// <item><b>Columns</b>: a ring of broken columns round a slab altar with candles: cover that you can circle through.</item>
    /// <item><b>Statue</b>: a toppled giant - the head and a shoulder - to put between you and archers.</item>
    /// <item><b>Well</b>: a round stone well, chest high: blocks the rush but not the shot.</item>
    /// </list>
    /// Solid (the dressing batcher builds colliders; the navmesh is baked round them). Off in smoke runs (<see cref="Off"/>),
    /// which teleport players to room centres.
    /// </summary>
    public static class Centrepieces
    {
        public enum Kind { None, Hearth, Bell, Columns, Statue, Well }
        /// <summary>Smoke tests: no centrepieces (they stand players at room centres).</summary>
        public static bool Off;
        /// <summary>The radius each one takes up (clutter and loot keep out of it).</summary>
        public static float Radius(Kind k) => k switch { Kind.Hearth => 1.2f, Kind.Bell => 2.1f, Kind.Columns => 3.6f, Kind.Statue => 2.4f, Kind.Well => 1.4f, _ => 0f };

        /// <summary>Which centrepiece a room has (deterministic from the room).</summary>
        public static Kind Of(DungeonLayout.Room room)
        {
            // Every combat room has a mechanic (DungeonLayout hands them out), so "plain" is rare: the ones whose middle is
            // free are plain rooms, great halls (open colonnades), crypts (coffins at the walls) and lightless halls.
            if (Off || room == null || room.Kind != RoomKind.Combat || room.Size.x < 16f || room.Size.y < 16f) return Kind.None;
            var f = room.Feature;
            if (f != RoomFeature.None && f != RoomFeature.GreatHall && f != RoomFeature.Crypt && f != RoomFeature.Lightless) return Kind.None;
            unchecked
            {
                int h = room.Id * 73856093 ^ Mathf.RoundToInt(room.Center.x * 7f) * 19349663 ^ Mathf.RoundToInt(room.Center.z * 13f) * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                int roll = (h & 0x7fffffff) % 100;
                if (roll < 22) return Kind.None;
                // In a lightless hall the only thing worth finding in the dark is a fire.
                if (f == RoomFeature.Lightless) return Kind.Hearth;
                if (roll < 44) return Kind.Hearth;
                if (roll < 59) return Kind.Bell;
                if (roll < 74) return Kind.Columns;
                if (roll < 88) return Kind.Statue;
                return Kind.Well;
            }
        }

        /// <summary>Builds the static part of a room's centrepiece (the hearth's brazier is a networked object).</summary>
        public static void Build(DungeonLayout.Room room, Kind kind, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            float yaw = (float)rng.NextDouble() * 360f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            switch (kind)
            {
                case Kind.Hearth:
                    // A worn hexagonal hearthstone under the brazier, ash ground into it.
                    b.Add(MeshKit.Cylinder(1.25f, 1.35f, 0.12f, 6), Mat.Flagstone, Matrix4x4.TRS(c + Vector3.up * 0.0f, rot, Vector3.one));
                    b.Add(MeshKit.Cylinder(0.9f, 0.9f, 0.01f, 12), Mat.Dark, Matrix4x4.TRS(c + Vector3.up * 0.125f, rot, Vector3.one));
                    break;
                case Kind.Bell:
                {
                    // On its side, mouth toward the room, a crack up the waist; the broken frame beside it.
                    var bell = MeshKit.Lathe("greatBell", new[]
                    {
                        new Vector2(0.01f, 2.3f), new Vector2(0.5f, 2.25f), new Vector2(0.8f, 1.95f), new Vector2(0.95f, 1.4f),
                        new Vector2(1.15f, 0.7f), new Vector2(1.45f, 0.18f), new Vector2(1.55f, 0f),
                    }, 18, true);
                    var lay = rot * Quaternion.Euler(0f, 0f, 90f);
                    var at = c + Vector3.up * 1.3f + rot * new Vector3(-1.1f, 0f, 0f);
                    b.Add(bell, Mat.Gold + "#" + ColorUtility.ToHtmlStringRGB(new Color(0.62f, 0.48f, 0.3f)), Matrix4x4.TRS(at, lay, Vector3.one));
                    b.Add(MeshKit.Lathe("greatBellIn", new[] { new Vector2(1.4f, 0.15f), new Vector2(1.05f, 0.7f), new Vector2(0.75f, 1.6f), new Vector2(0.01f, 2.1f) }, 18, true), Mat.Gold + "#" + ColorUtility.ToHtmlStringRGB(new Color(0.2f, 0.14f, 0.09f)), Matrix4x4.TRS(at, lay, Vector3.one));
                    b.AddBox(at + rot * new Vector3(0.6f, 0.55f, 0.7f), new Vector3(0.05f, 0.9f, 0.05f), Mat.Dark, rot * Quaternion.Euler(30f, 0f, 20f));
                    // The frame: two posts and a fallen beam.
                    var f = c + rot * new Vector3(1.6f, 0f, 1.2f);
                    b.AddBox(f + Vector3.up * 1.1f, new Vector3(0.35f, 2.2f, 0.35f), Mat.Wood, rot);
                    b.AddBox(f + rot * new Vector3(0.4f, 0.22f, -1.4f), new Vector3(0.3f, 0.3f, 3.2f), Mat.Wood, rot * Quaternion.Euler(0f, 18f, 6f));
                    Props.Rubble(b, c + rot * new Vector3(0.6f, 0f, -1.6f), rng);
                    // Someone keeps votive candles at its lip: the bell was the only light in its room in the 2026-11-13 shots.
                    Props.Candles(lights, c + rot * new Vector3(0.2f, 0f, 1.8f), new Color(1f, 0.72f, 0.4f));
                    break;
                }
                case Kind.Columns:
                {
                    // Six broken drums of column round a slab altar; candles stuck to the slab.
                    for (int i = 0; i < 6; i++)
                    {
                        if (i == 1 || i == 4) { if (rng.NextDouble() < 0.5) continue; }
                        float a = yaw + i * 60f;
                        var p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 3.2f;
                        float hgt = 0.9f + (float)rng.NextDouble() * 1.9f;
                        b.Add(MeshKit.Cylinder(0.42f, 0.46f, hgt, 10), Mat.StoneWall, Matrix4x4.TRS(p, Quaternion.Euler(0f, a, 0f), Vector3.one));
                        b.Add(MeshKit.Cylinder(0.55f, 0.6f, 0.22f, 10), Mat.StoneWall, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one));
                        // A fallen drum beside some of them.
                        if (rng.NextDouble() < 0.4)
                            b.Add(MeshKit.Cylinder(0.4f, 0.4f, 0.8f, 10), Mat.StoneWall, Matrix4x4.TRS(p + Quaternion.Euler(0f, a + 90f, 0f) * Vector3.forward * 0.9f + Vector3.up * 0.4f, Quaternion.Euler(0f, a, 90f), Vector3.one));
                    }
                    b.AddBox(c + Vector3.up * 0.45f, new Vector3(1.8f, 0.9f, 0.9f), Mat.StoneWall, rot);
                    b.AddBox(c + Vector3.up * 0.93f, new Vector3(2f, 0.08f, 1.05f), Mat.StoneWall, rot);
                    Props.Candles(lights, c + Vector3.up * 0.97f + rot * new Vector3(0.6f, 0f, 0f), new Color(1f, 0.72f, 0.4f));
                    break;
                }
                case Kind.Statue:
                {
                    // A toppled giant: the helmed head on its cheek and a broken shoulder with the arm stump.
                    var head = c + rot * new Vector3(-0.8f, 0f, 0f);
                    b.Add(MeshKit.Ball(1.15f, 12, 8), Mat.Marble, Matrix4x4.TRS(head + Vector3.up * 0.95f, rot * Quaternion.Euler(0f, 0f, 70f), new Vector3(1f, 1.15f, 1f)));
                    b.Add(MeshKit.Cylinder(1.2f, 1.05f, 0.9f, 12), Mat.Marble, Matrix4x4.TRS(head + Vector3.up * 1.0f + rot * new Vector3(-0.3f, 0f, 0f), rot * Quaternion.Euler(0f, 0f, 70f), Vector3.one));
                    b.AddBox(head + Vector3.up * 0.5f + rot * new Vector3(0.95f, 0f, 0f), new Vector3(0.12f, 0.22f, 1.1f), Mat.Dark, rot);
                    var sh = c + rot * new Vector3(1.4f, 0f, 0.6f);
                    b.AddBox(sh + Vector3.up * 0.7f, new Vector3(1.6f, 1.4f, 1.3f), Mat.Marble, rot * Quaternion.Euler(0f, 25f, 8f));
                    b.Add(MeshKit.Cylinder(0.45f, 0.4f, 1.6f, 10), Mat.Marble, Matrix4x4.TRS(sh + Vector3.up * 0.45f + rot * new Vector3(0.6f, 0f, -1.1f), rot * Quaternion.Euler(90f, 30f, 0f), Vector3.one));
                    Props.Rubble(b, c + rot * new Vector3(0.2f, 0f, -1.8f), rng);
                    break;
                }
                case Kind.Well:
                {
                    var stone = MeshKit.Lathe("roomWell", new[] { new Vector2(1.3f, 0f), new Vector2(1.3f, 1.05f), new Vector2(1.0f, 1.05f), new Vector2(1.0f, 0.1f) }, 16, true);
                    b.Add(stone, Mat.StoneWall, Matrix4x4.TRS(c, rot, Vector3.one));
                    b.Add(MeshKit.Cylinder(1.0f, 1.0f, 0.02f, 16), Mat.Dark, Matrix4x4.TRS(c + Vector3.up * 0.6f, Quaternion.identity, Vector3.one));
                    // A winch frame and a hanging bucket.
                    foreach (float s in new[] { -1f, 1f }) b.AddBox(c + rot * new Vector3(s * 1.15f, 1.6f, 0f), new Vector3(0.16f, 1.3f, 0.16f), Mat.Wood, rot);
                    b.AddBox(c + rot * new Vector3(0f, 2.2f, 0f), new Vector3(2.5f, 0.14f, 0.14f), Mat.Wood, rot);
                    b.Add(MeshKit.Cylinder(0.18f, 0.15f, 0.3f, 8), Mat.Wood, Matrix4x4.TRS(c + rot * new Vector3(0.3f, 1.35f, 0f), rot, Vector3.one));
                    break;
                }
            }
        }
    }
}
