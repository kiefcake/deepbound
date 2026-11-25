using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    public static class Centrepieces
    {
        public enum Kind { None, Hearth, Bell, Columns, Statue, Well }

        public static bool Off;

        public static float Radius(Kind k) => k switch { Kind.Hearth => 1.2f, Kind.Bell => 2.1f, Kind.Columns => 3.6f, Kind.Statue => 2.4f, Kind.Well => 1.4f, _ => 0f };

        public static Kind Of(DungeonLayout.Room room)
        {
            if (Off || room == null || room.Kind != RoomKind.Combat || room.Size.x < 16f || room.Size.y < 16f) return Kind.None;
            var f = room.Feature;
            if (f != RoomFeature.None && f != RoomFeature.GreatHall && f != RoomFeature.Crypt && f != RoomFeature.Lightless) return Kind.None;
            unchecked
            {
                int h = room.Id * 73856093 ^ Mathf.RoundToInt(room.Center.x * 7f) * 19349663 ^ Mathf.RoundToInt(room.Center.z * 13f) * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                int roll = (h & 0x7fffffff) % 100;
                if (roll < 22) return Kind.None;

                if (f == RoomFeature.Lightless) return Kind.Hearth;
                if (roll < 44) return Kind.Hearth;
                if (roll < 59) return Kind.Bell;
                if (roll < 74) return Kind.Columns;
                if (roll < 88) return Kind.Statue;
                return Kind.Well;
            }
        }

        public static void Build(DungeonLayout.Room room, Kind kind, MeshKit.Batcher b, Transform lights, System.Random rng)
        {
            var c = room.Center;
            float yaw = (float)rng.NextDouble() * 360f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            switch (kind)
            {
                case Kind.Hearth:

                    b.Add(MeshKit.Cylinder(1.25f, 1.35f, 0.12f, 6), Mat.Flagstone, Matrix4x4.TRS(c + Vector3.up * 0.0f, rot, Vector3.one));
                    b.Add(MeshKit.Cylinder(0.9f, 0.9f, 0.01f, 12), Mat.Dark, Matrix4x4.TRS(c + Vector3.up * 0.125f, rot, Vector3.one));
                    break;
                case Kind.Bell:
                {
                    var bell = MeshKit.Lathe("greatBell", new[]
                    {
                        new Vector2(0.01f, 2.3f), new Vector2(0.5f, 2.25f), new Vector2(0.8f, 1.95f), new Vector2(0.95f, 1.4f),
                        new Vector2(1.15f, 0.7f), new Vector2(1.45f, 0.18f), new Vector2(1.55f, 0f),
                    }, 18, true);
                    var lay = rot * Quaternion.Euler(0f, 0f, 90f);
                    var at = c + Vector3.up * 1.3f + rot * new Vector3(-1.1f, 0f, 0f);

                    var bellGo = new GameObject("GreatBell");
                    bellGo.transform.SetParent(lights, false);
                    bellGo.transform.SetPositionAndRotation(at, lay);
                    bellGo.isStatic = true;
                    var outer = MeshKit.Part(bellGo.transform, "Bronze", bell, Mat.Marble, Vector3.zero);
                    ArtLibrary.Tint(outer.GetComponent<Renderer>(), new Color(0.62f, 0.46f, 0.26f));
                    var inner = MeshKit.Part(bellGo.transform, "Hollow", MeshKit.Lathe("greatBellIn", new[] { new Vector2(1.4f, 0.15f), new Vector2(1.05f, 0.7f), new Vector2(0.75f, 1.6f), new Vector2(0.01f, 2.1f) }, 18, true), Mat.Marble, Vector3.zero);
                    ArtLibrary.Tint(inner.GetComponent<Renderer>(), new Color(0.26f, 0.18f, 0.11f));
                    foreach (var c0 in bellGo.GetComponentsInChildren<Collider>()) Object.Destroy(c0);
                    bellGo.AddComponent<MeshCollider>().sharedMesh = bell;
                    bellGo.GetComponent<MeshCollider>().convex = true;
                    b.AddBox(at + rot * new Vector3(0.6f, 0.55f, 0.7f), new Vector3(0.05f, 0.9f, 0.05f), Mat.Dark, rot * Quaternion.Euler(30f, 0f, 20f));

                    var f = c + rot * new Vector3(1.6f, 0f, 1.2f);
                    b.AddBox(f + Vector3.up * 1.1f, new Vector3(0.35f, 2.2f, 0.35f), Mat.Wood, rot);
                    b.AddBox(f + rot * new Vector3(0.4f, 0.22f, -1.4f), new Vector3(0.3f, 0.3f, 3.2f), Mat.Wood, rot * Quaternion.Euler(0f, 18f, 6f));
                    Props.Rubble(b, c + rot * new Vector3(0.6f, 0f, -1.6f), rng);

                    Props.Candles(lights, c + rot * new Vector3(0.2f, 0f, 1.8f), new Color(1f, 0.72f, 0.4f));

                    Landmark(lights, c + Vector3.up * 2.9f + rot * new Vector3(0.8f, 0f, 1.2f), new Color(1f, 0.78f, 0.5f), 8f, 5f);
                    break;
                }
                case Kind.Columns:
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (i == 1 || i == 4) { if (rng.NextDouble() < 0.5) continue; }
                        float a = yaw + i * 60f;
                        var p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 3.2f;
                        float hgt = 0.9f + (float)rng.NextDouble() * 1.9f;
                        b.Add(MeshKit.Cylinder(0.42f, 0.46f, hgt, 10), Mat.StoneWall, Matrix4x4.TRS(p, Quaternion.Euler(0f, a, 0f), Vector3.one));
                        b.Add(MeshKit.Cylinder(0.55f, 0.6f, 0.22f, 10), Mat.StoneWall, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one));

                        if (rng.NextDouble() < 0.4)
                            b.Add(MeshKit.Cylinder(0.4f, 0.4f, 0.8f, 10), Mat.StoneWall, Matrix4x4.TRS(p + Quaternion.Euler(0f, a + 90f, 0f) * Vector3.forward * 0.9f + Vector3.up * 0.4f, Quaternion.Euler(0f, a, 90f), Vector3.one));
                    }
                    b.AddBox(c + Vector3.up * 0.45f, new Vector3(1.8f, 0.9f, 0.9f), Mat.StoneWall, rot);
                    b.AddBox(c + Vector3.up * 0.93f, new Vector3(2f, 0.08f, 1.05f), Mat.StoneWall, rot);
                    Props.Candles(lights, c + Vector3.up * 0.97f + rot * new Vector3(0.6f, 0f, 0f), new Color(1f, 0.72f, 0.4f));
                    Landmark(lights, c + Vector3.up * 2.8f, new Color(0.95f, 0.8f, 0.6f), 8.5f, 5f);
                    break;
                }
                case Kind.Statue:
                {
                    if (ModelKit.Has("gothic_statue"))
                    {
                        var fall = rot * Vector3.right;
                        var tilt = Quaternion.AngleAxis(74f, Vector3.Cross(Vector3.up, fall)) * Quaternion.LookRotation(rot * Vector3.forward);
                        if (b.AddModel("gothic_statue", 5.4f, Matrix4x4.TRS(c - fall * 2.4f + Vector3.down * 0.35f, tilt, Vector3.one)))
                        {
                            Props.Rubble(b, c - fall * 2.6f + rot * new Vector3(0f, 0f, 0.9f), rng);
                            Props.Rubble(b, c + fall * 2.2f + rot * new Vector3(0f, 0f, -0.8f), rng);
                            b.AddModel("rock_moss_set_01", 0.45f, c + rot * new Vector3(0.4f, 0f, -1.4f), (float)rng.NextDouble() * 360f);
                            Landmark(lights, c + Vector3.up * 3.2f + rot * new Vector3(0.6f, 0f, 1.6f), new Color(0.85f, 0.85f, 0.95f), 8.5f, 5f);
                            break;
                        }
                    }

                    var head = c + rot * new Vector3(-0.8f, 0f, 0f);
                    b.Add(MeshKit.Ball(1.15f, 12, 8), Mat.Marble, Matrix4x4.TRS(head + Vector3.up * 0.95f, rot * Quaternion.Euler(0f, 0f, 70f), new Vector3(1f, 1.15f, 1f)));
                    b.Add(MeshKit.Cylinder(1.2f, 1.05f, 0.9f, 12), Mat.Marble, Matrix4x4.TRS(head + Vector3.up * 1.0f + rot * new Vector3(-0.3f, 0f, 0f), rot * Quaternion.Euler(0f, 0f, 70f), Vector3.one));
                    b.AddBox(head + Vector3.up * 0.5f + rot * new Vector3(0.95f, 0f, 0f), new Vector3(0.12f, 0.22f, 1.1f), Mat.Dark, rot);
                    var sh = c + rot * new Vector3(1.4f, 0f, 0.6f);
                    b.AddBox(sh + Vector3.up * 0.7f, new Vector3(1.6f, 1.4f, 1.3f), Mat.Marble, rot * Quaternion.Euler(0f, 25f, 8f));
                    b.Add(MeshKit.Cylinder(0.45f, 0.4f, 1.6f, 10), Mat.Marble, Matrix4x4.TRS(sh + Vector3.up * 0.45f + rot * new Vector3(0.6f, 0f, -1.1f), rot * Quaternion.Euler(90f, 30f, 0f), Vector3.one));
                    Props.Rubble(b, c + rot * new Vector3(0.2f, 0f, -1.8f), rng);
                    Landmark(lights, c + Vector3.up * 3.2f + rot * new Vector3(0.6f, 0f, 1.6f), new Color(0.85f, 0.85f, 0.95f), 8.5f, 5f);
                    break;
                }
                case Kind.Well:
                {
                    var stone = MeshKit.Lathe("roomWell", new[] { new Vector2(1.3f, 0f), new Vector2(1.3f, 1.05f), new Vector2(1.0f, 1.05f), new Vector2(1.0f, 0.1f) }, 16, true);
                    b.Add(stone, Mat.StoneWall, Matrix4x4.TRS(c, rot, Vector3.one));
                    b.Add(MeshKit.Cylinder(1.0f, 1.0f, 0.02f, 16), Mat.Dark, Matrix4x4.TRS(c + Vector3.up * 0.6f, Quaternion.identity, Vector3.one));

                    foreach (float s in new[] { -1f, 1f }) b.AddBox(c + rot * new Vector3(s * 1.15f, 1.6f, 0f), new Vector3(0.16f, 1.3f, 0.16f), Mat.Wood, rot);
                    b.AddBox(c + rot * new Vector3(0f, 2.2f, 0f), new Vector3(2.5f, 0.14f, 0.14f), Mat.Wood, rot);
                    b.Add(MeshKit.Cylinder(0.18f, 0.15f, 0.3f, 8), Mat.Wood, Matrix4x4.TRS(c + rot * new Vector3(0.3f, 1.35f, 0f), rot, Vector3.one));

                    Landmark(lights, c + Vector3.up * 0.5f, new Color(0.45f, 0.65f, 1f), 4.5f, 3f);
                    Landmark(lights, c + Vector3.up * 2.9f, new Color(0.9f, 0.85f, 0.75f), 7f, 3.5f);
                    break;
                }
            }
        }

        private static void Landmark(Transform lights, Vector3 at, Color color, float range, float intensity)
        {
            var l = new GameObject("LandmarkLight").AddComponent<Light>();
            l.transform.SetParent(lights, false);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }
    }
}
