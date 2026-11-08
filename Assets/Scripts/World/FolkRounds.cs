using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The camp folk's rounds (2026-11-07, the user: "wander and chat"): now and then someone leaves their post for a short
    /// errand and walks back. Hobb cools off at the well, Vey goes to pick at Silas's hood, Brann warms his hands at the fire,
    /// Ysolde listens to the gong, Silas wanders to the tavern door. Deterministic from the server clock, so every peer sees
    /// the same walk and the server moves the struck-body (<see cref="CampFolk"/>) with the figure. Each route is checked
    /// once for clear ground (sphere casts round the stalls); a route that cannot be walked is dropped and that person stays home.
    /// </summary>
    public static class FolkRounds
    {
        public const float Speed = 1.25f, Stay = 14f;

        private class Round
        {
            public FolkId Who;
            public float Period, Offset;
            public Vector3[] Path;   // home ... destination
            public float Length;
            public Vector3 FaceAtDest;
            public string[] Lines;
            public FolkId? Answer;   // someone who answers at the destination
            public string[] Answers;
        }

        private static Dictionary<FolkId, Round> rounds;
        private static readonly int[] lastSaid = new int[16];

        public static double Clock => DebugShift + (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening
            ? Unity.Netcode.NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble);

        /// <summary>Tests and the camp inspector: moves everyone's rounds clock.</summary>
        public static double DebugShift;
        /// <summary>Tests: everyone stays home (a buy must not miss a person who just left on an errand).</summary>
        public static bool StayHome;

        /// <summary>The people who go on rounds (a clear way was found).</summary>
        public static IEnumerable<FolkId> Walkers { get { if (rounds == null) Build(); return rounds.Keys; } }

        /// <summary>Tests: shift the clock so <paramref name="who"/> stands at their errand's end (or, with <paramref name="along"/> 0.5, halfway there).</summary>
        public static void DebugArrive(FolkId who, float along = -1f)
        {
            if (rounds == null) Build();
            if (!rounds.TryGetValue(who, out var r)) return;
            DebugShift = 0;
            double t = DungeonDirector.FolkClock(who, Clock) + r.Offset; // a person held in conversation keeps their own clock still
            float walk = r.Length / Speed;
            float target = along < 0f ? walk + Stay * 0.5f : walk * along;
            double inPeriod = t - System.Math.Floor(t / r.Period) * r.Period;
            DebugShift = target - inPeriod;
        }

        /// <summary>Forget the routes (a new camp was built).</summary>
        public static void Reset() { rounds = null; builtAt = Time.time; }
        private static float builtAt;

        private static void Build()
        {
            rounds = new Dictionary<FolkId, Round>();
            Physics.SyncTransforms();
            Add(FolkId.Hobb, Lobby.HobbSpot, Vector3.right, Lobby.WellSpot + new Vector3(1.4f, 0f, 0.5f), 210f, 40f,
                new[] { "*Hobb splashes well water on his face.* Forge heat. You never stop sweating it.", "*Hobb drinks from the bucket.* Iron is thirsty work.", "Cold water and a hot anvil. All a man needs." }, null, null);
            Add(FolkId.Vey, Lobby.VeySpot, Lobby.TailorFacing, Lobby.MerchantSpot + Vector3.right * 2.7f + Vector3.forward * 0.9f, 240f, 120f,
                new[] { "Silas, that hood. Let me burn it and make you a new one.", "You have been wearing green since the war, Silas. Which war?", "Your pendants are crooked. All of them." },
                FolkId.Silas, new[] { "The hood stays, Vey.", "I like the green. The green likes me.", "They are not crooked. They are rustic." });
            Add(FolkId.Brann, Lobby.YardSpot, Vector3.right, Lobby.FirePit + new Vector3(Mathf.Cos(150f * Mathf.Deg2Rad), 0f, Mathf.Sin(150f * Mathf.Deg2Rad)) * 5.3f, 190f, 75f,
                new[] { "*Brann warms his hands at the fire.* Cold hands drop swords.", "Good fire. Somebody built this properly. Not one of you.", "*Brann stares into the flames.* Sat by a fire like this the night before Ravenhold's last stand." }, null, null);
            Add(FolkId.Ysolde, Lobby.RunewrightSpot, new Vector3(-1f, 0f, -0.25f).normalized, Lobby.GongSpot + new Vector3(-2.3f, 0f, -1.3f), 260f, 160f,
                new[] { "*Ysolde lays a palm on the gong.* The bronze remembers every blow.", "It still rings from the last strike. Under the hearing. Can you feel it?", "One day someone will strike this hard enough to wake what sleeps under the keep." }, null, null);
            Add(FolkId.Silas, Lobby.MerchantSpot, Vector3.right, Lobby.TavernCenter + new Vector3(Lobby.TavernHalfX + 1.7f, 0f, 1.3f), 330f, 200f,
                new[] { "Marta! Put one on my tab. And one for the hood.", "*Silas peers into the tavern.* Business is better when they are drunk.", "Just checking the competition. Mead is a potion with ambition." }, null, null);
        }

        private static void Add(FolkId who, Vector3 home, Vector3 front, Vector3 dest, float period, float offset, string[] lines, FolkId? answer, string[] answers)
        {
            var path = FindPath(home, dest);
            if (path == null) { Debug.Log("[FolkRounds] " + who + " stays home: no clear way to " + (dest - Lobby.Origin)); return; }
            float len = 0f;
            for (int i = 1; i < path.Length; i++) len += Flat(path[i] - path[i - 1]);
            // Face what they came for: the person they came to talk to, else the thing nearest the errand's end.
            var face = (answer.HasValue ? AnswerSpot(answer.Value) : LookFor(dest)) - dest;
            face.y = 0f;
            rounds[who] = new Round { Who = who, Period = period, Offset = offset, Path = path, Length = len, FaceAtDest = face.sqrMagnitude > 0.01f ? face.normalized : front, Lines = lines, Answer = answer, Answers = answers };
        }

        private static Vector3 LookFor(Vector3 dest)
        {
            Vector3 best = Lobby.FirePit; float bd = Flat(dest - best);
            foreach (var p in new[] { Lobby.WellSpot, Lobby.GongSpot, Lobby.TavernCenter + new Vector3(Lobby.TavernHalfX, 0f, 0f), Lobby.FirePit })
                if (Flat(dest - p) < bd) { bd = Flat(dest - p); best = p; }
            return best;
        }

        private static Vector3 AnswerSpot(FolkId who)
        {
            foreach (var (id, at) in Lobby.FolkBodies) if (id == who) return at;
            return Lobby.Origin;
        }

        // ---- A small grid search round the stalls (0.5 m cells): the same answer on every peer, people and players ignored ----
        private const float Cell = 0.5f, Radius = 0.3f;
        private static readonly Collider[] hits = new Collider[16];

        private static bool Free(Vector3 at)
        {
            // From 15 cm up: rugs, the road kerb and floor mats are stepped over.
            int n = Physics.OverlapCapsuleNonAlloc(at + Vector3.up * (Radius + 0.15f), at + Vector3.up * 1.7f, Radius, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (!Ignored(hits[i])) return false;
            return true;
        }

        private static bool Ignored(Collider c) =>
            c == null || c.GetComponentInParent<CampFolk>() != null || c.GetComponentInParent<Art.FolkIdle>() != null || c.GetComponentInParent<Player.NetworkPlayer>() != null || c is CharacterController
            || c.GetComponentInParent<Unity.Netcode.NetworkObject>() != null && c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic;

        private static bool Walkable(Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            int steps = Mathf.CeilToInt(len / 0.25f);
            for (int i = 1; i <= steps; i++)
                if (!Free(a + d * (i / (float)steps))) return false;
            return true;
        }

        private static Vector3[] FindPath(Vector3 home, Vector3 dest)
        {
            var min = Vector3.Min(home, dest) - new Vector3(5f, 0f, 5f);
            var max = Vector3.Max(home, dest) + new Vector3(5f, 0f, 5f);
            int w = Mathf.CeilToInt((max.x - min.x) / Cell) + 1, h = Mathf.CeilToInt((max.z - min.z) / Cell) + 1;
            float y = home.y;
            Vector3 P(int x, int z) => new Vector3(min.x + x * Cell, y, min.z + z * Cell);
            (int, int) C(Vector3 p) => (Mathf.Clamp(Mathf.RoundToInt((p.x - min.x) / Cell), 0, w - 1), Mathf.Clamp(Mathf.RoundToInt((p.z - min.z) / Cell), 0, h - 1));
            var free = new bool[w, h];
            for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) free[x, z] = Free(P(x, z));
            var (sx, sz) = C(home); var (gx, gz) = C(dest);
            // Home is behind a counter and dest may be by someone: let the walk start and end there, and clear a step round them.
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (sx + dx >= 0 && sx + dx < w && sz + dz >= 0 && sz + dz < h && Walkable(home, P(sx + dx, sz + dz))) free[sx + dx, sz + dz] = true;
            }
            free[sx, sz] = true; free[gx, gz] = true;
            var prev = new int[w * h];
            for (int i = 0; i < prev.Length; i++) prev[i] = -1;
            var queue = new Queue<int>();
            int start = sx + sz * w, goal = gx + gz * w;
            queue.Enqueue(start); prev[start] = start;
            int[] ox = { 1, -1, 0, 0, 1, 1, -1, -1 }, oz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (queue.Count > 0 && prev[goal] < 0)
            {
                int cur = queue.Dequeue(); int cx = cur % w, cz = cur / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + ox[k], nz = cz + oz[k];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h || !free[nx, nz]) continue;
                    if (k >= 4 && (!free[cx + ox[k], cz] || !free[cx, cz + oz[k]])) continue; // no corner cutting
                    int ni = nx + nz * w;
                    if (prev[ni] >= 0) continue;
                    prev[ni] = cur;
                    queue.Enqueue(ni);
                }
            }
            if (prev[goal] < 0)
            {
                int open = 0; foreach (bool f in free) if (f) open++;
                string blocker = "";
                int bn = Physics.OverlapCapsuleNonAlloc(dest + Vector3.up * (Radius + 0.15f), dest + Vector3.up * 1.7f, Radius, hits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < bn; i++) if (!Ignored(hits[i])) blocker += hits[i].name + " ";
                Debug.Log("[FolkRounds] no path: " + open + "/" + (w * h) + " cells open, dest blocked by: " + blocker);
                return null;
            }
            var cells = new List<Vector3>();
            for (int at = goal; ; at = prev[at]) { cells.Add(P(at % w, at / w)); if (at == start) break; }
            cells.Reverse();
            cells[0] = home; cells[cells.Count - 1] = dest;
            // Straighten: skip every corner that a clear straight line can cut.
            var path = new List<Vector3> { cells[0] };
            int from = 0;
            while (from < cells.Count - 1)
            {
                int far = from + 1;
                for (int k = cells.Count - 1; k > from + 1; k--)
                    if (Walkable(cells[from], cells[k])) { far = k; break; }
                path.Add(cells[far]);
                from = far;
            }
            return path.ToArray();
        }

        /// <summary>Does this person go on rounds at all (a clear route was found)?</summary>
        public static bool Walks(FolkId who)
        {
            if (rounds == null && Time.time - builtAt < 2.5f) return false; // the stations are still spawning
            if (rounds == null) Build();
            return rounds.ContainsKey(who);
        }

        public struct Pose
        {
            public Vector3 Position;
            public Vector3 Facing;
            public bool Away, Walking, AtDest;
            public int Visit;   // which errand (for picking a line)
        }

        /// <summary>Where <paramref name="who"/> is at the server clock (Away false = at home, untouched).</summary>
        public static Pose At(FolkId who)
        {
            if (rounds == null) Build();
            var pose = new Pose();
            if (StayHome || !rounds.TryGetValue(who, out var r)) return pose;
            double t = DungeonDirector.FolkClock(who, Clock) + r.Offset; // a person held in conversation keeps their own clock still
            pose.Visit = (int)(t / r.Period);
            float tau = (float)(t - pose.Visit * (double)r.Period);
            float walk = r.Length / Speed;
            if (tau >= walk * 2f + Stay) return pose;
            pose.Away = true;
            float along;
            if (tau < walk) { along = tau * Speed; pose.Walking = true; }
            else if (tau < walk + Stay) { along = r.Length; pose.AtDest = true; }
            else { along = r.Length - (tau - walk - Stay) * Speed; pose.Walking = true; }
            along = Mathf.Clamp(along, 0f, r.Length);
            bool goingOut = tau < walk;
            // Along the path.
            float left = along;
            for (int i = 1; i < r.Path.Length; i++)
            {
                var seg = r.Path[i] - r.Path[i - 1]; seg.y = 0f;
                float L = seg.magnitude;
                if (left <= L || i == r.Path.Length - 1)
                {
                    pose.Position = r.Path[i - 1] + seg * (L > 0.001f ? Mathf.Clamp01(left / L) : 1f);
                    pose.Facing = (goingOut ? seg : -seg).normalized;
                    break;
                }
                left -= L;
            }
            if (pose.AtDest) pose.Facing = r.FaceAtDest;
            return pose;
        }

        /// <summary>Local, once per errand: the line said on arrival (and the answer).</summary>
        public static void Chatter(FolkId who, Pose pose)
        {
            if (!pose.AtDest || rounds == null || !rounds.TryGetValue(who, out var r)) return;
            if (lastSaid[(int)who] == pose.Visit + 1) return;
            lastSaid[(int)who] = pose.Visit + 1;
            int k = Mathf.Abs(pose.Visit) % r.Lines.Length;
            UI.CampChatter.Say(who, r.Lines[k], 5.5f);
            if (r.Answer.HasValue && r.Answers != null) UI.CampChatter.SayLater(r.Answer.Value, r.Answers[k % r.Answers.Length], 3.2f);
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
