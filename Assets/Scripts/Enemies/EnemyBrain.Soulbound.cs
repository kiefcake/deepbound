using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// SOULBOUND twins (2026-10-28, co-op content): two elites bound by a thread of pale light. Strike one down
    /// while its twin stands and it only falls - it lies SEVERED for <see cref="SoulboundWindow"/> seconds, and
    /// gets up again unless the twin dies before then. Kill the twin while the first lies severed and both go
    /// for good. Two players split them, one each; alone you have longer (<see cref="SoulboundSoloWindow"/>).
    /// <para>Server holds the bond; every peer draws the thread (<see cref="TwinId"/>) and the fall (FxFlags bit 2).</para>
    /// </summary>
    public partial class EnemyBrain
    {
        public const float SoulboundWindow = 5f, SoulboundSoloWindow = 8f, SoulboundRise = 0.5f, SoulboundRiseFade = 0.7f, SoulboundRiseFloor = 0.1f;
        /// <summary>After this many rises the bond gives out and the next fall is for good (2026-11-13: a Knight bot
        /// killed one Bone Piper over and over for six minutes while its twin stood rooms away).</summary>
        public const int SoulboundMaxRises = 3;
        private int rises;

        /// <summary>The twin's NetworkObjectId (0 for everyone unbound). Synced for the thread.</summary>
        public readonly NetworkVariable<ulong> TwinId = new NetworkVariable<ulong>();
        public bool SeveredNow => (FxFlags.Value & 4) != 0;

        private EnemyBrain twin;
        private float severedUntil;
        private bool Severed => severedUntil > 0f;

        public static float SoulboundSeconds => Player.PlayerLife.All.Count > 1 ? SoulboundWindow : SoulboundSoloWindow;

        /// <summary>For tests: cut the severed wait short, so it rises now.</summary>
        public void ServerDebugEndWindow() { if (Severed) severedUntil = Time.time; }

        /// <summary>For tests: is this one lying severed (server)?</summary>
        public bool ServerSevered => Severed;

        /// <summary>Server: bind two enemies as soulbound twins (both made elite, if they are not already).</summary>
        public static bool ServerBind(EnemyBrain a, EnemyBrain b)
        {
            if (a == null || b == null || a == b || a.IsBoss || b.IsBoss || a.twin != null || b.twin != null || a.IsChampion || b.IsChampion) return false;
            foreach (var x in new[] { a, b })
            {
                if (!x.Elite.Value) x.ServerMakeElite(EliteAffix.Soulbound);
                else x.Affix.Value = (byte)EliteAffix.Soulbound;
                x.health.IncomingModifiers.Add(x.SoulboundModifier);
                // However it dies, a twin lying severed goes with it.
                var self = x;
                x.health.Died += _ => { if (self.twin != null && self.twin.Severed) self.twin.ServerEndSevered(self.health.LastHitFrom); };
            }
            a.twin = b;
            b.twin = a;
            a.TwinId.Value = b.NetworkObjectId;
            b.TwinId.Value = a.NetworkObjectId;
            return true;
        }

        private bool TwinStanding => twin != null && !twin.IsDead && twin.health != null && twin.health.IsAlive;

        private float SoulboundModifier(float dmg, Vector3 from)
        {
            if (IsDead || dmg >= BossBrain.KillDamage) return dmg;
            // Lying severed it cannot be hurt: the only way to finish it is through its twin.
            if (Severed) return 0f;
            if (dmg < health.Current.Value) return dmg;
            if (rises >= SoulboundMaxRises) { BondBrokeRpc(); return dmg; }
            if (TwinStanding && !twin.Severed)
            {
                severedUntil = Time.time + SoulboundSeconds;
                ServerStun(SoulboundSeconds);
                StartCoroutine(SeveredWait());
                SeveredRpc();
                return Mathf.Max(0f, health.Current.Value - 1f);
            }
            // The twin already lies severed: this blow ends them both (see the Died hook in ServerBind).
            return dmg;
        }

        private void ServerEndSevered(ulong by)
        {
            severedUntil = 0f;
            if (!IsDead && health.IsAlive) health.ServerDamage(BossBrain.KillDamage, transform.position, by);
        }

        private System.Collections.IEnumerator SeveredWait()
        {
            while (Severed && !IsDead && Time.time < severedUntil) yield return null;
            if (!Severed || IsDead || !health.IsAlive) yield break;
            severedUntil = 0f;
            stunnedUntil = 0f;
            // Each rise comes back weaker, down to a tenth, so a pair that cannot be burst down in the window still ends:
            // at a flat half, four bots spent eight minutes on one Bone Brute and a soloist had no way out (2026-11-05).
            health.ServerHeal(health.Max.Value * Mathf.Max(SoulboundRiseFloor, SoulboundRise * Mathf.Pow(SoulboundRiseFade, rises++)));
            RisenRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SeveredRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, "SEVERED - KILL ITS TWIN", new Color(0.8f, 0.9f, 1f));
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.9f, 0.7f);
            Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 1.8f, new Color(0.75f, 0.85f, 1f), 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BondBrokeRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, "THE BOND BREAKS", new Color(0.75f, 0.85f, 1f));
            Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 2.6f, new Color(0.75f, 0.85f, 1f), 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RisenRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, "THE BOND HOLDS", new Color(0.75f, 0.85f, 1f));
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 1f, 0.55f);
            Vfx.Ring(Vfx.Floor(transform.position) + Vector3.up * 0.05f, 2.2f, new Color(0.75f, 0.85f, 1f), 0.7f);
        }
    }
}
