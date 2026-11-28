using System.Collections.Generic;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        public const float TripBlastRadius = 3.6f, TripBlastShare = 0.5f, TripClearance = 0.12f;

        public readonly NetworkVariable<ulong> TripSprung = new NetworkVariable<ulong>();

        private readonly Dictionary<(int wire, ulong player), float> tripSide = new Dictionary<(int, ulong), float>();

        public static bool TripsWire(bool high, float feetAboveFloor, bool crouched)
        {
            if (high) return !crouched || feetAboveFloor > TripClearance;
            return feetAboveFloor < DungeonLayout.TripLowHeight + TripClearance;
        }

        private void ResetTripWires()
        {
            tripSide.Clear();
            if (IsServer) TripSprung.Value = 0;
        }

        private void TickTripWires()
        {
            if (TestMode || Layout == null || Layout.TripWires.Count == 0 || Phase.Value != RunPhase.Exploring) return;
            float half = DungeonLayout.CorridorWidth / 2f;
            for (int i = 0; i < Layout.TripWires.Count && i < 64; i++)
            {
                if ((TripSprung.Value & (1UL << i)) != 0) continue;
                var t = Layout.TripWires[i];
                var along = Vector3.Cross(t.Across, Vector3.up);
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || p.Downed.Value) continue;
                    var rel = p.transform.position - t.At;
                    if (Mathf.Abs(Vector3.Dot(rel, t.Across)) > half || Mathf.Abs(rel.y) > 2.5f) continue;
                    float side = Vector3.Dot(rel, along);
                    var key = (i, p.NetworkObjectId);
                    bool crossed = tripSide.TryGetValue(key, out var was) && Mathf.Sign(was) != Mathf.Sign(side) && Mathf.Abs(was - side) < 2f;
                    tripSide[key] = side;
                    if (!crossed || !TripsWire(t.High, rel.y, p.Crouched.Value)) continue;
                    ServerSpringTrip(i, p);
                    break;
                }
            }
        }

        public void ServerSpringTrip(int i, PlayerLife by)
        {
            if (Layout == null || i < 0 || i >= Layout.TripWires.Count || i >= 64 || (TripSprung.Value & (1UL << i)) != 0) return;
            TripSprung.Value |= 1UL << i;
            var t = Layout.TripWires[i];
            var at = t.At + Vector3.up * 0.5f;
            foreach (var h in CombatQuery.Circle(at, TripBlastRadius, Team.Players))
            {
                h.Unavoidable().ServerDamage(TrapBite(h) * (TripBlastShare / TrapHealthShare), at);
                var push = h.transform.position - at;
                push.y = 0f;
                if (h.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(push.normalized * 11f + Vector3.up * 5f);
            }
            foreach (var h in CombatQuery.Circle(at, TripBlastRadius, Team.Enemies))
                h.ServerDamage(TrapHurt(120f + 30f * Tier(Layout.Depth)), at);
            OilSlick.ServerIgniteAt(at, ulong.MaxValue, TripBlastRadius);
            ServerNoise(by != null ? by.Health : null, at, 18f);
            TripBlastRpc(at);
            Progression.RunLog.Note("tripwire", t.High ? "high" : "low", by);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TripBlastRpc(Vector3 at)
        {
            Art.Sfx.Play(Art.Sound.Slam, at, 1f, 0.9f);
            var fire = new Color(1f, 0.55f, 0.2f);
            Art.Vfx.Flash(at + Vector3.up, fire, 12f, 12f, 0.7f);
            Art.Vfx.Fireball(at, TripBlastRadius * 0.7f, fire);
            Art.FxSprite.Scorch(Art.Vfx.Floor(at), TripBlastRadius * 0.7f);
            Art.Vfx.Embers(at, 18, fire, 7f, 0.08f, 0.9f);
            Art.Vfx.Smoke(at, 5, 0.8f, 2.2f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 16f) local.AddShake(0.45f);
        }
    }
}
