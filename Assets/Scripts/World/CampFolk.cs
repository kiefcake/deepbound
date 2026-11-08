using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>The people of the camp, in the order <see cref="UI.CampChatter"/> lists them.</summary>
    public enum FolkId : byte { Silas = 0, Hobb = 1, Aldric = 2, Vey = 3, Corwin = 4, Bones = 5, Peddler = 6, Brann = 7, Ysolde = 8 }

    /// <summary>
    /// The body of a camp character: an invisible capsule with a <see cref="Health"/> that no attack
    /// can actually hurt. What a hit does instead is offend them: the server records a grudge on the
    /// attacker (<see cref="PlayerProgress.Grudge"/>) and everyone hears them react. The merchant,
    /// the smith and the shrine charge a grudge-holder more; the dealer stops dealing to them.
    /// Grudges ease by one each time the party comes back to camp, and amends can be paid at a stall.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class CampFolk : NetworkBehaviour, ICrowdControl
    {
        public readonly NetworkVariable<byte> Who = new NetworkVariable<byte>();

        private Health health;
        private readonly Dictionary<ulong, float> lastOffence = new Dictionary<ulong, float>();

        private void Awake() => health = GetComponent<Health>();
        private Vector3 home;
        private bool homeSet;

        /// <summary>Server: the body walks the same rounds as the figure (World/FolkRounds), so a blow lands where they are.</summary>
        private void Update()
        {
            if (!IsServer || !DungeonDirector.InLobby) return;
            if (!homeSet) { home = transform.position; homeSet = true; }
            var who = (FolkId)Who.Value;
            if (!FolkRounds.Walks(who)) return;
            var pose = FolkRounds.At(who);
            transform.position = pose.Away ? pose.Position + (home.y - pose.Position.y) * Vector3.up : home;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            health.ServerSetMax(1000f, refill: true);
            // A hit never lands as damage: it lands as an insult.
            health.IncomingModifiers.Add((dealt, from) => { ServerOffended(health.LastHitFrom); return 0f; });
        }

        private void ServerOffended(ulong attackerId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) || obj == null) return;
            if (!obj.TryGetComponent<PlayerProgress>(out var who)) return;
            // One offence per attacker per moment: a flurry or a volley is one insult, not twelve.
            if (lastOffence.TryGetValue(attackerId, out var at) && Time.time - at < 1.5f) return;
            lastOffence[attackerId] = Time.time;
            int level = who.ServerAddGrudge((FolkId)Who.Value);
            ReactRpc(Who.Value, (byte)level, obj.name);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ReactRpc(byte folk, byte level, string attacker)
        {
            Art.Sfx.Play(Art.Sound.Hit, transform.position + Vector3.up * 1.4f, 0.4f, 1.2f);
            UI.CampChatter.Offended((FolkId)folk, level, attacker, transform.position);
        }

        // Shield Bash, War Cry and kicks land on a person too; they just do not do anything.
        public void ServerStun(float seconds) { }
        public void ServerTaunt(Health taunter, float seconds) { }
        public void ServerKnockback(Vector3 velocity) { }
    }
}
