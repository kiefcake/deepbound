using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Deepbound.Core;

namespace Deepbound.UI
{
    public enum Bind
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Jump, Sprint, Dodge, Slide,
        AbilityQ, AbilityE, Ultimate, Kick, Use, Interact, Emote, Inventory, Map, Ping, Scrap, Stats, Class,
        Potion1, Potion2, Potion3, Potion4, PushToTalk, Rare,
        Tool,
    }

    public static class Binds
    {
        public static readonly (Bind bind, string label, Key key)[] Defaults =
        {
            (Bind.MoveForward, "Move forward", Key.W), (Bind.MoveBack, "Move back", Key.S), (Bind.MoveLeft, "Move left", Key.A), (Bind.MoveRight, "Move right", Key.D),
            (Bind.Jump, "Jump", Key.Space), (Bind.Sprint, "Sprint", Key.LeftShift), (Bind.Dodge, "Dodge", Key.LeftCtrl), (Bind.Slide, "Crouch (hold) / slide while sprinting", Key.C),
            (Bind.AbilityQ, "Ability 1", Key.Q), (Bind.AbilityE, "Ability 2", Key.E), (Bind.Ultimate, "Ultimate", Key.R), (Bind.Kick, "Kick", Key.V),
            (Bind.Use, "Use / pick up", Key.F), (Bind.Interact, "Talk / trade / drag", Key.F), (Bind.Emote, "Wave / emote wheel (hold)", Key.H),
            (Bind.Inventory, "Inventory", Key.I), (Bind.Map, "Map", Key.M), (Bind.Ping, "Give to an ally (hold)", Key.G), (Bind.Scrap, "Scrap", Key.X),
            (Bind.Stats, "Stat sheet (hold)", Key.Tab), (Bind.Class, "Class screen", Key.B),
            (Bind.Potion1, "Health potion", Key.Digit1), (Bind.Potion2, "Frost flask", Key.Digit2), (Bind.Potion3, "Holy water", Key.Digit3), (Bind.Potion4, "Holy water (spare key)", Key.Digit4),
            (Bind.PushToTalk, "Push to talk", Key.T), (Bind.Rare, "Rare draught / tome", Key.Digit5),
            (Bind.Tool, "Throw a crafted tool", Key.Z),
        };

        private static Key[] current;

        private static Key[] Current
        {
            get
            {
                if (current != null) return current;
                current = new Key[Defaults.Length];
                for (int i = 0; i < Defaults.Length; i++)
                {
                    int saved = Progression.Cosmetics.TestMode ? (int)Defaults[i].key : Prefs.GetInt("db.bind." + Defaults[i].bind, (int)Defaults[i].key);

                    if (Defaults[i].bind == Bind.Interact && saved == (int)Key.G && Prefs.GetInt("db.bind.fmerge", 0) == 0) saved = (int)Defaults[i].key;
                    current[i] = System.Enum.IsDefined(typeof(Key), saved) && saved != (int)Key.None ? (Key)saved : Defaults[i].key;
                }
                return current;
            }
        }

        public static Key KeyOf(Bind b) => Current[(int)b];

        public static bool Merged => KeyOf(Bind.Use) == KeyOf(Bind.Interact);

        private static KeyControl Control(Bind b)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            var k = KeyOf(b);
            return k == Key.None ? null : kb[k];
        }

        public static bool Down(Bind b)
        {
            var c = Control(b); var p = Pad(b);
            bool down = (c != null && c.wasPressedThisFrame) || (p != null && p.wasPressedThisFrame) || BotPressedNow(b);
            if (down && Locked(b)) { LockedPress(); return false; }
            return down;
        }
        public static bool Held(Bind b) { var c = Control(b); var p = Pad(b); return !Locked(b) && ((c != null && c.isPressed) || (p != null && p.isPressed) || BotPressedNow(b)); }

        private static readonly System.Collections.Generic.Dictionary<Bind, int> botPressFrame = new System.Collections.Generic.Dictionary<Bind, int>();
        private static int botPrimaryFrame = -1, botSecondaryFrame = -1;
        public static bool BotPrimary { get; private set; }
        public static bool BotSecondary { get; private set; }

        private static bool BotPressedNow(Bind b) => botPressFrame.Count > 0 && botPressFrame.TryGetValue(b, out int f) && f == Time.frameCount;

        public static void BotPress(Bind b) => botPressFrame[b] = Time.frameCount + 1;
        public static void BotSetPrimary(bool on) { if (on && !BotPrimary) botPrimaryFrame = Time.frameCount + 1; BotPrimary = on; }
        public static void BotSetSecondary(bool on) { if (on && !BotSecondary) botSecondaryFrame = Time.frameCount + 1; BotSecondary = on; }

        public static System.Func<bool> AbilityLock;
        private static bool Locked(Bind b) => (b == Bind.AbilityQ || b == Bind.AbilityE || b == Bind.Ultimate) && AbilityLock != null && AbilityLock();
        private static float lockedPressAt = -10f;
        private static void LockedPress()
        {
            if (Time.unscaledTime - lockedPressAt < 0.4f) return;
            lockedPressAt = Time.unscaledTime;
            Art.Sfx.Play2D(Art.Sound.UiError, 0.5f);

            if (GameHud.FirstTime("silencedpress")) GameHud.Feed("SILENCED - your abilities return in a moment (Holy Water lifts it)", Enemies.CantorBrain.HushColor);
        }
        public static bool Up(Bind b) { var c = Control(b); var p = Pad(b); return (c != null && c.wasReleasedThisFrame) || (p != null && p.wasReleasedThisFrame); }

        public static ButtonControl Pad(Bind b)
        {
            var g = Gamepad.current;
            if (g == null) return null;
            switch (b)
            {
                case Bind.Jump: return g.buttonSouth;
                case Bind.Dodge: return g.buttonEast;
                case Bind.Use: return g.buttonWest;
                case Bind.Interact: return g.buttonNorth;
                case Bind.AbilityQ: return g.leftShoulder;
                case Bind.AbilityE: return g.rightShoulder;
                case Bind.Ultimate: return g.dpad.up;
                case Bind.Kick: return g.rightStickButton;
                case Bind.Potion1: return g.dpad.left;
                case Bind.Potion2: return g.dpad.right;
                case Bind.Potion4: return g.dpad.down;
                case Bind.Map: return g.selectButton;
                default: return null;
            }
        }

        public static bool PadActive { get; private set; }

        public static void Tick()
        {
            var g = Gamepad.current;
            if (g == null) { PadActive = false; return; }
            if ((g.leftStick.ReadValue().sqrMagnitude > 0.1f || g.rightStick.ReadValue().sqrMagnitude > 0.1f || g.rightTrigger.isPressed || g.leftTrigger.isPressed
                || g.buttonSouth.isPressed || g.buttonEast.isPressed || g.buttonWest.isPressed || g.buttonNorth.isPressed || g.leftShoulder.isPressed || g.rightShoulder.isPressed))
                PadActive = true;
            else if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) || (Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 9f || Mouse.current.leftButton.wasPressedThisFrame)))
                PadActive = false;
        }

        private static string PadLabel(Bind b)
        {
            switch (b)
            {
                case Bind.Jump: return "A";
                case Bind.Dodge: return "B";
                case Bind.Use: return "X";
                case Bind.Interact: return "Y";
                case Bind.AbilityQ: return "LB";
                case Bind.AbilityE: return "RB";
                case Bind.Ultimate: return "UP";
                case Bind.Kick: return "R3";
                case Bind.Sprint: return "L3";
                case Bind.Potion1: return "LEFT";
                case Bind.Potion2: return "RIGHT";
                case Bind.Potion4: return "DOWN";
                case Bind.Map: return "VIEW";
                default: return null;
            }
        }

        public static bool PrimaryHeld => BotPrimary || (Mouse.current != null && Mouse.current.leftButton.isPressed) || (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);
        public static bool PrimaryDown => botPrimaryFrame == Time.frameCount || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);

        public static bool SecondaryHeld => BotSecondary || (Mouse.current != null && Mouse.current.rightButton.isPressed) || (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed);
        public static bool SecondaryDown => botSecondaryFrame == Time.frameCount || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.leftTrigger.wasPressedThisFrame);
        public static bool PadSprintDown => Gamepad.current != null && Gamepad.current.leftStickButton.wasPressedThisFrame;
        public static bool PauseDown => Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;

        public static Vector2 PadMove => Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
        public static Vector2 PadLook
        {
            get
            {
                if (Gamepad.current == null) return Vector2.zero;
                var v = Gamepad.current.rightStick.ReadValue();

                return v * v.magnitude;
            }
        }

        public static void Set(Bind b, Key k)
        {
            var cur = Current;

            bool pair = b == Bind.Use || b == Bind.Interact;
            Bind other = b == Bind.Use ? Bind.Interact : Bind.Use;
            bool together = b == Bind.Use && Merged;
            for (int i = 0; i < cur.Length; i++)
                if (i != (int)b && cur[i] == k && !(pair && i == (int)other)) { cur[i] = cur[(int)b]; Save((Bind)i, cur[i]); }
            cur[(int)b] = k;
            Save(b, k);
            if (together) { cur[(int)other] = k; Save(other, k); }
            if (!Progression.Cosmetics.TestMode) Prefs.Save();
        }

        private static void Save(Bind b, Key k)
        {
            if (Progression.Cosmetics.TestMode) return;
            Prefs.SetInt("db.bind." + b, (int)k);
            if (b == Bind.Interact) Prefs.SetInt("db.bind.fmerge", 1);
        }

        public static void ResetAll()
        {
            current = null;
            if (Progression.Cosmetics.TestMode) return;
            for (int i = 0; i < Defaults.Length; i++) Prefs.DeleteKey("db.bind." + Defaults[i].bind);
            Prefs.Save();
        }

        public static string Label(Key k)
        {
            switch (k)
            {
                case Key.LeftCtrl: case Key.RightCtrl: return "CTRL";
                case Key.LeftShift: case Key.RightShift: return "SHIFT";
                case Key.LeftAlt: case Key.RightAlt: return "ALT";
                case Key.Space: return "SPACE";
                case Key.Tab: return "TAB";
                case Key.Backquote: return "`";
            }
            string s = k.ToString();
            if (s.StartsWith("Digit")) return s.Substring(5);
            if (s.StartsWith("Numpad")) return "NUM" + s.Substring(6);
            return s.ToUpperInvariant();
        }

        public static string Label(Bind b) => Label(KeyOf(b));

        public static Bind? ForDefaultLabel(string label)
        {
            foreach (var (bind, _, key) in Defaults) if (string.Equals(Label(key), label, System.StringComparison.OrdinalIgnoreCase)) return bind;
            return null;
        }

        public static bool AnyChanged { get { for (int i = 0; i < Defaults.Length; i++) if (Current[i] != Defaults[i].key) return true; return false; } }

        public static string Show(string defaultLabel)
        {
            if (string.IsNullOrEmpty(defaultLabel)) return defaultLabel;
            if (PadActive && defaultLabel == "LMB") return "RT";
            if (PadActive && defaultLabel == "RMB") return "LT";
            var b = ForDefaultLabel(defaultLabel);
            if (!b.HasValue) return defaultLabel;
            if (PadActive) { var p = PadLabel(b.Value); if (p != null) return p; }
            return Label(b.Value);
        }

        public static string Live(Bind b) => PadActive && PadLabel(b) != null ? PadLabel(b) : Label(b);

        private static readonly System.Text.RegularExpressions.Regex HintKey =
            new System.Text.RegularExpressions.Regex(@"(?<=^|\n|\u00b7  |   |, |/ )(Q|E|R|F|G|V|M|Z|H|I|X|B|T|Tab|TAB|CTRL|Shift|SHIFT|Space|SPACE|[1-4])(?=  | / | [a-z]| \(|$)", System.Text.RegularExpressions.RegexOptions.Multiline);

        private static readonly System.Text.RegularExpressions.Regex BracketKey =
            new System.Text.RegularExpressions.Regex(@"\((Q|E|R|F|G|V|M|Z|H|I|X|B|T|Tab|TAB|Ctrl|CTRL|Shift|SHIFT|Space|SPACE|[1-4])\)");

        public static string Rekey(string text)
        {
            if (string.IsNullOrEmpty(text) || (!AnyChanged && !PadActive)) return text;
            text = HintKey.Replace(text, m => Show(m.Value));

            text = BracketKey.Replace(text, m => "(" + Show(m.Groups[1].Value) + ")");
            return text.Replace("WASD", Label(Bind.MoveForward) + Label(Bind.MoveLeft) + Label(Bind.MoveBack) + Label(Bind.MoveRight));
        }
    }
}
