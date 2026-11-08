using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A STANDING MIRROR in the light-beam room (2026-11-08, dungeon puzzles: "rotate standing mirrors so a shaft of light
    /// reaches a crystal"). It stands on one cell of the room's grid (<see cref="PuzzleRoom"/>, kind Mirrors) on one of two
    /// diagonals; F turns it to the other. The beam is traced by the room on every peer from the mirrors' states; when it
    /// reaches the crystal the coffer opens.
    /// </summary>
    public class PuzzleMirror : Interactable
    {
        public static readonly List<PuzzleMirror> All = new List<PuzzleMirror>();
        public readonly NetworkVariable<int> Group = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<sbyte> GridX = new NetworkVariable<sbyte>();
        public readonly NetworkVariable<sbyte> GridR = new NetworkVariable<sbyte>();
        /// <summary>0 = '/' (along a+b), 1 = '\' (along a-b), in the room's grid.</summary>
        public readonly NetworkVariable<byte> State = new NetworkVariable<byte>();
        /// <summary>The world directions of the two diagonals (set by the room's spawner).</summary>
        public readonly NetworkVariable<Vector3> DiagSlash = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<Vector3> DiagBack = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<bool> Solved = new NetworkVariable<bool>();

        /// <summary>Server only: the state on the solution path (tests).</summary>
        public byte SolutionState { get; set; }

        private Transform glass;
        private Renderer faceRenderer;
        private bool shownSolved;
        // Dark silvered glass with a cold sheen (a bright glow read as a blank white board under the torches); gold once
        // the light is through.
        private static readonly Color GlassTint = new Color(0.16f, 0.2f, 0.26f);
        private static readonly Color GlassSheen = new Color(0.32f, 0.42f, 0.55f) * 0.16f;
        private static readonly Color GlassLit = new Color(1f, 0.78f, 0.4f) * 0.35f;
        private float busyUntil;
        public override float Range => 2.4f;

        public override string Prompt(PlayerProgress who) => Solved.Value ? null : "Turn the mirror  -  send the light on toward the crystal";

        protected override void ServerUse(PlayerProgress who) => ServerTurn();

        public void ServerTurn(bool force = false)
        {
            if (Solved.Value || (!force && Time.time < busyUntil)) return;
            busyUntil = Time.time + 0.35f;
            State.Value = (byte)(1 - State.Value);
            TurnRpc();
            foreach (var room in PuzzleRoom.Rooms) if (room != null && room.RoomId.Value == Group.Value) room.ServerCheckBeam();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TurnRpc() => Sfx.Play(Sound.Creak, transform.position + Vector3.up, 0.7f, 1.3f);

        private void Update()
        {
            if (glass == null) return;
            if (faceRenderer != null && Solved.Value != shownSolved)
            {
                shownSolved = Solved.Value;
                ArtLibrary.SetGlow(faceRenderer, shownSolved ? GlassLit : GlassSheen);
            }
            var d = State.Value == 0 ? DiagSlash.Value : DiagBack.Value;
            d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            var want = Yaw(d);
            glass.rotation = Quaternion.Slerp(glass.rotation, want, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }

        /// <summary>The yaw that lays the mirror's width (local x) along <paramref name="d"/> (flat).</summary>
        private static Quaternion Yaw(Vector3 d) => Quaternion.Euler(0f, Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg, 0f);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);
            // A heavy iron foot, and on it a tall framed mirror that turns about its upright.
            MeshKit.Part(transform, "Foot", MeshKit.Cylinder(0.32f, 0.36f, 0.12f, 10), Mat.Steel, Vector3.zero);
            glass = MeshKit.Pivot(transform, "Mirror", Vector3.up * 0.12f);
            MeshKit.Part(glass, "Post", MeshKit.Box(new Vector3(0.06f, 1.2f, 0.06f)), Mat.Steel, new Vector3(0f, 0.6f, -0.05f));
            MeshKit.Part(glass, "Frame", MeshKit.Box(new Vector3(1.0f, 1.25f, 0.06f)), Mat.Gold, new Vector3(0f, 1.0f, 0f));
            var face = MeshKit.Part(glass, "Glass", MeshKit.Box(new Vector3(0.88f, 1.12f, 0.08f)), Mat.Glow, new Vector3(0f, 1.0f, 0f));
            face.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            faceRenderer = face.GetComponent<Renderer>();
            ArtLibrary.Tint(faceRenderer, GlassTint);
            ArtLibrary.SetGlow(faceRenderer, GlassSheen);
            // A pale streak across the glass so it reads as a mirror, not a dark panel.
            var streak = MeshKit.Part(glass, "Sheen", MeshKit.Box(new Vector3(0.08f, 0.9f, 0.085f)), Mat.Glow, new Vector3(-0.2f, 1.02f, 0f), new Vector3(0f, 0f, -24f));
            streak.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.Tint(streak.GetComponent<Renderer>(), new Color(0.45f, 0.5f, 0.56f));
            ArtLibrary.SetGlow(streak.GetComponent<Renderer>(), new Color(0.5f, 0.58f, 0.68f) * 0.12f);
            var d = State.Value == 0 ? DiagSlash.Value : DiagBack.Value; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) glass.rotation = Yaw(d);
        }

        public override void OnNetworkDespawn() { All.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }
    }
}
