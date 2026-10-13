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
    /// The Mage's kit: set up, then cash in.
    ///
    /// <list type="bullet">
    /// <item><b>Passive — Shatter.</b> Fire that hits a chilled enemy deals +75% and
    ///   consumes the chill, with a glassy crack you learn to listen for.</item>
    /// <item><b>LMB — Firebolt.</b> Hold to fire fast, cheap bolts.</item>
    /// <item><b>RMB — Frostbolt.</b> A slower, costlier bolt that chills (slows) its target —
    ///   the setup for Shatter.</item>
    /// <item><b>Q — Blink.</b> Teleport a short way in the direction you are looking. The
    ///   Mage has no armour; this is its defence.</item>
    /// <item><b>E — Meteor.</b> Call a meteor onto the spot under your crosshair. A blue
    ///   circle shows your party where it will land; then it hits everything inside, and
    ///   shatters every chilled enemy at once. The Mage's big moment.</item>
    /// </list>
    ///
    /// Mana is owner-side, like the Knight's stamina; every hit is resolved on the server.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class MageKit : NetworkBehaviour, IClassKit
    {
        public const float FireboltInterval = 0.42f, FireboltDamage = 20f, FireboltSpeed = 32f, FireboltMana = 5f;
        public const float FrostboltInterval = 0.95f, FrostboltDamage = 12f, FrostboltSpeed = 24f, FrostboltMana = 14f, ChillSeconds = 4f;
        public const float BlinkCooldown = 8f, BlinkDistance = 7f;
        public const float MeteorCooldown = 14f, MeteorMana = 35f, MeteorDelay = 1.3f, MeteorRadius = 3.6f, MeteorDamage = 70f, MeteorRange = 30f;
        public const float ShatterMultiplier = 1.75f;

        private Health health;
        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;

        // Owner-side.
        public float Mana { get; private set; }
        private float nextCast, blinkReadyAt, meteorReadyAt;
        private bool manaInitialised;

        // Server-side.
        private struct PendingMeteor { public Vector3 At; public float When; }
        private readonly List<PendingMeteor> meteors = new List<PendingMeteor>();
        private float lastServerBolt;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private bool Active => progress.ClassId.Value == ClassId.Mage;
        private float ManaMax => progress.Value(StatType.MP);
        private float ManaRegen => 4f + progress.Value(StatType.WIS) * 0.15f;

        // ---- IClassKit ----

        public ClassId Class => ClassId.Mage;
        public string ResourceName => "Mana";
        public Color ResourceColor => new Color(0.3f, 0.5f, 1f);
        public float Resource => Mana;
        public float ResourceMax => ManaMax;

        public void FillAbilities(List<AbilityView> into)
        {
            into.Add(new AbilityView { Key = "LMB", Name = "Firebolt", Unaffordable = Mana < FireboltMana });
            into.Add(new AbilityView { Key = "RMB", Name = "Frostbolt", Unaffordable = Mana < FrostboltMana });
            into.Add(new AbilityView { Key = "Q", Name = "Blink", Remaining = blinkReadyAt - Time.time, Cooldown = BlinkCooldown });
            into.Add(new AbilityView { Key = "E", Name = "Meteor", Remaining = meteorReadyAt - Time.time, Cooldown = MeteorCooldown, Unaffordable = Mana < MeteorMana });
        }

        // ==================================================================
        // Owner
        // ==================================================================

        private void Update()
        {
            if (IsServer) ResolveMeteors();
            if (!IsOwner || !Active) return;

            if (!manaInitialised) { Mana = ManaMax; manaInitialised = true; }
            Mana = Mathf.Min(ManaMax, Mana + ManaRegen * Time.deltaTime);
            motor.SpeedMultiplier = progress.SpeedMultiplier;
            motor.CanSprint = true;

            bool canAct = life.CanAct && motor.HasControl;
            if (!canAct) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            float rate = progress.AttackTimeMultiplier;

            // --- LMB held: Firebolt ---
            if (mouse != null && mouse.leftButton.isPressed && Time.time >= nextCast && Mana >= FireboltMana)
            {
                Mana -= FireboltMana;
                nextCast = Time.time + FireboltInterval * rate;
                player.ViewModelCast(false);
                BoltRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward, false);
            }
            // --- RMB: Frostbolt ---
            else if (mouse != null && mouse.rightButton.isPressed && Time.time >= nextCast && Mana >= FrostboltMana)
            {
                Mana -= FrostboltMana;
                nextCast = Time.time + FrostboltInterval * rate;
                player.ViewModelCast(true);
                BoltRpc(player.AimOrigin + player.AimForward * 0.6f, player.AimForward, true);
            }

            // --- Q: Blink ---
            if (keyboard != null && keyboard.qKey.wasPressedThisFrame && Time.time >= blinkReadyAt && TryBlink())
            {
                blinkReadyAt = Time.time + BlinkCooldown;
            }

            // --- E: Meteor at the crosshair ---
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && Time.time >= meteorReadyAt && Mana >= MeteorMana &&
                TryAimGround(out var spot))
            {
                Mana -= MeteorMana;
                meteorReadyAt = Time.time + MeteorCooldown;
                player.ViewModelCast(false);
                MeteorRpc(spot);
            }
        }

        /// <summary>
        /// Blink along the look direction, flattened, stopping short of walls, and only
        /// onto solid floor — never into a wall or off a ledge into nothing.
        /// </summary>
        private bool TryBlink()
        {
            var dir = player.AimForward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return false;
            dir.Normalize();

            var chest = transform.position + Vector3.up * 1.2f;
            float distance = BlinkDistance;
            if (Physics.SphereCast(chest, 0.35f, dir, out var wall, BlinkDistance, ~0, QueryTriggerInteraction.Ignore) &&
                wall.collider.GetComponentInParent<Health>() == null)
            {
                distance = Mathf.Max(0f, wall.distance - 0.4f);
            }
            if (distance < 1f) return false;

            var target = chest + dir * distance;
            if (!Physics.Raycast(target, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)) return false;

            var from = transform.position;
            motor.Teleport(floor.point + Vector3.up * 0.05f, transform.rotation);
            BlinkFxRpc(from, transform.position);
            return true;
        }

        private bool TryAimGround(out Vector3 spot)
        {
            spot = default;
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            Vector3 point;
            if (Physics.Raycast(origin, dir, out var hit, MeteorRange, ~0, QueryTriggerInteraction.Ignore)) point = hit.point;
            else point = origin + dir * MeteorRange;

            // Drop to the floor beneath wherever the crosshair landed (a wall, an enemy).
            if (!Physics.Raycast(point + Vector3.up * 0.5f - dir * 0.3f, Vector3.down, out var floor, 12f, ~0, QueryTriggerInteraction.Ignore)) return false;
            spot = floor.point;
            return true;
        }

        // ==================================================================
        // Server
        // ==================================================================

        [Rpc(SendTo.Server)]
        private void BoltRpc(Vector3 origin, Vector3 direction, bool frost)
        {
            if (!life.CanAct) return;
            if (Time.time - lastServerBolt < FireboltInterval * 0.5f) return;
            lastServerBolt = Time.time;
            ServerBolt(origin, direction, frost);
        }

        /// <summary>Server: launch a bolt. Public for the smoke test, which fires without input.</summary>
        public void ServerBolt(Vector3 origin, Vector3 direction, bool frost)
        {
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.6f) > 2.5f) origin = transform.position + Vector3.up * 1.6f;

            float att = progress.AttackMultiplier;
            ulong me = NetworkObjectId;
            if (frost)
            {
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, FrostboltSpeed, h =>
                {
                    h.ServerDamage(FrostboltDamage * att, transform.position, me);
                    h.ServerChill(ChillSeconds);
                    ImpactFxRpc(h.Center, true, false);
                });
            }
            else
            {
                Arrows.ServerFire(origin, direction, 0f, Team.Enemies, me, FireboltSpeed, h =>
                {
                    bool shatter = h.ServerConsumeChill();
                    h.ServerDamage(FireboltDamage * att * (shatter ? ShatterMultiplier : 1f), transform.position, me);
                    ImpactFxRpc(h.Center, false, shatter);
                });
            }
            BoltFxRpc(origin, direction, frost);
        }

        [Rpc(SendTo.Server)]
        private void MeteorRpc(Vector3 spot)
        {
            if (!life.CanAct || Vector3.Distance(spot, transform.position) > MeteorRange + 3f) return;
            meteors.Add(new PendingMeteor { At = spot, When = Time.time + MeteorDelay });
            MeteorWarnRpc(spot);
        }

        private void ResolveMeteors()
        {
            for (int i = meteors.Count - 1; i >= 0; i--)
            {
                if (Time.time < meteors[i].When) continue;
                var at = meteors[i].At;
                meteors.RemoveAt(i);

                int shattered = 0;
                foreach (var h in CombatQuery.Circle(at, MeteorRadius, Team.Enemies))
                {
                    bool shatter = h.ServerConsumeChill();
                    if (shatter) shattered++;
                    h.ServerDamage(MeteorDamage * progress.AttackMultiplier * (shatter ? ShatterMultiplier : 1f), at + Vector3.up, NetworkObjectId);
                }
                MeteorImpactRpc(at, shattered > 0);
            }
        }

        // ==================================================================
        // Presentation
        // ==================================================================

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltFxRpc(Vector3 origin, Vector3 direction, bool frost)
        {
            Arrows.SpawnVisual(origin, direction, frost ? ProjectileKind.Frostbolt : ProjectileKind.Firebolt, frost ? FrostboltSpeed : FireboltSpeed);
            Sfx.Play(frost ? Sound.Frost : Sound.Fire, origin, 0.55f);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ImpactFxRpc(Vector3 at, bool frost, bool shatter)
        {
            if (shatter) Sfx.Play(Sound.Shatter, at, 1f);
            else Sfx.Play(frost ? Sound.Frost : Sound.Hit, at, 0.6f, frost ? 1.4f : 1.1f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlinkFxRpc(Vector3 from, Vector3 to)
        {
            Sfx.Play(Sound.Summon, from + Vector3.up, 0.5f, 2f);
            Telegraph.Show(from, 0.8f, 0.3f, null, Telegraph.Friendly);
            Telegraph.Show(to, 0.8f, 0.3f, null, Telegraph.Friendly);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorWarnRpc(Vector3 at)
        {
            Telegraph.Show(at, MeteorRadius, MeteorDelay, null, Telegraph.Friendly);
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.6f);
            Sfx.Play(Sound.Fire, at + Vector3.up * 8f, 0.9f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MeteorImpactRpc(Vector3 at, bool shattered)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            if (shattered) Sfx.Play(Sound.Shatter, at + Vector3.up, 1f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 12f) local.ViewModelBlockImpact();
            MeteorFlash.Spawn(at);
        }
    }

    /// <summary>A brief burst of firelight and a falling-rock streak where a meteor lands.</summary>
    public class MeteorFlash : MonoBehaviour
    {
        private Light flash;
        private Transform rock;
        private float t;

        public static void Spawn(Vector3 at)
        {
            var go = new GameObject("MeteorFlash");
            go.transform.position = at;
            var m = go.AddComponent<MeteorFlash>();
            m.flash = new GameObject("Light").AddComponent<Light>();
            m.flash.transform.SetParent(go.transform, false);
            m.flash.transform.localPosition = Vector3.up * 1.5f;
            m.flash.type = LightType.Point;
            m.flash.color = new Color(1f, 0.55f, 0.2f);
            m.flash.range = 14f;
            m.flash.intensity = 14f;

            var ring = MeshKit.Part(go.transform, "Scorch", MeshKit.Lathe("scorchRing", new[]
            {
                new Vector2(0.2f, 0f), new Vector2(MageKit.MeteorRadius, 0.02f), new Vector2(0f, 0.04f),
            }, 24, false), Mat.Glow, Vector3.up * 0.02f);
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.4f, 0.1f) * 2f);
            m.rock = ring.transform;
            Destroy(go, 1.2f);
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - t / 1.1f);
            flash.intensity = 14f * k * k;
            if (rock != null) rock.localScale = new Vector3(1f, 1f, 1f) * (0.6f + t * 0.5f);
        }
    }
}
