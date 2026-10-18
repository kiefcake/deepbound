using System.Collections.Generic;
using Deepbound.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// Text chat. Enter opens a line at the bottom left, Enter sends, Esc cancels. While you type, the
    /// game's keyboard device is switched off (IMGUI still gets the keystrokes), so W does not walk
    /// you off a ledge and Q does not fire your ability. Messages go owner -> server -> everyone
    /// (NetworkPlayer.ChatRpc); recent lines show for a while, the whole recent history while typing.
    /// </summary>
    public class ChatBox : MonoBehaviour
    {
        public static bool Typing { get; private set; }
        public const int MaxLength = 140;

        private struct Line { public string Who; public Color Color; public string Text; public float At; }
        private static readonly List<Line> lines = new List<Line>();
        private string draft = "";
        private bool focusNext;
        private float reopenAfter, enableKeysAt = -1f;
        private const float ShowSeconds = 12f;
        private const int Visible = 7;

        /// <summary>Every peer: a message arrived.</summary>
        public static void Add(string who, Color color, string text)
        {
            lines.Add(new Line { Who = who, Color = color, Text = text, At = Time.time });
            if (lines.Count > 40) lines.RemoveAt(0);
            if (NetworkPlayer.Local != null) Art.Sfx.Play2D(Art.Sound.Card, 0.25f);
        }

        /// <summary>Tests: the last message text, or null.</summary>
        public static string Last => lines.Count > 0 ? lines[lines.Count - 1].Text : null;
        public static bool Heard(string text) { foreach (var l in lines) if (l.Text == text) return true; return false; }

        private void Update()
        {
            // The keyboard comes back a beat after closing, once Enter or Esc is let go (so the same
            // press does not reopen the chat or open the pause menu).
            if (enableKeysAt > 0f && Time.time >= enableKeysAt)
            {
                enableKeysAt = -1f;
                if (Keyboard.current != null && !Keyboard.current.enabled) InputSystem.EnableDevice(Keyboard.current);
            }
            var me = NetworkPlayer.Local;
            if (me == null) { if (Typing) Close(); return; }
            if (Time.time < reopenAfter) return;
            // Open with Enter (or / ) when nothing else has the keyboard.
            var kb = Keyboard.current;
            if (!Typing && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.slashKey.wasPressedThisFrame)
                && !PauseMenu.Open && !CharacterSelect.Open && !Wardrobe.Open && !World.BlackjackTable.LocalOpen)
                Open(kb.slashKey.wasPressedThisFrame ? "/" : "");
        }

        private void Open(string start)
        {
            Typing = true;
            draft = start;
            focusNext = true;
            if (Keyboard.current != null) InputSystem.DisableDevice(Keyboard.current);
        }

        private void Close()
        {
            Typing = false;
            draft = "";
            enableKeysAt = Time.time + 0.2f;
            reopenAfter = Time.time + 0.35f;
        }

        private void OnDisable()
        {
            if (Typing) Close();
            if (Keyboard.current != null && !Keyboard.current.enabled) InputSystem.EnableDevice(Keyboard.current);
        }
        private void OnApplicationFocus(bool focus) { if (!focus && Typing) Close(); }

        private void OnGUI()
        {
            if (NetworkPlayer.Local == null) return;
            UiSkin.Apply();
            float w = Screen.width, h = Screen.height;
            float x = 18f, bottom = h - 168f, width = Mathf.Min(460f, w * 0.38f);
            GUI.depth = -3;

            // Recent lines (all of them, on a panel, while typing).
            int shown = 0;
            float now = Time.time;
            var body = new GUIStyle(UiSkin.Small) { wordWrap = true, fontSize = 14 };
            for (int i = lines.Count - 1; i >= 0 && shown < Visible; i--)
            {
                var l = lines[i];
                float age = now - l.At;
                if (!Typing && age > ShowSeconds) break;
                float alpha = Typing ? 1f : Mathf.Clamp01((ShowSeconds - age) / 2f);
                string text = l.Who + ":  " + l.Text;
                float lh = body.CalcHeight(new GUIContent(text), width - 12f);
                bottom -= lh + 2f;
                UiSkin.Rect(new Rect(x - 4, bottom - 1, width + 8, lh + 2), new Color(0f, 0f, 0f, (Typing ? 0.55f : 0.35f) * alpha));
                var nameStyle = new GUIStyle(body) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(l.Color.r, l.Color.g, l.Color.b, alpha) } };
                GUI.Label(new Rect(x, bottom, width - 12f, lh), l.Who + ":", nameStyle);
                GUI.Label(new Rect(x, bottom, width - 12f, lh), PadFor(l.Who, nameStyle, body) + l.Text, new GUIStyle(body) { normal = { textColor = new Color(0.93f, 0.9f, 0.84f, alpha) } });
                shown++;
            }

            if (!Typing) return;
            // The input line.
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                var text = draft.Trim();
                if (text.Length > 0) NetworkPlayer.Local.ChatRpc(text.Length > MaxLength ? text.Substring(0, MaxLength) : text);
                e.Use();
                Close();
                return;
            }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); Close(); return; }
            var field = new Rect(x - 4, h - 162f, width + 8, 26f);
            UiSkin.Rect(field, new Color(0f, 0f, 0f, 0.75f));
            GUI.Label(new Rect(x, h - 160f, 60f, 22f), "Say:", new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } });
            GUI.SetNextControlName("ChatField");
            draft = GUI.TextField(new Rect(x + 40f, h - 160f, width - 40f, 22f), draft, MaxLength, new GUIStyle(GUI.skin.textField) { fontSize = 14 });
            if (focusNext) { GUI.FocusControl("ChatField"); focusNext = false; }
            GUI.Label(new Rect(x, h - 134f, width, 18f), "Enter to send   ·   Esc to cancel", new GUIStyle(UiSkin.Small) { fontSize = 11, normal = { textColor = new Color(1f, 1f, 1f, 0.45f) } });
        }

        /// <summary>Spaces that push the message text past the bold name.</summary>
        private static string PadFor(string who, GUIStyle name, GUIStyle body)
        {
            float target = name.CalcSize(new GUIContent(who + ":  ")).x;
            float space = Mathf.Max(1f, body.CalcSize(new GUIContent(" ")).x);
            return new string(' ', Mathf.CeilToInt(target / space));
        }
    }
}
