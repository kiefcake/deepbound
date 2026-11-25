using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Player
{
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

        public readonly NetworkVariable<double> HexUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<ulong> HexSource = new NetworkVariable<ulong>(ulong.MaxValue);

        public readonly NetworkVariable<double> BlindUntil = new NetworkVariable<double>();
        public const float WeaknessDamage = 0.6f, CurseTaken = 1.4f;

        public readonly NetworkVariable<ulong> RootedBy = new NetworkVariable<ulong>(ulong.MaxValue);
        public bool Entangled => RootedBy.Value != ulong.MaxValue;
        private double maniaGraceUntil, rootGraceUntil, silenceGraceUntil, dazeGraceUntil, blindGraceUntil;
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

        public bool AnyAffliction => Poisoned || Frostbitten || Cursed || Silenced || Dazed || Manic || Now < RootUntil.Value;

        public bool Accursed => Now < HexUntil.Value;
        public bool Blinded => Now < BlindUntil.Value;

        public static bool LocalBlind
        {
            get
            {
                var me = NetworkPlayer.Local;
                return me != null && me.TryGetComponent<PlayerDebuffs>(out var d) && d.Blinded;
            }
        }

        public const float DazedTurn = 0.5f;
        public float TurnMultiplier => Dazed ? DazedTurn : 1f;
        public bool Rooted => Now < RootUntil.Value || Entangled;

        public float SpeedMultiplier => Rooted ? 0f : Frostbitten ? 0.6f : 1f;
        public float DamageMultiplier => (Cursed ? WeaknessDamage : 1f) * (1f + DrunkCourage * Drunk);

        public readonly NetworkVariable<byte> DrunkLevel = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> DrunkUntil = new NetworkVariable<double>();
        public const int DrunkMax = 3;

        public const float DrunkSecondsPerLevel = 75f;

        public const float DrunkCourage = 0.04f;
        public int Drunk => DrunkLevel.Value > 0 && Now < DrunkUntil.Value ? DrunkLevel.Value : 0;

        public bool Confused => Drunk >= DrunkMax;
        public const float ConfuseShuffle = 30f;
        public static string DrunkName(int level) => level >= 3 ? "Sloshed" : level == 2 ? "Merry" : "Tipsy";

        public void ServerDrink()
        {
            if (!IsServer) return;
            DrunkLevel.Value = (byte)Mathf.Min(DrunkMax, Drunk + 1);
            DrunkUntil.Value = Now + DrunkSecondsPerLevel;
        }

        public void ServerSober()
        {
            if (!IsServer) return;
            DrunkLevel.Value = 0;
            DrunkUntil.Value = 0;
        }

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

        public bool ServerAccurse(float seconds, ulong source)
        {
            if (!IsServer || life.Downed.Value || Warded || Unshakable) return false;
            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.IronPsalter)) seconds *= 0.5f;
            HexUntil.Value = Now + seconds;
            HexSource.Value = source;
            AfflictedRpc(5);
            return true;
        }

        public bool ServerBlind(float seconds)
        {
            if (!IsServer || life.Downed.Value || Warded || Unshakable) return false;
            if (Blinded)
            {
                BlindUntil.Value = System.Math.Max(BlindUntil.Value, Now + seconds);
                blindGraceUntil = BlindUntil.Value + 1.5;
                return true;
            }
            if (Now < blindGraceUntil) return false;
            BlindUntil.Value = Now + seconds;
            blindGraceUntil = BlindUntil.Value + 1.5;
            AfflictedRpc(4);
            return true;
        }

        public void ServerMania(float seconds)
        {
            if (!IsServer || life.Downed.Value || Now < maniaGraceUntil || Warded || Unshakable) return;
            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.IronPsalter)) return;
            ManiaUntil.Value = Now + seconds;
            maniaGraceUntil = Now + seconds * 3f;
            AfflictedRpc(0);
        }

        public bool ServerSilence(float seconds)
        {
            if (!IsServer || life.Downed.Value || Now < silenceGraceUntil || Warded || Unshakable) return false;

            if (TryGetComponent<Progression.PlayerProgress>(out var pp) && pp.HasRelic(Loot.Relic.IronPsalter)) seconds *= 0.5f;
            SilenceUntil.Value = Now + seconds;
            silenceGraceUntil = Now + seconds * 2.5f;
            AfflictedRpc(2);
            return true;
        }

        public void ServerResetGrace() { maniaGraceUntil = rootGraceUntil = silenceGraceUntil = dazeGraceUntil = blindGraceUntil = 0; }

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

            if (IsOwner) UI.Binds.AbilityLock = () => this != null && Silenced;

            if (IsServer && health != null) health.IncomingModifiers.Add((d, from) => Accursed ? d * CurseTaken : d);
        }

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
                global::Deepbound.Art.Vfx.Embers(at - Vector3.up * 0.6f, 10, new Color(0.85f, 0.4f, 1f), 3f, 0.05f, 0.6f);
                Sfx.Play(Sound.Summon, at, 0.7f, 1.6f);
            }
            else if (kind == 3)
            {
                global::Deepbound.Art.Vfx.Embers(at - Vector3.up * 0.3f, 8, new Color(0.95f, 0.85f, 0.55f), 2.5f, 0.05f, 0.5f);
                Sfx.Play(Sound.Block, at, 0.5f, 0.5f);
            }
            else if (kind == 4)
            {
                global::Deepbound.Art.Vfx.Smoke(at - Vector3.up * 0.5f, 3, 0.3f, 1.2f);
                Sfx.Play(Sound.Splash, at, 0.6f, 0.7f);
            }
            else if (kind == 5)
            {
                global::Deepbound.Art.Vfx.Embers(at - Vector3.up * 0.4f, 10, new Color(0.4f, 0.6f, 0.1f), 2f, 0.05f, 0.8f);
                Sfx.Play(Sound.Shade, at, 0.7f, 0.7f);
            }
            else if (kind == 2)
            {
                global::Deepbound.Art.Vfx.Ring(transform.position + Vector3.up * 1.55f, 0.5f, Enemies.CantorBrain.HushColor, 0.6f);
                Sfx.Play(Sound.Whisper, at, 0.8f, 0.6f);
            }
            else
            {
                Sfx.Play(Sound.Frost, transform.position, 0.8f, 1.2f);
                Vfx.IceSpikes(transform.position, 0.8f, 6);
            }
        }

        public void ServerCleanse()
        {
            if (!IsServer) return;
            bool any = Poisoned || Frostbitten || Cursed || Manic || Silenced || Dazed || Accursed || Blinded || Now < RootUntil.Value;
            HexUntil.Value = 0;
            HexSource.Value = ulong.MaxValue;
            BlindUntil.Value = 0;
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

                if (Cursed && CurseSource.Value != ulong.MaxValue &&
                    (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(CurseSource.Value, out var src) ||
                     (src.TryGetComponent<Health>(out var sh) && !sh.IsAlive)))
                    ServerCleanseCurse();

                if (Accursed && HexSource.Value != ulong.MaxValue &&
                    (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(HexSource.Value, out var hag) ||
                     (hag.TryGetComponent<Health>(out var hh0) && !hh0.IsAlive)))
                {
                    HexUntil.Value = 0;
                    HexSource.Value = ulong.MaxValue;
                    CurseLiftedRpc();
                }
                if (life.Downed.Value && (Poisoned || Frostbitten || Cursed || Manic || Rooted || Accursed || Blinded)) ServerCleanse();

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

            if (!IsOwner && Time.time >= nextWisp)
            {
                nextWisp = Time.time + 0.2f;
                if (Poisoned) Vfx.Mote(transform.position, new Color(0.45f, 0.9f, 0.2f));
                if (Frostbitten) Vfx.Mote(transform.position, new Color(0.6f, 0.85f, 1f));
                if (Cursed) Vfx.Mote(transform.position, new Color(0.55f, 0.2f, 0.8f));
                if (Manic) { Vfx.Mote(transform.position, new Color(0.9f, 0.4f, 1f)); Vfx.Mote(transform.position, new Color(1f, 0.5f, 0.9f)); }
                if (Rooted) Vfx.Mote(transform.position + Vector3.down * 0.9f, new Color(0.7f, 0.95f, 1f));

                if (Dazed)
                    for (int i = 0; i < 3; i++)
                    {
                        float ang = Time.time * 5f + i * 2.094f;
                        Vfx.Speck(transform.position + Vector3.up * 2.05f + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.32f, new Color(1f, 0.9f, 0.5f) * 1.6f);
                    }
                if (Silenced) Vfx.Mote(transform.position + Vector3.up * 0.6f, new Color(0.62f, 0.62f, 0.8f));
                if (Accursed) Vfx.Mote(transform.position, new Color(0.55f, 0.75f, 0.2f));
                if (Blinded) Vfx.Speck(transform.position + Vector3.up * 1.75f + transform.forward * 0.2f, new Color(0.08f, 0.05f, 0.12f));
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
