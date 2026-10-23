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
        private float lidAngle, lidVel, openedAt = -10f;
        private Renderer gleam;
        private Light gleamLight;

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
            LootDrop.ServerSpawnFromTable(lootPrefab, LootTable.Chest, transform.position + transform.forward * 1.1f, 1f + 0.5f * DungeonDirector.Tier(depth), Enemies.Difficulty.FixedLootShare(Enemies.Difficulty.Party));
            GearDrop.ServerSpawnForParty(transform.position + transform.forward * 1.6f, 0.4f, 0);
            OpenFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OpenFxRpc()
        {
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f, 0.8f);
            // A gust of gold light out of the opened lid.
            var gold = new Color(1f, 0.78f, 0.35f);
            var mouth = transform.position + Vector3.up * 0.7f;
            Vfx.Flash(mouth, gold, 4f, 5f, 0.5f);
            Vfx.Embers(mouth, 18, gold, 3.5f, 0.05f, 1.1f);
            openedAt = Time.time;
        }

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
            if (Opened.Value)
            {
                // Thrown open: a spring that overshoots and bounces against the hinge.
                lidVel += ((target - lidAngle) * 140f - lidVel * 11f) * Time.deltaTime;
                lidAngle += lidVel * Time.deltaTime;
                if (lidAngle < -122f) { lidAngle = -122f; lidVel = Mathf.Abs(lidVel) * 0.35f; }
            }
            else lidAngle = Mathf.MoveTowards(lidAngle, target, Time.deltaTime * 260f);
            lid.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);
            if (gleam != null)
            {
                float since = Time.time - openedAt;
                float k = Opened.Value ? (since < 1f ? 2f - since * 0.8f : 1.2f) : 1f + Mathf.Sin(Time.time * 2f) * 0.2f;
                gleamLight.intensity = 1.8f * k;
            }
        }

        private void BuildVisual()
        {
            var iron = Mat.Steel;
            MeshKit.Part(transform, "Body", MeshKit.Box(new Vector3(1.1f, 0.56f, 0.7f)), Mat.Wood, new Vector3(0f, 0.32f, 0f));
            // Plinth and four squat feet.
            MeshKit.Part(transform, "Base", MeshKit.Box(new Vector3(1.16f, 0.06f, 0.76f)), Mat.Wood, new Vector3(0f, 0.07f, 0f));
            foreach (float x in new[] { -0.5f, 0.5f })
                foreach (float z in new[] { -0.3f, 0.3f })
                    MeshKit.Part(transform, "Foot", MeshKit.Box(new Vector3(0.12f, 0.06f, 0.12f)), iron, new Vector3(x, 0.03f, z));
            // Gold bands front to back, iron corner brackets, studs.
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.36f : 0.36f;
                MeshKit.Part(transform, "Band" + i, MeshKit.Box(new Vector3(0.08f, 0.58f, 0.72f)), Mat.Gold, new Vector3(x, 0.33f, 0f));
                for (int k = 0; k < 3; k++)
                    MeshKit.Part(transform, "Stud", MeshKit.Ball(0.018f, 6, 4), Mat.Gold, new Vector3(x, 0.14f + k * 0.17f, 0.365f));
            }
            foreach (float x in new[] { -0.54f, 0.54f })
                foreach (float z in new[] { -0.34f, 0.34f })
                    MeshKit.Part(transform, "Bracket", MeshKit.Box(new Vector3(0.05f, 0.58f, 0.05f)), iron, new Vector3(x, 0.33f, z));
            // Ring handles on the sides.
            foreach (float x in new[] { -0.56f, 0.56f })
                MeshKit.Part(transform, "Handle", MeshKit.Cylinder(0.07f, 0.07f, 0.02f, 10), iron, new Vector3(x, 0.4f, 0f), new Vector3(0f, 0f, 90f), new Vector3(1f, 1f, 0.35f));

            // The lid hinges at the back top edge: a stepped dome, so it reads as curved.
            lid = MeshKit.Pivot(transform, "LidHinge", new Vector3(0f, 0.6f, -0.35f));
            MeshKit.Part(lid, "Lid", MeshKit.Box(new Vector3(1.12f, 0.12f, 0.72f)), Mat.Wood, new Vector3(0f, 0.06f, 0.35f));
            MeshKit.Part(lid, "LidMid", MeshKit.Box(new Vector3(1.1f, 0.08f, 0.58f)), Mat.Wood, new Vector3(0f, 0.15f, 0.35f));
            MeshKit.Part(lid, "LidTop", MeshKit.Box(new Vector3(1.08f, 0.05f, 0.38f)), Mat.Wood, new Vector3(0f, 0.21f, 0.35f));
            foreach (float x in new[] { -0.36f, 0.36f })
            {
                MeshKit.Part(lid, "LidBand", MeshKit.Box(new Vector3(0.085f, 0.13f, 0.74f)), Mat.Gold, new Vector3(x, 0.06f, 0.35f));
                MeshKit.Part(lid, "LidBandMid", MeshKit.Box(new Vector3(0.085f, 0.09f, 0.6f)), Mat.Gold, new Vector3(x, 0.15f, 0.35f));
                MeshKit.Part(lid, "LidBandTop", MeshKit.Box(new Vector3(0.085f, 0.06f, 0.4f)), Mat.Gold, new Vector3(x, 0.21f, 0.35f));
            }
            MeshKit.Part(lid, "LidLip", MeshKit.Box(new Vector3(1.14f, 0.05f, 0.06f)), iron, new Vector3(0f, 0.02f, 0.71f));
            MeshKit.Part(lid, "Hasp", MeshKit.Box(new Vector3(0.1f, 0.14f, 0.03f)), Mat.Gold, new Vector3(0f, -0.04f, 0.73f));
            MeshKit.Part(transform, "Lock", MeshKit.Box(new Vector3(0.16f, 0.16f, 0.05f)), Mat.Gold, new Vector3(0f, 0.48f, 0.37f));
            MeshKit.Part(transform, "Keyhole", MeshKit.Box(new Vector3(0.025f, 0.06f, 0.02f)), Mat.Dark, new Vector3(0f, 0.47f, 0.4f));

            var glow = MeshKit.Part(transform, "Gleam", MeshKit.Box(new Vector3(0.95f, 0.02f, 0.55f)), Mat.Glow, new Vector3(0f, 0.6f, 0f));
            ArtLibrary.SetGlow(glow.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.3f) * 1.5f);
            gleam = glow.GetComponent<Renderer>();
            // A heap of coins inside, seen when the lid is up.
            for (int i = 0; i < 9; i++)
                MeshKit.Part(transform, "Coin", MeshKit.Cylinder(0.05f, 0.05f, 0.012f, 8), Mat.Gold,
                    new Vector3(-0.3f + (i % 5) * 0.15f, 0.6f + (i % 2) * 0.012f, -0.12f + (i / 5) * 0.2f), new Vector3(8f * (i % 3 - 1), i * 40f, 6f * (i % 2)));
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = new Vector3(0f, 1f, 0.3f);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.75f, 0.35f);
            light.range = 3.5f;
            light.intensity = 1.8f;
            gleamLight = light;
        }
    }
}
