using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// VIGIL CANDLES (2026-11-08, dungeon interactables: "light all the candles in a room for a short blessing; enemies
    /// try to snuff them"). Four tall candles stand on iron prickets round a combat room, cold. F lights one. Light all four
    /// of a vigil and it is KEPT: every player in the room is mended a quarter, and each one without a shrine blessing
    /// gets one for this floor (else embers). The dead hate the light: any of them within <see cref="SnuffRange"/> of a
    /// lit candle of an unkept vigil puts it out. A ring under each lit candle shows which are burning.
    /// </summary>
    public class VigilCandle : Interactable
    {
        public static readonly List<VigilCandle> All = new List<VigilCandle>();
        public const float SnuffRange = 1.6f, KeptRadius = 22f;
        public const int PerVigil = 4;

        /// <summary>Which vigil this belongs to (the floor's candles are grouped four at a time).</summary>
        public readonly NetworkVariable<byte> Vigil = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> Lit = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> Kept = new NetworkVariable<bool>();

        private Transform flame, ring;
        private Light glow;
        private float nextSnuffCheck;
        public override float Range => 2.2f;

        public override string Prompt(PlayerProgress who)
        {
            if (Lit.Value) return null;
            int lit = 0;
            foreach (var c in All) if (c != null && c.Vigil.Value == Vigil.Value && c.Lit.Value) lit++;
            return "Light the vigil candle  (" + lit + " / " + PerVigil + " burning - light them all for a blessing; the dead snuff them)";
        }

        protected override void ServerUse(PlayerProgress who) => ServerLight(who);

        public void ServerLight(PlayerProgress who)
        {
            if (Lit.Value) return;
            Lit.Value = true;
            LightFxRpc();
            int lit = 0;
            var vigil = new List<VigilCandle>();
            foreach (var c in All) if (c != null && c.Vigil.Value == Vigil.Value) { vigil.Add(c); if (c.Lit.Value) lit++; }
            if (lit < PerVigil || vigil.Count < PerVigil) return;
            // Kept: the party in the room is mended and blessed.
            foreach (var c in vigil) c.Kept.Value = true;
            var centre = Vector3.zero;
            foreach (var c in vigil) centre += c.transform.position;
            centre /= vigil.Count;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value || Vector3.Distance(p.transform.position, centre) > KeptRadius) continue;
                p.Health.ServerHeal(p.Health.Max.Value * 0.25f);
                var prog = p.GetComponent<PlayerProgress>();
                if (prog.ActiveBlessing.Value == 0)
                {
                    var pick = new[] { Blessing.Wrath, Blessing.Bulwark, Blessing.Swiftness, Blessing.Vigor }[Random.Range(0, 4)];
                    prog.ServerBless(pick);
                }
                else DungeonDirector.Instance?.ServerDropEmbers(p.transform.position + Vector3.up * 0.5f, 10);
            }
            KeptRpc(centre);
        }

        private void Update()
        {
            if (flame != null && Lit.Value)
            {
                float f = 0.9f + Mathf.PerlinNoise(Time.time * 7f, NetworkObjectId * 0.37f) * 0.25f;
                flame.localScale = new Vector3(1f, f, 1f);
                if (glow != null) glow.intensity = 1.1f * f;
            }
            if (!IsServer || !Lit.Value || Kept.Value || Time.time < nextSnuffCheck) return;
            nextSnuffCheck = Time.time + 0.3f;
            foreach (var e in Enemies.EnemyBrain.All)
            {
                if (e == null || e.IsDead) continue;
                var d = e.transform.position - transform.position; d.y = 0f;
                if (d.magnitude > SnuffRange) continue;
                Lit.Value = false;
                SnuffRpc();
                break;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LightFxRpc()
        {
            Sfx.Play(Sound.Fire, transform.position + Vector3.up * 1.4f, 0.6f, 1.6f);
            Vfx.Flash(transform.position + Vector3.up * 1.5f, new Color(1f, 0.75f, 0.4f), 2f, 4f, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnuffRpc()
        {
            Vfx.Smoke(transform.position + Vector3.up * 1.5f, 2, 0.15f, 1.4f);
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up * 1.4f, 0.8f, 0.9f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void KeptRpc(Vector3 centre)
        {
            var gold = new Color(1f, 0.85f, 0.5f);
            Sfx.Play(Sound.Toll, centre + Vector3.up * 2f, 0.8f, 1.5f);
            Sfx.Play(Sound.Revive, centre + Vector3.up, 0.8f, 1.2f);
            Vfx.Flash(centre + Vector3.up * 2f, gold, 5f, 14f, 0.6f);
            UI.GameHud.ShowTitleCard("THE VIGIL IS KEPT", "Mended, and blessed for this floor", gold);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);
            // A tall iron pricket with a fat church candle; a ring of wax drips at its foot.
            MeshKit.Part(transform, "Foot", MeshKit.Cylinder(0.22f, 0.26f, 0.06f, 8), Mat.Steel, Vector3.zero);
            MeshKit.Part(transform, "Stem", MeshKit.Cylinder(0.03f, 0.03f, 1.05f, 6), Mat.Steel, Vector3.up * 0.05f);
            MeshKit.Part(transform, "Pan", MeshKit.Cylinder(0.14f, 0.1f, 0.05f, 8), Mat.Steel, Vector3.up * 1.08f);
            MeshKit.Part(transform, "Candle", MeshKit.Cylinder(0.07f, 0.065f, 0.32f, 8), Mat.Bone, Vector3.up * 1.12f);
            flame = MeshKit.Pivot(transform, "FlamePivot", Vector3.up * 1.47f);
            var f = MeshKit.Part(flame, "Flame", MeshKit.Lathe("vigilFlame", new[] { new Vector2(0f, 0f), new Vector2(0.035f, 0.04f), new Vector2(0f, 0.13f) }, 6), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(f.GetComponent<Renderer>(), new Color(1f, 0.7f, 0.3f) * 2f);
            ring = MeshKit.Part(transform, "LitRing", MeshKit.Cylinder(0.55f, 0.55f, 0.006f, 16), Mat.Glow, Vector3.up * 0.01f).transform;
            ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.35f) * 0.35f);
            ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glow = new GameObject("VigilLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.up * 1.6f;
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.72f, 0.4f);
            glow.range = 4.5f;
            glow.shadows = LightShadows.None;
            Lit.OnValueChanged += (_, on) => Show(on);
            Kept.OnValueChanged += (_, on) => { if (on && ring != null) ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.5f) * 0.8f); };
            Show(Lit.Value);
        }

        private void Show(bool on)
        {
            if (flame != null) flame.gameObject.SetActive(on);
            if (ring != null) ring.gameObject.SetActive(on);
            if (glow != null) glow.enabled = on;
        }

        public override void OnNetworkDespawn() { All.Remove(this); base.OnNetworkDespawn(); }
        public override void OnDestroy() { All.Remove(this); base.OnDestroy(); }
    }
}
