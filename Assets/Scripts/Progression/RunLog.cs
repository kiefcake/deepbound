using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Progression
{
    public class RunLog : MonoBehaviour
    {
        public const int FormatVersion = 3;
        private const float PollSeconds = 0.5f, TickSeconds = 10f, IdleSeconds = 45f, IdleStep = 1.5f, CensusDelay = 4f;

        public static string Folder => Path.Combine(Application.persistentDataPath, "RunLogs");

        private class Member
        {
            public float Dealt, DealtBoss, Taken, Healed, DownedFor, DownedAt = -1f, StillSince, Hidden;
            public int Kills, Downs, Revives;
            public Vector3 LastSpot;
            public bool Idle;
            public int GoldAt, EmbersAt, SigilsAt, GoldEarnedAt;
            public readonly Dictionary<string, float> BySource = new Dictionary<string, float>();
            public readonly Dictionary<string, float> TakenFrom = new Dictionary<string, float>();
            public readonly Dictionary<string, int> KillsOf = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Used = new Dictionary<string, int>();
        }

        private static RunLog instance;

        private readonly Dictionary<ulong, Member> members = new Dictionary<ulong, Member>();
        private readonly Dictionary<string, float> roomTime = new Dictionary<string, float>();
        private readonly Dictionary<string, int> spawned = new Dictionary<string, int>();
        private StreamWriter file;
        private bool clientFile;
        private double runKey = -1;
        private int depth = -1, floorsLogged, partySeen, frames;
        private float floorStartedAt, guardianStartedAt = -1f, guardianSeconds = -1f, nextPoll, nextTick, runStartedAt, censusAt = -1f, lastPollAt;
        private float frameTime, worstFrame;
        private RunPhase lastPhase = RunPhase.Lobby;
        private bool lastSealed;
        private string bossName = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null || Application.isBatchMode) return;
            var go = new GameObject("RunLog");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<RunLog>();
        }

        public static bool Active => instance != null && instance.file != null && !instance.clientFile;

        public static void Note(string kind, string detail, Component who = null)
        {
            if (Active) instance.WriteEvent(kind, detail, who);
        }

        public static void NoteRevive(Component revived)
        {
            if (!Active || revived == null || !revived.TryGetComponent<NetworkObject>(out var no)) return;
            var m = instance.MemberOf(no.NetworkObjectId);
            m.Revives++;
            float downed = m.DownedAt >= 0f ? Time.time - m.DownedAt : -1f;
            if (downed >= 0f) m.DownedFor += downed;
            m.DownedAt = -1f;
            instance.WriteEvent("revived", downed >= 0f ? "after " + F(downed) + " s" : "", revived);
        }

        private void OnEnable()
        {
            Health.ServerHitTagged += OnHit;
            Health.AnyDied += OnDied;
            Health.AnyHealed += OnHealed;
            UI.GameHud.Heard += OnHeard;
        }

        private void OnDisable()
        {
            Health.ServerHitTagged -= OnHit;
            Health.AnyDied -= OnDied;
            Health.AnyHealed -= OnHealed;
            UI.GameHud.Heard -= OnHeard;
        }

        private void OnApplicationQuit()
        {
            if (file != null) EndRun("quit");
        }

        private static bool Testing => Networking.NetworkLauncher.TestRun || Cosmetics.TestMode;

        private static bool Recording
        {
            get
            {
                var net = NetworkManager.Singleton;
                return net != null && net.IsServer && !Testing
                    && DungeonDirector.Instance != null && !DungeonDirector.InTutorial && !DungeonDirector.InPractice;
            }
        }

        private static bool ClientRecording
        {
            get
            {
                var net = NetworkManager.Singleton;
                return net != null && net.IsClient && !net.IsServer && net.IsConnectedClient && !Testing
                    && DungeonDirector.Instance != null && DungeonDirector.Instance.RunInProgress;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            frames++;
            frameTime += dt;
            if (dt > worstFrame) worstFrame = dt;
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollSeconds;
            float step = lastPollAt > 0f ? Time.time - lastPollAt : 0f;
            lastPollAt = Time.time;

            if (clientFile)
            {
                if (!ClientRecording) CloseFile("disconnected");
                else if (Time.time >= nextTick) Perf(false);
                return;
            }
            if (file == null && ClientRecording)
            {
                OpenClient();
                return;
            }
            if (!Recording)
            {
                if (file != null && (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)) EndRun("left");
                return;
            }

            var dd = DungeonDirector.Instance;
            var phase = dd.Phase.Value;
            if (dd.RunInProgress && dd.RunStartedAt.Value != runKey)
            {
                if (file != null) EndRun("abandoned");
                BeginRun(dd);
            }
            if (file == null) return;

            WatchParty();
            if (phase == RunPhase.Exploring && !DungeonDirector.InLobby && (depth < 0 || dd.Floor.Value.y != depth))
            {
                if (depth >= 0) EndFloor("left");
                BeginFloor(dd);
            }
            bool onFloor = depth >= 0;
            if (onFloor && censusAt >= 0f && Time.time >= censusAt) Census("census");
            bool sealedNow = dd.BossSealedSync.Value;
            if (onFloor && phase == RunPhase.Exploring && sealedNow && !lastSealed)
            {
                guardianStartedAt = Time.time;
                bossName = Enemies.BossBrain.Active != null ? Enemies.BossBrain.Active.DisplayName : "";
                WriteEvent("guardian", bossName, null);
            }
            if (onFloor && phase == RunPhase.Exploring)
            {
                TrackPlayers(step);
                if (Time.time >= nextTick) Perf(true);
            }
            if (onFloor && (phase != RunPhase.Exploring || DungeonDirector.InLobby))
            {
                string result = phase == RunPhase.FloorCleared ? "cleared" : phase == RunPhase.Wiped ? "wiped" : phase == RunPhase.RunComplete ? "won" : "retreated";
                if (guardianStartedAt >= 0f && (phase == RunPhase.FloorCleared || phase == RunPhase.RunComplete)) guardianSeconds = Time.time - guardianStartedAt;
                EndFloor(result);
            }
            if (phase == RunPhase.Wiped) EndRun("wipe");
            else if (phase == RunPhase.RunComplete && lastPhase != RunPhase.RunComplete) EndRun("win");
            else if (DungeonDirector.InLobby && !dd.RunInProgress && floorsLogged > 0) EndRun("ended");
            lastPhase = phase;
            lastSealed = sealedNow;
        }

        private bool Open(string prefix)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, prefix + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + ".jsonl");
                file = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[RunLog] " + e.Message);
                file = null;
                return false;
            }
        }

        private void CloseFile(string result)
        {
            if (file == null) return;
            Write("{\"t\":\"end\",\"result\":" + Q(result) + ",\"floors\":" + floorsLogged + ",\"seconds\":" + F(Time.time - runStartedAt) + "}");
            try { file.Dispose(); }
            catch (Exception e) { Debug.LogWarning("[RunLog] " + e.Message); }
            file = null;
            clientFile = false;
            runKey = -1;
            depth = -1;
        }

        private void OpenClient()
        {
            if (!Open("client_")) return;
            clientFile = true;
            runStartedAt = Time.time;
            nextTick = Time.time + TickSeconds;
            Write("{\"t\":\"client\",\"v\":" + FormatVersion + ",\"build\":" + Q(Application.version) + ",\"date\":" + Q(DateTime.Now.ToString("s", CultureInfo.InvariantCulture))
                + ",\"me\":" + Q(NetworkPlayer.Local != null ? NetworkPlayer.Local.name : "") + "," + Machine() + "}");
        }

        private static string Machine()
        {
            var limits = UI.Settings.FrameLimitNames;
            return "\"machine\":{\"gpu\":" + Q(SystemInfo.graphicsDeviceName) + ",\"vram\":" + SystemInfo.graphicsMemorySize
                + ",\"cpu\":" + Q(SystemInfo.processorType) + ",\"cores\":" + SystemInfo.processorCount + ",\"ram\":" + SystemInfo.systemMemorySize
                + ",\"screen\":" + Q(Screen.currentResolution.width + "x" + Screen.currentResolution.height + " " + Screen.fullScreenMode)
                + ",\"quality\":" + UI.Settings.GraphicsQuality + ",\"retro\":" + (UI.Settings.Retro ? UI.Settings.RetroSize : -1)
                + ",\"frameLimit\":" + Q(limits[Mathf.Clamp(UI.Settings.FrameLimit, 0, limits.Length - 1)]) + "}";
        }

        private void BeginRun(DungeonDirector dd)
        {
            if (!Open("run_")) return;
            runKey = dd.RunStartedAt.Value;
            runStartedAt = Time.time;
            floorsLogged = 0;
            partySeen = PlayerLife.All.Count;
            members.Clear();
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"run\",\"v\":").Append(FormatVersion)
              .Append(",\"build\":").Append(Q(Application.version))
              .Append(",\"date\":").Append(Q(DateTime.Now.ToString("s", CultureInfo.InvariantCulture)))
              .Append(",\"difficulty\":").Append(dd.DifficultyIndex.Value)
              .Append(",\"daily\":").Append(dd.DailyRun.Value ? "true" : "false")
              .Append(",\"omen\":").Append(Q(DungeonDirector.OmenName((Omen)dd.OmenValue.Value)))
              .Append(",\"steam\":").Append(Networking.SteamPlatform.InLobby ? "true" : "false")
              .Append(',').Append(Machine())
              .Append(",\"party\":[");
            bool first = true;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.TryGetComponent<PlayerProgress>(out var pp)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":").Append(Q(p.name)).Append(",\"class\":").Append(Q(pp.ClassId.Value.ToString())).Append('}');
            }
            sb.Append("]}");
            Write(sb.ToString());
        }

        private void WatchParty()
        {
            int n = PlayerLife.All.Count;
            if (n == partySeen) return;
            WriteEvent(n > partySeen ? "joined" : "left", "party of " + n, null);
            partySeen = n;
        }

        private void BeginFloor(DungeonDirector dd)
        {
            depth = dd.Floor.Value.y;
            floorStartedAt = Time.time;
            nextTick = Time.time + TickSeconds;
            censusAt = Time.time + CensusDelay;
            guardianStartedAt = guardianSeconds = -1f;
            bossName = "";
            members.Clear();
            roomTime.Clear();
            spawned.Clear();
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.TryGetComponent<PlayerProgress>(out var pp)) continue;
                var m = MemberOf(p.NetworkObjectId);
                m.GoldAt = pp.Gold.Value;
                m.EmbersAt = pp.Embers.Value;
                m.SigilsAt = pp.Sigils.Value;
                m.GoldEarnedAt = pp.GoldEarned.Value;
                m.LastSpot = p.transform.position;
                m.StillSince = Time.time;
            }
            var layout = DungeonDirector.Layout;
            Write("{\"t\":\"floorStart\",\"depth\":" + depth + ",\"name\":" + Q(DungeonTheme.FloorName(depth))
                + ",\"event\":" + Q(DungeonDirector.CurrentEvent.ToString()) + ",\"party\":" + PlayerLife.All.Count
                + ",\"runSeconds\":" + F(Time.time - runStartedAt)
                + ",\"rooms\":" + (layout != null ? layout.Rooms.Count : 0) + ",\"layout\":" + RoomList(layout) + "}");
        }

        private static string RoomKey(DungeonLayout.Room r) => r == null ? "corridor" : r.Id + ":" + r.Kind + "/" + r.Feature;

        private static string RoomList(DungeonLayout layout)
        {
            if (layout == null) return "[]";
            var sb = new StringBuilder("[");
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(RoomKey(layout.Rooms[i])));
            }
            return sb.Append(']').ToString();
        }

        private void Census(string label)
        {
            if (label == "census") censusAt = -1f;
            var foes = new Dictionary<string, int>();
            foreach (var e in Enemies.EnemyBrain.All)
                if (e != null && !e.IsDead) Count(foes, KindOf(e.name) + (e.Elite.Value ? " (elite)" : ""));
            var things = new Dictionary<string, int>();
            foreach (var i in Interactable.All)
                if (i != null) Count(things, i.GetType().Name);
            foreach (var b in FindObjectsByType<Breakable>(FindObjectsSortMode.None))
                if (b != null) Count(things, "Breakable:" + b.Type.Value);
            int loot = 0;
            foreach (var l in Loot.LootDrop.All) if (l != null) loot++;
            if (label == "census") foreach (var kv in foes) spawned[kv.Key] = kv.Value;
            Write("{\"t\":\"" + label + "\",\"depth\":" + depth + ",\"s\":" + F(Time.time - floorStartedAt)
                + ",\"enemies\":" + Map(foes) + ",\"objects\":" + Map(things) + ",\"lootOnGround\":" + loot + "}");
        }

        private void TrackPlayers(float step)
        {
            var layout = DungeonDirector.Layout;
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var m = MemberOf(p.NetworkObjectId);
                var at = p.transform.position;
                if (p.Downed.Value)
                {
                    m.StillSince = Time.time;
                    m.LastSpot = at;
                    continue;
                }
                if (layout != null && step > 0f) Add(roomTime, RoomKey(layout.RoomAt(at)), step);
                if (p.Hidden.Value) m.Hidden += step;
                if ((at - m.LastSpot).sqrMagnitude > IdleStep * IdleStep)
                {
                    if (m.Idle) WriteEvent("idleEnd", F(Time.time - m.StillSince) + " s", p);
                    m.Idle = false;
                    m.LastSpot = at;
                    m.StillSince = Time.time;
                }
                else if (!m.Idle && Time.time - m.StillSince > IdleSeconds)
                {
                    m.Idle = true;
                    WriteEvent("idle", RoomKey(layout != null ? layout.RoomAt(at) : null), p);
                }
            }
        }

        private void Perf(bool host)
        {
            nextTick = Time.time + TickSeconds;
            float fps = frameTime > 0f ? frames / frameTime : 0f;
            var dd = DungeonDirector.Instance;
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"tick\",\"depth\":").Append(host ? depth : dd != null ? dd.Floor.Value.y : -1)
              .Append(",\"s\":").Append(F(host ? Time.time - floorStartedAt : Time.time - runStartedAt))
              .Append(",\"fps\":").Append(F(fps)).Append(",\"worstMs\":").Append(F(worstFrame * 1000f))
              .Append(",\"enemiesAlive\":").Append(Enemies.EnemyBrain.All.Count);
            frames = 0;
            frameTime = worstFrame = 0f;
            var net = NetworkManager.Singleton;
            if (!host && net != null && net.NetworkConfig.NetworkTransport != null)
                sb.Append(",\"rttMs\":").Append(net.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId));
            sb.Append(",\"players\":[");
            bool first = true;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.TryGetComponent<Health>(out var h)) continue;
                if (!first) sb.Append(',');
                first = false;
                var room = DungeonDirector.Layout != null ? DungeonDirector.Layout.RoomAt(p.transform.position) : null;
                sb.Append("{\"name\":").Append(Q(p.name)).Append(",\"hp\":").Append(F(h.Fraction))
                  .Append(",\"downed\":").Append(p.Downed.Value ? "true" : "false")
                  .Append(",\"room\":").Append(Q(RoomKey(room))).Append('}');
            }
            sb.Append("]}");
            Write(sb.ToString());
        }

        private static string Pouch(Component who)
        {
            if (who == null || !who.TryGetComponent<Flasks>(out var f)) return "";
            return "health " + f.Count(Flasks.Kind.Health) + ", draught " + f.Count(Flasks.Kind.Mana) + ", frost " + f.Count(Flasks.Kind.Frost) + ", holy " + f.Count(Flasks.Kind.Holy);
        }

        private void EndFloor(string result)
        {
            Census("leftover");
            var sb = new StringBuilder();
            sb.Append("{\"t\":\"floor\",\"depth\":").Append(depth)
              .Append(",\"name\":").Append(Q(DungeonTheme.FloorName(depth)))
              .Append(",\"event\":").Append(Q(DungeonDirector.CurrentEvent.ToString()))
              .Append(",\"result\":").Append(Q(result))
              .Append(",\"seconds\":").Append(F(Time.time - floorStartedAt))
              .Append(",\"guardian\":").Append(Q(bossName))
              .Append(",\"guardianSeconds\":").Append(F(guardianSeconds))
              .Append(",\"spawned\":").Append(Map(spawned))
              .Append(",\"roomSeconds\":").Append(Map(roomTime))
              .Append(",\"players\":[");
            bool first = true;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.TryGetComponent<PlayerProgress>(out var pp)) continue;
                var m = MemberOf(p.NetworkObjectId);
                if (m.DownedAt >= 0f)
                {
                    m.DownedFor += Time.time - m.DownedAt;
                    m.DownedAt = -1f;
                }
                if (!first) sb.Append(',');
                first = false;
                var hp = p.GetComponent<Health>();
                sb.Append("{\"name\":").Append(Q(p.name)).Append(",\"class\":").Append(Q(pp.ClassId.Value.ToString()))
                  .Append(",\"weapon\":").Append(pp.WeaponLevel.Value).Append(",\"path\":").Append(pp.WeaponPath.Value)
                  .Append(",\"maxHp\":").Append(F(hp != null ? hp.Max.Value : 0f))
                  .Append(",\"dealt\":").Append(F(m.Dealt)).Append(",\"dealtGuardian\":").Append(F(m.DealtBoss))
                  .Append(",\"taken\":").Append(F(m.Taken)).Append(",\"healed\":").Append(F(m.Healed))
                  .Append(",\"kills\":").Append(m.Kills).Append(",\"downs\":").Append(m.Downs).Append(",\"revives\":").Append(m.Revives)
                  .Append(",\"downedSeconds\":").Append(F(m.DownedFor)).Append(",\"hiddenSeconds\":").Append(F(m.Hidden))
                  .Append(",\"gold\":").Append(pp.Gold.Value).Append(",\"goldGained\":").Append(pp.GoldEarned.Value - m.GoldEarnedAt)
                  .Append(",\"embers\":").Append(pp.Embers.Value).Append(",\"embersDelta\":").Append(pp.Embers.Value - m.EmbersAt)
                  .Append(",\"sigils\":").Append(pp.Sigils.Value).Append(",\"sigilsDelta\":").Append(pp.Sigils.Value - m.SigilsAt)
                  .Append(",\"pouch\":").Append(Q(Pouch(p)))
                  .Append(",\"relics\":[");
                for (int i = 0; i < pp.RelicCount; i++) sb.Append(i > 0 ? "," : "").Append(Q(pp.RelicLabel(i)));
                sb.Append("],\"bySource\":").Append(Map(m.BySource))
                  .Append(",\"takenFrom\":").Append(Map(m.TakenFrom))
                  .Append(",\"killsOf\":").Append(Map(m.KillsOf))
                  .Append(",\"used\":").Append(Map(m.Used)).Append('}');
            }
            sb.Append("]}");
            Write(sb.ToString());
            floorsLogged++;
            depth = -1;
        }

        private void EndRun(string result)
        {
            if (file == null) return;
            if (depth >= 0) EndFloor(result == "quit" ? "quit" : "left");
            CloseFile(result);
        }

        private void WriteEvent(string kind, string detail, Component who)
        {
            string name = who != null ? who.name : "";
            if (who != null && depth >= 0 && who.TryGetComponent<NetworkObject>(out var no)) Count(MemberOf(no.NetworkObjectId).Used, kind + ":" + detail);
            Write("{\"t\":\"ev\",\"depth\":" + depth + ",\"s\":" + F(depth >= 0 ? Time.time - floorStartedAt : Time.time - runStartedAt)
                + ",\"k\":" + Q(kind) + ",\"x\":" + Q(detail) + (name.Length > 0 ? ",\"who\":" + Q(name) : "") + "}");
        }

        private void OnHeard(string kind, string text)
        {
            if (file == null || kind == "pickup") return;
            Write("{\"t\":\"msg\",\"depth\":" + depth + ",\"s\":" + F(depth >= 0 ? Time.time - floorStartedAt : Time.time - runStartedAt) + ",\"k\":" + Q(kind) + ",\"x\":" + Q(text) + "}");
        }

        private static string KindOf(string objectName) => objectName.Replace("(Clone)", "").Trim();

        private void OnHit(Health target, float dealt, ulong attacker, string tag)
        {
            if (!Active || depth < 0 || dealt <= 0f) return;
            if (target.Team == Team.Players)
            {
                var victim = MemberOf(target.NetworkObjectId);
                victim.Taken += dealt;
                Add(victim.TakenFrom, Health.NameOf(attacker) + (string.IsNullOrEmpty(tag) ? "" : " (" + tag + ")"), dealt);
                return;
            }
            if (!IsPlayer(attacker)) return;
            var m = MemberOf(attacker);
            m.Dealt += dealt;
            if (target.TryGetComponent<Enemies.BossBrain>(out _)) m.DealtBoss += dealt;
            Add(m.BySource, string.IsNullOrEmpty(tag) ? "other" : tag, dealt);
        }

        private void OnHealed(Health h, float amount)
        {
            if (!Active || depth < 0 || amount <= 0f || h.Team != Team.Players) return;
            MemberOf(h.NetworkObjectId).Healed += amount;
        }

        private void OnDied(Health h)
        {
            if (!Active || depth < 0) return;
            float at = Time.time - floorStartedAt;
            if (h.Team == Team.Players)
            {
                var victim = MemberOf(h.NetworkObjectId);
                victim.Downs++;
                victim.DownedAt = Time.time;
                var room = DungeonDirector.Layout != null ? DungeonDirector.Layout.RoomAt(h.transform.position) : null;
                int standing = 0;
                foreach (var p in PlayerLife.All) if (p != null && !p.Downed.Value) standing++;
                Write("{\"t\":\"down\",\"depth\":" + depth + ",\"s\":" + F(at) + ",\"who\":" + Q(h.name) + ",\"by\":" + Q(Health.NameOf(h.LastHitFrom))
                    + ",\"room\":" + Q(RoomKey(room)) + ",\"standing\":" + standing + ",\"guardianFight\":" + (guardianStartedAt >= 0f ? "true" : "false")
                    + ",\"pouch\":" + Q(Pouch(h)) + "}");
                return;
            }
            if (h.Team != Team.Enemies) return;
            string kind = KindOf(h.name);
            if (IsPlayer(h.LastHitFrom))
            {
                var m = MemberOf(h.LastHitFrom);
                m.Kills++;
                Count(m.KillsOf, kind);
            }
            if (h.TryGetComponent<Enemies.BossBrain>(out var boss))
                Write("{\"t\":\"guardianDown\",\"depth\":" + depth + ",\"s\":" + F(at) + ",\"name\":" + Q(boss.DisplayName)
                    + ",\"fightSeconds\":" + F(guardianStartedAt >= 0f ? Time.time - guardianStartedAt : -1f) + ",\"by\":" + Q(Health.NameOf(h.LastHitFrom)) + "}");
            else if (h.TryGetComponent<Enemies.EnemyBrain>(out var e) && (e.Elite.Value || e.Title.Value.Length > 0))
                Write("{\"t\":\"eliteDown\",\"depth\":" + depth + ",\"s\":" + F(at) + ",\"kind\":" + Q(kind) + ",\"title\":" + Q(e.Title.Value.ToString())
                    + ",\"by\":" + Q(Health.NameOf(h.LastHitFrom)) + "}");
        }

        private static bool IsPlayer(ulong id)
        {
            var net = NetworkManager.Singleton;
            return net != null && net.SpawnManager != null && net.SpawnManager.SpawnedObjects.TryGetValue(id, out var o) && o.TryGetComponent<PlayerLife>(out _);
        }

        private Member MemberOf(ulong id)
        {
            if (!members.TryGetValue(id, out var m)) members[id] = m = new Member();
            return m;
        }

        private static void Add(Dictionary<string, float> map, string key, float v)
        {
            map.TryGetValue(key, out var was);
            map[key] = was + v;
        }

        private static void Count(Dictionary<string, int> map, string key)
        {
            map.TryGetValue(key, out var was);
            map[key] = was + 1;
        }

        private void Write(string line)
        {
            if (file == null) return;
            try { file.WriteLine(line); }
            catch (Exception e) { Debug.LogWarning("[RunLog] " + e.Message); }
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        private static string Map(Dictionary<string, float> map)
        {
            var sb = new StringBuilder("{");
            foreach (var kv in map)
            {
                if (sb.Length > 1) sb.Append(',');
                sb.Append(Q(kv.Key)).Append(':').Append(F(kv.Value));
            }
            return sb.Append('}').ToString();
        }

        private static string Map(Dictionary<string, int> map)
        {
            var sb = new StringBuilder("{");
            foreach (var kv in map)
            {
                if (sb.Length > 1) sb.Append(',');
                sb.Append(Q(kv.Key)).Append(':').Append(kv.Value);
            }
            return sb.Append('}').ToString();
        }
    }
}
