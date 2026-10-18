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
        private Transform horseBody, horseHead;
        private readonly List<(Transform leg, float phase)> horseLegs = new List<(Transform, float)>();
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
            float gait = Time.time * (speed > 0.1f ? 2.2f + speed * 0.5f : 0f);
            foreach (var (leg, phase) in horseLegs)
                leg.localRotation = Quaternion.Euler(speed > 0.1f ? Mathf.Sin(gait * Mathf.PI * 2f + phase) * Mathf.Lerp(12f, 28f, speed / 6.4f) : 0f, 0f, 0f);
            if (horseBody != null) horseBody.localPosition = new Vector3(0f, 0f, 0f) + Vector3.up * (speed > 0.1f ? Mathf.Abs(Mathf.Sin(gait * Mathf.PI * 2f)) * 0.06f : Mathf.Sin(Time.time * 1.3f) * 0.01f);
            if (horseHead != null) horseHead.localRotation = Quaternion.Euler(speed > 0.1f ? Mathf.Sin(gait * Mathf.PI * 4f) * 5f : Mathf.Sin(Time.time * 0.7f) * 6f - 4f, 0f, 0f);
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

        /// <summary>A skeletal horse in harness, its legs on pivots so it can walk and trot.</summary>
        private void BuildHorse(Transform root)
        {
            var horse = MeshKit.Pivot(root, "Horse", new Vector3(0f, 0f, 5.2f));
            horseBody = MeshKit.Pivot(horse, "HorseBody", Vector3.zero);
            var b = horseBody;
            MeshKit.Part(b, "Spine", MeshKit.Cylinder(0.05f, 0.05f, 1.8f, 6), Mat.Bone, new Vector3(0f, 1.55f, 0f), new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 6; i++)
                MeshKit.Part(b, "Rib" + i, MeshKit.Lathe("horseRib2", new[] { new Vector2(0.3f, 0f), new Vector2(0.33f, 0.03f), new Vector2(0.3f, 0.06f) }, 12, true, 230f), Mat.Bone,
                    new Vector3(0f, 1.52f - Mathf.Abs(i - 2.5f) * 0.01f, 0.5f - i * 0.17f), (Quaternion.Euler(0f, 0f, -115f) * Quaternion.Euler(90f, 0f, 0f)).eulerAngles, new Vector3(1f, 1f, 1f - Mathf.Abs(i - 2.5f) * 0.05f));
            MeshKit.Part(b, "Pelvis", MeshKit.Box(new Vector3(0.46f, 0.22f, 0.34f)), Mat.Bone, new Vector3(0f, 1.5f, -0.88f));
            MeshKit.Part(b, "Withers", MeshKit.Box(new Vector3(0.36f, 0.2f, 0.3f)), Mat.Bone, new Vector3(0f, 1.6f, 0.75f));
            // Saddle cloth and harness.
            var cloth = MeshKit.Part(b, "Caparison", MeshKit.Box(new Vector3(0.75f, 0.5f, 0.7f)), Mat.Cloth, new Vector3(0f, 1.45f, -0.05f));
            ArtLibrary.Tint(cloth.GetComponent<Renderer>(), new Color(0.3f, 0.07f, 0.08f));
            MeshKit.Part(b, "Collar", MeshKit.Lathe("collar2", new[] { new Vector2(0.2f, 0f), new Vector2(0.25f, 0.06f), new Vector2(0.2f, 0.12f) }, 12), Mat.Leather, new Vector3(0f, 1.75f, 0.85f), new Vector3(45f, 0f, 0f));
            // Tail of tattered hair.
            for (int i = 0; i < 4; i++)
                MeshKit.Part(b, "Tail" + i, MeshKit.Box(new Vector3(0.04f, 0.6f, 0.02f)), Mat.Dark, new Vector3(-0.05f + i * 0.035f, 1.25f, -1.05f), new Vector3(-20f, 0f, -6f + i * 4f));

            // Neck and skull, with a mane and two cold eyes.
            horseHead = MeshKit.Pivot(b, "Neck", new Vector3(0f, 1.7f, 0.9f));
            MeshKit.Part(horseHead, "NeckBone", MeshKit.Cylinder(0.06f, 0.05f, 0.85f, 6), Mat.Bone, new Vector3(0f, 0.3f, 0.2f), new Vector3(35f, 0f, 0f));
            for (int i = 0; i < 5; i++)
                MeshKit.Part(horseHead, "Mane" + i, MeshKit.Box(new Vector3(0.03f, 0.22f, 0.06f)), Mat.Dark, new Vector3(0f, 0.15f + i * 0.13f, 0.05f + i * 0.07f), new Vector3(-30f, 0f, 0f));
            var skull = MeshKit.Pivot(horseHead, "Skull", new Vector3(0f, 0.68f, 0.5f), new Vector3(70f, 0f, 0f));
            MeshKit.Part(skull, "Cranium", MeshKit.Box(new Vector3(0.2f, 0.22f, 0.24f)), Mat.Bone, Vector3.zero);
            MeshKit.Part(skull, "Muzzle", MeshKit.Box(new Vector3(0.14f, 0.16f, 0.42f)), Mat.Bone, new Vector3(0f, -0.03f, 0.3f), new Vector3(-6f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                var eye = MeshKit.Part(skull, "Eye", MeshKit.Ball(0.025f, 6, 4), Mat.Glow, new Vector3(s * 0.09f, 0.05f, 0.08f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.45f, 0.75f, 1f) * 2f);
                MeshKit.Part(skull, "Ear", MeshKit.Lathe("horseEar", new[] { new Vector2(0.03f, 0f), new Vector2(0f, 0.12f) }, 4), Mat.Bone, new Vector3(s * 0.07f, 0.12f, -0.08f), new Vector3(-20f, 0f, s * 15f));
            }
            MeshKit.Part(skull, "Bridle", MeshKit.Box(new Vector3(0.18f, 0.03f, 0.03f)), Mat.Leather, new Vector3(0f, -0.02f, 0.25f));

            // Four legs: thigh and shin bones, a hoof, swinging from hip and shoulder.
            foreach (var (x, z, phase) in new[] { (0.18f, 0.65f, 0f), (-0.18f, 0.65f, Mathf.PI), (0.18f, -0.8f, Mathf.PI), (-0.18f, -0.8f, 0f) })
            {
                var leg = MeshKit.Pivot(b, "Leg", new Vector3(x, 1.45f, z));
                MeshKit.Part(leg, "Upper", MeshKit.Cylinder(0.05f, 0.04f, 0.72f, 6), Mat.Bone, new Vector3(0f, -0.36f, 0f), new Vector3(z > 0 ? 4f : -8f, 0f, 0f));
                MeshKit.Part(leg, "Lower", MeshKit.Cylinder(0.035f, 0.03f, 0.72f, 6), Mat.Bone, new Vector3(0f, -1.06f, z > 0 ? 0.03f : -0.05f));
                MeshKit.Part(leg, "Hoof", MeshKit.Box(new Vector3(0.11f, 0.08f, 0.15f)), Mat.Dark, new Vector3(0f, -1.41f, z > 0 ? 0.05f : -0.03f));
                horseLegs.Add((leg, phase));
            }
            // Reins back to the box.
            foreach (float s in new[] { -0.12f, 0.12f })
                MeshKit.Part(root, "Rein", MeshKit.Box(new Vector3(0.015f, 0.015f, 3.6f)), Mat.Leather, new Vector3(s, 1.95f, 3.95f), new Vector3(-2f, 0f, 0f));
        }
    }
}
