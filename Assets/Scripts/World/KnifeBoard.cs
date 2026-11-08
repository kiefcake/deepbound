using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE KNIFE BOARD by Brann's yard (2026-11-08, camp pastimes: "knife throwing ... three throws a round, scored by
    /// ring"). Stand at the chalk line and press F: a gold mark swings over the painted rings; press F to let a knife fly at
    /// it. Three knives a round, 0-10 each (bull 10, then 7, 4, 1). The best round of the camp visit is chalked on the post,
    /// and BRANN'S WAGER pays once a visit: 24 or better is 25 gold, a perfect 30 is 60. Everyone sees the knives stick.
    /// The NetworkObject stands <see cref="LineBack"/> m AHEAD of the chalk line (an F target at your feet only worked when you
    /// looked down at it); the board is <see cref="Distance"/> metres past the line.
    /// </summary>
    public class KnifeBoard : Interactable
    {
        public const float Distance = 5.5f, BoardRadius = 0.6f, BoardHeight = 1.55f, LineBack = 1.0f;
        /// <summary>Where you stand to throw.</summary>
        public Vector3 LinePoint => transform.position - transform.forward * LineBack;
        public const int Knives = 3, WagerScore = 24, WagerGold = 25, PerfectGold = 60;
        public static readonly float[] Rings = { 0.08f, 0.2f, 0.36f, 0.55f };
        public static readonly int[] RingScore = { 10, 7, 4, 1 };

        public readonly NetworkVariable<int> Best = new NetworkVariable<int>();
        public readonly NetworkVariable<FixedString64Bytes> BestBy = new NetworkVariable<FixedString64Bytes>();

        private class Round { public int Thrown, Score, Visit; public bool Paid; }
        private readonly Dictionary<ulong, Round> rounds = new Dictionary<ulong, Round>();

        // Local: the swinging mark and the knives in the board.
        private int localThrown, localScore;
        private float roundStarted = -1f;
        private Transform board, mark;
        private readonly List<GameObject> stuck = new List<GameObject>();
        private float nextLocalThrow;

        public override float Range => 2f;
        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value * 1000 + (int)(DungeonDirector.Instance.RunStartedAt.Value % 997) : 0;

        /// <summary>Where the swinging mark is (board units, -1..1 of the board radius) at a time into the round.</summary>
        public static Vector2 MarkAt(float t) => new Vector2(Mathf.Sin(t * 2.3f) * 0.75f, Mathf.Sin(t * 3.4f + 1.1f) * 0.6f);

        public static int ScoreOf(Vector2 boardUnits)
        {
            float d = boardUnits.magnitude * BoardRadius;
            for (int i = 0; i < Rings.Length; i++) if (d <= Rings[i]) return RingScore[i];
            return 0;
        }

        public override string Prompt(PlayerProgress who)
        {
            if (roundStarted < 0f || localThrown >= Knives) return "Throw knives  -  3 to a round; stop the swinging mark on the bull (F)   ·   Brann pays " + WagerGold + " gold for " + WagerScore + "+" + (Best.Value > 0 ? "   ·   best today " + Best.Value + " (" + BestBy.Value + ")" : "");
            return "F - throw knife " + (localThrown + 1) + " of " + Knives + "   (" + localScore + " so far)";
        }

        protected override void ServerUse(PlayerProgress who) => StartRpc(RpcTarget.Single(who.OwnerClientId, RpcTargetUse.Temp));

        [Rpc(SendTo.SpecifiedInParams)]
        private void StartRpc(RpcParams p = default)
        {
            if (roundStarted >= 0f && localThrown < Knives) { ThrowLocal(); return; }
            roundStarted = Time.time;
            localThrown = 0; localScore = 0;
            nextLocalThrow = Time.time + 0.3f;
            Sfx.Play2D(Sound.Unsheathe, 0.6f);
        }

        /// <summary>Shots: start aiming here, as F would.</summary>
        public void LocalDebugAim() { roundStarted = Time.time; localThrown = 0; localScore = 0; nextLocalThrow = Time.time; }

        private void ThrowLocal()
        {
            if (Time.time < nextLocalThrow) return;
            nextLocalThrow = Time.time + 0.45f;
            var me = NetworkPlayer.Local;
            if (me == null) return;
            var at = MarkAt(Time.time - roundStarted);
            ThrowRpc(me.NetworkObject, at);
        }

        [Rpc(SendTo.Server)]
        private void ThrowRpc(NetworkObjectReference player, Vector2 at)
        {
            if (!player.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1.5f) return;
            ServerThrow(who, at);
        }

        /// <summary>Server: one knife lands at <paramref name="at"/> (board units). Public for tests.</summary>
        public int ServerThrow(PlayerProgress who, Vector2 at)
        {
            if (!rounds.TryGetValue(who.OwnerClientId, out var r)) rounds[who.OwnerClientId] = r = new Round();
            if (r.Visit != Visit) { r.Visit = Visit; r.Paid = false; r.Thrown = 0; r.Score = 0; }
            if (r.Thrown >= Knives) { r.Thrown = 0; r.Score = 0; }
            at = Vector2.ClampMagnitude(at, 1.1f);
            int s = ScoreOf(at);
            r.Thrown++;
            r.Score += s;
            int paid = 0;
            if (r.Thrown >= Knives)
            {
                if (r.Score > Best.Value) { Best.Value = r.Score; BestBy.Value = who.name; }
                if (!r.Paid && r.Score >= WagerScore)
                {
                    r.Paid = true;
                    paid = r.Score >= Knives * RingScore[0] ? PerfectGold : WagerGold;
                    who.ServerAddGold(paid);
                }
            }
            KnifeRpc(who.OwnerClientId, who.name, at, s, r.Thrown, r.Score, paid);
            return s;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void KnifeRpc(ulong client, string who, Vector2 at, int score, int thrown, int total, int paid)
        {
            if (board != null)
            {
                // The knife: a steel blade buried in the board, a leather grip sticking out toward the line.
                var k = new GameObject("Knife").transform;
                k.SetParent(board, false);
                k.localPosition = new Vector3(at.x * BoardRadius, at.y * BoardRadius, -0.04f);
                k.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(-8f, 8f), Random.Range(0f, 360f));
                MeshKit.Part(k, "Blade", MeshKit.Box(new Vector3(0.025f, 0.006f, 0.12f)), Mat.Steel, new Vector3(0f, 0f, 0.0f));
                var grip = MeshKit.Part(k, "Grip", MeshKit.Box(new Vector3(0.03f, 0.03f, 0.1f)), Mat.Leather, new Vector3(0f, 0f, -0.1f));
                ArtLibrary.Tint(grip.GetComponent<Renderer>(), new Color(0.35f, 0.2f, 0.1f));
                stuck.Add(k.gameObject);
                while (stuck.Count > 9) { Destroy(stuck[0]); stuck.RemoveAt(0); }
            }
            var hitAt = board != null ? board.TransformPoint(new Vector3(at.x * BoardRadius, at.y * BoardRadius, 0f)) : transform.position;
            Sfx.Play(Sound.Hit, hitAt, 0.7f, score >= 10 ? 1.4f : 1.1f);
            UI.GameHud.ShowCombo(hitAt + Vector3.up * 0.4f, score == 10 ? "BULL! 10" : score > 0 ? score.ToString() : "MISS", score >= 10 ? new Color(1f, 0.85f, 0.35f) : new Color(0.9f, 0.88f, 0.84f));
            if (thrown >= Knives)
            {
                var me = NetworkPlayer.Local;
                if (me != null && Vector3.Distance(me.transform.position, transform.position) < 14f)
                    UI.GameHud.Feed(who + " throws " + total + " at the knife board" + (paid > 0 ? "  -  Brann pays " + paid + " gold" : ""), paid > 0 ? new Color(1f, 0.85f, 0.4f) : new Color(0.85f, 0.85f, 0.8f));
                if (paid > 0) UI.CampChatter.Say(FolkId.Brann, total >= 30 ? "Three in the bull. I've seen that once before. He's dead now. Don't let it go to your head." : "Not bad. Not good. Not bad. Take your coin.", 4f);
            }
            if (NetworkManager != null && client == NetworkManager.LocalClientId) { localThrown = thrown; localScore = total; if (thrown >= Knives) roundStarted = -1f; }
        }

        private void Update()
        {
            if (mark == null) return;
            bool aiming = roundStarted >= 0f && localThrown < Knives;
            var me = NetworkPlayer.Local;
            if (aiming && (me == null || Vector3.Distance(me.transform.position, transform.position) > Range + 2.5f)) { roundStarted = -1f; aiming = false; }
            mark.gameObject.SetActive(aiming);
            if (!aiming) return;
            var at = MarkAt(Time.time - roundStarted);
            mark.localPosition = new Vector3(at.x * BoardRadius, at.y * BoardRadius, -0.03f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // The chalk line at the stand, and the round board on its post ahead.
            var chalk = MeshKit.Part(transform, "ChalkLine", MeshKit.Box(new Vector3(1.6f, 0.01f, 0.06f)), Mat.Bone, new Vector3(0f, 0.01f, -LineBack + 0.3f));
            ArtLibrary.Tint(chalk.GetComponent<Renderer>(), new Color(0.9f, 0.88f, 0.82f));
            var post = transform.position + transform.forward * Distance;
            MeshKit.Part(transform, "Post", MeshKit.Box(new Vector3(0.18f, BoardHeight + 0.4f, 0.18f)), Mat.Wood, new Vector3(0f, 0f, Distance - LineBack + 0.15f) + Vector3.up * ((BoardHeight + 0.4f) / 2f));
            board = MeshKit.Pivot(transform, "Board", new Vector3(0f, BoardHeight, Distance - LineBack));
            board.localRotation = Quaternion.identity;
            MeshKit.Part(board, "Wood", MeshKit.Cylinder(BoardRadius, BoardRadius, 0.08f, 20), Mat.Wood, new Vector3(0f, 0f, 0.08f), new Vector3(-90f, 0f, 0f)); // grows toward the line: its face is at z 0
            var colours = new[] { new Color(0.8f, 0.15f, 0.12f), new Color(0.9f, 0.85f, 0.7f), new Color(0.15f, 0.15f, 0.15f), new Color(0.9f, 0.85f, 0.7f) };
            for (int i = Rings.Length - 1; i >= 0; i--)
            {
                var ring = MeshKit.Part(board, "Ring" + i, MeshKit.Cylinder(Rings[i], Rings[i], 0.01f, 20), Mat.Cloth, new Vector3(0f, 0f, -0.001f * (Rings.Length - i)), new Vector3(-90f, 0f, 0f));
                ArtLibrary.Tint(ring.GetComponent<Renderer>(), colours[i]);
            }
            mark = MeshKit.Pivot(board, "Mark", Vector3.zero);
            var m = MeshKit.Part(mark, "MarkRing", MeshKit.Cylinder(0.06f, 0.06f, 0.006f, 12), Mat.Glow, Vector3.zero, new Vector3(-90f, 0f, 0f));
            ArtLibrary.SetGlow(m.GetComponent<Renderer>(), new Color(1f, 0.8f, 0.3f) * 1.4f);
            mark.gameObject.SetActive(false);
            _ = post;
        }
    }
}
