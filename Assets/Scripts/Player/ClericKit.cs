using System.Collections.Generic;
using System.Linq;
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
    [RequireComponent(typeof(Health))]
    public class ClericKit : NetworkBehaviour, IClassKit
    {
        public const float SmiteInterval = 0.55f, SmiteDamage = 21f, SmiteSpeed = 28f, SmiteMana = 0f, SmiteRange = 18f;
        public float AttackRange => SmiteRange * (progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;
        private float BeamReach => BeamRange * RangeMul;
        public const float BeamRange = 16f, BeamHealPerSec = 22f, BeamManaPerSec = 16f;
        public const float ConsecrateCooldown = 10f, ConsecrateMana = 42f, ConsecrateRadius = 4f, ConsecrateSeconds = 6f;
        public const float ConsecrateHealPerSec = 9f, ConsecrateBurnPerSec = 12f;
        public const float AegisCooldown = 16f, AegisMana = 60f, AegisRadius = 12f, AegisAmount = 60f, AegisSeconds = 8f;

        public ulong DebugBeamTarget { get; set; }

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
        private float nextMendMote;

        private struct Zone { public Vector3 At; public float Until, NextPillar; public bool Follow; }
        private readonly List<Zone> zones = new List<Zone>();
        private float zoneTick;

        private static readonly List<ClericKit> AllClerics = new List<ClericKit>();

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

        public static bool IsInspired(Health ally)
        {
            if (ally == null) return false;
            foreach (var c in AllClerics)
                if (c != null && c.Active && c != ally.GetComponent<ClericKit>() && c.BeamTarget.Value == ally.NetworkObjectId && c.life.CanAct) return true;
            return false;
        }

        private void OnEnable() => AllClerics.Add(this);
        private void OnDisable() => AllClerics.Remove(this);

        public float ZoneRadius => ConsecrateRadius;

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
        private float ManaRegen => (2.5f + progress.Value(StatType.WIS) * 0.11f) * progress.ManaRegenScale;

        private float Potency => 1f + (progress.Value(StatType.WIS) - 50f) / 100f;

        public const float BenedictionCap = 0.2f, BenedictionSeconds = 6f;

        public static void Bless(Health h, float amount)
        {
            if (h == null || !h.IsAlive || amount <= 0f) return;
            float room = h.Max.Value - h.Current.Value;
            h.ServerHeal(amount);
            float over = amount - Mathf.Max(0f, room);
            if (over <= 0f) return;
            float cap = h.Max.Value * BenedictionCap;
            if (h.Shield.Value >= cap) return;
            h.ServerShield(Mathf.Min(cap, h.Shield.Value + over), BenedictionSeconds);
        }

        public ClassId Class => ClassId.Cleric;
        public string ResourceName => "Mana";
        public string ResourceHint => BeamTarget.Value != 0 ? "Beam active: no regeneration" : "Spent by Mending Beam and spells";
        public Color ResourceColor => new Color(0.95f, 0.8f, 0.35f);
        public float Resource => Mana;
        public float ResourceMax => ManaMax;
        public void RestoreResource(float fraction) => Mana = Mathf.Min(ResourceMax, Mana + ResourceMax * fraction);
        public void ShaveCooldowns(float seconds) { consecrateReadyAt -= seconds; aegisReadyAt -= seconds; }

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = "Smite", Unaffordable = Mana < SmiteMana });
            into.Add(new AbilityView { Key = "RMB", Name = "Mend", Active = BeamTarget.Value != 0, Unaffordable = Mana < 2f });
            into.Add(new AbilityView { Key = "Q", Name = "Consecrate", Remaining = consecrateReadyAt - Time.time,
                Cooldown = ConsecrateCooldown * progress.CooldownFor(AbilitySlot.Q), Unaffordable = Mana < ConsecrateMana });
            into.Add(new AbilityView { Key = "E", Name = "Aegis", Remaining = aegisReadyAt - Time.time,
                Cooldown = AegisCooldown * progress.CooldownFor(AbilitySlot.E), Unaffordable = Mana < AegisMana });
        }

        private void Update()
        {
            if (IsServer)
            {
                ServerBeamTick();
                ServerZoneTick();
                ServerTickPlant();
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

            ulong wantBeam = 0;
            if (canAct && UI.Binds.SecondaryHeld && Mana > 0.5f)
            {
                var ally = AllyUnderCrosshair();
                wantBeam = ally != null ? ally.NetworkObjectId : NetworkObjectId;
                Mana = Mathf.Max(0f, Mana - BeamManaPerSec * Time.deltaTime);
            }
            else
            {
                Mana = Mathf.Min(ManaMax, Mana + ManaRegen * Time.deltaTime);
            }
            if (DebugBeamTarget != 0) wantBeam = DebugBeamTarget;
            if (BeamTarget.Value != wantBeam) BeamTarget.Value = wantBeam;

            if (!canAct) return;

            if (wantBeam == 0 && UI.Binds.PrimaryHeld && Time.time >= nextSmite && Mana >= SmiteMana)
            {
                Mana -= SmiteMana;
                nextSmite = Time.time + SmiteInterval * progress.AttackTimeMultiplier * (progress.OnPath(1) ? HammerInterval : 1f);
                player.ViewModelCast(false);
                SmiteRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward);
            }

            if (UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= consecrateReadyAt && Mana >= ConsecrateMana)
            {
                Mana -= ConsecrateMana;
                consecrateReadyAt = Time.time + ConsecrateCooldown * progress.CooldownFor(AbilitySlot.Q);
                player.ViewModelCast(true);

                if (progress.B(AbilitySlot.Q, 1) && player.TryAimFloor(ThrowRange * RangeMul, out var aimed)) ConsecrateAtRpc(aimed);
                else ConsecrateRpc();
            }

            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= aegisReadyAt && Mana >= AegisMana)
            {
                Mana -= AegisMana;
                aegisReadyAt = Time.time + AegisCooldown * progress.CooldownFor(AbilitySlot.E);
                player.ViewModelCast(true);
                AegisRpc();
            }
        }

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

        private void ServerBeamTick()
        {
            ulong id = BeamTarget.Value;
            if (id == 0 || !life.CanAct || !Active) return;

            Health target = null;
            if (id == NetworkObjectId) target = health;
            else if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)) target = obj.GetComponent<Health>();
            if (target == null || target.Team != Team.Players || !target.IsAlive) return;
            if (target != health && Vector3.Distance(target.transform.position, transform.position) > BeamReach + 2f) return;

            float rate = BeamHealPerSec * Potency * (target == health ? 0.33f : 1f) * (progress.Mastered(2) ? 1.4f : 1f);
            if (progress.Mastered(2) && target.Current.Value >= target.Max.Value - 0.01f)
                target.ServerShield(Mathf.Min(40f, target.Shield.Value + rate * Time.deltaTime), 4f);
            Bless(target, rate * Time.deltaTime);
            Enemies.EnemyBrain.ServerHealThreat(health, rate * Time.deltaTime);
        }

        public const float LoneSmiteBonus = 1.35f;

        public const float HammerDamage = 1.8f, HammerRadius = 2.5f, HammerInterval = 1.6f, ShepherdHeal = 14f, PlantAfter = 1f, PlantedTaken = 0.7f;
        private float stillSince = -1f;

        public bool Planted => IsServer && progress != null && progress.OnPath(Classes.PathTriads.Path) && stillSince >= 0f && Time.time - stillSince >= PlantAfter;
        private bool wasPlanted;
        private float nextPlantMend;

        private void ServerTickPlant()
        {
            if (!progress.OnPath(Classes.PathTriads.Path) || !Active) { stillSince = -1f; wasPlanted = false; return; }
            bool still = life.ServerVelocity.sqrMagnitude < 0.25f && !life.Downed.Value;
            if (!still) stillSince = -1f;
            else if (stillSince < 0f) stillSince = Time.time;
            bool now = Planted;
            if (now != wasPlanted) { wasPlanted = now; PlantFxRpc(now); }
            if (now && progress.Mastered(Classes.PathTriads.Path) && Time.time >= nextPlantMend)
            {
                nextPlantMend = Time.time + 0.5f;
                foreach (var ally in CombatQuery.Circle(transform.position, 5f, Team.Players))
                    if (ally != health) Bless(ally, ally.Max.Value * 0.01f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlantFxRpc(bool planted)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Pray, 0.6f);
            if (!planted) return;
            var gold = new Color(1f, 0.85f, 0.45f);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 1.2f, gold, 0.5f);
            FxSprite.Decal("magic_03", transform.position, 1.6f, 1.8f, 1.2f, new Color(gold.r, gold.g, gold.b, 0.6f), 30f);
            if (IsOwner) UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "PLANTED", gold);
        }
        private static float LoneSmite => PlayerLife.All.Count <= 1 ? LoneSmiteBonus : 1f;

        [Rpc(SendTo.Server)]
        private void SmiteRpc(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct || Time.time - lastServerSmite < SmiteInterval * 0.5f) return;
            lastServerSmite = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            float smite = SmiteDamage * progress.AttackMultiplier * LoneSmite;
            ulong me = NetworkObjectId;
            if (progress.OnPath(1))
            {
                float dmg = smite * HammerDamage;
                bool stun = progress.Mastered(1);
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, SmiteSpeed * 0.8f, h => { }, false, SmiteRange * RangeMul, 0f, at =>
                {
                    if (this == null) return;
                    float dealt = 0f;
                    foreach (var h in CombatQuery.Circle(at, HammerRadius, Team.Enemies))
                    {
                        dealt += h.Tag("Hammer of Light").ServerDamage(dmg * (Vector3.Distance(h.transform.position, at) < 1.2f ? 1f : 0.6f), at, me);
                        if (stun) AbilityKit.Stun(h, 0.5f);
                    }
                    progress.AbilityBurstRpc(at, HammerRadius, PlayerProgress.FxKind.Holy);
                    Health worst = null;
                    foreach (var ally in CombatQuery.Circle(transform.position, 12f, Team.Players))
                        if (ally.IsAlive && (worst == null || ally.Fraction < worst.Fraction)) worst = ally;
                    if (worst != null && dealt > 0f) Bless(worst, dealt * 0.4f * Potency);
                });
            }
            else if (progress.OnPath(2))
            {
                Arrows.ServerFire(origin, direction, smite, Team.Enemies, me, SmiteSpeed, null, true, SmiteRange * RangeMul);
                var healed = new HashSet<Health>();
                Arrows.ServerFire(origin, direction, 0f, Team.Players, me, SmiteSpeed, h =>
                {
                    if (this == null || h == health || !healed.Add(h)) return;
                    Bless(h, ShepherdHeal * Potency);
                    progress.AbilityBurstRpc(h.Center, 0.6f, PlayerProgress.FxKind.Holy);
                }, true, SmiteRange * RangeMul);
            }
            else Arrows.ServerFire(origin, direction, smite, Team.Enemies, me, SmiteSpeed, null, false, SmiteRange * RangeMul);

            if (Planted)
            {
                var twin = Quaternion.AngleAxis(4f, Vector3.up) * direction;
                Arrows.ServerFire(origin + transform.right * 0.25f, twin, smite * 0.8f, Team.Enemies, me, SmiteSpeed, null, false, SmiteRange * RangeMul);
                SmiteFxRpc(origin + transform.right * 0.25f, twin);
            }
            SmiteFxRpc(origin, direction);
        }

        public const float ThrowRange = 16f, PillarEvery = 1.5f, HaloRadius = 5f, HaloSeconds = 4f;

        [Rpc(SendTo.Server)]
        private void ConsecrateRpc() => ServerConsecrate();

        [Rpc(SendTo.Server)]
        private void ConsecrateAtRpc(Vector3 at)
        {
            if (Vector3.Distance(at, transform.position) > ThrowRange * RangeMul + 3f) at = transform.position;
            ServerConsecrate(at);
        }

        public void ServerConsecrate() => ServerConsecrate(transform.position);

        public void ServerConsecrate(Vector3 at)
        {
            if (!life.CanAct) return;
            bool follow = progress.A(AbilitySlot.Q, 1);
            if (follow) at = transform.position;
            zones.Add(new Zone { At = at, Until = Time.time + ConsecrateSeconds, NextPillar = Time.time + 0.6f, Follow = follow });
            AbilityKit.ServerClearSanctuary(NetworkObjectId);

            if (progress.A(AbilitySlot.Q, 2))
            {
                AbilityKit.Blast(at, ZoneRadius, ConsecrateBurnPerSec * progress.AttackMultiplier, NetworkObjectId, "Searing Ground", h => { h.ServerScorch(4f, NetworkObjectId); AbilityKit.Stun(h, 1f); });
                progress.AbilityBurstRpc(at, ZoneRadius, PlayerProgress.FxKind.Fire);
            }
            ConsecrateFxRpc(at, follow);
        }

        private Vector3 ZoneAt(Zone z) => z.Follow ? transform.position : z.At;

        private void OnEnemyDied(Health dead)
        {
            if (!IsServer || !Active || !progress.B(AbilitySlot.Q, 4) || dead == null || dead.Team != Team.Enemies) return;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (Time.time > z.Until || Vector3.Distance(ZoneAt(z), dead.transform.position) > ZoneRadius + 0.5f) continue;
                z.Until += 2f;
                zones[i] = z;
                foreach (var ally in CombatQuery.Circle(ZoneAt(z), ZoneRadius, Team.Players)) Bless(ally, ally.Max.Value * 0.05f);
                progress.AbilityBurstRpc(dead.transform.position, 1f, PlayerProgress.FxKind.Holy);
                ZoneExtendRpc(i, 2f);
                break;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ZoneExtendRpc(int index, float seconds) => UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "EVERBURNING +" + seconds.ToString("F0") + "s", new Color(1f, 0.85f, 0.45f));

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
                var at = ZoneAt(z);
                if (Time.time > z.Until)
                {
                    zones.RemoveAt(i);

                    if (progress.A(AbilitySlot.Q, 4))
                    {
                        foreach (var ally in CombatQuery.Circle(at, ZoneRadius, Team.Players)) Bless(ally, ally.Max.Value * 0.2f);
                        AbilityKit.Blast(at, ZoneRadius, ConsecrateBurnPerSec * 2.5f * progress.AttackMultiplier, NetworkObjectId, "Hallowed Burst", h => AbilityKit.Stun(h, 1f));
                        progress.AbilityBurstRpc(at, ZoneRadius, PlayerProgress.FxKind.Holy);
                    }
                    continue;
                }
                foreach (var ally in CombatQuery.Circle(at, ZoneRadius, Team.Players))
                {
                    Bless(ally, ConsecrateHealPerSec * Potency * progress.Potency(AbilitySlot.Q) * dt);

                    if (progress.B(AbilitySlot.Q, 2)) AbilityKit.ServerSanctuary(ally, 0.6f, NetworkObjectId);

                    if (progress.B(AbilitySlot.Q, 3) && ally.TryGetComponent<PlayerDebuffs>(out var db) && db.AnyAffliction) { db.ServerCleanse(); progress.AbilityTextRpc(ally.Center + Vector3.up, "PURIFIED", PlayerProgress.FxKind.Holy); }
                }
                foreach (var foe in CombatQuery.Circle(at, ZoneRadius, Team.Enemies))
                    foe.Tag("Consecrate").Ticking().ServerDamage(ConsecrateBurnPerSec * progress.AttackMultiplier * progress.Potency(AbilitySlot.Q) * dt, at, NetworkObjectId);

                if (progress.A(AbilitySlot.Q, 3) && Time.time >= z.NextPillar)
                {
                    z.NextPillar = Time.time + PillarEvery;
                    zones[i] = z;
                    var foes = CombatQuery.Circle(at, ZoneRadius, Team.Enemies).Where(h => h.IsAlive).ToList();
                    if (foes.Count > 0)
                    {
                        var t = foes[Random.Range(0, foes.Count)];
                        t.Tag("Judgement").ServerDamage(SmiteDamage * 1.6f * progress.AttackMultiplier, t.transform.position + Vector3.up * 6f, NetworkObjectId);
                        AbilityKit.Stun(t, 0.4f);
                        progress.AbilityLinkRpc(t.transform.position + Vector3.up * 7f, t.transform.position, PlayerProgress.FxKind.Holy);
                        progress.AbilityBurstRpc(t.transform.position, 1.2f, PlayerProgress.FxKind.Holy);
                    }
                }
            }
        }

        [Rpc(SendTo.Server)]
        private void AegisRpc() => ServerAegis();

        public void ServerAegis()
        {
            if (!life.CanAct) return;
            var allies = CombatQuery.Circle(transform.position, AegisRadius, Team.Players).ToList();
            Health worst = null;
            foreach (var ally in allies) if (ally.IsAlive && (worst == null || ally.Fraction < worst.Fraction)) worst = ally;
            foreach (var ally in allies)
            {
                float amount = AegisAmount * Potency * progress.Potency(AbilitySlot.E);

                if (progress.B(AbilitySlot.E, 3) && ally == worst && allies.Count > 1) { amount *= 2f; progress.AbilityTextRpc(ally.Center + Vector3.up, "MARTYR'S SHARE", PlayerProgress.FxKind.Holy); }
                ally.ServerShield(amount, AegisSeconds);
                aegisOn[ally] = Time.time + AegisSeconds;

                if (progress.A(AbilitySlot.E, 1)) AbilityKit.ServerThorn(ally, AegisSeconds);

                if (progress.B(AbilitySlot.E, 1) && ally.TryGetComponent<PlayerProgress>(out var ap)) ap.ServerRally(4f, 1f);

                if (progress.A(AbilitySlot.E, 2)) Bless(ally, ally.Max.Value * 0.25f);

                if (progress.B(AbilitySlot.E, 2) && ally.TryGetComponent<PlayerDebuffs>(out var db)) { db.ServerCleanse(); db.ServerWard(4f); }

                if (progress.B(AbilitySlot.E, 4))
                {
                    var who = ally;
                    AbilityKit.Later(this, AegisSeconds - 0.15f, () =>
                    {
                        if (who == null || !who.IsAlive || who.Shield.Value <= 0f) return;
                        float left = who.Shield.Value;
                        Bless(who, left);
                        progress.AbilityTextRpc(who.Center + Vector3.up, "GRACE +" + Mathf.RoundToInt(left), PlayerProgress.FxKind.Holy);
                    });
                }
            }

            if (progress.A(AbilitySlot.E, 3)) StartCoroutine(Halo());
            AegisFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SmiteFxRpc(Vector3 origin, Vector3 direction)
        {
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Smite, SmiteSpeed, SmiteRange * RangeMul);
            AbilityFx.Muzzle(player.FxHand, direction, new Color(1f, 0.85f, 0.45f), player.FxHandScale);
            Sfx.Play(Sound.Frost, origin, 0.45f, 0.7f);
            if (!IsOwner) player.Rig?.Play(RigAction.Point, 0.3f);
        }

        private System.Collections.IEnumerator Halo()
        {
            progress.AbilityCircleRpc(transform.position, HaloRadius, HaloSeconds, PlayerProgress.FxKind.Holy);
            var at = transform.position;
            float end = Time.time + HaloSeconds;
            while (Time.time < end)
            {
                foreach (var h in CombatQuery.Circle(at, HaloRadius, Team.Enemies))
                {
                    if (AbilityKit.IsBoss(h)) continue;
                    var away = h.transform.position - at; away.y = 0f;
                    AbilityKit.Push(h, away.normalized * 11f);
                    AbilityKit.Stun(h, 0.3f);
                }
                yield return new WaitForSeconds(0.3f);
            }
        }

        private void OnShieldBroken(Health who)
        {
            if (!IsServer || !Active || who == null || !progress.A(AbilitySlot.E, 4)) return;
            if (!aegisOn.TryGetValue(who, out var until) || Time.time > until) return;
            aegisOn.Remove(who);
            AbilityKit.Blast(who.transform.position, 3.5f, AegisAmount * 0.6f * progress.AttackMultiplier, NetworkObjectId, "Breaking Light", h => AbilityKit.Stun(h, 1.2f));
            progress.AbilityBurstRpc(who.transform.position, 3.5f, PlayerProgress.FxKind.Holy);
        }

        private readonly Dictionary<Health, float> aegisOn = new Dictionary<Health, float>();

        public override void OnNetworkSpawn()
        {
            if (IsServer) { Health.ServerShieldBroken += OnShieldBroken; Health.AnyDied += OnEnemyDied; }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) { Health.ServerShieldBroken -= OnShieldBroken; Health.AnyDied -= OnEnemyDied; }
            base.OnNetworkDespawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ConsecrateFxRpc(Vector3 at, bool follow)
        {
            var g = HolyGround.Spawn(at, ConsecrateRadius, ConsecrateSeconds);
            if (follow) g.Follow = transform;

            FxSprite.Decal("magic_05", at, ConsecrateRadius * 1.9f, ConsecrateRadius * 2f, ConsecrateSeconds, new Color(1f, 0.85f, 0.45f, 0.55f), 18f);

            ClassFx.ClericConsecrate(at, ConsecrateRadius, ConsecrateSeconds, transform.forward, follow ? transform : null);
            Sfx.Play(Sound.Revive, at + Vector3.up, 0.9f, 0.85f);
            if (!IsOwner) player.Rig?.Play(RigAction.Pray, 0.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AegisFxRpc()
        {
            Sfx.Play(Sound.Relic, transform.position + Vector3.up, 0.9f, 1.2f);
            var gold = new Color(1f, 0.85f, 0.45f);
            Vfx.AbilityFlash(transform.position + Vector3.up * 1.5f, gold, 4f, 10f, 0.5f);
            player.Rig?.Play(RigAction.Point, 0.5f);

            foreach (var ally in PlayerLife.All)
            {
                if (ally == null || ally.Downed.Value || Vector3.Distance(ally.transform.position, transform.position) > AegisRadius) continue;
                ClassFx.AegisGyro(ally.transform, transform.position + Vector3.up * 1.3f, ally.gameObject == gameObject);
            }
        }

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

            if (Time.time >= nextMendMote)
            {
                nextMendMote = Time.time + 0.2f;
                ClassFx.MendMote(to);
            }
        }

        public override void OnDestroy()
        {
            if (beam != null) Destroy(beam.gameObject);
            base.OnDestroy();
        }
    }
}
