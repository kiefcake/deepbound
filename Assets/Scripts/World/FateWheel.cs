using System.Collections;
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
    /// The Wheel of Fate: a great carved stone wheel set into a wall, found now and then in
    /// a fight room. One spin per floor for the whole party. It mostly gives — gold, healing,
    /// embers, a sigil, rarely a relic — but sometimes it takes, and once in a while it
    /// demands blood and the dead climb out around you.
    /// </summary>
    public class FateWheel : Interactable
    {
        public enum Fate : byte { Fortune, Mending, Embers, Sigil, Relic, Blood, Ambush }

        private static readonly (Fate fate, float weight, string name, Color color)[] Segments =
        {
            (Fate.Fortune, 24f, "FORTUNE", new Color(1f, 0.8f, 0.3f)),
            (Fate.Mending, 20f, "MENDING", new Color(0.5f, 1f, 0.6f)),
            (Fate.Embers, 16f, "EMBERS", new Color(1f, 0.5f, 0.2f)),
            (Fate.Sigil, 12f, "A SIGIL", new Color(0.7f, 0.5f, 1f)),
            (Fate.Relic, 7f, "A RELIC", new Color(1f, 0.95f, 0.75f)),
            (Fate.Blood, 9f, "BLOOD", new Color(0.8f, 0.1f, 0.12f)),
            (Fate.Ambush, 12f, "THE DEAD", new Color(0.35f, 0.35f, 0.4f)),
        };

        public readonly NetworkVariable<bool> Spun = new NetworkVariable<bool>();
        private Transform wheel;
        private float angle, spinFrom, spinTo, spinStart = -10f;
        private const float SpinSeconds = 2.4f;

        public override float Range => 3f;

        public override string Prompt(PlayerProgress who) => Spun.Value ? null : "Spin the Wheel of Fate (one spin for the party)";

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Spun.Value) return;
            Spun.Value = true;
            float total = 0f;
            foreach (var s in Segments) total += s.weight;
            float roll = Random.value * total;
            int pick = 0;
            for (; pick < Segments.Length - 1; pick++)
            {
                roll -= Segments[pick].weight;
                if (roll < 0f) break;
            }
            SpinRpc((byte)pick);
            StartCoroutine(Resolve(pick, who));
        }

        private IEnumerator Resolve(int pick, PlayerProgress who)
        {
            yield return new WaitForSeconds(SpinSeconds + 0.2f);
            var d = DungeonDirector.Instance;
            int depth = DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0;
            var front = transform.position + transform.forward * 2f + Vector3.up * 0.5f;
            switch (Segments[pick].fate)
            {
                case Fate.Fortune:
                    for (int i = 0; i < 6; i++)
                        d?.ServerDropLoot(front + Quaternion.Euler(0f, i * 60f, 0f) * Vector3.forward * 1.2f, LootKind.Gold, 12 + 8 * depth);
                    break;
                case Fate.Mending:
                    foreach (var p in PlayerLife.All)
                    {
                        if (p == null) continue;
                        if (p.Downed.Value) p.ServerRevive(0.5f);
                        p.Health.ServerHeal(p.Health.Max.Value);
                        p.Health.ServerShield(40f, 8f);
                    }
                    break;
                case Fate.Embers: d?.ServerDropEmbers(front, 14 + 4 * depth); break;
                case Fate.Sigil: d?.ServerDropLoot(front, LootKind.Sigil, 1); break;
                case Fate.Relic: d?.ServerDropLoot(front, LootKind.Relic, (int)Relics.Random()); break;
                case Fate.Blood:
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.Health.IsAlive && !p.Downed.Value)
                            p.Health.ServerDamage(Mathf.Min(p.Health.Current.Value - 1f, p.Health.Max.Value * 0.25f), transform.position);
                    break;
                case Fate.Ambush: d?.ServerSpawnPack(front, 3, true); break;
            }
            FateFxRpc((byte)pick);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpinRpc(byte pick)
        {
            // Land the chosen segment at the top, after a few whole turns.
            float seg = 360f / Segments.Length;
            spinFrom = angle;
            spinTo = angle - (angle % 360f) + 360f * 4f + pick * seg + seg / 2f;
            spinStart = Time.time;
            Sfx.Play(Sound.Rattle, transform.position + Vector3.up * 2f, 1f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FateFxRpc(byte pick)
        {
            var s = Segments[pick];
            bool bad = s.fate == Fate.Blood || s.fate == Fate.Ambush;
            UI.GameHud.ShowTitleCard("THE WHEEL GRANTS " + s.name, bad ? "Fate is cruel" : "Fate smiles on you");
            Sfx.Play(bad ? Sound.Summon : Sound.Relic, transform.position + Vector3.up * 2f, 1f, bad ? 0.7f : 1.1f);
            Vfx.Flash(transform.position + transform.forward + Vector3.up * 2f, s.color, 6f, 8f, 0.5f);
            Vfx.Embers(transform.position + transform.forward * 0.8f + Vector3.up * 2f, 20, s.color, 4f, 0.06f, 0.8f);
        }

        private void Update()
        {
            if (wheel == null) return;
            float t = (Time.time - spinStart) / SpinSeconds;
            if (t >= 0f && t <= 1f) angle = Mathf.Lerp(spinFrom, spinTo, 1f - Mathf.Pow(1f - t, 3f));
            else if (t > 1f) angle = spinTo;
            wheel.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void BuildVisual()
        {
            // A stone frame and plinth, the wheel itself with coloured segments, and a pointer at the top.
            MeshKit.Part(transform, "Plinth", MeshKit.Box(new Vector3(3f, 0.5f, 1f)), Mat.StoneWall, new Vector3(0f, 0.25f, 0f));
            foreach (float x in new[] { -1.35f, 1.35f })
                MeshKit.Part(transform, "Post", MeshKit.Box(new Vector3(0.35f, 3.6f, 0.5f)), Mat.StoneWall, new Vector3(x, 2.1f, -0.1f));
            MeshKit.Part(transform, "Lintel", MeshKit.Box(new Vector3(3.1f, 0.4f, 0.55f)), Mat.StoneWall, new Vector3(0f, 4.05f, -0.1f));
            wheel = MeshKit.Pivot(transform, "Wheel", new Vector3(0f, 2.2f, 0.1f));
            float seg = 360f / Segments.Length;
            for (int i = 0; i < Segments.Length; i++)
            {
                var spoke = MeshKit.Pivot(wheel, "Seg" + i, Vector3.zero);
                spoke.localRotation = Quaternion.Euler(0f, 0f, -(i * seg + seg / 2f));
                var tile = MeshKit.Part(spoke, "Tile", MeshKit.Box(new Vector3(0.62f, 1.05f, 0.1f)), Mat.Cloth, new Vector3(0f, 0.68f, 0f));
                ArtLibrary.Tint(tile.GetComponent<Renderer>(), Segments[i].color * 0.75f);
                var gem = MeshKit.Part(spoke, "Gem", MeshKit.Ball(0.08f, 6, 4), Mat.Glow, new Vector3(0f, 1.05f, 0.08f));
                ArtLibrary.SetGlow(gem.GetComponent<Renderer>(), Segments[i].color * 1.2f);
                var divider = MeshKit.Pivot(wheel, "Div" + i, Vector3.zero);
                divider.localRotation = Quaternion.Euler(0f, 0f, -i * seg);
                MeshKit.Part(divider, "Bar", MeshKit.Box(new Vector3(0.05f, 1.3f, 0.14f)), Mat.Gold, new Vector3(0f, 0.65f, 0f));
            }
            MeshKit.Part(wheel, "Rim", MeshKit.Lathe("fateRim", new[] { new Vector2(1.28f, -0.08f), new Vector2(1.36f, 0f), new Vector2(1.28f, 0.08f) }, 28), Mat.Gold, Vector3.zero, new Vector3(90f, 0f, 0f));
            MeshKit.Part(wheel, "Hub", MeshKit.Cylinder(0.2f, 0.2f, 0.25f, 10), Mat.Gold, new Vector3(0f, 0f, 0.02f), new Vector3(90f, 0f, 0f));
            MeshKit.Part(transform, "Pointer", MeshKit.Lathe("fatePointer", new[] { new Vector2(0.12f, 0f), new Vector2(0f, 0.35f) }, 4), Mat.Gold, new Vector3(0f, 3.75f, 0.25f), new Vector3(180f, 0f, 0f));
            var l = new GameObject("WheelLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0f, 2.4f, 1.4f);
            l.type = LightType.Point;
            l.color = new Color(1f, 0.8f, 0.5f);
            l.range = 6f;
            l.intensity = 1.6f;
            l.shadows = LightShadows.None;
        }
    }
}
