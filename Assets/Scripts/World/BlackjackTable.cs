using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Deadman's Hand: blackjack against a skeleton dealer in the gambling tent. F sits you down and
    /// deals (the stake is taken up front); HIT or STAND in the table's panel. Closest to 21 without
    /// going over wins double; a natural 21 pays 3:2; a tie returns the stake; the dealer draws to 17.
    /// Server-authoritative: hands live on the server, each player sees their own through an owner RPC.
    /// </summary>
    public class BlackjackTable : Interactable
    {
        private class Hand { public readonly List<byte> Mine = new List<byte>(), House = new List<byte>(); public int Stake; public float StartedAt; }
        private readonly Dictionary<ulong, Hand> hands = new Dictionary<ulong, Hand>();

        // ---- The local player's view of their own hand ----
        public static bool LocalOpen { get; private set; }
        private static BlackjackTable localTable;
        private byte[] seenMine = new byte[0], seenHouse = new byte[0];
        private bool seenDone;
        private string seenResult = "";
        private int seenStake;

        public override float Range => 2.6f;

        public static int StakeFor(int gold) => Mathf.Clamp(Mathf.RoundToInt(gold * 0.2f / 5f) * 5, 20, 100);

        public override string Prompt(PlayerProgress who)
        {
            if (hands.ContainsKey(who.NetworkObjectId) || (LocalOpen && localTable == this)) return null;
            int stake = StakeFor(who.Gold.Value);
            return who.Gold.Value < stake ? "Deadman's Hand: you need " + stake + " gold" : "Play Deadman's Hand  -  stake " + stake + " gold";
        }

        private static byte Draw() => (byte)Random.Range(1, 14);

        public static int Total(IList<byte> cards)
        {
            int total = 0, aces = 0;
            foreach (var c in cards)
            {
                int v = c >= 10 ? 10 : c;
                if (c == 1) { aces++; v = 11; }
                total += v;
            }
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (hands.ContainsKey(who.NetworkObjectId)) return;
            int stake = StakeFor(who.Gold.Value);
            if (who.Gold.Value < stake) return;
            who.ServerAddGold(-stake);
            var h = new Hand { Stake = stake, StartedAt = Time.time };
            h.Mine.Add(Draw()); h.House.Add(Draw()); h.Mine.Add(Draw()); h.House.Add(Draw());
            hands[who.NetworkObjectId] = h;
            DealFxRpc();
            if (Total(h.Mine) == 21) { Resolve(who, h); return; }
            Send(who, h, false, "");
        }

        [Rpc(SendTo.Server)]
        public void HitRpc(NetworkObjectReference player)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who) || !hands.TryGetValue(who.NetworkObjectId, out var h)) return;
            h.Mine.Add(Draw());
            DealFxRpc();
            if (Total(h.Mine) >= 21) Resolve(who, h);
            else Send(who, h, false, "");
        }

        [Rpc(SendTo.Server)]
        public void StandRpc(NetworkObjectReference player)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who) || !hands.TryGetValue(who.NetworkObjectId, out var h)) return;
            Resolve(who, h);
        }

        private void Resolve(PlayerProgress who, Hand h)
        {
            int mine = Total(h.Mine);
            bool natural = mine == 21 && h.Mine.Count == 2;
            if (mine <= 21 && !natural) while (Total(h.House) < 17) h.House.Add(Draw());
            int house = Total(h.House);
            int payout; string result;
            if (mine > 21) { payout = 0; result = "BUST  -  the dead keep " + h.Stake; }
            else if (natural && !(house == 21 && h.House.Count == 2)) { payout = h.Stake + h.Stake * 3 / 2; result = "DEADMAN'S HAND!  +" + (payout - h.Stake); }
            else if (house > 21 || mine > house) { payout = h.Stake * 2; result = "YOU WIN  +" + h.Stake; }
            else if (mine == house) { payout = h.Stake; result = "PUSH  -  stake returned"; }
            else { payout = 0; result = "THE HOUSE WINS  -" + h.Stake; }
            who.ServerAddGold(payout);
            hands.Remove(who.NetworkObjectId);
            Send(who, h, true, result);
            ResultFxRpc(who.name, result, payout > h.Stake);
        }

        private void Send(PlayerProgress who, Hand h, bool done, string result) =>
            HandRpc(h.Mine.ToArray(), h.House.ToArray(), done, result, h.Stake, RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void HandRpc(byte[] mine, byte[] house, bool done, string result, int stake, RpcParams p = default)
        {
            seenMine = mine; seenHouse = house; seenDone = done; seenResult = result; seenStake = stake;
            LocalOpen = true;
            localTable = this;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DealFxRpc() => Sfx.Play(Sound.Card, transform.position + Vector3.up, 0.8f, Random.Range(0.9f, 1.2f));

        [Rpc(SendTo.ClientsAndHost)]
        private void ResultFxRpc(string who, string result, bool won)
        {
            var color = won ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.6f, 0.5f);
            UI.GameHud.Feed(who + " at Deadman's Hand: " + result, color);
            if (won) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 1f);
        }

        private void Update()
        {
            // Server: a hand left too long (or a player who wandered off) stands.
            if (IsServer && hands.Count > 0)
            {
                foreach (var id in new List<ulong>(hands.Keys))
                {
                    if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out var o) || o == null) { hands.Remove(id); continue; }
                    bool far = Vector3.Distance(o.transform.position, transform.position) > 5f;
                    if (far || Time.time - hands[id].StartedAt > 60f) Resolve(o.GetComponent<PlayerProgress>(), hands[id]);
                }
            }
            if (localTable != this || !LocalOpen) return;
            var local = NetworkPlayer.Local;
            if (local == null || Vector3.Distance(local.transform.position, transform.position) > 5f) { LocalOpen = false; return; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (!seenDone) StandRpc(local.NetworkObject);
                LocalOpen = false;
            }
        }

        private static string Rank(byte c) => c == 1 ? "A" : c == 11 ? "J" : c == 12 ? "Q" : c == 13 ? "K" : c.ToString();

        private void OnGUI()
        {
            if (localTable != this || !LocalOpen) return;
            UI.UiSkin.Apply();
            GUI.depth = -15;
            float w = Screen.width, h = Screen.height;
            float pw = 520, ph = 300, px = w / 2f - pw / 2f, py = h - ph - 150;
            UI.UiSkin.Panel(new Rect(px - 10, py - 10, pw + 20, ph + 20), 0.95f);
            GUI.Label(new Rect(px, py, pw, 28), "DEADMAN'S HAND   -   stake " + seenStake, new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter });
            DrawRow(px, py + 40, pw, "THE DEALER", seenHouse, !seenDone);
            DrawRow(px, py + 140, pw, "YOU", seenMine, false);
            if (seenDone)
            {
                GUI.Label(new Rect(px, py + 236, pw, 26), seenResult, new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter });
                var local = NetworkPlayer.Local;
                if (GUI.Button(new Rect(px + 60, py + 266, 180, 30), "DEAL AGAIN", UI.UiSkin.Button) && local != null) UseRpc(local.NetworkObject);
                if (GUI.Button(new Rect(px + pw - 240, py + 266, 180, 30), "LEAVE", UI.UiSkin.Button)) LocalOpen = false;
            }
            else
            {
                var local = NetworkPlayer.Local;
                if (GUI.Button(new Rect(px + 60, py + 250, 180, 40), "HIT", UI.UiSkin.ButtonBig) && local != null) { HitRpc(local.NetworkObject); Sfx.Play2D(Sound.Card, 0.5f); }
                if (GUI.Button(new Rect(px + pw - 240, py + 250, 180, 40), "STAND", UI.UiSkin.ButtonBig) && local != null) StandRpc(local.NetworkObject);
            }
        }

        private static void DrawRow(float x, float y, float width, string who, byte[] cards, bool hideSecond)
        {
            int total = Total(hideSecond && cards.Length > 0 ? new[] { cards[0] } : cards);
            GUI.Label(new Rect(x + 10, y, width, 20), who + "   " + (hideSecond ? total + " + ?" : total.ToString()), UI.UiSkin.Body);
            for (int i = 0; i < cards.Length; i++)
            {
                var r = new Rect(x + 10 + i * 62, y + 24, 54, 72);
                UI.UiSkin.Rect(r, new Color(0.92f, 0.88f, 0.78f));
                UI.UiSkin.Rect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color(0.98f, 0.95f, 0.86f));
                bool hidden = hideSecond && i == 1;
                if (hidden) { UI.UiSkin.Rect(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), new Color(0.35f, 0.08f, 0.1f)); continue; }
                var ink = (cards[i] + i) % 2 == 0 ? new Color(0.15f, 0.1f, 0.1f) : new Color(0.65f, 0.1f, 0.1f);
                GUI.Label(r, Rank(cards[i]), new GUIStyle(UI.UiSkin.Heading) { alignment = TextAnchor.MiddleCenter, fontSize = 26, normal = { textColor = ink } });
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // A long felt table, candles, and the dealer: a skeleton in a black waistcoat, waiting.
            MeshKit.Part(transform, "Top", MeshKit.Box(new Vector3(1.8f, 0.08f, 1.0f)), Mat.Wood, Vector3.up * 0.88f);
            var felt = MeshKit.Part(transform, "Felt", MeshKit.Box(new Vector3(1.6f, 0.02f, 0.82f)), Mat.Cloth, Vector3.up * 0.93f);
            ArtLibrary.Tint(felt.GetComponent<Renderer>(), new Color(0.1f, 0.28f, 0.14f));
            foreach (float x in new[] { -0.8f, 0.8f })
                foreach (float z in new[] { -0.4f, 0.4f })
                    MeshKit.Part(transform, "Leg", MeshKit.Box(new Vector3(0.08f, 0.86f, 0.08f)), Mat.Wood, new Vector3(x, 0.43f, z));
            for (int i = 0; i < 3; i++)
                MeshKit.Part(transform, "Card", MeshKit.Box(new Vector3(0.1f, 0.005f, 0.14f)), Mat.Bone, new Vector3(-0.3f + i * 0.14f, 0.95f, 0.1f), new Vector3(0f, i * 8f - 8f, 0f));
            var dealer = CharacterArt.BuildSkeleton(transform, CharacterArt.SkeletonKind.Warrior);
            dealer.transform.localPosition = new Vector3(0f, 0f, 0.95f);
            dealer.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Props.Candles(transform, transform.position + transform.right * 0.7f + Vector3.up * 0.93f, new Color(1f, 0.6f, 0.3f));
        }
    }
}
