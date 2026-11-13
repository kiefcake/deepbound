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
    /// <summary>
    /// A HEARTH BRAZIER (2026-11-13, a room centrepiece; the user: "braziers to light also", every centrepiece must
    /// serve a purpose). A great iron bowl of cold coals on a hearthstone in the middle of a combat room. F lights it.
    /// Lit, it burns for the rest of the floor and:
    /// <list type="bullet">
    /// <item>lights the room (a big warm light);</item>
    /// <item>WARMS whoever stands within <see cref="WarmRadius"/>: frostbite melts off (a Rime Magus's blizzard cannot slow
    ///   you there) and you mend <see cref="MendPerSecond"/> of your health a second while no living foe is within
    ///   <see cref="QuietRadius"/>;</item>
    /// <item>SCORCHES any of the dead within <see cref="BurnRadius"/> - kick or bash them into it.</item>
    /// </list>
    /// </summary>
    public class HearthBrazier : Interactable
    {
        public static readonly List<HearthBrazier> All = new List<HearthBrazier>();
        public const float WarmRadius = 4.5f, BurnRadius = 1.6f, MendPerSecond = 0.015f, QuietRadius = 9f;
        public static readonly Color Fire = new Color(1f, 0.6f, 0.25f);
        public readonly NetworkVariable<bool> Lit = new NetworkVariable<bool>();
        private Torch flame;
        private Transform ring;
        private float nextTick;
        public override float Range => 2.6f;

        /// <summary>Is <paramref name="at"/> warmed by a lit hearth (any peer)?</summary>
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
            if (flame != null && Lit.Value && ring != null) ring.localRotation = Quaternion.Euler(0f, Time.time * 8f, 0f);
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
            Vfx.Flash(transform.position + Vector3.up * 1.4f, Fire, 4f, 10f, 0.4f);
            Vfx.Embers(transform.position + Vector3.up * 1.2f, 18, Fire, 4f, 0.05f, 1f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "HEARTH LIT", Fire);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);
            // A heavy iron bowl on three legs, banded, full of cold black coals; its flame and light wake when lit.
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
            flame = Props.AddFlame(transform, Vector3.up * 1.3f, 2.8f, Fire, 4f, 16f, true);
            ring = MeshKit.Part(transform, "WarmRing", MeshKit.Cylinder(WarmRadius, WarmRadius, 0.005f, 32), Mat.Glow, Vector3.up * 0.14f).transform;
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), Fire * 0.035f);
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
        }

        public override void OnNetworkDespawn() { All.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }
    }
}
