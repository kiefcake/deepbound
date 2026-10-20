using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// The Cleric's kit: keep everyone standing.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Grace.</b> Revives teammates twice as fast (in PlayerLife).</item>
    /// <item><b>LMB — Smite.</b> Golden bolts; modest damage so the healer can still contribute.</item>
    /// <item><b>RMB — Mending Beam.</b> Hold on a teammate under your crosshair to heal them
    ///   continuously; with no one targeted it heals you at a third of the rate. Costs mana
    ///   per second — the Cleric's whole game is spending mana well.</item>
    /// <item><b>Q — Consecrate.</b> Holy ground at your feet for six seconds: allies in it
    ///   heal, enemies in it burn. Drop it where the tank is holding the line.</item>
    /// <item><b>E — Divine Aegis.</b> Every ally within 12m gains a shield that soaks the next
    ///   60 damage (scaled by WIS) for eight seconds. Cast it before the boss slam lands.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class ClericKit : NetworkBehaviour, IClassKit
    {
        public const float SmiteInterval = 0.55f, SmiteDamage = 18f, SmiteSpeed = 28f, SmiteMana = 4f, SmiteRange = 18f;
        public float AttackRange => SmiteRange * (progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;
        private float BeamReach => BeamRange * RangeMul;
        public const float BeamRange = 16f, BeamHealPerSec = 22f, BeamManaPerSec = 11f;
        public const float ConsecrateCooldown = 14f, ConsecrateMana = 30f, ConsecrateRadius = 4f, ConsecrateSeconds = 6f;
        public const float ConsecrateHealPerSec = 9f, ConsecrateBurnPerSec = 12f;
        public const float AegisCooldown = 22f, AegisMana = 45f, AegisRadius = 12f, AegisAmount = 60f, AegisSeconds = 8f;

        /// <summary>Who the beam is on: a player's NetworkObjectId, the Cleric itself, or 0 for off. Owner-written.</summary>
        public readonly NetworkVariable<ulong> BeamTarget = new NetworkVariable<ulong>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        public float Mana { get; private set; }
        private bool manaInitialised;
        private float nextSmite, consecrateReadyAt, aegisReadyAt;
        private float lastServerSmite;
        private BeamVisual beam;

        private struct Zone { public Vector3 At; public float Until; }
        private readonly List<Zone> zones = new List<Zone>();
        private float zoneTick;

        private static readonly List<ClericKit> AllClerics = new List<ClericKit>();

        /// <summary>Is this point inside anyone's consecrated ground? (Blessed: +20% damage.)</summary>
        public static bool InConsecration(Vector3 at)
        {
            foreach (var c in AllClerics)
            {
                if (c == null) continue;
                foreach (var z in c.zones)
                    if (Time.time <= z.Until && Vector3.Distance(z.At, at) <= c.ZoneRadius) return true;
            }
            return false;
        }

        /// <summary>Is this player held in a Cleric's Mending Beam? (Inspired: +15% damage.)</summary>
        public static bool IsInspired(Health ally)
        {
            if (ally == null) return false;
            foreach (var c in AllClerics)
                if (c != null && c.Active && c != ally.GetComponent<ClericKit>() && c.BeamTarget.Value == ally.NetworkObjectId && c.life.CanAct) return true;
            return false;
        }

        private void OnEnable() => AllClerics.Add(this);
        private void OnDisable() => AllClerics.Remove(this);

        /// <summary>Consecrate's radius (grows with its rank).</summary>
        public float ZoneRadius => ConsecrateRadius * (progress != null && progress.Twist(AbilitySlot.Q) ? 1.5f : 1f);

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Cleric;
        private float ManaMax => progress.Value(StatType.MP);
        private float ManaRegen => 5f + progress.Value(StatType.WIS) * 0.18f;
        /// <summary>WIS 50 is the reference; healing and shields scale ±1% per point.</summary>
        private float Potency => 1f + (progress.Value(StatType.WIS) - 50f) / 100f;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Cleric;
        public string ResourceName => "Mana";
        public Color ResourceColor => new Color(0.95f, 0.8f, 0.35f);
        public float Resource => Mana;
        public float ResourceMax => ManaMax;
        public void RestoreResource(float fraction) => Mana = Mathf.Min(ResourceMax, Mana + ResourceMax * fraction);

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = "Smite", Unaffordable = Mana < SmiteMana });
            into.Add(new AbilityView { Key = "RMB", Name = "Mend", Active = BeamTarget.Value != 0, Unaffordable = Mana < 2f });
            into.Add(new AbilityView { Key = "Q", Name = "Consecrate", Remaining = consecrateReadyAt - Time.time,
                Cooldown = ConsecrateCooldown * progress.CooldownFor(AbilitySlot.Q), Unaffordable = Mana < ConsecrateMana });
            into.Add(new AbilityView { Key = "E", Name = "Aegis", Remaining = aegisReadyAt - Time.time,
                Cooldown = AegisCooldown * progress.CooldownFor(AbilitySlot.E), Unaffordable = Mana < AegisMana });
        }

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer)
            {
                ServerBeamTick();
                ServerZoneTick();
            }
            DrawBeam();

            if (!IsOwner) return;
            if (!Active)
            {
                if (BeamTarget.Value != 0) BeamTarget.Value = 0;
                return;
            }

            if (!manaInitialised) { Mana = ManaMax; manaInitialised = true; }
            motor.SpeedMultiplier = progress.SpeedMultiplier;
            motor.CanSprint = true;

            bool canAct = life.CanAct && motor.ActionsReady;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // --- RMB held: Mending Beam ---
            ulong wantBeam = 0;
            if (canAct && mouse != null && mouse.rightButton.isPressed && Mana > 0.5f)
            {
                var ally = AllyUnderCrosshair();
                wantBeam = ally != null ? ally.NetworkObjectId : NetworkObjectId;
                Mana = Mathf.Max(0f, Mana - BeamManaPerSec * Time.deltaTime);
            }
            else
            {
                Mana = Mathf.Min(ManaMax, Mana + ManaRegen * Time.deltaTime);
            }
            if (BeamTarget.Value != wantBeam) BeamTarget.Value = wantBeam;

            if (!canAct) return;

            // --- LMB held: Smite ---
            if (wantBeam == 0 && mouse != null && mouse.leftButton.isPressed && Time.time >= nextSmite && Mana >= SmiteMana)
            {
                Mana -= SmiteMana;
                nextSmite = Time.time + SmiteInterval * progress.AttackTimeMultiplier;
                player.ViewModelCast(false);
                SmiteRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward);
            }

            // --- Q: Consecrate ---
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame && Time.time >= consecrateReadyAt && Mana >= ConsecrateMana)
            {
                Mana -= ConsecrateMana;
                consecrateReadyAt = Time.time + ConsecrateCooldown * progress.CooldownFor(AbilitySlot.Q);
                player.ViewModelCast(true);
                ConsecrateRpc();
            }

            // --- E: Divine Aegis ---
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && Time.time >= aegisReadyAt && Mana >= AegisMana)
            {
                Mana -= AegisMana;
                aegisReadyAt = Time.time + AegisCooldown * progress.CooldownFor(AbilitySlot.E) * (progress.Mastered(2) ? 0.7f : 1f);
                player.ViewModelCast(true);
                AegisRpc();
            }
        }

        /// <summary>The teammate nearest the centre of the screen, within beam range and sight.</summary>
        private PlayerLife AllyUnderCrosshair()
        {
            PlayerLife best = null;
            float bestAngle = 9f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p == life || p.Downed.Value) continue;
                var to = p.Health.Center - player.AimOrigin;
                if (to.magnitude > BeamReach) continue;
                float angle = Vector3.Angle(player.AimForward, to);
                if (angle < bestAngle && CombatQuery.ClearLine(player.AimOrigin, p.Health)) { bestAngle = angle; best = p; }
            }
            return best;
        }

        // ==================================================================
        // Server
        // ==================================================================

        private void ServerBeamTick()
        {
            ulong id = BeamTarget.Value;
            if (id == 0 || !life.CanAct || !Active) return;

            Health target = null;
            if (id == NetworkObjectId) target = health;
            else if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)) target = obj.GetComponent<Health>();
            if (target == null || target.Team != Team.Players || !target.IsAlive) return;
            if (target != health && Vector3.Distance(target.transform.position, transform.position) > BeamReach + 2f) return;

            // Shepherd: stronger beam, and healing past full becomes a shield.
            float rate = BeamHealPerSec * Potency * (target == health ? 0.33f : 1f) * (progress.OnPath(2) ? 1.4f : 1f);
            if (progress.OnPath(2) && target.Current.Value >= target.Max.Value - 0.01f)
                target.ServerShield(Mathf.Min(40f, target.Shield.Value + rate * Time.deltaTime), 4f);
            target.ServerHeal(rate * Time.deltaTime);
            Enemies.EnemyBrain.ServerHealThreat(health, rate * Time.deltaTime);
        }

        [Rpc(SendTo.Server)]
        private void SmiteRpc(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct || Time.time - lastServerSmite < SmiteInterval * 0.5f) return;
            lastServerSmite = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            if (progress.OnPath(1))
            {
                // Zealot: harder smites that mend the most wounded ally in reach.
                float dmg = SmiteDamage * progress.AttackMultiplier * 1.35f;
                ulong me = NetworkObjectId;
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, SmiteSpeed, h =>
                {
                    h.ServerDamage(dmg, origin, me);
                    if (this == null) return;
                    Health worst = null;
                    foreach (var ally in CombatQuery.Circle(transform.position, 12f, Team.Players))
                        if (ally.IsAlive && (worst == null || ally.Fraction < worst.Fraction)) worst = ally;
                    if (worst != null) worst.ServerHeal(dmg * 0.4f * Potency);
                }, false, SmiteRange * RangeMul);
            }
            else Arrows.ServerFire(origin, direction, SmiteDamage * progress.AttackMultiplier, Team.Enemies, NetworkObjectId, SmiteSpeed, null, false, SmiteRange * RangeMul);
            SmiteFxRpc(origin, direction);
        }

        [Rpc(SendTo.Server)]
        private void ConsecrateRpc() => ServerConsecrate();

        /// <summary>Server: lay holy ground at your feet. Public for the smoke test.</summary>
        public void ServerConsecrate()
        {
            if (!life.CanAct) return;
            zones.Add(new Zone { At = transform.position, Until = Time.time + (progress.Twist(AbilitySlot.Q) ? 9f : ConsecrateSeconds) });
            ConsecrateFxRpc(transform.position);
        }

        private void ServerZoneTick()
        {
            if (zones.Count == 0) return;
            zoneTick += Time.deltaTime;
            if (zoneTick < 0.5f) return;
            float dt = zoneTick;
            zoneTick = 0f;

            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var z = zones[i];
                if (Time.time > z.Until) { zones.RemoveAt(i); continue; }
                foreach (var ally in CombatQuery.Circle(z.At, ZoneRadius, Team.Players).ToArray())
                    ally.ServerHeal(ConsecrateHealPerSec * Potency * progress.Potency(AbilitySlot.Q) * dt);
                foreach (var foe in CombatQuery.Circle(z.At, ZoneRadius, Team.Enemies).ToArray())
                    foe.ServerDamage(ConsecrateBurnPerSec * progress.AttackMultiplier * progress.Potency(AbilitySlot.Q) * dt * (progress.Mastered(1) ? 2f : 1f), z.At, NetworkObjectId);
            }
        }

        [Rpc(SendTo.Server)]
        private void AegisRpc() => ServerAegis();

        /// <summary>Server: shield everyone nearby. Public for the smoke test.</summary>
        public void ServerAegis()
        {
            if (!life.CanAct) return;
            foreach (var ally in CombatQuery.Circle(transform.position, AegisRadius, Team.Players))
            {
                ally.ServerShield(AegisAmount * Potency * progress.Potency(AbilitySlot.E) * (progress.Mastered(2) ? 1.6f : 1f), AegisSeconds);
                if (progress.Twist(AbilitySlot.E)) ally.ServerHeal(ally.Max.Value * 0.25f);
            }
            AegisFxRpc();
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void SmiteFxRpc(Vector3 origin, Vector3 direction)
        {
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Smite, SmiteSpeed, SmiteRange * RangeMul);
            Sfx.Play(Sound.Frost, origin, 0.45f, 0.7f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ConsecrateFxRpc(Vector3 at)
        {
            HolyGround.Spawn(at, ConsecrateRadius, ConsecrateSeconds);
            Sfx.Play(Sound.Revive, at + Vector3.up, 0.9f, 0.85f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AegisFxRpc()
        {
            Sfx.Play(Sound.Relic, transform.position + Vector3.up, 0.9f, 1.2f);
            var gold = new Color(1f, 0.85f, 0.45f);
            Vfx.Ring(transform.position, AegisRadius, gold, 0.5f);
            Vfx.Flash(transform.position + Vector3.up * 1.5f, gold, 4f, 10f, 0.5f);
            Vfx.Embers(transform.position + Vector3.up * 0.5f, 16, gold, 3.5f, 0.06f, 0.9f);
            player.Rig?.Play(RigAction.Cast, 0.6f);
        }

        /// <summary>Every peer draws the beam from its Cleric's hand to the target.</summary>
        private void DrawBeam()
        {
            ulong id = BeamTarget.Value;
            Transform to = null;
            if (id != 0 && id != NetworkObjectId && NetworkManager.SpawnManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)) to = obj.transform;

            if (to == null)
            {
                if (beam != null) beam.gameObject.SetActive(false);
                return;
            }
            if (beam == null) beam = BeamVisual.Create();
            beam.gameObject.SetActive(true);
            var from = IsOwner ? player.AimOrigin + player.AimForward * 0.5f - Vector3.up * 0.25f : transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
            beam.Set(from, to.position + Vector3.up * 1.1f);
        }

        public override void OnDestroy()
        {
            if (beam != null) Destroy(beam.gameObject);
            base.OnDestroy();
        }
    }
}
