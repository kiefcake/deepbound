using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Castle Ravenhold: the party's home between floors. A round, walled courtyard open to
    /// a moonlit sky, with a gothic keep at its back. A cobbled road runs from the bonfire
    /// past the stalls — merchant, blacksmith, the gambling tent, the Test of Might — to the
    /// gatehouse, where the carriage waits to take the party down.
    ///
    /// <para>
    /// Fixed (no seed) so every peer builds the same thing; it sits far above the dungeon so
    /// the two never overlap. No navmesh: nothing hostile comes here.
    /// </para>
    /// </summary>
    public static class Lobby
    {
        public static readonly Vector3 Origin = new Vector3(0f, 300f, 0f);
        public const float Radius = 26f;
        private const float WallHeight = 9f, WallThick = 1.6f;

        // The road runs south (spawn) to north (gate).
        public static readonly Vector3 BoardingCenter = Origin + new Vector3(0f, 0f, 18f);
        public const float BoardingRadius = 3.2f;

        // Stalls along the road.
        public static readonly Vector3 MerchantSpot = Origin + new Vector3(-7.4f, 0f, -5f);
        public static readonly Vector3 AnvilSpot = Origin + new Vector3(5.6f, 0f, -5f);
        public static readonly Vector3 AnvilFacing = Vector3.left;
        public static readonly Vector3 TentCenter = Origin + new Vector3(-9.5f, 0f, 6f);
        public static readonly Vector3 DiceSpot = TentCenter + new Vector3(1.4f, 0f, -1.2f);
        public static readonly Vector3 CofferSpot = TentCenter + new Vector3(1.6f, 0f, 1.6f);
        public static readonly Vector3 TentFacing = Vector3.right;
        public static readonly Vector3 GongSpot = Origin + new Vector3(8.5f, 0f, 6f);
        public static readonly Vector3 GongFacing = Vector3.left;

        public static readonly Vector3 ShrineSpot = Origin + new Vector3(15.5f, 0f, -4f);

        /// <summary>The archery range in the north-east: three targets on rails, a shooting line at x = 10.</summary>
        public static readonly Vector3[] TargetSpots =
        {
            Origin + new Vector3(15.5f, 0f, 11f),
            Origin + new Vector3(17.5f, 0f, 11.5f),
            Origin + new Vector3(19.5f, 0f, 10.5f),
        };
        public static readonly Vector3 RangeCenter = Origin + new Vector3(15f, 0f, 11f);

        /// <summary>The training yard, west of the bonfire plaza.</summary>
        public static readonly Vector3[] DummySpots =
        {
            Origin + new Vector3(-13f, 0f, -16f),
            Origin + new Vector3(-13f, 0f, -11f),
        };

        public static Vector3 SpawnFor(ulong clientId)
        {
            float angle = clientId * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            return Origin + new Vector3(0f, 0f, -14f) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.6f + Vector3.up * 0.1f;
        }

        public static bool InBoardingCircle(Vector3 p)
        {
            var d = p - BoardingCenter;
            return Mathf.Abs(d.y) < 2f && new Vector2(d.x, d.z).magnitude <= BoardingRadius;
        }

        private static readonly Color Fire = new Color(1f, 0.6f, 0.3f);

        public static GameObject Build()
        {
            var root = new GameObject("Lobby (Castle Ravenhold)");
            var structure = new MeshKit.Batcher();
            var dressing = new MeshKit.Batcher();
            var lights = root.transform;
            var o = Origin;

            BuildGround(o, structure, dressing);
            BuildCurtainWall(o, structure, dressing, lights);
            BuildKeep(o, structure, dressing, lights);
            BuildGatehouse(o, structure, dressing, lights);
            BuildRoad(o, dressing, lights);
            BuildStalls(o, dressing, lights);
            BuildTent(lights, dressing);
            BuildYard(o, dressing, lights);
            BuildShrineNook(dressing, lights);
            BuildRange(dressing, lights);
            BuildSky(lights);

            // The bonfire on the plaza where everyone arrives.
            var fire = o + new Vector3(0f, 0f, -14f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                dressing.AddBox(fire + new Vector3(Mathf.Cos(a), 0.14f, Mathf.Sin(a)) * 1.3f, new Vector3(0.4f, 0.28f, 0.35f), Mat.Rubble, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var p = fire + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.8f;
                dressing.Add(MeshKit.Cylinder(0.25f, 0.25f, 1.8f, 8), Mat.Wood, Matrix4x4.TRS(p + Vector3.up * 0.25f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 90f), Vector3.one));
            }
            Props.Brazier(lights, fire, new Color(1f, 0.55f, 0.25f), true);

            Props.Carriage(lights, dressing, BoardingCenter + new Vector3(6f, 0f, 1f), Quaternion.Euler(0f, 90f, 0f));
            Props.BoardingCircle(lights, BoardingCenter, BoardingRadius);

            structure.Build(root.transform, "LobbyStructure", collide: true);
            dressing.Build(root.transform, "LobbyDressing", collide: true);
            ApplyCastleAtmosphere();
            return root;
        }

        /// <summary>Night over the castle: lighter fog than the depths, cool moonlit ambient.</summary>
        public static void ApplyCastleAtmosphere()
        {
            DungeonBuilder.ApplyAtmosphere();
            RenderSettings.ambientSkyColor = new Color(0.16f, 0.19f, 0.28f);
            RenderSettings.ambientEquatorColor = new Color(0.1f, 0.1f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.05f, 0.045f, 0.05f);
            RenderSettings.fogColor = new Color(0.035f, 0.045f, 0.08f);
            RenderSettings.fogDensity = 0.012f;
            foreach (var cam in Camera.allCameras) cam.backgroundColor = RenderSettings.fogColor;
        }

        // ------------------------------------------------------------------

        private static void BuildGround(Vector3 o, MeshKit.Batcher s, MeshKit.Batcher d)
        {
            s.Add(MeshKit.Cylinder(Radius + 2f, Radius + 2f, 0.4f, 48), Mat.Flagstone, Matrix4x4.Translate(o + Vector3.down * 0.4f));
            // Grass-dark earth beds in the quadrants, so the courtyard is not one flat sheet.
            foreach (var c in new[] { new Vector3(-15f, 0f, 0f), new Vector3(15f, 0f, -12f), new Vector3(14f, 0f, 14f) })
                d.Add(MeshKit.Cylinder(4.5f, 4.5f, 0.06f, 20), Mat.Rubble, Matrix4x4.Translate(o + c));
        }

        private static void BuildCurtainWall(Vector3 o, MeshKit.Batcher s, MeshKit.Batcher d, Transform lights)
        {
            const int segments = 40;
            float segLen = 2f * Mathf.PI * (Radius + WallThick / 2f) / segments * 1.04f;
            for (int i = 0; i < segments; i++)
            {
                float deg = i * 360f / segments;
                // The gate faces north (+z, i.e. 90° from +x).
                if (Mathf.Abs(Mathf.DeltaAngle(deg, 90f)) < 7f) continue;
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var rot = Quaternion.LookRotation(dir);
                var mid = o + dir * (Radius + WallThick / 2f);
                s.AddBox(mid + Vector3.up * WallHeight / 2f, new Vector3(segLen, WallHeight, WallThick), Mat.StoneWall, rot);
                // A walkway ledge on the inside and merlons along the top.
                d.AddBox(mid - dir * (WallThick / 2f + 0.3f) + Vector3.up * (WallHeight - 0.9f), new Vector3(segLen, 0.3f, 0.6f), Mat.StoneWall, rot);
                if (i % 2 == 0) d.AddBox(mid + Vector3.up * (WallHeight + 0.55f), new Vector3(segLen * 0.55f, 1.1f, WallThick), Mat.StoneWall, rot);
                // Buttresses every fourth segment.
                if (i % 4 == 2) d.AddBox(mid - dir * (WallThick / 2f + 0.5f) + Vector3.up * WallHeight * 0.35f, new Vector3(1f, WallHeight * 0.7f, 1f), Mat.StoneWall, rot);
            }

            // Six round towers with slate cones; two flank the gate.
            var cone = MeshKit.Lathe("towerCone", new[] { new Vector2(0f, 0f), new Vector2(3.4f, 0f), new Vector2(0f, 6.5f) }, 12);
            for (int i = 0; i < 6; i++)
            {
                float deg = 30f + i * 60f;
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = o + dir * (Radius + 0.6f);
                s.Add(MeshKit.Cylinder(2.8f, 2.6f, 15f, 14), Mat.StoneWall, Matrix4x4.Translate(p));
                d.Add(MeshKit.Cylinder(3.1f, 3.1f, 0.6f, 14), Mat.StoneWall, Matrix4x4.Translate(p + Vector3.up * 15f));
                d.Add(cone, Mat.Dark, Matrix4x4.Translate(p + Vector3.up * 15.6f));
                // Lit arrow-slit windows facing the courtyard.
                for (int k = 0; k < 2; k++)
                {
                    var w = p - dir * 2.65f + Vector3.up * (6f + k * 4.5f);
                    d.AddBox(w, new Vector3(0.3f, 1.2f, 0.12f), Mat.Flame, Quaternion.LookRotation(dir));
                }
                Props.WallTorch(lights, p - dir * 2.75f + Vector3.up * 3f, -dir, i % 3 == 0, Fire);
            }
        }

        /// <summary>The keep at the courtyard's back: a tall gothic front with spires and a rose window.</summary>
        private static void BuildKeep(Vector3 o, MeshKit.Batcher s, MeshKit.Batcher d, Transform lights)
        {
            var c = o + new Vector3(0f, 0f, -Radius + 3f);
            s.AddBox(c + new Vector3(0f, 9f, -2f), new Vector3(16f, 18f, 6f), Mat.StoneWall);
            s.AddBox(c + new Vector3(0f, 14f, -2f), new Vector3(10f, 10f, 5f), Mat.StoneWall);
            var spire = MeshKit.Lathe("keepSpire", new[] { new Vector2(0f, 0f), new Vector2(1.2f, 0f), new Vector2(0f, 9f) }, 8);
            foreach (float x in new[] { -7.5f, 7.5f })
            {
                s.Add(MeshKit.Cylinder(1.4f, 1.3f, 21f, 10), Mat.StoneWall, Matrix4x4.Translate(c + new Vector3(x, 0f, 0.6f)));
                d.Add(spire, Mat.Dark, Matrix4x4.Translate(c + new Vector3(x, 21f, 0.6f)));
            }
            d.Add(spire, Mat.Dark, Matrix4x4.TRS(c + new Vector3(0f, 19f, -2f), Quaternion.identity, new Vector3(1.6f, 1.4f, 1.6f)));
            // Great doors, a pointed arch over them, and the rose window above.
            d.AddBox(c + new Vector3(0f, 2.6f, 1.05f), new Vector3(3.6f, 5.2f, 0.2f), Mat.Wood);
            d.AddBox(c + new Vector3(0f, 2.6f, 1.15f), new Vector3(0.12f, 5.2f, 0.1f), Mat.Steel);
            d.AddBox(c + new Vector3(-1.4f, 6f, 1.1f), new Vector3(1.8f, 0.5f, 0.4f), Mat.StoneWall, Quaternion.Euler(0f, 0f, -35f));
            d.AddBox(c + new Vector3(1.4f, 6f, 1.1f), new Vector3(1.8f, 0.5f, 0.4f), Mat.StoneWall, Quaternion.Euler(0f, 0f, 35f));
            var rose = MeshKit.Part(lights, "RoseWindow", MeshKit.Cylinder(2.4f, 2.4f, 0.1f, 20), Mat.Glow, c + new Vector3(0f, 11f, 1.05f), new Vector3(90f, 0f, 0f));
            ArtLibrary.SetGlow(rose.GetComponent<Renderer>(), new Color(0.9f, 0.35f, 0.25f) * 1.3f);
            for (int i = 0; i < 8; i++)
                d.AddBox(c + new Vector3(0f, 11f, 1.12f), new Vector3(0.12f, 4.8f, 0.08f), Mat.StoneWall, Quaternion.Euler(0f, 0f, i * 22.5f));
            // Tall lancet windows either side.
            foreach (float x in new[] { -4.5f, 4.5f })
                d.AddBox(c + new Vector3(x, 8f, 1.05f), new Vector3(0.9f, 4f, 0.1f), Mat.Flame);
            var glow = new GameObject("RoseLight").AddComponent<Light>();
            glow.transform.SetParent(lights, false);
            glow.transform.position = c + new Vector3(0f, 10f, 4f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.45f, 0.35f);
            glow.range = 14f;
            glow.intensity = 2.2f;
            Props.Banner(lights, c + new Vector3(-4.5f, 4.5f, 1.1f), Vector3.forward, new Color(0.45f, 0.08f, 0.1f));
            Props.Banner(lights, c + new Vector3(4.5f, 4.5f, 1.1f), Vector3.forward, new Color(0.45f, 0.08f, 0.1f));
        }

        private static void BuildGatehouse(Vector3 o, MeshKit.Batcher s, MeshKit.Batcher d, Transform lights)
        {
            var g = o + new Vector3(0f, 0f, Radius + 0.8f);
            foreach (float x in new[] { -3.6f, 3.6f })
            {
                s.AddBox(g + new Vector3(x, 6f, 0f), new Vector3(2.4f, 12f, 3.6f), Mat.StoneWall);
                d.AddBox(g + new Vector3(x, 12.6f, 0f), new Vector3(2.8f, 1.2f, 4f), Mat.StoneWall);
            }
            s.AddBox(g + new Vector3(0f, 9.5f, 0f), new Vector3(4.8f, 5f, 3.6f), Mat.StoneWall);
            // The raised portcullis teeth, and the dark road beyond.
            for (int i = -2; i <= 2; i++) d.AddBox(g + new Vector3(i * 0.9f, 6.6f, -1.2f), new Vector3(0.14f, 1.4f, 0.14f), Mat.Steel);
            d.AddBox(g + new Vector3(0f, 3.5f, 1.9f), new Vector3(4.8f, 7f, 0.2f), Mat.Dark);
            Props.WallTorch(lights, g + new Vector3(-2.3f, 3.2f, -1.85f), Vector3.back, true, Fire);
            Props.WallTorch(lights, g + new Vector3(2.3f, 3.2f, -1.85f), Vector3.back, false, Fire);
        }

        /// <summary>The cobbled road from the bonfire to the gate, braziers along both sides.</summary>
        private static void BuildRoad(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            d.AddBox(o + new Vector3(0f, 0.02f, 3f), new Vector3(4.2f, 0.04f, 38f), Mat.Rubble);
            d.AddBox(o + new Vector3(-2.2f, 0.06f, 3f), new Vector3(0.3f, 0.12f, 38f), Mat.StoneWall);
            d.AddBox(o + new Vector3(2.2f, 0.06f, 3f), new Vector3(0.3f, 0.12f, 38f), Mat.StoneWall);
            for (float z = -9f; z <= 15f; z += 8f)
            {
                Props.Brazier(lights, o + new Vector3(-3.2f, 0f, z), Fire, false);
                Props.Brazier(lights, o + new Vector3(3.2f, 0f, z), Fire, z > 10f);
            }
        }

        /// <summary>A market stall: counter, four posts and a striped awning.</summary>
        private static void Stall(Transform parent, MeshKit.Batcher d, Vector3 front, Vector3 facing, Color a, Color b)
        {
            var rot = Quaternion.LookRotation(facing);
            var right = rot * Vector3.right;
            var back = -facing;
            d.AddBox(front + Vector3.up * 0.5f, new Vector3(3.6f, 1f, 0.8f), Mat.Wood, rot);
            d.AddBox(front + Vector3.up * 1.02f, new Vector3(3.8f, 0.06f, 0.95f), Mat.Wood, rot);
            foreach (float sx in new[] { -1.8f, 1.8f })
                foreach (float sz in new[] { 0f, 2.4f })
                    d.AddBox(front + right * sx + back * sz + Vector3.up * 1.4f, new Vector3(0.16f, 2.8f, 0.16f), Mat.Wood);
            // Striped awning: alternating cloth bands, tilted toward the road.
            for (int i = 0; i < 6; i++)
            {
                var stripe = MeshKit.Part(parent, "Awning" + i, MeshKit.Box(new Vector3(0.64f, 0.05f, 3.2f)), Mat.Cloth,
                    front + right * (-1.6f + i * 0.64f) + back * 1.1f + Vector3.up * 2.95f, (rot * Quaternion.Euler(-14f, 0f, 0f)).eulerAngles);
                ArtLibrary.Tint(stripe.GetComponent<Renderer>(), i % 2 == 0 ? a : b);
            }
            var lamp = new GameObject("StallLamp").AddComponent<Light>();
            lamp.transform.SetParent(parent, false);
            lamp.transform.position = front + back * 0.6f + Vector3.up * 2.4f;
            lamp.type = LightType.Point;
            lamp.color = Fire;
            lamp.range = 6f;
            lamp.intensity = 1.6f;
        }

        private static void BuildStalls(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            // Merchant (west), behind his own table of wares.
            Stall(lights, d, o + new Vector3(-4.8f, 0f, -5f), Vector3.right, new Color(0.55f, 0.12f, 0.12f), new Color(0.85f, 0.75f, 0.5f));
            Props.Merchant(lights, MerchantSpot, MerchantSpot + Vector3.right * 3f, d);

            // Blacksmith (east): forge with a chimney and a quench barrel beside the anvil.
            Stall(lights, d, o + new Vector3(4.8f, 0f, -5f), Vector3.left, new Color(0.2f, 0.2f, 0.24f), new Color(0.6f, 0.35f, 0.15f));
            var forge = o + new Vector3(8.2f, 0f, -6.6f);
            d.AddBox(forge + Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, 1.4f), Mat.StoneWall);
            d.AddBox(forge + Vector3.up * 1.42f, new Vector3(1.4f, 0.05f, 1f), Mat.Flame);
            d.AddBox(forge + new Vector3(0f, 3.4f, -0.3f), new Vector3(0.8f, 4f, 0.8f), Mat.StoneWall);
            var forgeLight = new GameObject("ForgeGlow").AddComponent<Light>();
            forgeLight.transform.SetParent(lights, false);
            forgeLight.transform.position = forge + Vector3.up * 2f;
            forgeLight.type = LightType.Point;
            forgeLight.color = new Color(1f, 0.45f, 0.15f);
            forgeLight.range = 7f;
            forgeLight.intensity = 3f;
            Props.Barrel(d, o + new Vector3(6.8f, 0f, -3.2f));
            Props.WeaponRack(d, o + new Vector3(8.6f, 0f, -3.4f), Quaternion.Euler(0f, -90f, 0f));

            // The Test of Might's frame (the gong itself is a networked object).
            var g = GongSpot;
            foreach (float z in new[] { -1.5f, 1.5f })
                d.AddBox(g + new Vector3(0.3f, 1.8f, z), new Vector3(0.25f, 3.6f, 0.25f), Mat.Wood);
            d.AddBox(g + new Vector3(0.3f, 3.65f, 0f), new Vector3(0.3f, 0.3f, 3.6f), Mat.Wood);
            d.AddBox(g + new Vector3(-1.4f, 0.05f, 0f), new Vector3(1.6f, 0.1f, 2.4f), Mat.Rubble);
            Props.Banner(lights, g + new Vector3(0.45f, 3.2f, 1.5f), Vector3.left, new Color(0.7f, 0.5f, 0.1f));
        }

        /// <summary>The gambling tent: a round striped pavilion, open toward the road.</summary>
        private static void BuildTent(Transform lights, MeshKit.Batcher d)
        {
            var c = TentCenter;
            var red = new Color(0.55f, 0.1f, 0.12f);
            var gold = new Color(0.8f, 0.62f, 0.25f);
            // Walls: cloth panels round 270 degrees, leaving the east side open.
            for (int i = 0; i < 14; i++)
            {
                float deg = 135f + i * (270f / 13f);
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var panel = MeshKit.Part(lights, "TentWall" + i, MeshKit.Box(new Vector3(1.25f, 2.6f, 0.06f)), Mat.Cloth,
                    c + dir * 4.2f + Vector3.up * 1.3f, Quaternion.LookRotation(dir).eulerAngles);
                ArtLibrary.Tint(panel.GetComponent<Renderer>(), i % 2 == 0 ? red : gold);
            }
            var roof = MeshKit.Part(lights, "TentRoof", MeshKit.Lathe("tentRoof", new[]
            {
                new Vector2(4.6f, 0f), new Vector2(2.2f, 1.4f), new Vector2(0f, 2.6f),
            }, 16), Mat.Cloth, c + Vector3.up * 2.6f);
            ArtLibrary.Tint(roof.GetComponent<Renderer>(), red);
            d.AddBox(c + Vector3.up * 2.6f, new Vector3(0.18f, 5.2f, 0.18f), Mat.Wood);
            var pennant = MeshKit.Part(lights, "Pennant", MeshKit.Box(new Vector3(0.05f, 0.4f, 0.9f)), Mat.Cloth, c + new Vector3(0f, 5.6f, 0.45f));
            ArtLibrary.Tint(pennant.GetComponent<Renderer>(), gold);
            var rug = MeshKit.Part(lights, "Rug", MeshKit.Cylinder(3.4f, 3.4f, 0.03f, 18), Mat.Cloth, c + Vector3.up * 0.02f);
            ArtLibrary.Tint(rug.GetComponent<Renderer>(), new Color(0.3f, 0.08f, 0.15f));
            Props.Candles(lights, c + new Vector3(-2.6f, 0f, -1.4f), Fire);
            Props.Candles(lights, c + new Vector3(-2.4f, 0f, 2f), Fire);
            var lamp = new GameObject("TentLamp").AddComponent<Light>();
            lamp.transform.SetParent(lights, false);
            lamp.transform.position = c + Vector3.up * 3.2f;
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.65f, 0.35f);
            lamp.range = 8f;
            lamp.intensity = 2.4f;
        }

        private static void BuildYard(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            // Straw under the dummies, a fence on the plaza side, and spare arms.
            d.AddBox(o + new Vector3(-13f, 0.02f, -13.5f), new Vector3(5f, 0.03f, 9f), Mat.Wood);
            for (float z = -17.5f; z <= -9.5f; z += 2f)
                d.AddBox(o + new Vector3(-10.2f, 0.5f, z), new Vector3(0.14f, 1f, 0.14f), Mat.Wood);
            d.AddBox(o + new Vector3(-10.2f, 0.85f, -13.5f), new Vector3(0.08f, 0.1f, 8f), Mat.Wood);
            Props.WeaponRack(d, o + new Vector3(-15.8f, 0f, -13.5f), Quaternion.Euler(0f, 90f, 0f));
            Props.Brazier(lights, o + new Vector3(-11f, 0f, -18.5f), Fire, false);
            Props.Crate(d, o + new Vector3(14f, 0f, -14f), 1.2f, 12f);
            Props.Crate(d, o + new Vector3(15.2f, 0f, -12.8f), 0.9f, -8f);
            Props.Barrel(d, o + new Vector3(13.8f, 0f, -12.2f));
        }

        /// <summary>Steps, flanking braziers and a backing wall for the shrine.</summary>
        private static void BuildShrineNook(MeshKit.Batcher d, Transform lights)
        {
            var s = ShrineSpot;
            d.AddBox(s + new Vector3(0.2f, 0.06f, 0f), new Vector3(3.4f, 0.12f, 3.2f), Mat.StoneWall);
            d.AddBox(s + new Vector3(-1.4f, 0.03f, 0f), new Vector3(0.6f, 0.06f, 3.2f), Mat.StoneWall);
            d.AddBox(s + new Vector3(2.2f, 2f, 0f), new Vector3(0.5f, 4f, 3.6f), Mat.StoneWall);
            d.AddBox(s + new Vector3(2.2f, 4.2f, 0f), new Vector3(0.7f, 0.4f, 4f), Mat.StoneWall);
            Props.Brazier(lights, s + new Vector3(-0.8f, 0f, 1.9f), Fire, false);
            Props.Brazier(lights, s + new Vector3(-0.8f, 0f, -1.9f), Fire, false);
            Props.Banner(lights, s + new Vector3(1.95f, 3.2f, 1.2f), Vector3.left, new Color(0.45f, 0.08f, 0.1f));
            Props.Banner(lights, s + new Vector3(1.95f, 3.2f, -1.2f), Vector3.left, new Color(0.45f, 0.08f, 0.1f));
        }

        /// <summary>The butts: rails for the targets, a shooting line, hay bales behind.</summary>
        private static void BuildRange(MeshKit.Batcher d, Transform lights)
        {
            foreach (var t in TargetSpots)
            {
                d.AddBox(t + new Vector3(0f, 0.03f, 0f), new Vector3(0.12f, 0.06f, 7.6f), Mat.Wood);
                d.AddBox(t + new Vector3(0.5f, 0.03f, 0f), new Vector3(0.12f, 0.06f, 7.6f), Mat.Wood);
            }
            // The shooting line: a low rail at x = 10, with a sign post.
            var line = Origin + new Vector3(10f, 0f, 11f);
            d.AddBox(line + new Vector3(0f, 0.55f, 0f), new Vector3(0.1f, 0.1f, 6f), Mat.Wood);
            for (int i = -1; i <= 1; i++) d.AddBox(line + new Vector3(0f, 0.3f, i * 2.9f), new Vector3(0.12f, 0.6f, 0.12f), Mat.Wood);
            d.AddBox(line + new Vector3(0f, 1.2f, -3.4f), new Vector3(0.1f, 2.4f, 0.1f), Mat.Wood);
            d.AddBox(line + new Vector3(0f, 2.2f, -3.4f), new Vector3(0.08f, 0.5f, 1f), Mat.Wood);
            // Hay bales stacked behind the targets to catch the misses.
            for (int i = 0; i < 5; i++)
                d.AddBox(Origin + new Vector3(21.2f, 0.45f + (i % 2) * 0.9f, 7.5f + i * 1.6f), new Vector3(1f, 0.9f, 1.5f), Mat.Wood, Quaternion.Euler(0f, 12f * (i % 3), 0f));
            Props.Brazier(lights, line + new Vector3(-0.8f, 0f, 3.6f), Fire, false);
        }

        /// <summary>No roof: a pale moon and a scatter of stars over the walls.</summary>
        private static void BuildSky(Transform lights)
        {
            var moon = MeshKit.Part(lights, "Moon", MeshKit.Ball(4f, 16, 12), Mat.Glow, Origin + new Vector3(28f, 42f, 50f));
            ArtLibrary.SetGlow(moon.GetComponent<Renderer>(), new Color(0.85f, 0.88f, 1f) * 1.4f);
            moon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rng = new System.Random(7);
            var star = MeshKit.Box(Vector3.one * 0.22f);
            for (int i = 0; i < 90; i++)
            {
                float az = (float)rng.NextDouble() * Mathf.PI * 2f, el = 0.35f + (float)rng.NextDouble() * 1.1f;
                var dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                var s = MeshKit.Part(lights, "Star", star, Mat.Glow, Origin + dir * 68f);
                ArtLibrary.SetGlow(s.GetComponent<Renderer>(), new Color(0.8f, 0.85f, 1f) * (0.6f + (float)rng.NextDouble() * 0.9f));
                s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var moonlight = new GameObject("Moonlight").AddComponent<Light>();
            moonlight.transform.SetParent(lights, false);
            moonlight.transform.position = Origin + new Vector3(0f, 40f, 10f);
            moonlight.type = LightType.Point;
            moonlight.color = new Color(0.55f, 0.65f, 1f);
            moonlight.range = 90f;
            moonlight.intensity = 0.5f;
            moonlight.shadows = LightShadows.None;
        }
    }
}
