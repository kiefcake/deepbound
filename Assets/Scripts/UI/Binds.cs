using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Deepbound.UI
{
    /// <summary>The rebindable actions. Append only: names are saved.</summary>
    public enum Bind
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Jump, Sprint, Dodge, Slide,
        AbilityQ, AbilityE, Ultimate, Kick, Use, Interact, Emote, Inventory, Map, Ping, Scrap, Stats, Class,
        Potion1, Potion2, Potion3, Potion4, PushToTalk, Rare,
    }

    /// <summary>
    /// Key rebinding (2026-10-20): every gameplay key is read through here, so a player can move any of
    /// them in Options > Controls. Bindings are saved on this machine (PlayerPrefs). Escape, chat, Alt
    /// and the developer keys stay where they are.
    /// </summary>
    public static class Binds
    {
        public static readonly (Bind bind, string label, Key key)[] Defaults =
        {
            (Bind.MoveForward, "Move forward", Key.W), (Bind.MoveBack, "Move back", Key.S), (Bind.MoveLeft, "Move left", Key.A), (Bind.MoveRight, "Move right", Key.D),
            (Bind.Jump, "Jump", Key.Space), (Bind.Sprint, "Sprint", Key.LeftShift), (Bind.Dodge, "Dodge", Key.LeftCtrl), (Bind.Slide, "Slide", Key.C),
            (Bind.AbilityQ, "Ability 1", Key.Q), (Bind.AbilityE, "Ability 2", Key.E), (Bind.Ultimate, "Ultimate", Key.R), (Bind.Kick, "Kick", Key.V),
            (Bind.Use, "Use / pick up", Key.F), (Bind.Interact, "Talk / trade / stow", Key.G), (Bind.Emote, "High five / reply", Key.H),
            (Bind.Inventory, "Inventory", Key.I), (Bind.Map, "Map", Key.M), (Bind.Ping, "Ping", Key.Z), (Bind.Scrap, "Scrap", Key.X),
            (Bind.Stats, "Stat sheet (hold)", Key.Tab), (Bind.Class, "Class screen", Key.B),
            (Bind.Potion1, "Health potion", Key.Digit1), (Bind.Potion2, "Mana potion", Key.Digit2), (Bind.Potion3, "Frost flask", Key.Digit3), (Bind.Potion4, "Holy water", Key.Digit4),
            (Bind.PushToTalk, "Push to talk", Key.T), (Bind.Rare, "Rare draught / tome", Key.Digit5),
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
                    // Tests run on the defaults and never read or write the player's bindings.
                    int saved = Progression.Cosmetics.TestMode ? (int)Defaults[i].key : PlayerPrefs.GetInt("db.bind." + Defaults[i].bind, (int)Defaults[i].key);
                    current[i] = System.Enum.IsDefined(typeof(Key), saved) && saved != (int)Key.None ? (Key)saved : Defaults[i].key;
                }
                return current;
            }
        }

        public static Key KeyOf(Bind b) => Current[(int)b];

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
            bool down = (c != null && c.wasPressedThisFrame) || (p != null && p.wasPressedThisFrame);
            if (down && Locked(b)) { LockedPress(); return false; }
            return down;
        }
        public static bool Held(Bind b) { var c = Control(b); var p = Pad(b); return !Locked(b) && ((c != null && c.isPressed) || (p != null && p.isPressed)); }

        /// <summary>Set by the local player's afflictions: true while Q, E and the ultimate are SILENCED.</summary>
        public static System.Func<bool> AbilityLock;
        private static bool Locked(Bind b) => (b == Bind.AbilityQ || b == Bind.AbilityE || b == Bind.Ultimate) && AbilityLock != null && AbilityLock();
        private static float lockedPressAt = -10f;
        private static void LockedPress()
        {
            if (Time.unscaledTime - lockedPressAt < 0.4f) return;
            lockedPressAt = Time.unscaledTime;
            Art.Sfx.Play2D(Art.Sound.UiError, 0.5f);
            GameHud.Feed("SILENCED - your abilities return in a moment (Holy Water lifts it)", Enemies.CantorBrain.HushColor);
        }
        public static bool Up(Bind b) { var c = Control(b); var p = Pad(b); return (c != null && c.wasReleasedThisFrame) || (p != null && p.wasReleasedThisFrame); }

        // ---- Gamepad (2026-10-20): play with a pad; the menus still want the mouse ----
        //   left stick move, right stick look, A jump, B dodge (slide at a sprint), X use, Y talk/trade,
        //   LB / RB abilities, RT attack, LT secondary (block, frostbolt, beam...), L3 sprint (toggle),
        //   R3 kick, d-pad up ultimate, left health potion, right mana potion, down holy water,
        //   View the map, Start pauses.

        /// <summary>The pad button for an action, or null (unmapped, or no pad).</summary>
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

        /// <summary>True while the pad is what you last played with: hints name pad buttons.</summary>
        public static bool PadActive { get; private set; }

        /// <summary>Each frame (the local motor): which device did the player last touch?</summary>
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

        /// <summary>The main attack: left mouse or the right trigger.</summary>
        public static bool PrimaryHeld => (Mouse.current != null && Mouse.current.leftButton.isPressed) || (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);
        public static bool PrimaryDown => (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
        /// <summary>The secondary: right mouse or the left trigger.</summary>
        public static bool SecondaryHeld => (Mouse.current != null && Mouse.current.rightButton.isPressed) || (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed);
        public static bool SecondaryDown => (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) || (Gamepad.current != null && Gamepad.current.leftTrigger.wasPressedThisFrame);
        public static bool PadSprintDown => Gamepad.current != null && Gamepad.current.leftStickButton.wasPressedThisFrame;
        public static bool PauseDown => Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;

        public static Vector2 PadMove => Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
        public static Vector2 PadLook
        {
            get
            {
                if (Gamepad.current == null) return Vector2.zero;
                var v = Gamepad.current.rightStick.ReadValue();
                // A gentle curve: fine aim near the centre, a fast turn at full tilt.
                return v * v.magnitude;
            }
        }

        /// <summary>Set a binding. A key already used elsewhere swaps over, so nothing is left unbound.</summary>
        public static void Set(Bind b, Key k)
        {
            var cur = Current;
            for (int i = 0; i < cur.Length; i++)
                if (i != (int)b && cur[i] == k) { cur[i] = cur[(int)b]; Save((Bind)i, cur[i]); }
            cur[(int)b] = k;
            Save(b, k);
            if (!Progression.Cosmetics.TestMode) PlayerPrefs.Save();
        }

        private static void Save(Bind b, Key k)
        {
            if (!Progression.Cosmetics.TestMode) PlayerPrefs.SetInt("db.bind." + b, (int)k);
        }

        public static void ResetAll()
        {
            current = null;
            if (Progression.Cosmetics.TestMode) return;
            for (int i = 0; i < Defaults.Length; i++) PlayerPrefs.DeleteKey("db.bind." + Defaults[i].bind);
            PlayerPrefs.Save();
        }

        /// <summary>A short label for a key, as the HUD shows it ("Q", "SPACE", "CTRL", "1").</summary>
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

        /// <summary>The bind a default key letter stands for in a hint ("G", "F", "Q"...), or null.</summary>
        public static Bind? ForDefaultLabel(string label)
        {
            foreach (var (bind, _, key) in Defaults) if (string.Equals(Label(key), label, System.StringComparison.OrdinalIgnoreCase)) return bind;
            return null;
        }

        /// <summary>True once anything differs from the defaults (hint text is only rewritten then).</summary>
        public static bool AnyChanged { get { for (int i = 0; i < Defaults.Length; i++) if (Current[i] != Defaults[i].key) return true; return false; } }

        /// <summary>A default key label as it is bound now: Show("G") is "G" until Interact moves.</summary>
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

        /// <summary>The live label for an action: the pad button while the pad is in use, else the key.</summary>
        public static string Live(Bind b) => PadActive && PadLabel(b) != null ? PadLabel(b) : Label(b);

        private static readonly System.Text.RegularExpressions.Regex HintKey =
            new System.Text.RegularExpressions.Regex(@"(?<=^|\n|\u00b7  |   |, |/ )(Q|E|R|F|G|V|M|Z|H|I|X|B|T|Tab|TAB|CTRL|Shift|SHIFT|Space|SPACE|[1-4])(?=  | / | [a-z]| \(|$)", System.Text.RegularExpressions.RegexOptions.Multiline);

        /// <summary>
        /// Rewrites the key names in a fixed hint ("Q  Blink", "F   Pick up") to the current
        /// bindings. Leaves the text alone while nothing is rebound.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex BracketKey =
            new System.Text.RegularExpressions.Regex(@"\((Q|E|R|F|G|V|M|Z|H|I|X|B|T|Tab|TAB|Ctrl|CTRL|Shift|SHIFT|Space|SPACE|[1-4])\)");

        public static string Rekey(string text)
        {
            if (string.IsNullOrEmpty(text) || (!AnyChanged && !PadActive)) return text;
            text = HintKey.Replace(text, m => Show(m.Value));
            // Prose names keys in brackets: "KICK it (V)", "Holy Water (4)".
            text = BracketKey.Replace(text, m => "(" + Show(m.Groups[1].Value) + ")");
            return text.Replace("WASD", Label(Bind.MoveForward) + Label(Bind.MoveLeft) + Label(Bind.MoveBack) + Label(Bind.MoveRight));
        }
    }
}
