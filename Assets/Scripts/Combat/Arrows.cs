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
    /// <summary>What a projectile looks like. APPEND ONLY: sent in RPCs.</summary>
    public enum ProjectileKind : byte { Arrow = 0, Firebolt = 1, Frostbolt = 2, ShadowBolt = 3, Smite = 4, Knife = 5, Spit = 6 }

    public class Arrows : MonoBehaviour
    {
        public const float Speed = 21f;
        /// <summary>The dead's projectiles, and anything not given a range.</summary>
        public const float MaxRange = 30f;
        public static int LiveCount => instance != null ? instance.live.Count : 0;

        /// <summary>Server projectiles still in flight from one shooter (tests: other people's arrows do not count).</summary>
        public static int LiveFrom(ulong shooter)
        {
            int n = 0;
            if (instance != null) foreach (var a in instance.live) if (a.ServerSide && a.Shooter == shooter) n++;
            return n;
        }
        /// <summary>How fat a projectile is for hit tests, on top of the target's real body.</summary>
        private const float ProjectileRadius = 0.12f;

        /// <summary>Server: tell everyone a shot found a head or a weak point (the shooter hears it).</summary>
        public static void ReportZone(ulong shooter, Vector3 at, HitZone zone)
        {
            var net = Unity.Netcode.NetworkManager.Singleton;
            if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(shooter, out var obj)) return;
            if (obj.TryGetComponent<Progression.PlayerProgress>(out var p)) p.ZoneHitFxRpc(at, (byte)zone);
        }

        private struct Arrow
        {
            public Vector3 Position, Velocity;
            public float Travelled, Damage;
            public ulong Shooter;
            public Team Targets;
            public Transform Visual;
            /// <summary>How far it flies before it is spent (fizzles, or falls).</summary>
            public float Range;
            /// <summary>Visual copies of spell bolts: the colour of the small burst where it ends (alpha 0 = none).</summary>
            public Color Burst;
            public bool ServerSide;
            /// <summary>Server: replaces the default damage on hit (for chill, shatter and the like).</summary>
            public System.Action<Health> OnHit;
            /// <summary>Passes through what it hits, hitting each target at most once.</summary>
            public bool Pierce;
            public HashSet<Health> AlreadyHit;
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
        public static void ServerFire(Vector3 origin, Vector3 direction, float damage, Team targets, ulong shooter,
            float speed = Speed, System.Action<Health> onHit = null, bool pierce = false, float range = MaxRange)
        {
            Instance.live.Add(new Arrow
            {
                Range = range,
                Position = origin, Velocity = direction.normalized * speed, Damage = damage,
                Targets = targets, Shooter = shooter, ServerSide = true, OnHit = onHit,
                Pierce = pierce, AlreadyHit = pierce ? new HashSet<Health>() : null,
            });
        }

        /// <summary>Any peer: fly a harmless visual copy.</summary>
        public static void SpawnVisual(Vector3 origin, Vector3 direction, ProjectileKind kind = ProjectileKind.Arrow, float speed = Speed, float range = MaxRange)
        {
            Instance.live.Add(new Arrow
            {
                Range = range,
                Position = origin, Velocity = direction.normalized * speed,
                Visual = kind == ProjectileKind.Arrow ? BuildVisual(origin, direction)
                    : kind == ProjectileKind.Knife ? BuildKnife(origin, direction)
                    : BuildBolt(origin, direction, kind),
                Burst = kind == ProjectileKind.Arrow || kind == ProjectileKind.Knife ? Color.clear : BoltColor(kind),
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
                    // Stick in the wall for a moment, then vanish (bolts burst instead).
                    a.Position = wall.point;
                    done = true;
                    if (a.Visual != null && a.Burst.a > 0f) { Art.BoltFx.Burst(wall.point, a.Burst); Destroy(a.Visual.gameObject); a.Visual = null; }
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
                    if (a.Travelled > (a.Range > 0f ? a.Range : MaxRange))
                    {
                        done = true;
                        // Spent: an arrow or knife falls with a puff of dust; a bolt fizzles out (its burst, below).
                        if (a.Visual != null && a.Burst.a <= 0f) Art.Vfx.Embers(a.Position, 3, new Color(0.6f, 0.55f, 0.48f), 1.2f, 0.03f, 0.4f);
                    }
                }

                if (done)
                {
                    if (a.Burst.a > 0f && a.Visual != null) Art.BoltFx.Burst(a.Position, a.Burst);
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
                if (h == null || !CombatQuery.Matches(h, a.Targets) || !h.IsAlive) continue;
                if (!h.SegmentTouchesBody(from, to, ProjectileRadius)) continue;
                if (a.Pierce && !a.AlreadyHit.Add(h)) continue;

                if (a.OnHit != null)
                {
                    var spellZone = HitZones.Along(h, from - a.Velocity.normalized * 0.5f, to + a.Velocity.normalized * 0.5f, out var spellPoint);
                    Health.SetZoneBoost(h, HitZones.Multiplier(h, spellZone, true));
                    try { a.OnHit(h); }
                    finally { Health.SetZoneBoost(null, 1f); }
                    if (spellZone != HitZone.Body) ReportZone(a.Shooter, spellPoint, spellZone);
                }
                else
                {
                    // Where along its flight it struck: a head, a glowing weak point, or the body.
                    var zone = HitZones.Along(h, from - a.Velocity.normalized * 0.5f, to + a.Velocity.normalized * 0.5f, out var zonePoint);
                    if (zone == HitZone.Weak && h.TryGetComponent<Enemies.HoarderBrain>(out var hoarder)) hoarder.ServerSackHit();
                    float mult = HitZones.Multiplier(h, zone, true);
                    // "From" for blocking purposes is a point back along the flight path.
                    h.ServerDamage(a.Damage * mult, h.Center - a.Velocity.normalized * 3f, a.Shooter);
                    if (zone != HitZone.Body) ReportZone(a.Shooter, zonePoint, zone);
                    Sfx.Play(Sound.Hit, h.Center, 0.6f, 1.3f);
                }
                // One hit per step either way: damage can despawn things in Health.All.
                return !a.Pierce;
            }
            return false;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>A spell bolt: a glowing core, a lit halo and a trailing tail.</summary>
        private static Transform BuildBolt(Vector3 origin, Vector3 direction, ProjectileKind kind)
        {
            bool fire = kind == ProjectileKind.Firebolt || kind == ProjectileKind.ShadowBolt || kind == ProjectileKind.Smite || kind == ProjectileKind.Spit;
            var color = BoltColor(kind);

            var root = new GameObject(kind.ToString()).transform;
            root.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            var core = MeshKit.Part(root, "Core", fire ? MeshKit.Ball(0.12f, 8, 6) : MeshKit.Lathe("frostShard", new[]
            {
                new Vector2(0f, -0.25f), new Vector2(0.08f, 0f), new Vector2(0f, 0.12f),
            }, 5), Mat.Glow, Vector3.zero, fire ? Vector3.zero : new Vector3(90f, 0f, 0f));
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), color * 1.9f);
            var tail = MeshKit.Part(root, "Tail", MeshKit.Lathe("boltTail", new[] { new Vector2(0f, 0f), new Vector2(0.07f, 0.1f), new Vector2(0f, 0.6f) }, 6, false),
                Mat.Glow, new Vector3(0f, 0f, -0.05f), new Vector3(-90f, 0f, 0f));
            ArtLibrary.SetGlow(tail.GetComponent<Renderer>(), color * 1.5f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(root, false);
            light.type = LightType.Point;
            light.color = color;
            light.range = 5f;
            light.intensity = 3f;
            var fx = root.gameObject.AddComponent<Art.BoltFx>();
            fx.Color = color;
            fx.Core = core.transform;
            fx.Frost = !fire;
            return root;
        }

        private static Color BoltColor(ProjectileKind kind)
        {
            bool fire = kind == ProjectileKind.Firebolt || kind == ProjectileKind.ShadowBolt || kind == ProjectileKind.Smite || kind == ProjectileKind.Spit;
            return kind == ProjectileKind.Spit ? new Color(0.45f, 0.85f, 0.15f)
                : kind == ProjectileKind.Smite ? new Color(1f, 0.85f, 0.4f)
                : kind == ProjectileKind.ShadowBolt ? new Color(0.7f, 0.3f, 1f)
                : fire ? new Color(1f, 0.45f, 0.12f) : new Color(0.45f, 0.8f, 1f);
        }

        /// <summary>A thrown knife: a steel blade and a leather grip, with a pale glint.</summary>
        private static Transform BuildKnife(Vector3 origin, Vector3 direction)
        {
            var root = new GameObject("Knife").transform;
            root.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            MeshKit.Part(root, "Blade", MeshKit.Box(new Vector3(0.03f, 0.008f, 0.2f)), Mat.Steel, new Vector3(0f, 0f, 0.05f));
            MeshKit.Part(root, "Grip", MeshKit.Box(new Vector3(0.025f, 0.02f, 0.1f)), Mat.Leather, new Vector3(0f, 0f, -0.1f));
            var glint = MeshKit.Part(root, "Glint", MeshKit.Ball(0.02f, 6, 4), Mat.Glow, new Vector3(0f, 0f, 0.14f));
            ArtLibrary.SetGlow(glint.GetComponent<Renderer>(), new Color(0.8f, 0.85f, 1f) * 2f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
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
