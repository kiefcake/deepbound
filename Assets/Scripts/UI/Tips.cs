using System.Linq;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Teach by play: the first time a player meets a mechanic, one short tip says what it is
    /// and what to do about it — then never again on this machine (Options can reset them,
    /// or turn them off). One tip at a time, queued, each on screen for a few seconds.
    /// </summary>
    public static class Tips
    {
        public static readonly string[] Keys =
        {
            "camp", "ult", "shield", "weak", "afflicted", "gargoyle", "pendulum", "ritual", "cistern", "mimic", "hoarder", "wheel", "bones", "tide", "lantern", "bell",
        };

        private static readonly System.Collections.Generic.HashSet<string> sessionSeen = new System.Collections.Generic.HashSet<string>();
        private static string current;
        private static float shownAt = -100f, nextCheck;
        private const float Duration = 7f;
        private static readonly System.Collections.Generic.Queue<(string key, string text)> queue = new System.Collections.Generic.Queue<(string, string)>();

        private static void Offer(string key, string text)
        {
            if (!Settings.ShowTips || sessionSeen.Contains(key) || Settings.TipSeen(key)) return;
            sessionSeen.Add(key);
            // Automated tests show tips but never mark them seen in the real save.
            if (!DungeonDirector.TestMode) Settings.MarkTipSeen(key);
            queue.Enqueue((key, text));
        }

        /// <summary>Owner, from the HUD: look around for anything worth a first-time tip.</summary>
        public static void Check(NetworkPlayer player)
        {
            if (Time.time < nextCheck || player == null) return;
            nextCheck = Time.time + 0.5f;
            var me = player.transform.position;

            if (DungeonDirector.InLobby)
            {
                Offer("camp", "CAMP: trade at the merchant, temper your weapon at the anvil, take a contract at the bounty board and set an omen at the stone. Board the carriage when ready.");
                return;
            }
            var layout = DungeonDirector.Layout;
            if (layout == null) return;

            var ult = player.GetComponent<Ultimates>();
            if (ult != null && ult.Ready) Offer("ult", "Your ULTIMATE is charged: press R.");

            var deb = player.GetComponent<PlayerDebuffs>();
            if (deb != null && (deb.Poisoned || deb.Frostbitten || deb.Cursed))
                Offer("afflicted", "You are AFFLICTED. Holy Water (3) cleanses it; a curse also ends when its Hexer dies.");

            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, me);
                if (d > 14f) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (b is ShieldbearerBrain sb && sb.ShieldUp) Offer("shield", "SHIELDBEARER: its shield blocks from the front. KICK it (V) or Shield Bash to knock the shield aside, or get behind it.");
                else if (b is GargoyleBrain g && g.Woken.Value) Offer("gargoyle", "GARGOYLE: it dives along a marked line, then lands exposed. Sidestep, then punish.");
                else if (b is HoarderBrain) Offer("hoarder", "BONE HOARDER: it runs with a sack of gold. Every hit spills coins; hit the sack for more. It escapes in 25 s.");
                else if (b is MimicBrain) Offer("mimic", "MIMIC! Kill it for a chest's worth of loot - and watch for lids that breathe.");
                if (d < 10f && h.GetComponentInChildren<WeakPoint>() != null)
                    Offer("weak", "That GLOWING SPOT is a weak point: aim there for double damage. Headshots deal extra too.");
            }

            var room = layout.RoomAt(me, 0f);
            if (room != null)
            {
                if (room.Feature == RoomFeature.Pendulums) Offer("pendulum", "PENDULUM HALL: the blades cut the dead as well as you. Time your crossing - and kick enemies into them.");
                if (room.Feature == RoomFeature.Ritual && DungeonDirector.InRitualWard(room.Center)) Offer("ritual", "RITUAL CHAMBER: the violet braziers ward the dead and raise more. Smash all three.");
                if (room.Feature == RoomFeature.Cistern) Offer("cistern", "CISTERN: the water slows everyone - but frost freezes anything standing in it.");
            }
            if (Object.FindObjectsByType<FateWheel>(FindObjectsSortMode.None).Any(w => !w.Spun.Value && Vector3.Distance(w.transform.position, me) < 9f))
                Offer("wheel", "WHEEL OF FATE: one spin for the whole party. It usually gives... usually.");
            if (layout.Depth == 0) Offer("bones", "THE OSSUARY: fallen skeletons pull themselves together. Walk over a bone pile to stamp it out.");
            if (layout.Depth == 1) Offer("tide", "THE DROWNED VAULTS: when the tide surges, get onto stairs, balconies or stones.");
            if (layout.Depth == 2) Offer("lantern", "THE HOLLOW THRONE: smash a soul lantern to heal your party and stun the dead nearby.");
            if (layout.Depth == 3) Offer("bell", "THE BLACK CATHEDRAL: when the bell tolls, JUMP the shockwave.");
        }

        /// <summary>Owner, from the HUD's OnGUI.</summary>
        public static void Draw(float w, float h)
        {
            if (current == null || Time.time - shownAt > Duration)
            {
                current = null;
                if (queue.Count == 0) return;
                current = queue.Dequeue().text;
                shownAt = Time.time;
                Sfx.Play2D(Sound.Relic, 0.3f);
            }
            float t = Time.time - shownAt;
            float a = Mathf.Clamp01(t * 4f) * Mathf.Clamp01((Duration - t) * 2f);
            float pw = Mathf.Min(620f, w - 40f);
            var style = new GUIStyle(UiSkin.Body) { fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.92f, 0.85f, a) } };
            float ph = style.CalcHeight(new GUIContent(current), pw - 40f) + 34f;
            var r = new Rect(w / 2f - pw / 2f, 104f, pw, ph);
            UiSkin.Panel(r, 0.85f * a);
            GUI.Label(new Rect(r.x + 20, r.y + 6, pw - 40, 18), "TIP", new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, a) } });
            GUI.Label(new Rect(r.x + 20, r.y + 24, pw - 40, ph - 30), current, style);
        }
    }
}
