using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The local player's view of Deadman's Hand, casino style (2026-11-07, the user: "make the blackjack like gta5
    /// blackjack"): you take a stool, the camera settles over the felt, and real cards slide out of the shoe to your betting
    /// spot and the dealer's line; the hole card turns over at the reveal; your bet sits on the betting circle as chips.
    /// Purely local presentation of what the server sends (<see cref="BlackjackTable"/> keeps every rule and RPC).
    /// Added at runtime to the table on every peer; only the seated local player's copy shows anything.
    /// </summary>
    public class BlackjackView : MonoBehaviour
    {
        public const float FeltY = 0.92f, StoolRadius = 1.58f, BetRadius = 0.9f, CardRadius = 0.64f;
        // Larger than life so they read from the stool (2026-11-13: 0.12 x 0.168 was too small to read a rank).
        public const float CardW = 0.155f, CardH = 0.217f, HandFan = 0.105f, HouseStep = 0.175f, HouseZ = -0.17f;
        public static readonly Vector3 ShoeLocal = new Vector3(0.62f, 1.0f, 0.12f);

        /// <summary>Stool k's direction from the table centre, on the players' side (-Z); k = 0..3 left to right as seen from the dealer.</summary>
        public static Vector3 StoolDir(int k)
        {
            float a = (-54f + 36f * k) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));
        }

        private BlackjackTable table;
        private int stool = -1;
        private readonly List<CardObj> mine = new List<CardObj>(), house = new List<CardObj>();
        private readonly List<GameObject> chips = new List<GameObject>();
        private string chipKey = "";
        private float nextDealAt;
        private static Material cardMat;

        private class CardObj
        {
            public Transform T;
            public MeshFilter F;
            public byte Card;
            public bool FaceUp, ShownUp;
            public Vector3 From, To;
            public Quaternion RotTo;
            public float Start;
            public float FlipStart = -1f;
        }

        public void Init(BlackjackTable t) => table = t;

        /// <summary>The stool the local player sits on, or -1.</summary>
        public int Stool => stool;

        // ---- Sitting down and getting up ----

        public void SitLocal()
        {
            var local = NetworkPlayer.Local;
            if (local == null) return;
            var motor = local.GetComponent<FirstPersonMotor>();
            if (motor == null) return;
            // The nearest stool no one else sits on.
            float best = float.MaxValue;
            stool = 0;
            for (int k = 0; k < BlackjackTable.Stools; k++)
            {
                var at = transform.TransformPoint(StoolDir(k) * StoolRadius);
                bool taken = false;
                foreach (var other in PlayerLife.All)
                    if (other != null && other.gameObject != local.gameObject && Flat(other.transform.position - at) < 0.45f) taken = true;
                float d = Flat(local.transform.position - at) + (taken ? 100f : 0f);
                if (d < best) { best = d; stool = k; }
            }
            var seat = SeatPoint();
            var face = transform.TransformDirection(-StoolDir(stool));
            motor.Teleport(seat, Quaternion.LookRotation(face));
            motor.PinTo(seat);
            motor.SetPitch(SeatPitch);
        }

        /// <param name="moved">Something else moved the player (a teleport): let go where they are now.</param>
        public void StandLocal(bool moved = false)
        {
            var local = NetworkPlayer.Local;
            if (stool >= 0 && local != null)
            {
                var motor = local.GetComponent<FirstPersonMotor>();
                if (motor != null && motor.Pinned) motor.Unpin(moved ? local.transform.position : SeatPoint() + transform.TransformDirection(StoolDir(stool)) * 0.7f + Vector3.up * 0.05f);
                if (motor != null) motor.SetPitch(0f);
            }
            stool = -1;
            Clear();
        }

        /// <summary>Looking down between your hand and the dealer's line, so both sit near the middle of the screen.</summary>
        public const float SeatPitch = 36f;

        private Vector3 SeatPoint() => transform.TransformPoint(StoolDir(stool) * StoolRadius) + Vector3.up * 0.1f;
        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var c in mine) if (c.T != null) Destroy(c.T.gameObject);
            foreach (var c in house) if (c.T != null) Destroy(c.T.gameObject);
            foreach (var g in chips) if (g != null) Destroy(g);
            mine.Clear(); house.Clear(); chips.Clear(); chipKey = "";
        }

        // ---- Following the server's state ----

        private void LateUpdate()
        {
            if (stool < 0 || table == null) return;
            var local = NetworkPlayer.Local;
            if (local != null)
            {
                var motor = local.GetComponent<FirstPersonMotor>();
                // Moved off the stool by something else (a teleport, a cutscene): get up and leave the table.
                if (motor != null && (!motor.Pinned || Flat(local.transform.position - SeatPoint()) > 0.3f)) { table.Leave(moved: true); return; }
                if (motor != null) motor.PinTo(SeatPoint());
            }
            bool dealt = table.SeenPhase != BlackjackTable.Phase.Betting;
            var hands = dealt ? table.SeenHands : new byte[0][];
            var houseCards = dealt ? table.SeenHouse : new byte[0];
            // A new deal (fewer cards than shown): sweep the felt.
            int total = 0; foreach (var h in hands) total += h.Length;
            if (total < mine.Count || houseCards.Length < house.Count) { Clear(); }

            var d = StoolDir(stool);
            var tangent = new Vector3(-d.z, 0f, d.x);
            int idx = 0;
            for (int h = 0; h < hands.Length; h++)
            {
                var basePos = d * CardRadius + tangent * ((h - (hands.Length - 1) * 0.5f) * 0.42f);
                for (int i = 0; i < hands[h].Length; i++, idx++)
                {
                    var pos = basePos + tangent * (i * HandFan) - d * (i * 0.012f) + Vector3.up * (FeltY + 0.002f + i * 0.0015f);
                    var rot = Quaternion.LookRotation(-d) * Quaternion.Euler(0f, (i - 1) * 3f, 0f);
                    Place(mine, idx, hands[h][i], true, pos, rot);
                }
            }
            for (int i = 0; i < houseCards.Length; i++)
            {
                bool up = !(i == 1 && table.SeenHoleHidden);
                var pos = new Vector3(-0.26f + i * HouseStep, FeltY + 0.002f + i * 0.0015f, HouseZ);
                Place(house, i, houseCards[i], up, pos, Quaternion.identity);
            }
            foreach (var c in mine) Animate(c);
            foreach (var c in house) Animate(c);
            UpdateChips(hands.Length > 0 ? table.SeenBets : new[] { table.BetChoice }, d, tangent, hands.Length);
        }

        private void Place(List<CardObj> list, int i, byte card, bool faceUp, Vector3 pos, Quaternion rot)
        {
            if (i >= list.Count)
            {
                var go = new GameObject("Card");
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = CardMaterial();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var c = new CardObj { T = go.transform, F = mf, Card = card, FaceUp = faceUp, ShownUp = faceUp, From = ShoeLocal, To = pos, RotTo = rot };
                // Cards leave the shoe one after another, like a dealer's hand.
                c.Start = Mathf.Max(Time.time, nextDealAt);
                nextDealAt = c.Start + 0.22f;
                mf.sharedMesh = CardArt.Mesh(faceUp ? card : CardArt.Back);
                go.transform.localPosition = ShoeLocal;
                list.Add(c);
                if (c.Start > Time.time + 0.01f) go.SetActive(false);
                return;
            }
            var o = list[i];
            o.To = pos; o.RotTo = rot;
            if (o.FaceUp != faceUp) { o.FaceUp = faceUp; o.FlipStart = Time.time; }
        }

        private void Animate(CardObj c)
        {
            if (c.T == null) return;
            float t = (Time.time - c.Start) / 0.28f;
            if (t < 0f) return;
            if (!c.T.gameObject.activeSelf) { c.T.gameObject.SetActive(true); Sfx.Play(Sound.Card, transform.TransformPoint(ShoeLocal), 0.5f, Random.Range(0.95f, 1.2f)); }
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
            var p = Vector3.Lerp(c.From, c.To, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.05f;
            var r = Quaternion.Slerp(Quaternion.Euler(0f, 90f, 0f), c.RotTo, e);
            // The reveal: the card turns over along its long edge.
            if (c.FlipStart >= 0f)
            {
                float f = Mathf.Clamp01((Time.time - c.FlipStart) / 0.35f);
                if (f >= 0.5f && c.ShownUp != c.FaceUp) { c.ShownUp = c.FaceUp; c.F.sharedMesh = CardArt.Mesh(c.FaceUp ? c.Card : CardArt.Back); }
                float ang = f < 0.5f ? f * 180f : (f - 1f) * 180f;
                r = r * Quaternion.Euler(0f, 0f, ang);
                p += Vector3.up * Mathf.Sin(f * Mathf.PI) * 0.04f;
                if (f >= 1f) c.FlipStart = -1f;
            }
            c.T.localPosition = p;
            c.T.localRotation = r;
        }

        // ---- Chips: 100 black, 25 green, 5 red, stacked on the betting circle (one stack per hand) ----

        private static readonly (int value, Color color)[] Denoms =
        {
            (100, new Color(0.08f, 0.08f, 0.09f)), (25, new Color(0.12f, 0.45f, 0.2f)), (5, new Color(0.62f, 0.1f, 0.1f)), (1, new Color(0.85f, 0.82f, 0.75f)),
        };

        private void UpdateChips(int[] bets, Vector3 d, Vector3 tangent, int hands)
        {
            string key = stool + ":" + string.Join(",", bets);
            if (key == chipKey) return;
            chipKey = key;
            foreach (var g in chips) if (g != null) Destroy(g);
            chips.Clear();
            for (int h = 0; h < bets.Length; h++)
            {
                var at = d * BetRadius + tangent * ((h - (bets.Length - 1) * 0.5f) * 0.12f);
                int left = bets[h], n = 0;
                foreach (var (value, color) in Denoms)
                    while (left >= value && n < 14)
                    {
                        left -= value;
                        var chip = MeshKit.Part(transform, "Chip", MeshKit.Cylinder(0.024f, 0.024f, 0.007f, 12), Mat.Bone, at + Vector3.up * (FeltY + 0.002f + n * 0.0075f));
                        ArtLibrary.Tint(chip.GetComponent<Renderer>(), color);
                        chips.Add(chip);
                        n++;
                    }
            }
        }

        // ---- What the HUD draws next to the cards ----

        /// <summary>World points for the labels: each hand's total, and the dealer's.</summary>
        public Vector3 HandLabelPoint(int hand, int hands)
        {
            var d = StoolDir(Mathf.Max(0, stool));
            var tangent = new Vector3(-d.z, 0f, d.x);
            // Nearer you than the cards: on screen the total sits just under your hand, never on it.
            return transform.TransformPoint(d * (CardRadius + CardH * 0.5f + 0.09f) + tangent * ((hand - (hands - 1) * 0.5f) * 0.42f) + Vector3.up * FeltY);
        }

        /// <summary>Beyond the dealer's cards (toward the dealer): on screen just above them.</summary>
        public Vector3 HouseLabelPoint => transform.TransformPoint(new Vector3(0f, FeltY + 0.02f, HouseZ + CardH * 0.5f + 0.07f));

        private static Material CardMaterial()
        {
            if (cardMat != null) return cardMat;
            cardMat = new Material(ArtLibrary.Get("Cards")) { name = "Cards" };
            cardMat.SetTexture("_BaseMap", CardArt.Atlas);
            return cardMat;
        }
    }
}
