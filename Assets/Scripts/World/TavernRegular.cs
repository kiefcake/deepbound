using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.UI;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// OLD HOLLIS, the regular at the Gutted Lantern's bar (2026-11-08, the activities plan: "a drinker at the bar"). He
    /// leans on the counter with a tankard (<see cref="FolkIdle.WorkKind.Drink"/>) and, when someone stands near him in
    /// the tavern, says something: idle nonsense, the weather, and a toast to each lost party on the
    /// <see cref="MemorialBoard"/> (once a visit each). Local to every machine and on nobody's books: he sells nothing,
    /// gives no quests, holds no grudges.
    /// </summary>
    public class TavernRegular : MonoBehaviour
    {
        public const float Earshot = 5f;
        public const string Name = "Old Hollis";

        private static readonly string[] Idle =
        {
            "Forty years I've drunk here. Never once went down that hole. That's the secret. Write it down.",
            "Marta waters the mead on Tuesdays. Don't ask me how I know what day it is.",
            "Saw the Tallyman once, through the window. He was counting something. I hope it wasn't me.",
            "Corwin's horse winked at me. Dead horses shouldn't wink.",
            "My brother went down in the old days. Came back with a ring and no thumbs. Fair trade, he said.",
            "Hobb owes me a hinge. Six years now. I bring it up every winter.",
            "If the cat follows you home, that's luck. If you follow the cat, that's more luck. Don't follow the cat.",
            "One more and I'll tell you about the time I nearly went down. Nearly.",
            "The band's got four songs. I've requested a fifth every night since the war. Which war, you ask. Exactly.",
            "Delvers come in loud and go out quiet. The ones that come back, it's the other way round.",
        };

        private readonly HashSet<string> toasted = new HashSet<string>();
        private string current;
        private float until, nextAt;
        private int idleIndex;
        private bool wasInCamp;
        private FolkIdle idle;
        private static GUIStyle nameStyle, bodyStyle;

        /// <summary>Screenshots: say this now.</summary>
        public static void DebugSay(string text) { foreach (var r in FindObjectsByType<TavernRegular>(FindObjectsSortMode.None)) r.Say(text, 8f); }

        private void Start()
        {
            idle = GetComponent<FolkIdle>();
            idleIndex = Random.Range(0, Idle.Length);
            nextAt = Time.time + Random.Range(2f, 5f);
        }

        private void Say(string text, float seconds)
        {
            current = text;
            until = Time.time + seconds;
            nextAt = until + Random.Range(6f, 11f);
            GameHud.Heard?.Invoke("folk", Name + ": " + text);
        }

        private Vector3 Head => idle != null ? idle.HeadTop : transform.position + Vector3.up * 1.9f;

        private void Update()
        {
            bool inCamp = DungeonDirector.InLobby;
            if (inCamp && !wasInCamp) toasted.Clear(); // a new visit: he drinks to them all again
            wasInCamp = inCamp;
            var me = NetworkPlayer.Local;
            if (me == null || !inCamp || Time.time < nextAt || (current != null && Time.time < until)) return;
            if (Vector3.Distance(me.transform.position, transform.position) > Earshot || !Lobby.InTavern(me.transform.position)) return;
            Say(NextLine(me), 7f);
        }

        private string NextLine(NetworkPlayer me)
        {
            // A toast to the lost, once each a visit, before anything else.
            foreach (var line in MemorialBoard.DebugLines ?? Records.Fallen ?? new string[0])
            {
                if (string.IsNullOrEmpty(line) || !toasted.Add(line)) continue;
                int cut = line.IndexOf("  -  ", System.StringComparison.Ordinal);
                string names = cut > 0 ? line.Substring(0, cut) : line;
                string where = cut > 0 ? line.Substring(cut + 5) : "below";
                return "*Hollis lifts his tankard to the board on the wall.* To " + names + ". They " + where.Replace("fell", "went down") + ". Drink for them, delver.";
            }
            var debuffs = me.GetComponent<PlayerDebuffs>();
            if (debuffs != null && debuffs.Drunk >= 2 && Random.value < 0.5f) return "Look at you. Merry as a bishop. Sit down before the floor comes up to meet you.";
            if (CampWeather.Now == CampWeather.Kind.Rain && Random.value < 0.4f) return "Rain's good for Marta. Everyone ends up in here sooner or later. Even the folk who don't drink.";
            if (CampWeather.Now == CampWeather.Kind.Mist && Random.value < 0.4f) return "Fog's thick tonight. Things walk out of the pines in a fog. Usually Corwin.";
            idleIndex = (idleIndex + 1) % Idle.Length;
            return Idle[idleIndex];
        }

        private void OnGUI()
        {
            useGUILayout = false;
            if (current == null || Time.time > until) return;
            var cam = ViewCamera.Current;
            if (cam == null || NetworkPlayer.Local == null || PauseMenu.Open || GameHud.MenuOpen || GameHud.PanelShowing) return;
            float dist = Vector3.Distance(cam.transform.position, Head);
            if (dist > 12f) return;
            UiSkin.Apply();
            var screen = UiScale.World(cam, Head);
            if (screen.z <= 0f) return;
            float a = Mathf.Clamp01((until - Time.time) / 0.6f) * Mathf.Clamp01((12f - dist) / 3f);
            // The same speech card the camp folk use.
            bodyStyle ??= new GUIStyle(UiSkin.Small) { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter };
            nameStyle ??= new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            bodyStyle.fontStyle = current.StartsWith("*") ? FontStyle.Italic : FontStyle.Normal;
            bodyStyle.normal.textColor = new Color(0.95f, 0.92f, 0.85f, a);
            nameStyle.normal.textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, a);
            const float bw = 280f;
            float bh = bodyStyle.CalcHeight(new GUIContent(current), bw - 16f) + 26f;
            var r = new Rect(screen.x - bw / 2f, UiScale.H - screen.y - bh, bw, bh);
            UiSkin.Rect(r, new Color(0.04f, 0.035f, 0.03f, 0.78f * a));
            GUI.Label(new Rect(r.x + 8, r.y + 4, bw - 16, 18), Name.ToUpperInvariant(), nameStyle);
            GUI.Label(new Rect(r.x + 8, r.y + 22, bw - 16, bh - 24), current, bodyStyle);
        }
    }
}
