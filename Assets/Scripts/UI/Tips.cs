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
            "rift", "shieldcharge", "blink", "repulse", "breach", "bargain", "cellar", "jam", "choke",
            "flamevents", "expedition", "edges", "relictray", "hone", "purse", "falloff", "entling", "rare", "necro", "hiddenplates", "gravewarden", "silenced",
            "camp", "quests", "tavern", "emotewheel", "potions", "gamble2", "levers", "voice", "phases", "mourner", "pingwheel", "reliquary", "inventory", "bleed", "soul", "shield", "weak", "afflicted", "gargoyle", "pendulum", "ritual", "cistern", "hoarder", "bones", "tide", "lantern", "bell", "altar", "pyre", "gauntlet", "treasury", "beggar", "proving", "armoury", "mirror", "orbweaver", "piper", "graze", "tallyman", "oath", "acolyte", "abysseye", "lightless", "shelter", "cantor", "magus", "lamprey", "hag", "bellringer", "blinded", "accursed", "dazed", "decree", "stagger", "brand", "eclipse", "teamwork", "soulbound", "casino3", "poker", "questfloor", "oil", "chandelier", "pillar", "vigil", "prisoner", "watchers", "chimes", "fishing", "knives", "bells", "wrestle", "mirrors", "scales", "counter", "draught", "eruption", "spikes",
        };

        private static readonly System.Collections.Generic.HashSet<string> sessionSeen = new System.Collections.Generic.HashSet<string>();
        private static string current;
        private static float shownAt = -100f, nextCheck;
        private const float Duration = 7f;
        private static readonly System.Collections.Generic.Queue<(string key, string text, bool camp, float at)> queue = new System.Collections.Generic.Queue<(string, string, bool, float)>();
        /// <summary>Seconds of quiet after a tip before the next one (they used to arrive back to back).</summary>
        private const float Gap = 9f, MaxWait = 25f;
        private static bool currentCamp;

        /// <summary>
        /// The tips that always show (2026-11-14, the user: "we are giving way too many tips, some things should be left to
        /// the player to figure out"): controls, what is being done to you, co-op rescue, and the few rules that kill without a
        /// tell. Everything else - enemies, guardians, rooms, traps, puzzles, secrets, finds, combat finesse - is for the
        /// player to work out (telegraphs, prompts, the Bestiary and the Guide still say it). Options can turn the rest back on.
        /// </summary>
        public static readonly System.Collections.Generic.HashSet<string> Essential = new System.Collections.Generic.HashSet<string>
        {
            "camp", "quests", "questfloor", "potions", "inventory", "relictray", "rare", "pingwheel", "emotewheel", "purse", "voice",
            "bleed", "soul", "reliquary",
            "afflicted", "accursed", "silenced", "blinded", "dazed",
            "tallyman", "mourner", "phases",
            "fishing", "knives", "wrestle", "bells", "poker", "tavern",
        };

        private static void Offer(string key, string text)
        {
            if (!Settings.AllTips && !Essential.Contains(key)) return;
            if (!Settings.ShowTips || sessionSeen.Contains(key) || (Settings.TipSeen(key) && !Progression.Cosmetics.TestMode)) return;
            sessionSeen.Add(key);
            queue.Enqueue((key, text, DungeonDirector.InLobby, Time.time));
        }

        /// <summary>A first-time tip raised by an event (a kit, a station) rather than by looking around.</summary>
        public static void OfferNow(string key, string text) => Offer(key, text);

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
                    if (it is MysteryCoffer) Offer("casino3", "LOCKSMITH'S COFFER: each lock you pick improves the prize; a snapped pick loses everything. Step back at any time to keep your winnings.");
                    if (it is PokerDiceTable) Offer("poker", "SKULL POKER: dice against other players. Sit (F), ante, hold and reroll once, then check, bet, call or fold. The best hand wins the pot.");
                    if (it is SlotMachine || it is BlackjackTable) Offer("gamble2", "THE TENT: the Bone Reels pay out on three of a kind. At Deadman's Hand, beat the skeleton dealer to 21.");
                    if (it is TavernKeep || it is TavernBand) Offer("tavern", "THE GUTTED LANTERN: talk to Marta (F) for mead. Each drink makes you braver in combat but less steady on your feet. F at the stage requests a song.");
                    if (it is WellFishing) Offer("fishing", "THE WELL: press F to cast, wait for the float to dip, then press F quickly to reel in.");
                    if (it is ArmWrestle) Offer("wrestle", "ARM WRESTLING: hold SPACE to strain against Big Gorm; it drains your grip. Rest while he leans, brace when he gathers (!), and push when he tires.");
                    if (it is BellFrame) Offer("bells", "THE BELLS: take a rope (F) and ask Aldric for a peal. Ring 1-4 as each strike reaches the line. A clean peal blesses everyone who rang for the next floor.");
                    if (it is KnifeBoard) Offer("knives", "THE KNIFE BOARD: press F at the line, then F again to throw when the mark is on the bull. Three knives per round; Brann pays once per visit for 24 or more.");
                }
                Offer("camp", "CAMP: look at anyone and press F to talk or trade. M shows where everyone is. Board Corwin's carriage (F) to descend.");
                Offer("quests", "QUESTS: ask the camp folk for work or take a notice from the board (up to three). Completing one earns that person's favour: better prices and special jobs.");
                var prog = player.GetComponent<Progression.PlayerProgress>();
                if (prog != null && prog.OpenEdgeSlot >= 0) Offer("edges", "AN EDGE: your weapon has reached +" + Progression.Edges.Levels[prog.OpenEdgeSlot] + ". At the anvil (F) forge one of three edges into it - each makes it better at a different way of fighting. Tempering waits until you choose.");
                if (prog != null && prog.RelicCount >= Loot.Relics.MaxCarried) Offer("relictray", "RELIC TRAY FULL (8): new relics now empower one you carry (II, III) instead.");
                if (prog != null && prog.WeaponLevel.Value >= Anvil.MaxLevel && prog.Hones.Value == 0) Offer("hone", "FULLY TEMPERED: Hobb can still hone your weapon at the anvil, up to five times, for +3% damage each.");
                if (PlayerLife.All.Count > 1 && PlayerVoice.MicAvailable && Settings.VoiceMode != 2)
                    Offer("voice", "PROXIMITY VOICE: nearby players hear you, muffled through walls. Shouting draws nearby enemies. Push-to-talk (T) is in Options.");
                if (PlayerLife.All.Count > 1) Offer("pingwheel", "GIVING: look at a nearby ally and hold G to give potions, gold, embers or your rare item.");
                if (PlayerLife.All.Count > 1) Offer("emotewheel", "EMOTES: tap H to wave (two players waving High Five). Hold H for the emote wheel.");
                if (PlayerLife.All.Count > 1) Offer("purse", "SHARED LOOT: gold and embers are split evenly across the party. Open the inventory (I) to give gold or hand gear to an ally.");
                return;
            }
            var layout = DungeonDirector.Layout;
            if (layout == null) return;

            foreach (var pl in PlayerLife.All)
            {
                if (pl == null || pl.gameObject == player.gameObject) continue;
                if (pl.Downed.Value && !pl.Soul.Value) Offer("bleed", "ALLY DOWN: hold F beside them to revive within 40 s. If they bleed out, carry their soul skull to the start room's waystone.");
                if (pl.Soul.Value) Offer("soul", "SOUL SKULL: carry it (F) to the start room's waystone to revive its owner. You move slower and cannot fight while carrying it.");
            }
            // Voice and pings are for a party: alone they mean nothing.
            bool party = PlayerLife.All.Count > 1;
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            {
                if (b == null || Vector3.Distance(b.transform.position, me) > 9f) continue;
                if (b.Type.Value == Breakable.Kind.OilJar || b.Type.Value == Breakable.Kind.FireBowl)
                    Offer("oil", "OIL JARS spill a flammable slick. Any fire ignites it: lure enemies in and stay clear.");
                if (b.Type.Value == Breakable.Kind.CrackedPillar)
                    Offer("pillar", "CRACKED PILLAR: break it to topple it away from your last hit, crushing anything in its path. The rubble remains as cover.");
            }
            foreach (var c in Chandelier.FindObjectsByType<Chandelier>(FindObjectsSortMode.None))
                if (c != null && !c.Fallen.Value && Vector3.Distance(c.HangAt.Value, me) < 12f)
                    Offer("chandelier", "CHANDELIER: press F at the wall cleat to drop it on anything below, crushing enemies and staggering bosses.");
            foreach (var v in VigilCandle.All)
                if (v != null && !v.Kept.Value && Vector3.Distance(v.transform.position, me) < 10f)
                { Offer("vigil", "VIGIL CANDLES: light all four (F) to be healed and blessed for the floor. Enemies snuff any lit candle they pass."); break; }
            foreach (var pc in Object.FindObjectsByType<PrisonerCage>(FindObjectsSortMode.None))
                if (pc != null && Vector3.Distance(pc.transform.position, me) < 10f)
                { Offer("prisoner", "PRISONER: free them (F) to gain an ally for the floor. Occasionally, the prisoner is not what it seems."); break; }
            var questDir = DungeonDirector.Instance;
            if (questDir != null)
                foreach (var q in questDir.Quests)
                    if (q.Status == DungeonDirector.ContractStatus.Active && (q.Contract == Contract.Cargo || q.Contract == Contract.Gather || q.Contract == Contract.Mark))
                    { Offer("questfloor", "QUEST ON THIS FLOOR: carry cargo into the guardian's arena before it seals, hold finds when the lift rises, and slay named marks."); break; }
            var myKit = player.ActiveKit;
            if (myKit != null && myKit.AttackRange > 6f) Offer("falloff", "RANGED ATTACKS deal less damage beyond half range and drop over distance. Aim slightly high at long range.");
            if (EnemyBrain.All.Any(e => e is EntlingBrain && !e.IsDead && Vector3.Distance(e.transform.position, me) < 16f))
                Offer("entling", "ENTLINGS root you in place until they die. Kill them before they reach you.");
            if (Health.All.Any(hh => hh != null && hh.IsAlive && hh.Team == Team.Enemies && (hh.transform.position - me).sqrMagnitude < 400f && hh.TryGetComponent<EnemyBrain>(out var sb) && sb.TwinId.Value != 0))
                Offer("soulbound", "SOULBOUND TWINS, joined by a pale thread: strike one down and it only falls - it gets up again unless its twin dies within a few seconds. " + (party ? "Split up: one each, and finish them together." : "Bring one low, then the other, and finish them back to back."));
            var relic = Reliquary.Current;
            if (relic != null && !relic.Carried && Vector3.Distance(relic.transform.position, me) < 10f)
                Offer("reliquary", "RELIQUARY: carry it (F) to the start room's waystone to revive the fallen, reward everyone and weaken the guardian. The carrier is slow, cannot fight, and attracts enemies.");
            var progT = player.GetComponent<Progression.PlayerProgress>();
            if (progT != null && progT.BagCount > 0)
                Offer("inventory", "Gear equips automatically into empty slots; the rest goes to your pack. Press I to manage it; scrap spares at Hobb's anvil.");
            foreach (var it in Interactable.All)
                if (it is Lever lv && !lv.Pulled.Value && Vector3.Distance(lv.transform.position, me) < 9f)
                    Offer("levers", "VAULT LEVER: pull all three on this floor to open the vault.");
            // Taught on the first real wound, while there is still a potion to drink.
            if (player.GetComponent<Health>().Fraction < 0.7f)
            {
                var pouch = player.GetComponent<Flasks>();
                if (pouch != null && pouch.Count(Flasks.Kind.Health) > 0)
                    Offer("potions", "Press 1 to drink a Health Potion. Potions are your main source of healing in combat.");
            }
            var rarePouch = player.GetComponent<Flasks>();
            if (rarePouch != null && rarePouch.RareHeld != Progression.RareItem.None)
                Offer("rare", "A RARE " + (Progression.RareItems.IsTome(rarePouch.RareHeld) ? "TOME" : "DRAUGHT") + " sits in your pouch's fifth slot: press 5 to use it. One at a time.");

            if (PlayerLife.All.Count > 1 && !DungeonDirector.InLobby)
                Offer("teamwork", "PINCER: striking an enemy opposite an ally who just hit it deals 15% more damage.");
            var knight = player.GetComponent<KnightKit>();
            if (knight != null && knight.ShieldRaised && PlayerLife.All.Count > 1)
                Offer("shelter", "SHIELD RAISED: allies standing directly behind you take half damage from attacks your shield faces.");
            var deb = player.GetComponent<PlayerDebuffs>();
            if (deb != null && (deb.Poisoned || deb.Frostbitten || deb.Cursed))
                Offer("afflicted", "AFFLICTED: Holy Water (3) cleanses it. Weakness also ends when the Hexer dies.");
            foreach (var it in Interactable.All)
                if (it is ExpeditionRemains er && !er.Searched.Value && Vector3.Distance(er.transform.position, me) < 9f)
                { Offer("expedition", "THE FIRST EXPEDITION: four of its members lie below, one in each of the first four acts. Search them (F) and bring them home."); break; }
            if (HearthBrazier.All.Count > 0)
                foreach (var hb in HearthBrazier.All)
                    if (hb != null && !hb.Lit.Value && Vector3.Distance(hb.transform.position, player.transform.position) < 9f)
                    { Offer("hearth", "HEARTH BRAZIER: light it (F) to illuminate the room, thaw frost, heal between fights, and burn enemies knocked into it."); break; }
            if (deb != null && deb.Blinded)
                Offer("blinded", "BLINDED: your map and enemy health bars are hidden. Avoid the ink pools; Holy Water (3) clears it.");
            if (deb != null && deb.Accursed)
                Offer("accursed", "CURSED: you take 40% more damage until it expires or the Withering Hag dies. Holy Water (3) removes it."); 
            if (deb != null && deb.Dazed)
                Offer("dazed", "DAZED: you turn at half speed for a moment. Block or dodge heavy attacks to avoid it.");
            if (deb != null && deb.Silenced)
                Offer("silenced", "SILENCED: you cannot use Q, E or your ultimate for a few seconds. Avoid the Cantor's circles; Holy Water removes it.");

            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, me);
                if (d > 14f) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (b == null) continue; // a rift: a body with no brain
                if (b is BossBrain bb && bb.Immune) Offer("phases", "The boss is IMMUNE. Warded bosses stay immune until their guards die; during SURVIVE phases, dodge until the timer ends.");
                if (b is AbyssEyeBrain) Offer("abysseye", "ABYSSAL EYE: step off the violet line before its beam fires. The iris is its weak point.");
                if (b is ChoirAcolyteBrain) Offer("acolyte", "CHOIR ACOLYTE: when its hymn completes, nearby enemies are Shielded. Stun it or hit it hard to interrupt the song.");
                if (b is BossBrain staggered && staggered.Stagger.Value > 0.2f) Offer("stagger", "BREAK METER: weak points, headshots, stuns and kicks fill the gold bar under the boss's health. When full, the boss is Broken and takes 50% more damage.");
                if (b is CardinalBrain) Offer("brand", "EXCOMMUNICATED: the Pale Cardinal's brand bursts and burns everyone nearby. The branded player should move away from the party or dodge through the burst.");
                if (b is UnsungBrain) Offer("eclipse", "THE UNSUNG uses the attacks of guardians you have slain. When pools of dawn open, stand in the light to survive the Eclipse.");
                if (b is NecromancerBrain) Offer("necro", "NECROMANCER: heals nearby enemies and raises more. Kill it first, or stun it while its circle glows.");
                if (b is HollowKingBrain hk && hk.DecreesFallen > 0) Offer("decree", "THE HOLLOW KING'S DECREES: BE STILL strikes anyone moving when it falls. BEGONE strikes anyone in his sight: break line of sight.");
                if (b.Recovering) Offer("punish", "After attacking, enemies slump briefly (weapon low, eyes dim). Hit them then for 20% more damage (Punish), and step back before their next attack.");
                if (b is RimeMagusBrain) Offer("magus", "RIME MAGUS: its blizzard follows you, slowing and freezing. Leave the storm or kill the Magus to end it.");
                if (b is InkLampreyBrain) Offer("lamprey", "INK LAMPREY: it spits ink where you are heading. Getting caught Blinds you. Sidestep the dark circle.");
                if (b is WitheringHagBrain) Offer("hag", "WITHERING HAG: her Evil Eye targets a fixed lane. Getting caught Curses you (40% more damage taken) until she dies.");
                if (b is BellringerBrain) Offer("bellringer", "BELL-RINGER: its ringing shockwave Silences anyone it passes under. Jump over it.");
                if (b is CantorBrain) Offer("cantor", "TONGUELESS CANTOR: step out of its closing circle or be Silenced. Stun it mid-cast or remove it with Holy Water. It is frail.");
                if (b is BonePiperBrain) Offer("piper", "BONE PIPER: each tune fires a different pattern. It stands still while playing; hit the glowing bag.");
                if (b is BonePiperBrain || b is OrbweaverBrain) Offer("graze", "GRAZE: near-misses from projectiles build a streak. Weave through patterns rather than fleeing them.");
                if (b is OrbweaverBrain) Offer("orbweaver", "ORBWEAVER: its orbs fire spiral and ring patterns. Find the gaps, and kill the Orbweaver to destroy its orbs.");
                if (b is MournerBrain) Offer("mourner", "MOURNER: it only moves while no one is looking at it. Keep it in view and listen for scraping stone.");
                if (b is GravewardenBrain) Offer("gravewarden", "THE GRAVEWARDEN: its shield blocks frontal attacks. Strike after its Mace Sweep, from behind, or while it is dazed from charging into a wall.");
                if (b is ShieldbearerBrain sb && sb.ShieldUp) Offer("shield", "SHIELDBEARER: its shield blocks frontal attacks. Kick (V) or Shield Bash to break its guard, or attack from behind.");
                else if (b is GargoyleBrain g && g.Woken.Value) Offer("gargoyle", "GARGOYLE: it dives along a marked line and lands exposed. Sidestep, then counterattack.");
                else if (b is HoarderBrain) Offer("hoarder", "BONE HOARDER: each hit spills gold; hit the sack for more. It escapes after 25 s.");
                if (d < 10f && h.GetComponentInChildren<WeakPoint>() != null)
                    Offer("weak", "GLOWING SPOTS are weak points that take bonus damage. Headshots also deal extra damage.");
            }

            if (World.Tallyman.Active != null && !World.Tallyman.Active.Leaving.Value)
                Offer("tallyman", "THE TALLYMAN cannot be harmed. Outrun him, slow him with Holy Water (3), or enter the guardian's arena: he leaves when it seals.");

            foreach (var vent in layout.FlameTraps)
                if ((vent.At - me).sqrMagnitude < 144f) { Offer("flamevents", "FLAME VENTS fire on a steady rhythm and deal a third of your health. Cross as soon as the fire drops."); break; }
            // Hidden spike plates (2026-11-01): from the second act some lie in the floor's own flagstone.
            if (layout.Depth >= 2) Offer("hiddenplates", "HIDDEN PLATES: some spike plates blend into the floor. If you hear a click, move immediately.");

            var room = layout.RoomAt(me, 0f);
            if (room != null)
            {
                if (room.Feature == RoomFeature.Pendulums) Offer("pendulum", "PENDULUM HALL: the blades hit enemies too. Time your crossing, and kick enemies into them.");
                if (room.Feature == RoomFeature.Ritual && DungeonDirector.InRitualWard(room.Center)) Offer("ritual", "RITUAL CHAMBER: the violet braziers protect and summon enemies. Destroy all three.");
                if (room.Feature == RoomFeature.Treasury) Offer("treasury", "TREASURY: each player may take one relic.");
                if (room.Feature == RoomFeature.Beggar) Offer("beggar", "BEGGAR: give him gold (F). The more you give, the likelier he repays you with a relic, potions, a Sigil or gold.");
                if (room.Feature == RoomFeature.Proving) Offer("proving", "PROVING BELL: ring it to summon two waves of enemies. Clear them for gear, a Sigil and gold.");
                if (room.Feature == RoomFeature.Armoury) Offer("armoury", "ARMOURY: each player may sharpen their weapon once for a free temper.");
                if (room.Feature == RoomFeature.Lightless) Offer("lightless", "LIGHTLESS HALL: carry the torch by the door (F) to see. Its carrier cannot fight; set it down to fight in its light.");
                if (room.Feature == RoomFeature.Statues) Offer("watchers", "WATCHERS' ROOM: press F to rotate a statue. When all four face the floor sigil, the coffer opens. The noise may attract enemies.");
                if (room.Feature == RoomFeature.Draught) Offer("draught", "THE DRAUGHT: drinking it (F) permanently raises one stat for this run. Its name shows which. One drink only.");
                if (room.Feature == RoomFeature.Scales) Offer("scales", "THE SCALES: carry the urns (F) to the empty pan until the beam is exactly level. The studs on each urn show its weight.");
                if (room.Feature == RoomFeature.Mirrors) Offer("mirrors", "LIGHT-BEAM ROOM: press F to turn a mirror. Guide the beam to the crystal to open the coffer.");
                if (room.Feature == RoomFeature.Chimes) Offer("chimes", "CHIME ROOM: step on the coloured plates in the order the bells ring. A wrong plate wakes enemies and resets the tune.");
                if (room.Feature == RoomFeature.Oath) Offer("oath", "OATH ROOM: both plates must be held at once. Stand on one each, or carry the Oath Stone (F) onto one.");
                if (room.Feature == RoomFeature.Mirror) Offer("mirror", "DARK MIRROR: grants a relic and gold, but takes a fifth of your health until you return to camp.");
                if (room.Feature == RoomFeature.Pyre) Offer("pyre", "PYRE HALL: fire erupts on a rhythm. Leave the red ring before it fills.");
                if (room.Feature == RoomFeature.Gauntlet) Offer("gauntlet", "SPIKE GAUNTLET: rows glow before they fire. Move between them; a row that hits you pins you briefly.");
                if (room.Feature == RoomFeature.Cistern) Offer("cistern", "CISTERN: water slows everyone, and frost freezes anything standing in it.");
            }
            if (Object.FindObjectsByType<BloodAltar>(FindObjectsSortMode.None).Any(b => !b.Spent.Value && Vector3.Distance(b.transform.position, me) < 9f))
                Offer("altar", "BLOOD ALTAR: each offering (F) costs 10% max health for the run and grants +4% damage. Three offerings reward each contributor with a relic.");
            int act = DungeonDirector.Act(layout.Depth);
            if (act == 0) Offer("bones", "THE OSSUARY: fallen skeletons reassemble. Walk over bone piles to destroy them.");
            if (act == 1) Offer("tide", "THE DROWNED VAULTS: rising water halves your speed. Move onto stairs, balconies or stones.");
            if (act == 2) Offer("lantern", "THE HOLLOW THRONE: smash a soul lantern to heal your party and stun nearby enemies.");
            if (act == 3) Offer("bell", "THE BLACK CATHEDRAL: jump the shockwave when the bell tolls.");
        }

        /// <summary>Owner, from the HUD's OnGUI.</summary>
        /// <summary>The bottom of the tip box drawn last frame, 0 when none is up (the feed keeps below it).</summary>
        public static float BottomEdge { get => Time.frameCount - bottomSetAt <= 1 ? bottomEdge : 0f; private set => bottomEdge = value; }
        private static float bottomEdge;
        private static int bottomSetAt = -10;

        public static void Draw(float w, float h)
        {
            // A boss's entrance has the screen to itself, and so does being down; the tip waits (its timer held).
            var meDown = NetworkPlayer.Local != null ? NetworkPlayer.Local.GetComponent<PlayerLife>() : null;
            if (GameHud.CentreBusy || (meDown != null && meDown.Downed.Value))
            {
                if (current != null) shownAt = Time.time;
                return;
            }
            if (current != null && Time.time - shownAt > Duration) current = null;
            // A camp tip still up when the carriage arrives (a floor banner holds its timer) goes at once, and the other way round.
            if (current != null && currentCamp != DungeonDirector.InLobby) current = null;
            if (current == null)
            {
                if (queue.Count == 0 || Time.time - shownAt < Duration + Gap) return;
                // A tip offered at camp means nothing in the dungeon (and the other way round): drop stale ones.
                (string key, string text, bool camp, float at) next = default;
                bool found = false;
                while (queue.Count > 0)
                {
                    next = queue.Dequeue();
                    // So is one that waited too long: "an Entling!" a minute after it died teaches nothing. It can be offered again.
                    if (next.camp == DungeonDirector.InLobby && (next.camp || Time.time - next.at <= MaxWait)) { found = true; break; }
                    sessionSeen.Remove(next.key);
                }
                if (!found) return;
                current = next.text;
                currentCamp = next.camp;
                // Seen once it is shown (not when offered: a stale one dropped above can come back in its place).
                // Automated tests show tips but never mark them seen in the real save.
                if (!DungeonDirector.TestMode && !Progression.Cosmetics.TestMode) Settings.MarkTipSeen(next.key);
                GameHud.Heard?.Invoke("tip", current);
                shownAt = Time.time;
                Sfx.Play2D(Sound.Relic, 0.3f);
            }
            float t = Time.time - shownAt;
            float a = Mathf.Clamp01(t * 4f) * Mathf.Clamp01((Duration - t) * 2f);
            // 2026-11-13, the user: "make the popup more see through". A light veil instead of a solid panel (the world shows
            // through it); the words carry a shadow so they still read over a bright room.
            float pw = Mathf.Min(560f, w - 40f);
            string text = Binds.Rekey(current);
            var style = new GUIStyle(UiSkin.Body) { fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.97f, 0.94f, 0.87f, a) } };
            var shadowStyle = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f * a) } };
            float ph = style.CalcHeight(new GUIContent(text), pw - 40f) + 28f;
            // Below the floor banner, the boss bar and the rage meter (at 104 it sat on top of them).
            var r = new Rect(w / 2f - pw / 2f, 176f, pw, ph);
            BottomEdge = r.yMax;
            bottomSetAt = Time.frameCount;
            UiSkin.Rect(r, new Color(0.03f, 0.025f, 0.02f, 0.38f * a));
            UiSkin.Rect(new Rect(r.x, r.y, r.width, 1f), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.55f * a));
            UiSkin.Rect(new Rect(r.x, r.yMax - 1f, r.width, 1f), new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.25f * a));
            GUI.Label(new Rect(r.x + 20, r.y + 4, pw - 40, 16), "TIP", new GUIStyle(UiSkin.Small) { fontSize = 11, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = new Color(UiSkin.Gold.r, UiSkin.Gold.g, UiSkin.Gold.b, 0.9f * a) } });
            var body = new Rect(r.x + 20, r.y + 20, pw - 40, ph - 24);
            GUI.Label(new Rect(body.x + 1.5f, body.y + 1.5f, body.width, body.height), text, shadowStyle);
            GUI.Label(body, text, style);
        }
    }
}
