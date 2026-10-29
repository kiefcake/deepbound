using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// Playing together (2026-10-28, the user: "continue work on multiplayer fun"). Everything here only happens with
    /// two or more players:
    /// <list type="bullet">
    /// <item><b>PINCER</b>: hit an enemy an ally struck in the last <see cref="PincerWindow"/> s from the other side
    ///   (at least <see cref="PincerAngle"/> degrees apart around it) for x<see cref="PincerMultiplier"/>.</item>
    /// <item><b>ASSIST</b>: when an enemy dies, everyone else who hurt it in the last <see cref="AssistWindow"/> s
    ///   gets ultimate charge and an ASSIST tick.</item>
    /// <item><b>LAST STAND</b>: the only one still standing while the rest are down hits x<see cref="LastStandDamage"/>
    ///   and takes x<see cref="LastStandTaken"/>. Getting someone up from it is a CLUTCH: both get Brothers in Arms,
    ///   the saviour a slice of ultimate, and the party hears about it.</item>
    /// </list>
    /// Kicking an ally (Kick.cs) shoves them, no harm done.
    /// Second round (same day): SPECTATE while down (LMB cycles the standing allies, RMB back to your own eyes; a
    /// soul watches someone on its own), VENGEANCE (whatever downs a player is Marked; kill it to AVENGE them - their
    /// bleed-out clock winds back), FLOOR HONOURS (feed lines when a floor is cleared), and two revivers are faster
    /// (PlayerLife).
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class Teamwork : NetworkBehaviour
    {
        public const float PincerWindow = 2.5f, PincerAngle = 120f, PincerMultiplier = 1.15f, PincerReach = 25f;
        public const float AssistWindow = 6f, AssistCharge = 3f;
        public const float LastStandDamage = 1.25f, LastStandTaken = 0.8f, ClutchCharge = 15f;
        public const float VengeanceSeconds = 20f, AvengeBleedBack = 15f, AvengeCharge = 10f;
        public const float WatchBack = 2.8f, WatchUp = 0.9f;

        private PlayerLife life;
        private bool wasLastStand;

        /// <summary>For tests: assists and clutches this player was credited with (server).</summary>
        public int ServerAssists { get; private set; }
        public int ServerClutches { get; private set; }

        private void Awake() => life = GetComponent<PlayerLife>();

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            EnsureServerHooks();
            GetComponent<Health>().IncomingModifiers.Add((dealt, from) => InLastStand(life) ? dealt * LastStandTaken : dealt);
        }

        /// <summary>Any peer: the last of a party of two or more still on their feet, with an ally down.</summary>
        public static bool InLastStand(PlayerLife me)
        {
            if (me == null || me.Downed.Value || me.Health == null || !me.Health.IsAlive || PlayerLife.All.Count < 2) return false;
            bool anyDown = false;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p == me) continue;
                if (!p.Downed.Value) return false;
                anyDown = true;
            }
            return anyDown;
        }

        /// <summary>The local player's last stand (the overlay and status badges read it).</summary>
        public static bool LocalLastStand => NetworkPlayer.Local != null && InLastStand(NetworkPlayer.Local.GetComponent<PlayerLife>());

        // ---- spectating (owner) ----

        /// <summary>The ally the local player is watching while down, or null.</summary>
        public static PlayerLife Watching { get; private set; }
        private bool watchRefused;
        private Vector3 watchPos;
        private bool watchSnapped;

        /// <summary>Allies on their feet, in a fixed order, for cycling.</summary>
        /// <summary>Tests: watch <paramref name="who"/> as if LMB had picked them.</summary>
        public static void DebugWatch(PlayerLife who) { Watching = who; }

        /// <summary>For tests: where the watching camera was last put (the camera itself is moved back to the body
        /// in Update and over the ally again in LateUpdate, so mid-frame it reads wrong).</summary>
        public static Vector3 LastWatchCamera { get; private set; }

        private List<PlayerLife> Standing()
        {
            var list = new List<PlayerLife>();
            foreach (var p in PlayerLife.All)
                if (p != null && p != life && !p.Downed.Value) list.Add(p);
            list.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
            return list;
        }

        private void OwnerSpectate()
        {
            if (life == null || !life.Downed.Value)
            {
                Watching = null;
                watchRefused = false;
                return;
            }
            var standing = Standing();
            if (Watching != null && (Watching.Downed.Value || !standing.Contains(Watching))) Watching = null;
            if (UI.PauseMenu.Open) return;
            if (UI.Binds.PrimaryDown && standing.Count > 0)
            {
                int i = Watching != null ? standing.IndexOf(Watching) : -1;
                Watching = standing[(i + 1) % standing.Count];
                watchRefused = false;
                watchSnapped = false;
            }
            else if (UI.Binds.SecondaryDown && Watching != null)
            {
                Watching = null;
                watchRefused = true;
            }
            // A soul has a long wait: it watches someone without being asked (RMB still looks away).
            if (Watching == null && life.Soul.Value && !watchRefused && standing.Count > 0)
            {
                Watching = standing[0];
                watchSnapped = false;
            }
        }

        private void LateUpdate()
        {
            if (!IsOwner || Watching == null) return;
            var cam = ViewCamera.Current;
            if (cam == null) return;
            // Over the shoulder, pulled in where a wall would come between.
            var head = Watching.transform.position + Vector3.up * 1.7f;
            var back = -Watching.transform.forward * WatchBack + Vector3.up * WatchUp;
            float dist = back.magnitude;
            if (Physics.SphereCast(head, 0.2f, back / dist, out var hit, dist, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<PlayerLife>() == null)
                dist = Mathf.Max(0.4f, hit.distance - 0.1f);
            var want = head + back.normalized * dist;
            watchPos = watchSnapped ? Vector3.Lerp(watchPos, want, Time.deltaTime * 10f) : want;
            watchSnapped = true;
            cam.transform.position = watchPos;
            LastWatchCamera = watchPos;
            cam.transform.rotation = Quaternion.LookRotation(head + Watching.transform.forward * 3f - watchPos);
        }

        private void Update()
        {
            if (!IsOwner) return;
            OwnerSpectate();
            bool now = InLastStand(life);
            if (now && !wasLastStand)
            {
                UI.GameHud.ShowTitleCard("LAST STAND", "Your party is down. Get them up - you hit harder and take less until you do.", new Color(0.95f, 0.25f, 0.2f));
                Sfx.Play(Sound.Toll, transform.position + Vector3.up * 1.6f, 0.35f, 0.8f);
            }
            wasLastStand = now;
        }

        // ---- server bookkeeping: who hit what, from where, when ----

        private struct Hit { public ulong Attacker; public float Time; }
        private static readonly Dictionary<ulong, List<Hit>> hits = new Dictionary<ulong, List<Hit>>();
        private static bool hooked;

        public static void EnsureServerHooks()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerAnyDamaged += OnAnyDamaged;
        }

        /// <summary>Server: the PINCER multiplier for this hit (and it records the hit). Called from the combo chain.</summary>
        public static float ServerPincer(Health target, PlayerProgress attacker)
        {
            if (target == null || attacker == null || PlayerLife.All.Count < 2) return 1f;
            if (!hits.TryGetValue(target.NetworkObjectId, out var list))
            {
                if (hits.Count > 512) hits.Clear();
                hits[target.NetworkObjectId] = list = new List<Hit>();
            }
            float now = Time.time;
            ulong me = attacker.NetworkObjectId;
            var at = target.transform.position;
            var mine = Flat(attacker.transform.position - at);
            bool pincer = false;
            var net = NetworkManager.Singleton;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var h = list[i];
                if (now - h.Time > AssistWindow) { list.RemoveAt(i); continue; }
                if (h.Attacker == me) { list.RemoveAt(i); continue; }
                if (pincer || now - h.Time > PincerWindow || net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(h.Attacker, out var o)) continue;
                if (!o.TryGetComponent<PlayerLife>(out var ally) || ally.Downed.Value) continue;
                var theirs = Flat(ally.transform.position - at);
                if (mine.sqrMagnitude < 0.01f || theirs.sqrMagnitude < 0.01f) continue;
                if (mine.magnitude > PincerReach || theirs.magnitude > PincerReach) continue;
                if (Vector3.Angle(mine, theirs) >= PincerAngle) pincer = true;
            }
            list.Add(new Hit { Attacker = me, Time = now });
            if (!pincer) return 1f;
            Classes.Combos.Popup(target, Classes.ComboKind.Pincer, attacker);
            return PincerMultiplier;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static void OnAnyDamaged(Health target, float dealt, ulong attackerId)
        {
            if (target.Team != Team.Enemies || target.IsAlive) return;
            ServerCheckAvenged(target, attackerId);
            if (!hits.TryGetValue(target.NetworkObjectId, out var list)) return;
            hits.Remove(target.NetworkObjectId);
            var net = NetworkManager.Singleton;
            if (net == null) return;
            var credited = new HashSet<ulong>();
            foreach (var h in list)
            {
                if (h.Attacker == attackerId || Time.time - h.Time > AssistWindow || !credited.Add(h.Attacker)) continue;
                if (!net.SpawnManager.SpawnedObjects.TryGetValue(h.Attacker, out var o) || !o.TryGetComponent<Teamwork>(out var tw)) continue;
                tw.ServerAssists++;
                if (o.TryGetComponent<Ultimates>(out var u)) u.ServerAddCharge(AssistCharge);
                tw.AssistRpc(target.Center + Vector3.up * 0.5f);
            }
        }

        // ---- vengeance (server) ----

        private struct Grudge { public PlayerLife Fallen; public float Until; }
        private static readonly Dictionary<ulong, Grudge> grudges = new Dictionary<ulong, Grudge>();

        /// <summary>For tests: players avenged so far (server).</summary>
        public static int ServerAvenged { get; private set; }

        /// <summary>Server: <paramref name="fallen"/> just went down to <paramref name="byId"/>. Mark it for the others.</summary>
        public static void ServerVengeance(PlayerLife fallen, ulong byId)
        {
            if (fallen == null || PlayerLife.All.Count < 2) return;
            var net = NetworkManager.Singleton;
            if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(byId, out var o)) return;
            if (!o.TryGetComponent<Enemies.EnemyBrain>(out var brain) || brain.IsDead || brain is Enemies.BossBrain) return;
            var h = brain.GetComponent<Health>();
            if (h == null || !h.IsAlive) return;
            if (grudges.Count > 64) grudges.Clear();
            grudges[o.NetworkObjectId] = new Grudge { Fallen = fallen, Until = Time.time + VengeanceSeconds };
            h.ServerMark(VengeanceSeconds);
            World.DungeonDirector.Instance?.ServerFeedAll("AVENGE " + fallen.name + ": what downed them is MARKED", new Color(1f, 0.45f, 0.35f));
        }

        private static void ServerCheckAvenged(Health target, ulong killerId)
        {
            if (!grudges.TryGetValue(target.NetworkObjectId, out var g)) return;
            grudges.Remove(target.NetworkObjectId);
            var net = NetworkManager.Singleton;
            if (g.Fallen == null || Time.time > g.Until || net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(killerId, out var o)) return;
            if (!o.TryGetComponent<PlayerLife>(out var avenger) || avenger == g.Fallen) return;
            ServerAvenged++;
            if (o.TryGetComponent<Ultimates>(out var u)) u.ServerAddCharge(AvengeCharge);
            bool down = g.Fallen.Downed.Value && !g.Fallen.Soul.Value;
            if (down) g.Fallen.Bleed.Value = Mathf.Max(0f, g.Fallen.Bleed.Value - AvengeBleedBack);
            World.DungeonDirector.Instance?.ServerFeedAll(avenger.name + " AVENGED " + g.Fallen.name + (down ? " - their bleeding slows" : ""), new Color(1f, 0.6f, 0.4f));
            if (o.TryGetComponent<Teamwork>(out var tw)) tw.AvengedRpc(target.Center + Vector3.up * 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AvengedRpc(Vector3 at) => UI.GameHud.ShowCombo(at, "AVENGED", new Color(1f, 0.55f, 0.4f));

        // ---- floor honours (server) ----

        private struct Tally { public int Kills, Dealt, Taken, Revives, Downs, Assists; }
        private static readonly Dictionary<ulong, Tally> floorStart = new Dictionary<ulong, Tally>();

        private static Tally Now(PlayerLife p)
        {
            var pr = p.GetComponent<PlayerProgress>();
            var tw = p.GetComponent<Teamwork>();
            return new Tally { Kills = pr.Kills.Value, Dealt = pr.DamageDealt.Value, Taken = pr.DamageTaken.Value, Revives = pr.Revives.Value, Downs = pr.Downs.Value, Assists = tw != null ? tw.ServerAssists : 0 };
        }

        /// <summary>Server: a floor began; remember everyone's tallies.</summary>
        public static void ServerFloorStarted()
        {
            floorStart.Clear();
            grudges.Clear();
            foreach (var p in PlayerLife.All) if (p != null) floorStart[p.NetworkObjectId] = Now(p);
        }

        /// <summary>Server: the floor's guardian fell; hand out the honours. Returns the lines (tests read them).</summary>
        public static List<string> ServerFloorHonours()
        {
            var lines = new List<string>();
            if (PlayerLife.All.Count < 2) return lines;
            var rows = new List<(PlayerLife p, Tally t)>();
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var n = Now(p);
                floorStart.TryGetValue(p.NetworkObjectId, out var s);
                rows.Add((p, new Tally { Kills = n.Kills - s.Kills, Dealt = n.Dealt - s.Dealt, Taken = n.Taken - s.Taken, Revives = n.Revives - s.Revives, Downs = n.Downs - s.Downs, Assists = n.Assists - s.Assists }));
            }
            void Best(string title, System.Func<Tally, int> of, string unit)
            {
                (PlayerLife p, int v) top = (null, 0);
                foreach (var r in rows) if (of(r.t) > top.v) top = (r.p, of(r.t));
                if (top.p != null) lines.Add(title + ": " + top.p.name + " (" + top.v + " " + unit + ")");
            }
            Best("BUTCHER", t => t.Kills, "kills");
            Best("HEAVY HAND", t => t.Dealt, "damage");
            Best("BULWARK", t => t.Taken, "damage taken");
            Best("GUARDIAN ANGEL", t => t.Revives, "revives");
            Best("WINGMAN", t => t.Assists, "assists");
            Best("FLOOR INSPECTOR", t => t.Downs, "times down");
            var dir = World.DungeonDirector.Instance;
            if (dir != null && lines.Count > 0)
            {
                dir.ServerFeedAll("FLOOR HONOURS", new Color(1f, 0.85f, 0.45f));
                foreach (var l in lines) dir.ServerFeedAll(l, new Color(1f, 0.92f, 0.7f));
            }
            return lines;
        }

        [Rpc(SendTo.Owner)]
        private void AssistRpc(Vector3 at) => UI.GameHud.ShowCombo(at, "ASSIST", new Color(0.6f, 0.85f, 1f));

        /// <summary>Server: <paramref name="saviour"/> got <paramref name="saved"/> up from a last stand.</summary>
        public static void ServerClutch(PlayerLife saviour, PlayerLife saved)
        {
            if (saviour == null || saved == null) return;
            if (saviour.TryGetComponent<Teamwork>(out var tw)) tw.ServerClutches++;
            if (saviour.TryGetComponent<Ultimates>(out var u)) u.ServerAddCharge(ClutchCharge);
            var nm = NetworkManager.Singleton;
            double until = nm.ServerTime.Time + Emotes.BuffSeconds;
            if (saviour.TryGetComponent<Emotes>(out var a)) a.BrothersUntil.Value = until;
            if (saved.TryGetComponent<Emotes>(out var b)) b.BrothersUntil.Value = until;
            World.DungeonDirector.Instance?.ServerFeedAll(saviour.name + " CLUTCHED " + saved.name + " UP - Brothers in Arms", new Color(1f, 0.8f, 0.35f));
            if (saviour.TryGetComponent<Teamwork>(out var t)) t.ClutchFxRpc((saviour.transform.position + saved.transform.position) / 2f + Vector3.up * 1.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ClutchFxRpc(Vector3 at)
        {
            UI.GameHud.ShowCombo(at, "CLUTCH!", new Color(1f, 0.8f, 0.35f));
            Vfx.Embers(at, 16, new Color(1f, 0.8f, 0.35f), 2f, 0.05f, 1.2f);
            Sfx.Play(Sound.Cleared, at, 0.7f, 1.1f);
        }
    }
}
