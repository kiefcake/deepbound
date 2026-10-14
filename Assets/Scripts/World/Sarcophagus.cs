using Deepbound.Art;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// A stone sarcophagus in a crypt. Get close and the lid grinds aside and its occupant
    /// climbs out to meet you. A crypt is quiet until it is not — and you choose the pace by
    /// how you walk through it.
    /// </summary>
    public class Sarcophagus : NetworkBehaviour
    {
        private const float WakeRange = 4.5f;

        public readonly NetworkVariable<bool> Opened = new NetworkVariable<bool>();

        private static readonly System.Collections.Generic.List<Sarcophagus> All = new System.Collections.Generic.List<Sarcophagus>();

        private NetworkObject occupantPrefab;
        private int players = 1, depth, roomId = -1;
        private float wakeAt = -1f, rattleUntil;
        private Transform lid;
        private float open;

        public bool HasOccupant => occupantPrefab != null;

        /// <summary>Server: already queued to open (its crypt has woken).</summary>
        public bool Scheduled => wakeAt >= 0f;

        /// <summary>Tests: why a coffin did or did not wake.</summary>
        public string DebugState()
        {
            string players = "";
            foreach (var p in PlayerLife.All)
                if (p != null) players += p.name + "@" + Vector3.Distance(p.transform.position, transform.position).ToString("F1") + (p.Targetable ? "" : "(untargetable)") + " ";
            return "enabled " + isActiveAndEnabled + ", last tick " + (Time.time - lastTick).ToString("F2") + "s ago, room " + roomId + ", players " + players.Trim();
        }
        private float lastTick = -99f;

        public void ServerLoad(NetworkObject prefab, int playerCount, int floorDepth, int room)
        {
            occupantPrefab = prefab;
            players = playerCount;
            depth = floorDepth;
            roomId = room;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            BuildVisual();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        /// <summary>
        /// Server: the first coffin to open wakes the crypt. The rest follow two at a time,
        /// nearest first, lids rattling a moment before they go — a rising tide, not a wall.
        /// </summary>
        private void WakeTheCrypt()
        {
            var rest = All.FindAll(s => s != null && s != this && s.roomId == roomId && !s.Opened.Value && s.wakeAt < 0f);
            rest.Sort((a, b) => Vector3.Distance(a.transform.position, transform.position).CompareTo(Vector3.Distance(b.transform.position, transform.position)));
            for (int i = 0; i < rest.Count; i++) rest[i].wakeAt = Time.time + 2.2f + (i / 2) * 2.6f;
        }

        private void Update()
        {
            open = Mathf.MoveTowards(open, Opened.Value ? 1f : 0f, Time.deltaTime * 2.5f);
            if (lid != null)
            {
                lid.localPosition = new Vector3(0.7f * open, 0.9f - 0.6f * open, 0f);
                lid.localRotation = Quaternion.Euler(0f, 0f, -35f * open);
            }

            if (lid != null && Time.time < rattleUntil)
                lid.localPosition += new Vector3(Random.Range(-0.02f, 0.02f), Random.Range(0f, 0.03f), Random.Range(-0.02f, 0.02f));

            if (!IsServer || Opened.Value || occupantPrefab == null) return;
            lastTick = Time.time;
            if (wakeAt >= 0f)
            {
                if (Time.time >= wakeAt - 0.9f && rattleUntil < Time.time) { rattleUntil = wakeAt; RattleRpc(wakeAt - Time.time); }
                if (Time.time >= wakeAt) Wake();
                return;
            }
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                if (Vector3.Distance(p.transform.position, transform.position) > WakeRange) continue;
                Wake();
                WakeTheCrypt();
                return;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RattleRpc(float seconds)
        {
            rattleUntil = Time.time + seconds;
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up, 0.8f, 0.7f);
        }

        private void Wake()
        {
            Opened.Value = true;
            var at = transform.position + transform.forward * 1.3f;
            if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
            var obj = Instantiate(occupantPrefab, at, transform.rotation);
            obj.Spawn(destroyWithScene: true);
            var brain = obj.GetComponent<EnemyBrain>();
            brain.ServerScale(players, depth);
            brain.ServerAggroNearest();
            WakeFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WakeFxRpc()
        {
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.7f, 1.4f);
            BoneBurst.Spawn(transform.position + Vector3.up * 0.4f, 0.7f, false, Mat.Rubble);
        }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Base", MeshKit.Box(new Vector3(2.2f, 0.8f, 0.95f)), Mat.StoneWall, new Vector3(0f, 0.4f, 0f));
            lid = MeshKit.Pivot(transform, "Lid", new Vector3(0f, 0.9f, 0f));
            MeshKit.Part(lid, "Slab", MeshKit.Box(new Vector3(2.3f, 0.2f, 1.05f)), Mat.StoneWall, Vector3.zero);
            MeshKit.Part(lid, "Effigy", MeshKit.Box(new Vector3(1.6f, 0.12f, 0.4f)), Mat.Bone, new Vector3(0f, 0.15f, 0f));
            MeshKit.Part(lid, "Head", MeshKit.Ball(0.14f, 8, 6), Mat.Bone, new Vector3(-0.85f, 0.2f, 0f));
        }
    }
}
