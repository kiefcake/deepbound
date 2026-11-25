using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public enum FolkId : byte { Silas = 0, Hobb = 1, Aldric = 2, Vey = 3, Corwin = 4, Bones = 5, Peddler = 6, Brann = 7, Ysolde = 8, Marta = 9 }

    [RequireComponent(typeof(Health))]
    public class CampFolk : NetworkBehaviour, ICrowdControl
    {
        public readonly NetworkVariable<byte> Who = new NetworkVariable<byte>();

        private Health health;
        private readonly Dictionary<ulong, float> lastOffence = new Dictionary<ulong, float>();

        private void Awake() => health = GetComponent<Health>();
        private Vector3 home;
        private bool homeSet;

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

            health.IncomingModifiers.Add((dealt, from) => { ServerOffended(health.LastHitFrom); return 0f; });
        }

        private void ServerOffended(ulong attackerId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj) || obj == null) return;
            if (!obj.TryGetComponent<PlayerProgress>(out var who)) return;

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

        public void ServerStun(float seconds) { }
        public void ServerTaunt(Health taunter, float seconds) { }
        public void ServerKnockback(Vector3 velocity) { }
    }
}
