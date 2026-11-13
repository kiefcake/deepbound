using Deepbound.Art;
using Deepbound.Core;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Draught room's flask (2026-11-12, the user: "a room that spawns ... a potion that for the run permanently
    /// increases a random stat ... drunk off the ground, so that there can be a decision in the party who should drink
    /// it, or a solo player will just be rewarded"; asked, "+3": three of Brann's drills in one gulp). It stands on the
    /// floor in a ring of candles, its colour and its name telling which stat it holds (rolled when the floor is made,
    /// the same for everyone), so the party can argue over who needs it. One drink, then the flask is empty.
    /// </summary>
    public class StatDraught : Interactable
    {
        public readonly NetworkVariable<byte> Stat = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> Drunk = new NetworkVariable<bool>();

        public override float Range => 2f;

        /// <summary>Three drills' worth of each stat (Shop.Drills): the gulp a party should argue over.</summary>
        public static float Amount(StatType s) => s switch
        {
            StatType.HP => 75f, StatType.MP => 36f, StatType.ATT => 6f, StatType.DEF => 6f,
            StatType.SPD => 6f, StatType.DEX => 9f, StatType.VIT => 15f, _ => 9f,
        };

        public static string DraughtName(StatType s) => s switch
        {
            StatType.HP => "Draught of the Ox", StatType.MP => "Draught of Deep Wells", StatType.ATT => "Draught of Might",
            StatType.DEF => "Draught of Iron Skin", StatType.SPD => "Draught of the Hare", StatType.DEX => "Draught of Quick Hands",
            StatType.VIT => "Draught of the Unbowed", _ => "Draught of Clear Sight",
        };

        public static string Gain(StatType s) => "+" + Mathf.RoundToInt(Amount(s)) + " " + (s == StatType.HP ? "max HP" : s == StatType.MP ? "max MP" : s.ToString());

        public static Color ColorOf(StatType s) => s switch
        {
            StatType.HP => new Color(0.95f, 0.25f, 0.2f), StatType.MP => new Color(0.3f, 0.45f, 1f), StatType.ATT => new Color(1f, 0.55f, 0.15f),
            StatType.DEF => new Color(0.75f, 0.78f, 0.82f), StatType.SPD => new Color(0.4f, 1f, 0.55f), StatType.DEX => new Color(1f, 0.9f, 0.3f),
            StatType.VIT => new Color(0.95f, 0.4f, 0.65f), _ => new Color(0.6f, 0.4f, 1f),
        };

        public StatType Kind => (StatType)Stat.Value;

        public override string Prompt(PlayerProgress who) => Drunk.Value ? null
            : "Drink the " + DraughtName(Kind) + "  (" + Gain(Kind) + " for the run; one drink)";

        protected override void ServerUse(PlayerProgress who)
        {
            if (Drunk.Value || who == null) return;
            Drunk.Value = true;
            who.ServerAddBonus(Kind, Amount(Kind));
            DrankRpc(who.NetworkObjectId);
        }

        /// <summary>Server, before Spawn: which stat it holds.</summary>
        public void Prepare(StatType s) => Stat.Value = (byte)s;

        private Renderer flaskR;
        private Light glow;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            var c = ColorOf(Kind);
            // A ring of stubby candles round a round-bellied flask on the bare floor.
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.75f;
                MeshKit.Part(transform, "Candle" + i, MeshKit.Cylinder(0.035f, 0.035f, 0.12f + (i % 3) * 0.05f, 6), Mat.Bone, at);
                var f = MeshKit.Part(transform, "Wick" + i, MeshKit.Ball(0.025f, 5, 4), Mat.Glow, at + Vector3.up * (0.15f + (i % 3) * 0.05f));
                ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(1f, 0.7f, 0.35f) * 1.4f);
            }
            // The flask is the draught: its glass glows the stat's colour; drunk, it goes dark.
            var flask = MeshKit.Part(transform, "Flask", MeshKit.Lathe("draughtFlask", new[]
            {
                new Vector2(0.05f, 0f), new Vector2(0.15f, 0.04f), new Vector2(0.18f, 0.16f), new Vector2(0.14f, 0.3f),
                new Vector2(0.05f, 0.36f), new Vector2(0.045f, 0.5f), new Vector2(0.06f, 0.52f), new Vector2(0f, 0.53f),
            }, 12), Mat.Glow, Vector3.zero);
            flaskR = flask.GetComponent<Renderer>();
            flaskR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            MeshKit.Part(transform, "Cork", MeshKit.Cylinder(0.05f, 0.045f, 0.06f, 8), Mat.Wood, Vector3.up * 0.51f);
            glow = new GameObject("DraughtLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 0.6f;
            glow.type = LightType.Point;
            glow.color = c;
            glow.range = 4.5f;
            glow.intensity = 1.6f;
            Drunk.OnValueChanged += (_, now) => ShowDrunk(now);
            ShowDrunk(Drunk.Value);
        }

        private void ShowDrunk(bool drunk)
        {
            if (flaskR != null) ArtLibrary.SetGlow(flaskR, drunk ? new Color(0.12f, 0.12f, 0.14f) : ColorOf(Kind) * 1.5f);
            if (glow != null) glow.enabled = !drunk;
        }

        private void Update()
        {
            if (Drunk.Value) return;
            if (glow != null) glow.intensity = 1.4f + Mathf.Sin(Time.time * 2.4f) * 0.3f;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DrankRpc(ulong drinker)
        {
            var c = ColorOf(Kind);
            var at = transform.position + Vector3.up * 0.4f;
            Sfx.Play(Sound.Drink, at, 1f, 0.8f);
            Sfx.Play(Sound.Relic, at, 0.7f, 1.1f);
            Vfx.Flash(at, c, 3f, 6f, 0.5f);
            Vfx.Embers(at, 18, c, 3f, 0.08f, 1.2f);
            string who = NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(drinker, out var o) ? o.name : "Someone";
            UI.GameHud.Feed(who + " drank the " + DraughtName(Kind) + ": " + Gain(Kind) + " for the run", c);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.6f, Gain(Kind), c);
        }
    }
}
