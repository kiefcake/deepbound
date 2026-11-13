using System.Collections.Generic;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Everything currently affecting you, as a row of badges above your health: green for help,
    /// red for harm, gold for the run's conditions. A thin bar under a badge runs down its time.
    /// Hover one to read it - hold ALT to free the cursor (or hover while the inventory or pause
    /// menu is open).
    /// </summary>
    public static class StatusEffects
    {
        public struct Status
        {
            public string Glyph, Name, Text, Lasts;
            public Color Color;
            public float Left, Total; // seconds; Total 0 = untimed
        }

        private static readonly Color Good = new Color(0.45f, 0.9f, 0.5f), Bad = new Color(0.95f, 0.38f, 0.32f), Run = new Color(0.95f, 0.78f, 0.4f);
        private static readonly List<Status> list = new List<Status>();
        private static Font glyphFont;
        /// <summary>Screenshots: show this badge's tooltip as if hovered.</summary>
        public static int DebugHover = -1;

        private static double ServerNow => Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening
            ? Unity.Netcode.NetworkManager.Singleton.ServerTime.Time : 0;

        public static List<Status> Gather(NetworkPlayer player)
        {
            list.Clear();
            if (player == null) return list;
            var progress = player.GetComponent<PlayerProgress>();
            var life = player.GetComponent<PlayerLife>();
            var health = player.GetComponent<Health>();
            var pos = player.transform.position;
            double now = ServerNow;

            if (progress != null && progress.ActiveBlessing.Value != 0)
            {
                var b = (Blessing)progress.ActiveBlessing.Value;
                list.Add(new Status { Glyph = "✝", Name = Blessings.Name(b), Text = Blessings.Describe(b), Lasts = "Until you return to camp", Color = Blessings.Color(b) });
            }
            var afflict = player.GetComponent<PlayerDebuffs>();
            if (afflict != null)
            {
                if (afflict.Drunk > 0) list.Add(Timed("☕", PlayerDebuffs.DrunkName(afflict.Drunk) + (afflict.Drunk > 1 ? " (" + afflict.Drunk + " meads)" : ""),
                    "Mead from the tavern: +" + Mathf.RoundToInt(PlayerDebuffs.DrunkCourage * 100f * afflict.Drunk) + "% damage, and the world will not hold still. Wears off a level at a time.",
                    new Color(0.95f, 0.72f, 0.3f), afflict.DrunkUntil.Value - now, PlayerDebuffs.DrunkSecondsPerLevel));
                if (afflict.Poisoned) list.Add(Timed("☠", "Poisoned", "You are losing health over time. Holy Water cleanses it.", Bad, afflict.PoisonUntil.Value - now, 8f));
                if (afflict.Dazed) list.Add(Timed("\u2736", "Dazed", "A heavy blow rang your head: you turn at half speed. Block or dodge the big hits. Holy Water clears it.", new Color(0.95f, 0.85f, 0.55f), afflict.DazeUntil.Value - now, 1.6f));
                if (afflict.Silenced) list.Add(Timed("\u266A", "Silenced", "A Cantor's hush: no Q, E or ultimate. You can still attack, block, dodge and drink. Holy Water lifts it.", Enemies.CantorBrain.HushColor, afflict.SilenceUntil.Value - now, Enemies.CantorBrain.SilenceSeconds));
                if (afflict.Manic) list.Add(Timed("\u2735", "Mania", "Madness has your legs: you run wild and cannot attack. Holy Water cleanses it.", new Color(0.85f, 0.4f, 1f), afflict.ManiaUntil.Value - now, 3f));
                if (afflict.Entangled) list.Add(new Status { Glyph = "\u2618", Name = "Entangled", Text = "An Entling's roots hold your legs: you cannot move until it dies. You can still fight it.", Lasts = "Until the Entling dies", Color = new Color(0.55f, 0.85f, 0.35f) });
                else if (afflict.Rooted) list.Add(Timed("\u2744", "Rooted", "Ice holds your feet: you cannot move, but you can still fight.", new Color(0.65f, 0.92f, 1f), afflict.RootUntil.Value - now, 2f));
                if (afflict.Frostbitten) list.Add(Timed("❄", "Frostbitten", "-40% movement speed. Holy Water cleanses it.", new Color(0.6f, 0.85f, 1f), afflict.FrostUntil.Value - now, 6f));
                if (afflict.Accursed) list.Add(Timed("\u2620", "Cursed", "The Withering Hag's evil eye: you take 40% more damage, until it runs out or she dies. Holy Water cleanses it.", Enemies.WitheringHagBrain.EyeColor, afflict.HexUntil.Value - now, Enemies.WitheringHagBrain.CurseSeconds));
                if (afflict.Blinded) list.Add(Timed("\u25C9", "Blinded", "Ink in your eyes: no map and no health bars on the dead. Keep out of the ink pools. Holy Water washes it.", new Color(0.6f, 0.45f, 0.95f), afflict.BlindUntil.Value - now, Enemies.InkLampreyBrain.BlindSeconds));
                if (afflict.Cursed) list.Add(Timed("✷", "Weakened", "-40% damage dealt and -50% healing received, until the Hexer that weakened you dies. Holy Water cleanses it.", new Color(0.75f, 0.45f, 1f), afflict.CurseUntil.Value - now, 20f));
            }
            if (HearthBrazier.Warms(pos)) list.Add(new Status { Glyph = "\u2668", Name = "Warmed", Text = "By a lit hearth: frost cannot take hold, and you mend while no foe is near.", Lasts = "While you stand by it", Color = HearthBrazier.Fire });
            float water = DungeonDirector.WaterSlow(pos, true);
            if (water < 0.99f)
                list.Add(new Status { Glyph = "≈", Name = DungeonDirector.Thickness > 0.05f ? "Mired" : "Wading",
                    Text = (DungeonDirector.Thickness > 0.05f ? "The thickened water drags at your legs" : "The water slows you") + ": -" + Mathf.RoundToInt((1f - water) * 100f) + "% movement speed. Get onto stairs, balconies or stones.",
                    Lasts = "While you stand in it", Color = new Color(0.55f, 0.75f, 0.45f) });
            var flasks = player.GetComponent<Flasks>();
            if (flasks != null && flasks.Ironbark) list.Add(Timed("\u2742", "Ironbark", "Half damage, a shield, and nothing knocks, roots, chills or maddens you.", RareItems.Color(RareItem.Ironbark), flasks.IronbarkUntil.Value - now, RareItems.IronbarkSeconds));
            if (flasks != null && flasks.Bloodfury) list.Add(Timed("\u2694", "Bloodfury", "+40% damage, attacks 25% faster.", RareItems.Color(RareItem.Bloodfury), flasks.BloodfuryUntil.Value - now, RareItems.BloodfurySeconds));
            if (flasks != null && flasks.InStasis) list.Add(Timed("\u25C9", "Grave Stillness", "Stone: nothing can hurt you and the dead have forgotten you. You cannot move or act.", RareItems.Color(RareItem.GraveStillness), flasks.StasisUntil.Value - now, RareItems.StasisSeconds));
            if (flasks != null && flasks.Healing)
                list.Add(new Status { Glyph = "♥", Name = "Mending", Text = "A health potion is closing your wounds.", Lasts = "A few seconds", Color = Good });
            var emotes = player.GetComponent<Emotes>();
            if (emotes != null && emotes.Brothers) list.Add(Timed("✋", "Brothers in Arms", "+10% damage. You high-fived an ally.", Good, emotes.BrothersUntil.Value - now, Emotes.BuffSeconds));
            if (life != null && Teamwork.InLastStand(life)) list.Add(new Status { Glyph = "!", Name = "Last Stand", Text = "Everyone else is down: +25% damage, -20% damage taken. Get them up - a revive now is a CLUTCH.", Lasts = "Until someone gets up", Color = Good });
            if (ClericKit.InConsecration(pos)) list.Add(new Status { Glyph = "✦", Name = "Blessed", Text = "+20% damage while you stand in the Cleric's consecrated ground.", Lasts = "While you stand in it", Color = Good });
            if (health != null && ClericKit.IsInspired(health)) list.Add(new Status { Glyph = "★", Name = "Inspired", Text = "+15% damage while the Cleric's beam is on you.", Lasts = "While the beam holds", Color = Good });
            if (life != null && Time.time < life.VeiledUntil) list.Add(new Status { Glyph = "☁", Name = "Smoke Screen", Text = "The dead cannot see you while you stay in the smoke.", Lasts = "While you stay in it", Color = Good });
            var debT = player.GetComponent<PlayerDebuffs>();
            if (debT != null && debT.Warded) list.Add(Timed("\u2695", "Plague Mask", "Warded: no new poison, frostbite, curses, mania or roots.", Good, (float)(debT.WardUntil.Value - now), PlagueDoctorKit.MaskWard));
            var flagT = player.GetComponent<FlagellantKit>();
            if (flagT != null && flagT.Zealous) list.Add(Timed("\u271D", "Zeal", "Faster, harder flail swings. You paid for it in blood.", Good, (float)(flagT.ZealEnds.Value - now), FlagellantKit.ZealSeconds));
            if (flagT != null && flagT.Martyr) list.Add(Timed("\u2620", "Martyrdom", "You cannot fall below 1 health. Hit 30% harder, heal for a quarter of it.", Good, (float)(flagT.MartyrEnds.Value - now), 8f));
            if (progress != null && progress.MirrorCursed.Value)
                list.Add(new Status { Glyph = "\u25C8", Name = "The Mirror's Price", Text = "Your reflection keeps a fifth of your max health.", Lasts = "Until you are back at camp", Color = Bad });
            if (progress != null && progress.Bloodlust)
                list.Add(Timed("\u2620", "Bloodlust", "+15% damage. The blood altar answered your offering.", Good, (float)(progress.BloodlustEnds.Value - now), World.BloodAltar.BloodlustSeconds));
            if (progress != null && progress.Salts.Value)
                list.Add(new Status { Glyph = "\u2668", Name = "Smelling Salts", Text = "If you go down, you come round on your own " + (int)Player.PlayerLife.SaltsSeconds + " s later at 30% health.", Lasts = "Until used, or the run ends", Color = new Color(0.85f, 0.95f, 0.8f) });
            if (progress != null && (progress.ScoutMap.Value || progress.MapRevealed.Value))
                list.Add(new Status { Glyph = "\u2316", Name = "Scout's Map", Text = progress.MapRevealed.Value ? "Corwin's chart: this floor's rooms are all on your map." : "Corwin's chart of the next floor: it will be on your map from the start.", Lasts = progress.MapRevealed.Value ? "This floor" : "Next floor", Color = new Color(0.9f, 0.8f, 0.55f) });
            if (progress != null && progress.PrayedFor.Value)
                list.Add(new Status { Glyph = "\u2629", Name = "Prayed For", Text = "Brother Aldric prayed for you (it cost you). The first blow that would down you leaves you standing at 1 health instead.", Lasts = "Until it saves you, or the run ends", Color = new Color(1f, 0.88f, 0.55f) });
            if (progress != null && progress.CatWhisker.Value) list.Add(new Status { Glyph = "❧", Name = "Cat Whisker", Text = "+1% chance to evade any hit outright. A gift from the cat, for petting it first.", Lasts = "This run", Color = Good });
            if (progress != null && progress.CatPeed.Value) list.Add(new Status { Glyph = "☂", Name = "Peed On", Text = "-5% max health. The cat's opinion of being picked up without asking.", Lasts = "This run", Color = Bad });
            var dir = DungeonDirector.Instance;
            var floorEvent = DungeonDirector.CurrentEvent;
            if (floorEvent != FloorEvent.None)
                list.Add(new Status { Glyph = "!", Name = DungeonDirector.EventName(floorEvent), Text = "This floor: " + DungeonDirector.EventText(floorEvent) + ".", Lasts = "This floor", Color = DungeonDirector.EventColor(floorEvent) });
            if (dir != null && dir.RunOmen != Omen.None && (dir.RunInProgress || DungeonDirector.InLobby))
                list.Add(new Status { Glyph = "☾", Name = "Omen: " + DungeonDirector.OmenName(dir.RunOmen), Text = DungeonDirector.OmenText(dir.RunOmen) + ". +35% Renown.", Lasts = "This run", Color = DungeonDirector.OmenColor(dir.RunOmen) });
            if (progress != null && DungeonDirector.InLobby)
            {
                var sore = new List<string>();
                foreach (var (id, nm) in new[] { (FolkId.Silas, "Silas"), (FolkId.Hobb, "Hobb"), (FolkId.Aldric, "Brother Aldric"), (FolkId.Vey, "Mistress Vey"), (FolkId.Corwin, "Corwin"), (FolkId.Bones, "Mister Bones") })
                    if (progress.Grudge(id) > 0) sore.Add(nm + " (" + progress.Grudge(id) + ")");
                if (sore.Count > 0)
                    list.Add(new Status { Glyph = "⚔", Name = "Held a Grudge", Text = "You struck: " + string.Join(", ", sore) + ". Vendors charge +25% per level; Mister Bones will not deal at 3. Pay amends at a stall, or let it fade (one level per return to camp).", Lasts = "Fades with time", Color = Bad });
            }
            return list;
        }

        private static Status Timed(string glyph, string name, string text, Color color, double left, float total) =>
            new Status { Glyph = glyph, Name = name, Text = text, Color = color, Left = Mathf.Max(0f, (float)left), Total = Mathf.Max(total, (float)left), Lasts = Mathf.CeilToInt(Mathf.Max(0f, (float)left)) + " s left" };

        /// <summary>Draw the row from (x, y) rightward; returns true if the mouse is over a badge.</summary>
        public static void Draw(NetworkPlayer player, float x, float y, bool cursorFree)
        {
            var all = Gather(player);
            if (all.Count == 0) return;
            if (glyphFont == null) { try { glyphFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Arial Unicode MS", "Arial" }, 18); } catch { } }
            const float size = 30f, gap = 6f;
            int hover = -1;
            var mouse = Event.current.mousePosition;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                var r = new Rect(x + i * (size + gap), y, size, size);
                bool over = cursorFree && r.Contains(mouse);
                if (over) hover = i;
                UiSkin.Rect(new Rect(r.x - 1.5f, r.y - 1.5f, size + 3f, size + 3f), new Color(s.Color.r, s.Color.g, s.Color.b, over ? 1f : 0.75f));
                UiSkin.Rect(r, new Color(0.05f, 0.04f, 0.035f, 0.92f));
                var g = new GUIStyle(GUI.skin.label) { font = glyphFont, fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = s.Color } };
                GUI.Label(new Rect(r.x, r.y - 1f, size, size), s.Glyph, g);
                if (s.Total > 0f)
                {
                    UiSkin.Rect(new Rect(r.x + 2f, r.yMax - 4f, size - 4f, 2f), new Color(0f, 0f, 0f, 0.7f));
                    UiSkin.Rect(new Rect(r.x + 2f, r.yMax - 4f, (size - 4f) * Mathf.Clamp01(s.Left / s.Total), 2f), s.Color);
                }
            }
            if (DebugHover >= 0 && DebugHover < all.Count) hover = DebugHover;
            float rowEnd = x + all.Count * (size + gap);
            if (!cursorFree)
                GUI.Label(new Rect(rowEnd, y + 7f, 120f, 18f), "ALT: inspect", new GUIStyle(UiSkin.Small) { fontSize = 11, normal = { textColor = new Color(0.75f, 0.72f, 0.65f, 0.75f) } });
            if (hover < 0) return;

            // The tooltip card above the badge.
            var h = all[hover];
            var body = new GUIStyle(UiSkin.Small) { wordWrap = true, fontSize = 13, normal = { textColor = new Color(0.92f, 0.9f, 0.85f) } };
            const float tw = 300f;
            float textH = body.CalcHeight(new GUIContent(h.Text), tw - 20f);
            float th = 30f + textH + 22f;
            float tx = Mathf.Min(x + hover * (size + gap), UiScale.W - tw - 8f), ty = y - th - 8f;
            UiSkin.Rect(new Rect(tx - 1.5f, ty - 1.5f, tw + 3f, th + 3f), new Color(h.Color.r, h.Color.g, h.Color.b, 0.9f));
            UiSkin.Rect(new Rect(tx, ty, tw, th), new Color(0.04f, 0.035f, 0.03f, 0.96f));
            GUI.Label(new Rect(tx + 10f, ty + 6f, tw - 20f, 22f), h.Name.ToUpperInvariant(), new GUIStyle(UiSkin.Small) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = h.Color } });
            GUI.Label(new Rect(tx + 10f, ty + 28f, tw - 20f, textH + 4f), h.Text, body);
            GUI.Label(new Rect(tx + 10f, ty + th - 22f, tw - 20f, 18f), h.Lasts, new GUIStyle(UiSkin.Small) { fontSize = 11, fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.75f, 0.72f, 0.65f) } });
        }
    }
}
