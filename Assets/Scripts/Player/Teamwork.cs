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
    /// </summary>
    public class Teamwork : NetworkBehaviour
    {
        public const float PincerWindow = 2.5f, PincerAngle = 120f, PincerMultiplier = 1.15f, PincerReach = 25f;
        public const float AssistWindow = 6f, AssistCharge = 3f;
        public const float LastStandDamage = 1.25f, LastStandTaken = 0.8f, ClutchCharge = 15f;

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

        private void Update()
        {
            if (!IsOwner) return;
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
