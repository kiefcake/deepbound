using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deepbound.Combat;
using Deepbound.Core;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Networking
{
    /// <summary>
    /// A bot that plays a whole run the way a person would (2026-10-21, the user: "play a full run
    /// through and see what needs changed"): <c>-autoplay &lt;dir&gt; [-autoclass Mage]</c>, windowed.
    /// It walks the floors on the navmesh, fights with the class's own attacks through a virtual
    /// gamepad (abilities too), drinks potions, gathers loot, opens chests, fights the guardian, rides
    /// the lift, spends at Hobb and Ysolde, buys potions from Silas and boards the carriage, for all ten
    /// floors. It writes <c>autoplay.txt</c>: everything the game said (feed, cards, tips, folk, pickups),
    /// per floor time, damage taken by source, downs, gold/embers, and what it could not do, plus
    /// screenshots. It never writes the real save (the test flags are on), but the dungeon itself runs
    /// as it would for a player (mimics, the Tallyman, events, crits). When downed it notes it and gets
    /// back up, so one run sees every floor.
    /// </summary>
    public class AutoPlay : MonoBehaviour
    {
        private string dir;
        private NetworkLauncher launcher;
        private Classes.ClassId cls = Classes.ClassId.Knight;
        private StreamWriter log;
        private float started;
        private int floorNo = -1;
        /// <summary>-autodepth N: start at floor N with a loadout like a run's by then (to see the deep floors sooner).</summary>
        private int startFloor;

        private NetworkPlayer me;
        private FirstPersonMotor motor;
        private PlayerLife life;
        private PlayerProgress prog;
        private Health hp;
        private Flasks flasks;

        private readonly Dictionary<string, float> takenBy = new Dictionary<string, float>();
        private float taken, dealt;
        private int downs, kills, shotNo, saves, savesFloor;
        private float bossHpMark = -1f, bossHpAt, nextBlock;
        private bool pendingHeal;
        private int bossHits; private float bossDealt;
        private float lootOnSince = -1f;
        private readonly HashSet<ulong> ignoredLoot = new HashSet<ulong>();
        private Health stuckTarget;

        // ---- Party autoplay (2026-11-01, "continue further work on balance in parties of more than 1") ----
        // The host bot runs with -autoparty N and waits for N players; each other bot is a client run with
        // -autofollow K (K = 1..3: its seat on the carriage) that joins on -port, plays near its leader (the host) and
        // shops at camp on its own. The host sees every hit, so it keeps the books for the whole party and gives every
        // member the same "would have gone down" save.
        private bool follower;
        /// <summary>-autofloors N: stop after N floors (tools/test_party.py).</summary>
        private int floorsWanted = int.MaxValue;
        private int seatNo, partySize = 1;
        private class Member
        {
            public string Name; public float Dealt, Taken, BossDealt; public int Saves, SavesFloor;
            // This floor's damage by source: all of it / on the guardian, and hits on the guardian.
            public readonly Dictionary<string, float> Source = new Dictionary<string, float>(), BossSource = new Dictionary<string, float>();
            public readonly Dictionary<string, int> BossHits = new Dictionary<string, int>();
        }
        private readonly Dictionary<ulong, Member> party = new Dictionary<ulong, Member>();
        private readonly HashSet<Health> hooked = new HashSet<Health>();
        private readonly List<Health> healQueue = new List<Health>();
        private float disconnectedSince = -1f;

        private PlayerLife Leader
        {
            get { foreach (var p in PlayerLife.All) if (p != null && p.OwnerClientId == NetworkManager.ServerClientId) return p; return null; }
        }
        private float flankStarted = -1f;
        private string lastHeard;
        private readonly List<string> problems = new List<string>();

        public void Run(string outDir, NetworkLauncher owner, string[] args)
        {
            dir = outDir;
            launcher = owner;
            int c = System.Array.IndexOf(args, "-autoclass");
            if (c >= 0 && c + 1 < args.Length && System.Enum.TryParse<Classes.ClassId>(args[c + 1], true, out var parsed)) cls = parsed;
            int ad = System.Array.IndexOf(args, "-autodepth");
            if (ad >= 0 && ad + 1 < args.Length && int.TryParse(args[ad + 1], out var d0)) startFloor = Mathf.Clamp(d0, 1, DungeonDirector.FloorsPerRun) - 1;
            int ap = System.Array.IndexOf(args, "-autoparty");
            if (ap >= 0 && ap + 1 < args.Length && int.TryParse(args[ap + 1], out var n0)) partySize = Mathf.Clamp(n0, 1, 4);
            int fl = System.Array.IndexOf(args, "-autofloors");
            if (fl >= 0 && fl + 1 < args.Length && int.TryParse(args[fl + 1], out var fl0)) floorsWanted = Mathf.Max(1, fl0);
            int af = System.Array.IndexOf(args, "-autofollow");
            if (af >= 0) { follower = true; seatNo = af + 1 < args.Length && int.TryParse(args[af + 1], out var k0) ? Mathf.Clamp(k0, 1, 3) : 1; startFloor = 0; }
            // Never touch the real save; the dungeon still behaves as in play (DungeonDirector.TestMode stays off).
            Cosmetics.TestMode = true;
            UI.Journal.TestMode = true;
            Deeds.TestMode = true;
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "autoplay.txt")) { AutoFlush = true };
            started = Time.time;
            UI.GameHud.Heard += (kind, text) =>
            {
                string line = kind + ": " + text;
                if (line == lastHeard) return;
                lastHeard = line;
                L("   [" + kind + "] " + text);
            };
            PlayerLife.DownedRecap += (killer, lines) => L("!! DOWNED by " + killer + " | " + (lines ?? "").Replace("\n", "; ").Replace("\t", " "));
            StartCoroutine(System.Array.IndexOf(args, "-navcheck") >= 0 ? NavCheck() : Main());
        }

        /// <summary>
        /// -navcheck: build each floor and check every room can be walked to from the start room on the
        /// navmesh (what the dead and the bot walk on). An unreachable room is a room the dead cannot
        /// follow you into, or a guardian no one can get to.
        /// </summary>
        private IEnumerator NavCheck()
        {
            launcher.StartHost();
            for (float t = 0f; t < 15f && NetworkPlayer.Local == null; t += 0.2f) yield return new WaitForSeconds(0.2f);
            yield return new WaitForSeconds(2f);
            var dd = DungeonDirector.Instance;
            int bad = 0;
            for (int depth = 0; depth < DungeonDirector.FloorsPerRun; depth++)
            {
                for (int seed = 0; seed < (System.Environment.GetCommandLineArgs().Contains("-navquick") ? 1 : 3); seed++)
                {
                    dd.ServerDebugStartFloor(depth);
                    yield return new WaitForSeconds(2.5f);
                    var layout = DungeonDirector.Layout;
                    floorNo = depth;
                    if (layout == null) { L("no layout"); continue; }
                    var start = layout.StartRoom.Center;
                    var path = new NavMeshPath();
                    var lost = new List<string>();
                    foreach (var room in layout.Rooms)
                    {
                        if (!NavMesh.SamplePosition(room.Center, out var c, 4f, NavMesh.AllAreas)) { lost.Add(room.Id + " " + room.Kind + "/" + room.Feature + " (no navmesh at its centre)"); continue; }
                        if (!NavMesh.SamplePosition(start, out var s0, 4f, NavMesh.AllAreas)) continue;
                        if (!NavMesh.CalculatePath(s0.position, c.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        {
                            var end = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : s0.position;
                            var near = Physics.OverlapSphere(end + Vector3.up, 2.5f, ~0, QueryTriggerInteraction.Ignore).Select(x => x.name).Distinct().Take(6);
                            var obs = FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None).Where(o => o.enabled && Vector3.Distance(o.transform.position, end) < 6f).Select(o => o.name);
                            lost.Add(room.Id + " " + room.Kind + "/" + room.Feature + " (" + path.status + ", the way ends at " + Fmt(end) + " near " + string.Join(", ", near) + (obs.Any() ? "; obstacles " + string.Join(", ", obs) : "") + ")");
                        }
                    }
                    // Hollow walls, once broken: the niche behind must join the room (breach dead come out of it).
                    foreach (var cw in FindObjectsByType<CrackedWall>(FindObjectsSortMode.None)) if (cw != null && cw.IsSpawned) cw.NetworkObject.Despawn();
                    yield return new WaitForSeconds(0.6f);
                    foreach (var room in layout.Rooms)
                        foreach (var hole in room.Holes)
                        {
                            var inNiche = room.HoleFace(hole) + DungeonLayout.Room.HoleOutward(hole) * 1.5f;
                            if (!NavMesh.SamplePosition(start, out var sn, 4f, NavMesh.AllAreas)) continue;
                            bool on = NavMesh.SamplePosition(inNiche, out var tn, 1.5f, NavMesh.AllAreas);
                            if (!on || !NavMesh.CalculatePath(sn.position, tn.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                            {
                                var endN = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : sn.position;
                                lost.Add((hole.Breach ? "breach" : "secret") + " niche off " + room.Feature + " room " + room.Id + " (" + (on ? path.status.ToString() : "no navmesh inside") + ", way ends " + endN.ToString("F1") + ", niche " + inNiche.ToString("F1") + ", near the opening: " + string.Join(", ", Physics.OverlapSphere(room.HoleFace(hole) + Vector3.up, 2.2f, ~0, QueryTriggerInteraction.Ignore).Select(x => x.name).Distinct().Take(6)) + ")");
                            }
                        }
                    // Pockets: walkable patches inside a room that cannot reach the rest (enemies and the bot trapped).
                    foreach (var room in layout.Rooms)
                    {
                        if (room.Feature == RoomFeature.Vault || room.Feature == RoomFeature.Chasm) continue;
                        if (!NavMesh.SamplePosition(start, out var sp, 4f, NavMesh.AllAreas)) break;
                        int pockets = 0; string first = null;
                        for (float gx = -room.Size.x / 2f + 1f; gx < room.Size.x / 2f; gx += 2f)
                            for (float gz = -room.Size.y / 2f + 1f; gz < room.Size.y / 2f; gz += 2f)
                            {
                                var q = room.Center + new Vector3(gx, 0.3f, gz);
                                if (!NavMesh.SamplePosition(q, out var qh, 0.6f, NavMesh.AllAreas)) continue;
                                if (NavMesh.CalculatePath(qh.position, sp.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) continue;
                                pockets++;
                                if (first == null) first = qh.position.ToString("F1") + " near " + string.Join(", ", Physics.OverlapSphere(qh.position + Vector3.up, 1.6f, ~0, QueryTriggerInteraction.Ignore).Select(x => x.name).Distinct().Take(5));
                            }
                        if (pockets > 0) lost.Add("pocket in " + room.Feature + " room " + room.Id + ": " + pockets + " cut-off points, e.g. " + first);
                    }
                    // The guardian's spot and the lift's (a dais is raised: its ramps must join the floor).
                    foreach (var (what, at) in new[] { ("boss spot", layout.BossSpawn), ("lift spot", layout.LiftSpot) })
                    {
                        if (!NavMesh.SamplePosition(start, out var sa, 4f, NavMesh.AllAreas)) continue;
                        bool onMesh = NavMesh.SamplePosition(at, out var ta, 1.5f, NavMesh.AllAreas);
                        bool ok = onMesh && NavMesh.CalculatePath(sa.position, ta.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                        if (!ok) lost.Add(what + " in a " + layout.BossRoom.Arena + " arena (" + (onMesh ? path.status.ToString() : "no navmesh within 1.5 m of " + at.ToString("F1")) + ")");
                    }
                    L("floor " + (depth + 1) + " seed " + layout.Seed + ": " + layout.Rooms.Count + " rooms, " + (lost.Count == 0 ? "all reachable" : lost.Count + " UNREACHABLE: " + string.Join(" | ", lost)));
                    if (lost.Count > 0)
                    {
                        bad++;
                        // Look at where the way ends, toward the room it was trying to reach.
                        foreach (var room in layout.Rooms)
                        {
                            if (room.Feature == RoomFeature.Vault) continue;
                            if (!NavMesh.SamplePosition(room.Center, out var c2, 4f, NavMesh.AllAreas) || !NavMesh.SamplePosition(start, out var s2, 4f, NavMesh.AllAreas)) continue;
                            if (NavMesh.CalculatePath(s2.position, c2.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) continue;
                            var end = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : s2.position;
                            L("   route to " + room.Feature + " room " + room.Id + " (centre " + room.Center.x.ToString("F0") + "," + room.Center.y.ToString("F1") + "," + room.Center.z.ToString("F0") + ", size " + room.Size.x.ToString("F0") + "x" + room.Size.y.ToString("F0") + ", balconies " + string.Join("/", room.Balconies) + ", doors " + string.Join("", room.Doors.Select(dd2 => dd2 ? "1" : "0")) + "), target " + c2.position.ToString("F1") + ": " + string.Join(" > ", path.corners.Select(k => k.ToString("F1"))));
                            var look = Flat(room.Center) - Flat(end);
                            motor = NetworkPlayer.Local.GetComponent<FirstPersonMotor>();
                            motor.Teleport(end - look.normalized * 1.5f + Vector3.up * 0.1f, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));
                            yield return new WaitForSeconds(0.8f);
                            yield return Shot("nav_F" + (depth + 1) + "_" + seed + "_" + room.Feature);
                            break;
                        }
                    }
                }
            }
            L("== NAVCHECK: " + bad + " floors with unreachable rooms ==");
            log.Close();
            Application.Quit(0);
        }

        private void Update()
        {
            if (pendingHeal && hp != null) { pendingHeal = false; hp.ServerSetFraction(0.6f); }
            foreach (var h in healQueue) if (h != null && h.IsAlive) h.ServerSetFraction(0.6f);
            healQueue.Clear();
            if (follower && me != null)
            {
                // The host's run ended (or it closed): nothing left to play.
                var nm = NetworkManager.Singleton;
                if (nm != null && nm.IsConnectedClient) disconnectedSince = -1f;
                else if (disconnectedSince < 0f) disconnectedSince = Time.time;
                else if (Time.time - disconnectedSince > 5f) { L("== the host is gone: done =="); log?.Close(); log = null; Application.Quit(0); }
            }
            else if (!follower && me != null) HookParty();
        }

        /// <summary>Host: the books and the save for every party member who has joined (the followers come later).</summary>
        private void HookParty()
        {
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var h = p.GetComponent<Health>();
                if (h == null || hooked.Contains(h)) continue;
                hooked.Add(h);
                var pp = p.GetComponent<PlayerProgress>();
                party[p.NetworkObjectId] = new Member { Name = Health.NameOf(p.NetworkObjectId) + " (" + (pp != null ? pp.ClassId.Value.ToString() : "?") + ")" };
                if (h == hp) continue; // the host's own save is set up in Main
                var id = p.NetworkObjectId;
                h.IncomingModifiers.Add((dmg, from) =>
                {
                    if (h.Current.Value - dmg > 1f) return dmg;
                    var m = party[id];
                    m.Saves++; m.SavesFloor++;
                    L("!! " + m.Name + " would have gone down (save #" + m.Saves + "): a blow of " + dmg.ToString("F0") + " at " + h.Current.Value.ToString("F0") + " hp");
                    healQueue.Add(h);
                    return Mathf.Max(0f, h.Current.Value - 1f);
                });
                L("party: " + party[id].Name + " joined (" + PlayerLife.All.Count + " in the party)");
            }
        }

        private string PartyLine(bool floor)
        {
            if (party.Count < 2) return "";
            return string.Join(" | ", party.Values.Select(m => m.Name + ": dealt " + m.Dealt.ToString("F0") + " (boss " + m.BossDealt.ToString("F0") + "), taken " + m.Taken.ToString("F0") + ", would-have-gone-down " + (floor ? m.SavesFloor : m.Saves)));
        }

        private void L(string s)
        {
            float t = Time.time - started;
            string line = $"[{(int)(t / 60):00}:{(int)(t % 60):00}] F{floorNo + 1} {s}";
            log?.WriteLine(line);
            Debug.Log("[AutoPlay] " + line);
        }

        private void Problem(string s) { problems.Add("F" + (floorNo + 1) + ": " + s); L("?? " + s); }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, (shotNo++).ToString("000") + "_" + name + ".png"));
        }

        private IEnumerator Main()
        {
            if (follower) launcher.StartClient("127.0.0.1:" + launcher.Port);
            else launcher.StartHost();
            for (float t = 0f; t < 15f && NetworkPlayer.Local == null; t += 0.2f) yield return new WaitForSeconds(0.2f);
            yield return new WaitForSeconds(3f);
            me = NetworkPlayer.Local;
            if (me == null) { L("no local player"); Application.Quit(1); yield break; }
            motor = me.GetComponent<FirstPersonMotor>();
            life = me.GetComponent<PlayerLife>();
            prog = me.GetComponent<PlayerProgress>();
            hp = me.GetComponent<Health>();
            flasks = me.GetComponent<Flasks>();
            // The bot's window is often not the focused one (someone is watching, or three other bots are running): it needs
            // neither the cursor nor the input system, see Binds.BotPress.
            FirstPersonMotor.BotControl = true;
            Application.runInBackground = true;
            if (!follower) Health.ServerHitTagged += (h, amount, from, tag) =>
            {
                string source = tag ?? "other";
                if (party.TryGetValue(from, out var md) && source != "forced")
                {
                    md.Dealt += amount;
                    md.Source.TryGetValue(source, out float sourceSoFar);
                    md.Source[source] = sourceSoFar + amount;
                    if (BossBrain.Active != null && h == BossBrain.Active.GetComponent<Health>())
                    {
                        md.BossDealt += amount;
                        md.BossSource.TryGetValue(source, out float bossSoFar);
                        md.BossSource[source] = bossSoFar + amount;
                        md.BossHits.TryGetValue(source, out int hitsSoFar);
                        md.BossHits[source] = hitsSoFar + 1;
                    }
                }
            };
            if (!follower) Health.ServerAnyDamaged += (h, amount, from) =>
            {
                if (h != null && party.TryGetValue(h.NetworkObjectId, out var mt) && h.Team == Team.Players) mt.Taken += amount;
                if (h == hp)
                {
                    taken += amount;
                    string who = Health.NameOfHit(from, hp);
                    takenBy.TryGetValue(who, out var v);
                    takenBy[who] = v + amount;
                }
                else if (from == me.NetworkObjectId) { dealt += amount; if (BossBrain.Active != null && h == BossBrain.Active.GetComponent<Health>()) { bossHits++; bossDealt += amount; } if (!h.IsAlive && h.Team == Team.Enemies) kills++; }
            };

            // A bot cannot dodge like a person: a blow that would put it down leaves it at 1 HP instead,
            // logged as "would have gone down" (alone, a down ends the run), so one run sees every floor.
            if (!follower) hp.IncomingModifiers.Add((dmg, from) =>
            {
                if (hp.Current.Value - dmg > 1f) return dmg;
                saves++; savesFloor++;
                if (party.TryGetValue(me.NetworkObjectId, out var ms)) { ms.Saves++; ms.SavesFloor++; }
                L("!! would have gone down (save #" + saves + "): a blow of " + dmg.ToString("F0") + " at " + hp.Current.Value.ToString("F0") + " hp; worst so far " + string.Join(", ", takenBy.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key)));
                pendingHeal = true;
                return Mathf.Max(0f, hp.Current.Value - 1f);
            });
            if (follower) prog.ChooseClassRpc(cls);
            else prog.ClassId.Value = cls;
            yield return new WaitForSeconds(1f);
            if (!follower && partySize > 1)
            {
                for (float t = 0f; t < 120f && PlayerLife.All.Count < partySize; t += 0.5f) yield return new WaitForSeconds(0.5f);
                yield return new WaitForSeconds(2f);
                HookParty();
                // The names and classes are known now.
                foreach (var p in PlayerLife.All)
                    if (p != null && party.TryGetValue(p.NetworkObjectId, out var mm) && p.GetComponent<PlayerProgress>() is PlayerProgress pq) mm.Name = Health.NameOf(p.NetworkObjectId) + " (" + pq.ClassId.Value + ")";
                L("== PARTY of " + PlayerLife.All.Count + " (wanted " + partySize + "): " + string.Join(", ", party.Values.Select(m => m.Name)) + " ==");
            }
            L("== RUN START as " + cls + (follower ? ", following the host (seat " + seatNo + ")" : "") + " ==");
            yield return Shot("camp_start");

            if (startFloor > 0)
            {
                // A run's worth of kit by this floor (roughly what runs 4 and 5 had).
                for (int i = 0; i < Mathf.Min(10, Mathf.RoundToInt(startFloor * 1.5f)); i++)
                {
                    var why = prog.CannotTemperReason();
                    if (why != null && why.ToLowerInvariant().Contains("path")) { prog.ChoosePathRpc((byte)Random.Range(1, Classes.WeaponPaths.Count + 1)); yield return new WaitForSeconds(0.2f); }
                    if (prog.OpenEdgeSlot >= 0) { prog.ServerChooseEdge(prog.EdgeOffer(prog.OpenEdgeSlot)[0]); yield return null; }
                    prog.ServerFreeTemper();
                    yield return null;
                }
                prog.RankQ.Value = (byte)Mathf.Min(Classes.AbilityRanks.MaxRank, 1 + startFloor / 3 + 1);
                prog.RankE.Value = (byte)Mathf.Min(Classes.AbilityRanks.MaxRank, 1 + startFloor / 3);
                for (int i = 0; i < startFloor; i++) prog.ServerAddRelic(Relics.Random());
                prog.ServerEquip(new GearItem(Gear.RollableOf(GearSlot.Armor)[0], (byte)Mathf.Min(2, startFloor / 3), (byte)startFloor));
                prog.ServerEquip(new GearItem(Gear.RollableOf(GearSlot.Charm)[0], (byte)Mathf.Min(2, startFloor / 3), (byte)startFloor));
                prog.Gold.Value = 150 * startFloor;
                L("== starting at floor " + (startFloor + 1) + " with a loadout ==");
                Status("loadout");
                DungeonDirector.Instance.ServerDebugStartFloor(startFloor);
                yield return Guarded(Floor);
            }
            for (int f = startFloor > 0 ? startFloor + 1 : 0; f < DungeonDirector.FloorsPerRun; f++)
            {
                yield return Camp();
                yield return Guarded(Floor);
                var ph = DungeonDirector.Instance.Phase.Value;
                if (ph == RunPhase.RunComplete || ph == RunPhase.Wiped) break;
                if (f + 1 >= floorsWanted) break;
            }

            L("KIT " + KitLine());
            L("== RUN OVER: phase " + DungeonDirector.Instance.Phase.Value + " ==");
            if (!follower && party.Count > 1) L("== PARTY TOTALS: " + PartyLine(false) + " ==");
            yield return new WaitForSeconds(6f);
            yield return Shot("run_summary");
            yield return new WaitForSeconds(20f);
            yield return Shot("run_summary_late");
            L("== PROBLEMS SEEN (" + problems.Count + ") ==");
            foreach (var p in problems) L("  - " + p);
            log.Close();
            Application.Quit(0);
        }

        // ------------------------------------------------------------------ camp

        /// <summary>Spend the run's parts as a careful player would (2026-11-07): run upgrades first, then potions, then tools.</summary>
        private IEnumerator Craft()
        {
            int made = 0;
            for (int pass = 0; pass < 12; pass++)
            {
                int pick = -1;
                foreach (var kind in new[] { CraftKind.Lining, CraftKind.Edge, CraftKind.Boots, CraftKind.Potion, CraftKind.Caltrops, CraftKind.Rattle })
                {
                    for (int i = 0; i < Crafting.Recipes.Length && pick < 0; i++)
                        if (Crafting.Recipes[i].Kind == kind && prog.CannotCraftReason(i) == null) pick = i;
                    if (pick >= 0) break;
                }
                if (pick < 0) break;
                prog.CraftRpc((byte)pick);
                made++;
                yield return new WaitForSeconds(0.25f);
            }
            if (made > 0) L("crafted " + made + " (parts left: " + prog.PartCount(Part.Bone) + " bone, " + prog.PartCount(Part.Cloth) + " cloth, " + prog.PartCount(Part.Iron) + " iron; lining " + prog.Lining + ", edge " + prog.Whetted + ", boots " + prog.Boots + ")");
        }

        private IEnumerator Camp()
        {
            // Long enough for the lift after a slow floor: at 60 s two followers gave up, counted a floor they never played,
            // and left the run a floor early (2026-11-04).
            for (float t = 0f; t < 240f && !DungeonDirector.InLobby; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (!DungeonDirector.InLobby) { Problem("never got back to camp"); yield break; }
            yield return new WaitForSeconds(2f);
            Status("at camp");
            if (floorNo >= 0) yield return Shot("camp_after_F" + (floorNo + 1));

            // A careful player's visit (2026-11-04, "equip the best, use every material"): before this the bots wore gear by
            // quality alone and never set a rune, refined, attuned, melded or scrapped, so balance runs measured a weak kit.
            yield return WearBest();
            yield return Craft();
            yield return AtStones(false);
            yield return TopUpEmbers();
            yield return AtAnvil();
            yield return AtStones(true);
            yield return Provision();
            // Buying can pay embers back (the Pilgrim's Purse) or bring a better piece: one more trip if there is work.
            if (AnvilWorkLeft()) yield return AtAnvil();
            yield return new WaitForSeconds(0.5f);
            AuditKit();
            L("gold left at camp: " + prog.Gold.Value);

            // Board: a seat on the carriage; it leaves when everyone sits.
            var ride = CarriageRide.Instance;
            if (ride == null) { Problem("no carriage at camp"); yield break; }
            yield return WalkTo(ride.SeatGround(seatNo), 1.5f);
            life.SitRpc(seatNo);
            L("sat on the carriage");
            yield return new WaitForSeconds(4f);
            if (!follower) yield return Shot("carriage_F" + (floorNo + 2));
            // It leaves when everyone sits: the others may still be at the shops.
            for (float t = 0f; t < (partySize > 1 || follower ? 150f : 45f) && DungeonDirector.InLobby; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (DungeonDirector.InLobby) { Problem("the carriage never left (seat " + life.Seat.Value + ")"); }
        }

        // ------------------------------------------------------------------ the kit

        /// <summary>What a piece is worth to this class: its stats in units of "one weight point", the deep and the fine count for more.</summary>
        private float GearScore(GearItem g)
        {
            if (g.IsEmpty) return 0f;
            bool caster = cls == Classes.ClassId.Mage || cls == Classes.ClassId.Cleric || cls == Classes.ClassId.PlagueDoctor;
            float score = 0f;
            foreach (var st in Gear.StatsOf(g))
            {
                float unit, worth;
                switch (st)
                {
                    case StatType.HP: unit = 10f; worth = 1f; break;
                    case StatType.MP: unit = 6f; worth = caster ? 0.6f : 0.2f; break;
                    case StatType.ATT: unit = 2f; worth = 1.4f; break;
                    case StatType.DEF: unit = 1f; worth = 1f; break;
                    case StatType.SPD: unit = 2f; worth = 0.5f; break;
                    case StatType.DEX: unit = 2f; worth = 1.1f; break;
                    case StatType.VIT: unit = 3f; worth = 0.5f; break;
                    default: unit = 3f; worth = caster ? 1f : 0.35f; break;
                }
                score += Gear.StatValue(g, st) / unit * worth;
            }
            return score + (g.Quality >= 3 ? 2f : 0f);
        }

        private IEnumerator WearBest()
        {
            for (int pass = 0; pass < 8; pass++)
            {
                int best = -1;
                float gain = 0.001f;
                for (int i = 0; i < prog.BagCount; i++)
                {
                    var g = prog.BagItem(i);
                    float d = GearScore(g) - GearScore(prog.Equipped(Gear.SlotOf((GearId)g.Id)));
                    if (d > gain) { gain = d; best = i; }
                }
                if (best < 0) yield break;
                var item = prog.BagItem(best);
                L("wearing " + Gear.Name(item) + " (score " + GearScore(item).ToString("F1") + ", was " + Gear.Name(prog.Equipped(Gear.SlotOf((GearId)item.Id))) + " " + GearScore(prog.Equipped(Gear.SlotOf((GearId)item.Id))).ToString("F1") + ")");
                prog.BagEquipRpc(best);
                yield return new WaitForSeconds(0.4f);
            }
        }

        private static readonly Progression.Edge[] EdgeOrder =
        {
            Progression.Edge.Momentum, Progression.Edge.Opportunist, Progression.Edge.Cleaver, Progression.Edge.Quickening,
            Progression.Edge.Desperation, Progression.Edge.Stalwart, Progression.Edge.LoneWolf, Progression.Edge.SecondWind,
        };

        private bool GearWorkLeft(out byte slot, out byte mode)
        {
            slot = 0; mode = 0;
            int best = int.MaxValue;
            for (byte sl = 0; sl < 2; sl++)
            {
                var w = prog.Equipped((GearSlot)sl);
                int refine = Gear.RefineCost(w), attune = Gear.AttuneCost(w, DungeonDirector.GearDepth);
                if (refine > 0 && refine <= prog.Embers.Value && refine < best) { best = refine; slot = sl; mode = 0; }
                if (attune > 0 && attune <= prog.Embers.Value && attune < best) { best = attune; slot = sl; mode = 1; }
            }
            return best < int.MaxValue;
        }

        private bool AnvilWorkLeft() => prog.CannotTemperReason() == null || prog.CannotHoneReason() == null || prog.OpenEdgeSlot >= 0 || GearWorkLeft(out _, out _);

        /// <summary>Hobb: break down what is spare, temper while it can, then refine and attune what is worn.</summary>
        private IEnumerator AtAnvil()
        {
            yield return WalkTo(Lobby.AnvilSpot + Vector3.back * 1.6f, 1.2f);
            for (int i = prog.BagCount - 1; i >= 0; i--)
            {
                var g = prog.BagItem(i);
                int e0 = prog.Embers.Value;
                prog.BagScrapRpc(i);
                yield return new WaitForSeconds(0.3f);
                L("scrapped " + Gear.Name(g) + " for " + (prog.Embers.Value - e0) + " embers");
            }
            for (int i = 0; i < 14; i++)
            {
                string why = prog.CannotTemperReason();
                if (why != null && why.ToLowerInvariant().Contains("path"))
                {
                    prog.ChoosePathRpc((byte)Random.Range(1, Classes.WeaponPaths.Count + 1));
                    yield return new WaitForSeconds(0.4f);
                    L("chose the weapon path of the " + Classes.WeaponPaths.Name(cls, prog.WeaponPath.Value));
                    why = prog.CannotTemperReason();
                }
                if (prog.OpenEdgeSlot >= 0)
                {
                    var offer = prog.EdgeOffer(prog.OpenEdgeSlot);
                    var pick = EdgeOrder.First(e => System.Array.IndexOf(offer, e) >= 0);
                    prog.ChooseEdgeRpc((byte)pick);
                    yield return new WaitForSeconds(0.4f);
                    L("forged the " + Progression.Edges.Name(pick) + " edge (offered " + string.Join(", ", offer.Select(Progression.Edges.Name)) + ")");
                    why = prog.CannotTemperReason();
                }
                if (prog.WeaponLevel.Value >= Anvil.MaxLevel)
                {
                    if (prog.CannotHoneReason() != null) break;
                    prog.HoneRpc();
                    yield return new WaitForSeconds(0.5f);
                    L("honed (" + prog.Hones.Value + ")");
                    continue;
                }
                if (why != null)
                {
                    L("temper +" + prog.WeaponLevel.Value + " -> +" + (prog.WeaponLevel.Value + 1) + ": " + why + " (needs " + Anvil.Cost(prog.WeaponLevel.Value) + " embers, " + Anvil.GoldCost(prog.WeaponLevel.Value) + " gold; has " + prog.Embers.Value + ", " + prog.Gold.Value + ")");
                    break;
                }
                prog.TemperRpc();
                yield return new WaitForSeconds(0.5f);
                L("tempered to +" + prog.WeaponLevel.Value);
            }
            for (int i = 0; i < 8 && GearWorkLeft(out byte slot, out byte mode); i++)
            {
                int e0 = prog.Embers.Value;
                var was = prog.Equipped((GearSlot)slot);
                prog.ReforgeRpc(slot, mode);
                yield return new WaitForSeconds(0.5f);
                if (prog.Embers.Value == e0) { L("could not " + (mode == 0 ? "refine " : "attune ") + Gear.Name(was)); break; }
                L((mode == 0 ? "refined " : "attuned ") + Gear.Name(was) + " (depth " + was.Depth + ") for " + (e0 - prog.Embers.Value) + " embers: now " + Gear.Name(prog.Equipped((GearSlot)slot)) + " depth " + prog.Equipped((GearSlot)slot).Depth);
            }
        }

        private float RuneWorth(Rune r)
        {
            if (r == Rune.None) return 0f;
            float w;
            switch (Runes.Kind(r))
            {
                case Rune.Keen: w = 1.2f; break;
                case Rune.Hunter: case Rune.Ember: w = 1.1f; break;
                case Rune.Holy: w = cls == Classes.ClassId.Cleric ? 1.3f : 0.8f; break;
                case Rune.Farsight: w = 0.6f; break;
                default: w = 1f; break;
            }
            return w * Runes.Power(Runes.Tier(r));
        }

        /// <summary>The best change to the weapon's sockets, or false. Two of a kind do nothing: only the best tier counts.</summary>
        private bool BestSocketing(out Rune rune, out int socket)
        {
            rune = Rune.None; socket = -1;
            int sockets = Runes.SocketsAt(prog.WeaponLevel.Value);
            float bestGain = 0.001f;
            for (int i = 0; i < prog.RuneBagCount; i++)
            {
                var r = prog.RuneInBag(i);
                int same = -1, empty = -1, weakest = -1;
                for (int k = 0; k < sockets; k++)
                {
                    var c = prog.Socketed(k);
                    if (c == Rune.None) { if (empty < 0) empty = k; continue; }
                    if (Runes.Kind(c) == Runes.Kind(r)) same = k;
                    if (weakest < 0 || RuneWorth(c) < RuneWorth(prog.Socketed(weakest))) weakest = k;
                }
                int to = same >= 0 ? same : empty >= 0 ? empty : weakest;
                if (to < 0) continue;
                float gain = RuneWorth(r) - RuneWorth(prog.Socketed(to));
                if (gain > bestGain) { bestGain = gain; rune = r; socket = to; }
            }
            return socket >= 0;
        }

        private bool MeldReady(out Rune rune)
        {
            rune = Rune.None;
            for (int i = 0; i < prog.RuneBagCount; i++)
            {
                var r = prog.RuneInBag(i);
                if (Runes.Tier(r) < Runes.MaxTier && prog.RuneCount(r) >= 3 && prog.Embers.Value >= Runes.MeldCost(Runes.Tier(r))) { rune = r; return true; }
            }
            return false;
        }

        private bool RankWorkLeft(out byte slot)
        {
            slot = (byte)(prog.Rank(Classes.AbilitySlot.Q) <= prog.Rank(Classes.AbilitySlot.E) ? 0 : 1);
            for (int n = 0; n < 2; n++, slot = (byte)(1 - slot))
            {
                var sl = slot == 0 ? Classes.AbilitySlot.Q : Classes.AbilitySlot.E;
                if (prog.Rank(sl) < Classes.AbilityRanks.MaxRank && prog.Sigils.Value >= Classes.AbilityRanks.SigilCost(prog.Rank(sl))) return true;
            }
            return false;
        }

        /// <summary>Ysolde: meld, rank abilities with Sigils (buying them when <paramref name="buy"/>), melt what is spare, set the best runes.</summary>
        private IEnumerator AtStones(bool buy)
        {
            bool sigilsForSale = buy && (prog.Rank(Classes.AbilitySlot.Q) < Classes.AbilityRanks.MaxRank || prog.Rank(Classes.AbilitySlot.E) < Classes.AbilityRanks.MaxRank);
            if (prog.Sigils.Value == 0 && prog.RuneBagCount == 0 && !sigilsForSale) yield break;
            yield return WalkTo(FolkPlaces.Now(FolkId.Ysolde) + Vector3.forward * 1.6f, 1.2f);
            for (int i = 0; i < 16 && MeldReady(out var meld); i++)
            {
                prog.MeldRunesRpc((byte)meld);
                yield return new WaitForSeconds(0.4f);
                L("melded three " + Runes.Name(meld));
            }
            if (sigilsForSale)
            {
                var stock = Shop.For(me.OwnerClientId, Shop.Vendor.Runewright);
                int idx = System.Array.FindIndex(stock, it => it.Kind == Shop.ItemKind.Sigil);
                for (int i = 0; i < 3 && idx >= 0 && prog.Gold.Value >= stock[idx].Price + GoldReserve + 60; i++)
                {
                    int g0 = prog.Gold.Value;
                    prog.BuyRpc(Shop.Vendor.Runewright, idx);
                    yield return new WaitForSeconds(0.4f);
                    if (prog.Gold.Value == g0) break;
                    L("bought a Sigil for " + (g0 - prog.Gold.Value));
                }
            }
            for (int i = 0; i < 8 && RankWorkLeft(out byte slot); i++)
            {
                int before = prog.Sigils.Value;
                prog.EmpowerRpc(slot);
                yield return new WaitForSeconds(0.4f);
                if (prog.Sigils.Value == before) { L("could not empower (" + before + " sigils, Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E) + ")"); break; }
                L("empowered " + (slot == 0 ? "Q" : "E") + " -> Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E));
            }
            for (int i = 0; i < 6 && prog.Sigils.Value > 0 && prog.Rank(Classes.AbilitySlot.Q) >= Classes.AbilityRanks.MaxRank && prog.Rank(Classes.AbilitySlot.E) >= Classes.AbilityRanks.MaxRank; i++)
            {
                prog.MeltSigilRpc();
                yield return new WaitForSeconds(0.4f);
                L("melted a spare Sigil into embers");
            }
            for (int i = 0; i < 6 && BestSocketing(out var rune, out int socket); i++)
            {
                prog.SocketRuneRpc((byte)rune, (sbyte)socket);
                yield return new WaitForSeconds(0.4f);
                L("set a " + Runes.Name(rune) + " in socket " + socket);
            }
        }

        /// <summary>Gold the next temper or hone will ask for: nothing else may eat it.</summary>
        private int GoldReserve => prog.WeaponLevel.Value < Anvil.MaxLevel ? Anvil.GoldCost(prog.WeaponLevel.Value) : prog.Hones.Value < Anvil.MaxHones ? Anvil.HoneGold(prog.Hones.Value) : 0;

        private IEnumerator WalkToVendor(Shop.Vendor v)
        {
            var at = Shop.Position(v);
            if (at != null) yield return WalkTo(at.Value + Vector3.back * 1.6f, 1.2f);
        }

        /// <summary>A temper just out of reach: buy the embers it is short of, when there is gold to spare.</summary>
        private IEnumerator TopUpEmbers()
        {
            if (prog.WeaponLevel.Value >= Anvil.MaxLevel) yield break;
            int shortBy = Anvil.Cost(prog.WeaponLevel.Value) - prog.Embers.Value;
            if (shortBy <= 0 || shortBy > 50) yield break;
            bool walked = false;
            for (int i = 0; i < 3 && prog.Embers.Value < Anvil.Cost(prog.WeaponLevel.Value); i++)
            {
                var stock = Shop.For(me.OwnerClientId, Shop.Vendor.Smith);
                int idx = System.Array.FindIndex(stock, it => it.Kind == Shop.ItemKind.Embers && it.Amount >= Anvil.Cost(prog.WeaponLevel.Value) - prog.Embers.Value);
                if (idx < 0) idx = System.Array.FindLastIndex(stock, it => it.Kind == Shop.ItemKind.Embers);
                if (idx < 0 || prog.Gold.Value < stock[idx].Price + GoldReserve + 40) break;
                if (!walked) { walked = true; yield return WalkToVendor(Shop.Vendor.Smith); }
                int g0 = prog.Gold.Value;
                prog.BuyRpc(Shop.Vendor.Smith, idx);
                yield return new WaitForSeconds(0.4f);
                if (prog.Gold.Value == g0) break;
                L("bought " + stock[idx].Name + " for " + (g0 - prog.Gold.Value));
            }
        }

        private IEnumerator BuyPotions(Flasks.Kind kind, string name, int upTo)
        {
            var stock = Shop.For(me.OwnerClientId, Shop.Vendor.Merchant);
            int idx = System.Array.FindIndex(stock, it => it.Name != null && it.Name.Contains(name));
            for (int i = 0; i < 5 && idx >= 0 && flasks.Count(kind) < upTo && prog.Gold.Value >= stock[idx].Price + GoldReserve; i++)
            {
                int g0 = prog.Gold.Value;
                prog.BuyRpc(Shop.Vendor.Merchant, idx);
                yield return new WaitForSeconds(0.35f);
                if (prog.Gold.Value == g0) { L("could not buy " + stock[idx].Name + ": " + prog.CannotBuyReason(Shop.Vendor.Merchant, idx)); break; }
                L("bought " + stock[idx].Name + " for " + (g0 - prog.Gold.Value));
            }
        }

        /// <summary>A fitted piece on offer that beats what is worn: buy it, take it from where it is laid, put it on.</summary>
        private IEnumerator BuyBetterGear(Shop.Vendor vendor)
        {
            var stock = Shop.For(me.OwnerClientId, vendor);
            int best = -1;
            float gain = 0.5f;
            for (int i = 0; i < stock.Length; i++)
            {
                if (stock[i].Kind != Shop.ItemKind.Gear || stock[i].Gear.IsEmpty) continue;
                float d = GearScore(stock[i].Gear) - GearScore(prog.Equipped(Gear.SlotOf(stock[i].Gear.Kind)));
                if (d > gain && prog.CannotBuyReason(vendor, i) == null && prog.Gold.Value >= stock[i].Price + GoldReserve + 40) { gain = d; best = i; }
            }
            if (best < 0) yield break;
            int g0 = prog.Gold.Value;
            prog.BuyRpc(vendor, best);
            yield return new WaitForSeconds(0.5f);
            if (prog.Gold.Value == g0) yield break;
            L("bought " + stock[best].Name + " for " + (g0 - prog.Gold.Value) + " (better by " + gain.ToString("F1") + ")");
            for (float t = 0f; t < 6f; t += 0.5f)
            {
                var g = NearestGear(6f);
                if (g == null) { yield return new WaitForSeconds(0.5f); continue; }
                g.UseRpc(me.NetworkObject);
                yield return new WaitForSeconds(0.5f);
                break;
            }
            yield return WearBest();
        }

        /// <summary>Gold, most useful first: potions, better gear, a rare, relics, drills, a blessing. Always leaves the next temper's gold.</summary>
        private IEnumerator Provision()
        {
            if (flasks != null && prog.Gold.Value > GoldReserve + 20)
            {
                yield return WalkToVendor(Shop.Vendor.Merchant);
                yield return BuyPotions(Flasks.Kind.Health, "Health", 5);
                yield return BuyPotions(Flasks.Kind.Mana, "Mana", 3);
                yield return BuyPotions(Flasks.Kind.Frost, "Frost", 2);
                yield return BuyBetterGear(Shop.Vendor.Merchant);
                if (flasks.RareHeld == Progression.RareItem.None) yield return SpendAt(Shop.Vendor.Merchant, Lobby.MerchantSpot, it => it.Kind == Shop.ItemKind.Rare, 1, GoldReserve + 120);
            }
            yield return WalkToVendor(Shop.Vendor.Smith);
            yield return BuyBetterGear(Shop.Vendor.Smith);
            yield return SpendAt(Shop.Vendor.Shrine, Lobby.ShrineSpot, it => it.Kind == Shop.ItemKind.Relic, 2, GoldReserve + 40);
            yield return SpendAt(Shop.Vendor.Yard, Lobby.YardSpot, it => it.Kind == Shop.ItemKind.Drill && (it.Stat != StatType.MP || cls == Classes.ClassId.Mage), 8, GoldReserve + 40);
            yield return SpendAt(Shop.Vendor.Shrine, Lobby.ShrineSpot, it => it.Kind == Shop.ItemKind.Blessing, 1, GoldReserve + 20);
        }

        private string KitLine()
        {
            string sockets = string.Join("/", Enumerable.Range(0, Runes.SocketsAt(prog.WeaponLevel.Value)).Select(k => prog.Socketed(k) == Rune.None ? "empty" : Runes.Name(prog.Socketed(k))));
            string relicNames = string.Join(", ", Enumerable.Range(0, prog.RelicCount).Select(prog.RelicLabel));
            string bagRunes = string.Join(", ", Enumerable.Range(0, prog.RuneBagCount).Select(i => Runes.Name(prog.RuneInBag(i))));
            int drills = 0;
            foreach (StatType st in System.Enum.GetValues(typeof(StatType))) drills += prog.DrillCount(st);
            return cls + " | weapon +" + prog.WeaponLevel.Value + " hones " + prog.Hones.Value + " path " + (prog.WeaponPath.Value == 0 ? "none" : Classes.WeaponPaths.Name(cls, prog.WeaponPath.Value)) + RelicTiers() +
                " | sockets [" + sockets + "] | armour " + Gear.Name(prog.Equipped(GearSlot.Armor)) + " d" + prog.Equipped(GearSlot.Armor).Depth + " (" + GearScore(prog.Equipped(GearSlot.Armor)).ToString("F1") + ")" +
                " | charm " + Gear.Name(prog.Equipped(GearSlot.Charm)) + " d" + prog.Equipped(GearSlot.Charm).Depth + " (" + GearScore(prog.Equipped(GearSlot.Charm)).ToString("F1") + ")" +
                " | Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E) + " | drills " + drills + " | relics " + prog.RelicCount + " [" + relicNames + "]" +
                " | stats HP " + prog.Value(StatType.HP).ToString("F0") + " ATT " + prog.Value(StatType.ATT).ToString("F0") + " DEF " + prog.Value(StatType.DEF).ToString("F0") + " DEX " + prog.Value(StatType.DEX).ToString("F0") +
                " | held: gold " + prog.Gold.Value + ", embers " + prog.Embers.Value + ", sigils " + prog.Sigils.Value + ", spare runes [" + bagRunes + "], pack " + prog.BagCount +
                (flasks != null ? ", pouch H" + flasks.Count(Flasks.Kind.Health) + " M" + flasks.Count(Flasks.Kind.Mana) + " F" + flasks.Count(Flasks.Kind.Frost) + " W" + flasks.Count(Flasks.Kind.Holy) + " rare " + Progression.RareItems.Short(flasks.RareHeld) : "");
        }

        /// <summary>Leaving camp: the kit as it stands, and anything a careful player would not have left undone (tools/test_party.py fails on these).</summary>
        private void AuditKit()
        {
            L("KIT " + KitLine());
            var left = new List<string>();
            for (int i = 0; i < prog.BagCount; i++)
                if (GearScore(prog.BagItem(i)) > GearScore(prog.Equipped(Gear.SlotOf((GearId)prog.BagItem(i).Id))) + 0.001f) left.Add("a better piece in the pack: " + Gear.Name(prog.BagItem(i)));
            if (prog.CannotTemperReason() == null) left.Add("could still temper at +" + prog.WeaponLevel.Value);
            if (prog.CannotHoneReason() == null) left.Add("could still hone");
            if (prog.OpenEdgeSlot >= 0) left.Add("an edge not chosen");
            if (GearWorkLeft(out byte sl, out byte mo)) left.Add("could still " + (mo == 0 ? "refine " : "attune ") + Gear.Name(prog.Equipped((GearSlot)sl)) + " with " + prog.Embers.Value + " embers");
            if (RankWorkLeft(out byte rs)) left.Add("Sigils unspent: " + prog.Sigils.Value + " with " + (rs == 0 ? "Q" : "E") + " to rank");
            if (MeldReady(out var mr)) left.Add("three " + Runes.Name(mr) + " not melded");
            if (BestSocketing(out var br, out int bs)) left.Add(Runes.Name(br) + " not set in socket " + bs);
            foreach (var w in left) Problem("UNSPENT " + w);
        }

        private readonly HashSet<ulong> ignoredGear = new HashSet<ulong>();
        private float dormantSince = -1f, nextDormantNote, outsideSince = -1f;

        private GearDrop NearestGear(float within)
        {
            GearDrop best = null;
            float bd = within;
            foreach (var g in Interactable.All.OfType<GearDrop>().ToArray())
            {
                if (g == null || !g.IsSpawned || ignoredGear.Contains(g.NetworkObjectId) || g.Prompt(prog) == null) continue;
                float d = Vector3.Distance(g.transform.position, me.transform.position);
                if (d < bd && (d < 2.2f || Reachable(g.transform.position, new NavMeshPath()))) { bd = d; best = g; }
            }
            return best;
        }

        private IEnumerator SpendAt(Shop.Vendor vendor, Vector3 at, System.Func<Shop.Item, bool> want, int maxBuys, int keep)
        {
            var stock = Shop.For(me.OwnerClientId, vendor);
            if (!stock.Any(want)) yield break;
            at = Shop.Position(vendor) ?? at; // trade follows the person (they may be out on an errand)
            yield return WalkTo(at + Vector3.back * 1.6f, 1.2f);
            for (int n = 0; n < maxBuys; n++)
            {
                stock = Shop.For(me.OwnerClientId, vendor);
                int best = -1;
                for (int i = 0; i < stock.Length; i++)
                    if (want(stock[i]) && prog.CannotBuyReason(vendor, i) == null && prog.Gold.Value - stock[i].Price >= keep && (best < 0 || stock[i].Price < stock[best].Price)) best = i;
                if (best < 0) break;
                int g0 = prog.Gold.Value;
                prog.BuyRpc(vendor, best);
                yield return new WaitForSeconds(0.3f);
                L((prog.Gold.Value < g0 ? "bought " : "failed to buy ") + stock[best].Name + " (" + stock[best].Price + ") from " + vendor);
            }
        }

        private string RelicTiers()
        {
            int t = 0;
            for (int i = 0; i < prog.RelicCount; i++) t += prog.RelicTierAt(i);
            string edges = "";
            for (int s = 0; s < Progression.Edges.Slots; s++) if (prog.EdgeAt(s) != Progression.Edge.None) edges += (edges.Length > 0 ? "/" : "") + Progression.Edges.Name(prog.EdgeAt(s));
            return (t > 0 ? " (+" + t + " tiers)" : "") + (edges.Length > 0 ? ", edges " + edges : "");
        }

        private double clockServer0 = -1, clockReal0;

        /// <summary>Server clock against the wall clock since the first status (a run summary once said 952 minutes for a 97-minute run).</summary>
        private string ClockCheck()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            if (nm == null) return "";
            double st = nm.ServerTime.Time, rt = Time.realtimeSinceStartupAsDouble;
            if (clockServer0 < 0) { clockServer0 = st; clockReal0 = rt; return ""; }
            double dr = rt - clockReal0;
            return dr > 1 ? ", clock x" + ((st - clockServer0) / dr).ToString("F2") + " (run start " + (World.DungeonDirector.Instance != null ? World.DungeonDirector.Instance.RunStartedAt.Value.ToString("F0") : "-") + ", now " + st.ToString("F0") + ")" : "";
        }

        // ---- What the bot actually did with its kit: the abilities it set going, per floor (2026-11-03: in a four-bot run
        // three bots never attacked at all and the log did not say so). ----
        private readonly List<AbilityView> abilityViews = new List<AbilityView>();
        private readonly Dictionary<string, float> abilityWas = new Dictionary<string, float>();
        private readonly Dictionary<string, int> abilityUses = new Dictionary<string, int>();

        private void CountAbilities()
        {
            if (me == null || me.ActiveKit == null) return;
            abilityViews.Clear();
            me.ActiveKit.FillAbilities(abilityViews);
            foreach (var v in abilityViews)
            {
                string key = v.Key + " " + v.Name;
                abilityWas.TryGetValue(key, out float was);
                if (v.Remaining > 0.05f && was <= 0.05f) { abilityUses.TryGetValue(key, out int n); abilityUses[key] = n + 1; }
                abilityWas[key] = v.Remaining;
            }
        }

        private string AbilitiesLine()
        {
            string line = abilityUses.Count == 0 ? "none" : string.Join(", ", abilityUses.Select(kv => kv.Key + " x" + kv.Value));
            abilityUses.Clear();
            return line;
        }

        private float nextSecondary, nextFlask;
        private const float BeamReach = 14f;

        /// <summary>The most hurt other player within reach, for the kits that heal them.</summary>
        private PlayerLife HurtAlly(float below)
        {
            PlayerLife best = null;
            float low = below;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p == life || p.Health == null || !p.Health.IsAlive || p.Downed.Value) continue;
                if (Vector3.Distance(p.transform.position, me.transform.position) > BeamReach || p.Health.Fraction >= low) continue;
                low = p.Health.Fraction;
                best = p;
            }
            return best;
        }

        /// <summary>
        /// The right mouse button, which the bot never used: frostbolts, fans, the Scourge, the Cleric's beam and the Plague
        /// Doctor's tonic, Disengage when something is on top of the Ranger. (The Knight's is its block, handled apart.)
        /// </summary>
        private IEnumerator UseSecondary(Health target, float dist)
        {
            if (Time.time < nextSecondary || cls == Classes.ClassId.Knight) yield break;
            switch (cls)
            {
                case Classes.ClassId.Mage:
                    nextSecondary = Time.time + 2.6f;
                    yield return HoldSecondary(0.15f);
                    break;
                case Classes.ClassId.Rogue:
                    nextSecondary = Time.time + 4f;
                    yield return HoldSecondary(0.1f);
                    break;
                case Classes.ClassId.Flagellant:
                    if (hp.Fraction < 0.3f) yield break;
                    nextSecondary = Time.time + 4f;
                    yield return HoldSecondary(0.1f);
                    break;
                case Classes.ClassId.Ranger:
                    if (dist > 3.5f) yield break;
                    nextSecondary = Time.time + 6f;
                    yield return HoldSecondary(0.1f);
                    break;
                case Classes.ClassId.Cleric:
                case Classes.ClassId.PlagueDoctor:
                {
                    var ally = HurtAlly(0.7f);
                    if (ally == null && hp.Fraction >= 0.6f) yield break;
                    nextSecondary = Time.time + (cls == Classes.ClassId.Cleric ? 2f : 6f);
                    abilityUses.TryGetValue("RMB heal", out int healUses);
                    abilityUses["RMB heal"] = healUses + 1;
                    // Look at whoever needs it (no one under the crosshair: it lands on yourself).
                    if (ally != null) Face(ally.Health.Center);
                    yield return HoldSecondary(cls == Classes.ClassId.Cleric ? 1.2f : 0.15f);
                    if (target != null) Face(target.Center);
                    break;
                }
            }
        }

        private IEnumerator HoldSecondary(float seconds)
        {
            UI.Binds.BotSetSecondary(true);
            float until = Time.time + seconds;
            while (Time.time < until) yield return null;
            UI.Binds.BotSetSecondary(false);
            yield return null;
        }

        private string Status(string where)
        {
            var kit = me.ActiveKit;
            string s = where + " | hp " + hp.Current.Value.ToString("F0") + "/" + hp.Max.Value.ToString("F0") + ", gold " + prog.Gold.Value + ", embers " + prog.Embers.Value + ", sigils " + prog.Sigils.Value +
                       ", weapon +" + prog.WeaponLevel.Value + (prog.Hones.Value > 0 ? " honed " + prog.Hones.Value : "") + ", Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E) + ", relics " + prog.RelicCount + RelicTiers() +
                       (flasks != null ? ", pouch H" + flasks.Count(Flasks.Kind.Health) + " M" + flasks.Count(Flasks.Kind.Mana) : "") + ", armour " + Gear.Name(prog.Equipped(GearSlot.Armor)) + ", charm " + Gear.Name(prog.Equipped(GearSlot.Charm));
            s += ClockCheck();
            s += ", hands " + (life.CanAct ? "free" : "BUSY") + (motor.ActionsReady ? "" : " NOT-READY") + (Application.isFocused ? " focused" : "");
            L(s);
            return s;
        }

        // ------------------------------------------------------------------ a floor

        /// <summary>
        /// Runs a bot routine and keeps going if it throws: a follower once lost its whole run to one NullReference on
        /// floor 1 (2026-11-07) and stood idle. The error is a Problem in the log, then the routine starts over (3 times).
        /// </summary>
        private float nextSpinNote;
        /// <summary>Seconds on a floor before the bot stops clearing and heads for the guardian.</summary>
        private const float HurryAfter = 390f;
        private bool hurrying;
        /// <summary>Things used this run, by kind (written to the log at each floor's end).</summary>
        private readonly Dictionary<string, int> usedThings = new Dictionary<string, int>();

        private IEnumerator Guarded(System.Func<IEnumerator> routine)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                // Nested steps (StepToward, Press, Shot...) are driven here too, so their errors are caught as well.
                var stack = new System.Collections.Generic.Stack<IEnumerator>();
                stack.Push(routine());
                bool threw = false;
                // 2026-11-13: the bot froze the whole game (one core at 100%, not responding) on floor 1 of the full
                // playtest. Nested steps run here by hand, and a step that ends without waiting a frame, called in a
                // loop, never handed the frame back. Now a long run of frameless steps waits a frame, and says so.
                int frameless = 0;
                while (stack.Count > 0)
                {
                    object cur;
                    if (++frameless > 400)
                    {
                        frameless = 0;
                        if (Time.time >= nextSpinNote)
                        {
                            nextSpinNote = Time.time + 30f;
                            string where = "";
                            foreach (var e in stack) where = e.GetType().Name.Split('>')[0].TrimStart('<') + (where.Length > 0 ? " > " + where : "");
                            Problem("bot loop ran 400 steps without a frame (" + where + "); waiting a frame");
                        }
                        yield return null;
                        continue;
                    }
                    try
                    {
                        var top = stack.Peek();
                        if (!top.MoveNext()) { stack.Pop(); continue; }
                        cur = top.Current;
                        if (cur is IEnumerator inner) { stack.Push(inner); continue; }
                    }
                    catch (System.Exception ex)
                    {
                        Problem("bot routine threw (" + ex.GetType().Name + ": " + ex.Message + ") " + ex.StackTrace?.Split((char)10)[0]?.Trim() + "; starting it over");
                        threw = true;
                        break;
                    }
                    frameless = 0;
                    yield return cur;
                }
                if (!threw) yield break;
                yield return new WaitForSeconds(1f);
                if (DungeonDirector.InLobby || DungeonDirector.Instance == null || DungeonDirector.Instance.Phase.Value != RunPhase.Exploring) yield break;
            }
        }

        private IEnumerator Floor()
        {
            savesFloor = 0;
            foreach (var pl in PlayerLife.All)
                if (pl != null && party.TryGetValue(pl.NetworkObjectId, out var pm) && pl.GetComponent<PlayerProgress>() is PlayerProgress pc) pm.Name = Health.NameOf(pl.NetworkObjectId) + " (" + pc.ClassId.Value + ")";
            bossHpMark = -1f;
            for (float t = 0f; t < 30f && (DungeonDirector.InLobby || DungeonDirector.Layout == null); t += 0.5f) yield return new WaitForSeconds(0.5f);
            var dd = DungeonDirector.Instance;
            var layout = DungeonDirector.Layout;
            if (layout == null || DungeonDirector.InLobby) { Problem("no floor to play"); yield break; }
            floorNo = layout.Depth;
            yield return new WaitForSeconds(2.5f);
            if (follower) { dd.DebugBakeLocalNavMesh(); yield return null; }
            foreach (var m in party.Values) m.SavesFloor = 0;
            var partyStart = party.ToDictionary(kv => kv.Key, kv => (kv.Value.Dealt, kv.Value.Taken, kv.Value.BossDealt));
            float floorStart = Time.time, takenStart = taken, dealtStart = dealt;
            hurrying = false;
            int killsStart = kills, downsStart = downs;
            L("== FLOOR " + (floorNo + 1) + " (" + DungeonTheme.FloorName(floorNo) + "), " + layout.Rooms.Count + " rooms, event " + dd.FloorEventValue.Value + " ==");
            Status("arrived");
            yield return Shot("F" + (floorNo + 1) + "_arrive");

            float nextShot = Time.time + 30f, nextStatus = Time.time + 60f, bossSeenAt = -1f, stuckSince = Time.time, nextAbility = Time.time + 2f;
            Vector3 lastPos = me.transform.position;
            Health target = null;
            Vector3? goal = null;
            string goalWhat = "";
            float floorLimit = 11f * 60f;
            var openedChests = new HashSet<ulong>();
            var skip = new HashSet<ulong>();

            ulong lastLootId = ulong.MaxValue;
            while (dd.Phase.Value == RunPhase.Exploring && !DungeonDirector.InLobby && Time.time - floorStart < floorLimit)
            {
                Cursor.lockState = CursorLockMode.Locked;
                CountAbilities();
                if (!follower)
                    foreach (var p in PlayerLife.All)
                        if (p != null && p != life && p.Downed.Value) { L("stood " + Health.NameOf(p.NetworkObjectId) + " back up"); p.ServerRevive(1f); }
                if (life.Downed.Value && follower) { yield return new WaitForSeconds(1f); continue; }
                if (life.Downed.Value)
                {
                    downs++;
                    L("down #" + downs + " (this floor " + (downs - downsStart) + "); getting back up to keep playing");
                    yield return Shot("F" + (floorNo + 1) + "_downed");
                    yield return new WaitForSeconds(2f);
                    life.ServerRevive(1f);
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }
                if (pendingHeal) { pendingHeal = false; hp.ServerSetFraction(0.6f); }
                // Bookkeeping first, so no branch below can skip it (a `continue` toward a sleeping guardian's arena
                // once froze the bot without a word, 2026-10-27).
                // Stuck: no progress for 12 s.
                if ((me.transform.position - lastPos).sqrMagnitude > 1f) { lastPos = me.transform.position; stuckSince = Time.time; gateWaitSince = -1f; }
                else if (Time.time - stuckSince > 12f && ShutGateNear(me.transform.position) && (gateWaitSince < 0f || Time.time - gateWaitSince < 90f))
                {
                    // A gate shut in front of us (an ambush sprung on whoever was inside, an arena sealed): by design it lifts
                    // when the fight inside ends, so wait for it rather than call it stuck (2026-11-09, the commonest "stuck").
                    if (gateWaitSince < 0f) { gateWaitSince = Time.time; L("waiting at a shut gate (" + goalWhat + " is beyond it)"); }
                    stuckSince = Time.time;
                }
                else if (Time.time - stuckSince > 12f)
                {
                    Problem("stuck for 12 s going for " + goalWhat + " at " + Fmt(goal) + " from " + Fmt(me.transform.position) + " | " + WhyStuck(goal));
                    if (target != null) skip.Add(target.NetworkObjectId);
                    // Loot it cannot get to (behind a secret wall, on a ledge): give it up rather than walk into the wall all floor.
                    if (goalWhat == "loot" && lastLootId != ulong.MaxValue) ignoredLoot.Add(lastLootId);
                    stuckSince = Time.time;
                    if (follower && Leader != null) { motor.Teleport(Leader.transform.position - Leader.transform.forward * 2f + Vector3.up * 0.2f, me.transform.rotation); L("caught up with the leader"); }
                }
                if (Time.time >= nextShot) { nextShot = Time.time + 35f; yield return Shot("F" + (floorNo + 1) + "_" + (int)(Time.time - floorStart) + "s"); }
                if (Time.time >= nextStatus) { nextStatus = Time.time + 75f; Status("progress (" + LivingEnemies() + " enemies left)"); }
                // Block (Knight) or dodge a swing that is coming: a red ! within reach.
                if (Time.time >= nextBlock && ThreatNear(3.6f) && (BossBrain.Active == null || Random.value < 0.5f))
                {
                    nextBlock = Time.time + 1.6f;
                    if (cls == Classes.ClassId.Knight) yield return Block(0.6f);
                    else yield return Press(UI.Bind.Dodge);
                }
                // A potion below 40%.
                if (flasks != null && hp.Current.Value < hp.Max.Value * 0.4f && flasks.Count(Flasks.Kind.Health) > 0)
                {
                    int before = flasks.Count(Flasks.Kind.Health);
                    yield return Press(UI.Bind.Potion1);
                    yield return null;
                    // Count drinks, not presses (2026-11-14: a press during the drink cooldown counted, 47 "drinks" on a floor).
                    if (flasks.Count(Flasks.Kind.Health) < before)
                    {
                        abilityUses.TryGetValue("health potion", out int hn);
                        abilityUses["health potion"] = hn + 1;
                    }
                }
                if (me.ActiveKit != null && me.ActiveKit.Resource < me.ActiveKit.ResourceMax * 0.15f && flasks != null && flasks.Count(Flasks.Kind.Mana) > 0) yield return Press(UI.Bind.Potion2);
                // The rare slot, as a player would: a defensive draught when it is going badly, the rest at the guardian.
                if (flasks != null && flasks.RareHeld != Progression.RareItem.None && !flasks.InStasis)
                {
                    var rare = flasks.RareHeld;
                    bool low = hp.Current.Value < hp.Max.Value * 0.35f;
                    bool atBoss = BossBrain.Active != null && dd.BossSealed;
                    if ((low && (rare == Progression.RareItem.Ironbark || rare == Progression.RareItem.GraveStillness || rare == Progression.RareItem.SanctuaryTome))
                        || (atBoss && (rare == Progression.RareItem.Bloodfury || rare == Progression.RareItem.PhoenixTome || rare == Progression.RareItem.StormTome)))
                    {
                        if (follower) { flasks.UseRareRpc(); L("used " + Progression.RareItems.Name(rare)); }
                        else if (flasks.ServerUseRare()) L("used " + Progression.RareItems.Name(rare) + (low ? " (low on health)" : " (at the guardian)"));
                    }
                }

                // Pick what to do: the nearest living enemy we can walk to (the guardian last), else loot, else a chest.
                var boss = BossBrain.Active;
                target = NearestEnemy(skip, out float dist, allowBoss: false);
                // A soulbound twin lying severed cannot be hurt: go for the one still standing, as the screen tells a player to.
                var standingTwin = StandingTwin();
                if (standingTwin != null) { target = standingTwin; dist = Vector3.Distance(target.transform.position, me.transform.position); }
                stuckTarget = target;
                if (target == null && boss != null && !boss.IsDead) { target = boss.GetComponent<Health>(); dist = Vector3.Distance(target.transform.position, me.transform.position); }
                stuckTarget = target;
                // A healing pylon comes before the guardian, as it would for a player (the user watched a bot hit a boss
                // that its pylons kept topping up).
                if (target != null && boss != null && target == boss.GetComponent<Health>())
                {
                    Breakable pylon = null;
                    float pd = float.MaxValue;
                    foreach (var p in Breakable.Pylons)
                        if (p != null && p.Health != null && p.Health.IsAlive && Vector3.Distance(p.transform.position, me.transform.position) < pd) { pd = Vector3.Distance(p.transform.position, me.transform.position); pylon = p; }
                    // Any standing pylon, not only a feeding one: a hit stops the beam for 4 s, so 'feeding' flickered off after
                    // each swing and the bot went back to the guardian, which two pylons out-healed (2026-11-13, Hollow King, 5 min).
                    if (pylon != null) { target = pylon.Health; dist = pd; }
                }
                if (boss != null && bossSeenAt < 0f && dd.BossSealed) { bossSeenAt = Time.time; L("guardian fight begins: " + boss.DisplayName); yield return Shot("F" + (floorNo + 1) + "_boss_start"); }

                // The guardian sleeps until the whole party is in its arena. Run of 2026-11-04: on floor 2 it slept for eight
                // minutes with the bots beating on it and no record of who stood outside. Say who, and walk in.
                if (boss != null && boss.Dormant && LivingEnemies() <= 3)
                {
                    if (dormantSince < 0f) dormantSince = Time.time;
                    if (Time.time - dormantSince > 90f && Time.time >= nextDormantNote)
                    {
                        nextDormantNote = Time.time + 30f;
                        var outside = PlayerLife.All.Where(p => p != null && !p.Downed.Value && !layout.BossRoom.Contains(p.transform.position, -1f)).Select(p => "client " + p.OwnerClientId + " at " + Fmt(p.transform.position));
                        Problem("guardian asleep for " + (Time.time - dormantSince).ToString("F0") + " s with " + LivingEnemies() + " enemies left, me at " + Fmt(me.transform.position) + ", goal " + goalWhat + ", outside the arena: " + string.Join("; ", outside));
                        if (!layout.BossRoom.Contains(me.transform.position, -2f)) motor.Teleport(layout.BossRoom.Center + Vector3.right * 4f + Vector3.up * 0.2f, me.transform.rotation);
                    }
                }
                else dormantSince = -1f;

                var lead = follower ? Leader : null;
                float leadDist = lead != null ? Vector3.Distance(Flat(lead.transform.position), Flat(me.transform.position)) : 0f;
                // A client cannot see the guardian sleeping: if the leader has waited in its arena for a while, join them.
                if (lead != null && boss != null && LivingEnemies() <= 3 && layout.BossRoom.Contains(lead.transform.position, -1f) && !layout.BossRoom.Contains(me.transform.position, -1.5f))
                {
                    if (outsideSince < 0f) outsideSince = Time.time;
                    if (Time.time - outsideSince > 30f)
                    {
                        outsideSince = -1f;
                        Problem("stood outside the arena for 30 s with the leader inside, at " + Fmt(me.transform.position));
                        motor.Teleport(layout.BossRoom.Center + Vector3.left * 4f + Vector3.up * 0.2f, me.transform.rotation);
                        yield return null;
                        continue;
                    }
                }
                else outsideSince = -1f;

                // Far behind (the leader rode ahead, or a wall between): catch up, as a friend would run to rejoin.
                if (lead != null && (leadDist > 40f || (dd.BossSealed && !layout.BossRoom.Contains(me.transform.position, 0f) && layout.BossRoom.Contains(lead.transform.position, 0f))))
                {
                    motor.Teleport(lead.transform.position - lead.transform.forward * 2f + Vector3.up * 0.2f, me.transform.rotation);
                    yield return null;
                    continue;
                }
                var loot = follower && leadDist > 10f ? null : NearestLoot(follower ? 8f : 18f);
                lastLootId = loot != null ? loot.NetworkObjectId : ulong.MaxValue;
                var chest = follower ? null : NearestChest(openedChests);
                var myGear = target == null || dist >= 14f ? NearestGear(22f) : null;
                // HURRY (2026-11-13 playtest: on floor 8 rifts kept a choir coming, there was always something left to fight,
                // and the bot never reached the Pale Cardinal in 11 minutes): after a while on a floor, push for the guardian - fight only what is in the way, leave the loot.
                if (!follower && Time.time - floorStart > HurryAfter)
                {
                    if (!hurrying) { hurrying = true; L("hurrying to the guardian (" + ((Time.time - floorStart) / 60f).ToString("F1") + " min on the floor, " + LivingEnemies() + " still about)"); }
                    loot = null; chest = null; myGear = null;
                    // Not once the arena is sealed: a guardian's wards (the Cardinal's choir) are the way through (2026-11-13: the Knight
                    // stood under an immune Cardinal for six minutes ignoring the choir 10 m away).
                    if (target != null && dist > 6f && !dd.BossSealed && !(dd.AmbushWave >= 0 && dd.AmbushRoom != null && dd.AmbushRoom.Contains(me.transform.position, 0f)) && !(target.GetComponent<EnemyBrain>() is BossBrain)) target = null;
                }
                if (lead != null && target == null && loot == null && myGear == null)
                {
                    goal = lead.transform.position; goalWhat = "the leader";
                    if (leadDist > 3.5f) yield return StepToward(lead.transform.position, 3f);
                    else { stuckSince = Time.time; yield return new WaitForSeconds(0.2f); }
                    yield return null;
                    continue;
                }
                if (target != null && (dist < 14f || loot == null))
                {
                    goal = target.transform.position; goalWhat = Health.NameOf(target.NetworkObjectId);
                    float reach = Mathf.Max(1.8f, me.ActiveKit != null ? me.ActiveKit.AttackRange : 2.5f);
                    bool melee = reach < 4f;
                    // Measured to the body's edge: a giant is struck from outside it, not by walking into it.
                    float edge = target.BodyRadius;
                    float want = melee ? edge + reach * 0.75f : Mathf.Min(reach * 0.7f, 10f) + edge;
                    float flatDist = Vector3.Distance(Flat(target.transform.position), Flat(me.transform.position));
                    bool clear = CombatQuery.ClearLine(me.transform.position + Vector3.up * 1.6f, target);
                    // A sleeping guardian wakes only when the party is in its arena: a bow from the doorway does nothing
                    // (run 13, the Ranger, shot a dormant boss from outside for eight minutes).
                    // Not "boss.Dormant": that lives on the server, so a client always read it as awake and a Cleric on a client stood at
                    // range in the doorway holding the fight up (F2 of one run, F10 of the next, 2026-11-04). Outside is outside.
                    if (boss != null && target == boss.GetComponent<Health>() && !layout.BossRoom.Contains(me.transform.position, -1.5f))
                    {
                        yield return StepToward(layout.BossRoom.Center, 2f);
                        continue;
                    }
                    // Hits not landing for a while (a shield, a guard): go round behind it, as a player would.
                    // Only for a few seconds at a time, then back to fighting: a flank spot it cannot reach (the Lich backed into
                    // water) once kept the Ranger walking in place for seven minutes, never shooting, so the boss's health
                    // never moved and the flank never ended (2026-10-28).
                    bool flank = boss != null && target == boss.GetComponent<Health>() && bossHpMark >= 0f && Time.time - bossHpAt > 6f;
                    if (flank && flankStarted < 0f) flankStarted = Time.time;
                    if (flank && Time.time - flankStarted > 4f) { bossHpAt = Time.time; flankStarted = -1f; flank = false; }
                    if (!flank) flankStarted = -1f;
                    if (flank)
                    {
                        var behind = target.transform.position - target.transform.forward * (edge + reach * 0.6f);
                        if (Vector3.Distance(Flat(me.transform.position), Flat(behind)) > 1.2f) { yield return StepToward(behind, 0.6f); continue; }
                    }
                    // In melee reach a clipped line (a crate edge, a rail) is no reason to stand there: swing.
                    // No clear line (a dais lip, a pillar, a crate): close in, and if already close, circle round it, as a
                    // player would (2026-10-28: the Ranger stood 5 m from a raised Tide Lich for seven minutes, "in range").
                    if (!clear && !melee && flatDist <= want)
                    {
                        if (flatDist > edge + 2f) yield return StepToward(target.transform.position, edge + 1.5f);
                        else
                        {
                            var round = Quaternion.Euler(0f, 70f * (Time.frameCount % 2 == 0 ? 1f : -1f), 0f) * (Flat(me.transform.position) - Flat(target.transform.position)).normalized;
                            yield return StepToward(target.transform.position + round * (edge + 3f), 0.5f);
                        }
                    }
                    else if (flatDist > want || (!clear && !melee)) yield return StepToward(target.transform.position, want * 0.9f);
                    // Rooted (an Entling holding you): no stepping anywhere, so fight what is in reach.
                    else if (melee && flatDist < edge + 0.6f && !(me.GetComponent<PlayerDebuffs>() is PlayerDebuffs rootedNow && rootedNow.Rooted))
                    {
                        // Too close (inside its reach and ours is wasted): step back out to the edge.
                        var back = Flat(me.transform.position) - Flat(target.transform.position);
                        if (back.sqrMagnitude < 0.01f) back = -me.transform.forward;
                        yield return StepToward(me.transform.position + back.normalized * 1.5f, 0.2f);
                    }
                    else
                    {
                        // Ranged shots lead a moving target, as a player's would (run 14's Ranger aimed where bosses had been).
                        var aimAt = target.Center;
                        if (!melee && target.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var tAgent) && tAgent.enabled)
                            aimAt += tAgent.velocity * (flatDist / (cls == Classes.ClassId.Ranger ? RangerKit.ArrowSpeed : cls == Classes.ClassId.Mage ? MageKit.FireboltSpeed : 24f));
                        // Arrows drop: aim over the target by the fall a full draw takes to get there, as an archer would
                        // (2026-10-28: the Ranger bot's long shots landed short all run and every guardian took 6-8 min).
                        if (cls == Classes.ClassId.Ranger)
                        {
                            float tof = flatDist / RangerKit.ArrowSpeed;
                            aimAt += Vector3.up * 0.5f * RangerKit.ArrowGravity(1f) * tof * tof;
                        }
                        Face(aimAt);
                        yield return UseSecondary(target, flatDist);
                        // Abilities now and then, when something is close.
                        if (Time.time >= nextAbility)
                        {
                            // Whatever is off cooldown (it used to be one at random every few seconds: abilities sat ready for most of a fight).
                            nextAbility = Time.time + Random.Range(1f, 1.6f);
                            var ready = new List<AbilityView>();
                            if (me.ActiveKit != null) me.ActiveKit.FillAbilities(ready);
                            foreach (var v in ready)
                            {
                                if (v.Remaining > 0.05f) continue;
                                if (v.Key == "Q") yield return Press(UI.Bind.AbilityQ);
                                else if (v.Key == "E") yield return Press(UI.Bind.AbilityE);
                            }
                            if (me.GetComponent<Ultimates>() is Ultimates u && u.Ready)
                            {
                                yield return Press(UI.Bind.Ultimate);
                                abilityUses.TryGetValue("R Ultimate", out int ur);
                                abilityUses["R Ultimate"] = ur + 1;
                            }
                            // Thrown flasks: at a guardian, as a player saves them.
                            if (flasks != null && boss != null && target == boss.GetComponent<Health>() && Time.time >= nextFlask)
                            {
                                var throwKind = flasks.Count(Flasks.Kind.Frost) > 0 ? Flasks.Kind.Frost : Flasks.Kind.Holy;
                                if (flasks.Count(throwKind) > 0)
                                {
                                    nextFlask = Time.time + 9f;
                                    yield return Press(throwKind == Flasks.Kind.Frost ? UI.Bind.Potion3 : UI.Bind.Potion4);
                                    string fk = "flask " + throwKind;
                                    abilityUses.TryGetValue(fk, out int fn);
                                    abilityUses[fk] = fn + 1;
                                }
                            }
                        }
                        yield return Attack(cls == Classes.ClassId.Ranger ? 0.9f : 0.8f);
                        stuckSince = Time.time; // fighting in place is not being stuck
                        // A guardian that does not lose health while we hit it: say why.
                        if (boss != null && target == boss.GetComponent<Health>())
                        {
                            var bh = target;
                            if (bossHpMark < 0f || bh.Current.Value < bossHpMark - 1f) { bossHpMark = bh.Current.Value; bossHpAt = Time.time; }
                            else if (Time.time - bossHpAt > 30f)
                            {
                                bossHpAt = Time.time;
                                var eye = me.transform.position + Vector3.up * 1.6f;
                                var fwd = me.transform.forward;
                                bool inArc = CombatQuery.Arc(eye, fwd, 2.7f, 120f, Team.Enemies).Contains(bh);
                                var to = bh.Center - eye;
                                bool lineOk = CombatQuery.ClearLine(eye, bh);
                                string blocker = Physics.Linecast(eye, bh.Center, out var lh, ~0, QueryTriggerInteraction.Ignore) ? lh.collider.name + "/" + (lh.collider.GetComponentInParent<Health>() != null) : "none";
                                float before = bh.Current.Value;
                                float d = follower ? -1f : bh.ServerDamage(10f, me.transform.position, me.NetworkObjectId);
                                Problem("guardian at " + (bh.Fraction * 100f).ToString("F0") + "% has not lost health in 30 s of attacking: in swing arc " + inArc + ", flat dist " + new Vector2(to.x, to.z).magnitude.ToString("F1") + " (body r " + bh.BodyRadius.ToString("F1") + "), vertical " + to.y.ToString("F1") + " (centre h " + bh.CenterHeight.ToString("F1") + "), clear line " + clear + " (" + blocker + "), dormant " + boss.Dormant + ", immune " + boss.Immune + ", a test hit of 10 dealt " + d.ToString("F1") + ", my " + (me.ActiveKit != null ? me.ActiveKit.ResourceName + " " + me.ActiveKit.Resource.ToString("F0") + "/" + me.ActiveKit.ResourceMax.ToString("F0") : "?") + ", can act " + life.CanAct + ", my hits on it in that time " + bossHits + " for " + bossDealt.ToString("F0"));
                                bossHits = 0; bossDealt = 0f;
                            }
                        }
                    }
                }
                else if (loot != null)
                {
                    goal = loot.transform.position; goalWhat = "loot";
                    // Standing on it and it stays (a full pouch, someone else's): leave it.
                    if (Vector3.Distance(Flat(loot.transform.position), Flat(me.transform.position)) < 0.8f)
                    {
                        if (lootOnSince < 0f) lootOnSince = Time.time;
                        else if (Time.time - lootOnSince > 2.5f) { ignoredLoot.Add(loot.NetworkObjectId); lootOnSince = -1f; L("left loot it cannot take (" + loot.name + ")"); }
                    }
                    else lootOnSince = -1f;
                    yield return StepToward(loot.transform.position, 0.3f);
                }
                else if (myGear != null)
                {
                    goal = myGear.transform.position; goalWhat = "gear";
                    if (Vector3.Distance(myGear.transform.position, me.transform.position) > 1.9f) yield return StepToward(myGear.transform.position, 1.6f);
                    else { myGear.UseRpc(me.NetworkObject); ignoredGear.Add(myGear.NetworkObjectId); L("took gear: " + Gear.Name(myGear.Item.Value)); yield return new WaitForSeconds(0.4f); }
                }
                else if (chest != null)
                {
                    string what = chest is Chest ? "chest" : chest.GetType().Name;
                    goal = chest.transform.position; goalWhat = what;
                    if (Vector3.Distance(chest.transform.position, me.transform.position) > 2f) yield return StepToward(chest.transform.position, 1.6f);
                    else
                    {
                        string said = chest.Prompt(prog) ?? "";
                        chest.UseRpc(me.NetworkObject);
                        openedChests.Add(chest.NetworkObjectId);
                        L(chest is Chest ? "opened a chest" : "used " + what + ": " + (said.Length > 110 ? said.Substring(0, 110) + "..." : said));
                        usedThings[what] = usedThings.TryGetValue(what, out var n) ? n + 1 : 1;
                        yield return new WaitForSeconds(0.6f);
                    }
                }
                else
                {
                    // Nothing left in reach: the arena. Walk in so it seals.
                    var arenaAt = layout.BossRoom.Center;
                    goal = arenaAt; goalWhat = "the guardian's arena";
                    yield return StepToward(arenaAt, 2f);
                }

                // Gear for me lying close: pick it up (F), and wear it if it is better (worn first, simple).
                foreach (var g in Interactable.All.OfType<GearDrop>().ToArray())
                    if (g != null && g.Prompt(prog) != null && Vector3.Distance(g.transform.position, me.transform.position) < 2.4f) { g.UseRpc(me.NetworkObject); L("picked up gear"); yield return new WaitForSeconds(0.2f); }

                yield return null;
            }

            float took = Time.time - floorStart;
            if (usedThings.Count > 0) L("used so far this run: " + string.Join(", ", usedThings.Select(kv => kv.Key + " x" + kv.Value)));
            if (!follower && dd.Phase.Value == RunPhase.Exploring && !DungeonDirector.InLobby)
            {
                Problem("floor not finished in " + (floorLimit / 60f).ToString("F0") + " min (" + LivingEnemies() + " enemies left, boss " + (BossBrain.Active != null ? BossBrain.Active.DisplayName + " " + (BossBrain.Active.GetComponent<Health>().Fraction * 100f).ToString("F0") + "%" : "none") + "): forcing it");
                var b = BossBrain.Active;
                if (b != null) b.GetComponent<Health>().Tag("forced").ServerDamage(1e6f, b.transform.position, me.NetworkObjectId);
                for (float t = 0f; t < 8f && dd.Phase.Value == RunPhase.Exploring; t += 0.5f) yield return new WaitForSeconds(0.5f);
            }
            L("floor done in " + (took / 60f).ToString("F1") + " min" + (bossSeenAt > 0f ? " (guardian " + ((Time.time - bossSeenAt) / 60f).ToString("F1") + " min)" : "") +
              ": kills " + (kills - killsStart) + ", dealt " + (dealt - dealtStart).ToString("F0") + ", taken " + (taken - takenStart).ToString("F0") + ", downs " + (downs - downsStart) + ", would-have-gone-down " + savesFloor);
            if (party.Count > 1)
                L("   party this floor: " + string.Join(" | ", party.Select(kv =>
                {
                    var s0 = partyStart.TryGetValue(kv.Key, out var v) ? v : (0f, 0f, 0f);
                    var m = kv.Value;
                    return m.Name + ": dealt " + (m.Dealt - s0.Item1).ToString("F0") + " (boss " + (m.BossDealt - s0.Item3).ToString("F0") + "), taken " + (m.Taken - s0.Item2).ToString("F0") + ", would-have-gone-down " + m.SavesFloor;
                })));
            foreach (var m in party.Values)
            {
                if (m.Source.Count > 0)
                    L("   damage by source, " + m.Name + ": " + string.Join(", ", m.Source.OrderByDescending(s => s.Value).Select(s =>
                        s.Key + " " + s.Value.ToString("F0") + (m.BossSource.TryGetValue(s.Key, out float bs) ? " (boss " + bs.ToString("F0") + " in " + m.BossHits[s.Key] + " hits)" : ""))));
                m.Source.Clear(); m.BossSource.Clear(); m.BossHits.Clear();
            }
            string usedLine = AbilitiesLine();
            L("   abilities used this floor: " + usedLine);
            if (usedLine == "none") Problem("played a whole floor without using one ability");
            // Someone who played a whole floor and hit nothing is a broken bot, not a balance result.
            if (party.Count > 1)
                foreach (var kv in party)
                    if (kv.Value.Dealt - (partyStart.TryGetValue(kv.Key, out var ps) ? ps.Item1 : 0f) <= 0f) Problem(kv.Value.Name + " dealt no damage this floor");
            L("   damage taken so far by source: " + string.Join(", ", takenBy.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value.ToString("F0"))));
            Status("floor end");
            yield return Shot("F" + (floorNo + 1) + "_cleared");
            if (dd.Phase.Value != RunPhase.FloorCleared) yield break;

            // What the guardian left: a player does not ride away from a legendary.
            for (float t0 = Time.time; Time.time - t0 < 25f && dd.Phase.Value == RunPhase.FloorCleared;)
            {
                var left = NearestGear(45f);
                if (left == null) break;
                if (Vector3.Distance(left.transform.position, me.transform.position) > 1.9f) yield return StepToward(left.transform.position, 1.6f);
                else { left.UseRpc(me.NetworkObject); ignoredGear.Add(left.NetworkObjectId); L("took gear: " + Gear.Name(left.Item.Value)); yield return new WaitForSeconds(0.4f); }
            }

            // The grave lift: step in and wait.
            yield return new WaitForSeconds(3f);
            for (float t0 = Time.time; Time.time - t0 < 90f && !DungeonDirector.InLobby && dd.Phase.Value == RunPhase.FloorCleared;)
            {
                if (Vector3.Distance(Flat(me.transform.position), Flat(DungeonDirector.ExitPosition)) > 1.2f) yield return StepToward(DungeonDirector.ExitPosition, 0.5f);
                else yield return new WaitForSeconds(0.2f);
            }
            if (!DungeonDirector.InLobby && dd.Phase.Value == RunPhase.FloorCleared && Vector3.Distance(Flat(me.transform.position), Flat(DungeonDirector.ExitPosition)) > 1.2f)
            {
                // The bot's pathing gave out (it is not a player): put it in the cage so one bad spot does not end the test run.
                Problem("could not walk to the lift from " + Fmt(me.transform.position) + " (" + Vector3.Distance(me.transform.position, DungeonDirector.ExitPosition).ToString("F0") + " m): " + WhyStuck(DungeonDirector.ExitPosition) + "; teleporting");
                me.GetComponent<FirstPersonMotor>().Teleport(DungeonDirector.ExitPosition + Vector3.up * 0.2f, me.transform.rotation);
                yield return new WaitForSeconds(1f);
            }
            yield return Shot("F" + (floorNo + 1) + "_lift");
            for (float t = 0f; t < 30f && !DungeonDirector.InLobby && dd.Phase.Value == RunPhase.FloorCleared; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (!DungeonDirector.InLobby && dd.Phase.Value == RunPhase.FloorCleared) Problem("the lift never took me up (" + DungeonDirector.PlayersAtExit() + " at the exit)");
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        private static string Fmt(Vector3? v) => v.HasValue ? v.Value.x.ToString("F0") + "," + v.Value.z.ToString("F0") : "-";

        private int LivingEnemies() => Health.All.Count(h => h != null && h.IsAlive && h.Team == Team.Enemies && h.GetComponent<EnemyBrain>() != null);

        /// <summary>The living twin of a soulbound enemy lying severed within 40 m, or null.</summary>
        private Health StandingTwin()
        {
            var spawned = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
            foreach (var h in Health.All)
            {
                if (h == null || !h.IsAlive || h.Team != Team.Enemies) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (b == null || !b.SeveredNow || b.TwinId.Value == 0 || Vector3.Distance(h.transform.position, me.transform.position) > 40f) continue;
                if (spawned.TryGetValue(b.TwinId.Value, out var o) && o.TryGetComponent<Health>(out var other) && other.IsAlive) return other;
            }
            return null;
        }

        private Health NearestEnemy(HashSet<ulong> skip, out float dist, bool allowBoss)
        {
            Health best = null;
            dist = float.MaxValue;
            var path = new NavMeshPath();
            foreach (var h in Health.All)
            {
                if (h == null || !h.IsAlive || h.Team != Team.Enemies || skip.Contains(h.NetworkObjectId)) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (b == null || (!allowBoss && b is BossBrain)) continue;
                if (b is MournerBrain || b is GargoyleBrain g && !g.Woken.Value || b.SeveredNow) continue;
                float d = Vector3.Distance(h.transform.position, me.transform.position);
                if (follower && Leader != null && Vector3.Distance(h.transform.position, Leader.transform.position) > 16f) continue;
                // A player kills the healer first: a Necromancer within 16 m counts as much closer than it is.
                float rank = b is NecromancerBrain && d < 16f ? d * 0.3f : d;
                if (rank >= dist) continue;
                if (d > 6f && !Reachable(h.transform.position, path)) continue;
                best = h; dist = rank;
            }
            return best;
        }

        private LootDrop NearestLoot(float within)
        {
            LootDrop best = null;
            float bd = within;
            foreach (var l in FindObjectsByType<LootDrop>(FindObjectsSortMode.None))
            {
                if (l == null || !l.IsSpawned || ignoredLoot.Contains(l.NetworkObjectId)) continue;
                float d = Vector3.Distance(l.transform.position, me.transform.position);
                if (d < bd && Reachable(l.transform.position, new NavMeshPath())) { bd = d; best = l; }
            }
            return best;
        }

        /// <summary>
        /// What the bot uses as it finds it (2026-11-13, the user: the playtest bot "should make use of everything they find
        /// and record everything"): chests and every one-press thing on a floor. Not the puzzles (random turning solves
        /// nothing) and not carried things (they need their own walk).
        /// </summary>
        private static bool Usable(Interactable c) => c is Chest || c is HearthBrazier || c is ExpeditionRemains || c is ShaftCat || c is VigilCandle || c is FateWheel || c is BloodAltar
            || c is BargainAltar || c is Lever || c is SideShrine || c is LorePage || c is PrisonerCage || c is Chandelier || c is CellarHatch
            || c is SoulRift || c is Fountain || c is StatDraught;

        private Interactable NearestChest(HashSet<ulong> opened)
        {
            Interactable best = null;
            float bd = 22f;
            foreach (var c in Interactable.All)
            {
                if (c == null || !Usable(c) || opened.Contains(c.NetworkObjectId) || c.Prompt(prog) == null) continue;
                float d = Vector3.Distance(c.transform.position, me.transform.position);
                if (d < bd && Reachable(c.transform.position, new NavMeshPath())) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Why the way on fails: the path's state, where it ends, and what stands near us.</summary>
        private float gateWaitSince = -1f;

        /// <summary>A shut portcullis within 5 m.</summary>
        private static bool ShutGateNear(Vector3 at)
        {
            foreach (var pc in FindObjectsByType<Portcullis>(FindObjectsSortMode.None))
                if (pc != null && pc.Closed.Value && Vector3.Distance(pc.transform.position, at) < 5f) return true;
            return false;
        }

        private string WhyStuck(Vector3? goal)
        {
            if (!goal.HasValue) return "no goal";
            var stuckDeb = me.GetComponent<PlayerDebuffs>();
            string held = (stuckDeb != null && stuckDeb.Rooted ? (stuckDeb.Entangled ? "ENTANGLED, " : "ROOTED, ") : "") + (motor.Pinned ? "PINNED, " : "")
                + (NavMesh.SamplePosition(me.transform.position, out var close, 0.9f, NavMesh.AllAreas) ? "" : "no navmesh within 0.9 m (y " + me.transform.position.y.ToString("F1") + "), ");
            // A target we cannot see: name what is in the way.
            string sight = "";
            if (stuckTarget != null)
            {
                var eye = me.transform.position + Vector3.up * 1.6f;
                if (Physics.Linecast(eye, stuckTarget.Center, out var lh, ~0, QueryTriggerInteraction.Ignore) && lh.collider.GetComponentInParent<Health>() == null)
                    sight = "line to target blocked by " + lh.collider.name + " (" + (lh.collider.transform.parent != null ? lh.collider.transform.parent.name : "-") + ") at " + lh.distance.ToString("F1") + " m, ";
            }
            return held + sight + WhyStuckPath(goal);
        }

        private string WhyStuckPath(Vector3? goal)
        {
            var path = new NavMeshPath();
            bool fromOk = NavMesh.SamplePosition(me.transform.position, out var a, 2f, NavMesh.AllAreas);
            bool toOk = NavMesh.SamplePosition(goal.Value, out var b, 3f, NavMesh.AllAreas);
            string st = !fromOk ? "I am off the navmesh" : !toOk ? "goal off the navmesh" :
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) ? path.status + " (ends " + Fmt(path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : (Vector3?)null) + ")" : "no path";
            var near = Physics.OverlapSphere(me.transform.position + Vector3.up, 2.2f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => c.transform.root != me.transform.root).Select(c => c.name + (c.GetComponentInParent<NetworkObject>() is NetworkObject no ? "<" + no.name + ">" : "")).Distinct().Take(6);
            var obstacles = FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None).Where(o => o.enabled && Vector3.Distance(o.transform.position, me.transform.position) < 6f).Select(o => o.name + (o.carving ? "(carves)" : ""));
            return st + "; near: " + string.Join(", ", near) + "; obstacles: " + string.Join(", ", obstacles);
        }

        private bool Reachable(Vector3 to, NavMeshPath path)
        {
            if (!NavMesh.SamplePosition(me.transform.position, out var a, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var b, 2.5f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>Walk (at a run's pace, over the navmesh) toward a point for one short stretch.</summary>
        private IEnumerator StepToward(Vector3 to, float stopAt)
        {
            // Off the navmesh (a Disengage leap onto a ledge or a prop): a player would just step off, so step to the
            // nearest floor (2026-10-27: the Ranger stood on one spot for nine minutes).
            if (!NavMesh.SamplePosition(me.transform.position, out var a, 0.9f, NavMesh.AllAreas) && NavMesh.SamplePosition(me.transform.position, out var near, 3.5f, NavMesh.AllAreas))
            {
                L("stepped off a spot with no floor under it, back onto " + Fmt(near.position));
                motor.Teleport(near.position + Vector3.up * 0.05f, me.transform.rotation);
                yield return null;
                yield break;
            }
            if (!NavMesh.SamplePosition(me.transform.position, out a, 0.9f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas))
            {
                yield return null;
                yield break;
            }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.corners.Length < 2) { yield return null; yield break; }
            float speed = 6.2f * prog.SpeedMultiplier;
            float budget = 0.35f;
            int corner = 1;
            bool stepped = false;
            var pos = me.transform.position;
            while (budget > 0f && corner < path.corners.Length)
            {
                if (life.Downed.Value || motor.Pinned || (me.GetComponent<PlayerDebuffs>() is PlayerDebuffs deb && deb.Rooted)) break;
                var next = path.corners[corner];
                var flatTo = Flat(next) - Flat(pos);
                if (Flat(to - pos).magnitude <= stopAt) break;
                float step = speed * Time.deltaTime;
                if (flatTo.magnitude <= step) { pos = next; corner++; continue; }
                pos += flatTo.normalized * step;
                // Follow the path's own height: snapping to the nearest navmesh could hop onto a ledge no one could
                // walk onto (an island), which is not something a player can do.
                float segT = Mathf.InverseLerp(0f, Flat(next - path.corners[corner - 1]).magnitude, Flat(pos - path.corners[corner - 1]).magnitude);
                pos.y = Mathf.Lerp(path.corners[corner - 1].y, next.y, segT);
                var look = Flat(to) - Flat(pos);
                motor.Teleport(pos + Vector3.up * 0.05f, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : me.transform.rotation);
                motor.SetPitch(0f);
                budget -= Time.deltaTime;
                stepped = true;
                yield return null;
            }
            // Already there, rooted or pinned: still hand the frame back (callers loop on this; the frameless guard
            // caught it nine times in the Knight run).
            if (!stepped) yield return null;
        }

        private IEnumerator WalkTo(Vector3 to, float stopAt)
        {
            for (float t = 0f; t < 25f && Vector3.Distance(Flat(me.transform.position), Flat(to)) > stopAt; t += 0.35f)
                yield return StepToward(to, stopAt);
            if (Vector3.Distance(Flat(me.transform.position), Flat(to)) > stopAt + 1f)
            {
                // Camp has no navmesh (only floors are baked): there it simply steps across, and that is no problem.
                if (DungeonDirector.InLobby) L("stepped across camp to " + Fmt(to));
                else Problem("could not walk to " + Fmt(to) + " (stopped at " + Fmt(me.transform.position) + "): stepping there");
                motor.Teleport(to + Vector3.up * 0.1f, me.transform.rotation);
                yield return new WaitForSeconds(0.3f);
            }
        }

        private void Face(Vector3 at)
        {
            var eye = me.transform.position + Vector3.up * 1.6f;
            var flat = Flat(at) - Flat(eye);
            if (flat.sqrMagnitude < 0.01f) return;
            motor.Teleport(me.transform.position, Quaternion.LookRotation(flat));
            motor.SetPitch(Mathf.Atan2(eye.y - at.y, flat.magnitude) * Mathf.Rad2Deg);
        }

        private bool ThreatNear(float within)
        {
            foreach (var h in Health.All)
            {
                if (h == null || !h.IsAlive || h.Team != Team.Enemies) continue;
                var b = h.GetComponent<EnemyBrain>();
                if (b == null || (b.Status.Value & 4) == 0) continue;
                if (Vector3.Distance(h.transform.position, me.transform.position) < within + h.BodyRadius) { Face(h.Center); return true; }
            }
            return false;
        }

        private IEnumerator Block(float seconds)
        {
            UI.Binds.BotSetSecondary(true);
            float until = Time.time + seconds;
            while (Time.time < until) { Cursor.lockState = CursorLockMode.Locked; yield return null; }
            UI.Binds.BotSetSecondary(false);
            yield return null;
        }

        private IEnumerator Attack(float hold)
        {
            UI.Binds.BotSetPrimary(true);
            float until = Time.time + hold;
            while (Time.time < until) { Cursor.lockState = CursorLockMode.Locked; yield return null; }
            UI.Binds.BotSetPrimary(false);
            yield return null;
            yield return null;
        }

        private IEnumerator Press(UI.Bind b)
        {
            UI.Binds.BotPress(b);
            yield return null;
            yield return null;
        }
    }
}
