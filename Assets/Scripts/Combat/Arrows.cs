using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// Arrows: simulated on the server, drawn on every peer.
    ///
    /// <para>
    /// An arrow is not a networked object. The server steps a list of (position,
    /// velocity) records and resolves hits; each client is told "an arrow left here at
    /// this velocity" once, and flies an identical visual copy locally. Flight is a
    /// straight line at fixed speed, so the copies match closely without any per-frame
    /// sync — dozens of arrows cost nothing on the network.
    /// </para>
    ///
    /// Arrows are slow enough to sidestep, and they count as coming from where they are,
    /// so a Knight's block stops them from the front.
    /// </summary>
    public class Arrows : MonoBehaviour
    {
        public const float Speed = 21f;
        private const float MaxRange = 30f;
        private const float HitRadius = 0.55f;

        private struct Arrow
        {
            public Vector3 Position, Velocity;
            public float Travelled, Damage;
            public ulong Shooter;
            public Team Targets;
            public Transform Visual;
            public bool ServerSide;
        }

        private static Arrows instance;
        private readonly List<Arrow> live = new List<Arrow>();

        private static Arrows Instance
        {
            get
            {
                if (instance == null) instance = new GameObject("Arrows").AddComponent<Arrows>();
                return instance;
            }
        }

        /// <summary>Server: fire an arrow that deals damage. Call the shooter's visual RPC alongside.</summary>
        public static void ServerFire(Vector3 origin, Vector3 direction, float damage, Team targets, ulong shooter)
        {
            Instance.live.Add(new Arrow
            {
                Position = origin, Velocity = direction.normalized * Speed, Damage = damage,
                Targets = targets, Shooter = shooter, ServerSide = true,
            });
        }

        /// <summary>Any peer: fly a harmless visual copy.</summary>
        public static void SpawnVisual(Vector3 origin, Vector3 direction)
        {
            Instance.live.Add(new Arrow
            {
                Position = origin, Velocity = direction.normalized * Speed, Visual = BuildVisual(origin, direction),
            });
        }

        /// <summary>Drop every arrow in flight, e.g. when the arena changes.</summary>
        public static void Clear()
        {
            if (instance == null) return;
            foreach (var a in instance.live) if (a.Visual != null) Destroy(a.Visual.gameObject);
            instance.live.Clear();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var a = live[i];
                var step = a.Velocity * dt;
                bool done = false;

                if (Physics.Raycast(a.Position, step.normalized, out var wall, step.magnitude, ~0, QueryTriggerInteraction.Ignore) &&
                    wall.collider.GetComponentInParent<Health>() == null)
                {
                    // Stick in the wall for a moment, then vanish.
                    a.Position = wall.point;
                    done = true;
                    if (a.Visual != null)
                    {
                        a.Visual.position = wall.point - step.normalized * 0.3f;
                        Destroy(a.Visual.gameObject, 2f);
                        a.Visual = null;
                    }
                }
                else
                {
                    if (a.ServerSide && TryHit(ref a, a.Position, a.Position + step)) done = true;
                    a.Position += step;
                    a.Travelled += step.magnitude;
                    if (a.Visual != null) a.Visual.position = a.Position;
                    if (a.Travelled > MaxRange) done = true;
                }

                if (done)
                {
                    if (a.Visual != null) Destroy(a.Visual.gameObject);
                    live.RemoveAt(i);
                }
                else
                {
                    live[i] = a;
                }
            }
        }

        private static bool TryHit(ref Arrow a, Vector3 from, Vector3 to)
        {
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != a.Targets || !h.IsAlive) continue;
                if (DistanceToSegment(h.Center, from, to) > HitRadius) continue;

                // "From" for blocking purposes is a point back along the flight path.
                h.ServerDamage(a.Damage, h.Center - a.Velocity.normalized * 3f, a.Shooter);
                Sfx.Play(Sound.Hit, h.Center, 0.6f, 1.3f);
                return true;
            }
            return false;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        private static Transform BuildVisual(Vector3 origin, Vector3 direction)
        {
            var root = new GameObject("Arrow").transform;
            root.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            MeshKit.Part(root, "Shaft", MeshKit.Box(new Vector3(0.02f, 0.02f, 0.7f)), Mat.Wood, new Vector3(0f, 0f, -0.35f));
            MeshKit.Part(root, "Head", MeshKit.Lathe("arrowHead", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.08f) }, 4),
                Mat.Steel, Vector3.zero, new Vector3(90f, 0f, 0f));
            var fletch = MeshKit.Part(root, "Fletching", MeshKit.Box(new Vector3(0.08f, 0.005f, 0.12f)), Mat.Cloth, new Vector3(0f, 0f, -0.65f));
            ArtLibrary.Tint(fletch.GetComponent<Renderer>(), new Color(0.6f, 0.1f, 0.08f));
            // A faint ember glow so arrows read in the dark — the player has to see them to dodge.
            var glow = MeshKit.Part(root, "Glint", MeshKit.Ball(0.03f, 6, 4), Mat.Glow, new Vector3(0f, 0f, 0.02f));
            ArtLibrary.SetGlow(glow.GetComponent<Renderer>(), new Color(1f, 0.4f, 0.15f) * 2.5f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }
    }
}
