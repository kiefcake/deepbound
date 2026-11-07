using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    /// <summary>
    /// Crafting state and RPCs (2026-11-07, see <see cref="Crafting"/>). <see cref="Parts"/> packs, 16 bits each: bone (0),
    /// cloth (16), iron (32); then crafted upgrades, 2 bits each: lining (48), edge (50), boots (52); the readied tool (54,
    /// 0 none / 1 caltrops / 2 rattle); caltrops held (56, 4 bits) and rattles held (60, 4 bits). Cleared with the run.
    /// </summary>
    public partial class PlayerProgress
    {
        public readonly NetworkVariable<ulong> Parts = new NetworkVariable<ulong>();

        private static int Field(ulong v, int shift, int bits) => (int)((v >> shift) & ((1UL << bits) - 1));
        private static ulong WithField(ulong v, int shift, int bits, int value)
        {
            ulong mask = ((1UL << bits) - 1) << shift;
            return (v & ~mask) | (((ulong)Mathf.Max(0, value) << shift) & mask);
        }

        public int PartCount(Part p) => Field(Parts.Value, 16 * (int)p, 16);
        public int Lining => Field(Parts.Value, 48, 2);
        public int Whetted => Field(Parts.Value, 50, 2);
        public int Boots => Field(Parts.Value, 52, 2);
        public CraftKind? ReadiedTool => Field(Parts.Value, 54, 2) switch { 1 => CraftKind.Caltrops, 2 => CraftKind.Rattle, _ => (CraftKind?)null };
        public int ToolCount(CraftKind k) => k == CraftKind.Caltrops ? Field(Parts.Value, 56, 4) : k == CraftKind.Rattle ? Field(Parts.Value, 60, 4) : 0;

        private int UpgradeShift(CraftKind k) => k == CraftKind.Lining ? 48 : k == CraftKind.Edge ? 50 : 52;
        private int ToolShift(CraftKind k) => k == CraftKind.Caltrops ? 56 : 60;

        /// <summary>Server: add (or with a negative amount, take) parts, capped.</summary>
        public void ServerAddPart(Part p, int amount)
        {
            if (!IsServer || amount == 0) return;
            Parts.Value = WithField(Parts.Value, 16 * (int)p, 16, Mathf.Clamp(PartCount(p) + amount, 0, Crafting.PartCap));
        }

        /// <summary>Why this recipe cannot be made now, or null.</summary>
        public string CannotCraftReason(int index)
        {
            if (index < 0 || index >= Crafting.Recipes.Length) return "Unknown";
            var r = Crafting.Recipes[index];
            switch (r.Kind)
            {
                case CraftKind.Potion:
                    var pouch = GetComponent<Flasks>();
                    if (pouch != null && pouch.IsFull((Flasks.Kind)r.Arg)) return "Pouch full";
                    break;
                case CraftKind.Lining: case CraftKind.Edge: case CraftKind.Boots:
                    if (Field(Parts.Value, UpgradeShift(r.Kind), 2) >= Crafting.MaxUpgrade) return "Done (" + Crafting.MaxUpgrade + ")";
                    break;
                case CraftKind.Caltrops: case CraftKind.Rattle:
                    if (ToolCount(r.Kind) >= Crafting.ToolCap(this)) return "Carrying " + Crafting.ToolCap(this);
                    break;
            }
            if (PartCount(Part.Bone) < r.Bone || PartCount(Part.Cloth) < r.Cloth || PartCount(Part.Iron) < r.Iron) return "Need " + Crafting.Cost(r);
            return null;
        }

        [Rpc(SendTo.Server)]
        public void CraftRpc(byte index)
        {
            if (Stats == null || CannotCraftReason(index) != null) return;
            var r = Crafting.Recipes[index];
            bool made = false;
            switch (r.Kind)
            {
                case CraftKind.Potion:
                    made = GetComponent<Flasks>()?.ServerAdd((Flasks.Kind)r.Arg, 1) > 0;
                    break;
                case CraftKind.Lining: case CraftKind.Edge: case CraftKind.Boots:
                {
                    int sh = UpgradeShift(r.Kind);
                    Parts.Value = WithField(Parts.Value, sh, 2, Field(Parts.Value, sh, 2) + 1);
                    if (r.Kind == CraftKind.Lining) ApplyToHealth(false);
                    made = true;
                    break;
                }
                case CraftKind.Caltrops: case CraftKind.Rattle:
                {
                    int sh = ToolShift(r.Kind);
                    Parts.Value = WithField(Parts.Value, sh, 4, Field(Parts.Value, sh, 4) + 1);
                    // The first tool made is the one readied.
                    if (ReadiedTool == null) Parts.Value = WithField(Parts.Value, 54, 2, r.Kind == CraftKind.Caltrops ? 1 : 2);
                    made = true;
                    break;
                }
            }
            if (!made) return;
            ServerAddPart(Part.Bone, -r.Bone);
            ServerAddPart(Part.Cloth, -r.Cloth);
            ServerAddPart(Part.Iron, -r.Iron);
            CraftedRpc(index, RpcTarget.Single(OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void CraftedRpc(byte index, RpcParams p)
        {
            Art.Sfx.Play(Art.Sound.Gear, transform.position + Vector3.up, 0.6f, 1.15f);
            UI.GameHud.Pickup("craft" + index, 1, " " + Crafting.Recipes[index].Name + " (crafted)", new Color(0.85f, 0.8f, 0.65f));
        }

        /// <summary>Owner: ready a tool for the Z key.</summary>
        [Rpc(SendTo.Server)]
        public void ReadyToolRpc(byte kind)
        {
            var k = (CraftKind)kind;
            if (k != CraftKind.Caltrops && k != CraftKind.Rattle) return;
            Parts.Value = WithField(Parts.Value, 54, 2, k == CraftKind.Caltrops ? 1 : 2);
        }

        /// <summary>Owner: throw the readied tool at a floor point. The server checks range and stock.</summary>
        [Rpc(SendTo.Server)]
        public void UseToolRpc(Vector3 at)
        {
            var tool = ReadiedTool;
            if (tool == null || ToolCount(tool.Value) <= 0) return;
            var life = GetComponent<PlayerLife>();
            if (life == null || life.Downed.Value) return;
            if (Vector3.Distance(transform.position, at) > Crafting.ToolRange + 1f) at = transform.position + Vector3.ClampMagnitude(at - transform.position, Crafting.ToolRange);
            int sh = ToolShift(tool.Value);
            Parts.Value = WithField(Parts.Value, sh, 4, Field(Parts.Value, sh, 4) - 1);
            // Out of that one: ready the other if any are left.
            if (ToolCount(tool.Value) == 0)
            {
                var other = tool.Value == CraftKind.Caltrops ? CraftKind.Rattle : CraftKind.Caltrops;
                Parts.Value = WithField(Parts.Value, 54, 2, ToolCount(other) > 0 ? (other == CraftKind.Caltrops ? 1 : 2) : 0);
            }
            var belt = GetComponent<ToolBelt>();
            if (tool.Value == CraftKind.Caltrops) belt?.ServerCaltrops(at);
            else ServerRattle(at);
            ToolFxRpc((byte)tool.Value, at);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ToolFxRpc(byte kind, Vector3 at)
        {
            var belt = GetComponent<ToolBelt>();
            if (belt != null) belt.ShowTool((CraftKind)kind, at);
        }

        /// <summary>Server: the dead within reach of the rattle stop hunting and go to the noise.</summary>
        private void ServerRattle(Vector3 at)
        {
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || e.IsDead) continue;
                if (Vector3.Distance(e.transform.position, at) > Crafting.RattleRadius) continue;
                e.ServerLure(at, Crafting.RattleSeconds);
            }
        }
    }
}
