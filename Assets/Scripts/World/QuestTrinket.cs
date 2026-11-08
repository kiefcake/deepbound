using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A GATHER quest's find (2026-11-08): several of them lie in different rooms of the floor, each tucked against a
    /// wall, each with its own tell so a party can hunt for them -
    /// <list type="bullet">
    /// <item>Aldric's saint's knucklebones: in little gilt reliquary niches, a faint gold glow and a whisper nearby.</item>
    /// <item>Brann's fallen standards: his lost squad's banners, a broken pole stood in the floor, torn red cloth.</item>
    /// <item>Ysolde's rune shards: violet crystal splinters that hum (a toll, softly) when you are near.</item>
    /// </list>
    /// F picks one up for the party.
    /// </summary>
    public class QuestTrinket : Interactable
    {
        public readonly NetworkVariable<byte> Giver = new NetworkVariable<byte>();
        public FolkId Who => (FolkId)Giver.Value;
        public override float Range => 2.4f;
        private bool taken;
        private float nextHum;
        private Transform spin;

        public static string NameOf(FolkId who) => who switch
        {
            FolkId.Aldric => "a saint's knucklebone",
            FolkId.Brann => "a fallen standard",
            FolkId.Ysolde => "a rune shard",
            _ => "a keepsake",
        };

        public override string Prompt(PlayerProgress who) => taken ? null : "Take " + NameOf(Who) + "  (" + UI.Dialogue.NameOf(Who) + "'s quest)";

        /// <summary>Server, tests: take it from anywhere.</summary>
        public void ServerTake(PlayerProgress who) => ServerUse(who);

        protected override void ServerUse(PlayerProgress who)
        {
            if (taken) return;
            taken = true;
            TakenRpc(who.name);
            DungeonDirector.Instance?.ServerTrinketTaken(Who, who);
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TakenRpc(string by)
        {
            Sfx.Play(Who == FolkId.Ysolde ? Sound.Toll : Sound.Relic, transform.position + Vector3.up * 0.8f, 0.8f, Who == FolkId.Ysolde ? 1.8f : 1.1f);
            Vfx.Flash(transform.position + Vector3.up * 0.8f, new Color(1f, 0.85f, 0.5f), 2f, 5f, 0.5f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            var v = new GameObject("Trinket").transform;
            v.SetParent(transform, false);
            Color glow;
            switch (Who)
            {
                case FolkId.Brann:
                    glow = new Color(1f, 0.45f, 0.3f);
                    MeshKit.Part(v, "Pole", MeshKit.Cylinder(0.035f, 0.03f, 1.7f, 6), Mat.Wood, Vector3.zero, new Vector3(8f, 0f, 4f));
                    var cloth = MeshKit.Part(v, "Standard", MeshKit.Box(new Vector3(0.5f, 0.7f, 0.02f)), Mat.Cloth, new Vector3(0.3f, 1.25f, 0.05f), new Vector3(0f, 0f, -6f));
                    ArtLibrary.Tint(cloth.GetComponent<Renderer>(), new Color(0.5f, 0.07f, 0.07f));
                    MeshKit.Part(v, "Crest", MeshKit.Box(new Vector3(0.16f, 0.16f, 0.025f)), Mat.Gold, new Vector3(0.3f, 1.3f, 0.06f));
                    break;
                case FolkId.Ysolde:
                    glow = new Color(0.65f, 0.4f, 1f);
                    spin = MeshKit.Pivot(v, "Spin", new Vector3(0f, 0.9f, 0f));
                    for (int i = 0; i < 3; i++)
                    {
                        var shard = MeshKit.Part(spin, "Shard" + i, MeshKit.Lathe("runeShard", new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0.12f), new Vector2(0f, 0.38f) }, 4), Mat.Glow,
                            Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0.07f, -0.15f, 0f), new Vector3(0f, i * 120f, 12f));
                        ArtLibrary.SetGlow(shard.GetComponent<Renderer>(), glow * 1.3f);
                    }
                    MeshKit.Part(v, "Stone", MeshKit.Box(new Vector3(0.4f, 0.3f, 0.35f)), Mat.StoneWall, new Vector3(0f, 0.15f, 0f), new Vector3(0f, 20f, 0f));
                    break;
                default: // Aldric's knucklebone in a niche
                    glow = new Color(1f, 0.82f, 0.45f);
                    MeshKit.Part(v, "Plinth", MeshKit.Box(new Vector3(0.36f, 0.7f, 0.3f)), Mat.StoneWall, new Vector3(0f, 0.35f, 0f));
                    MeshKit.Part(v, "Casket", MeshKit.Box(new Vector3(0.24f, 0.14f, 0.18f)), Mat.Gold, new Vector3(0f, 0.77f, 0f));
                    MeshKit.Part(v, "Bone", MeshKit.Cylinder(0.025f, 0.03f, 0.14f, 6), Mat.Bone, new Vector3(-0.07f, 0.85f, 0f), new Vector3(0f, 0f, -70f));
                    for (int i = 0; i < 2; i++)
                        MeshKit.Part(v, "Candle" + i, MeshKit.Cylinder(0.025f, 0.025f, 0.12f, 6), Mat.Bone, new Vector3(i == 0 ? -0.13f : 0.13f, 0.7f, 0.08f));
                    break;
            }
            var light = new GameObject("TrinketLight").AddComponent<Light>();
            light.transform.SetParent(v, false);
            light.transform.localPosition = new Vector3(0f, 1f, 0.3f);
            light.type = LightType.Point;
            light.color = glow;
            light.range = 4.5f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (spin != null) spin.Rotate(Vector3.up, 40f * Time.deltaTime, Space.Self);
            // Its tell: a hum / whisper when the local player is near (hunt it by ear).
            var me = Player.NetworkPlayer.Local;
            if (me == null || Time.time < nextHum) return;
            float d = Vector3.Distance(me.transform.position, transform.position);
            if (d > 16f) { nextHum = Time.time + 1f; return; }
            nextHum = Time.time + Mathf.Lerp(1.6f, 4.5f, d / 16f);
            var sound = Who == FolkId.Ysolde ? Sound.Toll : Who == FolkId.Aldric ? Sound.Whisper : Sound.Creak;
            Sfx.Play(sound, transform.position + Vector3.up, Mathf.Lerp(0.55f, 0.12f, d / 16f), Who == FolkId.Ysolde ? 2.2f : 1f);
        }
    }
}
