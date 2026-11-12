using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The camp's working things, each one USED by looking at it and pressing F (2026-11-12, the user: "tie existing
    /// infrastructure to objects, for example the blacksmith anvil or forge for forging and upgrading"). Until then every
    /// camp trade went through a PERSON (talk to Hobb, "Work the forge.") and the anvil only opened by standing near it,
    /// hidden behind Hobb's counter. Now:
    /// <list type="bullet">
    /// <item>the ANVIL tempers the class weapon, sets its path and forges its edges;</item>
    /// <item>the GRINDSTONE (a treadle wheel at the front of Hobb's stall) hones a +10 blade and refines worn gear;</item>
    /// <item>the FORGE's hearth melts pack gear down for embers and attunes worn gear to the next floor;</item>
    /// <item>Silas's table opens his trade, Ysolde's rune table her runes, Brann's chalk drill board his drills (the keeper
    /// must be there: a stall is still a person's, <see cref="Progression.Shop.InReach"/>).</item>
    /// </list>
    /// Positions are fixed from <see cref="Lobby"/>'s constants, so the server knows them without building the camp
    /// (the smithy's reach widens <see cref="Anvil.Near"/>). Each station is an axis-aligned box: the crosshair ray must
    /// actually enter it, so looking past the anvil at Hobb's face still talks to Hobb.
    /// </summary>
    public static class CampStations
    {
        public enum Job : byte { Anvil, Grindstone, Hearth, Merchant, Runes, Drills }

        public sealed class Station
        {
            public readonly Job Job;
            /// <summary>The box's centre and half size.</summary>
            public readonly Vector3 Center, Half;
            /// <summary>A good place to stand to use it (tests, shots).</summary>
            public readonly Vector3 Stand;
            public readonly string Noun;

            public Station(Job job, Vector3 center, Vector3 half, Vector3 stand, string noun)
            {
                Job = job; Center = center; Half = half; Stand = stand; Noun = noun;
            }

            /// <summary>Where the ray from <paramref name="eye"/> along <paramref name="dir"/> enters the box, or -1.</summary>
            public float Hit(Vector3 eye, Vector3 dir)
            {
                float tMin = 0f, tMax = float.MaxValue;
                for (int a = 0; a < 3; a++)
                {
                    float o = eye[a], d = dir[a], lo = Center[a] - Half[a], hi = Center[a] + Half[a];
                    if (Mathf.Abs(d) < 1e-6f) { if (o < lo || o > hi) return -1f; continue; }
                    float t1 = (lo - o) / d, t2 = (hi - o) / d;
                    if (t1 > t2) (t1, t2) = (t2, t1);
                    tMin = Mathf.Max(tMin, t1);
                    tMax = Mathf.Min(tMax, t2);
                    if (tMin > tMax) return -1f;
                }
                return tMin;
            }
        }

        /// <summary>How far from the eye a station can be used (the box's near face).</summary>
        public const float Reach = 3f;

        /// <summary>The treadle grindstone, at the north front corner of Hobb's stall (the old wheel on his back bench sat
        /// behind the counter where nobody could see it).</summary>
        public static Vector3 GrindstoneSpot => Lobby.AnvilSpot + new Vector3(0.45f, 0f, 1.4f);
        /// <summary>The forge's stone hearth (the coals glow on top at 1.43 m).</summary>
        public static Vector3 HearthSpot => Lobby.AnvilSpot + Lobby.S(-2.6f, 0f, -1.6f);
        /// <summary>Silas's trestle table, 0.9 m in front of him.</summary>
        private static Vector3 SilasTable => Lobby.MerchantSpot + Vector3.right * 0.9f;
        /// <summary>Ysolde's stone rune table at her side.</summary>
        private static Vector3 RuneTable => Lobby.RunewrightSpot + new Vector3(0.9f, 0f, 0.25f);
        /// <summary>Brann's chalk drill board by his lamp post.</summary>
        private static Vector3 DrillBoard => Lobby.YardSpot + new Vector3(-1.15f, 0f, 1.65f);

        private static Station[] all;
        public static Station[] All => all ??= new[]
        {
            new Station(Job.Anvil, Lobby.AnvilSpot + new Vector3(0f, 0.5f, 0f), new Vector3(0.35f, 0.45f, 0.55f), Lobby.AnvilSpot + Vector3.right * 1.5f, "the anvil"),
            new Station(Job.Grindstone, GrindstoneSpot + new Vector3(0f, 0.55f, 0f), new Vector3(0.3f, 0.55f, 0.42f), GrindstoneSpot + Vector3.right * 1.4f, "the grindstone"),
            new Station(Job.Hearth, HearthSpot + new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 0.9f, 0.7f), HearthSpot + new Vector3(1.0f, 0f, -1.7f), "the forge"),
            new Station(Job.Merchant, SilasTable + new Vector3(0f, 0.8f, 0f), new Vector3(0.4f, 0.16f, 0.92f), SilasTable + Vector3.right * 1.4f, "Silas's table"),
            new Station(Job.Runes, RuneTable + new Vector3(0f, 0.78f, 0f), new Vector3(0.42f, 0.18f, 0.68f), RuneTable + Vector3.right * 1.3f, "Ysolde's rune table"),
            new Station(Job.Drills, DrillBoard + new Vector3(0f, 1.25f, 0f), new Vector3(0.12f, 0.42f, 0.64f), DrillBoard + Vector3.right * 1.5f, "Brann's drill board"),
        };

        public static Station Get(Job j) { foreach (var s in All) if (s.Job == j) return s; return null; }

        /// <summary>The station the ray enters first, within <see cref="Reach"/> (<paramref name="dist"/>: how far). Camp only.</summary>
        public static Station Aimed(Vector3 eye, Vector3 dir, out float dist)
        {
            dist = float.MaxValue;
            if (!DungeonDirector.InLobby) return null;
            Station best = null;
            foreach (var s in All)
            {
                float t = s.Hit(eye, dir);
                if (t < 0f || t > Reach || t >= dist) continue;
                best = s; dist = t;
            }
            return best;
        }

        /// <summary>The station under the local player's crosshair this frame (set by the HUD: null when a person, a
        /// thing to pick up or an open panel comes first).</summary>
        public static Station LocalAimed;

        /// <summary>The vendor whose panel a station opens, or null (the smithy's three open the forge).</summary>
        public static Progression.Shop.Vendor? VendorOf(Job j) => j == Job.Merchant ? Progression.Shop.Vendor.Merchant
            : j == Job.Runes ? Progression.Shop.Vendor.Runewright : j == Job.Drills ? Progression.Shop.Vendor.Yard : (Progression.Shop.Vendor?)null;

        public static bool IsSmithy(Job j) => j == Job.Anvil || j == Job.Grindstone || j == Job.Hearth;

        /// <summary>Within working reach of the grindstone or the forge (the server's check; the anvil has its own).</summary>
        public static bool AtSmithy(Vector3 at)
        {
            foreach (var j in new[] { Job.Grindstone, Job.Hearth })
            {
                var s = Get(j);
                var d = at - s.Center;
                if (Mathf.Abs(d.y) > 2.5f) continue;
                d.y = 0f;
                if (Mathf.Abs(d.x) <= s.Half.x + 1.9f && Mathf.Abs(d.z) <= s.Half.z + 1.9f) return true;
            }
            return false;
        }

        /// <summary>The smithy station nearest <paramref name="at"/> (F with nothing aimed opens its tab).</summary>
        public static Job NearestSmithy(Vector3 at)
        {
            Job best = Job.Anvil;
            float bd = float.MaxValue;
            foreach (var s in All)
            {
                if (!IsSmithy(s.Job)) continue;
                var d = s.Center - at; d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = s.Job; }
            }
            return best;
        }
    }

    /// <summary>
    /// The smithy answering the work done at it, on every peer: the grindstone's wheel spins and throws sparks, the
    /// forge's coals flare and its light leaps (<see cref="Anvil.WorkFxRpc"/>). Built with the camp by <see cref="Lobby"/>.
    /// </summary>
    public class SmithyFx : MonoBehaviour
    {
        public static SmithyFx Instance;
        public Transform Wheel;
        public Light ForgeLight;
        public Renderer Coals;
        private float spinUntil, spinSpeed, flare;
        private float baseIntensity = -1f;
        private Color coalGlow;

        public void Init(Transform wheel, Light forgeLight, Renderer coals, Color glow)
        {
            Instance = this;
            Wheel = wheel; ForgeLight = forgeLight; Coals = coals; coalGlow = glow;
            baseIntensity = forgeLight != null ? forgeLight.intensity : -1f;
        }

        /// <summary>The wheel runs for a couple of seconds (it coasts down at the end).</summary>
        public void Spin() { spinUntil = Time.time + 1.8f; spinSpeed = 720f; }

        /// <summary>The coals flare: <paramref name="k"/> 1 = a full roar (scrap melting), 0.5 = a lift (tempering).</summary>
        public void Flare(float k) { flare = Mathf.Max(flare, k); }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            if (Wheel != null && (Time.time < spinUntil || spinSpeed > 1f))
            {
                if (Time.time >= spinUntil) spinSpeed = Mathf.MoveTowards(spinSpeed, 0f, 600f * Time.deltaTime);
                Wheel.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
            }
            if (flare > 0f || (ForgeLight != null && baseIntensity >= 0f && ForgeLight.intensity != baseIntensity))
            {
                flare = Mathf.MoveTowards(flare, 0f, Time.deltaTime * 0.6f);
                float flick = 1f + 0.15f * Mathf.Sin(Time.time * 23f) * flare;
                if (ForgeLight != null && baseIntensity >= 0f) ForgeLight.intensity = baseIntensity * (1f + 1.6f * flare) * flick;
                if (Coals != null) ArtLibrary.SetGlow(Coals, coalGlow * (1f + 1.4f * flare));
            }
        }
    }
}
