using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// A NECROMANCER'S RIFT (2026-11-13, the user: "Necromancer portals that stay open until destroyed, spitting out an
    /// enemy every few seconds. A clear target to rush."). A violet tear standing on the floor: every
    /// <see cref="Interval"/> seconds something steps out of it (through <see cref="DungeonDirector.ServerSpawnEntering"/>,
    /// portal-first), up to <see cref="AliveCap"/> of its own alive at once and <see cref="TotalCap"/> in all. It has a
    /// body (<see cref="Health"/>, Team.Enemies, so every attack hits it) and stays open after its caster falls: break it.
    /// A pulse runs up its edge before each spawn (the tell), and it takes x<see cref="SpawnTaken"/> in the second after.
    /// </summary>
    public class SoulRift : NetworkBehaviour
    {
        public const float Interval = 5f, PulseLead = 1.1f, SpawnTaken = 1.5f, BaseHealth = 110f;
        public const int AliveCap = 3, TotalCap = 9;

        /// <summary>Server time of the next spawn (everyone draws the pulse from it).</summary>
        public readonly NetworkVariable<double> NextAt = new NetworkVariable<double>();
        public readonly NetworkVariable<int> Made = new NetworkVariable<int>();

        private NetworkObject spawnPrefab;
        private int players = 1, depth;
        private readonly System.Collections.Generic.List<EnemyBrain> mine = new System.Collections.Generic.List<EnemyBrain>();
        private Health health;
        private float vulnerableUntil;
        private Transform disc, rim;
        private Renderer discR;
        private static readonly Color Violet = new Color(0.55f, 0.25f, 1f);

        public static readonly System.Collections.Generic.List<SoulRift> All = new System.Collections.Generic.List<SoulRift>();

        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        /// <summary>Server, before Spawn: what it calls through, for how big a party, how deep.</summary>
        public void Prepare(NetworkObject prefab, int party, int floorDepth)
        {
            spawnPrefab = prefab;
            players = Mathf.Max(1, party);
            depth = floorDepth;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            health = GetComponent<Health>();
            if (IsServer && health != null)
            {
                float hp = BaseHealth * Difficulty.DepthHealth(depth) * Difficulty.PartyHealth(players, false);
                health.ServerSetMax(hp, refill: true);
                health.IncomingModifiers.Add((dmg, from) => Time.time < vulnerableUntil ? dmg * SpawnTaken : dmg);
                health.Died += _ => ServerClose();
                NextAt.Value = Now + 2.2;
            }
            BuildArt();
            OpenFxRpcLocal();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        private void Update()
        {
            AnimateArt();
            if (!IsServer || health == null || !health.IsAlive) return;
            mine.RemoveAll(b => b == null || b.IsDead);
            if (Now < NextAt.Value) return;
            NextAt.Value = Now + Interval;
            if (spawnPrefab == null || mine.Count >= AliveCap || Made.Value >= TotalCap) return;
            var at = transform.position + transform.forward * 1.2f;
            if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
            var obj = DungeonDirector.ServerSpawnEntering(spawnPrefab, at, transform.rotation, 0f, 1f);
            if (obj == null) return;
            var b = obj.GetComponent<EnemyBrain>();
            if (b != null)
            {
                b.NoLoot = true;
                b.ServerScale(players, depth);
                b.ServerAggroNearest();
                mine.Add(b);
            }
            Made.Value++;
            vulnerableUntil = Time.time + 1f;
            SpewRpc();
        }

        private void ServerClose()
        {
            CloseFxRpc(transform.position);
            if (IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpewRpc()
        {
            Sfx.Play(Sound.Shade, transform.position + Vector3.up, 0.7f, 0.8f);
            Vfx.Flash(transform.position + Vector3.up * 1.2f, Violet, 1.6f, 4f, 0.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CloseFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Shatter, at + Vector3.up, 0.9f, 0.7f);
            Vfx.Ring(at + Vector3.up * 0.05f, 2.4f, Violet, 0.6f);
            Vfx.Embers(at + Vector3.up * 1.2f, 22, Violet, 4f, 0.08f, 1.2f);
            if (NetworkPlayerNear(at, 24f)) UI.GameHud.Feed("The rift tears shut.", new Color(0.75f, 0.6f, 1f));
        }

        private static bool NetworkPlayerNear(Vector3 at, float r)
        {
            var me = Player.NetworkPlayer.Local;
            return me != null && (me.transform.position - at).sqrMagnitude < r * r;
        }

        private void OpenFxRpcLocal()
        {
            Sfx.Play(Sound.Summon, transform.position + Vector3.up, 0.9f, 0.7f);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 2f, Violet, 0.8f);
            if (NetworkPlayerNear(transform.position, 30f)) UI.Tips.OfferNow("rift", "A NECROMANCER'S RIFT: it spits out the dead every few seconds and stays open after its caster falls. Rush it and break it - it is weakest just after something steps out.");
        }

        private void BuildArt()
        {
            var root = MeshKit.Pivot(transform, "Rift", Vector3.up * 1.25f);
            disc = MeshKit.Part(root, "Disc", MeshKit.Cylinder(0.8f, 0.8f, 0.04f, 22), Mat.Glow, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 1.55f)).transform;
            discR = disc.GetComponent<Renderer>();
            discR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(discR, Violet * 1.4f);
            rim = MeshKit.Part(root, "Rim", MeshKit.Cylinder(0.92f, 0.92f, 0.02f, 22), Mat.Glow, Vector3.forward * -0.03f, new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 1.55f)).transform;
            var rr = rim.GetComponent<Renderer>();
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(rr, new Color(0.12f, 0.04f, 0.25f) * 2f);
            // Bone shards orbiting the tear.
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72f;
                MeshKit.Part(root, "Shard", MeshKit.Box(new Vector3(0.07f, 0.24f, 0.05f)), Mat.Bone, Quaternion.Euler(0f, 0f, a) * Vector3.up * 1.15f, new Vector3(0f, 0f, a));
            }
            // A scorched ring on the floor.
            FxSprite.Scorch(transform.position + Vector3.up * 0.02f, 1.4f);
        }

        private void AnimateArt()
        {
            if (disc == null) return;
            var root = disc.parent;
            root.localRotation = Quaternion.Euler(0f, 0f, Time.time * 25f);
            // The pulse up to a spawn.
            double left = NextAt.Value - Now;
            float pulse = left < PulseLead && left > 0 ? 1f - (float)(left / PulseLead) : 0f;
            float k = 1f + 0.08f * Mathf.Sin(Time.time * 6f) + 0.35f * pulse;
            disc.localScale = new Vector3(k, k, 1.55f * k);
            ArtLibrary.SetGlow(discR, Violet * (1.2f + 1.6f * pulse));
        }
    }
}
