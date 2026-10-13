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

        private CharacterController controller;
        private float verticalVelocity;
        private float pitch;
        private Vector3 impulse;
        private float pivotRestHeight;

        /// <summary>Scales walk and sprint speed. Blocking and the SPD stat both feed this.</summary>
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>No movement input while true (downed); gravity and look still apply.</summary>
        public bool Frozen { get; set; }

        /// <summary>Sprint is refused while false, so the combat kit can gate it on stamina.</summary>
        public bool CanSprint { get; set; } = true;

        /// <summary>True on frames where sprint is held and actually applied.</summary>
        public bool IsSprinting { get; private set; }

        /// <summary>Lowers the camera, e.g. to kneeling height while downed.</summary>
        public float CameraDrop { get; set; }

        /// <summary>True while the player has captured the mouse — i.e. is playing, not in a menu.</summary>
        public bool HasControl => Cursor.lockState == CursorLockMode.Locked;

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
            if (Cursor.lockState != CursorLockMode.Locked &&
                Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                !NetworkLauncherHasFocus())
            {
                SetCursorLocked(true);
            }

            bool hasControl = HasControl;
            bool canMove = hasControl && !Frozen;

            if (hasControl) ApplyLook(ReadLookInput());
            ApplyMovement(canMove ? ReadMoveInput() : Vector2.zero,
                          canMove && CanSprint && keyboard != null && keyboard.leftShiftKey.isPressed,
                          canMove && keyboard != null && keyboard.spaceKey.wasPressedThisFrame);

            if (cameraPivot != null)
            {
                var p = cameraPivot.localPosition;
                p.y = Mathf.Lerp(p.y, pivotRestHeight - CameraDrop, Time.deltaTime * 8f);
                cameraPivot.localPosition = p;
            }
        }

        private static Vector2 ReadMoveInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;

            var input = new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));

            // Without this, diagonal movement is ~41% faster than straight movement.
            return Vector2.ClampMagnitude(input, 1f);
        }

        private static Vector2 ReadLookInput()
        {
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        }

        private void ApplyLook(Vector2 delta)
        {
            // Yaw turns the whole body, so the networked transform carries facing to other
            // players. Pitch only tilts the local camera — nobody else needs it yet.
            transform.Rotate(0f, delta.x * mouseSensitivity, 0f);

            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -maxPitch, maxPitch);
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        private void ApplyMovement(Vector2 input, bool sprint, bool jump)
        {
            if (controller.isGrounded)
            {
                // A small constant downward push keeps isGrounded true while walking down
                // slopes; resetting to exactly zero makes it flicker every other frame.
                if (verticalVelocity < 0f) verticalVelocity = -2f;
                if (jump) verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            verticalVelocity += gravity * Time.deltaTime;

            // Sprint only counts moving forward-ish; sprinting sideways or backwards is not a thing.
            IsSprinting = sprint && input.y > 0.1f;
            float speed = (IsSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;
            Vector3 horizontal = (transform.right * input.x + transform.forward * input.y) * speed;

            impulse = Vector3.MoveTowards(new Vector3(impulse.x, 0f, impulse.z), Vector3.zero, Time.deltaTime * 40f);
            Vector3 velocity = horizontal + impulse + Vector3.up * verticalVelocity;

            controller.Move(velocity * Time.deltaTime);
        }

        /// <summary>
        /// Moves the player without fighting the CharacterController.
        ///
        /// A direct <c>transform.position</c> write is overwritten by the controller's next
        /// <c>Move</c> call — the old prototype's "teleport doesn't stick" bug. Disabling the
        /// controller around the write is the fix.
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = wasEnabled;
            verticalVelocity = 0f;
            impulse = Vector3.zero;
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
