using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// CELLAR VAULTS (2026-11-13, the user: "Vault rooms behind breakable floors. Smash a cracked floor tile to drop into a
    /// small cellar room, with a rope to climb back out."). One prefab, two jobs:
    /// <list type="bullet">
    /// <item><see cref="Mode.Hatch"/>: a cracked flagstone in a side room (it has a <see cref="Health"/>, Team.Props: any blow
    /// that lands breaks it). Broken, it is a dark hole; F drops you into the cellar below
    /// (<see cref="DungeonLayout.Room.CellarCenter"/>), unhurt.</item>
    /// <item><see cref="Mode.Rope"/>: the rope hanging under the hole; F climbs back up beside it.</item>
    /// </list>
    /// The cellar itself is built by <see cref="DungeonBuilder"/>; what is in it, by <see cref="DungeonDirector"/>.SpawnCellars.
    /// </summary>
    public class CellarHatch : Interactable
    {
        public enum Mode : byte { Hatch = 0, Rope = 1 }
        public readonly NetworkVariable<byte> Kind = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> Broken = new NetworkVariable<bool>();
        /// <summary>Where using it puts you (the cellar floor for a hatch, beside the hole for the rope).</summary>
        public readonly NetworkVariable<Vector3> Leads = new NetworkVariable<Vector3>();

        private Transform tile, hole;
        public override float Range => 2.2f;
        public Mode What => (Mode)Kind.Value;

        public void Prepare(Mode m, Vector3 leadsTo)
        {
            Kind.Value = (byte)m;
            Leads.Value = leadsTo;
            if (m == Mode.Rope) Broken.Value = true;
        }

        public override string Prompt(PlayerProgress who)
        {
            if (What == Mode.Rope) return "Climb the rope back up";
            return Broken.Value ? "Drop through into the cellar  (a rope hangs below)" : null;
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (who == null || !Broken.Value) return;
            var np = who.GetComponent<NetworkPlayer>();
            if (np == null) return;
            np.RescueRpc(Leads.Value + Vector3.up * 0.15f);
            UsedRpc(What == Mode.Hatch, transform.position);
        }

        /// <summary>Server: break the tile (a blow, or a test).</summary>
        public void ServerBreak()
        {
            if (Broken.Value || What != Mode.Hatch) return;
            Broken.Value = true;
            BrokeRpc(transform.position);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && TryGetComponent<Health>(out var h))
            {
                if (What == Mode.Rope) h.enabled = false;
                else { h.ServerSetMax(1f, true); h.Died += _ => ServerBreak(); }
            }
            Build();
            Broken.OnValueChanged += (_, _) => Build();
        }

        private void Build()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            if (What == Mode.Rope)
            {
                var rope = MeshKit.Part(transform, "Rope", MeshKit.Cylinder(0.035f, 0.035f, DungeonLayout.CellarHeight + 0.4f, 6), Mat.Wood, Vector3.zero);
                ArtLibrary.Tint(rope.GetComponent<Renderer>(), new Color(0.55f, 0.45f, 0.3f));
                for (int k = 1; k < 5; k++)
                {
                    var knot = MeshKit.Part(transform, "Knot", MeshKit.Ball(0.07f, 6, 4), Mat.Wood, Vector3.up * k * 0.7f);
                    ArtLibrary.Tint(knot.GetComponent<Renderer>(), new Color(0.5f, 0.4f, 0.27f));
                }
                return;
            }
            if (!Broken.Value)
            {
                // A paler flagstone, split across with dark cracks, a hair proud of the floor.
                var t = MeshKit.Part(transform, "Tile", MeshKit.Box(new Vector3(1.3f, 0.05f, 1.3f)), Mat.Flagstone, Vector3.up * 0.025f);
                // 2026-11-13 shots: it read as a plain dark square. Paler stone, a pale chipped edge, and the cracks breathe a
                // faint cold light from the vault below (with dust drifting up through them, Update).
                ArtLibrary.Tint(t.GetComponent<Renderer>(), new Color(0.7f, 0.66f, 0.6f));
                foreach (var (ex, ez, sx, sz) in new[] { (0f, 0.66f, 1.34f, 0.05f), (0f, -0.66f, 1.34f, 0.05f), (0.66f, 0f, 0.05f, 1.34f), (-0.66f, 0f, 0.05f, 1.34f) })
                {
                    var edge = MeshKit.Part(transform, "Edge", MeshKit.Box(new Vector3(sx, 0.06f, sz)), Mat.Flagstone, new Vector3(ex, 0.028f, ez));
                    ArtLibrary.Tint(edge.GetComponent<Renderer>(), new Color(0.82f, 0.78f, 0.7f));
                }
                for (int k = 0; k < 4; k++)
                {
                    var c = MeshKit.Part(transform, "Crack", MeshKit.Box(new Vector3(k % 2 == 0 ? 0.95f : 0.6f, 0.056f, 0.035f)), Mat.Glow, new Vector3(0f, 0.03f, 0f), new Vector3(0f, k * 47f + 12f, 0f));
                    ArtLibrary.SetGlow(c.GetComponent<Renderer>(), new Color(0.16f, 0.2f, 0.26f));
                    c.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                tile = t.transform;
                return;
            }
            // The hole: a black square with broken edges, the rope's top tied off on a stake.
            var holeGo = MeshKit.Part(transform, "Hole", MeshKit.Box(new Vector3(1.1f, 0.02f, 1.1f)), Mat.Dark, Vector3.up * 0.012f);
            ArtLibrary.Tint(holeGo.GetComponent<Renderer>(), new Color(0.01f, 0.01f, 0.01f));
            hole = holeGo.transform;
            for (int k = 0; k < 4; k++)
            {
                var a = k * 90f * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.68f;
                var r = MeshKit.Part(transform, "Rim", MeshKit.Box(new Vector3(0.35f, 0.08f, 0.25f)), Mat.Rubble, p + Vector3.up * 0.04f, new Vector3(0f, k * 90f + 15f, 0f));
            }
            MeshKit.Part(transform, "Stake", MeshKit.Cylinder(0.05f, 0.04f, 0.35f, 6), Mat.Wood, new Vector3(0.75f, 0f, 0.6f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BrokeRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 0.9f, 0.8f);
            Sfx.Play(Sound.BoneBreak, at, 0.6f, 0.6f);
            Vfx.Smoke(at, 4, 0.6f, 0.8f);
            var me = NetworkPlayer.Local;
            if (me != null && (me.transform.position - at).sqrMagnitude < 25f * 25f) UI.GameHud.Feed("The floor gives way: a cellar below, and a rope.", new Color(0.85f, 0.8f, 0.6f));
            UI.Tips.OfferNow("cellar", "CRACKED FLOOR TILES hide CELLAR VAULTS: smash the tile, drop in for the loot, and climb the rope back out. The dead down there have waited a long time.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void UsedRpc(bool down, Vector3 at)
        {
            Sfx.Play(down ? Sound.Dodge : Sound.Creak, at, 0.7f, down ? 0.8f : 1.1f);
        }
    }
}
