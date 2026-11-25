using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    public class HearthBrazier : Interactable
    {
        public static new readonly List<HearthBrazier> All = new List<HearthBrazier>();
        public const float WarmRadius = 4.5f, BurnRadius = 1.6f, MendPerSecond = 0.015f, QuietRadius = 9f;
        public static readonly Color Fire = new Color(1f, 0.6f, 0.25f);
        public readonly NetworkVariable<bool> Lit = new NetworkVariable<bool>();
        private Torch flame;
        private Transform ring;
        private readonly List<GameObject> licks = new List<GameObject>();
        private float nextTick;
        public override float Range => 2.6f;

        public static bool Warms(Vector3 at)
        {
            foreach (var h in All)
            {
                if (h == null || !h.Lit.Value) continue;
                var d = h.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude <= WarmRadius * WarmRadius) return true;
            }
            return false;
        }

        public override string Prompt(PlayerProgress who) => Lit.Value ? null : "Light the hearth  (it lights the room, warms off frost, mends you between fights, burns the dead)";

        protected override void ServerUse(PlayerProgress who) => ServerLight();

        public void ServerLight()
        {
            if (!IsServer || Lit.Value) return;
            Lit.Value = true;
            LitRpc();
        }

        private void Update()
        {
            if (ring != null && Lit.Value) ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), Fire * (0.55f + 0.15f * Mathf.Sin(Time.time * 3.1f) + 0.08f * Mathf.Sin(Time.time * 7.3f)));
            if (!IsServer || !Lit.Value || Time.time < nextTick) return;
            nextTick = Time.time + 0.5f;
            var at = transform.position;
            bool quiet = true;
            foreach (var e in Enemies.EnemyBrain.All)
            {
                if (e == null || e.IsDead) continue;
                var d = e.transform.position - at; d.y = 0f;
                float m = d.magnitude;
                if (m < QuietRadius) quiet = false;
                if (m < BurnRadius && e.TryGetComponent<Health>(out var eh) && eh.IsAlive) eh.ServerScorch(3f, NetworkObjectId);
            }
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value) continue;
                var d = p.transform.position - at; d.y = 0f;
                if (d.magnitude > WarmRadius) continue;
                if (p.TryGetComponent<PlayerDebuffs>(out var deb) && deb.Frostbitten) deb.FrostUntil.Value = 0;
                if (quiet && p.Health.Fraction < 1f) p.Health.ServerHeal(p.Health.Max.Value * MendPerSecond * 0.5f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LitRpc()
        {
            Sfx.Play(Sound.Fire, transform.position + Vector3.up * 1.2f, 0.9f, 0.8f);
            Vfx.Flash(transform.position + Vector3.up * 1.4f, Fire, 1.6f, 8f, 0.35f);
            Vfx.Embers(transform.position + Vector3.up * 1.2f, 14, Fire, 3f, 0.05f, 1f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);

            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                MeshKit.Part(transform, "Leg", MeshKit.Box(new Vector3(0.08f, 1.0f, 0.08f)), Mat.Steel,
                    Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.48f, 0.42f), new Vector3(-14f, a, 0f));
            }
            MeshKit.Part(transform, "Bowl", MeshKit.Lathe("hearthBowl", new[] { new Vector2(0.15f, 0f), new Vector2(0.55f, 0.12f), new Vector2(0.78f, 0.34f), new Vector2(0.82f, 0.42f) }, 14, true), Mat.Steel, Vector3.up * 0.9f);
            MeshKit.Part(transform, "Band", MeshKit.Cylinder(0.8f, 0.8f, 0.05f, 14), Mat.Steel, Vector3.up * 1.27f);
            for (int i = 0; i < 9; i++)
            {
                var r = Random.insideUnitCircle * 0.5f;
                ArtLibrary.Tint(MeshKit.Part(transform, "Coal", MeshKit.Ball(0.13f, 6, 4), Mat.Rubble, new Vector3(r.x, 1.22f, r.y)).GetComponent<Renderer>(), new Color(0.12f, 0.1f, 0.09f));
            }
            MeshKit.MergeParts(transform);

            flame = Props.AddFlame(transform, Vector3.up * 1.26f, 1.25f, Fire, 2.2f, 11f, true);
            for (int i = 0; i < 4; i++)
            {
                var off = Quaternion.Euler(0f, i * 90f + 20f, 0f) * new Vector3(0f, 0f, 0.36f);
                Props.AddFlame(transform, Vector3.up * 1.24f + off, 0.6f + 0.12f * (i % 2), Fire, 0f, 1f, false, light: false);
                licks.Add(transform.GetChild(transform.childCount - 1).gameObject);
            }
            ring = MeshKit.Part(transform, "EmberBed", MeshKit.Cylinder(0.62f, 0.62f, 0.03f, 14), Mat.Glow, Vector3.up * 1.2f).transform;
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), Fire * 0.6f);
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (IsServer)
            {
                var ob = gameObject.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Capsule;
                ob.radius = 0.9f; ob.height = 1.4f; ob.carving = true;
            }
            Lit.OnValueChanged += (_, on) => Show(on);
            Show(Lit.Value);
        }

        private void Show(bool on)
        {
            if (flame != null) flame.gameObject.SetActive(on);
            if (ring != null) ring.gameObject.SetActive(on);
            foreach (var l in licks) if (l != null) l.SetActive(on);
        }

        public override void OnNetworkDespawn() { All.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }
    }
}
