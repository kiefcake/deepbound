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
    /// A treasure chest, one per side room: the reason to take the detour. F opens it; the
    /// lid swings up and loot spills out. Opened once, for the whole party.
    /// </summary>
    public class Chest : Interactable
    {
        [SerializeField] private NetworkObject lootPrefab;
        public readonly NetworkVariable<bool> Opened = new NetworkVariable<bool>();
        /// <summary>Not a chest at all. Reaching for it springs the Mimic.</summary>
        public readonly NetworkVariable<bool> Mimic = new NetworkVariable<bool>();
        private float nextBreath;

        private Transform lid;
        private float breathUntil;
        private float lidAngle;

        public void SetLootPrefab(NetworkObject prefab) => lootPrefab = prefab;

        public override string Prompt(PlayerProgress who) => Opened.Value ? null : "Open the chest";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Mimic.Value && DungeonDirector.Instance != null)
            {
                DungeonDirector.Instance.ServerSpringMimic(this, who.GetComponent<Health>());
                return;
            }
            Opened.Value = true;
            int depth = Mathf.Max(0, DungeonDirector.Instance != null ? DungeonDirector.Instance.Floor.Value.y : 0);
            LootDrop.ServerSpawnFromTable(lootPrefab, LootTable.Chest, transform.position + transform.forward * 1.1f, 1f + 0.5f * depth);
            GearDrop.ServerSpawnForParty(transform.position + transform.forward * 1.6f, 0.6f, 0);
            OpenFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OpenFxRpc() => Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f, 0.8f);

        private void Update()
        {
            if (lid == null) return;
            // The tell: a mimic's lid lifts a crack now and then, and it breathes.
            float target = Opened.Value ? -110f : 0f;
            if (Mimic.Value && !Opened.Value)
            {
                if (Time.time >= nextBreath)
                {
                    nextBreath = Time.time + Random.Range(5f, 9f);
                    breathUntil = Time.time + 0.9f;
                    var cam = Art.ViewCamera.Current;
                    if (cam != null && Vector3.Distance(cam.transform.position, transform.position) < 12f)
                        Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 0.6f, 0.5f, 0.45f);
                }
                if (Time.time < breathUntil) target = -9f * Mathf.Sin((breathUntil - Time.time) / 0.9f * Mathf.PI);
            }
            lidAngle = Mathf.MoveTowards(lidAngle, target, Time.deltaTime * 260f);
            lid.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);
        }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Body", MeshKit.Box(new Vector3(1.1f, 0.6f, 0.7f)), Mat.Wood, new Vector3(0f, 0.3f, 0f));
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.38f : 0.38f;
                MeshKit.Part(transform, "Band" + i, MeshKit.Box(new Vector3(0.08f, 0.62f, 0.72f)), Mat.Gold, new Vector3(x, 0.31f, 0f));
            }
            // The lid hinges at the back top edge.
            lid = MeshKit.Pivot(transform, "LidHinge", new Vector3(0f, 0.6f, -0.35f));
            MeshKit.Part(lid, "Lid", MeshKit.Box(new Vector3(1.12f, 0.22f, 0.72f)), Mat.Wood, new Vector3(0f, 0.11f, 0.35f));
            MeshKit.Part(lid, "LidBand", MeshKit.Box(new Vector3(1.14f, 0.06f, 0.1f)), Mat.Gold, new Vector3(0f, 0.12f, 0.7f));
            MeshKit.Part(transform, "Lock", MeshKit.Box(new Vector3(0.14f, 0.16f, 0.05f)), Mat.Gold, new Vector3(0f, 0.5f, 0.37f));
            var glow = MeshKit.Part(transform, "Gleam", MeshKit.Box(new Vector3(0.95f, 0.02f, 0.55f)), Mat.Glow, new Vector3(0f, 0.6f, 0f));
            ArtLibrary.SetGlow(glow.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.3f) * 1.5f);
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = new Vector3(0f, 1f, 0.3f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.35f);
            light.range = 3.5f;
            light.intensity = 1.8f;
        }
    }
}
