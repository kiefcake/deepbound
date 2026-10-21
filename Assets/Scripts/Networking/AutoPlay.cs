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
        private float lootOnSince = -1f;
        private readonly HashSet<ulong> ignoredLoot = new HashSet<ulong>();
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
            StartCoroutine(Main());
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
                    string who = Health.NameOf(from);
                    takenBy.TryGetValue(who, out var v);
                    takenBy[who] = v + amount;
                }
                else if (from == me.NetworkObjectId) { dealt += amount; if (!h.IsAlive && h.Team == Team.Enemies) kills++; }
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
                for (int i = 0; i < Mathf.Min(10, Mathf.RoundToInt(startFloor * 1.5f)); i++) prog.ServerFreeTemper();
                prog.RankQ.Value = (byte)Mathf.Min(8, 1 + startFloor / 2 + 1);
                prog.RankE.Value = (byte)Mathf.Min(8, 1 + startFloor / 2);
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
                    prog.ChoosePathRpc((byte)Random.Range(0, 2));
                    yield return new WaitForSeconds(0.3f);
                    L("chose a weapon path (" + why + ")");
                    why = prog.CannotTemperReason();
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

        private string Status(string where)
        {
            var kit = me.ActiveKit;
            string s = where + " | hp " + hp.Current.Value.ToString("F0") + "/" + hp.Max.Value.ToString("F0") + ", gold " + prog.Gold.Value + ", embers " + prog.Embers.Value + ", sigils " + prog.Sigils.Value +
                       ", weapon +" + prog.WeaponLevel.Value + ", Q" + prog.Rank(Classes.AbilitySlot.Q) + " E" + prog.Rank(Classes.AbilitySlot.E) + ", relics " + prog.RelicCount +
                       (flasks != null ? ", pouch H" + flasks.Count(Flasks.Kind.Health) + " M" + flasks.Count(Flasks.Kind.Mana) : "") + ", armour " + Gear.Name(prog.Equipped(GearSlot.Armor)) + ", charm " + Gear.Name(prog.Equipped(GearSlot.Charm));
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

                // Pick what to do: the nearest living enemy we can walk to (the guardian last), else loot, else a chest.
                var boss = BossBrain.Active;
                target = NearestEnemy(skip, out float dist, allowBoss: false);
                if (target == null && boss != null && !boss.IsDead) { target = boss.GetComponent<Health>(); dist = Vector3.Distance(target.transform.position, me.transform.position); }
                if (boss != null && bossSeenAt < 0f && dd.BossSealed) { bossSeenAt = Time.time; L("guardian fight begins: " + boss.DisplayName); yield return Shot("F" + (floorNo + 1) + "_boss_start"); }

                var loot = NearestLoot(18f);
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
                    // Hits not landing for a while (a shield, a guard): go round behind it, as a player would.
                    bool flank = boss != null && target == boss.GetComponent<Health>() && bossHpMark >= 0f && Time.time - bossHpAt > 6f;
                    if (flank)
                    {
                        var behind = target.transform.position - target.transform.forward * (edge + reach * 0.6f);
                        if (Vector3.Distance(Flat(me.transform.position), Flat(behind)) > 1.2f) { yield return StepToward(behind, 0.6f); continue; }
                    }
                    if (flatDist > want || !clear) yield return StepToward(target.transform.position, want * 0.9f);
                    else if (melee && flatDist < edge + 0.6f)
                    {
                        // Too close (inside its reach and ours is wasted): step back out to the edge.
                        var back = Flat(me.transform.position) - Flat(target.transform.position);
                        if (back.sqrMagnitude < 0.01f) back = -me.transform.forward;
                        yield return StepToward(me.transform.position + back.normalized * 1.5f, 0.2f);
                    }
                    else
                    {
                        Face(target.Center);
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
                                Problem("guardian at " + (bh.Fraction * 100f).ToString("F0") + "% has not lost health in 30 s of attacking: in swing arc " + inArc + ", flat dist " + new Vector2(to.x, to.z).magnitude.ToString("F1") + " (body r " + bh.BodyRadius.ToString("F1") + "), vertical " + to.y.ToString("F1") + " (centre h " + bh.CenterHeight.ToString("F1") + "), clear line " + clear + " (" + blocker + "), dormant " + boss.Dormant + ", immune " + boss.Immune + ", a test hit of 10 dealt " + d.ToString("F1") + ", my " + (me.ActiveKit != null ? me.ActiveKit.ResourceName + " " + me.ActiveKit.Resource.ToString("F0") + "/" + me.ActiveKit.ResourceMax.ToString("F0") : "?") + ", can act " + life.CanAct);
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

                // Stuck: no progress for 12 s.
                if ((me.transform.position - lastPos).sqrMagnitude > 1f) { lastPos = me.transform.position; stuckSince = Time.time; }
                else if (Time.time - stuckSince > 12f)
                {
                    Problem("stuck for 12 s going for " + goalWhat + " at " + Fmt(goal) + " from " + Fmt(me.transform.position));
                    if (target != null) skip.Add(target.NetworkObjectId);
                    stuckSince = Time.time;
                }
                if (Time.time >= nextShot) { nextShot = Time.time + 35f; yield return Shot("F" + (floorNo + 1) + "_" + (int)(Time.time - floorStart) + "s"); }
                if (Time.time >= nextStatus) { nextStatus = Time.time + 75f; Status("progress (" + LivingEnemies() + " enemies left)"); }
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
                if (d >= dist) continue;
                if (d > 6f && !Reachable(h.transform.position, path)) continue;
                best = h; dist = d;
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

        private bool Reachable(Vector3 to, NavMeshPath path)
        {
            if (!NavMesh.SamplePosition(me.transform.position, out var a, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out var b, 2.5f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        /// <summary>Walk (at a run's pace, over the navmesh) toward a point for one short stretch.</summary>
        private IEnumerator StepToward(Vector3 to, float stopAt)
        {
            if (!NavMesh.SamplePosition(me.transform.position, out var a, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out var b, 3f, NavMesh.AllAreas))
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
                if (NavMesh.SamplePosition(pos, out var on, 1.5f, NavMesh.AllAreas)) pos.y = on.position.y;
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
