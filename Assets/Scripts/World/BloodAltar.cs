using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A blood altar: give up a third of your current health for a relic. A real choice at
    /// a bad moment, which is the point — better taken after a fight than before one.
    /// </summary>
    public class BloodAltar : Interactable
    {
        public readonly NetworkVariable<bool> Spent = new NetworkVariable<bool>();
        private Renderer pool;

        public override string Prompt(PlayerProgress who)
        {
            if (Spent.Value) return null;
            return "Offer blood for a relic (costs a third of your health)";
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
            Spent.OnValueChanged += (_, __) => UpdateGlow();
            UpdateGlow();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            var hp = who.GetComponent<Health>();
            if (hp.Current.Value < hp.Max.Value * 0.35f) return; // would kill you: refuse

            // Pick a relic this player does not have yet.
            for (int tries = 0; tries < 30; tries++)
            {
                var r = Relics.Random();
                if (who.HasRelic(r)) continue;
                // Taken straight from the pool, not as a hit: blocks and wards do not apply.
                hp.ServerSetFraction(hp.Current.Value * (2f / 3f) / hp.Max.Value);
                who.ServerAddRelic(r);
                Spent.Value = true;
                return;
            }
        }

        private void UpdateGlow()
        {
            if (pool != null) ArtLibrary.SetGlow(pool.GetComponent<Renderer>(), Spent.Value ? new Color(0.2f, 0f, 0f) : new Color(1f, 0.05f, 0.05f) * 2f);
        }

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Base", MeshKit.Box(new Vector3(1.4f, 0.4f, 1.0f)), Mat.StoneWall, new Vector3(0f, 0.2f, 0f));
            MeshKit.Part(transform, "Top", MeshKit.Box(new Vector3(1.2f, 0.5f, 0.8f)), Mat.StoneWall, new Vector3(0f, 0.65f, 0f));
            pool = MeshKit.Part(transform, "Blood", MeshKit.Cylinder(0.32f, 0.32f, 0.03f, 12), Mat.Glow, new Vector3(0f, 0.91f, 0f)).GetComponent<Renderer>();
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -0.7f : 0.7f;
                MeshKit.Part(transform, "Horn" + i, MeshKit.Lathe("altarHorn", new[] { new Vector2(0.08f, 0f), new Vector2(0f, 0.7f) }, 5), Mat.Bone,
                    new Vector3(x, 0.9f, 0f), new Vector3(0f, 0f, i == 0 ? 20f : -20f));
            }
            MeshKit.Part(transform, "Skull", MeshKit.Ball(0.14f, 8, 6), Mat.Bone, new Vector3(0f, 1.05f, -0.3f));
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = Vector3.up * 1.4f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.1f);
            light.range = 5f;
            light.intensity = 2.5f;
        }
    }
}
