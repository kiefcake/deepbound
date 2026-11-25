using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace Deepbound.World
{
    public class SoulRift : NetworkBehaviour
    {
        public const float Interval = 5f, PulseLead = 1.1f, SpawnTaken = 1.5f, BaseHealth = 110f;
        public const int AliveCap = 3, TotalCap = 9;
        private const float FirstSpawnDelay = 2.2f, ArtHeight = 1.3f, WispEvery = 0.18f;

        public static readonly List<SoulRift> All = new List<SoulRift>();

        public readonly NetworkVariable<double> NextAt = new NetworkVariable<double>();
        public readonly NetworkVariable<int> Made = new NetworkVariable<int>();

        private static readonly Color Violet = new Color(0.55f, 0.25f, 1f);

        private readonly List<EnemyBrain> mine = new List<EnemyBrain>();
        private NetworkObject spawnPrefab;
        private int players = 1, depth;
        private Health health;
        private float vulnerableUntil, nextWisp;
        private bool closed;
        private Transform disc, rim, ringB, shards;
        private Renderer discR;
        private Light glow;

        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

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
                health.ServerSetMax(BaseHealth * Difficulty.DepthHealth(depth) * Difficulty.PartyHealth(players, false), refill: true);
                health.IncomingModifiers.Add((dmg, from) => Time.time < vulnerableUntil ? dmg * SpawnTaken : dmg);
                health.Died += _ => ServerClose();
                NextAt.Value = Now + FirstSpawnDelay;
            }
            BuildArt();
            PlayOpening();
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
            if (obj.TryGetComponent<EnemyBrain>(out var b))
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
            if (closed || !IsSpawned) return;
            closed = true;
            CloseFxRpc(transform.position);
            NetworkObject.Despawn();
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
            if (LocalPlayerWithin(at, 24f)) UI.GameHud.Feed("The rift tears shut.", new Color(0.75f, 0.6f, 1f));
        }

        private static bool LocalPlayerWithin(Vector3 at, float r)
        {
            var me = Player.NetworkPlayer.Local;
            return me != null && (me.transform.position - at).sqrMagnitude < r * r;
        }

        private void PlayOpening()
        {
            Sfx.Play(Sound.Summon, transform.position + Vector3.up, 0.9f, 0.7f);
            Vfx.Ring(transform.position + Vector3.up * 0.05f, 2f, Violet, 0.8f);
            if (LocalPlayerWithin(transform.position, 30f))
                UI.Tips.OfferNow("rift", "A NECROMANCER'S RIFT: it spits out the dead every few seconds and stays open after its caster falls. Rush it and break it - it is weakest just after something steps out.");
        }

        private static void Unshadowed(Component part) => part.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        private void BuildArt()
        {
            var frame = MeshKit.Pivot(transform, "Rift", Vector3.up * ArtHeight);
            frame.localScale = new Vector3(1f, 1.55f, 1f);
            var face = new Vector3(90f, 0f, 0f);

            disc = MeshKit.Part(frame, "Disc", MeshKit.Cylinder(0.8f, 0.8f, 0.04f, 24), Mat.Glow, Vector3.zero, face).transform;
            discR = disc.GetComponent<Renderer>();
            Unshadowed(disc);
            ArtLibrary.SetGlow(discR, Violet * 1.4f);

            var core = MeshKit.Part(frame, "Void", MeshKit.Ball(0.6f, 20, 12), Mat.Dark, Vector3.zero, Vector3.zero, new Vector3(1f, 1f, 0.14f));
            ArtLibrary.Tint(core.GetComponent<Renderer>(), new Color(0.015f, 0f, 0.03f));
            Unshadowed(core.transform);

            var ringMesh = MeshKit.Lathe("riftRing", new[] { new Vector2(0.84f, 0f), new Vector2(0.9f, 0.012f), new Vector2(0.96f, 0f) }, 28, false);
            rim = MeshKit.Pivot(frame, "RingA", Vector3.zero);
            var ra = MeshKit.Part(rim, "Ring", ringMesh, Mat.Glow, Vector3.zero, face, new Vector3(1f, 3f, 1f));
            ArtLibrary.SetGlow(ra.GetComponent<Renderer>(), new Color(0.95f, 0.6f, 1f) * 1.8f);
            ringB = MeshKit.Pivot(frame, "RingB", Vector3.zero);
            var rb = MeshKit.Part(ringB, "Ring", ringMesh, Mat.Glow, Vector3.zero, face, new Vector3(1.14f, 3f, 1.14f));
            ArtLibrary.SetGlow(rb.GetComponent<Renderer>(), new Color(0.3f, 0.1f, 0.6f) * 1.6f);
            Unshadowed(ra.transform);
            Unshadowed(rb.transform);

            var rng = new System.Random((int)(NetworkObjectId * 7919 + 11));
            for (int i = 0; i < 9; i++)
            {
                float a = i * 40f + (float)rng.NextDouble() * 22f, len = 0.25f + (float)rng.NextDouble() * 0.4f;
                var dir = Quaternion.Euler(0f, 0f, a) * Vector3.up;
                var crack = MeshKit.Part(frame, "Crack", MeshKit.Box(new Vector3(0.025f, len, 0.02f)), Mat.Glow, dir * (0.9f + len / 2f), new Vector3(0f, 0f, a));
                ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), Violet * (0.9f + (float)rng.NextDouble()));
                Unshadowed(crack.transform);
            }

            shards = MeshKit.Pivot(transform, "Shards", Vector3.up * ArtHeight);
            var shard = MeshKit.Box(new Vector3(0.07f, 0.22f, 0.05f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                MeshKit.Part(shards, "Shard", shard, Mat.Bone, Quaternion.Euler(0f, 0f, a) * new Vector3(0f, 1.55f, 0f) * (i % 2 == 0 ? 1f : 0.85f), new Vector3(0f, 0f, a + 20f));
            }

            glow = new GameObject("RiftLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * ArtHeight;
            glow.type = LightType.Point;
            glow.color = Violet;
            glow.range = 6.5f;
            glow.intensity = 2.5f;
            glow.shadows = LightShadows.None;

            FxSprite.Scorch(transform.position + Vector3.up * 0.02f, 1.4f);
        }

        private void AnimateArt()
        {
            if (disc == null) return;
            rim.localRotation = Quaternion.Euler(0f, 0f, Time.time * 40f);
            ringB.localRotation = Quaternion.Euler(0f, 0f, -Time.time * 22f);
            shards.localRotation = Quaternion.Euler(0f, 0f, Time.time * 18f);
            double left = NextAt.Value - Now;
            float pulse = left < PulseLead && left > 0 ? 1f - (float)(left / PulseLead) : 0f;
            float k = 1f + 0.06f * Mathf.Sin(Time.time * 6f) + 0.3f * pulse;
            disc.localScale = new Vector3(k, 1f, k);
            ArtLibrary.SetGlow(discR, Violet * (1.2f + 1.6f * pulse));
            if (glow != null) glow.intensity = 2.2f + 0.4f * Mathf.Sin(Time.time * 5f) + 3f * pulse;
            if (Time.time < nextWisp) return;
            nextWisp = Time.time + WispEvery;
            var off = Quaternion.AngleAxis(Random.value * 360f, transform.forward) * transform.up;
            off = new Vector3(off.x * 1.2f, off.y * 1.9f, off.z * 1.2f);
            Vfx.Embers(transform.position + Vector3.up * ArtHeight + off, 1, Color.Lerp(Violet, Color.white, 0.3f), 0.5f, 0.04f, 0.6f);
        }
    }
}
