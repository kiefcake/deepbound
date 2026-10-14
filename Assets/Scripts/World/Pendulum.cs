using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A swinging blade in a pendulum hall. Its motion is a pure function of the server
    /// clock, so every peer draws it in the same place without syncing anything, and the
    /// server (<see cref="DungeonDirector"/>) cuts whatever is where the blade is — players
    /// and the dead alike. Lure them in.
    /// </summary>
    public class Pendulum : MonoBehaviour
    {
        public const float Period = 3.2f, MaxAngle = 58f, HitRadius = 1.35f;

        private Vector3 pivot, axis;
        private float arm;
        private int index;
        private Transform swing;

        public static double Clock => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        public static float Angle(int index, double time) => MaxAngle * Mathf.Sin((float)(time * 2.0 * Mathf.PI / Period) + index * 1.7f);

        public static void Geometry(DungeonLayout.Room room, int index, out Vector3 pivot, out Vector3 axis, out float arm)
        {
            var spot = room.FeatureSpots[index];
            pivot = new Vector3(spot.x, room.Height - 0.8f, spot.z);
            // Spaced along the long axis; each blade sweeps across the room, perpendicular to the way through.
            axis = DungeonLayout.PendulumsAlongX(room) ? Vector3.right : Vector3.forward;
            arm = room.Height - 0.8f - 1.1f;
        }

        /// <summary>Where blade <paramref name="index"/>'s centre is at <paramref name="time"/>.</summary>
        public static Vector3 BladeCenter(DungeonLayout.Room room, int index, double time)
        {
            Geometry(room, index, out var pivot, out var axis, out float arm);
            return pivot + Quaternion.AngleAxis(Angle(index, time), axis) * Vector3.down * arm;
        }

        /// <summary>The blade's direction of travel right now (for knockback).</summary>
        public static Vector3 SwingDirection(DungeonLayout.Room room, int index, double time)
        {
            Geometry(room, index, out _, out var axis, out _);
            float a0 = Angle(index, time), a1 = Angle(index, time + 0.02);
            var across = Vector3.Cross(axis, Vector3.up).normalized;
            return across * Mathf.Sign(a1 - a0) * (axis == Vector3.right ? -1f : 1f);
        }

        public static void Build(Transform parent, DungeonLayout.Room room, int index)
        {
            Geometry(room, index, out var pivot, out var axis, out float arm);
            var go = new GameObject("Pendulum" + index);
            go.transform.SetParent(parent, false);
            go.transform.position = pivot;
            var p = go.AddComponent<Pendulum>();
            p.pivot = pivot;
            p.axis = axis;
            p.arm = arm;
            p.index = index;
            p.swing = MeshKit.Pivot(go.transform, "Swing", Vector3.zero);

            // The rod, a counterweight collar, and a great crescent blade, edge leading.
            var across = Vector3.Cross(axis, Vector3.up).normalized;
            MeshKit.Part(p.swing, "Hub", MeshKit.Cylinder(0.18f, 0.18f, 0.5f, 8), Mat.Steel, -axis * 0.25f, Quaternion.LookRotation(Vector3.up, axis).eulerAngles);
            MeshKit.Part(p.swing, "Rod", MeshKit.Box(new Vector3(0.1f, arm, 0.1f)), Mat.Steel, Vector3.down * arm / 2f);
            MeshKit.Part(p.swing, "Collar", MeshKit.Box(new Vector3(0.3f, 0.3f, 0.3f)), Mat.Steel, Vector3.down * (arm - 0.55f));
            for (int i = -3; i <= 3; i++)
            {
                // A crescent from short boxes, curving up at the tips.
                float t = i / 3f;
                var at = Vector3.down * (arm + 0.15f - t * t * 0.45f) + across * t * 1.1f;
                var seg = MeshKit.Part(p.swing, "Blade" + i, MeshKit.Box(new Vector3(0.05f, 0.5f - Mathf.Abs(t) * 0.25f, 0.4f)), Mat.Steel, at);
                seg.transform.localRotation = Quaternion.LookRotation(across, Vector3.up) * Quaternion.Euler(-t * 35f, 0f, 0f);
            }
            var edge = MeshKit.Part(p.swing, "Edge", MeshKit.Box(new Vector3(0.03f, 0.04f, 2.1f)), Mat.Glow, Vector3.down * (arm + 0.42f));
            edge.transform.localRotation = Quaternion.LookRotation(across, Vector3.up);
            ArtLibrary.SetGlow(edge.GetComponent<Renderer>(), new Color(1f, 0.35f, 0.2f) * 0.6f);
        }

        private float lastWhoosh;

        private void Update()
        {
            double t = Clock;
            float a = Angle(index, t);
            swing.localRotation = Quaternion.AngleAxis(a, axis);
            // A whoosh as it passes the bottom of its swing, when you are near enough to hear.
            if (Mathf.Abs(a) < 8f && Time.time - lastWhoosh > Period * 0.4f)
            {
                lastWhoosh = Time.time;
                var cam = Camera.main;
                if (cam != null && Vector3.Distance(cam.transform.position, pivot) < 22f)
                    Sfx.Play(Sound.Swing, pivot + Vector3.down * arm, 0.5f, 0.55f);
            }
        }
    }
}
