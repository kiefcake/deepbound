using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public class PuzzleStatue : Interactable
    {
        public static new readonly List<PuzzleStatue> All = new List<PuzzleStatue>();
        public readonly NetworkVariable<byte> Facing = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Target = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> Group = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<bool> Solved = new NetworkVariable<bool>();

        private Transform body;
        private Renderer[] eyes;
        private float turnFrom, turnTo, turnStart = -10f;
        private float busyUntil;
        public const float TurnSeconds = 0.6f, RaiseChance = 0.2f;

        public override float Range => 2.4f;
        public bool Watching => Facing.Value == Target.Value;

        public override string Prompt(PlayerProgress who) => Solved.Value ? null : "Turn the watcher  (" + (Watching ? "its eyes burn - it sees the sigil" : "it looks away from the sigil") + ")";

        protected override void ServerUse(PlayerProgress who) => ServerTurn();

        public void ServerTurn(bool force = false)
        {
            if (Solved.Value || (!force && Time.time < busyUntil)) return;
            busyUntil = Time.time + TurnSeconds;
            Facing.Value = (byte)((Facing.Value + 1) % 4);
            TurnRpc();

            if (!DungeonDirector.TestMode && DungeonDirector.Layout != null && DungeonDirector.Layout.Depth >= 1 && Random.value < RaiseChance)
            {
                var room = DungeonDirector.Layout.Rooms.Count > Group.Value && Group.Value >= 0 ? DungeonDirector.Layout.Rooms[Group.Value] : null;
                var at = room != null ? room.Center + (transform.position - room.Center).normalized * 1.2f : transform.position + transform.forward * 2f;
                DungeonDirector.Instance?.ServerDebugSpawn("skeleton", at, false);
            }
            CheckGroup();
        }

        private void CheckGroup()
        {
            var group = All.FindAll(s => s != null && s.IsSpawned && s.Group.Value == Group.Value);
            if (group.Count < 4 || !group.TrueForAll(s => s.Watching)) return;
            foreach (var s in group) s.Solved.Value = true;
            foreach (var room in PuzzleRoom.Rooms) if (room != null && room.RoomId.Value == Group.Value) room.ServerSolve();
        }

        public static float YawOf(byte facing) => facing * 90f;

        [Rpc(SendTo.ClientsAndHost)]
        private void TurnRpc()
        {
            turnFrom = body != null ? body.localEulerAngles.y : 0f;
            turnTo = YawOf(Facing.Value);
            turnStart = Time.time;
            Sfx.Play(Sound.Creak, transform.position + Vector3.up, 1f, 0.55f);
            Vfx.Smoke(transform.position + Vector3.up * 0.2f, 2, 0.5f, 1.2f);
        }

        private void Update()
        {
            if (body == null) return;
            float t = Mathf.Clamp01((Time.time - turnStart) / TurnSeconds);
            float yaw = t < 1f ? Mathf.LerpAngle(turnFrom, turnTo, Mathf.SmoothStep(0f, 1f, t)) : YawOf(Facing.Value);
            body.localRotation = Quaternion.Euler(0f, yaw, 0f);
            bool lit = Watching || Solved.Value;
            if (eyes != null)
                foreach (var e in eyes) ArtLibrary.SetGlow(e, lit ? new Color(1f, 0.75f, 0.3f) * 1.6f : new Color(0.12f, 0.12f, 0.15f) * 0.3f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);

            var stone = new Color(0.58f, 0.57f, 0.55f);
            var dark = new Color(0.42f, 0.41f, 0.4f);
            void Part(Transform parent, string n, Mesh m, Vector3 at, Color c, Vector3 euler = default)
            {
                var go = MeshKit.Part(parent, n, m, Mat.Marble, at, euler);
                ArtLibrary.Tint(go.GetComponent<Renderer>(), c);
            }
            Part(transform, "Plinth", MeshKit.Box(new Vector3(0.95f, 0.4f, 0.95f)), Vector3.up * 0.2f, dark);
            Part(transform, "PlinthLip", MeshKit.Box(new Vector3(1.05f, 0.08f, 1.05f)), Vector3.up * 0.42f, dark);
            body = MeshKit.Pivot(transform, "Watcher", Vector3.up * 0.46f);

            Part(body, "Skirt", MeshKit.Lathe("watcherSkirt", new[] { new Vector2(0.34f, 0f), new Vector2(0.3f, 0.45f), new Vector2(0.25f, 0.85f), new Vector2(0f, 0.86f) }, 10), Vector3.zero, stone);
            Part(body, "Chest", MeshKit.Box(new Vector3(0.52f, 0.5f, 0.32f)), new Vector3(0f, 1.08f, 0f), stone);
            Part(body, "Shoulders", MeshKit.Box(new Vector3(0.7f, 0.16f, 0.36f)), new Vector3(0f, 1.3f, 0f), dark);
            Part(body, "Belt", MeshKit.Box(new Vector3(0.54f, 0.07f, 0.34f)), new Vector3(0f, 0.86f, 0f), dark);

            Part(body, "Neck", MeshKit.Cylinder(0.09f, 0.09f, 0.1f, 8), new Vector3(0f, 1.38f, 0f), dark);
            Part(body, "Helm", MeshKit.Cylinder(0.16f, 0.15f, 0.32f, 10), new Vector3(0f, 1.47f, 0f), stone);
            Part(body, "HelmTop", MeshKit.Cylinder(0.16f, 0.16f, 0.03f, 10), new Vector3(0f, 1.79f, 0f), dark);
            Part(body, "Visor", MeshKit.Box(new Vector3(0.24f, 0.05f, 0.04f)), new Vector3(0f, 1.62f, 0.145f), new Color(0.12f, 0.12f, 0.13f));

            foreach (float sx in new[] { -1f, 1f })
                Part(body, "Arm", MeshKit.Box(new Vector3(0.11f, 0.48f, 0.12f)), new Vector3(sx * 0.17f, 1.06f, 0.2f), stone, new Vector3(-28f, 0f, -sx * 22f));
            Part(body, "Hands", MeshKit.Box(new Vector3(0.2f, 0.1f, 0.12f)), new Vector3(0f, 0.86f, 0.36f), stone);
            Part(body, "Pommel", MeshKit.Ball(0.05f, 6, 4), new Vector3(0f, 0.96f, 0.37f), dark);
            Part(body, "Grip", MeshKit.Box(new Vector3(0.04f, 0.16f, 0.04f)), new Vector3(0f, 0.84f, 0.37f), dark);
            Part(body, "Guard", MeshKit.Box(new Vector3(0.34f, 0.05f, 0.06f)), new Vector3(0f, 0.75f, 0.37f), dark);
            Part(body, "Blade", MeshKit.Box(new Vector3(0.08f, 0.72f, 0.025f)), new Vector3(0f, 0.38f, 0.37f), stone);
            eyes = new Renderer[2];
            for (int i = 0; i < 2; i++)
            {
                var e = MeshKit.Part(body, "Eye", MeshKit.Box(new Vector3(0.05f, 0.022f, 0.02f)), Mat.Glow, new Vector3(i == 0 ? -0.055f : 0.055f, 1.62f, 0.168f));
                e.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                eyes[i] = e.GetComponent<Renderer>();
            }
            body.localRotation = Quaternion.Euler(0f, YawOf(Facing.Value), 0f);
        }

        public override void OnNetworkDespawn() { All.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }
    }
}
