using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.UI;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deepbound.Player
{
    /// <summary>
    /// Decides, per peer, what a spawned player object is: the local player you control,
    /// or someone else's avatar you only see. Also owns the player's visuals.
    ///
    /// <para>
    /// Every peer builds the full Knight model for every player. On the owner the model
    /// only casts shadows (you see your shadow, not the inside of your own helmet) and a
    /// separate first-person view model of sword and shield is attached to the camera.
    /// </para>
    ///
    /// <para>
    /// Movement is owner-authoritative: the client moves itself and the server accepts it.
    /// That is the right call for co-op — it feels instant with no prediction code — and
    /// everything that matters for fairness (damage, loot, potions) stays on the server.
    /// </para>
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private FirstPersonMotor motor;

        /// <summary>One colour per player slot, carried on tabard, plume and shield.</summary>
        public static readonly Color[] SlotColors =
        {
            new Color(0.75f, 0.12f, 0.1f),  // crimson
            new Color(0.12f, 0.3f, 0.8f),   // royal blue
            new Color(0.15f, 0.55f, 0.2f),  // forest green
            new Color(0.85f, 0.65f, 0.1f),  // gold
        };

        /// <summary>The player this machine controls, or null before spawning.</summary>
        public static NetworkPlayer Local { get; private set; }

        public HumanoidRig Rig { get; private set; }
        public Color SlotColor => SlotColors[(int)(OwnerClientId % (ulong)SlotColors.Length)];
        public Camera Camera => playerCamera;

        public Vector3 AimOrigin => playerCamera.transform.position;
        public Vector3 AimForward => playerCamera.transform.forward;

        private Health health;
        private PlayerLife life;
        private KnightKit kit;
        private CharacterArt.ViewModel viewModel;
        private ViewModelAnimator viewAnim;
        private float shake;
        private int teleportedForFloor = -1;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            kit = GetComponent<KnightKit>();
        }

        public override void OnNetworkSpawn()
        {
            Rig = CharacterArt.BuildKnight(transform, SlotColor);
            name = "Player " + OwnerClientId;

            bool isLocal = IsOwner;
            playerCamera.enabled = isLocal;
            audioListener.enabled = isLocal;

            if (isLocal)
            {
                foreach (var r in Rig.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

                viewModel = CharacterArt.BuildKnightViewModel(playerCamera.transform, SlotColor);
                viewAnim = new ViewModelAnimator(viewModel);

                Local = this;
                motor.enabled = true;
                gameObject.AddComponent<GameHud>().Init(this);
                health.Hurt += OnLocalHurt;

                DungeonDirector.FloorBuilt += HandleFloorBuilt;
                if (DungeonDirector.Layout != null) HandleFloorBuilt(DungeonDirector.Layout);
            }
            else
            {
                health.Hurt += _ => Rig.Play(Rig.Current == RigAction.None ? RigAction.Stagger : Rig.Current, 0.25f);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            motor.enabled = false;
            DungeonDirector.FloorBuilt -= HandleFloorBuilt;
        }

        private void HandleFloorBuilt(DungeonLayout layout)
        {
            // Rebuilt floors (new depth, wipe restart) move everyone back to the start
            // room. The owner places itself; NetworkTransform carries that to everyone.
            int floorKey = layout.Seed ^ (layout.Depth * 7919);
            if (teleportedForFloor == floorKey) return;
            teleportedForFloor = floorKey;

            var spawn = layout.PlayerSpawnFor(OwnerClientId);
            var look = layout.StartRoom.Center - spawn;
            look.y = 0f;
            motor.Teleport(spawn, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-look) : Quaternion.identity);
        }

        private void OnLocalHurt(float amount)
        {
            shake = Mathf.Min(1f, shake + amount / 40f);
            Sfx.Play2D(Sound.Hurt, 0.7f);
        }

        private void Update()
        {
            if (Rig != null)
            {
                Rig.Blocking = kit != null && kit.Blocking.Value;
                Rig.Downed = life != null && life.Downed.Value;
            }

            if (!IsOwner) return;

            motor.Frozen = life.Downed.Value;
            motor.CameraDrop = life.Downed.Value ? 0.9f : 0f;

            viewAnim?.Update(kit.Blocking.Value, life.Downed.Value, motor.IsSprinting);

            // Damage shake: a brief, decaying jitter on the camera — felt more than seen.
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            var cam = playerCamera.transform;
            cam.localPosition = shake > 0f ? Random.insideUnitSphere * shake * 0.06f : Vector3.zero;
        }

        // ------------------------------------------------------------------
        // View model hooks, called by the kit on the owner
        // ------------------------------------------------------------------

        public void ViewModelSwing(float windup, float rest, float side) => viewAnim?.Swing(windup, rest, side);
        public void ViewModelBash() => viewAnim?.Bash();
        public void ViewModelBlockImpact() { viewAnim?.BlockImpact(); shake = Mathf.Min(1f, shake + 0.3f); }

        /// <summary>
        /// Procedural animation for the first-person sword and shield. Plain class driven
        /// from <see cref="Update"/>; it only exists on the owner.
        /// </summary>
        private class ViewModelAnimator
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 swordRestPos, shieldRestPos;
            private readonly Quaternion swordRestRot, shieldRestRot;

            private float swingStart = -10f, swingWindup, swingRest, swingSide = 1f;
            private float bashStart = -10f, impactStart = -10f;
            private float block, bob;

            public ViewModelAnimator(CharacterArt.ViewModel vm)
            {
                this.vm = vm;
                swordRestPos = vm.SwordPivot.localPosition;
                swordRestRot = vm.SwordPivot.localRotation;
                shieldRestPos = vm.ShieldPivot.localPosition;
                shieldRestRot = vm.ShieldPivot.localRotation;
            }

            public void Swing(float windup, float rest, float side)
            {
                swingStart = Time.time; swingWindup = windup; swingRest = rest; swingSide = side;
            }

            public void Bash() => bashStart = Time.time;
            public void BlockImpact() => impactStart = Time.time;

            public void Update(bool blocking, bool downed, bool sprinting)
            {
                float dt = Time.deltaTime;
                block = Mathf.MoveTowards(block, blocking ? 1f : 0f, dt * 8f);
                bob += dt * (sprinting ? 11f : 0f);

                // --- Sword ---
                Vector3 pos = swordRestPos;
                Quaternion rot = swordRestRot;
                float t = Time.time - swingStart;

                if (t < swingWindup)
                {
                    // Draw back and up over the shoulder.
                    float k = Smooth(t / swingWindup);
                    pos += new Vector3(0.12f * swingSide, 0.18f, -0.1f) * k;
                    rot *= Quaternion.Euler(-50f * k, 0f, 45f * swingSide * k);
                }
                else if (t < swingWindup + swingRest)
                {
                    // Fast cut across the screen, then ease back to rest.
                    float s = t - swingWindup;
                    float cut = Smooth(Mathf.Clamp01(s / 0.12f));
                    float back = Smooth(Mathf.Clamp01((s - 0.12f) / Mathf.Max(0.01f, swingRest - 0.12f)));
                    var from = new Vector3(0.12f * swingSide, 0.18f, -0.1f);
                    var to = new Vector3(-0.45f * swingSide, -0.12f, 0.15f);
                    var p = Vector3.Lerp(Vector3.Lerp(from, to, cut), Vector3.zero, back);
                    pos += p;
                    var euler = Vector3.Lerp(Vector3.Lerp(new Vector3(-50f, 0f, 45f * swingSide), new Vector3(30f, -70f * swingSide, -60f * swingSide), cut), Vector3.zero, back);
                    rot *= Quaternion.Euler(euler);
                }

                if (sprinting) pos += new Vector3(Mathf.Sin(bob) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f);
                if (downed) pos += new Vector3(0f, -0.35f, -0.1f);
                vm.SwordPivot.localPosition = pos;
                vm.SwordPivot.localRotation = rot;

                // --- Shield ---
                Vector3 spos = shieldRestPos + new Vector3(0.3f, 0.14f, -0.05f) * block;
                Quaternion srot = shieldRestRot * Quaternion.Euler(0f, -18f * block, 0f);

                float b = Time.time - bashStart;
                if (b < 0.35f) spos += new Vector3(0.15f, 0.05f, 0.3f) * Mathf.Sin(b / 0.35f * Mathf.PI);

                float i = Time.time - impactStart;
                if (i < 0.2f) spos += new Vector3(0f, 0f, -0.08f) * (1f - i / 0.2f);

                if (sprinting) spos += new Vector3(Mathf.Sin(bob + 1f) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f);
                if (downed) spos += new Vector3(0f, -0.4f, -0.1f);
                vm.ShieldPivot.localPosition = spos;
                vm.ShieldPivot.localRotation = srot;
            }

            private static float Smooth(float t)
            {
                t = Mathf.Clamp01(t);
                return t * t * (3f - 2f * t);
            }
        }
    }
}
