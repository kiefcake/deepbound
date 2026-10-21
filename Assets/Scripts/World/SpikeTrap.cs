using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A pressure-plate spike trap in a corridor. Step on it and the plate clicks and glows;
    /// a moment later spikes punch up through anything standing there — skeletons
    /// included, so a clever party can lead a pack across one.
    /// </summary>
    public class SpikeTrap : NetworkBehaviour
    {
        private const float Radius = 1.25f, Delay = 0.55f, Damage = 50f, Rearm = 2.5f;

        private Transform spikes;
        private Renderer plateGlow;
        private float armedAt = -1f, readyAt;
        private float spikeOut, glow;

        public override void OnNetworkSpawn() => BuildVisual();

        private void Update()
        {
            spikeOut = Mathf.MoveTowards(spikeOut, 0f, Time.deltaTime * 1.2f);
            glow = Mathf.MoveTowards(glow, 0f, Time.deltaTime * 0.8f);
            if (spikes != null) spikes.localPosition = new Vector3(0f, Mathf.Lerp(-0.45f, 0.05f, Mathf.Clamp01(spikeOut * 4f)), 0f);
            if (plateGlow != null) ArtLibrary.SetGlow(plateGlow, new Color(1f, 0.2f, 0.1f) * (0.2f + 2.5f * glow));

            if (!IsServer) return;

            if (armedAt > 0f)
            {
                if (Time.time >= armedAt) Fire();
                return;
            }
            if (Time.time < readyAt) return;

            foreach (var h in Health.All)
            {
                if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                var d = h.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1f) continue;
                d.y = 0f;
                if (d.magnitude > Radius) continue;
                armedAt = Time.time + Delay;
                ArmRpc();
                return;
            }
        }

        private void Fire()
        {
            armedAt = -1f;
            readyAt = Time.time + Rearm;
            int depth = Mathf.Max(0, DungeonDirector.Instance != null ? DungeonDirector.Instance.Floor.Value.y : 0);
            foreach (var h in Health.All.ToArray())
            {
                if (h == null || !h.IsAlive || h.Team == Team.Props) continue;
                var d = h.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1f) continue;
                d.y = 0f;
                if (d.magnitude > Radius) continue;
                h.ServerDamage(Damage * (1f + 0.2f * DungeonDirector.Tier(depth)) * (h.Team == Team.Enemies ? 2f : 1f), transform.position + Vector3.down, NetworkObjectId);
            }
            FireRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ArmRpc()
        {
            glow = 1f;
            Sfx.Play(Sound.Bash, transform.position, 0.5f, 2f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FireRpc()
        {
            spikeOut = 1f;
            Sfx.Play(Sound.Shatter, transform.position + Vector3.up * 0.3f, 0.8f, 0.7f);
        }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Plate", MeshKit.Box(new Vector3(2.4f, 0.04f, 2.4f)), Mat.Steel, new Vector3(0f, 0.02f, 0f));
            plateGlow = MeshKit.Part(transform, "Seams", MeshKit.Box(new Vector3(2.2f, 0.045f, 2.2f)), Mat.Glow, new Vector3(0f, 0.02f, 0f)).GetComponent<Renderer>();
            spikes = MeshKit.Pivot(transform, "Spikes", new Vector3(0f, -0.45f, 0f));
            for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
            {
                MeshKit.Part(spikes, "Spike", MeshKit.Lathe("trapSpike", new[] { new Vector2(0.06f, 0f), new Vector2(0f, 0.5f) }, 4), Mat.Steel,
                    new Vector3(-0.9f + x * 0.45f, 0f, -0.9f + z * 0.45f));
            }
            MeshKit.MergeParts(spikes); // the bed of spikes rises as one
        }
    }
}
