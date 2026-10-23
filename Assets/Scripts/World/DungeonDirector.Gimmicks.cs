using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// Each floor's own rule, layered over its rooms:
    ///
    /// <list type="bullet">
    /// <item><b>The Ossuary — Restless Bones.</b> Fallen skeletons leave a twitching bone
    ///   pile that pulls itself back together after a few seconds. Stamp it out (walk over
    ///   it) before it rises.</item>
    /// <item><b>The Drowned Vaults — The Tide.</b> Every minute or so the water surges knee
    ///   deep: everyone on the floor is slowed, and frost freezes whatever stands in it. Get
    ///   up on a balcony, a stair or a stone.</item>
    /// <item><b>The Hollow Throne — Soul Lanterns.</b> Lanterns of trapped souls hang in every
    ///   fight; smash one to set them free — a burst that heals the party and stuns the dead
    ///   around it.</item>
    /// <item><b>The Black Cathedral — The Bell.</b> The great bell tolls through the nave: a
    ///   shockwave rolls out across whichever room you are in. Jump it.</item>
    /// </list>
    /// </summary>
    public partial class DungeonDirector
    {
        public static string GimmickHint(int floor) => ActHint(Act(floor));
        private static string ActHint(int depth) =>
            depth == 0 ? "Restless Bones: stamp out the bone piles before the dead rise again"
            : depth == 1 ? "The Mire: when the water thickens, it drags at your legs - get to high ground"
            : depth == 2 ? "Soul Lanterns: smash one to heal your party and stun the dead"
            : depth == 3 ? "The Bell: when it tolls, jump the shockwave"
            : depth == 4 ? "The Silence: now and then the light fails. Stay together, and watch for the red !"
            : "";

        public int DebugPileCount => bonePiles.Count;

        /// <summary>Set by the automated tests: bone piles are made but never rise (the test player stands still).</summary>
        public static bool TestMode;
        public void ServerDebugTide() => TideStart.Value = NetworkManager.ServerTime.Time - TideRise;

        private void ResetGimmicks()
        {
            bonePiles.Clear();
            risen.Clear();
            waves.Clear();
            nextToll = 0f;
            if (IsServer)
            {
                TideStart.Value = -1000;
                nextTide = 0f;
            }
        }

        private void TickGimmicks()
        {
            TickBoneChoir();
            if (Layout == null) return;
            switch (Act(Layout.Depth))
            {
                case 0: TickBones(); break;
                case 1: TickTide(); break;
                case 3: TickBell(); break;
                case 4: TickSilence(); break;
            }
            // Gravebound elites leave bones on any floor.
            if (Act(Layout.Depth) != 0 && bonePiles.Count > 0) TickBones();
            TickWaves();
        }

        // ------------------------------------------------------------------
        // Floor 1: Restless Bones
        // ------------------------------------------------------------------

        private struct BonePile { public int Id; public Vector3 At; public float RiseAt; public NetworkObject Prefab; }
        private readonly List<BonePile> bonePiles = new List<BonePile>();
        private readonly HashSet<ulong> risen = new HashSet<ulong>();
        private int nextPileId;
        private const float PileRiseSeconds = 7f, StompRadius = 1.6f;
        public const float GraveboundRiseSeconds = 6f;

        /// <summary>Server, for tests: bone piles waiting to rise.</summary>
        public int BonePileCount => bonePiles.Count;
        /// <summary>Server, for tests: Gravebound piles waiting to rise.</summary>
        /// <summary>Server, for tests: Gravebound piles laid this session (a count, so an ally stamping one at once cannot hide it).</summary>
        public int GraveboundPilesMade { get; private set; }
        public int GraveboundPileCount { get { int n = 0; foreach (var p in bonePiles) if (p.Prefab != null) n++; return n; } }

        /// <summary>
        /// Server: a Gravebound elite fell (2026-10-20). Its bones pile up and rise as one of its kind (not an
        /// elite, at full strength for the floor) after <see cref="GraveboundRiseSeconds"/>, unless stamped.
        /// Works on every floor, not only the Ossuary.
        /// </summary>
        public void ServerGravebound(EnemyBrain brain)
        {
            if (Layout == null || brain == null) return;
            NetworkObject prefab = null;
            foreach (var p in AllEnemyPrefabNetworkObjects())
                if (p != null && p.TryGetComponent<EnemyBrain>(out var b) && b.GetType() == brain.GetType()) { prefab = p; break; }
            if (prefab == null) prefab = skeletonPrefab;
            var at = brain.transform.position;
            int id = nextPileId++;
            GraveboundPilesMade++;
            bonePiles.Add(new BonePile { Id = id, At = at, RiseAt = Time.time + GraveboundRiseSeconds, Prefab = prefab });
            BonePileRpc(id, at, GraveboundRiseSeconds);
            GraveboundRpc(at + Vector3.up * 1.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void GraveboundRpc(Vector3 at) => UI.GameHud.ShowCombo(at, "IT WILL RISE - STAMP ITS BONES", new Color(0.75f, 0.8f, 0.7f));

        /// <summary>Server: a kill — if it was a plain skeleton on the Ossuary, it leaves a pile.</summary>
        private void OnKillForBones(Health target)
        {
            if (Layout == null || Act(Layout.Depth) != 0 || skeletonPrefab == null) return;
            if (!target.TryGetComponent<EnemyBrain>(out var brain) || brain.GetType() != typeof(EnemyBrain)) return;
            if (brain.Elite.Value || risen.Contains(target.NetworkObjectId)) return;
            if (Layout.BossRoom.Contains(target.transform.position, 1f)) return;
            var at = target.transform.position;
            int id = nextPileId++;
            bonePiles.Add(new BonePile { Id = id, At = at, RiseAt = Time.time + PileRiseSeconds });
            BonePileRpc(id, at, PileRiseSeconds);
        }

        private void TickBones()
        {
            for (int i = bonePiles.Count - 1; i >= 0; i--)
            {
                var pile = bonePiles[i];
                bool stomped = false;
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    var d = p.transform.position - pile.At;
                    d.y = 0f;
                    if (d.magnitude <= StompRadius && Mathf.Abs(p.transform.position.y - pile.At.y) < 1.2f) { stomped = true; break; }
                }
                if (stomped)
                {
                    bonePiles.RemoveAt(i);
                    BonePileGoneRpc(pile.Id, true);
                    if (UnityEngine.Random.value < 0.3f) ServerDropEmbers(pile.At + Vector3.up * 0.3f, 1);
                    continue;
                }
                if (Time.time < pile.RiseAt || TestMode) continue;
                bonePiles.RemoveAt(i);
                BonePileGoneRpc(pile.Id, false);
                var at = pile.At;
                if (NavMesh.SamplePosition(at, out var hit, 2f, NavMesh.AllAreas)) at = hit.position;
                var obj = Spawn(pile.Prefab != null ? pile.Prefab : skeletonPrefab, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                obj.GetComponent<EnemyBrain>().ServerScale(Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count), Layout.Depth);
                risen.Add(obj.NetworkObjectId);
                spawned.Add(obj);
                RiseFxRpc(new[] { at });
            }
        }

        private static readonly Dictionary<int, GameObject> pileVisuals = new Dictionary<int, GameObject>();

        [Rpc(SendTo.ClientsAndHost)]
        private void BonePileRpc(int id, Vector3 at, float seconds)
        {
            var go = Art.BonePileMarker.Spawn(at, seconds);
            pileVisuals[id] = go;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BonePileGoneRpc(int id, bool stomped)
        {
            if (pileVisuals.TryGetValue(id, out var go) && go != null)
            {
                if (stomped)
                {
                    Sfx.Play(Sound.BoneBreak, go.transform.position + Vector3.up * 0.3f, 0.9f, 1.3f);
                    BoneBurst.Spawn(go.transform.position + Vector3.up * 0.2f, 0.7f, false, Mat.Bone);
                    UI.GameHud.ShowCombo(go.transform.position + Vector3.up * 0.8f, "SCATTERED", new Color(0.92f, 0.88f, 0.75f));
                }
                Destroy(go);
            }
            pileVisuals.Remove(id);
        }

        // ------------------------------------------------------------------
        // Floor 2: The Tide
        // ------------------------------------------------------------------

        /// <summary>Server time the current (or last) tide began; synced so everyone draws the water.</summary>
        public readonly NetworkVariable<double> TideStart = new NetworkVariable<double>(-1000);
        private float nextTide;
        public const float TideSeconds = 14f, TideRise = 2.5f, TidePeak = 0.55f;

        /// <summary>0..1: how thick the Drowned Vaults' water is right now (it rises and turns to mire).</summary>
        public static float Thickness
        {
            get
            {
                var nm = NetworkManager.Singleton;
                if (Instance == null || nm == null || !nm.IsListening || Layout == null || Act(Layout.Depth) != 1) return 0f;
                float t = (float)(nm.ServerTime.Time - Instance.TideStart.Value);
                if (t < 0f || t > TideSeconds) return 0f;
                return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / TideRise) * Mathf.Clamp01((TideSeconds - t) / TideRise));
            }
        }

        /// <summary>Anyone: the water line right now on a tidal floor (or -10 when there is none).</summary>
        public static float TideLevel
        {
            get
            {
                if (Instance == null || Layout == null || Act(Layout.Depth) != 1) return -10f;
                var nm = NetworkManager.Singleton;
                if (nm == null || !nm.IsListening) return -10f;
                float t = (float)(nm.ServerTime.Time - Instance.TideStart.Value);
                if (t < 0f || t > TideSeconds) return -10f;
                float k = Mathf.Clamp01(t / TideRise) * Mathf.Clamp01((TideSeconds - t) / TideRise);
                return Mathf.Lerp(DungeonTheme.ForAct(1).WaterDepth, TidePeak, Mathf.SmoothStep(0f, 1f, k));
            }
        }

        public static bool TideHigh => TideLevel > 0.5f;

        private void TickTide()
        {
            if (nextTide <= 0f) { nextTide = Time.time + 35f; return; }
            if (Time.time < nextTide) return;
            nextTide = Time.time + 45f;
            TideStart.Value = NetworkManager.ServerTime.Time + 3.0;
            TideWarnRpc();
        }

        private int tideCardFloor = int.MinValue, bellFeedFloor = int.MinValue, silenceFeedFloor = int.MinValue;

        [Rpc(SendTo.ClientsAndHost)]
        private void TideWarnRpc()
        {
            // The full card the first time on a floor; after that a feed line and the sound (a card every 45 s
            // was a dozen cards a floor in the autoplay run).
            int floor = Floor.Value.y;
            if (tideCardFloor != floor) { tideCardFloor = floor; UI.GameHud.ShowTitleCard("THE WATER THICKENS", "It drags at your legs: get to high ground", new Color(0.55f, 0.75f, 0.45f)); }
            else UI.GameHud.Feed("The water thickens - get to high ground", new Color(0.55f, 0.75f, 0.45f));
            Sfx.Play2D(Sound.Splash, 1f);
            Sfx.Play2D(Sound.Whisper, 0.6f);
        }

        private Transform tideWater;
        private float nextBubble;

        /// <summary>Everyone: move the floor's water sheet with the tide.</summary>
        private void UpdateTideVisual()
        {
            if (Layout == null || Act(Layout.Depth) != 1 || built == null) { tideWater = null; return; }
            if (tideWater == null) tideWater = built.transform.Find("Water");
            if (tideWater == null) return;
            float level = TideLevel;
            var p = tideWater.position;
            p.y = level > -5f ? level : DungeonTheme.ForAct(1).WaterDepth;
            tideWater.position = p;
            // Thickening: the clear green murk turns to an opaque brown-green mire, its ripples slowing to a crawl.
            float thick = Thickness;
            var rend = tideWater.GetComponent<Renderer>();
            if (rend != null)
            {
                var baseCol = DungeonTheme.ForAct(1).WaterColor;
                ArtLibrary.Tint(rend, Color.Lerp(baseCol, new Color(0.13f, 0.12f, 0.05f, 0.97f), thick));
            }
            Art.WaterFlow.Sluggish = thick;
            // Bubbles of marsh gas around you while it is thick.
            var cam = Art.ViewCamera.Current;
            if (thick > 0.3f && cam != null && Time.time >= nextBubble)
            {
                nextBubble = Time.time + 0.18f;
                var at = cam.transform.position + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
                at.y = p.y + 0.02f;
                Art.Vfx.Ring(at, Random.Range(0.12f, 0.3f), new Color(0.35f, 0.4f, 0.22f, 0.6f), 0.7f);
            }
        }

        // ------------------------------------------------------------------
        // Floor 4: The Bell
        // ------------------------------------------------------------------

        private float nextToll;

        private float nextSilence;

        /// <summary>The Abyss: every ~40 s the light fails for seven seconds.</summary>
        private void TickSilence()
        {
            if (nextSilence <= 0f) { nextSilence = Time.time + 25f; return; }
            if (Time.time < nextSilence) return;
            nextSilence = Time.time + 40f;
            SilenceRpc(7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SilenceRpc(float seconds)
        {
            Art.TheSilence.Begin(seconds);
            if (silenceFeedFloor != Floor.Value.y) { silenceFeedFloor = Floor.Value.y; UI.GameHud.Feed("The light fails - it comes back in a few seconds", new Color(0.6f, 0.45f, 1f)); }
        }

        private void TickBell()
        {
            if (nextToll <= 0f) { nextToll = Time.time + 30f; return; }
            if (Time.time < nextToll) return;
            nextToll = Time.time + 42f;
            var rooms = new HashSet<DungeonLayout.Room>();
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var room = Layout.RoomAt(p.transform.position, 0.5f);
                // The Cardinal rings its own bell.
                if (room != null && room.Kind != RoomKind.Boss && room.Kind != RoomKind.Start) rooms.Add(room);
            }
            BellTollRpc();
            foreach (var room in rooms)
            {
                var origin = room.Center + (DungeonLayout.InLowArea(room, room.Center) && room.Feature == RoomFeature.SunkenCourt ? Vector3.down * DungeonLayout.SunkenDepth : Vector3.zero);
                LaunchWave(origin, 9f, Mathf.Max(room.Size.x, room.Size.y) * 0.75f, 16f + 6f * Tier(Layout.Depth), 2.2f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BellTollRpc()
        {
            Sfx.Play2D(Sound.Toll, 1f);
            Sfx.Play2D(Sound.Slam, 0.35f);
            // Said once a floor; after that the bell itself is the warning (a line every 42 s was a dozen a floor).
            if (bellFeedFloor != Floor.Value.y) { bellFeedFloor = Floor.Value.y; UI.GameHud.Feed("The bell tolls - jump its shockwave", new Color(1f, 0.75f, 0.45f)); }
            var local = NetworkPlayer.Local;
            if (local != null) local.AddShake(0.25f);
        }

        // ------------------------------------------------------------------
        // Shockwaves outside boss fights (the Bell): jumpable rings
        // ------------------------------------------------------------------

        private class Wave { public Vector3 Origin; public float Start, Speed, Max, Damage; public readonly HashSet<PlayerLife> Hit = new HashSet<PlayerLife>(); }
        private readonly List<Wave> waves = new List<Wave>();

        private void LaunchWave(Vector3 origin, float speed, float max, float damage, float delay)
        {
            waves.Add(new Wave { Origin = origin, Start = Time.time + delay, Speed = speed, Max = max, Damage = damage });
            WaveFxRpc(origin, speed, max, delay);
        }

        private void TickWaves()
        {
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var w = waves[i];
                if (Time.time < w.Start) continue;
                float r = (Time.time - w.Start) * w.Speed;
                if (r > w.Max + 1f) { waves.RemoveAt(i); continue; }
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable || w.Hit.Contains(p)) continue;
                    var d = p.transform.position - w.Origin;
                    if (d.y > 0.45f || d.y < -0.6f) continue; // airborne, or on another level
                    d.y = 0f;
                    if (Mathf.Abs(d.magnitude - r) > 0.8f) continue;
                    w.Hit.Add(p);
                    p.Health.ServerDamage(w.Damage, w.Origin + Vector3.up, ulong.MaxValue);
                    if (p.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(d.normalized * 6f + Vector3.up * 3f);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void WaveFxRpc(Vector3 origin, float speed, float max, float delay)
        {
            Telegraph.Show(origin, 2f, delay, null, new Color(1f, 0.7f, 0.35f, 0.35f));
            StartCoroutine(WaveAfter(origin, speed, max, delay));
        }

        private System.Collections.IEnumerator WaveAfter(Vector3 origin, float speed, float max, float delay)
        {
            yield return new WaitForSeconds(delay);
            ShockwaveRing.Spawn(origin, speed, max, new Color(1f, 0.72f, 0.38f));
            Sfx.Play(Sound.Slam, origin + Vector3.up, 0.8f, 0.55f);
        }
    }
}
