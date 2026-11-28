using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public abstract class LairBrain : BossBrain
    {
        public readonly NetworkVariable<bool> Engaged = new NetworkVariable<bool>();

        public readonly NetworkVariable<byte> Role = new NetworkVariable<byte>();
        public bool IsEcho => Role.Value != 0;

        protected override bool ClaimsActiveOnSpawn => false;
        protected override bool Hunts => !IsEcho;
        protected override bool Dooms => !IsEcho;
        protected override float HuntDamageScale => 0.5f;
        protected override bool Reaches(PlayerLife p) => p != null && InRoom(p.transform.position, 1.5f);

        public bool InRoom(Vector3 at, float slack) =>
            Mathf.Abs(at.x - RoomCenter.x) <= RoomSize.x / 2f + slack && Mathf.Abs(at.z - RoomCenter.z) <= RoomSize.y / 2f + slack;
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.LairCleared;

        public Vector3 RoomCenter { get; set; }
        public Vector2 RoomSize { get; set; } = new Vector2(16f, 16f);
        protected float RoomRadius => Mathf.Max(4f, Mathf.Min(RoomSize.x, RoomSize.y) / 2f - 2f);

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
                    if (!shattered) EchoHit(dmg, from);
                    return 0f;
                });
        }

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

        protected override Vector3 ChaseDestination(float distanceToTarget)
        {
            var p = base.ChaseDestination(distanceToTarget);
            float hx = Mathf.Max(1f, RoomSize.x / 2f - 1.2f), hz = Mathf.Max(1f, RoomSize.y / 2f - 1.2f);
            p.x = Mathf.Clamp(p.x, RoomCenter.x - hx, RoomCenter.x + hx);
            p.z = Mathf.Clamp(p.z, RoomCenter.z - hz, RoomCenter.z + hz);
            return p;
        }

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

        protected readonly List<LairBrain> echoes = new List<LairBrain>();

        public int EchoCount => echoes.Count;
        protected LairBrain SpawnEcho(byte role, Vector3 at)
        {
            if (minionPrefab == null) return null;
            at = World.DropSpots.Stand(transform.position, at, 0.4f, 1.2f);
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

        protected virtual void EchoHit(float dmg, Vector3 from) { }

        private bool shattered;

        public void Shatter(bool backlash)
        {
            if (!IsServer || !IsSpawned || shattered) return;
            shattered = true;
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

        protected virtual void OnRoleChanged() { }

        protected Vector3 RandomInRoom(float margin)
        {
            float r = Mathf.Max(1f, RoomRadius - margin);
            var p = RoomCenter + new Vector3(Random.Range(-r, r), 0f, Random.Range(-r, r));
            return UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : RoomCenter;
        }

        protected int LaneHit(Vector3 from, Vector3 to, float width, float dmg, HashSet<PlayerLife> struck = null)
        {
            int n = 0;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable || !Reaches(p) || (struck != null && struck.Contains(p))) continue;
                if (!p.Health.SegmentTouchesBody(from, to, width)) continue;
                p.Health.ServerDamage(dmg, from, NetworkObjectId);
                struck?.Add(p);
                n++;
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
