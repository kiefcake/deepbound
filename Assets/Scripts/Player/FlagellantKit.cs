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
    /// <summary>
    /// The Flagellant (2026-10-20): a penitent bruiser who spends his own blood for power.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Mortification.</b> The more wounded you are, the harder you hit (up to
    ///   +40% near death), and every hit heals you for a share of it (Penance).</item>
    /// <item><b>LMB — Flail.</b> A heavy swinging arc; every third swing is an overhead CRASH that
    ///   hits harder and bursts round its landing.</item>
    /// <item><b>RMB — Scourge.</b> Lash yourself: lose a tenth of your health, gain Fervor and
    ///   ZEAL (faster, harder swings) for a few seconds.</item>
    /// <item><b>Q — Chains of Penance.</b> Hurl a chain at the enemy under the crosshair and drag
    ///   it to you, stunned. Too heavy to drag (bosses, brutes): it drags you to them.</item>
    /// <item><b>E — Penitent Leap.</b> Leap at the crosshair and crash down: a ring of damage and a
    ///   stun, fed by every point of Fervor you hold.</item>
    /// </list>
    /// Fervor (the resource) builds from landing hits and from being hurt, and drains when you stop.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class FlagellantKit : NetworkBehaviour, IClassKit
    {
        public const float SwingWindup = 0.2f, SwingStrike = 0.14f, SwingRecovery = 0.36f;
        public const float SwingRange = 2.6f, SwingArc = 120f, SwingDamage = 22f; // 27 -> 22 (2026-10-20 class DPS table: it out-hit the Rogue from the front at full health)
        public const int CrashEvery = 3;
        public const float CrashMultiplier = 1.6f, CrashRadius = 1.7f;
        public const float ScourgeCooldown = 6f, ScourgeCost = 0.1f, ScourgeFervor = 30f, ZealSeconds = 5f, ZealAttackTime = 0.7f, ZealDamage = 1.15f;
        public const float ChainCooldown = 9f, ChainRange = 16f, ChainStun = 0.9f, ChainDamage = 14f;
        public const float LeapCooldown = 12f, LeapRange = 12f, LeapDamage = 40f, LeapRadius = 3.5f, LeapStun = 1f;
        public const float FervorMax = 100f, FervorPerHit = 8f, FervorDecay = 6f;
        public const float MortificationMax = 0.4f, Penance = 0.04f;

        public float AttackRange => SwingRange * (progress != null ? progress.ReachMultiplier : 1f);

        /// <summary>Server-written: Fervor (synced so the owner's HUD shows it).</summary>
        public readonly NetworkVariable<float> Fervor = new NetworkVariable<float>();
        /// <summary>Server time Zeal ends.</summary>
        public readonly NetworkVariable<double> ZealEnds = new NetworkVariable<double>();
        /// <summary>Server time Martyrdom (the ultimate) ends.</summary>
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

        // ---- IClassKit ----

        public ClassId Class => ClassId.Flagellant;
        public string ResourceName => "Fervor";
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
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) Health.ServerAnyDamaged -= OnAnyDamaged;
        }

        /// <summary>Martyrdom: you cannot fall while it burns.</summary>
        private float ModifyIncoming(float dmg, Vector3 from)
        {
            if (!Active || !Martyr) return dmg;
            return Mathf.Min(dmg, Mathf.Max(0f, health.Current.Value - 1f));
        }

        private void OnAnyDamaged(Health t, float dealt, ulong from)
        {
            if (this == null || !Active || dealt <= 0f) return;
            // Pain feeds Fervor.
            if (t == health) Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + dealt / Mathf.Max(1f, health.Max.Value) * 60f);
        }

        // ==================================================================
        // Owner
        // ==================================================================

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

            // --- LMB: Flail ---
            if (canAct && UI.Binds.PrimaryHeld && Time.time >= swingBusyUntil)
            {
                side = -side;
                swingPending = true;
                swingStrikeAt = Time.time + SwingWindup;
                float speed = progress.AttackTimeMultiplier * (Zealous ? ZealAttackTime : 1f);
                swingBusyUntil = Time.time + SwingWindup + SwingStrike + SwingRecovery * speed;
                player.ViewModelSwing(SwingWindup, SwingStrike + SwingRecovery * speed, side);
                Sfx.Play(Sound.Swing, transform.position, 0.7f, 0.75f);
                // Weave: a foe just out of reach is stepped to with the swing (MeleeStep).
                MeleeStep.TryStepIn(motor, transform, player.AimForward, SwingRange * progress.ReachMultiplier, 10f);
            }
            if (swingPending && Time.time >= swingStrikeAt)
            {
                swingPending = false;
                SwingRpc(player.AimOrigin, player.AimForward, side);
            }

            // --- RMB: Scourge ---
            if (canAct && UI.Binds.SecondaryDown && Time.time >= scourgeReadyAt && health.Fraction >= 0.25f)
            {
                scourgeReadyAt = Time.time + ScourgeCooldown;
                player.ViewModelBash();
                ScourgeRpc();
            }

            // --- Q: Chains of Penance ---
            if (canAct && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= chainReadyAt)
            {
                var target = AimedEnemy(ChainRange * progress.Potency(AbilitySlot.Q) * progress.RangeMultiplier);
                if (target == null) UI.GameHud.Feed("No enemy in reach of the chain", new Color(0.8f, 0.75f, 0.7f));
                else
                {
                    chainReadyAt = Time.time + ChainCooldown * progress.CooldownFor(AbilitySlot.Q);
                    player.ViewModelCast(false);
                    ChainRpc(target.NetworkObjectId);
                }
            }

            // --- E: Penitent Leap ---
            if (canAct && UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= leapReadyAt)
            {
                leapReadyAt = Time.time + LeapCooldown * progress.CooldownFor(AbilitySlot.E);
                var to = LeapTarget();
                var flat = to - transform.position;
                flat.y = 0f;
                float dist = Mathf.Min(flat.magnitude, LeapRange);
                // Impulse decays at 40 m/s^2, so this carries the leap about `dist` metres.
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

        // ==================================================================
        // Server
        // ==================================================================

        /// <summary>Mortification, Fervor, Zeal and Martyrdom on top of ATT.</summary>
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

        /// <summary>Server: one flail swing (the third is a crash). Public for the smoke test.</summary>
        public void ServerSwing(Vector3 origin, Vector3 forward, float swingSide)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;
            swings++;
            bool crash = swings % CrashEvery == 0;
            float range = SwingRange * progress.ReachMultiplier;
            float dmg = SwingDamage * DamageMultiplier * (crash ? CrashMultiplier * (progress.OnPath(1) ? 1.3f : 1f) : 1f);
            float total = 0f;
            var hit = new HashSet<Health>();
            foreach (var target in CombatQuery.Arc(origin, forward, range, SwingArc, Team.Enemies))
            {
                var zone = HitZones.Aimed(target, origin, forward, range, out var zonePoint);
                total += target.Tag("FlagellantKit").ServerDamage(dmg * HitZones.Multiplier(target, zone, false) * Combos.TryShatter(target, progress), transform.position, NetworkObjectId);
                if (zone != HitZone.Body) Arrows.ReportZone(NetworkObjectId, zonePoint, zone);
                hit.Add(target);
            }
            Vector3 crashAt = transform.position + Flat(forward) * (range * 0.75f);
            if (crash)
            {
                // The overhead crash bursts where it lands: Zealots' wider, and stunning at mastery.
                float radius = CrashRadius * (progress.OnPath(1) ? 1.5f : 1f);
                foreach (var target in CombatQuery.Circle(crashAt, radius, Team.Enemies))
                {
                    if (!hit.Contains(target)) total += target.Tag("FlagellantKit").ServerDamage(dmg * 0.6f, crashAt, NetworkObjectId);
                    if (progress.Mastered(1) && target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(0.8f);
                }
            }
            if (total > 0f) Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + FervorPerHit * (crash ? 1.5f : 1f));
            AfterHits(total);
            SwingFxRpc(swingSide, total > 0f, crash, crashAt);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.forward; }

        [Rpc(SendTo.Server)]
        private void ScourgeRpc() => ServerScourge();

        /// <summary>Server: lash yourself for Fervor and Zeal. Public for the smoke test.</summary>
        public void ServerScourge()
        {
            if (!life.CanAct || health.Fraction < 0.25f) return;
            float cost = health.Max.Value * ScourgeCost;
            health.ServerSetFraction((health.Current.Value - cost) / health.Max.Value);
            Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + ScourgeFervor);
            ZealEnds.Value = Now + ZealSeconds;
            lastHitAt = Time.time;
            // Penitent mastery: the blood you shed mends the faithful round you.
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

        /// <summary>Server: the chain. Drags the target to you, or you to it if it is too heavy. Public for the smoke test.</summary>
        public bool ServerChain(Health target)
        {
            float potency = progress.Potency(AbilitySlot.Q);
            if (Vector3.Distance(target.transform.position, transform.position) > ChainRange * potency * progress.RangeMultiplier + 2f) return false;
            var from = transform.position;
            var toMe = Flat(target.transform.position - from);
            var spot = from + toMe * 1.6f;
            bool dragged = target.TryGetComponent<Enemies.EnemyBrain>(out var brain) && brain.ServerYank(spot);
            target.Tag("FlagellantKit").ServerDamage(ChainDamage * DamageMultiplier * potency, from, NetworkObjectId);
            if (target.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(ChainStun * (dragged ? 1f : 0.6f));
            // Rank 3: everything near the target comes along too.
            if (progress.Twist(AbilitySlot.Q))
                foreach (var other in CombatQuery.Circle(target.transform.position, 3f, Team.Enemies).ToArray())
                    if (other != target && other.TryGetComponent<Enemies.EnemyBrain>(out var ob))
                    {
                        ob.ServerYank(spot + Random.insideUnitSphere.normalized * 1.2f);
                        if (other.TryGetComponent<ICrowdControl>(out var occ)) occ.ServerStun(ChainStun * 0.7f);
                    }
            Fervor.Value = Mathf.Min(FervorMax, Fervor.Value + FervorPerHit);
            lastHitAt = Time.time;
            ChainFxRpc(transform.position + Vector3.up * 1.3f, target.Center, dragged);
            if (!dragged) DragMeRpc(target.transform.position - toMe * 1.8f);
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
            StartCoroutine(LeapCrash());
        }

        private IEnumerator LeapCrash()
        {
            yield return new WaitForSeconds(0.6f);
            ServerLeapCrash(transform.position);
        }

        /// <summary>Server: the landing. Spends every point of Fervor for damage. Public for the smoke test.</summary>
        public void ServerLeapCrash(Vector3 at)
        {
            float potency = progress.Potency(AbilitySlot.E);
            float fervor = Fervor.Value;
            Fervor.Value = 0f;
            float dmg = LeapDamage * DamageMultiplier * potency * (1f + fervor / 100f);
            bool twist = progress.Twist(AbilitySlot.E);
            float total = 0f;
            foreach (var target in CombatQuery.Circle(at, LeapRadius, Team.Enemies).ToArray())
            {
                total += target.Tag("FlagellantKit").ServerDamage(dmg, at, NetworkObjectId);
                if (target.TryGetComponent<ICrowdControl>(out var cc))
                {
                    cc.ServerStun(LeapStun * (twist ? 1.5f : 1f));
                    if (twist) cc.ServerKnockback(Flat(target.transform.position - at) * 7f);
                }
            }
            AfterHits(total);
            LeapFxRpc(at, fervor);
        }

        /// <summary>Server: the ultimate, Martyrdom.</summary>
        public void ServerMartyrdom(float seconds)
        {
            MartyrEnds.Value = Now + seconds;
            Fervor.Value = FervorMax;
            StartCoroutine(MartyrEnd(seconds));
        }

        private IEnumerator MartyrEnd(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (health.IsAlive) health.ServerHeal(health.Max.Value * 0.3f);
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        private static readonly Color Blood = new Color(0.85f, 0.08f, 0.06f);

        [Rpc(SendTo.ClientsAndHost)]
        private void SwingFxRpc(float swingSide, bool hit, bool crash, Vector3 crashAt)
        {
            SwingTrail.Spawn(transform, SwingRange * 0.85f, SwingArc, swingSide, player.TrailColor(new Color(1f, 0.55f, 0.45f) * 1.05f));
            if (!IsOwner)
            {
                player.Rig?.Play(RigAction.Swing, SwingStrike + 0.2f);
                Sfx.Play(Sound.Swing, transform.position, 0.7f, 0.75f);
            }
            if (crash)
            {
                Sfx.Play(Sound.Slam, crashAt, 0.9f, 1.1f);
                Vfx.Ring(crashAt + Vector3.up * 0.05f, CrashRadius * (progress.OnPath(1) ? 1.5f : 1f), new Color(1f, 0.5f, 0.35f), 0.35f);
                Vfx.Embers(crashAt + Vector3.up * 0.2f, 10, new Color(0.7f, 0.6f, 0.5f), 4f, 0.05f, 0.5f);
                if (IsOwner) { player.ConfirmHit(1.8f); player.AddShake(0.22f); }
            }
            if (hit)
            {
                Sfx.Play(Sound.Hit, transform.position + transform.forward * 1.5f + Vector3.up, 1f, 0.8f);
                // The flail lands HEAVY (2026-11-13, the user: "i want to feel the flagellents swing more"): a harder
                // kick and a shake, a thud of chain under the hit.
                if (IsOwner && !crash) { player.ConfirmHit(1.5f); player.AddShake(0.12f); Sfx.Play(Sound.Chain, transform.position + transform.forward * 1.4f + Vector3.up, 0.55f, 0.7f); }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ScourgeFxRpc()
        {
            Sfx.Play(Sound.Hit, transform.position + Vector3.up * 1.2f, 0.9f, 1.4f);
            Sfx.Play(Sound.Hurt, transform.position + Vector3.up * 1.2f, 0.6f, 0.8f);
            Vfx.Embers(transform.position + Vector3.up * 1.3f - transform.forward * 0.2f, 14, Blood, 3f, 0.05f, 0.6f);
            if (IsOwner) UI.GameHud.ShowCombo(transform.position + Vector3.up * 2f + transform.forward, "ZEAL", new Color(1f, 0.35f, 0.25f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChainFxRpc(Vector3 from, Vector3 to, bool dragged)
        {
            Sfx.Play(Sound.Chain, from, 0.9f, 1.1f);
            Sfx.Play(Sound.Hit, to, 0.8f, 0.9f);
            ChainLink.Spawn(from, to, 0.35f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapFxRpc(Vector3 at, float fervor)
        {
            float k = 1f + fervor / 100f;
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            Vfx.Ring(at + Vector3.up * 0.05f, LeapRadius, new Color(1f, 0.35f, 0.25f), 0.45f);
            Vfx.Embers(at + Vector3.up * 0.3f, Mathf.RoundToInt(14 * k), Blood, 5f * k, 0.06f, 0.7f);
            Vfx.Flash(at + Vector3.up, new Color(1f, 0.3f, 0.2f), 4f * k, 8f, 0.35f);
            // The landing cracks the floor and leaves blood on it.
            FxSprite.Decal("dirt_02", at, LeapRadius * 1.3f, LeapRadius * 1.4f, 6f, new Color(0.25f, 0.03f, 0.02f, 0.6f));
            if (IsOwner) player.ConfirmHit(1.5f);
        }
    }
}
