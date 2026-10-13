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
            yield return new WaitForSeconds(seconds * 0.25f);

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
                Log("firebolt shatters", "chill consumed " + !dummy.Chilled.Value + ", dealt " + (before - dummy.Current.Value).ToString("F0"),
                    !dummy.Chilled.Value && before - dummy.Current.Value > MageKit.FireboltDamage);
                DungeonDirector.Instance.ServerStartRun();
            }

            yield return new WaitForSeconds(seconds * 0.15f);

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
                // Kill a handful of skeletons and confirm they drop loot that a player can take.
                var victims = FindObjectsByType<EnemyBrain>(FindObjectsSortMode.None).Where(e => !(e is BossBrain)).Take(6).ToList();
                foreach (var v in victims) v.GetComponent<Health>().ServerDamage(1e6f, v.transform.position);
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
                int potions = 0;
                for (int i = 0; i < Core.Stats.Count; i++) potions += progress.PotionsApplied((Core.StatType)i);
                Log("pickup", "gold " + goldBefore + " -> " + progress.Gold.Value + ", potions " + potions,
                    progress.Gold.Value > goldBefore || potions > 0);
                motor.Teleport(layout.PlayerSpawnFor(local.OwnerClientId), local.transform.rotation);
            }

            yield return new WaitForSeconds(seconds * 0.1f);

            if (net.IsServer && boss != null)
            {
                boss.Health.ServerDamage(1e6f, boss.transform.position);
                yield return new WaitForSeconds(1f);
                var phase = DungeonDirector.Instance.Phase.Value;
                Log("boss kill -> phase", phase.ToString(), phase == RunPhase.FloorCleared);
            }

            yield return new WaitForSeconds(seconds * 0.5f - 1f);
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

            var m = layout.MerchantPosition;
            var mFrom = m + (layout.StartRoom.Center - m).normalized * 3.2f + Vector3.up * 0.1f;
            motor.Teleport(mFrom, Quaternion.LookRotation(m - mFrom));
            yield return new WaitForSeconds(0.5f);
            yield return Shot(dir, "1b_merchant");

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
