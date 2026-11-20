using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The look of one floor. Every floor shares the same generator and art kit; a theme
    /// changes the lighting, tints and dressing on top, which is enough to make three
    /// floors feel like a descent through three different places.
    ///
    /// Indexed by depth, so it is deterministic on every peer like the layout itself.
    /// </summary>
    public class DungeonTheme
    {
        public string Name;
        public Color Fog;
        public float FogDensity;
        public Color AmbientSky, AmbientEquator, AmbientGround;
        public Color TorchColor;
        public Color BrazierColor;
        /// <summary>Multiplies the stone and floor textures. White = untinted.</summary>
        public Color StoneTint = Color.white;
        public Color FloorTint = Color.white;
        /// <summary>Standing water over the floor, if > 0: its depth in metres.</summary>
        public float WaterDepth;
        public Color WaterColor;
        /// <summary>Light falling through cracks in the ceiling (moonlight, mostly).</summary>
        public Color ShaftColor = new Color(0.55f, 0.65f, 0.85f);
        /// <summary>Chance per enemy spawn of a ranged archer instead of a swordsman.</summary>
        public float ArcherChance;

        public static readonly DungeonTheme[] ByDepth =
        {
            new DungeonTheme
            {
                Name = "The Ossuary",
                Fog = new Color(0.018f, 0.02f, 0.028f), FogDensity = 0.042f,
                AmbientSky = new Color(0.07f, 0.075f, 0.095f), AmbientEquator = new Color(0.05f, 0.05f, 0.06f), AmbientGround = new Color(0.03f, 0.028f, 0.026f),
                TorchColor = new Color(1f, 0.62f, 0.32f), BrazierColor = new Color(1f, 0.55f, 0.25f),
                ArcherChance = 0.2f,
            },
            new DungeonTheme
            {
                // Flooded vaults: green-teal murk, moss on the stone, shin-deep water.
                Name = "The Drowned Vaults",
                Fog = new Color(0.012f, 0.03f, 0.03f), FogDensity = 0.05f,
                AmbientSky = new Color(0.05f, 0.09f, 0.085f), AmbientEquator = new Color(0.035f, 0.06f, 0.055f), AmbientGround = new Color(0.02f, 0.035f, 0.03f),
                TorchColor = new Color(0.85f, 0.75f, 0.45f), BrazierColor = new Color(0.45f, 0.95f, 0.7f),
                StoneTint = new Color(0.72f, 0.85f, 0.72f), FloorTint = new Color(0.65f, 0.78f, 0.68f),
                WaterDepth = 0.28f, WaterColor = new Color(0.05f, 0.13f, 0.11f, 0.82f),
                ShaftColor = new Color(0.45f, 0.8f, 0.75f),
                ArcherChance = 0.3f,
            },
            new DungeonTheme
            {
                // The king's halls: cold blue, violet shadows, gilded stone.
                Name = "The Hollow Throne",
                Fog = new Color(0.02f, 0.018f, 0.035f), FogDensity = 0.038f,
                AmbientSky = new Color(0.085f, 0.08f, 0.14f), AmbientEquator = new Color(0.06f, 0.055f, 0.095f), AmbientGround = new Color(0.036f, 0.03f, 0.048f),
                TorchColor = new Color(0.55f, 0.7f, 1f), BrazierColor = new Color(0.35f, 0.65f, 1f),
                StoneTint = new Color(0.78f, 0.76f, 0.92f), FloorTint = new Color(0.75f, 0.72f, 0.85f),
                ShaftColor = new Color(0.68f, 0.6f, 1f),
                ArcherChance = 0.35f,
            },
            new DungeonTheme
            {
                // The Black Cathedral: blood-red candlelight, black stone, a wine-dark murk.
                Name = "The Black Cathedral",
                Fog = new Color(0.03f, 0.012f, 0.016f), FogDensity = 0.036f,
                // 2026-11-20 visual pass: a little more light in the stone (the deep acts read as black in every shot).
                AmbientSky = new Color(0.12f, 0.06f, 0.072f), AmbientEquator = new Color(0.085f, 0.042f, 0.048f), AmbientGround = new Color(0.048f, 0.024f, 0.024f),
                TorchColor = new Color(1f, 0.45f, 0.3f), BrazierColor = new Color(0.95f, 0.3f, 0.2f),
                StoneTint = new Color(0.7f, 0.62f, 0.65f), FloorTint = new Color(0.68f, 0.58f, 0.6f),
                ShaftColor = new Color(1f, 0.5f, 0.38f),
                ArcherChance = 0.3f,
            },
            new DungeonTheme
            {
                // The Abyss (secret, below the Cathedral): black stone veined violet, thick dark, cold violet fire.
                Name = "The Abyss",
                Fog = new Color(0.012f, 0.006f, 0.022f), FogDensity = 0.043f,
                AmbientSky = new Color(0.075f, 0.045f, 0.125f), AmbientEquator = new Color(0.05f, 0.028f, 0.088f), AmbientGround = new Color(0.026f, 0.015f, 0.044f),
                TorchColor = new Color(0.7f, 0.45f, 1f), BrazierColor = new Color(0.55f, 0.3f, 1f),
                StoneTint = new Color(0.56f, 0.5f, 0.66f), FloorTint = new Color(0.52f, 0.47f, 0.6f),
                ShaftColor = new Color(0.6f, 0.4f, 1f),
                ArcherChance = 0.35f,
            },
        };

        /// <summary>The theme of a floor: two floors to an act (see DungeonDirector.Act).</summary>
        public static DungeonTheme ForDepth(int depth) => DungeonDirector.InPit ? Pit
            : DungeonDirector.IsWardenFloor(depth) ? ForAct(DungeonDirector.Act(depth)) : Deeper(DungeonDirector.Act(depth));

        /// <summary>The Cinder Pit (2026-11-14): black basalt veined red, a smoky ember murk, fire for light.</summary>
        public static readonly DungeonTheme Pit = new DungeonTheme
        {
            Name = "The Cinder Pit", ActIndex = 4,
            Fog = new Color(0.05f, 0.015f, 0.01f), FogDensity = 0.04f,
            AmbientSky = new Color(0.14f, 0.05f, 0.03f), AmbientEquator = new Color(0.1f, 0.035f, 0.02f), AmbientGround = new Color(0.07f, 0.025f, 0.015f),
            TorchColor = new Color(1f, 0.5f, 0.2f), BrazierColor = new Color(1f, 0.4f, 0.12f),
            StoneTint = new Color(0.48f, 0.36f, 0.33f), FloorTint = new Color(0.42f, 0.3f, 0.27f),
            ShaftColor = new Color(1f, 0.4f, 0.15f),
            ArcherChance = 0.25f,
        };

        /// <summary>Which act a (possibly variant) theme belongs to; -1 for the originals (look them up).</summary>
        public int ActIndex = -1;
        private static readonly DungeonTheme[] deeper = new DungeonTheme[5];

        /// <summary>
        /// An act's second floor is the same place, further down: thicker fog, less light from above,
        /// redder torches, darker stone. Same theme for everything else (gimmicks, dressing, enemies).
        /// </summary>
        private static DungeonTheme Deeper(int act)
        {
            act = Mathf.Clamp(act, 0, ByDepth.Length - 1);
            if (deeper[act] != null) return deeper[act];
            var t = (DungeonTheme)ByDepth[act].MemberwiseClone();
            t.ActIndex = act;
            t.FogDensity *= 1.15f;
            t.Fog *= 0.75f;
            t.AmbientSky *= 0.8f;
            t.AmbientEquator *= 0.8f;
            t.AmbientGround *= 0.85f;
            // Darker, not black: under the retro pass's ink crush the second floor of the deeper acts lost everything.
            t.AmbientSky = Max(t.AmbientSky, new Color(0.08f, 0.07f, 0.1f));
            t.AmbientEquator = Max(t.AmbientEquator, new Color(0.058f, 0.048f, 0.072f));
            t.AmbientGround = Max(t.AmbientGround, new Color(0.03f, 0.025f, 0.04f));
            t.TorchColor = Color.Lerp(t.TorchColor, new Color(1f, 0.35f, 0.2f), 0.2f);
            t.StoneTint *= 0.88f;
            t.FloorTint *= 0.9f;
            t.ShaftColor *= 0.75f;
            return deeper[act] = t;
        }
        public static DungeonTheme ForAct(int act) => ByDepth[Mathf.Clamp(act, 0, ByDepth.Length - 1)];

        /// <summary>Each act's two floors have a name of their own under the act's.</summary>
        private static readonly string[] FloorNames =
        {
            "The Upper Crypts", "The Bone Pits",
            "The Flooded Stair", "The Sunken Archive",
            "The Broken Court", "The Empty Throne",
            "The Nave", "The Choir Below",
            "The Last Stair", "Where the Hymn Ends",
        };
        public static string FloorName(int depth) => DungeonDirector.InPit ? "The Furnace Below"
            : DungeonDirector.Endless ? DungeonDirector.EndlessFloorName(FloorNames[Mathf.Max(0, depth) % FloorNames.Length], depth)
            : FloorNames[Mathf.Clamp(depth, 0, FloorNames.Length - 1)];
        private static Color Max(Color a, Color b) => new Color(Mathf.Max(a.r, b.r), Mathf.Max(a.g, b.g), Mathf.Max(a.b, b.b), a.a);
    }
}
