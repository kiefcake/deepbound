using System.Collections;
using System.Linq;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Progression;
using Deepbound.Player;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// Headless end-to-end check, run with <c>-batchmode -nographics -host -smoketest 20</c>
    /// (and optionally a second instance with <c>-client</c>).
    ///
    /// <para>
    /// There is no Play-mode test suite; this is the next best thing. It boots a real
    /// player build, lets the dungeon generate, and logs one <c>[Smoke]</c> line per check
    /// to the player log, then quits. On the host it also kills the boss server-side to
    /// prove the floor-cleared transition fires. A missing line or a FAIL means a broken
    /// build.
    /// </para>
    /// </summary>
    public class SmokeTest : MonoBehaviour
    {
        // Tests never touch the player's real wardrobe or Renown.
        private void Awake()
        {
            Progression.Cosmetics.TestMode = true;
            DungeonDirector.TestMode = true;
        }

        public void Run(float seconds)
        {
            StartCoroutine(Sequence(seconds));
            // Watchdog: if the sequence throws, the coroutine dies silently and the
            // process would idle forever. Fail loudly instead.
            Invoke(nameof(Timeout), seconds * 2.5f);
        }

        private void Timeout()
        {
            Debug.Log("[Smoke] FAIL watchdog: sequence did not finish (see exceptions above)");
            Application.Quit(1);
        }

        private IEnumerator Sequence(float seconds)
        {
            yield return new WaitForSeconds(4f);

            var net = NetworkManager.Singleton;
            if (net.IsServer)
            {
                // Sessions start at camp. Check it, then board the carriage.
                int dummies = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).Length;
                Log("lobby", "in lobby " + DungeonDirector.InLobby + ", " + dummies + " dummies", DungeonDirector.InLobby && dummies == Lobby.DummySpots.Length);

                // Switch to Mage at camp, then chill a dummy and shatter it.
                var me = NetworkPlayer.Local;
                var progressMe = me.GetComponent<PlayerProgress>();
                progressMe.ClassId.Value = Classes.ClassId.Mage;
                yield return new WaitForSeconds(0.5f);
                Log("class switch", progressMe.Class.Name + ", max hp " + me.GetComponent<Health>().Max.Value + ", rig " + me.Rig.name,
                    progressMe.Class.Name == "Mage" && me.Rig.name == "MageVisual" && me.ActiveKit is MageKit);

                var dummy = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None)[0].GetComponent<Health>();
                var mage = me.GetComponent<MageKit>();
                // Fire from where the player really stands: the server clamps bolt origins
                // to the caster, so a bolt "from" anywhere else would be moved.
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 5f, Quaternion.LookRotation(Vector3.left));
                yield return new WaitForSeconds(0.2f);
                var from = me.transform.position + Vector3.up * 1.6f;
                mage.ServerBolt(from, dummy.Center - from, true);
                yield return new WaitForSeconds(0.6f);
                Log("frostbolt chills", "chilled " + dummy.Chilled.Value, dummy.Chilled.Value);
                float before = dummy.Current.Value;
                mage.ServerBolt(from, dummy.Center - from, false);
                yield return new WaitForSeconds(0.5f);
                // Relics: Thunder Sigil procs on the 6th hit; Vampiric Fang heals the attacker.
                progressMe.ServerAddRelic(Relic.ThunderSigil);
                progressMe.ServerAddRelic(Relic.VampiricFang);
                var meHp = me.GetComponent<Health>();
                meHp.ServerSetFraction(0.5f);
                float hpBefore = meHp.Current.Value;
                dummy.ServerSetFraction(1f);
                float dummyBefore = dummy.Current.Value;
                for (int i = 0; i < 6; i++) dummy.ServerDamage(10f, me.transform.position, me.NetworkObjectId);
                float dealt = dummyBefore - dummy.Current.Value;
                Log("relics", "6 hits of 10 dealt " + dealt.ToString("F0") + " (lightning adds more), hp " + hpBefore.ToString("F0") + " -> " + meHp.Current.Value.ToString("F0"),
                    dealt > 60f && meHp.Current.Value > hpBefore && progressMe.RelicCount == 2);
                meHp.ServerSetFraction(1f);

                Log("firebolt shatters", "chill consumed " + !dummy.Chilled.Value + ", dealt " + (before - dummy.Current.Value).ToString("F0"),
                    !dummy.Chilled.Value && before - dummy.Current.Value > MageKit.FireboltDamage);

                // Cryomancer path: one frostbolt pierces both dummies, which stand in a line.
                var dummiesAll = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None).Select(d => d.GetComponent<Health>()).OrderBy(d => d.transform.position.z).ToArray();
                if (dummiesAll.Length >= 2)
                {
                    foreach (var d in dummiesAll) d.ServerConsumeChill();
                    progressMe.WeaponPath.Value = 2;
                    var near = dummiesAll[0].transform.position;
                    me.GetComponent<FirstPersonMotor>().Teleport(near + Vector3.back * 5f, Quaternion.LookRotation(Vector3.forward));
                    yield return new WaitForSeconds(0.2f);
                    var cfrom = me.transform.position + Vector3.up * 1.6f;
                    mage.ServerBolt(cfrom, dummiesAll[1].Center - cfrom, true);
                    yield return new WaitForSeconds(0.8f);
                    Log("cryomancer pierce", "chilled " + dummiesAll[0].Chilled.Value + " and " + dummiesAll[1].Chilled.Value, dummiesAll[0].Chilled.Value && dummiesAll[1].Chilled.Value);
                    progressMe.WeaponPath.Value = 0;
                }

                // Cleric: Aegis absorbs a hit completely; Consecrate burns a dummy standing in it.
                progressMe.ClassId.Value = Classes.ClassId.Cleric;
                yield return new WaitForSeconds(0.5f);
                var cleric = me.GetComponent<ClericKit>();
                var hpCleric = me.GetComponent<Health>();
                hpCleric.ServerSetFraction(1f);
                cleric.ServerAegis();
                float shield = hpCleric.Shield.Value, beforeHit = hpCleric.Current.Value;
                hpCleric.ServerDamage(30f, me.transform.position + me.transform.forward);
                Log("cleric aegis", "shield " + shield.ToString("F0") + ", hp " + beforeHit.ToString("F0") + " -> " + hpCleric.Current.Value.ToString("F0") + ", shield left " + hpCleric.Shield.Value.ToString("F0"),
                    shield > 0f && Mathf.Approximately(hpCleric.Current.Value, beforeHit) && hpCleric.Shield.Value < shield);

                dummy.ServerSetFraction(1f);
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 1.5f, me.transform.rotation);
                yield return new WaitForSeconds(0.2f);
                float dummyFull = dummy.Current.Value;
                cleric.ServerConsecrate();
                yield return new WaitForSeconds(1.6f);
                Log("cleric consecrate", "dummy " + dummyFull.ToString("F0") + " -> " + dummy.Current.Value.ToString("F0"), dummy.Current.Value < dummyFull);
                // Rogue: from the smoke, a stab in the back is an Ambush opener (x1.5, x2).
                progressMe.ClassId.Value = Classes.ClassId.Rogue;
                yield return new WaitForSeconds(0.5f);
                var rogue = me.GetComponent<RogueKit>();
                dummy.ServerSetFraction(1f);
                var behind = dummy.transform.position - dummy.transform.forward * 1.4f;
                me.GetComponent<FirstPersonMotor>().Teleport(behind, Quaternion.LookRotation(dummy.transform.forward));
                yield return new WaitForSeconds(0.2f);
                rogue.ServerSmoke();
                bool wasHidden = me.GetComponent<PlayerLife>().Hidden.Value;
                float dBefore = dummy.Current.Value;
                rogue.ServerStab(me.transform.position + Vector3.up * 1.5f, dummy.transform.forward);
                float opener = dBefore - dummy.Current.Value;
                Log("rogue ambush", "hidden " + wasHidden + " -> " + me.GetComponent<PlayerLife>().Hidden.Value + ", opener stab dealt " + opener.ToString("F0") + " (plain ~13)",
                    wasHidden && !me.GetComponent<PlayerLife>().Hidden.Value && opener > 30f);

                // Ranger: three full draws mark the dummy; a trap set on it springs.
                progressMe.ClassId.Value = Classes.ClassId.Ranger;
                yield return new WaitForSeconds(0.5f);
                var ranger = me.GetComponent<RangerKit>();
                dummy.ServerSetFraction(1f);
                me.GetComponent<FirstPersonMotor>().Teleport(dummy.transform.position + Vector3.right * 6f, Quaternion.LookRotation(Vector3.left));
                yield return new WaitForSeconds(0.2f);
                for (int i = 0; i < 3; i++)
                {
                    var o = me.transform.position + Vector3.up * 1.6f;
                    ranger.ServerShoot(o, dummy.Center - o, 1f);
                    yield return new WaitForSeconds(0.4f);
                }
                Log("ranger mark", "marked " + dummy.Marked.Value + ", dummy " + dummy.Current.Value.ToString("F0") + " / " + dummy.Max.Value.ToString("F0"), dummy.Marked.Value);
                ranger.ServerPlaceTrap(dummy.transform.position);
                yield return new WaitForSeconds(0.3f);
                var trap = FindObjectsByType<BearTrap>(FindObjectsSortMode.None).FirstOrDefault();
                Log("ranger trap", "trap " + (trap != null) + ", sprung " + (trap != null && trap.Sprung.Value), trap != null && trap.Sprung.Value);

                progressMe.ClassId.Value = Classes.ClassId.Mage;
                yield return new WaitForSeconds(0.3f);
                DungeonDirector.Instance.ServerStartRun();
            }

            // Wait for the dungeon itself, not a guess at how long the lobby checks take.
            float waitUntil = Time.time + 30f;
            while ((DungeonDirector.Layout == null || NetworkPlayer.Local == null) && Time.time < waitUntil) yield return null;
            yield return new WaitForSeconds(2f);

            var layout = DungeonDirector.Layout;
            Log("role", net.IsHost ? "host" : net.IsClient ? "client" : "none", net.IsHost || net.IsConnectedClient);
            Log("dungeon", layout != null ? layout.Rooms.Count + " rooms, depth " + layout.Depth : "missing", layout != null);

            int enemies = Health.All.Count(h => h != null && h.Team == Team.Enemies);
            Log("enemies", enemies.ToString(), enemies > 0);
            Log("players", PlayerLife.All.Count.ToString(), PlayerLife.All.Count > 0);

            var local = NetworkPlayer.Local;
            if (local != null && layout != null)
            {
                var room = layout.RoomAt(local.transform.position);
                Log("local player", local.transform.position.ToString("F1") + " in " + (room != null ? room.Kind.ToString() : "no room"),
                    room != null && local.transform.position.y > -1f);
            }
            else
            {
                Log("local player", "missing", false);
            }

            if (local != null)
            {
                local.GetComponent<PlayerPings>().PingSpot(local.transform.position + local.transform.forward * 5f);
                yield return new WaitForSeconds(0.5f);
                Log("ping round trip", "own ping visible: " + PlayerPings.Active.ContainsKey(local.NetworkObjectId), PlayerPings.Active.ContainsKey(local.NetworkObjectId));
            }

            var boss = BossBrain.Active;
            Log("boss", boss != null ? boss.Health.Current.Value + " hp" : "missing", boss != null);

            if (net.IsServer && net.IsHost)
            {
                int agentsOnMesh = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None)
                    .Count(e => e.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh);
                Log("navmesh agents", agentsOnMesh + " / " + enemies, agentsOnMesh == enemies);
            }

            if (net.IsServer && local != null)
            {
                // Dungeon objects: a chest per side room; open one; blow up a barrel.
                int sideRooms = layout.Rooms.Count(r => r.Kind == RoomKind.Side);
                var chests = FindObjectsByType<Chest>(FindObjectsSortMode.None);
                int urns = FindObjectsByType<Breakable>(FindObjectsSortMode.None).Count(b => b.Type.Value == Breakable.Kind.Urn);
                Log("dungeon objects", chests.Length + " chests for " + sideRooms + " side rooms, " + urns + " urns, " +
                    FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).Length + " traps", chests.Length == sideRooms + (layout.Rooms.Any(r => r.Feature == RoomFeature.Vault) ? 2 : 0) + layout.Rooms.Sum(r => r.Holes.Count(hh => !hh.Breach)) && urns > 0);
                if (chests.Length > 0)
                {
                    int dropsBefore = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length;
                    var cmotor = local.GetComponent<FirstPersonMotor>();
                    cmotor.Teleport(chests[0].transform.position + chests[0].transform.forward * 2f, local.transform.rotation);
                    yield return new WaitForSeconds(0.3f);
                    chests[0].UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.3f);
                    int dropsAfter = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length;
                    Log("open chest", "opened " + chests[0].Opened.Value + ", drops " + dropsBefore + " -> " + dropsAfter, chests[0].Opened.Value && dropsAfter > dropsBefore);
                    cmotor.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                }
                var barrel = FindObjectsByType<Breakable>(FindObjectsSortMode.None).FirstOrDefault(b => b.Type.Value == Breakable.Kind.PowderBarrel);
                if (barrel != null)
                {
                    barrel.GetComponent<Health>().ServerDamage(100f, barrel.transform.position);
                    yield return new WaitForSeconds(1.2f);
                    Log("powder barrel", "exploded and gone: " + (barrel == null), barrel == null);
                }

                // Hollow walls: smash a secret one open; force a breach and count who comes out.
                var walls = FindObjectsByType<CrackedWall>(FindObjectsSortMode.None);
                var secret = walls.FirstOrDefault(w => !w.Breach.Value);
                int breachWalls = walls.Count(w => w.Breach.Value);
                bool hadSecret = secret != null;
                if (secret != null)
                {
                    secret.GetComponent<Health>().ServerDamage(1000f, secret.transform.position - secret.transform.forward, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.3f);
                }
                bool secretGone = hadSecret && (secret == null || !secret.IsSpawned);
                Log("secret wall", walls.Length + " hollow walls (" + breachWalls + " breaches), secret broken: " + secretGone,
                    walls.Length >= 2 && breachWalls >= 1 && secretGone);
                if (DungeonDirector.Instance.BreachCount > 0)
                {
                    int enemiesBefore = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Count(e => !e.IsDead);
                    var bw = DungeonDirector.Instance.BreachWall(0);
                    var bwFrom = bw.transform.position - bw.transform.forward * 5f + Vector3.up * 0.1f;
                    local.GetComponent<FirstPersonMotor>().Teleport(bwFrom, Quaternion.LookRotation(bw.transform.forward));
                    DungeonDirector.Instance.ServerDebugTriggerBreach(0);
                    yield return new WaitForSeconds(2.4f);
                    int enemiesAfter = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Count(e => !e.IsDead);
                    Log("wall breach", "enemies " + enemiesBefore + " -> " + enemiesAfter, enemiesAfter > enemiesBefore);
                    // Clear the fight and stand everyone back up, so later checks start clean.
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && Vector3.Distance(e.transform.position, bwFrom) < 30f) e.GetComponent<Health>().ServerDamage(1e6f, bwFrom);
                    foreach (var pl in PlayerLife.All)
                    {
                        if (pl.Downed.Value) pl.ServerRevive(1f);
                        pl.Health.ServerSetFraction(1f);
                    }
                    yield return new WaitForSeconds(0.2f);
                }

                // Feature rooms.
                var fm = local.GetComponent<FirstPersonMotor>();
                string features = string.Join(", ", layout.Rooms.Where(r => r.Feature != RoomFeature.None).Select(r => r.Feature.ToString()));
                // Restless Bones: a plain skeleton killed on the Ossuary leaves a pile.
                var plainSk = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => e.GetType() == typeof(EnemyBrain) && !e.IsDead && !e.Elite.Value &&
                    !layout.BossRoom.Contains(e.transform.position, 1f));
                if (plainSk != null)
                {
                    int pilesBefore = DungeonDirector.Instance.DebugPileCount;
                    plainSk.GetComponent<Health>().ServerDamage(1e6f, plainSk.transform.position + Vector3.forward, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.2f);
                    Log("restless bones", "piles " + pilesBefore + " -> " + DungeonDirector.Instance.DebugPileCount, DungeonDirector.Instance.DebugPileCount > pilesBefore);
                }
                // Ritual: braziers burn until smashed, then the ward lifts.
                var ritual = layout.Rooms.FirstOrDefault(r => r.Feature == RoomFeature.Ritual);
                if (ritual != null)
                {
                    bool wardOn = DungeonDirector.InRitualWard(ritual.Center);
                    foreach (var br in FindObjectsByType<Breakable>(FindObjectsSortMode.None).Where(x => x.Type.Value == Breakable.Kind.RitualBrazier).ToArray())
                        br.GetComponent<Health>().ServerDamage(1e6f, br.transform.position + Vector3.up, local.NetworkObjectId);
                    yield return new WaitForSeconds(0.4f);
                    Log("ritual", "ward " + wardOn + " -> " + DungeonDirector.InRitualWard(ritual.Center), wardOn && !DungeonDirector.InRitualWard(ritual.Center));
                }
                Log("feature rooms", features, layout.Rooms.Any(r => r.Feature == RoomFeature.GreatHall) && layout.Rooms.Any(r => r.Feature == RoomFeature.Chasm));

                var sarc = FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsSpawned && x.HasOccupant && !x.Opened.Value && !x.Scheduled);
                if (sarc != null)
                {
                    fm.Teleport(sarc.transform.position + sarc.transform.forward * 3f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.5f);
                    Log("crypt", "sarcophagus opened on approach: " + sarc.Opened.Value + " (spawned " + sarc.IsSpawned + ", occupant " + sarc.HasOccupant + ", targetable " + local.GetComponent<PlayerLife>().Targetable +
                        ", downed " + local.GetComponent<PlayerLife>().Downed.Value + (sarc.Opened.Value ? "" : ", " + sarc.DebugState()) + ", dist " + Vector3.Distance(local.transform.position, sarc.transform.position).ToString("F1") + ")", sarc.Opened.Value);
                    // The rest of the crypt follows, two at a time.
                    yield return new WaitForSeconds(5.2f);
                    var all = FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None);
                    int open = all.Count(x => x.Opened.Value);
                    Log("crypt wakes", open + " of " + all.Length + " open 5 s later", open >= Mathf.Min(all.Length, 3));
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && Vector3.Distance(e.transform.position, sarc.transform.position) < 20f) e.GetComponent<Health>().ServerDamage(1e6f, sarc.transform.position);
                    local.GetComponent<Health>().ServerSetFraction(1f);
                }

                var levers = FindObjectsByType<Lever>(FindObjectsSortMode.None);
                var gatesBefore = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Count(g => g.Closed.Value);
                foreach (var lv in levers)
                {
                    fm.Teleport(lv.transform.position + lv.transform.forward * 1.5f, Quaternion.LookRotation(-lv.transform.forward));
                    yield return new WaitForSeconds(0.2f);
                    lv.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                }
                yield return new WaitForSeconds(0.3f);
                var gatesAfter = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Count(g => g.Closed.Value);
                Log("vault", levers.Length + " levers pulled, closed gates " + gatesBefore + " -> " + gatesAfter + ", open " + DungeonDirector.Instance.VaultOpen, levers.Length == 3 && DungeonDirector.Instance.VaultOpen);

                var fountain = FindObjectsByType<Fountain>(FindObjectsSortMode.None).FirstOrDefault();
                if (fountain != null)
                {
                    var hpF = local.GetComponent<Health>();
                    hpF.ServerSetFraction(0.3f);
                    fm.Teleport(fountain.transform.position + Vector3.forward * 2f, Quaternion.LookRotation(Vector3.back));
                    yield return new WaitForSeconds(0.2f);
                    fountain.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    Log("fountain", "hp " + hpF.Current.Value.ToString("F0") + " / " + hpF.Max.Value.ToString("F0") + ", spent " + fountain.Spent.Value, fountain.Spent.Value && hpF.Fraction > 0.99f);
                }

                var chasmRoom = layout.Rooms.FirstOrDefault(r => r.Feature == RoomFeature.Chasm);
                if (chasmRoom != null)
                {
                    // Drop the player into the pit (beside the bridges) and expect a rescue.
                    var pitPoint = new Vector3(chasmRoom.Pit.xMin + 1.5f, -6f, chasmRoom.Pit.yMin + 1.5f);
                    fm.Teleport(pitPoint, local.transform.rotation);
                    yield return new WaitForSeconds(1.2f);
                    Log("chasm fall", "y after fall " + local.transform.position.y.ToString("F1") + ", hp " + local.GetComponent<Health>().Current.Value.ToString("F0"),
                        local.transform.position.y > -0.5f);
                }
                fm.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                local.GetComponent<Health>().ServerSetFraction(1f);
                yield return new WaitForSeconds(0.3f);

                // Ambush: step into the trap room, clear both waves, find the chest.
                var dir = DungeonDirector.Instance;
                if (dir.AmbushRoom != null)
                {
                    var amb = dir.AmbushRoom;
                    var am = local.GetComponent<FirstPersonMotor>();
                    int chestsBefore = FindObjectsByType<Chest>(FindObjectsSortMode.None).Length;
                    am.Teleport(amb.Center + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.5f);
                    int waveSeen = dir.AmbushWave;
                    bool gatesShut = FindObjectsByType<Portcullis>(FindObjectsSortMode.None).Any(g => g.Closed.Value);
                    for (int pass = 0; pass < 6 && dir.AmbushWave < 3; pass++)
                    {
                        foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                            if (!(e is BossBrain) && !e.IsDead && amb.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, e.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.6f);
                    }
                    int chestsAfter = FindObjectsByType<Chest>(FindObjectsSortMode.None).Length;
                    Log("ambush", "triggered wave " + waveSeen + ", gates shut " + gatesShut + ", final wave " + dir.AmbushWave + ", chests " + chestsBefore + " -> " + chestsAfter,
                        waveSeen == 1 && gatesShut && dir.AmbushWave == 3 && chestsAfter == chestsBefore + 1);
                    am.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
                    yield return new WaitForSeconds(0.3f);
                }

                // Kill a handful of skeletons and confirm they drop loot that a player can take.
                var victims = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Where(e => !(e is BossBrain)).Take(6).ToList();
                foreach (var v in victims) v.GetComponent<Health>().ServerDamage(1e6f, v.transform.position, local.NetworkObjectId);
                yield return new WaitForSeconds(0.3f);

                var drops = FindObjectsByType<LootDrop>(FindObjectsSortMode.None);
                Log("loot drops", drops.Length + " from " + victims.Count + " kills", drops.Length > 0);

                var progress = local.GetComponent<PlayerProgress>();
                int goldBefore = progress.Gold.Value;
                var motor = local.GetComponent<FirstPersonMotor>();
                foreach (var drop in drops)
                {
                    // Standing on one drop can collect a neighbour too, despawning it.
                    if (drop == null) continue;
                    motor.Teleport(drop.transform.position, local.transform.rotation);
                    yield return new WaitForSeconds(0.9f);
                }
                Log("pickup", "gold " + goldBefore + " -> " + progress.Gold.Value + ", embers " + progress.Embers.Value,
                    progress.Gold.Value > goldBefore || progress.Embers.Value > 0);

                // Class combos: a stunned enemy is Exposed; a heavy hit Shatters chill.
                var foe = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead);
                if (foe != null)
                {
                    var fh = foe.GetComponent<Health>();
                    float plain = Classes.Combos.ServerMultiplier(fh, progress);
                    foe.ServerStun(2f);
                    float exposed = Classes.Combos.ServerMultiplier(fh, progress);
                    fh.ServerChill(3f);
                    float shatter = Classes.Combos.TryShatter(fh);
                    Log("combos", "plain x" + plain.ToString("F2") + ", stunned x" + exposed.ToString("F2") + ", shatter x" + shatter + ", chill left " + fh.Chilled.Value + ", credited " + progress.CombosTriggered.Value,
                        exposed > plain * 1.2f && shatter > 1.4f && !fh.Chilled.Value && progress.CombosTriggered.Value > 0);
                }

                // Elite affixes: a Warded elite shrugs off damage until a stun breaks the ward.
                var eliteTest = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(e => !(e is BossBrain) && !e.IsDead && !e.Elite.Value && !e.IsStunned && !e.GetComponent<Health>().Chilled.Value && e != foe);
                if (eliteTest != null)
                {
                    var eh = eliteTest.GetComponent<Health>();
                    eliteTest.ServerMakeElite(EliteAffix.Warded);
                    if (eliteTest.AffixKind == EliteAffix.Warded)
                    {
                        float hpA = eh.Current.Value;
                        eh.ServerDamage(50f, local.transform.position);
                        float warded = hpA - eh.Current.Value;
                        eliteTest.ServerStun(1f);
                        yield return null;
                        float hpB = eh.Current.Value;
                        eh.ServerDamage(50f, local.transform.position);
                        float broken = hpB - eh.Current.Value;
                        Log("elite ward", "warded hit " + warded.ToString("F0") + ", after stun " + broken.ToString("F0") + ", affix now " + eliteTest.AffixKind, broken > warded * 2f && eliteTest.AffixKind == EliteAffix.None);
                    }
                    else Log("elite ward", "rolled " + eliteTest.AffixKind + " (no ward to test)", true);
                }

                motor.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
            }

            yield return new WaitForSeconds(1f);

            if (net.IsServer && boss != null)
            {
                boss.Health.ServerDamage(1e6f, boss.transform.position);
                yield return new WaitForSeconds(1f);
                var phase = DungeonDirector.Instance.Phase.Value;
                Log("boss kill -> phase", phase.ToString(), phase == RunPhase.FloorCleared);
                Log("boss arena", "sealed when hit: " + DungeonDirector.Instance.BossSealed, DungeonDirector.Instance.BossSealed);
            }

            // After the boss the party goes home to camp, run intact; spend there, then ride down.
            float campBy = Time.time + 20f;
            while (net.IsServer && DungeonDirector.Instance.Phase.Value != RunPhase.Lobby && Time.time < campBy) yield return null;
            yield return new WaitForSeconds(1f);
            if (net.IsServer && local != null)
            {
                var dc = DungeonDirector.Instance;
                var pc = local.GetComponent<PlayerProgress>();
                Log("camp between floors", "in camp " + DungeonDirector.InLobby + ", next floor " + (dc.NextDepth.Value + 1) + ", kills kept " + pc.Kills.Value + ", gold " + pc.Gold.Value,
                    DungeonDirector.InLobby && dc.RunInProgress && dc.NextDepth.Value == 1 && pc.Kills.Value > 0);
                Log("camp stations", FindObjectsByType<Anvil>(FindObjectsSortMode.None).Length + " anvil, " + FindObjectsByType<DiceTable>(FindObjectsSortMode.None).Length + " dice, " +
                    FindObjectsByType<MysteryCoffer>(FindObjectsSortMode.None).Length + " coffer, " + FindObjectsByType<Gong>(FindObjectsSortMode.None).Length + " gong",
                    FindObjectsByType<Anvil>(FindObjectsSortMode.None).Length == 1 && FindObjectsByType<DiceTable>(FindObjectsSortMode.None).Length == 1 && FindObjectsByType<Gong>(FindObjectsSortMode.None).Length == 1);

                {
                    var progress = pc;
                    var motor = local.GetComponent<FirstPersonMotor>();
                    // The anvil: hand the player embers, temper twice, check ATT rose.
                    var anvil = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
                    if (anvil != null)
                    {
                        float attBefore = progress.Value(Core.StatType.ATT);
                        progress.ServerAddEmbers(Anvil.Cost(0) + Anvil.Cost(1));
                        var ap = anvil.transform.position + anvil.transform.forward * 1.6f + Vector3.up * 0.1f;
                        motor.Teleport(ap, Quaternion.LookRotation(-anvil.transform.forward));
                        yield return new WaitForSeconds(0.2f);
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.2f);
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.3f);
                        Log("anvil", "weapon +" + progress.WeaponLevel.Value + ", ATT " + attBefore + " -> " + progress.Value(Core.StatType.ATT) + ", embers left " + progress.Embers.Value,
                            progress.WeaponLevel.Value == 2 && progress.Value(Core.StatType.ATT) > attBefore);

                        // Gear: a drop meant for this player, equip it, then scrap a second.
                    var ddef = local.GetComponent<Health>().Defense;
                    var gearDrop = GearDrop.ServerSpawn(new GearItem(GearId.OssuaryPlate, 1, 0), local.OwnerClientId, local.transform.position + local.transform.forward * 1.2f);
                    yield return new WaitForSeconds(0.2f);
                    gearDrop.UseRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    Log("gear equip", "armor " + Gear.Name(progress.Armor.Value) + ", DEF " + ddef + " -> " + local.GetComponent<Health>().Defense,
                        progress.Armor.Value.Kind == GearId.OssuaryPlate && local.GetComponent<Health>().Defense > ddef);
                    // Refine it at the anvil: Fine -> Masterwork, DEF rises.
                    var anvilR = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
                    if (anvilR != null)
                    {
                        var keep = local.transform.position;
                        motor.Teleport(anvilR.transform.position + anvilR.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        float defR = local.GetComponent<Health>().Defense;
                        progress.ServerAddEmbers(Gear.RefineCost(progress.Armor.Value));
                        progress.ReforgeRpc(0, 0);
                        yield return new WaitForSeconds(0.2f);
                        Log("gear refine", Gear.Name(progress.Armor.Value) + ", DEF " + defR + " -> " + local.GetComponent<Health>().Defense,
                            progress.Armor.Value.Quality == 2 && local.GetComponent<Health>().Defense > defR);
                        motor.Teleport(keep, local.transform.rotation);
                        yield return new WaitForSeconds(0.1f);
                    }
                    int embersBefore = progress.Embers.Value;
                    var junk = GearDrop.ServerSpawn(new GearItem(GearId.Bloodstone, 0, 0), local.OwnerClientId, local.transform.position + local.transform.forward * 1.2f);
                    yield return new WaitForSeconds(0.2f);
                    junk.ScrapRpc(local.NetworkObject);
                    yield return new WaitForSeconds(0.2f);
                    Log("gear scrap", "embers " + embersBefore + " -> " + progress.Embers.Value, progress.Embers.Value > embersBefore);

                    // The merchant: this player's own gear offer lands at their feet.
                    var mpos = Lobby.MerchantSpot;
                    motor.Teleport(mpos + Vector3.right * 2.4f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);
                    progress.ServerAddGold(500);
                    int goldNow = progress.Gold.Value;
                    var stock = Shop.For(local.OwnerClientId);
                    int gearIndex = System.Array.FindIndex(stock, it => it.Kind == Shop.ItemKind.Gear);
                    int dropsBeforeBuy = FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                    progress.BuyRpc(gearIndex);
                    yield return new WaitForSeconds(0.3f);
                    Log("shop gear", "bought " + stock[gearIndex].Name + " for " + stock[gearIndex].Price + ", gold " + goldNow + " -> " + progress.Gold.Value + ", sold-out: " + progress.CannotBuyReason(gearIndex),
                        progress.Gold.Value == goldNow - stock[gearIndex].Price && FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length == dropsBeforeBuy + 1 && progress.CannotBuyReason(gearIndex) == "Sold");
                    var anvilAgain = FindObjectsByType<Anvil>(FindObjectsSortMode.None).First();
                    motor.Teleport(anvilAgain.transform.position + anvilAgain.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.2f);

                    // Runes: one in the bag, set it into the weapon's first socket.
                    int bagBefore = progress.RuneBagCount;
                    progress.ServerAddRune(Rune.Frost);
                    yield return new WaitForSeconds(0.1f);
                    progress.SocketRuneRpc((byte)Rune.Frost);
                    yield return new WaitForSeconds(0.2f);
                    Log("rune socket", "socket 1: " + Runes.Name(progress.Socketed(0)) + ", bag " + progress.RuneBagCount,
                        progress.Socketed(0) == Rune.Frost && progress.RuneBagCount == bagBefore);

                    // Sigils: rank Q twice at the anvil; the cooldown scale drops at rank 3.
                    float cdBefore = progress.CooldownFor(Classes.AbilitySlot.Q);
                    int sigilsBefore = progress.Sigils.Value;
                    progress.ServerAddSigil();
                    progress.ServerAddSigil();
                    progress.EmpowerRpc(0);
                    yield return new WaitForSeconds(0.2f);
                    progress.EmpowerRpc(0);
                    yield return new WaitForSeconds(0.2f);
                    Log("ability rank", "Q rank " + progress.RankQ.Value + ", sigils left " + progress.Sigils.Value + ", Q cooldown x" + cdBefore + " -> x" + progress.CooldownFor(Classes.AbilitySlot.Q),
                        progress.RankQ.Value == 3 && progress.Sigils.Value == sigilsBefore && progress.CooldownFor(Classes.AbilitySlot.Q) < cdBefore);

                    // Boss legendary: the Hollow Crown lifts both abilities a rank.
                    int qBefore = progress.Rank(Classes.AbilitySlot.Q), eBefore = progress.Rank(Classes.AbilitySlot.E);
                    var oldCharm = progress.ServerEquip(new GearItem(GearId.HollowCrown, 3, 2));
                    Log("legendary crown", "Q " + qBefore + " -> " + progress.Rank(Classes.AbilitySlot.Q) + ", E " + eBefore + " -> " + progress.Rank(Classes.AbilitySlot.E),
                        progress.Rank(Classes.AbilitySlot.E) == Mathf.Min(3, eBefore + 1));
                    if (!oldCharm.IsEmpty) progress.ServerEquip(oldCharm);

                    // +3 needs a path; choose Cleave and check the swing now reaches wider.
                        progress.ServerAddEmbers(Anvil.Cost(2));
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.2f);
                        bool gated = progress.WeaponLevel.Value == 2;
                        progress.ChoosePathRpc(1);
                        yield return new WaitForSeconds(0.2f);
                        progress.TemperRpc();
                        yield return new WaitForSeconds(0.2f);
                        Log("weapon path", "gated before choosing: " + gated + ", path " + progress.WeaponPath.Value + ", weapon +" + progress.WeaponLevel.Value,
                            gated && progress.WeaponPath.Value == 1 && progress.WeaponLevel.Value == 3);
                    }
                    else Log("anvil", "none at camp", false);

                    // The gambling tent: a roll of the bones moves gold one way or the other (or not).
                    var dice = FindObjectsByType<DiceTable>(FindObjectsSortMode.None).FirstOrDefault();
                    if (dice != null)
                    {
                        progress.ServerAddGold(200);
                        motor.Teleport(dice.transform.position + dice.transform.forward * 1.4f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        int g0 = progress.Gold.Value;
                        dice.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        int stake = DiceTable.Stake(g0);
                        int delta = progress.Gold.Value - g0;
                        Log("dice", "gold " + g0 + " -> " + progress.Gold.Value + " (stake " + stake + ")", delta == stake || delta == 0 || delta == -stake);
                    }
                    // The coffer: pay and something appears.
                    var coffer = FindObjectsByType<MysteryCoffer>(FindObjectsSortMode.None).FirstOrDefault();
                    if (coffer != null)
                    {
                        progress.ServerAddGold(MysteryCoffer.Price);
                        motor.Teleport(coffer.transform.position + coffer.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        int g1 = progress.Gold.Value;
                        int things = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length + FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                        coffer.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.3f);
                        int after = FindObjectsByType<LootDrop>(FindObjectsSortMode.None).Length + FindObjectsByType<GearDrop>(FindObjectsSortMode.None).Length;
                        Log("mystery coffer", "gold " + g1 + " -> " + progress.Gold.Value + ", prizes " + things + " -> " + after, progress.Gold.Value == g1 - MysteryCoffer.Price && after > things);
                    }
                    // The shrine: pay, and the blessing is on you.
                    var shrineT = FindObjectsByType<Shrine>(FindObjectsSortMode.None).FirstOrDefault();
                    if (shrineT != null)
                    {
                        progress.ServerAddGold(Blessings.Price(DungeonDirector.GearDepth));
                        motor.Teleport(shrineT.transform.position + shrineT.transform.forward * 1.6f + Vector3.up * 0.1f, local.transform.rotation);
                        yield return new WaitForSeconds(0.2f);
                        shrineT.UseRpc(local.NetworkObject);
                        yield return new WaitForSeconds(0.2f);
                        Log("shrine", "blessing " + Blessings.Name((Blessing)progress.ActiveBlessing.Value), progress.ActiveBlessing.Value != 0);
                    }
                    // The range: a hit on a target counts for the shooter.
                    var targetT = FindObjectsByType<RangeTarget>(FindObjectsSortMode.None).FirstOrDefault();
                    if (targetT != null)
                    {
                        RangeTarget.Tally.TryGetValue(local.name, out int before);
                        targetT.GetComponent<Health>().ServerDamage(10f, local.transform.position, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.1f);
                        RangeTarget.Tally.TryGetValue(local.name, out int after);
                        Log("archery range", "hits " + before + " -> " + after, after == before + 1);
                    }
                    // The Test of Might: a hit sets the record and the gong takes no damage.
                    var gongT = Gong.Instance;
                    if (gongT != null)
                    {
                        float hpG = gongT.GetComponent<Health>().Current.Value;
                        gongT.GetComponent<Health>().ServerDamage(77f, gongT.transform.position - gongT.transform.forward, local.NetworkObjectId);
                        yield return new WaitForSeconds(0.2f);
                        Log("test of might", "record " + gongT.Record.Value + " by " + gongT.RecordHolder.Value + ", hp unchanged " + (gongT.GetComponent<Health>().Current.Value == hpG),
                            gongT.Record.Value >= 77f && gongT.GetComponent<Health>().Current.Value == hpG);
                    }
                }

                // The tailor: wearing a dye rebuilds the body with it.
                var npT = local.GetComponent<NetworkPlayer>();
                var rigBefore = npT.Rig;
                npT.CloakDye.Value = 3;
                yield return new WaitForSeconds(0.3f);
                var cloakR = npT.Rig != null ? npT.Rig.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name == "Cloak") : null;
                Log("tailor dye", "dye " + npT.CloakDye.Value + ", rebuilt " + (npT.Rig != rigBefore) + ", cloak " + (cloakR != null) + ", renown for a clear " +
                    Progression.Cosmetics.RenownFor(4, 120, 30, true, 1), npT.Rig != rigBefore && cloakR != null && Progression.Cosmetics.RenownFor(4, 120, 30, true, 1) > 0);
                npT.CloakDye.Value = 0;
                npT.Aura.Value = 2;
                yield return new WaitForSeconds(0.3f);
                Log("tailor aura", "aura " + npT.Aura.Value + " (" + Progression.Cosmetics.Auras[npT.Aura.Value].Name + "), cardinal legendary " + Gear.Name(new GearItem(GearId.CardinalVestments, 3, 3)),
                    npT.Aura.Value == 2 && Progression.Cosmetics.AuraColor(2).HasValue);
                npT.Aura.Value = 0;

                // The ultimate: a full bar casts and empties; the Knight's banner protects.
                var ultT = local.GetComponent<Ultimates>();
                var clsT = local.GetComponent<PlayerProgress>().ClassId.Value;
                ultT.DebugCast(local.transform.position + local.transform.forward * 3f);
                yield return new WaitForSeconds(0.3f);
                Log("ultimate", Ultimates.Name(clsT) + ", charge after " + ultT.Charge.Value.ToString("F0") + ", in banner " + Ultimates.InBanner(local.transform.position),
                    ultT.Charge.Value < 1f && (clsT != Classes.ClassId.Knight || Ultimates.InBanner(local.transform.position)));

                // Board: floor two, same run.
                dc.ServerBoard();
                float boardBy = Time.time + 10f;
                while (dc.Phase.Value != RunPhase.Exploring && Time.time < boardBy) yield return null;
                yield return new WaitForSeconds(1.5f);
                Log("next floor", "depth " + dc.Floor.Value.y + ", weapon kept +" + pc.WeaponLevel.Value, dc.Floor.Value.y == 1 && pc.WeaponLevel.Value >= 2);
                // The Drowned Vaults' tide: low ground slows while it is up.
                dc.ServerDebugTide();
                yield return new WaitForSeconds(0.3f);
                var floorSpot = DungeonDirector.Layout.StartRoom.Center + Vector3.up * 0.05f;
                Log("tide", "level " + DungeonDirector.TideLevel.ToString("F2") + ", slow " + DungeonDirector.WaterSlow(floorSpot).ToString("F2"), DungeonDirector.WaterSlow(floorSpot) < 1f);
            }

            // Wipe the party and check the run summary has something to show.
            if (net.IsServer && local != null)
            {
                // Repeat: a Phoenix Feather (or its grace window) can cheat the first blow.
                for (int tries = 0; tries < 4 && DungeonDirector.Instance.Phase.Value != RunPhase.Wiped; tries++)
                {
                    foreach (var p in PlayerLife.All) p.Health.ServerDamage(1e6f, p.transform.position);
                    yield return new WaitForSeconds(tries == 0 ? 0.6f : 2.5f);
                }
                var d = DungeonDirector.Instance;
                var prog = local.GetComponent<PlayerProgress>();
                Log("run summary", "phase " + d.Phase.Value + ", run " + (d.RunEndedAt.Value - d.RunStartedAt.Value).ToString("F0") + "s, kills " + prog.Kills.Value +
                    ", damage " + prog.DamageDealt.Value + ", downs " + prog.Downs.Value,
                    d.Phase.Value == RunPhase.Wiped && prog.Kills.Value >= 6 && prog.Downs.Value >= 1 && d.RunEndedAt.Value > d.RunStartedAt.Value);
            }

            yield return new WaitForSeconds(2f);
            Log("final phase", DungeonDirector.Instance != null ? DungeonDirector.Instance.Phase.Value.ToString() : "no director", true);
            Debug.Log("[Smoke] done");
            Application.Quit(0);
        }

        /// <summary>
        /// Visual check: captures the title screen, then hosts and captures views from the
        /// start room, a combat room and the boss room into <paramref name="dir"/>. Run
        /// windowed (not -nographics), e.g. <c>-shots C:\temp\shots -screen-fullscreen 0</c>.
        /// </summary>
        public void Screenshots(string dir, NetworkLauncher launcher) => StartCoroutine(ShotSequence(dir, launcher));

        private IEnumerator ShotSequence(string dir, NetworkLauncher launcher)
        {
            System.IO.Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(2.5f);
            yield return Shot(dir, "0_menu");

            launcher.StartHost();
            yield return new WaitForSeconds(3f);

            var local = NetworkPlayer.Local;
            if (local == null)
            {
                Debug.Log("[Smoke] FAIL screenshots: no local player");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(1.5f);
            yield return Shot(dir, "0a_character_select");
            // Every class, front and back, for judging the character art.
            var select = local.GetComponent<UI.CharacterSelect>();
            for (int ci = 0; ci < Classes.ClassDefinition.ById.Length; ci++)
            {
                foreach (var (yaw, tag) in new[] { (200f, "front"), (20f, "back") })
                {
                    select.DebugView(ci, yaw);
                    yield return null;
                    yield return null;
                    yield return Shot(dir, "0p_class_" + ((Classes.ClassId)ci) + "_" + tag);
                }
            }
            select.DebugView((int)local.GetComponent<PlayerProgress>().ClassId.Value, 200f);
            local.GetComponent<UI.CharacterSelect>().Close();
            yield return new WaitForSeconds(0.2f);

            var lobbyMotor = local.GetComponent<FirstPersonMotor>();
            lobbyMotor.enabled = false;
            var lobbyFrom = Lobby.BoardingCenter + new Vector3(-6f, 0.1f, -6f);
            lobbyMotor.Teleport(lobbyFrom, Quaternion.LookRotation(Lobby.BoardingCenter + new Vector3(4f, 0f, 0f) - lobbyFrom));
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "0b_lobby_carriage");

            var dummyFrom = Lobby.DummySpots[0] + new Vector3(4f, 0.1f, 1f);
            lobbyMotor.Teleport(dummyFrom, Quaternion.LookRotation(Lobby.DummySpots[0] - dummyFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0c_lobby_dummies");
            Art.SmokeCloud.Spawn(Lobby.DummySpots[0] + new Vector3(2f, 0f, 2.5f), Player.RogueKit.SmokeCloudRadius, 4f);
            yield return new WaitForSeconds(0.9f);
            yield return Shot(dir, "0c2_smoke_cloud");

            // A tour of the castle: the road from the bonfire, the stalls, the tent, the gong, the gate.
            var tour = new (string name, Vector3 at, Vector3 look, float pitch)[]
            {
                ("0j_castle_road", Lobby.Origin + new Vector3(0f, 0.1f, -17f), Lobby.Origin + new Vector3(0f, 2f, 10f), -8f),
                ("0k_castle_stalls", Lobby.Origin + new Vector3(0f, 0.1f, -10f), Lobby.Origin + new Vector3(-5f, 1.5f, -5f), 0f),
                ("0l_castle_tent", Lobby.Origin + new Vector3(-2f, 0.1f, 6f), Lobby.TentCenter + Vector3.up * 1.5f, 0f),
                ("0m_castle_gong", Lobby.Origin + new Vector3(3f, 0.1f, 6f), Lobby.GongSpot + Vector3.up * 2f, 0f),
                ("0n_castle_gate", Lobby.Origin + new Vector3(-4f, 0.1f, 8f), Lobby.BoardingCenter + new Vector3(2f, 3f, 6f), -10f),
                ("0o_castle_keep", Lobby.Origin + new Vector3(3f, 0.1f, 2f), Lobby.Origin + new Vector3(0f, 9f, -24f), -18f),
                ("0q_castle_shrine", Lobby.Origin + new Vector3(10.5f, 0.1f, -4f), Lobby.ShrineSpot + Vector3.up * 1.5f, 0f),
                ("0r_castle_range", Lobby.Origin + new Vector3(8.5f, 0.1f, 11f), Lobby.RangeCenter + Vector3.up * 1.2f, 0f),
            };
            foreach (var (tname, tat, tlook, tpitch) in tour)
            {
                var flat = tlook - tat; flat.y = 0f;
                lobbyMotor.Teleport(tat, Quaternion.LookRotation(flat));
                lobbyMotor.SetPitch(tpitch);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, tname);
            }
            lobbyMotor.SetPitch(0f);

            // The Mage: switch class, frost the dummy, catch the bolt and the chill.
            var prog = local.GetComponent<PlayerProgress>();
            prog.ClassId.Value = Classes.ClassId.Mage;
            yield return new WaitForSeconds(0.5f);
            var mage = local.GetComponent<MageKit>();
            var dummyHp = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None)[0].GetComponent<Health>();
            lobbyMotor.Teleport(dummyHp.transform.position + new Vector3(4.5f, 0.1f, -0.5f), Quaternion.LookRotation(new Vector3(-4.5f, 0f, 0.5f)));
            yield return new WaitForSeconds(0.3f);
            var origin = local.transform.position + Vector3.up * 1.6f;
            local.ViewModelCast(true);
            mage.ServerBolt(origin + local.transform.forward * 0.6f, dummyHp.Center - origin, true);
            yield return new WaitForSeconds(0.08f);
            yield return Shot(dir, "0d_mage_frostbolt");
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0e_mage_chilled_dummy");

            // Meteor: falling through the ceiling, the impact, the crater left behind.
            var meteorSpot = dummyHp.transform.position + new Vector3(-1.5f, 0f, 2f);
            var mFrom2 = meteorSpot + new Vector3(8f, 0.1f, -4f);
            lobbyMotor.Teleport(mFrom2, Quaternion.LookRotation(meteorSpot - mFrom2));
            lobbyMotor.SetPitch(-12f);
            yield return new WaitForSeconds(0.2f);
            mage.ServerMeteor(meteorSpot);
            yield return new WaitForSeconds(0.85f);
            yield return Shot(dir, "0g_meteor_falling");
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0h_meteor_impact");
            lobbyMotor.SetPitch(8f);
            yield return new WaitForSeconds(1.4f);
            yield return Shot(dir, "0i_meteor_crater");
            lobbyMotor.SetPitch(0f);
            // The Ranger at full draw.
            prog.ClassId.Value = Classes.ClassId.Ranger;
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0f_ranger_bow");

            prog.ClassId.Value = Classes.ClassId.Knight;
            yield return new WaitForSeconds(0.3f);

            var m = Lobby.MerchantSpot;
            var mFrom = m + Vector3.right * 3.4f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(mFrom, Quaternion.LookRotation(m - mFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "1b_merchant");
            var mClose = m + Vector3.right * 2.4f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(mClose, Quaternion.LookRotation(m - mClose));
            yield return new WaitForSeconds(0.2f);
            local.GetComponent<PlayerProgress>().ServerAddGold(400);
            local.GetComponent<UI.GameHud>().DebugOpenShop();
            yield return null;
            yield return Shot(dir, "1b_merchant_panel");
            local.GetComponent<PlayerProgress>().ServerResetRun();

            // The tailor: the stall, the mannequin in a dye, and the wardrobe panel.
            var tFrom = Lobby.MannequinSpot + new Vector3(3.1f, 0.1f, -0.3f);
            var tLook = Lobby.MannequinSpot + new Vector3(-1.5f, 0f, -1.5f) - tFrom;
            tLook.y = 0f;
            lobbyMotor.Teleport(tFrom, Quaternion.LookRotation(tLook));
            local.GetComponent<NetworkPlayer>().CloakDye.Value = 2;
            local.GetComponent<NetworkPlayer>().Aura.Value = 1;
            yield return new WaitForSeconds(0.6f);
            yield return Shot(dir, "1c_tailor");
            var tClose = Lobby.TailorSpot + Lobby.TailorFacing * 1.8f + Vector3.up * 0.1f;
            lobbyMotor.Teleport(tClose, Quaternion.LookRotation(Lobby.MannequinSpot - tClose));
            yield return new WaitForSeconds(0.3f);
            local.GetComponent<UI.Wardrobe>().DebugOpen(true);
            yield return null;
            yield return Shot(dir, "1c_tailor_panel");
            local.GetComponent<UI.Wardrobe>().DebugOpen(false);
            local.GetComponent<NetworkPlayer>().CloakDye.Value = 0;
            local.GetComponent<NetworkPlayer>().Aura.Value = 0;

            var anvilObj = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
            if (anvilObj != null)
            {
                var af = anvilObj.transform.position + anvilObj.transform.forward * 2.2f + Vector3.up * 0.1f;
                lobbyMotor.Teleport(af, Quaternion.LookRotation(anvilObj.transform.position - af));
                lobbyMotor.SetPitch(20f);
                yield return new WaitForSeconds(0.5f);
                yield return Shot(dir, "1b_anvil");
                lobbyMotor.SetPitch(0f);
                var ap = local.GetComponent<PlayerProgress>();
                ap.ServerAddEmbers(60);
                ap.TemperRpc();
                ap.TemperRpc();
                ap.ServerAddRune(Rune.Storm);
                ap.ServerAddRune(Rune.Holy);
                ap.ServerAddRune(Rune.Ember);
                ap.ServerAddSigil();
                ap.ServerEquip(new GearItem(GearId.ThornhideJerkin, 0, 0));
                ap.ServerEquip(new GearItem(GearId.LichPhylactery, 3, 0));
                yield return new WaitForSeconds(0.3f);
                ap.SocketRuneRpc((byte)Rune.Storm);
                yield return new WaitForSeconds(0.2f);
                local.GetComponent<UI.GameHud>().DebugOpenForge();
                yield return null;
                yield return Shot(dir, "1b_forge_panel");
                ap.ServerResetRun();
                UI.PauseMenu.SetOpen(false);

                // Gear: a drop on the floor with its comparison card, then the Tab sheet.
                var gFrom = Lobby.Origin + new Vector3(0f, 0.1f, -8f);
                lobbyMotor.Teleport(gFrom, Quaternion.LookRotation(Vector3.forward));
                ap.ServerEquip(new GearItem(GearId.OssuaryPlate, 0, 0));
                ap.ServerEquip(new GearItem(GearId.HuntersTooth, 1, 0));
                GearDrop.ServerSpawn(new GearItem(GearId.KingsguardHauberk, 2, 1), local.OwnerClientId, gFrom + Vector3.forward * 1.6f + Vector3.down * 0.1f);
                GearDrop.ServerSpawn(new GearItem(GearId.LuckyKnucklebone, 0, 0), local.OwnerClientId, gFrom + new Vector3(1.4f, -0.1f, 2.6f));
                lobbyMotor.SetPitch(25f);
                yield return new WaitForSeconds(0.8f);
                yield return Shot(dir, "1d_gear_drop");
                lobbyMotor.SetPitch(0f);
                UI.GameHud.DebugShowStats = true;
                yield return null;
                yield return Shot(dir, "1e_stat_sheet");
                UI.GameHud.DebugShowStats = false;
                ap.ServerResetRun();
            }

            DungeonDirector.Instance.ServerStartRun();
            yield return new WaitForSeconds(2.5f);

            var layout = DungeonDirector.Layout;
            if (layout == null)
            {
                Debug.Log("[Smoke] FAIL screenshots: no local player or layout");
                Application.Quit(1);
                yield break;
            }

            var motor = local.GetComponent<FirstPersonMotor>();
            motor.enabled = false; // stop input and gravity fighting the staged camera

            yield return Shot(dir, "1_start_room");
            var sr = layout.StartRoom;
            var srFrom = sr.Center + new Vector3(-sr.Size.x * 0.42f, 0.1f, -sr.Size.y * 0.42f);
            motor.Teleport(srFrom, Quaternion.LookRotation(sr.Center - srFrom));
            motor.SetPitch(12f);
            yield return new WaitForSeconds(0.4f);
            yield return Shot(dir, "1_start_room_wide");
            motor.SetPitch(0f);
            var map = local.GetComponent<UI.Minimap>();
            map.Large = true;
            yield return null;
            yield return Shot(dir, "1a_large_map");
            map.Large = false;

            // Hollow walls: a secret one up close, then smash it to see the alcove; a breach as it bursts.
            foreach (var hw in FindObjectsByType<CrackedWall>(FindObjectsSortMode.None).OrderBy(x => x.Breach.Value).ToArray())
            {
                var hf = hw.transform.position - hw.transform.forward * 3.2f + Vector3.up * 0.1f;
                motor.Teleport(hf, Quaternion.LookRotation(hw.transform.forward));
                yield return new WaitForSeconds(0.6f);
                if (!hw.Breach.Value)
                {
                    yield return Shot(dir, "1f_secret_wall");
                    hw.GetComponent<Health>().ServerDamage(1000f, hf, local.NetworkObjectId);
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot(dir, "1g_secret_alcove");
                }
                else
                {
                    hw.ServerTriggerBreach(null);
                    yield return new WaitForSeconds(1.0f);
                    yield return Shot(dir, "1h_breach_rumble");
                    yield return new WaitForSeconds(0.75f);
                    yield return Shot(dir, "1i_breach_open");
                    break;
                }
            }

            // Every feature room on the floor, from a high corner looking across it.
            foreach (var room in layout.Rooms)
            {
                if (room.Feature == RoomFeature.None) continue;
                var fp = room.Center + new Vector3(-room.Size.x * 0.42f, 0.1f, -room.Size.y * 0.42f);
                if (room.Feature != RoomFeature.Crypt)
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && room.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, fp);
                var look = room.Center - fp;
                if (room.Feature == RoomFeature.Chasm)
                {
                    // At the lip, between a bridge and the corner, peering down.
                    fp = new Vector3(room.Pit.xMin - 0.8f, 0.1f, room.Pit.center.y + room.Pit.height * 0.25f);
                    look = Vector3.right;
                }
                motor.Teleport(fp, Quaternion.LookRotation(look));
                motor.SetPitch(room.Feature == RoomFeature.Chasm ? 50f : 0f);
                yield return new WaitForSeconds(1.2f);
                yield return Shot(dir, "1c_feature_" + room.Feature);
                motor.SetPitch(0f);
            }

            foreach (var room in layout.Rooms)
            {
                if (room.Kind != RoomKind.Combat) continue;
                var p = room.Center + new Vector3(-room.Size.x * 0.35f, 0.1f, -room.Size.y * 0.35f);
                motor.Teleport(p, Quaternion.LookRotation(room.Center - p));
                yield return new WaitForSeconds(1.5f);
                yield return Shot(dir, "2_combat_room");

                // Status tags: chill one, mark another, stun and taunt a third.
                var all = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Where(e => !(e is BossBrain) && !e.IsDead).ToArray();
                var anchor = all.FirstOrDefault();
                var inRoom = anchor == null ? new EnemyBrain[0] : all.OrderBy(e => Vector3.Distance(e.transform.position, anchor.transform.position)).Take(3).ToArray();
                if (anchor != null)
                {
                    var look = anchor.transform.position + anchor.transform.forward * 5f + Vector3.up * 0.1f;
                    motor.Teleport(look, Quaternion.LookRotation(anchor.transform.position - look));
                    yield return new WaitForSeconds(0.3f);
                }
                if (inRoom.Length > 0) inRoom[0].GetComponent<Health>().ServerChill(10f);
                if (inRoom.Length > 1) inRoom[1].GetComponent<Health>().ServerMark(10f);
                if (inRoom.Length > 2) { inRoom[2].ServerStun(10f); inRoom[2].ServerTaunt(local.GetComponent<Health>(), 10f); }
                foreach (var e in inRoom) e.ServerStun(10f);
                yield return new WaitForSeconds(0.4f);
                yield return Shot(dir, "2a_status_tags");

                // Kill everything in the room so the loot it leaves is visible.
                foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                {
                    if (!(e is BossBrain) && room.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, p);
                }
                yield return new WaitForSeconds(1.8f);
                yield return Shot(dir, "2b_loot");
                break;
            }

            var boss = layout.BossRoom;
            var bp = boss.Center + (layout.BossSpawn - boss.Center).normalized * -9f + Vector3.up * 0.1f;
            motor.Teleport(bp, Quaternion.LookRotation(layout.BossSpawn - bp));
            yield return new WaitForSeconds(1.5f);
            yield return Shot(dir, "3_boss_room");

            // Turn round to see a corridor mouth from inside the boss room.
            motor.Teleport(bp, Quaternion.LookRotation(bp - layout.BossSpawn));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "4_boss_room_back");

            // One look at each later floor's theme.
            var shotFeatures = new System.Collections.Generic.HashSet<RoomFeature>(layout.Rooms.Select(r => r.Feature));
            for (int depth = 1; depth < DungeonDirector.FloorsPerRun; depth++)
            {
                DungeonDirector.Instance.ServerDebugStartFloor(depth);
                yield return new WaitForSeconds(2.5f);
                var l = DungeonDirector.Layout;
                // Any room mechanic not seen yet: from a corner, and (galleries) from the balcony.
                foreach (var room in l.Rooms)
                {
                    if (room.Feature == RoomFeature.None || !shotFeatures.Add(room.Feature)) continue;
                    var fromF = room.Center + new Vector3(-room.Size.x * 0.42f, 0.1f, -room.Size.y * 0.42f);
                    foreach (var e in FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None))
                        if (!(e is BossBrain) && room.Contains(e.transform.position, 1f)) e.GetComponent<Health>().ServerDamage(1e6f, fromF);
                    motor.Teleport(fromF, Quaternion.LookRotation(room.Center - fromF));
                    yield return new WaitForSeconds(1.4f);
                    yield return Shot(dir, "1c_feature_" + room.Feature + "_floor" + (depth + 1));
                    if (room.Balconies.Count > 0)
                    {
                        var d = DungeonLayout.Dir[room.Balconies[0]];
                        bool alongX = room.Balconies[0] == 0 || room.Balconies[0] == 2;
                        var deck = room.Center + new Vector3(d.x, 0f, d.y) * ((alongX ? room.Size.y : room.Size.x) / 2f - 1.6f) + Vector3.up * (DungeonLayout.BalconyHeight + 0.1f);
                        motor.Teleport(deck, Quaternion.LookRotation(room.Center - deck - Vector3.up * 2f));
                        yield return new WaitForSeconds(1f);
                        yield return Shot(dir, "1c_feature_Gallery_balcony");
                    }
                }
                foreach (var room in l.Rooms)
                {
                    if (room.Kind != RoomKind.Combat) continue;
                    var from = room.Center + new Vector3(-room.Size.x * 0.4f, 0.1f, -room.Size.y * 0.4f);
                    motor.Teleport(from, Quaternion.LookRotation(room.Center - from));
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot(dir, "5_floor" + (depth + 1));

                    // Line up one of each newer enemy type for a close look.
                    foreach (var type in new[] { typeof(GhoulBrain), typeof(BruteBrain), typeof(NecromancerBrain), typeof(WraithBrain), typeof(ShieldbearerBrain) })
                    {
                        var e = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).FirstOrDefault(b => b.GetType() == type && !b.IsDead);
                        if (e == null) continue;
                        e.ServerStun(30f);
                        var ep = e.transform.position + e.transform.forward * 3.2f + Vector3.up * 0.1f;
                        motor.Teleport(ep, Quaternion.LookRotation(e.transform.position - ep));
                        yield return new WaitForSeconds(0.5f);
                        yield return Shot(dir, "7_" + type.Name + "_floor" + (depth + 1));
                    }

                    var archer = FindObjectsByType<ArcherBrain>(FindObjectsSortMode.None).FirstOrDefault();
                    if (archer != null)
                    {
                        archer.ServerStun(30f);
                        var ap = archer.transform.position + archer.transform.forward * 2.2f + Vector3.up * 0.1f;
                        motor.Teleport(ap, Quaternion.LookRotation(archer.transform.position - ap));
                        yield return new WaitForSeconds(0.6f);
                        yield return Shot(dir, "6_archer_closeup" + (depth + 1));
                    }
                    break;
                }
            }

            // A chest, the pause menu, and the end-of-run summary.
            var someChest = FindObjectsByType<Chest>(FindObjectsSortMode.None).FirstOrDefault();
            if (someChest != null)
            {
                var cp = someChest.transform.position + someChest.transform.forward * 2.6f + Vector3.up * 0.1f;
                motor.Teleport(cp, Quaternion.LookRotation(someChest.transform.position - cp));
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "8_chest");
                someChest.UseRpc(local.NetworkObject);
                yield return new WaitForSeconds(1f);
                yield return Shot(dir, "8b_chest_open");
            }
            var someUrn = FindObjectsByType<Breakable>(FindObjectsSortMode.None).FirstOrDefault();
            if (someUrn != null)
            {
                var up = someUrn.transform.position + (DungeonDirector.Layout.RoomAt(someUrn.transform.position, 2f)?.Center - someUrn.transform.position ?? Vector3.forward).normalized * 2.4f + Vector3.up * 0.1f;
                motor.Teleport(up, Quaternion.LookRotation(someUrn.transform.position - up));
                yield return new WaitForSeconds(0.6f);
                yield return Shot(dir, "8c_urns_barrels");
            }

            UI.PauseMenu.SetOpen(true);
            yield return null;
            yield return Shot(dir, "9_pause");
            UI.PauseMenu.DebugShowGuide(local.GetComponent<UI.PauseMenu>() ?? FindFirstObjectByType<UI.PauseMenu>());
            yield return null;
            yield return Shot(dir, "9a_guide");
            UI.PauseMenu.SetOpen(false);

            foreach (var p in PlayerLife.All) p.Health.ServerDamage(1e6f, p.transform.position);
            yield return new WaitForSeconds(1f);
            yield return Shot(dir, "9b_run_summary");

            Debug.Log("[Smoke] screenshots done");
            Application.Quit(0);
        }

        /// <summary>
        /// Boss mechanics check: for each floor boss, drop the host into the arena, keep it
        /// alive, and let the fight run while logging every mechanic ([Boss] lines) and
        /// capturing frames. Run windowed: <c>-bosstest C:\\temp\\boss</c>.
        /// </summary>
        public void BossTest(string dir, NetworkLauncher launcher) => StartCoroutine(BossSequence(dir, launcher));

        private IEnumerator BossSequence(string dir, NetworkLauncher launcher)
        {
            System.IO.Directory.CreateDirectory(dir);
            launcher.StartHost();
            yield return new WaitForSeconds(2f);

            var local = NetworkPlayer.Local;
            var motor = local.GetComponent<FirstPersonMotor>();
            var hp = local.GetComponent<Health>();

            for (int depth = 0; depth < DungeonDirector.FloorsPerRun; depth++)
            {
                DungeonDirector.Instance.ServerDebugStartFloor(depth);
                yield return new WaitForSeconds(2f);
                motor.enabled = false;

                var layout = DungeonDirector.Layout;
                var boss = BossBrain.Active;
                Debug.Log("[Smoke] boss test floor " + (depth + 1) + ": " + (boss != null ? boss.DisplayName : "NO BOSS"));
                if (boss == null) continue;

                // Stand across the room, in its line of sight, so it has to come — or shoot.
                var at = layout.BossRoom.Center - (layout.BossSpawn - layout.BossRoom.Center).normalized * 8f + Vector3.up * 0.1f;
                motor.Teleport(at, Quaternion.LookRotation(boss.transform.position - at));

                for (int i = 0; i < 14; i++)
                {
                    yield return new WaitForSeconds(1.2f);
                    hp.ServerSetFraction(1f);
                    var look = boss.transform.position - local.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.1f) local.transform.rotation = Quaternion.LookRotation(look);
                    if (i % 3 == 1) yield return Shot(dir, "boss" + (depth + 1) + "_" + i);
                    // Walk toward the Lich mid-test, to provoke the blink.
                    // Wound it halfway through, to see the late-fight phases (summons, surges, enrage).
                    if (i == 7) boss.GetComponent<Health>().ServerSetFraction(0.3f);
                    if (depth == 1 && i == 6) motor.Teleport(boss.transform.position + boss.transform.forward * 2f, local.transform.rotation);
                }
            }

            Debug.Log("[Smoke] boss test done");
            Application.Quit(0);
        }

        private static IEnumerator Shot(string dir, string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png"));
            yield return null;
            yield return null;
        }

        private static void Log(string check, string value, bool ok)
        {
            Debug.Log("[Smoke] " + (ok ? "PASS " : "FAIL ") + check + ": " + value);
        }
    }
}
