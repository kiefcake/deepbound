using System.Linq;
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
    /// Boss-arena machinery the party can turn on the boss (2026-10-20, the user: "ways to deal damage
    /// to the boss"):
    /// <list type="bullet">
    /// <item><b>Ballista</b>: crank and loose (F) a great bolt at the boss, a slice of its health, then
    ///   wait out the reload. Phase gates still hold.</item>
    /// <item><b>Winch</b>: a chandelier hangs over a lit circle in the arena. Cut its chain (F) when the
    ///   boss stands under it: it crashes down, stunning and wounding whatever is beneath. Once.</item>
    /// <item><b>Brazier</b> (the Sanctum): each lit brazier makes the boss take more damage. The boss
    ///   snuffs one now and then (<see cref="DungeonDirector"/>); relight them (F).</item>
    /// </list>
    /// </summary>
    public class ArenaEngine : Interactable
    {
        public enum Kind : byte { Ballista = 0, Winch = 1, Brazier = 2 }

        public readonly NetworkVariable<Kind> Type = new NetworkVariable<Kind>();
        /// <summary>Winch: where the chandelier hangs (floor point under it).</summary>
        public readonly NetworkVariable<Vector3> Target = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<double> ReadyAt = new NetworkVariable<double>();
        public readonly NetworkVariable<bool> Lit = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> Spent = new NetworkVariable<bool>();

        public const float BallistaReload = 18f, BallistaShare = 0.035f, ChandelierRadius = 4.5f, ChandelierShare = 0.06f, ChandelierStun = 3f;
        public const float SanctumPerBrazier = 0.06f;

        public static readonly System.Collections.Generic.List<ArenaEngine> All = new System.Collections.Generic.List<ArenaEngine>();

        private Transform arm, chandelier, flame;
        private Light flameLight;
        private float dropStart = -1f;
        private Vector3 chandelierTop;

        private double Now => NetworkManager != null ? NetworkManager.ServerTime.Time : 0;
        public override float Range => Type.Value == Kind.Winch ? 2.8f : 2.6f;

        public override string Prompt(PlayerProgress who)
        {
            var boss = BossBrain.Active;
            switch (Type.Value)
            {
                case Kind.Ballista:
                    if (boss == null || boss.IsDead) return null;
                    double left = ReadyAt.Value - Now;
                    return left > 0 ? "Ballista: reloading (" + Mathf.CeilToInt((float)left) + " s)" : "Loose the ballista at " + boss.DisplayName;
                case Kind.Winch:
                    return Spent.Value ? null : "Cut the chandelier's chain  (it falls on the lit circle: stun and wound the boss)";
                case Kind.Brazier:
                    return Lit.Value ? null : "Light the sanctum brazier  (each one lit: the boss takes more damage)";
            }
            return null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);
            BuildVisual();
            Type.OnValueChanged += (_, __) => Rebuild();
            Target.OnValueChanged += (_, __) => Rebuild();
            Lit.OnValueChanged += (_, now) => RefreshFlame(now);
            Spent.OnValueChanged += (_, now) => { if (now && Type.Value == Kind.Winch) dropStart = Time.time; };
            RefreshFlame(Lit.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            base.OnNetworkDespawn();
        }

        /// <summary>Server: how many sanctum braziers burn right now.</summary>
        public static int LitBraziers
        {
            get { int n = 0; foreach (var e in All) if (e != null && e.Type.Value == Kind.Brazier && e.Lit.Value) n++; return n; }
        }

        protected override void ServerUse(PlayerProgress who)
        {
            var boss = BossBrain.Active;
            switch (Type.Value)
            {
                case Kind.Ballista:
                {
                    if (boss == null || boss.IsDead || boss.Dormant || Now < ReadyAt.Value) return;
                    ReadyAt.Value = Now + BallistaReload;
                    var from = transform.position + Vector3.up * 1.3f + transform.forward * 1.1f;
                    var dir = boss.Health.Center - from;
                    float dmg = boss.Health.Max.Value * BallistaShare;
                    ulong shooter = who.NetworkObjectId;
                    // A siege bolt punches through rubble and pillars: it always reaches the boss, after its flight.
                    StartCoroutine(ServerBoltLands(boss, dmg, from, shooter, dir.magnitude / 42f));
                    LooseRpc(from, dir);
                    break;
                }
                case Kind.Winch:
                    if (Spent.Value) return;
                    Spent.Value = true;
                    Invoke(nameof(ServerChandelierLands), 0.9f);
                    break;
                case Kind.Brazier:
                    if (Lit.Value) return;
                    Lit.Value = true;
                    LightRpc(who.name);
                    break;
            }
        }

        private void ServerChandelierLands()
        {
            var at = Target.Value;
            foreach (var h in CombatQuery.Circle(at, ChandelierRadius, Team.Enemies).ToArray())
            {
                bool isBoss = h.TryGetComponent<BossBrain>(out _);
                h.ServerDamage(isBoss ? h.Max.Value * ChandelierShare : 400f, at + Vector3.up * 3f);
                if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(ChandelierStun);
            }
            foreach (var h in CombatQuery.Circle(at, ChandelierRadius * 0.6f, Team.Players).ToArray()) h.ServerDamage(h.Max.Value * 0.25f, at + Vector3.up * 3f);
            CrashRpc(at);
        }

        /// <summary>Server: the boss puts one out.</summary>
        public void ServerSnuff()
        {
            if (Type.Value != Kind.Brazier || !Lit.Value) return;
            Lit.Value = false;
            SnuffRpc();
        }

        // ---- Presentation ----

        [Rpc(SendTo.ClientsAndHost)]
        private void LooseRpc(Vector3 from, Vector3 dir)
        {
            // It flies only as far as the boss, through whatever is in the way.
            Arrows.SpawnVisual(from, dir, ProjectileKind.Arrow, 42f, dir.magnitude + 0.5f, 0f, throughWalls: true);
            Sfx.Play(Sound.Bow, from, 1f, 0.45f);
            Sfx.Play(Sound.Slam, from, 0.7f, 1.3f);
            if (arm != null) arm.localRotation = Quaternion.Euler(-25f, 0f, 0f);
        }

        private System.Collections.IEnumerator ServerBoltLands(BossBrain boss, float dmg, Vector3 from, ulong shooter, float flight)
        {
            yield return new WaitForSeconds(flight);
            if (boss == null || boss.IsDead || boss.Health == null) yield break;
            boss.Health.ServerDamage(dmg, from, shooter);
            BoltHitRpc(boss.Health.Center);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoltHitRpc(Vector3 at)
        {
            Vfx.Flash(at, new Color(1f, 0.85f, 0.6f), 4f, 6f, 0.25f);
            Vfx.Embers(at, 14, new Color(0.7f, 0.6f, 0.5f), 6f, 0.06f, 0.6f);
            UI.GameHud.ShowCombo(at + Vector3.up, "BALLISTA", new Color(1f, 0.85f, 0.55f));
            Sfx.Play(Sound.Hit, at, 1f, 0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CrashRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.5f);
            Sfx.Play(Sound.Shatter, at, 1f, 0.7f);
            Vfx.Ring(at + Vector3.up * 0.05f, ChandelierRadius, new Color(1f, 0.8f, 0.5f), 0.5f);
            Vfx.Embers(at + Vector3.up * 0.5f, 30, new Color(1f, 0.75f, 0.35f), 8f, 0.06f, 0.9f);
            Vfx.Flash(at + Vector3.up, new Color(1f, 0.8f, 0.5f), 6f, 12f, 0.4f);
            UI.GameHud.ShowCombo(at + Vector3.up * 2f, "CRUSHED", new Color(1f, 0.8f, 0.5f));
            var local = NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 18f) local.AddShake(0.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LightRpc(string who)
        {
            Sfx.Play(Sound.Fire, transform.position + Vector3.up, 0.8f, 0.9f);
            UI.GameHud.Feed(who + " lights a sanctum brazier (" + LitBraziers + " burning): the boss takes " + Mathf.RoundToInt(SanctumPerBrazier * 100f * Mathf.Max(1, LitBraziers)) + "% more damage", new Color(1f, 0.85f, 0.55f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnuffRpc()
        {
            Sfx.Play(Sound.Whisper, transform.position + Vector3.up, 1f, 0.6f);
            Vfx.Embers(transform.position + Vector3.up * 1.2f, 10, new Color(0.4f, 0.4f, 0.45f), 2f, 0.05f, 0.8f);
            UI.GameHud.Feed("The boss snuffs out a sanctum brazier. Relight it!", new Color(0.9f, 0.6f, 0.5f));
        }

        private void RefreshFlame(bool lit)
        {
            if (flame != null) flame.gameObject.SetActive(lit);
            if (flameLight != null) flameLight.intensity = lit ? 2.6f : 0f;
        }

        private void Update()
        {
            if (arm != null) arm.localRotation = Quaternion.Slerp(arm.localRotation, Quaternion.identity, Time.deltaTime * 0.35f);
            if (chandelier != null)
            {
                if (dropStart >= 0f)
                {
                    float t = Mathf.Clamp01((Time.time - dropStart) / 0.9f);
                    chandelier.position = Vector3.Lerp(chandelierTop, Target.Value + Vector3.up * 0.4f, t * t);
                    chandelier.rotation = Quaternion.Euler(t * 18f, 0f, t * 9f);
                }
                else chandelier.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.8f) * 2f, 0f, Mathf.Cos(Time.time * 0.6f) * 2f);
            }
        }

        private Transform vis;

        /// <summary>Any peer: (re)build the look for the current kind (values arrive just after spawn).</summary>
        private void Rebuild()
        {
            if (vis != null) Destroy(vis.gameObject);
            BuildVisual();
            RefreshFlame(Lit.Value);
        }

        private void BuildVisual()
        {
            vis = new GameObject("Visual").transform;
            vis.SetParent(transform, false);
            switch (Type.Value)
            {
                case Kind.Ballista:
                {
                    MeshKit.Part(vis, "Base", MeshKit.Box(new Vector3(1.2f, 0.5f, 1.6f)), Mat.Wood, new Vector3(0f, 0.25f, 0f));
                    MeshKit.Part(vis, "Post", MeshKit.Box(new Vector3(0.3f, 0.7f, 0.3f)), Mat.Wood, new Vector3(0f, 0.85f, 0f));
                    arm = MeshKit.Pivot(vis, "Arm", new Vector3(0f, 1.25f, 0f));
                    MeshKit.Part(arm, "Stock", MeshKit.Box(new Vector3(0.22f, 0.18f, 2.2f)), Mat.Wood, new Vector3(0f, 0f, 0.2f));
                    MeshKit.Part(arm, "Bow", MeshKit.Box(new Vector3(2.2f, 0.12f, 0.14f)), Mat.Wood, new Vector3(0f, 0.05f, 1.1f));
                    MeshKit.Part(arm, "String", MeshKit.Box(new Vector3(2f, 0.02f, 0.02f)), Mat.Leather, new Vector3(0f, 0.08f, 0.5f));
                    MeshKit.Part(arm, "Bolt", MeshKit.Box(new Vector3(0.06f, 0.06f, 1.6f)), Mat.Steel, new Vector3(0f, 0.15f, 0.5f));
                    MeshKit.Part(vis, "Crank", MeshKit.Cylinder(0.25f, 0.25f, 0.08f, 10), Mat.Steel, new Vector3(0.65f, 0.75f, -0.4f), new Vector3(0f, 0f, 90f));
                    break;
                }
                case Kind.Winch:
                {
                    MeshKit.Part(vis, "Drum", MeshKit.Cylinder(0.32f, 0.32f, 0.9f, 12), Mat.Wood, new Vector3(0f, 1.1f, 0f), new Vector3(0f, 0f, 90f));
                    foreach (float x in new[] { -0.55f, 0.55f }) MeshKit.Part(vis, "Frame", MeshKit.Box(new Vector3(0.12f, 1.4f, 0.5f)), Mat.Wood, new Vector3(x, 0.7f, 0f));
                    MeshKit.Part(vis, "Lever", MeshKit.Box(new Vector3(0.08f, 0.9f, 0.08f)), Mat.Steel, new Vector3(0.7f, 1.5f, 0f), new Vector3(0f, 0f, -20f));
                    // The chandelier itself hangs over the target, and a pool of light marks where it will land.
                    var room = DungeonDirector.Layout?.BossRoom;
                    float ceiling = room != null ? room.Height - 1.6f : 8f;
                    chandelierTop = Target.Value + Vector3.up * ceiling;
                    chandelier = new GameObject("Chandelier").transform;
                    chandelier.SetParent(vis, true);
                    chandelier.position = chandelierTop;
                    MeshKit.Part(chandelier, "Ring", MeshKit.Lathe("chandRing", new[] { new Vector2(1.6f, -0.08f), new Vector2(1.75f, 0f), new Vector2(1.6f, 0.08f) }, 24, false), Mat.Steel, Vector3.zero);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 45f * Mathf.Deg2Rad;
                        var p = new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * 1.65f;
                        MeshKit.Part(chandelier, "Candle", MeshKit.Cylinder(0.05f, 0.05f, 0.2f, 6), Mat.Bone, p);
                        Props.AddFlame(chandelier, p + Vector3.up * 0.18f, 0.35f, new Color(1f, 0.75f, 0.4f), i % 2 == 0 ? 0.5f : 0f, 5f, false);
                    }
                    for (int i = 0; i < 3; i++) MeshKit.Part(chandelier, "Chain", MeshKit.Box(new Vector3(0.04f, 3f, 0.04f)), Mat.Steel, Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0.9f, 1.5f, 0f), new Vector3(0f, i * 120f, -30f));
                    var pool = MeshKit.Part(vis, "MarkPool", MeshKit.Cylinder(ChandelierRadius, ChandelierRadius, 0.01f, 32), Mat.Telegraph, Vector3.zero);
                    pool.transform.position = Target.Value + Vector3.up * 0.02f;
                    ArtLibrary.Tint(pool.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.5f, 0.08f));
                    break;
                }
                case Kind.Brazier:
                {
                    MeshKit.Part(vis, "Stand", MeshKit.Lathe("sanctumStand", new[] { new Vector2(0.35f, 0f), new Vector2(0.12f, 0.2f), new Vector2(0.1f, 1.1f), new Vector2(0.45f, 1.25f), new Vector2(0.5f, 1.45f), new Vector2(0f, 1.4f) }, 10), Mat.Gold, Vector3.zero);
                    flame = MeshKit.Pivot(vis, "SanctumFlame", Vector3.up * 1.45f);
                    Props.AddFlame(flame, Vector3.zero, 1.6f, new Color(1f, 0.85f, 0.45f), 0f, 1f, false);
                    flameLight = new GameObject("SanctumLight").AddComponent<Light>();
                    flameLight.transform.SetParent(vis, false);
                    flameLight.transform.localPosition = Vector3.up * 1.8f;
                    flameLight.type = LightType.Point;
                    flameLight.color = new Color(1f, 0.85f, 0.5f);
                    flameLight.range = 7f;
                    flameLight.shadows = LightShadows.None;
                    break;
                }
            }
        }

        /// <summary>Tests: use it as this player.</summary>
        public void ServerDebugUse(PlayerProgress who) => ServerUse(who);
    }
}
