using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    public class PadCursor : MonoBehaviour
    {
        private const float Speed = 1.1f;
        private bool held;
        private float scrollCarry;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("PadCursor");
            DontDestroyOnLoad(go);
            go.AddComponent<PadCursor>();
        }

        public static bool Driving { get; private set; }

        private void Update()
        {
            var g = Gamepad.current;
            var mouse = Mouse.current;
            Driving = g != null && mouse != null && Application.isFocused && !Networking.NetworkLauncher.TestRun
                && Cursor.lockState != CursorLockMode.Locked && !(PauseMenu.Open && !PauseMenu.InOptions);
            if (!Driving) { Release(); return; }

            var stick = g.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.04f)
            {
                var v = stick * stick.magnitude * Speed * Screen.height * Time.unscaledDeltaTime;
                var at = mouse.position.ReadValue() + v;
                at.x = Mathf.Clamp(at.x, 1f, Screen.width - 2f);
                at.y = Mathf.Clamp(at.y, 1f, Screen.height - 2f);
                mouse.WarpCursorPosition(at);
            }

            bool down = g.buttonSouth.isPressed;
            if (down != held)
            {
                held = down;
                Click(down);
            }

            float scroll = g.rightStick.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.2f)
            {
                scrollCarry += scroll * 8f * Time.unscaledDeltaTime;
                int notches = (int)scrollCarry;
                if (notches != 0) { scrollCarry -= notches; Wheel(notches); }
            }
            else scrollCarry = 0f;
        }

        private void Release()
        {
            if (held) { held = false; Click(false); }
        }

        private void OnDisable() => Release();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, int data, System.UIntPtr extra);
        private const uint LeftDown = 0x0002, LeftUp = 0x0004, WheelMove = 0x0800;
        private static void Click(bool down) => mouse_event(down ? LeftDown : LeftUp, 0, 0, 0, System.UIntPtr.Zero);
        private static void Wheel(int notches) => mouse_event(WheelMove, 0, 0, notches * 120, System.UIntPtr.Zero);
#else
        private static void Click(bool down) { }
        private static void Wheel(int notches) { }
#endif
    }
}
