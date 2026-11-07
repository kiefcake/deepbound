using System.Collections.Generic;
using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The carriage at the gate, facing out down the forest road: a covered wagon with an open rear
    /// bench of four seats, Corwin on the box, and a skeletal horse in harness. Built on every peer
    /// (part of the camp); parked until the party is seated, then <see cref="Depart"/> rolls it out
    /// through the gate and down the dark road on a fixed curve of time, so every client sees the
    /// same ride and seated players (who pin themselves to their seat) ride along with it.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class CarriageRide : MonoBehaviour
    {
        public static CarriageRide Instance { get; private set; }
        public const int SeatCount = 4;
        /// <summary>How long the ride lasts before the floor fades in.</summary>
        public const float RideSeconds = 7.5f;

        private Vector3 parkedAt;
        private Quaternion facing;
        private float departStart = -1f;
        private readonly List<Transform> wheels = new List<Transform>();
        private Transform[] seats;
        /// <summary>The top of the canvas (the cat perches there while anyone is seated).</summary>
        public Transform Roof { get; private set; }
        private Transform horseBody, horseHead, horseSkull, horseJaw;
        private Transform[] horseTail;
        private sealed class HorseLeg { public Transform upper, lower; public float phase; public bool front, stamper; }
        private readonly List<HorseLeg> horseLegs = new List<HorseLeg>();
        private float gaitPhase, gaitBlend;
        private static readonly float[] TailRest = { 55f, -12f, -14f, -14f };
        private float wheelAngle;

        /// <summary>Where the carriage stands at camp: wagon centre at <paramref name="at"/>, facing <paramref name="dir"/>.</summary>
        public static CarriageRide Build(Transform parent, Vector3 at, Vector3 dir)
        {
            var root = new GameObject("CarriageRide").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(at, Quaternion.LookRotation(dir));
            var ride = root.gameObject.AddComponent<CarriageRide>();
            ride.parkedAt = at;
            ride.facing = root.rotation;
            ride.BuildWagon(root);
            Instance = ride;
            return ride;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        // ---- Seats ----

        /// <summary>World position a seated player's feet are pinned to (they sit on the tailgate, legs over the edge).</summary>
        public Vector3 SeatPosition(int i) => seats[Mathf.Clamp(i, 0, SeatCount - 1)].position;
        /// <summary>The way a seated player faces: back, toward the castle they are leaving.</summary>
        public Quaternion SeatFacing => transform.rotation * Quaternion.Euler(0f, 180f, 0f);
        /// <summary>The spot on the ground behind seat <paramref name="i"/>, where you climb on and get off.</summary>
        public Vector3 SeatGround(int i)
        {
            var p = SeatPosition(i) - transform.forward * 0.9f;
            p.y = parkedAt.y + 0.05f;
            return p;
        }

        public bool Riding => departStart >= 0f;

        public void Depart() => departStart = Time.time;
        public void Park() { departStart = -1f; transform.SetPositionAndRotation(parkedAt, facing); }

        /// <summary>Distance down the road at <paramref name="t"/> seconds: a gentle pull away, then a steady trot.</summary>
        public static float Travel(float t) => t < 4f ? 0.8f * t * t : 12.8f + 6.4f * (t - 4f);

        private void Update()
        {
            float speed = 0f;
            if (departStart >= 0f)
            {
                float t = Time.time - departStart;
                transform.position = parkedAt + facing * Vector3.forward * Travel(t);
                speed = t < 4f ? 1.6f * t : 6.4f;
                // Back at camp next time: once the floor has replaced the camp, park again.
                if (!DungeonDirector.InLobby && t > RideSeconds) Park();
            }
            // The wheels roll with the ground; the horse walks, then trots.
            wheelAngle += speed / 0.62f * Mathf.Rad2Deg * Time.deltaTime;
            foreach (var w in wheels) w.localRotation = Quaternion.Euler(wheelAngle, 0f, 0f);
            AnimateHorse(speed);
        }

        /// <summary>
        /// The horse's life, all a function of Time.time (built and animated locally on every peer): a diagonal trot
        /// (front knees fold back, hind hocks forward) once the carriage moves, and standing it shifts its weight,
        /// stamps a hoof, tosses its head, swishes its tail and chatters its jaw.
        /// </summary>
        private void AnimateHorse(float speed)
        {
            if (horseBody == null || horseTail == null) return;
            float t = Time.time;
            bool moving = speed > 0.1f;
            gaitBlend = Mathf.MoveTowards(gaitBlend, moving ? 1f : 0f, Time.deltaTime * 3f);
            if (moving) gaitPhase += (0.8f + speed * 0.28f) * Time.deltaTime;
            float gb = gaitBlend, idle = 1f - gb;
            float k = Mathf.Clamp01(speed / 6.4f);
            float ph = gaitPhase * Mathf.PI * 2f;

            foreach (var leg in horseLegs)
            {
                float shift = Mathf.Sin(t * 0.45f + leg.phase) * 1.5f;
                float iu = leg.front ? shift : 14f + shift, il = leg.front ? 0f : -22f;
                if (leg.stamper)
                {
                    float c = t % 6.3f;
                    if (c > 5.4f) { float s = Mathf.Abs(Mathf.Sin((c - 5.4f) / 0.9f * Mathf.PI * 2f)); iu -= 22f * s; il += 62f * s; }
                }
                float a = Mathf.Sin(ph + leg.phase), swing = Mathf.Max(0f, -Mathf.Cos(ph + leg.phase)), amp = Mathf.Lerp(9f, 18f, k);
                float gu = leg.front ? a * amp : 14f + a * amp * 0.9f;
                float gl = leg.front ? swing * Mathf.Lerp(40f, 65f, k) : -22f - swing * Mathf.Lerp(10f, 16f, k);
                leg.upper.localRotation = Quaternion.Euler(Mathf.Lerp(iu, gu, gb), 0f, 0f);
                leg.lower.localRotation = Quaternion.Euler(Mathf.Lerp(il, gl, gb), 0f, 0f);
            }

            horseBody.localPosition = Vector3.up * Mathf.Lerp(Mathf.Sin(t * 1.3f) * 0.01f, Mathf.Abs(Mathf.Sin(ph)) * 0.04f, gb);
            horseBody.localRotation = Quaternion.Euler(Mathf.Sin(ph * 2f) * 1.2f * gb, 0f, Mathf.Lerp(Mathf.Sin(t * 0.4f) * 0.8f, Mathf.Sin(ph) * 1.5f, gb));

            // Head toss every 11 s.
            float toss = 0f, twist = 0f, c2 = t % 11f;
            if (c2 > 9.6f)
            {
                float u = (c2 - 9.6f) / 1.4f;
                toss = Mathf.Sin(u * Mathf.PI) * (0.6f + 0.4f * Mathf.Sin(u * Mathf.PI * 3f));
                twist = Mathf.Sin(u * Mathf.PI * 2f);
            }
            float nod = Mathf.Sin(ph * 2f - 0.6f) * gb;
            if (horseHead != null)
                horseHead.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.5f) * 1.5f * idle - 10f * toss * idle + nod * 3f, (twist * 8f + Mathf.Sin(t * 0.35f) * 3f) * idle, 0f);
            if (horseSkull != null)
                horseSkull.localRotation = Quaternion.Euler(50f + Mathf.Sin(t * 0.7f) * 3f * idle - 18f * toss * idle + nod * 5f, 0f, 0f);

            // Jaw chatter in bursts when standing, a loose jostle when trotting.
            float cb = t % 5f, chat = 0f;
            if (cb > 3.7f) chat = Mathf.Sin((cb - 3.7f) / 1.3f * Mathf.PI) * Mathf.Abs(Mathf.Sin(t * 21f));
            if (horseJaw != null)
                horseJaw.localRotation = Quaternion.Euler(Mathf.Lerp(2f + chat * 9f, 1.5f + Mathf.Abs(Mathf.Sin(ph * 2f)) * 3f, gb), 0f, 0f);

            // Tail: a lazy sway with an occasional hard swish; streams behind at a trot.
            float cs = t % 7f, swish = 0f;
            if (cs > 5.8f) { float u = (cs - 5.8f) / 1.2f; swish = Mathf.Sin(u * Mathf.PI * 4f) * Mathf.Sin(u * Mathf.PI) * 35f; }
            for (int i = 0; i < horseTail.Length; i++)
            {
                float lag = i * 0.12f, grow = 1f + i * 0.4f;
                float zi = Mathf.Lerp(Mathf.Sin((t - lag) * 1.7f) * 8f * grow * 0.5f + swish * idle * Mathf.Min(1f, 0.4f + i * 0.3f),
                    Mathf.Sin(ph - i * 0.5f) * 6f * grow, gb);
                float xi = TailRest[i] + (i == 0 ? gb * 8f * k : 0f) + Mathf.Sin((t - lag) * 1.1f) * 2f * idle;
                horseTail[i].localRotation = Quaternion.Euler(xi, 0f, zi);
            }
        }

        /// <summary>
        /// Everyone else on the bench (play-test 2026-11-01: "the other players are sinking through the carriage"): their
        /// own machines pin them to their seats, but what arrives here is a tenth of a second old while the wagon rolls on
        /// under them. Each peer moves the wagon itself, so it seats them where the wagon is now.
        /// </summary>
        private void LateUpdate()
        {
            if (seats == null) return;
            foreach (var p in Player.PlayerLife.All)
                if (p != null && !p.IsOwner && p.Seat.Value >= 0 && DungeonDirector.InLobby) p.transform.position = SeatPosition(p.Seat.Value);
        }

        // ---- The wagon ----

        private void BuildWagon(Transform root)
        {
            var body = MeshKit.Pivot(root, "Body", Vector3.zero);
            var wood = new Color(0.42f, 0.3f, 0.2f);
            GameObject W(Transform p, string n, Vector3 size, Vector3 at, Vector3 euler = default, float shade = 1f)
            {
                var g = MeshKit.Part(p, n, MeshKit.Box(size), Mat.Wood, at, euler);
                ArtLibrary.Tint(g.GetComponent<Renderer>(), wood * shade);
                return g;
            }

            // Chassis and bed: a long plank deck on two beams, iron-banded.
            W(body, "BeamL", new Vector3(0.14f, 0.16f, 4.2f), new Vector3(-0.7f, 0.86f, 0f), default, 0.7f);
            W(body, "BeamR", new Vector3(0.14f, 0.16f, 4.2f), new Vector3(0.7f, 0.86f, 0f), default, 0.7f);
            W(body, "Deck", new Vector3(2.2f, 0.1f, 4f), new Vector3(0f, 1.0f, 0f));
            for (int i = 0; i < 7; i++) W(body, "Plank" + i, new Vector3(0.02f, 0.11f, 4f), new Vector3(-0.95f + i * 0.32f, 1.01f, 0f), default, 0.6f);
            foreach (float z in new[] { -1.9f, 1.9f }) MeshKit.Part(body, "Band", MeshKit.Box(new Vector3(2.26f, 0.06f, 0.06f)), Mat.Steel, new Vector3(0f, 1.0f, z));

            // The covered front half: sides, hoops and a canvas roof (Corwin sits ahead of it).
            foreach (float s in new[] { -1f, 1f })
            {
                W(body, "Side", new Vector3(0.08f, 0.55f, 2.2f), new Vector3(s * 1.07f, 1.32f, 0.85f));
                W(body, "Rail", new Vector3(0.1f, 0.06f, 4f), new Vector3(s * 1.07f, 1.62f, 0f), default, 0.8f);
                for (int k = 0; k < 5; k++) W(body, "Stake", new Vector3(0.07f, 0.6f, 0.07f), new Vector3(s * 1.07f, 1.32f, -1.9f + k * 0.95f), default, 0.75f);
            }
            var roof = Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 3; i++)
                MeshKit.Part(body, "Hoop", MeshKit.Lathe("hoop", new[] { new Vector2(1.08f, 0f), new Vector2(1.12f, 0.06f), new Vector2(1.08f, 0.12f) }, 12, true, 180f),
                    Mat.Wood, new Vector3(0f, 1.62f, -0.15f + i * 0.95f), roof.eulerAngles);
            var canvas = MeshKit.Part(body, "Canvas", MeshKit.Lathe("canvasShort", new[] { new Vector2(1.06f, -1.05f), new Vector2(1.06f, 1.05f) }, 12, true, 180f), Mat.Cloth,
                new Vector3(0f, 1.62f, 0.85f), roof.eulerAngles);
            ArtLibrary.Tint(canvas.GetComponent<Renderer>(), new Color(0.55f, 0.5f, 0.4f));
            Roof = MeshKit.Pivot(body, "CatPerch", new Vector3(0f, 2.7f, 0.85f)); // where the castle cat sits to see riders off
            // A back panel for the canvas, and the driver's box out front.
            W(body, "CabBack", new Vector3(2.1f, 1.2f, 0.06f), new Vector3(0f, 1.62f, -0.2f), default, 0.85f);
            W(body, "Box", new Vector3(1.8f, 0.5f, 0.6f), new Vector3(0f, 1.3f, 2.25f));
            W(body, "BoxSeat", new Vector3(1.8f, 0.08f, 0.5f), new Vector3(0f, 1.6f, 2.2f), default, 0.8f);
            W(body, "Footboard", new Vector3(1.8f, 0.06f, 0.45f), new Vector3(0f, 1.05f, 2.65f), new Vector3(-25f, 0f, 0f), 0.7f);

            // The open rear: a bench along the tailgate (four seats), luggage in the corners.
            W(body, "Bench", new Vector3(2.0f, 0.1f, 0.42f), new Vector3(0f, 1.24f, -1.72f));
            W(body, "BenchBack", new Vector3(2.0f, 0.42f, 0.06f), new Vector3(0f, 1.48f, -1.48f), new Vector3(-8f, 0f, 0f), 0.85f);
            for (int i = 0; i < 4; i++) W(body, "Cushion" + i, new Vector3(0.42f, 0.04f, 0.36f), new Vector3(-0.78f + i * 0.52f, 1.31f, -1.72f), default, 0.5f);
            foreach (var (x, z, sz) in new[] { (0.75f, -0.9f, 0.45f), (-0.8f, -0.95f, 0.4f), (-0.8f, -0.6f, 0.3f) })
            {
                W(body, "Luggage", new Vector3(sz, sz, sz), new Vector3(x, 1.05f + sz / 2f, z), new Vector3(0f, x * 20f, 0f), 0.9f);
            }
            var trunk = MeshKit.Part(body, "Trunk", MeshKit.Box(new Vector3(0.7f, 0.4f, 0.4f)), Mat.Leather, new Vector3(0.1f, 1.25f, -0.55f), new Vector3(0f, 8f, 0f));
            ArtLibrary.Tint(trunk.GetComponent<Renderer>(), new Color(0.35f, 0.18f, 0.12f));
            // A step at the back to climb up.
            W(body, "Step", new Vector3(1.4f, 0.06f, 0.3f), new Vector3(0f, 0.5f, -2.15f), default, 0.7f);
            foreach (float s in new[] { -0.6f, 0.6f }) MeshKit.Part(body, "StepIron", MeshKit.Box(new Vector3(0.04f, 0.5f, 0.04f)), Mat.Steel, new Vector3(s, 0.75f, -2.1f));

            // Seats: feet on the tailgate edge, facing back down the road behind.
            seats = new Transform[SeatCount];
            for (int i = 0; i < SeatCount; i++)
            {
                seats[i] = MeshKit.Pivot(root, "Seat" + i, new Vector3(-0.78f + i * 0.52f, 0.42f, -1.98f));
                seats[i].localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            // Wheels: spoked, iron-rimmed, turning on their axles.
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                float z = i < 2 ? 1.25f : -1.3f;
                var hub = MeshKit.Pivot(root, "Wheel" + i, new Vector3(1.18f * side, 0.62f, z));
                MeshKit.Part(hub, "Rim", MeshKit.Lathe("wheelRim2", new[] { new Vector2(0.62f, -0.06f), new Vector2(0.62f, 0.06f), new Vector2(0.55f, 0.06f), new Vector2(0.55f, -0.06f), new Vector2(0.62f, -0.06f) }, 18),
                    Mat.Wood, Vector3.zero, new Vector3(0f, 0f, 90f));
                MeshKit.Part(hub, "Tyre", MeshKit.Lathe("wheelTyre", new[] { new Vector2(0.64f, -0.05f), new Vector2(0.64f, 0.05f), new Vector2(0.615f, 0.05f), new Vector2(0.615f, -0.05f), new Vector2(0.64f, -0.05f) }, 18),
                    Mat.Steel, Vector3.zero, new Vector3(0f, 0f, 90f));
                MeshKit.Part(hub, "Hub", MeshKit.Cylinder(0.11f, 0.11f, 0.24f, 8), Mat.Steel, Vector3.zero, new Vector3(0f, 0f, 90f));
                for (int k = 0; k < 6; k++) W(hub, "Spoke", new Vector3(0.05f, 1.12f, 0.05f), Vector3.zero, new Vector3(k * 30f, 0f, 0f), 0.85f);
                wheels.Add(hub);
            }
            MeshKit.Part(root, "AxleF", MeshKit.Cylinder(0.05f, 0.05f, 2.36f, 6), Mat.Steel, new Vector3(0f, 0.62f, 1.25f), new Vector3(0f, 0f, 90f));
            MeshKit.Part(root, "AxleR", MeshKit.Cylinder(0.05f, 0.05f, 2.36f, 6), Mat.Steel, new Vector3(0f, 0.62f, -1.3f), new Vector3(0f, 0f, 90f));

            // Shafts out to the horse.
            foreach (float s in new[] { -0.45f, 0.45f }) W(body, "Shaft", new Vector3(0.08f, 0.08f, 2.8f), new Vector3(s, 1.05f, 3.4f), new Vector3(-4f, 0f, 0f), 0.75f);

            // Lanterns on hooks at the four corners, swinging a little.
            foreach (var (x, z) in new[] { (1.12f, 2.0f), (-1.12f, 2.0f), (1.12f, -1.95f), (-1.12f, -1.95f) })
            {
                var hook = MeshKit.Pivot(root, "Lantern", new Vector3(x, 2.15f, z));
                MeshKit.Part(hook, "Bracket", MeshKit.Box(new Vector3(0.03f, 0.25f, 0.03f)), Mat.Steel, new Vector3(0f, 0.12f, 0f));
                MeshKit.Part(hook, "Cage", MeshKit.Cylinder(0.1f, 0.085f, 0.24f, 6), Mat.Steel, new Vector3(0f, -0.14f, 0f));
                MeshKit.Part(hook, "Cap", MeshKit.Lathe("lanternCap", new[] { new Vector2(0.12f, 0f), new Vector2(0f, 0.08f) }, 6), Mat.Steel, new Vector3(0f, -0.02f, 0f));
                Props.AddFlame(hook, new Vector3(0f, -0.2f, 0f), 0.35f, new Color(1f, 0.7f, 0.35f), z > 0 ? 3.5f : 2.5f, 6f, false);
            }

            BuildHorse(root);
            // Corwin on the box, reins in hand.
            Props.Townsfolk(root, root.TransformPoint(new Vector3(0f, 0.62f, 2.2f)), root.TransformPoint(new Vector3(0f, 1.9f, 8f)), Props.Folk.Driver);
            MeshKit.MergeParts(body);
        }

        /// <summary>A bone as a tapered cylinder from <paramref name="a"/> to <paramref name="b"/> (optionally trimmed at both ends so a chain reads as separate vertebrae).</summary>
        private static GameObject HBone(Transform p, string n, Vector3 a, Vector3 b, float r0, float r1, string mat = Mat.Bone, int sides = 5, float trim = 0f)
        {
            var d = b - a;
            if (trim > 0f) { var t0 = a + d * trim; b = b - d * trim; a = t0; d = b - a; }
            float len = Mathf.Max(0.01f, Mathf.Round(d.magnitude * 1000f) / 1000f);
            return MeshKit.Part(p, n, MeshKit.Cylinder(r0, r1, len, sides), mat, a, Quaternion.FromToRotation(Vector3.up, d.normalized).eulerAngles);
        }

        /// <summary>
        /// A gaunt skeletal draught horse in harness (about 118 parts before merging, a few dozen renderers after).
        /// Facing +Z, withers ~1.62 m. Legs are upper pivot + lower pivot (knee/hock) + hoof; the neck, skull, jaw and tail
        /// chain are pivots too, all driven by <see cref="AnimateHorse"/>. Static parts under a pivot are merged.
        /// </summary>
        private void BuildHorse(Transform root)
        {
            var horse = MeshKit.Pivot(root, "Horse", new Vector3(0f, 0f, 5.2f));
            horseBody = MeshKit.Pivot(horse, "HorseBody", Vector3.zero);
            var b = horseBody;
            var glowBlue = new Color(0.45f, 0.75f, 1f) * 0.9f;

            // Spine: nine points from the withers to the tail head, eight vertebral segments, three spikes over the withers.
            float[] sz = { 0.78f, 0.55f, 0.32f, 0.09f, -0.14f, -0.37f, -0.6f, -0.83f, -1.0f };
            float[] sy = { 1.6f, 1.56f, 1.52f, 1.5f, 1.5f, 1.52f, 1.54f, 1.52f, 1.5f };
            for (int i = 0; i < sz.Length - 1; i++)
                HBone(b, "Vertebra" + i, new Vector3(0f, sy[i], sz[i]), new Vector3(0f, sy[i + 1], sz[i + 1]), 0.04f, 0.036f, Mat.Bone, 5, 0.04f);
            for (int i = 0; i < 3; i++)
                MeshKit.Part(b, "Spike" + i, MeshKit.Box(new Vector3(0.02f, 0.13f - i * 0.025f, 0.04f)), Mat.Bone, new Vector3(0f, 1.66f - i * 0.025f, 0.78f - i * 0.16f), new Vector3(-18f, 0f, 0f));

            // Ribcage: six pairs, wide at mid-chest and tapering to the belly, shorter and narrower toward the rear.
            float[] rz = { 0.62f, 0.46f, 0.30f, 0.14f, -0.02f, -0.18f };
            float[] rw = { 0.85f, 1f, 1f, 0.95f, 0.9f, 0.8f };
            float[] rf = { 0.95f, 1f, 1f, 0.95f, 0.88f, 0.78f };
            for (int i = 0; i < rz.Length; i++)
            {
                float y0 = Mathf.Lerp(1.54f, 1.5f, Mathf.InverseLerp(0.6f, 0.1f, rz[i]));
                foreach (float s in new[] { -1f, 1f })
                {
                    var p0 = new Vector3(s * 0.05f, y0, rz[i]);
                    var p1 = new Vector3(s * 0.25f * rw[i], y0 - 0.2f * rf[i], rz[i] + 0.01f);
                    var p2 = new Vector3(s * 0.09f, y0 - 0.58f * rf[i], rz[i] + 0.03f);
                    HBone(b, "Rib", p0, p1, 0.02f, 0.018f, Mat.Bone, 4);
                    HBone(b, "RibLow", p1, p2, 0.018f, 0.014f, Mat.Bone, 4);
                }
            }
            HBone(b, "Sternum", new Vector3(0f, 0.93f, 0.66f), new Vector3(0f, 0.93f, -0.2f), 0.03f, 0.025f, Mat.Bone, 5);

            // Pelvis (sacrum and flared ilia) and shoulder blades.
            MeshKit.Part(b, "Sacrum", MeshKit.Box(new Vector3(0.14f, 0.1f, 0.3f)), Mat.Bone, new Vector3(0f, 1.5f, -0.85f));
            foreach (float s in new[] { -1f, 1f })
            {
                MeshKit.Part(b, "Ilium", MeshKit.Box(new Vector3(0.12f, 0.07f, 0.32f)), Mat.Bone, new Vector3(s * 0.17f, 1.48f, -0.82f), new Vector3(0f, 0f, -s * 25f));
                MeshKit.Part(b, "Scapula", MeshKit.Box(new Vector3(0.04f, 0.4f, 0.15f)), Mat.Bone, new Vector3(s * 0.17f, 1.46f, 0.68f), new Vector3(-20f, 0f, s * 8f));
            }

            // Saddle pad over the back (the ribs stay visible beneath it).
            var cloth = MeshKit.Part(b, "Pad", MeshKit.Box(new Vector3(0.46f, 0.03f, 0.44f)), Mat.Cloth, new Vector3(0f, 1.56f, 0.05f));
            ArtLibrary.Tint(cloth.GetComponent<Renderer>(), new Color(0.3f, 0.07f, 0.08f));

            // Harness on the body: back-strap-free girth round the barrel, rump strap, and traces to the shafts.
            const float gz = 0.3f;
            foreach (float s in new[] { -1f, 1f })
            {
                HBone(b, "Girth", new Vector3(s * 0.07f, 1.53f, gz), new Vector3(s * 0.275f, 1.3f, gz), 0.014f, 0.014f, Mat.Leather, 4);
                HBone(b, "GirthLow", new Vector3(s * 0.275f, 1.3f, gz), new Vector3(s * 0.1f, 0.93f, gz), 0.014f, 0.014f, Mat.Leather, 4);
                HBone(b, "Trace", new Vector3(s * 0.15f, 1.62f, 0.8f), new Vector3(s * 0.45f, 1.1f, -0.38f), 0.012f, 0.012f, Mat.Leather, 4);
            }
            MeshKit.Part(b, "Breeching", MeshKit.Box(new Vector3(0.46f, 0.025f, 0.025f)), Mat.Leather, new Vector3(0f, 1.36f, -1.02f));

            // Neck: six vertebrae arching from the withers to the poll, a ragged mane, the collar and hames.
            horseHead = MeshKit.Pivot(b, "Neck", new Vector3(0f, 1.6f, 0.72f));
            var np = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0.1f, 0.14f), new Vector3(0f, 0.22f, 0.27f), new Vector3(0f, 0.34f, 0.39f),
                new Vector3(0f, 0.44f, 0.5f), new Vector3(0f, 0.5f, 0.57f), new Vector3(0f, 0.52f, 0.62f),
            };
            for (int i = 0; i < 6; i++)
                HBone(horseHead, "NeckVertebra" + i, np[i], np[i + 1], Mathf.Lerp(0.085f, 0.05f, i / 6f), Mathf.Lerp(0.085f, 0.05f, (i + 1) / 6f), Mat.Bone, 6, 0.05f);
            var perp = new Vector3(0f, 0.77f, -0.63f);
            for (int i = 0; i < 5; i++)
            {
                float len = 0.17f + 0.07f * Mathf.Abs(Mathf.Sin(i * 12.9f));
                MeshKit.Part(horseHead, "Mane" + i, MeshKit.Box(new Vector3(0.025f, len, 0.06f)), Mat.Dark, np[i + 1] + perp * (0.06f + len * 0.5f),
                    new Vector3(-39f + Mathf.Sin(i * 7.3f) * 14f, 0f, Mathf.Sin(i * 3.1f) * 10f));
            }
            var collarRig = MeshKit.Pivot(horseHead, "CollarRig", new Vector3(0f, 0.04f, 0.07f), new Vector3(51f, 0f, 0f));
            MeshKit.Part(collarRig, "Collar", MeshKit.Lathe("horseCollar", new[] { new Vector2(0.11f, 0f), new Vector2(0.155f, 0.05f), new Vector2(0.155f, 0.11f), new Vector2(0.11f, 0.16f) }, 10),
                Mat.Leather, Vector3.zero, Vector3.zero, new Vector3(1.1f, 1f, 0.9f));
            foreach (float s in new[] { -1f, 1f })
                MeshKit.Part(collarRig, "Hame", MeshKit.Box(new Vector3(0.025f, 0.24f, 0.04f)), Mat.Steel, new Vector3(s * 0.17f, 0.1f, 0f));

            // Skull on its own pivot at the poll, nose hanging down and forward; the jaw hangs on a hinge below it.
            horseSkull = MeshKit.Pivot(horseHead, "Skull", np[6], new Vector3(50f, 0f, 0f));
            var sk = horseSkull;
            MeshKit.Part(sk, "Cranium", MeshKit.Box(new Vector3(0.17f, 0.17f, 0.21f)), Mat.Bone, new Vector3(0f, 0f, 0.08f));
            HBone(sk, "Muzzle", new Vector3(0f, -0.01f, 0.16f), new Vector3(0f, -0.06f, 0.5f), 0.07f, 0.038f, Mat.Bone, 6);
            MeshKit.Part(sk, "NoseTip", MeshKit.Box(new Vector3(0.07f, 0.05f, 0.06f)), Mat.Bone, new Vector3(0f, -0.065f, 0.52f));
            MeshKit.Part(sk, "Mouth", MeshKit.Box(new Vector3(0.05f, 0.016f, 0.2f)), Mat.Dark, new Vector3(0f, -0.088f, 0.3f));
            foreach (float s in new[] { -1f, 1f })
            {
                HBone(sk, "Cheek", new Vector3(s * 0.085f, 0.01f, 0.1f), new Vector3(s * 0.07f, -0.06f, 0.3f), 0.014f, 0.012f, Mat.Bone, 4);
                MeshKit.Part(sk, "Socket", MeshKit.Ball(0.04f, 6, 4), Mat.Dark, new Vector3(s * 0.075f, 0.03f, 0.16f));
                var eye = MeshKit.Part(sk, "Eye", MeshKit.Ball(0.028f, 6, 4), Mat.Glow, new Vector3(s * 0.098f, 0.03f, 0.17f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), glowBlue);
                MeshKit.Part(sk, "Ear", MeshKit.Lathe("horseEar", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.12f) }, 4), Mat.Bone, new Vector3(s * 0.06f, 0.08f, -0.02f), new Vector3(-25f, 0f, -s * 18f));
                // Blinkers just behind each socket, and the bridle's cheek strap down to the bit.
                MeshKit.Part(sk, "Blinker", MeshKit.Box(new Vector3(0.015f, 0.07f, 0.06f)), Mat.Leather, new Vector3(s * 0.108f, 0.03f, 0.095f), new Vector3(0f, s * 12f, 0f));
                HBone(sk, "CheekStrap", new Vector3(s * 0.075f, -0.075f, 0.42f), new Vector3(s * 0.085f, 0.04f, 0.12f), 0.007f, 0.007f, Mat.Leather, 4);
            }
            MeshKit.Part(sk, "Browband", MeshKit.Box(new Vector3(0.19f, 0.018f, 0.018f)), Mat.Leather, new Vector3(0f, 0.075f, 0.15f));
            MeshKit.Part(sk, "Bit", MeshKit.Box(new Vector3(0.15f, 0.012f, 0.012f)), Mat.Steel, new Vector3(0f, -0.078f, 0.42f));
            horseJaw = MeshKit.Pivot(sk, "Jaw", new Vector3(0f, -0.05f, 0.06f));
            MeshKit.Part(horseJaw, "Ramus", MeshKit.Box(new Vector3(0.13f, 0.09f, 0.06f)), Mat.Bone, new Vector3(0f, 0f, -0.01f));
            HBone(horseJaw, "LowerJaw", new Vector3(0f, -0.035f, 0f), new Vector3(0f, -0.06f, 0.36f), 0.04f, 0.025f, Mat.Bone, 6);
            // Reins from the harness ring on the collar to the bit (the rest run back to the box, below).
            foreach (float s in new[] { -1f, 1f })
                HBone(horseHead, "ReinFront", new Vector3(s * 0.12f, 0.2f, 0.2f), new Vector3(s * 0.075f, 0.52f - 0.378f, 0.62f + 0.23f), 0.008f, 0.008f, Mat.Leather, 4);

            // Four legs: shoulder/hip joint, upper bone, knee (front, folds back) or hock (hind, folds forward), cannon, fetlock, hoof.
            const float U = 0.6f, L = 0.8f;
            bool first = true;
            foreach (var (x, z, phase) in new[] { (0.17f, 0.64f, 0f), (-0.17f, 0.64f, Mathf.PI), (0.17f, -0.82f, Mathf.PI), (-0.17f, -0.82f, 0f) })
            {
                bool front = z > 0f;
                var upper = MeshKit.Pivot(b, front ? "ForeUpper" : "HindUpper", new Vector3(x, 1.42f, z));
                MeshKit.Part(upper, "Joint", MeshKit.Ball(0.065f, 6, 4), Mat.Bone, Vector3.zero);
                HBone(upper, "UpperBone", Vector3.zero, new Vector3(0f, -U, 0f), 0.05f, 0.036f, Mat.Bone, 6);
                var lower = MeshKit.Pivot(upper, front ? "Knee" : "Hock", new Vector3(0f, -U, 0f));
                MeshKit.Part(lower, "KneeBall", MeshKit.Ball(0.05f, 6, 4), Mat.Bone, Vector3.zero);
                HBone(lower, "Cannon", Vector3.zero, new Vector3(0f, -L + 0.06f, 0f), 0.03f, 0.022f, Mat.Bone, 5);
                MeshKit.Part(lower, "Fetlock", MeshKit.Ball(0.036f, 6, 4), Mat.Bone, new Vector3(0f, -L + 0.11f, 0f));
                MeshKit.Part(lower, "Hoof", MeshKit.Cylinder(0.065f, 0.048f, 0.09f, 6), Mat.Dark, new Vector3(0f, -L, 0f));
                horseLegs.Add(new HorseLeg { upper = upper, lower = lower, phase = phase, front = front, stamper = first });
                first = false;
            }

            // Tail: four vertebrae on a chain of pivots with ragged hair.
            horseTail = new Transform[4];
            var parent = b;
            for (int i = 0; i < 4; i++)
            {
                var tp = MeshKit.Pivot(parent, "Tail" + i, i == 0 ? new Vector3(0f, 1.5f, -1.0f) : new Vector3(0f, -0.2f, 0f), new Vector3(TailRest[i], 0f, 0f));
                HBone(tp, "TailBone", Vector3.zero, new Vector3(0f, -0.2f, 0f), 0.032f - i * 0.004f, 0.028f - i * 0.004f, Mat.Bone, 5, 0.06f);
                if (i > 0) MeshKit.Part(tp, "TailHair", MeshKit.Box(new Vector3(0.05f + i * 0.01f, 0.28f + i * 0.04f, 0.012f)), Mat.Dark, new Vector3(0f, -0.14f - i * 0.02f, 0f), new Vector3(0f, 0f, (i - 2) * 6f));
                horseTail[i] = tp;
                parent = tp;
            }

            // Reins back to the box, from the harness ring above the collar.
            foreach (float s in new[] { -1f, 1f })
                HBone(root, "Rein", new Vector3(s * 0.12f, 1.95f, 2.15f), new Vector3(s * 0.12f, 1.8f, 5.2f + 0.9f), 0.008f, 0.008f, Mat.Leather, 4);

            MeshKit.MergeParts(horse);
        }
    }
}
