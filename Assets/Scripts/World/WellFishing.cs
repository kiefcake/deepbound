using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// FISHING THE WELL (2026-11-08, camp pastimes: "something quiet to do while friends shop"). F at the well drops a line
    /// into the dark water. Wait: after a few seconds something BITES (a splash, the rope jerks, the prompt says so) and you
    /// have <see cref="Window"/> seconds to press F again and haul it up. Too early or too late and it gets away. Mostly
    /// junk worth a coin or two; sometimes drowned coins, an ember stone, a stoppered health potion; very rarely the
    /// Well Pearl. <see cref="MaxCatches"/> catches a camp visit each, so it stays a pastime and not a mine.
    /// </summary>
    public class WellFishing : Interactable
    {
        public const int MaxCatches = 6;
        public const float Window = 0.8f, MinWait = 2.5f, MaxWait = 8f;

        private class Line { public double BiteAt; public int Caught; public int Visit; public bool Cast; }
        private readonly Dictionary<ulong, Line> lines = new Dictionary<ulong, Line>();

        // Local (the owner's view of their own line).
        private double localBite = -1;
        private int localCaught;
        private Transform rope, bob;
        private readonly List<(double bite, float until)> showing = new List<(double, float)>();
        private static double Now => NetworkManager.Singleton != null ? NetworkManager.Singleton.ServerTime.Time : Time.timeAsDouble;

        public override float Range => 2.6f;

        private static int Visit => DungeonDirector.Instance != null ? DungeonDirector.Instance.NextDepth.Value * 1000 + (int)(DungeonDirector.Instance.RunStartedAt.Value % 997) : 0;

        public override string Prompt(PlayerProgress who)
        {
            bool mine = NetworkManager.Singleton != null && who.OwnerClientId == NetworkManager.Singleton.LocalClientId;
            if (mine && localBite > 0)
            {
                double dt = Now - localBite;
                if (dt >= 0 && dt <= Window) return "SOMETHING BITES!  -  F to haul it up";
                return dt > Window ? "It got away...  (F to try again)" : "Line in the water... wait for the bite  (F pulls it up early)";
            }
            if (mine && localCaught >= MaxCatches) return "The well's fished out for this visit";
            return "Fish the well  -  drop a line, wait for the bite, F to haul it up  (" + (MaxCatches - (mine ? localCaught : 0)) + " left this visit)";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (!lines.TryGetValue(who.OwnerClientId, out var line)) lines[who.OwnerClientId] = line = new Line();
            if (line.Visit != Visit) { line.Visit = Visit; line.Caught = 0; line.Cast = false; }
            if (!line.Cast)
            {
                if (line.Caught >= MaxCatches) return;
                line.Cast = true;
                line.BiteAt = Now + Random.Range(MinWait, MaxWait);
                CastRpc(who.OwnerClientId, line.BiteAt, line.Caught);
                return;
            }
            line.Cast = false;
            double dt = Now - line.BiteAt;
            // A little slack for the round trip.
            if (dt < -0.15 || dt > Window + 0.35) { MissRpc(who.OwnerClientId, dt < 0); return; }
            line.Caught++;
            ServerCatch(who, line.Caught);
        }

        /// <summary>Shots: drop a line for someone with the bite <paramref name="wait"/> seconds away.</summary>
        public void ServerDebugCast(PlayerProgress who, float wait)
        {
            if (!lines.TryGetValue(who.OwnerClientId, out var line)) lines[who.OwnerClientId] = line = new Line();
            line.Visit = Visit; line.Cast = true; line.BiteAt = Now + wait;
            CastRpc(who.OwnerClientId, line.BiteAt, line.Caught);
        }

        /// <summary>Server: what comes up. Public for tests.</summary>
        public string ServerCatch(PlayerProgress who, int caught)
        {
            float r = Random.value;
            string what;
            int gold = 0, embers = 0;
            bool rare = false;
            if (r < 0.45f)
            {
                string[] junk = { "an old boot", "a rusted key", "a broken lantern", "somebody's teeth", "a drowned rat (ugh)", "a bent spoon", "a love letter, unreadable" };
                what = junk[Random.Range(0, junk.Length)];
                gold = Random.Range(1, 4);
            }
            else if (r < 0.75f) { what = "a fistful of drowned coins"; gold = Random.Range(6, 21); }
            else if (r < 0.9f) { what = "a cold ember stone"; embers = Random.Range(6, 13); }
            else if (r < 0.97f)
            {
                var pouch = who.GetComponent<Flasks>();
                if (pouch != null && pouch.Count(Flasks.Kind.Health) < Flasks.Max) { pouch.ServerAdd(Flasks.Kind.Health, 1); what = "a stoppered bottle - a health potion"; }
                else { what = "a stoppered bottle (sold to Silas)"; gold = 15; }
            }
            else { what = "THE WELL PEARL"; gold = 80; rare = true; }
            if (gold > 0) who.ServerAddGold(gold);
            if (embers > 0) DungeonDirector.Instance?.ServerDropEmbers(who.transform.position + Vector3.up * 0.5f, embers);
            CatchRpc(who.OwnerClientId, who.name, what, gold, rare, caught);
            return what;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CastRpc(ulong client, double biteAt, int caught)
        {
            showing.Add((biteAt, Time.time + (float)(biteAt - Now) + Window + 2f));
            Sfx.Play(Sound.Splash, transform.position + Vector3.up * 0.3f, 0.5f, 1.4f);
            if (NetworkManager != null && client == NetworkManager.LocalClientId) { localBite = biteAt; localCaught = caught; }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MissRpc(ulong client, bool early)
        {
            if (NetworkManager != null && client == NetworkManager.LocalClientId)
            {
                localBite = -1;
                UI.GameHud.ShowCombo(transform.position + Vector3.up * 1.6f, early ? "TOO SOON" : "IT GOT AWAY", new Color(0.75f, 0.75f, 0.8f));
            }
            showing.Clear();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CatchRpc(ulong client, string who, string what, int gold, bool rare, int caught)
        {
            showing.Clear();
            Sfx.Play(Sound.Splash, transform.position + Vector3.up * 0.4f, 0.9f, 1f);
            if (gold >= 6) Sfx.Play(Sound.Coins, transform.position + Vector3.up, 0.8f, 1.1f);
            var color = rare ? new Color(1f, 0.85f, 0.4f) : new Color(0.7f, 0.85f, 0.95f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 1.8f, rare ? "THE WELL PEARL!" : what.ToUpperInvariant(), color);
            var me = NetworkPlayer.Local;
            bool near = me != null && Vector3.Distance(me.transform.position, transform.position) < 14f;
            if (near || rare) UI.GameHud.Feed(who + " fishes up " + what + (gold > 0 ? "  (+" + gold + " gold)" : ""), color);
            if (rare) { Sfx.Play(Sound.Relic, transform.position + Vector3.up, 1f, 1f); Vfx.Embers(transform.position + Vector3.up * 1.2f, 20, color, 4f, 0.05f, 1f); }
            if (NetworkManager != null && client == NetworkManager.LocalClientId) { localBite = -1; localCaught = caught; }
        }

        private void Update()
        {
            // The rope down the shaft, and the float that ducks when something bites.
            if (rope == null) return;
            for (int i = showing.Count - 1; i >= 0; i--) if (Time.time > showing[i].until) showing.RemoveAt(i);
            bool active = showing.Count > 0;
            rope.gameObject.SetActive(active);
            bob.gameObject.SetActive(active);
            if (!active) return;
            double bite = showing[showing.Count - 1].bite;
            double dt = Now - bite;
            bool biting = dt >= 0 && dt <= Window;
            float jerk = biting ? Mathf.Sin(Time.time * 40f) * 0.04f : Mathf.Sin(Time.time * 2f) * 0.01f;
            bob.localPosition = new Vector3(0.3f, 0.86f + (biting ? -0.05f : 0f) + jerk, 0f);
            if (biting && Time.frameCount % 20 == 0) Sfx.Play(Sound.Splash, transform.position + Vector3.up * 0.3f, 0.35f, 1.8f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            rope = MeshKit.Part(transform, "FishLine", MeshKit.Box(new Vector3(0.015f, 1.3f, 0.015f)), Mat.Cloth, new Vector3(0.3f, 1.5f, 0f)).transform; // from the crossbeam (2.15) to the water (0.82)
            ArtLibrary.Tint(rope.GetComponent<Renderer>(), new Color(0.75f, 0.7f, 0.6f));
            bob = MeshKit.Pivot(transform, "Float", new Vector3(0.3f, 0.86f, 0f));
            var f = MeshKit.Part(bob, "FloatBall", MeshKit.Ball(0.05f, 6, 4), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(f.GetComponent<Renderer>(), new Color(0.8f, 0.15f, 0.12f));
            rope.gameObject.SetActive(false);
            bob.gameObject.SetActive(false);
        }
    }
}
