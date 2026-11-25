using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    public class TwiceCrownedBrain : LairBrain
    {
        public override string DisplayName => "The Twice-Crowned";
        public override string Epithet => "Two Kings of One Throne";
        public override string LastWords => "Which... of us... was real...?";
        public override string Hint => "Strike whoever wears the crown.";
        protected override float VisualScale => 1.5f;
        protected override float[] PhaseGates => IsEcho ? System.Array.Empty<float>() : new[] { 0.66f, 0.33f };
        protected override Color ShockwaveColor => new Color(1f, 0.82f, 0.4f);
        public static readonly Color CrownGold = new Color(1f, 0.82f, 0.35f);

        public readonly NetworkVariable<bool> Crowned = new NetworkVariable<bool>(true);

        private float nextPass, nextCharge;
        private int movement;

        public int Passes { get; private set; }
        private TwiceCrownedBrain Shadow => echoes.Count > 0 ? echoes[0] as TwiceCrownedBrain : null;

        protected override void OnEngaged()
        {
            var s = SpawnEcho(1, transform.position + transform.right * 4f) as TwiceCrownedBrain;
            if (s != null) s.Crowned.Value = false;
            nextPass = Time.time + 6f;
            nextCharge = Time.time + 12f;
            if (IsServer) health.IncomingModifiers.Insert(1, (dmg, from) => dmg >= KillDamage || Crowned.Value ? dmg : Bare(from));
        }

        private float Bare(Vector3 from)
        {
            BareFxRpc(transform.position + Vector3.up * 2.4f);
            return 0f;
        }

        protected override void EchoHit(float dmg, Vector3 from)
        {
            var m = Master as TwiceCrownedBrain;
            if (m == null || m.IsDead) return;
            if (!Crowned.Value) { BareFxRpc(transform.position + Vector3.up * 2.4f); return; }
            m.Health.ServerDamage(dmg, from, NetworkObjectId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BareFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Block, at, 0.5f, 1.4f);
            Vfx.Embers(at, 4, new Color(0.5f, 0.5f, 0.55f), 2f, 0.04f, 0.3f);
        }

        protected override void OnPhaseGate(int index)
        {
            movement = index + 1;
            if (index == 0) Transition("The Exchange", "Brother - your turn.", 1.4f, () => { Exchange(); SummonOf(guardPrefab, scaledPlayers, 4f, 0.8f); });
            else Transition("Twin Charge", "Together, then. As we always were.", 1.4f, () => { nextCharge = Time.time + 1f; ServerEnrage(9999f); });
        }

        protected override void Update()
        {
            if (IsServer && !IsDead && !IsEcho && Engaged.Value)
            {
                var s = Shadow;
                if (s != null && Time.time >= nextPass)
                {
                    nextPass = Time.time + (movement >= 1 ? 4.5f : 6.5f) * (StandingParty <= 1 ? 1.15f : 1f);
                    bool toShadow = Crowned.Value;
                    Crowned.Value = !toShadow;
                    s.Crowned.Value = toShadow;
                    Passes++;
                    CrownLeapRpc(toShadow ? transform.position : s.transform.position, toShadow ? s.transform.position : transform.position);
                    if (movement >= 1 && Passes % 2 == 0) Exchange();
                }
                if (movement >= 2 && s != null && Time.time >= nextCharge)
                {
                    nextCharge = Time.time + 9f / Cadence;
                    StartCoroutine(TwinCharge(s));
                }
            }
            if (IsServer && IsEcho && Master != null && !Master.IsSpawned) Shatter(false);
            base.Update();
            UpdateCrown();
        }

        private void Exchange()
        {
            var s = Shadow;
            if (s == null) return;
            var a = transform.position; var b = s.transform.position;
            Warp(b); s.Warp(a);
            ExchangeFxRpc(a, b);
        }

        private System.Collections.IEnumerator TwinCharge(TwiceCrownedBrain s)
        {
            var victim = NearestStanding(transform.position);
            if (victim == null) yield break;
            float tell = Tell(1.2f);
            var lanes = new List<(LairBrain who, Vector3 from, Vector3 to)>();
            foreach (var who in new LairBrain[] { this, s })
            {
                var from = who.transform.position;
                var dir = victim.transform.position - from; dir.y = 0f;
                float len = Mathf.Min(18f, dir.magnitude + 5f);
                LineTelegraphRpc(from, dir.normalized, len, 1.6f, tell);
                lanes.Add((who, from, from + dir.normalized * len));
            }
            castUntil = busyUntil = Time.time + tell + 0.6f;
            PlayActionRpc(RigAction.Charge, tell + 0.5f);
            yield return new WaitForSeconds(tell);
            foreach (var (who, from, to) in lanes)
            {
                if (who == null || !who.IsSpawned) continue;
                var end = UnityEngine.AI.NavMesh.Raycast(from, to, out var hit, UnityEngine.AI.NavMesh.AllAreas) ? hit.position : to;
                who.Warp(end);
                LaneHit(from, end, 0.8f, Signature(damage * 1.3f));
                ChargeFxRpc(from, end);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CrownLeapRpc(Vector3 from, Vector3 to)
        {
            Sfx.Play(Sound.Coins, to + Vector3.up * 2f, 0.9f, 1.3f);
            for (int i = 0; i <= 10; i++)
            {
                float k = i / 10f;
                Vfx.Embers(Vector3.Lerp(from, to, k) + Vector3.up * (2.6f + Mathf.Sin(k * Mathf.PI) * 2.5f), 1, CrownGold, 1f, 0.06f, 0.6f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ExchangeFxRpc(Vector3 a, Vector3 b)
        {
            Sfx.Play(Sound.Shade, (a + b) / 2f, 1f, 0.8f);
            Vfx.Flash(a + Vector3.up, CrownGold, 3f, 6f, 0.25f);
            Vfx.Flash(b + Vector3.up, CrownGold, 3f, 6f, 0.25f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ChargeFxRpc(Vector3 from, Vector3 to)
        {
            Sfx.Play(Sound.Slam, to, 1f, 0.8f);
            for (int i = 0; i <= 10; i++) Vfx.Smoke(Vector3.Lerp(from, to, i / 10f), 1, 0.4f, 0.8f);
        }

        private Transform crown;
        private void UpdateCrown()
        {
            if (Rig == null || Rig.Head == null) return;
            if (crown == null)
            {
                crown = MeshKit.Pivot(Rig.Head, "LairCrown", new Vector3(0f, 0.3f, 0f));
                MeshKit.Part(crown, "Band", MeshKit.Cylinder(0.17f, 0.18f, 0.08f, 10), Mat.Gold, Vector3.zero);
                for (int i = 0; i < 5; i++)
                    MeshKit.Part(crown, "Point", MeshKit.Cylinder(0.035f, 0f, 0.14f, 4), Mat.Gold, Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward * 0.16f + Vector3.up * 0.1f);
                GlowPart(MeshKit.Part(crown, "Gem", MeshKit.Ball(0.03f, 6, 4), Mat.Glow, new Vector3(0f, 0.04f, 0.17f)), CrownGold * 1.6f);
            }
            bool wear = Crowned.Value && !IsDead;
            if (crown.gameObject.activeSelf != wear) crown.gameObject.SetActive(wear);
        }

        protected override void OnRoleChanged()
        {
            if (Rig == null || Role.Value != 1) return;

            foreach (var r in Rig.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null && !r.sharedMaterial.name.Contains("Glow")) ArtLibrary.Tint(r, new Color(0.12f, 0.1f, 0.18f));
            Rig.GlowColor = new Color(0.6f, 0.4f, 1f);
        }

        protected override void BeginAttack()
        {
            if (IsEcho && StandingParty <= 1) busyUntil = Time.time + 0.6f;
            base.BeginAttack();
        }

        protected override void DressRig(HumanoidRig rig)
        {
            if (rig.Torso == null || rig.Head == null) return;

            TintPart(MeshKit.Part(rig.Torso, "Cloak", MeshKit.Box(new Vector3(0.62f, 1.0f, 0.04f)), Mat.Cloth, new Vector3(0f, 0.02f, -0.25f), new Vector3(8f, 0f, 0f)), new Color(0.35f, 0.08f, 0.12f));

            var drop = new List<GameObject>();
            foreach (Transform c in rig.Head)
                if (c.name == "Crown" || c.name == "CrownBand" || c.name.StartsWith("CrownPoint") || c.name.StartsWith("Gem") || c.name.StartsWith("Tine")) drop.Add(c.gameObject);
            foreach (var g in drop) DestroyImmediate(g);
            var glow = PlateKit.Armour(rig, new PlateKit.Look { Plate = new Color(0.25f, 0.23f, 0.27f), Trim = new Color(0.75f, 0.58f, 0.25f), Glow = new Color(1f, 0.62f, 0.2f), ClosedHelm = false, Horns = true });
            foreach (var n in new[] { "EyeL", "EyeR" }) { var e = rig.Head.Find(n); if (e != null) glow.Add(e.GetComponent<Renderer>()); }
            var grip = rig.ArmR != null ? rig.ArmR.Find("Grip") : null;
            if (grip != null)
            {
                foreach (var (n, scale, y) in new[] { ("Blade", new Vector3(1.8f, 1.2f, 1.6f), 0.73f), ("Rune", new Vector3(1.5f, 1.2f, 1f), 0.69f), ("Guard", new Vector3(1.4f, 1.6f, 1.4f), 0.04f) })
                {
                    var p = grip.Find(n);
                    if (p == null) continue;
                    p.localScale = Vector3.Scale(p.localScale, scale);
                    p.localPosition = new Vector3(0f, y, 0f);
                    if (n == "Rune") glow.Add(p.GetComponent<Renderer>());
                }
            }
            rig.GlowRenderers = glow.ToArray();
            rig.GlowColor = new Color(1f, 0.62f, 0.2f);
            rig.StrideLength = 1.8f;
        }
    }
}
