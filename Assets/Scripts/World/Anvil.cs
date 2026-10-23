using System.Collections.Generic;
using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The anvil beside the merchant in every start room (G opens the forge). Embers go in,
    /// your class weapon comes out a level stronger; at +2 you choose its path. Weapons are
    /// never found — each class always carries its own — so this is where the run's steady
    /// power comes from (relics are the spikes).
    /// </summary>
    public class Anvil : NetworkBehaviour
    {
        /// <summary>Ten levels for ten floors. From +6 Hobb wants gold as well as embers (good steel is dear).</summary>
        public const int MaxLevel = 10;
        public const float UseRange = 3f;
        private static readonly int[] Costs = { 15, 25, 40, 60, 85, 110, 140, 175, 215, 260 };
        /// <summary>+8: Ascended, the weapon's damage up a tenth. +10: Masterwork, a better chance to crit.</summary>
        public const int AscendedLevel = 8, MasterworkLevel = 10;
        public const float AscendedDamage = 1.1f, MasterworkCrit = 0.08f;

        /// <summary>
        /// Past +10 (2026-10-23): up to five hones, +3% damage each, for embers and gold. A full run
        /// reached +10 by floor 9 and then sat on 500-700 embers and 2000 gold with nothing to buy.
        /// </summary>
        public const int MaxHones = 5;
        public const float HoneDamage = 0.03f;
        /// <summary>1,250 embers and 2,000 gold for all five (the first run bought four in one visit at 90 + 30n).</summary>
        public static int HoneEmbers(int hones) => 150 + 50 * hones;
        public static int HoneGold(int hones) => 200 + 100 * hones;

        /// <summary>Gold to go from <paramref name="level"/> to the next (none below +6).</summary>
        public static int GoldCost(int level) => level >= 5 && level < MaxLevel ? 60 + 40 * (level - 5) : 0;

        public static readonly List<Anvil> All = new List<Anvil>();

        /// <summary>Embers to go from <paramref name="level"/> to the next. 0 at max.</summary>
        public static int Cost(int level) => level >= 0 && level < MaxLevel ? Costs[level] : 0;

        /// <summary>The anvil within reach of <paramref name="at"/>, or null.</summary>
        public static Anvil Near(Vector3 at)
        {
            foreach (var a in All)
                if (a != null && Vector3.Distance(a.transform.position, at) <= UseRange) return a;
            return null;
        }

        /// <summary>Everyone sees and hears the hammer fall.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        public void ForgeFxRpc(int level)
        {
            var top = transform.position + Vector3.up * 0.95f;
            Sfx.Play(Sound.Bash, top, 1f, 0.7f + level * 0.08f);
            Sfx.Play(Sound.Shatter, top, 0.5f, 1.6f);
            BoneBurst.Spawn(top, 0.5f, false, Mat.Flame);
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        public override void OnDestroy()
        {
            All.Remove(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
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

            var brazier = new Vector3(1.05f, 0f, -0.15f);
            MeshKit.Part(transform, "Brazier", MeshKit.Lathe("brazier", new[]
            {
                new Vector2(0.05f, 0f), new Vector2(0.12f, 0.02f), new Vector2(0.06f, 0.45f), new Vector2(0.32f, 0.6f), new Vector2(0.34f, 0.72f), new Vector2(0f, 0.66f),
            }, 10), Mat.Steel, brazier);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 1.05f;
                MeshKit.Part(transform, "Coal" + i, MeshKit.Ball(0.06f, 6, 4), Mat.Flame, brazier + new Vector3(Mathf.Cos(a) * 0.15f, 0.68f, Mathf.Sin(a) * 0.15f));
            }
            var light = new GameObject("Coals").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.transform.localPosition = brazier + Vector3.up * 1f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.15f);
            light.range = 5f;
            light.intensity = 1.4f;
        }
    }
}
