using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// A PRISONER IN A CAGE (2026-11-08, dungeon interactables: "a caged adventurer begs to be freed ... fights beside
    /// you for the floor, or turns out to be a trap"). An iron cage in a side room, a ragged delver kneeling inside who
    /// calls out when you come near. F breaks the lock:
    /// <list type="bullet">
    /// <item>Most of the time (<see cref="TrueChance"/>) they are who they say: a FREED DELVER on your side
    /// (<see cref="Team.Allies"/>) who follows the party for the rest of the floor and cuts at the nearest of the dead.
    /// The dead's blows can kill them. A name tag and a health bar float over them.</item>
    /// <item>Otherwise it was bait: the thing in the cage was a ghoul in a dead man's coat, and it and a friend come for you.</item>
    /// </list>
    /// The body is this NetworkObject (its health and collider), moved by a server-only NavMeshAgent and synced as
    /// <see cref="Pos"/> / <see cref="Yaw"/>; the cage is a local prop left where it stood.
    /// </summary>
    public class PrisonerCage : Interactable
    {
        public const float TrueChance = 0.82f, SeekRange = 12f, LeashRange = 16f, Reach = 2.3f, SwingEvery = 1.15f;
        public enum State : byte { Caged, Free, Dead, Bait }

        public readonly NetworkVariable<State> Now = new NetworkVariable<State>();
        public readonly NetworkVariable<Vector3> Pos = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<float> Yaw = new NetworkVariable<float>();
        public readonly NetworkVariable<FixedString64Bytes> Name = new NetworkVariable<FixedString64Bytes>();

        private static readonly string[] Names = { "Sir Edric the Bound", "Old Maud", "Brother Fennick", "Tamsin Hale", "Garrow Penniless", "Wick of the Fens", "Ser Hollis", "Ada Cartwright" };
        private Health health;
        private NavMeshAgent agent;
        private HumanoidRig rig;
        private Transform body, cage, door;
        private float nextThink, nextSwing, nextCall;
        private Health target;
        private bool wasNear;

        public override float Range => 2.4f;
        public bool IsFree => Now.Value == State.Free;
        public string DisplayName => Name.Value.ToString();

        private void Awake() => health = GetComponent<Health>();

        public override string Prompt(PlayerProgress who) => Now.Value == State.Caged ? "Break the cage's lock  -  free " + DisplayName + "?" : null;

        protected override void ServerUse(PlayerProgress who) => ServerOpen(Random.value < TrueChance);

        /// <summary>Server: the lock breaks (forced outcome in tests).</summary>
        public void ServerOpen(bool honest)
        {
            if (Now.Value != State.Caged) return;
            if (!honest)
            {
                Now.Value = State.Bait;
                var d = DungeonDirector.Instance;
                int depth = DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0;
                d?.ServerDebugSpawn("ghoul", transform.position + transform.forward * 0.8f, true);
                if (depth >= 3) d?.ServerDebugSpawn("ghoul", transform.position + transform.forward * 1.4f + transform.right, false);
                BaitRpc();
                NetworkObject.Despawn();
                return;
            }
            Now.Value = State.Free;
            float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
            health.ServerSetMax((240f + 120f * tier) * Mathf.Max(1f, 0.7f + 0.3f * PlayerLife.All.Count), refill: true);
            var start = transform.position + transform.forward * 1.1f;
            if (NavMesh.SamplePosition(start, out var hit, 3f, NavMesh.AllAreas)) start = hit.position;
            transform.position = start;
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f; agent.height = 1.8f; agent.speed = 4.6f; agent.acceleration = 20f; agent.angularSpeed = 540f; agent.stoppingDistance = 1.4f;
            agent.Warp(start);
            FreedRpc();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                if (Name.Value.Length == 0) Name.Value = Names[Random.Range(0, Names.Length)];
                Pos.Value = transform.position;
                Yaw.Value = transform.eulerAngles.y;
                health.Died += _ => ServerFall();
                // Caged, nothing reaches them (the dead pass a prisoner by).
                health.IncomingModifiers.Add((dmg, from) => Now.Value == State.Free ? dmg : 0f);
            }
            BuildVisual();
        }

        public override void OnNetworkDespawn()
        {
            if (cage != null) Destroy(cage.gameObject, 30f);
            base.OnNetworkDespawn();
        }

        private void ServerFall()
        {
            if (Now.Value != State.Free) return;
            Now.Value = State.Dead;
            if (agent != null) agent.enabled = false;
            FellRpc();
            Invoke(nameof(Gone), 5f);
        }

        private void Gone() { if (IsSpawned) NetworkObject.Despawn(); }

        // ---- Server: follow the party, cut at the dead ----
        private void Update()
        {
            if (IsServer && Now.Value == State.Free && agent != null && agent.enabled && agent.isOnNavMesh) Think();
            Present();
        }

        private void Think()
        {
            Pos.Value = transform.position;
            if (agent.velocity.sqrMagnitude > 0.05f) Yaw.Value = Quaternion.LookRotation(agent.velocity).eulerAngles.y;
            if (Time.time < nextThink) return;
            nextThink = Time.time + 0.2f;
            // The leader: the nearest standing player.
            PlayerLife leader = null; float best = float.MaxValue;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < best) { best = d; leader = p; }
            }
            if (leader == null) { agent.isStopped = true; return; }
            agent.isStopped = false;
            if (best > 40f) { agent.Warp(leader.transform.position - leader.transform.forward * 2f); return; }
            // The nearest of the dead near me and not far from the party; never a sleeping guardian.
            target = null; float tBest = SeekRange;
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || e.IsDead) continue;
                if (e is BossBrain b && b.Dormant) continue;
                float d = Vector3.Distance(e.transform.position, transform.position);
                if (d < tBest && Vector3.Distance(e.transform.position, leader.transform.position) < LeashRange) { tBest = d; target = e.GetComponent<Health>(); }
            }
            if (target != null)
            {
                agent.stoppingDistance = Reach * 0.75f;
                agent.SetDestination(target.transform.position);
                if (tBest <= Reach + 0.3f && Time.time >= nextSwing)
                {
                    nextSwing = Time.time + SwingEvery;
                    var to = target.transform.position - transform.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f) { transform.rotation = Quaternion.LookRotation(to); Yaw.Value = transform.eulerAngles.y; }
                    SwingRpc();
                    Invoke(nameof(Strike), 0.28f);
                }
            }
            else
            {
                agent.stoppingDistance = 2.2f;
                var side = Vector3.Cross(Vector3.up, leader.transform.forward).normalized;
                agent.SetDestination(leader.transform.position - leader.transform.forward * 1.6f + side * 1.4f);
            }
        }

        private void Strike()
        {
            if (Now.Value != State.Free) return;
            float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
            float dmg = (16f + 9f * tier) * Difficulty.DepthHealth(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0) * 0.8f;
            foreach (var victim in CombatQuery.Arc(transform.position + Vector3.up * 1f, transform.forward, Reach + 0.4f, 110f, Team.Enemies))
                victim.Tag("Freed delver").ServerDamage(dmg, transform.position + Vector3.up, NetworkObjectId);
        }

        // ---- Every peer ----
        [Rpc(SendTo.ClientsAndHost)] private void SwingRpc() { if (rig != null) rig.Play(RigAction.Swing, 0.35f); Sfx.Play(Sound.Swing, transform.position + Vector3.up, 0.6f, 1f); }

        [Rpc(SendTo.ClientsAndHost)]
        private void FreedRpc()
        {
            if (door != null) door.localRotation = Quaternion.Euler(0f, 100f, 0f);
            Sfx.Play(Sound.Chain, transform.position + Vector3.up, 1f, 1f);
            if (rig != null) rig.PlayEmote(Emote.None, 0f);
            UI.GameHud.Feed(DisplayName + " is free: \"I'll cut a way out with you. Lead on.\"", new Color(0.6f, 0.9f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BaitRpc()
        {
            Sfx.Play(Sound.Growl, transform.position + Vector3.up, 1f, 0.9f);
            UI.GameHud.Feed("It was bait - the thing in the cage was never a prisoner!", new Color(1f, 0.45f, 0.4f));
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "TRAP!", new Color(1f, 0.4f, 0.35f));
            if (door != null) door.localRotation = Quaternion.Euler(0f, 100f, 0f);
            if (body != null) body.gameObject.SetActive(false);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FellRpc()
        {
            if (rig != null) rig.Play(RigAction.Death, 5f);
            UI.GameHud.Feed(DisplayName + " falls. \"Go on... without me.\"", new Color(0.8f, 0.6f, 0.55f));
        }

        private void Present()
        {
            if (body == null) return;
            if (!IsServer && Now.Value == State.Free)
            {
                transform.position = Vector3.Lerp(transform.position, Pos.Value, 1f - Mathf.Exp(-12f * Time.deltaTime));
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, Yaw.Value, 0f), 1f - Mathf.Exp(-10f * Time.deltaTime));
            }
            // Caged: they call out to the first player who comes near.
            if (Now.Value == State.Caged && Time.time >= nextCall)
            {
                var me = NetworkPlayer.Local;
                bool near = me != null && Vector3.Distance(me.transform.position, transform.position) < 9f;
                if (near && !wasNear)
                {
                    nextCall = Time.time + 25f;
                    UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.3f, "\"Please... the lock!\"", new Color(0.85f, 0.85f, 0.8f));
                    Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 1.4f, 0.8f, 1.2f);
                }
                wasNear = near;
            }
        }

        private void OnGUI()
        {
            if (Now.Value != State.Free || health == null) return;
            var cam = ViewCamera.Current;
            var me = NetworkPlayer.Local;
            if (cam == null || me == null || Vector3.Distance(me.transform.position, transform.position) > 24f) return;
            useGUILayout = false;
            UI.UiSkin.Apply();
            var p = UI.UiScale.World(cam, transform.position + Vector3.up * 2.2f);
            if (p.z < 0f) return;
            float w = 110f;
            GUI.Label(new Rect(p.x - 100f, p.y - 34f, 200f, 18f), DisplayName, new GUIStyle(UI.UiSkin.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = new Color(0.6f, 0.9f, 1f) } });
            UI.UiSkin.Rect(new Rect(p.x - w / 2f, p.y - 14f, w, 6f), new Color(0f, 0f, 0f, 0.6f));
            UI.UiSkin.Rect(new Rect(p.x - w / 2f, p.y - 14f, w * health.Fraction, 6f), new Color(0.45f, 0.8f, 1f));
        }

        private void BuildVisual()
        {
            // The prisoner: a knight's frame in rags (grey), kneeling with bowed head while caged.
            body = new GameObject("Prisoner").transform;
            body.SetParent(transform, false);
            rig = CharacterArt.BuildKnight(body, new Color(0.38f, 0.36f, 0.33f));
            if (Now.Value == State.Caged) rig.PlayEmote(Emote.Kneel, 9999f);
            if (Now.Value == State.Dead || Now.Value == State.Bait) body.gameObject.SetActive(false);
            // The cage: a standalone prop, so it stays when the delver walks out.
            cage = new GameObject("PrisonCage").transform;
            cage.position = transform.position;
            cage.rotation = transform.rotation;
            MeshKit.Part(cage, "Floor", MeshKit.Cylinder(0.85f, 0.85f, 0.08f, 12), Mat.Steel, Vector3.zero);
            MeshKit.Part(cage, "Roof", MeshKit.Lathe("cageRoof", new[] { new Vector2(0.88f, 0f), new Vector2(0.2f, 0.45f), new Vector2(0f, 0.5f) }, 12), Mat.Steel, Vector3.up * 2.25f);
            MeshKit.Part(cage, "Ring", MeshKit.Cylinder(0.12f, 0.12f, 0.12f, 6), Mat.Steel, Vector3.up * 2.75f);
            for (int i = 0; i < 14; i++)
            {
                float a = i * (360f / 14f);
                if (a > 330f || a < 30f) continue; // the door's gap, facing out
                MeshKit.Part(cage, "Bar", MeshKit.Cylinder(0.025f, 0.025f, 2.25f, 5), Mat.Steel, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.82f));
            }
            door = MeshKit.Pivot(cage, "Door", Quaternion.Euler(0f, -30f, 0f) * new Vector3(0f, 0f, 0.82f));
            for (int i = 0; i < 4; i++)
                MeshKit.Part(door, "DoorBar", MeshKit.Cylinder(0.025f, 0.025f, 2.2f, 5), Mat.Steel, new Vector3(0.18f * i + 0.05f, 0.04f, 0f));
            MeshKit.Part(door, "Lock", MeshKit.Box(new Vector3(0.12f, 0.14f, 0.06f)), Mat.Gold, new Vector3(0.6f, 1.1f, 0.03f));
            if (Now.Value != State.Caged) door.localRotation = Quaternion.Euler(0f, 100f, 0f);
            MeshKit.MergeParts(cage);
        }
    }
}
