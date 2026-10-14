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
                    FindObjectsByType<SpikeTrap>(FindObjectsSortMode.None).Length + " traps", chests.Length == sideRooms + (layout.Rooms.Any(r => r.Feature == RoomFeature.Vault) ? 2 : 0) && urns > 0);
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

                // Feature rooms.
                var fm = local.GetComponent<FirstPersonMotor>();
                string features = string.Join(", ", layout.Rooms.Where(r => r.Feature != RoomFeature.None).Select(r => r.Feature.ToString()));
                Log("feature rooms", features, layout.Rooms.Any(r => r.Feature == RoomFeature.GreatHall) && layout.Rooms.Any(r => r.Feature == RoomFeature.Chasm));

                var sarc = FindObjectsByType<Sarcophagus>(FindObjectsSortMode.None).FirstOrDefault();
                if (sarc != null)
                {
                    fm.Teleport(sarc.transform.position + sarc.transform.forward * 3f + Vector3.up * 0.1f, local.transform.rotation);
                    yield return new WaitForSeconds(0.5f);
                    Log("crypt", "sarcophagus opened on approach: " + sarc.Opened.Value, sarc.Opened.Value);
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
                Log("vault", levers.Length + " levers pulled, closed gates " + gatesBefore + " -> " + gatesAfter, levers.Length == 3 && gatesAfter < gatesBefore);

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
                else Log("anvil", "none in the start room", false);
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

            // Wait until the party has descended to the next floor, so the wipe is not
            // undone by the descent reviving everyone.
            float descendBy = Time.time + 20f;
            while (net.IsServer && DungeonDirector.Instance.Phase.Value != RunPhase.Exploring && Time.time < descendBy) yield return null;
            yield return new WaitForSeconds(1f);

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
            // The Ranger at full draw.
            prog.ClassId.Value = Classes.ClassId.Ranger;
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "0f_ranger_bow");

            prog.ClassId.Value = Classes.ClassId.Knight;
            yield return new WaitForSeconds(0.3f);

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
            var map = local.GetComponent<UI.Minimap>();
            map.Large = true;
            yield return null;
            yield return Shot(dir, "1a_large_map");
            map.Large = false;

            var m = layout.MerchantPosition;
            var mFrom = m + (layout.StartRoom.Center - m).normalized * 3.2f + Vector3.up * 0.1f;
            motor.Teleport(mFrom, Quaternion.LookRotation(m - mFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "1b_merchant");

            var anvilObj = FindObjectsByType<Anvil>(FindObjectsSortMode.None).FirstOrDefault();
            if (anvilObj != null)
            {
                var af = anvilObj.transform.position + anvilObj.transform.forward * 2.2f + Vector3.up * 0.1f;
                motor.Teleport(af, Quaternion.LookRotation(anvilObj.transform.position - af));
                motor.SetPitch(20f);
                yield return new WaitForSeconds(0.5f);
                yield return Shot(dir, "1b_anvil");
                motor.SetPitch(0f);
                var ap = local.GetComponent<PlayerProgress>();
                ap.ServerAddEmbers(60);
                ap.TemperRpc();
                ap.TemperRpc();
                yield return new WaitForSeconds(0.3f);
                local.GetComponent<UI.GameHud>().DebugOpenForge();
                yield return null;
                yield return Shot(dir, "1b_forge_panel");
                ap.ServerResetRun();
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
            for (int depth = 1; depth < DungeonDirector.FloorsPerRun; depth++)
            {
                DungeonDirector.Instance.ServerDebugStartFloor(depth);
                yield return new WaitForSeconds(2.5f);
                var l = DungeonDirector.Layout;
                foreach (var room in l.Rooms)
                {
                    if (room.Kind != RoomKind.Combat) continue;
                    var from = room.Center + new Vector3(-room.Size.x * 0.4f, 0.1f, -room.Size.y * 0.4f);
                    motor.Teleport(from, Quaternion.LookRotation(room.Center - from));
                    yield return new WaitForSeconds(1.2f);
                    yield return Shot(dir, "5_floor" + (depth + 1));

                    // Line up one of each newer enemy type for a close look.
                    foreach (var type in new[] { typeof(GhoulBrain), typeof(BruteBrain), typeof(NecromancerBrain) })
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

            for (int depth = 0; depth < DungeonDirector.FloorsPerRun - 1; depth++)
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
