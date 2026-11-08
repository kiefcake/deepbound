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
        /// <summary>The courtyard's radius (26 m until 2026-10-20: the camp was cramped).</summary>
        public const float Radius = 35f;
        /// <summary>Every camp position is spread out by this from the plaza's centre (sizes stay as they were).</summary>
        public const float Spread = 1.35f;
        public static Vector3 S(float x, float y, float z) => new Vector3(x * Spread, y, z * Spread);
        private const float WallHeight = 9f, WallThick = 1.6f;

        // The road runs south (spawn) to north (gate).
        public static readonly Vector3 BoardingCenter = Origin + S(0f, 0f, 18f);
        public const float BoardingRadius = 3.2f;

        // Stalls along the road.
        public static readonly Vector3 MerchantSpot = Origin + S(-7.4f, 0f, -5f);
        public static readonly Vector3 AnvilSpot = Origin + S(-5.6f, 0f, 1.4f); // 2026-11-07 market square: the trade row (west) with Silas and Vey
        public static readonly Vector3 AnvilFacing = Vector3.right;
        public static readonly Vector3 TentCenter = Origin + new Vector3(12.6f, 0f, -19f); // 2026-11-07 (the user): by the spawn fire, east of it, opening west onto the logs
        public static readonly Vector3 DiceSpot = TentCenter + new Vector3(-1.4f, 0f, -1.2f);
        public static readonly Vector3 CofferSpot = TentCenter + new Vector3(-1.6f, 0f, 1.6f);
        public static readonly Vector3 TentFacing = Vector3.left;
        /// <summary>The bone reels, against the tent's back wall, and the card table by its south wall.</summary>
        public static readonly Vector3 SlotSpot = TentCenter + new Vector3(2.9f, 0f, 0.3f);
        public static readonly Vector3 BlackjackSpot = TentCenter + new Vector3(0.6f, 0f, -2.5f);
        public static readonly Vector3 GongSpot = Origin + S(8.5f, 0f, 6f);

        /// <summary>
        /// The Gutted Lantern (2026-11-01, the user: "a tavern nearby the casino tent ... mead ... a band"): a stone and
        /// timber alehouse north-west of the gambling tent, its door facing east onto the cart and the bounty board.
        /// Inside: Marta's bar (mead, <see cref="TavernKeep"/>), a hearth, tables, and a little stage for the band
        /// (<see cref="TavernBand"/>). Interior 8 x 7 m.
        /// </summary>
        public static readonly Vector3 TavernCenter = Origin + new Vector3(-12.6f, 0f, -19f); // 2026-11-07 (the user): by the spawn fire, west of it, the door facing the logs
        public const float TavernHalfX = 4f, TavernHalfZ = 3.5f, TavernWall = 3.2f;
        public static readonly Vector3 TavernKeepSpot = TavernCenter + new Vector3(-1.9f, 0f, 0.6f);
        public static readonly Vector3 TavernBandSpot = TavernCenter + new Vector3(1.4f, 0f, -1.6f);
        public static bool InTavern(Vector3 p) => Mathf.Abs(p.x - TavernCenter.x) < TavernHalfX && Mathf.Abs(p.z - TavernCenter.z) < TavernHalfZ && Mathf.Abs(p.y - TavernCenter.y) < 4f;
        public static readonly Vector3 GongFacing = Vector3.left;

        public static readonly Vector3 ShrineSpot = Origin + S(15.5f, 0f, -4f);
        /// <summary>The training yard's drill board, on the plaza side of its fence.</summary>
        public static readonly Vector3 YardSpot = Origin + S(-9.1f, 0f, -8.2f) + YardShift;
        /// <summary>2026-11-07: the whole training yard moved to the north-west, where the tavern stood (the tavern went by the fire).</summary>
        public static readonly Vector3 YardShift = new Vector3(-4.05f, 0f, 34.4f);
        /// <summary>Ysolde the runewright's stones, between the shrine and the gong: runes, sigils, abilities.</summary>
        public static readonly Vector3 RunewrightSpot = Origin + S(12.6f, 0f, 3.2f);

        /// <summary>Where you talk to Corwin, beside his carriage.</summary>
        public static Vector3 CarriageStall => CarriageAt + new Vector3(0f, 0f, 2.2f);
        /// <summary>The carriage: wagon centre, facing out of the gate (+z).</summary>
        public static readonly Vector3 CarriageAt = Origin + S(0f, 0f, 19.5f);

        /// <summary>Where each camp character stands (their struck-body, <see cref="CampFolk"/>).</summary>
        /// <summary>Where the people stand (2026-11-07 inspection: Hobb had his back to his customers behind the anvil,
        /// Aldric knelt square in front of the altar, Vey stood 1.8 m back from her own counter).</summary>
        public static Vector3 HobbSpot => AnvilSpot + Vector3.left * 0.95f;
        public static Vector3 AldricSpot => ShrineSpot + new Vector3(-0.7f, 0f, 1.45f);
        public static Vector3 VeySpot => TailorSpot + TailorFacing * 1.45f;

        public static (FolkId id, Vector3 at)[] FolkBodies => new[]
        {
            (FolkId.Silas, MerchantSpot),
            (FolkId.Hobb, HobbSpot),
            (FolkId.Aldric, AldricSpot),
            (FolkId.Vey, VeySpot),
            (FolkId.Corwin, CarriageAt + new Vector3(0f, 0.9f, 2.2f)),
            (FolkId.Bones, BlackjackSpot + Vector3.back * 1f),
            (FolkId.Brann, YardSpot),
            (FolkId.Ysolde, RunewrightSpot),
        };

        /// <summary>The bounty board, by the road near the gate: contracts for the next floor.</summary>
        public static readonly Vector3 BountySpot = Origin + S(-5.7f, 0f, 17.2f); // by the gate road, last stop before the carriage
        /// <summary>The Hall of Records slab, beside the bounty board.</summary>
        public static readonly Vector3 RecordsSpot = BountySpot + new Vector3(0f, 0f, -3.4f); // west of the road braziers (one stood right in front of the slab)
        public static readonly Vector3 BountyFacing = Vector3.right;
        /// <summary>The market square's well, between the arrival bonfire and the stalls.</summary>
        public static readonly Vector3 WellSpot = Origin + S(0f, 0f, -8.4f);
        /// <summary>Where smoke leaves the forge chimney (set when the camp is built).</summary>
        public static Vector3 ForgeChimney { get; private set; }
        public static Vector3 TavernChimney { get; private set; }

        /// <summary>
        /// Trees and planters (2026-11-07, the user: "trees and planters", "ambient effects"): gnarled oaks in the
        /// square's corners and the tavern yard, a dark yew by the shrine, flower planters along the trade row; then
        /// the camp's ambience (fireflies, chimney smoke, falling leaves, gate fog) on its own local component.
        /// </summary>
        private static void BuildGreenery(Vector3 o, MeshKit.Batcher d, Transform lights, Transform root)
        {
            var amb = root.gameObject.AddComponent<Art.CampAmbience>();
            amb.Ground = o.y;
            amb.Gate = CarriageAt + new Vector3(0f, 0f, 6f);
            amb.Chimneys.Add(TavernChimney);
            amb.Chimneys.Add(ForgeChimney);
            var rng = new System.Random(7);
            // Oaks: (x, z in S units, scale).
            foreach (var (x, z, k) in new[] { (8.2f, 16.2f, 1f) } /* 2026-11-08: the west oak's unlit crown sat right behind Brann's head from his customers' side (read as a black hat) */ /* 2026-11-07: the two by the spawn fire made way for the tavern and tent */)
            {
                var at = o + S(x, 0f, z);
                Oak(d, lights, at, k, rng);
                amb.Crowns.Add(at + Vector3.up * 4.2f * k);
                amb.Glades.Add(at);
            }
            // (2026-11-07, the user: the yew read as a tree with no trunk, the flower planters as out of place: both removed.)
        }

        /// <summary>A gnarled oak: a leaning trunk, three crooked limbs, clumps of dark leaves.</summary>
        private static void Oak(MeshKit.Batcher d, Transform lights, Vector3 at, float k, System.Random rng)
        {
            float lean = (float)rng.NextDouble() * 8f - 4f;
            d.Add(MeshKit.Lathe("oakTrunk", new[] { new Vector2(0f, 0f), new Vector2(0.45f, 0f), new Vector2(0.32f, 0.4f), new Vector2(0.26f, 2.4f), new Vector2(0.2f, 3.2f), new Vector2(0f, 3.3f) }, 9),
                Mat.Wood, Matrix4x4.TRS(at, Quaternion.Euler(lean, (float)rng.NextDouble() * 360f, 0f), Vector3.one * k));
            var trunkTint = new Color(0.22f, 0.16f, 0.12f);
            for (int i = 0; i < 3; i++)
            {
                float yaw = i * 120f + (float)rng.NextDouble() * 40f;
                var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var from = at + Vector3.up * (2.2f + i * 0.35f) * k;
                d.AddBox(from + dir * 0.7f * k + Vector3.up * 0.4f * k, new Vector3(0.14f, 0.14f, 1.6f) * k, Mat.Wood, trunkTint, Quaternion.LookRotation(dir + Vector3.up * 0.6f));
            }
            for (int i = 0; i < 9; i++)
            {
                var off = new Vector3(((float)rng.NextDouble() - 0.5f) * 2.6f, 2.8f + (float)rng.NextDouble() * 2.4f, ((float)rng.NextDouble() - 0.5f) * 2.6f) * k;
                // Clumps in two greens, batched; rounder and spread up the crown so it reads as a tree, not a mushroom cap.
                var green = rng.NextDouble() < 0.5 ? new Color(0.24f, 0.36f, 0.13f) : new Color(0.34f, 0.38f, 0.15f); // darker read as black balls at night
                d.Add(MeshKit.Ball(0.8f + (float)rng.NextDouble() * 0.45f, 8, 6), Mat.Cloth + "#" + ColorUtility.ToHtmlStringRGB(green), Matrix4x4.TRS(at + off, Quaternion.identity, Vector3.one * k));
            }
        }
        /// <summary>The Omen Stone, east of the boarding circle.</summary>
        public static readonly Vector3 OmenSpot = Origin + S(5.2f, 0f, 14.5f);

        /// <summary>The tailor, west of the merchant: cloak dyes for Renown (local, see <c>UI.Wardrobe</c>).</summary>
        public static readonly Vector3 TailorSpot = Origin + S(-14.6f, 0f, -4f);
        public static readonly Vector3 TailorFacing = Vector3.right;
        public static readonly Vector3 MannequinSpot = Origin + S(-11.4f, 0f, -0.9f);

        /// <summary>The archery range in the north-east: three targets on rails, a shooting line at x = 10.</summary>
        public static readonly Vector3[] TargetSpots =
        {
            Origin + S(15.5f, 0f, 11f),
            Origin + S(17.5f, 0f, 11.5f),
            Origin + S(19.5f, 0f, 10.5f),
        };
        public static readonly Vector3 RangeCenter = Origin + S(15f, 0f, 11f);

        /// <summary>The training yard, west of the bonfire plaza.</summary>
        public static readonly Vector3[] DummySpots =
        {
            Origin + S(-13f, 0f, -16f) + YardShift,
            Origin + S(-13f, 0f, -11f) + YardShift,
        };

        /// <summary>The spawn bonfire and the logs round it (2026-11-07, the user: "allow the players to sit around the camp fire").</summary>
        public static readonly Vector3 FirePit = Origin + S(0f, 0f, -14f);
        public const int FireLogCount = 6;
        public const float FireLogRadius = 3.8f;
        public static float FireLogAngle(int i) => (i * 60f + 30f) * Mathf.Deg2Rad;

        /// <summary>Two places to sit on each log, facing the fire: (where the body goes, which way it faces).</summary>
        public static (Vector3 at, Vector3 face)[] LogSeats()
        {
            var seats = new (Vector3, Vector3)[FireLogCount * 2];
            for (int i = 0; i < FireLogCount; i++)
            {
                float a = FireLogAngle(i);
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var along = Vector3.Cross(Vector3.up, outward);
                for (int k = 0; k < 2; k++)
                    seats[i * 2 + k] = (FirePit + outward * (FireLogRadius + 0.12f) + along * (k == 0 ? -0.45f : 0.45f) + Vector3.up * 0.12f, -outward);
            }
            return seats;
        }

        public static Vector3 SpawnFor(ulong clientId)
        {
            float angle = clientId * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            return Origin + S(0f, 0f, -14f) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.6f + Vector3.up * 0.1f;
        }

        public static bool InBoardingCircle(Vector3 p)
        {
            var d = p - BoardingCenter;
            return Mathf.Abs(d.y) < 2f && new Vector2(d.x, d.z).magnitude <= BoardingRadius;
        }

        private static readonly Color Fire = new Color(1f, 0.6f, 0.3f);

        public static GameObject Build()
        {
            FolkRounds.Reset();
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
            BuildForestRoad(o, root.transform, lights);
            BuildCastleLife(o, dressing, lights);
            BuildPlaza(o, dressing, lights);
            BuildStalls(o, dressing, lights);
            BuildTent(lights, dressing);
            BuildTavern(structure, dressing, lights);
            BuildStationLamps(dressing, lights);
            BuildSigns(dressing, lights);
            BuildYard(o, dressing, lights);
            BuildShrineNook(dressing, lights);
            BuildRange(dressing, lights);
            BuildGreenery(o, dressing, lights, root.transform);
            BuildSky(lights);

            // The bonfire on the plaza where everyone arrives.
            var fire = o + S(0f, 0f, -14f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                dressing.AddBox(fire + new Vector3(Mathf.Cos(a), 0.14f, Mathf.Sin(a)) * 1.3f, new Vector3(0.4f, 0.28f, 0.35f), Mat.Rubble, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
            }
            // Six logs to sit on (F), leaving the east and west open for the tent and the tavern door.
            for (int i = 0; i < FireLogCount; i++)
            {
                float a = FireLogAngle(i);
                var p = fire + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * FireLogRadius;
                var along = Vector3.Cross(Vector3.up, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                // Cylinder grows from its base along +along here: start half a log back so both seats are ON it (one sat on air).
                dressing.Add(MeshKit.Cylinder(0.25f, 0.25f, 1.8f, 8), Mat.Wood, Matrix4x4.TRS(p - along * 0.9f + Vector3.up * 0.25f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg - 90f, 90f), Vector3.one)); // lying ALONG the ring (they were spokes), so two can sit on each
            }
            Props.Bonfire(lights, fire, new Color(1f, 0.55f, 0.25f));

            // The carriage at the gate, facing out: sit on its tailgate to set off.
            CarriageRide.Build(root.transform, CarriageAt, Vector3.forward);

            // The camp's people (the merchant has his own stall above).
            Props.Townsfolk(lights, HobbSpot, HobbSpot + Vector3.right * 4f, Props.Folk.Smith);
            Props.Townsfolk(lights, AldricSpot, ShrineSpot + new Vector3(-4f, 0f, -0.6f), Props.Folk.Priest);
            Props.Townsfolk(lights, VeySpot, VeySpot + TailorFacing * 4f, Props.Folk.Tailor);
            Props.Townsfolk(lights, YardSpot, YardSpot + Vector3.right * 4f, Props.Folk.Sergeant);
            Props.Townsfolk(lights, RunewrightSpot, RunewrightSpot + new Vector3(-4f, 0f, -1f), Props.Folk.Runewright);
            BuildRuneStones(dressing, lights);

            structure.Build(root.transform, "LobbyStructure", collide: true);
            Art.AmbientLife.Ravens(lights, Origin, Radius, WallHeight);
            dressing.Build(root.transform, "LobbyDressing", collide: true);
            root.AddComponent<UI.CampChatter>();
            ApplyCastleAtmosphere();
            Art.PropColliders.Add(root.transform);
            return root;
        }

        /// <summary>Night over the castle: lighter fog than the depths, cool moonlit ambient.</summary>
        public static void ApplyCastleAtmosphere()
        {
            DungeonBuilder.ApplyAtmosphere();
            // Warm and well lit (the user, 2026-11-07): camp is the safe place. A little more sky, a warmer horizon.
            RenderSettings.ambientSkyColor = new Color(0.3f, 0.3f, 0.4f);
            RenderSettings.ambientEquatorColor = new Color(0.26f, 0.2f, 0.17f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.1f, 0.08f);
            RenderSettings.fogColor = new Color(0.045f, 0.055f, 0.09f);
            RenderSettings.fogDensity = 0.0095f;
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
                // Spaced so two flank the gate (at 90 degrees) instead of one standing in it.
                float deg = i * 60f;
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
            // The arch's underside and the open road beyond (see BuildForestRoad).
            d.AddBox(g + new Vector3(0f, 7.05f, 0f), new Vector3(4.8f, 0.1f, 3.6f), Mat.StoneWall);
            Props.WallTorch(lights, g + new Vector3(-2.3f, 3.2f, -1.85f), Vector3.back, true, Fire);
            Props.WallTorch(lights, g + new Vector3(2.3f, 3.2f, -1.85f), Vector3.back, false, Fire);
        }

        /// <summary>
        /// Out through the open gate: a dirt road running north into a dark pine wood, two lanterns on
        /// posts by the gate and then nothing but trees and fog. Players cannot walk out (an invisible
        /// bar across the gate's mouth); the carriage drives through it on its way down.
        /// </summary>
        private static void BuildForestRoad(Vector3 o, Transform root, Transform lights)
        {
            var f = new MeshKit.Batcher();
            float gateZ = Radius + 2.6f;
            // Ground outside the walls, the road, and its verges.
            f.AddBox(o + new Vector3(0f, -0.05f, gateZ + 45f), new Vector3(110f, 0.1f, 92f), Mat.Rubble);
            f.AddBox(o + new Vector3(0f, -0.02f, gateZ + 40f), new Vector3(4.4f, 0.06f, 82f), Mat.Flagstone);
            var rng = new System.Random(4021);
            for (float z = gateZ + 1f; z < gateZ + 80f; z += 1.6f)
                foreach (float s in new[] { -1f, 1f })
                    f.AddBox(o + new Vector3(s * (2.3f + (float)rng.NextDouble() * 0.3f), 0.04f, z), new Vector3(0.5f, 0.1f + (float)rng.NextDouble() * 0.08f, 1.3f), Mat.Rubble,
                        Quaternion.Euler(0f, (float)rng.NextDouble() * 20f - 10f, 0f));
            // The wood: dark pines and dead trees on both sides, thinning away from the road, with the
            // road swinging gently so the far end vanishes into the trees.
            var trunk = MeshKit.Cylinder(0.22f, 0.14f, 1f, 6);
            var cone = MeshKit.Lathe("pineTier", new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f) }, 7);
            for (int i = 0; i < 260; i++)
            {
                float z = gateZ + 3f + (float)rng.NextDouble() * 85f;
                float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                float x = side * (4.2f + (float)System.Math.Pow(rng.NextDouble(), 0.7) * 48f);
                // Keep the castle's own moat of open ground clear.
                if (z < gateZ + 6f && Mathf.Abs(x) < 12f) continue;
                var p = o + new Vector3(x, 0f, z);
                float h = 6f + (float)rng.NextDouble() * 7f;
                f.Add(trunk, Mat.Wood, Matrix4x4.TRS(p, Quaternion.identity, new Vector3(1f, h * 0.45f, 1f)));
                if (rng.NextDouble() < 0.78)
                {
                    float w = 1.6f + (float)rng.NextDouble() * 1.2f;
                    for (int t = 0; t < 4; t++)
                        f.Add(cone, Mat.Dark, Matrix4x4.TRS(p + Vector3.up * (h * 0.22f + t * h * 0.19f), Quaternion.Euler(0f, t * 37f, 0f), new Vector3(w * (1f - t * 0.2f), h * 0.3f, w * (1f - t * 0.2f))));
                }
                else
                {
                    // A dead tree: a bare trunk with a few crooked limbs.
                    for (int k = 0; k < 4; k++)
                        f.Add(trunk, Mat.Wood, Matrix4x4.TRS(p + Vector3.up * (h * 0.3f + k * h * 0.12f), Quaternion.Euler((float)rng.NextDouble() * 40f + 30f, k * 90f + (float)rng.NextDouble() * 40f, 0f), new Vector3(0.45f, h * 0.18f, 0.45f)));
                }
            }
            // Two lanterns on posts where the road leaves the gate, a milestone, a crooked sign.
            foreach (float s in new[] { -1f, 1f })
            {
                var post = o + new Vector3(s * 3.1f, 0f, gateZ + 2.5f);
                f.AddBox(post + Vector3.up * 1.3f, new Vector3(0.16f, 2.6f, 0.16f), Mat.Wood);
                f.AddBox(post + new Vector3(-s * 0.3f, 2.5f, 0f), new Vector3(0.6f, 0.08f, 0.08f), Mat.Wood);
                Props.WallTorch(lights, post + new Vector3(-s * 0.55f, 2.2f, 0f), Vector3.back, false, Fire);
            }
            f.AddBox(o + new Vector3(3.4f, 0.5f, gateZ + 9f), new Vector3(0.5f, 1f, 0.3f), Mat.StoneWall, Quaternion.Euler(0f, 0f, 6f));
            f.AddBox(o + new Vector3(-3.6f, 1f, gateZ + 13f), new Vector3(0.12f, 2f, 0.12f), Mat.Wood, Quaternion.Euler(0f, 0f, -8f));
            f.AddBox(o + new Vector3(-3.5f, 1.8f, gateZ + 13f), new Vector3(1.1f, 0.25f, 0.06f), Mat.Wood, Quaternion.Euler(0f, 20f, -10f));
            f.Build(root, "ForestRoad", collide: false);
            // You can look, but not leave: an invisible bar across the gate's mouth.
            var bar = new GameObject("GateBarrier");
            bar.transform.SetParent(root, false);
            bar.transform.position = o + new Vector3(0f, 2f, Radius + 2.3f);
            bar.AddComponent<BoxCollider>().size = new Vector3(5f, 4f, 0.4f);
        }

        /// <summary>
        /// The castle lived in: lanterns along the wall walk and on the towers, a well on the plaza,
        /// hay and barrels by the carriage, banners on the walls, and lit windows in the keep.
        /// </summary>
        /// <summary>
        /// The courtyard's furniture (2026-10-20, when it was spread out to stop it feeling cramped): lamp
        /// posts along both sides of the wider road, benches and planters round the bonfire, a cart and stacks
        /// of crates and barrels between the stalls, grass and a dead tree in the corners, and banner poles.
        /// It fills the space between the stations without crowding any of them.
        /// </summary>
        private static void BuildPlaza(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            var rng = new System.Random(7717);
            var lamp = new Color(1f, 0.72f, 0.4f);
            // Lamp posts along the road, every 9 m, alternating sides lit (to stay inside the light budget).
            int k = 0;
            for (float z = -22f; z <= 24f; z += 9f, k++)
                foreach (float s in new[] { -1f, 1f })
                {
                    var foot = o + new Vector3(s * 4.6f, 0f, z);
                    d.AddBox(foot + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f), Mat.StoneWall);
                    d.AddBox(foot + Vector3.up * 1.6f, new Vector3(0.12f, 3f, 0.12f), Mat.Steel);
                    d.AddBox(foot + new Vector3(-s * 0.3f, 3.05f, 0f), new Vector3(0.65f, 0.08f, 0.08f), Mat.Steel);
                    var lanternAt = foot + new Vector3(-s * 0.55f, 2.7f, 0f);
                    d.AddBox(lanternAt, new Vector3(0.26f, 0.36f, 0.26f), Mat.Steel);
                    var glass = MeshKit.Part(lights, "LampGlass", MeshKit.Box(new Vector3(0.2f, 0.28f, 0.2f)), Mat.Glow, lanternAt);
                    ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), lamp * 1.3f);
                    glass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (true) // every lamp lit since 2026-11-07 (the user: warm and well lit)
                    {
                        var l = new GameObject("LampLight").AddComponent<Light>();
                        l.transform.SetParent(lights, false);
                        l.transform.position = lanternAt;
                        l.type = LightType.Point;
                        l.color = lamp;
                        l.range = 10f;
                        l.intensity = 2.2f;
                        l.shadows = LightShadows.None;
                    }
                }
            // (The bench ring and stone planters round the bonfire went 2026-11-07: six logs to sit on instead, see Build.)
            // Crates, barrels and a hand cart between the stalls and the walls.
            foreach (var (x, z) in new[] { (-12f, -2f), (12.5f, 9f), (-4f, -21f), (7f, 18f) })
            {
                var at = o + S(x, 0f, z);
                for (int n = 0; n < 3; n++)
                {
                    var p = at + new Vector3((float)rng.NextDouble() * 1.6f - 0.8f, 0f, (float)rng.NextDouble() * 1.6f - 0.8f);
                    if (rng.NextDouble() < 0.5) Props.Crate(d, p, 0.7f + (float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() * 90f);
                    else Props.Barrel(d, p);
                }
            }
            // Patches of grass in the corners, a dead tree in the east.
            foreach (var (x, z, r) in new[] { (-18f, -16f, 3.2f), (18f, -15f, 2.8f), (-19f, -9f, 2.5f), (19f, 1f, 3f) })
            {
                var g = MeshKit.Part(lights, "Grass", MeshKit.Cylinder(r, r, 0.03f, 14), Mat.Cloth, o + S(x, 0.015f, z));
                ArtLibrary.Tint(g.GetComponent<Renderer>(), new Color(0.13f, 0.19f, 0.08f));
            }
            var tree = o + S(19f, 0f, 1f);
            d.Add(MeshKit.Cylinder(0.35f, 0.2f, 4.2f, 7), Mat.Wood, Matrix4x4.Translate(tree + Vector3.up * 2.1f));
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f + 15f;
                d.Add(MeshKit.Cylinder(0.1f, 0.03f, 2f, 5), Mat.Wood, Matrix4x4.TRS(tree + Vector3.up * (2.6f + i * 0.35f), Quaternion.Euler(50f, a, 0f), Vector3.one));
            }
            // Banner poles flanking the road where it meets the plaza.
            foreach (float s in new[] { -1f, 1f })
            {
                var pole = o + S(s * 6.2f, 0f, -6f);
                d.AddBox(pole + Vector3.up * 3f, new Vector3(0.14f, 6f, 0.14f), Mat.Wood);
                d.AddBox(pole + new Vector3(0f, 5.8f, 0.6f), new Vector3(0.08f, 0.08f, 1.3f), Mat.Wood);
                Props.Banner(lights, pole + new Vector3(0f, 5.7f, 0.65f), Vector3.right * s, s > 0 ? new Color(0.45f, 0.08f, 0.08f) : new Color(0.12f, 0.12f, 0.3f));
            }
        }

        private static void BuildCastleLife(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            // Lanterns on the inner face of the curtain wall, every 45 degrees (not at the gate).
            for (int i = 0; i < 8; i++)
            {
                float deg = 22.5f + i * 45f;
                if (Mathf.Abs(Mathf.DeltaAngle(deg, 90f)) < 20f) continue;
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Props.WallTorch(lights, o + dir * (Radius - 0.05f) + Vector3.up * 3.4f, -dir, false, Fire);
                Props.Banner(lights, o + dir * (Radius - 0.05f) + Vector3.up * 7.6f, -dir, i % 2 == 0 ? new Color(0.45f, 0.08f, 0.08f) : new Color(0.12f, 0.12f, 0.3f));
            }
            // A well on the plaza, with a lantern on its frame.
            var well = WellSpot; // the middle of the market square
            d.Add(MeshKit.Cylinder(1f, 1.05f, 0.85f, 16), Mat.StoneWall, Matrix4x4.Translate(well));
            d.Add(MeshKit.Cylinder(0.82f, 0.82f, 0.05f, 16), Mat.Dark, Matrix4x4.Translate(well + Vector3.up * 0.8f));
            foreach (float s in new[] { -0.9f, 0.9f }) d.AddBox(well + new Vector3(s, 1.4f, 0f), new Vector3(0.12f, 1.4f, 0.12f), Mat.Wood);
            d.AddBox(well + new Vector3(0f, 2.15f, 0f), new Vector3(2f, 0.12f, 0.12f), Mat.Wood);
            d.Add(MeshKit.Lathe("wellRoof", new[] { new Vector2(1.3f, 0f), new Vector2(0f, 0.8f) }, 4), Mat.Wood, Matrix4x4.TRS(well + Vector3.up * 2.2f, Quaternion.Euler(0f, 45f, 0f), Vector3.one));
            Props.WallTorch(lights, well + new Vector3(0.95f, 1.8f, 0f), Vector3.right, false, Fire);
            // Hay, barrels and a feed trough by the carriage.
            foreach (var (x, z, yaw) in new[] { (5.6f, 17.4f, 10f), (6f, 18.8f, -14f), (4.7f, 21.6f, 30f) })
            {
                var bale = MeshKit.Part(lights, "HayBale", MeshKit.Box(new Vector3(0.9f, 0.5f, 0.55f)), Mat.Cloth, o + S(x, 0.25f, z), new Vector3(0f, yaw, 0f));
                ArtLibrary.Tint(bale.GetComponent<Renderer>(), new Color(0.62f, 0.5f, 0.22f));
                foreach (float bx in new[] { -0.25f, 0.25f })
                {
                    MeshKit.Part(bale.transform, "Twine", MeshKit.Box(new Vector3(0.03f, 0.52f, 0.57f)), Mat.Leather, new Vector3(bx, 0f, 0f));
                }
            }
            Props.Barrel(d, o + S(4.4f, 0f, 18.2f));
            Props.Barrel(d, o + S(5f, 0f, 17.4f));
            d.AddBox(o + S(-4.2f, 0.4f, 22.5f), new Vector3(0.8f, 0.5f, 2.2f), Mat.Wood);
            // Lit windows in the keep's front and the towers: warm squares high in the dark.
            var keep = o + new Vector3(0f, 0f, -Radius + 3f);
            foreach (var (x, y) in new[] { (-6.2f, 4.5f), (6.2f, 4.5f), (-6.2f, 12.5f), (6.2f, 12.5f), (-2.6f, 15.2f), (2.6f, 15.2f) })
                d.AddBox(keep + new Vector3(x, y, 1.06f), new Vector3(0.7f, 1.3f, 0.1f), Mat.Flame);
            // Hanging lanterns over the road between the stalls.
            foreach (float z in new[] { -6f, 2f, 10f })
            {
                var at = o + S(0f, 4.2f, z);
                foreach (float s in new[] { -1f, 1f }) d.AddBox(o + S(s * 4.1f, 2.3f, z), new Vector3(0.14f, 4.6f, 0.14f), Mat.Wood);
                // A sagging rope of little lanterns between the poles.
                for (int k = 0; k <= 8; k++)
                {
                    float u = k / 8f;
                    float sag = Mathf.Sin(u * Mathf.PI) * 0.45f;
                    var p = o + S(-4.1f + u * 8.2f, 4.6f - sag, z);
                    if (k < 8) d.AddBox(p + new Vector3(0.51f, -Mathf.Cos(u * Mathf.PI) * 0.09f, 0f), new Vector3(1.03f * Spread, 0.025f, 0.025f), Mat.Leather, Quaternion.Euler(0f, 0f, -Mathf.Cos(u * Mathf.PI) * 10f));
                    if (k > 0 && k < 8)
                    {
                        d.AddBox(p + Vector3.down * 0.12f, new Vector3(0.012f, 0.22f, 0.012f), Mat.Steel);
                        d.AddBox(p + Vector3.down * 0.3f, new Vector3(0.12f, 0.16f, 0.12f), Mat.Flame);
                    }
                }
                Props.WallTorch(lights, at, Vector3.back, false, new Color(1f, 0.75f, 0.4f));
            }
        }

        /// <summary>The cobbled road from the bonfire to the gate, braziers along both sides.</summary>
        private static void BuildRoad(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            d.AddBox(o + S(0f, 0.02f, 3f), new Vector3(5.9f, 0.04f, 38f * Spread), Mat.Rubble);
            d.AddBox(o + S(-2.2f, 0.06f, 3f), new Vector3(0.3f, 0.12f, 38f * Spread), Mat.StoneWall);
            d.AddBox(o + S(2.2f, 0.06f, 3f), new Vector3(0.3f, 0.12f, 38f * Spread), Mat.StoneWall);
            for (float z = -9f; z <= 15f; z += 8f)
            {
                Props.Brazier(lights, o + S(-3.2f, 0f, z), Fire, false);
                Props.Brazier(lights, o + S(3.2f, 0f, z), Fire, z > 10f);
            }
        }

        /// <summary>A market stall: counter, four posts and a striped awning.</summary>
        private static void Stall(Transform parent, MeshKit.Batcher d, Vector3 front, Vector3 facing, Color a, Color b, bool counter = true)
        {
            var rot = Quaternion.LookRotation(facing);
            var right = rot * Vector3.right;
            var back = -facing;
            if (counter)
            {
                d.AddBox(front + Vector3.up * 0.5f, new Vector3(3.6f, 1f, 0.8f), Mat.Wood, rot);
                d.AddBox(front + Vector3.up * 1.02f, new Vector3(3.8f, 0.06f, 0.95f), Mat.Wood, rot);
            }
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
            lamp.range = 7.5f;
            lamp.intensity = 2.6f;
        }

        private static void BuildStalls(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            // Merchant (west), behind his own table of wares.
            Stall(lights, d, MerchantSpot + Vector3.right * 1.45f, Vector3.right, new Color(0.55f, 0.12f, 0.12f), new Color(0.85f, 0.75f, 0.5f), counter: false); // round Silas and his table (it stood 3.5 m in front of him, empty)
            Props.Merchant(lights, MerchantSpot, MerchantSpot + Vector3.right * 3f, d);

            // Blacksmith (the trade row, west, beside Silas): forge with a chimney and a quench barrel beside the anvil.
            Stall(lights, d, AnvilSpot + S(0.8f, 0f, 0f), Vector3.right, new Color(0.2f, 0.2f, 0.24f), new Color(0.6f, 0.35f, 0.15f));
            var forge = AnvilSpot + S(-2.6f, 0f, -1.6f);
            ForgeChimney = forge + new Vector3(0f, 5.5f, -0.3f);
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
            Props.Barrel(d, AnvilSpot + S(-1.2f, 0f, 1.8f));
            Props.WeaponRack(d, AnvilSpot + S(-3f, 0f, 1.6f), Quaternion.Euler(0f, 90f, 0f));

            // The bounty board: a timber frame with a little roof, notices pinned to it (networked).
            var bs = BountySpot;
            foreach (float z in new[] { -1.5f, 1.5f })
                d.AddBox(bs + new Vector3(-0.1f, 1.3f, z), new Vector3(0.2f, 2.6f, 0.2f), Mat.Wood);
            d.AddBox(bs + new Vector3(-0.12f, 1.55f, 0f), new Vector3(0.1f, 1.3f, 3f), Mat.Wood);
            d.AddBox(bs + new Vector3(-0.1f, 2.7f, 0f), new Vector3(0.9f, 0.08f, 3.4f), Mat.Wood, Quaternion.Euler(0f, 0f, -12f));
            var sign = MeshKit.Part(lights, "BountySign", MeshKit.Box(new Vector3(0.06f, 0.3f, 1.4f)), Mat.Wood, bs + new Vector3(0.0f, 2.42f, 0f));
            ArtLibrary.Tint(sign.GetComponent<Renderer>(), new Color(0.55f, 0.18f, 0.12f));
            var bl = new GameObject("BountyLamp").AddComponent<Light>();
            bl.transform.SetParent(lights, false);
            bl.transform.position = bs + new Vector3(1.2f, 2.4f, 0f);
            bl.type = LightType.Point;
            bl.color = Fire;
            bl.range = 5f;
            bl.intensity = 1.5f;

            // The Hall of Records: a carved slab on a plinth, gilded lines, a candle either side.
            var rs = RecordsSpot;
            d.AddBox(rs + new Vector3(0f, 0.2f, 0f), new Vector3(0.7f, 0.4f, 1.5f), Mat.StoneWall);
            d.AddBox(rs + new Vector3(0f, 1.2f, 0f), new Vector3(0.22f, 1.7f, 1.2f), Mat.Marble, new Color(0.5f, 0.48f, 0.46f), Quaternion.identity); // grey: white marble under the road brazier read as a blank lit panel
            d.AddBox(rs + new Vector3(0f, 2.1f, 0f), new Vector3(0.3f, 0.12f, 1.32f), Mat.StoneWall);
            for (int ln = 0; ln < 5; ln++)
                d.AddBox(rs + new Vector3(0.115f, 1.75f - ln * 0.2f, 0f), new Vector3(0.01f, 0.05f, 0.8f - (ln % 2) * 0.2f), Mat.Gold);
            foreach (float z in new[] { -0.85f, 0.85f })
                Props.AddFlame(lights, rs + new Vector3(0.1f, 0.48f, z), 0.25f, Fire, 0f, 1f, false, light: false);
            // The tailor (far west): bolts of dyed cloth on the counter, more on a rack behind.
            Stall(lights, d, TailorSpot + TailorFacing * 2.4f, TailorFacing, new Color(0.3f, 0.08f, 0.32f), new Color(0.8f, 0.72f, 0.55f));
            var bolts = new[] { new Color(0.42f, 0.04f, 0.05f), new Color(0.05f, 0.07f, 0.18f), new Color(0.26f, 0.07f, 0.36f), new Color(0.82f, 0.79f, 0.7f), new Color(0.04f, 0.2f, 0.2f) };
            for (int i = 0; i < bolts.Length; i++)
            {
                var bolt = MeshKit.Part(lights, "ClothBolt" + i, MeshKit.Cylinder(0.12f, 0.12f, 0.7f, 8), Mat.Cloth,
                    TailorSpot + TailorFacing * 1.5f + new Vector3(0f, 1.02f, -1.1f + i * 0.5f), new Vector3(0f, 0f, 90f));
                ArtLibrary.Tint(bolt.GetComponent<Renderer>(), bolts[i]);
                var hung = MeshKit.Part(lights, "HungCloth" + i, MeshKit.Box(new Vector3(0.05f, 1.3f, 0.42f)), Mat.Cloth,
                    TailorSpot - TailorFacing * 0.9f + new Vector3(0f, 1.25f, -1.1f + i * 0.5f));
                ArtLibrary.Tint(hung.GetComponent<Renderer>(), bolts[(i + 2) % bolts.Length]);
            }
            d.AddBox(TailorSpot - TailorFacing * 0.9f + new Vector3(0f, 2f, 0f), new Vector3(0.06f, 0.06f, 2.8f), Mat.Wood);
            d.AddBox(MannequinSpot + new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.1f, 0.9f), Mat.Wood);

            // The Test of Might's frame (the gong itself is a networked object).
            var g = GongSpot;
            foreach (float z in new[] { -1.5f, 1.5f })
                d.AddBox(g + new Vector3(0.3f, 1.8f, z), new Vector3(0.25f, 3.6f, 0.25f), Mat.Wood);
            d.AddBox(g + new Vector3(0.3f, 3.65f, 0f), new Vector3(0.3f, 0.3f, 3.6f), Mat.Wood);
            d.AddBox(g + new Vector3(-1.4f, 0.05f, 0f), new Vector3(1.6f, 0.1f, 2.4f), Mat.Rubble);
            Props.Banner(lights, g + new Vector3(0.45f, 3.2f, 1.5f), Vector3.left, new Color(0.7f, 0.5f, 0.1f));
        }

        /// <summary>
        /// An iron lamp post with a lantern hung on an arm toward <paramref name="arm"/> (2026-11-07, the user: "MORE LIGHT
        /// SOURCES ... AROUND THINGS WHERE THE PLAYER NEEDS TO SEE OR GO").
        /// </summary>
        private static void LampPost(MeshKit.Batcher d, Transform lights, Vector3 foot, Vector3 arm, float range = 9f, float intensity = 2.6f)
        {
            arm.y = 0f; arm.Normalize();
            var lamp = new Color(1f, 0.72f, 0.4f);
            var rot = Quaternion.LookRotation(arm);
            d.AddBox(foot + Vector3.up * 0.1f, new Vector3(0.4f, 0.2f, 0.4f), Mat.StoneWall);
            d.AddBox(foot + Vector3.up * 1.55f, new Vector3(0.11f, 2.9f, 0.11f), Mat.Steel);
            d.AddBox(foot + arm * 0.3f + Vector3.up * 2.95f, new Vector3(0.07f, 0.07f, 0.66f), Mat.Steel, rot);
            var at = foot + arm * 0.55f + Vector3.up * 2.62f;
            d.AddBox(at + Vector3.up * 0.25f, new Vector3(0.03f, 0.18f, 0.03f), Mat.Steel);
            d.AddBox(at, new Vector3(0.26f, 0.34f, 0.26f), Mat.Steel);
            d.AddBox(at + Vector3.up * 0.2f, new Vector3(0.32f, 0.05f, 0.32f), Mat.Steel);
            var glass = MeshKit.Part(lights, "LampGlass", MeshKit.Box(new Vector3(0.2f, 0.27f, 0.2f)), Mat.Glow, at);
            ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), lamp * 1.3f);
            glass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var l = new GameObject("StationLamp").AddComponent<Light>();
            l.transform.SetParent(lights, false);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = lamp;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        /// <summary>
        /// Painted signs over every station (2026-11-07, the user: show what each station is from a distance), hung from the
        /// stall awnings, fixed over the tavern door and the gong, or on posts where there is nothing to hang them from.
        /// </summary>
        private static void BuildSigns(MeshKit.Batcher d, Transform lights)
        {
            SignArt.Hang(lights, d, MerchantSpot + Vector3.right * 1.65f + Vector3.up * 2.75f, Vector3.right, "Merchant", SignArt.Icon.Potion);
            SignArt.Hang(lights, d, AnvilSpot + S(0.8f, 0f, 0f) + Vector3.right * 0.2f + Vector3.up * 2.75f, Vector3.right, "Smith", SignArt.Icon.Anvil);
            SignArt.Hang(lights, d, TailorSpot + TailorFacing * 2.6f + Vector3.up * 2.75f, TailorFacing, "Tailor", SignArt.Icon.Needle);
            SignArt.Post(lights, d, YardSpot + new Vector3(2.0f, 0f, 1.8f), Vector3.back, Vector3.right, "Drills", SignArt.Icon.Sword);
            var runeFront = new Vector3(-1f, 0f, -0.25f).normalized;
            var runeSide = Vector3.Cross(Vector3.up, runeFront);
            SignArt.Post(lights, d, RunewrightSpot + runeFront * 2.8f + runeSide * 1.7f, -runeSide, runeFront, "Runes", SignArt.Icon.Rune);
            SignArt.Post(lights, d, ShrineSpot + new Vector3(-2.4f, 0f, -2.5f), Vector3.forward, Vector3.left, "Shrine", SignArt.Icon.Candle);
            SignArt.Flat(lights, d, TavernCenter + new Vector3(TavernHalfX + 0.24f, 2.86f, 0f), Vector3.right, "Tavern", SignArt.Icon.Mug, 1.3f);
            SignArt.Hang(lights, d, TentCenter + new Vector3(-4.45f, 2.66f, 0f), Vector3.left, "Games", SignArt.Icon.Dice);
            SignArt.Flat(lights, d, BountySpot + new Vector3(0.07f, 2.42f, 0f), BountyFacing, "Quests", SignArt.Icon.Scroll, 1.3f);
            SignArt.Flat(lights, d, GongSpot + new Vector3(0.3f, 4.02f, 0f), GongFacing, "Might", SignArt.Icon.Bell, 1.2f);
            SignArt.Flat(lights, d, Origin + S(10f, 0f, 11f) + new Vector3(-0.07f, 2.2f, -3.4f), Vector3.left, "Archery", SignArt.Icon.Target, 0.95f);
        }

        /// <summary>Lamps at the stations that stood in the dark: the tavern door and its yard, the tailor, the yard
        /// sergeant, the gong, the omen stone and the hall of records.</summary>
        private static void BuildStationLamps(MeshKit.Batcher d, Transform lights)
        {
            // Two lanterns either side of the tavern door.
            foreach (float z in new[] { -1.65f, 1.65f })
                Props.WallTorch(lights, TavernCenter + new Vector3(TavernHalfX + 0.22f, 2.3f, z), Vector3.right, false, Fire);
            LampPost(d, lights, TailorSpot + new Vector3(1.6f, 0f, -2.6f), Vector3.up + Vector3.forward);
            LampPost(d, lights, YardSpot + new Vector3(2.2f, 0f, -1.5f), Vector3.left);
            LampPost(d, lights, GongSpot + new Vector3(-1.8f, 0f, 2.6f), Vector3.back);
            LampPost(d, lights, OmenSpot + new Vector3(1.6f, 0f, -1.4f), Vector3.left);
            LampPost(d, lights, RecordsSpot + new Vector3(-2.6f, 0f, -1.6f), Vector3.right, 8f, 1.6f); // nearer and brighter blew the marble out to white
            LampPost(d, lights, MannequinSpot + new Vector3(1.1f, 0f, -1.3f), Vector3.left, 9f, 3.4f);
            // The archery range was the darkest corner of camp: the shooting line and both ends of the lanes.
            LampPost(d, lights, Origin + S(10f, 0f, 11f) + new Vector3(-0.7f, 0f, 4.4f), Vector3.right, 11f, 2.8f);
            LampPost(d, lights, Origin + S(17.5f, 0f, 6.2f), Vector3.forward, 10f, 2.6f);
            LampPost(d, lights, Origin + S(17.5f, 0f, 16f), Vector3.back, 10f, 2.6f);
            var wireA = Origin + S(17.5f, 0f, 6.2f) + Vector3.up * 3.0f; var wireB = Origin + S(17.5f, 0f, 16f) + Vector3.up * 3.0f;
            d.AddBox((wireA + wireB) / 2f, new Vector3(0.025f, 0.025f, Vector3.Distance(wireA, wireB)), Mat.Steel);
            for (int i = 1; i <= 3; i++)
            {
                var at = Vector3.Lerp(wireA, wireB, i / 4f) + Vector3.down * 0.35f;
                d.AddBox(at + Vector3.up * 0.18f, new Vector3(0.02f, 0.2f, 0.02f), Mat.Steel);
                d.AddBox(at, new Vector3(0.22f, 0.3f, 0.22f), Mat.Steel);
                var glass = MeshKit.Part(lights, "RangeLantern", MeshKit.Box(new Vector3(0.17f, 0.23f, 0.17f)), Mat.Glow, at);
                ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), new Color(1f, 0.72f, 0.4f) * 1.3f);
                glass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var l = new GameObject("RangeLight").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.position = at;
                l.type = LightType.Point;
                l.color = new Color(1f, 0.72f, 0.4f);
                l.range = 10f;
                l.intensity = 3.4f;
                l.shadows = LightShadows.None;
            }
        }

        /// <summary>The gambling tent: a round striped pavilion, open to the west (the spawn fire).</summary>
        private static void BuildTent(Transform lights, MeshKit.Batcher d)
        {
            var c = TentCenter;
            var red = new Color(0.55f, 0.1f, 0.12f);
            var gold = new Color(0.8f, 0.62f, 0.25f);
            // Walls: cloth panels round 270 degrees, leaving the west side open, toward the fire (until 2026-11-07 the loop left the NORTH open).
            for (int i = 0; i < 14; i++)
            {
                float deg = 225f + i * (270f / 13f); // gap from 135 to 225: open to the west (the fire)
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var panel = MeshKit.Part(lights, "TentWall" + i, MeshKit.Box(new Vector3(1.52f, 2.6f, 0.06f)), Mat.Cloth,
                    c + dir * 4.2f + Vector3.up * 1.3f, Quaternion.LookRotation(dir).eulerAngles);
                ArtLibrary.Tint(panel.GetComponent<Renderer>(), i % 2 == 0 ? red : gold);
            }
            var roof = MeshKit.Part(lights, "TentRoof", MeshKit.Lathe("tentRoof", new[]
            {
                new Vector2(4.6f, 0f), new Vector2(2.2f, 1.4f), new Vector2(0f, 2.6f),
            }, 16), Mat.Cloth, c + Vector3.up * 2.6f);
            ArtLibrary.Tint(roof.GetComponent<Renderer>(), red);
            // The underside (a lathe is one-sided: from inside the roof was missing).
            var roofIn = MeshKit.Part(lights, "TentRoofInside", MeshKit.Lathe("tentRoofIn", new[]
            {
                new Vector2(0f, 2.58f), new Vector2(2.2f, 1.38f), new Vector2(4.6f, -0.02f),
            }, 16), Mat.Cloth, c + Vector3.up * 2.6f);
            ArtLibrary.Tint(roofIn.GetComponent<Renderer>(), new Color(0.4f, 0.08f, 0.1f));
            // Lamp posts either side of the opening.
            LampPost(d, lights, c + new Vector3(-5.3f, 0f, 3.1f), Vector3.right);
            LampPost(d, lights, c + new Vector3(-5.3f, 0f, -3.1f), Vector3.right);
            d.AddBox(c + Vector3.up * 2.6f, new Vector3(0.18f, 5.2f, 0.18f), Mat.Wood);
            var pennant = MeshKit.Part(lights, "Pennant", MeshKit.Box(new Vector3(0.05f, 0.4f, 0.9f)), Mat.Cloth, c + new Vector3(0f, 5.6f, 0.45f));
            ArtLibrary.Tint(pennant.GetComponent<Renderer>(), gold);
            var rug = MeshKit.Part(lights, "Rug", MeshKit.Cylinder(3.4f, 3.4f, 0.03f, 18), Mat.Cloth, c + Vector3.up * 0.02f);
            ArtLibrary.Tint(rug.GetComponent<Renderer>(), new Color(0.3f, 0.08f, 0.15f));
            Props.Candles(lights, c + new Vector3(2.6f, 0f, -1.4f), Fire);
            Props.Candles(lights, c + new Vector3(2.4f, 0f, 2f), Fire);
            var lamp = new GameObject("TentLamp").AddComponent<Light>();
            lamp.transform.SetParent(lights, false);
            lamp.transform.position = c + Vector3.up * 3.2f;
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.65f, 0.35f);
            lamp.range = 8f;
            lamp.intensity = 2.4f;
        }

        /// <summary>The Gutted Lantern: walls with an east door, a pitched roof, the bar, the hearth, tables and the stage.</summary>
        private static void BuildTavern(MeshKit.Batcher s, MeshKit.Batcher d, Transform lights)
        {
            var c = TavernCenter;
            float hx = TavernHalfX, hz = TavernHalfZ, wallH = TavernWall, t = 0.4f;
            var timber = new Color(0.22f, 0.15f, 0.1f);
            var plank = new Color(0.42f, 0.3f, 0.2f);
            // Floorboards.
            d.AddBox(c + new Vector3(0f, 0.015f, 0f), new Vector3(hx * 2f, 0.03f, hz * 2f), Mat.Wood, plank);
            // Walls: west and north/south whole, the east wall with a door 2.2 m wide and 2.5 m high.
            s.AddBox(c + new Vector3(-hx, wallH / 2f, 0f), new Vector3(t, wallH, hz * 2f + t), Mat.StoneWall);
            s.AddBox(c + new Vector3(0f, wallH / 2f, hz), new Vector3(hx * 2f + t, wallH, t), Mat.StoneWall);
            s.AddBox(c + new Vector3(0f, wallH / 2f, -hz), new Vector3(hx * 2f + t, wallH, t), Mat.StoneWall);
            float door = 1.1f, doorH = 2.5f;
            float side = hz - door;
            s.AddBox(c + new Vector3(hx, wallH / 2f, door + side / 2f), new Vector3(t, wallH, side + t), Mat.StoneWall);
            s.AddBox(c + new Vector3(hx, wallH / 2f, -door - side / 2f), new Vector3(t, wallH, side + t), Mat.StoneWall);
            s.AddBox(c + new Vector3(hx, doorH + (wallH - doorH) / 2f, 0f), new Vector3(t, wallH - doorH, door * 2f), Mat.StoneWall);
            // Timber: corner posts, a wall plate round the top, a lintel and door posts.
            foreach (float x in new[] { -hx, hx })
                foreach (float z in new[] { -hz, hz })
                    d.AddBox(c + new Vector3(x, wallH / 2f, z), new Vector3(t + 0.12f, wallH, t + 0.12f), Mat.Wood, timber);
            d.AddBox(c + new Vector3(0f, wallH + 0.1f, hz), new Vector3(hx * 2f + 0.6f, 0.2f, t + 0.1f), Mat.Wood, timber);
            d.AddBox(c + new Vector3(0f, wallH + 0.1f, -hz), new Vector3(hx * 2f + 0.6f, 0.2f, t + 0.1f), Mat.Wood, timber);
            d.AddBox(c + new Vector3(hx + 0.02f, doorH + 0.1f, 0f), new Vector3(t + 0.1f, 0.2f, door * 2f + 0.5f), Mat.Wood, timber);
            foreach (float z in new[] { -door - 0.1f, door + 0.1f }) d.AddBox(c + new Vector3(hx + 0.02f, doorH / 2f, z), new Vector3(t + 0.1f, doorH, 0.18f), Mat.Wood, timber);
            // The roof: two slopes up to a ridge running east-west, and stepped stone gables.
            float rise = 1.9f, eave = hz + 0.5f;
            float slope = Mathf.Sqrt(eave * eave + rise * rise);
            float ang = Mathf.Atan2(rise, eave) * Mathf.Rad2Deg;
            var roof = new Color(0.25f, 0.17f, 0.12f);
            foreach (float sgn in new[] { -1f, 1f })
                s.AddBox(c + new Vector3(0f, wallH + 0.2f + rise / 2f, sgn * eave / 2f), new Vector3(hx * 2f + 0.9f, 0.16f, slope), Mat.Wood, roof, Quaternion.Euler(sgn * ang, 0f, 0f));
            d.AddBox(c + new Vector3(0f, wallH + 0.2f + rise + 0.05f, 0f), new Vector3(hx * 2f + 1f, 0.18f, 0.25f), Mat.Wood, timber);
            foreach (float x in new[] { -hx, hx })
                for (int i = 0; i < 4; i++)
                {
                    float y0 = wallH + i * rise / 4f, w = (eave * 2f) * (1f - (i + 0.5f) / 4f);
                    s.AddBox(c + new Vector3(x, y0 + rise / 8f + 0.1f, 0f), new Vector3(t, rise / 4f, w), Mat.StoneWall);
                }
            // Rafters under the roof.
            for (int i = -3; i <= 3; i += 2) d.AddBox(c + new Vector3(i * 1.05f, wallH - 0.15f, 0f), new Vector3(0.16f, 0.2f, hz * 2f), Mat.Wood, timber);
            // A chimney above the hearth.
            s.AddBox(c + new Vector3(0.6f, wallH + rise * 0.5f + 0.6f, hz - 0.1f), new Vector3(1f, rise + 1.4f, 0.8f), Mat.StoneWall);
            TavernChimney = c + new Vector3(0.6f, wallH + rise + 1.4f, hz - 0.1f);

            // The hearth on the north wall, a fire in it.
            var hearth = c + new Vector3(0.6f, 0f, hz - 0.45f);
            s.AddBox(hearth + new Vector3(-0.75f, 0.65f, 0f), new Vector3(0.3f, 1.3f, 0.7f), Mat.StoneWall);
            s.AddBox(hearth + new Vector3(0.75f, 0.65f, 0f), new Vector3(0.3f, 1.3f, 0.7f), Mat.StoneWall);
            s.AddBox(hearth + new Vector3(0f, 1.4f, 0f), new Vector3(1.9f, 0.3f, 0.8f), Mat.StoneWall);
            d.AddBox(hearth + new Vector3(0f, 0.05f, 0f), new Vector3(1.2f, 0.1f, 0.6f), Mat.Rubble);
            for (int i = 0; i < 3; i++) d.Add(MeshKit.Cylinder(0.06f, 0.06f, 0.7f, 6), Mat.Wood, Matrix4x4.TRS(hearth + new Vector3(0f, 0.15f, 0f), Quaternion.Euler(0f, i * 60f, 85f), Vector3.one));
            Props.Candles(lights, c + new Vector3(-hx + 0.5f, 0f, hz - 0.5f), Fire);
            var fire = MeshKit.Pivot(lights, "TavernFire", hearth + Vector3.up * 0.1f);
            Props.AddFlame(fire, Vector3.zero, 1.2f, new Color(1f, 0.55f, 0.25f), 4f, 8f, false);

            // The bar: a counter along the west side, a shelf of barrels behind it.
            var bar = TavernKeepSpot + new Vector3(-0.4f, 0f, 0f);
            d.AddBox(bar + new Vector3(0f, 0.55f, 0f), new Vector3(0.6f, 1.1f, 3.4f), Mat.Wood, plank * 0.8f);
            d.AddBox(bar + new Vector3(0.05f, 1.12f, 0f), new Vector3(0.75f, 0.06f, 3.5f), Mat.Wood, timber);
            for (int i = 0; i < 3; i++)
            {
                var keg = c + new Vector3(-hx + 0.55f, 0.45f, -0.9f + i * 0.9f);
                // On its side a Cylinder runs from its base toward -x: start half a keg to +x so the hoops sit on it.
                d.Add(MeshKit.Cylinder(0.32f, 0.32f, 0.75f, 12), Mat.Wood, Matrix4x4.TRS(keg + new Vector3(0.375f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
                d.Add(MeshKit.Cylinder(0.33f, 0.33f, 0.05f, 12), Mat.Steel, Matrix4x4.TRS(keg + new Vector3(0.225f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
                d.Add(MeshKit.Cylinder(0.33f, 0.33f, 0.05f, 12), Mat.Steel, Matrix4x4.TRS(keg + new Vector3(-0.175f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
            }
            d.AddBox(c + new Vector3(-hx + 0.3f, 1.75f, 0f), new Vector3(0.3f, 0.05f, 3f), Mat.Wood, timber);
            for (int i = 0; i < 7; i++) d.Add(MeshKit.Cylinder(0.05f, 0.05f, 0.14f, 8), Mat.Wood, Matrix4x4.Translate(c + new Vector3(-hx + 0.3f, 1.85f, -1.3f + i * 0.43f)));

            // The stage, and tables with stools.
            d.AddBox(TavernBandSpot + new Vector3(0f, 0.12f, -0.95f), new Vector3(3.2f, 0.25f, 1.6f), Mat.Wood, plank * 0.9f);
            foreach (var at in new[] { c + new Vector3(0.1f, 0f, 0.9f), c + new Vector3(2.4f, 0f, 1.6f), c + new Vector3(-0.4f, 0f, -1.6f) })
            {
                d.Add(MeshKit.Cylinder(0.08f, 0.14f, 0.75f, 8), Mat.Wood, Matrix4x4.Translate(at));
                d.Add(MeshKit.Cylinder(0.6f, 0.6f, 0.06f, 14), Mat.Wood, Matrix4x4.Translate(at + Vector3.up * 0.75f));
                for (int k = 0; k < 3; k++)
                {
                    float a = k * 120f * Mathf.Deg2Rad + 0.4f;
                    var stool = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.95f;
                    d.Add(MeshKit.Cylinder(0.18f, 0.16f, 0.45f, 8), Mat.Wood, Matrix4x4.Translate(stool));
                }
                d.Add(MeshKit.Cylinder(0.05f, 0.05f, 0.13f, 8), Mat.Wood, Matrix4x4.Translate(at + new Vector3(0.15f, 0.78f, 0.1f)));
                // One stub of candle on each table (a floor cluster of them was the size of a bonfire up here).
                d.Add(MeshKit.Cylinder(0.03f, 0.03f, 0.1f, 6), Mat.Bone, Matrix4x4.Translate(at + new Vector3(-0.2f, 0.78f, -0.1f)));
                var wick = MeshKit.Part(lights, "TableCandle", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, at + new Vector3(-0.2f, 0.91f, -0.1f), default, new Vector3(1f, 1.6f, 1f));
                ArtLibrary.SetGlow(wick.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 2f);
            }

            // Windows glowing warm on the north and south walls; a lamp over the bar; a chandelier.
            foreach (float sgn in new[] { -1f, 1f })
                foreach (float x in new[] { -1.8f, 2.4f })
                {
                    var pane = MeshKit.Part(lights, "TavernWindow", MeshKit.Box(new Vector3(0.8f, 0.9f, 0.05f)), Mat.Glow, c + new Vector3(x, 1.9f, sgn * (hz + t / 2f + 0.01f)));
                    ArtLibrary.SetGlow(pane.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 1.3f);
                    d.AddBox(c + new Vector3(x, 1.9f, sgn * (hz + t / 2f + 0.03f)), new Vector3(0.06f, 0.95f, 0.04f), Mat.Wood, timber);
                    d.AddBox(c + new Vector3(x, 1.9f, sgn * (hz + t / 2f + 0.03f)), new Vector3(0.85f, 0.06f, 0.04f), Mat.Wood, timber);
                }
            Props.Chandelier(lights, d, c + new Vector3(0.6f, wallH + 0.3f, 0.2f), 0.9f, Fire);
            var lamp = new GameObject("TavernLamp").AddComponent<Light>();
            lamp.transform.SetParent(lights, false);
            // Hung over the customer side of the counter: behind the keep it lit only the back of their head.
            var lampAt = TavernKeepSpot + new Vector3(0.9f, 2.25f, 0f);
            lamp.transform.position = lampAt;
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.66f, 0.38f);
            lamp.range = 6.5f;
            lamp.intensity = 2.4f;
            d.AddBox(lampAt + new Vector3(0f, 0.45f, 0f), new Vector3(0.03f, 0.6f, 0.03f), Mat.Steel);
            d.AddBox(lampAt + new Vector3(0f, 0.12f, 0f), new Vector3(0.2f, 0.05f, 0.2f), Mat.Steel);
            d.AddBox(lampAt + new Vector3(0f, -0.14f, 0f), new Vector3(0.18f, 0.04f, 0.18f), Mat.Steel);
            var lampPane = MeshKit.Part(lights, "BarLantern", MeshKit.Box(new Vector3(0.13f, 0.2f, 0.13f)), Mat.Glow, lampAt);
            ArtLibrary.SetGlow(lampPane.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.18f) * 0.8f);

            // The sign over the door: a board on an iron arm, a lantern painted on it, its own lantern beneath.
            var signArm = c + new Vector3(hx + 0.6f, doorH + 0.5f, -door - 0.5f);
            d.AddBox(signArm, new Vector3(1.2f, 0.06f, 0.06f), Mat.Steel);
            d.AddBox(signArm + new Vector3(0.25f, -0.45f, 0f), new Vector3(0.06f, 0.7f, 0.9f), Mat.Wood, plank);
            // A lantern painted on both faces: a lit pane caged by dark bars, cap, base and ring. It was one bare glowing
            // slab that bloomed into a blank white card from the street.
            for (int face2 = -1; face2 <= 1; face2 += 2)
            {
                var face = signArm + new Vector3(0.25f + face2 * 0.04f, -0.45f, 0f);
                var pane = MeshKit.Part(lights, "TavernSignLantern", MeshKit.Box(new Vector3(0.012f, 0.2f, 0.14f)), Mat.Glow, face + new Vector3(0f, -0.03f, 0f));
                ArtLibrary.SetGlow(pane.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.15f) * 0.55f);
                var ink = new Color(0.08f, 0.06f, 0.05f);
                float o = face2 * 0.008f;
                d.AddBox(face + new Vector3(o, 0.1f, 0f), new Vector3(0.014f, 0.05f, 0.2f), Mat.Steel, ink);   // cap
                d.AddBox(face + new Vector3(o, 0.155f, 0f), new Vector3(0.014f, 0.06f, 0.05f), Mat.Steel, ink); // ring
                d.AddBox(face + new Vector3(o, -0.15f, 0f), new Vector3(0.014f, 0.04f, 0.18f), Mat.Steel, ink); // base
                for (int b = -1; b <= 1; b++)
                    d.AddBox(face + new Vector3(o, -0.03f, b * 0.06f), new Vector3(0.014f, 0.2f, 0.016f), Mat.Steel, ink);
            }
            Props.WallTorch(lights, c + new Vector3(hx + t / 2f, 2.3f, door + 0.5f), Vector3.right, false, Fire);
        }

        private static void BuildYard(Vector3 o, MeshKit.Batcher d, Transform lights)
        {
            // Straw under the dummies, a fence on the plaza side, and spare arms.
            d.AddBox(o + YardShift + S(-13f, 0.02f, -13.5f), new Vector3(5f, 0.03f, 9f), Mat.Wood);
            for (float z = -17.5f; z <= -9.5f; z += 2f)
                d.AddBox(o + YardShift + S(-10.2f, 0.5f, z), new Vector3(0.14f, 1f, 0.14f), Mat.Wood);
            d.AddBox(o + YardShift + S(-10.2f, 0.85f, -13.5f), new Vector3(0.08f, 0.1f, 8f), Mat.Wood);
            Props.WeaponRack(d, o + YardShift + S(-15.8f, 0f, -13.5f), Quaternion.Euler(0f, 90f, 0f));
            // Sergeant Brann's corner: a stool, a bucket and a rack of practice swords by the yard fence.
            d.AddBox(YardSpot + new Vector3(-0.9f, 0.25f, 0.8f), new Vector3(0.4f, 0.5f, 0.4f), Mat.Wood);
            d.Add(MeshKit.Cylinder(0.18f, 0.15f, 0.32f, 10), Mat.Wood, Matrix4x4.Translate(YardSpot + new Vector3(-0.9f, 0f, -0.8f)));
            for (int i = 0; i < 4; i++) d.AddBox(YardSpot + new Vector3(-1.05f, 0.6f, -0.3f + i * 0.18f), new Vector3(0.04f, 1.2f, 0.03f), Mat.Wood, Quaternion.Euler(0f, 0f, 10f));
            Props.Brazier(lights, o + YardShift + S(-11f, 0f, -18.5f), Fire, false);
            Props.Crate(d, o + S(14f, 0f, -14f), 1.2f, 12f);
            Props.Crate(d, o + S(15.2f, 0f, -12.8f), 0.9f, -8f);
            Props.Barrel(d, o + S(13.8f, 0f, -12.2f));
        }

        /// <summary>Ysolde's ring of standing stones: tall slabs cut with glowing runes, a low rune table at her side.</summary>
        private static void BuildRuneStones(MeshKit.Batcher d, Transform lights)
        {
            var c = RunewrightSpot + new Vector3(0.8f, 0f, 0.2f);
            var glow = new Color(0.45f, 0.85f, 1f);
            // Six tall, tapering menhirs in a broken ring (an open side facing the road), runes cut on
            // both faces so they glow from outside the ring too; a lintel across the tallest pair.
            var menhir = MeshKit.Lathe("menhir", new[]
            {
                new Vector2(0.42f, 0f), new Vector2(0.46f, 0.3f), new Vector2(0.4f, 1.4f), new Vector2(0.3f, 2.4f), new Vector2(0.12f, 2.8f), new Vector2(0f, 2.86f),
            }, 7);
            var tops = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float a = (35f + i * 58f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = c + dir * 3.1f;
                float k = 0.85f + (i % 3) * 0.14f;
                var rot = Quaternion.LookRotation(-dir) * Quaternion.Euler(i % 2 == 0 ? 3f : -4f, 0f, i % 2 == 0 ? 2f : -3f);
                d.Add(menhir, Mat.StoneWall, Matrix4x4.TRS(p, rot, new Vector3(1f, k, 0.62f)));
                tops[i] = p + Vector3.up * 2.86f * k;
                for (int f = -1; f <= 1; f += 2)
                {
                    var rune = MeshKit.Part(lights, "StandingRune", MeshKit.Box(new Vector3(0.16f, 0.5f, 0.02f)), Mat.Glow, p + dir * (f * 0.27f) + Vector3.up * (1.5f * k), (rot * Quaternion.Euler(0f, f > 0 ? 180f : 0f, 0f)).eulerAngles);
                    ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), glow * 1.25f);
                    var bar = MeshKit.Part(lights, "StandingRuneBar", MeshKit.Box(new Vector3(0.3f, 0.05f, 0.02f)), Mat.Glow, p + dir * (f * 0.27f) + Vector3.up * (1.62f * k), (rot * Quaternion.Euler(0f, f > 0 ? 180f : 0f, 25f)).eulerAngles);
                    ArtLibrary.SetGlow(bar.GetComponent<Renderer>(), glow * 1.1f);
                }
            }
            var lintelMid = (tops[1] + tops[2]) / 2f;
            d.AddBox(lintelMid + Vector3.up * 0.1f, new Vector3(0.5f, 0.38f, Vector3.Distance(tops[1], tops[2]) + 0.6f), Mat.StoneWall, Quaternion.LookRotation(tops[2] - tops[1]));
            // A ring of runes cut in the turf between the stones, glowing faintly.
            var ring = MeshKit.Part(lights, "RuneRing", MeshKit.Lathe("runeRing", new[] { new Vector2(2.35f, 0f), new Vector2(2.45f, 0.01f), new Vector2(2.55f, 0f) }, 40, false), Mat.Glow, c + Vector3.up * 0.02f);
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), glow * 0.7f);
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f * Mathf.Deg2Rad;
                var mark = MeshKit.Part(lights, "RingRune", MeshKit.Box(new Vector3(0.08f, 0.012f, 0.22f)), Mat.Glow, c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.1f + Vector3.up * 0.02f, new Vector3(0f, -i * 22.5f + (i % 2) * 30f, 0f));
                ArtLibrary.SetGlow(mark.GetComponent<Renderer>(), glow * 0.9f);
            }
            // The rune table: a slab on two stones, with rune tiles laid out and a candle.
            d.AddBox(RunewrightSpot + new Vector3(0.9f, 0.4f, -0.2f), new Vector3(0.3f, 0.8f, 0.3f), Mat.StoneWall);
            d.AddBox(RunewrightSpot + new Vector3(0.9f, 0.4f, 0.7f), new Vector3(0.3f, 0.8f, 0.3f), Mat.StoneWall);
            d.AddBox(RunewrightSpot + new Vector3(0.9f, 0.84f, 0.25f), new Vector3(0.75f, 0.08f, 1.3f), Mat.StoneWall);
            for (int i = 0; i < 5; i++)
            {
                var tile = MeshKit.Part(lights, "RuneTile", MeshKit.Box(new Vector3(0.1f, 0.02f, 0.13f)), Mat.Glow, RunewrightSpot + new Vector3(0.85f + (i % 2) * 0.15f, 0.9f, -0.15f + i * 0.2f), new Vector3(0f, i * 17f, 0f));
                ArtLibrary.SetGlow(tile.GetComponent<Renderer>(), glow * (0.7f + 0.15f * i));
            }
            Props.Candles(lights, RunewrightSpot + new Vector3(0.95f, 0.88f, 0.75f), new Color(0.6f, 0.85f, 1f));
            var l = new GameObject("RuneLight").AddComponent<Light>();
            l.transform.SetParent(lights, false);
            l.transform.position = c + Vector3.up * 2.4f;
            l.type = LightType.Point;
            l.color = glow;
            l.intensity = 2.8f;
            l.range = 10f;
            // A cold key light on Ysolde herself, so she is not a silhouette against the stones.
            var key = new GameObject("YsoldeLight").AddComponent<Light>();
            key.transform.SetParent(lights, false);
            key.transform.position = RunewrightSpot + new Vector3(-1.2f, 2.2f, -1.4f);
            key.type = LightType.Point;
            key.color = new Color(0.75f, 0.85f, 1f);
            key.intensity = 1.4f;
            key.range = 5f;
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
            var line = Origin + S(10f, 0f, 11f);
            d.AddBox(line + new Vector3(0f, 0.55f, 0f), new Vector3(0.1f, 0.1f, 6f), Mat.Wood);
            for (int i = -1; i <= 1; i++) d.AddBox(line + new Vector3(0f, 0.3f, i * 2.9f), new Vector3(0.12f, 0.6f, 0.12f), Mat.Wood);
            d.AddBox(line + new Vector3(0f, 1.2f, -3.4f), new Vector3(0.1f, 2.4f, 0.1f), Mat.Wood);
            d.AddBox(line + new Vector3(0f, 2.2f, -3.4f), new Vector3(0.08f, 0.5f, 1f), Mat.Wood);
            // Hay bales stacked behind the targets to catch the misses.
            for (int i = 0; i < 5; i++)
                d.AddBox(Origin + S(21.2f, 0.45f + (i % 2) * 0.9f, 7.5f + i * 1.6f), new Vector3(1f, 0.9f, 1.5f), Mat.Wood, Quaternion.Euler(0f, 12f * (i % 3), 0f));
            Props.Brazier(lights, line + new Vector3(-0.8f, 0f, 3.6f), Fire, false);
        }

        /// <summary>No roof: a pale moon and a scatter of stars over the walls.</summary>
        private static void BuildSky(Transform lights)
        {
            var moonAt = Origin + S(28f, 42f, 50f);
            var moon = MeshKit.Part(lights, "Moon", MeshKit.Ball(4f, 16, 12), Mat.Glow, moonAt);
            ArtLibrary.SetGlow(moon.GetComponent<Renderer>(), new Color(0.9f, 0.9f, 1f) * 1.15f);
            moon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Maria: darker seas on the face turned toward the castle.
            var toCastle = (Origin + Vector3.up * 2f - moonAt).normalized;
            var face = Quaternion.LookRotation(toCastle);
            var mrng = new System.Random(11);
            for (int i = 0; i < 6; i++)
            {
                var off = face * new Vector3((float)mrng.NextDouble() * 4.4f - 2.2f, (float)mrng.NextDouble() * 4.4f - 2.2f, 0f);
                if (off.magnitude > 2.6f) off = off.normalized * 2.6f;
                var sea = MeshKit.Part(lights, "Mare", MeshKit.Ball(0.9f + (float)mrng.NextDouble() * 0.7f, 10, 8), Mat.Glow, moonAt + off + toCastle * (Mathf.Sqrt(Mathf.Max(0f, 16f - off.sqrMagnitude)) - 0.35f), face.eulerAngles, new Vector3(1f, 0.8f, 0.3f));
                ArtLibrary.SetGlow(sea.GetComponent<Renderer>(), new Color(0.7f, 0.73f, 0.84f) * 0.95f);
                sea.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // A pale halo round it.
            // Three faint shells stacked, so the glow falls off instead of ending at an edge.
            foreach (var (radius, alpha) in new[] { (5.6f, 0.05f), (7.6f, 0.035f), (10.5f, 0.022f) })
            {
                var halo = MeshKit.Part(lights, "MoonHalo", MeshKit.Ball(radius, 18, 12), Mat.Smoke, moonAt - toCastle * (radius - 3f));
                ArtLibrary.Tint(halo.GetComponent<Renderer>(), new Color(0.55f, 0.62f, 0.85f, alpha));
                halo.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var rng = new System.Random(7);
            var star = MeshKit.Box(Vector3.one * 0.22f);
            var stars = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 120; i++)
            {
                float az = (float)rng.NextDouble() * Mathf.PI * 2f, el = 0.35f + (float)rng.NextDouble() * 1.1f;
                var dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                var s = MeshKit.Part(lights, "Star", star, Mat.Glow, Origin + dir * 68f);
                s.transform.localScale = Vector3.one * (0.6f + (float)rng.NextDouble() * 0.9f);
                ArtLibrary.SetGlow(s.GetComponent<Renderer>(), new Color(0.8f, 0.85f, 1f) * (0.6f + (float)rng.NextDouble() * 0.9f));
                s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                stars.Add(s.GetComponent<Renderer>());
            }
            // Ragged night clouds, drifting slowly over the moon (Art/NightSky moves them).
            var clouds = new System.Collections.Generic.List<Transform>();
            var crng = new System.Random(5);
            for (int c = 0; c < 7; c++)
            {
                var cloud = MeshKit.Pivot(lights, "Cloud", Origin + Vector3.up * 46f);
                for (int k = 0; k < 4; k++)
                {
                    var puff = MeshKit.Part(cloud, "Puff", MeshKit.Ball(5f, 10, 6), Mat.Smoke,
                        new Vector3(k * 6f - 9f + (float)crng.NextDouble() * 3f, (float)crng.NextDouble() * 1.5f, (float)crng.NextDouble() * 4f - 2f), Vector3.zero,
                        new Vector3(1.6f + (float)crng.NextDouble(), 0.35f + (float)crng.NextDouble() * 0.2f, 1f));
                    ArtLibrary.Tint(puff.GetComponent<Renderer>(), new Color(0.12f, 0.12f, 0.17f, 0.38f));
                    puff.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                clouds.Add(cloud);
            }
            var sky = lights.gameObject.AddComponent<Art.NightSky>();
            sky.Stars = stars.ToArray();
            sky.Clouds = clouds.ToArray();
            sky.Center = Origin;
            var moonlight = new GameObject("Moonlight").AddComponent<Light>();
            moonlight.transform.SetParent(lights, false);
            moonlight.transform.position = Origin + S(0f, 40f, 10f);
            moonlight.type = LightType.Point;
            moonlight.color = new Color(0.55f, 0.65f, 1f);
            moonlight.range = 90f;
            moonlight.intensity = 0.5f;
            moonlight.shadows = LightShadows.None;
        }
    }
}
