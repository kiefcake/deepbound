using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public partial class DungeonDirector
    {
        public const float ChokeSeconds = 2.4f, ChokeReach = 2.6f;

        public readonly NetworkVariable<ulong> ChokedJets = new NetworkVariable<ulong>();

        public bool JetChoked(int index) => index >= 0 && index < 64 && (ChokedJets.Value & (1UL << index)) != 0;

        public static bool JetBurning(int index, int phase, double time) => FlameJet.Lit(phase, time) && (Instance == null || !Instance.JetChoked(index));

        public static bool BesideJet(DungeonLayout.FlameTrap trap, Vector3 p)
        {
            var d = p - trap.At;
            if (Mathf.Abs(d.y) > 1.5f) return false;
            var along = Vector3.Cross(trap.Across, Vector3.up);
            float a = Mathf.Abs(Vector3.Dot(d, along)), c = Mathf.Abs(Vector3.Dot(d, trap.Across));
            return a > FlameJet.HalfWidth && a < ChokeReach && c < DungeonLayout.CorridorWidth / 2f + 0.3f;
        }

        public static int JetBeside(Vector3 p)
        {
            if (Layout == null || Instance == null) return -1;
            for (int i = 0; i < Layout.FlameTraps.Count && i < 64; i++)
                if (!Instance.JetChoked(i) && BesideJet(Layout.FlameTraps[i], p)) return i;
            return -1;
        }

        [Rpc(SendTo.Server)]
        public void ChokeJetRpc(int index, NetworkObjectReference who)
        {
            if (!who.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var p)) return;
            ServerChokeJet(index, p);
        }

        public bool ServerChokeJet(int index, PlayerProgress by)
        {
            if (!IsServer || Layout == null || by == null || index < 0 || index >= Layout.FlameTraps.Count || index >= 64 || JetChoked(index)) return false;
            if (!by.CanDisarm || !BesideJet(Layout.FlameTraps[index], by.transform.position)) return false;
            bool rogue = by.ClassId.Value == Classes.ClassId.Rogue;
            if (!rogue) by.Picks.Value--;
            ChokedJets.Value |= 1UL << index;
            JetChokedRpc(index, by.name, rogue);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JetChokedRpc(int index, string who, bool rogue)
        {
            if (Layout == null || index >= Layout.FlameTraps.Count) return;
            var at = Layout.FlameTraps[index].At;
            Art.Sfx.Play(Art.Sound.Creak, at + Vector3.up, 0.8f, 0.7f);
            Art.Sfx.Play(Art.Sound.Block, at + Vector3.up, 0.5f, 1.4f);
            Art.Vfx.Embers(at + Vector3.up * 0.4f, 14, new Color(0.45f, 0.42f, 0.4f), 1.4f, 0.06f, 1.2f);
            var me = NetworkPlayer.Local;
            if (me != null && (me.transform.position - at).sqrMagnitude < 20f * 20f)
                UI.GameHud.Feed(who + (rogue ? " packs the vent with rag and a knife blade. It coughs and goes dark." : " jams the vent's grate with a pick. It coughs and goes dark."), new Color(0.85f, 0.85f, 0.7f));
        }

        public static string ChokePrompt(PlayerProgress p)
        {
            if (p == null || JetBeside(p.transform.position) < 0) return null;
            UI.Tips.OfferNow("choke", "FLAME VENTS CAN BE CHOKED: kneel beside the grate (out of the fire) and hold F. A Rogue needs nothing; anyone else spends one of Hobb's Tinker's Picks.");
            return p.CanDisarm ? "Hold F to choke the flame vent" + (p.ClassId.Value == Classes.ClassId.Rogue ? "" : "  (" + p.Picks.Value + " picks)") : "A flame vent  -  Tinker's Picks (Hobb) or a Rogue could choke it";
        }
    }
}
