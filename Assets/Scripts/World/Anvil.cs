using System.Collections.Generic;
using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public class Anvil : NetworkBehaviour
    {
        public const int MaxLevel = 10;
        public const float UseRange = 3f;
        private static readonly int[] Costs = { 15, 25, 40, 60, 85, 110, 140, 175, 215, 260 };

        public const int AscendedLevel = 8, MasterworkLevel = 10;
        public const float AscendedDamage = 1.1f, MasterworkCrit = 0.08f;

        public const int MaxHones = 5;
        public const float HoneDamage = 0.03f;

        public static int HoneEmbers(int hones) => 150 + 50 * hones;
        public static int HoneGold(int hones) => 200 + 100 * hones;

        public static int GoldCost(int level) => level >= 5 && level < MaxLevel ? 60 + 40 * (level - 5) : 0;

        public static readonly List<Anvil> All = new List<Anvil>();

        public static int Cost(int level) => level >= 0 && level < MaxLevel ? Costs[level] : 0;

        public static Anvil Near(Vector3 at)
        {
            foreach (var a in All)
                if (a != null && Vector3.Distance(a.transform.position, at) <= UseRange) return a;

            if (DungeonDirector.InLobby && CampStations.AtSmithy(at))
                foreach (var a in All)
                    if (a != null && Vector3.Distance(a.transform.position, Lobby.AnvilSpot) < 2f) return a;
            return null;
        }

        public enum Work : byte { Anvil, Grindstone, Hearth }

        [Rpc(SendTo.ClientsAndHost)]
        public void WorkFxRpc(Work where, float k)
        {
            if (where == Work.Anvil || !DungeonDirector.InLobby) { HammerFx(3); return; }
            var fx = SmithyFx.Instance;
            if (where == Work.Grindstone)
            {
                var at = CampStations.GrindstoneSpot + new Vector3(0.12f, 0.5f, 0f);
                Sfx.Play(Sound.Unsheathe, at, 0.9f, 0.8f);
                Sfx.Play(Sound.Graze, at, 0.6f, 1.3f);
                BoneBurst.Spawn(at, 0.35f, false, Mat.Flame);
                if (fx != null) fx.Spin();
            }
            else
            {
                var at = CampStations.HearthSpot + Vector3.up * 1.5f;
                Sfx.Play(Sound.Fire, at, 1f, 0.75f);
                BoneBurst.Spawn(at, 0.4f + 0.3f * k, false, Mat.Flame);
                if (fx != null) fx.Flare(k);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        public void ForgeFxRpc(int level)
        {
            HammerFx(level);

            if (DungeonDirector.InLobby && SmithyFx.Instance != null) SmithyFx.Instance.Flare(0.5f);
        }

        private void HammerFx(int level)
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
