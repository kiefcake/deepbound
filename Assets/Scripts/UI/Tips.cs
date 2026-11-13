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
            "flamevents", "edges", "relictray", "hone", "purse", "falloff", "entling", "rare", "necro", "hiddenplates", "gravewarden", "silenced",
            "camp", "quests", "tavern", "emotewheel", "potions", "gamble2", "levers", "voice", "phases", "mourner", "pingwheel", "reliquary", "inventory", "bleed", "soul", "shield", "weak", "afflicted", "gargoyle", "pendulum", "ritual", "cistern", "hoarder", "bones", "tide", "lantern", "bell", "altar", "pyre", "gauntlet", "treasury", "beggar", "proving", "armoury", "mirror", "orbweaver", "piper", "graze", "tallyman", "oath", "acolyte", "abysseye", "lightless", "shelter", "cantor", "magus", "lamprey", "hag", "bellringer", "blinded", "accursed", "dazed", "decree", "stagger", "brand", "eclipse", "teamwork", "soulbound", "casino3", "poker", "questfloor", "oil", "chandelier", "pillar", "vigil", "prisoner", "watchers", "chimes", "fishing", "knives", "bells", "wrestle", "mirrors", "scales", "counter", "draught", "eruption", "spikes",
        };

        private static readonly System.Collections.Generic.HashSet<string> sessionSeen = new System.Collections.Generic.HashSet<string>();
        private static string current;
        private static float shownAt = -100f, nextCheck;
        private const float Duration = 7f;
        private static readonly System.Collections.Generic.Queue<(string key, string text, bool camp, float at)> queue = new System.Collections.Generic.Queue<(string, string, bool, float)>();
        /// <summary>Seconds of quiet after a tip before the next one (they used to arrive back to back).</summary>
        private const float Gap = 9f, MaxWait = 25f;

        private static void Offer(string key, string text)
        {
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
                    if (it is MysteryCoffer) Offer("casino3", "The LOCKSMITH'S COFFER has three locks: each one you pick betters the prize, a snapped pick loses it all - step back to take what you have.");
                    if (it is PokerDiceTable) Offer("poker", "SKULL POKER: dice against the other players, no house. Sit (F), ante, hold and reroll once, then CHECK / BET / CALL / FOLD. Best hand takes the pot. Only you see your dice - the others see how many you kept.");
                    if (it is SlotMachine || it is BlackjackTable) Offer("gamble2", "THE TENT: the Bone Reels pay big for three of a kind; at Deadman's Hand beat the skeleton dealer to 21. HIT or STAND in the table's panel.");
                    if (it is TavernKeep || it is TavernBand) Offer("tavern", "THE GUTTED LANTERN: talk to Marta (F) for mead: each horn makes you Tipsy, Merry, then Sloshed - a little braver in a fight, and the world sways. F at the stage pays the band for the next song.");
                    if (it is WellFishing) Offer("fishing", "THE WELL: F drops a line. Wait for the bite - a splash, the float ducks - then F again, quick, to haul it up. Mostly junk; sometimes coins, a potion... a pearl, they say.");
                    if (it is ArmWrestle) Offer("wrestle", "ARM-WRESTLE BIG GORM by the tavern: HOLD SPACE to strain, but it drains your grip - empty it and you cramp. Rest while he leans; when he gathers himself (!) brace, and when he tires, drive. Pin him for double (three times a visit). C challenges another delver: the winner takes both stakes.");
                    if (it is BellFrame) Offer("bells", "THE BELLS: take a rope (F) and ask Aldric for a peal (SPACE). Ring 1-4 as each strike reaches the line - share the ropes with friends. A clean peal blesses everyone who rang for the next floor.");
                    if (it is KnifeBoard) Offer("knives", "THE KNIFE BOARD: F at the chalk line, then F again to throw when the swinging mark is on the bull. Three knives a round; Brann pays once a visit for 24 or better.");
                }
                Offer("camp", "CAMP: look at anyone and press F to talk and trade. M shows who is where. To go down, sit on Corwin's carriage (F).");
                Offer("quests", "QUESTS: ask the camp folk for work or take a notice from the board (three at once). Each one done wins that person's favour: better prices, and later a special job.");
                var prog = player.GetComponent<Progression.PlayerProgress>();
                if (prog != null && prog.OpenEdgeSlot >= 0) Offer("edges", "AN EDGE: your weapon has reached +" + Progression.Edges.Levels[prog.OpenEdgeSlot] + ". At the anvil (F) forge one of three edges into it - each makes it better at a different way of fighting. Tempering waits until you choose.");
                if (prog != null && prog.RelicCount >= Loot.Relics.MaxCarried) Offer("relictray", "YOUR RELIC TRAY IS FULL (8). New relics now EMPOWER one you carry (II, III) instead of adding another.");
                if (prog != null && prog.WeaponLevel.Value >= Anvil.MaxLevel && prog.Hones.Value == 0) Offer("hone", "FULLY TEMPERED. Hobb can still HONE the blade at the anvil: five times, +3% damage each, for embers and gold.");
                if (PlayerLife.All.Count > 1 && PlayerVoice.MicAvailable && Settings.VoiceMode != 2)
                    Offer("voice", "PROXIMITY VOICE is on: players near you hear you, muffled through walls. SHOUTING wakes the dead nearby. Options has push-to-talk (T).");
                if (PlayerLife.All.Count > 1) Offer("pingwheel", "GIVING: look at an ally up close and HOLD G: potions, 25 gold, 10 embers, your rare draught, or a high five. Hold on gold or embers to keep giving.");
                if (PlayerLife.All.Count > 1) Offer("emotewheel", "EMOTES: tap H to wave (two players waving together HIGH FIVE). HOLD H for the emote wheel: cheer, bow, laugh, dance, point, beckon, sit.");
                if (PlayerLife.All.Count > 1) Offer("purse", "PARTY LOOT is shared by default: gold and embers anyone picks up are split evenly. In the inventory (I) you can give gold, even out the purse, or ctrl-click a piece to hand it to the nearest ally.");
                return;
            }
            var layout = DungeonDirector.Layout;
            if (layout == null) return;

            foreach (var pl in PlayerLife.All)
            {
                if (pl == null || pl.gameObject == player.gameObject) continue;
                if (pl.Downed.Value && !pl.Soul.Value) Offer("bleed", "An ally is DOWN: hold F beside them before they bleed out (40 s). If they do, carry their soul skull to the waystone in the start room.");
                if (pl.Soul.Value) Offer("soul", "A SOUL SKULL glows where an ally bled out. F to carry it (you move slower and cannot fight) to the start room's waystone. A heavy hit knocks it loose.");
            }
            // Voice and pings are for a party: alone they mean nothing.
            bool party = PlayerLife.All.Count > 1;
            foreach (var b in Object.FindObjectsByType<Breakable>(FindObjectsSortMode.None))
            {
                if (b == null || Vector3.Distance(b.transform.position, me) > 9f) continue;
                if (b.Type.Value == Breakable.Kind.OilJar || b.Type.Value == Breakable.Kind.FireBowl)
                    Offer("oil", "OIL JARS spill a slick; any fire lights it (spells, a torch, a knocked-over FIRE BOWL). Lure the dead in, stay out yourself.");
                if (b.Type.Value == Breakable.Kind.CrackedPillar)
                    Offer("pillar", "A CRACKED PILLAR: break it and it falls AWAY from your last blow, crushing whatever stands in that lane (the warning lane shows it). The rubble stays: cover from shots.");
            }
            foreach (var c in Chandelier.FindObjectsByType<Chandelier>(FindObjectsSortMode.None))
                if (c != null && !c.Fallen.Value && Vector3.Distance(c.HangAt.Value, me) < 12f)
                    Offer("chandelier", "A CHANDELIER hangs over this room. Its rope runs to a cleat on the wall: F there drops it on whatever stands under it - crushing the dead, staggering a guardian. Mind your own head.");
            foreach (var v in VigilCandle.All)
                if (v != null && !v.Kept.Value && Vector3.Distance(v.transform.position, me) < 10f)
                { Offer("vigil", "VIGIL CANDLES: light all four (F) and the vigil is kept - you are mended and blessed for the floor. The dead snuff any lit candle they pass: clear the room, or be quick."); break; }
            foreach (var pc in Object.FindObjectsByType<PrisonerCage>(FindObjectsSortMode.None))
                if (pc != null && Vector3.Distance(pc.transform.position, me) < 10f)
                { Offer("prisoner", "A PRISONER begs to be let out. Free them (F) and they fight beside you for the floor - though now and then the thing in the cage was never a prisoner."); break; }
            var questDir = DungeonDirector.Instance;
            if (questDir != null)
                foreach (var q in questDir.Quests)
                    if (q.Status == DungeonDirector.ContractStatus.Active && (q.Contract == Contract.Cargo || q.Contract == Contract.Gather || q.Contract == Contract.Mark))
                    { Offer("questfloor", "QUEST ON THIS FLOOR: cargo (a light column) must be CARRIED into the guardian's arena before the gates fall - then it is safe. Finds must be held when the lift rises; a named MARK must die first."); break; }
            var myKit = player.ActiveKit;
            if (myKit != null && myKit.AttackRange > 6f) Offer("falloff", "RANGED shots lose bite past half their range (60% at the very end), and arrows and knives DROP: aim a little high at distance.");
            if (EnemyBrain.All.Any(e => e is EntlingBrain && !e.IsDead && Vector3.Distance(e.transform.position, me) < 16f))
                Offer("entling", "ENTLINGS sprint at you and root you to the spot: you cannot move until it dies. Kill it on the way in, or turn and cut it off you.");
            if (Health.All.Any(hh => hh != null && hh.IsAlive && hh.Team == Team.Enemies && (hh.transform.position - me).sqrMagnitude < 400f && hh.TryGetComponent<EnemyBrain>(out var sb) && sb.TwinId.Value != 0))
                Offer("soulbound", "SOULBOUND TWINS, joined by a pale thread: strike one down and it only falls - it gets up again unless its twin dies within a few seconds. " + (party ? "Split up: one each, and finish them together." : "Bring one low, then the other, and finish them back to back."));
            var relic = Reliquary.Current;
            if (relic != null && !relic.Carried && Vector3.Distance(relic.transform.position, me) < 10f)
                Offer("reliquary", "A RELIQUARY: carry it (F) to the start room's waystone to raise the fallen, pay everyone and weaken the guardian. Its carrier is slow, cannot fight, and draws the dead.");
            var progT = player.GetComponent<Progression.PlayerProgress>();
            if (progT != null && progT.BagCount > 0)
                Offer("inventory", "Gear you pick up goes straight on if that slot is empty, otherwise into your PACK. Press I for the inventory: click a piece to wear it, shift-click to drop. Scrap spares at Hobb's anvil.");
            foreach (var it in Interactable.All)
                if (it is Lever lv && !lv.Pulled.Value && Vector3.Distance(lv.transform.position, me) < 9f)
                    Offer("levers", "A VAULT LEVER. Three are bolted to walls in different rooms on this floor; pull all three (they stay down) to open the vault.");
            // Taught on the first real wound, while there is still a potion to drink.
            if (player.GetComponent<Health>().Fraction < 0.7f)
            {
                var pouch = player.GetComponent<Flasks>();
                if (pouch != null && pouch.Count(Flasks.Kind.Health) > 0)
                    Offer("potions", "Hurt? 1 drinks a Health Potion (5 in the pouch). VIT mends you slowly; potions are what save a fight.");
            }
            var rarePouch = player.GetComponent<Flasks>();
            if (rarePouch != null && rarePouch.RareHeld != Progression.RareItem.None)
                Offer("rare", "A RARE " + (Progression.RareItems.IsTome(rarePouch.RareHeld) ? "TOME" : "DRAUGHT") + " sits in your pouch's fifth slot: press 5 to use it. " + Progression.RareItems.Describe(rarePouch.RareHeld) + " You carry one at a time; save it for the moment that matters.");

            if (PlayerLife.All.Count > 1 && !DungeonDirector.InLobby)
                Offer("teamwork", "TOGETHER: hit a foe from the side opposite an ally for a PINCER (+15%). Last one standing hits harder.");
            var knight = player.GetComponent<KnightKit>();
            if (knight != null && knight.ShieldRaised && PlayerLife.All.Count > 1)
                Offer("shelter", "Your shield is up: allies standing close BEHIND you are SHELTERED, taking half from blows at your shield. Hold the line for them.");
            var deb = player.GetComponent<PlayerDebuffs>();
            if (deb != null && (deb.Poisoned || deb.Frostbitten || deb.Cursed))
                Offer("afflicted", "You are AFFLICTED. Holy Water (4) cleanses it; weakness also ends when its Hexer dies.");
            if (HearthBrazier.All.Count > 0)
                foreach (var hb in HearthBrazier.All)
                    if (hb != null && !hb.Lit.Value && Vector3.Distance(hb.transform.position, player.transform.position) < 9f)
                    { Offer("hearth", "A HEARTH BRAZIER stands cold in the middle of this room. Light it (F): it lights the room, melts frost off anyone beside it, mends you between fights, and burns any of the dead you kick into it."); break; }
            if (deb != null && deb.Blinded)
                Offer("blinded", "BLINDED: ink in your eyes - no map, no health bars on the dead, only a small clear hole. Keep out of the black pools; Holy Water (4) washes it.");
            if (deb != null && deb.Accursed)
                Offer("accursed", "CURSED: you take 40% more damage until it runs out or the Withering Hag dies. Kill her, or lift it with Holy Water (4)."); 
            if (deb != null && deb.Dazed)
                Offer("dazed", "DAZED: a heavy blow (a Brute's pound, a Gargoyle's dive) rang your head - you turn at half speed for a moment. Block or dodge the big ones.");
            if (deb != null && deb.Silenced)
                Offer("silenced", "SILENCED: a Cantor's pale circle closed on you - no Q, E or ultimate for a few seconds. Step out of the circles; Holy Water lifts it.");

            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                float d = Vector3.Distance(h.transform.position, me);
                if (d > 14f) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (b == null) continue; // a rift: a body with no brain
                if (b is BossBrain bb && bb.Immune) Offer("phases", "The boss is IMMUNE for now. A WARDED boss stays immune until its guards die; SURVIVE means dodge until the timer runs out. The boss bar says which.");
                if (b is AbyssEyeBrain) Offer("abysseye", "An ABYSSAL EYE: when it locks on, a violet line runs along the floor toward you - step off it before the beam fires. Shoot the iris.");
                if (b is ChoirAcolyteBrain) Offer("acolyte", "A CHOIR ACOLYTE: when its golden circle fills, every one of the dead inside is SHIELDED. Stun it or hit it hard while it sings to break the hymn - or kill it first.");
                if (b is BossBrain staggered && staggered.Stagger.Value > 0.2f) Offer("stagger", "The gold line under the boss bar is its BREAK meter: weak points, headshots, stuns and kicks fill it. Full, the boss is BROKEN for a few seconds - it stops and takes half again. Save your big hits for it.");
                if (b is CardinalBrain) Offer("brand", "The PALE CARDINAL brands one of you with a gold ring (EXCOMMUNICATED). When it bursts it burns everyone inside it, hardest the ones standing with you: the branded walks away from the party, or dodges through the burst.");
                if (b is UnsungBrain) Offer("eclipse", "THE UNSUNG sings ECHOES of the guardians you slew - their attacks, in their colours. At the very end, when POOLS OF DAWN open, stand in the light: the ECLIPSE strikes everyone outside one.");
                if (b is NecromancerBrain) Offer("necro", "A NECROMANCER mends every one of the dead near it (green rings) and raises more. Kill it first, or stun it while its circle glows to break the spell.");
                if (b is HollowKingBrain hk && hk.DecreesFallen > 0) Offer("decree", "The Hollow King's DECREES: BE STILL - whoever is still moving when it falls is struck (you can still swing and turn). BEGONE - whoever he can see is struck: put a pillar between you.");
                if (b.Recovering) Offer("punish", "After a blow the dead SLUMP for a moment - weapon low, eyes dim. That is your opening: step in (a melee swing carries you a pace toward a foe just out of reach), hit it for PUNISH +20%, and step back out before its next wind-up.");
                if (b is RimeMagusBrain) Offer("magus", "A RIME MAGUS calls a BLIZZARD that drifts after you: inside it you are slowed and the cold bites. Do not fight in it - walk or dodge clear, or kill the Magus and the storm dies.");
                if (b is InkLampreyBrain) Offer("lamprey", "An INK LAMPREY spits where you are going - a dark circle. Caught under it you are BLINDED for a few seconds, and the ink pools there a while. Sidestep the circle; Holy Water washes your eyes.");
                if (b is WitheringHagBrain) Offer("hag", "A WITHERING HAG fixes her EVIL EYE down a lane (it does not turn once drawn). Step out of it: caught, you are CURSED - +40% damage taken until she dies.");
                if (b is BellringerBrain) Offer("bellringer", "A BELL-RINGER heaves and rings its bell: a low pale ring rolls out along the floor and SILENCES whoever it passes under. JUMP it, as you jump a guardian's shockwave.");
                if (b is CantorBrain) Offer("cantor", "A TONGUELESS CANTOR: when its pale circle closes under you, you are SILENCED - no Q, E or ultimate for a few seconds. Step out of the circle, stun it mid-cast, or lift it with Holy Water. It is frail: reach it.");
                if (b is BonePiperBrain) Offer("piper", "A BONE PIPER: each tune is a pattern - a ROSE that blooms, SCISSORS that sweep, SEEDPODS that burst where they land, a MARCH of walls with a gap. Its name floats up as it starts. It stands still to play: hit the glowing bag.");
                if (b is BonePiperBrain || b is OrbweaverBrain) Offer("graze", "GRAZE: a shot that slips past you close without touching you counts toward a streak. Weave through patterns, do not just run from them.");
                if (b is OrbweaverBrain) Offer("orbweaver", "An ORBWEAVER: it hangs back and conjures orbs that fire patterns - spirals, rings with a gap, aimed bursts. Watch the gap, and kill the Orbweaver: its orbs die with it.");
                if (b is MournerBrain) Offer("mourner", "A MOURNER: it only moves while no one is looking at it. Keep your eyes on it, back away facing it, and listen for stone scraping behind you.");
                if (b is GravewardenBrain) Offer("gravewarden", "THE GRAVEWARDEN: its shield blocks from the front. Hit it right after each Mace Sweep (OPENING), from behind, or while it is dazed from charging into stone.");
                if (b is ShieldbearerBrain sb && sb.ShieldUp) Offer("shield", "SHIELDBEARER: its shield blocks from the front. KICK it (V) or Shield Bash to knock the shield aside, or get behind it.");
                else if (b is GargoyleBrain g && g.Woken.Value) Offer("gargoyle", "GARGOYLE: it dives along a marked line, then lands exposed. Sidestep, then punish.");
                else if (b is HoarderBrain) Offer("hoarder", "BONE HOARDER: it runs with a sack of gold. Every hit spills coins; hit the sack for more. It escapes in 25 s - and the GILDED SKULL is found nowhere but in its sack.");
                if (d < 10f && h.GetComponentInChildren<WeakPoint>() != null)
                    Offer("weak", "GLOWING SPOTS are weak points: hit one for a burst of damage. Headshots hit harder too.");
            }

            if (World.Tallyman.Active != null && !World.Tallyman.Active.Leaving.Value)
                Offer("tallyman", "THE TALLYMAN comes for parties that linger. He cannot be hurt: sprint away (he gets faster), throw HOLY WATER (4) to hold him for a moment, and get into the guardian's arena - when it seals, he leaves.");

            foreach (var vent in layout.FlameTraps)
                if ((vent.At - me).sqrMagnitude < 144f) { Offer("flamevents", "FLAME VENTS burn across the corridor on a steady beat, and a hit takes a THIRD of your health. The grates glow just before they light: go the moment the fire drops."); break; }
            // Hidden spike plates (2026-11-01): from the second act some lie in the floor's own flagstone.
            if (layout.Depth >= 2) Offer("hiddenplates", "WATCH THE FLOOR: from here some spike plates are laid in flagstone like the rest. A click underfoot means MOVE - the spikes pin whoever they catch.");

            var room = layout.RoomAt(me, 0f);
            if (room != null)
            {
                if (room.Feature == RoomFeature.Pendulums) Offer("pendulum", "PENDULUM HALL: the blades cut the dead as well as you. Time your crossing - and kick enemies into them.");
                if (room.Feature == RoomFeature.Ritual && DungeonDirector.InRitualWard(room.Center)) Offer("ritual", "RITUAL CHAMBER: the violet braziers ward the dead and raise more. Smash all three.");
                if (room.Feature == RoomFeature.Treasury) Offer("treasury", "THE TREASURY: three relics on pedestals. Each of you may take ONE - choose well, the rest stay for your friends.");
                if (room.Feature == RoomFeature.Beggar) Offer("beggar", "A BEGGAR: give him gold (F). Every coin makes it likelier he pays you back - a relic, potions, a Sigil or a fat purse.");
                if (room.Feature == RoomFeature.Proving) Offer("proving", "THE PROVING BELL: ring it and two waves of the dead come. Clear them for gear for everyone, a Sigil and gold.");
                if (room.Feature == RoomFeature.Armoury) Offer("armoury", "THE ARMOURY: each of you may sharpen your weapon once on the whetstone - a free temper.");
                if (room.Feature == RoomFeature.Lightless) Offer("lightless", "A LIGHTLESS HALL: no torch burns here but the one by the door. Carry it (F) - whoever holds it cannot fight - or set it down and fight in its glow. Something waits at the far end.");
                if (room.Feature == RoomFeature.Statues) Offer("watchers", "THE WATCHERS' ROOM: four stone watchers, all looking away. F turns one a quarter round; when every one of them looks at the sigil in the floor, their eyes burn and the coffer opens. The grinding can wake the dead.");
                if (room.Feature == RoomFeature.Draught) Offer("draught", "THE DRAUGHT: one flask on the floor, glowing the colour of what it holds - its name says which stat it raises for the rest of the run. One drink only: decide who needs it most (F to drink).");
                if (room.Feature == RoomFeature.Scales) Offer("scales", "THE SCALES: a great balance stands in the middle, a stone counterweight on one pan. Carry the urns (F) - the gold studs show each one's weight - and set them down by the empty pan until the beam hangs level. Exactly level opens the coffer.");
                if (room.Feature == RoomFeature.Mirrors) Offer("mirrors", "THE LIGHT-BEAM ROOM: a lens throws light across the room. F turns a standing mirror to its other diagonal; send the beam from mirror to mirror until it strikes the crystal, and the coffer opens.");
                if (room.Feature == RoomFeature.Chimes) Offer("chimes", "THE CHIME ROOM: listen - the bells ring a tune. Step on the coloured plates in the same order. A wrong plate wakes the dead and the tune starts over; F at the coffer rings it again.");
                if (room.Feature == RoomFeature.Oath) Offer("oath", "THE OATH ROOM: two plates must be held down at once. Stand on one each - or, alone, carry the Oath Stone (F) onto one and stand on the other.");
                if (room.Feature == RoomFeature.Mirror) Offer("mirror", "THE DARK MIRROR: a relic and gold for whoever touches it... and it keeps a fifth of their health until camp.");
                if (room.Feature == RoomFeature.Pyre) Offer("pyre", "PYRE HALL: the fire erupts on a beat. When the red ring fills, get out past it - and leave the dead inside.");
                if (room.Feature == RoomFeature.Gauntlet) Offer("gauntlet", "SPIKE GAUNTLET: a wave of spikes runs down the hall. Rows glow before they fire: step between them, and let the dead walk in. A row that catches you PINS you for a moment.");
                if (room.Feature == RoomFeature.Cistern) Offer("cistern", "CISTERN: the water slows everyone - but frost freezes anything standing in it.");
            }
            if (Object.FindObjectsByType<BloodAltar>(FindObjectsSortMode.None).Any(b => !b.Spent.Value && Vector3.Distance(b.transform.position, me) < 9f))
                Offer("altar", "BLOOD ALTAR: each offering (F) costs a tenth of your MAX health for the run and gives +4% damage for the run. Three fill the chalice: a relic for each who bled, Bloodlust for all.");
            int act = DungeonDirector.Act(layout.Depth);
            if (act == 0) Offer("bones", "THE OSSUARY: fallen skeletons pull themselves together. Walk over a bone pile to stamp it out.");
            if (act == 1) Offer("tide", "THE DROWNED VAULTS: when the water thickens it halves your speed (the dead barely slow). Get onto stairs, balconies or stones.");
            if (act == 2) Offer("lantern", "THE HOLLOW THRONE: smash a soul lantern to heal your party and stun the dead nearby.");
            if (act == 3) Offer("bell", "THE BLACK CATHEDRAL: when the bell tolls, JUMP the shockwave.");
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
