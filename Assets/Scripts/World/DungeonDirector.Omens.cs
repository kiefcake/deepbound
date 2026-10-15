using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>Append only: synced.</summary>
    public enum Omen : byte { None, BloodMoon, Famine, RestlessDead, GlassBones }

    /// <summary>
    /// Run omens, set at the camp's Omen Stone before a run: each makes the descent harder
    /// in one way and gives something back, and every omen raises the Renown the run earns.
    /// </summary>
    public partial class DungeonDirector
    {
        public const int OmenCount = 5;
        public readonly NetworkVariable<byte> OmenValue = new NetworkVariable<byte>();
        public Omen RunOmen => (Omen)OmenValue.Value;

        public static string OmenName(Omen o) =>
            o == Omen.BloodMoon ? "Blood Moon" : o == Omen.Famine ? "Famine" : o == Omen.RestlessDead ? "Restless Dead" : o == Omen.GlassBones ? "Glass Bones" : "No Omen";

        public static string OmenText(Omen o) =>
            o == Omen.BloodMoon ? "far more elites; +50% gold"
            : o == Omen.Famine ? "healing halved; +50% embers"
            : o == Omen.RestlessDead ? "30% more of the dead; +50% loot from them"
            : o == Omen.GlassBones ? "everyone, you and the dead, hits 40% harder"
            : "";

        public static Color OmenColor(Omen o) =>
            o == Omen.BloodMoon ? new Color(0.95f, 0.15f, 0.12f) : o == Omen.Famine ? new Color(0.75f, 0.6f, 0.3f)
            : o == Omen.RestlessDead ? new Color(0.6f, 0.4f, 1f) : o == Omen.GlassBones ? new Color(0.7f, 0.95f, 1f) : new Color(0.6f, 0.62f, 0.7f);

        /// <summary>Renown multiplier for the run's omen.</summary>
        public static float OmenRenown(Omen o) => o == Omen.None ? 1f : 1.35f;

        // Effects, read on the server where the systems run.
        public static Omen Current => Instance != null ? Instance.RunOmen : Omen.None;
        public static float OmenGold => Current == Omen.BloodMoon ? 1.5f : Current == Omen.RestlessDead ? 1.5f : 1f;
        public static float OmenElites => Current == Omen.BloodMoon ? 1.8f : 1f;
        public static float OmenCrowd => Current == Omen.RestlessDead ? 1.3f : 1f;
        public static float OmenEmbers => Current == Omen.Famine ? 1.5f : 1f;

        public void ServerSetOmen(Omen o)
        {
            if (RunInProgress) return;
            OmenValue.Value = (byte)o;
        }

        /// <summary>Server: push the omen's global numbers into combat for this floor (or clear them at camp).</summary>
        private void ApplyOmen(bool onFloor)
        {
            var o = onFloor ? RunOmen : Omen.None;
            Health.OmenDamageScale = o == Omen.GlassBones ? 1.4f : 1f;
            Health.OmenPlayerHealScale = o == Omen.Famine ? 0.5f : 1f;
        }
    }
}
