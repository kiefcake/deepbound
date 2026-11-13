using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// Meta-progression that outlives a run: <b>Renown</b>, earned at the end of every run,
    /// spent at the camp tailor on cosmetics — cloak dyes and auras. Nothing here changes
    /// power; it is how you show where you have been. Some pieces also need a boss felled
    /// at least once.
    ///
    /// <para>
    /// Saved per machine in PlayerPrefs (the client owns its own wardrobe); what you wear is
    /// synced to the party through <see cref="Player.NetworkPlayer.CloakDye"/> and
    /// <see cref="Player.NetworkPlayer.Aura"/>.
    /// </para>
    /// </summary>
    public static class Cosmetics
    {
        public enum Kind { Cloak, Aura, Heraldry, Pattern, WeaponGlow, Title, Pet }

        public struct Item
        {
            public string Name;
            public Color Cloak, Trim;   // for an aura, Cloak is the mote colour
            public int Cost;
            public int NeedsBoss;       // -1 = none; otherwise the depth of the boss that must have been felled
            public Motif Motif;         // patterns only
            public bool Glows, Gilded;  // patterns only: the motif glows / is gold leaf
        }

        /// <summary>
        /// Index 0 is "your class's own colours" (no override). Append only: indices are saved and synced.
        /// Brightened 2026-10-21 (the user: "make the accessory colours brighter"): the cloth bake and the
        /// dungeon's dim light ate the old near-black dyes, so every dye now reads at a glance.
        /// </summary>
        public static readonly Item[] Dyes =
        {
            new Item { Name = "Class Colours", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Ashen Grey", Cloak = new Color(0.58f, 0.57f, 0.55f), Trim = new Color(0.92f, 0.92f, 0.95f), Cost = 40, NeedsBoss = -1 },
            new Item { Name = "Blood Crimson", Cloak = new Color(0.78f, 0.08f, 0.1f), Trim = new Color(1f, 0.78f, 0.3f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Midnight", Cloak = new Color(0.14f, 0.24f, 0.62f), Trim = new Color(0.82f, 0.87f, 1f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Royal Violet", Cloak = new Color(0.52f, 0.18f, 0.78f), Trim = new Color(1f, 0.8f, 0.3f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Bone White", Cloak = new Color(0.97f, 0.94f, 0.84f), Trim = new Color(0.8f, 0.1f, 0.1f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Raven Black", Cloak = new Color(0.13f, 0.13f, 0.17f), Trim = new Color(0.95f, 0.14f, 0.16f), Cost = 150, NeedsBoss = -1 },
            new Item { Name = "Warden's Ember", Cloak = new Color(0.82f, 0.32f, 0.08f), Trim = new Color(1f, 0.72f, 0.25f), Cost = 120, NeedsBoss = 0 },
            new Item { Name = "Drowned Teal", Cloak = new Color(0.06f, 0.55f, 0.52f), Trim = new Color(0.55f, 1f, 0.88f), Cost = 160, NeedsBoss = 1 },
            new Item { Name = "Hollow Gold", Cloak = new Color(0.92f, 0.68f, 0.16f), Trim = new Color(0.5f, 0.8f, 1f), Cost = 220, NeedsBoss = 2 },
            new Item { Name = "Cardinal's Scarlet", Cloak = new Color(0.95f, 0.07f, 0.09f), Trim = new Color(1f, 0.9f, 0.5f), Cost = 300, NeedsBoss = 3 },
            new Item { Name = "Plague Green", Cloak = new Color(0.36f, 0.66f, 0.14f), Trim = new Color(0.95f, 0.92f, 0.78f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Frost Blue", Cloak = new Color(0.45f, 0.72f, 1f), Trim = new Color(0.95f, 0.97f, 1f), Cost = 140, NeedsBoss = -1 },
            new Item { Name = "Grave Rose", Cloak = new Color(0.92f, 0.42f, 0.6f), Trim = new Color(0.98f, 0.95f, 0.88f), Cost = 140, NeedsBoss = -1 },
            new Item { Name = "Candle Yellow", Cloak = new Color(1f, 0.84f, 0.3f), Trim = new Color(0.45f, 0.12f, 0.08f), Cost = 160, NeedsBoss = -1 },
            // 2026-11-13, the user: "please also work on cosmetics, there needs to be more".
            new Item { Name = "Moss and Iron", Cloak = new Color(0.3f, 0.42f, 0.22f), Trim = new Color(0.62f, 0.64f, 0.68f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Gravedust", Cloak = new Color(0.62f, 0.55f, 0.44f), Trim = new Color(0.3f, 0.22f, 0.16f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Wine Dark", Cloak = new Color(0.42f, 0.06f, 0.18f), Trim = new Color(0.95f, 0.8f, 0.55f), Cost = 140, NeedsBoss = -1 },
            new Item { Name = "Penitent Sackcloth", Cloak = new Color(0.66f, 0.56f, 0.4f), Trim = new Color(0.55f, 0.08f, 0.06f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Witchlight", Cloak = new Color(0.18f, 0.7f, 0.4f), Trim = new Color(0.75f, 0.3f, 0.95f), Cost = 180, NeedsBoss = 1 },
            new Item { Name = "Black and Gold", Cloak = new Color(0.1f, 0.09f, 0.08f), Trim = new Color(1f, 0.8f, 0.3f), Cost = 260, NeedsBoss = 2 },
            new Item { Name = "Dawnshroud", Cloak = new Color(1f, 0.9f, 0.72f), Trim = new Color(0.95f, 0.55f, 0.2f), Cost = 360, NeedsBoss = 4 },
        };

        /// <summary>Motes that drift up around you. Index 0 is none. Append only.</summary>
        public static readonly Item[] Auras =
        {
            new Item { Name = "No Aura", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Cinders", Cloak = new Color(1f, 0.5f, 0.15f), Cost = 90, NeedsBoss = -1 },
            new Item { Name = "Grave Mist", Cloak = new Color(0.6f, 1f, 0.6f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Frostbreath", Cloak = new Color(0.6f, 0.9f, 1f), Cost = 140, NeedsBoss = 1 },
            new Item { Name = "Hollow Starlight", Cloak = new Color(0.75f, 0.65f, 1f), Cost = 200, NeedsBoss = 2 },
            new Item { Name = "Last Rites", Cloak = new Color(1f, 0.86f, 0.45f), Cost = 280, NeedsBoss = 3 },
            new Item { Name = "Ashfall", Cloak = new Color(0.72f, 0.7f, 0.68f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Blood Moon", Cloak = new Color(1f, 0.15f, 0.12f), Cost = 140, NeedsBoss = -1 },
            new Item { Name = "Moth Dust", Cloak = new Color(0.95f, 0.85f, 0.6f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Witch Fire", Cloak = new Color(0.35f, 1f, 0.75f), Cost = 180, NeedsBoss = 0 },
            new Item { Name = "Void Motes", Cloak = new Color(0.55f, 0.25f, 1f), Cost = 240, NeedsBoss = 2 },
            new Item { Name = "The Unsung's Dawn", Cloak = new Color(1f, 0.95f, 0.75f), Cost = 400, NeedsBoss = 4 },
        };

        /// <summary>
        /// Heraldry (2026-10-20): the colour of your tabard, plume, sash, hat band and every other piece
        /// in the party colour, on any class. Index 0 is "your party colour". Append only. Brightened 2026-10-21.
        /// </summary>
        public static readonly Item[] Heraldries =
        {
            new Item { Name = "Party Colour", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Oxblood", Cloak = new Color(0.8f, 0.12f, 0.12f), Cost = 40, NeedsBoss = -1 },
            new Item { Name = "Forest Green", Cloak = new Color(0.22f, 0.66f, 0.28f), Cost = 40, NeedsBoss = -1 },
            new Item { Name = "Azure", Cloak = new Color(0.25f, 0.52f, 1f), Cost = 40, NeedsBoss = -1 },
            new Item { Name = "Saffron", Cloak = new Color(1f, 0.76f, 0.2f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Plum", Cloak = new Color(0.68f, 0.26f, 0.74f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Ivory", Cloak = new Color(0.98f, 0.95f, 0.86f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Charcoal", Cloak = new Color(0.32f, 0.32f, 0.36f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Teal", Cloak = new Color(0.12f, 0.76f, 0.72f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Rose", Cloak = new Color(1f, 0.5f, 0.66f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Ember Orange", Cloak = new Color(1f, 0.5f, 0.12f), Cost = 120, NeedsBoss = 0 },
            new Item { Name = "Sea Glass", Cloak = new Color(0.55f, 0.97f, 0.86f), Cost = 160, NeedsBoss = 1 },
            new Item { Name = "Royal Gold", Cloak = new Color(1f, 0.82f, 0.3f), Cost = 220, NeedsBoss = 2 },
            new Item { Name = "Cathedral Purple", Cloak = new Color(0.55f, 0.24f, 0.95f), Cost = 300, NeedsBoss = 3 },
            new Item { Name = "Bone", Cloak = new Color(0.93f, 0.89f, 0.76f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Lime", Cloak = new Color(0.62f, 0.92f, 0.2f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Rust", Cloak = new Color(0.72f, 0.32f, 0.14f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Slate Blue", Cloak = new Color(0.36f, 0.46f, 0.62f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Bruise", Cloak = new Color(0.4f, 0.2f, 0.45f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Witch Green", Cloak = new Color(0.3f, 0.95f, 0.55f), Cost = 120, NeedsBoss = 0 },
            new Item { Name = "Bloodgold", Cloak = new Color(0.9f, 0.3f, 0.12f), Cost = 260, NeedsBoss = 3 },
        };

        /// <summary>
        /// Patterns (2026-10-21, the user: "add some patterns that fit the game theme, like a skeleton head or
        /// bone print"): a motif sewn on the back of your cloak, on any class and any dye (built by
        /// <see cref="Art.GothicKit"/>). Cloak = the motif's colour, Trim = its eye sockets and seams;
        /// Motif picks the shape. Index 0 is a plain cloak. Append only.
        /// </summary>
        public static readonly Item[] Patterns =
        {
            new Item { Name = "Plain Cloak", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Death's Head", Cloak = new Color(0.96f, 0.93f, 0.84f), Trim = new Color(0.05f, 0.04f, 0.04f), Motif = Motif.Skull, Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Skull and Crossbones", Cloak = new Color(0.96f, 0.93f, 0.84f), Trim = new Color(0.05f, 0.04f, 0.04f), Motif = Motif.Crossbones, Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Bone Print", Cloak = new Color(0.95f, 0.92f, 0.82f), Trim = new Color(0.05f, 0.04f, 0.04f), Motif = Motif.BonePrint, Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Ribcage", Cloak = new Color(0.95f, 0.92f, 0.82f), Trim = new Color(0.05f, 0.04f, 0.04f), Motif = Motif.Ribcage, Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Skull Hem", Cloak = new Color(0.96f, 0.93f, 0.84f), Trim = new Color(0.05f, 0.04f, 0.04f), Motif = Motif.SkullHem, Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Grave Glow", Cloak = new Color(0.45f, 1f, 0.5f), Trim = new Color(0.02f, 0.06f, 0.03f), Motif = Motif.Skull, Glows = true, Cost = 140, NeedsBoss = 0 },
            new Item { Name = "Gilded Jolly Roger", Cloak = new Color(1f, 0.82f, 0.36f), Trim = new Color(0.08f, 0.05f, 0.02f), Motif = Motif.Crossbones, Gilded = true, Cost = 220, NeedsBoss = 2 },
            new Item { Name = "Abyssal Spine", Cloak = new Color(0.72f, 0.45f, 1f), Trim = new Color(0.04f, 0.02f, 0.06f), Motif = Motif.Ribcage, Glows = true, Cost = 300, NeedsBoss = 3 },
            new Item { Name = "Black Bone Print", Cloak = new Color(0.08f, 0.08f, 0.1f), Trim = new Color(0.85f, 0.1f, 0.1f), Motif = Motif.BonePrint, Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Ember Skull", Cloak = new Color(1f, 0.55f, 0.2f), Trim = new Color(0.1f, 0.03f, 0.01f), Motif = Motif.Skull, Glows = true, Cost = 180, NeedsBoss = 1 },
            // 2026-11-13: four new motifs, each in a plain and a finer cut.
            new Item { Name = "Death's-Head Moth", Cloak = new Color(0.9f, 0.82f, 0.62f), Trim = new Color(0.08f, 0.05f, 0.04f), Motif = Motif.Moth, Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Ghost Moth", Cloak = new Color(0.8f, 0.92f, 1f), Trim = new Color(0.04f, 0.06f, 0.12f), Motif = Motif.Moth, Glows = true, Cost = 200, NeedsBoss = 1 },
            new Item { Name = "Gallows Raven", Cloak = new Color(0.86f, 0.84f, 0.8f), Trim = new Color(0.75f, 0.1f, 0.08f), Motif = Motif.Raven, Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Golden Raven", Cloak = new Color(1f, 0.82f, 0.36f), Trim = new Color(0.08f, 0.05f, 0.02f), Motif = Motif.Raven, Gilded = true, Cost = 240, NeedsBoss = 2 },
            new Item { Name = "The Watching Eye", Cloak = new Color(0.95f, 0.92f, 0.84f), Trim = new Color(0.12f, 0.06f, 0.04f), Motif = Motif.Eye, Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Abyssal Gaze", Cloak = new Color(0.4f, 1f, 0.9f), Trim = new Color(0.02f, 0.05f, 0.05f), Motif = Motif.Eye, Glows = true, Cost = 320, NeedsBoss = 4 },
            new Item { Name = "Crowned Skull", Cloak = new Color(0.96f, 0.93f, 0.84f), Trim = new Color(0.05f, 0.04f, 0.04f), Motif = Motif.CrownedSkull, Cost = 140, NeedsBoss = -1 },
            new Item { Name = "Hollow King's Seal", Cloak = new Color(1f, 0.82f, 0.36f), Trim = new Color(0.08f, 0.05f, 0.02f), Motif = Motif.CrownedSkull, Gilded = true, Cost = 260, NeedsBoss = 2 },
        };

        public enum Motif { None, Skull, Crossbones, BonePrint, Ribcage, SkullHem, Moth, Raven, Eye, CrownedSkull }

        /// <summary>
        /// Weapon glows (2026-10-24, the user: "work on more customisation"): a burning edge along your blade, staff or
        /// bow and the colour of your swing trails. Index 0 is none. Append only.
        /// </summary>
        public static readonly Item[] WeaponGlows =
        {
            new Item { Name = "No Glow", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Ember Edge", Cloak = new Color(1f, 0.5f, 0.15f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Frostfire", Cloak = new Color(0.5f, 0.85f, 1f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Grave Green", Cloak = new Color(0.45f, 1f, 0.45f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Bloodletter", Cloak = new Color(1f, 0.12f, 0.1f), Cost = 140, NeedsBoss = -1 },
            new Item { Name = "Void", Cloak = new Color(0.62f, 0.32f, 1f), Cost = 160, NeedsBoss = 1 },
            new Item { Name = "Kingsgold", Cloak = new Color(1f, 0.82f, 0.35f), Cost = 220, NeedsBoss = 2 },
            new Item { Name = "Hymnlight", Cloak = new Color(1f, 0.97f, 0.85f), Cost = 260, NeedsBoss = 3 },
            new Item { Name = "Abyssal", Cloak = new Color(0.35f, 1f, 0.9f), Cost = 320, NeedsBoss = 4 },
            new Item { Name = "Witchfire", Cloak = new Color(0.3f, 1f, 0.65f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Rosewater", Cloak = new Color(1f, 0.5f, 0.7f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Cold Iron", Cloak = new Color(0.7f, 0.78f, 0.9f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Tide Light", Cloak = new Color(0.2f, 0.7f, 1f), Cost = 180, NeedsBoss = 1 },
            new Item { Name = "Cardinal's Wrath", Cloak = new Color(1f, 0.3f, 0.15f), Cost = 260, NeedsBoss = 3 },
        };

        /// <summary>Titles worn under your name on nameplates and the run summary. Index 0 is none. Append only.</summary>
        public static readonly Item[] Titles =
        {
            new Item { Name = "No Title", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "the Delver", Cost = 30, NeedsBoss = -1 },
            new Item { Name = "the Grim", Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Bonebreaker", Cost = 80, NeedsBoss = -1 },
            new Item { Name = "the Unlit", Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Lantern-Bearer", Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Gravebane", Cost = 120, NeedsBoss = 0 },
            new Item { Name = "Tidebreaker", Cost = 150, NeedsBoss = 1 },
            new Item { Name = "Kingslayer", Cost = 200, NeedsBoss = 2 },
            new Item { Name = "Hymnbreaker", Cost = 250, NeedsBoss = 3 },
            new Item { Name = "Who Silenced the Unsung", Cost = 400, NeedsBoss = 4 },
            new Item { Name = "the Candle-Keeper", Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Oathbreaker", Cost = 90, NeedsBoss = -1 },
            new Item { Name = "the Twice-Buried", Cost = 110, NeedsBoss = -1 },
            new Item { Name = "Friend of Rats", Cost = 50, NeedsBoss = -1 },
            new Item { Name = "Gorm's Better", Cost = 120, NeedsBoss = -1 },
            new Item { Name = "the Unbowed", Cost = 140, NeedsBoss = 0 },
            new Item { Name = "Drowned and Returned", Cost = 170, NeedsBoss = 1 },
            new Item { Name = "Crownless", Cost = 220, NeedsBoss = 2 },
            new Item { Name = "Last of the Choir", Cost = 270, NeedsBoss = 3 },
        };

        /// <summary>
        /// Companions (2026-11-07), in <see cref="Player.PetCompanion.Kind"/> order. Index 0 is none. Append only. They fetch
        /// gold and embers and warn of hidden plates; none fights.
        /// </summary>
        public static readonly Item[] Pets =
        {
            new Item { Name = "No Companion", Cost = 0, NeedsBoss = -1 },
            new Item { Name = "Gallows Raven", Cloak = new Color(0.1f, 0.1f, 0.12f), Cost = 80, NeedsBoss = -1 },
            new Item { Name = "Cellar Rat", Cloak = new Color(0.4f, 0.36f, 0.32f), Cost = 60, NeedsBoss = -1 },
            new Item { Name = "Bone Hound", Cloak = new Color(0.86f, 0.82f, 0.72f), Cost = 160, NeedsBoss = 0 },
            new Item { Name = "Castle Kitten", Cloak = new Color(0.45f, 0.4f, 0.36f), Cost = 120, NeedsBoss = -1 },
            new Item { Name = "Lantern Wisp", Cloak = new Color(0.6f, 0.85f, 1f), Cost = 220, NeedsBoss = 3 },
            new Item { Name = "Grave Bat", Cloak = new Color(0.2f, 0.16f, 0.18f), Cost = 100, NeedsBoss = -1 },
            new Item { Name = "Plague Toad", Cloak = new Color(0.4f, 0.52f, 0.22f), Cost = 90, NeedsBoss = -1 },
            new Item { Name = "Candle Imp", Cloak = new Color(0.55f, 0.3f, 0.22f), Cost = 180, NeedsBoss = 1 },
            new Item { Name = "Mourning Moth", Cloak = new Color(0.85f, 0.8f, 0.65f), Cost = 140, NeedsBoss = -1 },
        };

        public static Color? WeaponGlowColor(int i) => i > 0 && i < WeaponGlows.Length ? WeaponGlows[i].Cloak : (Color?)null;
        public static string TitleOf(int i) => i > 0 && i < Titles.Length ? Titles[i].Name : null;

        public static Item[] Of(Kind kind) => kind == Kind.Aura ? Auras : kind == Kind.Heraldry ? Heraldries : kind == Kind.Pattern ? Patterns
            : kind == Kind.WeaponGlow ? WeaponGlows : kind == Kind.Title ? Titles : kind == Kind.Pet ? Pets : Dyes;

        /// <summary>The pattern to sew on a cloak, or null for a plain one.</summary>
        public static Item? PatternOf(int i) => i > 0 && i < Patterns.Length ? Patterns[i] : (Item?)null;

        /// <summary>The heraldry colour, or null for the party colour.</summary>
        public static Color? HeraldryColor(int i) => i > 0 && i < Heraldries.Length ? Heraldries[i].Cloak : (Color?)null;

        private const string KeyRenown = "db.renown", KeyBosses = "db.bosses", KeyLastRun = "db.lastRun", KeyRuns = "db.runs";
        // Glows and titles fell through to the dye keys until 2026-11-07 (wearing a title changed your cloak on the next start).
        private static string KeyOwned(Kind k) => k == Kind.Aura ? "db.auras" : k == Kind.Heraldry ? "db.heraldries" : k == Kind.Pattern ? "db.patterns"
            : k == Kind.WeaponGlow ? "db.glows" : k == Kind.Title ? "db.titles" : k == Kind.Pet ? "db.pets" : "db.dyes";
        private static string KeyWorn(Kind k) => k == Kind.Aura ? "db.aura" : k == Kind.Heraldry ? "db.heraldry" : k == Kind.Pattern ? "db.pattern"
            : k == Kind.WeaponGlow ? "db.glow" : k == Kind.Title ? "db.title" : k == Kind.Pet ? "db.pet" : "db.cloak";

        /// <summary>Set by the smoke/screenshot tests: nothing is read from or written to the real save.</summary>
        public static bool TestMode;

        public static int Renown => TestMode ? 0 : PlayerPrefs.GetInt(KeyRenown, 0);
        public static int RunsFinished => PlayerPrefs.GetInt(KeyRuns, 0);
        private static int BossMask => PlayerPrefs.GetInt(KeyBosses, 0);
        /// <summary>Acts whose master this machine has beaten (bit per act): boss practice unlocks by it.</summary>
        public static int ActsBeaten => BossMask;

        public static int Worn(Kind kind) => TestMode ? 0 : Mathf.Clamp(PlayerPrefs.GetInt(KeyWorn(kind), 0), 0, Of(kind).Length - 1);
        public static int Equipped => Worn(Kind.Cloak);

        /// <summary>For now (the user's call, 2026-10-20) every dye and aura is free and unlocked.</summary>
        public const bool AllFree = true;

        public static bool Owns(Kind kind, int i) => AllFree || i == 0 || (PlayerPrefs.GetInt(KeyOwned(kind), 0) & (1 << i)) != 0;
        public static bool BossFelled(int depth) => (BossMask & (1 << depth)) != 0;
        public static bool Unlocked(Kind kind, int i) => AllFree || Of(kind)[i].NeedsBoss < 0 || BossFelled(Of(kind)[i].NeedsBoss);

        /// <summary>The colours to dress a cloak in, or null for the class's own.</summary>
        public static Item? Get(int dye) => dye > 0 && dye < Dyes.Length ? Dyes[dye] : (Item?)null;

        /// <summary>An aura's mote colour, or null for none.</summary>
        public static Color? AuraColor(int aura) => aura > 0 && aura < Auras.Length ? Auras[aura].Cloak : (Color?)null;

        public static bool Buy(Kind kind, int i)
        {
            var items = Of(kind);
            if (TestMode || Owns(kind, i) || !Unlocked(kind, i) || Renown < items[i].Cost) return false;
            PlayerPrefs.SetInt(KeyRenown, Renown - items[i].Cost);
            PlayerPrefs.SetInt(KeyOwned(kind), PlayerPrefs.GetInt(KeyOwned(kind), 0) | (1 << i));
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>Spend Renown on something that is not a cosmetic (the Legacy boons). False if short or in test mode.</summary>
        public static bool SpendRenown(int amount)
        {
            if (TestMode || amount < 0 || Renown < amount) return false;
            PlayerPrefs.SetInt(KeyRenown, Renown - amount);
            PlayerPrefs.Save();
            return true;
        }

        public static bool Wear(Kind kind, int i)
        {
            if (TestMode || !Owns(kind, i)) return false;
            PlayerPrefs.SetInt(KeyWorn(kind), i);
            PlayerPrefs.Save();
            return true;
        }

        /// <summary>What a run is worth: bosses, kills and combos, a big bonus for clearing it, scaled by difficulty.</summary>
        public static int RenownFor(int bossesFelled, int kills, int combos, bool cleared, int difficulty)
        {
            float r = bossesFelled * 25 + kills / 4 + combos / 5 + (cleared ? 100 : 0);
            return Mathf.RoundToInt(r * (1f + 0.5f * Mathf.Max(0, difficulty)));
        }

        /// <summary>
        /// Award a finished run once (keyed by its start time, so the summary can call this
        /// every frame). Returns the Renown earned, or -1 if this run was already counted.
        /// </summary>
        /// <summary>Gold left in your purse when a run ends is tithed: this much gold per Renown, up to <see cref="TitheCap"/>.</summary>
        public const int GoldPerRenown = 100, TitheCap = 50;
        public static int TitheFor(int goldLeft) => Mathf.Clamp(goldLeft / GoldPerRenown, 0, TitheCap);

        public static int AwardRun(double runKey, int bossesFelled, int kills, int combos, bool cleared, int difficulty, float bonus = 1f, int goldLeft = 0)
        {
            if (TestMode) return -1;
            string key = runKey.ToString("F3");
            if (PlayerPrefs.GetString(KeyLastRun, "") == key) return -1;
            int earned = Mathf.RoundToInt(RenownFor(bossesFelled, kills, combos, cleared, difficulty) * bonus) + TitheFor(goldLeft);
            PlayerPrefs.SetString(KeyLastRun, key);
            PlayerPrefs.SetInt(KeyRenown, Renown + earned);
            PlayerPrefs.SetInt(KeyRuns, RunsFinished + 1);
            int mask = BossMask;
            for (int i = 0; i < bossesFelled; i++) mask |= 1 << i;
            PlayerPrefs.SetInt(KeyBosses, mask);
            PlayerPrefs.Save();
            return earned;
        }
    }
}
