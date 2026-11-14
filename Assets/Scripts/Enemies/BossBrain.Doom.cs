using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// SHARED DOOM (2026-10-29, co-op content): with two or more standing, every boss now and then lays a doom on one
    /// player - a dark ring that follows them for a few seconds and then bursts. Its weight is split between everyone
    /// standing inside it: alone the marked one takes all of it, with a friend beside them each takes half. The party
    /// runs TO the marked player instead of away. Never cast on a lone player (solo play is unchanged), never in tests
    /// unless a test asks.
    /// </summary>
    public abstract partial class BossBrain
    {
        public const float DoomRadius = 3f, DoomTellSeconds = 3.2f, DoomWeight = 1.8f;
        public static readonly Color DoomColor = new Color(0.62f, 0.25f, 0.9f);

        private float nextDoom = -1f;
        private ulong lastDoomed = ulong.MaxValue;

        /// <summary>Tests: dooms laid, and how many shared the last one.</summary>
        public int DoomsCast { get; private set; }
        public static int LastDoomShared { get; private set; }

        private void TickDoom()
        {
            if (World.DungeonDirector.TestMode || target == null || scaledDepth < 1 || Dormant || Immune || CurrentStance == Stance.Transition || StandingParty < 2) return;
            if (nextDoom < 0f) { nextDoom = Time.time + 20f; return; }
            if (Time.time < nextDoom) return;
            nextDoom = Time.time + Mathf.Lerp(30f, 20f, Difficulty.Lead(scaledDepth));
            ServerDoomNow(null);
        }

        /// <summary>Server: lay a doom now (on <paramref name="on"/>, or a standing player not doomed last time).</summary>
        public void ServerDoomNow(PlayerLife on, float tell = -1f)
        {
            if (on == null)
            {
                var pool = new List<PlayerLife>();
                foreach (var p in PlayerLife.All) if (p != null && p.Targetable) pool.Add(p);
                if (pool.Count == 0) return;
                if (pool.Count > 1) pool.RemoveAll(p => p.NetworkObjectId == lastDoomed);
                on = pool[Random.Range(0, pool.Count)];
            }
            lastDoomed = on.NetworkObjectId;
            DoomsCast++;
            if (tell < 0f) tell = Tell(DoomTellSeconds);
            DoomMarkRpc(on.NetworkObjectId, tell);
            StartCoroutine(DoomFall(on, tell));
        }

        private System.Collections.IEnumerator DoomFall(PlayerLife on, float tell)
        {
            yield return new WaitForSeconds(tell);
            if (IsDead || on == null) yield break;
            var at = on.transform.position;
            int n = ServerResolveDoom(at, damage * DoomWeight, NetworkObjectId);
            DoomBurstRpc(at, (byte)n);
        }

        /// <summary>Server: burst a doom of <paramref name="total"/> damage at a spot, split evenly between the standing players
        /// inside <see cref="DoomRadius"/>. Returns how many shared it.</summary>
        public static int ServerResolveDoom(Vector3 at, float total, ulong by)
        {
            var inside = new List<PlayerLife>();
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                var d = p.transform.position - at;
                if (Mathf.Abs(d.y) > 3f) continue;
                d.y = 0f;
                if (d.magnitude <= DoomRadius + 0.3f) inside.Add(p);
            }
            LastDoomShared = inside.Count;
            if (inside.Count == 0) return 0;
            float share = total / inside.Count;
            foreach (var p in inside)
            {
                p.Health.ServerDamage(share, at + Vector3.up, by);
                var push = p.transform.position - at;
                push.y = 0f;
                KnockPlayer(p.Health, (push.sqrMagnitude > 0.01f ? push.normalized * 2.5f : Vector3.zero) + Vector3.up * 3f);
            }
            return inside.Count;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DoomMarkRpc(ulong targetId, float seconds)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj)) return;
            DoomMark.Attach(obj.transform, DoomRadius, seconds, DoomColor);
            var me = NetworkPlayer.Local;
            bool mine = me != null && me.gameObject == obj.gameObject;
            string who = Health.NameOf(targetId);
            if (mine)
            {
                UI.GameHud.ShowCombo(obj.transform.position + Vector3.up * 2.4f, UI.GameHud.FirstTime("doom") ? "DOOM ON YOU - STAND WITH YOUR PARTY" : "DOOM", new Color(0.8f, 0.55f, 1f));
                Sfx.Play(Sound.Summon, obj.transform.position, 0.7f, 0.6f);
            }
            else UI.GameHud.Feed("DOOM on " + who + (UI.GameHud.FirstTime("doomshare") ? " - stand inside the ring to share it" : ""), new Color(0.8f, 0.55f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DoomBurstRpc(Vector3 at, byte shared)
        {
            Vfx.Ring(at + Vector3.up * 0.05f, DoomRadius, DoomColor, 0.5f);
            Vfx.Flash(at + Vector3.up, DoomColor, 3f, 8f, 0.3f);
            Vfx.Embers(at + Vector3.up * 0.4f, 14, DoomColor, 5f, 0.06f, 0.6f);
            Sfx.Play(Sound.Slam, at, 0.8f, 0.7f);
            string text = shared >= 2 ? "SHARED x" + shared : shared == 1 ? "BORNE ALONE" : "";
            if (text.Length > 0) UI.GameHud.ShowCombo(at + Vector3.up * 2.2f, text, shared >= 2 ? new Color(0.75f, 0.95f, 0.8f) : new Color(1f, 0.5f, 0.45f));
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 8f) local.AddShake(0.3f);
        }
    }
}
