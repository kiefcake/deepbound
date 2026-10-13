using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Last Lantern: the camp at the mouth of the dungeon where a party gathers
    /// between runs (design doc section 6, "LOBBY").
    ///
    /// <para>
    /// A single fixed cavern, so unlike floors it needs no seed — every peer builds the
    /// same thing. It sits far above the dungeon's footprint so the two can never overlap,
    /// and it has no navmesh: nothing hostile lives here.
    /// </para>
    ///
    /// Contents: a campfire to stand around, training dummies to test your kit on, and the
    /// carriage. Everyone standing in the carriage's boarding circle starts the run.
    /// </summary>
    public static class Lobby
    {
        public static readonly Vector3 Origin = new Vector3(0f, 300f, 0f);
        public static readonly Vector2 Size = new Vector2(30f, 26f);
        private const float Height = 10f;
        private const float Wall = 0.8f;

        /// <summary>Boarding circle beside the carriage, at the north end of the cavern.</summary>
        public static readonly Vector3 BoardingCenter = Origin + new Vector3(0f, 0f, 8.5f);
        public const float BoardingRadius = 3.2f;

        public static readonly Vector3[] DummySpots =
        {
            Origin + new Vector3(-10f, 0f, -4f),
            Origin + new Vector3(-10f, 0f, 1f),
        };

        public static Vector3 SpawnFor(ulong clientId)
        {
            float angle = clientId * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
            return Origin + new Vector3(0f, 0f, -6f) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.2f + Vector3.up * 0.1f;
        }

        public static bool InBoardingCircle(Vector3 p)
        {
            var d = p - BoardingCenter;
            return Mathf.Abs(d.y) < 2f && new Vector2(d.x, d.z).magnitude <= BoardingRadius;
        }

        public static GameObject Build()
        {
            var root = new GameObject("Lobby (The Last Lantern)");
            var structure = new MeshKit.Batcher();
            var dressing = new MeshKit.Batcher();
            var lights = root.transform;
            var o = Origin;
            float hx = Size.x / 2f, hz = Size.y / 2f;

            // Shell.
            structure.AddBox(o + Vector3.down * 0.2f, new Vector3(Size.x + Wall * 2f, 0.4f, Size.y + Wall * 2f), Mat.Flagstone);
            structure.AddBox(o + Vector3.up * (Height + 0.2f), new Vector3(Size.x + Wall * 2f, 0.4f, Size.y + Wall * 2f), Mat.Rubble);
            structure.AddBox(o + new Vector3(0f, Height / 2f, hz + Wall / 2f), new Vector3(Size.x + Wall * 2f, Height, Wall), Mat.StoneWall);
            structure.AddBox(o + new Vector3(0f, Height / 2f, -hz - Wall / 2f), new Vector3(Size.x + Wall * 2f, Height, Wall), Mat.StoneWall);
            structure.AddBox(o + new Vector3(hx + Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Size.y), Mat.StoneWall);
            structure.AddBox(o + new Vector3(-hx - Wall / 2f, Height / 2f, 0f), new Vector3(Wall, Height, Size.y), Mat.StoneWall);

            // A dark archway in the north wall: the road down. The carriage waits in front of it.
            dressing.AddBox(o + new Vector3(0f, 2.6f, hz - 0.05f), new Vector3(5f, 5.2f, 0.2f), Mat.Dark);
            dressing.AddBox(o + new Vector3(-2.9f, 2.8f, hz - 0.4f), new Vector3(0.8f, 5.6f, 0.8f), Mat.StoneWall);
            dressing.AddBox(o + new Vector3(2.9f, 2.8f, hz - 0.4f), new Vector3(0.8f, 5.6f, 0.8f), Mat.StoneWall);
            dressing.AddBox(o + new Vector3(0f, 5.9f, hz - 0.4f), new Vector3(6.6f, 0.8f, 0.9f), Mat.StoneWall);

            // Timber roof props, like a mine head.
            for (float x = -hx + 4f; x < hx - 1f; x += 6f)
            {
                dressing.AddBox(o + new Vector3(x, Height - 0.4f, 0f), new Vector3(0.5f, 0.6f, Size.y), Mat.Wood);
                dressing.AddBox(o + new Vector3(x, Height / 2f, hz - 0.5f), new Vector3(0.45f, Height, 0.45f), Mat.Wood);
                dressing.AddBox(o + new Vector3(x, Height / 2f, -hz + 0.5f), new Vector3(0.45f, Height, 0.45f), Mat.Wood);
            }

            // Camp: fire ring of stones and log seats.
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                dressing.AddBox(o + new Vector3(Mathf.Cos(a), 0.12f, Mathf.Sin(a)) * 1.1f, new Vector3(0.35f, 0.25f, 0.3f), Mat.Rubble, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                var p = o + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.4f;
                dressing.Add(MeshKit.Cylinder(0.25f, 0.25f, 1.8f, 8), Mat.Wood,
                    Matrix4x4.TRS(p + Vector3.up * 0.25f, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 90f) , Vector3.one));
            }
            Props.Brazier(lights, o, new Color(1f, 0.55f, 0.25f), true);
            Props.Brazier(lights, o + new Vector3(-7f, 0f, -1.5f), new Color(1f, 0.6f, 0.3f), false);
            Props.Brazier(lights, o + new Vector3(10f, 0f, 5f), new Color(1f, 0.6f, 0.3f), false);

            // Clutter along the walls.
            Props.Crate(dressing, o + new Vector3(12.5f, 0f, -10.5f), 1.2f, 8f);
            Props.Crate(dressing, o + new Vector3(11.2f, 0f, -10.8f), 0.9f, -12f);
            Props.Barrel(dressing, o + new Vector3(12.6f, 0f, -8.8f));
            Props.Barrel(dressing, o + new Vector3(-12.6f, 0f, 10.5f));
            Props.Barrel(dressing, o + new Vector3(-11.7f, 0f, 10.9f));
            Props.WeaponRack(dressing, o + new Vector3(-14.2f, 0f, -1.5f), Quaternion.Euler(0f, 90f, 0f));

            // Training ground marker: straw-strewn patch under the dummies.
            dressing.AddBox(o + new Vector3(-10f, 0.01f, -1.5f), new Vector3(4f, 0.02f, 8f), Mat.Wood);

            Props.Carriage(lights, dressing, o + new Vector3(6f, 0f, 9f), Quaternion.Euler(0f, 90f, 0f));
            Props.BoardingCircle(lights, BoardingCenter, BoardingRadius);

            // Wall torches, two per long wall, one per short.
            Props.WallTorch(lights, o + new Vector3(-8f, 2.8f, -hz), Vector3.forward, true);
            Props.WallTorch(lights, o + new Vector3(8f, 2.8f, -hz), Vector3.forward, false);
            Props.WallTorch(lights, o + new Vector3(-hx, 2.8f, -6f), Vector3.right, false);
            Props.WallTorch(lights, o + new Vector3(hx, 2.8f, 2f), Vector3.left, true);
            Props.WallTorch(lights, o + new Vector3(-6f, 2.8f, hz), Vector3.back, false);
            Props.WallTorch(lights, o + new Vector3(-hx, 2.8f, 6f), Vector3.right, false);

            structure.Build(root.transform, "LobbyStructure", collide: true);
            dressing.Build(root.transform, "LobbyDressing", collide: true);
            DungeonBuilder.ApplyAtmosphere();
            return root;
        }
    }
}
