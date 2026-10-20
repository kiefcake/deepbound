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
        public const float Cycle = 7f, Sweep = 3.6f, WarnSeconds = 0.8f, UpSeconds = 0.45f, RowSpacing = 2.6f, RowHalf = 0.75f;

        private DungeonLayout.Room room;
        private Transform[] spikes;
        private Renderer[] plates;
        private bool[] wasUp;

        public static bool AlongX(DungeonLayout.Room room) => room.Size.x >= room.Size.y;

        public static int Rows(DungeonLayout.Room room)
        {
            float len = AlongX(room) ? room.Size.x : room.Size.y;
            return Mathf.Max(3, Mathf.FloorToInt((len - 6f) / RowSpacing));
        }

        /// <summary>The centre of row <paramref name="i"/> (on the floor).</summary>
        public static Vector3 RowCenter(DungeonLayout.Room room, int i)
        {
            int n = Rows(room);
            float t = (i - (n - 1) / 2f) * RowSpacing;
            return room.Center + (AlongX(room) ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t));
        }

        /// <summary>Seconds since row i's spikes fired this cycle (negative: still to come).</summary>
        public static float SinceFire(DungeonLayout.Room room, int i, double time)
        {
            int n = Rows(room);
            double tt = time + (room.DecorSeed & 511) * 0.21;
            int cycle = (int)(tt / Cycle);
            float ph = (float)(tt - cycle * (double)Cycle);
            int order = cycle % 2 == 0 ? i : n - 1 - i;
            float fireAt = 0.6f + Sweep * order / Mathf.Max(1, n - 1);
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
            g.wasUp = new bool[n];
            bool ax = AlongX(room);
            float width = (ax ? room.Size.y : room.Size.x) - 1.2f;
            var acrossDir = ax ? Vector3.forward : Vector3.right;
            for (int i = 0; i < n; i++)
            {
                var c = RowCenter(room, i);
                var row = MeshKit.Pivot(go.transform, "Row" + i, c);
                var plate = MeshKit.Part(row, "Plate", MeshKit.Box(ax ? new Vector3(RowHalf * 2f, 0.04f, width) : new Vector3(width, 0.04f, RowHalf * 2f)), Mat.Glow, Vector3.up * 0.02f);
                g.plates[i] = plate.GetComponent<Renderer>();
                g.plates[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                MeshKit.Part(row, "Grate", MeshKit.Box(ax ? new Vector3(RowHalf * 2f - 0.1f, 0.05f, width - 0.1f) : new Vector3(width - 0.1f, 0.05f, RowHalf * 2f - 0.1f)), Mat.Steel, Vector3.up * 0.03f);
                var bed = MeshKit.Pivot(row, "Spikes", Vector3.down * 0.5f);
                for (float a = -width / 2f + 0.35f; a <= width / 2f - 0.3f; a += 0.7f)
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var at = acrossDir * a + (ax ? Vector3.right : Vector3.forward) * (k * RowHalf * 0.45f);
                        MeshKit.Part(bed, "Spike", MeshKit.Lathe("gauntletSpike", new[] { new Vector2(0.07f, 0f), new Vector2(0f, 0.55f) }, 4), Mat.Steel, at);
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
                ArtLibrary.SetGlow(plates[i], new Color(1f, 0.15f, 0.05f) * (0.05f + 1.6f * warn * beat + (up ? 1.2f : 0f)));
                if (up && !wasUp[i]) Sfx.Play(Sound.Slam, RowCenter(room, i), 0.35f, 1.7f);
                wasUp[i] = up;
            }
        }
    }
}
