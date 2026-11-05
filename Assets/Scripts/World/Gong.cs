using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Test of Might: a great bronze gong at camp. Hit it with anything — a sword, a
    /// meteor, a full-draw arrow — and it rings with the size of the blow. The heaviest hit
    /// of the session is the record, with the name of whoever struck it. It never breaks.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Gong : NetworkBehaviour
    {
        public static Gong Instance { get; private set; }

        public readonly NetworkVariable<float> Record = new NetworkVariable<float>();
        public readonly NetworkVariable<FixedString64Bytes> RecordHolder = new NetworkVariable<FixedString64Bytes>();

        // Survives camp rebuilds within a session (the gong is respawned at every visit).
        private static float sessionRecord;
        private static string sessionHolder = "";

        private Health health;
        private Transform disc;
        private float swing;

        private void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            BuildVisual();
            if (!IsServer) return;
            Record.Value = sessionRecord;
            RecordHolder.Value = sessionHolder;
            health.ServerSetMax(1e6f, refill: true);
            health.IncomingModifiers.Add(Strike);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Server: every blow lands here first. Measure it, ring, and absorb it.</summary>
        private float Strike(float damage, Vector3 from)
        {
            // Who struck: the nearest player to where the blow came from.
            string who = "Someone";
            float best = float.MaxValue;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, from);
                if (d < best) { best = d; who = p.name; }
            }
            bool record = damage > Record.Value;
            if (record)
            {
                Record.Value = sessionRecord = damage;
                RecordHolder.Value = sessionHolder = who;
            }
            RingRpc(damage, who, record);
            return 0f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RingRpc(float damage, string who, bool record)
        {
            swing = Mathf.Min(1f, swing + 0.3f + damage / 300f);
            UI.CampChatter.GongStruck(who, damage, record);
            // Bigger blows ring deeper and louder.
            float pitch = Mathf.Lerp(1.3f, 0.55f, Mathf.Clamp01(damage / 250f));
            Sfx.Play(Sound.Block, transform.position + Vector3.up * 2f, Mathf.Clamp(0.5f + damage / 150f, 0.5f, 1f), pitch);
            Sfx.Play(Sound.Slam, transform.position + Vector3.up * 2f, 0.4f, pitch * 1.4f);
            var bronze = new Color(1f, 0.72f, 0.3f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 3.2f, Mathf.RoundToInt(damage).ToString(), record ? new Color(1f, 0.9f, 0.4f) : bronze);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 1.5f + damage / 60f, bronze, 0.4f);
            if (record && damage > 1f)
            {
                UI.GameHud.Feed("NEW RECORD on the Test of Might: " + Mathf.RoundToInt(damage) + " by " + who + "!", new Color(1f, 0.85f, 0.35f));
                Vfx.Embers(transform.position + Vector3.up * 2.2f, 30, bronze, 5f, 0.07f, 1f);
            }
        }

        private void Update()
        {
            swing = Mathf.MoveTowards(swing, 0f, Time.deltaTime * 0.6f);
            if (disc != null) disc.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 9f) * 18f * swing, 0f, Mathf.Sin(Time.time * 13f) * 4f * swing);
        }

        private void BuildVisual()
        {
            // The disc hangs from the frame's crossbar (the frame is part of the camp).
            disc = MeshKit.Pivot(transform, "Hang", new Vector3(0f, 3.5f, 0f));
            MeshKit.Part(disc, "Chain", MeshKit.Box(new Vector3(0.04f, 0.5f, 0.04f)), Mat.Steel, new Vector3(0f, -0.25f, 0f));
            MeshKit.Part(disc, "Disc", MeshKit.Cylinder(1.15f, 1.15f, 0.08f, 24), Mat.Gold, new Vector3(0f, -1.6f, 0f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(disc, "Boss", MeshKit.Cylinder(0.35f, 0.3f, 0.12f, 16), Mat.Gold, new Vector3(0f, -1.6f, 0.06f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(disc, "Rim", MeshKit.Lathe("gongRim", new[] { new Vector2(1.1f, 0f), new Vector2(1.2f, 0.05f), new Vector2(1.1f, 0.1f) }, 24, false), Mat.Steel,
                new Vector3(0f, -1.6f, -0.05f), new Vector3(90f, 0f, 0f));
            // Hammered bronze with a darker ring band each side: plain gold under the brazier read as a glowing sun.
            var bronze = new Color(0.36f, 0.24f, 0.11f);
            var band = new Color(0.14f, 0.09f, 0.05f);
            ArtLibrary.Tint(disc.Find("Disc").GetComponent<Renderer>(), bronze);
            ArtLibrary.Tint(disc.Find("Boss").GetComponent<Renderer>(), new Color(0.62f, 0.44f, 0.2f));
            foreach (float s in new[] { -1f, 1f })
            {
                var r1 = MeshKit.Part(disc, "Band", MeshKit.Cylinder(0.82f, 0.82f, 0.01f, 24), Mat.Gold, new Vector3(0f, -1.6f, s * 0.042f), new Vector3(90f, 0f, 0f));
                ArtLibrary.Tint(r1.GetComponent<Renderer>(), band);
                var r2 = MeshKit.Part(disc, "Face", MeshKit.Cylinder(0.7f, 0.7f, 0.01f, 24), Mat.Gold, new Vector3(0f, -1.6f, s * 0.047f), new Vector3(90f, 0f, 0f));
                ArtLibrary.Tint(r2.GetComponent<Renderer>(), bronze);
            }
        }
    }
}
