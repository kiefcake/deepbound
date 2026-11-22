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
    /// The Plague Doctor (2026-10-20): the alchemist. Rots the dead, patches the living.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Contagion.</b> Your vials BLIGHT what they splash (poison over time,
    ///   and +10% damage from everyone). A blighted enemy that dies passes it to the nearest one.</item>
    /// <item><b>LMB — Vial.</b> Lob a vial in an arc; it shatters where it lands in a small splash.</item>
    /// <item><b>RMB — Tonic.</b> Lob a tonic: it bursts into a healing mist for every ally inside
    ///   (yourself included).</item>
    /// <item><b>Q — Miasma.</b> A lingering plague cloud at the crosshair: the dead inside choke,
    ///   blighted again and again.</item>
    /// <item><b>E — Plague Mask.</b> Cleanse every ally nearby of their afflictions, heal them a
    ///   little, and ward them against new ones for a few seconds.</item>
    /// </list>
    /// REAGENTS (reworked 2026-11-22, the user: "plague doctors ability bar reagents doesn't seem to have any purpose"; vials
    /// were free and the bar refilled in 8 s): every vial costs <see cref="VialCost"/>, a Tonic <see cref="TonicCost"/>.
    /// Out of reagents your vials still fly but DILUTE: <see cref="DiluteDamage"/> damage and no blight. The bar brews
    /// back at <see cref="ReagentRegen"/>/s (twice that after <see cref="BrewPause"/> s without a throw), and every
    /// blighted enemy that dies gives back <see cref="HarvestReagents"/>: a Doctor who keeps the rot spreading keeps
    /// throwing; one who sprays at nothing runs dry.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlagueDoctorKit : NetworkBehaviour, IClassKit
    {
        public const float VialInterval = 0.55f, VialCost = 7f, VialDamage = 15f, VialSpeed = 19f, VialRange = 20f, VialGravity = 9f, SplashRadius = 1.7f;
        public const float BlightSeconds = 4f, BlightDps = 9f;
        public const float TonicCooldown = 7f, TonicCost = 35f, TonicRadius = 3f, TonicHeal = 0.2f;
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
        private float nextVial, tonicReadyAt, miasmaReadyAt, maskReadyAt;

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

        // ---- IClassKit ----

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
            into.Add(new AbilityView { Key = "RMB", Name = "Tonic", Remaining = tonicReadyAt - Time.time, Cooldown = TonicCooldown, Unaffordable = Reagents < TonicCost });
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
            // Contagion: a blighted death passes the blight on (to everything near, at Pestilence mastery).
            Health.BlightedDied += (dead, source) =>
            {
                if (dead == null || dead.Team != Team.Enemies) return;
                // The Pandemic: a plagued death bursts and passes the plague on, whoever laid the last blight.
                if (plagued.TryGetValue(dead, out var pl))
                {
                    plagued.Remove(dead);
                    if (Time.time < pl.until && pl.kit != null) pl.kit.ServerPandemicSpread(dead.transform.position, pl.until - Time.time);
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

        // ---- Hobb's paths (2026-11-20) ----
        public const float PuddleSeconds = 2.5f, VialHeal = 0.06f, MithridateReachMelee = 3.5f;

        /// <summary>Server: the Mithridate: whatever strikes you in melee is blighted.</summary>
        private void OnHurt(float lost, ulong from)
        {
            if (!Active || !progress.OnPath(Classes.PathTriads.Path) || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(from, out var obj)) return;
            if (!obj.TryGetComponent<Health>(out var foe) || foe.Team != Team.Enemies || !foe.IsAlive) return;
            if (Vector3.Distance(foe.transform.position, transform.position) > MithridateReachMelee) return;
            foe.ServerBlight(BlightSeconds, BlightDps * BlightPower, NetworkObjectId);
        }

        // ---- Ysolde's options for Miasma and Plague Mask (2026-11-20) ----
        public const float DriftSpeed = 1.2f, ChokeEvery = 2f, VolatileShare = 3.5f, WindRadius = 10f, RepelRadius = 5f, LordRadius = 8f;
        private int nextCloudId;
        private bool diedHooked;
        private static readonly Dictionary<int, SmokeCloud> shownClouds = new Dictionary<int, SmokeCloud>();

        /// <summary>Server: Contagious: an enemy that dies inside one of my clouds bursts into a small cloud.</summary>
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

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer && clouds.Count > 0 && Time.time >= nextCloudTick) TickClouds();
            if (!IsOwner || !Active) return;

            // WIS (Brann's Meditation drill) brews reagents faster: +2.5% per point over the class's 42.
            float brew = Time.time - lastThrowAt > BrewPause ? 2f : 1f;
            Reagents = Mathf.Min(ReagentMax, Reagents + ReagentRegen * brew * Mathf.Max(0.5f, 1f + (progress.Value(Core.StatType.WIS) - 42f) * 0.025f) * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier;
            motor.CanSprint = true;
            if (!life.CanAct || !motor.ActionsReady) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // --- LMB held: Vials ---
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

            // --- RMB: Tonic ---
            if (UI.Binds.SecondaryDown && Time.time >= tonicReadyAt && Reagents >= TonicCost)
            {
                Reagents -= TonicCost;
                lastThrowAt = Time.time;
                tonicReadyAt = Time.time + TonicCooldown;
                player.ViewModelCast(false);
                TonicRpc(player.AimOrigin + player.AimForward * 0.6f, LobDirection());
            }

            // --- Q: Miasma ---
            if (UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= miasmaReadyAt)
            {
                miasmaReadyAt = Time.time + MiasmaCooldown * progress.CooldownFor(AbilitySlot.Q);
                player.ViewModelCast(false);
                MiasmaRpc(AimGround(18f * RangeMul));
            }

            // --- E: Plague Mask ---
            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= maskReadyAt)
            {
                maskReadyAt = Time.time + MaskCooldown * progress.CooldownFor(AbilitySlot.E);
                MaskRpc();
            }
        }

        /// <summary>A vial is lobbed a little above the crosshair so its arc lands near where you aim.</summary>
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

        // ==================================================================
        // Server
        // ==================================================================

        private float BlightPower => progress.AttackMultiplier;

        [Rpc(SendTo.Server)]
        private void VialRpc(Vector3 origin, Vector3 direction, bool dilute)
        {
            if (!life.CanAct) return;
            ServerVial(origin, direction, dilute);
        }

        /// <summary>Server -> owner: a blighted enemy died; the Doctor harvests reagents from it.</summary>
        [Rpc(SendTo.Owner)]
        private void HarvestRpc(float amount) => Reagents = Mathf.Min(ReagentMax, Reagents + amount);

        public string ResourceHint => Reagents >= VialCost ? "vials blight  ·  blighted kills refill" : "DILUTE: vials won't blight";

        /// <summary>Server: throw one vial. Public for the smoke test.</summary>
        public void ServerVial(Vector3 origin, Vector3 direction, bool dilute = false)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            ulong me = NetworkObjectId;
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, VialSpeed, h => { }, false, VialRange * RangeMul, VialGravity,
                at => { if (this != null) ServerSplash(at, dilute); });
            VialFxRpc(origin, direction, false);
        }

        /// <summary>Server: a vial shatters here: damage and blight in a small splash.</summary>
        public void ServerSplash(Vector3 at, bool dilute = false)
        {
            float radius = SplashRadius;
            float p = BlightPower;
            if (dilute)
            {
                // Out of reagents: watered-down glass, a weak splash, no rot.
                foreach (var h in CombatQuery.Circle(at, radius * 0.8f, Team.Enemies).ToArray())
                    h.Tag("PlagueDoctorKit").ServerDamage(VialDamage * DiluteDamage * p, at, NetworkObjectId);
                SplashFxRpc(at, radius * 0.6f, false);
                return;
            }
            // Pestilence: a puddle of plague lingers where the vial burst.
            if (progress.OnPath(1)) AddCloud(at, radius, PuddleSeconds, BlightDps * 0.6f, true);
            // Physician: the splash heals the allies it touches.
            if (progress.OnPath(2))
                foreach (var ally in CombatQuery.Circle(at, radius + 0.4f, Team.Players).ToArray())
                    if (ally.IsAlive) ally.ServerHeal(ally.Max.Value * VialHeal);
            // Volatile (rank 4 B): a vial into my cloud sets it off.
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
            foreach (var h in CombatQuery.Circle(at, radius, Team.Enemies).ToArray())
            {
                h.Tag("PlagueDoctorKit").ServerDamage(VialDamage * p, at, NetworkObjectId);
                h.ServerBlight(BlightSeconds, BlightDps * p, NetworkObjectId);
            }
            SplashFxRpc(at, radius, false);
        }

        [Rpc(SendTo.Server)]
        private void TonicRpc(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            // A tonic flies over the dead (it targets nobody) and bursts where it lands.
            Arrows.ServerFire(origin, direction, 0f, Team.Props, NetworkObjectId, VialSpeed, h => { }, false, VialRange * RangeMul, VialGravity,
                at => { if (this != null) ServerTonic(at); });
            VialFxRpc(origin, direction, true);
        }

        /// <summary>Server: a tonic bursts: heal every ally in the mist. Public for the smoke test.</summary>
        public int ServerTonic(Vector3 at)
        {
            float heal = TonicHeal;
            int n = 0;
            foreach (var ally in PlayerLife.All)
            {
                if (ally == null || ally.Downed.Value || !ally.Health.IsAlive) continue;
                if (Vector3.Distance(ally.transform.position, at) > TonicRadius + 0.6f) continue;
                ally.Health.ServerHeal(ally.Health.Max.Value * heal);
                if (progress.Mastered(2) && ally.TryGetComponent<PlayerDebuffs>(out var db)) db.ServerCleanse();
                n++;
            }
            SplashFxRpc(at, TonicRadius, true);
            return n;
        }

        [Rpc(SendTo.Server)]
        private void MiasmaRpc(Vector3 at)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(at, transform.position) > 22f * RangeMul) at = transform.position + transform.forward * 6f;
            ServerMiasma(at);
        }

        /// <summary>Server: a plague cloud. Public for the smoke test.</summary>
        public void ServerMiasma(Vector3 at)
        {
            float potency = progress.Potency(AbilitySlot.Q);
            float radius = MiasmaRadius;
            // Twin Clouds (rank 5 A): one each side of where you aim.
            if (progress.A(AbilitySlot.Q, 4))
            {
                var side = transform.right; side.y = 0f; side.Normalize();
                AddCloud(at + side * radius * 0.9f, radius, MiasmaSeconds, MiasmaDps * potency, false);
                AddCloud(at - side * radius * 0.9f, radius, MiasmaSeconds, MiasmaDps * potency, false);
            }
            else AddCloud(at, radius, MiasmaSeconds, MiasmaDps * potency, false);
        }

        // ---- The ultimate: PANDEMIC (2026-11-20, replaces Black Death, a cloud at the crosshair: the user found it weak) ----
        // Every enemy within PandemicRadius of you is struck at once (PandemicBurst), rots hard for PandemicSeconds
        // (a blight PandemicRot times the vials') and takes 25% more from everyone. A plagued creature that dies
        // bursts and plagues everything within PandemicSpread for what was left of its time (at least
        // PandemicMinSpread), so a crowd goes down in a chain. Allies within the radius are healed PandemicHeal.
        public const float PandemicRadius = 12f, PandemicSeconds = 10f, PandemicBurst = 30f, PandemicRot = 3f;
        public const float PandemicSpread = 6f, PandemicMinSpread = 5f, PandemicSpreadBurst = 0.6f, PandemicHeal = 0.2f;
        private static readonly Dictionary<Health, (float until, PlagueDoctorKit kit)> plagued = new Dictionary<Health, (float until, PlagueDoctorKit kit)>();

        /// <summary>Server: the ultimate (the old entry point; the crosshair no longer matters).</summary>
        public void ServerBlackDeath(Vector3 at) => ServerPandemic();

        public int ServerPandemic()
        {
            var at = transform.position;
            int n = 0;
            foreach (var h in CombatQuery.Circle(at, PandemicRadius, Team.Enemies).ToArray())
                if (Afflict(h, PandemicSeconds, PandemicBurst)) n++;
            foreach (var ally in CombatQuery.Circle(at, PandemicRadius, Team.Players).ToArray())
                ally.ServerHeal(ally.Max.Value * PandemicHeal);
            PandemicFxRpc(at, PandemicRadius);
            return n;
        }

        private bool Afflict(Health h, float seconds, float burst)
        {
            if (h == null || !h.IsAlive) return false;
            float p = BlightPower;
            plagued[h] = (Time.time + seconds, this);
            if (h.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerAddThreat(NetworkObjectId, 10f);
            h.ServerBlight(seconds, BlightDps * PandemicRot * p, NetworkObjectId);
            if (burst > 0f) h.Tag("Pandemic").ServerDamage(burst * p, transform.position, NetworkObjectId);
            return true;
        }

        /// <summary>Server: a plagued death bursts: everything near catches what was left of it.</summary>
        public void ServerPandemicSpread(Vector3 at, float left)
        {
            float seconds = Mathf.Max(PandemicMinSpread, left);
            foreach (var h in CombatQuery.Circle(at, PandemicSpread, Team.Enemies).ToArray())
                if (!plagued.TryGetValue(h, out var pl) || pl.until - Time.time < seconds - 0.5f) Afflict(h, seconds, PandemicBurst * PandemicSpreadBurst);
            PandemicBurstFxRpc(at);
        }

        /// <summary>Server: is this creature plagued (+25% damage taken)?</summary>
        public static bool IsPlagued(Health h) => h != null && plagued.TryGetValue(h, out var pl) && Time.time < pl.until;

        [Rpc(SendTo.ClientsAndHost)]
        private void PandemicFxRpc(Vector3 at, float radius)
        {
            Sfx.Play(Sound.Slime, at, 1f, 0.6f);
            Sfx.Play(Sound.Toll, at, 0.6f, 0.7f);
            Vfx.Embers(at + Vector3.up, 30, Plague, 6f, 0.06f, 1f);
            // 2026-11-21 (distinct abilities): black veins creep over the floor from the Doctor to every creature it takes,
            // and the flies go to each of them (who is plagued, at a glance).
            var taken = new List<Vector3>();
            foreach (var h in Health.All)
            {
                if (taken.Count >= 12) break;
                if (h == null || h.Team != Team.Enemies || !h.IsAlive || Vector3.Distance(h.transform.position, at) > radius) continue;
                taken.Add(h.transform.position);
                ClassFx.Flies(h.transform, h.transform.position, 0.7f, Mathf.Max(1.2f, (h.Center.y - h.transform.position.y) * 1.8f), 5f, 9);
            }
            ClassFx.Veins(at, taken, 3.5f, 0.55f);
            ClassFx.Shock(at, radius, Plague, 0.6f, 0.6f);
            ClassFx.Flies(null, Vfx.Floor(at), 1.2f, 1.4f, 3f, 14);
            UI.GameHud.ShowCombo(at + Vector3.up * 2.4f, "PANDEMIC", Plague);
            if (IsOwner) player.AddShake(0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PandemicBurstFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slime, at, 0.8f, 0.8f);
            Vfx.Smoke(at + Vector3.up * 0.8f, 3, 1f, 1.8f);
            Vfx.Embers(at + Vector3.up, 14, Plague, 4f, 0.05f, 0.7f);
            // A plagued death bursts: short veins split out of it and its flies hang over the spot.
            var spokes = new List<Vector3>();
            for (int i = 0; i < 5; i++) spokes.Add(at + Quaternion.Euler(0f, i * 72f + Random.Range(-20f, 20f), 0f) * Vector3.forward * PandemicSpread * Random.Range(0.3f, 0.5f));
            ClassFx.Veins(at, spokes, 2.2f, 0.3f);
            ClassFx.Flies(null, Vfx.Floor(at), 0.8f, 1f, 2.5f, 8);
        }

        private static readonly List<PlagueDoctorKit> all = new List<PlagueDoctorKit>();

        /// <summary>Server: the +25% hook in <see cref="Health"/> (named for the old cloud).</summary>
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
                    // Plague Wind (rank 5 B): it ends by blowing outward.
                    if (!c.Black && !c.Small && progress.B(AbilitySlot.Q, 4))
                    {
                        foreach (var h in CombatQuery.Circle(c.At, WindRadius, Team.Enemies).ToArray()) h.ServerBlight(BlightSeconds, BlightDps * p, NetworkObjectId);
                        progress.AbilityBurstRpc(c.At, WindRadius, PlayerProgress.FxKind.Poison);
                    }
                    continue;
                }
                bool mine = !c.Black;
                // Creeping Cloud (rank 2 A): it drifts toward the nearest enemy.
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
                foreach (var h in CombatQuery.Circle(c.At, c.Radius, Team.Enemies).ToArray())
                {
                    h.ServerBlight(1.5f, c.Dps * p, NetworkObjectId);
                    if (c.Black && h.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerAddThreat(NetworkObjectId, 2f);
                    // Clinging Fog (rank 2 B).
                    if (mine && progress.B(AbilitySlot.Q, 1)) h.ServerChill(1f);
                    // Choking (rank 4 A): their wind-ups are cut short.
                    if (choke && !AbilityKit.IsBoss(h)) AbilityKit.Stun(h, 0.35f);
                }
                if (choke) progress.AbilityTextRpc(c.At + Vector3.up * 2f, "CHOKING", PlayerProgress.FxKind.Poison);
                // Fog of Mercy (rank 3 B): allies inside are healed.
                if (mine && progress.B(AbilitySlot.Q, 2))
                    foreach (var ally in CombatQuery.Circle(c.At, c.Radius, Team.Players).ToArray()) ally.ServerHeal(ally.Max.Value * 0.03f * 0.5f);
            }
        }

        public void ServerContagion(Vector3 at)
        {
            // Pestilence mastery: everything near catches it; otherwise the nearest one.
            bool all = progress.Mastered(1);
            Health nearest = null;
            float best = ContagionRadius;
            foreach (var h in CombatQuery.Circle(at, ContagionRadius, Team.Enemies).ToArray())
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

        /// <summary>Server: the mask. Cleanse, heal and ward the party nearby. Public for the smoke test.</summary>
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
                // Last Breath (rank 5 B): one downed ally in reach is lifted.
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
                    // Purge (rank 3 A): what is cleansed burns the enemies round them.
                    if (progress.A(AbilitySlot.E, 2) && db.AnyAffliction)
                    {
                        AbilityKit.Blast(ally.transform.position, 3f, VialDamage * 2f * BlightPower, NetworkObjectId, "Purge", h => h.ServerBlight(BlightSeconds, BlightDps * BlightPower, NetworkObjectId));
                        progress.AbilityBurstRpc(ally.transform.position, 3f, PlayerProgress.FxKind.Poison);
                    }
                    db.ServerCleanse();
                    db.ServerWard(ward);
                }
                ally.Health.ServerHeal(ally.Health.Max.Value * MaskHeal * potency);
                // Bloodlust (rank 2 A): the breath steadies the hand too.
                if (progress.A(AbilitySlot.E, 1) && ally.TryGetComponent<PlayerProgress>(out var bp)) bp.BloodlustEnds.Value = System.Math.Max(bp.BloodlustEnds.Value, NetworkManager.ServerTime.Time + 6.0);
                // Vapour Shield (rank 2 B).
                if (progress.B(AbilitySlot.E, 1)) ally.Health.ServerShield(ally.Health.Max.Value * 0.15f, 8f);
                // Deep Breath (rank 3 B): it mends while the ward holds.
                if (progress.B(AbilitySlot.E, 2)) { var who = ally; AbilityKit.Repeat(this, ward, 0.5f, () => { if (who != null && who.Health.IsAlive && !who.Downed.Value) who.Health.ServerHeal(who.Health.Max.Value * 0.01f); }); }
                n++;
            }
            // Exhale (rank 4 A): a cloud at your feet.
            if (progress.A(AbilitySlot.E, 3)) AddCloud(transform.position, MiasmaRadius, MiasmaSeconds, MiasmaDps * progress.Potency(AbilitySlot.Q), false);
            // Repel (rank 4 B).
            if (progress.B(AbilitySlot.E, 3))
                foreach (var h in CombatQuery.Circle(transform.position, RepelRadius, Team.Enemies).ToArray())
                {
                    var away = h.transform.position - transform.position; away.y = 0f;
                    AbilityKit.Push(h, away.normalized * 12f);
                    AbilityKit.Stun(h, 0.5f);
                }
            // Plague Lord (rank 5 A).
            if (progress.A(AbilitySlot.E, 4))
            {
                foreach (var h in CombatQuery.Circle(transform.position, LordRadius, Team.Enemies).ToArray()) h.ServerBlight(BlightSeconds * 1.5f, BlightDps * BlightPower, NetworkObjectId);
                progress.AbilityBurstRpc(transform.position, LordRadius, PlayerProgress.FxKind.Poison);
            }
            MaskFxRpc(transform.position);
            return n;
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        private static readonly Color Plague = new Color(0.55f, 1f, 0.35f);
        private static readonly Color TonicColor = new Color(1f, 0.55f, 0.6f);

        [Rpc(SendTo.ClientsAndHost)]
        private void VialFxRpc(Vector3 origin, Vector3 direction, bool tonic)
        {
            Arrows.SpawnVisual(origin, direction, tonic ? ProjectileKind.Tonic : ProjectileKind.Vial, VialSpeed, VialRange * RangeMul, VialGravity);
            if (!IsOwner) { player.Rig?.Play(RigAction.Cast, 0.25f); Sfx.Play(Sound.Swing, transform.position, 0.35f, 1.7f); }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SplashFxRpc(Vector3 at, float radius, bool tonic)
        {
            var c = tonic ? TonicColor : Plague;
            Sfx.Play(Sound.Shatter, at, 0.6f, tonic ? 1.6f : 1.3f);
            Sfx.Play(tonic ? Sound.Potion : Sound.Splash, at, 0.5f, 1.2f);
            Vfx.Embers(at + Vector3.up * 0.3f, 6, c, 2.5f, 0.05f, 0.7f);
            if (tonic) SmokeCloud.Spawn(at, radius * 0.8f, 0.9f, TonicColor);
            // 2026-11-21 (distinct abilities): the flask bursts in glass; a vial leaves a bubbling pool of sick green, a tonic
            // a rose mist lifting off the floor.
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
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.3f);
            // 2026-11-21 (distinct abilities): the Miasma is the cloud with the FLIES in it; they ride it where it drifts.
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
            Sfx.Play(Sound.Potion, at + Vector3.up, 0.9f, 0.8f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.6f, 1.3f);
            // 2026-11-21 (distinct abilities): a ghostly beaked mask rises over the Doctor; a ring of pale smoke lifts round
            // every ally it cleanses and wards.
            var warded = new List<Transform>();
            foreach (var ally in PlayerLife.All)
                if (ally != null && !ally.Downed.Value && Vector3.Distance(ally.transform.position, at) <= MaskRadius) warded.Add(ally.transform);
            ClassFx.PlagueMask(transform, warded);
        }
    }
}
