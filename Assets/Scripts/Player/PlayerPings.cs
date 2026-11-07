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
    /// Pings: the co-op shorthand. Tap middle mouse (or Z) to mark what you are aiming at for the
    /// whole party — an enemy (the marker follows it), a piece of loot (named), or a spot.
    /// Hold it for the ping wheel (League's semantic pings): DANGER up, ON MY WAY right,
    /// NEED HELP down (pinned to you), LOOK left. Downed, a tap calls for help.
    /// Each player has one ping at a time, lasting six seconds.
    /// </summary>
    public class PlayerPings : NetworkBehaviour
    {
        public enum Kind : byte { Spot, Enemy, Loot, Danger, OnMyWay, Help, Look }

        /// <summary>The wheel, clockwise from the top.</summary>
        private static readonly Kind[] Wheel = { Kind.Danger, Kind.OnMyWay, Kind.Help, Kind.Look };
        public static string WheelName(Kind k) => k == Kind.Danger ? "DANGER" : k == Kind.OnMyWay ? "ON MY WAY" : k == Kind.Help ? "NEED HELP" : k == Kind.Look ? "LOOK" : "";
        public static Color KindColor(Kind k) => k == Kind.Danger ? new Color(1f, 0.25f, 0.2f) : k == Kind.OnMyWay ? new Color(0.35f, 0.7f, 1f)
            : k == Kind.Help ? new Color(1f, 0.6f, 0.15f) : k == Kind.Look ? new Color(1f, 0.88f, 0.4f) : Color.white;

        private const float HoldForWheel = 0.22f;
        private bool debugHold;
        private float heldSince = -1f;
        private Vector2 wheelAim;
        private bool wheelOpen;
        public static bool WheelOpenLocal { get; private set; }

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
            if (!IsOwner || debugHold) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            // Pinging is gone (2026-11-07, the user: "remove pinging entirely"); its key is the give wheel's now.
            bool pressed = false, held = false;
            var motor = GetComponent<FirstPersonMotor>();
            var life = GetComponent<PlayerLife>();

            // Downed: a tap calls for help, pinned to you.
            if (life != null && life.Downed.Value)
            {
                CloseWheel();
                if (pressed && Time.time >= nextPing) { nextPing = Time.time + Cooldown; PingRpc(transform.position, NetworkObjectId, Kind.Help, ""); }
                return;
            }
            if (!motor.ActionsReady) { CloseWheel(); heldSince = -1f; return; }

            if (pressed) { heldSince = Time.time; wheelAim = Vector2.zero; }
            if (heldSince >= 0f && held)
            {
                if (!wheelOpen && Time.time - heldSince >= HoldForWheel) { wheelOpen = true; WheelOpenLocal = true; FirstPersonMotor.LookHeld = true; }
                if (wheelOpen && mouse != null) wheelAim = Vector2.ClampMagnitude(wheelAim + mouse.delta.ReadValue(), 120f);
                return;
            }
            if (heldSince < 0f) return;
            // Released.
            bool fromWheel = wheelOpen;
            var choice = WheelChoice();
            heldSince = -1f;
            CloseWheel();
            if (Time.time < nextPing) return;
            if (fromWheel)
            {
                if (choice == Kind.Spot) return; // let go in the middle: cancelled
                nextPing = Time.time + Cooldown;
                if (choice == Kind.Help) { PingRpc(transform.position, NetworkObjectId, Kind.Help, ""); return; }
                PingRpc(AimPoint(), 0, choice, "");
                return;
            }
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

        /// <summary>Screenshots: show the wheel with the mouse pushed toward <paramref name="aim"/>.</summary>
        public void DebugShowWheel(bool on, Vector2 aim)
        {
            wheelOpen = on;
            WheelOpenLocal = on;
            wheelAim = aim;
            debugHold = on;
        }

        private void CloseWheel()
        {
            if (!wheelOpen) return;
            wheelOpen = false;
            WheelOpenLocal = false;
            FirstPersonMotor.LookHeld = false;
        }

        /// <summary>The slice the mouse has pushed toward (Spot = still in the middle).</summary>
        private Kind WheelChoice()
        {
            if (wheelAim.magnitude < 30f) return Kind.Spot;
            float ang = Mathf.Atan2(wheelAim.x, wheelAim.y) * Mathf.Rad2Deg; // 0 = up, 90 = right
            int slice = Mathf.RoundToInt(((ang + 360f) % 360f) / 90f) % 4;
            return Wheel[slice];
        }

        private Vector3 AimPoint()
        {
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            return player.AimRay(Range, out var hit) ? hit.point : origin + dir * 20f;
        }

        /// <summary>Owner (tests): a semantic ping as if chosen on the wheel.</summary>
        public void PingKind(Kind kind, Vector3 point)
        {
            if (IsOwner) PingRpc(point, kind == Kind.Help ? NetworkObjectId : 0, kind, "");
        }

                private void OnGUI()
        {
            Deepbound.UI.UiTimer.Begin();
            OnGUIBody();
            Deepbound.UI.UiTimer.Mark("PingWheel");
        }

        private void OnGUIBody()
        {
            useGUILayout = false; // no GUILayout here: skip the layout pass (perf)
            if (!IsOwner || !wheelOpen) return;
            float w = UI.UiScale.W, h = UI.UiScale.H;
            var c = new Vector2(w / 2f, h / 2f);
            var choice = WheelChoice();
            UI.UiSkin.Apply();
            GUI.depth = -15; // above the HUD's cards
            UI.UiSkin.Rect(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.3f));
            for (int i = 0; i < Wheel.Length; i++)
            {
                var k = Wheel[i];
                float a = i * 90f * Mathf.Deg2Rad;
                var pos = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * 110f;
                bool on = k == choice;
                var col = KindColor(k);
                UI.UiSkin.Rect(new Rect(pos.x - 70, pos.y - 20, 140, 40), new Color(0f, 0f, 0f, on ? 0.9f : 0.75f));
                if (on) UI.UiSkin.Rect(new Rect(pos.x - 70, pos.y + 18, 140, 3), col);
                GUI.Label(new Rect(pos.x - 70, pos.y - 20, 140, 40), WheelName(k),
                    new GUIStyle(UI.UiSkin.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = on ? col : new Color(col.r * 0.8f + 0.15f, col.g * 0.8f + 0.15f, col.b * 0.8f + 0.15f, 0.85f) } });
            }
            // Where the mouse has pushed.
            var dot = c + new Vector2(wheelAim.x, -wheelAim.y) * 0.7f;
            UI.UiSkin.Rect(new Rect(dot.x - 4, dot.y - 4, 8, 8), Color.white);
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

            bool semantic = kind >= Kind.Danger;
            var color = semantic ? KindColor(kind) : player.SlotColor;
            var marker = BuildMarker(point, color, kind);
            Active[NetworkObjectId] = new Ping
            {
                Position = point, Follow = follow, Kind = kind, Color = color, Until = Time.time + Lifetime, Marker = marker,
                Label = kind == Kind.Enemy ? "ENEMY" : kind == Kind.Loot ? label : semantic ? WheelName(kind) + "  (" + name + ")" : "HERE",
            };
            // Each kind sounds different, so you know what it is before you look.
            if (kind == Kind.Danger) Sfx.Play2D(Sound.Bash, 0.7f);
            else if (kind == Kind.Help) Sfx.Play2D(Sound.WarCry, 0.45f);
            else Sfx.Play(kind == Kind.Enemy ? Sound.Bash : Sound.Purchase, point, 0.6f, kind == Kind.Enemy ? 1.8f : kind == Kind.OnMyWay ? 1.1f : 1.4f);
            string what = kind == Kind.Enemy ? " marked an enemy" : kind == Kind.Loot ? " spotted " + label : kind == Kind.Danger ? ": Danger here!"
                : kind == Kind.OnMyWay ? ": On my way." : kind == Kind.Help ? ": I need help!" : kind == Kind.Look ? ": Look here." : " pinged a spot";
            UI.GameHud.Feed(name + what, color);
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
            if (Time.time > p.Until || ((p.Kind == Kind.Enemy || p.Kind == Kind.Loot || p.Kind == Kind.Help) && p.Follow == null))
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
            float height = kind == Kind.Enemy ? 3.5f : kind == Kind.Danger ? 7f : kind == Kind.Help ? 4f : 5f;
            var beam = MeshKit.Part(root.transform, "Beam", MeshKit.Cylinder(kind == Kind.Danger ? 0.07f : 0.04f, 0.01f, height, 6), Mat.Glow, Vector3.zero);
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
