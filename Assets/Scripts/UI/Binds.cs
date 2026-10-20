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
        Potion1, Potion2, Potion3, Potion4, PushToTalk,
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
            (Bind.PushToTalk, "Push to talk", Key.T),
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

        public static bool Down(Bind b) { var c = Control(b); return c != null && c.wasPressedThisFrame; }
        public static bool Held(Bind b) { var c = Control(b); return c != null && c.isPressed; }
        public static bool Up(Bind b) { var c = Control(b); return c != null && c.wasReleasedThisFrame; }

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
            var b = ForDefaultLabel(defaultLabel);
            return b.HasValue ? Label(b.Value) : defaultLabel;
        }

        private static readonly System.Text.RegularExpressions.Regex HintKey =
            new System.Text.RegularExpressions.Regex(@"(?<=^|\n|\u00b7  |   |, |/ )(Q|E|R|F|G|V|M|Z|H|I|X|B|T|Tab|TAB|CTRL|Shift|SHIFT|Space|SPACE|[1-4])(?=  | / | [a-z]| \(|$)", System.Text.RegularExpressions.RegexOptions.Multiline);

        /// <summary>
        /// Rewrites the key names in a fixed hint ("Q  Blink", "F   Pick up") to the current
        /// bindings. Leaves the text alone while nothing is rebound.
        /// </summary>
        public static string Rekey(string text)
        {
            if (string.IsNullOrEmpty(text) || !AnyChanged) return text;
            text = HintKey.Replace(text, m => Show(m.Value));
            return text.Replace("WASD", Label(Bind.MoveForward) + Label(Bind.MoveLeft) + Label(Bind.MoveBack) + Label(Bind.MoveRight));
        }
    }
}
