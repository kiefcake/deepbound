using System.Collections;
using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Bone Reels, in the gambling tent: pull the lever for a flat stake; three reels of
    /// skulls, bones, candles, coins and crowns spin and stop one by one. Three of a kind pays
    /// big (crowns most), any pair pays double (crowns triple), skulls pay nothing. The server
    /// rolls; every peer watches the reels land on the same symbols. Returns about 86%.
    /// </summary>
    public class SlotMachine : Interactable
    {
        public const int Stake = 10;
        public enum Symbol : byte { Skull, Bone, Candle, Coin, Crown }
        private static readonly float[] Weights = { 30f, 28f, 20f, 14f, 8f };
        private static readonly string[] Names = { "SKULL", "BONE", "CANDLE", "COIN", "CROWN" };

        private float busyUntil;
        private readonly Transform[] reels = new Transform[3];
        private Transform lever;

        public override float Range => 2.2f;

        public override string Prompt(PlayerProgress who) =>
            Time.time < busyUntil ? null : who.Gold.Value < Stake ? "Bone Reels: you need " + Stake + " gold" : "Pull the Bone Reels  -  " + Stake + " gold";

        private static Symbol Roll()
        {
            float total = 0f;
            foreach (var w in Weights) total += w;
            float r = Random.value * total;
            for (int i = 0; i < Weights.Length; i++) { if (r < Weights[i]) return (Symbol)i; r -= Weights[i]; }
            return Symbol.Skull;
        }

        /// <summary>The payout (gold returned, stake included) for three symbols.</summary>
        public static int Payout(Symbol a, Symbol b, Symbol c)
        {
            if (a == b && b == c)
            {
                switch (a)
                {
                    case Symbol.Crown: return Stake * 25;
                    case Symbol.Coin: return Stake * 10;
                    case Symbol.Candle: return Stake * 6;
                    case Symbol.Bone: return Stake * 4;
                    default: return 0;
                }
            }
            Symbol pair = a == b || a == c ? a : b == c ? b : Symbol.Skull;
            bool hasPair = a == b || a == c || b == c;
            if (!hasPair || pair == Symbol.Skull) return 0;
            return pair == Symbol.Crown ? Stake * 3 : Stake * 2;
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Time.time < busyUntil || who.Gold.Value < Stake) return;
            busyUntil = Time.time + 2.2f;
            var a = Roll(); var b = Roll(); var c = Roll();
            int win = Payout(a, b, c);
            who.ServerAddGold(win - Stake);
            SpinRpc(who.name, (byte)a, (byte)b, (byte)c, win);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpinRpc(string who, byte a, byte b, byte c, int win)
        {
            busyUntil = Time.time + 2.2f;
            StartCoroutine(Spin(who, new[] { a, b, c }, win));
        }

        private IEnumerator Spin(string who, byte[] stops, int win)
        {
            Sfx.Play(Sound.Bash, transform.position + Vector3.up * 1.2f, 0.5f, 1.4f);
            Sfx.Play(Sound.SlotSpin, transform.position + Vector3.up * 1.2f, 0.8f, 1.2f);
            float start = Time.time;
            var spin = new float[3];
            bool[] stopped = new bool[3];
            while (Time.time - start < 1.9f)
            {
                float t = Time.time - start;
                if (lever != null) lever.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 70f, Mathf.Sin(Mathf.Clamp01(t / 0.4f) * Mathf.PI)), 0f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    if (reels[i] == null) continue;
                    float stopAt = 0.8f + i * 0.45f;
                    if (t < stopAt) spin[i] += Time.deltaTime * 1400f;
                    else if (!stopped[i])
                    {
                        stopped[i] = true;
                        // Land with the rolled symbol facing out.
                        spin[i] = -stops[i] * 72f;
                        Sfx.Play(Sound.Block, reels[i].position, 0.35f, 1.8f);
                    }
                    reels[i].localRotation = Quaternion.Euler(spin[i], 0f, 0f);
                }
                yield return null;
            }
            string line = Names[stops[0]] + " | " + Names[stops[1]] + " | " + Names[stops[2]];
            var color = win > Stake ? new Color(1f, 0.85f, 0.35f) : win == Stake ? Color.white : new Color(1f, 0.45f, 0.4f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.1f, win > 0 ? "+" + (win - Stake) : "-" + Stake, color);
            UI.GameHud.Feed(who + " pulls the Bone Reels: " + line + (win > 0 ? "  -  wins " + win + " gold!" : "  -  the dead keep it."), color);
            if (win >= Stake * 6) { Sfx.Play(Sound.Relic, transform.position + Vector3.up, 1f); Vfx.Embers(transform.position + Vector3.up * 1.6f, 24, new Color(1f, 0.8f, 0.3f), 4f, 0.07f, 1f); }
            else if (win > 0) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BuildVisual();
        }

        private void BuildVisual()
        {
            // The cabinet: dark wood, gilt trim, a window over three reels, a lever on the right.
            var cab = MeshKit.Part(transform, "Cabinet", MeshKit.Box(new Vector3(1f, 1.7f, 0.7f)), Mat.Wood, Vector3.up * 0.85f);
            ArtLibrary.Tint(cab.GetComponent<Renderer>(), new Color(0.45f, 0.28f, 0.2f));
            MeshKit.Part(transform, "TrimTop", MeshKit.Box(new Vector3(1.06f, 0.08f, 0.76f)), Mat.Gold, Vector3.up * 1.72f);
            MeshKit.Part(transform, "TrimBase", MeshKit.Box(new Vector3(1.06f, 0.1f, 0.76f)), Mat.Gold, Vector3.up * 0.05f);
            var window = MeshKit.Part(transform, "Window", MeshKit.Box(new Vector3(0.84f, 0.36f, 0.04f)), Mat.Dark, new Vector3(0f, 1.2f, 0.34f));
            for (int i = 0; i < 3; i++)
            {
                var reel = MeshKit.Pivot(transform, "Reel" + i, new Vector3((i - 1) * 0.26f, 1.2f, 0.2f));
                MeshKit.Part(reel, "Drum", MeshKit.Cylinder(0.17f, 0.17f, 0.22f, 15), Mat.Bone, new Vector3(-0.11f, 0f, 0f), new Vector3(0f, 0f, 90f));
                // Five symbols round the drum, 72 degrees apart; symbol 0 faces out at rest.
                for (int s = 0; s < 5; s++)
                {
                    var face = MeshKit.Pivot(reel, "Face" + s, Vector3.zero, new Vector3(s * 72f, 0f, 0f));
                    BuildSymbol(face, (Symbol)s, new Vector3(0f, 0f, 0.18f));
                }
                reels[i] = reel;
            }
            var arm = MeshKit.Pivot(transform, "LeverPivot", new Vector3(0.56f, 1.05f, 0f));
            MeshKit.Part(arm, "Arm", MeshKit.Cylinder(0.025f, 0.025f, 0.5f, 6), Mat.Steel, Vector3.zero);
            MeshKit.Part(arm, "Knob", MeshKit.Ball(0.06f, 8, 6), Mat.Bone, Vector3.up * 0.52f);
            lever = arm;
            // A skull on top, candles either side.
            MeshKit.Part(transform, "Skull", MeshKit.Ball(0.12f, 8, 6), Mat.Bone, new Vector3(0f, 1.86f, 0.1f));
            foreach (float x in new[] { -0.38f, 0.38f })
            {
                MeshKit.Part(transform, "Candle", MeshKit.Cylinder(0.03f, 0.03f, 0.16f, 6), Mat.Bone, new Vector3(x, 1.76f, 0.15f));
                var flame = MeshKit.Part(transform, "Flame", MeshKit.Ball(0.025f, 6, 4), Mat.Glow, new Vector3(x, 1.95f, 0.15f));
                ArtLibrary.SetGlow(flame.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.25f) * 2f);
            }
            var l = new GameObject("ReelLight").AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.transform.localPosition = new Vector3(0f, 1.3f, 0.7f);
            l.color = new Color(1f, 0.7f, 0.4f);
            l.range = 2.5f;
            l.intensity = 1.2f;
            l.shadows = LightShadows.None;
        }

        private static void BuildSymbol(Transform at, Symbol s, Vector3 p)
        {
            switch (s)
            {
                case Symbol.Skull:
                    MeshKit.Part(at, "Skull", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, p);
                    MeshKit.Part(at, "EyeL", MeshKit.Ball(0.013f, 4, 3), Mat.Dark, p + new Vector3(-0.018f, 0.008f, 0.042f));
                    MeshKit.Part(at, "EyeR", MeshKit.Ball(0.013f, 4, 3), Mat.Dark, p + new Vector3(0.018f, 0.008f, 0.042f));
                    break;
                case Symbol.Bone:
                    MeshKit.Part(at, "Shaft", MeshKit.Cylinder(0.012f, 0.012f, 0.1f, 5), Mat.Bone, p + new Vector3(-0.05f, 0f, 0f), new Vector3(0f, 0f, 90f));
                    MeshKit.Part(at, "EndL", MeshKit.Ball(0.02f, 5, 4), Mat.Bone, p + new Vector3(-0.05f, 0f, 0f));
                    MeshKit.Part(at, "EndR", MeshKit.Ball(0.02f, 5, 4), Mat.Bone, p + new Vector3(0.05f, 0f, 0f));
                    break;
                case Symbol.Candle:
                    MeshKit.Part(at, "Wax", MeshKit.Cylinder(0.015f, 0.015f, 0.07f, 6), Mat.Bone, p + new Vector3(0f, -0.04f, 0f));
                    var f = MeshKit.Part(at, "Flame", MeshKit.Ball(0.015f, 5, 4), Mat.Glow, p + new Vector3(0f, 0.04f, 0f));
                    ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(1f, 0.6f, 0.2f) * 2f);
                    break;
                case Symbol.Coin:
                    MeshKit.Part(at, "Coin", MeshKit.Cylinder(0.045f, 0.045f, 0.012f, 12), Mat.Gold, p, new Vector3(90f, 0f, 0f));
                    break;
                default:
                    MeshKit.Part(at, "Band", MeshKit.Cylinder(0.04f, 0.04f, 0.025f, 10), Mat.Gold, p + new Vector3(0f, -0.02f, 0f), new Vector3(90f, 0f, 0f));
                    for (int i = -1; i <= 1; i++)
                        MeshKit.Part(at, "Point", MeshKit.Lathe("crownPoint", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.035f) }, 4), Mat.Gold, p + new Vector3(i * 0.025f, 0.0f, 0.012f));
                    break;
            }
        }
    }
}
