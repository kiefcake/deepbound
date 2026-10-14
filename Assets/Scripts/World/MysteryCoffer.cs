using Deepbound.Art;
using Deepbound.Loot;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Mystery Coffer, in the gambling tent: pay the price, open the lid, take what is
    /// inside. Usually gear (often fine), sometimes a rune or a Sigil, rarely a relic — and
    /// now and then just a handful of embers for your trouble.
    /// </summary>
    public class MysteryCoffer : Interactable
    {
        private Transform lid;
        private float lidOpen, busyUntil;
        private float openUntil;

        public static int Price => 120 + 40 * DungeonDirector.GearDepth;

        public override float Range => 2.4f;

        public override string Prompt(PlayerProgress who) =>
            who.Gold.Value < Price ? "Mystery Coffer: " + Price + " gold (you have " + who.Gold.Value + ")" : "Open the Mystery Coffer  -  " + Price + " gold";

        protected override void ServerUse(PlayerProgress who)
        {
            if (Time.time < busyUntil || !who.ServerSpendGold(Price)) return;
            busyUntil = Time.time + 1.5f;
            int depth = DungeonDirector.GearDepth;
            var at = transform.position + transform.forward * 1.1f + Vector3.up * 0.05f;
            float r = Random.value;
            string prize;
            if (r < 0.45f)
            {
                var item = Gear.Roll(depth, Random.value < 0.35f ? 1 : 0);
                GearDrop.ServerSpawn(item, who.OwnerClientId, at);
                prize = Gear.Name(item);
            }
            else if (r < 0.65f) { DungeonDirector.Instance.ServerDropLoot(at, LootKind.Rune, (int)Runes.Random()); prize = "a rune"; }
            else if (r < 0.75f) { DungeonDirector.Instance.ServerDropLoot(at, LootKind.Sigil, 1); prize = "a Sigil"; }
            else if (r < 0.80f) { DungeonDirector.Instance.ServerDropLoot(at, LootKind.Relic, (int)Relics.Random()); prize = "A RELIC"; }
            else { DungeonDirector.Instance.ServerDropLoot(at, LootKind.Embers, Random.Range(15, 31)); prize = "a few embers"; }
            OpenFxRpc(who.name, prize, r < 0.80f && r >= 0.75f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OpenFxRpc(string who, string prize, bool jackpot)
        {
            openUntil = Time.time + 2f;
            Sfx.Play(Sound.Relic, transform.position + Vector3.up, 0.9f, jackpot ? 0.8f : 1.2f);
            var gold = new Color(1f, 0.8f, 0.35f);
            Vfx.Embers(transform.position + Vector3.up * 0.8f, jackpot ? 40 : 16, gold, 4f, 0.06f, 0.9f);
            UI.GameHud.Feed(who + " opens the Mystery Coffer: " + prize + (jackpot ? "!" : ""), gold);
        }

        private void Update()
        {
            lidOpen = Mathf.MoveTowards(lidOpen, Time.time < openUntil ? 1f : 0f, Time.deltaTime * 3f);
            if (lid != null) lid.localRotation = Quaternion.Euler(-100f * lidOpen, 0f, 0f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // An iron-bound black coffer on a little plinth, with a question-mark glow.
            MeshKit.Part(transform, "Plinth", MeshKit.Box(new Vector3(1.2f, 0.25f, 0.8f)), Mat.StoneWall, Vector3.up * 0.125f);
            MeshKit.Part(transform, "Body", MeshKit.Box(new Vector3(1f, 0.55f, 0.6f)), Mat.Dark, Vector3.up * 0.53f);
            foreach (float x in new[] { -0.35f, 0.35f })
                MeshKit.Part(transform, "Band", MeshKit.Box(new Vector3(0.07f, 0.57f, 0.62f)), Mat.Gold, new Vector3(x, 0.53f, 0f));
            lid = MeshKit.Pivot(transform, "Hinge", new Vector3(0f, 0.8f, -0.3f));
            MeshKit.Part(lid, "Lid", MeshKit.Box(new Vector3(1.02f, 0.18f, 0.62f)), Mat.Dark, new Vector3(0f, 0.09f, 0.3f));
            var gem = MeshKit.Part(lid, "Gem", MeshKit.Ball(0.07f, 8, 6), Mat.Glow, new Vector3(0f, 0.12f, 0.62f));
            ArtLibrary.SetGlow(gem.GetComponent<Renderer>(), new Color(0.75f, 0.45f, 1f) * 1.6f);
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.2f, 0.5f);
            glow.type = LightType.Point;
            glow.color = new Color(0.75f, 0.45f, 1f);
            glow.range = 3f;
            glow.intensity = 1.2f;
        }
    }
}
