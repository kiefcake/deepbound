using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// Pings: the co-op shorthand. Middle mouse (or Z) marks what you are aiming at for the
    /// whole party — an enemy (the marker follows it), a piece of loot (named), or a spot.
    /// Each player has one ping at a time, in their slot colour, lasting six seconds.
    /// </summary>
    public class PlayerPings : NetworkBehaviour
    {
        public enum Kind : byte { Spot, Enemy, Loot }

        public struct Ping
        {
            public Vector3 Position;
            public Transform Follow;
            public Kind Kind;
            public string Label;
            public Color Color;
            public float Until;
            public GameObject Marker;
        }

        private const float Lifetime = 6f, Range = 80f, Cooldown = 0.4f;

        /// <summary>Live pings on this peer, keyed by the pinging player's object id.</summary>
        public static readonly Dictionary<ulong, Ping> Active = new Dictionary<ulong, Ping>();

        private float nextPing;
        private NetworkPlayer player;

        private void Awake() => player = GetComponent<NetworkPlayer>();

        public override void OnNetworkDespawn() => Clear(NetworkObjectId);

        private void Update()
        {
            TickMarkers();
            if (!IsOwner) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool pressed = (mouse != null && mouse.middleButton.wasPressedThisFrame) || (keyboard != null && keyboard.zKey.wasPressedThisFrame);
            if (!pressed || Time.time < nextPing || !GetComponent<FirstPersonMotor>().HasControl) return;
            nextPing = Time.time + Cooldown;

            var origin = player.AimOrigin;
            var dir = player.AimForward;
            ulong targetId = 0;
            var kind = Kind.Spot;
            Vector3 point = origin + dir * Range;
            string label = "";

            // Aim assist: an enemy or loot near the crosshair counts even if the ray misses it.
            float best = 4f;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var to = h.Center - origin;
                if (to.magnitude > Range) continue;
                float angle = Vector3.Angle(dir, to);
                if (angle < best && CombatQuery.ClearLine(origin, h)) { best = angle; targetId = h.NetworkObjectId; kind = Kind.Enemy; point = h.Center; }
            }
            if (kind == Kind.Spot)
            {
                best = 5f;
                foreach (var drop in LootDrop.All)
                {
                    if (drop == null) continue;
                    var to = drop.transform.position + Vector3.up * 0.4f - origin;
                    if (to.magnitude > 30f) continue;
                    float angle = Vector3.Angle(dir, to);
                    if (angle < best) { best = angle; kind = Kind.Loot; point = drop.transform.position; label = drop.Kind.Value == LootKind.Relic ? Relics.Name((Relic)drop.Amount.Value) : LootInfo.Name(drop.Kind.Value); targetId = drop.NetworkObjectId; }
                }
            }
            if (kind == Kind.Spot && Physics.Raycast(origin, dir, out var hit, Range, ~0, QueryTriggerInteraction.Ignore)) point = hit.point;

            PingRpc(point, targetId, kind, label);
        }

        /// <summary>Owner: ping a spot (the smoke test uses this; players use the key).</summary>
        public void PingSpot(Vector3 point)
        {
            if (IsOwner) PingRpc(point, 0, Kind.Spot, "");
        }

        [Rpc(SendTo.Server)]
        private void PingRpc(Vector3 point, ulong targetId, Kind kind, string label) => PingFxRpc(point, targetId, kind, label);

        [Rpc(SendTo.ClientsAndHost)]
        private void PingFxRpc(Vector3 point, ulong targetId, Kind kind, string label)
        {
            Clear(NetworkObjectId);
            Transform follow = null;
            if (targetId != 0 && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out var obj)) follow = obj.transform;

            var color = player.SlotColor;
            var marker = BuildMarker(point, color, kind);
            Active[NetworkObjectId] = new Ping
            {
                Position = point, Follow = follow, Kind = kind, Color = color, Until = Time.time + Lifetime, Marker = marker,
                Label = kind == Kind.Enemy ? "ENEMY" : kind == Kind.Loot ? label : "HERE",
            };
            Sfx.Play(kind == Kind.Enemy ? Sound.Bash : Sound.Purchase, point, 0.6f, kind == Kind.Enemy ? 1.8f : 1.4f);
            UI.GameHud.Feed(name + (kind == Kind.Enemy ? " marked an enemy" : kind == Kind.Loot ? " spotted " + label : " pinged a spot"), color);
        }

        private static void Clear(ulong id)
        {
            if (Active.TryGetValue(id, out var old) && old.Marker != null) Object.Destroy(old.Marker);
            Active.Remove(id);
        }

        /// <summary>Moves markers with what they follow and retires expired ones.</summary>
        private void TickMarkers()
        {
            if (!Active.TryGetValue(NetworkObjectId, out var p)) return;
            if (Time.time > p.Until || (p.Kind != Kind.Spot && p.Follow == null))
            {
                Clear(NetworkObjectId);
                return;
            }
            if (p.Follow != null)
            {
                p.Position = p.Follow.position;
                if (p.Marker != null) p.Marker.transform.position = p.Position;
                Active[NetworkObjectId] = p;
            }
        }

        /// <summary>A thin beam of the player's colour and a ring on the ground.</summary>
        private static GameObject BuildMarker(Vector3 at, Color color, Kind kind)
        {
            var root = new GameObject("Ping");
            root.transform.position = at;
            var beam = MeshKit.Part(root.transform, "Beam", MeshKit.Cylinder(0.04f, 0.01f, kind == Kind.Enemy ? 3.5f : 5f, 6), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), color * 2f);
            var ring = MeshKit.Part(root.transform, "Ring", MeshKit.Lathe("pingRing", new[]
            {
                new Vector2(0.5f, 0f), new Vector2(0.58f, 0.02f), new Vector2(0.5f, 0.04f),
            }, 20, false), Mat.Glow, Vector3.up * 0.03f);
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), color * 1.8f);
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Object.Destroy(root, Lifetime + 0.5f);
            return root;
        }
    }
}
