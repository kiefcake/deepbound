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
            "camp", "lore", "potions", "gamble2", "cat", "levers", "voice", "phases", "mourner", "pingwheel", "reliquary", "inventory", "tell", "bleed", "soul", "warden", "ult", "shield", "weak", "afflicted", "gargoyle", "pendulum", "ritual", "cistern", "mimic", "hoarder", "wheel", "bones", "tide", "lantern", "bell",
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
                foreach (var it in Interactable.All)
                {
                    if (it == null || Vector3.Distance(it.transform.position, me) > 6f) continue;
                    if (it is SlotMachine || it is BlackjackTable) Offer("gamble2", "THE TENT: the Bone Reels pay big for three of a kind; at Deadman's Hand beat the skeleton dealer to 21. HIT or STAND in the table's panel.");
                    if (it is CampCat) Offer("cat", "The castle cat. F pets it (sometimes it has found something for you). G picks it up... ask it first.");
                }
                Offer("camp", "CAMP: Silas sells potions, Hobb at the anvil sells embers and gear, the shrine offers relics. Temper your weapon at the anvil, take a contract at the bounty board and set an omen at the stone. When ready, talk to Corwin at the carriage (difficulty) and sit on its back (F): when everyone is seated, it sets off.");
                if (Vector3.Distance(me, Lobby.YardSpot) < 6f) Offer("drills", "SERGEANT BRANN drills you for gold (G): a stat for the run. Each drill costs more than the last.");
                if (Vector3.Distance(me, Lobby.RunewrightSpot) < 6f) Offer("runewright", "YSOLDE'S STONES (G): set runes, rank abilities with Sigils, and MELD three of the same rune into a stronger one.");
                if (Vector3.Distance(me, Lobby.CarriageStall) < 6f) Offer("corwin", "CORWIN knows the way down: a SCOUT'S MAP charts the next floor; SMELLING SALTS get you up on your own once.");
                if (PlayerLife.All.Count > 1) Offer("chat", "TEXT CHAT: press Enter to type to your party, Enter to send.");
                if (PlayerLife.All.Count > 1) Offer("purse", "PARTY LOOT is shared by default: gold and embers anyone picks up are split evenly. In the inventory (I) you can give gold, even out the purse, or ctrl-click a piece to hand it to the nearest ally.");
                return;
            }
            var layout = DungeonDirector.Layout;
            if (layout == null) return;

            var ult = player.GetComponent<Ultimates>();
            foreach (var pl in PlayerLife.All)
            {
                if (pl == null || pl.gameObject == player.gameObject) continue;
                if (pl.Downed.Value && !pl.Soul.Value) Offer("bleed", "An ally is DOWN: hold F beside them before they bleed out (40 s). If they do, carry their soul skull to the waystone in the start room.");
                if (pl.Soul.Value) Offer("soul", "A SOUL SKULL glows where an ally bled out. F to carry it (you move slower and cannot fight) to the start room's waystone. A heavy hit knocks it loose.");
            }
            if (PlayerVoice.MicAvailable && Settings.VoiceMode != 2)
                Offer("voice", "PROXIMITY VOICE is on: players near you hear you, muffled through walls. SHOUTING wakes the dead nearby. Options has push-to-talk (T).");
            if (DungeonDirector.IsWardenFloor(layout.Depth)) Offer("warden", "Each act's first floor ends at a WARDEN, the second at the act's master. Ten floors in all; the Unsung waits at the bottom.");
            var myKit = player.ActiveKit;
            if (myKit != null && myKit.AttackRange > 6f) Offer("falloff", "RANGED shots lose bite past half their range (60% at the very end), and arrows and knives DROP: aim a little high at distance.");
            if (Object.FindObjectsByType<EntlingBrain>(FindObjectsSortMode.None).Any(e => !e.IsDead && Vector3.Distance(e.transform.position, me) < 16f))
                Offer("entling", "ENTLINGS sprint at you and root you to the spot: you cannot move until it dies. Kill it on the way in, or turn and cut it off you.");
            Offer("pingwheel", "PINGS: tap Z (or middle mouse) to mark what you aim at. HOLD it for the wheel: Danger, On my way, Need help, Look.");
            var relic = Reliquary.Current;
            if (relic != null && !relic.Carried && Vector3.Distance(relic.transform.position, me) < 10f)
                Offer("reliquary", "A RELIQUARY. Carry it (F) to the waystone in the start room for gold, embers and maybe a Sigil for everyone. The carrier is slow and cannot fight, its hymn draws the dead, and every hit cracks it.");
            var progT = player.GetComponent<Progression.PlayerProgress>();
            if (progT != null && progT.BagCount > 0)
                Offer("inventory", "Gear you pick up goes in your PACK. Press I for the inventory: click to wear, shift-click to drop. Scrap it at Hobb's anvil.");
            foreach (var it in Interactable.All)
                if (it is Lever lv && !lv.Pulled.Value && Vector3.Distance(lv.transform.position, me) < 9f)
                    Offer("levers", "A VAULT LEVER. Three are bolted to walls in different rooms on this floor; pull all three (they stay down) to open the vault.");
            if (player.GetComponent<Health>().Fraction < 0.5f)
                Offer("potions", "You do not heal on your own down here. Press 1 to drink a Health Potion (your pouch holds 5); the dead drop more.");
            foreach (var it in Interactable.All)
                if (it is LorePage && Vector3.Distance(it.transform.position, me) < 8f)
                    Offer("lore", "A JOURNAL PAGE on a lectern. Press F to read it; every page you find is kept in your Journal (Esc > Guide), run after run.");
            if (ult != null && ult.Ready) Offer("ult", "Your ULTIMATE is charged: press R.");

            var deb = player.GetComponent<PlayerDebuffs>();
            if (deb != null && (deb.Poisoned || deb.Frostbitten || deb.Cursed))
                Offer("afflicted", "You are AFFLICTED. Holy Water (4) cleanses it; a curse also ends when its Hexer dies.");

            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, me);
                if (d > 14f) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (d < 9f && b != null && (b.Status.Value & 4) != 0) Offer("tell", "A red ! over an enemy means it is about to strike. Dodge (Ctrl), block, or step out of reach.");
                if (b is BossBrain bb && bb.Immune) Offer("phases", "The boss is IMMUNE for now. A WARDED boss stays immune until its guards die; SURVIVE means dodge until the timer runs out. The boss bar says which.");
                if (b is MournerBrain) Offer("mourner", "A MOURNER: it only moves while no one is looking at it. Keep your eyes on it, back away facing it, and listen for stone scraping behind you.");
                if (b is GravewardenBrain) Offer("warden", "THE GRAVEWARDEN: its shield blocks from the front. Hit it right after each Mace Sweep (OPENING), from behind, or while it is dazed from charging into stone.");
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
            int act = DungeonDirector.Act(layout.Depth);
            if (act == 0) Offer("bones", "THE OSSUARY: fallen skeletons pull themselves together. Walk over a bone pile to stamp it out.");
            if (act == 1) Offer("tide", "THE DROWNED VAULTS: when the water thickens it halves your speed (the dead barely slow). Get onto stairs, balconies or stones.");
            if (act == 2) Offer("lantern", "THE HOLLOW THRONE: smash a soul lantern to heal your party and stun the dead nearby.");
            if (act == 3) Offer("bell", "THE BLACK CATHEDRAL: when the bell tolls, JUMP the shockwave.");
        }

        /// <summary>Owner, from the HUD's OnGUI.</summary>
        public static void Draw(float w, float h)
        {
            // A boss's entrance has the screen to itself; the tip waits (its timer held) until it ends.
            if (GameHud.BossIntroShowing)
            {
                if (current != null) shownAt = Time.time;
                return;
            }
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
