using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// First-person walk, look and jump for the locally owned player.
    ///
    /// <para>
    /// Knows nothing about networking. <see cref="NetworkPlayer"/> enables it on the owner
    /// and leaves it disabled on every other peer, where the transform is driven by the
    /// network instead. Keeping the two apart means movement feel can be tuned — and later
    /// swapped for a stat-driven version that reads SPD — without touching spawn logic.
    /// </para>
    ///
    /// <para>
    /// Reads devices directly (<c>Keyboard.current</c>, <c>Mouse.current</c>) rather than
    /// through an Input Actions asset. That is a deliberate M0 shortcut: rebinding and
    /// gamepad support belong to a later milestone, and an actions asset is easy to slot in
    /// behind <see cref="ReadMoveInput"/> and <see cref="ReadLookInput"/> when they do.
    /// </para>
    /// </summary>
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
        /// <summary>A jump still counts this long after walking off a ledge, and a press this early before landing.</summary>
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

        /// <summary>Scales walk and sprint speed. Blocking and the SPD stat both feed this.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>No movement input while true (downed); gravity and look still apply.</summary>
        public bool Frozen { get; set; }

        /// <summary>Sprint is refused while false, so the combat kit can gate it on stamina.</summary>
        public bool CanSprint { get; set; } = true;

        /// <summary>The last frame's movement input (x = strafe, y = forward), for directional dodges.</summary>
        public Vector2 MoveInput { get; private set; }
        /// <summary>The move keys even while pinned (a seated player stands up by walking off).</summary>
        public Vector2 RawMove { get; private set; }

        /// <summary>True on frames where sprint is held and actually applied.</summary>
        public bool IsSprinting { get; private set; }

        // ---- Slide: C while sprinting. A low, fast burst; a kick during it hits much harder. ----
        public const float SlideSeconds = 0.75f, SlideCooldown = 1.4f;
        private float slideUntil = -1f, slideReadyAt;
        private Vector3 slideDir;

        /// <summary>Owner: begin a slide if sprinting on the ground. Returns whether it did.</summary>
        public bool TryStartSlide()
        {
            if (!IsSprinting || !controller.isGrounded || Time.time < slideReadyAt) return false;
            slideUntil = Time.time + SlideSeconds;
            slideReadyAt = Time.time + SlideCooldown;
            slideDir = transform.forward;
            return true;
        }

        /// <summary>True during a slide.</summary>
        public bool IsSliding => Time.time < slideUntil;

        /// <summary>Lowers the camera, e.g. to kneeling height while downed.</summary>
        public float CameraDrop { get; set; }

        /// <summary>True while the player has captured the mouse — i.e. is playing, not in a menu.</summary>
        public bool HasControl => BotControl || Cursor.lockState == CursorLockMode.Locked;
        /// <summary>
        /// The autoplay bot's window is often not the focused one, and only the focused window of several can hold the
        /// cursor (2026-11-03: in a four-bot run only one bot could act). It plays with no mouse, so it needs no lock.
        /// </summary>
        public static bool BotControl { get; set; }

        /// <summary>
        /// Attacks and abilities: control, and not in the quarter second after a menu closed or the
        /// window was clicked back into (so the click that closes a menu never swings at a vendor).
        /// </summary>
        public bool ActionsReady => HasControl && Time.time >= actionsGraceUntil;
        private float actionsGraceUntil;
        private bool menuWasOpen;
        public const float ActionGrace = 0.25f;

        /// <summary>Set while the ping wheel is open: the mouse picks a slice instead of turning the view.</summary>
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

        /// <summary>A velocity burst that decays over ~0.3s: shield bash lunges, boss knockback.</summary>
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

            // Escape frees the cursor so the connection panel (and the rest of the desktop)
            // stays reachable; clicking back into the window recaptures it.
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(false);

            // A menu (the merchant's shop) owns the cursor while it is open; closing it takes the
            // cursor straight back (no click needed), and the hands wait a moment before acting.
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
            // L3 latches a sprint until you stop pushing forward (holding a stick in is tiring).
            UI.Binds.Tick();
            if (UI.Binds.PadSprintDown || (UI.Settings.ToggleSprint && UI.Binds.Down(UI.Bind.Sprint))) padSprint = !padSprint;
            if (MoveInput.y < 0.2f) padSprint = false;
            bool sprintKey = (keyboard != null && !UI.Settings.ToggleSprint && UI.Binds.Held(UI.Bind.Sprint)) || padSprint;
            bool jumpKey = UI.Binds.Down(UI.Bind.Jump);
            jumpHeldNow = UI.Binds.Held(UI.Bind.Jump);
            // Mania: your legs are not your own. Rooted: they are frozen to the floor.
            if (debuffs == null) debuffs = GetComponent<PlayerDebuffs>();
            bool manic = debuffs != null && debuffs.Manic && canMove;
            if (manic) { MoveInput = ManiaInput(); sprintKey = true; jumpKey = false; }
            if (debuffs != null && debuffs.Rooted) jumpKey = false;

            if (controller.enabled) ApplyMovement(MoveInput, canMove && (CanSprint || manic) && sprintKey, canMove && jumpKey);

            // The world sways while madness has you.
            maniaRoll = Mathf.Lerp(maniaRoll, manic ? Mathf.Sin(Time.time * 4.3f) * 7f + Mathf.Sin(Time.time * 7.1f) * 3f : 0f, 1f - Mathf.Exp(-Time.deltaTime * 6f));

            // Camera feel: a lean into strafes, a gentle bob with your stride, a dip when you land
            // hard, and a slightly wider view at a sprint. All scaled by the screen-shake setting.
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
                p.y = Mathf.Lerp(p.y, pivotRestHeight - CameraDrop - (IsSliding ? 0.7f : 0f) - landDip, Time.deltaTime * (IsSliding ? 14f : 12f));
                cameraPivot.localPosition = p + Vector3.up * (bobY - lastBobY);
                lastBobY = bobY;
            }
            // Sprint widens the view a touch (on top of the FOV setting).
            viewCam ??= cameraPivot != null ? cameraPivot.GetComponentInChildren<Camera>() : null;
            fovKick = Mathf.Lerp(fovKick, IsSprinting && speedNow > walkSpeed ? 5f * feel : 0f, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            if (viewCam != null) viewCam.fieldOfView = UI.Settings.FieldOfView + fovKick;
        }

        private PlayerDebuffs debuffs;
        private bool jumpHeldNow, padSprint;

        /// <summary>Degrees a second at full stick, scaled by the sensitivity setting.</summary>
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

        /// <summary>Owner, while manic: a wild run that lurches to a new heading every half second or so.</summary>
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
            // A pad alone is enough: Binds reads whichever is there.

            var input = new Vector2(
                (UI.Binds.Held(UI.Bind.MoveRight) ? 1f : 0f) - (UI.Binds.Held(UI.Bind.MoveLeft) ? 1f : 0f),
                (UI.Binds.Held(UI.Bind.MoveForward) ? 1f : 0f) - (UI.Binds.Held(UI.Bind.MoveBack) ? 1f : 0f));
            input += UI.Binds.PadMove;

            // Without this, diagonal movement is ~41% faster than straight movement.
            return Vector2.ClampMagnitude(input, 1f);
        }

        private static Vector2 ReadLookInput()
        {
            return Mouse.current != null && !BotControl ? Mouse.current.delta.ReadValue() : Vector2.zero;
        }

        private void ApplyLook(Vector2 delta)
        {
            // Yaw turns the whole body, so the networked transform carries facing to other
            // players. Pitch only tilts the local camera — nobody else needs it yet.
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

            // Landing: a dip in the view by how hard you came down, and the thump of your boots.
            if (grounded && !wasGrounded && fallSpeed > 5f)
            {
                landDip = Mathf.Clamp((fallSpeed - 5f) * 0.018f, 0.02f, 0.16f) * Mathf.Clamp01(UI.Settings.ShakeScale + 0.25f);
                Art.Sfx.Play(Art.Sound.Step, transform.position, Mathf.Clamp01(fallSpeed / 14f) * 0.6f, 0.75f);
            }
            wasGrounded = grounded;
            if (!grounded) fallSpeed = Mathf.Max(0f, -verticalVelocity);

            if (grounded && verticalVelocity < 0f)
            {
                // A small constant downward push keeps isGrounded true while walking down
                // slopes; resetting to exactly zero makes it flicker every other frame.
                verticalVelocity = -2f;
                jumping = false;
            }
            // Coyote time and a jump buffer: a press a moment early, or a moment after stepping off
            // an edge, still jumps.
            bool canJump = Time.time - lastGroundedAt <= CoyoteTime && !jumping;
            if (canJump && Time.time - jumpPressedAt <= JumpBuffer)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                jumping = true;
                jumpPressedAt = -10f;
                lastGroundedAt = -10f;
            }
            // Let go of jump early for a short hop.
            if (jumping && verticalVelocity > 0f && !jumpHeldNow) verticalVelocity *= Mathf.Exp(-Time.deltaTime * 14f);

            verticalVelocity += gravity * Time.deltaTime;

            // Sprint only counts moving forward-ish; sprinting sideways or backwards is not a thing.
            IsSprinting = sprint && input.y > 0.1f;
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;
            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;
            if (IsSliding)
            {
                // Fast at first, easing off; steering is locked to the direction you slid.
                float k = (slideUntil - Time.time) / SlideSeconds;
                wish = slideDir * sprintSpeed * SpeedMultiplier * Mathf.Lerp(0.9f, 1.7f, k);
                IsSprinting = true;
                planar = wish;
            }
            else
            {
                // Quick to get going and to stop on the ground (it should feel responsive, not icy);
                // in the air you keep your momentum and can only steer a little.
                bool hasInput = wish.sqrMagnitude > 0.01f;
                float rate = grounded ? (hasInput ? groundAccel : groundDecel) : airAccel;
                // A slowing effect (frostbite, roots) applies at once, never "coasting" past it.
                if (grounded && planar.magnitude > speed + 0.01f) planar = Vector3.ClampMagnitude(planar, Mathf.Max(speed, planar.magnitude - groundDecel * 2f * Time.deltaTime));
                planar = Vector3.MoveTowards(planar, grounded || hasInput ? wish : planar, rate * Time.deltaTime);
                if (SpeedMultiplier <= 0.001f) planar = Vector3.zero;
            }

            // A hard ceiling on running speed, whatever adds to it (a slide starts at 1.7x sprint).
            planar = Vector3.ClampMagnitude(planar, sprintSpeed * Mathf.Max(1f, SpeedMultiplier) * 1.8f);
            impulse = Vector3.MoveTowards(new Vector3(impulse.x, 0f, impulse.z), Vector3.zero, Time.deltaTime * 40f);
            Vector3 velocity = planar + impulse + Vector3.up * verticalVelocity;

            var flags = controller.Move(velocity * Time.deltaTime);
            // Bumping a wall or ceiling kills the speed into it, so you do not "stick" and then shoot off.
            if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
            // A wall only ever takes speed away. It used to blend toward the controller's whole velocity, which includes the
            // dodge/knockback impulse: brushing a wall folded half the impulse into running speed every frame, and in the
            // air nothing bled it off (the user, 2026-11-07: "almost infinite speeds if you dodge into a wall at an angle").
            if ((flags & CollisionFlags.Sides) != 0)
            {
                var moved = new Vector3(controller.velocity.x, 0f, controller.velocity.z) - new Vector3(impulse.x, 0f, impulse.z);
                float before = planar.magnitude;
                planar = Vector3.ClampMagnitude(Vector3.Lerp(planar, moved, 0.5f), before);
                // The impulse itself is spent against the wall too, so it cannot slide you along for its whole life.
                impulse = Vector3.ClampMagnitude(impulse, Mathf.Max(0f, new Vector3(controller.velocity.x, 0f, controller.velocity.z).magnitude));
            }
        }

        /// <summary>
        /// Moves the player without fighting the CharacterController.
        ///
        /// A direct <c>transform.position</c> write is overwritten by the controller's next
        /// <c>Move</c> call — the old prototype's "teleport doesn't stick" bug. Disabling the
        /// controller around the write is the fix.
        /// </summary>
        /// <summary>Tilt the view (degrees, positive looks down). For staged screenshots.</summary>
        public void SetPitch(float degrees)
        {
            pitch = Mathf.Clamp(degrees, -maxPitch, maxPitch);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Owner: riding something (the carriage seat). The body follows it; the head still turns.</summary>
        public bool Pinned { get; private set; }

        public void PinTo(Vector3 position)
        {
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

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = wasEnabled;
            verticalVelocity = 0f;
            impulse = Vector3.zero;
            planar = Vector3.zero;
            fallSpeed = 0f;
        }

        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private static bool NetworkLauncherHasFocus()
        {
            // So a click on "Disconnect" does not also recapture the mouse. IMGUI rects
            // have their origin at the top-left; the mouse position's is at the bottom-left.
            Vector2 mouse = Mouse.current.position.ReadValue();
            return Networking.NetworkLauncher.PanelRect.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
        }
    }
}
