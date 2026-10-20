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
    /// Reagents (the resource) refill steadily; the Doctor is limited by aim and cooldowns.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlagueDoctorKit : NetworkBehaviour, IClassKit
    {
        public const float VialInterval = 0.55f, VialCost = 7f, VialDamage = 15f, VialSpeed = 19f, VialRange = 20f, VialGravity = 9f, SplashRadius = 1.7f;
        public const float BlightSeconds = 4f, BlightDps = 9f;
        public const float TonicCooldown = 7f, TonicCost = 25f, TonicRadius = 3f, TonicHeal = 0.2f;
        public const float MiasmaCooldown = 12f, MiasmaRadius = 4f, MiasmaSeconds = 6f, MiasmaDps = 9f;
        public const float MaskCooldown = 18f, MaskRadius = 8f, MaskHeal = 0.1f, MaskWard = 4f;
        public const float ReagentMax = 100f, ReagentRegen = 16f;
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

        private struct Cloud { public Vector3 At; public float Radius, Until, Dps; public bool Black; }
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
            Health.InPlagueCloud = InBlackDeath;
            if (contagionHooked) return;
            contagionHooked = true;
            // Contagion: a blighted death passes the blight on (to everything near, at Pestilence mastery).
            Health.BlightedDied += (dead, source) =>
            {
                if (dead == null || dead.Team != Team.Enemies) return;
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(source, out var obj) || !obj.TryGetComponent<PlagueDoctorKit>(out var kit)) return;
                kit.ServerContagion(dead.transform.position);
            };
        }

        public override void OnNetworkDespawn() => all.Remove(this);

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer && clouds.Count > 0 && Time.time >= nextCloudTick) TickClouds();
            if (!IsOwner || !Active) return;

            Reagents = Mathf.Min(ReagentMax, Reagents + ReagentRegen * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier;
            motor.CanSprint = true;
            if (!life.CanAct || !motor.ActionsReady) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            // --- LMB held: Vials ---
            if (mouse != null && mouse.leftButton.isPressed && Time.time >= nextVial && Reagents >= VialCost)
            {
                Reagents -= VialCost;
                nextVial = Time.time + VialInterval * progress.AttackTimeMultiplier;
                player.ViewModelCast(false);
                Sfx.Play(Sound.Swing, transform.position, 0.35f, 1.7f);
                VialRpc(player.AimOrigin + player.AimForward * 0.6f, LobDirection());
            }

            // --- RMB: Tonic ---
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && Time.time >= tonicReadyAt && Reagents >= TonicCost)
            {
                Reagents -= TonicCost;
                tonicReadyAt = Time.time + TonicCooldown * (progress.OnPath(2) ? 0.85f : 1f);
                player.ViewModelCast(false);
                TonicRpc(player.AimOrigin + player.AimForward * 0.6f, LobDirection());
            }

            // --- Q: Miasma ---
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= miasmaReadyAt)
            {
                miasmaReadyAt = Time.time + MiasmaCooldown * progress.CooldownFor(AbilitySlot.Q);
                player.ViewModelCast(false);
                MiasmaRpc(AimGround(18f * RangeMul));
            }

            // --- E: Plague Mask ---
            if (keyboard != null && UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= maskReadyAt)
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
            var o = player.AimOrigin;
            var dir = player.AimForward;
            if (Physics.Raycast(o, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
            var far = o + dir * range;
            return Physics.Raycast(far, Vector3.down, out var down, 20f, ~0, QueryTriggerInteraction.Ignore) ? down.point : transform.position + transform.forward * 6f;
        }

        // ==================================================================
        // Server
        // ==================================================================

        private float BlightPower => progress.AttackMultiplier * (progress.OnPath(1) ? 1.4f : 1f);

        [Rpc(SendTo.Server)]
        private void VialRpc(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct) return;
            ServerVial(origin, direction);
        }

        /// <summary>Server: throw one vial. Public for the smoke test.</summary>
        public void ServerVial(Vector3 origin, Vector3 direction)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            ulong me = NetworkObjectId;
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, VialSpeed, h => { }, false, VialRange * RangeMul, VialGravity,
                at => { if (this != null) ServerSplash(at); });
            VialFxRpc(origin, direction, false);
        }

        /// <summary>Server: a vial shatters here: damage and blight in a small splash.</summary>
        public void ServerSplash(Vector3 at)
        {
            float radius = SplashRadius * (progress.OnPath(1) ? 1.4f : 1f);
            float p = BlightPower;
            foreach (var h in CombatQuery.Circle(at, radius, Team.Enemies).ToArray())
            {
                h.ServerDamage(VialDamage * p, at, NetworkObjectId);
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
            float heal = TonicHeal * (progress.OnPath(2) ? 1.4f : 1f);
            int n = 0;
            foreach (var ally in PlayerLife.All)
            {
                if (ally == null || ally.Downed.Value || !ally.Health.IsAlive) continue;
                if (Vector3.Distance(ally.transform.position, at) > TonicRadius + 0.6f) continue;
                ally.Health.ServerHeal(ally.Health.Max.Value * heal);
                if (progress.OnPath(2)) ally.Health.ServerShield(20f, 6f);
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
            float radius = MiasmaRadius * (progress.Twist(AbilitySlot.Q) ? 1.5f : 1f);
            clouds.Add(new Cloud { At = at, Radius = radius, Until = Time.time + MiasmaSeconds, Dps = MiasmaDps * potency });
            CloudFxRpc(at, radius, MiasmaSeconds, false);
        }

        /// <summary>Server: the ultimate, Black Death: a vast cloud that rots everything inside.</summary>
        public void ServerBlackDeath(Vector3 at)
        {
            clouds.Add(new Cloud { At = at, Radius = 8f, Until = Time.time + 8f, Dps = MiasmaDps * 2.2f, Black = true });
            CloudFxRpc(at, 8f, 8f, true);
        }

        /// <summary>Server: is this spot inside a Black Death cloud (+25% damage taken)?</summary>
        private static readonly List<PlagueDoctorKit> all = new List<PlagueDoctorKit>();

        public static bool InBlackDeath(Vector3 at)
        {
            foreach (var k in all)
                if (k != null)
                foreach (var c in k.clouds)
                    if (c.Black && Time.time < c.Until && Vector3.Distance(at, c.At) < c.Radius) return true;
            return false;
        }

        private void TickClouds()
        {
            nextCloudTick = Time.time + 0.5f;
            float p = BlightPower;
            for (int i = clouds.Count - 1; i >= 0; i--)
            {
                var c = clouds[i];
                if (Time.time >= c.Until) { clouds.RemoveAt(i); continue; }
                foreach (var h in CombatQuery.Circle(c.At, c.Radius, Team.Enemies).ToArray())
                {
                    h.ServerBlight(1.5f, c.Dps * p, NetworkObjectId);
                    if (c.Black && h.TryGetComponent<Enemies.EnemyBrain>(out var b)) b.ServerAddThreat(NetworkObjectId, 2f);
                }
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
            int n = 0;
            foreach (var ally in PlayerLife.All)
            {
                if (ally == null || ally.Downed.Value || !ally.Health.IsAlive || Vector3.Distance(ally.transform.position, transform.position) > MaskRadius) continue;
                if (ally.TryGetComponent<PlayerDebuffs>(out var db)) { db.ServerCleanse(); db.ServerWard(MaskWard); }
                ally.Health.ServerHeal(ally.Health.Max.Value * MaskHeal * potency);
                // Rank 3: the mask's breath steadies the hand too.
                if (progress.Twist(AbilitySlot.E) && ally.TryGetComponent<PlayerProgress>(out var ap)) ap.BloodlustEnds.Value = System.Math.Max(ap.BloodlustEnds.Value, NetworkManager.ServerTime.Time + 6.0);
                n++;
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
            Vfx.Ring(at + Vector3.up * 0.05f, radius, c, 0.35f);
            Vfx.Embers(at + Vector3.up * 0.3f, 10, c, 2.5f, 0.05f, 0.7f);
            if (tonic) SmokeCloud.Spawn(at, radius * 0.8f, 0.9f, TonicColor);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CloudFxRpc(Vector3 at, float radius, float seconds, bool black)
        {
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.8f, black ? 0.5f : 0.8f);
            SmokeCloud.Spawn(at, radius, seconds, black ? new Color(0.15f, 0.28f, 0.08f) : new Color(0.35f, 0.7f, 0.2f));
            Vfx.Ring(at + Vector3.up * 0.05f, radius, black ? new Color(0.25f, 0.6f, 0.15f) : Plague, 0.5f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ContagionFxRpc(Vector3 at)
        {
            Vfx.Embers(at + Vector3.up, 12, Plague, 3f, 0.05f, 0.7f);
            UI.GameHud.ShowCombo(at + Vector3.up * 1.4f, "CONTAGION", Plague);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MaskFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Potion, at + Vector3.up, 0.9f, 0.8f);
            Sfx.Play(Sound.Whisper, at + Vector3.up, 0.6f, 1.3f);
            Vfx.Ring(at + Vector3.up * 0.05f, MaskRadius, new Color(0.7f, 1f, 0.65f), 0.5f);
            Vfx.Embers(at + Vector3.up, 16, new Color(0.75f, 1f, 0.7f), 3f, 0.05f, 0.8f);
        }
    }
}
