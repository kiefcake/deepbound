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
    /// <item><b>Silenced</b> (Tongueless Cantors, 2026-10-27): no Q, E or ultimate for a few seconds; attacks,
    ///   blocks, dodges and potions still work. A grace follows so it never chains.</item>
    /// <item><b>Dazed</b> (a Brute's pound, a Gargoyle's dive, 2026-10-27): your head rings - turning is sluggish
    ///   for a moment. Nothing sways the camera; you just cannot whip round.</item>
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
        public readonly NetworkVariable<double> SilenceUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> DazeUntil = new NetworkVariable<double>();
        /// <summary>An Entling holding you by the legs (its NetworkObjectId), until it dies. MaxValue: nobody.</summary>
        public readonly NetworkVariable<ulong> RootedBy = new NetworkVariable<ulong>(ulong.MaxValue);
        public bool Entangled => RootedBy.Value != ulong.MaxValue;
        private double maniaGraceUntil, rootGraceUntil, silenceGraceUntil, dazeGraceUntil;
        private float rootedAt = -1f;

        private float poisonDps, nextTick;
        private Health health;
        private PlayerLife life;

        private double Now => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : 0;
        public bool Poisoned => Now < PoisonUntil.Value;
        public bool Frostbitten => Now < FrostUntil.Value;
        public bool Cursed => Now < CurseUntil.Value;
        public bool Manic => Now < ManiaUntil.Value;
        public bool Silenced => Now < SilenceUntil.Value;
        public bool Dazed => Now < DazeUntil.Value;
        /// <summary>How fast you can turn while dazed.</summary>
        public const float DazedTurn = 0.5f;
        public float TurnMultiplier => Dazed ? DazedTurn : 1f;
        public bool Rooted => Now < RootUntil.Value || Entangled;

        public float SpeedMultiplier => Rooted ? 0f : Frostbitten ? 0.6f : 1f;
        public float DamageMultiplier => (Cursed ? 0.7f : 1f) * (1f + DrunkCourage * Drunk);

        // ---- Mead (2026-11-01, the tavern at camp) ----
        /// <summary>How much mead is in you: 1 Tipsy, 2 Merry, 3 Sloshed. Wears off a level at a time.</summary>
        public readonly NetworkVariable<byte> DrunkLevel = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> DrunkUntil = new NetworkVariable<double>();
        public const int DrunkMax = 3;
        /// <summary>Seconds each level of drink lasts before the next one wears off.</summary>
        public const float DrunkSecondsPerLevel = 75f;
        /// <summary>Liquid courage: +4% damage a level. The world sways with it (the camera, and your body for others).</summary>
        public const float DrunkCourage = 0.04f;
        public int Drunk => DrunkLevel.Value > 0 && Now < DrunkUntil.Value ? DrunkLevel.Value : 0;
        public static string DrunkName(int level) => level >= 3 ? "Sloshed" : level == 2 ? "Merry" : "Tipsy";

        /// <summary>Server: down a mead. One more level (to <see cref="DrunkMax"/>); the clock starts over for the level.</summary>
        public void ServerDrink()
        {
            if (!IsServer) return;
            DrunkLevel.Value = (byte)Mathf.Min(DrunkMax, Drunk + 1);
            DrunkUntil.Value = Now + DrunkSecondsPerLevel;
        }

        /// <summary>Server: the drink wears off a level at a time.</summary>
        private void TickDrunk()
        {
            if (DrunkLevel.Value == 0 || Now < DrunkUntil.Value) return;
            DrunkLevel.Value = (byte)(DrunkLevel.Value - 1);
            if (DrunkLevel.Value > 0) DrunkUntil.Value = Now + DrunkSecondsPerLevel;
        }
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
            if (TryGetComponent<Progression.PlayerProgress>(out var hide) && hide.Wears(Loot.GearId.GorrsHide)) return;
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

        /// <summary>Server: a Cantor's hush takes your abilities for <paramref name="seconds"/>. False if it did not take.</summary>
        public bool ServerSilence(float seconds)
        {
            if (!IsServer || life.Downed.Value || Now < silenceGraceUntil || Warded || Unshakable) return false;
            // The Iron Psalter's hymn keeps a voice in you: half as long.
            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.IronPsalter)) seconds *= 0.5f;
            SilenceUntil.Value = Now + seconds;
            silenceGraceUntil = Now + seconds * 2.5f;
            AfflictedRpc(2);
            return true;
        }

        /// <summary>For tests: forget every grace, so the next affliction takes.</summary>
        public void ServerResetGrace() { maniaGraceUntil = rootGraceUntil = silenceGraceUntil = dazeGraceUntil = 0; }

        /// <summary>Server: a heavy blow rings your head for <paramref name="seconds"/>.</summary>
        public bool ServerDaze(float seconds)
        {
            if (!IsServer || life.Downed.Value || Now < dazeGraceUntil || Warded || Unshakable) return false;
            DazeUntil.Value = Now + seconds;
            dazeGraceUntil = Now + seconds * 2.5f;
            AfflictedRpc(3);
            return true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // Silence is felt at the keys: Q, E and the ultimate do nothing while it lasts (every kit at once).
            if (IsOwner) UI.Binds.AbilityLock = () => this != null && Silenced;
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
            else if (kind == 3)
            {
                UI.GameHud.ShowCombo(at, "DAZED", new Color(0.95f, 0.85f, 0.55f));
                Sfx.Play(Sound.Block, at, 0.5f, 0.5f);
            }
            else if (kind == 2)
            {
                UI.GameHud.ShowCombo(at, "SILENCED", Enemies.CantorBrain.HushColor);
                Sfx.Play(Sound.Whisper, at, 0.8f, 0.6f);
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
            bool any = Poisoned || Frostbitten || Cursed || Manic || Silenced || Dazed || Now < RootUntil.Value;
            ManiaUntil.Value = 0;
            SilenceUntil.Value = 0;
            DazeUntil.Value = 0;
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
                TickDrunk();
                if (Poisoned && Time.time >= nextTick && !life.Downed.Value)
                {
                    nextTick = Time.time + 0.5f;
                    health.Tag("PlayerDebuffs").ServerDamage(poisonDps * 0.5f, transform.position);
                }
                // A curse ends with whoever laid it.
                if (Cursed && CurseSource.Value != ulong.MaxValue &&
                    (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(CurseSource.Value, out var src) ||
                     (src.TryGetComponent<Health>(out var sh) && !sh.IsAlive)))
                    ServerCleanseCurse();
                if (life.Downed.Value && (Poisoned || Frostbitten || Cursed || Manic || Rooted)) ServerCleanse();
                // Whatever held you is gone, has been knocked away, or has held too long (a stunned Entling's own clock
                // stops; 2026-10-27 autoplay: a Ranger stood rooted for nine minutes): the roots go with it.
                if (Entangled)
                {
                    if (rootedAt < 0f) rootedAt = Time.time;
                    bool gone = life.Downed.Value || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(RootedBy.Value, out var holder) ||
                        (holder.TryGetComponent<Health>(out var hh) && !hh.IsAlive) ||
                        (holder.transform.position - transform.position).sqrMagnitude > 16f || Time.time - rootedAt > Enemies.EntlingBrain.HoldLimit + 1f;
                    if (gone) { RootedBy.Value = ulong.MaxValue; rootedAt = -1f; }
                }
                else rootedAt = -1f;
            }

            // Everyone: wisps around an afflicted player, so the party can see who needs help.
            // Not on your own screen: there they spawned at the camera as big squares, and the overlay
            // (UI/AfflictionOverlay) shows you instead.
            if (!IsOwner && Time.time >= nextWisp)
            {
                nextWisp = Time.time + 0.2f;
                if (Poisoned) Vfx.Mote(transform.position, new Color(0.45f, 0.9f, 0.2f));
                if (Frostbitten) Vfx.Mote(transform.position, new Color(0.6f, 0.85f, 1f));
                if (Cursed) Vfx.Mote(transform.position, new Color(0.55f, 0.2f, 0.8f));
                if (Manic) { Vfx.Mote(transform.position, new Color(0.9f, 0.4f, 1f)); Vfx.Mote(transform.position, new Color(1f, 0.5f, 0.9f)); }
                if (Rooted) Vfx.Mote(transform.position + Vector3.down * 0.9f, new Color(0.7f, 0.95f, 1f));
                // The newer two, for the party to see (2026-10-28): stars round a dazed head, grey haze off the silenced.
                if (Dazed)
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = Time.time * 5f + i * 2.094f;
                        Vfx.Speck(transform.position + Vector3.up * 2.05f + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.32f, new Color(1f, 0.9f, 0.5f) * 1.6f);
                    }
                if (Silenced) Vfx.Mote(transform.position + Vector3.up * 0.6f, new Color(0.62f, 0.62f, 0.8f));
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
            if (IsOwner) { UI.GameHud.Feed("Cleansed", new Color(1f, 0.95f, 0.7f)); UI.AfflictionOverlay.Cleansed(); }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CurseLiftedRpc()
        {
            if (IsOwner) UI.GameHud.Feed("The curse lifts", new Color(0.75f, 0.55f, 1f));
        }
    }
}
