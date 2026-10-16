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
        protected void Transition(string phase, string taunt, float seconds = 2.2f, System.Action then = null)
        {
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
            if (IsServer && !IsDead) { TickShockwaves(); TickFury(); }
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
            if (IsServer && !IsDead && IsSpawned) TickPhases();
            TickBubble();
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
        }

        [Rpc(SendTo.ClientsAndHost)]
        protected void LineTelegraphRpc(Vector3 start, Vector3 direction, float length, float width, float duration)
        {
            Telegraph.ShowLine(start, direction, length, width, duration, this);
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
