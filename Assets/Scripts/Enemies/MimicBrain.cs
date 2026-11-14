using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Mimic: a chest that was never a chest. Reach for its lock and it bites, then
    /// scuttles after you on bony legs, lid snapping. Kill it and it coughs up a better
    /// chest's worth of treasure than a real one — the risk is the reward.
    ///
    /// <para>The tell, for the attentive: now and then a mimic chest's lid lifts a finger's
    /// width, and something breathes.</para>
    /// </summary>
    public class MimicBrain : EnemyBrain
    {
        protected override LootTable Loot => LootTable.Chest;

        private float chompPhase;

        /// <summary>Server: sprung by <paramref name="victim"/> reaching for it — bite at once.</summary>
        public void ServerAmbush(Health victim)
        {
            target = victim;
            victim.ServerDamage(damage * 1.2f, transform.position, NetworkObjectId);
            if (victim.TryGetComponent<Player.KnockbackReceiver>(out var k))
                k.KnockbackRpc((victim.transform.position - transform.position).normalized * 6f + Vector3.up * 2.5f);
            SprungFxRpc();
        }

        protected override void Strike()
        {
            if (target == null) return;
            PlayActionRpc(RigAction.Swing, 0.25f);
            foreach (var victim in CombatQuery.Arc(transform.position + Vector3.up * 0.5f, transform.forward, attackRange + 0.3f, 100f, Team.Players))
                victim.ServerDamage(damage, transform.position, NetworkObjectId);
            ChompRpc();
        }

        protected override void OnDied(Health h)
        {
            base.OnDied(h);
            // Its hoard: a real chest's gear roll on top of the chest loot table.
            GearDrop.ServerSpawnForParty(transform.position + transform.forward * 0.8f, 0.8f, 0);
        }

        private void LateUpdate()
        {
            if (Rig == null || Rig.Head == null) return;
            // The lid: snapping while it moves or bites, clamped shut when dead.
            float speed = IsDead ? 0f : 1f;
            chompPhase += Time.deltaTime * 11f * speed;
            float open = IsDead ? 0f : 18f + 22f * Mathf.Max(0f, Mathf.Sin(chompPhase));
            Rig.Head.localRotation = Quaternion.Euler(-open, 0f, 0f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SprungFxRpc()
        {
            Sfx.Play(Sound.Hurt, transform.position + Vector3.up, 1f, 0.5f);
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 1f, 0.6f);
            Vfx.Embers(transform.position + Vector3.up * 0.7f, 12, new Color(1f, 0.75f, 0.3f), 4f, 0.05f, 0.6f);
            var local = Player.NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, transform.position) < 6f) local.AddShake(0.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChompRpc() => Sfx.Play(Sound.Bash, transform.position + Vector3.up * 0.6f, 0.8f, 1.5f);
    }
}
