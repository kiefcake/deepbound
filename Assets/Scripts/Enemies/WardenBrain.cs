using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Wardens: the guardians at the end of the first floor of every act (floors 1, 3, 5, 7, 9),
    /// a step below the act's own boss on the floor after. One brain, five of them: each prefab sets
    /// <see cref="act"/>, which picks its name, look (a giant of that act's rank and file) and its
    /// signature move. All of them swing wide, roll a jumpable shockwave, and at half health call
    /// their act's dead to their side.
    ///
    /// <list type="bullet">
    /// <item><b>Ossuary</b> - Marrowgut, the Bone Colossus: Bone Quake, a red circle round it that
    ///   throws everyone inside. Get out, or bash it during the wind-up.</item>
    /// <item><b>Drowned Vaults</b> - the Drowned Knight: Black Tide nova that frostbites (40% slower).</item>
    /// <item><b>Hollow Throne</b> - Sir Aldous Vane, the King's Shield: blocks most of what hits it
    ///   from the front, and charges down a red lane; into a wall, it is dazed and open.</item>
    /// <item><b>Black Cathedral</b> - the Choirmaster: a fan of hex bolts, and a curse on its prey.</item>
    /// <item><b>Abyss</b> - Gorr, the Rot Below: spits a spray of plague and leaves poison pools.</item>
    /// </list>
    /// Below half (2026-10-26) each learns a second move: Marrowgut hurls bone at whoever keeps away (Bone
    /// Toss), the Drowned Knight drags everyone in before the Black Tide breaks (Undertow), Sir Aldous charges
    /// again if the first one finds no wall, the Choirmaster sings a Second Verse after each hymn, and Gorr's
    /// slams leave pools of rot behind.
    /// </summary>
    public class WardenBrain : BossBrain
    {
        [SerializeField] private int act;
        public int Act => act;

        private static readonly string[] Names = { "Marrowgut, the Bone Colossus", "The Drowned Knight", "Sir Aldous Vane", "The Choirmaster", "Gorr, the Rot Below" };
        private static readonly string[] Epithets = { "Warden of the Upper Crypts", "Warden of the Flooded Stair", "The King's Shield", "Warden of the Nave", "Warden of the Last Stair" };
        private static readonly string[] Hints =
        {
            "When the red circle fills around it, get out (or bash it). Jump its shockwaves.",
            "Leave the ring before the Black Tide breaks: it freezes your legs.",
            "It blocks from the front: get round it. Make it charge into a wall and it is open.",
            "Spread out against the hex bolts. Its curse ends when it dies.",
            "Keep moving: its spit and its pools poison. Holy water cleanses.",
        };
        private static readonly string[] SecondHints =
        {
            "Wounded, it hurls bone at whoever keeps away: watch for the circle at your feet.",
            "Wounded, the Undertow drags you in before the Tide breaks: sprint out as soon as it pulls.",
            "Wounded, it charges twice: the second lane is short, so dodge sideways, not back.",
            "Wounded, every hymn has a second verse: move again once the first circles break.",
            "Wounded, its slams leave rot on the floor: fight it away from the pools.",
        };
        private static readonly string[] Words =
        {
            "Bones... to the... pile...",
            "The water is... warm... at last...",
            "The King... will need... another shield...",
            "The hymn... goes on... without me...",
            "Below me... it is... still... singing...",
        };

        public override string DisplayName => Names[Mathf.Clamp(act, 0, 4)];
        public override string Epithet => Epithets[Mathf.Clamp(act, 0, 4)];
        public override string Hint => Hints[Mathf.Clamp(act, 0, 4)];
        /// <summary>Below half every Warden knows its second move.</summary>
        private bool Second => forceSecond || (health != null && health.Fraction < 0.5f);
        private bool forceSecond;

        /// <summary>For tests: use the second move now, wounded or not. False if it could not start.</summary>
        public bool ServerDebugSecond()
        {
            forceSecond = true;
            slamAt = -1f; charge = 0;
            switch (act)
            {
                case 0: return BoneToss();
                case 2:
                    if (target == null) return false;
                    rechargeLeft = 1;
                    BeginChargeWindup(1.1f);
                    return true;
                case 3: BeginHymn(); return true;
                default: BeginSlam(); return true;
            }
        }
        /// <summary>For tests: second moves used (toss, undertow, recharge, verse, rot pool).</summary>
        public int SecondMoves { get; private set; }
        public override string LastWords => Words[Mathf.Clamp(act, 0, 4)];
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.Warden;

        protected override float[] PhaseGates => new[] { 0.5f };
        protected override float ShockwaveCooldown => health.Fraction < 0.5f ? 10f : 14f;
        protected override Color ShockwaveColor => act == 1 ? new Color(0.5f, 0.85f, 1f) : act == 3 ? new Color(0.75f, 0.45f, 1f) : act == 4 ? new Color(0.6f, 0.9f, 0.3f) : new Color(1f, 0.6f, 0.3f);

        private float nextSpecial;
        private float slamAt = -1f;
        private const float SlamRadius = 5.5f, SlamWindup = 1.3f;

        // Built at each kind's own size (Brute 1.45, Plaguebearer 1.15, the rest 1): these make them giants.
        private static readonly float[] Giant = { 1.45f, 1.9f, 1.9f, 1.95f, 1.5f };
        protected override float VisualScale => Giant[Mathf.Clamp(act, 0, 4)];

        /// <summary>Each Warden's own look on top of its kind: trophies, weed, a plume, a candle halo, plague sacs.</summary>
        protected override void DressRig(HumanoidRig rig)
        {
            var t = rig.Torso; var head = rig.Head;
            if (t == null || head == null) return;
            Renderer Tint(GameObject g, Color c) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, c); return r; }
            switch (act)
            {
                case 0:
                    // Marrowgut: a ridge of bone spikes down the back and a string of skulls across the chest.
                    for (int i = 0; i < 6; i++)
                        MeshKit.Part(t, "BackSpike", MeshKit.Lathe("wardenSpike", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.32f) }, 5), Mat.Bone,
                            new Vector3(0f, 0.12f + i * 0.09f, -0.2f), new Vector3(-55f, 0f, (i % 2 == 0 ? 10f : -10f)));
                    for (int i = 0; i < 5; i++)
                    {
                        float a = -50f + i * 25f;
                        var at = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.22f, 0.36f - Mathf.Abs(i - 2) * 0.03f, Mathf.Cos(a * Mathf.Deg2Rad) * 0.2f);
                        MeshKit.Part(t, "TrophySkull", MeshKit.Ball(0.05f, 7, 5), Mat.Bone, at, default, new Vector3(1f, 1.1f, 1f));
                        Tint(MeshKit.Part(t, "TrophyEye", MeshKit.Box(new Vector3(0.04f, 0.012f, 0.01f)), Mat.Dark, at + new Vector3(0f, 0.01f, 0.045f)), Color.white);
                    }
                    break;
                case 1:
                    // The Drowned Knight: weed hanging from shoulders and arms, barnacles, a corroded crest.
                    for (int i = 0; i < 10; i++)
                    {
                        float x = -0.24f + i * 0.053f;
                        Tint(MeshKit.Part(t, "Weed", MeshKit.Box(new Vector3(0.03f, 0.35f + (i % 3) * 0.12f, 0.01f)), Mat.Cloth, new Vector3(x, 0.45f - (i % 3) * 0.06f, (i % 2 == 0 ? 0.16f : -0.17f)), new Vector3(0f, 0f, (i % 2 == 0 ? 6f : -8f))),
                            new Color(0.12f, 0.28f, 0.2f));
                    }
                    for (int i = 0; i < 8; i++)
                        MeshKit.Part(t, "Barnacle", MeshKit.Ball(0.025f, 5, 4), Mat.Rubble, new Vector3(-0.18f + (i % 4) * 0.12f, 0.1f + (i / 4) * 0.3f, 0.17f), default, new Vector3(1f, 0.7f, 1f));
                    Tint(MeshKit.Part(head, "Crest", MeshKit.Box(new Vector3(0.025f, 0.12f, 0.3f)), Mat.Steel, new Vector3(0f, 0.28f, -0.02f)), new Color(0.35f, 0.55f, 0.5f));
                    break;
                case 2:
                    // Sir Aldous Vane: a tall crimson plume, gold pauldrons and the King's tabard.
                    for (int i = 0; i < 5; i++)
                        Tint(MeshKit.Part(head, "Plume", MeshKit.Box(new Vector3(0.05f, 0.3f - i * 0.03f, 0.06f)), Mat.Cloth, new Vector3(0f, 0.3f + i * 0.04f, -0.02f - i * 0.06f), new Vector3(-30f - i * 12f, 0f, 0f)),
                            new Color(0.7f, 0.08f, 0.1f));
                    foreach (float s in new[] { -1f, 1f })
                        MeshKit.Part(t, "Pauldron", MeshKit.Ball(0.13f, 8, 6), Mat.Gold, new Vector3(s * 0.26f, 0.58f, 0f), default, new Vector3(1.1f, 0.6f, 1f));
                    Tint(MeshKit.Part(t, "Tabard", MeshKit.Box(new Vector3(0.34f, 0.62f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.22f, 0.19f)), new Color(0.32f, 0.08f, 0.36f));
                    MeshKit.Part(t, "TabardCrown", MeshKit.Box(new Vector3(0.14f, 0.07f, 0.01f)), Mat.Gold, new Vector3(0f, 0.36f, 0.205f));
                    break;
                case 3:
                {
                    // The Choirmaster: no hood (a boss never hides its face under one); a choir circlet and
                    // a slow halo of floating candles, an open hymnal in the off hand.
                    var hood = head.Find("Hood");
                    if (hood != null) DestroyImmediate(hood.gameObject);
                    MeshKit.Part(head, "Circlet", MeshKit.Cylinder(0.14f, 0.14f, 0.03f, 12), Mat.Gold, new Vector3(0f, 0.2f, 0f));
                    var halo = MeshKit.Pivot(head, "CandleHalo", new Vector3(0f, 0.42f, 0f));
                    halo.gameObject.AddComponent<Art.Spin>().DegreesPerSecond = 25f;
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * 360f / 7f * Mathf.Deg2Rad;
                        var c = new Vector3(Mathf.Cos(a) * 0.32f, 0f, Mathf.Sin(a) * 0.32f);
                        Tint(MeshKit.Part(halo, "Candle", MeshKit.Cylinder(0.018f, 0.018f, 0.1f, 6), Mat.Cloth, c), new Color(0.9f, 0.86f, 0.7f));
                        var fl = MeshKit.Part(halo, "CandleFlame", MeshKit.Ball(0.016f, 5, 4), Mat.Glow, c + Vector3.up * 0.07f, default, new Vector3(1f, 1.6f, 1f));
                        ArtLibrary.SetGlow(fl.GetComponent<Renderer>(), new Color(0.85f, 0.5f, 1f) * 1.4f);
                    }
                    if (rig.ArmL != null)
                    {
                        var book = MeshKit.Part(rig.ArmL, "Hymnal", MeshKit.Box(new Vector3(0.2f, 0.03f, 0.15f)), Mat.Leather, new Vector3(0.04f, -0.6f, 0.08f), new Vector3(20f, 0f, 0f));
                        Tint(book, new Color(0.25f, 0.08f, 0.1f));
                        MeshKit.Part(rig.ArmL, "Pages", MeshKit.Box(new Vector3(0.18f, 0.035f, 0.13f)), Mat.Bone, new Vector3(0.04f, -0.58f, 0.08f), new Vector3(20f, 0f, 0f));
                    }
                    break;
                }
                default:
                {
                    // Gorr: bloated, glowing plague sacs on its back and belly (the weak points), and a cloud of flies.
                    foreach (var (at, r) in new[] { (new Vector3(-0.1f, 0.42f, -0.22f), 0.12f), (new Vector3(0.12f, 0.3f, -0.24f), 0.1f), (new Vector3(0f, 0.12f, 0.2f), 0.14f) })
                    {
                        var sac = MeshKit.Part(t, "PlagueSac", MeshKit.Ball(r, 9, 7), Mat.Glow, at);
                        ArtLibrary.SetGlow(sac.GetComponent<Renderer>(), new Color(0.45f, 0.85f, 0.15f) * 0.9f);
                    }
                    WeakPoint.Add(t, new Vector3(0f, 0.12f, 0.24f), 0.1f, new Color(0.5f, 0.95f, 0.2f));
                    var flies = MeshKit.Pivot(head, "Flies", new Vector3(0f, 0.3f, 0f));
                    flies.gameObject.AddComponent<Art.Spin>().DegreesPerSecond = 160f;
                    for (int i = 0; i < 9; i++)
                        MeshKit.Part(flies, "Fly", MeshKit.Ball(0.01f, 4, 3), Mat.Dark, new Vector3(Mathf.Cos(i * 0.7f) * (0.2f + (i % 3) * 0.08f), (i % 4) * 0.06f, Mathf.Sin(i * 0.7f) * (0.2f + (i % 3) * 0.08f)));
                    break;
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HintFeedRpc(string words) => UI.GameHud.Feed(words, new Color(1f, 0.82f, 0.5f));

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextSpecial = Time.time + 5f;
            if (IsServer && act == 2) health.IncomingModifiers.Add(GuardModifier);
        }

        protected override void OnPhaseGate(int index)
        {
            // The second move, named as it is learned.
            HintFeedRpc(SecondHints[Mathf.Clamp(act, 0, 4)]);
            string[] taunts = { "More bones for the pile!", "Rise, drowned ones. Rise!", "To me, the King's guard!", "Sing, my choir. SING!", "Wake, all of you. Feed." };
            string taunt = taunts[Mathf.Clamp(act, 0, 4)];
            switch (act)
            {
                case 0:
                    // The first boss of the game: a breather and a few skeletons, nothing more.
                    Transition("Called to Arms", taunt, 1.8f, () => Summon(1 + scaledPlayers, 4f));
                    break;
                case 1:
                    // The Undertow: rings of black water with a gap that shifts each time.
                    Transition("The Undertow", taunt, 1.4f, () =>
                    {
                        Summon(scaledPlayers, 4f, 0.8f);
                        StartPattern(new Pattern { Arms = 16, Interval = 0.95f, Speed = 9f, Spin = 360f / 32f + 7f, Damage = damage * 0.5f, Kind = ProjectileKind.Frostbolt, Duration = 3.2f, Range = 24f });
                    });
                    break;
                case 2:
                    // Shield Wall: invulnerable until its guard falls.
                    var guards = Summon(1 + scaledPlayers, 4.5f);
                    InvulnerableWhile("Shield Wall", taunt, "The King's guard shields Sir Aldous: kick their shields aside and cut them down", guards, 22f,
                        () => Taunt("Then I hold the line alone."));
                    break;
                case 3:
                    // The Choir Sings: immune while two counter-turning spirals sweep the nave.
                    Survive("The Choir Sings", taunt, "Survive the hymn: the spirals turn both ways, so keep moving", 9f, () => Summon(1 + scaledPlayers, 4f, 0.8f));
                    for (int e = 0; e < 2; e++)
                        StartPattern(new Pattern
                        {
                            Arms = 3, Interval = 0.13f, Speed = 11f, Spin = e == 0 ? 12f : -12f, Damage = damage * 0.4f, Kind = ProjectileKind.ShadowBolt,
                            Duration = 8.6f, AimedEvery = e == 0 ? 1.6f : 0f, Range = 26f, Origin = Orbit(3f, e == 0 ? 35f : -35f, e * 180f, 1.6f),
                        });
                    break;
                default:
                    // Rot Rising: immune while a poison spiral spits out, flipping as it goes.
                    Survive("Rot Rising", taunt, "Survive the rot: move with the spiral and slip its gaps", 8f, () => Summon(1 + scaledPlayers, 4f, 0.8f));
                    StartPattern(new Pattern
                    {
                        Arms = 4, Interval = 0.12f, Speed = 10.5f, Spin = 13f, FlipEvery = 2.5f, Kind = ProjectileKind.Spit, Duration = 7.6f, AimedEvery = 1.5f, Range = 24f,
                        Damage = damage * 0.35f, Origin = Orbit(1.8f, 60f, 0f, 1.6f),
                        OnHit = hit => { hit.ServerDamage(damage * 0.35f * PatternDamageScale, transform.position, NetworkObjectId); if (hit.TryGetComponent<PlayerDebuffs>(out var d)) d.ServerPoison(3f, 3f + 1.5f * scaledDepth); },
                    });
                    break;
            }
        }

        // What it throws at a fleeing party: the spear of the King's Shield, hex bolts, spit.
        protected override ProjectileKind? QuickKind => act == 2 ? ProjectileKind.Arrow : act == 3 ? ProjectileKind.ShadowBolt : act == 4 ? ProjectileKind.Spit : act == 1 ? ProjectileKind.Frostbolt : (ProjectileKind?)null;

        protected override void BeginAttack()
        {
            windup = health.Fraction < 0.5f ? 0.75f : 0.9f;
            attackArc = 150f;
            base.BeginAttack();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (TryShockwave()) return true;
            if (Time.time < nextSpecial) return false;
            switch (act)
            {
                case 2:
                    if (distanceToTarget < 4.5f || !CombatQuery.ClearLine(transform.position + Vector3.up * 1.2f, target)) return false;
                    rechargeLeft = Second ? 1 : 0;
                    BeginChargeWindup(1.1f);
                    nextSpecial = Time.time + 9f;
                    return true;
                case 3:
                    // Alternates: a fan of hex bolts (and a curse on its prey), then the Hymn of Madness.
                    if (hymnNext) { BeginHymn(); nextSpecial = Time.time + 7f; hymnNext = false; return true; }
                    Volley(ProjectileKind.ShadowBolt, 5, 14f, 15f);
                    if (target != null && target.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerCurse(8f, NetworkObjectId);
                    nextSpecial = Time.time + 6.5f;
                    hymnNext = true;
                    return true;
                case 4:
                    if (distanceToTarget > SlamRadius + 3f) { Volley(ProjectileKind.Spit, 5, 12f, 13f); nextSpecial = Time.time + 6f; return true; }
                    BeginSlam();
                    nextSpecial = Time.time + 8f;
                    return true;
                default:
                    if (distanceToTarget > SlamRadius + 1.5f)
                    {
                        if (!Second || !BoneToss()) return false;
                        nextSpecial = Time.time + 6f;
                        return true;
                    }
                    BeginSlam();
                    nextSpecial = Time.time + 8f;
                    return true;
            }
        }

        protected override void Update()
        {
            if (HeldByStun()) return;
            if (IsServer && !IsDead) { ResolveHymns(); ResolveTosses(); TickRot(); }
            if (IsServer && !IsDead)
            {
                if (slamAt > 0f)
                {
                    agent.isStopped = true;
                    if (Time.time >= slamAt) ResolveSlam();
                    return;
                }
                if (charge == 1)
                {
                    agent.isStopped = true;
                    transform.rotation = Quaternion.LookRotation(chargeDir);
                    if (Time.time >= chargeAt) { charge = 2; charged = 0f; PlayActionRpc(RigAction.Bash, 0.6f); }
                    return;
                }
                if (charge == 2) { StepCharge(); return; }
                if (Time.time < dazedUntil) { agent.isStopped = true; return; }
            }
            base.Update();
        }

        public override void ServerStun(float seconds)
        {
            // A bash in the wind-up snuffs the slam or the charge, as with the rank and file.
            if (slamAt > 0f) { slamAt = -1f; CancelTelegraphRpc(); }
            if (charge == 1) { charge = 0; CancelTelegraphRpc(); }
            if (charge == 2) return;
            base.ServerStun(Mathf.Min(seconds, 0.8f));
        }

        // ---- The Hymn of Madness: violet circles open where each player is heading; inside, madness ----
        private bool hymnNext;
        private readonly List<(Vector3 at, float when)> hymns = new List<(Vector3, float)>();
        private const float HymnRadius = 2.6f, HymnWindup = 1.4f;

        private float secondVerseAt = -1f;

        private void BeginHymn()
        {
            PlayActionRpc(RigAction.Cast, 1f);
            busyUntil = Time.time + 1f;
            SingVerse();
            // The Second Verse: as the first circles break, a second set opens where everyone is heading next.
            if (Second) secondVerseAt = Time.time + Tell(HymnWindup) + 0.25f;
        }

        private void SingVerse()
        {
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                var at = PredictedFeet(p, HymnWindup);
                hymns.Add((at, Time.time + HymnWindup));
                TintedCircleRpc(at, HymnRadius, HymnWindup, Telegraph.Madness);
            }
        }

        private void ResolveHymns()
        {
            if (secondVerseAt > 0f && Time.time >= secondVerseAt)
            {
                secondVerseAt = -1f;
                SecondMoves++;
                SecondFxRpc("SECOND VERSE", new Color(0.8f, 0.35f, 1f));
                SingVerse();
            }
            for (int i = hymns.Count - 1; i >= 0; i--)
            {
                if (Time.time < hymns[i].when) continue;
                var at = hymns[i].at;
                hymns.RemoveAt(i);
                foreach (var v in CombatQuery.Circle(at, HymnRadius, Team.Players))
                {
                    v.ServerDamage(damage * 0.6f, at + Vector3.up, NetworkObjectId);
                    if (v.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerMania(2.2f);
                }
                HymnFxRpc(at);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnFxRpc(Vector3 at)
        {
            var c = new Color(0.8f, 0.35f, 1f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 1f, 0.8f);
            Vfx.Ring(at, HymnRadius, c, 0.4f);
            Vfx.Embers(at, 16, c, 3f, 0.06f, 0.9f);
        }

        // ---- Slam / nova / pools: a red circle round it fills, then breaks ----

        private void BeginSlam()
        {
            slamAt = Time.time + Tell(SlamWindup);
            // The Undertow: wounded, the Drowned Knight drags the party in first, and gives them a breath longer to run.
            if (act == 1 && Second)
            {
                slamAt += 0.4f;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var to = transform.position - p.transform.position;
                    to.y = 0f;
                    float d = to.magnitude;
                    if (d < 2f || d > 14f) continue;
                    KnockPlayer(p.Health, to / d * Mathf.Min(9f, d * 1.1f) + Vector3.up * 2f);
                }
                SecondMoves++;
                SecondFxRpc("THE UNDERTOW", new Color(0.5f, 0.85f, 1f));
            }
            busyUntil = slamAt + 1f;
            PlayActionRpc(act == 1 || act == 4 ? RigAction.Cast : RigAction.Slam, Tell(SlamWindup) + 0.3f);
            if (act == 1 || act == 4) TintedKeyedCircleRpc(transform.position, SlamRadius, Tell(SlamWindup), act == 1 ? Telegraph.Frost : Telegraph.Poison);
            else CircleTelegraphRpc(transform.position, SlamRadius, Tell(SlamWindup), true);
        }

        private void ResolveSlam()
        {
            slamAt = -1f;
            foreach (var victim in CombatQuery.Circle(transform.position, SlamRadius, Team.Players))
            {
                victim.ServerDamage(damage * 1.5f, transform.position, NetworkObjectId);
                var deb = victim.GetComponent<PlayerDebuffs>();
                if (act == 1 && deb != null) { deb.ServerFrostbite(4f); deb.ServerRoot(0.9f); }
                else if (act == 4 && deb != null) deb.ServerPoison(5f, 4f + 2f * scaledDepth);
                else
                {
                    var away = victim.transform.position - transform.position;
                    away.y = 0f;
                    KnockPlayer(victim, away.normalized * 11f + Vector3.up * 5f);
                }
            }
            SlamFxRpc(transform.position, SlamRadius, act);
            if (act == 4 && Second) AddRot(transform.position);
        }

        protected override void OnBroken()
        {
            slamAt = -1f;
            secondVerseAt = -1f;
            if (charge != 0) { charge = 0; rechargeLeft = 0; }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SecondFxRpc(string words, Color c)
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 4.6f, words, c);
            Sfx.Play(act == 1 ? Sound.Splash : act == 2 ? Sound.WarCry : Sound.Whisper, transform.position + Vector3.up * 2f, 1f, 0.8f);
            if (act == 1) Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 14f, c, 0.7f);
        }

        // ---- Bone Toss (Marrowgut, wounded): a lump of bone lobbed at whoever keeps away ----
        private readonly List<(Vector3 at, float when)> tosses = new List<(Vector3, float)>();
        private const float TossRadius = 2.4f, TossFlight = 1.3f;

        private bool BoneToss()
        {
            if (target == null || !target.TryGetComponent<PlayerLife>(out var quarry)) return false;
            float flight = Tell(TossFlight);
            var at = PredictedFeet(quarry, flight);
            tosses.Add((at, Time.time + flight));
            busyUntil = Time.time + 0.8f;
            PlayActionRpc(RigAction.Slam, 0.6f);
            TintedCircleRpc(at, TossRadius, flight, new Color(0.9f, 0.82f, 0.65f, 0.24f));
            TossFxRpc(transform.position + Vector3.up * 3.2f, at, flight);
            SecondMoves++;
            return true;
        }

        private void ResolveTosses()
        {
            for (int i = tosses.Count - 1; i >= 0; i--)
            {
                if (Time.time < tosses[i].when) continue;
                var at = tosses[i].at;
                tosses.RemoveAt(i);
                foreach (var v in CombatQuery.Circle(at, TossRadius, Team.Players))
                {
                    v.ServerDamage(damage * 1.2f, at + Vector3.up, NetworkObjectId);
                    var away = v.transform.position - at;
                    away.y = 0f;
                    KnockPlayer(v, (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 7f + Vector3.up * 4f);
                }
                SlamFxRpc(at, TossRadius, 0);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TossFxRpc(Vector3 from, Vector3 to, float seconds)
        {
            Sfx.Play(Sound.Swing, from, 1f, 0.5f);
            StartCoroutine(Lob(from, to, seconds));
        }

        private System.Collections.IEnumerator Lob(Vector3 from, Vector3 to, float seconds)
        {
            var rock = MeshKit.Part(null, "Bone Toss", MeshKit.Ball(0.55f, 8, 6), Mat.Bone, from);
            if (rock.TryGetComponent<Collider>(out var col)) Destroy(col);
            float h = Mathf.Max(3f, Vector3.Distance(from, to) * 0.35f);
            for (float t = 0f; t < seconds && rock != null; t += Time.deltaTime)
            {
                float k = t / seconds;
                rock.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (4f * h * k * (1f - k));
                rock.transform.Rotate(300f * Time.deltaTime, 170f * Time.deltaTime, 0f);
                yield return null;
            }
            if (rock != null) Destroy(rock);
        }

        // ---- Rot pools (Gorr, wounded): each slam leaves a churning pool of rot that poisons whoever stands in it ----
        private readonly List<(Vector3 at, float until)> rot = new List<(Vector3, float)>();
        private float nextRotTick;
        public const float RotRadius = 3.2f, RotSeconds = 8f;
        /// <summary>For tests: pools of rot on the floor right now.</summary>
        public int RotPools => rot.Count;

        private void AddRot(Vector3 at)
        {
            at = Vfx.Floor(at);
            if (rot.Count >= 3) rot.RemoveAt(0);
            rot.Add((at, Time.time + RotSeconds));
            RotFxRpc(at, RotRadius, RotSeconds);
            SecondMoves++;
        }

        private void TickRot()
        {
            if (rot.Count == 0 || Time.time < nextRotTick) return;
            nextRotTick = Time.time + 0.5f;
            for (int i = rot.Count - 1; i >= 0; i--)
            {
                if (Time.time >= rot[i].until) { rot.RemoveAt(i); continue; }
                foreach (var v in CombatQuery.Circle(rot[i].at, RotRadius, Team.Players))
                    if (v.TryGetComponent<PlayerDebuffs>(out var d)) d.ServerPoison(1.5f, 3f + 1.5f * scaledDepth);
            }
        }

        /// <summary>For tests: drop a pool of rot right here.</summary>
        public void ServerDebugRot() => AddRot(transform.position);

        [Rpc(SendTo.ClientsAndHost)]
        private void RotFxRpc(Vector3 at, float radius, float seconds)
        {
            // Low and flat (AoE stays quiet): a sickly film on the floor and a faint rim, a little smoke as it lands.
            var pool = MeshKit.Part(null, "Rot Pool", MeshKit.Cylinder(radius, radius, 0.02f, 28), Mat.Telegraph, at + Vector3.up * 0.04f);
            if (pool.TryGetComponent<Collider>(out var col)) Destroy(col);
            var rend = pool.GetComponent<Renderer>();
            ArtLibrary.Tint(rend, new Color(0.3f, 0.45f, 0.08f, 0.1f));
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rim = MeshKit.Part(pool.transform, "Rim", MeshKit.Cylinder(radius + 0.06f, radius + 0.06f, 0.01f, 28), Mat.Glow, Vector3.down * 0.012f);
            if (rim.TryGetComponent<Collider>(out var rc)) Destroy(rc);
            ArtLibrary.SetGlow(rim.GetComponent<Renderer>(), new Color(0.35f, 0.6f, 0.1f) * 0.18f);
            Destroy(pool, seconds);
            Vfx.Ring(at + Vector3.up * 0.05f, radius, new Color(0.55f, 0.9f, 0.25f), 0.6f);
            Vfx.Smoke(at, 3, radius * 0.5f, 1.6f);
            Sfx.Play(Sound.Slime, at + Vector3.up, 1f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TintedKeyedCircleRpc(Vector3 at, float radius, float seconds, Color tint) { Telegraph.Show(at, radius, seconds, this, tint); Gather(seconds, 1f); }

        [Rpc(SendTo.ClientsAndHost)]
        private void SlamFxRpc(Vector3 at, float radius, int kind)
        {
            if (kind == 1)
            {
                Sfx.Play(Sound.Shatter, at + Vector3.up, 1f, 0.7f);
                Vfx.IceSpikes(at, radius, 22);
                Vfx.Ring(at, radius, new Color(0.5f, 0.85f, 1f), 0.5f);
            }
            else if (kind == 4)
            {
                Sfx.Play(Sound.Summon, at + Vector3.up, 0.9f, 0.6f);
                Vfx.Ring(at, radius, new Color(0.55f, 0.9f, 0.25f), 0.6f);
                Vfx.Smoke(at, 6, radius * 0.6f, 2f);
            }
            else
            {
                Sfx.Play(Sound.Slam, at, 1f, 0.6f);
                Vfx.Ring(at, radius, new Color(1f, 0.6f, 0.3f), 0.45f);
                Vfx.Smoke(at, 5, radius * 0.5f, 1.4f);
                var local = NetworkPlayer.Local;
                if (local != null && Vector3.Distance(local.transform.position, at) < 16f) local.ViewModelBlockImpact();
            }
        }

        // ---- Volley: a fan of bolts or spit at its prey ----

        private void Volley(ProjectileKind kind, int count, float speed, float range)
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 2.4f + transform.forward * 0.8f;
            var at = AimDir(origin, speed);
            PlayActionRpc(RigAction.Cast, 0.6f);
            busyUntil = Time.time + 0.9f;
            for (int i = 0; i < count; i++)
            {
                var dir = Quaternion.AngleAxis((i - (count - 1) / 2f) * 9f, Vector3.up) * at;
                if (kind == ProjectileKind.Spit)
                    Arrows.ServerFire(origin, dir, damage * 0.5f, Team.Players, NetworkObjectId, speed, hit =>
                    {
                        hit.ServerDamage(damage * 0.5f, transform.position, NetworkObjectId);
                        if (hit.TryGetComponent<PlayerDebuffs>(out var d)) d.ServerPoison(4f, 3f + 1.5f * scaledDepth);
                    }, false, range * 1.6f);
                else
                    Arrows.ServerFire(origin, dir, damage * 0.6f, Team.Players, NetworkObjectId, speed, null, false, range * 1.6f);
                VolleyFxRpc(origin, dir, (byte)kind, speed, range * 1.6f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void VolleyFxRpc(Vector3 origin, Vector3 dir, byte kind, float speed, float range) =>
            Arrows.SpawnVisual(origin, dir, (ProjectileKind)kind, speed, range);

        // ---- The King's Shield: guard from the front, and the charge ----

        private int charge; // 0 none, 1 wind-up, 2 running
        private int rechargeLeft;
        private Vector3 chargeDir;
        private float chargeAt, chargeLength, charged, dazedUntil;
        private readonly HashSet<Health> trampled = new HashSet<Health>();

        private float GuardModifier(float dmg, Vector3 from)
        {
            if (Time.time < dazedUntil || IsStunned) return dmg * 1.4f;
            var to = from - transform.position;
            to.y = 0f;
            if (charge != 2 && Vector3.Angle(transform.forward, to) < 60f)
            {
                GuardClangRpc();
                return dmg * (scaledPlayers <= 1 ? 0.45f : 0.3f);
            }
            return dmg;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GuardClangRpc() => Sfx.Play(Sound.Block, transform.position + transform.forward * 0.8f + Vector3.up * 1.6f, 0.8f, 0.75f);

        private void BeginChargeWindup(float windup)
        {
            // Aimed where its quarry is going, not where it stands.
            chargeDir = (target.TryGetComponent<PlayerLife>(out var quarry) ? PredictedFeet(quarry, Tell(windup)) : target.transform.position) - transform.position;
            chargeDir.y = 0f;
            chargeDir.Normalize();
            var from = transform.position;
            chargeLength = 16f;
            if (NavMesh.Raycast(from, from + chargeDir * chargeLength, out var hit, NavMesh.AllAreas)) chargeLength = hit.distance;
            charge = 1;
            chargeAt = Time.time + Tell(windup);
            trampled.Clear();
            PlayActionRpc(RigAction.Windup, Tell(windup));
            LineTelegraphRpc(from, chargeDir, chargeLength + 1f, 2.6f, Tell(windup));
        }

        private void StepCharge()
        {
            float step = 14f * Time.deltaTime;
            var before = transform.position;
            agent.Move(chargeDir * step);
            float moved = Vector3.Distance(before, transform.position);
            charged += moved;
            foreach (var victim in CombatQuery.Circle(transform.position, 1.8f, Team.Players))
            {
                if (!trampled.Add(victim)) continue;
                victim.ServerDamage(damage * 1.4f, transform.position - chargeDir, NetworkObjectId);
                var side = Vector3.Cross(Vector3.up, chargeDir);
                if (Vector3.Dot(victim.transform.position - transform.position, side) < 0f) side = -side;
                KnockPlayer(victim, (side * 1.2f + chargeDir).normalized * 12f + Vector3.up * 4f);
            }
            if (moved < step * 0.4f)
            {
                charge = 0;
                dazedUntil = Time.time + 2.6f;
                busyUntil = dazedUntil;
                PlayActionRpc(RigAction.Stagger, 2.6f);
                ImpactFxRpc(transform.position + chargeDir, 14f);
            }
            else if (charged >= chargeLength + 0.5f)
            {
                charge = 0;
                busyUntil = Time.time + 0.8f;
                // Wounded, a charge that finds no wall turns and comes again, quicker.
                if (rechargeLeft > 0 && target != null && target.IsAlive)
                {
                    rechargeLeft--;
                    SecondMoves++;
                    SecondFxRpc("AGAIN!", new Color(1f, 0.6f, 0.3f));
                    BeginChargeWindup(0.75f);
                }
            }
        }
    }
}
