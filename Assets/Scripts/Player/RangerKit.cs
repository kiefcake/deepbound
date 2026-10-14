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
    /// <item><b>Q — Bear Trap.</b> Set a trap at your crosshair. The first enemy to step in it
    ///   is snapped, held, and hurt. Up to two at once.</item>
    /// <item><b>E — Volley.</b> A blue circle at your crosshair, then arrows rain on it for two
    ///   seconds.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class RangerKit : NetworkBehaviour, IClassKit
    {
        public const float DrawMin = 0.15f, DrawFull = 0.85f, ArrowMinDamage = 12f, ArrowMaxDamage = 42f, ArrowSpeed = 34f;
        public const float DisengageCooldown = 6f, DisengageFocus = 25f;
        public const float TrapCooldown = 10f, TrapRange = 9f;
        public const float VolleyCooldown = 16f, VolleyFocus = 40f, VolleyRadius = 4f, VolleyDelay = 0.8f, VolleyDamage = 11f;
        public const int VolleyWaves = 6;
        public const float FocusMax = 100f, FocusRegen = 14f;
        public const int MarkHits = 3;
        public const float MarkSeconds = 6f;

        [SerializeField] private NetworkObject trapPrefab;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        public float Focus { get; private set; } = FocusMax;
        private float drawStart = -1f;
        private float disengageReadyAt, trapReadyAt, volleyReadyAt;

        // Server.
        private ulong markTarget;
        private int markCount;
        private readonly List<NetworkObject> traps = new List<NetworkObject>();
        private struct Volley { public Vector3 At; public float Next; public int Left; }
        private readonly List<Volley> volleys = new List<Volley>();
        private float lastServerShot;

        public void SetTrapPrefab(NetworkObject prefab) => trapPrefab = prefab;

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
        public float Charge => drawStart < 0f ? 0f : Mathf.Clamp01((Time.time - drawStart) / DrawFull);

        public void FillAbilities(List<AbilityView> into)
        {
            float cd = progress.CooldownMultiplier;
            into.Add(new AbilityView { Key = "LMB", Name = "Longbow", Active = drawStart >= 0f });
            into.Add(new AbilityView { Key = "RMB", Name = "Disengage", Remaining = disengageReadyAt - Time.time, Cooldown = DisengageCooldown * cd, Unaffordable = Focus < DisengageFocus });
            into.Add(new AbilityView { Key = "Q", Name = "Bear Trap", Remaining = trapReadyAt - Time.time, Cooldown = TrapCooldown * progress.CooldownFor(AbilitySlot.Q) });
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

            bool canAct = life.CanAct && motor.HasControl;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (!canAct)
            {
                drawStart = -1f;
                return;
            }

            // --- LMB: draw while held, loose on release ---
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) drawStart = Time.time;
            if (mouse != null && drawStart >= 0f && !mouse.leftButton.isPressed)
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
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && Time.time >= disengageReadyAt && Focus >= DisengageFocus)
            {
                Focus -= DisengageFocus;
                disengageReadyAt = Time.time + DisengageCooldown * progress.CooldownMultiplier;
                var back = -transform.forward;
                back.y = 0f;
                motor.AddImpulse(back.normalized * 17f + Vector3.up * 6f);
                DisengageFxRpc();
            }

            // --- Q: Bear Trap at the crosshair ---
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame && Time.time >= trapReadyAt && TryAimFloor(TrapRange, out var trapAt))
            {
                trapReadyAt = Time.time + TrapCooldown * progress.CooldownFor(AbilitySlot.Q) * (progress.OnPath(2) ? 0.6f : 1f);
                TrapRpc(trapAt);
            }

            // --- E: Volley at the crosshair ---
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && Time.time >= volleyReadyAt && Focus >= VolleyFocus && TryAimFloor(30f, out var volleyAt))
            {
                Focus -= VolleyFocus;
                volleyReadyAt = Time.time + VolleyCooldown * progress.CooldownFor(AbilitySlot.E);
                player.ViewModelCast(true);
                VolleyRpc(volleyAt);
            }
        }

        private bool TryAimFloor(float range, out Vector3 spot)
        {
            spot = default;
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            var point = Physics.Raycast(origin, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore) ? hit.point : origin + dir * range;
            if (!Physics.Raycast(point + Vector3.up * 0.5f - dir * 0.3f, Vector3.down, out var floor, 12f, ~0, QueryTriggerInteraction.Ignore)) return false;
            spot = floor.point;
            return true;
        }

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
            bool pierce = charge >= 1f;
            if (pierce && progress.OnPath(1)) dmg *= 1.4f; // Sharpshooter
            bool instantMark = pierce && progress.Mastered(1);
            ulong me = NetworkObjectId;
            var from = transform.position;
            Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, ArrowSpeed * (0.7f + 0.3f * charge), h =>
            {
                h.ServerDamage(dmg * (pierce && h.Team == Team.Enemies ? Combos.TryShatter(h, progress) : 1f), from, me);
                if (this == null) return;
                if (instantMark && h.Team == Team.Enemies)
                {
                    h.ServerMark(MarkSeconds);
                    MarkFxRpc(h.Center);
                }
                else CountMark(h);
            }, pierce);
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

        [Rpc(SendTo.Server)]
        private void TrapRpc(Vector3 at) => ServerPlaceTrap(at);

        /// <summary>Server: set a bear trap. Public for the smoke test.</summary>
        public void ServerPlaceTrap(Vector3 at)
        {
            if (!life.CanAct || trapPrefab == null || Vector3.Distance(at, transform.position) > TrapRange + 2f) return;
            traps.RemoveAll(t => t == null || !t.IsSpawned);
            if (traps.Count >= (progress.OnPath(2) ? 3 : 2))
            {
                traps[0].Despawn();
                traps.RemoveAt(0);
            }
            var trap = Instantiate(trapPrefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            trap.Spawn(destroyWithScene: true);
            trap.GetComponent<World.BearTrap>().ServerArm(progress.AttackMultiplier * progress.Potency(AbilitySlot.Q), NetworkObjectId, progress.Twist(AbilitySlot.Q));
            traps.Add(trap);
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
                    h.ServerDamage(VolleyDamage * progress.AttackMultiplier * progress.Potency(AbilitySlot.E), v.At + Vector3.up * 6f, NetworkObjectId);
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
            Arrows.SpawnVisual(origin, direction, ProjectileKind.Arrow, ArrowSpeed * (0.7f + 0.3f * charge));
            Sfx.Play(Sound.Bow, origin, 0.6f + 0.4f * charge, 1.2f - 0.3f * charge);
            if (!IsOwner) player.Rig?.Play(RigAction.Draw, 0.2f);
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
