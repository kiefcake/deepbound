using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// The bad things the dead can do to you, beyond hurting you:
    ///
    /// <list type="bullet">
    /// <item><b>Poisoned</b> (Plaguebearers): damage every half second. Stacks the timer, not the rate.</item>
    /// <item><b>Frostbitten</b> (Frost Wights): 40% slower on your feet.</item>
    /// <item><b>Cursed</b> (Hexers): you deal 30% less damage and heal half as well — until it
    ///   wears off, or the Hexer that laid it dies.</item>
    /// <item><b>Mania</b> (the Cathedral's choir, the Unsung): for a couple of seconds you sprint about
    ///   wildly and cannot attack; you can still look around. A short grace follows, so it never chains.</item>
    /// <item><b>Rooted</b> (the drowned and the frozen): ice locks your feet for a moment; you can still fight.</item>
    /// </list>
    /// Holy Water cleanses everything it splashes. Server-owned; synced as timestamps.
    /// </summary>
    public class PlayerDebuffs : NetworkBehaviour
    {
        public readonly NetworkVariable<double> PoisonUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> FrostUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> CurseUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<ulong> CurseSource = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<double> ManiaUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> RootUntil = new NetworkVariable<double>();
        /// <summary>An Entling holding you by the legs (its NetworkObjectId), until it dies. MaxValue: nobody.</summary>
        public readonly NetworkVariable<ulong> RootedBy = new NetworkVariable<ulong>(ulong.MaxValue);
        public bool Entangled => RootedBy.Value != ulong.MaxValue;
        private double maniaGraceUntil, rootGraceUntil;

        private float poisonDps, nextTick;
        private Health health;
        private PlayerLife life;

        private double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : 0;
        public bool Poisoned => Now < PoisonUntil.Value;
        public bool Frostbitten => Now < FrostUntil.Value;
        public bool Cursed => Now < CurseUntil.Value;
        public bool Manic => Now < ManiaUntil.Value;
        public bool Rooted => Now < RootUntil.Value || Entangled;

        public float SpeedMultiplier => Rooted ? 0f : Frostbitten ? 0.6f : 1f;
        public float DamageMultiplier => Cursed ? 0.7f : 1f;
        public float HealMultiplier => Cursed ? 0.5f : 1f;

        private void Awake()
        {
            health = GetComponent<Health>();
            life = GetComponent<PlayerLife>();
        }

        /// <summary>Server time the Plague Mask's ward ends: no new afflictions until then.</summary>
        public readonly NetworkVariable<double> WardUntil = new NetworkVariable<double>();
        public bool Warded => Now < WardUntil.Value;
        public void ServerWard(float seconds) { if (IsServer) WardUntil.Value = System.Math.Max(WardUntil.Value, Now + seconds); }

        public void ServerPoison(float seconds, float dps)
        {
            if (!IsServer || life.Downed.Value || Warded) return;
            poisonDps = Mathf.Max(poisonDps * (Poisoned ? 1f : 0f), dps);
            PoisonUntil.Value = System.Math.Max(PoisonUntil.Value, Now + seconds);
        }

        /// <summary>Ironbark: nothing takes hold of you while it lasts.</summary>
        private bool Unshakable => TryGetComponent<Flasks>(out var f) && f.Ironbark;

        public void ServerFrostbite(float seconds)
        {
            if (!IsServer || life.Downed.Value || Warded || Unshakable) return;
            FrostUntil.Value = System.Math.Max(FrostUntil.Value, Now + seconds);
        }

        public void ServerCurse(float seconds, ulong source)
        {
            if (!IsServer || life.Downed.Value || Warded) return;
            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.IronPsalter)) seconds *= 0.5f;
            CurseUntil.Value = Now + seconds;
            CurseSource.Value = source;
        }

        /// <summary>Server: madness takes you for <paramref name="seconds"/> (then a grace of twice as long).</summary>
        public void ServerMania(float seconds)
        {
            if (!IsServer || life.Downed.Value || Now < maniaGraceUntil || Warded || Unshakable) return;
            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.IronPsalter)) return;
            ManiaUntil.Value = Now + seconds;
            maniaGraceUntil = Now + seconds * 3f;
            AfflictedRpc(0);
        }

        /// <summary>Server: an Entling wraps your legs until it dies (or lets go).</summary>
        public void ServerHoldRoots(ulong entling)
        {
            if (!IsServer) return;
            RootedBy.Value = entling;
        }

        public void ServerReleaseRoots(ulong entling)
        {
            if (!IsServer || RootedBy.Value != entling) return;
            RootedBy.Value = ulong.MaxValue;
        }

        /// <summary>Server: ice locks your feet for <paramref name="seconds"/>.</summary>
        public void ServerRoot(float seconds)
        {
            if (!IsServer || life.Downed.Value || Now < rootGraceUntil || Warded || Unshakable) return;
            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.RootbreakerCharm)) seconds *= 0.5f;
            RootUntil.Value = Now + seconds;
            rootGraceUntil = Now + seconds * 2.5f;
            AfflictedRpc(1);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AfflictedRpc(byte kind)
        {
            var at = transform.position + Vector3.up * 2.1f;
            if (kind == 0)
            {
                UI.GameHud.ShowCombo(at, "MANIA", new Color(0.85f, 0.4f, 1f));
                Sfx.Play(Sound.Summon, at, 0.7f, 1.6f);
            }
            else
            {
                UI.GameHud.ShowCombo(at, "ROOTED", new Color(0.6f, 0.9f, 1f));
                Sfx.Play(Sound.Frost, transform.position, 0.8f, 1.2f);
                Vfx.IceSpikes(transform.position, 0.8f, 6);
            }
        }

        /// <summary>Server: Holy Water, a fountain, a revive — wash it all away.</summary>
        public void ServerCleanse()
        {
            if (!IsServer) return;
            bool any = Poisoned || Frostbitten || Cursed || Manic || Now < RootUntil.Value;
            ManiaUntil.Value = 0;
            RootUntil.Value = 0;
            PoisonUntil.Value = 0;
            FrostUntil.Value = 0;
            CurseUntil.Value = 0;
            CurseSource.Value = ulong.MaxValue;
            if (any) CleansedRpc();
        }

        private void Update()
        {
            if (IsServer)
            {
                if (Poisoned && Time.time >= nextTick && !life.Downed.Value)
                {
                    nextTick = Time.time + 0.5f;
                    health.ServerDamage(poisonDps * 0.5f, transform.position);
                }
                // A curse ends with whoever laid it.
                if (Cursed && CurseSource.Value != ulong.MaxValue &&
                    (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(CurseSource.Value, out var src) ||
                     (src.TryGetComponent<Health>(out var sh) && !sh.IsAlive)))
                    ServerCleanseCurse();
                if (life.Downed.Value && (Poisoned || Frostbitten || Cursed || Manic || Rooted)) ServerCleanse();
                // Whatever held you is gone: the roots go with it.
                if (Entangled && (life.Downed.Value || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(RootedBy.Value, out var holder) ||
                    (holder.TryGetComponent<Health>(out var hh) && !hh.IsAlive))) RootedBy.Value = ulong.MaxValue;
            }

            // Everyone: wisps around an afflicted player, so the party can see who needs help.
            if (Time.time >= nextWisp)
            {
                nextWisp = Time.time + 0.2f;
                if (Poisoned) Vfx.Mote(transform.position, new Color(0.45f, 0.9f, 0.2f));
                if (Frostbitten) Vfx.Mote(transform.position, new Color(0.6f, 0.85f, 1f));
                if (Cursed) Vfx.Mote(transform.position, new Color(0.55f, 0.2f, 0.8f));
                if (Manic) { Vfx.Mote(transform.position, new Color(0.9f, 0.4f, 1f)); Vfx.Mote(transform.position, new Color(1f, 0.5f, 0.9f)); }
                if (Rooted) Vfx.Mote(transform.position + Vector3.down * 0.9f, new Color(0.7f, 0.95f, 1f));
            }
        }

        private float nextWisp;

        private void ServerCleanseCurse()
        {
            CurseUntil.Value = 0;
            CurseSource.Value = ulong.MaxValue;
            CurseLiftedRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CleansedRpc()
        {
            Vfx.Ring(transform.position, 1.5f, new Color(1f, 0.95f, 0.7f), 0.4f);
            if (IsOwner) UI.GameHud.Feed("Cleansed", new Color(1f, 0.95f, 0.7f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CurseLiftedRpc()
        {
            if (IsOwner) UI.GameHud.Feed("The curse lifts", new Color(0.75f, 0.55f, 1f));
        }
    }
}
