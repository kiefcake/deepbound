using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// LAIRS (2026-11-14, the user: "create 5 new mini bosses and make them all have unique fight sequences"): a mini boss
    /// asleep in a sealed side lair mid-floor, one per act (<see cref="World.DungeonDirector"/> Lair). Whoever walks in is shut
    /// in with it and it wakes scaled to them - so it is a fight for one or for four (the user: "doable solo and in a team").
    /// It takes the boss bar only while it is fought (<see cref="Engaged"/>), and leaves a lair hoard when it dies.
    /// Echoes (a shadow twin, mirror copies) are spawned from the same prefab with a <see cref="Role"/>: they never die
    /// as a boss (no bar, no last words, no loot) - they shatter and despawn.
    /// </summary>
    public abstract class LairBrain : BossBrain
    {
        /// <summary>Any peer: true while the lair is sealed and this is fought.</summary>
        public readonly NetworkVariable<bool> Engaged = new NetworkVariable<bool>();
        /// <summary>0 the boss itself, 1 a shadow twin, 2 a mirror copy.</summary>
        public readonly NetworkVariable<byte> Role = new NetworkVariable<byte>();
        public bool IsEcho => Role.Value != 0;

        protected override bool ClaimsActiveOnSpawn => false;
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.LairCleared;

        /// <summary>Server: the lair (set by the director).</summary>
        public Vector3 RoomCenter { get; set; }
        public Vector2 RoomSize { get; set; } = new Vector2(16f, 16f);
        protected float RoomRadius => Mathf.Max(4f, Mathf.Min(RoomSize.x, RoomSize.y) / 2f - 2f);

        /// <summary>The boss this echo belongs to (server).</summary>
        protected LairBrain Master;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Engaged.OnValueChanged += (_, now) => { if (!IsEcho) ClaimActive(now ? this : null); };
            Role.OnValueChanged += (_, now) => OnRoleChanged();
            if (Engaged.Value && !IsEcho) ClaimActive(this);
            if (IsEcho) OnRoleChanged();
            if (IsServer)
                health.IncomingModifiers.Insert(0, (dmg, from) =>
                {
                    if (!IsEcho || dmg >= KillDamage) return dmg;
                    EchoHit(dmg, from);
                    return 0f;
                });
        }

        /// <summary>Server: the lair is sealed on whoever came in; wake, scaled to them.</summary>
        public void ServerEngage(int inside, int depth)
        {
            ServerScaleBoss(Mathf.Max(1, inside), depth);
            Dormant = false;
            Engaged.Value = true;
            ClaimActive(this);
            ServerEntrance();
            OnEngaged();
        }

        protected virtual void OnEngaged() { }

        /// <summary>Any peer: the boss bar is ours only while we are fought and alive - otherwise hand it back.</summary>
        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (Active == this && !IsEcho && (IsDead || !Engaged.Value)) ClaimActive(null);
        }

        protected override void OnDied(Health h)
        {
            base.OnDied(h);
            if (!IsServer) return;
            Engaged.Value = false;
            ClaimActive(null);
            foreach (var e in echoes.ToArray()) if (e != null && e.IsSpawned) e.Shatter(false);
            echoes.Clear();
        }

        // ---- Echoes ----

        protected readonly List<LairBrain> echoes = new List<LairBrain>();

        /// <summary>Server: an echo of this boss (its own prefab, given by the director as the minion prefab).</summary>
        public int EchoCount => echoes.Count;
        protected LairBrain SpawnEcho(byte role, Vector3 at)
        {
            if (minionPrefab == null) return null;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 3f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            var obj = World.DungeonDirector.ServerSpawnEntering(minionPrefab, at, transform.rotation, 0f, 0f);
            var echo = obj.GetComponent<LairBrain>();
            echo.NoLoot = true;
            echo.Role.Value = role;
            echo.Master = this;
            echo.Dormant = false;
            echo.RoomCenter = RoomCenter;
            echo.RoomSize = RoomSize;
            echo.ServerScale(scaledPlayers, scaledDepth);
            echo.ServerAggroNearest();
            echoes.Add(echo);
            return echo;
        }

        /// <summary>Server: an echo was struck (it takes no damage itself).</summary>
        protected virtual void EchoHit(float dmg, Vector3 from) { }

        /// <summary>Server: an echo breaks apart and is gone.</summary>
        public void Shatter(bool backlash)
        {
            if (!IsServer || !IsSpawned) return;
            ShatterFxRpc(transform.position + Vector3.up * 1.2f, backlash);
            if (backlash)
                foreach (var p in CombatQuery.Circle(transform.position, 3.2f, Team.Players))
                    p.ServerDamage(damage * 0.8f, transform.position + Vector3.up, NetworkObjectId);
            if (Master != null) Master.echoes.Remove(this);
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShatterFxRpc(Vector3 at, bool backlash)
        {
            Sfx.Play(Sound.Shatter, at, 1f, backlash ? 0.8f : 1.2f);
            Vfx.Embers(at, 22, backlash ? new Color(0.85f, 0.75f, 1f) : new Color(0.5f, 0.45f, 0.6f), 5f, 0.07f, 0.8f);
            if (backlash) Vfx.Ring(at - Vector3.up * 1.15f, 3.2f, new Color(0.85f, 0.75f, 1f), 0.4f);
        }

        /// <summary>Any peer: an echo's look (darker, no shadow for a copy).</summary>
        protected virtual void OnRoleChanged() { }

        protected Vector3 RandomInRoom(float margin)
        {
            float r = Mathf.Max(1f, RoomRadius - margin);
            var p = RoomCenter + new Vector3(Random.Range(-r, r), 0f, Random.Range(-r, r));
            return UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : RoomCenter;
        }

        /// <summary>Server: hit every player along a lane (the body's edge counts).</summary>
        protected int LaneHit(Vector3 from, Vector3 to, float width, float dmg)
        {
            int n = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                if (p.Health.SegmentTouchesBody(from, to, width)) { p.Health.ServerDamage(dmg, from, NetworkObjectId); n++; }
            }
            return n;
        }

        protected IEnumerable<PlayerLife> Standing()
        {
            foreach (var p in PlayerLife.All) if (p != null && p.Targetable) yield return p;
        }

        protected PlayerLife NearestStanding(Vector3 from)
        {
            PlayerLife best = null; float bd = float.MaxValue;
            foreach (var p in Standing()) { float d = Vector3.Distance(p.transform.position, from); if (d < bd) { bd = d; best = p; } }
            return best;
        }

        public void Warp(Vector3 to)
        {
            if (agent != null && agent.enabled) agent.Warp(to); else transform.position = to;
        }

        protected static Renderer TintPart(GameObject g, Color c) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, c); return r; }
        protected static Renderer GlowPart(GameObject g, Color c) { var r = g.GetComponent<Renderer>(); ArtLibrary.SetGlow(r, c); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return r; }
    }
}
