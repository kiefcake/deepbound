using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// The Tome of the Phoenix's bird (2026-10-24, the user: "summon a phoenix with a scroll, that will fight for us for
    /// 30 seconds scorching enemies flying around, rebirthing on death"). Server-driven: it wheels round its summoner
    /// and dives at the nearest of the dead, burning and scorching what it strikes (frost on them is then a Thermal
    /// Shock). It is on the players' side (<see cref="Team.Allies"/>): the dead's blows and bolts can bring it down,
    /// and then it bursts into flame, lies as a glowing egg for a moment and is reborn at full strength.
    /// Position is synced as a variable; every peer builds and flaps its own bird.
    /// </summary>
    public class Phoenix : NetworkBehaviour
    {
        public static readonly List<Phoenix> All = new List<Phoenix>();

        public readonly NetworkVariable<ulong> Summoner = new NetworkVariable<ulong>(ulong.MaxValue);
        public readonly NetworkVariable<Vector3> Pos = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<bool> Egg = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> EndsAt = new NetworkVariable<double>();

        public const float OrbitRadius = 2.6f, OrbitHeight = 2.8f, SeekRange = 14f, DiveSpeed = 16f, DiveEvery = 1.4f;
        public const float DiveDamage = 22f, BurstDamage = 30f, BurstRadius = 4f, EggSeconds = 3f, HealthShare = 0.3f;

        private Health health;
        private PlayerProgress owner;
        private Health diveTarget;
        private float nextDive, eggUntil, orbitAngle, maxHealth;
        private int rebirths;
        private Transform bird, egg, wingL, wingR;
        private Renderer eggR;
        private Light glow;
        private float nextEmber;

        public int Rebirths => rebirths;
        public bool Diving => diveTarget != null;

        private void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            BuildVisual();
            transform.position = Pos.Value;
            if (!IsServer) return;
            health.IncomingModifiers.Add((d, from) => Egg.Value ? 0f : d);
            health.Died += _ => ServerFall();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        /// <summary>Server: a fresh phoenix for <paramref name="who"/>.</summary>
        public void ServerInit(PlayerProgress who)
        {
            owner = who;
            Summoner.Value = who.NetworkObjectId;
            maxHealth = Mathf.Max(60f, who.GetComponent<Health>().Max.Value * HealthShare);
            health.ServerSetMax(maxHealth, refill: true);
            EndsAt.Value = NetworkManager.ServerTime.Time + RareItems.PhoenixSeconds;
            Pos.Value = who.transform.position + Vector3.up * OrbitHeight;
            transform.position = Pos.Value;
            nextDive = Time.time + 0.6f;
        }

        private void Update()
        {
            if (IsServer) ServerTick();
            // Everyone: glide to the synced spot and flap.
            var moveDir = Pos.Value - transform.position;
            transform.position = Vector3.Lerp(transform.position, Pos.Value, 1f - Mathf.Exp(-Time.deltaTime * 18f));
            if (moveDir.sqrMagnitude > 0.0004f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 1f - Mathf.Exp(-Time.deltaTime * 10f));
            if (bird != null)
            {
                bird.gameObject.SetActive(!Egg.Value);
                egg.gameObject.SetActive(Egg.Value);
                float flap = Mathf.Sin(Time.time * (Diving ? 22f : 12f)) * 40f;
                if (wingL != null) wingL.localRotation = Quaternion.Euler(0f, 0f, 20f + flap);
                if (wingR != null) wingR.localRotation = Quaternion.Euler(0f, 0f, -20f - flap);
                if (Egg.Value) ArtLibrary.SetGlow(eggR, new Color(1f, 0.55f, 0.2f) * (1.2f + 0.6f * Mathf.Sin(Time.time * 9f)));
                if (glow != null) glow.intensity = Egg.Value ? 0.8f : 1.6f;
                if (!Egg.Value && Time.time >= nextEmber)
                {
                    nextEmber = Time.time + 0.12f;
                    Vfx.Mote(transform.position + Vector3.down * 0.6f, new Color(1f, 0.55f, 0.15f));
                }
            }
        }

        private void ServerTick()
        {
            if (owner == null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(Summoner.Value, out var so)) owner = so.GetComponent<PlayerProgress>();
            if (owner == null || NetworkManager.ServerTime.Time >= EndsAt.Value) { ServerExpire(); return; }
            if (Egg.Value)
            {
                if (Time.time >= eggUntil) ServerReborn();
                return;
            }
            var home = owner.transform.position;
            if (diveTarget != null && (!diveTarget.IsAlive || Vector3.Distance(diveTarget.transform.position, home) > SeekRange + 6f)) diveTarget = null;
            if (diveTarget == null && Time.time >= nextDive) diveTarget = Seek(home);
            Vector3 want;
            if (diveTarget != null)
            {
                want = diveTarget.Center;
                var p = Vector3.MoveTowards(Pos.Value, want, DiveSpeed * Time.deltaTime);
                Pos.Value = p;
                if ((p - want).sqrMagnitude < 0.5f) ServerStrike();
                return;
            }
            orbitAngle += Time.deltaTime * 140f;
            want = home + Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward * OrbitRadius + Vector3.up * (OrbitHeight + Mathf.Sin(Time.time * 2f) * 0.3f);
            Pos.Value = Vector3.MoveTowards(Pos.Value, want, 12f * Time.deltaTime);
        }

        private Health Seek(Vector3 home)
        {
            Health best = null;
            float bd = SeekRange;
            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                if (h.TryGetComponent<Enemies.GargoyleBrain>(out var g) && !g.Woken.Value) continue;
                float d = Vector3.Distance(h.transform.position, home);
                if (d < bd && CombatQuery.ClearLine(Pos.Value, h)) { bd = d; best = h; }
            }
            return best;
        }

        private void ServerStrike()
        {
            var t = diveTarget;
            diveTarget = null;
            nextDive = Time.time + DiveEvery;
            if (t == null) return;
            float att = owner != null ? owner.AttackMultiplier : 1f;
            t.Tag("Phoenix").ServerDamage(DiveDamage * att, Pos.Value, owner != null ? owner.NetworkObjectId : ulong.MaxValue);
            if (t.IsAlive) t.ServerScorch(5f, owner != null ? owner.NetworkObjectId : ulong.MaxValue);
            foreach (var near in CombatQuery.Circle(t.transform.position, 1.6f, Team.Enemies).ToArray())
                if (near != t) near.Tag("Phoenix").ServerDamage(DiveDamage * 0.4f * att, Pos.Value, owner != null ? owner.NetworkObjectId : ulong.MaxValue);
            StrikeFxRpc(Pos.Value);
        }

        /// <summary>Server: struck down - it bursts, then lies as an egg until it is reborn.</summary>
        private void ServerFall()
        {
            if (Egg.Value) return;
            Egg.Value = true;
            diveTarget = null;
            eggUntil = Time.time + EggSeconds;
            float att = owner != null ? owner.AttackMultiplier : 1f;
            ulong by = owner != null ? owner.NetworkObjectId : ulong.MaxValue;
            foreach (var foe in CombatQuery.Circle(Pos.Value, BurstRadius, Team.Enemies).ToArray())
            {
                foe.Tag("Phoenix").ServerDamage(BurstDamage * att, Pos.Value, by);
                if (foe.IsAlive) foe.ServerScorch(5f, by);
            }
            // The egg settles where it fell (a little above the floor).
            var floor = Vfx.Floor(Pos.Value);
            Pos.Value = floor + Vector3.up * 0.4f;
            BurstFxRpc(Pos.Value, true);
        }

        private void ServerReborn()
        {
            Egg.Value = false;
            rebirths++;
            health.ServerSetMax(maxHealth, refill: true);
            nextDive = Time.time + 0.4f;
            BurstFxRpc(Pos.Value, false);
        }

        private void ServerExpire()
        {
            if (!IsSpawned) return;
            BurstFxRpc(Pos.Value, false);
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StrikeFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Fire, at, 0.7f, 1.4f);
            Vfx.Embers(at, 8, new Color(1f, 0.55f, 0.15f), 4f, 0.06f, 0.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BurstFxRpc(Vector3 at, bool death)
        {
            var c = new Color(1f, 0.55f, 0.15f);
            Sfx.Play(death ? Sound.Fire : Sound.Revive, at, 1f, death ? 0.8f : 1.3f);
            Vfx.Fireball(at, death ? 2.4f : 1.4f, c);
            Vfx.Embers(at, death ? 24 : 14, c, 6f, 0.07f, 0.9f);
            if (death) Vfx.Ring(Vfx.Floor(at), BurstRadius, c, 0.5f);
            if (death) UI.GameHud.ShowCombo(at + Vector3.up, "PHOENIX FALLS - REBORN SOON", c);
        }

        private void BuildVisual()
        {
            var c = new Color(1f, 0.55f, 0.15f);
            bird = MeshKit.Pivot(transform, "Bird", Vector3.zero);
            var body = MeshKit.Part(bird, "Body", MeshKit.Ball(0.22f, 10, 8), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(0.8f, 0.8f, 1.4f));
            ArtLibrary.SetGlow(body.GetComponent<Renderer>(), new Color(1f, 0.42f, 0.08f) * 0.95f);
            var head = MeshKit.Part(bird, "Head", MeshKit.Ball(0.13f, 8, 6), Mat.Glow, new Vector3(0f, 0.12f, 0.3f));
            ArtLibrary.SetGlow(head.GetComponent<Renderer>(), new Color(1f, 0.7f, 0.25f) * 1f);
            MeshKit.Part(bird, "Beak", MeshKit.Cylinder(0.04f, 0f, 0.14f, 5), Mat.Gold, new Vector3(0f, 0.11f, 0.45f), new Vector3(90f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                var piv = MeshKit.Pivot(bird, side < 0 ? "WingL" : "WingR", new Vector3(side * 0.12f, 0.05f, 0f));
                var wing = MeshKit.Part(piv, "Feathers", MeshKit.Box(new Vector3(0.75f, 0.03f, 0.32f)), Mat.Glow, new Vector3(side * 0.38f, 0f, -0.04f));
                ArtLibrary.SetGlow(wing.GetComponent<Renderer>(), new Color(0.95f, 0.28f, 0.05f) * 0.85f);
                if (side < 0) wingL = piv; else wingR = piv;
            }
            for (int i = 0; i < 3; i++)
            {
                var tail = MeshKit.Part(bird, "Tail" + i, MeshKit.Box(new Vector3(0.06f, 0.02f, 0.45f)), Mat.Glow, new Vector3((i - 1) * 0.08f, 0f, -0.42f), new Vector3(0f, (i - 1) * 14f, 0f));
                ArtLibrary.SetGlow(tail.GetComponent<Renderer>(), new Color(0.9f, 0.18f, 0.04f) * 0.9f);
            }
            egg = MeshKit.Part(transform, "Egg", MeshKit.Ball(0.28f, 10, 8), Mat.Glow, Vector3.zero, Vector3.zero, new Vector3(0.85f, 1.1f, 0.85f)).transform;
            eggR = egg.GetComponent<Renderer>();
            egg.gameObject.SetActive(false);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = new GameObject("PhoenixLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.type = LightType.Point;
            glow.color = c;
            glow.range = 6f;
            glow.intensity = 1.6f;
        }
    }
}
