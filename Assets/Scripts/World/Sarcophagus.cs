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

        private NetworkObject occupantPrefab;
        private int players = 1, depth;
        private Transform lid;
        private float open;

        public void ServerLoad(NetworkObject prefab, int playerCount, int floorDepth)
        {
            occupantPrefab = prefab;
            players = playerCount;
            depth = floorDepth;
        }

        public override void OnNetworkSpawn() => BuildVisual();

        private void Update()
        {
            open = Mathf.MoveTowards(open, Opened.Value ? 1f : 0f, Time.deltaTime * 2.5f);
            if (lid != null)
            {
                lid.localPosition = new Vector3(0.7f * open, 0.9f - 0.6f * open, 0f);
                lid.localRotation = Quaternion.Euler(0f, 0f, -35f * open);
            }

            if (!IsServer || Opened.Value || occupantPrefab == null) return;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                if (Vector3.Distance(p.transform.position, transform.position) > WakeRange) continue;
                Wake();
                return;
            }
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
