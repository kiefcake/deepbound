using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A lectern with an open book: one page of Cathedra's history (UI.Journal). F reads it: the page
    /// opens on screen and goes into your journal (kept across runs). Each player keeps their own
    /// journal; the page stays for everyone to read.
    /// </summary>
    public class LorePage : Interactable
    {
        public readonly NetworkVariable<byte> Index = new NetworkVariable<byte>();
        private Light glow;

        public override float Range => 2.2f;

        public override string Prompt(PlayerProgress who) => UI.Journal.Has(Index.Value) ? "Read the page again" : "Read the page";

        protected override void ServerUse(PlayerProgress who) => ReadRpc(RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void ReadRpc(RpcParams p = default)
        {
            bool isNew = UI.Journal.Add(Index.Value);
            UI.GameHud.ShowPage(Index.Value, isNew);
            Sfx.Play2D(Sound.PageTurn, 0.7f);
            if (isNew) Sfx.Play2D(Sound.Relic, 0.4f);
        }

        /// <summary>Tests: read it as the local player would.</summary>
        public void DebugRead()
        {
            UI.Journal.Add(Index.Value);
            UI.GameHud.ShowPage(Index.Value, true);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A wooden lectern, a slanted top, an open book, and a candle burning beside it.
            MeshKit.Part(transform, "Post", MeshKit.Box(new Vector3(0.14f, 1.0f, 0.14f)), Mat.Wood, Vector3.up * 0.5f);
            MeshKit.Part(transform, "Foot", MeshKit.Box(new Vector3(0.5f, 0.06f, 0.4f)), Mat.Wood, Vector3.up * 0.03f);
            MeshKit.Part(transform, "Top", MeshKit.Box(new Vector3(0.55f, 0.04f, 0.4f)), Mat.Wood, new Vector3(0f, 1.05f, 0f), new Vector3(-22f, 0f, 0f));
            foreach (float s in new[] { -1f, 1f })
            {
                var page = MeshKit.Part(transform, "Page", MeshKit.Box(new Vector3(0.22f, 0.012f, 0.3f)), Mat.Cloth, new Vector3(s * 0.12f, 1.085f, 0f), new Vector3(-22f, 0f, s * -4f));
                ArtLibrary.Tint(page.GetComponent<Renderer>(), new Color(0.92f, 0.86f, 0.7f));
            }
            Props.Candles(transform, transform.position + transform.right * 0.45f, new Color(1f, 0.65f, 0.3f));
            glow = new GameObject("PageLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.5f, 0.2f);
            glow.color = new Color(1f, 0.8f, 0.55f);
            glow.range = 4f;
            glow.intensity = 1.4f;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            // Unread pages glow warmer, so you can tell from the doorway.
            if (glow != null) glow.intensity = UI.Journal.Has(Index.Value) ? 0.8f : 1.5f + Mathf.Sin(Time.time * 2f) * 0.25f;
        }
    }
}
