using System.Collections.Generic;
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
    /// The Ranger's kit: range, control, and making everything else hit harder.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Hunter's Mark.</b> Three hits on the same enemy mark it for six
    ///   seconds: it takes +25% damage from the whole party.</item>
    /// <item><b>LMB — Longbow.</b> Hold to draw, release to loose. Damage grows with the draw;
    ///   a full draw pierces through every enemy in line.</item>
    /// <item><b>RMB — Disengage.</b> Leap backwards and up, out of reach.</item>
    /// <item><b>Q — Snare Shot.</b> (2026-11-01, replacing the Bear Trap, which waited for one enemy to walk onto a
    ///   plate.) Loose a weighted net-arrow: where it strikes, a net bursts open and everything under it is held, hurt,
    ///   and SNARED: your arrows and Volley hit it harder while it struggles.</item>
    /// <item><b>E — Volley.</b> A blue circle at your crosshair, then arrows rain on it for two
    ///   seconds.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class RangerKit : NetworkBehaviour, IClassKit
    {
        public const float DrawMin = 0.15f, DrawFull = 0.85f, ArrowMinDamage = 12f, ArrowMaxDamage = 42f, ArrowSpeed = 34f;
        /// <summary>A snap shot carries 20 m; a full draw 34 m (x the anvil / Farsight range multiplier).</summary>
        public const float ArrowMinRange = 20f, ArrowMaxRange = 34f;
        /// <summary>Arrows drop: a snap shot falls fast, a full draw flies flat.</summary>
        public static float ArrowGravity(float charge) => Mathf.Lerp(7f, 2.5f, Mathf.Clamp01(charge));
        public static float ArrowRange(float charge, float mul) => Mathf.Lerp(ArrowMinRange, ArrowMaxRange, Mathf.Clamp01(charge)) * mul;
        public float AttackRange => ArrowRange(Mathf.Max(Charge, 1f), progress != null ? progress.RangeMultiplier : 1f);
        private float RangeMul => progress != null ? progress.RangeMultiplier : 1f;
        public const float DisengageCooldown = 6f, DisengageFocus = 35f;
        public const float SnareCooldown = 9f, SnareFocus = 20f, SnareRadius = 3f, SnareHold = 2f, SnareDamage = 18f;
        public const float SnareSpeed = 30f, SnareRange = 26f, SnareGravity = 4f;
        /// <summary>Snared: the Ranger's arrows and Volley deal this much more for <see cref="SnareSeconds"/>.</summary>
        public const float SnareExposed = 1.25f, SnareSeconds = 4f;
        public const float VolleyCooldown = 16f, VolleyFocus = 55f, VolleyRadius = 4f, VolleyDelay = 0.8f, VolleyDamage = 11f;
        public const int VolleyWaves = 6;
        public const float FocusMax = 100f, FocusRegen = 11f;
        public const int MarkHits = 3;
        public const float MarkSeconds = 6f;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        public float Focus { get; private set; } = FocusMax;
        private float drawStart = -1f;
        private float disengageReadyAt, snareReadyAt, volleyReadyAt;

        // Server.
        private ulong markTarget;
        private int markCount;
        private readonly Dictionary<ulong, float> snaredUntil = new Dictionary<ulong, float>();
        /// <summary>Tests: how many the last snare caught.</summary>
        public int LastSnareCaught { get; private set; } = -1;
        private struct Volley { public Vector3 At; public float Next; public int Left; }
        private readonly List<Volley> volleys = new List<Volley>();
        private float lastServerShot;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Ranger;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Ranger;
        public string ResourceName => "Focus";
        public Color ResourceColor => new Color(0.45f, 0.85f, 0.4f);
        public float Resource => Focus;
        public float ResourceMax => FocusMax;
        public void RestoreResource(float fraction) => Focus = Mathf.Min(ResourceMax, Focus + ResourceMax * fraction);
        public void ShaveCooldowns(float seconds) { snareReadyAt -= seconds; volleyReadyAt -= seconds; }
        public float Charge => drawStart < 0f ? 0f : Mathf.Clamp01((Time.time - drawStart) / DrawFull);

        public void FillAbilities(List<AbilityView> into)
        {
            float cd = progress.CooldownMultiplier;
            into.Add(new AbilityView { Key = "LMB", Name = "Longbow", Active = drawStart >= 0f });
            into.Add(new AbilityView { Key = "RMB", Name = "Disengage", Remaining = disengageReadyAt - Time.time, Cooldown = DisengageCooldown * cd, Unaffordable = Focus < DisengageFocus });
            into.Add(new AbilityView { Key = "Q", Name = "Snare Shot", Remaining = snareReadyAt - Time.time, Cooldown = SnareCooldownNow, Unaffordable = Focus < SnareFocus });
            into.Add(new AbilityView { Key = "E", Name = "Volley", Remaining = volleyReadyAt - Time.time, Cooldown = VolleyCooldown * progress.CooldownFor(AbilitySlot.E), Unaffordable = Focus < VolleyFocus });
        }

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer) ServerVolleyTick();
            if (!IsOwner || !Active)
            {
                drawStart = -1f;
                return;
            }

            Focus = Mathf.Min(FocusMax, Focus + FocusRegen * Time.deltaTime);
            // Drawing a bow slows you; that is the price of the big shot.
            motor.SpeedMultiplier = progress.SpeedMultiplier * (drawStart >= 0f ? 0.6f : 1f);
            motor.CanSprint = drawStart < 0f;

            bool canAct = life.CanAct && motor.ActionsReady;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (!canAct)
            {
                drawStart = -1f;
                return;
            }

            // --- LMB: draw while held, loose on release ---
            if (UI.Binds.PrimaryDown) drawStart = Time.time;
            if (drawStart >= 0f && !UI.Binds.PrimaryHeld)
            {
                float held = Time.time - drawStart;
                drawStart = -1f;
                if (held >= DrawMin)
                {
                    float charge = Mathf.Clamp01(held / (DrawFull * progress.AttackTimeMultiplier));
                    player.ViewModelCast(charge >= 1f);
                    ShootRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward, charge);
                }
            }

            // --- RMB: Disengage ---
            if (UI.Binds.SecondaryDown && Time.time >= disengageReadyAt && Focus >= DisengageFocus)
            {
                Focus -= DisengageFocus;
                disengageReadyAt = Time.time + DisengageCooldown * progress.CooldownMultiplier;
                var back = -transform.forward;
                back.y = 0f;
                motor.AddImpulse(back.normalized * 17f + Vector3.up * 6f);
                DisengageFxRpc();
            }

            // --- Q: Snare Shot along the crosshair ---
            if (UI.Binds.Down(UI.Bind.AbilityQ) && Time.time >= snareReadyAt && Focus >= SnareFocus)
            {
                Focus -= SnareFocus;
                snareReadyAt = Time.time + SnareCooldownNow;
                player.ViewModelCast(true);
                SnareRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward);
            }

            // --- E: Volley at the crosshair ---
            if (UI.Binds.Down(UI.Bind.AbilityE) && Time.time >= volleyReadyAt && Focus >= VolleyFocus && TryAimFloor(30f, out var volleyAt))
            {
                Focus -= VolleyFocus;
                volleyReadyAt = Time.time + VolleyCooldown * progress.CooldownFor(AbilitySlot.E);
                player.ViewModelCast(true);
                VolleyRpc(volleyAt);
            }
        }

        private bool TryAimFloor(float range, out Vector3 spot) => player.TryAimFloor(range, out spot);

        // ==================================================================
        // Server
        // ==================================================================

        [Rpc(SendTo.Server)]
        private void ShootRpc(Vector3 origin, Vector3 direction, float charge) => ServerShoot(origin, direction, charge);

        /// <summary>Server: loose an arrow at a given draw (0-1). Public for the smoke test.</summary>
        public void ServerShoot(Vector3 origin, Vector3 direction, float charge)
        {
            if (!life.CanAct || Time.time - lastServerShot < DrawMin * 0.8f) return;
            lastServerShot = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;

            charge = Mathf.Clamp01(charge);
            float dmg = Mathf.Lerp(ArrowMinDamage, ArrowMaxDamage, charge) * progress.AttackMultiplier;
            var kit = this;
            bool pierce = charge >= 1f;
            if (pierce && progress.OnPath(1)) dmg *= 1.4f; // Sharpshooter
            bool instantMark = pierce && progress.Mastered(1);
            ulong me = NetworkObjectId;
            var from = transform.position;
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, ArrowSpeed * (0.7f + 0.3f * charge), h =>
            {
                h.ServerDamage(dmg * (pierce && h.Team == Team.Enemies ? Combos.TryShatter(h, progress) : 1f) * (kit != null ? kit.Snared(h) : 1f), from, me);
                if (this == null) return;
                if (instantMark && h.Team == Team.Enemies)
                {
                    h.ServerMark(MarkSeconds);
                    MarkFxRpc(h.Center);
                }
                else CountMark(h);
            }, pierce, ArrowRange(charge, RangeMul), ArrowGravity(charge));
            ShotFxRpc(origin, direction, charge);
        }

        /// <summary>Hunter's Mark: three hits on the same enemy in a row marks it.</summary>
        private void CountMark(Health h)
        {
            if (h.Team != Team.Enemies) return;
            if (h.NetworkObjectId != markTarget)
            {
                markTarget = h.NetworkObjectId;
                markCount = 0;
            }
            if (++markCount >= MarkHits)
            {
                markCount = 0;
                h.ServerMark(MarkSeconds);
                MarkFxRpc(h.Center);
            }
        }

        // ---- Q: Snare Shot ----

        /// <summary>The Trapper path: snares come back 40% sooner and burst 40% wider.</summary>
        private float SnareCooldownNow => SnareCooldown * progress.CooldownFor(AbilitySlot.Q) * (progress.OnPath(2) ? 0.6f : 1f);
        public float SnareRadiusNow => SnareRadius * (progress != null && progress.OnPath(2) ? 1.4f : 1f);

        /// <summary>Server: the extra the Ranger's own hits deal to something still struggling in a net.</summary>
        public float Snared(Health h) => h != null && snaredUntil.TryGetValue(h.NetworkObjectId, out var until) && Time.time < until ? SnareExposed : 1f;

        [Rpc(SendTo.Server)]
        private void SnareRpc(Vector3 origin, Vector3 direction) => ServerSnareShot(origin, direction);

        /// <summary>Server: loose the net-arrow; it bursts where it strikes (an enemy, a wall, the floor). Public for the smoke test.</summary>
        public void ServerSnareShot(Vector3 origin, Vector3 direction)
        {
            if (!life.CanAct) return;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;
            // No damage of its own in flight: the net does the work where it lands.
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, NetworkObjectId, SnareSpeed, h => { }, false, SnareRange * RangeMul, SnareGravity,
                at => { if (this != null) ServerSnareBurst(at); });
            SnareShotFxRpc(origin, direction);
        }

        /// <summary>Server: the net opens at <paramref name="at"/>. Public for the smoke test.</summary>
        public void ServerSnareBurst(Vector3 at)
        {
            // Lie on the floor under where it struck (an arrow in a wall drops its net at the wall's foot).
            var ground = Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Health>() == null ? hit.point : at;
            float r = SnareRadiusNow;
            int caught = 0;
            foreach (var h in CombatQuery.Circle(ground, r, Team.Enemies).ToArray())
            {
                if (Mathf.Abs(h.transform.position.y - ground.y) > 2.5f) continue;
                caught++;
                h.ServerDamage(SnareDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.Q), ground + Vector3.up, NetworkObjectId);
                if (!h.IsAlive) continue;
                // Bosses shrug a stun off sooner (their own ServerStun), but are snared for the full time.
                if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(SnareHold);
                if (h.TryGetComponent<Enemies.BossBrain>(out var boss)) boss.ServerAddStagger(Enemies.BossBrain.StaggerStun);
                snaredUntil[h.NetworkObjectId] = Time.time + SnareSeconds;
                if (progress.Twist(AbilitySlot.Q)) { h.ServerMark(MarkSeconds); MarkFxRpc(h.Center); }
            }
            LastSnareCaught = caught;
            if (snaredUntil.Count > 64)
            {
                var stale = new List<ulong>();
                foreach (var kv in snaredUntil) if (kv.Value < Time.time) stale.Add(kv.Key);
                foreach (var k in stale) snaredUntil.Remove(k);
            }
            SnareBurstFxRpc(ground, r, caught);
        }

        [Rpc(SendTo.Server)]
        private void VolleyRpc(Vector3 at)
        {
            if (!life.CanAct) return;
            volleys.Add(new Volley { At = at, Next = Time.time + VolleyDelay, Left = progress.Mastered(2) ? 9 : VolleyWaves });
            VolleyWarnRpc(at);
        }

        private void ServerVolleyTick()
        {
            for (int i = volleys.Count - 1; i >= 0; i--)
            {
                var v = volleys[i];
                if (Time.time < v.Next) continue;
                foreach (var h in CombatQuery.Circle(v.At, progress.Mastered(2) ? 5f : VolleyRadius, Team.Enemies).ToArray())
                {
                    h.ServerDamage(VolleyDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.E) * Snared(h), v.At + Vector3.up * 6f, NetworkObjectId);
                    if (progress.Twist(AbilitySlot.E)) h.ServerChill(2f);
                    CountMark(h);
                }
                VolleyWaveRpc(v.At);
                v.Left--;
                v.Next = Time.time + 0.33f;
                if (v.Left <= 0) volleys.RemoveAt(i); else volleys[i] = v;
            }
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void ShotFxRpc(Vector3 origin, Vector3 direction, float charge)
        {
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Arrow, ArrowSpeed * (0.7f + 0.3f * charge), ArrowRange(charge, RangeMul), ArrowGravity(charge));
            Sfx.Play(Sound.Bow, origin, 0.6f + 0.4f * charge, 1.2f - 0.3f * charge);
            if (!IsOwner) player.Rig?.Play(RigAction.Draw, 0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnareShotFxRpc(Vector3 origin, Vector3 direction)
        {
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Arrow, SnareSpeed, SnareRange * RangeMul, SnareGravity);
            Sfx.Play(Sound.Bow, origin, 0.85f, 0.75f);
            if (!IsOwner) player.Rig?.Play(RigAction.Draw, 0.2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnareBurstFxRpc(Vector3 at, float radius, int caught)
        {
            SnareNet.Spawn(at, radius, SnareHold + 0.4f);
            Sfx.Play(Sound.Chain, at + Vector3.up * 0.4f, 0.7f, 1.25f);
            if (caught > 0) Sfx.Play(Sound.Block, at + Vector3.up * 0.4f, 0.6f, 0.8f);
            Vfx.Ring(at + Vector3.up * 0.04f, radius, new Color(0.62f, 0.5f, 0.32f), 0.35f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DisengageFxRpc()
        {
            Sfx.Play(Sound.Dodge, transform.position + Vector3.up, 0.8f, 0.9f);
            SmokePuff.Spawn(transform.position, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MarkFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Relic, at, 0.6f, 1.6f);
            var red = new Color(1f, 0.3f, 0.25f);
            Vfx.Ring(Vfx.Ground(at), 1.2f, red, 0.35f);
            Vfx.Embers(at, 8, red, 2.5f, 0.05f, 0.5f);
            // A hunter's mark: a sharp red star snapping onto the quarry.
            FxSprite.Billboard("spark_05", at + Vector3.up * 0.2f, Vector3.zero, 0.3f, 1.2f, 0.5f, new Color(red.r, red.g, red.b, 0.95f), 200f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void VolleyWarnRpc(Vector3 at)
        {
            Telegraph.Show(at, VolleyRadius, VolleyDelay, null, Telegraph.Friendly);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.5f);
        }

        /// <summary>Each wave: a handful of arrows dropping out of the dark onto the circle.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void VolleyWaveRpc(Vector3 at)
        {
            for (int i = 0; i < 5; i++)
            {
                var land = at + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * VolleyRadius * 0.8f;
                var from = land + new Vector3(Random.Range(-1.5f, 1.5f), 7f, Random.Range(-1.5f, 1.5f));
                Arrows.SpawnVisual(from, land - from, ProjectileKind.Arrow, 26f);
            }
            Sfx.Play(Sound.Hit, at + Vector3.up, 0.5f, 1.4f);
        }
    }
}
