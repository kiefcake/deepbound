using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Progression;
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
    /// Every peer builds the full model of every player's chosen class, and rebuilds it if
    /// they switch class in the lobby. On the owner the model only casts shadows (you see
    /// your shadow, not the inside of your own helmet) and a separate first-person view
    /// model — sword and shield, or staff and casting hand — is attached to the camera.
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

        /// <summary>Display names by slot, matching the slot colours.</summary>
        public static readonly string[] SlotNames = { "Crimson", "Azure", "Verdant", "Gilded" };

        /// <summary>The player this machine controls, or null before spawning.</summary>
        public static NetworkPlayer Local { get; private set; }

        public HumanoidRig Rig { get; private set; }

        /// <summary>The cloak dye this player wears (<see cref="Cosmetics.Dyes"/>); the owner sets it from its saved wardrobe.</summary>
        public readonly NetworkVariable<byte> CloakDye = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>The aura this player wears (<see cref="Cosmetics.Auras"/>): motes drifting up around them.</summary>
        public readonly NetworkVariable<byte> Aura = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private float nextMote;
        public Color SlotColor => SlotColors[(int)(OwnerClientId % (ulong)SlotColors.Length)];
        public Camera Camera => playerCamera;

        public Vector3 AimOrigin => playerCamera.transform.position;
        public Vector3 AimForward => playerCamera.transform.forward;

        private Health health;
        private PlayerLife life;
        private KnightKit knight;
        private PlayerProgress progress;
        private IClassKit[] kits;
        private CharacterArt.ViewModel viewModel;
        private IViewAnim viewAnim;
        private float shake;
        private int teleportedForArena = -1;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            knight = GetComponent<KnightKit>();
            progress = GetComponent<PlayerProgress>();
            kits = GetComponents<IClassKit>();
        }

        /// <summary>The kit for the player's current class.</summary>
        public IClassKit ActiveKit
        {
            get
            {
                foreach (var k in kits) if (k.Class == progress.ClassId.Value) return k;
                return kits[0];
            }
        }

        public override void OnNetworkSpawn()
        {
            int slot = (int)(OwnerClientId % (ulong)SlotNames.Length);
            name = SlotNames[slot] + (OwnerClientId >= (ulong)SlotNames.Length ? " " + (OwnerClientId / (ulong)SlotNames.Length + 1) : "");
            BuildVisuals();
            progress.ClassId.OnValueChanged += (_, __) => BuildVisuals();
            CloakDye.OnValueChanged += (_, __) => BuildVisuals();
            if (IsOwner)
            {
                CloakDye.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Cloak);
                Aura.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Aura);
            }
            progress.WeaponLevel.OnValueChanged += (_, __) => RefreshWeaponGlow();
            progress.Socket0.OnValueChanged += (_, __) => RefreshWeaponGlow();
            progress.Socket1.OnValueChanged += (_, __) => RefreshWeaponGlow();

            bool isLocal = IsOwner;
            playerCamera.enabled = isLocal;
            if (isLocal) playerCamera.fieldOfView = Settings.FieldOfView;
            audioListener.enabled = isLocal;

            if (isLocal)
            {
                Local = this;
                motor.enabled = true;
                gameObject.AddComponent<GameHud>().Init(this);
                gameObject.AddComponent<CharacterSelect>().Init(this);
                gameObject.AddComponent<Minimap>().Init(this);
                gameObject.AddComponent<RunSummary>();
                gameObject.AddComponent<Wardrobe>().Init(this);
                gameObject.AddComponent<AmbientMotes>();
                gameObject.AddComponent<PauseMenu>();
                health.Hurt += OnLocalHurt;

                DungeonDirector.ArenaBuilt += HandleArenaBuilt;
                if (DungeonDirector.ArenaId > 0) HandleArenaBuilt(DungeonDirector.ArenaId);
            }
            else
            {
                health.Hurt += _ => Rig.Play(Rig.Current == RigAction.None ? RigAction.Stagger : Rig.Current, 0.25f);
            }
        }

        /// <summary>(Re)builds the body and, on the owner, the view model for the current class.</summary>
        private void BuildVisuals()
        {
            if (Rig != null) Destroy(Rig.gameObject);
            if (viewModel != null) Destroy(viewModel.Root.gameObject);
            viewModel = null;
            vmRestKnown = false;
            viewAnim = null;

            var cls = progress.ClassId.Value;
            bool mage = cls == ClassId.Mage, cleric = cls == ClassId.Cleric, rogue = cls == ClassId.Rogue, ranger = cls == ClassId.Ranger;
            Rig = BuildBody(cls, transform, SlotColor, CloakDye.Value);
            MeshKit.MergeRig(Rig);

            if (!IsOwner)
            {
                RefreshWeaponGlow();
                return;
            }
            foreach (var r in Rig.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            if (mage)
            {
                viewModel = CharacterArt.BuildMageViewModel(playerCamera.transform, SlotColor);
                viewAnim = new MageViewAnimator(viewModel, new Color(1f, 0.5f, 0.15f), new Color(0.45f, 0.8f, 1f));
            }
            else if (ranger)
            {
                viewModel = CharacterArt.BuildRangerViewModel(playerCamera.transform, SlotColor);
                viewAnim = new BowViewAnimator(viewModel);
            }
            else if (rogue)
            {
                viewModel = CharacterArt.BuildRogueViewModel(playerCamera.transform, SlotColor);
                viewAnim = new DaggerViewAnimator(viewModel);
            }
            else if (cleric)
            {
                viewModel = CharacterArt.BuildClericViewModel(playerCamera.transform, SlotColor);
                viewAnim = new MageViewAnimator(viewModel, new Color(1f, 0.85f, 0.45f), new Color(1f, 0.95f, 0.75f));
            }
            else
            {
                viewModel = CharacterArt.BuildKnightViewModel(playerCamera.transform, SlotColor);
                viewAnim = new ViewModelAnimator(viewModel);
            }
            RefreshWeaponGlow();
            ViewModelLayer(viewModel.Root);
        }

        /// <summary>The layer only the view-model camera draws (so walls and bodies never cut into your hands).</summary>
        public const int ViewModelLayerIndex = 8;
        private Camera viewModelCamera;

        /// <summary>
        /// Owner: put the hands on their own layer, drawn by an overlay camera after the world with a
        /// cleared depth buffer. Swings, casts and draws then stay whole even pressed into a wall or an
        /// enemy, and a fixed field of view keeps them framed whatever FOV the player picks.
        /// </summary>
        private void ViewModelLayer(Transform root)
        {
            if (root == null || playerCamera == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ViewModelLayerIndex;
            playerCamera.cullingMask &= ~(1 << ViewModelLayerIndex);
            if (viewModelCamera != null) return;
            var go = new GameObject("ViewModelCamera");
            go.transform.SetParent(playerCamera.transform, false);
            viewModelCamera = go.AddComponent<Camera>();
            viewModelCamera.cullingMask = 1 << ViewModelLayerIndex;
            viewModelCamera.nearClipPlane = 0.01f;
            viewModelCamera.farClipPlane = 6f;
            viewModelCamera.fieldOfView = 68f;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Overlay;
            data.renderShadows = false;
            data.renderPostProcessing = true;
            var baseData = playerCamera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (baseData == null) baseData = playerCamera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (!baseData.cameraStack.Contains(viewModelCamera)) baseData.cameraStack.Add(viewModelCamera);
        }

        /// <summary>A class's third-person body, in a cloak dye (also used by the tailor's mannequin).</summary>
        public static HumanoidRig BuildBody(ClassId cls, Transform root, Color slot, int dye)
        {
            var d = Cosmetics.Get(dye);
            GothicKit.CloakOverride = d?.Cloak;
            GothicKit.TrimOverride = d?.Trim;
            try
            {
                return cls == ClassId.Mage ? CharacterArt.BuildMage(root, slot)
                    : cls == ClassId.Cleric ? CharacterArt.BuildCleric(root, slot)
                    : cls == ClassId.Rogue ? CharacterArt.BuildRogue(root, slot)
                    : cls == ClassId.Ranger ? CharacterArt.BuildRanger(root, slot)
                    : CharacterArt.BuildKnight(root, slot);
            }
            finally
            {
                GothicKit.CloakOverride = null;
                GothicKit.TrimOverride = null;
            }
        }

        /// <summary>
        /// The tempered weapon shows it: a light on the main hand that brightens with each
        /// anvil level and takes the colour of the first socketed rune (ember-orange without).
        /// </summary>
        private void RefreshWeaponGlow()
        {
            // The owner sees it on the view model; everyone else on the body's weapon hand.
            var hand = viewModel != null && viewModel.SwordPivot != null ? viewModel.SwordPivot : Rig != null ? Rig.ArmR : null;
            if (hand == null) return;
            var old = hand.Find("WeaponGlow");
            if (old != null) Destroy(old.gameObject);
            int level = progress.WeaponLevel.Value;
            var rune = progress.Socketed(0) != Loot.Rune.None ? progress.Socketed(0) : progress.Socketed(1);
            if (level == 0 && rune == Loot.Rune.None) return;

            var color = rune != Loot.Rune.None ? Loot.Runes.Color(rune) : Loot.LootInfo.EmberColor;
            bool firstPerson = hand != (Rig != null ? Rig.ArmR : null);
            var glow = new GameObject("WeaponGlow").AddComponent<Light>();
            glow.transform.SetParent(hand, false);
            glow.transform.localPosition = firstPerson ? new Vector3(0f, 0.35f, 0.15f) : new Vector3(0f, -0.7f, 0.2f);
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = firstPerson ? 1.1f : 1.8f;
            glow.intensity = 0.4f + 0.3f * level;
            glow.shadows = LightShadows.None;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            motor.enabled = false;
            DungeonDirector.ArenaBuilt -= HandleArenaBuilt;
        }

        private void HandleArenaBuilt(int arenaId)
        {
            // Every new arena — a floor, or the lobby — moves everyone to its spawn. The
            // owner places itself; NetworkTransform carries that to everyone else.
            if (teleportedForArena == arenaId) return;
            teleportedForArena = arenaId;

            var spawn = DungeonDirector.SpawnFor(OwnerClientId);
            var away = spawn - DungeonDirector.SpawnLookTarget;
            away.y = 0f;
            // Face outward from the start room's centre (toward the doors), but toward the
            // fire in the lobby.
            var facing = DungeonDirector.InLobby ? -away : away;
            motor.Teleport(spawn, facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
        }

        private Vector3 lastStepPos;
        private float stepDistance;

        /// <summary>Your own footsteps: a step every ~1.6m walking, splashing on flooded floors.</summary>
        private void Footsteps()
        {
            var p = transform.position;
            var d = p - lastStepPos;
            lastStepPos = p;
            d.y = 0f;
            if (d.magnitude > 2f || life.Downed.Value) return; // teleport
            stepDistance += d.magnitude;
            float stride = motor.IsSprinting ? 2.1f : 1.6f;
            if (stepDistance < stride) return;
            stepDistance = 0f;
            bool wet = DungeonDirector.Layout != null && DungeonTheme.ForDepth(DungeonDirector.Layout.Depth).WaterDepth > 0f;
            Sfx.Play(wet ? Sound.Splash : Sound.Step, p, wet ? 0.35f : 0.25f);
        }

        /// <summary>Server → owner: you fell; climb out here.</summary>
        [Rpc(SendTo.Owner)]
        public void RescueRpc(Vector3 at)
        {
            motor.Teleport(at, transform.rotation);
            UI.GameHud.Feed("You climb out of the depths, battered.", new Color(1f, 0.5f, 0.4f));
        }

        private void OnLocalHurt(float amount)
        {
            shake = Mathf.Min(1f, shake + amount / 40f);
            Sfx.Play2D(Sound.Hurt, 0.7f);
        }

        private void Update()
        {
            // Draw no further than the fog lets anyone see: beyond ~2.6/density (exp-squared fog) it is solid,
            // and with no occlusion culling everything past the walls would otherwise still be drawn.
            if (IsOwner && playerCamera != null)
                playerCamera.farClipPlane = DungeonDirector.InLobby ? 220f : Mathf.Clamp(2.6f / Mathf.Max(0.001f, RenderSettings.fogDensity), 45f, 120f);
            if (Rig != null)
            {
                Rig.Blocking = knight != null && knight.Blocking.Value;
                Rig.Downed = life != null && life.Downed.Value;
            }

            // A cosmetic aura; not while hidden in smoke, and sparser on your own screen.
            var aura = Cosmetics.AuraColor(Aura.Value);
            if (aura.HasValue && Time.time >= nextMote && life != null && !life.Hidden.Value && !life.Downed.Value)
            {
                nextMote = Time.time + (IsOwner ? 0.4f : 0.09f);
                Vfx.Mote(transform.position, aura.Value);
            }

            if (!IsOwner) return;
            Footsteps();

            motor.Frozen = life.Downed.Value || UI.GameHud.CutsceneFreeze;
            motor.CameraDrop = life.Downed.Value ? 0.9f : 0f;

            viewAnim?.SetCharge(ActiveKit.Charge);
            viewAnim?.Update(knight.Blocking.Value, life.Downed.Value, motor.IsSprinting);

            // Damage shake: a brief, decaying jitter on the camera — felt more than seen.
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            var cam = playerCamera.transform;
            var shakeOffset = shake > 0f ? Random.insideUnitSphere * shake * 0.06f * Settings.ShakeScale : Vector3.zero;
            cam.localPosition = shakeOffset + FirstPersonFeel(cam);
        }

        // ---- First-person feel: walk bob, weapon lag on turns, landing dip, slide lean. ----
        private Vector3 vmRestPos;
        private Quaternion vmRestRot = Quaternion.identity;
        private bool vmRestKnown, wasGrounded = true;
        private float walkPhase, landDip, lastVy, lastYaw, lastPitch, slideLean, downRoll, downTilt;
        private Vector2 sway;

        /// <summary>Owner: animate the view model and return the camera's bob offset.</summary>
        private Vector3 FirstPersonFeel(Transform cam)
        {
            float dt = Time.deltaTime;
            var cc = GetComponent<CharacterController>();
            var vel = cc != null ? cc.velocity : Vector3.zero;
            float speed = new Vector2(vel.x, vel.z).magnitude;
            bool grounded = cc == null || cc.isGrounded;
            if (grounded && !wasGrounded && lastVy < -4f) landDip = Mathf.Min(1f, -lastVy / 12f);
            wasGrounded = grounded;
            lastVy = vel.y;
            landDip = Mathf.MoveTowards(landDip, 0f, dt * 3f);
            bool sliding = motor.IsSliding;
            if (grounded && !sliding) walkPhase += dt * speed * 1.7f;
            float amp = Mathf.Clamp01(speed / 5f) * (grounded && !sliding ? 1f : 0f);

            // The weapon trails a fast turn and settles back.
            float yaw = transform.eulerAngles.y, pitch = cam.parent != null ? cam.parent.localEulerAngles.x : 0f;
            float dYaw = Mathf.DeltaAngle(lastYaw, yaw), dPitch = Mathf.DeltaAngle(lastPitch, pitch);
            lastYaw = yaw;
            lastPitch = pitch;
            var want = new Vector2(Mathf.Clamp(dPitch * 0.5f, -5f, 5f), Mathf.Clamp(-dYaw * 0.5f, -6f, 6f));
            sway = Vector2.Lerp(sway, want, dt * 9f);

            if (viewModel != null && viewModel.Root != null)
            {
                if (!vmRestKnown) { vmRestPos = viewModel.Root.localPosition; vmRestRot = viewModel.Root.localRotation; vmRestKnown = true; }
                var bob = new Vector3(Mathf.Sin(walkPhase) * 0.011f, -Mathf.Abs(Mathf.Cos(walkPhase)) * 0.013f, 0f) * amp;
                // Standing still the hands breathe: a slow rise and a faint drift.
                float still = 1f - amp, tb = Time.time;
                bob += new Vector3(Mathf.Sin(tb * 0.7f) * 0.003f, Mathf.Sin(tb * 1.5f) * 0.004f, Mathf.Sin(tb * 1.5f + 0.6f) * 0.002f) * still;
                viewModel.Root.localPosition = vmRestPos + bob + Vector3.down * 0.07f * landDip + Vector3.down * (sliding ? 0.04f : 0f);
                viewModel.Root.localRotation = vmRestRot * Quaternion.Euler(sway.x, sway.y, sway.y * 0.6f);
            }

            // A slide leans the view; walking nods it a little (scaled with the shake option).
            slideLean = Mathf.MoveTowards(slideLean, sliding ? 7f : 0f, dt * 40f);
            // Downed: you crumple — the view rolls onto its side and tips up at whatever stands over you.
            bool down = life != null && life.Downed.Value;
            downRoll = Mathf.MoveTowards(downRoll, down ? 24f : 0f, dt * (down ? 60f : 90f));
            downTilt = Mathf.MoveTowards(downTilt, down ? 18f : 0f, dt * (down ? 40f : 80f));
            cam.localRotation = Quaternion.Euler(-downTilt, 0f, slideLean + downRoll);
            return (Vector3.up * Mathf.Sin(walkPhase * 2f) * 0.022f * amp + Vector3.down * 0.12f * landDip) * Settings.ShakeScale;
        }

        // ------------------------------------------------------------------
        // View model hooks, called by the kit on the owner
        // ------------------------------------------------------------------

        public void ViewModelSwing(float windup, float rest, float side) => viewAnim?.Swing(windup, rest, side);
        public void ViewModelBash() => viewAnim?.Bash();

        /// <summary>Owner: shake the camera (explosions, breaking walls, meteor strikes).</summary>
        public void AddShake(float amount) => shake = Mathf.Min(1f, shake + amount);
        public void ViewModelBlockImpact() { viewAnim?.BlockImpact(); shake = Mathf.Min(1f, shake + 0.3f); }
        public void ViewModelCast(bool frost) => viewAnim?.Cast(frost);

        /// <summary>
        /// Owner: your hit connected. A tiny camera kick sells the impact without
        /// touching time scale (which, on a host, would slow the whole server).
        /// </summary>
        public void ConfirmHit(float strength)
        {
            shake = Mathf.Min(1f, shake + 0.12f * strength);
        }

        /// <summary>One first-person animator per class; the kit calls whichever hooks apply.</summary>
        private interface IViewAnim
        {
            void Update(bool blocking, bool downed, bool sprinting);
            void Swing(float windup, float rest, float side);
            void Bash();
            void BlockImpact();
            void Cast(bool frost);
            void SetCharge(float charge);
        }

        /// <summary>
        /// The Ranger's hands: the draw hand pulls back to the cheek as the bow charges,
        /// snapping forward on release; a full draw makes the bow tremble.
        /// </summary>
        private class BowViewAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 drawRest, bowRest;
            private readonly Quaternion bowRot;
            private float charge, release = -10f, bob;

            public BowViewAnimator(CharacterArt.ViewModel vm)
            {
                this.vm = vm;
                drawRest = vm.SwordPivot.localPosition;
                bowRest = vm.ShieldPivot.localPosition;
                bowRot = vm.ShieldPivot.localRotation;
            }

            public void SetCharge(float c) => charge = c;
            public void Swing(float windup, float rest, float side) { }
            public void Bash() { }
            public void BlockImpact() { }
            public void Cast(bool full) => release = Time.time;

            public void Update(bool blocking, bool downed, bool sprinting)
            {
                bob += Time.deltaTime * (sprinting ? 11f : 0f);
                var run = sprinting ? new Vector3(Mathf.Sin(bob) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f) : Vector3.zero;
                var down = downed ? new Vector3(0f, -0.4f, -0.1f) : Vector3.zero;
                float snap = Time.time - release < 0.15f ? 1f - (Time.time - release) / 0.15f : 0f;
                float tremble = charge >= 1f ? Mathf.Sin(Time.time * 60f) * 0.003f : 0f;

                // Raised to aim while drawing, lowered at rest.
                var aimLift = new Vector3(0.12f, 0.1f, 0f) * Mathf.Clamp01(charge * 3f);
                vm.ShieldPivot.localPosition = bowRest + run + down + aimLift + new Vector3(tremble, tremble, -0.03f * snap);
                vm.ShieldPivot.localRotation = bowRot;
                vm.SwordPivot.localPosition = drawRest + run + down + aimLift + new Vector3(0f, 0f, -0.22f * charge + 0.08f * snap);
            }
        }

        /// <summary>
        /// The Rogue's hands: alternate quick forward stabs, a flick for thrown knives.
        /// </summary>
        private class DaggerViewAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 rRest, lRest;
            private readonly Quaternion rRot, lRot;
            private float rStab = -10f, lStab = -10f, throwAt = -10f, bob;

            public DaggerViewAnimator(CharacterArt.ViewModel vm)
            {
                this.vm = vm;
                rRest = vm.SwordPivot.localPosition; rRot = vm.SwordPivot.localRotation;
                lRest = vm.ShieldPivot.localPosition; lRot = vm.ShieldPivot.localRotation;
            }

            public void Swing(float windup, float rest, float side)
            {
                if (side > 0f) rStab = Time.time; else lStab = Time.time;
            }

            public void Bash() { }
            public void BlockImpact() { }
            public void Cast(bool frost) => throwAt = Time.time;
            public void SetCharge(float charge) { }

            public void Update(bool blocking, bool downed, bool sprinting)
            {
                bob += Time.deltaTime * (sprinting ? 12f : 0f);
                var run = sprinting ? new Vector3(Mathf.Sin(bob) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f) : Vector3.zero;
                var down = downed ? new Vector3(0f, -0.4f, -0.1f) : Vector3.zero;

                float r = Stab(rStab), l = Stab(lStab);
                float t = Time.time - throwAt;
                float flick = t < 0.25f ? Mathf.Sin(t / 0.25f * Mathf.PI) : 0f;

                vm.SwordPivot.localPosition = rRest + run + down + new Vector3(-0.08f, 0.04f, 0.28f) * r + new Vector3(0f, 0.12f, 0.1f) * flick;
                vm.SwordPivot.localRotation = rRot * Quaternion.Euler(-45f * r - 60f * flick, 0f, 0f);
                vm.ShieldPivot.localPosition = lRest + run + down + new Vector3(0.08f, 0.04f, 0.28f) * l;
                vm.ShieldPivot.localRotation = lRot * Quaternion.Euler(-45f * l, 0f, 0f);
            }

            private static float Stab(float start)
            {
                float t = Time.time - start;
                return t < 0.2f ? Mathf.Sin(t / 0.2f * Mathf.PI) : 0f;
            }
        }

        /// <summary>
        /// The Mage's hands: the staff sways and dips; the casting hand thrusts forward on
        /// each spell, its palm flaring orange for fire or blue for frost.
        /// </summary>
        private class MageViewAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 staffRest, handRest;
            private readonly Quaternion staffRot, handRot;
            private float castStart = -10f, impactStart = -10f, bob, idle;
            private bool lastFrost;

            // Primary and secondary spell colours: fire and frost for the Mage, gold and white for the Cleric.
            private readonly Color Fire, Frost;

            public MageViewAnimator(CharacterArt.ViewModel vm, Color primary, Color secondary)
            {
                this.vm = vm;
                Fire = primary;
                Frost = secondary;
                staffRest = vm.SwordPivot.localPosition;
                staffRot = vm.SwordPivot.localRotation;
                handRest = vm.ShieldPivot.localPosition;
                handRot = vm.ShieldPivot.localRotation;
            }

            public void Swing(float windup, float rest, float side) { }
            public void Bash() { }
            public void SetCharge(float charge) { }
            public void BlockImpact() => impactStart = Time.time;

            public void Cast(bool frost)
            {
                castStart = Time.time;
                lastFrost = frost;
            }

            public void Update(bool blocking, bool downed, bool sprinting)
            {
                bob += Time.deltaTime * (sprinting ? 11f : 0f);
                idle += Time.deltaTime;

                float c = Time.time - castStart;
                float thrust = c < 0.25f ? Mathf.Sin(c / 0.25f * Mathf.PI) : 0f;
                float hit = Time.time - impactStart < 0.2f ? 1f - (Time.time - impactStart) / 0.2f : 0f;

                var sway = new Vector3(Mathf.Sin(idle * 1.3f) * 0.008f, Mathf.Sin(idle * 2.1f) * 0.006f, 0f);
                var run = sprinting ? new Vector3(Mathf.Sin(bob) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f) : Vector3.zero;
                var down = downed ? new Vector3(0f, -0.4f, -0.1f) : Vector3.zero;

                vm.SwordPivot.localPosition = staffRest + sway + run + down + new Vector3(0f, 0f, -0.06f) * hit + new Vector3(-0.02f, 0.02f, 0.05f) * thrust;
                vm.SwordPivot.localRotation = staffRot * Quaternion.Euler(-10f * thrust, 0f, 0f);

                vm.ShieldPivot.localPosition = handRest + run + down + new Vector3(0.12f, 0.06f, 0.22f) * thrust - sway;
                vm.ShieldPivot.localRotation = handRot * Quaternion.Euler(-35f * thrust, 0f, 0f);

                if (vm.SpellGlow != null)
                {
                    var color = lastFrost ? Frost : Fire;
                    // Kept under ~2x HDR so bloom tints it instead of whiting it out.
                    float flick = Mathf.PerlinNoise(idle * 5f, 0.3f) * 0.3f;
                    ArtLibrary.SetGlow(vm.SpellGlow, color * (0.8f + 0.9f * thrust + flick));
                    vm.SpellGlow.transform.localScale = Vector3.one * (0.9f + flick * 0.5f + thrust * 0.5f);
                    vm.SpellGlow.transform.localRotation = Quaternion.Euler(0f, idle * 140f, 0f);
                    if (vm.SpellLight != null)
                    {
                        vm.SpellLight.color = color;
                        vm.SpellLight.intensity = 0.5f + flick + thrust * 1.6f;
                    }
                }
            }
        }

        /// <summary>
        /// Procedural animation for the first-person sword and shield. Plain class driven
        /// from <see cref="Update"/>; it only exists on the owner.
        /// </summary>
        private class ViewModelAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 swordRestPos, shieldRestPos;
            private readonly Quaternion swordRestRot, shieldRestRot;

            private float swingStart = -10f, swingWindup, swingRest, swingSide = 1f;
            private float bashStart = -10f, impactStart = -10f;
            private float block, bob;
            private MaterialPropertyBlock slashBlock;

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
            public void Cast(bool frost) { }
            public void SetCharge(float charge) { }

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

                // The slash arc wipes across with the cut and fades.
                if (vm.Slash != null)
                {
                    float c = (t - swingWindup) / 0.2f;
                    bool on = c >= 0f && c < 1f;
                    vm.Slash.enabled = on;
                    if (on)
                    {
                        var pivot = vm.Slash.transform.parent;
                        pivot.localScale = new Vector3(swingSide, 1f, 1f);
                        pivot.localRotation = Quaternion.Euler(0f, 0f, 40f + Mathf.Lerp(-35f, 35f, Smooth(c)));
                        slashBlock ??= new MaterialPropertyBlock();
                        vm.Slash.GetPropertyBlock(slashBlock);
                        float alpha = Mathf.Sin(c * Mathf.PI) * 0.38f;
                        slashBlock.SetColor("_BaseColor", new Color(1f, 0.93f, 0.8f, alpha));
                        vm.Slash.SetPropertyBlock(slashBlock);
                    }
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
