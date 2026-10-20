using Deepbound.Art;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// The Tallyman (2026-10-20): the reason not to linger. A party still on a floor after
    /// <see cref="DungeonDirector.TallyArriveSeconds"/> hears a far bell, then he comes in at the
    /// start of the floor: a gaunt collector in a stovepipe hat with a green lantern and a ledger. He
    /// cannot be hurt, stunned or stopped; he walks after the nearest standing player, a little faster
    /// every minute (a sprint always outruns him, a walk soon does not, a reliquary carrier never does),
    /// and his touch takes a quarter of your life a second. He goes when the guardian's arena seals or
    /// the floor ends. The dread of Spelunky's ghost, in a crypt.
    /// <para>Server moves him on the navmesh; every peer draws him from the synced position.</para>
    /// </summary>
    public class Tallyman : NetworkBehaviour
    {
        public const float BaseSpeed = 3.5f, MaxSpeed = 5.7f, SpeedGrowth = 0.012f, Reach = 1.7f, DrainPerSecond = 0.25f;

        public static Tallyman Active { get; private set; }

        public readonly NetworkVariable<Vector3> Pos = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<float> Yaw = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> Leaving = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> ArrivedAt = new NetworkVariable<double>();

        /// <summary>Server, for the screenshot run: ignore the arena seal and the phase (only a dismissal sends him off).</summary>
        public bool ServerStay { get; set; }

        /// <summary>Server, for tests: the damage his touch has dealt.</summary>
        public float DamageDealt { get; private set; }

        private NavMeshPath path;
        private Vector3[] corners;
        private int corner;
        private float repathAt, drainAt, leftAt = -1f, stepAt, whisperAt;
        private float stride;
        private Transform vis, legL, legR, armL, armR, lantern, head;
        private Light lanternLight;
        private Transform mist;
        private Vector3 lastPos;
        private static readonly Color LanternGreen = new Color(0.62f, 1f, 0.7f);

        private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public float Speed => Mathf.Min(MaxSpeed, BaseSpeed + (float)(Now - ArrivedAt.Value) * SpeedGrowth);

        public override void OnNetworkSpawn()
        {
            Active = this;
            if (IsServer)
            {
                path = new NavMeshPath();
                if (NavMesh.SamplePosition(transform.position, out var hit, 6f, NavMesh.AllAreas)) transform.position = hit.position;
                Pos.Value = transform.position;
                Yaw.Value = transform.eulerAngles.y;
                ArrivedAt.Value = Now;
            }
            transform.position = Pos.Value;
            lastPos = transform.position;
            Build();
        }

        public override void OnNetworkDespawn()
        {
            if (Active == this) Active = null;
        }

        public override void OnDestroy()
        {
            if (Active == this) Active = null;
            base.OnDestroy();
        }

        /// <summary>Server: tip the hat and go (the arena sealed, the floor ended).</summary>
        public void ServerLeave()
        {
            if (!IsServer || Leaving.Value) return;
            Leaving.Value = true;
        }

        private static bool Huntable(PlayerLife p) => p != null && !p.Downed.Value && !p.Soul.Value && p.Health != null && p.Health.IsAlive;

        private PlayerLife Nearest()
        {
            PlayerLife best = null;
            float bestD = float.MaxValue;
            foreach (var p in PlayerLife.All)
            {
                if (!Huntable(p)) continue;
                float d = (p.transform.position - Pos.Value).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private void Update()
        {
            if (IsServer) ServerTick();
            Animate();
        }

        private void ServerTick()
        {
            var dd = DungeonDirector.Instance;
            if (!Leaving.Value && !ServerStay && dd != null && (dd.Phase.Value != RunPhase.Exploring || dd.BossSealed)) ServerLeave();
            if (Leaving.Value)
            {
                if (leftAt < 0f) leftAt = Time.time;
                if (Time.time - leftAt > 2.6f) NetworkObject.Despawn();
                return;
            }
            var target = Nearest();
            if (target == null) return;
            var at = Pos.Value;
            var goal = target.transform.position;

            if (Time.time >= repathAt)
            {
                repathAt = Time.time + 0.35f;
                corners = null;
                if (NavMesh.SamplePosition(goal, out var gh, 3f, NavMesh.AllAreas) && NavMesh.SamplePosition(at, out var sh, 3f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(sh.position, gh.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                {
                    corners = path.corners;
                    corner = corners.Length > 1 ? 1 : 0;
                }
            }

            // No way round: he walks straight at you, through whatever is in between, at half pace.
            float speed = Speed * (corners == null ? 0.5f : 1f) * Time.deltaTime;
            Vector3 next = goal;
            if (corners != null && corner < corners.Length)
            {
                next = corners[corner];
                var flat = next - at;
                flat.y = 0f;
                if (flat.magnitude < 0.3f && corner < corners.Length - 1) { corner++; next = corners[corner]; }
            }
            var step = Vector3.MoveTowards(at, next, speed);
            var dir = step - at;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f) Yaw.Value = Mathf.MoveTowardsAngle(Yaw.Value, Quaternion.LookRotation(dir).eulerAngles.y, 240f * Time.deltaTime);
            Pos.Value = step;

            // His touch: a quarter of your life each second, through armour.
            if (Time.time >= drainAt)
            {
                foreach (var p in PlayerLife.All)
                {
                    if (!Huntable(p)) continue;
                    var d = p.transform.position - step;
                    if (Mathf.Abs(d.y) > 2.4f) continue;
                    d.y = 0f;
                    if (d.magnitude > Reach) continue;
                    drainAt = Time.time + 0.5f;
                    DamageDealt += p.Health.ServerDamage(p.Health.Max.Value * DrainPerSecond * 0.5f + p.Health.Defense * Combat.Health.DefenseWeight, step, NetworkObjectId);
                    TouchRpc(p.transform.position);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TouchRpc(Vector3 at)
        {
            Vfx.Ring(at + Vector3.up * 0.05f, 1.1f, LanternGreen, 0.35f);
            Sfx.Play(Sound.Whisper, at, 0.9f, 0.6f);
        }

        // ------------------------------------------------------------------
        // Presentation (every peer)
        // ------------------------------------------------------------------

        private void Animate()
        {
            if (vis == null) return;
            var want = Pos.Value;
            transform.position = IsServer ? want : Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, Yaw.Value, 0f), 1f - Mathf.Exp(-Time.deltaTime * 8f));
            var moved = transform.position - lastPos;
            moved.y = 0f;
            float pace = moved.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            lastPos = transform.position;
            stride += moved.magnitude * 2.2f;

            // A long, unhurried gait: stiff legs, the coat swaying, the lantern swinging on its chain.
            float walk = Mathf.Clamp01(pace / 2f);
            float s = Mathf.Sin(stride);
            legL.localRotation = Quaternion.Euler(s * 24f * walk, 0f, 0f);
            legR.localRotation = Quaternion.Euler(-s * 24f * walk, 0f, 0f);
            armL.localRotation = Quaternion.Euler(-55f + s * 4f * walk, 0f, -6f);
            armR.localRotation = Quaternion.Euler(-28f - s * 10f * walk, 0f, 8f);
            lantern.localRotation = Quaternion.Euler(s * 14f * walk + Mathf.Sin(Time.time * 1.7f) * 4f, 0f, Mathf.Cos(stride * 0.5f) * 8f * walk);
            head.localRotation = Quaternion.Euler(8f + Mathf.Sin(Time.time * 0.6f) * 3f, Mathf.Sin(Time.time * 0.45f) * 14f, Mathf.Sin(Time.time * 0.8f) * 5f);
            float bob = Mathf.Abs(Mathf.Cos(stride)) * 0.05f * walk;

            float sink = 0f;
            if (Leaving.Value)
            {
                if (leftAt < 0f) leftAt = Time.time;
                sink = Mathf.Clamp01((Time.time - leftAt) / 2.4f);
            }
            vis.localPosition = new Vector3(0f, bob - sink * 2.9f, 0f);
            if (lanternLight != null) lanternLight.intensity = (2.2f + Mathf.Sin(Time.time * 7f) * 0.2f) * (1f - sink);
            if (mist != null) mist.localScale = new Vector3(1f + Mathf.Sin(Time.time * 0.9f) * 0.08f, 1f, 1f + Mathf.Cos(Time.time * 0.7f) * 0.08f) * (1f - sink * 0.6f);

            // Heavy, slow footfalls and a murmur from the ledger, near enough to hear.
            var cam = ViewCamera.Current;
            float near = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 99f;
            if (near < 30f && !Leaving.Value)
            {
                if (walk > 0.3f && Time.time >= stepAt) { stepAt = Time.time + Mathf.PI / Mathf.Max(0.5f, pace * 2.2f); Sfx.Play(Sound.Step, transform.position, 0.9f, 0.55f); }
                if (Time.time >= whisperAt) { whisperAt = Time.time + Random.Range(2.2f, 3.6f); Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 2.4f, 0.55f, 0.75f); }
            }
        }

        private void Build()
        {
            vis = new GameObject("Vis").transform;
            vis.SetParent(transform, false);
            // Dark, but not so dark he vanishes into a crypt: a grave-green black that his lantern catches.
            var coat = new Color(0.13f, 0.15f, 0.13f);
            var trim = new Color(0.32f, 0.08f, 0.08f);

            // Legs: long and thin, in black hose, with buckled shoes.
            foreach (float side in new[] { -1f, 1f })
            {
                var hip = MeshKit.Pivot(vis, side < 0 ? "LegL" : "LegR", new Vector3(side * 0.12f, 1.25f, 0f));
                var leg = MeshKit.Part(hip, "Shin", MeshKit.Cylinder(0.055f, 0.075f, 1.2f, 7), Mat.Cloth, new Vector3(0f, -1.22f, 0f));
                ArtLibrary.Tint(leg.GetComponent<Renderer>(), coat);
                MeshKit.Part(hip, "Shoe", MeshKit.Box(new Vector3(0.12f, 0.08f, 0.3f)), Mat.Leather, new Vector3(0f, -1.21f, 0.06f));
                MeshKit.Part(hip, "Buckle", MeshKit.Box(new Vector3(0.07f, 0.05f, 0.02f)), Mat.Steel, new Vector3(0f, -1.18f, 0.2f));
                if (side < 0) legL = hip; else legR = hip;
            }

            // The coat: a long tailed frock flaring below the waist, a high collar, a red sash.
            var skirt = MeshKit.Part(vis, "Coat", MeshKit.Lathe("tallyCoat", new[]
            {
                new Vector2(0.44f, 0f), new Vector2(0.38f, 0.35f), new Vector2(0.27f, 0.85f), new Vector2(0.22f, 1.05f), new Vector2(0f, 1.06f),
            }, 14), Mat.Cloth, new Vector3(0f, 0.62f, 0f));
            ArtLibrary.Tint(skirt.GetComponent<Renderer>(), coat);
            var chest = MeshKit.Part(vis, "Chest", MeshKit.Box(new Vector3(0.48f, 0.68f, 0.28f)), Mat.Cloth, new Vector3(0f, 1.98f, 0f));
            ArtLibrary.Tint(chest.GetComponent<Renderer>(), coat);
            var sash = MeshKit.Part(vis, "Sash", MeshKit.Box(new Vector3(0.5f, 0.1f, 0.3f)), Mat.Cloth, new Vector3(0f, 1.7f, 0f));
            ArtLibrary.Tint(sash.GetComponent<Renderer>(), trim);
            var collar = MeshKit.Part(vis, "Collar", MeshKit.Lathe("tallyCollar", new[] { new Vector2(0.16f, 0f), new Vector2(0.2f, 0.2f), new Vector2(0.18f, 0.22f) }, 12, true, 260f),
                Mat.Cloth, new Vector3(0f, 2.3f, 0f), new Vector3(0f, 50f, 0f));
            ArtLibrary.Tint(collar.GetComponent<Renderer>(), coat);
            foreach (float side in new[] { -1f, 1f })
            {
                var lapel = MeshKit.Part(vis, "Lapel", MeshKit.Box(new Vector3(0.1f, 0.5f, 0.02f)), Mat.Cloth, new Vector3(side * 0.1f, 2.04f, 0.15f), new Vector3(0f, 0f, side * 10f));
                ArtLibrary.Tint(lapel.GetComponent<Renderer>(), trim);
                for (int b = 0; b < 3; b++)
                    MeshKit.Part(vis, "Button", MeshKit.Ball(0.022f, 6, 4), Mat.Gold, new Vector3(side * 0.06f, 1.85f + b * 0.14f, 0.15f));
            }

            // The head: a long bare skull under a stovepipe hat, two pale points in the sockets.
            head = MeshKit.Pivot(vis, "Head", new Vector3(0f, 2.42f, 0.02f));
            MeshKit.Part(head, "Neck", MeshKit.Cylinder(0.05f, 0.045f, 0.12f, 6), Mat.Bone, Vector3.zero);
            // The skull is faintly self-lit, like a soul skull, so his face reads across a dark room.
            var skull = MeshKit.Part(head, "Skull", MeshKit.Ball(0.15f, 12, 8), Mat.Glow, new Vector3(0f, 0.22f, 0f), default, new Vector3(0.86f, 1.15f, 0.95f));
            ArtLibrary.SetGlow(skull.GetComponent<Renderer>(), new Color(0.72f, 0.8f, 0.7f) * 0.42f);
            MeshKit.Part(head, "Jaw", MeshKit.Box(new Vector3(0.15f, 0.06f, 0.11f)), Mat.Bone, new Vector3(0f, 0.08f, 0.05f));
            for (int t = 0; t < 6; t++)
                MeshKit.Part(head, "Tooth", MeshKit.Box(new Vector3(0.016f, 0.03f, 0.012f)), Mat.Bone, new Vector3(-0.05f + t * 0.02f, 0.12f, 0.115f));
            MeshKit.Part(head, "Nose", MeshKit.Box(new Vector3(0.03f, 0.045f, 0.02f)), Mat.Dark, new Vector3(0f, 0.19f, 0.135f));
            foreach (float side in new[] { -1f, 1f })
            {
                MeshKit.Part(head, "Socket", MeshKit.Ball(0.04f, 8, 6), Mat.Dark, new Vector3(side * 0.05f, 0.25f, 0.115f), default, new Vector3(1f, 0.85f, 0.6f));
                var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.012f, 6, 4), Mat.Glow, new Vector3(side * 0.05f, 0.25f, 0.135f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), LanternGreen * 3f);
                eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var hat = MeshKit.Part(head, "Hat", MeshKit.Cylinder(0.15f, 0.165f, 0.42f, 12), Mat.Cloth, new Vector3(0f, 0.34f, -0.01f), new Vector3(-6f, 0f, 0f));
            ArtLibrary.Tint(hat.GetComponent<Renderer>(), coat * 0.8f);
            var brim = MeshKit.Part(head, "Brim", MeshKit.Cylinder(0.29f, 0.29f, 0.02f, 16), Mat.Cloth, new Vector3(0f, 0.34f, -0.01f), new Vector3(-6f, 0f, 0f));
            ArtLibrary.Tint(brim.GetComponent<Renderer>(), coat * 0.8f);
            var band = MeshKit.Part(head, "Band", MeshKit.Cylinder(0.157f, 0.158f, 0.06f, 12), Mat.Cloth, new Vector3(0f, 0.37f, -0.013f), new Vector3(-6f, 0f, 0f));
            ArtLibrary.Tint(band.GetComponent<Renderer>(), trim);
            // A tally card tucked in the band.
            MeshKit.Part(head, "Card", MeshKit.Box(new Vector3(0.07f, 0.1f, 0.005f)), Mat.Cloth, new Vector3(0.1f, 0.45f, 0.08f), new Vector3(-6f, 30f, 8f));

            // Right arm: the lantern on a short chain. Left arm: the ledger, held open.
            armR = MeshKit.Pivot(vis, "ArmR", new Vector3(0.3f, 2.24f, 0f));
            var sleeveR = MeshKit.Part(armR, "Sleeve", MeshKit.Cylinder(0.06f, 0.075f, 0.9f, 7), Mat.Cloth, new Vector3(0f, -0.9f, 0f));
            ArtLibrary.Tint(sleeveR.GetComponent<Renderer>(), coat);
            MeshKit.Part(armR, "Hand", MeshKit.Box(new Vector3(0.06f, 0.14f, 0.06f)), Mat.Bone, new Vector3(0f, -0.98f, 0f));
            lantern = MeshKit.Pivot(armR, "Lantern", new Vector3(0f, -1.04f, 0f));
            MeshKit.Part(lantern, "Chain", MeshKit.Box(new Vector3(0.012f, 0.22f, 0.012f)), Mat.Steel, new Vector3(0f, -0.11f, 0f));
            MeshKit.Part(lantern, "Cap", MeshKit.Cylinder(0.1f, 0.03f, 0.08f, 6), Mat.Steel, new Vector3(0f, -0.3f, 0f));
            MeshKit.Part(lantern, "Base", MeshKit.Cylinder(0.1f, 0.1f, 0.04f, 6), Mat.Steel, new Vector3(0f, -0.56f, 0f));
            for (int b = 0; b < 4; b++)
            {
                float a = b * 90f * Mathf.Deg2Rad + Mathf.PI / 4f;
                MeshKit.Part(lantern, "Bar", MeshKit.Box(new Vector3(0.014f, 0.24f, 0.014f)), Mat.Steel, new Vector3(Mathf.Cos(a) * 0.085f, -0.42f, Mathf.Sin(a) * 0.085f));
            }
            var glass = MeshKit.Part(lantern, "Glass", MeshKit.Cylinder(0.075f, 0.075f, 0.22f, 6), Mat.Glow, new Vector3(0f, -0.53f, 0f));
            ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), LanternGreen * 1.5f);
            glass.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lanternLight = new GameObject("LanternLight").AddComponent<Light>();
            lanternLight.transform.SetParent(lantern, false);
            lanternLight.transform.localPosition = new Vector3(0f, -0.42f, 0f);
            lanternLight.type = LightType.Point;
            lanternLight.color = LanternGreen;
            lanternLight.range = 10f;
            lanternLight.intensity = 2.2f;
            lanternLight.shadows = LightShadows.None;

            armL = MeshKit.Pivot(vis, "ArmL", new Vector3(-0.3f, 2.24f, 0f));
            var sleeveL = MeshKit.Part(armL, "Sleeve", MeshKit.Cylinder(0.06f, 0.075f, 0.82f, 7), Mat.Cloth, new Vector3(0f, -0.82f, 0f));
            ArtLibrary.Tint(sleeveL.GetComponent<Renderer>(), coat);
            MeshKit.Part(armL, "Hand", MeshKit.Box(new Vector3(0.06f, 0.12f, 0.06f)), Mat.Bone, new Vector3(0f, -0.88f, 0f));
            var book = MeshKit.Pivot(armL, "Ledger", new Vector3(0.12f, -0.92f, 0.06f), new Vector3(60f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                MeshKit.Part(book, "Cover", MeshKit.Box(new Vector3(0.17f, 0.24f, 0.02f)), Mat.Leather, new Vector3(side * 0.085f, 0f, 0f), new Vector3(0f, side * -12f, 0f));
                var page = MeshKit.Part(book, "Pages", MeshKit.Box(new Vector3(0.15f, 0.22f, 0.02f)), Mat.Cloth, new Vector3(side * 0.08f, 0f, 0.014f), new Vector3(0f, side * -12f, 0f));
                ArtLibrary.Tint(page.GetComponent<Renderer>(), new Color(0.78f, 0.72f, 0.58f));
            }
            // A low grave-mist round his feet.
            var fog = MeshKit.Part(transform, "Mist", MeshKit.Cylinder(1.3f, 0.9f, 0.18f, 18), Mat.Smoke, Vector3.up * 0.02f);
            ArtLibrary.Tint(fog.GetComponent<Renderer>(), new Color(0.55f, 0.9f, 0.6f, 0.22f));
            fog.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mist = fog.transform;
            MeshKit.MergeParts(vis);
        }
    }
}
