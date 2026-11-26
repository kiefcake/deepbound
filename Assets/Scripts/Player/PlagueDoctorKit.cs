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
    public class PlagueDoctorKit : NetworkBehaviour, IClassKit
    {
        public const float VialInterval = 0.55f, VialCost = 7f, VialDamage = 15f, VialSpeed = 19f, VialRange = 20f, VialGravity = 9f, SplashRadius = 1.7f;
        public const float BlightSeconds = 4f, BlightDps = 9f;
        public const float ExpungeCooldown = 10f, ExpungeCost = 35f, ExpungeRadius = 16f, ExpungePerStack = 16f;
        public const float ExpungeHealRadius = 12f, ExpungeHealBase = 0.05f, ExpungeHealPerStack = 0.03f, ExpungeHealCap = 0.3f;

        public const float StackBonus = 0.15f;
        public const float MiasmaCooldown = 12f, MiasmaRadius = 4f, MiasmaSeconds = 6f, MiasmaDps = 9f;
        public const float MaskCooldown = 18f, MaskRadius = 8f, MaskHeal = 0.1f, MaskWard = 4f;
        public const float ReagentMax = 100f, ReagentRegen = 7f, BrewPause = 1.5f, HarvestReagents = 12f, DiluteDamage = 0.6f;
        private float lastThrowAt = -99f;
        public const float ContagionRadius = 6f;

        public float AttackRange => VialRange * (progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        public float Reagents { get; private set; } = ReagentMax;
        private float nextVial, expungeReadyAt, miasmaReadyAt, maskReadyAt;

        private struct Cloud { public Vector3 At; public float Radius, Until, Dps, NextChoke; public bool Black, Small; public int Id; }
        private readonly List<Cloud> clouds = new List<Cloud>();
        private float nextCloudTick;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.PlagueDoctor;

        public ClassId Class => ClassId.PlagueDoctor;
        public string ResourceName => "Reagents";
        public Color ResourceColor => new Color(0.5f, 0.95f, 0.35f);
        public float Resource => Reagents;
        public float ResourceMax => ReagentMax;
        public void RestoreResource(float fraction) => Reagents = Mathf.Min(ReagentMax, Reagents + ReagentMax * fraction);
        public void ShaveCooldowns(float seconds) { miasmaReadyAt -= seconds; maskReadyAt -= seconds; }

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = "Vial", Unaffordable = Reagents < VialCost });
            into.Add(new AbilityView { Key = "RMB", Name = "Expunge", Remaining = expungeReadyAt - Time.time, Cooldown = ExpungeCooldown, Unaffordable = Reagents < ExpungeCost });
            into.Add(new AbilityView { Key = "Q", Name = "Miasma", Remaining = miasmaReadyAt - Time.time, Cooldown = MiasmaCooldown * progress.CooldownFor(AbilitySlot.Q) });
            into.Add(new AbilityView { Key = "E", Name = "Mask", Remaining = maskReadyAt - Time.time, Cooldown = MaskCooldown * progress.CooldownFor(AbilitySlot.E) });
        }

        private static bool contagionHooked;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            all.Add(this);
            health.DamagedServer += OnHurt;
            Health.InPlagueCloud = InBlackDeath;
            if (contagionHooked) return;
            contagionHooked = true;

            Health.BlightedDied += (dead, source) =>
            {
                if (dead == null || dead.Team != Team.Enemies) return;

                if (plagued.TryGetValue(dead, out var pl))
                {
                    plagued.Remove(dead);

                    if (pl.kit != null && pl.kit.zero == dead) pl.kit.ServerZeroJump(dead.transform.position, pl.until - Time.time, dead);
                    else if (Time.time < pl.until && pl.kit != null) pl.kit.ServerContagionSpread(dead.transform.position, Mathf.Min(ZeroCoughSeconds, pl.until - Time.time));
                }
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(source, out var obj) || !obj.TryGetComponent<PlagueDoctorKit>(out var kit)) return;
                kit.ServerContagion(dead.transform.position);
                kit.HarvestRpc(HarvestReagents);
            };
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
            if (IsServer) Health.AnyDied -= OnSomethingDied;
        }

        public const float PuddleSeconds = 2.5f, VialHeal = 0.06f, MithridateReachMelee = 3.5f;

        private void OnHurt(float lost, ulong from)
        {
            if (!Active || !progress.OnPath(Classes.PathTriads.Path) || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(from, out var obj)) return;
            if (!obj.TryGetComponent<Health>(out var foe) || foe.Team != Team.Enemies || !foe.IsAlive) return;
            if (Vector3.Distance(foe.transform.position, transform.position) > MithridateReachMelee) return;
            foe.ServerBlight(BlightSeconds, BlightDps * BlightPower, NetworkObjectId);
        }

        public const float DriftSpeed = 1.2f, ChokeEvery = 2f, VolatileShare = 3.5f, WindRadius = 10f, RepelRadius = 5f, LordRadius = 8f;
        private int nextCloudId;
        private bool diedHooked;
        private static readonly Dictionary<int, SmokeCloud> shownClouds = new Dictionary<int, SmokeCloud>();

        private void OnSomethingDied(Health dead)
        {
            if (!Active || dead == null || dead.Team != Team.Enemies || !progress.A(AbilitySlot.Q, 2)) return;
            foreach (var c in clouds)
                if (!c.Black && !c.Small && Time.time < c.Until && Vector3.Distance(c.At, dead.transform.position) < c.Radius)
                {
                    AddCloud(dead.transform.position, MiasmaRadius * 0.5f, MiasmaSeconds * 0.6f, MiasmaDps * progress.Potency(AbilitySlot.Q) * 0.6f, true);
                    return;
                }
        }

        private void AddCloud(Vector3 at, float radius, float seconds, float dps, bool small)
        {
            if (!diedHooked && IsServer) { diedHooked = true; Health.AnyDied += OnSomethingDied; }
            int id = ++nextCloudId;
            clouds.Add(new Cloud { At = at, Radius = radius, Until = Time.time + seconds, Dps = dps, Small = small, Id = id, NextChoke = Time.time + ChokeEvery });
            CloudFxRpc(at, radius, seconds, false, id);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CloudDriftRpc(int id, Vector3 to)
        {
            if (shownClouds.TryGetValue(NetworkObjectId.GetHashCode() * 1000 + id, out var c) && c != null) c.DriftTo = Vfx.Floor(to);
        }

        private void Update()
        {
            if (IsServer && clouds.Count > 0 && Time.time >= nextCloudTick) TickClouds();
            if (IsServer && zero != null && Time.time >= nextZeroTick) TickZero();
            if (!IsOwner || !Active) return;

            float brew = Time.time - lastThrowAt > BrewPause ? 2f : 1f;
            Reagents = Mathf.Min(ReagentMax, Reagents + ReagentRegen * brew * Mathf.Max(0.5f, 1f + (progress.Value(Core.StatType.WIS) - 42f) * 0.025f) * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier;
            motor.CanSprint = true;
            if (!life.CanAct || !motor.ActionsReady) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (UI.Binds.PrimaryHeld && Time.time >= nextVial)
            {
                bool dilute = Reagents < VialCost;
                if (!dilute) Reagents -= VialCost;
                lastThrowAt = Time.time;
                nextVial = Time.time + VialInterval * progress.AttackTimeMultiplier;
                player.ViewModelCast(false);
                Sfx.Play(Sound.Swing, transform.position, 0.35f, 1.7f);
                VialRpc(player.AimOrigin + player.AimForward * 0.6f, LobDirection(), dilute);
            }

            if (UI.Binds.SecondaryDown && Time.time >= expungeReadyAt && Reagents >= ExpungeCost)
            {
                Reagents -= ExpungeCost;
                lastThrowAt = Time.time;
                expungeReadyAt = Time.time + ExpungeCooldown;
                player.Gesture(RigAction.Siphon, 0.8f);
                ExpungeRpc();
            }

            if (UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= miasmaReadyAt)
            {
                miasmaReadyAt = Time.time + MiasmaCooldown * progress.CooldownFor(AbilitySlot.Q);
                player.Gesture(RigAction.Cast, 0.45f);
                MiasmaRpc(AimGround(18f * RangeMul));
            }

            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= maskReadyAt)
            {
                maskReadyAt = Time.time + MaskCooldown * progress.CooldownFor(AbilitySlot.E);
                MaskRpc();
            }
        }

        private Vector3 LobDirection()
        {
            var f = player.AimForward;
            return (f + Vector3.up * 0.12f).normalized;
        }

        private Vector3 AimGround(float range)
        {
            if (player.AimRay(range, out var hit)) return hit.point;
            return player.TryAimFloor(range, out var spot) ? spot : transform.position + transform.forward * 6f;
        }

        private float BlightPower => progress.AttackMultiplier;

        [Rpc(SendTo.Server)]
        private void VialRpc(Vector3 origin, Vector3 direction, bool dilute)
        {
            if (!life.CanAct) return;
            ServerVial(origin, direction, dilute);
        }

        [Rpc(SendTo.Owner)]
        private void HarvestRpc(float amount) => Reagents = Mathf.Min(ReagentMax, Reagents + amount);

        public string ResourceHint => Reagents >= VialCost ? "Vials apply Blight  ·  Blighted kills restore reagents" : "Depleted: vials will not Blight";

        public void ServerVial(Vector3 origin, Vector3 direction, bool dilute = false)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            ulong me = NetworkObjectId;
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, VialSpeed, h => { }, false, VialRange * RangeMul, VialGravity,
                at => { if (this != null) ServerSplash(at, dilute); });
            VialFxRpc(origin, direction, false);
        }

        public void ServerSplash(Vector3 at, bool dilute = false)
        {
            float radius = SplashRadius;
            float p = BlightPower;
            if (dilute)
            {
                foreach (var h in CombatQuery.Circle(at, radius * 0.8f, Team.Enemies))
                    h.Tag("PlagueDoctorKit").ServerDamage(VialDamage * DiluteDamage * p, at, NetworkObjectId);
                SplashFxRpc(at, radius * 0.6f, false);
                return;
            }

            if (progress.OnPath(1)) AddCloud(at, radius, PuddleSeconds, BlightDps * 0.6f, true);

            if (progress.OnPath(2))
                foreach (var ally in CombatQuery.Circle(at, radius + 0.4f, Team.Players))
                    if (ally.IsAlive) ally.ServerHeal(ally.Max.Value * VialHeal);

            if (progress.B(AbilitySlot.Q, 3))
                for (int i = clouds.Count - 1; i >= 0; i--)
                {
                    var c = clouds[i];
                    if (c.Black || Time.time >= c.Until || Vector3.Distance(c.At, at) > c.Radius) continue;
                    clouds.RemoveAt(i);
                    AbilityKit.Blast(c.At, c.Radius + 0.5f, VialDamage * VolatileShare * p, NetworkObjectId, "Volatile", h => h.ServerScorch(4f, NetworkObjectId));
                    progress.AbilityBurstRpc(c.At, c.Radius + 0.5f, PlayerProgress.FxKind.Fire);
                    progress.AbilityTextRpc(c.At + Vector3.up * 2f, "IGNITED", PlayerProgress.FxKind.Fire);
                    CloudGoneRpc(c.Id);
                }
            int deepest = 0;
            foreach (var h in CombatQuery.Circle(at, radius, Team.Enemies))
            {
                int stacks = h.Blighted.Value ? h.BlightStacks.Value : 0;
                deepest = Mathf.Max(deepest, stacks);
                h.Tag("PlagueDoctorKit").ServerDamage(VialDamage * p * (1f + StackBonus * stacks), at, NetworkObjectId);
                h.ServerBlight(BlightSeconds, BlightDps * p, NetworkObjectId);
            }
            SplashFxRpc(at, radius, false, (byte)deepest);
        }

        [Rpc(SendTo.Server)]
        private void ExpungeRpc()
        {
            if (!life.CanAct) return;
            ServerExpunge(transform.position, out _);
        }

        public int ServerExpunge(Vector3 at, out int stacks)
        {
            stacks = 0;
            float p = BlightPower * 1f;
            var drained = new List<ulong>();
            var counts = new List<byte>();
            foreach (var h in CombatQuery.Circle(at, ExpungeRadius * RangeMul, Team.Enemies))
            {
                if (h == null || !h.IsAlive) continue;
                int n = h.BlightStackCount;
                if (n <= 0) continue;
                stacks += n;
                drained.Add(h.NetworkObjectId);
                counts.Add((byte)n);
                h.Tag("Expunge").ServerDamage(ExpungePerStack * n * p, at, NetworkObjectId);
                if (h.IsAlive) h.ServerClearBlight();
                if (h == zero && h.IsAlive) KeepZeroFestering();
                if (drained.Count >= 16) break;
            }
            float heal = Mathf.Min(ExpungeHealCap, ExpungeHealBase + ExpungeHealPerStack * stacks);
            var healed = new List<ulong>();
            foreach (var ally in PlayerLife.All)
            {
                if (ally == null || ally.Downed.Value || !ally.Health.IsAlive) continue;
                if (Vector3.Distance(ally.transform.position, at) > ExpungeHealRadius + 0.6f) continue;
                ally.Health.ServerHeal(ally.Health.Max.Value * heal);
                if (progress.Mastered(2) && ally.TryGetComponent<PlayerDebuffs>(out var db)) db.ServerCleanse();
                healed.Add(ally.NetworkObjectId);
            }
            ExpungeFxRpc(drained.ToArray(), counts.ToArray(), healed.ToArray());
            return healed.Count;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ExpungeFxRpc(ulong[] drained, byte[] counts, ulong[] healed)
        {
            player.Act(RigAction.Siphon, 0.8f);
            Sfx.Play(Sound.Slime, transform.position + Vector3.up, 0.7f, 0.6f);
            Sfx.Play(Sound.Revive, transform.position + Vector3.up, 0.4f, 0.8f);
            var nm = NetworkManager.Singleton;
            var from = new List<(Transform, int)>();
            for (int i = 0; i < drained.Length; i++)
                if (nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(drained[i], out var o)) from.Add((o.transform, i < counts.Length ? counts[i] : 1));
            var who = new List<Transform>();
            foreach (var id in healed)
                if (nm != null && nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var o)) who.Add(o.transform);
            ClassFx.Expunge(transform, from, who);
            if (IsOwner && from.Count > 0) player.AddShake(0.08f);
        }

        [Rpc(SendTo.Server)]
        private void MiasmaRpc(Vector3 at)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(at, transform.position) > 22f * RangeMul) at = transform.position + transform.forward * 6f;
            ServerMiasma(at);
        }

        public void ServerMiasma(Vector3 at)
        {
            float potency = progress.Potency(AbilitySlot.Q);
            float radius = MiasmaRadius;

            if (progress.A(AbilitySlot.Q, 4))
            {
                var side = transform.right; side.y = 0f; side.Normalize();
                AddCloud(at + side * radius * 0.9f, radius, MiasmaSeconds, MiasmaDps * potency, false);
                AddCloud(at - side * radius * 0.9f, radius, MiasmaSeconds, MiasmaDps * potency, false);
            }
            else AddCloud(at, radius, MiasmaSeconds, MiasmaDps * potency, false);
        }

        public const float ZeroSeconds = 12f, ZeroBurst = 40f, ZeroRot = 3f, ZeroCough = 2.5f, ZeroCoughRadius = 5f, ZeroCoughSeconds = 4f;
        public const float ZeroJump = 12f, ZeroMinJump = 4f, ZeroPick = 6f, ZeroReach = 26f, ZeroHeal = 0.2f, ZeroHealRadius = 12f;
        public const float ContagionSpread = 6f, ContagionMinSpread = 3f, ContagionSpreadBurst = 0.4f, ContagionBurst = 30f, ContagionRot = 3f;
        private static readonly Dictionary<Health, (float until, PlagueDoctorKit kit)> plagued = new Dictionary<Health, (float until, PlagueDoctorKit kit)>();
        private Health zero;
        private float zeroUntil, nextZeroTick, nextCough;

        public Health PatientZero => zero != null && zero.IsAlive && Time.time < zeroUntil ? zero : null;

        public void ServerBlackDeath(Vector3 at) => ServerPatientZero(at);

        public Health ServerPatientZero(Vector3 aim)
        {
            var me = transform.position;
            Health pick = null;
            float best = ZeroPick;

            foreach (var h in CombatQuery.Circle(me, ZeroReach, Team.Enemies))
            {
                if (h == null || !h.IsAlive || !h.TryGetComponent<Enemies.EnemyBrain>(out _)) continue;
                float d = Vector3.Distance(Flat(h.transform.position), Flat(aim));
                if (d < best) { best = d; pick = h; }
            }
            if (pick == null)
            {
                float bestScore = float.MaxValue;
                var fwd = transform.forward; fwd.y = 0f;
                foreach (var h in CombatQuery.Circle(me, ZeroReach, Team.Enemies))
                {
                    if (h == null || !h.IsAlive || !h.TryGetComponent<Enemies.EnemyBrain>(out _)) continue;
                    var to = h.transform.position - me; to.y = 0f;
                    float score = to.magnitude * (Vector3.Angle(fwd, to) < 45f ? 1f : 2.5f);
                    if (score < bestScore) { bestScore = score; pick = h; }
                }
            }
            foreach (var ally in CombatQuery.Circle(me, ZeroHealRadius, Team.Players))
                ally.ServerHeal(ally.Max.Value * ZeroHeal);
            if (pick == null)
            {
                if (TryGetComponent<Ultimates>(out var ult)) ult.Charge.Value = Ultimates.ChargeMax;
                return null;
            }
            MakeZero(pick, ZeroSeconds, ZeroBurst, me);
            return pick;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private void MakeZero(Health h, float seconds, float burst, Vector3 from)
        {
            zero = h;
            zeroUntil = Time.time + seconds;
            nextCough = Time.time + ZeroCough;
            nextZeroTick = Time.time;
            plagued[h] = (zeroUntil, this);
            if (h.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerAddThreat(NetworkObjectId, 10f);
            KeepZeroFestering();
            if (burst > 0f) h.Tag("Patient Zero").ServerDamage(burst * BlightPower, from, NetworkObjectId);
            ZeroFxRpc(h.NetworkObjectId, from, seconds);
        }

        private void KeepZeroFestering()
        {
            if (zero == null || !zero.IsAlive) return;
            zero.ServerBlight(Mathf.Max(0.6f, zeroUntil - Time.time), BlightDps * ZeroRot * BlightPower, NetworkObjectId);
            zero.ServerFesterFull();
        }

        private void TickZero()
        {
            nextZeroTick = Time.time + 0.5f;
            if (zero == null) return;
            if (!zero.IsAlive) { ServerZeroJump(zero.transform.position, zeroUntil - Time.time, zero); return; }
            if (Time.time >= zeroUntil) { plagued.Remove(zero); zero = null; return; }
            KeepZeroFestering();
            if (Time.time < nextCough) return;
            nextCough = Time.time + ZeroCough;
            var at = zero.transform.position;
            int n = 0;
            foreach (var h in CombatQuery.Circle(at, ZeroCoughRadius, Team.Enemies))
                if (h != zero && (!plagued.TryGetValue(h, out var pl) || pl.until - Time.time < ZeroCoughSeconds - 0.5f) && Afflict(h, ZeroCoughSeconds, 0f)) n++;
            CoughFxRpc(zero.NetworkObjectId, n > 0);
        }

        private void ServerZeroJump(Vector3 at, float left, Health dead)
        {
            if (dead != null) plagued.Remove(dead);
            zero = null;
            if (left <= 0.2f) return;
            Health next = null;
            float best = ZeroJump;
            foreach (var h in CombatQuery.Circle(at, ZeroJump, Team.Enemies))
            {
                if (h == null || !h.IsAlive || h == dead || !h.TryGetComponent<Enemies.EnemyBrain>(out _)) continue;
                float d = Vector3.Distance(h.transform.position, at);
                if (d < best) { best = d; next = h; }
            }
            if (next != null) MakeZero(next, Mathf.Max(ZeroMinJump, left), ZeroBurst * 0.5f, at);
        }

        private bool Afflict(Health h, float seconds, float burst)
        {
            if (h == null || !h.IsAlive) return false;
            float p = BlightPower;
            plagued[h] = (Time.time + seconds, this);
            if (h.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerAddThreat(NetworkObjectId, 4f);
            h.ServerBlight(seconds, BlightDps * ContagionRot * p, NetworkObjectId);
            if (burst > 0f) h.Tag("Contagion").ServerDamage(burst * p, transform.position, NetworkObjectId);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ZeroFxRpc(ulong target, Vector3 from, float seconds)
        {
            player.Act(RigAction.Point, 0.6f);
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(target, out var o)) return;
            var h = o.GetComponent<Health>();
            Sfx.Play(Sound.Slime, o.transform.position, 1f, 0.55f);
            Sfx.Play(Sound.Toll, o.transform.position, 0.5f, 0.6f);
            ClassFx.Veins(Vfx.Floor(from), new List<Vector3> { o.transform.position }, 2.5f, 0.35f);
            ClassFx.PatientZero(o.transform, h != null ? h.Center.y - o.transform.position.y : 1f, seconds);
            UI.GameHud.ShowCombo(o.transform.position + Vector3.up * 2.6f, "PATIENT ZERO", Plague);
            if (IsOwner) player.AddShake(0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CoughFxRpc(ulong target, bool caught)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(target, out var o)) return;
            Sfx.Play(Sound.Slime, o.transform.position, 0.45f, 0.75f);
            ClassFx.Cough(o.transform.position, ZeroCoughRadius);
        }

        public void ServerContagionSpread(Vector3 at, float left)
        {
            float seconds = Mathf.Max(ContagionMinSpread, left);
            foreach (var h in CombatQuery.Circle(at, ContagionSpread, Team.Enemies))
                if (!plagued.TryGetValue(h, out var pl) || pl.until - Time.time < seconds - 0.5f) Afflict(h, seconds, ContagionBurst * ContagionSpreadBurst);
            ContagionBurstFxRpc(at);
        }

        public static bool IsPlagued(Health h) => h != null && plagued.TryGetValue(h, out var pl) && Time.time < pl.until;

        [Rpc(SendTo.ClientsAndHost)]
        private void ContagionBurstFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slime, at, 0.8f, 0.8f);
            Vfx.Smoke(at + Vector3.up * 0.8f, 3, 1f, 1.8f);
            Vfx.Embers(at + Vector3.up, 14, Plague, 4f, 0.05f, 0.7f);

            var spokes = new List<Vector3>();
            for (int i = 0; i < 5; i++) spokes.Add(at + Quaternion.Euler(0f, i * 72f + Random.Range(-20f, 20f), 0f) * Vector3.forward * ContagionSpread * Random.Range(0.3f, 0.5f));
            ClassFx.Veins(at, spokes, 2.2f, 0.3f);
            ClassFx.Flies(null, Vfx.Floor(at), 0.8f, 1f, 2.5f, 8);
        }

        private static readonly List<PlagueDoctorKit> all = new List<PlagueDoctorKit>();

        public static bool InBlackDeath(Health h) => IsPlagued(h);

        private void TickClouds()
        {
            nextCloudTick = Time.time + 0.5f;
            float p = BlightPower;
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                var c = clouds[i];
                if (Time.time >= c.Until)
                {
                    clouds.RemoveAt(i);

                    if (!c.Black && !c.Small && progress.B(AbilitySlot.Q, 4))
                    {
                        foreach (var h in CombatQuery.Circle(c.At, WindRadius, Team.Enemies)) h.ServerBlight(BlightSeconds, BlightDps * p, NetworkObjectId);
                        progress.AbilityBurstRpc(c.At, WindRadius, PlayerProgress.FxKind.Poison);
                    }
                    continue;
                }
                bool mine = !c.Black;

                if (mine && progress.A(AbilitySlot.Q, 1))
                {
                    var near = AbilityKit.Nearest(c.At, 14f);
                    if (near != null && Vector3.Distance(near.transform.position, c.At) > 0.8f)
                    {
                        c.At = Vector3.MoveTowards(c.At, near.transform.position, DriftSpeed * 0.5f);
                        clouds[i] = c;
                        CloudDriftRpc(c.Id, c.At);
                    }
                }
                bool choke = mine && progress.A(AbilitySlot.Q, 3) && Time.time >= c.NextChoke;
                if (choke) { c.NextChoke = Time.time + ChokeEvery; clouds[i] = c; }
                foreach (var h in CombatQuery.Circle(c.At, c.Radius, Team.Enemies))
                {
                    h.ServerBlight(1.5f, c.Dps * p, NetworkObjectId);
                    if (c.Black && h.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerAddThreat(NetworkObjectId, 2f);

                    if (mine && progress.B(AbilitySlot.Q, 1)) h.ServerChill(1f);

                    if (choke && !AbilityKit.IsBoss(h)) AbilityKit.Stun(h, 0.35f);
                }
                if (choke) progress.AbilityTextRpc(c.At + Vector3.up * 2f, "CHOKING", PlayerProgress.FxKind.Poison);

                if (mine && progress.B(AbilitySlot.Q, 2))
                    foreach (var ally in CombatQuery.Circle(c.At, c.Radius, Team.Players)) ally.ServerHeal(ally.Max.Value * 0.03f * 0.5f);
            }
        }

        public void ServerContagion(Vector3 at)
        {
            bool all = progress.Mastered(1);
            Health nearest = null;
            float best = ContagionRadius;
            foreach (var h in CombatQuery.Circle(at, ContagionRadius, Team.Enemies))
            {
                if (!h.IsAlive) continue;
                if (all) { h.ServerBlight(BlightSeconds, BlightDps * BlightPower, NetworkObjectId); continue; }
                float d = Vector3.Distance(h.transform.position, at);
                if (d < best) { best = d; nearest = h; }
            }
            if (nearest != null) nearest.ServerBlight(BlightSeconds, BlightDps * BlightPower, NetworkObjectId);
            if (nearest != null || all) ContagionFxRpc(at);
        }

        [Rpc(SendTo.Server)]
        private void MaskRpc() => ServerMask();

        public int ServerMask()
        {
            if (!life.CanAct) return 0;
            float potency = progress.Potency(AbilitySlot.E);
            float ward = MaskWard * (progress.B(AbilitySlot.E, 2) ? 2f : 1f);
            int n = 0;
            bool lifted = false;
            foreach (var ally in PlayerLife.All)
            {
                if (ally == null || Vector3.Distance(ally.transform.position, transform.position) > MaskRadius) continue;

                if (ally.Downed.Value && !lifted && progress.B(AbilitySlot.E, 4) && !ally.Soul.Value)
                {
                    lifted = true;
                    ally.ServerRevive(0.25f);
                    progress.AbilityTextRpc(ally.transform.position + Vector3.up * 2f, "LAST BREATH", PlayerProgress.FxKind.Poison);
                    continue;
                }
                if (ally.Downed.Value || !ally.Health.IsAlive) continue;
                if (ally.TryGetComponent<PlayerDebuffs>(out var db))
                {
                    if (progress.A(AbilitySlot.E, 2) && db.AnyAffliction)
                    {
                        AbilityKit.Blast(ally.transform.position, 3f, VialDamage * 2f * BlightPower, NetworkObjectId, "Purge", h => h.ServerBlight(BlightSeconds, BlightDps * BlightPower, NetworkObjectId));
                        progress.AbilityBurstRpc(ally.transform.position, 3f, PlayerProgress.FxKind.Poison);
                    }
                    db.ServerCleanse();
                    db.ServerWard(ward);
                }
                ally.Health.ServerHeal(ally.Health.Max.Value * MaskHeal * potency);

                if (progress.A(AbilitySlot.E, 1) && ally.TryGetComponent<PlayerProgress>(out var bp)) bp.BloodlustEnds.Value = System.Math.Max(bp.BloodlustEnds.Value, NetworkManager.ServerTime.Time + 6.0);

                if (progress.B(AbilitySlot.E, 1)) ally.Health.ServerShield(ally.Health.Max.Value * 0.15f, 8f);

                if (progress.B(AbilitySlot.E, 2)) { var who = ally; AbilityKit.Repeat(this, ward, 0.5f, () => { if (who != null && who.Health.IsAlive && !who.Downed.Value) who.Health.ServerHeal(who.Health.Max.Value * 0.01f); }); }
                n++;
            }

            if (progress.A(AbilitySlot.E, 3)) AddCloud(transform.position, MiasmaRadius, MiasmaSeconds, MiasmaDps * progress.Potency(AbilitySlot.Q), false);

            if (progress.B(AbilitySlot.E, 3))
                foreach (var h in CombatQuery.Circle(transform.position, RepelRadius, Team.Enemies))
                {
                    var away = h.transform.position - transform.position; away.y = 0f;
                    AbilityKit.Push(h, away.normalized * 12f);
                    AbilityKit.Stun(h, 0.5f);
                }

            if (progress.A(AbilitySlot.E, 4))
            {
                foreach (var h in CombatQuery.Circle(transform.position, LordRadius, Team.Enemies)) h.ServerBlight(BlightSeconds * 1.5f, BlightDps * BlightPower, NetworkObjectId);
                progress.AbilityBurstRpc(transform.position, LordRadius, PlayerProgress.FxKind.Poison);
            }
            MaskFxRpc(transform.position);
            return n;
        }

        private static readonly Color Plague = new Color(0.55f, 1f, 0.35f);
        private static readonly Color TonicColor = new Color(1f, 0.55f, 0.6f);

        [Rpc(SendTo.ClientsAndHost)]
        private void VialFxRpc(Vector3 origin, Vector3 direction, bool tonic)
        {
            Arrows.SpawnVisual(origin, direction, tonic ? ProjectileKind.Tonic : ProjectileKind.Vial, VialSpeed, VialRange * RangeMul, VialGravity);
            if (!IsOwner) { player.Rig?.Play(RigAction.Lob, 0.35f); Sfx.Play(Sound.Swing, transform.position, 0.35f, 1.7f); }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SplashFxRpc(Vector3 at, float radius, bool tonic, byte stacks = 0)
        {
            if (stacks > 0) ClassFx.FesterBurst(at, stacks);
            var c = tonic ? TonicColor : Plague;
            Sfx.Play(Sound.Shatter, at, 0.6f, tonic ? 1.6f : 1.3f);
            Sfx.Play(tonic ? Sound.Potion : Sound.Splash, at, 0.5f, 1.2f);
            Vfx.Embers(at + Vector3.up * 0.3f, 6, c, 2.5f, 0.05f, 0.7f);
            if (tonic) SmokeCloud.Spawn(at, radius * 0.8f, 0.9f, TonicColor);

            ClassFx.FlaskBurst(at, radius, tonic, 3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CloudFxRpc(Vector3 at, float radius, float seconds, bool black, int id)
        {
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.8f, black ? 0.5f : 0.8f);
            var shown = SmokeCloud.Spawn(at, radius, seconds, black ? new Color(0.15f, 0.28f, 0.08f) : new Color(0.35f, 0.7f, 0.2f));
            int key = NetworkObjectId.GetHashCode() * 1000 + id;
            shownClouds[key] = shown;
            if (shownClouds.Count > 40) { var stale = new List<int>(); foreach (var kv in shownClouds) if (kv.Value == null) stale.Add(kv.Key); foreach (var k in stale) shownClouds.Remove(k); }
            Vfx.Ring(at + Vector3.up * 0.05f, radius, black ? new Color(0.25f, 0.6f, 0.15f) : Plague, 0.5f);
            player.Act(RigAction.Cast, 0.45f);

            if (shown != null) ClassFx.Flies(shown.transform, shown.transform.position, radius * 0.6f, 1.6f, seconds, 22, dark: true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CloudGoneRpc(int id)
        {
            if (shownClouds.TryGetValue(NetworkObjectId.GetHashCode() * 1000 + id, out var c) && c != null) Destroy(c.gameObject);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ContagionFxRpc(Vector3 at)
        {
            Vfx.Embers(at + Vector3.up, 12, Plague, 3f, 0.05f, 0.7f);
            ClassFx.Flies(null, Vfx.Floor(at), 0.6f, 1.1f, 2f, 6);
            UI.GameHud.ShowCombo(at + Vector3.up * 1.4f, "CONTAGION", Plague);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MaskFxRpc(Vector3 at)
        {
            player.Act(RigAction.Drink, 0.55f);
            Sfx.Play(Sound.Potion, at + Vector3.up, 0.9f, 0.8f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.6f, 1.3f);

            var warded = new List<Transform>();
            foreach (var ally in PlayerLife.All)
                if (ally != null && !ally.Downed.Value && Vector3.Distance(ally.transform.position, at) <= MaskRadius) warded.Add(ally.transform);
            ClassFx.PlagueMask(transform, warded);
        }
    }
}
