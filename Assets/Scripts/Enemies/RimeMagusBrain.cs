using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Rime Magus (2026-11-13, the user: "a mage enemy type, that casts an aoe blizzard spell that slowly follows a
    /// player for time, slowing them and dealing small amount of damage"). A frost-robed caster of the Drowned Vaults and
    /// below. It hangs back and calls a BLIZZARD: a storm of <see cref="StormRadius"/> opens where its target stands and
    /// drifts after them at <see cref="StormSpeed"/> (a little slower than a walk) for <see cref="StormSeconds"/>. Inside
    /// it you are FROSTBITTEN (slowed) and take a little damage every half second. Outwalk it - sprint or dodge clear,
    /// never stand and fight in it - or kill the Magus: the storm dies with it. A stun scatters the storm.
    /// </summary>
    public class RimeMagusBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 11f, stormCooldown = 12f, stormWindup = 1.1f;
        public const float StormRadius = 3.2f, StormSpeed = 2.3f, StormSeconds = 6.5f, StormTick = 0.5f, StormDamage = 0.25f;
        public static readonly Color RimeColor = new Color(0.7f, 0.9f, 1f);
        private float nextStorm, castAt = -1f, stormUntil = -1f, nextTick, nextFx;
        private Vector3 stormPos;
        private ulong stormTarget = ulong.MaxValue;
        /// <summary>For tests: storms called, and ticks that caught a player.</summary>
        public int Storms { get; private set; }
        public int Caught { get; private set; }
        public bool StormUp => Time.time < stormUntil;
        public Vector3 StormPosition => stormPos;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextStorm = Time.time + 3f;
        }

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.6f + transform.forward * 0.4f;
            var dir = AimDir(origin, 16f);
            Arrows.ServerFire(origin, dir, damage, Team.Players, NetworkObjectId, 16f);
            BoltRpc(origin, dir);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (Time.time < nextStorm || StormUp || distanceToTarget > 18f || target == null) return false;
            if (!CombatQuery.ClearLine(transform.position + Vector3.up * 1.6f, target)) return false;
            BeginStorm(target, stormWindup);
            return true;
        }

        private void BeginStorm(Health who, float windup)
        {
            nextStorm = Time.time + stormCooldown;
            stormTarget = who.NetworkObjectId;
            stormPos = who.transform.position;
            castAt = Time.time + windup;
            busyUntil = castAt + 0.4f;
            PlayActionRpc(RigAction.Cast, windup);
            CallRpc(stormPos, windup);
        }

        /// <summary>For tests: call a blizzard on <paramref name="who"/> now.</summary>
        public void ServerDebugStorm(Health who, float windup) => BeginStorm(who, windup);

        protected override void Update()
        {
            if (HeldByStun()) { TickStorm(); return; }
            if (IsServer && !IsDead && castAt > 0f && Time.time >= castAt)
            {
                castAt = -1f;
                Storms++;
                stormUntil = Time.time + StormSeconds;
                nextTick = Time.time + StormTick;
                StormRpc(stormPos, stormTarget, StormSeconds);
            }
            TickStorm();
            base.Update();
        }

        /// <summary>Every peer drifts the storm after its quarry the same way; the server also bites.</summary>
        private void TickStorm()
        {
            if (!StormUp) return;
            if (IsDead) { stormUntil = -1f; return; }
            if (NetworkManager != null && NetworkManager.SpawnManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(stormTarget, out var quarry) && quarry != null)
            {
                var want = quarry.transform.position;
                want.y = stormPos.y;
                stormPos = Vector3.MoveTowards(stormPos, want, StormSpeed * Time.deltaTime);
                stormPos.y = Mathf.Lerp(stormPos.y, quarry.transform.position.y, Time.deltaTime * 4f);
            }
            if (!Application.isBatchMode && Time.time >= nextFx)
            {
                nextFx = Time.time + 0.05f;
                var r = Random.insideUnitCircle * StormRadius;
                var at = stormPos + new Vector3(r.x, Random.Range(1.2f, 3f), r.y);
                Vfx.Embers(at, 2, new Color(0.92f, 0.97f, 1f), 1.8f, 0.085f, 1.2f);
                if (Random.value < 0.25f) FxSprite.Billboard(FxSprite.AnySmoke, stormPos + new Vector3(r.x * 0.7f, 0.6f, r.y * 0.7f),
                    new Vector3(-r.y, 0.2f, r.x) * 0.5f, 1.4f, 1.2f, 2.2f, new Color(0.85f, 0.93f, 1f, 0.28f), 40f);
                if (Random.value < 0.12f) Vfx.Ring(Vfx.Floor(stormPos) + Vector3.up * 0.05f, StormRadius, RimeColor, 0.6f);
            }
            if (!IsServer || Time.time < nextTick) return;
            nextTick = Time.time + StormTick;
            foreach (var v in CombatQuery.Circle(stormPos, StormRadius, Team.Players))
            {
                Caught++;
                v.Tag("Blizzard").ServerDamage(damage * StormDamage, stormPos + Vector3.up, NetworkObjectId);
                if (v.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerFrostbite(1.2f);
            }
        }

        public override void ServerStun(float seconds)
        {
            if (castAt > 0f) castAt = -1f;
            if (StormUp) { stormUntil = -1f; ScatterRpc(stormPos); }
            Telegraph.Cancel(this);
            base.ServerStun(seconds);
        }

        protected override void OnDied(Health h)
        {
            if (StormUp) ScatterRpc(stormPos);
            stormUntil = -1f;
            base.OnDied(h);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.Frostbolt, 16f);
            Sfx.Play(Sound.Frost, origin, 0.4f, 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CallRpc(Vector3 at, float seconds)
        {
            Telegraph.Show(at, StormRadius, seconds, this, new Color(RimeColor.r, RimeColor.g, RimeColor.b, 0.22f));
            Sfx.Play(Sound.Frost, transform.position + Vector3.up * 1.6f, 0.5f, 0.7f);
            Vfx.Embers(transform.position + Vector3.up * 2f, 10, RimeColor, 2.5f, 0.05f, 0.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StormRpc(Vector3 at, ulong quarry, float seconds)
        {
            stormPos = at;
            stormTarget = quarry;
            stormUntil = Time.time + seconds;
            Vfx.Ring(Vfx.Floor(at) + Vector3.up * 0.05f, StormRadius, RimeColor, 0.5f);
            Sfx.Play(Sound.Frost, at + Vector3.up, 0.9f, 0.55f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ScatterRpc(Vector3 at)
        {
            stormUntil = -1f;
            Vfx.Embers(at + Vector3.up, 14, RimeColor, 4f, 0.05f, 0.6f);
            Sfx.Play(Sound.Frost, at + Vector3.up, 0.6f, 1.3f);
        }
    }
}
