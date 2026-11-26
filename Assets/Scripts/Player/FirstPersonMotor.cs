using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float jumpHeight = 1.1f;
        [SerializeField] private float gravity = -20f;

        [Header("Look")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float mouseSensitivity = 0.12f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Feel (2026-10-20 movement pass)")]
        [SerializeField] private float groundAccel = 70f, groundDecel = 55f, airAccel = 16f;

        public const float CoyoteTime = 0.12f, JumpBuffer = 0.15f;

        private CharacterController controller;
        private float verticalVelocity;
        private Vector3 planar;
        private float lastGroundedAt = -10f, jumpPressedAt = -10f, landDip, bobPhase, lean, fovKick;
        private bool wasGrounded, jumping;
        private float fallSpeed;
        private Camera viewCam;
        private float pitch;
        private Vector3 impulse;
        private float pivotRestHeight;

        public float SpeedMultiplier { get; set; } = 1f;

        public const float PaceCut = 0.8f;

        public bool Frozen { get; set; }

        public bool CanSprint { get; set; } = true;

        public Vector2 MoveInput { get; private set; }

        public Vector2 RawMove { get; private set; }

        public bool IsSprinting { get; private set; }

        public const float SlideSeconds = 0.75f, SlideCooldown = 1.4f;
        private float slideUntil = -1f, slideReadyAt;
        private Vector3 slideDir;

        public bool TryStartSlide()
        {
            if (!IsSprinting || !controller.isGrounded || Time.time < slideReadyAt) return false;
            slideUntil = Time.time + SlideSeconds;
            slideReadyAt = Time.time + SlideCooldown;
            slideDir = transform.forward;
            return true;
        }

        public bool IsSliding => Time.time < slideUntil;

        public const float CrouchSpeed = 0.5f, CrouchHeight = 1.05f, CrouchCameraDrop = 0.6f;
        public bool Crouching { get; private set; }
        private float standHeight, standCenterY;
        private PlayerLife lifeRef;

        private void TickCrouch(bool want)
        {
            if (standHeight <= 0f) { standHeight = controller.height; standCenterY = controller.center.y; }
            if (want == Crouching) return;
            if (!want && HeadroomBlocked()) return;
            Crouching = want;
            float h = want ? CrouchHeight : standHeight;
            controller.height = h;
            controller.center = new Vector3(controller.center.x, standCenterY - (standHeight - h) / 2f, controller.center.z);
            lifeRef ??= GetComponent<PlayerLife>();
            if (lifeRef != null && lifeRef.IsOwner) lifeRef.Crouched.Value = want;
        }

        private bool HeadroomBlocked()
        {
            var bottom = transform.position + Vector3.up * (CrouchHeight - controller.radius);
            var top = transform.position + Vector3.up * (standHeight - controller.radius);
            foreach (var c in Physics.OverlapCapsule(bottom, top, controller.radius * 0.9f, ~(1 << 2 | 1 << 8), QueryTriggerInteraction.Ignore))
                if (c.transform.root != transform.root) return true;
            return false;
        }

        public float CameraDrop { get; set; }

        public bool HasControl => BotControl || Cursor.lockState == CursorLockMode.Locked;

        public static bool BotControl { get; set; }

        public bool ActionsReady => HasControl && Time.time >= actionsGraceUntil;
        private float actionsGraceUntil;
        private bool menuWasOpen;
        public const float ActionGrace = 0.25f;

        public static bool LookHeld;

        public Transform CameraPivot
        {
            get => cameraPivot;
            set => cameraPivot = value;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (cameraPivot != null) pivotRestHeight = cameraPivot.localPosition.y;
        }

        public void AddImpulse(Vector3 velocity)
        {
            impulse += velocity;
            if (velocity.y > 0f) verticalVelocity = Mathf.Max(verticalVelocity, velocity.y);
        }

        private void OnEnable() => SetCursorLocked(true);

        private void OnDisable() => SetCursorLocked(false);

        private void Update()
        {
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);

            bool menuOpen = UI.GameHud.MenuOpen;
            if (menuOpen)
            {
                if (Cursor.lockState == CursorLockMode.Locked) SetCursorLocked(false);
            }
            else if (menuWasOpen && !UI.PauseMenu.Open && Application.isFocused)
            {
                SetCursorLocked(true);
                actionsGraceUntil = Time.time + ActionGrace;
            }
            else if (Cursor.lockState != CursorLockMode.Locked &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                !NetworkLauncherHasFocus())
            {
                SetCursorLocked(true);
                actionsGraceUntil = Time.time + ActionGrace;
            }
            menuWasOpen = menuOpen;

            bool hasControl = HasControl;
            bool canMove = hasControl && !Frozen && !Pinned;

            if (hasControl && !LookHeld) { ApplyLook(ReadLookInput()); ApplyPadLook(); }
            MoveInput = canMove ? ReadMoveInput() : Vector2.zero;
            RawMove = hasControl && !Frozen ? (canMove ? MoveInput : ReadMoveInput()) : Vector2.zero;

            UI.Binds.Tick();
            if (UI.Binds.PadSprintDown || (UI.Settings.ToggleSprint && UI.Binds.Down(UI.Bind.Sprint))) padSprint = !padSprint;
            if (MoveInput.y < 0.2f) padSprint = false;
            bool sprintKey = (keyboard != null && !UI.Settings.ToggleSprint && UI.Binds.Held(UI.Bind.Sprint)) || padSprint;
            bool jumpKey = UI.Binds.Down(UI.Bind.Jump);
            jumpHeldNow = UI.Binds.Held(UI.Bind.Jump);

            if (debuffs == null) debuffs = GetComponent<PlayerDebuffs>();
            bool manic = debuffs != null && debuffs.Manic && canMove;
            if (manic) { MoveInput = ManiaInput(); sprintKey = true; jumpKey = false; }
            else if (debuffs != null && debuffs.Confused && canMove) MoveInput = ConfuseInput(MoveInput);
            if (debuffs != null && debuffs.Rooted) jumpKey = false;

            TickCrouch(canMove && controller.enabled && !IsSprinting && !IsSliding && UI.Binds.Held(UI.Bind.Slide) && !(UI.Binds.Down(UI.Bind.Slide) && sprintKey));
            if (Crouching) sprintKey = false;
            if (mantling) TickMantle();
            else if (controller.enabled) ApplyMovement(MoveInput, canMove && (CanSprint || manic) && sprintKey, canMove && jumpKey);

            if (!controller.enabled) airStale = true;

            maniaRoll = Mathf.Lerp(maniaRoll, manic ? Mathf.Sin(Time.time * 4.3f) * 7f + Mathf.Sin(Time.time * 7.1f) * 3f : 0f, 1f - Mathf.Exp(-Time.deltaTime * 6f));

            float feel = Mathf.Clamp01(UI.Settings.ShakeScale);
            float speedNow = new Vector2(planar.x, planar.z).magnitude;
            bool grounded = controller.enabled && controller.isGrounded;
            bool bobs = UI.Settings.HeadBob;
            lean = Mathf.Lerp(lean, bobs ? -MoveInput.x * 1.6f * feel : 0f, 1f - Mathf.Exp(-Time.deltaTime * 8f));
            if (grounded && speedNow > 0.6f) bobPhase += Time.deltaTime * speedNow * 1.85f;
            float bobK = grounded && bobs ? Mathf.Clamp01(speedNow / 7f) * feel : 0f;
            float bobY = Mathf.Sin(bobPhase * 2f) * 0.032f * bobK;
            float bobRoll = Mathf.Sin(bobPhase) * 0.45f * bobK;
            landDip = Mathf.MoveTowards(landDip, 0f, Time.deltaTime * 0.9f);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, maniaRoll + lean + bobRoll);

            if (cameraPivot != null)
            {
                var p = cameraPivot.localPosition;
                p.y = Mathf.Lerp(p.y, pivotRestHeight - CameraDrop - (IsSliding ? 0.7f : Crouching ? CrouchCameraDrop : 0f) - landDip, Time.deltaTime * (IsSliding ? 14f : 12f));
                cameraPivot.localPosition = p + Vector3.up * (bobY - lastBobY);
                lastBobY = bobY;
            }

            viewCam ??= cameraPivot != null ? cameraPivot.GetComponentInChildren<Camera>() : null;
            fovKick = Mathf.Lerp(fovKick, IsSprinting && speedNow > walkSpeed ? 5f * feel : 0f, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            if (viewCam != null) viewCam.fieldOfView = UI.Settings.FieldOfView + fovKick;
        }

        private PlayerDebuffs debuffs;
        private bool jumpHeldNow, padSprint;

        public const float PadTurnSpeed = 190f;

        private void ApplyPadLook()
        {
            var v = UI.Binds.PadLook;
            if (v.sqrMagnitude < 1e-4f) return;
            float k = PadTurnSpeed * UI.Settings.PadSensitivity * Time.deltaTime * (debuffs != null ? debuffs.TurnMultiplier : 1f);
            transform.Rotate(0f, v.x * k, 0f);
            pitch = Mathf.Clamp(pitch - v.y * k * 0.7f * (UI.Settings.InvertLook ? -1f : 1f), -maxPitch, maxPitch);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
        private float lastBobY;
        private float maniaRoll, nextManiaTurn;
        private Vector3 maniaDir;

        private Vector2 ConfuseInput(Vector2 v)
        {
            if (Time.time >= confuseUntil)
            {
                int was = confuseMap;
                do confuseMap = Random.Range(1, 6); while (confuseMap == was);
                confuseUntil = Time.time + PlayerDebuffs.ConfuseShuffle;
                if (was != 0) { Art.Sfx.Play(Art.Sound.Potion, transform.position + Vector3.up * 1.5f, 0.35f, 0.55f); GetComponent<NetworkPlayer>()?.AddShake(0.15f); }
            }
            switch (confuseMap)
            {
                case 1: return new Vector2(v.x, -v.y);
                case 2: return new Vector2(-v.x, v.y);
                case 3: return new Vector2(v.y, -v.x);
                case 4: return new Vector2(-v.y, v.x);
                default: return -v;
            }
        }
        private int confuseMap;
        private float confuseUntil;

        private Vector2 ManiaInput()
        {
            bool stuck = controller.velocity.sqrMagnitude < 1f && Time.time > nextManiaTurn - 0.4f;
            if (Time.time >= nextManiaTurn || stuck || maniaDir == Vector3.zero)
            {
                nextManiaTurn = Time.time + Random.Range(0.45f, 0.85f);
                float a = Random.Range(0f, Mathf.PI * 2f);
                maniaDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            return new Vector2(Vector3.Dot(maniaDir, transform.right), Vector3.Dot(maniaDir, transform.forward)).normalized;
        }

        private static Vector2 ReadMoveInput()
        {
            var keyboard = Keyboard.current;

            var input = new Vector2(
                (UI.Binds.Held(UI.Bind.MoveRight) ? 1f : 0f) - (UI.Binds.Held(UI.Bind.MoveLeft) ? 1f : 0f),
                (UI.Binds.Held(UI.Bind.MoveForward) ? 1f : 0f) - (UI.Binds.Held(UI.Bind.MoveBack) ? 1f : 0f));
            input += UI.Binds.PadMove;

            return Vector2.ClampMagnitude(input, 1f);
        }

        private static Vector2 ReadLookInput()
        {
            return Mouse.current != null && !BotControl ? Mouse.current.delta.ReadValue() : Vector2.zero;
        }

        private void ApplyLook(Vector2 delta)
        {
            float sens = mouseSensitivity * UI.Settings.MouseSensitivity * (debuffs != null ? debuffs.TurnMultiplier : 1f);
            transform.Rotate(0f, delta.x * sens, 0f);

            pitch = Mathf.Clamp(pitch - delta.y * sens * (UI.Settings.InvertLook ? -1f : 1f), -maxPitch, maxPitch);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ApplyMovement(Vector2 input, bool sprint, bool jump)
        {
            bool grounded = controller.isGrounded;
            if (grounded) lastGroundedAt = Time.time;
            if (jump) jumpPressedAt = Time.time;

            if (grounded && !wasGrounded && fallSpeed > 5f)
            {
                landDip = Mathf.Clamp((fallSpeed - 5f) * 0.018f, 0.02f, 0.16f) * Mathf.Clamp01(UI.Settings.ShakeScale + 0.25f);
                Art.Sfx.Play(Art.Sound.Step, transform.position, Mathf.Clamp01(fallSpeed / 14f) * 0.6f, 0.75f);
            }

            if (grounded && !wasGrounded && !Pinned && transform.position.y > -5f)
            {
                float drop = airStale ? 0f : airTop - transform.position.y;

                if (drop > SafeDrop && fallSpeed >= Mathf.Sqrt(2f * -gravity * drop) * 0.6f)
                {
                    if (fallLife == null) fallLife = GetComponent<PlayerLife>();
                    if (fallLife != null) fallLife.FallRpc(drop);
                }
            }
            if (grounded || Pinned || airStale) airTop = transform.position.y; else airTop = Mathf.Max(airTop, transform.position.y);
            airStale = false;
            wasGrounded = grounded;
            if (!grounded) fallSpeed = Mathf.Max(0f, -verticalVelocity);

            if (grounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
                jumping = false;
            }

            bool canJump = Time.time - lastGroundedAt <= CoyoteTime && !jumping;
            if (canJump && Time.time - jumpPressedAt <= JumpBuffer)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                jumping = true;
                jumpPressedAt = -10f;
                lastGroundedAt = -10f;
            }

            if (grounded) groundY = transform.position.y;
            else if (input.y > 0.3f && verticalVelocity < 4.5f && TryMantle()) return;

            if (jumping && verticalVelocity > 0f && !jumpHeldNow) verticalVelocity *= Mathf.Exp(-Time.deltaTime * 14f);

            verticalVelocity += gravity * Time.deltaTime;

            IsSprinting = sprint && input.y > 0.1f;
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier * PaceCut * (Crouching ? CrouchSpeed : 1f);
            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;
            if (IsSliding)
            {
                float k = (slideUntil - Time.time) / SlideSeconds;
                wish = slideDir * sprintSpeed * SpeedMultiplier * PaceCut * Mathf.Lerp(0.9f, 1.7f, k);
                IsSprinting = true;
                planar = wish;
            }
            else
            {
                bool hasInput = wish.sqrMagnitude > 0.01f;
                float rate = grounded ? (hasInput ? groundAccel : groundDecel) : airAccel;

                if (grounded && planar.magnitude > speed + 0.01f) planar = Vector3.ClampMagnitude(planar, Mathf.Max(speed, planar.magnitude - groundDecel * 2f * Time.deltaTime));
                planar = Vector3.MoveTowards(planar, grounded || hasInput ? wish : planar, rate * Time.deltaTime);
                if (SpeedMultiplier <= 0.001f) planar = Vector3.zero;
            }

            planar = Vector3.ClampMagnitude(planar, sprintSpeed * Mathf.Max(1f, SpeedMultiplier) * 1.8f);
            impulse = Vector3.MoveTowards(new Vector3(impulse.x, 0f, impulse.z), Vector3.zero, Time.deltaTime * 40f);
            Vector3 velocity = planar + impulse + Vector3.up * verticalVelocity;

            var flags = controller.Move(velocity * Time.deltaTime);

            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;

            if ((flags & CollisionFlags.Sides) != 0)
            {
                var moved = new Vector3(controller.velocity.x, 0f, controller.velocity.z) - new Vector3(impulse.x, 0f, impulse.z);
                float before = planar.magnitude;
                planar = Vector3.ClampMagnitude(Vector3.Lerp(planar, moved, 0.5f), before);

                impulse = Vector3.ClampMagnitude(impulse, Mathf.Max(0f, new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude));
            }
        }

        public void SetPitch(float degrees)
        {
            pitch = Mathf.Clamp(degrees, -maxPitch, maxPitch);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        public bool Pinned { get; private set; }

        public const float SafeDrop = 4.2f, FallShare = 0.075f, FallMax = 0.75f;
        private float airTop;
        private bool airStale = true;
        private PlayerLife fallLife;

        public void PinTo(Vector3 position)
        {
            mantling = false;
            if (controller.enabled) controller.enabled = false;
            transform.position = position;
            Pinned = true;
            verticalVelocity = 0f;
            impulse = Vector3.zero;
        }

        public void Unpin(Vector3 position)
        {
            if (!Pinned) return;
            Pinned = false;
            transform.position = position;
            controller.enabled = true;
            verticalVelocity = 0f;
        }

        public const float MantleReach = 2.2f, MantleSeconds = 0.32f;
        public bool Mantling => mantling;
        private bool mantling;
        private Vector3 mantleFrom, mantleTo;
        private float mantleT, groundY;

        private static bool MantleSolid(Collider c) => c != null && !c.isTrigger && c.attachedRigidbody == null && c.GetComponentInParent<Combat.Health>() == null;

        public string MantleWhy { get; private set; } = "";

        public bool TryMantle()
        {
            MantleWhy = mantling ? "mantling" : Pinned ? "pinned" : !controller.enabled ? "controller off" : "";
            if (mantling || Pinned || !controller.enabled) return false;
            var pos = transform.position;
            var fwd = transform.forward;
            float r = controller.radius;
            if (!Physics.Raycast(pos + Vector3.up * 0.9f, fwd, out var wall, r + 0.45f, ~0, QueryTriggerInteraction.Ignore)
                && !Physics.Raycast(pos + Vector3.up * 0.3f, fwd, out wall, r + 0.45f, ~0, QueryTriggerInteraction.Ignore)) { MantleWhy = "no wall ahead"; return false; }
            if (!MantleSolid(wall.collider) || Mathf.Abs(wall.normal.y) > 0.4f) { MantleWhy = "wall " + wall.collider.name + " not solid/upright"; return false; }
            var over = pos + fwd * (wall.distance + 0.4f);
            float limit = Mathf.Min(pos.y + 1.75f, groundY + MantleReach);
            float span = limit + 0.3f - pos.y - 0.45f;
            if (span <= 0.05f || !Physics.Raycast(new Vector3(over.x, limit + 0.3f, over.z), Vector3.down, out var top, span, ~0, QueryTriggerInteraction.Ignore)) { MantleWhy = "no top within reach (span " + span.ToString("F2") + ", ground " + groundY.ToString("F2") + ")"; return false; }
            if (!MantleSolid(top.collider) || top.normal.y < 0.75f) { MantleWhy = "top " + top.collider.name + " not solid/flat"; return false; }

            controller.enabled = false;
            bool blocked = Physics.CheckCapsule(top.point + Vector3.up * (r + 0.12f), top.point + Vector3.up * (controller.height - r), r * 0.9f, ~0, QueryTriggerInteraction.Ignore);
            if (blocked)
            {
                var hits = Physics.OverlapCapsule(top.point + Vector3.up * (r + 0.12f), top.point + Vector3.up * (controller.height - r), r * 0.9f, ~0, QueryTriggerInteraction.Ignore);
                controller.enabled = true;
                MantleWhy = "no room on top (" + (hits.Length > 0 ? hits[0].name + " near " + hits[0].ClosestPoint(top.point + Vector3.up * 1f).ToString("F2") : "?") + ", top " + top.point.ToString("F2") + ")";
                return false;
            }
            mantling = true;
            mantleFrom = pos;
            mantleTo = top.point + Vector3.up * 0.05f;
            mantleT = 0f;
            verticalVelocity = 0f;
            planar = Vector3.zero;
            impulse = Vector3.zero;
            jumping = false;
            Art.Sfx.Play(Art.Sound.Step, pos, 0.5f, 0.85f);
            return true;
        }

        private void TickMantle()
        {
            mantleT += Time.deltaTime / MantleSeconds;
            float k = Mathf.Clamp01(mantleT);

            float up = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 1.5f)), fwd = Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (k - 0.35f) / 0.65f));
            var p = Vector3.Lerp(mantleFrom, mantleTo, fwd);
            p.y = Mathf.Lerp(mantleFrom.y, mantleTo.y, up);
            transform.position = p;
            if (k < 1f) return;
            mantling = false;
            controller.enabled = !Pinned;
            verticalVelocity = -2f;
            fallSpeed = 0f;
            airTop = groundY = mantleTo.y;
            wasGrounded = true;
            lastGroundedAt = Time.time;
            landDip = 0.05f * Mathf.Clamp01(UI.Settings.ShakeScale + 0.25f);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = controller.enabled;
            if (mantling) { mantling = false; wasEnabled = !Pinned; }
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = wasEnabled;
            verticalVelocity = 0f;
            impulse = Vector3.zero;
            planar = Vector3.zero;
            fallSpeed = 0f;
            airTop = position.y;
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private static bool NetworkLauncherHasFocus()
        {
            Vector2 mouse = Mouse.current.position.ReadValue();
            return Networking.NetworkLauncher.PanelRect.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }
    }
}
