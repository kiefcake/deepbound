using Deepbound.Art;
using Deepbound.Classes;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The anvil beside the merchant in every start room. Embers go in, your class weapon
    /// comes out a level stronger. Weapons are never found — each class always carries its
    /// own — so this is where the run's steady power comes from (relics are the spikes).
    /// </summary>
    public class Anvil : Interactable
    {
        public const int MaxLevel = 5;
        private static readonly int[] Costs = { 15, 25, 40, 60, 85 };

        /// <summary>Embers to go from <paramref name="level"/> to the next. 0 at max.</summary>
        public static int Cost(int level) => level >= 0 && level < MaxLevel ? Costs[level] : 0;

        public override float Range => 2.8f;

        public override string Prompt(PlayerProgress who)
        {
            int level = who.WeaponLevel.Value;
            string weapon = ClassDefinition.WeaponName(who.ClassId.Value);
            if (level >= MaxLevel) return null;
            int cost = Cost(level);
            return who.Embers.Value >= cost
                ? "Temper your " + weapon + " to +" + (level + 1) + "  (" + cost + " embers)"
                : weapon + " +" + (level + 1) + " needs " + cost + " embers  (you have " + who.Embers.Value + ")";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (who.ServerUpgradeWeapon()) ForgeFxRpc(who.WeaponLevel.Value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ForgeFxRpc(int level)
        {
            var top = transform.position + Vector3.up * 0.95f;
            Sfx.Play(Sound.Bash, top, 1f, 0.7f + level * 0.08f);
            Sfx.Play(Sound.Shatter, top, 0.5f, 1.6f);
            BoneBurst.Spawn(top, 0.5f, false, Mat.Flame);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        private void BuildVisual()
        {
            // A stone block, the iron anvil on top, and a brazier of coals beside it.
            MeshKit.Part(transform, "Block", MeshKit.Box(new Vector3(0.7f, 0.55f, 0.55f)), Mat.StoneWall, new Vector3(0f, 0.275f, 0f));
            MeshKit.Part(transform, "Waist", MeshKit.Box(new Vector3(0.35f, 0.18f, 0.3f)), Mat.Steel, new Vector3(0f, 0.64f, 0f));
            MeshKit.Part(transform, "Face", MeshKit.Box(new Vector3(0.75f, 0.16f, 0.36f)), Mat.Steel, new Vector3(0.05f, 0.81f, 0f));
            MeshKit.Part(transform, "Horn", MeshKit.Cylinder(0.12f, 0.02f, 0.35f, 8), Mat.Steel, new Vector3(-0.47f, 0.82f, 0f), new Vector3(0f, 0f, 90f));
            MeshKit.Part(transform, "Hammer", MeshKit.Box(new Vector3(0.05f, 0.05f, 0.4f)), Mat.Wood, new Vector3(0.2f, 0.92f, 0.05f), new Vector3(0f, 25f, 0f));
            MeshKit.Part(transform, "HammerHead", MeshKit.Box(new Vector3(0.14f, 0.09f, 0.09f)), Mat.Steel, new Vector3(0.28f, 0.92f, 0.22f), new Vector3(0f, 25f, 0f));

            var brazier = new Vector3(0f, 0f, 0.85f);
            MeshKit.Part(transform, "Brazier", MeshKit.Lathe("brazier", new[]
            {
                new Vector2(0.05f, 0f), new Vector2(0.12f, 0.02f), new Vector2(0.06f, 0.45f), new Vector2(0.32f, 0.6f), new Vector2(0.34f, 0.72f), new Vector2(0f, 0.66f),
            }, 10), Mat.Steel, brazier);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f;
                MeshKit.Part(transform, "Coal" + i, MeshKit.Ball(0.08f, 6, 4), Mat.Flame, brazier + new Vector3(Mathf.Cos(a) * 0.16f, 0.72f, Mathf.Sin(a) * 0.16f));
            }
            var light = new GameObject("Coals").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = brazier + Vector3.up * 1f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.15f);
            light.range = 5f;
            light.intensity = 2.2f;
        }
    }
}
