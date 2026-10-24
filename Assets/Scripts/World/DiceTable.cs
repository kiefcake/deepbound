using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Bones, in the gambling tent: you and the house each roll two dice. Beat the house and
    /// your stake doubles; tie and it comes back; lose and the house keeps it. The stake is a
    /// quarter of your purse (10 to 100 gold), so it always feels like it matters.
    /// </summary>
    public class DiceTable : Interactable
    {
        private float busyUntil;

        public static int Stake(int gold) => Mathf.Clamp(Mathf.RoundToInt(gold * 0.25f / 5f) * 5, 10, 100);

        public override float Range => 2.4f;

        public override string Prompt(PlayerProgress who)
        {
            int stake = Stake(who.Gold.Value);
            return who.Gold.Value < stake ? "Bones: you need " + stake + " gold to play" : "Roll the bones  -  stake " + stake + " gold";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            int stake = Stake(who.Gold.Value);
            if (Time.time < busyUntil || who.Gold.Value < stake) return;
            busyUntil = Time.time + 1.2f;
            int a = Random.Range(1, 7), b = Random.Range(1, 7), c = Random.Range(1, 7), d = Random.Range(1, 7);
            int mine = a + b, house = c + d;
            int result = mine > house ? 1 : mine == house ? 0 : -1;
            // Snake eyes for the house is always a loss; double six for you always a win.
            if (a == 6 && b == 6) result = 1;
            who.ServerAddGold(result * stake);
            RollFxRpc(who.name, a, b, c, d, result, stake);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RollFxRpc(string who, int a, int b, int c, int d, int result, int stake)
        {
            Sfx.Play(Sound.Dice, transform.position + Vector3.up, 0.9f);
            // The dice themselves: four bone cubes tumbling across the table.
            for (int i = 0; i < 4; i++)
            {
                var die = MeshKit.Part(null, "Die", MeshKit.Box(Vector3.one * 0.12f), Mat.Bone, transform.position + Vector3.up * 1.2f + transform.right * (i - 1.5f) * 0.2f,
                    new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), 0f));
                var body = die.AddComponent<Rigidbody>();
                die.AddComponent<BoxCollider>().size = Vector3.one * 0.12f;
                body.linearVelocity = transform.forward * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.5f;
                body.angularVelocity = Random.insideUnitSphere * 20f;
                die.layer = 2;
                Destroy(die, 2.5f);
            }
            string line = who + " rolls " + a + "+" + b + " against the house's " + c + "+" + d + ":  " +
                          (result > 0 ? "WINS " + stake + " gold!" : result == 0 ? "a draw" : "loses " + stake + " gold");
            var color = result > 0 ? new Color(1f, 0.85f, 0.35f) : result == 0 ? Color.white : new Color(1f, 0.45f, 0.4f);
            UI.GameHud.Feed(line, color);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 1.6f, result > 0 ? "+" + stake : result == 0 ? "DRAW" : "-" + stake, color);
            if (result > 0) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f, 1.1f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A low round table, a green baize top, and the house's cup.
            MeshKit.Part(transform, "Leg", MeshKit.Cylinder(0.15f, 0.25f, 0.85f, 10), Mat.Wood, Vector3.zero);
            MeshKit.Part(transform, "Top", MeshKit.Cylinder(0.75f, 0.75f, 0.08f, 16), Mat.Wood, Vector3.up * 0.85f);
            var baize = MeshKit.Part(transform, "Baize", MeshKit.Cylinder(0.68f, 0.68f, 0.02f, 16), Mat.Cloth, Vector3.up * 0.93f);
            ArtLibrary.Tint(baize.GetComponent<Renderer>(), new Color(0.12f, 0.32f, 0.16f));
            MeshKit.Part(transform, "Cup", MeshKit.Cylinder(0.1f, 0.13f, 0.22f, 10), Mat.Leather, new Vector3(-0.35f, 0.94f, 0.2f));
            for (int i = 0; i < 2; i++)
                MeshKit.Part(transform, "Die" + i, MeshKit.Box(Vector3.one * 0.1f), Mat.Bone, new Vector3(0.15f + i * 0.14f, 1f, -0.1f), new Vector3(0f, i * 30f, 0f));
        }
    }
}
