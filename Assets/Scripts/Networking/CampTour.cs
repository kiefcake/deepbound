using System.Collections;
using System.Collections.Generic;
using System.IO;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// <c>-camptour &lt;dir&gt;</c> (2026-11-07, the user: camp placements "very very sloppy", "nothing should seem out of place"):
    /// hosts, then renders the camp from its own camera into PNGs with no window and no HUD: a lit top-down plan of the whole
    /// castle, four quadrant plans, and eye-level views of every station and corner from two sides. Works in batchmode
    /// (not with -nographics), so it can run while someone plays. Writes <c>tour.txt</c> listing every named spot.
    /// </summary>
    public class CampTour : MonoBehaviour
    {
        private string dir;
        private NetworkLauncher launcher;
        private readonly Texture2D[] last = new Texture2D[1];

        public void Run(string outDir, NetworkLauncher l)
        {
            dir = outDir;
            launcher = l;
            StartCoroutine(Tour());
        }

        private IEnumerator Tour()
        {
            Directory.CreateDirectory(dir);
            yield return new WaitForSeconds(2f);
            launcher.StartHost();
            for (float t = 0f; t < 20f && (NetworkPlayer.Local == null || !DungeonDirector.InLobby); t += 0.25f) yield return new WaitForSeconds(0.25f);
            yield return new WaitForSeconds(3f);
            var local = NetworkPlayer.Local;
            // The photographer stands out of every shot.
            if (local != null) local.GetComponent<FirstPersonMotor>().Teleport(Lobby.Origin + new Vector3(0f, -40f, 0f), Quaternion.identity);
            yield return new WaitForSeconds(0.5f);

            var camGo = new GameObject("TourCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            var lines = new List<string>();

            // ---- Plans: bright, flat light from above so every footprint reads.
            var sun = new GameObject("TourSun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(70f, 30f, 0f);
            var ambient = RenderSettings.ambientLight;
            var ambientMode = RenderSettings.ambientMode;
            bool fog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.58f);
            RenderSettings.fog = false;

            var o = Lobby.Origin;
            float r = Lobby.Radius + 3f;
            cam.orthographic = true;
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.transform.position = o + Vector3.up * 80f;
            cam.orthographicSize = r * 1.08f;
            yield return Render(cam, "plan_all", 2048, 2048);
            // Quadrants, each half the camp, overlapping at the middle.
            foreach (var (qx, qz, tag) in new[] { (-1f, 1f, "NW"), (1f, 1f, "NE"), (-1f, -1f, "SW"), (1f, -1f, "SE") })
            {
                cam.transform.position = o + new Vector3(qx * r * 0.45f, 80f, qz * r * 0.45f);
                cam.orthographicSize = r * 0.58f;
                yield return Render(cam, "plan_" + tag, 2048, 2048);
            }
            // A grid of 10 m cells, labelled, as a key for the plans: printed into tour.txt.
            lines.Add("PLAN: camera straight down, +X right (east), +Z up (north). Origin " + o + ", radius " + r.ToString("F1") + " m.");

            // ---- -tourbot: the inspector (CampInspector) with the game's own light, then done.
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-tourbot") >= 0)
            {
                Destroy(sun.gameObject);
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.fog = fog;
                cam.orthographic = false;
                cam.fieldOfView = 70f;
                // The light a player carries at camp (Art/PresenceLight is off in batchmode): half strength, 3 m ahead.
                var carried = new GameObject("TourPresence").AddComponent<Light>();
                carried.type = LightType.Point;
                carried.range = Art.PresenceLight.Range;
                carried.intensity = Art.PresenceLight.Intensity * 0.5f;
                carried.color = new Color(1f, 0.86f, 0.7f);
                carried.shadows = LightShadows.None;
                var follow = carried.gameObject.AddComponent<TourFollow>();
                follow.Cam = cam;
                yield return CampInspector.Run(cam, dir, Render, last);
                Application.Quit(0);
                yield break;
            }

            // ---- Eye level, as a player sees it: the game's own light, a little brighter so dark corners show.
            Destroy(sun.gameObject);
            RenderSettings.ambientLight = ambient * 1.6f + new Color(0.06f, 0.06f, 0.07f);
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.fog = fog;
            cam.orthographic = false;
            cam.fieldOfView = 70f;

            var spots = new List<(string name, Vector3 at)>
            {
                ("bonfire", o), ("boarding", Lobby.BoardingCenter), ("carriage", Lobby.CarriageAt), ("merchant", Lobby.MerchantSpot),
                ("anvil", Lobby.AnvilSpot), ("tent", Lobby.TentCenter), ("gong", Lobby.GongSpot), ("tavern", Lobby.TavernCenter),
                ("shrine", Lobby.ShrineSpot), ("yard", Lobby.YardSpot), ("runewright", Lobby.RunewrightSpot), ("bounty", Lobby.BountySpot),
                ("records", Lobby.RecordsSpot), ("well", Lobby.WellSpot), ("omen", Lobby.OmenSpot), ("tailor", Lobby.TailorSpot),
                ("mannequin", Lobby.MannequinSpot), ("range", Lobby.RangeCenter), ("dummies", Lobby.DummySpots[0]),
            };
            foreach (var (name, at) in spots) lines.Add(name + " " + (at - o).ToString("F1"));
            // Ring of the wall: eight views from inside the curtain wall looking in, to judge the edges.
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dirOut = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                spots.Add(("wall" + (int)a, o + dirOut * r * 0.82f));
            }

            int n = 0;
            // -tourquick: plans, the fire, the tent and the card table only (the walk grid and station views take minutes).
            bool quick = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-tourquick") >= 0;
            // Aerial obliques (the user: "take more angles"): eight from outside the wall high up, eight closer and lower.
            for (int i = 0; i < 8; i++)
            {
                var d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                cam.transform.position = o + d * 34f + Vector3.up * 24f;
                cam.transform.rotation = Quaternion.LookRotation(o - d * 4f - cam.transform.position);
                yield return Render(cam, "air_" + (n++).ToString("000") + "_high_" + i * 45, 1600, 900);
                cam.transform.position = o + d * 20f + Vector3.up * 11f;
                cam.transform.rotation = Quaternion.LookRotation(o + d * 2f - cam.transform.position);
                yield return Render(cam, "air_" + (n++).ToString("000") + "_low_" + i * 45, 1600, 900);
            }
            // A walk across the courtyard: every 8 m, looking north, east, south and west at head height.
            for (float gx = -32f; gx <= 32f && !quick; gx += 8f)
                for (float gz = -32f; gz <= 32f; gz += 8f)
                {
                    if (gx * gx + gz * gz > 31f * 31f) continue;
                    var at = o + new Vector3(gx, 1.7f, gz);
                    foreach (var (yaw, tag) in new[] { (0f, "N"), (90f, "E"), (180f, "S"), (270f, "W") })
                    {
                        cam.transform.position = at;
                        cam.transform.rotation = Quaternion.Euler(4f, yaw, 0f);
                        yield return Render(cam, "walk_" + gx.ToString("+00;-00") + "_" + gz.ToString("+00;-00") + "_" + tag, 1280, 720);
                    }
                }
            // From the spawn fire, sitting and standing, toward each side; and inside the tent looking up (its roof).
            var pit = Lobby.FirePit;
            foreach (var (yaw, tag) in new[] { (270f, "tavern"), (90f, "tent"), (0f, "north"), (180f, "south"), (235f, "sw"), (125f, "se") })
                foreach (var (hgt, pose) in new[] { (1.75f, "stand"), (1.15f, "sit") })
                {
                    var dirF = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    cam.transform.position = pit - dirF * 3.9f + Vector3.up * hgt;
                    cam.transform.rotation = Quaternion.Euler(3f, yaw, 0f);
                    yield return Render(cam, "fire_" + tag + "_" + pose, 1280, 720);
                }
            cam.transform.position = Lobby.TentCenter + new Vector3(1.2f, 1.7f, 0.5f);
            cam.transform.rotation = Quaternion.Euler(-55f, 250f, 0f);
            yield return Render(cam, "tent_inside_up", 1280, 720);
            cam.transform.position = Lobby.TentCenter + new Vector3(2.8f, 1.7f, 0f);
            cam.transform.rotation = Quaternion.Euler(5f, 270f, 0f);
            yield return Render(cam, "tent_inside_out", 1280, 720);
            cam.transform.position = Lobby.TentCenter + new Vector3(-1.5f, 1.7f, 1.5f);
            cam.transform.rotation = Quaternion.Euler(8f, 150f, 0f);
            yield return Render(cam, "tent_inside_table", 1280, 720);
            foreach (var (name, at) in quick ? new List<(string, Vector3)>() : spots)
            {
                // Two views of each: from the camp's middle side and from the side away from it, at head height.
                var toCentre = o - at; toCentre.y = 0f;
                if (toCentre.sqrMagnitude < 1f) toCentre = Vector3.back;
                toCentre.Normalize();
                var side = Vector3.Cross(Vector3.up, toCentre);
                var views = name.StartsWith("wall")
                    ? new[] { (at + Vector3.up * 1.7f, at + Vector3.up * 1.2f + toCentre * 10f, "in"), (at + Vector3.up * 1.7f - side * 0.5f, at + Vector3.up * 1.2f + side * 8f, "along") }
                    : new[]
                    {
                        (at + new Vector3(0f, 1.75f, 5f), at + Vector3.up * 1f, "fromN"), (at + new Vector3(5f, 1.75f, 0f), at + Vector3.up * 1f, "fromE"),
                        (at + new Vector3(0f, 1.75f, -5f), at + Vector3.up * 1f, "fromS"), (at + new Vector3(-5f, 1.75f, 0f), at + Vector3.up * 1f, "fromW"),
                        (at + toCentre * 5.5f + side * 1.5f + Vector3.up * 1.75f, at + Vector3.up * 1f, "a"), (at + Vector3.up * 9f + toCentre * 3f, at, "top"),
                    };
                foreach (var (from, look, tag) in views)
                {
                    cam.transform.position = from;
                    cam.transform.rotation = Quaternion.LookRotation(look - from);
                    yield return Render(cam, "eye_" + (n++).ToString("00") + "_" + name + "_" + tag, 1280, 720);
                }
            }
            // Seated at Deadman's Hand with a hand dealt: what the player sees over the felt.
            var bj = FindAnyObjectByType<BlackjackTable>();
            if (bj != null && local != null)
            {
                var bjProg = local.GetComponent<Progression.PlayerProgress>();
                bjProg.ServerAddGold(300);
                bj.ServerDebugDeal(bjProg, 25, new byte[] { 9, 0, 19, 51 });
                yield return new WaitForSeconds(2.5f);
                var vc = Art.ViewCamera.Current;
                if (vc != null)
                {
                    cam.transform.SetPositionAndRotation(vc.transform.position, vc.transform.rotation);
                    yield return Render(cam, "blackjack_seat", 1280, 720);
                    bj.HitRpc(local.NetworkObject);
                    yield return new WaitForSeconds(1.5f);
                    cam.transform.SetPositionAndRotation(vc.transform.position, vc.transform.rotation);
                    yield return Render(cam, "blackjack_hit", 1280, 720);
                    lines.Add("blackjack seat camera " + vc.transform.position + " " + vc.transform.eulerAngles);
                }
            }
            // The roll of the first expedition (2026-11-14): bare (nobody home), half home, then all four and the fifth name.
            var dd = World.DungeonDirector.Instance;
            if (dd != null && dd.IsServer)
            {
                var rdir = Quaternion.Euler(0f, -320f, 0f) * Vector3.right;
                var roll = World.Lobby.TentCenter + rdir * 3.9f;
                uint keep0 = dd.CampKeeps.Value;
                var home = new uint[] { 0u, Progression.Expedition.Bit(0) | Progression.Expedition.Bit(2), Progression.Expedition.Bit(0) | Progression.Expedition.Bit(1) | Progression.Expedition.Bit(2) | Progression.Expedition.Bit(3) };
                for (int k = 0; k < home.Length; k++)
                {
                    dd.CampKeeps.Value = (keep0 & ~(0xFu << Progression.Expedition.FirstBit)) | home[k];
                    yield return new WaitForSeconds(0.4f);
                    cam.transform.position = roll - rdir * 2.4f + Vector3.up * 1.6f;
                    cam.transform.rotation = Quaternion.LookRotation(roll + Vector3.up * 1.35f - cam.transform.position);
                    yield return Render(cam, "expedition_roll_" + k, 1280, 720);
                }
                cam.transform.position = roll - rdir * 4.5f + Quaternion.Euler(0f, 90f, 0f) * rdir * 1.8f + Vector3.up * 1.8f;
                cam.transform.rotation = Quaternion.LookRotation(roll + Vector3.up * 1f - cam.transform.position);
                yield return Render(cam, "expedition_roll_wide", 1280, 720);
                dd.CampKeeps.Value = keep0;
            }
            File.WriteAllLines(Path.Combine(dir, "tour.txt"), lines);
            Debug.Log("[CampTour] done: " + n + " views");
            Application.Quit(0);
        }

        private IEnumerator Render(Camera cam, string name, int w, int h)
        {
            yield return new WaitForEndOfFrame();
            // The retro pass stays off for these (it would grid them to the screen's lines).
            Shader.SetGlobalFloat("_RetroOn", 0f);
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(88));
            // Kept until the next shot, so the inspector can measure it.
            if (last[0] != null) Destroy(last[0]);
            last[0] = tex;
        }
    }
}
