using System.Collections.Generic;
using System.Linq;
using Deepbound.Classes;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.UI
{
    /// <summary>
    /// The developer menu (F1, or the ` key): buttons for the common tools and a command line
    /// (type "help"). Host only: every tool acts on the server directly, which the host is.
    /// Tools: god mode, heal, gold/embers/sigils, runes and relics, temper and ability ranks,
    /// jump to any floor or back to camp, teleport to the boss or the exit, kill everything,
    /// spawn any enemy (optionally elite), reveal the map, slow or speed time.
    /// </summary>
    public class DevMenu : MonoBehaviour
    {
        public static bool Open { get; private set; }
        public static bool God { get; private set; }
        private NetworkPlayer player;
        private string command = "";
        private readonly List<string> log = new List<string>();
        private Vector2 scroll;
        private int floorPick, spawnPick;
        private bool spawnElite, focus;

        private void Start() => player = GetComponent<NetworkPlayer>();

        private bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || ChatBox.Typing) return;
            if (kb.f1Key.wasPressedThisFrame || kb.backquoteKey.wasPressedThisFrame)
            {
                Open = !Open;
                focus = Open;
                if (Open && log.Count == 0) Print("Developer menu. Type 'help' for the command list.");
            }
            if (Open && kb.escapeKey.wasPressedThisFrame) Open = false;
        }

        private void OnDisable() { Open = false; }

        private void Print(string line)
        {
            log.Add(line);
            if (log.Count > 60) log.RemoveAt(0);
            scroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!Open || player == null) return;
            UiSkin.Apply();
            GUI.depth = -40;
            float pw = 720f, ph = 560f, x = Screen.width - pw - 20f, y = 40f;
            UiSkin.Panel(new Rect(x - 8, y - 8, pw + 16, ph + 16), 0.97f);
            GUI.Label(new Rect(x, y, pw, 26), "DEVELOPER MENU" + (IsHost ? "" : "   (host only: you are a client)"), new GUIStyle(UiSkin.Heading) { fontSize = 20 });
            if (!IsHost) { GUI.Label(new Rect(x, y + 34, pw, 40), "Only the host can use developer tools.", UiSkin.Body); return; }
            var p = player.GetComponent<PlayerProgress>();
            var dir = DungeonDirector.Instance;

            float bx = x, by = y + 34;
            bool B(string label, float w = 112f)
            {
                bool r = GUI.Button(new Rect(bx, by, w, 26), label, UiSkin.Button);
                bx += w + 6f;
                if (bx > x + pw - 112f) { bx = x; by += 30f; }
                return r;
            }
            void Row() { if (bx != x) { bx = x; by += 30f; } }
            void Head(string t) { Row(); GUI.Label(new Rect(x, by, pw, 20), t, new GUIStyle(UiSkin.Small) { fontStyle = FontStyle.Bold, normal = { textColor = UiSkin.Gold } }); by += 20f; }

            Head("PLAYER");
            if (B(God ? "God: ON" : "God: off")) Run("god");
            if (B("Heal")) Run("heal");
            if (B("+500 gold")) Run("gold 500");
            if (B("+300 embers")) Run("embers 300");
            if (B("+5 sigils")) Run("sigils 5");
            if (B("Temper +10")) Run("temper 10");
            if (B("Ranks max")) Run("rank 8");
            if (B("All relics")) Run("relic all");
            if (B("A rune of each")) Run("rune all");
            if (B("Cleanse")) Run("cleanse");
            Head("WORLD");
            if (B("Kill all")) Run("kill");
            if (B("To boss")) Run("tp boss");
            if (B("To exit")) Run("tp exit");
            if (B("Back to camp")) Run("camp");
            if (B("Reveal map")) Run("map");
            if (B("Time x0.25")) Run("time 0.25");
            if (B("Time x1")) Run("time 1");
            if (B("Time x2")) Run("time 2");
            Row();
            GUI.Label(new Rect(bx, by + 4, 60, 20), "Floor", UiSkin.Small);
            bx += 50f;
            if (GUI.Button(new Rect(bx, by, 26, 26), "<", UiSkin.Button)) floorPick = Mathf.Max(0, floorPick - 1);
            GUI.Label(new Rect(bx + 30, by + 2, 50, 22), (floorPick + 1).ToString(), new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(bx + 82, by, 26, 26), ">", UiSkin.Button)) floorPick = Mathf.Min(DungeonDirector.FinalDepth, floorPick + 1);
            if (GUI.Button(new Rect(bx + 114, by, 90, 26), "Go", UiSkin.Button)) Run("floor " + (floorPick + 1));
            bx = x; by += 32f;
            Head("SPAWN (in front of you)");
            if (GUI.Button(new Rect(bx, by, 26, 26), "<", UiSkin.Button)) spawnPick = (spawnPick + DungeonDirector.DebugSpawnKinds.Length - 1) % DungeonDirector.DebugSpawnKinds.Length;
            GUI.Label(new Rect(bx + 30, by + 2, 130, 22), DungeonDirector.DebugSpawnKinds[spawnPick], new GUIStyle(UiSkin.Body) { alignment = TextAnchor.MiddleCenter });
            if (GUI.Button(new Rect(bx + 164, by, 26, 26), ">", UiSkin.Button)) spawnPick = (spawnPick + 1) % DungeonDirector.DebugSpawnKinds.Length;
            spawnElite = GUI.Toggle(new Rect(bx + 200, by + 4, 80, 20), spawnElite, " elite");
            if (GUI.Button(new Rect(bx + 284, by, 100, 26), "Spawn", UiSkin.Button)) Run("spawn " + DungeonDirector.DebugSpawnKinds[spawnPick] + (spawnElite ? " elite" : ""));
            by += 34f;

            // The console.
            var logRect = new Rect(x, by, pw, y + ph - by - 40f);
            UiSkin.Rect(logRect, new Color(0f, 0f, 0f, 0.5f));
            GUILayout.BeginArea(new Rect(logRect.x + 6, logRect.y + 4, logRect.width - 12, logRect.height - 8));
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var l in log) GUILayout.Label(l, new GUIStyle(UiSkin.Small) { wordWrap = true });
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "DevCommand")
            {
                if (command.Trim().Length > 0) { Print("> " + command); Run(command.Trim()); }
                command = "";
                e.Use();
            }
            GUI.SetNextControlName("DevCommand");
            command = GUI.TextField(new Rect(x, y + ph - 30f, pw, 26f), command, new GUIStyle(GUI.skin.textField) { fontSize = 14 });
            if (focus) { GUI.FocusControl("DevCommand"); focus = false; }
        }

        private static readonly string[] Help =
        {
            "god                 toggle taking no damage",
            "heal                full health and resource",
            "gold N | embers N | sigils N",
            "temper N            weapon to +N (embers and gold supplied)",
            "rank N              Q and E to rank N",
            "relic NAME | all    add a relic (part of its name is enough)",
            "rune KIND [TIER] | all   add a rune (frost storm hunter holy ember keen farsight; tier 1-3)",
            "cleanse             clear every affliction",
            "kill                kill every enemy on the floor (not bosses: 'kill boss')",
            "floor N             start floor N (1-10)",
            "camp                back to camp, keeping the run",
            "tp boss | exit | camp     teleport",
            "spawn KIND [elite]  spawn an enemy in front of you",
            "map                 chart every room of this floor",
            "time X              time scale (0.1 - 4)",
        };

        private void Run(string line)
        {
            var parts = line.ToLowerInvariant().Split(' ').Where(t => t.Length > 0).ToArray();
            if (parts.Length == 0 || !IsHost) return;
            var p = player.GetComponent<PlayerProgress>();
            var h = player.GetComponent<Health>();
            var dir = DungeonDirector.Instance;
            var motor = player.GetComponent<FirstPersonMotor>();
            int N(int i, int fallback) => parts.Length > i && int.TryParse(parts[i], out var v) ? v : fallback;
            switch (parts[0])
            {
                case "help": foreach (var l in Help) Print(l); break;
                case "god":
                    God = !God;
                    if (God && !godHooked) { godHooked = true; h.IncomingModifiers.Insert(0, (d, f) => God && d < 1e5f ? 0f : d); }
                    Print("God mode " + (God ? "on" : "off"));
                    break;
                case "heal":
                    h.ServerSetFraction(1f);
                    player.ActiveKit?.RestoreResource(1f);
                    Print("Healed");
                    break;
                case "gold": p.ServerAddGold(N(1, 500)); Print("Gold " + p.Gold.Value); break;
                case "embers": p.ServerAddEmbers(N(1, 300)); Print("Embers " + p.Embers.Value); break;
                case "sigils": for (int i = 0; i < N(1, 5); i++) p.ServerAddSigil(); Print("Sigils " + p.Sigils.Value); break;
                case "temper":
                {
                    int to = Mathf.Clamp(N(1, Anvil.MaxLevel), 0, Anvil.MaxLevel);
                    if (to >= WeaponPaths.PathLevel && p.WeaponPath.Value == 0) p.WeaponPath.Value = 1;
                    for (int g = 0; g < 12 && p.WeaponLevel.Value < to; g++) { p.ServerAddEmbers(400); p.ServerAddGold(300); p.ServerUpgradeWeapon(); }
                    Print("Weapon +" + p.WeaponLevel.Value);
                    break;
                }
                case "rank":
                {
                    int to = Mathf.Clamp(N(1, AbilityRanks.MaxRank), 1, AbilityRanks.MaxRank);
                    p.RankQ.Value = (byte)to; p.RankE.Value = (byte)to;
                    Print("Q and E rank " + to);
                    break;
                }
                case "relic":
                {
                    string want = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "all";
                    int n = 0;
                    for (int r = 0; r < Relics.Count; r++)
                        if (want == "all" || Relics.Name((Relic)r).ToLowerInvariant().Contains(want)) { if (p.ServerAddRelic((Relic)r)) n++; if (want != "all") break; }
                    Print(n + " relic(s) added");
                    break;
                }
                case "rune":
                {
                    string kind = parts.Length > 1 ? parts[1] : "all";
                    int tier = Mathf.Clamp(N(2, 1), 1, 3) - 1;
                    int n = 0;
                    for (int r = 1; r <= Runes.Count; r++)
                        if (kind == "all" || Runes.Name((Rune)r).ToLowerInvariant().StartsWith(kind)) { p.ServerAddRune(Runes.Make((Rune)r, tier)); n++; }
                    Print(n + " rune(s) added to the bag");
                    break;
                }
                case "cleanse": player.GetComponent<PlayerDebuffs>()?.ServerCleanse(); Print("Cleansed"); break;
                case "kill":
                {
                    bool bosses = parts.Length > 1 && parts[1] == "boss";
                    int n = 0;
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!e.IsDead && (bosses || !(e is BossBrain))) { e.GetComponent<Health>().ServerDamage(1e6f, e.transform.position, player.NetworkObjectId); n++; }
                    Print(n + " killed");
                    break;
                }
                case "floor":
                    if (dir == null) break;
                    dir.ServerDebugStartFloor(Mathf.Clamp(N(1, 1), 1, DungeonDirector.FloorsPerRun) - 1);
                    Print("Floor " + Mathf.Clamp(N(1, 1), 1, DungeonDirector.FloorsPerRun));
                    break;
                case "camp": dir?.ServerDebugCamp(); Print("Back to camp"); break;
                case "tp":
                {
                    string where = parts.Length > 1 ? parts[1] : "";
                    Vector3? to = where == "boss" && DungeonDirector.Layout != null && !DungeonDirector.InLobby ? DungeonDirector.Layout.BossRoom.Center + Vector3.up * 0.2f
                        : where == "exit" && !DungeonDirector.InLobby ? DungeonDirector.ExitPosition + Vector3.up * 0.2f
                        : where == "camp" && DungeonDirector.InLobby ? Lobby.SpawnFor(player.OwnerClientId) : (Vector3?)null;
                    if (to != null) { motor.Teleport(to.Value, player.transform.rotation); Print("Teleported"); }
                    else Print("Nowhere to go ('" + where + "')");
                    break;
                }
                case "spawn":
                {
                    if (dir == null || parts.Length < 2) { Print("spawn what? " + string.Join(", ", DungeonDirector.DebugSpawnKinds)); break; }
                    var at = player.transform.position + player.transform.forward * 4f;
                    var b = dir.ServerDebugSpawn(parts[1], at, parts.Length > 2 && parts[2] == "elite");
                    Print(b != null ? "Spawned " + parts[1] : "Unknown enemy: " + parts[1]);
                    break;
                }
                case "map": p.MapRevealed.Value = true; Print("Map charted"); break;
                case "time":
                {
                    float t = parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 1f;
                    Time.timeScale = Mathf.Clamp(t, 0.1f, 4f);
                    Print("Time x" + Time.timeScale.ToString("0.##"));
                    break;
                }
                default: Print("Unknown command. Type 'help'."); break;
            }
        }

        private bool godHooked;
    }
}
