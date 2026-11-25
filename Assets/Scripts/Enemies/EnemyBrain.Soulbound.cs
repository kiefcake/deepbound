using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public partial class EnemyBrain
    {
        public const float SoulboundWindow = 5f, SoulboundSoloWindow = 8f, SoulboundRise = 0.5f, SoulboundRiseFade = 0.7f, SoulboundRiseFloor = 0.1f;

        public const int SoulboundMaxRises = 3;
        private int rises;

        public readonly NetworkVariable<ulong> TwinId = new NetworkVariable<ulong>();
        public bool SeveredNow => (FxFlags.Value & 4) != 0;

        private EnemyBrain twin;
        private float severedUntil;
        private bool Severed => severedUntil > 0f;

        public static float SoulboundSeconds => Player.PlayerLife.All.Count > 1 ? SoulboundWindow : SoulboundSoloWindow;

        public void ServerDebugEndWindow() { if (Severed) severedUntil = Time.time; }

        public bool ServerSevered => Severed;

        public static bool ServerBind(EnemyBrain a, EnemyBrain b)
        {
            if (a == null || b == null || a == b || a.IsBoss || b.IsBoss || a.twin != null || b.twin != null || a.IsChampion || b.IsChampion) return false;
            foreach (var x in new[] { a, b })
            {
                if (!x.Elite.Value) x.ServerMakeElite(EliteAffix.Soulbound);
                else x.Affix.Value = (byte)EliteAffix.Soulbound;
                x.health.IncomingModifiers.Add(x.SoulboundModifier);

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

            health.ServerHeal(health.Max.Value * Mathf.Max(SoulboundRiseFloor, SoulboundRise * Mathf.Pow(SoulboundRiseFade, rises++)));
            RisenRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SeveredRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.4f, "SEVERED", new Color(0.8f, 0.9f, 1f));
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
