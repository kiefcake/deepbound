using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

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
        private Gamepad pad;

        private readonly Dictionary<string, float> takenBy = new Dictionary<string, float>();
        private float taken, dealt;
        private int downs, kills, shotNo, saves, savesFloor;
        private float bossHpMark = -1f, bossHpAt, nextBlock;
        private bool pendingHeal;
        private int bossHits; private float bossDealt;
        private float lootOnSince = -1f;
        private readonly HashSet<ulong> ignoredLoot = new HashSet<ulong>();
        private Health stuckTarget;
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
            launcher.StartHost();
            for (float t = 0f; t < 15f && NetworkPlayer.Local == null; t += 0.2f) yield return new WaitForSeconds(0.2f);
            yield return new WaitForSeconds(3f);
            me = NetworkPlayer.Local;
            if (me == null) { L("no local player"); Application.Quit(1); yield break; }
            motor = me.GetComponent<FirstPersonMotor>();
            life = me.GetComponent<PlayerLife>();
            prog = me.GetComponent<PlayerProgress>();
            hp = me.GetComponent<Health>();
            flasks = me.GetComponent<Flasks>();
            pad = InputSystem.AddDevice<Gamepad>();
            // The bot's window is often not the focused one (someone is watching or working beside it):
            // input from its virtual pad must count anyway.
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Health.ServerAnyDamaged += (h, amount, from) =>
            {
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
            hp.IncomingModifiers.Add((dmg, from) =>
            {
                if (hp.Current.Value - dmg > 1f) return dmg;
                saves++; savesFloor++;
                L("!! would have gone down (save #" + saves + "): a blow of " + dmg.ToString("F0") + " at " + hp.Current.Value.ToString("F0") + " hp; worst so far " + string.Join(", ", takenBy.OrderByDescending(kv => kv.Value).Take(3).Select(kv => kv.Key)));
                pendingHeal = true;
                return Mathf.Max(0f, hp.Current.Value - 1f);
            });
            prog.ClassId.Value = cls;
            yield return new WaitForSeconds(1f);
            L("== RUN START as " + cls + " ==");
            yield return Shot("camp_start");

            if (startFloor > 0)
            {
                // A run's worth of kit by this floor (roughly what runs 4 and 5 had).
                for (int i = 0; i < Mathf.Min(10, Mathf.RoundToInt(startFloor * 1.5f)); i++)
                {
                    var why = prog.CannotTemperReason();
                    if (why != null && why.ToLowerInvariant().Contains("path")) { prog.ChoosePathRpc((byte)Random.Range(1, 3)); yield return new WaitForSeconds(0.2f); }
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
                yield return Floor();
            }
            for (int f = startFloor > 0 ? startFloor + 1 : 0; f < DungeonDirector.FloorsPerRun; f++)
            {
                yield return Camp();
                yield return Floor();
                var ph = DungeonDirector.Instance.Phase.Value;
                if (ph == RunPhase.RunComplete || ph == RunPhase.Wiped) break;
            }

            L("== RUN OVER: phase " + DungeonDirector.Instance.Phase.Value + " ==");
            yield return new WaitForSeconds(6f);
            yield return Shot("run_summary");
            yield return new WaitForSeconds(20f);
            yield return Shot("run_summary_late");
            L("== PROBLEMS SEEN (" + problems.Count + ") ==");
            foreach (var p in problems) L("  - " + p);
            InputSystem.RemoveDevice(pad);
            log.Close();
            Application.Quit(0);
        }

        // ------------------------------------------------------------------ camp

        private IEnumerator Camp()
        {
            for (float t = 0f; t < 60f && !DungeonDirector.InLobby; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (!DungeonDirector.InLobby) { Problem("never got back to camp"); yield break; }
            yield return new WaitForSeconds(2f);
            Status("at camp");
            if (floorNo >= 0) yield return Shot("camp_after_F" + (floorNo + 1));

            // The pack: wear anything better than what is on (higher quality).
            for (int i = prog.BagCount - 1; i >= 0; i--)
            {
                var g = prog.BagItem(i);
                var worn = prog.Equipped(Gear.SlotOf((GearId)g.Id));
                if (g.Quality > worn.Quality || worn.IsEmpty)
                {
                    L("wearing " + Gear.Name(g) + " from the pack (was " + Gear.Name(worn) + ")");
                    prog.BagEquipRpc(i);
                    yield return new WaitForSeconds(0.3f);
                }
            }

            // Hobb: temper while it can.
            yield return WalkTo(Lobby.AnvilSpot + Vector3.back * 1.6f, 1.2f);
            for (int i = 0; i < 6; i++)
            {
                string why = prog.CannotTemperReason();
                if (why != null && why.ToLowerInvariant().Contains("path"))
                {
                    prog.ChoosePathRpc((byte)Random.Range(1, 3));
                    yield return new WaitForSeconds(0.3f);
                    L("chose a weapon path (" + why + ")");
                    why = prog.CannotTemperReason();
                }
                if (prog.OpenEdgeSlot >= 0)
                {
                    var pick = prog.EdgeOffer(prog.OpenEdgeSlot)[Random.Range(0, Progression.Edges.OfferSize)];
                    prog.ChooseEdgeRpc((byte)pick);
                    yield return new WaitForSeconds(0.3f);
                    L("forged the " + Progression.Edges.Name(pick) + " edge");
                    why = prog.CannotTemperReason();
                }
                if (prog.WeaponLevel.Value >= Anvil.MaxLevel)
                {
                    if (prog.CannotHoneReason() != null) break;
                    prog.HoneRpc();
                    yield return new WaitForSeconds(0.4f);
                    L("honed (" + prog.Hones.Value + ")");
                    continue;
                }
                int cost = Anvil.Cost(prog.WeaponLevel.Value), gold = Anvil.GoldCost(prog.WeaponLevel.Value);
                if (why != null || prog.Embers.Value < cost || prog.Gold.Value < gold)
                {
                    L("temper +" + prog.WeaponLevel.Value + " -> +" + (prog.WeaponLevel.Value + 1) + ": " + (why ?? "") + " (needs " + cost + " embers" + (gold > 0 ? ", " + gold + " gold" : "") + "; has " + prog.Embers.Value + "e " + prog.Gold.Value + "g)");
                    break;
                }
                prog.TemperRpc();
                yield return new WaitForSeconds(0.4f);
                L("tempered to +" + prog.WeaponLevel.Value);
            }

            // Ysolde: rank abilities with Sigils.
            if (prog.Sigils.Value > 0)
            {
                yield return WalkTo(Lobby.RunewrightSpot + Vector3.forward * 1.6f, 1.2f);
                for (int i = 0; i < 8 && prog.Sigils.Value > 0; i++)
                {
                    byte slot = (byte)(prog.Rank(Classes.AbilitySlot.Q) <= prog.Rank(Classes.AbilitySlot.E) ? 0 : 1);
                    int before = prog.Sigils.Value;
                    prog.EmpowerRpc(slot);
                    yield return new WaitForSeconds(0.3f);
                    if (prog.Sigils.Value == before) { L("could not empower (" + prog.Sigils.Value + " sigils, Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E) + ", at stones " + prog.AtRunewright + ")"); break; }
                    L("empowered " + (slot == 0 ? "Q" : "E") + " -> Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E));
                }
            }

            // Silas: health potions while there is gold to spare.
            if (flasks != null && flasks.Count(Flasks.Kind.Health) < 4 && prog.Gold.Value > 60)
            {
                yield return WalkTo(Lobby.MerchantSpot + Vector3.back * 1.6f, 1.2f);
                var stock = Shop.For(me.OwnerClientId, Shop.Vendor.Merchant);
                int idx = System.Array.FindIndex(stock, it => it.Name != null && it.Name.Contains("Health"));
                if (idx < 0) L("Silas sells no health potion? stock: " + string.Join(", ", stock.Select(s => s.Name + " " + s.Price)));
                for (int i = 0; i < 3 && idx >= 0 && flasks.Count(Flasks.Kind.Health) < 4; i++)
                {
                    int g0 = prog.Gold.Value;
                    if (g0 < stock[idx].Price + 20) break;
                    prog.BuyRpc(Shop.Vendor.Merchant, idx);
                    yield return new WaitForSeconds(0.3f);
                    L(prog.Gold.Value < g0 ? "bought " + stock[idx].Name + " for " + (g0 - prog.Gold.Value) : "could not buy " + stock[idx].Name + ": " + prog.CannotBuyReason(Shop.Vendor.Merchant, idx));
                }
            }

            // Spare gold: Brann's drills (cheapest first), then a relic at the shrine.
            yield return SpendAt(Shop.Vendor.Yard, Lobby.YardSpot, it => it.Kind == Shop.ItemKind.Drill, 4, 120);
            yield return SpendAt(Shop.Vendor.Shrine, Lobby.ShrineSpot, it => it.Kind == Shop.ItemKind.Relic, 1, 100);
            // A blessing for the next floor (two on offer; the cheaper rule picks the first, they cost the same).
            yield return SpendAt(Shop.Vendor.Shrine, Lobby.ShrineSpot, it => it.Kind == Shop.ItemKind.Blessing, 1, 60);
            L("gold left at camp: " + prog.Gold.Value);

            // Board: a seat on the carriage; it leaves when everyone sits.
            var ride = CarriageRide.Instance;
            if (ride == null) { Problem("no carriage at camp"); yield break; }
            yield return WalkTo(ride.SeatGround(0), 1.5f);
            life.SitRpc(0);
            L("sat on the carriage");
            yield return new WaitForSeconds(4f);
            yield return Shot("carriage_F" + (floorNo + 2));
            for (float t = 0f; t < 45f && DungeonDirector.InLobby; t += 0.5f) yield return new WaitForSeconds(0.5f);
            if (DungeonDirector.InLobby) { Problem("the carriage never left (seat " + life.Seat.Value + ")"); }
        }

        private IEnumerator SpendAt(Shop.Vendor vendor, Vector3 at, System.Func<Shop.Item, bool> want, int maxBuys, int keep)
        {
            var stock = Shop.For(me.OwnerClientId, vendor);
            if (!stock.Any(want)) yield break;
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

        private string Status(string where)
        {
            var kit = me.ActiveKit;
            string s = where + " | hp " + hp.Current.Value.ToString("F0") + "/" + hp.Max.Value.ToString("F0") + ", gold " + prog.Gold.Value + ", embers " + prog.Embers.Value + ", sigils " + prog.Sigils.Value +
                       ", weapon +" + prog.WeaponLevel.Value + (prog.Hones.Value > 0 ? " honed " + prog.Hones.Value : "") + ", Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E) + ", relics " + prog.RelicCount + RelicTiers() +
                       (flasks != null ? ", pouch H" + flasks.Count(Flasks.Kind.Health) + " M" + flasks.Count(Flasks.Kind.Mana) : "") + ", armour " + Gear.Name(prog.Equipped(GearSlot.Armor)) + ", charm " + Gear.Name(prog.Equipped(GearSlot.Charm));
            s += ClockCheck();
            L(s);
            return s;
        }

        // ------------------------------------------------------------------ a floor

        private IEnumerator Floor()
        {
            savesFloor = 0;
            bossHpMark = -1f;
            for (float t = 0f; t < 30f && (DungeonDirector.InLobby || DungeonDirector.Layout == null); t += 0.5f) yield return new WaitForSeconds(0.5f);
            var dd = DungeonDirector.Instance;
            var layout = DungeonDirector.Layout;
            if (layout == null || DungeonDirector.InLobby) { Problem("no floor to play"); yield break; }
            floorNo = layout.Depth;
            yield return new WaitForSeconds(2.5f);
            float floorStart = Time.time, takenStart = taken, dealtStart = dealt;
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
                if ((me.transform.position - lastPos).sqrMagnitude > 1f) { lastPos = me.transform.position; stuckSince = Time.time; }
                else if (Time.time - stuckSince > 12f)
                {
                    Problem("stuck for 12 s going for " + goalWhat + " at " + Fmt(goal) + " from " + Fmt(me.transform.position) + " | " + WhyStuck(goal));
                    if (target != null) skip.Add(target.NetworkObjectId);
                    // Loot it cannot get to (behind a secret wall, on a ledge): give it up rather than walk into the wall all floor.
                    if (goalWhat == "loot" && lastLootId != ulong.MaxValue) ignoredLoot.Add(lastLootId);
                    stuckSince = Time.time;
                }
                if (Time.time >= nextShot) { nextShot = Time.time + 35f; yield return Shot("F" + (floorNo + 1) + "_" + (int)(Time.time - floorStart) + "s"); }
                if (Time.time >= nextStatus) { nextStatus = Time.time + 75f; Status("progress (" + LivingEnemies() + " enemies left)"); }
                // Block (Knight) or dodge a swing that is coming: a red ! within reach.
                if (Time.time >= nextBlock && ThreatNear(3.6f) && (BossBrain.Active == null || Random.value < 0.5f))
                {
                    nextBlock = Time.time + 1.6f;
                    if (cls == Classes.ClassId.Knight) yield return Block(0.6f);
                    else yield return Press(GamepadButton.East);
                }
                // A potion below 40%.
                if (flasks != null && hp.Current.Value < hp.Max.Value * 0.4f && flasks.Count(Flasks.Kind.Health) > 0) yield return Press(GamepadButton.DpadLeft);
                if (me.ActiveKit != null && me.ActiveKit.Resource < me.ActiveKit.ResourceMax * 0.15f && flasks != null && flasks.Count(Flasks.Kind.Mana) > 0) yield return Press(GamepadButton.DpadRight);
                // The rare slot, as a player would: a defensive draught when it is going badly, the rest at the guardian.
                if (flasks != null && flasks.RareHeld != Progression.RareItem.None && !flasks.InStasis)
                {
                    var rare = flasks.RareHeld;
                    bool low = hp.Current.Value < hp.Max.Value * 0.35f;
                    bool atBoss = BossBrain.Active != null && dd.BossSealed;
                    if ((low && (rare == Progression.RareItem.Ironbark || rare == Progression.RareItem.GraveStillness || rare == Progression.RareItem.SanctuaryTome))
                        || (atBoss && (rare == Progression.RareItem.Bloodfury || rare == Progression.RareItem.PhoenixTome || rare == Progression.RareItem.StormTome)))
                    {
                        if (flasks.ServerUseRare()) L("used " + Progression.RareItems.Name(rare) + (low ? " (low on health)" : " (at the guardian)"));
                    }
                }

                // Pick what to do: the nearest living enemy we can walk to (the guardian last), else loot, else a chest.
                var boss = BossBrain.Active;
                target = NearestEnemy(skip, out float dist, allowBoss: false);
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
                        if (p != null && p.Health != null && p.Health.IsAlive && p.Feeding.Value && Vector3.Distance(p.transform.position, me.transform.position) < pd) { pd = Vector3.Distance(p.transform.position, me.transform.position); pylon = p; }
                    if (pylon != null) { target = pylon.Health; dist = pd; }
                }
                if (boss != null && bossSeenAt < 0f && dd.BossSealed) { bossSeenAt = Time.time; L("guardian fight begins: " + boss.DisplayName); yield return Shot("F" + (floorNo + 1) + "_boss_start"); }

                var loot = NearestLoot(18f);
                lastLootId = loot != null ? loot.NetworkObjectId : ulong.MaxValue;
                var chest = NearestChest(openedChests);
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
                    if (boss != null && target == boss.GetComponent<Health>() && boss.Dormant && !layout.BossRoom.Contains(me.transform.position, -1.5f))
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
                        // Abilities now and then, when something is close.
                        if (Time.time >= nextAbility)
                        {
                            nextAbility = Time.time + Random.Range(2.5f, 4.5f);
                            var btn = Random.value < 0.5f ? GamepadButton.LeftShoulder : GamepadButton.RightShoulder;
                            yield return Press(btn);
                            if (me.GetComponent<Ultimates>() is Ultimates u && u.Ready) yield return Press(GamepadButton.DpadUp);
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
                                float d = bh.ServerDamage(10f, me.transform.position, me.NetworkObjectId);
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
                else if (chest != null)
                {
                    goal = chest.transform.position; goalWhat = "chest";
                    if (Vector3.Distance(chest.transform.position, me.transform.position) > 2f) yield return StepToward(chest.transform.position, 1.6f);
                    else { chest.UseRpc(me.NetworkObject); openedChests.Add(chest.NetworkObjectId); L("opened a chest"); yield return new WaitForSeconds(0.6f); }
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
            if (dd.Phase.Value == RunPhase.Exploring && !DungeonDirector.InLobby)
            {
                Problem("floor not finished in " + (floorLimit / 60f).ToString("F0") + " min (" + LivingEnemies() + " enemies left, boss " + (BossBrain.Active != null ? BossBrain.Active.DisplayName + " " + (BossBrain.Active.GetComponent<Health>().Fraction * 100f).ToString("F0") + "%" : "none") + "): forcing it");
                var b = BossBrain.Active;
                if (b != null) b.GetComponent<Health>().ServerDamage(1e6f, b.transform.position, me.NetworkObjectId);
                for (float t = 0f; t < 8f && dd.Phase.Value == RunPhase.Exploring; t += 0.5f) yield return new WaitForSeconds(0.5f);
            }
            L("floor done in " + (took / 60f).ToString("F1") + " min" + (bossSeenAt > 0f ? " (guardian " + ((Time.time - bossSeenAt) / 60f).ToString("F1") + " min)" : "") +
              ": kills " + (kills - killsStart) + ", dealt " + (dealt - dealtStart).ToString("F0") + ", taken " + (taken - takenStart).ToString("F0") + ", downs " + (downs - downsStart) + ", would-have-gone-down " + savesFloor);
            L("   damage taken so far by source: " + string.Join(", ", takenBy.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + " " + kv.Value.ToString("F0"))));
            Status("floor end");
            yield return Shot("F" + (floorNo + 1) + "_cleared");
            if (dd.Phase.Value != RunPhase.FloorCleared) yield break;

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
                if (b is MournerBrain || b is GargoyleBrain g && !g.Woken.Value) continue;
                float d = Vector3.Distance(h.transform.position, me.transform.position);
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

        private Interactable NearestChest(HashSet<ulong> opened)
        {
            Interactable best = null;
            float bd = 22f;
            foreach (var c in Interactable.All)
            {
                if (c == null || !(c is Chest) || opened.Contains(c.NetworkObjectId) || c.Prompt(prog) == null) continue;
                float d = Vector3.Distance(c.transform.position, me.transform.position);
                if (d < bd && Reachable(c.transform.position, new NavMeshPath())) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Why the way on fails: the path's state, where it ends, and what stands near us.</summary>
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
                yield return null;
            }
        }

        private IEnumerator WalkTo(Vector3 to, float stopAt)
        {
            for (float t = 0f; t < 25f && Vector3.Distance(Flat(me.transform.position), Flat(to)) > stopAt; t += 0.35f)
                yield return StepToward(to, stopAt);
            if (Vector3.Distance(Flat(me.transform.position), Flat(to)) > stopAt + 1f)
            {
                Problem("could not walk to " + Fmt(to) + " (stopped at " + Fmt(me.transform.position) + "): stepping there");
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
            InputSystem.QueueStateEvent(pad, new GamepadState { leftTrigger = 1f });
            float until = Time.time + seconds;
            while (Time.time < until) { Cursor.lockState = CursorLockMode.Locked; yield return null; }
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }

        private IEnumerator Attack(float hold)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
            float until = Time.time + hold;
            while (Time.time < until) { Cursor.lockState = CursorLockMode.Locked; yield return null; }
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
        }

        private IEnumerator Press(GamepadButton b)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(b));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
        }
    }
}
