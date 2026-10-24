using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A fountain of clear water in a side room. Drinking from it fully heals the whole
    /// party, wherever they are — once per floor. Worth deciding when to spend it.
    /// </summary>
    public class Fountain : Interactable
    {
        public readonly NetworkVariable<bool> Spent = new NetworkVariable<bool>();
        private Renderer water;
        private Light glow;

        public override string Prompt(PlayerProgress who) => Spent.Value ? null : "Drink from the fountain (heals the whole party)";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
            AmbientLoop.Attach(gameObject, "Audio/Ambience/water_loop", 12f, 0.3f, Vector3.up * 0.8f);
            Spent.OnValueChanged += (_, __) => Refresh();
            Refresh();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            Spent.Value = true;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                if (p.Downed.Value) p.ServerRevive(1f);
                else p.Health.ServerHeal(p.Health.Max.Value);
            }
            DrinkFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DrinkFxRpc()
        {
            Sfx.Play(Sound.Revive, transform.position + Vector3.up, 1f, 0.9f);
            UI.GameHud.ShowTitleCard("RENEWED", "The fountain's water heals the whole party");
        }

        private void Refresh()
        {
            if (water != null) ArtLibrary.SetGlow(water, Spent.Value ? new Color(0.1f, 0.15f, 0.2f) : new Color(0.35f, 0.75f, 1f) * 1.4f);
            if (glow != null) glow.intensity = Spent.Value ? 0.4f : 3f;
        }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Basin", MeshKit.Lathe("fountainBasin", new[]
            {
                new Vector2(0f, 0f), new Vector2(1.3f, 0f), new Vector2(1.35f, 0.6f), new Vector2(1.15f, 0.65f), new Vector2(1.1f, 0.4f), new Vector2(0f, 0.4f),
            }, 14), Mat.StoneWall, Vector3.zero);
            water = MeshKit.Part(transform, "Water", MeshKit.Cylinder(1.1f, 1.1f, 0.02f, 14), Mat.Glow, new Vector3(0f, 0.5f, 0f)).GetComponent<Renderer>();
            MeshKit.Part(transform, "Column", MeshKit.Cylinder(0.25f, 0.2f, 1.4f, 8), Mat.StoneWall, new Vector3(0f, 0.4f, 0f));
            MeshKit.Part(transform, "Bowl", MeshKit.Lathe("fountainBowl", new[]
            {
                new Vector2(0.1f, 0f), new Vector2(0.55f, 0.15f), new Vector2(0.6f, 0.3f), new Vector2(0f, 0.2f),
            }, 12), Mat.StoneWall, new Vector3(0f, 1.8f, 0f));
            glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 1.2f;
            glow.type = LightType.Point;
            glow.color = new Color(0.4f, 0.75f, 1f);
            glow.range = 7f;
        }
    }
}
