using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// What every boss shares: the boss-bar identity, party scaling, guaranteed loot,
    /// immunity to knockback, summoning, and the telegraph and impact effects. Each floor's
    /// boss subclasses this with its own mechanics.
    ///
    /// Design rule for every boss: each mechanic has one visible tell and one answer, and
    /// the answers are different skills — so a fight tests the whole kit and the whole
    /// party, not just damage output.
    ///
    /// <para>Phases, after Realm of the Mad God: a boss declares HP gates (<see cref="PhaseGates"/>).
    /// Damage cannot carry it past the next gate, so no burst skips a phase. At each gate it
    /// changes stance: a short invulnerable <b>Transition</b> with a taunt; an <b>Invulnerable</b>
    /// phase that lasts while its wards (summoned guards) live, with a timeout; a timed
    /// <b>Survival</b> phase (immune, dodge the pattern); or <b>Armored</b> (it takes a third).
    /// Every stance shows on the boss bar, on the boss (a shield bubble) and on hits (IMMUNE).</para>
    /// </summary>
    public abstract class BossBrain : EnemyBrain
    {
        public static BossBrain Active { get; private set; }

        [Header("Boss")]
        [SerializeField] protected NetworkObject minionPrefab;

        public abstract string DisplayName { get; }

        /// <summary>The line under the name on the intro card.</summary>
        public virtual string Epithet => "Guardian of the deep";

        /// <summary>What it says as it falls.</summary>
        public virtual string LastWords => "...";

        /// <summary>Which of the camp's remembered deeds this boss is.</summary>
        public virtual Progression.Deeds.Deed Deed => Progression.Deeds.Deed.Gravewarden;

        protected override void OnDied(Health h)
        {
            base.OnDied(h);
            LastWordsRpc(DisplayName, LastWords, (int)Deed);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LastWordsRpc(string who, string words, int deed)
        {
            UI.GameHud.ShowBossTaunt(who, words);
            Art.BossFall.Play(transform.position, Rig != null ? Mathf.Max(1f, Rig.transform.lossyScale.y * 0.6f) : 1f, Rig != null ? Rig.GlowColor : new Color(1f, 0.85f, 0.6f));
            UI.GameHud.ShowTitleCard(who.ToUpperInvariant() + " FALLS", Epithet, new Color(1f, 0.82f, 0.4f));
            if (Progression.Deeds.Add((Progression.Deeds.Deed)deed))
            {
                // A first kill on this machine: the guardian's eulogy goes into the journal.
                int page = UI.Journal.EulogyFor(deed);
                if (page >= 0 && UI.Journal.Add(page)) UI.GameHud.Feed("A new page in your Journal: " + UI.Journal.Entries[page].Title, new Color(0.9f, 0.8f, 0.55f));
            }
        }

        /// <summary>One sentence on how to beat it, shown on the intro card.</summary>
        public virtual string Hint => "Watch for the red ! and jump its shockwaves.";
        public Health Health => health;

        protected int scaledPlayers = 1, scaledDepth;

        // ------------------------------------------------------------------
        // Phases
        // ------------------------------------------------------------------

        public enum Stance : byte { Normal = 0, Transition = 1, Invulnerable = 2, Survival = 3, Armored = 4 }

        public readonly NetworkVariable<byte> StanceValue = new NetworkVariable<byte>();
        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> PhaseName = new NetworkVariable<Unity.Collections.FixedString64Bytes>();
        public readonly NetworkVariable<Unity.Collections.FixedString128Bytes> PhaseHint = new NetworkVariable<Unity.Collections.FixedString128Bytes>();
        /// <summary>Server time the current stance times out (0 = no timer), for the HUD countdown.</summary>
        public readonly NetworkVariable<double> StanceEndsAt = new NetworkVariable<double>();
        /// <summary>Wards still standing (the guards that keep an Invulnerable phase up).</summary>
        public readonly NetworkVariable<byte> WardsLeft = new NetworkVariable<byte>();

        public Stance CurrentStance => (Stance)StanceValue.Value;
        public bool Immune => CurrentStance == Stance.Transition || CurrentStance == Stance.Invulnerable || CurrentStance == Stance.Survival;
        protected bool InSurvival => CurrentStance == Stance.Survival;

        /// <summary>Damage taken while Armored.</summary>
        public const float ArmoredTaken = 0.35f;
        /// <summary>A hit this big is a scripted kill (tests, the abyss): it ignores phases.</summary>
        public const float KillDamage = 1e5f;

        /// <summary>HP fractions, highest first, at which <see cref="OnPhaseGate"/> fires.</summary>
        protected virtual float[] PhaseGates => System.Array.Empty<float>();
        public int GateCount => PhaseGates.Length;
        /// <summary>Any peer: the phase gates (for the boss bar's notches).</summary>
        public float[] Gates => PhaseGates;
        protected virtual void OnPhaseGate(int index) { }

        private int gatesPassed;
        private int pendingGate = -1;
        private float stanceEnd = -1f;
        private System.Func<bool> stanceDone;
        private System.Action stanceThen;
        private List<EnemyBrain> wards = new List<EnemyBrain>();
        private float nextImmuneFx;

        private float PhaseModifier(float dmg, Vector3 from)
        {
            if (dmg >= KillDamage) return dmg;
            if (Immune)
            {
                if (Time.time >= nextImmuneFx)
                {
                    nextImmuneFx = Time.time + 0.3f;
                    var dir = from - transform.position;
                    dir.y = 0f;
                    ImmuneFxRpc(transform.position + Vector3.up * (health.CenterHeight * 1.6f) + dir.normalized * health.BodyRadius);
                }
                return 0f;
            }
            if (CurrentStance == Stance.Armored) dmg *= ArmoredTaken;
            var gates = PhaseGates;
            // A gate reached but not yet begun holds: the boss cannot drop below it in between.
            if (pendingGate >= 0) return Mathf.Max(0f, Mathf.Min(dmg, health.Current.Value - gates[pendingGate] * health.Max.Value));
            if (gatesPassed < gates.Length && pendingGate < 0)
            {
                float floorHp = gates[gatesPassed] * health.Max.Value;
                if (health.Current.Value - dmg <= floorHp)
                {
                    dmg = Mathf.Max(0f, health.Current.Value - floorHp);
                    pendingGate = gatesPassed++;
                }
            }
            return dmg;
        }

        private bool phaseModifierAdded;

        private void TickPhases()
        {
            // Added on the first tick, so it runs after every modifier the subclass registered (shields, wards).
            if (!phaseModifierAdded) { phaseModifierAdded = true; health.IncomingModifiers.Add(PhaseModifier); }
            // Gates crossed without damage (set by tests or scripts) still fire, one at a time.
            var gates = PhaseGates;
            if (pendingGate < 0 && gatesPassed < gates.Length && health.Fraction < gates[gatesPassed] - 0.001f) pendingGate = gatesPassed++;
            if (pendingGate >= 0 && !Immune && !Dormant)
            {
                int g = pendingGate;
                pendingGate = -1;
                Debug.Log("[Boss] " + DisplayName + " phase gate " + g + " at " + Mathf.RoundToInt(health.Fraction * 100f) + "%");
                OnPhaseGate(g);
            }

            if (stanceDone == null && stanceEnd < 0f) return;
            if (stanceDone != null)
            {
                wards.RemoveAll(w => w == null || w.IsDead);
                if (WardsLeft.Value != wards.Count) WardsLeft.Value = (byte)wards.Count;
            }
            bool done = (stanceDone != null && stanceDone()) || (stanceEnd > 0f && Time.time >= stanceEnd);
            if (done) EndStance();
        }

        private void SetStance(Stance stance, string phase, string hint, float seconds)
        {
            StanceValue.Value = (byte)stance;
            if (phase != null) PhaseName.Value = phase;
            PhaseHint.Value = hint ?? "";
            stanceEnd = seconds > 0f ? Time.time + seconds : -1f;
            StanceEndsAt.Value = seconds > 0f ? NetworkManager.ServerTime.Time + seconds : 0.0;
            Debug.Log("[Boss] " + DisplayName + " stance " + stance + " (" + PhaseName.Value + ")");
        }

        private void EndStance()
        {
            var then = stanceThen;
            stanceThen = null;
            stanceDone = null;
            stanceEnd = -1f;
            wards.Clear();
            WardsLeft.Value = 0;
            bool wasArmored = CurrentStance == Stance.Armored;
            SetStance(wasArmored ? Stance.Armored : Stance.Normal, null, null, 0f);
            then?.Invoke();
        }

        /// <summary>Server: a brief, immune pause into a new phase: it stops, speaks, and gathers itself.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void RoarRpc() => Sfx.Play(Sound.Roar, transform.position + Vector3.up * 2f, 1f, Rig != null ? Mathf.Clamp(1.1f - 0.12f * Rig.transform.lossyScale.y, 0.6f, 1f) : 0.8f);

        protected void Transition(string phase, string taunt, float seconds = 2.2f, System.Action then = null)
        {
            if (IsServer) RoarRpc();
            SetStance(Stance.Transition, phase, null, seconds);
            stanceThen = then;
            castUntil = Time.time + seconds;
            busyUntil = castUntil;
            CancelTelegraphRpc();
            PlayActionRpc(RigAction.Cast, seconds);
            Taunt(taunt);
        }

        /// <summary>Server: immune while <paramref name="guards"/> live (or until the timeout). It keeps fighting.</summary>
        protected void InvulnerableWhile(string phase, string taunt, string hint, List<EnemyBrain> guards, float maxSeconds, System.Action then = null)
        {
            wards = guards ?? new List<EnemyBrain>();
            stanceDone = () => wards.Count == 0;
            stanceThen = then;
            SetStance(Stance.Invulnerable, phase, hint, maxSeconds);
            WardsLeft.Value = (byte)wards.Count;
            Taunt(taunt);
        }

        /// <summary>Server: immune for <paramref name="seconds"/> while the subclass runs a pattern to survive.</summary>
        protected void Survive(string phase, string taunt, string hint, float seconds, System.Action then = null)
        {
            stanceThen = then;
            SetStance(Stance.Survival, phase, hint, seconds);
            castUntil = Time.time + seconds;
            busyUntil = castUntil;
            Taunt(taunt);
        }

        /// <summary>Server: armour on (a third of damage gets through) or off.</summary>
        protected void SetArmored(bool on, string phase = null, string hint = null)
        {
            SetStance(on ? Stance.Armored : Stance.Normal, phase, hint, 0f);
        }

        protected void Taunt(string line)
        {
            if (!string.IsNullOrEmpty(line)) TauntRpc(DisplayName, line);
        }

        /// <summary>Tests: back to the first phase, normal stance.</summary>
        public void ServerDebugResetPhases()
        {
            gatesPassed = 0;
            pendingGate = -1;
            stanceThen = null;
            stanceDone = null;
            stanceEnd = -1f;
            wards.Clear();
            WardsLeft.Value = 0;
            castUntil = 0f;
            SetStance(Stance.Normal, "", null, 0f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DormantFxRpc(Vector3 at)
        {
            UI.GameHud.ShowCombo(at, "ASLEEP: ENTER THE ARENA", new Color(0.6f, 0.75f, 1f));
            Sfx.Play(Sound.Block, at, 0.4f, 1.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ImmuneFxRpc(Vector3 at)
        {
            UI.GameHud.ShowCombo(at, "IMMUNE", new Color(0.6f, 0.75f, 1f));
            Sfx.Play(Sound.Block, at, 0.5f, 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TauntRpc(string who, string line)
        {
            UI.GameHud.ShowBossTaunt(who, line);
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 2f, 1f, 0.55f);
        }

        // ---- The shield bubble: shown on every peer while it is immune. ----
        private Transform bubble;
        private Renderer bubbleRenderer;

        private void TickBubble()
        {
            bool show = Immune && !IsDead;
            if (show && bubble == null)
            {
                float r = Mathf.Max(health.BodyRadius * 1.9f, health.CenterHeight * 1.25f);
                var go = MeshKit.Part(transform, "PhaseBubble", MeshKit.Ball(1f, 16, 12), Mat.Telegraph, Vector3.up * health.CenterHeight, default, Vector3.one * r * 2f);
                bubble = go.transform;
                bubbleRenderer = go.GetComponent<Renderer>();
                bubbleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (bubble == null) return;
            bubble.gameObject.SetActive(show);
            if (!show) return;
            var c = CurrentStance == Stance.Survival ? new Color(1f, 0.55f, 0.3f) : new Color(0.55f, 0.75f, 1f);
            c.a = 0.11f + 0.04f * Mathf.Sin(Time.time * 4f);
            ArtLibrary.Tint(bubbleRenderer, c);
        }
        protected float castUntil;

        public void SetMinionPrefab(NetworkObject prefab) => minionPrefab = prefab;

        protected override LootTable Loot => LootTable.SkeletonKing;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Active = this;
            if (IsServer)
            {
                // Asleep on its throne it cannot be hurt (no plinking it from the doorway, no
                // sealing the gates on half the party); the director wakes it when everyone is in.
                health.IncomingModifiers.Insert(0, (dmg, from) =>
                {
                    if (!Dormant || dmg >= KillDamage) return dmg;
                    if (Time.time >= nextImmuneFx)
                    {
                        nextImmuneFx = Time.time + 0.6f;
                        DormantFxRpc(transform.position + Vector3.up * (health.CenterHeight * 1.6f));
                    }
                    return 0f;
                });
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Active == this) Active = null;
            base.OnNetworkDespawn();
        }

        public void ServerScaleBoss(int players, int depth)
        {
            scaledPlayers = players;
            scaledDepth = depth;
            ServerScale(players, depth);
        }

        protected bool Casting => Time.time < castUntil;

        // ---- Fury: a fight that drags past a few minutes makes the boss hit much harder. ----
        public const float FurySeconds = 210f;
        private float fightStartedAt = -1f;
        private bool furious;

        private void TickFury()
        {
            if (Dormant || furious) return;
            if (fightStartedAt < 0f) { fightStartedAt = Time.time; return; }
            if (Time.time - fightStartedAt < FurySeconds) return;
            furious = true;
            damage *= 1.4f;
            moveSpeed *= 1.15f;
            FuryRpc(DisplayName);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FuryRpc(string who)
        {
            UI.GameHud.ShowTitleCard(who.ToUpperInvariant() + " GROWS FURIOUS", "Finish this, quickly");
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * 2f, 1f, 0.6f);
            if (Rig != null) Rig.GlowColor = new Color(1f, 0.15f, 0.05f);
        }

        // ---- Shockwave: an expanding ring that hits grounded players as it passes. ----
        protected virtual float ShockwaveCooldown => 0f;   // 0 = this boss has none
        protected virtual Color ShockwaveColor => new Color(1f, 0.6f, 0.3f);
        protected virtual float ShockwaveDamage => damage * 0.9f;
        private float nextShockwave = -1f, shockwaveAt = -1f;

        private class Wave { public Vector3 Origin; public float Start, Speed, Max, Damage; public readonly HashSet<ulong> Hit = new HashSet<ulong>(); }
        private readonly List<Wave> waves = new List<Wave>();

        /// <summary>Server: start a shockwave wind-up if one is due. Call from ChooseSpecialAttack.</summary>
        protected bool TryShockwave()
        {
            if (ShockwaveCooldown <= 0f) return false;
            // The first comes a little after the fight starts (or after a phase unlocks it).
            if (nextShockwave < 0f) nextShockwave = Time.time + Mathf.Min(6f, ShockwaveCooldown * 0.6f);
            if (Time.time < nextShockwave) return false;
            nextShockwave = Time.time + ShockwaveCooldown;
            shockwaveAt = Time.time + 1.1f;
            castUntil = shockwaveAt + 0.3f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Slam, 1.3f);
            CircleTelegraphRpc(transform.position, 2.5f, 1.1f, false);
            return true;
        }

        /// <summary>Server: launch a wave now (bosses may also call this from their own attacks).</summary>
        protected void LaunchShockwave(Vector3 origin, float speed, float max, float dmg)
        {
            waves.Add(new Wave { Origin = origin, Start = Time.time, Speed = speed, Max = max, Damage = dmg });
            Debug.Log("[Boss] " + DisplayName + " shockwave");
            ShockwaveFxRpc(origin, speed, max, ShockwaveColor);
        }

        private void TickShockwaves()
        {
            if (shockwaveAt > 0f && Time.time >= shockwaveAt)
            {
                shockwaveAt = -1f;
                LaunchShockwave(transform.position, 9f, 16f, ShockwaveDamage);
            }
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                float r = (Time.time - w.Start) * w.Speed;
                if (r > w.Max + 1f) { waves.RemoveAt(i); continue; }
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable || w.Hit.Contains(p.NetworkObjectId)) continue;
                    var d = p.transform.position - w.Origin;
                    // Airborne players clear the wave: jump it.
                    if (d.y > 0.45f) continue;
                    d.y = 0f;
                    if (Mathf.Abs(d.magnitude - r) > 0.8f) continue;
                    w.Hit.Add(p.NetworkObjectId);
                    Debug.Log("[Boss] shockwave hit " + p.name);
                    p.Health.ServerDamage(w.Damage, w.Origin + Vector3.up, NetworkObjectId);
                    KnockPlayer(p.Health, d.normalized * 7f + Vector3.up * 3f);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShockwaveFxRpc(Vector3 origin, float speed, float max, Color color)
        {
            ShockwaveRing.Spawn(origin, speed, max, color);
            Sfx.Play(Sound.Slam, origin + Vector3.up, 1f, 0.6f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, origin) < 18f) local.AddShake(0.35f);
        }

        /// <summary>
        /// Server: asleep on the throne until the arena seals (or it is hit). The director
        /// wakes it; damage wakes it too, so a party cannot plink it from the doorway.
        /// </summary>
        public bool Dormant { get; set; }

        protected override bool IsBoss => true;
        protected override bool Heavy => true;

        protected override void Update()
        {
            if (IsServer && !IsDead) { TickShockwaves(); TickFury(); TickPatterns(); if (!IsStunned && !InEntrance) TickQuickShots(); }
            if (IsServer && !IsDead && Dormant)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                return;
            }
            if (IsServer && !IsDead && Casting)
            {
                agent.isStopped = true;
                return;
            }
            base.Update();
        }

        // Phases tick here, not in Update: subclasses return early from Update while they wind up.
        protected virtual void LateUpdate()
        {
            if (IsServer && !IsDead && IsSpawned)
            {
                TickPhases();
                // Hunts and their strikes tick here too: subclasses return early from Update mid-attack.
                TickStrikes();
                if (!IsStunned && !InEntrance) TickHunt();
            }
            TickBubble();
        }

        // ------------------------------------------------------------------
        // Bullet patterns: arms of bolts wheeling out from an emitter that MOVES (orbits the boss,
        // walks with it, circles the arena), spinning one way then the other, with aimed shots
        // mixed in, so standing still is never the answer.
        // ------------------------------------------------------------------

        protected class Pattern
        {
            public int Arms = 4;
            public float Interval = 0.12f, Speed = 12f, Spin = 12f, Damage = 10f, Range = 34f;
            /// <summary>Seconds between spin reversals (0 = never).</summary>
            public float FlipEvery;
            /// <summary>Degrees of extra sway per volley (a flower instead of a pinwheel).</summary>
            public float Wobble;
            /// <summary>Seconds between leading shots at every player (0 = none).</summary>
            public float AimedEvery;
            public ProjectileKind Kind = ProjectileKind.ShadowBolt;
            /// <summary>Where the emitter is, given seconds since the pattern began.</summary>
            public System.Func<float, Vector3> Origin;
            public System.Action<Health> OnHit;
            public float Duration = 6f;
            internal float Started, NextAt, NextAimed, Angle;
            internal byte Id;
        }

        /// <summary>Boss bullet patterns hit harder (2026-10-21, the user): every pattern's bolts and aimed shots.</summary>
        public const float PatternDamageScale = 1.5f;
        private byte nextPatternId;

        private readonly List<Pattern> patterns = new List<Pattern>();

        protected Pattern StartPattern(Pattern p)
        {
            p.Started = Time.time;
            p.NextAt = Time.time;
            p.NextAimed = Time.time + (p.AimedEvery > 0f ? p.AimedEvery * 0.6f : float.MaxValue);
            p.Angle = Random.Range(0f, 360f);
            p.Origin ??= _ => transform.position + Vector3.up * 1.3f;
            p.Damage *= PatternDamageScale;
            p.Id = nextPatternId++;
            patterns.Add(p);
            return p;
        }

        protected void StopPatterns() => patterns.Clear();
        protected bool PatternRunning => patterns.Count > 0;

        /// <summary>An emitter circling the boss at <paramref name="radius"/>, <paramref name="degPerSec"/> round.</summary>
        protected System.Func<float, Vector3> Orbit(float radius, float degPerSec, float phase = 0f, float height = 1.3f) =>
            t => transform.position + Quaternion.Euler(0f, phase + t * degPerSec, 0f) * Vector3.forward * radius + Vector3.up * height;

        private void TickPatterns()
        {
            for (int i = patterns.Count - 1; i >= 0; i--)
            {
                var p = patterns[i];
                float t = Time.time - p.Started;
                if (t >= p.Duration) { patterns.RemoveAt(i); continue; }
                int guard = 0;
                while (Time.time >= p.NextAt && guard++ < 3)
                {
                    p.NextAt += p.Interval;
                    bool flipped = p.FlipEvery > 0f && ((int)(t / p.FlipEvery)) % 2 == 1;
                    p.Angle += (flipped ? -p.Spin : p.Spin) + (p.Wobble > 0f ? Mathf.Sin(t * 2.4f) * p.Wobble : 0f);
                    var origin = p.Origin(t);
                    for (int a = 0; a < p.Arms; a++)
                    {
                        var dir = Quaternion.Euler(0f, p.Angle + a * 360f / p.Arms, 0f) * Vector3.forward;
                        Arrows.ServerFire(origin, dir, p.OnHit != null ? 0f : p.Damage, Team.Players, NetworkObjectId, p.Speed, p.OnHit, false, p.Range);
                    }
                    PatternVolleyRpc(origin, p.Angle, (byte)p.Arms, p.Speed, (byte)p.Kind, p.Range, p.Id);
                }
                if (Time.time >= p.NextAimed)
                {
                    p.NextAimed = Time.time + p.AimedEvery;
                    var origin = p.Origin(t);
                    foreach (var pl in PlayerLife.All)
                    {
                        if (pl == null || !pl.Targetable) continue;
                        var dir = Aim.Lead(pl.Health, origin, p.Speed * 1.4f, Mathf.Max(0.6f, LeadSkill)) - origin;
                        dir.y = 0f;
                        Arrows.ServerFire(origin, dir, p.Damage * 1.2f, Team.Players, NetworkObjectId, p.Speed * 1.4f, null, false, p.Range);
                        QuickVisualRpc(origin, dir, (byte)p.Kind, p.Speed * 1.4f, p.Range);
                    }
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PatternVolleyRpc(Vector3 origin, float angle, byte arms, float speed, byte kind, float range, byte id)
        {
            PatternEmitter.Volley(this, id, origin, angle, arms, Arrows.BoltColor((ProjectileKind)kind));
            for (int a = 0; a < arms; a++)
                Arrows.SpawnVisual(origin, Quaternion.Euler(0f, angle + a * 360f / arms, 0f) * Vector3.forward, (ProjectileKind)kind, speed, range);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void QuickVisualRpc(Vector3 origin, Vector3 dir, byte kind, float speed, float range)
        {
            Arrows.SpawnVisual(origin, dir, (ProjectileKind)kind, speed, range);
            Sfx.Play(Sound.Whisper, origin, 0.5f, 1.5f);
        }

        // ---- Quick shots: run from a boss on floor 3 or deeper and it answers with a fast, leading burst ----

        /// <summary>What it throws at a fleeing party (none: it never does).</summary>
        protected virtual ProjectileKind? QuickKind => ProjectileKind.ShadowBolt;
        public const float QuickSpeed = 26f;
        private float nextQuick, farSince = -1f;

        private void TickQuickShots()
        {
            if (QuickKind == null || scaledDepth < 2 || Dormant || Immune || target == null || Casting || attacking) { farSince = -1f; return; }
            if (FlatDistance(target.transform.position) < attackRange + 5f) { farSince = -1f; return; }
            if (farSince < 0f) farSince = Time.time;
            if (Time.time - farSince < 1.4f || Time.time < nextQuick) return;
            nextQuick = Time.time + Mathf.Lerp(5.5f, 3f, Difficulty.Lead(scaledDepth));
            StartCoroutine(QuickBurst(scaledDepth >= 6 ? 3 : 2));
        }

        private System.Collections.IEnumerator QuickBurst(int shots)
        {
            // The tell: a flare at its hand and a hiss, a beat before the first shot.
            var hand = transform.position + Vector3.up * 2.3f + transform.forward * 0.9f;
            QuickTellRpc(hand, (byte)QuickKind.Value);
            yield return new WaitForSeconds(0.4f);
            for (int i = 0; i < shots && target != null && !IsDead; i++)
            {
                FaceTarget(1f);
                var origin = transform.position + Vector3.up * 2.3f + transform.forward * 0.9f;
                var dir = AimDir(origin, QuickSpeed);
                Arrows.ServerFire(origin, dir, damage * 0.45f, Team.Players, NetworkObjectId, QuickSpeed, null, false, 36f);
                QuickVisualRpc(origin, dir, (byte)QuickKind.Value, QuickSpeed, 36f);
                yield return new WaitForSeconds(0.2f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void QuickTellRpc(Vector3 at, byte kind)
        {
            Vfx.Flash(at, Arrows.BoltColor((ProjectileKind)kind), 3f, 4f, 0.35f);
            Vfx.Embers(at, 6, Arrows.BoltColor((ProjectileKind)kind), 2f, 0.05f, 0.4f);
            Sfx.Play(Sound.Bow, at, 0.8f, 1.4f);
        }

        // ---- Cone warnings on its big swings ----

        protected override void BeginAttack()
        {
            base.BeginAttack();
            if (attacking && Rig != null)
                ConeTelegraphRpc(transform.position, transform.forward, Mathf.Clamp(attackArc, 40f, 300f), attackRange + health.BodyRadius + 0.6f, windup);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ConeTelegraphRpc(Vector3 apex, Vector3 dir, float angle, float radius, float seconds)
        {
            if (Rig != null) Telegraph.ShowCone(apex, dir, angle, radius, seconds, Rig);
            Gather(seconds, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CancelConeRpc() { if (Rig != null) Telegraph.Cancel(Rig); }

        public override void ServerStun(float seconds)
        {
            if (attacking) CancelConeRpc();
            base.ServerStun(seconds);
        }

        /// <summary>Where a player will be in <paramref name="seconds"/>, as far as this boss is clever enough to guess.</summary>
        protected Vector3 PredictedFeet(PlayerLife p, float seconds)
        {
            var v = p.ServerVelocity;
            v.y = 0f;
            var at = p.transform.position + v * seconds * LeadSkill;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            return at;
        }

        /// <summary>A cast-time summon of the minion prefab in a ring around the boss.</summary>
        protected NetworkObject guardPrefab;

        /// <summary>A second kind of minion some bosses call (the Hollow King's royal guard).</summary>
        public void SetGuardPrefab(NetworkObject prefab) => guardPrefab = prefab;

        protected List<EnemyBrain> Summon(int count, float radius, float castTime = 1.6f) => SummonOf(minionPrefab, count, radius, castTime);

        protected List<EnemyBrain> SummonOf(NetworkObject prefab, int count, float radius, float castTime = 1.6f)
        {
            var raised = new List<EnemyBrain>();
            var minionPrefab = prefab;
            castUntil = Time.time + castTime;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, castTime);
            SummonFxRpc();

            if (minionPrefab == null) return raised;
            count = Difficulty.SummonCount(count, StandingParty);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var pos = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                var minion = Instantiate(minionPrefab, pos, Quaternion.LookRotation(transform.position - pos));
                minion.Spawn(destroyWithScene: true);
                var brain = minion.GetComponent<EnemyBrain>();
                brain.NoLoot = true;
                brain.ServerScale(scaledPlayers, scaledDepth);
                brain.ServerAggroNearest();
                raised.Add(brain);
            }
            return raised;
        }

        /// <summary>Server: shove a player. Movement is owner-authoritative, so this asks their client to do it.</summary>
        protected static void KnockPlayer(Health victim, Vector3 velocity)
        {
            if (victim.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(velocity);
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <param name="keyed">Keyed telegraphs can be cancelled (a slam interrupted by a bash).</param>
        [Rpc(SendTo.ClientsAndHost)]
        protected void CircleTelegraphRpc(Vector3 center, float radius, float duration, bool keyed)
        {
            Telegraph.Show(center, radius, duration, keyed ? this : null);
            Gather(duration, 1f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void TintedCircleRpc(Vector3 center, float radius, float duration, Color tint)
        {
            Telegraph.Show(center, radius, duration, null, tint);
            Gather(duration, 0.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void LineTelegraphRpc(Vector3 start, Vector3 direction, float length, float width, float duration)
        {
            Telegraph.ShowLine(start, direction, length, width, duration, this);
            Gather(duration, 1f);
        }

        /// <summary>Any peer: the boss crouches, draws embers in and swells a core of light for <paramref name="seconds"/>.</summary>
        protected void Gather(float seconds, float k)
        {
            if (Rig == null) return;
            BossWindup.For(gameObject, Rig, health != null ? health.CenterHeight * 2f : 2f).Anticipate(seconds, k);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void GatherRpc(float seconds, float k) => Gather(seconds, k);

        // ------------------------------------------------------------------
        // Entrances (2026-10-20): when the arena seals, the guardian rises to meet you. A gather, a
        // roar and a ring of force (harmless), each boss in its own way, and it holds still for
        // EntranceSeconds while its name is on screen, so the first blow never lands under the title.
        // ------------------------------------------------------------------

        public const float EntranceSeconds = 2.4f;

        /// <summary>0 roar, 1 shield slam, 2 tide, 3 crown, 4 bell, 5 the abyss.</summary>
        protected virtual byte EntranceStyle => 0;

        /// <summary>Server: the arena has sealed.</summary>
        public void ServerEntrance()
        {
            heldUntil = Time.time + EntranceSeconds;
            // Face the party as it stands up.
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var p in Player.PlayerLife.All) if (p != null && !p.Downed.Value) { sum += p.transform.position; n++; }
            if (n > 0)
            {
                var to = sum / n - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
            }
            EntranceRpc(EntranceStyle);
        }

        /// <summary>Server, for tests: entrances played.</summary>
        public static int EntrancesPlayed { get; private set; }

        [Rpc(SendTo.ClientsAndHost)]
        private void EntranceRpc(byte style)
        {
            EntrancesPlayed++;
            StartCoroutine(Entrance(style));
        }

        private System.Collections.IEnumerator Entrance(byte style)
        {
            var feet = transform.position + Vector3.up * 0.05f;
            float height = health != null ? health.CenterHeight * 2f : 3f;
            float reach = Mathf.Max(3f, (health != null ? health.BodyRadius : 1f) * 3f);
            Color tint = style == 1 ? new Color(0.85f, 0.75f, 0.6f) : style == 2 ? new Color(0.4f, 0.75f, 1f) : style == 3 ? new Color(1f, 0.82f, 0.35f)
                : style == 4 ? new Color(1f, 0.55f, 0.25f) : style == 5 ? new Color(0.6f, 0.4f, 1f) : new Color(0.9f, 0.35f, 0.3f);

            // Gather: crouch, the core lights, embers drawn in.
            Gather(1.05f, 1f);
            Rig?.Play(RigAction.Cast, 1.05f);
            if (style == 2) Vfx.Ring(feet, reach * 1.4f, tint, 1f);
            if (style == 5) Vfx.Flash(transform.position + Vector3.up * height * 0.6f, tint, 4f, 14f, 1f);
            Sfx.Play(style == 4 ? Sound.Toll : Sound.Summon, transform.position + Vector3.up * height * 0.5f, 0.8f, style == 5 ? 0.5f : 0.7f);
            yield return new WaitForSeconds(1.05f);
            if (this == null || IsDead) yield break;

            // Release: the signature blow.
            switch (style)
            {
                case 1: Rig?.Play(RigAction.Bash, 0.7f); break;
                case 3: case 4: Rig?.Play(RigAction.Slam, 0.9f); break;
                default: Rig?.Play(RigAction.Cast, 0.9f); break;
            }
            Sfx.Play(Sound.WarCry, transform.position + Vector3.up * height * 0.7f, 1f, style == 5 ? 0.45f : 0.6f);
            if (style == 1 || style == 3 || style == 4) Sfx.Play(Sound.Slam, feet, 1f, 0.6f);
            for (int r = 0; r < 3; r++) Vfx.Ring(feet, reach * (1.2f + r * 0.9f), tint, 0.5f + r * 0.18f);
            Vfx.Smoke(feet, 10, reach * 0.8f, 1.6f);
            Vfx.Embers(transform.position + Vector3.up * height * 0.6f, 24, tint, 7f, 0.09f, 1.1f);
            if (style == 3 || style == 4) Vfx.Flash(transform.position + Vector3.up * height, tint, 3f, 16f, 0.5f);
            var cam = ViewCamera.Current;
            if (cam != null && Vector3.Distance(cam.transform.position, transform.position) < 30f)
                Player.NetworkPlayer.Local?.AddShake(0.35f);
        }

        // ------------------------------------------------------------------
        // Party-aware pressure (2026-10-20): every boss and Warden hunts EVERY standing player
        // with predicted strikes from floor 2, tells shorten with depth, and a soloist gets a
        // faster rhythm (one target, so it comes to you more often) while a party gets more
        // targets and more summons to split onto.
        // ------------------------------------------------------------------

        /// <summary>Server: how many players it can hunt right now.</summary>
        protected int StandingParty
        {
            get { int n = 0; foreach (var p in PlayerLife.All) if (p != null && p.Targetable) n++; return Mathf.Max(1, n); }
        }

        /// <summary>A tell's length here: shorter on deeper floors and when furious.</summary>
        protected float Tell(float seconds) => seconds * Difficulty.Tell(scaledDepth) * (furious ? 0.85f : 1f);

        /// <summary>Hunt cadence factor: alone, it comes more often.</summary>
        protected float Cadence => StandingParty <= 1 ? 0.8f : 1f;

        /// <summary>Bosses that should not hunt (none yet) override this.</summary>
        protected virtual bool Hunts => true;

        public const float StrikeRadius = 2.2f, LanceLength = 24f, LanceWidth = 1.7f;

        private struct Strike { public Vector3 At, Dir; public float When, Radius, Damage; public bool Line; }
        private readonly List<Strike> strikes = new List<Strike>();
        private float nextHunt = -1f;
        private int huntKind;

        /// <summary>Tests: how many hunts it has thrown.</summary>
        public int HuntsThrown { get; private set; }
        /// <summary>Tests: where the last hunting strike was aimed.</summary>
        public Vector3 LastStrikeAt { get; private set; }
        /// <summary>Tests: strikes that caught a player, and what the last one dealt.</summary>
        public int StrikeHits { get; private set; }
        public float LastStrikeDealt { get; private set; }

        private void TickHunt()
        {
            if (!Hunts || scaledDepth < 1 || Dormant || Immune || CurrentStance == Stance.Transition) return;
            if (nextHunt < 0f) { nextHunt = Time.time + 7f; return; }
            if (Time.time < nextHunt) return;
            nextHunt = Time.time + Mathf.Lerp(15f, 8.5f, Difficulty.Lead(scaledDepth)) * Cadence;
            ServerHuntNow();
        }

        /// <summary>Server: throw the next hunt now (tests call this too).</summary>
        public void ServerHuntNow()
        {
            HuntsThrown++;
            if (huntKind++ % 2 == 0) StartCoroutine(HuntingStrikes());
            else ServerLanceFan();
        }

        /// <summary>A chain of predicted circles that walks after each player: keep changing direction.</summary>
        private System.Collections.IEnumerator HuntingStrikes()
        {
            int count = scaledDepth >= 6 ? 4 : 3;
            float tell = Tell(0.85f);
            GatherRpc(tell, 0.7f);
            PlayActionRpc(RigAction.Cast, 0.6f);
            for (int k = 0; k < count && !IsDead; k++)
            {
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var at = PredictedFeet(p, tell + 0.1f);
                    LastStrikeAt = at;
                    strikes.Add(new Strike { At = at, When = Time.time + tell, Radius = StrikeRadius, Damage = damage * 0.55f });
                    TintedCircleRpc(at, StrikeRadius, tell, Telegraph.Harm);
                }
                yield return new WaitForSeconds(0.42f);
            }
        }

        /// <summary>A fan of lances at each player, aimed where they are going.</summary>
        public void ServerLanceFan()
        {
            float tell = Tell(1.0f);
            int lines = scaledDepth >= 4 ? 5 : 3;
            float spread = scaledDepth >= 4 ? 15f : 22f;
            PlayActionRpc(RigAction.Slam, tell + 0.2f);
            var from = transform.position + Vector3.up * 0.05f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                var to = PredictedFeet(p, tell) - from;
                to.y = 0f;
                if (to.sqrMagnitude < 0.5f) to = transform.forward;
                to.Normalize();
                for (int i = 0; i < lines; i++)
                {
                    var dir = Quaternion.Euler(0f, (i - (lines - 1) / 2f) * spread, 0f) * to;
                    strikes.Add(new Strike { At = from, Dir = dir, When = Time.time + tell, Damage = damage * 0.65f, Line = true });
                    LineTelegraphRpc(from, dir, LanceLength, LanceWidth, tell);
                }
            }
        }

        private void TickStrikes()
        {
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                var s = strikes[i];
                if (Time.time < s.When) continue;
                strikes.RemoveAt(i);
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var to = p.transform.position - s.At;
                    to.y = 0f;
                    bool hit;
                    if (s.Line)
                    {
                        float along = Vector3.Dot(to, s.Dir);
                        hit = along >= 0f && along <= LanceLength && (to - s.Dir * along).magnitude <= LanceWidth * 0.5f + 0.3f;
                    }
                    else hit = to.magnitude <= s.Radius + 0.3f;
                    if (!hit) continue;
                    StrikeHits++;
                    LastStrikeDealt = p.Health.ServerDamage(s.Damage, s.At + Vector3.up, NetworkObjectId);
                    KnockPlayer(p.Health, (s.Line ? s.Dir * 3f : to.normalized * 4f) + Vector3.up * 4f);
                }
                StrikeFxRpc(s.At, s.Line ? s.Dir : Vector3.zero, s.Line);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeFxRpc(Vector3 at, Vector3 dir, bool line)
        {
            var c = Rig != null ? Rig.GlowColor : new Color(1f, 0.4f, 0.2f);
            if (line)
            {
                for (float d = 1.5f; d < LanceLength; d += 2.5f)
                {
                    var p = at + dir * d;
                    Vfx.Embers(p + Vector3.up * 0.2f, 3, c, 3f, 0.05f, 0.4f);
                }
                Vfx.Flash(at + dir * 4f + Vector3.up, c, 3f, 8f, 0.25f);
                Sfx.Play(Sound.Slam, at + dir * 6f, 0.7f, 1.2f);
            }
            else
            {
                Vfx.Ring(at + Vector3.up * 0.05f, StrikeRadius, c, 0.3f);
                Vfx.Embers(at + Vector3.up * 0.3f, 8, c, 4f, 0.06f, 0.5f);
                Sfx.Play(Sound.Slam, at, 0.55f, 1.3f);
            }
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 7f) local.AddShake(0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void CancelTelegraphRpc() => Telegraph.Cancel(this);

        [Rpc(SendTo.ClientsAndHost)]
        protected void ImpactFxRpc(Vector3 at, float shakeRange)
        {
            Sfx.Play(Sound.Slam, at, 1f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < shakeRange) local.ViewModelBlockImpact();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SummonFxRpc() => Sfx.Play(Sound.Summon, transform.position + Vector3.up * 2f, 1f);
    }
}
