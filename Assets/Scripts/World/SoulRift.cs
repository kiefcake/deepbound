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
            // 2026-11-13 shots: a flat violet disc read as a cheap sticker. Now: a tall tear (fixed ellipse) with a black
            // void at its heart, a hot rim and two rings turning against each other, cracks running out into the air,
            // bone shards circling, its own light and wisps drawn in toward the void.
            var frame = MeshKit.Pivot(transform, "Rift", Vector3.up * 1.3f);
            frame.localScale = new Vector3(1f, 1.55f, 1f);
            var face = new Vector3(90f, 0f, 0f);
            disc = MeshKit.Part(frame, "Disc", MeshKit.Cylinder(0.8f, 0.8f, 0.04f, 24), Mat.Glow, Vector3.zero, face).transform;
            discR = disc.GetComponent<Renderer>();
            discR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(discR, Violet * 1.4f);
            var core = MeshKit.Part(frame, "Void", MeshKit.Ball(0.6f, 20, 12), Mat.Dark, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.14f));   // a flattened ball: the cylinder caps showed pie slices
            ArtLibrary.Tint(core.GetComponent<Renderer>(), new Color(0.015f, 0f, 0.03f));
            core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ringMesh = MeshKit.Lathe("riftRing", new[] { new Vector2(0.84f, 0f), new Vector2(0.9f, 0.012f), new Vector2(0.96f, 0f) }, 28, false);
            rim = MeshKit.Pivot(frame, "RingA", Vector3.zero);
            var ra = MeshKit.Part(rim, "Ring", ringMesh, Mat.Glow, Vector3.zero, face, new Vector3(1f, 3f, 1f));
            ArtLibrary.SetGlow(ra.GetComponent<Renderer>(), new Color(0.95f, 0.6f, 1f) * 1.8f);
            ringB = MeshKit.Pivot(frame, "RingB", Vector3.zero);
            var rb = MeshKit.Part(ringB, "Ring", ringMesh, Mat.Glow, Vector3.zero, face, new Vector3(1.14f, 3f, 1.14f));
            ArtLibrary.SetGlow(rb.GetComponent<Renderer>(), new Color(0.3f, 0.1f, 0.6f) * 1.6f);
            foreach (var r in new[] { ra, rb }) r.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Cracks in the air, out from the rim.
            var rng = new System.Random((int)(NetworkObjectId * 7919 + 11));
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f + (float)rng.NextDouble() * 22f, len = 0.25f + (float)rng.NextDouble() * 0.4f;
                var dir = Quaternion.Euler(0f, 0f, a) * Vector3.up;
                var crack = MeshKit.Part(frame, "Crack", MeshKit.Box(new Vector3(0.025f, len, 0.02f)), Mat.Glow, dir * (0.9f + len / 2f), new Vector3(0f, 0f, a));
                ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), Violet * (0.9f + (float)rng.NextDouble()));
                crack.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Bone shards circling the tear (outside the stretched frame, so they stay bone-shaped).
            shards = MeshKit.Pivot(transform, "Shards", Vector3.up * 1.3f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                MeshKit.Part(shards, "Shard", MeshKit.Box(new Vector3(0.07f, 0.22f, 0.05f)), Mat.Bone, Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 1.55f, 0f) * (i % 2 == 0 ? 1f : 0.85f), new Vector3(0f, 0f, a + 20f));
            }
            glow = new GameObject("RiftLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 1.3f;
            glow.type = LightType.Point;
            glow.color = Violet;
            glow.range = 6.5f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;
            // A scorched ring on the floor.
            FxSprite.Scorch(transform.position + Vector3.up * 0.02f, 1.4f);
        }

        private Transform ringB, shards;
        private Light glow;
        private float nextWisp;

        private void AnimateArt()
        {
            if (disc == null) return;
            rim.localRotation = Quaternion.Euler(0f, 0f, Time.time * 40f);
            ringB.localRotation = Quaternion.Euler(0f, 0f, -Time.time * 22f);
            shards.localRotation = Quaternion.Euler(0f, 0f, Time.time * 18f);
            // The pulse up to a spawn.
            double left = NextAt.Value - Now;
            float pulse = left < PulseLead && left > 0 ? 1f - (float)(left / PulseLead) : 0f;
            float k = 1f + 0.06f * Mathf.Sin(Time.time * 6f) + 0.3f * pulse;
            disc.localScale = new Vector3(k, 1f, k);
            ArtLibrary.SetGlow(discR, Violet * (1.2f + 1.6f * pulse));
            if (glow != null) glow.intensity = 2.2f + 0.4f * Mathf.Sin(Time.time * 5f) + 3f * pulse;
            // Wisps drawn in toward the void.
            if (Time.time >= nextWisp)
            {
                nextWisp = Time.time + 0.18f;
                var off = Quaternion.AngleAxis(Random.value * 360f, transform.forward) * transform.up;
                off = new Vector3(off.x * 1.2f, off.y * 1.9f, off.z * 1.2f);
                Vfx.Embers(transform.position + Vector3.up * 1.3f + off, 1, Color.Lerp(Violet, Color.white, 0.3f), 0.5f, 0.04f, 0.6f);
            }
        }
    }
}
