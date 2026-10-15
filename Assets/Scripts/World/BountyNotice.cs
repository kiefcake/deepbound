using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// One notice pinned to the camp's bounty board: a contract for the next floor. Any
    /// player can take one for the party; the others then show as passed over.
    /// </summary>
    public class BountyNotice : Interactable
    {
        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        private Renderer seal;
        public override float Range => 2.4f;

        private Contract Offer => (Contract)Kind.Value;

        public override string Prompt(PlayerProgress who)
        {
            var d = DungeonDirector.Instance;
            if (d == null || !DungeonDirector.InLobby) return null;
            if (d.ContractState.Value == (byte)DungeonDirector.ContractStatus.Taken) return null;
            return "Take " + DungeonDirector.ContractName(Offer).ToUpperInvariant() + ": " + DungeonDirector.ContractTask(Offer, d.NextDepth.Value).Replace(" on the next floor", "").Replace("the next floor", "next floor") +
                (DungeonDirector.HardContract(Offer) ? "  (gold, embers, Sigil)" : "  (gold, embers)");
        }

        protected override void ServerUse(PlayerProgress who)
        {
            DungeonDirector.Instance?.ServerTakeContract(Offer);
            TakenRpc(who.name);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TakenRpc(string who)
        {
            Sfx.Play(Sound.Purchase, transform.position + Vector3.up * 1.6f, 0.8f, 0.9f);
            UI.GameHud.Feed(who + " takes the contract: " + DungeonDirector.ContractName(Offer), new Color(0.95f, 0.8f, 0.5f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // Parchment pinned at eye height, a wax seal coloured by difficulty.
            var paper = MeshKit.Part(transform, "Paper", MeshKit.Box(new Vector3(0.62f, 0.8f, 0.02f)), Mat.Cloth, new Vector3(0f, 1.6f, 0f));
            ArtLibrary.Tint(paper.GetComponent<Renderer>(), new Color(0.86f, 0.8f, 0.64f));
            for (int i = 0; i < 5; i++)
            {
                MeshKit.Part(transform, "Line" + i, MeshKit.Box(new Vector3(0.44f - (i % 2) * 0.1f, 0.025f, 0.005f)), Mat.Dark, new Vector3(0f, 1.82f - i * 0.09f, 0.012f));
            }
            seal = MeshKit.Part(transform, "Seal", MeshKit.Cylinder(0.07f, 0.07f, 0.02f, 10), Mat.Glow, new Vector3(0.18f, 1.32f, 0.02f), new Vector3(90f, 0f, 0f)).GetComponent<Renderer>();
            MeshKit.Part(transform, "Pin", MeshKit.Ball(0.025f, 6, 4), Mat.Steel, new Vector3(0f, 1.97f, 0.02f));
        }

        private void Update()
        {
            if (seal == null) return;
            var d = DungeonDirector.Instance;
            bool taken = d != null && d.ContractState.Value == (byte)DungeonDirector.ContractStatus.Taken;
            bool mine = taken && d.ActiveContract.Value == Kind.Value;
            var c = DungeonDirector.HardContract(Offer) ? new Color(0.8f, 0.15f, 0.12f) : new Color(0.85f, 0.6f, 0.2f);
            ArtLibrary.SetGlow(seal, mine ? c * (1.2f + 0.4f * Mathf.Sin(Time.time * 4f)) : taken ? c * 0.15f : c * 0.6f);
        }
    }
}
