using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The floor's Reliquary (R.E.P.O.'s haul): a gilded shrine-box in a side room, too heavy to
    /// carry and fight. One player hauls it to the start room's waystone while the others guard
    /// them. It sings as it goes, and the dead come to the song (<see cref="HymnInterval"/>).
    /// Every hit the carrier takes chips its <see cref="Integrity"/>; the reward, paid to the whole
    /// party, scales with how intact it arrives.
    /// Reworked 2026-10-20 so it is always worth the haul: while it is carried its hymn MENDS the
    /// escort (allies within <see cref="AuraRadius"/>); delivered, it raises every fallen player,
    /// pays gold and embers, gives a relic each from 50% intact and a Sigil each from 70%, and
    /// HALLOWS the floor: its boss or Warden wakes weakened (<see cref="DungeonDirector.Hallowed"/>).
    /// </summary>
    public class Reliquary : Carryable
    {
        public const float HymnInterval = 4f, HymnRadius = 14f;
        /// <summary>The hymn mends allies near the carrier (not the carrier): this fraction of max health a second.</summary>
        public const float AuraRadius = 6f, AuraHeal = 0.012f;
        private float nextAura;

        /// <summary>0..100: how intact it is. The payout scales with it (never below a quarter).</summary>
        public readonly NetworkVariable<float> Integrity = new NetworkVariable<float>(100f);

        private float nextHymn;
        private Light glow;

        public override string ItemName => "the Reliquary";
        public override float CarrySpeed => 0.6f;
        protected override float DropFraction => 0.12f;
        public override float DeliverSeconds => 3f;
        protected override Vector3 HoldOffset => new Vector3(0f, 0.85f, 0.75f);
        public override Color MarkerColor => new Color(1f, 0.85f, 0.45f);

        public static Reliquary Current
        {
            get { foreach (var c in All) if (c is Reliquary r) return r; return null; }
        }

        /// <summary>What each player gets for a delivery at this integrity, on this floor.</summary>
        public static (int gold, int embers, bool sigil) Reward(float integrity, int depth)
        {
            float k = Mathf.Max(0.35f, integrity / 100f);
            float t = DungeonDirector.Tier(depth);
            return (Mathf.RoundToInt((120 + 55 * t) * k), Mathf.RoundToInt((12 + 5 * t) * k), integrity >= 70f);
        }

        /// <summary>From this integrity every player also gets a relic they lack.</summary>
        public const float RelicIntegrity = 50f;

        protected override void ServerCarrierHurt(float amount, bool knockedLoose)
        {
            Integrity.Value = Mathf.Max(0f, Integrity.Value - amount * 0.6f - (knockedLoose ? 8f : 0f));
            CrackFxRpc();
        }

        protected override void ServerCarriedTick(NetworkObject carrier)
        {
            if (Time.time >= nextAura)
            {
                nextAura = Time.time + 0.5f;
                foreach (var p in PlayerLife.All)
                    if (p != null && p.NetworkObject != carrier && !p.Downed.Value && p.Health.IsAlive && Vector3.Distance(p.transform.position, carrier.transform.position) < AuraRadius)
                        p.Health.ServerHeal(p.Health.Max.Value * AuraHeal * 0.5f);
            }
            if (Time.time < nextHymn) return;
            nextHymn = Time.time + HymnInterval;
            // The song carries: idle dead within earshot turn on the carrier.
            DungeonDirector.Instance?.ServerNoise(carrier.GetComponent<Combat.Health>(), carrier.transform.position, HymnRadius);
            HymnRpc();
        }

        protected override void ServerDelivered(NetworkObject carrier, DungeonLayout layout)
        {
            var (gold, embers, sigil) = Reward(Integrity.Value, layout.Depth);
            bool relic = Integrity.Value >= RelicIntegrity;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.TryGetComponent<PlayerProgress>(out var prog)) continue;
                if (p.Downed.Value && !p.Soul.Value) p.ServerRevive(0.5f);
                prog.ServerAddGold(gold);
                prog.ServerAddEmbers(embers);
                if (sigil) prog.ServerAddSigil();
                if (relic)
                    for (int tries = 0; tries < 30; tries++)
                    {
                        var r = Loot.Relics.Random();
                        if (prog.HasRelic(r)) continue;
                        prog.ServerAddRelic(r);
                        break;
                    }
            }
            if (DungeonDirector.Instance != null) DungeonDirector.Instance.Hallowed.Value = true;
            DeliveredRpc(Mathf.RoundToInt(Integrity.Value), gold, embers, sigil, relic);
            Debug.Log("[Reliquary] delivered at " + Mathf.RoundToInt(Integrity.Value) + "%: " + gold + " gold, " + embers + " embers each");
            if (NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void HymnRpc()
        {
            Sfx.Play(Sound.Relic, transform.position, 0.6f, 0.7f);
            Vfx.Embers(transform.position + Vector3.up * 0.4f, 6, MarkerColor, 1.5f, 0.04f, 0.6f);
            // The mending circle round the carrier, so the escort knows where to stand.
            Vfx.Ring(transform.position - Vector3.up * 0.8f, AuraRadius, new Color(1f, 0.85f, 0.45f), 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CrackFxRpc() => Sfx.Play(Sound.Shatter, transform.position, 0.5f, 1.6f);

        [Rpc(SendTo.ClientsAndHost)]
        private void DeliveredRpc(int integrity, int gold, int embers, bool sigil, bool relic)
        {
            Sfx.Play2D(Sound.Relic, 1f);
            Sfx.Play2D(Sound.Coins, 0.8f);
            UI.GameHud.ShowTitleCard("THE RELIQUARY IS SAFE", "Intact " + integrity + "%:  +" + gold + " gold, +" + embers + " embers" + (sigil ? ", a Sigil" : "") + (relic ? ", a relic" : "") + " each.  The fallen rise, and this floor's guardian is HALLOWED (weakened)");
        }

        protected override void VisualTick(bool carried)
        {
            if (glow != null) glow.intensity = 1.6f + Ritual.Value * 1.2f + Mathf.Sin(Time.time * 3f) * 0.25f;
        }

        protected override void BuildVisual()
        {
            visual = new GameObject("Reliquary").transform;
            visual.SetParent(transform, false);
            // A gilded box on short feet, a gabled lid with a cross, a glowing window, and carrying poles.
            MeshKit.Part(visual, "Body", MeshKit.Box(new Vector3(0.62f, 0.38f, 0.4f)), Mat.Gold, Vector3.up * 0.27f);
            MeshKit.Part(visual, "Base", MeshKit.Box(new Vector3(0.7f, 0.06f, 0.48f)), Mat.Wood, Vector3.up * 0.06f);
            foreach (float x in new[] { -0.28f, 0.28f })
                foreach (float z in new[] { -0.17f, 0.17f })
                    MeshKit.Part(visual, "Foot", MeshKit.Box(new Vector3(0.06f, 0.05f, 0.06f)), Mat.Gold, new Vector3(x, 0.025f, z));
            var roof = MeshKit.Lathe("reliquaryRoof", new[] { new Vector2(0.3f, 0f), new Vector2(0f, 0.18f) }, 4);
            MeshKit.Part(visual, "Roof", roof, Mat.Gold, Vector3.up * 0.46f, new Vector3(0f, 45f, 0f), new Vector3(1.45f, 1f, 1f));
            MeshKit.Part(visual, "CrossV", MeshKit.Box(new Vector3(0.03f, 0.2f, 0.03f)), Mat.Gold, Vector3.up * 0.72f);
            MeshKit.Part(visual, "CrossH", MeshKit.Box(new Vector3(0.12f, 0.03f, 0.03f)), Mat.Gold, Vector3.up * 0.77f);
            var window = MeshKit.Part(visual, "Window", MeshKit.Box(new Vector3(0.26f, 0.2f, 0.02f)), Mat.Glow, new Vector3(0f, 0.28f, 0.205f));
            ArtLibrary.SetGlow(window.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.5f) * 1.6f);
            var back = MeshKit.Part(visual, "WindowBack", MeshKit.Box(new Vector3(0.26f, 0.2f, 0.02f)), Mat.Glow, new Vector3(0f, 0.28f, -0.205f));
            ArtLibrary.SetGlow(back.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.5f) * 1.6f);
            foreach (float x in new[] { -0.36f, 0.36f })
                MeshKit.Part(visual, "Pole", MeshKit.Box(new Vector3(0.04f, 0.04f, 1.1f)), Mat.Wood, new Vector3(x, 0.2f, 0f));
            glow = new GameObject("ReliquaryLight").AddComponent<Light>();
            glow.transform.SetParent(visual, false);
            glow.transform.localPosition = Vector3.up * 0.5f;
            glow.color = new Color(1f, 0.82f, 0.5f);
            glow.range = 5f;
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;
        }
    }
}
