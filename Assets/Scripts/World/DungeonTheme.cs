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
                ArcherChance = 0.3f,
            },
            new DungeonTheme
            {
                // The king's halls: cold blue, violet shadows, gilded stone.
                Name = "The Hollow Throne",
                Fog = new Color(0.02f, 0.018f, 0.035f), FogDensity = 0.038f,
                AmbientSky = new Color(0.07f, 0.065f, 0.12f), AmbientEquator = new Color(0.05f, 0.045f, 0.08f), AmbientGround = new Color(0.03f, 0.025f, 0.04f),
                TorchColor = new Color(0.55f, 0.7f, 1f), BrazierColor = new Color(0.35f, 0.65f, 1f),
                StoneTint = new Color(0.78f, 0.76f, 0.92f), FloorTint = new Color(0.75f, 0.72f, 0.85f),
                ArcherChance = 0.35f,
            },
            new DungeonTheme
            {
                // The Black Cathedral: blood-red candlelight, black stone, a wine-dark murk.
                Name = "The Black Cathedral",
                Fog = new Color(0.03f, 0.012f, 0.016f), FogDensity = 0.036f,
                AmbientSky = new Color(0.1f, 0.05f, 0.06f), AmbientEquator = new Color(0.07f, 0.035f, 0.04f), AmbientGround = new Color(0.04f, 0.02f, 0.02f),
                TorchColor = new Color(1f, 0.45f, 0.3f), BrazierColor = new Color(0.95f, 0.3f, 0.2f),
                StoneTint = new Color(0.62f, 0.55f, 0.58f), FloorTint = new Color(0.6f, 0.5f, 0.52f),
                ArcherChance = 0.3f,
            },
        };

        public static DungeonTheme ForDepth(int depth) => ByDepth[Mathf.Clamp(depth, 0, ByDepth.Length - 1)];
    }
}
