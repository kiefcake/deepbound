using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    [RequireComponent(typeof(Health))]
    public class FlagellantKit : NetworkBehaviour, IClassKit
    {
        public const float SwingWindup = 0.2f, SwingStrike = 0.14f, SwingRecovery = 0.36f;
        public const float SwingRange = 2.6f, SwingArc = 120f, SwingDamage = 22f;
        public const int CrashEvery = 3;
        public const float CrashMultiplier = 1.6f, CrashRadius = 1.7f;
        public const float ScourgeCooldown = 6f, ScourgeCost = 0.1f, ScourgeFervor = 30f, ZealSeconds = 5f, ZealAttackTime = 0.7f, ZealDamage = 1.15f;
        public const float ChainCooldown = 9f, ChainRange = 16f, ChainStun = 0.9f, ChainDamage = 14f;
        public const float LeapCooldown = 12f, LeapRange = 12f, LeapDamage = 40f, LeapRadius = 3.5f, LeapStun = 1f;
        public const float FervorMax = 100f, FervorPerHit = 8f, FervorDecay = 6f;
        public const float MortificationMax = 0.4f, Penance = 0.04f;

        public float AttackRange => SwingRange * (progress != null ? progress.ReachMultiplier : 1f);

        public readonly NetworkVariable<float> Fervor = new NetworkVariable<float>();

        public readonly NetworkVariable<double> ZealEnds = new NetworkVariable<double>();

        public readonly NetworkVariable<double> MartyrEnds = new NetworkVariable<double>();

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        private float swingBusyUntil, swingStrikeAt, scourgeReadyAt, chainReadyAt, leapReadyAt;
        private bool swingPending;
        private float side = 1f;
        private float lastServerSwing, lastHitAt;
        private int swings;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Flagellant;
        private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public bool Zealous => ZealEnds.Value > Now;
        public bool Martyr => MartyrEnds.Value > Now;

        public ClassId Class => ClassId.Flagellant;
        public string ResourceName => "Fervor";

        public string ResourceHint => Fervor.Value < 1f ? "Builds from dealing and taking damage" : "+" + Mathf.RoundToInt(Fervor.Value / 5f) + "% damage  ·  Spent by Penitent Leap";
        public Color ResourceColor => new Color(0.9f, 0.2f, 0.15f);
        public float Resource => Fervor.Value;
        public float ResourceMax => FervorMax;
        public void RestoreResource(float fraction) => AddFervorRpc(FervorMax * fraction);
        public void ShaveCooldowns(float seconds) { chainReadyAt -= seconds; leapReadyAt -= seconds; }

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = (swings % CrashEvery) == CrashEvery - 1 ? "Crash!" : "Flail", Active = Zealous });
            into.Add(new AbilityView { Key = "RMB", Name = "Scourge", Remaining = scourgeReadyAt - Time.time, Cooldown = ScourgeCooldown, Active = Zealous, Unaffordable = health.Fraction < 0.25f });
            into.Add(new AbilityView { Key = "Q", Name = "Chains", Remaining = chainReadyAt - Time.time, Cooldown = ChainCooldown * progress.CooldownFor(AbilitySlot.Q) });
            into.Add(new AbilityView { Key = "E", Name = "Leap", Remaining = leapReadyAt - Time.time, Cooldown = LeapCooldown * progress.CooldownFor(AbilitySlot.E) });
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            health.IncomingModifiers.Add(ModifyIncoming);
            Health.ServerAnyDamaged += OnAnyDamaged;
            health.DamagedServer += OnHurt;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Health.ServerAnyDamaged -= OnAnyDamaged;
        }

        private float ModifyIncoming(float dmg, Vector3 from)
        {
            if (!Active || !Martyr) return dmg;
            return dmg * LitanyTaken;
        }

        private void OnAnyDamaged(Health t, float dealt, ulong from)
        {
            if (this == null || !Active || dealt <= 0f) return;

            if (t == health) Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + dealt / Mathf.Max(1f, health.Max.Value) * 60f);
        }

        private void Update()
        {
            if (IsServer && Active && Time.time - lastHitAt > 3f && Fervor.Value > 0f)
                Fervor.Value = Mathf.Max(0f, Fervor.Value - FervorDecay * Time.deltaTime);
            if (!IsOwner || !Active) return;

            motor.SpeedMultiplier = progress.SpeedMultiplier * (Zealous ? 1.08f : 1f);
            motor.CanSprint = true;
            bool canAct = life.CanAct && motor.ActionsReady;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (canAct && UI.Binds.PrimaryHeld && Time.time >= swingBusyUntil)
            {
                side = -side;
                swingPending = true;
                swingStrikeAt = Time.time + SwingWindup;
                float speed = progress.AttackTimeMultiplier * (Zealous ? ZealAttackTime : 1f);
                swingBusyUntil = Time.time + SwingWindup + SwingStrike + SwingRecovery * speed;
                player.ViewModelSwing(SwingWindup, SwingStrike + SwingRecovery * speed, side);
                Sfx.Play(Sound.Swing, transform.position, 0.7f, 0.75f);

                MeleeStep.TryStepIn(motor, transform, player.AimForward, SwingRange * progress.ReachMultiplier, 10f);
            }
            if (swingPending && Time.time >= swingStrikeAt)
            {
                swingPending = false;
                SwingRpc(player.AimOrigin, player.AimForward, side);
            }

            if (canAct && UI.Binds.SecondaryDown && Time.time >= scourgeReadyAt && health.Fraction >= 0.25f)
            {
                scourgeReadyAt = Time.time + ScourgeCooldown;
                player.ViewModelBash();
                ScourgeRpc();
            }

            if (canAct && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= chainReadyAt)
            {
                var target = AimedEnemy(ChainRange * progress.RangeMultiplier);
                if (target == null) UI.GameHud.Feed("No enemy in reach of the chain", new Color(0.8f, 0.75f, 0.7f));
                else
                {
                    chainReadyAt = Time.time + ChainCooldown * progress.CooldownFor(AbilitySlot.Q);
                    player.ViewModelCast(false);
                    ChainRpc(target.NetworkObjectId);
                }
            }

            bool freeLeap = Time.time < freeLeapUntil;
            if (canAct && UI.Binds.Down(UI.Bind.AbilityE) && (Time.time >= leapReadyAt || freeLeap))
            {
                if (freeLeap) freeLeapUntil = 0f;
                else
                {
                    leapReadyAt = Time.time + LeapCooldown * progress.CooldownFor(AbilitySlot.E);

                    if (progress.A(AbilitySlot.E, 2)) freeLeapUntil = Time.time + 0.6f + SecondLeapWindow;
                }
                var to = LeapTarget();
                var flat = to - transform.position;
                flat.y = 0f;
                float dist = Mathf.Min(flat.magnitude, LeapRange);

                motor.AddImpulse(flat.normalized * Mathf.Sqrt(2f * 40f * Mathf.Max(1f, dist)) + Vector3.up * 7.5f);
                player.ViewModelBash();
                LeapRpc();
            }
        }

        private Health AimedEnemy(float range)
        {
            Health best = null;
            float bestScore = float.MaxValue;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var to = h.Center - player.AimOrigin;
                float d = to.magnitude;
                if (d > range) continue;
                float angle = Vector3.Angle(player.AimForward, to);
                if (angle > 12f || !CombatQuery.ClearLine(player.AimOrigin, h)) continue;
                float score = angle + d * 0.3f;
                if (score < bestScore) { bestScore = score; best = h; }
            }
            return best;
        }

        private Vector3 LeapTarget()
        {
            if (player.AimRay(LeapRange + 2f, out var hit)) return hit.point;
            return player.TryAimFloor(LeapRange, out var spot) ? spot : transform.position + transform.forward * LeapRange;
        }

        private float DamageMultiplier
        {
            get
            {
                float missing = 1f - health.Fraction;
                float m = progress.AttackMultiplier * (1f + Mathf.Min(MortificationMax, missing * 0.5f)) * (1f + Fervor.Value / 500f);
                if (Zealous) m *= ZealDamage;
                if (Martyr) m *= 1.3f;
                return m;
            }
        }

        private float Lifesteal => (progress.OnPath(2) ? Penance * 2f : Penance) + (Martyr ? 0.25f : 0f);

        private void AfterHits(float dealt)
        {
            if (dealt <= 0f) return;
            lastHitAt = Time.time;
            health.ServerHeal(dealt * Lifesteal);
        }

        [Rpc(SendTo.Server)]
        private void AddFervorRpc(float amount) => Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + Mathf.Clamp(amount, 0f, FervorMax));

        [Rpc(SendTo.Server)]
        private void SwingRpc(Vector3 origin, Vector3 forward, float swingSide)
        {
            if (!life.CanAct) return;
            float minInterval = (SwingWindup + SwingStrike + SwingRecovery * progress.AttackTimeMultiplier * ZealAttackTime) * 0.7f;
            if (Time.time - lastServerSwing < minInterval) return;
            lastServerSwing = Time.time;
            ServerSwing(origin, forward, swingSide);
        }

        public void ServerSwing(Vector3 origin, Vector3 forward, float swingSide)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;
            swings++;

            bool crash = swings % (progress.OnPath(1) ? 2 : CrashEvery) == 0;
            float range = SwingRange * progress.ReachMultiplier;

            bool whirl = progress.OnPath(2);
            float dmg = SwingDamage * DamageMultiplier * (crash ? CrashMultiplier : 1f) * (whirl && !crash ? WhirlShare : 1f);
            float total = 0f;
            var hit = new HashSet<Health>();
            foreach (var target in CombatQuery.Arc(origin, forward, range, whirl && !crash ? 360f : SwingArc, Team.Enemies))
            {
                var zone = HitZones.Aimed(target, origin, forward, range, out var zonePoint);
                total += target.Tag("FlagellantKit").ServerDamage(dmg * HitZones.Multiplier(target, zone, false) * Combos.TryShatter(target, progress), transform.position, NetworkObjectId);
                if (zone != HitZone.Body) Arrows.ReportZone(NetworkObjectId, zonePoint, zone);
                hit.Add(target);
            }
            Vector3 crashAt = transform.position + Flat(forward) * (range * 0.75f);
            if (crash)
            {
                float radius = CrashRadius;

                if (progress.OnPath(1)) StartCoroutine(BurnGround(crashAt));
                foreach (var target in CombatQuery.Circle(crashAt, radius, Team.Enemies))
                {
                    if (!hit.Contains(target)) total += target.Tag("FlagellantKit").ServerDamage(dmg * 0.6f, crashAt, NetworkObjectId);
                    if (progress.Mastered(1) && target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(0.8f);
                }

                var struck = new List<Health>(hit);
                struck.AddRange(CombatQuery.Circle(crashAt, radius, Team.Enemies));
                Classes.Combos.ServerPlagueCrash(struck, crashAt, radius + 0.8f, progress);
            }
            if (total > 0f) Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + FervorPerHit * (crash ? 1.5f : 1f));
            AfterHits(total);
            SwingFxRpc(swingSide, total > 0f, crash, crashAt);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward; }

        [Rpc(SendTo.Server)]
        private void ScourgeRpc() => ServerScourge();

        public void ServerScourge()
        {
            if (!life.CanAct || health.Fraction < 0.25f) return;
            float cost = health.Max.Value * ScourgeCost;
            health.ServerSetFraction((health.Current.Value - cost) / health.Max.Value);
            Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + ScourgeFervor);
            ZealEnds.Value = Now + ZealSeconds;
            lastHitAt = Time.time;

            if (progress.Mastered(2))
                foreach (var ally in PlayerLife.All)
                    if (ally != null && ally != life && !ally.Downed.Value && Vector3.Distance(ally.transform.position, transform.position) < 6f)
                        ally.Health.ServerHeal(cost);
            ScourgeFxRpc();
        }

        [Rpc(SendTo.Server)]
        private void ChainRpc(ulong targetId)
        {
            if (!life.CanAct || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj) || !obj.TryGetComponent<Health>(out var target) || !target.IsAlive) return;
            ServerChain(target);
        }

        public const float SecondLeapWindow = 3f, PullRadius = 8f, QuakeRadius = 6f, BrandSeconds = 4f, DoubleChainRange = 8f;
        private float freeLeapUntil;

        public bool ServerChain(Health target) => ServerChain(target, true);

        private bool ServerChain(Health target, bool first)
        {
            float potency = progress.Potency(AbilitySlot.Q);
            if (Vector3.Distance(target.transform.position, transform.position) > ChainRange * progress.RangeMultiplier + 2f) return false;
            var from = transform.position;
            var toMe = Flat(target.transform.position - from);
            var spot = from + toMe * 1.6f;
            var startAt = target.transform.position;
            float haulFor = Enemies.EnemyBrain.HaulSeconds(Vector3.Distance(target.transform.position, spot));
            bool dragged = target.TryGetComponent<Enemies.EnemyBrain>(out var brain) && brain.ServerYank(spot, Art.ChainLink.ReachSeconds);
            target.Tag("FlagellantKit").ServerDamage(ChainDamage * DamageMultiplier * potency, from, NetworkObjectId);

            bool shackle = progress.A(AbilitySlot.Q, 3);
            if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(ChainStun * (dragged ? 1f : 0.6f) * (shackle ? 2f : 1f) + (dragged ? haulFor + Art.ChainLink.ReachSeconds : 0f));
            if (shackle) target.ServerMark(5f);
            ulong me = NetworkObjectId;

            if (progress.A(AbilitySlot.Q, 1))
                foreach (var other in CombatQuery.Circle(startAt, 3f, Team.Enemies).ToArray())
                    if (other != target && other.TryGetComponent<Enemies.EnemyBrain>(out var ob))
                    {
                        ob.ServerYank(spot + Random.insideUnitSphere.normalized * 1.2f, Art.ChainLink.ReachSeconds);
                        AbilityKit.Stun(other, ChainStun * 0.7f + haulFor);
                    }

            if (progress.B(AbilitySlot.Q, 1))
                foreach (var h in AbilityKit.Lane(from, startAt - from, Vector3.Distance(from, startAt), 1f))
                    if (h != target) { h.Tag("Lash").ServerDamage(ChainDamage * DamageMultiplier * potency, from, me); AbilityKit.Stun(h, 0.4f); }

            if (progress.A(AbilitySlot.Q, 2))
                AbilityKit.Bleed(this, target, ChainDamage * DamageMultiplier * 0.8f, 4f, me, "Hooked", d => { if (health.IsAlive) health.ServerHeal(d); });

            if (progress.B(AbilitySlot.Q, 2) && dragged)
                AbilityKit.Later(this, Art.ChainLink.ReachSeconds + haulFor, () =>
                {
                    AbilityKit.Blast(transform.position, 2.5f, ChainDamage * DamageMultiplier * 1.2f, me, "Chain Sweep", h => AbilityKit.Stun(h, 0.6f));
                    progress.AbilityBurstRpc(transform.position, 2.5f, PlayerProgress.FxKind.Blood);
                });

            if (progress.B(AbilitySlot.Q, 3) && !dragged)
                AbilityKit.Later(this, 0.55f, () =>
                {
                    float dmg = SwingDamage * DamageMultiplier * CrashMultiplier;
                    AfterHits(AbilityKit.Blast(transform.position + Flat(target != null ? target.transform.position - transform.position : transform.forward) * 1.2f, CrashRadius * 1.4f, dmg, me, "Reel-In Crash", h => AbilityKit.Stun(h, 0.6f)).Count > 0 ? dmg : 0f);
                    progress.AbilityBurstRpc(transform.position, CrashRadius * 1.4f, PlayerProgress.FxKind.Blood);
                });

            if (progress.B(AbilitySlot.Q, 4)) { health.ServerHeal(health.Max.Value * 0.06f); Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + 20f); }
            Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + FervorPerHit);
            lastHitAt = Time.time;
            ChainFxRpc(transform.position + Vector3.up * 1.3f, target.Center, dragged, target.NetworkObjectId, dragged ? haulFor : 0.2f);
            if (!dragged && first) DragMeRpc(target.transform.position - toMe * 1.8f);

            if (first && progress.A(AbilitySlot.Q, 4))
            {
                var next = AbilityKit.Nearest(startAt, DoubleChainRange, new[] { target });
                if (next != null) AbilityKit.Later(this, 0.2f, () => { if (next != null && next.IsAlive) ServerChain(next, false); });
            }
            return true;
        }

        [Rpc(SendTo.Owner)]
        private void DragMeRpc(Vector3 to)
        {
            var flat = to - transform.position;
            flat.y = 0f;
            motor.AddImpulse(flat.normalized * Mathf.Sqrt(2f * 40f * Mathf.Max(1f, flat.magnitude)) + Vector3.up * 3f);
        }

        [Rpc(SendTo.Server)]
        private void LeapRpc()
        {
            if (!life.CanAct) return;
            LeapTakeoffFxRpc();
            StartCoroutine(LeapCrash());
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapTakeoffFxRpc()
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Leap, 0.6f);
            ClassFx.FlagLeapTrail(transform, 0.65f);
        }

        private IEnumerator LeapCrash()
        {
            yield return new WaitForSeconds(0.6f);
            ServerLeapCrash(transform.position);
        }

        public void ServerLeapCrash(Vector3 at)
        {
            float potency = progress.Potency(AbilitySlot.E);
            float fervor = Fervor.Value;
            Fervor.Value = 0f;
            float dmg = LeapDamage * DamageMultiplier * potency * (1f + fervor / 100f);
            ulong me = NetworkObjectId;

            if (progress.A(AbilitySlot.E, 3))
            {
                foreach (var h in CombatQuery.Circle(at, PullRadius, Team.Enemies).ToArray())
                    if (!AbilityKit.IsBoss(h) && h.TryGetComponent<Enemies.EnemyBrain>(out var pb)) pb.ServerYank(at + Random.insideUnitSphere.normalized * 1.3f);
                progress.AbilityBurstRpc(at, PullRadius * 0.5f, PlayerProgress.FxKind.Shadow);
            }

            if (progress.B(AbilitySlot.E, 3) && health.Fraction > 0.15f)
            {
                health.ServerSetFraction(health.Fraction - 0.1f);
                dmg *= 2f;
                progress.AbilityTextRpc(at + Vector3.up * 2.2f, "MARTYR'S FALL", PlayerProgress.FxKind.Blood);
            }
            bool shockwave = progress.A(AbilitySlot.E, 1);
            float total = 0f;
            foreach (var target in CombatQuery.Circle(at, LeapRadius, Team.Enemies).ToArray())
            {
                total += target.Tag("FlagellantKit").ServerDamage(dmg, at, me);
                if (target.TryGetComponent<ICrowdControl>(out var cc))
                {
                    cc.ServerStun(LeapStun * (shockwave ? 1.5f : 1f));
                    if (shockwave) cc.ServerKnockback(Flat(target.transform.position - at) * 7f);
                }
            }

            if (progress.A(AbilitySlot.E, 4))
            {
                foreach (var h in CombatQuery.Circle(at, QuakeRadius, Team.Enemies).ToArray()) AbilityKit.Stun(h, 2f);
                progress.AbilityBurstRpc(at, QuakeRadius, PlayerProgress.FxKind.Earth);
            }

            if (progress.B(AbilitySlot.E, 4))
                for (int i = 1; i <= 3; i++)
                {
                    float r = LeapRadius + i * 1.6f;
                    AbilityKit.Later(this, i * 0.4f, () =>
                    {
                        AbilityKit.Blast(at, r, LeapDamage * DamageMultiplier * potency * 0.35f, me, "Afterquake", h => AbilityKit.Stun(h, 0.4f));
                        progress.AbilityBurstRpc(at, r, PlayerProgress.FxKind.Earth);
                    });
                }

            if (progress.B(AbilitySlot.E, 1)) StartCoroutine(Brand(at));

            if (progress.B(AbilitySlot.E, 2) && fervor > 0f)
            {
                foreach (var ally in CombatQuery.Circle(at, 5f, Team.Players).ToArray()) ally.ServerHeal(ally.Max.Value * 0.2f * (fervor / FervorMax));
                progress.AbilityBurstRpc(at, 5f, PlayerProgress.FxKind.Blood);
            }
            AfterHits(total);
            LeapFxRpc(at, fervor);
        }

        public const float WhirlShare = 0.7f, BurnSeconds = 2f, EruptRadius = 4f, EruptEvery = 20f, EruptEveryMastered = 12f;
        private float nextErupt;

        private IEnumerator BurnGround(Vector3 at)
        {
            progress.AbilityCircleRpc(at, CrashRadius, BurnSeconds, PlayerProgress.FxKind.Fire);
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(0.5f);
                AbilityKit.Blast(at, CrashRadius, 6f * DamageMultiplier * 0.5f, NetworkObjectId, "Burning Ground", h => h.ServerScorch(2f, NetworkObjectId));
            }
        }

        private void OnHurt(float lost, ulong from)
        {
            if (!Active || !progress.OnPath(Classes.PathTriads.Path) || !health.IsAlive || Time.time < nextErupt) return;
            float before = (health.Current.Value + lost) / Mathf.Max(1f, health.Max.Value);
            if (before < 0.5f || health.Fraction >= 0.5f) return;
            nextErupt = Time.time + (progress.Mastered(Classes.PathTriads.Path) ? EruptEveryMastered : EruptEvery);
            health.ServerHeal(health.Max.Value * 0.1f);
            foreach (var h in CombatQuery.Circle(transform.position, EruptRadius, Team.Enemies).ToArray()) AbilityKit.Stun(h, 1.2f);
            progress.AbilityBurstRpc(transform.position, EruptRadius, PlayerProgress.FxKind.Blood);
            progress.AbilityTextRpc(transform.position + Vector3.up * 2.3f, "SCARRED", PlayerProgress.FxKind.Blood);
        }

        private IEnumerator Brand(Vector3 at)
        {
            progress.AbilityCircleRpc(at, LeapRadius, BrandSeconds, PlayerProgress.FxKind.Holy);
            float end = Time.time + BrandSeconds;
            while (Time.time < end)
            {
                yield return new WaitForSeconds(0.5f);
                AbilityKit.Blast(at, LeapRadius, 10f * DamageMultiplier * 0.5f, NetworkObjectId, "Brand", h => h.ServerScorch(2f, NetworkObjectId));
                progress.AbilityBurstRpc(at + Random.insideUnitSphere * LeapRadius * 0.6f, 0.4f, PlayerProgress.FxKind.Holy);
            }
        }

        public const float LitanySeconds = 6f, LitanyTick = 0.3f, LitanyRadius = 3.4f, LitanyShare = 0.75f, LitanyTaken = 0.8f;
        public const float AmenRadius = 5f, AmenMultiplier = 3f, AmenStun = 1.2f;

        public void ServerMartyrdom(float seconds) => ServerLitany();

        public void ServerLitany()
        {
            MartyrEnds.Value = Now + LitanySeconds;
            Fervor.Value = FervorMax;
            StartCoroutine(Litany());
        }

        private IEnumerator Litany()
        {
            float end = Time.time + LitanySeconds;
            int beat = 0;
            while (Time.time < end && health.IsAlive)
            {
                float total = 0f;
                foreach (var h in AbilityKit.Blast(transform.position, LitanyRadius, 0f, NetworkObjectId, "Red Litany"))
                {
                    if (h == null || !h.IsAlive) continue;
                    float before = h.Current.Value;
                    h.Tag("Red Litany").ServerDamage(SwingDamage * LitanyShare * DamageMultiplier, transform.position, NetworkObjectId);
                    total += Mathf.Max(0f, before - h.Current.Value);
                }
                if (total > 0f) { AfterHits(total); Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + FervorPerHit * 0.5f); }
                LitanyFxRpc(beat++, total > 0f);
                yield return new WaitForSeconds(LitanyTick);
            }
            if (!health.IsAlive) yield break;

            float amen = SwingDamage * AmenMultiplier * DamageMultiplier;
            float dealt = 0f;
            foreach (var h in AbilityKit.Blast(transform.position, AmenRadius, 0f, NetworkObjectId, "Amen"))
            {
                if (h == null || !h.IsAlive) continue;
                float before = h.Current.Value;
                h.Tag("Amen").ServerDamage(amen, transform.position, NetworkObjectId);
                dealt += Mathf.Max(0f, before - h.Current.Value);
                AbilityKit.Stun(h, AmenStun);
            }
            AfterHits(dealt);
            AmenFxRpc(transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LitanyFxRpc(int beat, bool hit)
        {
            SwingTrail.Spawn(transform, LitanyRadius * 0.9f, 360f, beat % 2 == 0 ? 1f : -1f, player.TrailColor(new Color(1f, 0.3f, 0.25f)));
            if (!IsOwner) player.Rig?.Play(RigAction.Swing, LitanyTick);

            Vfx.Ring(transform.position + Vector3.up * 0.06f, LitanyRadius, new Color(1f, 0.22f, 0.16f) * (hit ? 1f : 0.7f), LitanyTick * 0.9f);
            if (beat % 3 == 0) Vfx.Embers(transform.position + Vector3.up * 0.4f, 4, Blood, 3f, 0.04f, 0.4f);

            if (beat == 0) ClassFx.FlagLitany(transform, LitanyRadius, LitanySeconds);
            if (beat % 2 == 0) Sfx.Play(Sound.Swing, transform.position + Vector3.up, 0.6f, 0.7f + (beat % 3) * 0.06f);
            if (beat % 4 == 0) Sfx.Play(Sound.Chain, transform.position + Vector3.up, 0.5f, 0.9f);
            if (hit)
            {
                Vfx.Embers(transform.position + Vector3.up * 0.9f, 6, Blood, 4f, 0.05f, 0.4f);
                if (IsOwner) player.ConfirmHit(0.5f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AmenFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.7f);
            Sfx.Play(Sound.Toll, at, 0.6f, 1.2f);
            Vfx.Ring(at + Vector3.up * 0.05f, AmenRadius, new Color(1f, 0.3f, 0.22f), 0.55f);
            Vfx.Embers(at + Vector3.up * 0.3f, 26, Blood, 6f, 0.06f, 0.8f);
            Vfx.AbilityFlash(at + Vector3.up, new Color(1f, 0.3f, 0.2f), 6f, 10f, 0.4f);

            ClassFx.FlagCrater(at, AmenRadius, 1.6f);
            if (IsOwner) { player.ConfirmHit(2f); player.AddShake(0.4f); }
        }

        private static readonly Color Blood = new Color(0.85f, 0.08f, 0.06f);

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingFxRpc(float swingSide, bool hit, bool crash, Vector3 crashAt)
        {
            SwingTrail.Spawn(transform, SwingRange * 0.85f, SwingArc, swingSide, player.TrailColor(new Color(1f, 0.55f, 0.45f) * 1.05f));

            if (!IsOwner || NetworkPlayer.DebugShowSelf) ClassFx.FlagCut(transform, swingSide, SwingRange, SwingArc, player.TrailColor(new Color(1f, 0.25f, 0.18f)));
            if (hit) ClassFx.FlailBite(transform.position + transform.forward * 1.5f + Vector3.up * 1.1f, transform.position + Vector3.up * 1.2f);
            if (!IsOwner)
            {
                player.Rig?.Play(RigAction.Swing, SwingStrike + 0.2f);
                Sfx.Play(Sound.Swing, transform.position, 0.7f, 0.75f);
            }
            if (crash)
            {
                Sfx.Play(Sound.Slam, crashAt, 0.9f, 1.1f);
                Vfx.Embers(crashAt + Vector3.up * 0.2f, 10, new Color(0.7f, 0.6f, 0.5f), 4f, 0.05f, 0.5f);

                ClassFx.FlagCrater(crashAt, CrashRadius, 0.6f);
                if (IsOwner) { player.ConfirmHit(1.8f); player.AddShake(0.22f); }
            }
            if (hit)
            {
                Sfx.Play(Sound.Hit, transform.position + transform.forward * 1.5f + Vector3.up, 1f, 0.8f);

                if (IsOwner && !crash) { player.ConfirmHit(1.5f); player.AddShake(0.12f); Sfx.Play(Sound.Chain, transform.position + transform.forward * 1.4f + Vector3.up, 0.55f, 0.7f); }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ScourgeFxRpc()
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Lash, 0.45f);
            Sfx.Play(Sound.Hit, transform.position + Vector3.up * 1.2f, 0.9f, 1.4f);
            Sfx.Play(Sound.Hurt, transform.position + Vector3.up * 1.2f, 0.6f, 0.8f);

            ClassFx.FlagScourge(transform, IsOwner && !NetworkPlayer.DebugShowSelf);
            FxSprite.Decal("dirt_02", transform.position, 0.7f, 0.9f, 5f, new Color(0.3f, 0.03f, 0.02f, 0.6f));
            Vfx.AbilityFlash(transform.position + Vector3.up * 1.2f, new Color(1f, 0.25f, 0.18f), 3f, 4f, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChainFxRpc(Vector3 from, Vector3 to, bool dragged, ulong targetId, float haulFor)
        {
            Sfx.Play(Sound.Chain, from, 0.9f, 1.1f);
            Sfx.Play(Sound.Hit, to, 0.8f, 0.9f);
            Transform toT = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var tobj) ? tobj.transform : null;
            ChainLink.Spawn(from, to, ChainLink.ReachSeconds + haulFor + 0.1f, transform, toT);

            ClassFx.FlagHook(from, to);
            Vfx.HitImpact(to, from, 0.8f, new Color(1f, 0.65f, 0.4f), Mat.Steel);
            if (dragged) Sfx.Play(Sound.Chain, to, 0.7f, 0.8f);
            if (!IsOwner) player.Rig?.Play(RigAction.Hurl, 0.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapFxRpc(Vector3 at, float fervor)
        {
            float k = 1f + fervor / 100f;
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            Vfx.Embers(at + Vector3.up * 0.3f, Mathf.RoundToInt(14 * k), Blood, 5f * k, 0.06f, 0.7f);
            Vfx.AbilityFlash(at + Vector3.up, new Color(1f, 0.3f, 0.2f), 4f * k, 8f, 0.35f);

            ClassFx.FlagCrater(at, LeapRadius, k);
            Vfx.Smoke(at, 3, LeapRadius * 0.4f, 1.4f);
            if (IsOwner) player.ConfirmHit(1.5f);
        }
    }
}
