using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A HANGING CHANDELIER (2026-11-08, dungeon interactables: "shoot the chain, the chandelier crashes down on whatever
    /// is under it"). An iron wheel of candles hangs over the middle of a combat room; its rope runs down to a cleat on
    /// the wall. F at the cleat cuts the rope (anyone, any class): a ring telegraph flashes under it and
    /// <see cref="FallSeconds"/> later it lands - the dead under it are crushed (most of their health, stunned), bosses
    /// are staggered, and players caught under it are hurt too. Once a floor, each. The networked object stands at the
    /// cleat; the chandelier hangs over <see cref="HangAt"/>.
    /// </summary>
    public class Chandelier : Interactable
    {
        public const float Radius = 2.6f, FallSeconds = 0.75f, HangHeight = 4.6f;
        public readonly NetworkVariable<Vector3> HangAt = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<bool> Fallen = new NetworkVariable<bool>();

        private Transform wheel, rope, chain;
        private Light glow;
        private float fallStart = -1f;
        private bool built;
        public override float Range => 2.2f;

        public override string Prompt(PlayerProgress who) => Fallen.Value || fallStart >= 0f ? null : "Cut the chandelier's rope  -  it drops on whatever stands under it";

        protected override void ServerUse(PlayerProgress who)
        {
            if (Fallen.Value) return;
            Fallen.Value = true;
            DropRpc();
            StartCoroutine(Crash(who.NetworkObjectId));
        }

        /// <summary>Server, tests: drop it now.</summary>
        public void ServerDebugDrop(ulong by) { if (!Fallen.Value) { Fallen.Value = true; DropRpc(); StartCoroutine(Crash(by)); } }

        private System.Collections.IEnumerator Crash(ulong by)
        {
            yield return new WaitForSeconds(FallSeconds);
            var at = HangAt.Value;
            float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
            int crushed = 0;
            foreach (var h in Health.All.ToArray())
            {
                if (h == null || !h.IsAlive) continue;
                var d = h.transform.position - at;
                if (Mathf.Abs(d.y) > 2.5f || new Vector2(d.x, d.z).magnitude > Radius + 0.3f) continue;
                if (h.Team == Team.Enemies)
                {
                    var boss = h.GetComponent<BossBrain>();
                    float dmg = boss != null ? h.Max.Value * 0.06f : Mathf.Max(h.Max.Value * 0.7f, 50f + 18f * tier);
                    h.Tag("Chandelier").ServerDamage(dmg, at + Vector3.up * 3f, by);
                    if (boss != null) boss.ServerAddStagger(0.3f);
                    else if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(2.5f);
                    crushed++;
                }
                else if (h.Team == Team.Players)
                    h.Tag("Chandelier").ServerDamage(h.Max.Value * 0.35f + h.Defense * Health.DefenseWeight, at + Vector3.up * 3f);
            }
            OilSlick.ServerIgniteAt(at, by, Radius * 0.5f); // its candles light any oil it lands in
            LandRpc(crushed);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DropRpc()
        {
            fallStart = Time.time;
            Telegraph.Show(HangAt.Value, Radius, FallSeconds, null, new Color(1f, 0.75f, 0.35f));
            Sfx.Play(Sound.Chain, transform.position + Vector3.up, 1f, 1.2f);
            Sfx.Play(Sound.Creak, HangAt.Value + Vector3.up * HangHeight, 1f, 0.7f);
            if (rope != null) rope.gameObject.SetActive(false);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LandRpc(int crushed)
        {
            var at = HangAt.Value;
            Sfx.Play(Sound.Slam, at, 1f, 0.8f);
            Sfx.Play(Sound.Shatter, at, 0.9f, 0.9f);
            Vfx.Ring(at + Vector3.up * 0.05f, Radius, new Color(1f, 0.8f, 0.5f), 0.5f);
            Vfx.Smoke(at + Vector3.up * 0.3f, 6, Radius * 0.6f, 2f);
            Vfx.Embers(at + Vector3.up * 0.4f, 22, new Color(1f, 0.7f, 0.3f), 6f, 0.06f, 0.9f);
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 18f) local.AddShake(0.5f);
            if (crushed > 0) UI.GameHud.ShowCombo(at + Vector3.up * 1.6f, crushed > 1 ? "CRUSHED x" + crushed : "CRUSHED", new Color(1f, 0.8f, 0.45f));
            if (glow != null) glow.intensity = 0.6f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            HangAt.OnValueChanged += (_, __) => Build();
            Fallen.OnValueChanged += (_, on) => { if (on && fallStart < 0f) fallStart = Time.time - FallSeconds; };
            Build();
            if (Fallen.Value) fallStart = Time.time - FallSeconds;
        }

        private void Build()
        {
            if (built || HangAt.Value == Vector3.zero) return;
            built = true;
            // The cleat: an iron hook on the wall with the rope wound round it.
            MeshKit.Part(transform, "Cleat", MeshKit.Box(new Vector3(0.3f, 0.08f, 0.08f)), Mat.Steel, new Vector3(0f, 1.25f, 0f));
            var coil = MeshKit.Part(transform, "Coil", MeshKit.Cylinder(0.07f, 0.07f, 0.16f, 8), Mat.Cloth, new Vector3(0f, 1.12f, 0f));
            ArtLibrary.Tint(coil.GetComponent<Renderer>(), new Color(0.55f, 0.45f, 0.3f));
            // The chandelier: an iron wheel, eight candles, hung on a chain from the dark.
            var top = HangAt.Value + Vector3.up * HangHeight;
            wheel = new GameObject("ChandelierWheel").transform;
            wheel.SetParent(transform, true);
            wheel.position = top;
            MeshKit.Part(wheel, "Ring", MeshKit.Cylinder(1.05f, 1.05f, 0.08f, 16), Mat.Steel, Vector3.zero);
            MeshKit.Part(wheel, "Hub", MeshKit.Cylinder(0.18f, 0.12f, 0.35f, 8), Mat.Steel, Vector3.down * 0.2f);
            for (int i = 0; i < 4; i++)
                MeshKit.Part(wheel, "Spoke", MeshKit.Box(new Vector3(2.05f, 0.05f, 0.05f)), Mat.Steel, Vector3.up * 0.04f, new Vector3(0f, i * 45f, 0f));
            for (int i = 0; i < 8; i++)
            {
                var p = Quaternion.Euler(0f, i * 45f, 0f) * new Vector3(0f, 0.04f, 1.0f);
                MeshKit.Part(wheel, "Candle", MeshKit.Cylinder(0.04f, 0.04f, 0.2f, 6), Mat.Bone, p);
                var f = MeshKit.Part(wheel, "Flame", MeshKit.Ball(0.035f, 6, 4), Mat.Glow, p + Vector3.up * 0.24f);
                ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(1f, 0.65f, 0.3f) * 1.8f);
            }
            chain = MeshKit.Part(wheel, "Chain", MeshKit.Box(new Vector3(0.05f, 6f, 0.05f)), Mat.Steel, Vector3.up * 3f).transform;
            glow = new GameObject("ChandelierLight").AddComponent<Light>();
            glow.transform.SetParent(wheel, false);
            glow.transform.localPosition = Vector3.down * 0.3f;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.7f, 0.4f);
            glow.range = 9f;
            glow.intensity = 2f;
            glow.shadows = LightShadows.None;
            // The rope from the wheel down to the cleat: the thing you look at to know what F does.
            var from = top + Vector3.up * 0.1f;
            var to = transform.position + Vector3.up * 1.2f;
            rope = MeshKit.Part(transform, "Rope", MeshKit.Box(new Vector3(0.035f, 0.035f, 1f)), Mat.Cloth, Vector3.zero).transform;
            ArtLibrary.Tint(rope.GetComponent<Renderer>(), new Color(0.6f, 0.5f, 0.32f));
            rope.position = (from + to) / 2f;
            rope.rotation = Quaternion.LookRotation(to - from);
            rope.localScale = new Vector3(1f, 1f, Vector3.Distance(from, to));
            if (Fallen.Value) rope.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (wheel == null || fallStart < 0f) return;
            float t = Mathf.Clamp01((Time.time - fallStart) / FallSeconds);
            var top = HangAt.Value + Vector3.up * HangHeight;
            var floor = HangAt.Value + Vector3.up * 0.1f;
            wheel.position = Vector3.Lerp(top, floor, t * t);
            wheel.rotation = Quaternion.Euler(t >= 1f ? 9f : 0f, t * 25f, t >= 1f ? -6f : 0f);
            if (chain != null && t >= 1f) chain.gameObject.SetActive(false);
        }
    }
}
