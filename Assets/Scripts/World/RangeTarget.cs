using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The archery range at camp: painted targets sliding back and forth on rails. Their
    /// motion is a function of server time, so every peer agrees where they are without
    /// syncing a transform. Hit one with anything and you score; the range keeps a tally per
    /// player for the session. Never breaks.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class RangeTarget : NetworkBehaviour
    {
        /// <summary>Hits per player name, this session (survives camp rebuilds).</summary>
        public static readonly Dictionary<string, int> Tally = new Dictionary<string, int>();

        public readonly NetworkVariable<float> Phase = new NetworkVariable<float>();
        public readonly NetworkVariable<float> Speed = new NetworkVariable<float>(1f);

        private Health health;
        private Vector3 home;
        private Transform face;
        private float flash;

        private const float Travel = 3.2f;

        private void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            home = transform.position;
            BuildVisual();
            if (!IsServer) return;
            health.ServerSetMax(1e6f, refill: true);
            health.IncomingModifiers.Add(Hit);
        }

        private float Hit(float damage, Vector3 from)
        {
            string who = "Someone";
            float best = float.MaxValue;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, from);
                if (d < best) { best = d; who = p.name; }
            }
            Tally.TryGetValue(who, out int n);
            Tally[who] = n + 1;
            HitRpc(who, n + 1);
            return 0f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HitRpc(string who, int total)
        {
            if (!IsServer) Tally[who] = total;
            flash = 1f;
            Sfx.Play(Sound.Hit, transform.position + Vector3.up * 1.4f, 0.8f, 1.5f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "+1", new Color(1f, 0.85f, 0.4f));
            Vfx.Embers(transform.position + Vector3.up * 1.4f, 6, new Color(1f, 0.8f, 0.4f), 2.5f, 0.04f, 0.4f);
        }

        private void Update()
        {
            var net = NetworkManager;
            if (net == null || !IsSpawned) return;
            float t = (float)net.ServerTime.Time * Speed.Value + Phase.Value;
            // Slide along the rail (local right), easing at the ends like a hauled sledge.
            transform.position = home + transform.right * Mathf.Sin(t) * Travel;
            flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 3f);
            if (face != null) face.localRotation = Quaternion.Euler(-flash * 25f, 0f, 0f);
        }

        private void BuildVisual()
        {
            // A straw butt on a little sledge, with a painted bullseye.
            MeshKit.Part(transform, "Sledge", MeshKit.Box(new Vector3(1f, 0.15f, 0.6f)), Mat.Wood, Vector3.up * 0.08f);
            MeshKit.Part(transform, "Post", MeshKit.Box(new Vector3(0.1f, 1f, 0.1f)), Mat.Wood, new Vector3(0f, 0.6f, 0f));
            face = MeshKit.Pivot(transform, "Face", new Vector3(0f, 1.1f, 0f));
            MeshKit.Part(face, "Butt", MeshKit.Cylinder(0.55f, 0.55f, 0.18f, 18), Mat.Wood, new Vector3(0f, 0.35f, 0f), new Vector3(90f, 0f, 0f));
            var colors = new[] { new Color(0.9f, 0.88f, 0.8f), new Color(0.15f, 0.3f, 0.7f), new Color(0.75f, 0.12f, 0.1f), new Color(0.95f, 0.8f, 0.2f) };
            for (int i = 0; i < 4; i++)
            {
                var ring = MeshKit.Part(face, "Ring" + i, MeshKit.Cylinder(0.5f - i * 0.12f, 0.5f - i * 0.12f, 0.02f, 18), Mat.Cloth, new Vector3(0f, 0.35f, 0.19f + i * 0.005f), new Vector3(90f, 0f, 0f));
                ArtLibrary.Tint(ring.GetComponent<Renderer>(), colors[i]);
            }
        }
    }
}
