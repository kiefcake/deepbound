using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The spike floor of a Gauntlet (<see cref="RoomFeature.Gauntlet"/>): rows of spike plates
    /// across the room's long axis. Every <see cref="Cycle"/> seconds a wave runs down the rows
    /// (the way it runs flips each cycle): each row glows for <see cref="WarnSeconds"/>, then its
    /// spikes punch up for <see cref="UpSeconds"/>, cutting players and the dead alike. Like the
    /// pendulums, all of it is a function of the server clock, so nothing is synced. Read the
    /// wave, step between the rows, and let the dead walk into it.
    /// </summary>
    public class SpikeGauntlet : MonoBehaviour
    {
        public const float Cycle = 7f, Sweep = 3.6f, WarnSeconds = 0.8f, UpSeconds = 0.45f, RowHalf = 0.75f, GapHalf = 0.95f;

        // ---- No two alike (2026-11-12, the user: "trap rooms are very easy to guess because they are usually the exact
        // same layout"). From the room's DecorSeed (the same on every peer, nothing synced): how far apart the rows are,
        // how the wave runs (a sweep, odd/even rows in turn, from the walls in to the middle and out, or a fresh shuffle
        // every cycle), and in some rooms a gap in each row that zig-zags across, a lane to weave down.
        public enum Pattern { Sweep, Alternate, Converge, Shuffle }
        public static Pattern PatternOf(DungeonLayout.Room room) => (Pattern)((room.DecorSeed >> 3) & 3);
        public static float RowSpacing(DungeonLayout.Room room) => 2.2f + ((room.DecorSeed >> 6) % 5) * 0.25f;
        public static bool Weaves(DungeonLayout.Room room) => ((room.DecorSeed >> 9) & 1) == 1;

        /// <summary>Row i's safe gap, as an offset across the room from its centre line (weaving rooms only).</summary>
        public static float GapAt(DungeonLayout.Room room, int i)
        {
            float half = (AlongX(room) ? room.Size.y : room.Size.x) / 2f - 0.6f;
            float side = ((room.DecorSeed >> 11) & 1) == 0 ? 1f : -1f;
            return side * (i % 2 == 0 ? 1f : -1f) * (half * 0.5f);
        }

        private DungeonLayout.Room room;
        private Transform[] spikes;
        private Renderer[] plates, extraPlates;
        private bool[] wasUp;

        public static bool AlongX(DungeonLayout.Room room) => room.Size.x >= room.Size.y;

        public static int Rows(DungeonLayout.Room room)
        {
            float len = AlongX(room) ? room.Size.x : room.Size.y;
            return Mathf.Max(3, Mathf.FloorToInt((len - 6f) / RowSpacing(room)));
        }

        /// <summary>The centre of row <paramref name="i"/> (on the floor).</summary>
        public static Vector3 RowCenter(DungeonLayout.Room room, int i)
        {
            int n = Rows(room);
            float t = (i - (n - 1) / 2f) * RowSpacing(room);
            return room.Center + (AlongX(room) ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
        }

        /// <summary>Seconds since row i's spikes fired this cycle (negative: still to come).</summary>
        public static float SinceFire(DungeonLayout.Room room, int i, double time)
        {
            int n = Rows(room);
            double tt = time + (room.DecorSeed & 511) * 0.21;
            int cycle = (int)(tt / Cycle);
            float ph = (float)(tt - cycle * (double)Cycle);
            float k;
            switch (PatternOf(room))
            {
                case Pattern.Alternate: k = (i + cycle) % 2 == 0 ? 0f : 1f; break;
                case Pattern.Converge:
                {
                    float mid = (n - 1) / 2f, d = Mathf.Abs(i - mid) / Mathf.Max(0.5f, mid);
                    k = cycle % 2 == 0 ? 1f - d : d;
                    break;
                }
                case Pattern.Shuffle:
                {
                    uint h = (uint)(cycle * 73856093) ^ (uint)(i * 19349663) ^ (uint)room.DecorSeed;
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    k = (h & 1023) / 1023f;
                    break;
                }
                default: k = (cycle % 2 == 0 ? i : n - 1 - i) / (float)Mathf.Max(1, n - 1); break;
            }
            float fireAt = 0.6f + Sweep * k;
            return ph - fireAt;
        }

        public static bool Up(DungeonLayout.Room room, int i, double time)
        {
            float s = SinceFire(room, i, time);
            return s >= 0f && s < UpSeconds;
        }

        /// <summary>Is <paramref name="p"/> on row i's strip of plates?</summary>
        public static bool OnRow(DungeonLayout.Room room, int i, Vector3 p)
        {
            var c = RowCenter(room, i);
            float along = AlongX(room) ? p.x - c.x : p.z - c.z;
            float across = AlongX(room) ? p.z - c.z : p.x - c.x;
            float halfAcross = (AlongX(room) ? room.Size.y : room.Size.x) / 2f - 0.6f;
            if (Weaves(room) && Mathf.Abs(across - GapAt(room, i)) <= GapHalf) return false;
            return Mathf.Abs(along) <= RowHalf && Mathf.Abs(across) <= halfAcross && p.y < 1.2f;
        }

        public static void Build(Transform parent, DungeonLayout.Room room)
        {
            var go = new GameObject("SpikeGauntlet");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<SpikeGauntlet>();
            g.room = room;
            int n = Rows(room);
            g.spikes = new Transform[n];
            g.plates = new Renderer[n];
            g.extraPlates = new Renderer[n];
            g.wasUp = new bool[n];
            bool ax = AlongX(room);
            float width = (ax ? room.Size.y : room.Size.x) - 1.2f;
            var acrossDir = ax ? Vector3.forward : Vector3.right;
            for (int i = 0; i < n; i++)
            {
                var c = RowCenter(room, i);
                var row = MeshKit.Pivot(go.transform, "Row" + i, c);
                // The row's plate, in one piece, or two either side of its gap in a weaving room.
                float gap = Weaves(room) ? GapAt(room, i) : float.NaN;
                var segs = new System.Collections.Generic.List<(float from, float to)>();
                if (float.IsNaN(gap)) segs.Add((-width / 2f, width / 2f));
                else { segs.Add((-width / 2f, gap - GapHalf)); segs.Add((gap + GapHalf, width / 2f)); }
                var glowMesh = new System.Collections.Generic.List<Renderer>();
                foreach (var (f0, f1) in segs)
                {
                    float sw = f1 - f0, mid = (f0 + f1) / 2f;
                    if (sw < 0.3f) continue;
                    var plate = MeshKit.Part(row, "Plate", MeshKit.Box(ax ? new Vector3(RowHalf * 2f, 0.04f, sw) : new Vector3(sw, 0.04f, RowHalf * 2f)), Mat.Glow, Vector3.up * 0.02f + acrossDir * mid);
                    var pr = plate.GetComponent<Renderer>();
                    pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    glowMesh.Add(pr);
                    MeshKit.Part(row, "Grate", MeshKit.Box(ax ? new Vector3(RowHalf * 2f - 0.1f, 0.05f, sw - 0.1f) : new Vector3(sw - 0.1f, 0.05f, RowHalf * 2f - 0.1f)), Mat.Steel, Vector3.up * 0.03f + acrossDir * mid);
                }
                g.plates[i] = glowMesh.Count > 0 ? glowMesh[0] : null;
                if (glowMesh.Count > 1) g.extraPlates[i] = glowMesh[1];
                var bed = MeshKit.Pivot(row, "Spikes", Vector3.down * 0.5f);
                for (float a = -width / 2f + 0.35f; a <= width / 2f - 0.3f; a += 0.7f)
                {
                    if (!float.IsNaN(gap) && Mathf.Abs(a - gap) < GapHalf) continue;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var at = acrossDir * a + (ax ? Vector3.right : Vector3.forward) * (k * RowHalf * 0.45f);
                        MeshKit.Part(bed, "Spike", MeshKit.Lathe("gauntletSpike", new[] { new Vector2(0.07f, 0f), new Vector2(0f, 0.55f) }, 4), Mat.Steel, at);
                    }
                }
                MeshKit.MergeParts(bed);
                g.spikes[i] = bed;
            }
        }

        private void Update()
        {
            if (room == null || spikes == null) return;
            double now = Pendulum.Clock;
            for (int i = 0; i < spikes.Length; i++)
            {
                float s = SinceFire(room, i, now);
                bool up = s >= 0f && s < UpSeconds;
                float rise = s >= 0f && s < UpSeconds + 0.25f ? (s < 0.06f ? s / 0.06f : s < UpSeconds ? 1f : 1f - (s - UpSeconds) / 0.25f) : 0f;
                spikes[i].localPosition = Vector3.down * Mathf.Lerp(0.55f, 0f, rise);
                float warn = s < 0f && s > -WarnSeconds ? 1f - (-s / WarnSeconds) : 0f;
                float beat = warn > 0f ? 0.6f + 0.4f * Mathf.Sin(Time.time * 30f) : 0f;
                var gc = new Color(1f, 0.15f, 0.05f) * (0.05f + 1.6f * warn * beat + (up ? 1.2f : 0f));
                if (plates[i] != null) ArtLibrary.SetGlow(plates[i], gc);
                if (extraPlates[i] != null) ArtLibrary.SetGlow(extraPlates[i], gc);
                if (up && !wasUp[i]) Sfx.Play(Sound.Slam, RowCenter(room, i), 0.35f, 1.7f);
                wasUp[i] = up;
            }
        }
    }
}
