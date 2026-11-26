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
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private FirstPersonMotor motor;

        public static readonly Color[] SlotColors =
        {
            new Color(0.75f, 0.12f, 0.1f),
            new Color(0.12f, 0.3f, 0.8f),
            new Color(0.15f, 0.55f, 0.2f),
            new Color(0.85f, 0.65f, 0.1f),
        };

        public static readonly string[] SlotNames = { "Crimson", "Azure", "Verdant", "Gilded" };

        public static NetworkPlayer Local { get; private set; }

        public HumanoidRig Rig { get; private set; }

        public readonly NetworkVariable<byte> CloakDye = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> SteamName = new NetworkVariable<Unity.Collections.FixedString64Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<byte> Aura = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<byte> Heraldry = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<byte> Pattern = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<byte> RegaliaCos = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<byte> WeaponGlowCos = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<byte> TitleCos = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<byte> PetKind = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<ushort> LegacyBits = new NetworkVariable<ushort>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public void RefreshLegacy() { if (IsOwner && IsSpawned) LegacyBits.Value = Legacy.Bits; }

        private static Unity.Collections.FixedString64Bytes SafeName(string n)
        {
            n = (n ?? "").Trim();
            if (n.Length > 24) n = n.Substring(0, 24);
            while (n.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(n) > 60) n = n.Substring(0, n.Length - 1);
            return new Unity.Collections.FixedString64Bytes(n);
        }

        public string NameWithTitle => Cosmetics.TitleOf(TitleCos.Value) is string t ? name + " " + t : name;

        public Color TrailColor(Color own) => Cosmetics.WeaponGlowColor(WeaponGlowCos.Value) is Color g ? g * 1.15f : own;

        public Color DressColor => Cosmetics.HeraldryColor(Heraldry.Value) ?? SlotColor;
        private float nextMote;
        public Color SlotColor => SlotColors[(int)(OwnerClientId % (ulong)SlotColors.Length)];
        public Camera Camera => playerCamera;

        public Vector3 AimOrigin => playerCamera.transform.position;

        public Vector3 FxHand => IsOwner && playerCamera != null ? AimOrigin + AimForward * 1.1f - Vector3.up * 0.3f + playerCamera.transform.right * 0.2f : transform.position + Vector3.up * 1.35f + transform.forward * 0.45f;

        public float FxHandScale => IsOwner ? 0.4f : 1f;
        public Vector3 AimForward => playerCamera.transform.forward;
        private bool selfShown;
        private HumanoidRig selfShownRig;
        private PlayerDebuffs debuffsRef;

        public int Drunk => (debuffsRef != null ? debuffsRef : (debuffsRef = GetComponent<PlayerDebuffs>())) is PlayerDebuffs d ? d.Drunk : 0;

        public bool AimRay(float range, out RaycastHit hit)
        {
            hit = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (var h in Physics.RaycastAll(AimOrigin, AimForward, range, ~(1 << ViewModelLayerIndex), QueryTriggerInteraction.Ignore))
            {
                if (h.distance >= best || h.collider.transform.IsChildOf(transform)) continue;
                best = h.distance;
                hit = h;
                found = true;
            }
            return found;
        }

        public bool TryAimFloor(float range, out Vector3 spot)
        {
            spot = default;
            var dir = AimForward;
            var point = AimRay(range, out var hit) ? hit.point : AimOrigin + dir * range;

            var from = point - dir * 0.35f + Vector3.up * 0.05f;
            foreach (var h in Physics.RaycastAll(from, Vector3.down, 40f, ~(1 << ViewModelLayerIndex), QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<Health>() != null || h.normal.y < 0.6f) continue;
                if (spot == default || h.point.y > spot.y) spot = h.point;
            }
            if (spot == default) return false;

            if (spot.y > transform.position.y + 5f) return false;
            return true;
        }

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
            if (SteamName.Value.Length > 0) name = SteamName.Value.ToString();

            if (IsOwner) localSpawnedAt = Time.time;
            else if (Local != null && Time.time - localSpawnedAt > 3f) UI.GameHud.Feed("A friend joined the party.", new Color(0.7f, 0.9f, 0.7f));
            SteamName.OnValueChanged += (_, now) => { if (now.Length > 0) name = now.ToString(); };
            if (IsOwner && Networking.SteamPlatform.Ready) SteamName.Value = SafeName(Networking.SteamPlatform.MyName);
            BuildVisuals();
            progress.ClassId.OnValueChanged += (_, __) => BuildVisuals();
            CloakDye.OnValueChanged += (_, __) => BuildVisuals();
            Heraldry.OnValueChanged += (_, __) => BuildVisuals();
            Pattern.OnValueChanged += (_, __) => BuildVisuals();
            RegaliaCos.OnValueChanged += (_, __) => BuildVisuals();
            if (IsOwner)
            {
                CloakDye.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Cloak);
                Aura.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Aura);
                Heraldry.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Heraldry);
                Pattern.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Pattern);
                RegaliaCos.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Regalia);
                WeaponGlowCos.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.WeaponGlow);
                TitleCos.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Title);
                PetKind.Value = (byte)Cosmetics.Worn(Cosmetics.Kind.Pet);
                LegacyBits.Value = Legacy.Bits;
            }
            gameObject.AddComponent<PetCompanion>().Init(this);
            gameObject.AddComponent<PetPerks>().Init(this);
            gameObject.AddComponent<ToolBelt>().Init(this);
            gameObject.AddComponent<CampSeat>().Init(this);
            gameObject.AddComponent<UI.Dialogue>().Init(this);
            WeaponGlowCos.OnValueChanged += (_, __) => RefreshWeaponGlow();
            progress.WeaponLevel.OnValueChanged += (_, __) => RefreshWeaponGlow();
            progress.Socket0.OnValueChanged += (_, __) => RefreshWeaponGlow();
            progress.Socket1.OnValueChanged += (_, __) => RefreshWeaponGlow();
            progress.Socket2.OnValueChanged += (_, __) => RefreshWeaponGlow();

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
                gameObject.AddComponent<ChatBox>();
                gameObject.AddComponent<DevMenu>();
                health.Hurt += OnLocalHurt;

                DungeonDirector.ArenaBuilt += HandleArenaBuilt;
                if (DungeonDirector.ArenaId > 0) HandleArenaBuilt(DungeonDirector.ArenaId);
            }
            else
            {
                health.Hurt += _ => Rig.Play(Rig.Current == RigAction.None ? RigAction.Stagger : Rig.Current, 0.25f);
            }
        }

        private void BuildVisuals()
        {
            if (Rig != null) Destroy(Rig.gameObject);
            if (viewModel != null) Destroy(viewModel.Root.gameObject);
            viewModel = null;
            vmRestKnown = false;
            viewAnim = null;
            vmRaisedAt = Time.time;

            var cls = progress.ClassId.Value;
            bool mage = cls == ClassId.Mage, cleric = cls == ClassId.Cleric, rogue = cls == ClassId.Rogue, ranger = cls == ClassId.Ranger;
            Rig = BuildBody(cls, transform, DressColor, CloakDye.Value, Pattern.Value, RegaliaCos.Value);
            MeshKit.MergeRig(Rig);

            if (!IsOwner)
            {
                RefreshWeaponGlow();
                return;
            }
            foreach (var r in Rig.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

            if (mage)
            {
                viewModel = CharacterArt.BuildMageViewModel(playerCamera.transform, DressColor);
                viewAnim = new MageViewAnimator(viewModel, new Color(1f, 0.5f, 0.15f), new Color(0.45f, 0.8f, 1f));
            }
            else if (ranger)
            {
                viewModel = CharacterArt.BuildRangerViewModel(playerCamera.transform, DressColor);
                viewAnim = new BowViewAnimator(viewModel);
            }
            else if (rogue)
            {
                viewModel = CharacterArt.BuildRogueViewModel(playerCamera.transform, DressColor);
                viewAnim = new DaggerViewAnimator(viewModel);
            }
            else if (cls == ClassId.Flagellant)
            {
                viewModel = CharacterArt.BuildFlagellantViewModel(playerCamera.transform, DressColor);
                viewAnim = new ViewModelAnimator(viewModel);
            }
            else if (cls == ClassId.PlagueDoctor)
            {
                viewModel = CharacterArt.BuildPlagueDoctorViewModel(playerCamera.transform, DressColor);
                viewAnim = new MageViewAnimator(viewModel, new Color(0.55f, 1f, 0.35f), new Color(1f, 0.55f, 0.6f));
            }
            else if (cleric)
            {
                viewModel = CharacterArt.BuildClericViewModel(playerCamera.transform, DressColor);
                viewAnim = new MageViewAnimator(viewModel, new Color(1f, 0.85f, 0.45f), new Color(1f, 0.95f, 0.75f));
            }
            else
            {
                viewModel = CharacterArt.BuildKnightViewModel(playerCamera.transform, DressColor);
                viewAnim = new ViewModelAnimator(viewModel);
            }
            RefreshWeaponGlow();
            ViewModelLayer(viewModel.Root);
        }

        public const int ViewModelLayerIndex = 8;
        private Camera viewModelCamera;

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

        public static bool DebugShowSelf;

        public static HumanoidRig BuildBody(ClassId cls, Transform root, Color slot, int dye, int pattern = 0, int regalia = 0)
        {
            var d = Cosmetics.Get(dye);
            GothicKit.CloakOverride = d?.Cloak;
            GothicKit.TrimOverride = d?.Trim;
            GothicKit.PatternOverride = Cosmetics.PatternOf(pattern);
            try
            {
                var rig = cls == ClassId.Mage ? CharacterArt.BuildMage(root, slot)
                    : cls == ClassId.Cleric ? CharacterArt.BuildCleric(root, slot)
                    : cls == ClassId.Rogue ? CharacterArt.BuildRogue(root, slot)
                    : cls == ClassId.Ranger ? CharacterArt.BuildRanger(root, slot)
                    : cls == ClassId.Flagellant ? CharacterArt.BuildFlagellant(root, slot)
                    : cls == ClassId.PlagueDoctor ? CharacterArt.BuildPlagueDoctor(root, slot)
                    : CharacterArt.BuildKnight(root, slot);
                Art.Regalia.Dress(rig, cls, regalia);
                return rig;
            }
            finally
            {
                GothicKit.CloakOverride = null;
                GothicKit.TrimOverride = null;
                GothicKit.PatternOverride = null;
            }
        }

        private void RefreshWeaponGlow()
        {
            var hand = viewModel != null && viewModel.SwordPivot != null ? viewModel.SwordPivot : Rig != null ? Rig.ArmR : null;
            if (hand == null) return;
            var old = hand.Find("WeaponGlow");
            if (old != null) Destroy(old.gameObject);
            int level = progress.WeaponLevel.Value;
            var rune = progress.Socketed(0) != Loot.Rune.None ? progress.Socketed(0) : progress.Socketed(1) != Loot.Rune.None ? progress.Socketed(1) : progress.Socketed(2);
            var oldEdge = hand.Find("CosmeticEdge");
            if (oldEdge != null) Destroy(oldEdge.gameObject);
            var cosmetic = Cosmetics.WeaponGlowColor(WeaponGlowCos.Value);
            bool firstPerson = hand != (Rig != null ? Rig.ArmR : null);
            if (cosmetic.HasValue) BuildCosmeticEdge(hand, cosmetic.Value, firstPerson);
            if (level == 0 && rune == Loot.Rune.None && !cosmetic.HasValue) return;

            var color = cosmetic ?? (rune != Loot.Rune.None ? Loot.Runes.Color(rune) : Loot.LootInfo.EmberColor);
            var glow = new GameObject("WeaponGlow").AddComponent<Light>();
            glow.transform.SetParent(hand, false);
            glow.transform.localPosition = firstPerson ? new Vector3(0f, 0.35f, 0.15f) : new Vector3(0f, -0.7f, 0.2f);
            glow.type = LightType.Point;
            glow.color = color;
            glow.range = firstPerson ? 1.1f : 1.8f;
            glow.intensity = 0.4f + 0.3f * level + (cosmetic.HasValue ? 0.4f : 0f);
            glow.shadows = LightShadows.None;
        }

        private void BuildCosmeticEdge(Transform hand, Color c, bool firstPerson)
        {
            var root = MeshKit.Pivot(hand, "CosmeticEdge", Vector3.zero);
            if (firstPerson && viewModel != null && viewModel.TipR != null && viewModel.EdgeR != null && viewModel.TipR.parent != null)
            {
                var parent = viewModel.TipR.parent;
                root.SetParent(parent, false);
                var a = viewModel.EdgeR.localPosition; var b = viewModel.TipR.localPosition;
                var edge = MeshKit.Part(root, "Edge", MeshKit.Box(new Vector3(0.012f, 1f, 0.012f)), Mat.Glow, (a + b) / 2f);
                edge.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
                edge.transform.localScale = new Vector3(1f, (b - a).magnitude * 1.15f, 1f);
                ArtLibrary.SetGlow(edge.GetComponent<Renderer>(), c * 1.4f);
                ViewModelLayer(root);
            }
            else if (!firstPerson)
            {
                root.gameObject.AddComponent<WeaponMotes>().Color = c;
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static float localSpawnedAt;

        public override void OnNetworkDespawn()
        {
            if (!IsOwner && Local != null && NetworkManager != null && !NetworkManager.ShutdownInProgress) UI.GameHud.Feed(name + " left the party.", new Color(1f, 0.8f, 0.45f));
            if (Local == this) Local = null;
            motor.enabled = false;
            DungeonDirector.ArenaBuilt -= HandleArenaBuilt;
        }

        private void HandleArenaBuilt(int arenaId)
        {
            if (teleportedForArena == arenaId) return;
            teleportedForArena = arenaId;

            var spawn = DungeonDirector.SpawnFor(OwnerClientId);
            var away = spawn - DungeonDirector.SpawnLookTarget;
            away.y = 0f;

            var facing = DungeonDirector.InLobby ? -away : away;
            motor.Teleport(spawn, facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
        }

        private Vector3 lastStepPos;
        private float stepDistance;

        private void Footsteps()
        {
            var p = transform.position;
            var d = p - lastStepPos;
            lastStepPos = p;
            d.y = 0f;
            if (d.magnitude > 2f || life.Downed.Value) return;
            stepDistance += d.magnitude;
            float stride = motor.IsSprinting ? 2.1f : 1.6f;
            if (stepDistance < stride) return;
            stepDistance = 0f;
            bool wet = DungeonDirector.Layout != null && !DungeonDirector.InLobby && (DungeonTheme.ForDepth(DungeonDirector.Layout.Depth).WaterDepth > 0f || DungeonDirector.InWater(p));

            Sfx.Play(wet ? Sound.Splash : DungeonDirector.InLobby ? Sound.StepSoft : Sound.Step, p, wet ? 0.35f : 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void PetCueRpc(byte cue, Vector3 at) { if (TryGetComponent<PetPerks>(out var pp)) pp.Show((PetPerks.Cue)cue, at); }

        public const float UnstuckCooldown = 8f;
        private float unstuckAt = -99f;

        [Rpc(SendTo.Server)]
        public void UnstuckRpc() => ServerUnstuck();

        public bool ServerUnstuck()
        {
            if (!IsServer || life == null || life.Downed.Value || Time.time - unstuckAt < UnstuckCooldown) return false;
            unstuckAt = Time.time;
            var pos = transform.position;
            Vector3 to;
            if (DungeonDirector.InLobby || DungeonDirector.Layout == null) to = Lobby.SpawnFor(OwnerClientId);
            else
            {
                var room = DungeonDirector.Layout.RoomAt(pos, 0.5f);
                if (room != null) to = DropSpots.Stand(room.Center, pos, 0.45f);
                else if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var h, 8f, UnityEngine.AI.NavMesh.AllAreas)) to = h.position;
                else to = DungeonDirector.Layout.PlayerSpawn;
            }
            ShoveRpc(to + Vector3.up * 0.1f);
            return true;
        }

        [Rpc(SendTo.Owner)]
        public void ShoveRpc(Vector3 at) => motor.Teleport(at, transform.rotation);

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
            bool hideHands = GameHud.Cinematic || GameHud.PhotoMode || Teamwork.Watching != null || Emotes.LocalShowing || World.BlackjackTable.LocalOpen || World.PokerDiceTable.LocalSeated || CampSeat.SeatedLocal;
            if (IsOwner && viewModel != null && viewModel.Root.gameObject.activeSelf == hideHands) viewModel.Root.gameObject.SetActive(!hideHands);

            bool showSelf = Teamwork.Watching != null || DebugShowSelf || Emotes.LocalShowing;
            if (IsOwner && Rig != null && (showSelf != selfShown || Rig != selfShownRig))
            {
                selfShown = showSelf;
                selfShownRig = Rig;
                foreach (var r in Rig.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = showSelf ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            }
            if (IsOwner && playerCamera != null)
                playerCamera.farClipPlane = DungeonDirector.InLobby ? 220f : Mathf.Clamp(2.6f / Mathf.Max(0.001f, RenderSettings.fogDensity), 45f, 120f);
            if (Rig != null)
            {
                Rig.Drunk = Drunk;
                Rig.Blocking = knight != null && knight.Blocking.Value;
                Rig.Downed = life != null && life.Downed.Value;
            }

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

            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            var cam = playerCamera.transform;
            var shakeOffset = shake > 0f ? Random.insideUnitSphere * shake * 0.06f * Settings.ShakeScale : Vector3.zero;
            cam.localPosition = shakeOffset + FirstPersonFeel(cam);
        }

        private Vector3 vmRestPos;
        private Quaternion vmRestRot = Quaternion.identity;
        private bool vmRestKnown, wasGrounded = true;
        private float walkPhase, landDip, lastVy, lastYaw, lastPitch, slideLean, downRoll, downTilt, drunkSway;
        private Vector2 sway, swayVel;

        private float strafeRoll, airLift, sprintPose, vmRaisedAt = -10f, punch;
        private const float RaiseSeconds = 0.5f;

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

            float yaw = transform.eulerAngles.y, pitch = cam.parent != null ? cam.parent.localEulerAngles.x : 0f;
            float dYaw = Mathf.DeltaAngle(lastYaw, yaw), dPitch = Mathf.DeltaAngle(lastPitch, pitch);
            lastYaw = yaw;
            lastPitch = pitch;
            var want = new Vector2(Mathf.Clamp(dPitch * 0.5f, -5f, 5f), Mathf.Clamp(-dYaw * 0.5f, -6f, 6f));

            for (float left = Mathf.Min(dt, 0.1f); left > 0f; left -= 1f / 120f)
            {
                float h = Mathf.Min(left, 1f / 120f);
                swayVel += ((want - sway) * 170f - swayVel * 15f) * h;
                sway += swayVel * h;
            }
            sway = Vector2.ClampMagnitude(sway, 9f);

            var localVel = transform.InverseTransformDirection(vel);
            strafeRoll = Mathf.Lerp(strafeRoll, Mathf.Clamp(-localVel.x * 1.1f, -5f, 5f), dt * 8f);
            airLift = Mathf.Lerp(airLift, grounded ? 0f : Mathf.Clamp(-vel.y * 0.006f, -0.05f, 0.05f), dt * 10f);
            sprintPose = Mathf.MoveTowards(sprintPose, motor.IsSprinting && grounded && speed > 3f ? 1f : 0f, dt * 5f);
            punch = Mathf.MoveTowards(punch, 0f, dt * 7f);

            if (viewModel != null && viewModel.Root != null)
            {
                if (!vmRestKnown) { vmRestPos = viewModel.Root.localPosition; vmRestRot = viewModel.Root.localRotation; vmRestKnown = true; }
                var bob = new Vector3(Mathf.Sin(walkPhase) * 0.011f, -Mathf.Abs(Mathf.Cos(walkPhase)) * 0.013f, 0f) * amp;

                float still = 1f - amp, tb = Time.time;
                bob += new Vector3(Mathf.Sin(tb * 0.7f) * 0.003f, Mathf.Sin(tb * 1.5f) * 0.004f, Mathf.Sin(tb * 1.5f + 0.6f) * 0.002f) * still;

                float raise = Mathf.Clamp01((Time.time - vmRaisedAt) / RaiseSeconds);
                raise = 1f - (1f - raise) * (1f - raise) * (1f - raise);
                var lag = new Vector3(-sway.y * 0.0022f, sway.x * 0.0018f, 0f);
                var carry = new Vector3(0.03f, -0.06f, -0.03f) * sprintPose;
                viewModel.Root.localPosition = vmRestPos + bob + lag + carry + Vector3.up * airLift + Vector3.back * 0.025f * punch
                    + Vector3.down * 0.07f * landDip + Vector3.down * (sliding ? 0.04f : 0f) + Vector3.down * 0.35f * (1f - raise);
                viewModel.Root.localRotation = vmRestRot * Quaternion.Euler(sway.x + 10f * sprintPose + 30f * (1f - raise) - 3f * punch, sway.y - 12f * sprintPose, sway.y * 0.6f + strafeRoll + 8f * sprintPose);
            }

            slideLean = Mathf.MoveTowards(slideLean, sliding ? 7f : 0f, dt * 40f);

            bool down = life != null && life.Downed.Value;
            downRoll = Mathf.MoveTowards(downRoll, down ? 24f : 0f, dt * (down ? 60f : 90f));
            downTilt = Mathf.MoveTowards(downTilt, down ? 18f : 0f, dt * (down ? 40f : 80f));

            float drunk = Mathf.MoveTowards(drunkSway, Drunk, dt * 0.5f);
            drunkSway = drunk;
            float dk = drunk * Mathf.Max(0.35f, Settings.ShakeScale), tt = Time.time;
            float dRoll = (2.2f * Mathf.Sin(tt * 0.83f) + 1.2f * Mathf.Sin(tt * 1.91f + 1f)) * dk;
            float drPitch = (1.1f * Mathf.Sin(tt * 0.61f + 2f)) * dk;
            float drYaw = (1.4f * Mathf.Sin(tt * 0.47f + 0.5f)) * dk;
            cam.localRotation = Quaternion.Euler(-downTilt + drPitch, drYaw, slideLean + downRoll + dRoll);
            return (Vector3.up * Mathf.Sin(walkPhase * 2f) * 0.022f * amp + Vector3.down * 0.12f * landDip) * Settings.ShakeScale;
        }

        public void ViewModelSwing(float windup, float rest, float side) => viewAnim?.Swing(windup, rest, side);

        private float nextChatAt;

        [Rpc(SendTo.Server)]
        public void ChatRpc(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Time.time < nextChatAt) return;
            nextChatAt = Time.time + 0.4f;
            text = text.Trim().Replace((char)10, ' ').Replace((char)13, ' ');
            if (text.Length > ChatBox.MaxLength) text = text.Substring(0, ChatBox.MaxLength);
            ChatShowRpc(name.Replace("(Clone)", "").Trim(), text);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChatShowRpc(string who, string text) => ChatBox.Add(who, SlotColor, text);
        public void ViewModelBash() => viewAnim?.Bash();

        public void AddShake(float amount) => shake = Mathf.Min(1f, shake + amount);
        public void ViewModelBlockImpact() { viewAnim?.BlockImpact(); shake = Mathf.Min(1f, shake + 0.3f); }
        public void ViewModelCast(bool frost) => viewAnim?.Cast(frost);

        public void ConfirmHit(float strength)
        {
            shake = Mathf.Min(1f, shake + 0.12f * strength);

            viewAnim?.HitPause(0.045f + 0.035f * Mathf.Clamp01(strength));
            punch = Mathf.Min(1f, punch + 0.6f * Mathf.Clamp01(strength + 0.3f));
        }

        private interface IViewAnim
        {
            void Update(bool blocking, bool downed, bool sprinting);
            void Swing(float windup, float rest, float side);
            void Bash();
            void BlockImpact();
            void Cast(bool frost);
            void SetCharge(float charge);

            void HitPause(float seconds) { }
        }

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

                var aimLift = new Vector3(0.12f, 0.1f, 0f) * Mathf.Clamp01(charge * 3f);
                vm.ShieldPivot.localPosition = bowRest + run + down + aimLift + new Vector3(tremble, tremble, -0.03f * snap);
                vm.ShieldPivot.localRotation = bowRot;
                vm.SwordPivot.localPosition = drawRest + run + down + aimLift + new Vector3(0f, 0f, -0.22f * charge + 0.08f * snap);
            }
        }

        private class DaggerViewAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 rRest, lRest;
            private readonly Quaternion rRot, lRot;
            private float rStab = -10f, lStab = -10f, throwAt = -10f, bob;
            private readonly BladeTrail rTrail, lTrail;
            private float clock, pauseUntil = -1f, idleSince;
            public void HitPause(float seconds) => pauseUntil = Mathf.Max(pauseUntil, clock + seconds);

            public DaggerViewAnimator(CharacterArt.ViewModel vm)
            {
                this.vm = vm;
                rRest = vm.SwordPivot.localPosition; rRot = vm.SwordPivot.localRotation;
                lRest = vm.ShieldPivot.localPosition; lRot = vm.ShieldPivot.localRotation;
                var steel = new Color(0.85f, 0.9f, 1f);
                if (vm.TipR != null) rTrail = new BladeTrail(vm.Root, vm.TipR, vm.EdgeR, steel);
                if (vm.TipL != null) lTrail = new BladeTrail(vm.Root, vm.TipL, vm.EdgeL, steel);
            }

            public void Swing(float windup, float rest, float side)
            {
                if (side > 0f) { rStab = clock; rTrail?.Arm(0.13f); }
                else { lStab = clock; lTrail?.Arm(0.13f); }
            }

            public void Bash() { }
            public void BlockImpact() { }
            public void Cast(bool frost) => throwAt = clock;
            public void SetCharge(float charge) { }

            public void Update(bool blocking, bool downed, bool sprinting)
            {
                clock += clock < pauseUntil ? Time.deltaTime * 0.08f : Time.deltaTime;
                if (sprinting || downed || clock - rStab < 0.6f || clock - lStab < 0.6f || clock - throwAt < 0.6f) idleSince = clock;

                float flipT = clock - idleSince - 6f;
                float flip = 0f, toss = 0f;
                if (flipT > 0f && flipT < 0.7f) { float k = flipT / 0.7f; flip = k * k * (3f - 2f * k) * 360f; toss = Mathf.Sin(k * Mathf.PI) * 0.09f; }
                else if (flipT >= 0.7f) idleSince = clock - Random.Range(0f, 2.5f);
                bob += Time.deltaTime * (sprinting ? 12f : 0f);
                var run = sprinting ? new Vector3(Mathf.Sin(bob) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f) : Vector3.zero;
                var down = downed ? new Vector3(0f, -0.4f, -0.1f) : Vector3.zero;

                float t = clock - throwAt;
                float flick = t < 0.25f ? Mathf.Sin(t / 0.25f * Mathf.PI) : 0f;

                var (rp, re) = Cut(clock - rStab, 1f);
                var (lp, le) = Cut(clock - lStab, -1f);
                vm.SwordPivot.localPosition = rRest + run + down + rp + new Vector3(0f, 0.12f, 0.1f) * flick;
                vm.SwordPivot.localPosition += Vector3.up * toss;
                vm.SwordPivot.localRotation = rRot * Quaternion.Euler(re + new Vector3(-60f * flick - flip, 0f, 0f));
                vm.ShieldPivot.localPosition = lRest + run + down + lp;
                vm.ShieldPivot.localRotation = lRot * Quaternion.Euler(le);
                rTrail?.Update();
                lTrail?.Update();
            }

            private static (Vector3 pos, Vector3 euler) Cut(float t, float s)
            {
                const float Cock = 0.05f, Thrust = 0.09f, Hold = 0.04f, Back = 0.14f;
                var cockP = new Vector3(0.05f * s, 0.05f, -0.1f);
                var cockE = new Vector3(25f, 10f * s, 25f * s);
                var hitP = new Vector3(-0.17f * s, 0.07f, 0.34f);
                var hitE = new Vector3(-75f, -25f * s, -40f * s);
                if (t < 0f || t > Cock + Thrust + Hold + Back) return (Vector3.zero, Vector3.zero);
                if (t < Cock) { float k = Ease(t / Cock); return (cockP * k, cockE * k); }
                t -= Cock;
                if (t < Thrust) { float k = 1f - (1f - t / Thrust) * (1f - t / Thrust); return (Vector3.Lerp(cockP, hitP * 1.06f, k), Vector3.Lerp(cockE, hitE * 1.06f, k)); }
                t -= Thrust;
                if (t < Hold) { float k = Ease(t / Hold); return (Vector3.Lerp(hitP * 1.06f, hitP, k), Vector3.Lerp(hitE * 1.06f, hitE, k)); }
                t -= Hold;
                float b = Ease(t / Back);
                return (Vector3.Lerp(hitP, Vector3.zero, b), Vector3.Lerp(hitE, Vector3.zero, b));
            }

            private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        }

        private class MageViewAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 staffRest, handRest;
            private readonly Quaternion staffRot, handRot;
            private float castStart = -10f, impactStart = -10f, bob, idle;
            private bool lastFrost;

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

                float flexT = Mathf.Repeat(idle, 9f) - 7.5f;
                float flex = flexT > 0f ? Mathf.Sin(flexT / 1.5f * Mathf.PI) : 0f;
                vm.ShieldPivot.localRotation = handRot * Quaternion.Euler(-35f * thrust, 0f, 40f * flex);
                vm.ShieldPivot.localPosition += new Vector3(-0.02f, 0.04f, 0.02f) * flex;
                thrust = Mathf.Max(thrust, flex * 0.4f);

                if (vm.SpellGlow != null)
                {
                    var color = lastFrost ? Frost : Fire;

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

        private class ViewModelAnimator : IViewAnim
        {
            private readonly CharacterArt.ViewModel vm;
            private readonly Vector3 swordRestPos, shieldRestPos;
            private readonly Quaternion swordRestRot, shieldRestRot;

            private float swingStart = -10f, swingWindup, swingRest, swingSide = 1f;
            private float bashStart = -10f, impactStart = -10f;
            private float block, bob;
            private readonly BladeTrail trail;
            private float armedFor = -1f;

            private float clock, pauseUntil = -1f, idleSince;
            public void HitPause(float seconds) => pauseUntil = Mathf.Max(pauseUntil, clock + seconds);

            public ViewModelAnimator(CharacterArt.ViewModel vm)
            {
                this.vm = vm;
                swordRestPos = vm.SwordPivot.localPosition;
                swordRestRot = vm.SwordPivot.localRotation;
                shieldRestPos = vm.ShieldPivot.localPosition;
                shieldRestRot = vm.ShieldPivot.localRotation;
                if (vm.TipR != null) trail = new BladeTrail(vm.Root, vm.TipR, vm.EdgeR, new Color(1f, 0.86f, 0.62f));
            }

            public void Swing(float windup, float rest, float side)
            {
                chain = clock - swingStart < swingWindup + swingRest + 0.5f ? chain + 1 : 0;
                overhead = chain % 3 == 2;
                swingStart = clock; swingWindup = windup; swingRest = rest; swingSide = side;
            }
            private int chain;
            private bool overhead;

            private static readonly Vector3 CoilOP = new Vector3(-0.08f, 0.32f, -0.12f), CoilOE = new Vector3(-70f, 0f, 20f);
            private static readonly Vector3 MidOP = new Vector3(-0.12f, 0.08f, 0.3f), MidOE = new Vector3(20f, 0f, 25f);
            private static readonly Vector3 EndOP = new Vector3(-0.14f, -0.34f, 0.3f), EndOE = new Vector3(88f, 0f, 22f);

            private static readonly Vector3 CoilP = new Vector3(0.12f, 0.2f, -0.08f), CoilE = new Vector3(-35f, 10f, -30f);
            private static readonly Vector3 MidP = new Vector3(-0.05f, 0.0f, 0.26f), MidE = new Vector3(35f, -20f, 30f);
            private static readonly Vector3 EndP = new Vector3(-0.34f, -0.3f, 0.26f), EndE = new Vector3(70f, -40f, 75f);

            private static readonly Vector3 CoilBP = new Vector3(-0.22f, -0.12f, 0f), CoilBE = new Vector3(30f, 25f, 70f);
            private static readonly Vector3 MidBP = new Vector3(-0.02f, 0.06f, 0.26f), MidBE = new Vector3(-10f, 20f, 10f);
            private static readonly Vector3 EndBP = new Vector3(0.22f, 0.22f, 0.2f), EndBE = new Vector3(-45f, 30f, -55f);
            private const float CutTime = 0.13f, FollowTime = 0.07f;

            private static Vector3 Mirror(Vector3 v, float side) => new Vector3(v.x * side, v.y, v.z * (side > 0f ? 1f : 0.9f));

            private static Vector3 Bez(Vector3 a, Vector3 m, Vector3 b, float t)
            {
                var c = 2f * m - 0.5f * (a + b);
                return Vector3.Lerp(Vector3.Lerp(a, c, t), Vector3.Lerp(c, b, t), t);
            }

            public void Bash() => bashStart = clock;
            public void BlockImpact() => impactStart = clock;
            public void Cast(bool frost) { }
            public void SetCharge(float charge) { }

            public void Update(bool blocking, bool downed, bool sprinting)
            {
                float dt = Time.deltaTime;
                clock += clock < pauseUntil ? dt * 0.08f : dt;
                if (blocking || sprinting || downed || clock - swingStart < swingWindup + swingRest + 0.3f || clock - bashStart < 0.6f) idleSince = clock;
                block = Mathf.MoveTowards(block, blocking ? 1f : 0f, dt * 8f);
                bob += dt * (sprinting ? 11f : 0f);

                Vector3 pos = swordRestPos;
                Quaternion rot = swordRestRot;
                float t = clock - swingStart;

                float sd = swingSide;
                bool fore = sd > 0f;
                var coilP = overhead ? CoilOP : fore ? CoilP : CoilBP; var coilE = overhead ? CoilOE : fore ? CoilE : CoilBE;
                var midP = overhead ? MidOP : fore ? MidP : MidBP; var midE = overhead ? MidOE : fore ? MidE : MidBE;
                var endP = overhead ? EndOP : fore ? EndP : EndBP; var endE = overhead ? EndOE : fore ? EndE : EndBE;
                Vector3 off = Vector3.zero, eul = Vector3.zero;
                if (t >= 0f && t < swingWindup)
                {
                    float k = Smooth(t / swingWindup);
                    float k2 = 1f - (1f - k) * (1f - k);
                    off = coilP * k2;
                    eul = coilE * k2 + new Vector3(Mathf.Sin(t * 60f) * 1.2f * k, 0f, 0f);
                }
                else if (t >= swingWindup && t < swingWindup + swingRest)
                {
                    float s = t - swingWindup;
                    if (armedFor != swingStart) { armedFor = swingStart; trail?.Arm(CutTime + 0.01f); }
                    if (s < CutTime)
                    {
                        float k = s / CutTime;
                        k = 1f - (1f - k) * (1f - k) * (1f - k);
                        off = Bez(coilP, midP, endP, k);
                        eul = Bez(coilE, midE, endE, k);
                    }
                    else if (s < CutTime + FollowTime)
                    {
                        float k = Mathf.Sin((s - CutTime) / FollowTime * Mathf.PI * 0.5f);
                        off = Vector3.Lerp(endP, endP * 1.12f, k);
                        eul = Vector3.Lerp(endE, endE * 1.1f, k);
                    }
                    else
                    {
                        float k = Smooth((s - CutTime - FollowTime) / Mathf.Max(0.05f, swingRest - CutTime - FollowTime));
                        float settle = Mathf.Sin(k * Mathf.PI) * 0.12f;
                        off = Vector3.Lerp(endP * 1.12f, Vector3.zero, k) + new Vector3(0f, settle * 0.1f, 0f);
                        eul = Vector3.Lerp(endE * 1.1f, Vector3.zero, k);
                    }
                }

                float idleT = clock - idleSince - 7f;
                if (idleT > 0f && idleT < 2.4f)
                {
                    float k = Mathf.Sin(Mathf.Clamp01(idleT / 2.4f) * Mathf.PI);
                    k = k * k * (3f - 2f * k);

                    off += new Vector3(-0.05f, 0.07f, 0.01f) * k;
                    eul += new Vector3(-8f, 80f, 6f) * k + new Vector3(0f, Mathf.Sin(idleT * 3f) * 5f * k, 0f);
                }
                else if (idleT >= 2.4f) idleSince = clock - Random.Range(0f, 3f);
                pos += off;
                rot *= Quaternion.Euler(eul);
                if (sprinting) pos += new Vector3(Mathf.Sin(bob) * 0.02f, Mathf.Abs(Mathf.Cos(bob)) * 0.025f, 0f);
                if (downed) pos += new Vector3(0f, -0.35f, -0.1f);
                vm.SwordPivot.localPosition = pos;
                vm.SwordPivot.localRotation = rot;
                trail?.Update();

                Vector3 spos = shieldRestPos + new Vector3(0.3f, 0.14f, -0.05f) * block;
                Quaternion srot = shieldRestRot * Quaternion.Euler(0f, -18f * block, 0f);

                float b = clock - bashStart;
                if (b < 0.35f) spos += new Vector3(0.15f, 0.05f, 0.3f) * Mathf.Sin(b / 0.35f * Mathf.PI);

                float i = clock - impactStart;
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
