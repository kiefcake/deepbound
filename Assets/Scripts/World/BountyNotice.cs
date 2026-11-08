using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// One notice pinned to the camp's quest board: one camp person's work for the next floor - the same quest they
    /// offer in conversation (<see cref="DungeonDirector.OfferOf"/>). Any player can take it for the party.
    /// </summary>
    public class BountyNotice : Interactable
    {
        /// <summary>Whose work this notice is (a <see cref="FolkId"/>).</summary>
        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        private Renderer seal;
        public override float Range => 2.4f;

        public FolkId Giver => (FolkId)Kind.Value;

        public override string Prompt(PlayerProgress who)
        {
            var d = DungeonDirector.Instance;
            if (d == null || !DungeonDirector.InLobby || d.CannotTakeQuest(Giver) != null) return null;
            var q = d.OfferOf(Giver);
            return "Take " + UI.Dialogue.NameOf(Giver) + "'s quest" + (q.Special ? " (SPECIAL)" : "") + ": " + DungeonDirector.QuestTask(q) + "  -  " + DungeonDirector.QuestReward(q);
        }

        protected override void ServerUse(PlayerProgress who) => DungeonDirector.Instance?.ServerTakeQuest(Giver, who.name);

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
            bool mine = d != null && d.QuestIndexFrom(Giver) >= 0;
            bool taken = mine || (d != null && d.CannotTakeQuest(Giver) != null);
            var c = d != null && DungeonDirector.HardContract(d.OfferOf(Giver).Contract) ? new Color(0.8f, 0.15f, 0.12f) : new Color(0.85f, 0.6f, 0.2f);
            ArtLibrary.SetGlow(seal, mine ? c * (1.2f + 0.4f * Mathf.Sin(Time.time * 4f)) : taken ? c * 0.15f : c * 0.6f);
        }
    }
}
