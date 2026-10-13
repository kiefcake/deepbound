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
        public void Run(float seconds) => StartCoroutine(Sequence(seconds));

        private IEnumerator Sequence(float seconds)
        {
            yield return new WaitForSeconds(seconds * 0.4f);

            var net = NetworkManager.Singleton;
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
            var layout = DungeonDirector.Layout;
            if (local == null || layout == null)
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

            Debug.Log("[Smoke] screenshots done");
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
