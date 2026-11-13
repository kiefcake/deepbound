using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A SHRINE OF TRADE-OFF (2026-11-13, the user: "Hidden altars offering a cursed bargain, e.g. '+15% damage for the run,
    /// -20 max HP', for a 'should I?' moment"). It stands in a secret niche (behind a cracked wall) instead of a chest; each
    /// offers one <see cref="PlayerProgress.Bargain"/>, and each delver decides for themselves: F once reads the terms,
    /// F again within <see cref="ConfirmSeconds"/> seals it in blood (a cut of <see cref="BloodCost"/> of your health now, the
    /// bargain for the rest of the run). A Cleric pays half the price. Each bargain only once a run.
    /// </summary>
    public class BargainAltar : Interactable
    {
        public const float ConfirmSeconds = 4f, BloodCost = 0.1f;
        public readonly NetworkVariable<byte> Offer = new NetworkVariable<byte>();
        /// <summary>Who has sealed it (bits by client id &lt; 16): the candles gutter for them.</summary>
        public readonly NetworkVariable<ushort> SealedBy = new NetworkVariable<ushort>();

        private readonly Dictionary<ulong, float> primed = new Dictionary<ulong, float>();
        private Renderer eyes;

        public override float Range => 2.2f;
        public PlayerProgress.Bargain Kind => (PlayerProgress.Bargain)Offer.Value;

        public void Prepare(PlayerProgress.Bargain b) => Offer.Value = (byte)b;

        public override string Prompt(PlayerProgress who)
        {
            if (who == null) return null;
            if (who.HasBargain(Kind)) return "The altar has had its due from you  (" + PlayerProgress.BargainName(Kind) + ")";
            return "A cursed bargain - " + PlayerProgress.BargainName(Kind) + ": " + PlayerProgress.BargainText(Kind) + (who.ClassId.Value == Classes.ClassId.Cleric ? "  (your faith halves the price)" : "") + "   F, then F again to seal it";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (who == null || who.HasBargain(Kind)) return;
            ulong id = who.OwnerClientId;
            if (!primed.TryGetValue(id, out var at) || Time.time - at > ConfirmSeconds)
            {
                primed[id] = Time.time;
                TermsRpc(PlayerProgress.BargainName(Kind), PlayerProgress.BargainText(Kind), RpcTarget.Single(id, RpcTargetUse.Temp));
                return;
            }
            primed.Remove(id);
            ServerSeal(who);
        }

        /// <summary>Server: <paramref name="who"/> takes the bargain (tests call this).</summary>
        public bool ServerSeal(PlayerProgress who)
        {
            if (who == null || !who.ServerStrikeBargain(Kind)) return false;
            var h = who.GetComponent<Combat.Health>();
            if (h != null && h.IsAlive) h.ServerSetFraction(Mathf.Max(0.05f, h.Fraction - BloodCost));
            if (who.OwnerClientId < 16) SealedBy.Value |= (ushort)(1 << (int)who.OwnerClientId);
            SealedRpc(who.name, PlayerProgress.BargainName(Kind), PlayerProgress.BargainText(Kind));
            return true;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void TermsRpc(string name, string text, RpcParams p = default)
        {
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up, 0.8f, 0.7f);
            UI.GameHud.ShowTitleCard(name.ToUpperInvariant(), text + "  -  F again to seal it, or walk away", new Color(0.85f, 0.3f, 0.3f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SealedRpc(string who, string name, string text)
        {
            Sfx.Play(Sound.Toll, transform.position + Vector3.up, 0.6f, 0.6f);
            Sfx.Play(Sound.Hurt, transform.position + Vector3.up, 0.5f, 0.8f);
            Vfx.Embers(transform.position + Vector3.up * 1.1f, 18, new Color(0.8f, 0.1f, 0.08f), 2f, 0.06f, 1f);
            UI.GameHud.Feed(who + " seals " + name + " in blood: " + text, new Color(0.9f, 0.4f, 0.35f));
            UI.Tips.OfferNow("bargain", "SHRINES OF TRADE-OFF hide behind cracked walls: something for the run, for a price. Each of you decides for yourself. A Cleric pays half.");
            if (eyes != null) ArtLibrary.SetGlow(eyes, new Color(1f, 0.15f, 0.1f) * 2.4f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A black stone altar, a bowl of blood, two black candles and a cracked idol whose eyes watch you.
            var stone = new Color(0.3f, 0.26f, 0.27f);
            var slab = MeshKit.Part(transform, "Altar", MeshKit.Box(new Vector3(1.1f, 0.85f, 0.6f)), Mat.StoneWall, Vector3.up * 0.425f);
            ArtLibrary.Tint(slab.GetComponent<Renderer>(), stone);
            var top = MeshKit.Part(transform, "Top", MeshKit.Box(new Vector3(1.25f, 0.08f, 0.72f)), Mat.StoneWall, Vector3.up * 0.89f);
            ArtLibrary.Tint(top.GetComponent<Renderer>(), stone * 1.2f);
            MeshKit.Part(transform, "Bowl", MeshKit.Cylinder(0.12f, 0.17f, 0.08f, 10), Mat.Bone, new Vector3(0f, 0.97f, 0.05f));
            var blood = MeshKit.Part(transform, "Blood", MeshKit.Cylinder(0.15f, 0.15f, 0.01f, 10), Mat.Glow, new Vector3(0f, 1.01f, 0.05f));
            ArtLibrary.SetGlow(blood.GetComponent<Renderer>(), new Color(0.35f, 0.02f, 0.02f));
            foreach (float s in new[] { -1f, 1f })
            {
                var c = MeshKit.Part(transform, "Candle", MeshKit.Cylinder(0.04f, 0.04f, 0.22f, 6), Mat.Dark, new Vector3(s * 0.45f, 1.04f, 0.1f));
                var f = MeshKit.Part(transform, "Flame", MeshKit.Ball(0.03f, 5, 4), Mat.Glow, new Vector3(s * 0.45f, 1.18f, 0.1f));
                ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(0.9f, 0.25f, 0.15f) * 1.5f);
            }
            // The idol: a squat horned head, split down the middle.
            var idol = MeshKit.Part(transform, "Idol", MeshKit.Box(new Vector3(0.34f, 0.42f, 0.26f)), Mat.StoneWall, new Vector3(0f, 1.15f, -0.18f));
            ArtLibrary.Tint(idol.GetComponent<Renderer>(), stone * 0.9f);
            foreach (float s in new[] { -1f, 1f })
                MeshKit.Part(transform, "Horn", MeshKit.Cylinder(0.05f, 0.01f, 0.24f, 5), Mat.Bone, new Vector3(s * 0.15f, 1.4f, -0.18f), new Vector3(0f, 0f, -s * 30f));
            var e = MeshKit.Part(transform, "Eyes", MeshKit.Box(new Vector3(0.22f, 0.04f, 0.02f)), Mat.Glow, new Vector3(0f, 1.24f, -0.045f));
            eyes = e.GetComponent<Renderer>();
            eyes.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(eyes, new Color(1f, 0.2f, 0.1f) * 1.3f);
            // 2026-11-13 shots: in a dark niche the slab was a black box. Its own low red light, and a carved sigil on the
            // front that smoulders.
            var l = new GameObject("AltarLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0f, 1.35f, 0.75f);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.35f, 0.25f);
            l.range = 4.5f;
            l.intensity = 1.8f;
            l.shadows = LightShadows.None;
            foreach (var (px, py, sx, sy, rz) in new[] { (0f, 0.45f, 0.04f, 0.5f, 0f), (0f, 0.55f, 0.36f, 0.04f, 0f), (-0.12f, 0.3f, 0.04f, 0.26f, 35f), (0.12f, 0.3f, 0.04f, 0.26f, -35f) })
            {
                var rune = MeshKit.Part(transform, "Sigil", MeshKit.Box(new Vector3(sx, sy, 0.02f)), Mat.Glow, new Vector3(px, py, 0.305f), new Vector3(0f, 0f, rz));
                ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), new Color(0.7f, 0.08f, 0.05f) * 1.2f);
                rune.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }
    }
}
