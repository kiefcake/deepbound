using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public abstract partial class BossBrain : EnemyBrain
    {
        public static BossBrain Active { get; private set; }

        [Header("Boss")]
        [SerializeField] protected NetworkObject minionPrefab;

        public abstract string DisplayName { get; }

        public virtual string Epithet => "Guardian of the deep";

        public virtual string LastWords => "...";

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
                int page = UI.Journal.EulogyFor(deed);
                if (page >= 0 && UI.Journal.Add(page)) UI.GameHud.Feed("A new page in your Journal: " + UI.Journal.Entries[page].Title, new Color(0.9f, 0.8f, 0.55f));
            }
        }

        public virtual string Hint => "Watch for the red ! and jump its shockwaves.";
        public Health Health => health;

        protected int scaledPlayers = 1, scaledDepth;

        public enum Stance : byte { Normal = 0, Transition = 1, Invulnerable = 2, Survival = 3, Armored = 4 }

        public readonly NetworkVariable<byte> StanceValue = new NetworkVariable<byte>();
        public readonly NetworkVariable<Unity.Collections.FixedString64Bytes> PhaseName = new NetworkVariable<Unity.Collections.FixedString64Bytes>();
        public readonly NetworkVariable<Unity.Collections.FixedString128Bytes> PhaseHint = new NetworkVariable<Unity.Collections.FixedString128Bytes>();

        public readonly NetworkVariable<double> StanceEndsAt = new NetworkVariable<double>();

        public readonly NetworkVariable<byte> WardsLeft = new NetworkVariable<byte>();

        public Stance CurrentStance => (Stance)StanceValue.Value;
        public bool Immune => CurrentStance == Stance.Transition || CurrentStance == Stance.Invulnerable || CurrentStance == Stance.Survival;
        protected bool InSurvival => CurrentStance == Stance.Survival;

        public const float ArmoredTaken = 0.35f;

        public const float KillDamage = 1e5f;

        protected virtual float[] PhaseGates => System.Array.Empty<float>();
        public int GateCount => PhaseGates.Length;

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
            if (!phaseModifierAdded) { phaseModifierAdded = true; health.IncomingModifiers.Add(PhaseModifier); }

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

        protected void EndSurvival() { if (CurrentStance == Stance.Survival) EndStance(); }

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

            if (IsServer) OnBroken();
            PlayActionRpc(RigAction.Cast, seconds);
            Taunt(taunt);
        }

        internal void SyncScaledPlayers(int players) => scaledPlayers = Mathf.Max(1, players);

        protected void InvulnerableWhile(string phase, string taunt, string hint, List<EnemyBrain> guards, float maxSeconds, System.Action then = null)
        {
            wards = guards ?? new List<EnemyBrain>();
            stanceDone = () => wards.Count == 0;
            stanceThen = then;
            SetStance(Stance.Invulnerable, phase, hint, maxSeconds);
            WardsLeft.Value = (byte)wards.Count;
            Taunt(taunt);
        }

        protected void Survive(string phase, string taunt, string hint, float seconds, System.Action then = null)
        {
            stanceThen = then;
            SetStance(Stance.Survival, phase, hint, seconds);
            castUntil = Time.time + seconds;
            busyUntil = castUntil;
            Taunt(taunt);
        }

        protected void SetArmored(bool on, string phase = null, string hint = null)
        {
            SetStance(on ? Stance.Armored : Stance.Normal, phase, hint, 0f);
        }

        protected void Taunt(string line)
        {
            if (!string.IsNullOrEmpty(line)) TauntRpc(DisplayName, line);
        }

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
            UI.GameHud.ShowCombo(at, "ASLEEP", new Color(0.6f, 0.75f, 1f));
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

        private Transform bubble;
        private Renderer bubbleRenderer;

        private Light bossLight;

        private void TickBossLight()
        {
            if (bossLight != null || Application.isBatchMode || health == null) return;
            bossLight = new GameObject("BossLight").AddComponent<Light>();
            bossLight.transform.SetParent(transform, false);
            bossLight.transform.localPosition = Vector3.up * (health.CenterHeight * 2f + 1.2f);
            bossLight.type = LightType.Point;
            bossLight.color = new Color(0.95f, 0.82f, 0.66f);
            bossLight.range = Mathf.Max(9f, health.CenterHeight * 4f);
            bossLight.intensity = 0.9f;
            bossLight.shadows = LightShadows.None;
        }

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
            TickBossLight();
            if (bubble == null) return;
            bubble.gameObject.SetActive(show);
            if (!show) return;
            var c = CurrentStance == Stance.Survival ? new Color(1f, 0.55f, 0.3f) : new Color(0.55f, 0.75f, 1f);
            c.a = 0.05f + 0.02f * Mathf.Sin(Time.time * 4f);
            ArtLibrary.Tint(bubbleRenderer, c);
        }
        protected float castUntil;

        public void SetMinionPrefab(NetworkObject prefab) => minionPrefab = prefab;

        public readonly NetworkVariable<float> Stagger = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> Broken = new NetworkVariable<bool>();
        public const float BreakSeconds = 3.5f, BreakTaken = 1.5f, StaggerGrace = 3f, StaggerDecay = 0.06f;

        public const float StaggerWeakBurst = 0.16f, StaggerWeak = 0.05f, StaggerHead = 0.035f, StaggerStun = 0.12f;
        private float lastStaggerAt, brokenUntil;
        public int Breaks { get; private set; }

        public void ServerAddStagger(float amount)
        {
            if (!IsServer || IsDead || Immune || Dormant || Broken.Value || InEntrance) return;
            amount /= 1f + 0.4f * Breaks;
            amount /= Mathf.Lerp(1f, 1.8f, (StandingParty - 1) / 3f);
            lastStaggerAt = Time.time;
            float v = Stagger.Value + amount;
            if (v >= 1f) ServerBreak();
            else Stagger.Value = v;
        }

        private void ServerBreak()
        {
            Breaks++;
            Stagger.Value = 1f;
            Broken.Value = true;
            brokenUntil = Time.time + BreakSeconds;
            heldUntil = Mathf.Max(heldUntil, brokenUntil);
            StopPatterns();
            if (attacking) CancelConeRpc();
            CancelWindup();
            CancelTelegraphRpc();
            OnBroken();
            BrokenRpc();
            Debug.Log("[Boss] " + DisplayName + " BROKEN (" + Breaks + ")");
        }

        protected virtual void OnBroken() { }

        public void ServerDebugBreak() { if (!Broken.Value) ServerBreak(); }

        private void TickStagger()
        {
            if (Broken.Value)
            {
                if (Time.time < brokenUntil) return;
                Broken.Value = false;
                Stagger.Value = 0f;
                return;
            }
            if (Stagger.Value > 0f && (Immune || Time.time - lastStaggerAt > StaggerGrace))
                Stagger.Value = Mathf.Max(0f, Stagger.Value - StaggerDecay * Time.deltaTime * (Immune ? 3f : 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BrokenRpc()
        {
            var gold = new Color(1f, 0.82f, 0.4f);
            UI.GameHud.ShowTitleCard(DisplayName.ToUpperInvariant() + " IS BROKEN", "Strike now: it takes half again", gold);
            Sfx.Play(Sound.Shatter, transform.position + Vector3.up * 2f, 1f, 0.6f);
            Sfx.Play(Sound.Roar, transform.position + Vector3.up * 2f, 0.8f, 1.2f);
            Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 4.5f, gold, 0.6f);
            Vfx.Flash(transform.position + Vector3.up * 2f, gold, 6f, 9f, 0.4f);
            Vfx.Embers(transform.position + Vector3.up * 2f, 24, gold, 5f, 0.06f, 0.9f);
            if (Rig != null) Rig.Flinch(transform.position + transform.forward * 2f, 1.4f);
        }

        protected override LootTable Loot => LootTable.SkeletonKing;

        protected virtual bool ClaimsActiveOnSpawn => true;

        public static void ClaimActive(BossBrain b)
        {
            if (b != null) { Active = b; return; }
            Active = null;
            foreach (var other in FindObjectsByType<BossBrain>(FindObjectsSortMode.None))
                if (other != null && other.ClaimsActiveOnSpawn && other.IsSpawned && !other.IsDead) { Active = other; break; }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (ClaimsActiveOnSpawn) Active = this;
            if (IsServer)
            {
                health.IncomingModifiers.Add((dmg, from) => Broken.Value && dmg < KillDamage ? dmg * BreakTaken : dmg);

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
            if (Active == this) { Active = null; if (!ClaimsActiveOnSpawn) ClaimActive(null); }
            base.OnNetworkDespawn();
        }

        public void ServerScaleBoss(int players, int depth)
        {
            scaledPlayers = players;
            scaledDepth = depth;
            ServerScale(players, depth);

            signatureScale = Difficulty.Level.Damage * Difficulty.DepthDamage(depth) / Difficulty.DepthDamage(Mathf.Min(depth, HomeDepthCap));
        }

        private const int HomeDepthCap = 7;
        private float signatureScale = 1f;

        protected float Signature(float baseHit) => baseHit * signatureScale;

        protected bool Casting => Time.time < castUntil;

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
            signatureScale *= 1.4f;
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

        protected virtual float ShockwaveCooldown => 0f;
        protected virtual Color ShockwaveColor => new Color(1f, 0.6f, 0.3f);
        protected virtual float ShockwaveDamage => damage * 0.9f;
        private float nextShockwave = -1f, shockwaveAt = -1f;

        private class Wave { public Vector3 Origin; public float Start, Speed, Max, Damage; public readonly HashSet<ulong> Hit = new HashSet<ulong>(); }
        private readonly List<Wave> waves = new List<Wave>();

        protected bool TryShockwave()
        {
            if (ShockwaveCooldown <= 0f) return false;

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

        public bool Dormant { get; set; }

        protected override bool CanDash => base.CanDash && !Dormant && !Immune && !InEntrance && Hunts;

        protected override bool IsBoss => true;
        protected override bool Heavy => true;

        protected override void Update()
        {
            if (IsServer && !IsDead) { TickShockwaves(); TickFury(); TickPatterns(); TickDoom(); if (!IsStunned && !InEntrance) TickQuickShots(); }
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

        protected virtual void LateUpdate()
        {
            if (IsServer && !IsDead && IsSpawned)
            {
                TickPhases();
                TickStagger();

                TickStrikes();
                if (!IsStunned && !InEntrance) TickHunt();
                TickDash();
                TickRepulse();
            }
            TickBubble();
        }

        protected class Pattern
        {
            public int Arms = 4;
            public float Interval = 0.12f, Speed = 12f, Spin = 12f, Damage = 10f, Range = 34f;

            public float FlipEvery;

            public float Wobble;

            public float AimedEvery;
            public ProjectileKind Kind = ProjectileKind.ShadowBolt;

            public System.Func<float, Vector3> Origin;
            public System.Action<Health> OnHit;
            public float Duration = 6f;
            internal float Started, NextAt, NextAimed, Angle;
            internal byte Id;
        }

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

        protected override void BeginAttack()
        {
            base.BeginAttack();
            if (attacking && Rig != null)

                ConeTelegraphRpc(transform.position, strikeDir, Mathf.Clamp(MoveArc, 40f, 300f), MoveReach + 0.45f, Mathf.Max(0.1f, strikeAt - Time.time));
        }

        protected override void Strike()
        {
            var apex = transform.position;
            float arc = MoveArc, reach = MoveReach;
            var dir = strikeDir;
            bool heavy = currentMove == Move.Heavy;
            base.Strike();
            StrikeWakeRpc(apex, dir, arc, reach, heavy);

            if (attacking && Rig != null)
                ConeTelegraphRpc(transform.position, strikeDir, Mathf.Clamp(MoveArc, 40f, 300f), MoveReach + 0.45f, Mathf.Max(0.1f, strikeAt - Time.time));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeWakeRpc(Vector3 apex, Vector3 dir, float arc, float reach, bool heavy)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;
            dir.Normalize();
            var grit = new Color(0.55f, 0.48f, 0.4f);
            int n = Mathf.Clamp(Mathf.RoundToInt(arc / 14f), 4, 16);
            for (int i = 0; i < n; i++)
            {
                float yaw = Mathf.Lerp(-arc / 2f, arc / 2f, n == 1 ? 0.5f : i / (float)(n - 1));
                var at = Vfx.Floor(apex + Quaternion.AngleAxis(yaw, Vector3.up) * dir * reach * Random.Range(0.8f, 1f)) + Vector3.up * 0.1f;
                Vfx.Embers(at, 2, grit, 2.4f, 0.05f, 0.5f);
            }
            if (heavy)
            {
                var hit = Vfx.Floor(apex + dir * Mathf.Min(reach, 2.2f));
                Vfx.Ring(hit + Vector3.up * 0.05f, 1.6f, new Color(1f, 0.55f, 0.3f), 0.4f);
                Vfx.Smoke(hit, 4, 1.2f, 1.2f);
                Vfx.Embers(hit + Vector3.up * 0.2f, 10, grit, 4.5f, 0.07f, 0.7f);
                Sfx.Play(Sound.Slam, hit, 0.8f, 0.8f);
            }
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
            if (IsDead || !IsSpawned) return;
            ServerAddStagger(StaggerStun * Mathf.Clamp(seconds, 0.5f, 2f));
            if (attacking) CancelConeRpc();
            base.ServerStun(seconds);
        }

        protected Vector3 PredictedFeet(PlayerLife p, float seconds)
        {
            var v = p.ServerVelocity;
            v.y = 0f;
            var at = p.transform.position + v * seconds * LeadSkill;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            return at;
        }

        protected NetworkObject guardPrefab;

        public void SetGuardPrefab(NetworkObject prefab) => guardPrefab = prefab;

        protected List<EnemyBrain> Summon(int count, float radius, float castTime = 1.6f) => SummonOf(minionPrefab, count, radius, castTime);

        protected List<EnemyBrain> SummonOf(NetworkObject prefab, int count, float radius, float castTime = 1.6f)
        {
            var raised = new List<EnemyBrain>();
            var minionPrefab = prefab;
            castUntil = Time.time + castTime;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Summon, castTime);
            SummonFxRpc();

            if (minionPrefab == null) return raised;
            count = Difficulty.SummonCount(count, StandingParty);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var pos = World.DropSpots.Stand(transform.position, transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius, 0.3f, 1f);

                var minion = World.DungeonDirector.ServerSpawnEntering(minionPrefab, pos, Quaternion.LookRotation(transform.position - pos), 0f, 0.6f);
                var brain = minion.GetComponent<EnemyBrain>();
                brain.NoLoot = true;
                brain.ServerScale(scaledPlayers, scaledDepth);
                brain.ServerAggroNearest();
                raised.Add(brain);
            }
            return raised;
        }

        protected static void KnockPlayer(Health victim, Vector3 velocity)
        {
            if (victim.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(velocity);
        }

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

        [Rpc(SendTo.ClientsAndHost)]
        protected void FreeLineTelegraphRpc(Vector3 start, Vector3 direction, float length, float width, float duration)
        {
            Telegraph.ShowLine(start, direction, length, width, duration, null, new Color(1f, 0.15f, 0.05f, 0.12f));
            Gather(duration, 0.8f);
        }

        protected void Gather(float seconds, float k)
        {
            if (k >= 0.6f && seconds >= 0.35f && Time.time >= nextGatherSoundAt)
            {
                nextGatherSoundAt = Time.time + 0.5f;
                Sfx.Play(Sound.Whisper, transform.position + Vector3.up * (health != null ? health.CenterHeight * 1.6f : 2f), 1f, 0.45f);
            }
            if (Rig == null) return;
            BossWindup.For(gameObject, Rig, health != null ? health.CenterHeight * 2f : 2f).Anticipate(seconds, k);

            if (seconds >= 0.8f && Time.time >= nextCircleAt)
            {
                nextCircleAt = Time.time + 3f;
                FxSprite.MagicCircle(transform.position, Mathf.Max(1.6f, BodyRadiusForFx * 1.6f), seconds, Rig.GlowColor);
            }
        }

        private float nextCircleAt, nextGatherSoundAt;
        private float BodyRadiusForFx => health != null ? health.BodyRadius : 1f;

        [Rpc(SendTo.ClientsAndHost)]
        protected void GatherRpc(float seconds, float k) => Gather(seconds, k);

        public const float BossEntranceSeconds = 2.4f;

        protected virtual byte EntranceStyle => 0;

        public void ServerEntrance()
        {
            heldUntil = Time.time + BossEntranceSeconds;

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

        public static int EntrancesPlayed { get; private set; }

        [Rpc(SendTo.ClientsAndHost)]
        private void EntranceRpc(byte style)
        {
            EntrancesPlayed++;
            StartCoroutine(PlayEntrance(style));
        }

        private System.Collections.IEnumerator PlayEntrance(byte style)
        {
            var feet = transform.position + Vector3.up * 0.05f;
            float height = health != null ? health.CenterHeight * 2f : 3f;
            float reach = Mathf.Max(3f, (health != null ? health.BodyRadius : 1f) * 3f);
            Color tint = style == 1 ? new Color(0.85f, 0.75f, 0.6f) : style == 2 ? new Color(0.4f, 0.75f, 1f) : style == 3 ? new Color(1f, 0.82f, 0.35f)
                : style == 4 ? new Color(1f, 0.55f, 0.25f) : style == 5 ? new Color(0.6f, 0.4f, 1f) : new Color(0.9f, 0.35f, 0.3f);

            Gather(1.05f, 1f);
            Rig?.Play(RigAction.Cast, 1.05f);
            if (style == 2) Vfx.Ring(feet, reach * 1.4f, tint, 1f);
            if (style == 5) Vfx.Flash(transform.position + Vector3.up * height * 0.6f, tint, 4f, 14f, 1f);
            Sfx.Play(style == 4 ? Sound.Toll : Sound.Summon, transform.position + Vector3.up * height * 0.5f, 0.8f, style == 5 ? 0.5f : 0.7f);
            yield return new WaitForSeconds(1.05f);
            if (this == null || IsDead) yield break;

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

        protected int StandingParty
        {
            get { int n = 0; foreach (var p in PlayerLife.All) if (p != null && p.Targetable) n++; return Mathf.Max(1, n); }
        }

        protected float Tell(float seconds) => seconds * Difficulty.Tell(scaledDepth) * (furious ? 0.85f : 1f);

        protected float Cadence => StandingParty <= 1 ? 0.8f : 1f;

        protected virtual bool Hunts => true;
        protected virtual bool Dooms => true;
        protected virtual float HuntDamageScale => 1f;
        protected virtual bool Reaches(PlayerLife p) => p != null;

        public const float StrikeRadius = 2.2f, LanceLength = 24f, LanceWidth = 1.7f;

        private struct PendingStrike { public Vector3 At, Dir; public float When, Radius, Damage, Length; public bool Line; }

        public static readonly Color StrikeUnderYou = new Color(1f, 0.22f, 0.08f, 0.55f);
        private readonly List<PendingStrike> strikes = new List<PendingStrike>();
        private float nextHunt = -1f;
        private int huntKind;

        public int HuntsThrown { get; private set; }

        public Vector3 LastStrikeAt { get; private set; }

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

        public void ServerHuntNow()
        {
            HuntsThrown++;
            if (huntKind++ % 2 == 0) StartCoroutine(HuntingStrikes());
            else ServerLanceFan();
        }

        private System.Collections.IEnumerator HuntingStrikes()
        {
            int count = scaledDepth >= 6 ? 4 : 3;
            float tell = Tell(1.15f);
            GatherRpc(tell, 0.7f);
            PlayActionRpc(RigAction.Hurl, 0.6f);
            for (int k = 0; k < count && !IsDead; k++)
            {
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable || !Reaches(p)) continue;
                    var at = PredictedFeet(p, tell + 0.1f);
                    LastStrikeAt = at;
                    strikes.Add(new PendingStrike { At = at, When = Time.time + tell, Radius = StrikeRadius, Damage = damage * 0.55f * HuntDamageScale });
                    StrikeCircleRpc(at, StrikeRadius, tell);
                }
                yield return new WaitForSeconds(0.42f);
            }
        }

        public void ServerLanceFan()
        {
            float tell = Tell(1.0f);
            int lines = scaledDepth >= 4 ? 5 : 3;
            float spread = scaledDepth >= 4 ? 15f : 22f;
            PlayActionRpc(RigAction.Slam, tell + 0.2f);
            var from = transform.position + Vector3.up * 0.05f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable || !Reaches(p)) continue;
                var to = PredictedFeet(p, tell) - from;
                to.y = 0f;
                if (to.sqrMagnitude < 0.5f) to = transform.forward;
                to.Normalize();
                for (int i = 0; i < lines; i++)
                {
                    var dir = Quaternion.Euler(0f, (i - (lines - 1) / 2f) * spread, 0f) * to;
                    float length = LanceReach(from, dir);
                    strikes.Add(new PendingStrike { At = from, Dir = dir, When = Time.time + tell, Damage = damage * 0.65f * HuntDamageScale, Length = length, Line = true });
                    FreeLineTelegraphRpc(from, dir, length, LanceWidth, tell);
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
                    if (p == null || !p.Targetable || !Reaches(p)) continue;
                    var to = p.transform.position - s.At;
                    to.y = 0f;
                    bool hit;
                    if (s.Line)
                    {
                        float along = Vector3.Dot(to, s.Dir);
                        hit = along >= 0f && along <= s.Length && (to - s.Dir * along).magnitude <= LanceWidth * 0.5f + 0.3f;
                    }
                    else hit = to.magnitude <= s.Radius + 0.3f;
                    if (!hit) continue;
                    StrikeHits++;
                    LastStrikeDealt = p.Health.ServerDamage(s.Damage, s.At + Vector3.up, NetworkObjectId);
                    KnockPlayer(p.Health, (s.Line ? s.Dir * 3f : to.normalized * 4f) + Vector3.up * 4f);
                }
                StrikeFxRpc(s.At, s.Line ? s.Dir * s.Length : Vector3.zero, s.Line);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeFxRpc(Vector3 at, Vector3 dir, bool line)
        {
            var c = Rig != null ? Rig.GlowColor : new Color(1f, 0.4f, 0.2f);
            if (line)
            {
                float length = dir.magnitude;
                dir /= Mathf.Max(0.01f, length);
                for (float d = 1.5f; d < length; d += 2.5f)
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

        private static float LanceReach(Vector3 from, Vector3 dir)
        {
            var to = from + dir * LanceLength;
            return UnityEngine.AI.NavMesh.Raycast(from, to, out var hit, UnityEngine.AI.NavMesh.AllAreas) ? Mathf.Max(2f, hit.distance) : LanceLength;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeCircleRpc(Vector3 center, float radius, float duration)
        {
            var me = NetworkPlayer.Local;
            var gap = me != null ? me.transform.position - center : Vector3.one * 99f;
            gap.y = 0f;
            bool mine = gap.magnitude <= radius + 1.5f;
            Telegraph.Show(center, radius, duration, null, mine ? StrikeUnderYou : Telegraph.Harm);
            if (mine) Sfx.Play2D(Sound.Creak, 0.8f);
            Gather(duration, 0.8f);
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
