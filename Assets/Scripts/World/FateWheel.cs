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
        public enum Fate : byte { Fortune, Mending, Embers, Sigil, Relic, Blood, Ambush,
            Jackpot, Apothecary, Fury, Swiftness, Foresight, Champion, Taxman, Hex, Again }

        private static readonly (Fate fate, float weight, string name, Color color)[] Segments =
        {
            (Fate.Fortune, 16f, "FORTUNE", new Color(1f, 0.8f, 0.3f)),
            (Fate.Mending, 13f, "MENDING", new Color(0.5f, 1f, 0.6f)),
            (Fate.Embers, 12f, "EMBERS", new Color(1f, 0.5f, 0.2f)),
            (Fate.Sigil, 8f, "A SIGIL", new Color(0.7f, 0.5f, 1f)),
            (Fate.Relic, 5f, "A RELIC", new Color(1f, 0.95f, 0.75f)),
            (Fate.Blood, 7f, "BLOOD", new Color(0.8f, 0.1f, 0.12f)),
            (Fate.Ambush, 9f, "THE DEAD", new Color(0.35f, 0.35f, 0.4f)),
            (Fate.Jackpot, 2f, "THE JACKPOT", new Color(1f, 1f, 0.55f)),
            (Fate.Apothecary, 10f, "THE APOTHECARY", new Color(0.95f, 0.35f, 0.45f)),
            (Fate.Fury, 7f, "FURY", new Color(1f, 0.45f, 0.15f)),
            (Fate.Swiftness, 6f, "SWIFTNESS", new Color(0.4f, 0.95f, 0.85f)),
            (Fate.Foresight, 6f, "FORESIGHT", new Color(0.75f, 0.85f, 1f)),
            (Fate.Champion, 5f, "A CHAMPION", new Color(0.95f, 0.6f, 0.95f)),
            (Fate.Taxman, 5f, "THE TAXMAN", new Color(0.55f, 0.5f, 0.3f)),
            (Fate.Hex, 5f, "A HEX", new Color(0.55f, 0.25f, 0.7f)),
            (Fate.Again, 4f, "SPIN AGAIN", new Color(1f, 1f, 1f)),
        };

        public readonly NetworkVariable<bool> Spun = new NetworkVariable<bool>();
        private Transform wheel;
        private float angle, spinFrom, spinTo, spinStart = -10f;
        private const float SpinSeconds = 2.4f;
        public const float SpinSecondsPublic = SpinSeconds;

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

        /// <summary>Server: the last fate the wheel gave (tests).</summary>
        public Fate LastFate { get; private set; }

        private IEnumerator Resolve(int pick, PlayerProgress who)
        {
            LastFate = Segments[pick].fate;
            yield return new WaitForSeconds(SpinSeconds + 0.2f);
            var d = DungeonDirector.Instance;
            int depth = Mathf.RoundToInt(DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0));
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
                case Fate.Jackpot:
                    // Everything at once: a rain of gold, embers, a Sigil and a relic.
                    for (int i = 0; i < 10; i++)
                        d?.ServerDropLoot(front + Quaternion.Euler(0f, i * 36f, 0f) * Vector3.forward * 1.6f, LootKind.Gold, 15 + 10 * depth);
                    d?.ServerDropEmbers(front, 20 + 6 * depth);
                    d?.ServerDropLoot(front + Vector3.left, LootKind.Sigil, 1);
                    d?.ServerDropLoot(front + Vector3.right, LootKind.Relic, (int)Relics.Random());
                    break;
                case Fate.Apothecary:
                    // A health and a mana potion for everyone, and a frost and holy flask besides.
                    foreach (var p in PlayerLife.All)
                    {
                        if (p == null || !p.TryGetComponent<Flasks>(out var f)) continue;
                        f.ServerAdd(Flasks.Kind.Health, 1);
                        f.ServerAdd(Flasks.Kind.Mana, 1);
                        f.ServerAdd(Random.value < 0.5f ? Flasks.Kind.Frost : Flasks.Kind.Holy, 1);
                    }
                    break;
                case Fate.Fury:
                    // Every ultimate filled to the brim.
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<Ultimates>(out var u)) u.Charge.Value = Ultimates.ChargeMax;
                    break;
                case Fate.Swiftness:
                    // The shrine's Swiftness for the rest of the floor (unless already blessed).
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<PlayerProgress>(out var prog) && prog.ActiveBlessing.Value == 0) prog.ServerBless(Blessing.Swiftness);
                    break;
                case Fate.Foresight:
                    // The whole floor on everyone's map.
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<PlayerProgress>(out var prog)) prog.MapRevealed.Value = true;
                    break;
                case Fate.Champion:
                    // A named elite steps out of the dark: dangerous, and it carries a champion's hoard.
                    d?.ServerSpawnPack(front, 1, true, champion: true);
                    break;
                case Fate.Taxman:
                    // The wheel takes a tithe: a fifth of everyone's purse.
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<PlayerProgress>(out var prog)) prog.ServerAddGold(-Mathf.RoundToInt(prog.Gold.Value * 0.2f));
                    break;
                case Fate.Hex:
                    // Everyone cursed for a while: less damage and healing.
                    foreach (var p in PlayerLife.All)
                        if (p != null && p.TryGetComponent<PlayerDebuffs>(out var db) && !p.Downed.Value) db.ServerCurse(20f, 0);
                    break;
                case Fate.Again:
                    // Fate is undecided: the wheel can be spun once more.
                    yield return new WaitForSeconds(0.4f);
                    Spun.Value = false;
                    break;
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
            bool bad = s.fate == Fate.Blood || s.fate == Fate.Ambush || s.fate == Fate.Taxman || s.fate == Fate.Hex || s.fate == Fate.Champion;
            UI.GameHud.ShowTitleCard("THE WHEEL GRANTS " + s.name, Describe(s.fate));
            Sfx.Play(bad ? Sound.Summon : Sound.Relic, transform.position + Vector3.up * 2f, 1f, bad ? 0.7f : 1.1f);
            Vfx.Flash(transform.position + transform.forward + Vector3.up * 2f, s.color, 6f, 8f, 0.5f);
            Vfx.Embers(transform.position + transform.forward * 0.8f + Vector3.up * 2f, 20, s.color, 4f, 0.06f, 0.8f);
        }

        /// <summary>What a fate did, for the title card.</summary>
        public static string Describe(Fate f) => f switch
        {
            Fate.Fortune => "Gold spills from the wheel",
            Fate.Mending => "Everyone healed, the fallen raised, a shield besides",
            Fate.Embers => "A heap of embers",
            Fate.Sigil => "A Sigil, for an ability rank",
            Fate.Relic => "A relic",
            Fate.Blood => "Fate is cruel: it takes a quarter of everyone's health",
            Fate.Ambush => "Fate is cruel: the dead climb out around you",
            Fate.Jackpot => "Gold, embers, a Sigil and a relic: everything",
            Fate.Apothecary => "Potions for everyone",
            Fate.Fury => "Every ultimate is ready",
            Fate.Swiftness => "Swiftness for the floor: faster feet, shorter cooldowns",
            Fate.Foresight => "The whole floor is on your map",
            Fate.Champion => "A champion steps out to meet you. It carries a hoard",
            Fate.Taxman => "Fate is cruel: a fifth of everyone's gold",
            Fate.Hex => "Fate is cruel: everyone is weakened for 20 s",
            Fate.Again => "Undecided. Spin again",
            _ => "",
        };

        /// <summary>Tests: resolve a given fate at once.</summary>
        public void ServerDebugFate(Fate f, PlayerProgress who)
        {
            for (int i = 0; i < Segments.Length; i++)
                if (Segments[i].fate == f) { Spun.Value = true; StartCoroutine(Resolve(i, who)); return; }
        }

        public static int FateCount => Segments.Length;

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
                var tile = MeshKit.Part(spoke, "Tile", MeshKit.Box(new Vector3(0.36f, 1.05f, 0.1f)), Mat.Cloth, new Vector3(0f, 0.68f, 0f));
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
