using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The camp shrine: kneel, pay, and carry a blessing down with you for one floor. Each
    /// player is offered their own blessing each visit.
    /// </summary>
    public class Shrine : Interactable
    {
        private Renderer sigil;
        private float pulse;

        public override float Range => 2.6f;

        private static int Seed => DungeonDirector.Instance != null ? DungeonDirector.Instance.Floor.Value.x : 0;

        public override string Prompt(PlayerProgress who)
        {
            if (who.ActiveBlessing.Value != 0) return null;
            var offer = Blessings.OfferFor(who.OwnerClientId, Seed);
            int price = Blessings.Price(DungeonDirector.GearDepth);
            return (who.Gold.Value >= price ? "Receive the " : "Needs " + price + " gold: ") + Blessings.Name(offer) + "  (" + Blessings.Describe(offer) + ")" +
                   (who.Gold.Value >= price ? "  -  " + price + " gold" : "");
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (who.ActiveBlessing.Value != 0) return;
            var offer = Blessings.OfferFor(who.OwnerClientId, Seed);
            if (!who.ServerSpendGold(Blessings.Price(DungeonDirector.GearDepth))) return;
            who.ServerBless(offer);
            BlessFxRpc(who.name, (byte)offer, who.transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlessFxRpc(string who, byte b, Vector3 at)
        {
            var blessing = (Blessing)b;
            var c = Blessings.Color(blessing);
            pulse = 1f;
            Sfx.Play(Sound.Revive, transform.position + Vector3.up, 0.9f, 1.1f);
            Vfx.Ring(at, 2f, c, 0.5f);
            Vfx.Embers(at + Vector3.up, 20, c, 3f, 0.06f, 1f);
            UI.GameHud.Feed(who + " receives the " + Blessings.Name(blessing).Replace("Blessing of ", "blessing of ") + " for the next floor", c);
        }

        private void Update()
        {
            pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime * 0.6f);
            if (sigil != null) ArtLibrary.SetGlow(sigil, new Color(1f, 0.85f, 0.5f) * (0.9f + 0.3f * Mathf.Sin(Time.time * 1.5f) + pulse * 1.5f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // An altar stone with candles, a weathered hooded saint above, a halo of light.
            MeshKit.Part(transform, "Altar", MeshKit.Box(new Vector3(1.6f, 0.9f, 0.8f)), Mat.StoneWall, Vector3.up * 0.45f);
            MeshKit.Part(transform, "AltarTop", MeshKit.Box(new Vector3(1.75f, 0.1f, 0.9f)), Mat.StoneWall, Vector3.up * 0.95f);
            var cloth = MeshKit.Part(transform, "AltarCloth", MeshKit.Box(new Vector3(0.5f, 0.6f, 0.92f)), Mat.Cloth, new Vector3(0f, 0.68f, 0f));
            ArtLibrary.Tint(cloth.GetComponent<Renderer>(), new Color(0.5f, 0.1f, 0.12f));
            MeshKit.Part(transform, "Plinth", MeshKit.Box(new Vector3(0.9f, 0.5f, 0.7f)), Mat.StoneWall, new Vector3(0f, 0.25f, -1f));
            MeshKit.Part(transform, "Saint", MeshKit.Lathe("saintRobe", new[]
            {
                new Vector2(0.32f, 0f), new Vector2(0.26f, 0.9f), new Vector2(0.22f, 1.5f), new Vector2(0.14f, 1.75f), new Vector2(0f, 1.95f),
            }, 10), Mat.StoneWall, new Vector3(0f, 0.5f, -1f));
            sigil = MeshKit.Part(transform, "Halo", MeshKit.Lathe("halo", new[] { new Vector2(0.3f, 0f), new Vector2(0.34f, 0.02f), new Vector2(0.3f, 0.04f) }, 20, false),
                Mat.Glow, new Vector3(0f, 2.55f, -0.95f), new Vector3(90f, 0f, 0f)).GetComponent<Renderer>();
            for (int i = 0; i < 5; i++)
            {
                var p = new Vector3(-0.6f + i * 0.3f, 1f, 0.25f - (i % 2) * 0.15f);
                MeshKit.Part(transform, "Candle" + i, MeshKit.Cylinder(0.035f, 0.035f, 0.12f + (i % 3) * 0.05f, 6), Mat.Bone, p);
                MeshKit.Part(transform, "Flame" + i, MeshKit.Ball(0.025f, 5, 4), Mat.Flame, p + Vector3.up * (0.15f + (i % 3) * 0.05f));
            }
            var l = new GameObject("ShrineLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0f, 1.8f, 0.4f);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.8f, 0.5f);
            l.range = 6f;
            l.intensity = 2f;
        }
    }
}
