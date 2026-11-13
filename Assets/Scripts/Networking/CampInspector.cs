using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Networking
{
    /// <summary>
    /// The camp inspector (2026-11-07, the user: "send a bot in and gather screenshots of anything that looks off, for
    /// example not enough light, stations facing wrong way"). Run by <see cref="CampTour"/> with <c>-tourbot</c>. For every
    /// station and person in camp it stands where a player would to use it and photographs it with the game's own light
    /// (no brightening), walks back 7 m and looks again, and checks by measurement:
    /// <list type="bullet">
    /// <item>LIGHT: mean brightness of the middle of the use view (DARK under <see cref="DarkLevel"/>, DIM under <see cref="DimLevel"/>);</item>
    /// <item>FACING: does the front point into a wall or prop within 2.5 m, and how far does it turn from the way players come (the fire / plaza);</item>
    /// <item>STANDING: is there room for a player at the use spot, and a clear line from it to the station;</item>
    /// <item>NEIGHBOURS: the nearest other station, flagged when closer than <see cref="Crowded"/>.</item>
    /// </list>
    /// Writes <c>inspect.txt</c> (one line per station, flags first) and <c>insp_*.jpg</c>.
    /// </summary>
    public static class CampInspector
    {
        public const float DarkLevel = 0.07f, DimLevel = 0.12f, Crowded = 2.6f;

        public struct Station
        {
            public string Name;
            public Vector3 At;      // the thing itself (feet)
            public Vector3 Front;   // the side a player uses it from
            public float Look;      // height to look at
            public float Stand;     // how far in front the player stands
        }

        public static List<Station> Stations()
        {
            Vector3 F(Vector3 v) { v.y = 0f; return v.normalized; }
            var list = new List<Station>
            {
                new Station { Name = "merchant_silas", At = Lobby.MerchantSpot, Front = Vector3.right },
                new Station { Name = "smith_hobb", At = Lobby.HobbSpot, Front = Lobby.AnvilFacing, Stand = 2.9f },
                new Station { Name = "tailor_vey", At = Lobby.VeySpot, Front = Lobby.TailorFacing, Stand = 1.9f },
                new Station { Name = "mannequin", At = Lobby.MannequinSpot, Front = Vector3.right },
                new Station { Name = "yard_brann", At = Lobby.YardSpot, Front = Vector3.right },
                new Station { Name = "dummy", At = Lobby.DummySpots[0], Front = Vector3.right },
                new Station { Name = "runewright_ysolde", At = Lobby.RunewrightSpot, Front = F(new Vector3(-4f, 0f, -1f)) },
                new Station { Name = "shrine_aldric", At = Lobby.ShrineSpot, Front = Vector3.left },
                new Station { Name = "omen_stone", At = Lobby.OmenSpot, Front = Vector3.left },
                new Station { Name = "gong", At = Lobby.GongSpot, Front = Lobby.GongFacing },
                new Station { Name = "bounty_board", At = Lobby.BountySpot, Front = Lobby.BountyFacing },
                new Station { Name = "hall_of_records", At = Lobby.RecordsSpot, Front = Lobby.BountyFacing },
                new Station { Name = "well", At = Lobby.WellSpot, Front = Vector3.back },
                new Station { Name = "carriage_corwin", At = Lobby.CarriageStall, Front = Vector3.back, Look = 1.8f },
                new Station { Name = "carriage_tailgate", At = Lobby.CarriageAt + Vector3.back * 2.2f, Front = Vector3.back },
                new Station { Name = "range", At = Lobby.RangeCenter, Front = Vector3.left },
                new Station { Name = "tavern_door", At = Lobby.TavernCenter + new Vector3(Lobby.TavernHalfX, 0f, 0f), Front = Vector3.right, Look = 1.6f, Stand = 3f },
                new Station { Name = "tavern_keep_marta", At = Lobby.TavernKeepSpot, Front = Vector3.right },
                new Station { Name = "tavern_band", At = Lobby.TavernBandSpot, Front = Vector3.forward },
                new Station { Name = "tent_mouth", At = Lobby.TentCenter + new Vector3(-4.2f, 0f, 0f), Front = Vector3.left, Look = 1.6f, Stand = 3f },
                new Station { Name = "tent_coffer", At = Lobby.CofferSpot, Front = Lobby.TentFacing },
                new Station { Name = "tent_slots", At = Lobby.SlotSpot, Front = Lobby.TentFacing },
                new Station { Name = "tent_blackjack", At = Lobby.BlackjackSpot, Front = Vector3.forward, Stand = 2.4f },
                new Station { Name = "arm_wrestle", At = Lobby.WrestleSpot, Front = Lobby.WrestleFacing, Stand = 2.2f },
                new Station { Name = "fire_logs", At = Lobby.FirePit, Front = Vector3.forward, Stand = 4.5f, Look = 0.8f },
            };
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.Look <= 0f) s.Look = 1.3f;
                if (s.Stand <= 0f) s.Stand = 2f;
                list[i] = s;
            }
            return list;
        }

        public static IEnumerator Run(Camera cam, string dir, System.Func<Camera, string, int, int, IEnumerator> render, Texture2D[] lastShot)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CAMP INSPECTION: use view = where a player stands to use it, the game's own light. Flags first.");
            sb.AppendLine("light = mean brightness of the middle third of the use view (DARK < " + DarkLevel + ", DIM < " + DimLevel + ").");
            sb.AppendLine("turn = degrees between the station's front and the way to the fire (players come from the plaza).");
            sb.AppendLine();
            var all = Stations();
            int mask = ~0;
            var thumbs = new List<Color32[]>();
            foreach (var s in all)
            {
                var flags = new List<string>();
                var front = s.Front; front.y = 0f; front.Normalize();
                var standAt = s.At + front * s.Stand;
                var eye = standAt + Vector3.up * 1.65f;
                var look = s.At + Vector3.up * s.Look;

                // Room to stand: a player's capsule at the use spot.
                bool room = !Physics.CheckCapsule(standAt + Vector3.up * 0.45f, standAt + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore);
                if (!room) flags.Add("NO-ROOM-TO-STAND");
                // A clear line from the eye to the thing (something between hides it).
                bool seen = !Physics.Linecast(eye, look + (eye - look).normalized * 0.6f, out var hitLine, mask, QueryTriggerInteraction.Ignore);
                if (!seen) flags.Add("BLOCKED-BY " + Clean(hitLine.collider.name));
                // Does its front run into something close?
                if (Physics.Raycast(s.At + Vector3.up * 1f + front * 0.6f, front, out var hitFront, 1.9f, mask, QueryTriggerInteraction.Ignore))
                    flags.Add("FRONT-INTO " + Clean(hitFront.collider.name) + " at " + (hitFront.distance + 0.6f).ToString("F1") + "m");
                // How it turns from the way players arrive.
                var toFire = Lobby.FirePit - s.At; toFire.y = 0f;
                var toPlaza = Lobby.Origin - s.At; toPlaza.y = 0f;
                float turn = Mathf.Min(Vector3.Angle(front, toFire), Vector3.Angle(front, toPlaza));
                if (turn > 100f && s.Name != "fire_logs") flags.Add("FACES-AWAY " + turn.ToString("F0") + "deg");
                // Neighbours.
                string near = ""; float nearD = 99f;
                foreach (var o in all)
                {
                    if (o.Name == s.Name) continue;
                    float d = Flat(o.At - s.At);
                    if (d < nearD) { nearD = d; near = o.Name; }
                }
                if (nearD < Crowded && !s.Name.StartsWith("tent_") && !s.Name.StartsWith("carriage")) flags.Add("CROWDED by " + near);

                // The use view, as the player sees it.
                cam.transform.position = eye;
                cam.transform.rotation = Quaternion.LookRotation(look - eye);
                yield return render(cam, "insp_" + s.Name + "_use", 1280, 720);
                float lum = Brightness(lastShot[0]);
                thumbs.Add(Half(lastShot[0]));
                if (lum < DarkLevel) flags.Add("DARK");
                else if (lum < DimLevel) flags.Add("DIM");
                // Walking up to it: 7 m out along its front (or from the fire's side if that is a wall).
                var far = s.At + front * 7f;
                if (Physics.Linecast(s.At + Vector3.up * 1.6f, far + Vector3.up * 1.6f, mask, QueryTriggerInteraction.Ignore))
                {
                    var dirIn = toFire.sqrMagnitude > 1f ? toFire.normalized : front;
                    far = s.At + dirIn * 7f;
                }
                cam.transform.position = far + Vector3.up * 1.7f;
                cam.transform.rotation = Quaternion.LookRotation(s.At + Vector3.up * 1.2f - cam.transform.position);
                yield return render(cam, "insp_" + s.Name + "_approach", 1280, 720);
                float lumFar = Brightness(lastShot[0]);
                thumbs.Add(Half(lastShot[0]));

                sb.Append(s.Name.PadRight(20)).Append(flags.Count == 0 ? "ok" : string.Join(", ", flags))
                  .Append("   | light ").Append(lum.ToString("F3")).Append(" (approach ").Append(lumFar.ToString("F3")).Append(")")
                  .Append(", turn ").Append(turn.ToString("F0"))
                  .Append(", nearest ").Append(near).Append(" ").Append(nearD.ToString("F1")).Append("m")
                  .Append(", at ").Append((s.At - Lobby.Origin).ToString("F1")).Append(" front ").Append(front.ToString("F1"))
                  .AppendLine();
            }
            // Rounds: each walker fast-forwarded to halfway out and to their errand's end, seen from 4 m.
            foreach (var who in new List<FolkId>(FolkRounds.Walkers))
            {
                foreach (var (along, tag) in new[] { (0.5f, "walking"), (-1f, "there") })
                {
                    FolkRounds.DebugArrive(who, along);
                    yield return new WaitForSeconds(0.6f);
                    var fig = Art.FolkIdle.Of(who);
                    if (fig == null) continue;
                    var p = fig.transform.position;
                    var view = (Lobby.Origin - p); view.y = 0f;
                    view = view.sqrMagnitude > 1f ? view.normalized : Vector3.forward;
                    cam.transform.position = p + view * 4f + Vector3.up * 2.2f;
                    cam.transform.rotation = Quaternion.LookRotation(p + Vector3.up * 1.1f - cam.transform.position);
                    yield return render(cam, "round_" + who + "_" + tag, 1280, 720);
                    sb.AppendLine("round " + who + " " + tag + ": at " + (p - Lobby.Origin).ToString("F1"));
                }
            }
            FolkRounds.DebugShift = 0;
            yield return new WaitForSeconds(0.6f);

            // Faces: every named camp person from 1.4 m in front, at the height of their eyes (portraits, two sheets).
            var faces = new List<Color32[]>();
            var faceNames = new List<string>();
            foreach (var f in Art.FolkIdle.All)
            {
                if (f == null || string.IsNullOrEmpty(f.DisplayName)) continue;
                var head = f.HeadTop + Vector3.down * 0.5f;
                var fwd = f.transform.forward; fwd.y = 0f; fwd.Normalize();
                cam.transform.position = head + fwd * 1.5f + Vector3.up * 0.05f;
                cam.transform.rotation = Quaternion.LookRotation(head + Vector3.down * 0.25f - cam.transform.position);
                yield return render(cam, "face_" + f.DisplayName.Replace(" ", "_"), 1280, 720);
                faces.Add(Half(lastShot[0]));
                faceNames.Add(f.DisplayName + (f.Work != Art.FolkIdle.WorkKind.None ? " (" + f.Work + ")" : ""));
            }
            for (int first = 0; first < faces.Count; first += 6)
            {
                int n = Mathf.Min(6, faces.Count - first);
                int rowsF = (n + 1) / 2;
                var sheet = new Texture2D(ThumbW * 2, ThumbH * rowsF, TextureFormat.RGB24, false);
                var px = new Color32[ThumbW * 2 * ThumbH * rowsF];
                for (int i = 0; i < n; i++)
                {
                    int r = i / 2, c = i % 2;
                    var t = faces[first + i];
                    int row0 = (rowsF - 1 - r) * ThumbH;
                    for (int y = 0; y < ThumbH; y++) System.Array.Copy(t, y * ThumbW, px, (row0 + y) * ThumbW * 2 + c * ThumbW, ThumbW);
                }
                sheet.SetPixels32(px);
                sheet.Apply();
                string fname = "faces_" + (first / 6 + 1).ToString("00");
                File.WriteAllBytes(Path.Combine(dir, fname + ".jpg"), sheet.EncodeToJPG(80));
                Object.Destroy(sheet);
                sb.Append(fname).Append(": ").AppendLine(string.Join(" | ", faceNames.GetRange(first, n)) + "   (left to right, top to bottom)");
            }

            // Contact sheets: three stations a sheet, the use view left and the approach right (cheap to look through).
            int per = 3;
            for (int first = 0; first < all.Count; first += per)
            {
                int rows = Mathf.Min(per, all.Count - first);
                var sheet = new Texture2D(ThumbW * 2, ThumbH * rows, TextureFormat.RGB24, false);
                var px = new Color32[ThumbW * 2 * ThumbH * rows];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < 2; c++)
                    {
                        var t = thumbs[(first + r) * 2 + c];
                        int row0 = (rows - 1 - r) * ThumbH;
                        for (int y = 0; y < ThumbH; y++)
                            System.Array.Copy(t, y * ThumbW, px, (row0 + y) * ThumbW * 2 + c * ThumbW, ThumbW);
                    }
                sheet.SetPixels32(px);
                sheet.Apply();
                int n = first / per + 1;
                File.WriteAllBytes(Path.Combine(dir, "sheet_" + n.ToString("00") + ".jpg"), sheet.EncodeToJPG(80));
                Object.Destroy(sheet);
                sb.Append("sheet_").Append(n.ToString("00")).Append(": ");
                for (int r = 0; r < rows; r++) sb.Append(r == 0 ? "" : " / ").Append(all[first + r].Name);
                sb.AppendLine("   (top to bottom; left = use view, right = approach)");
            }
            File.WriteAllText(Path.Combine(dir, "inspect.txt"), sb.ToString());
            Debug.Log("[CampInspector] " + all.Count + " stations inspected");
        }

        private const int ThumbW = 640, ThumbH = 360;

        /// <summary>A 1280x720 shot at half size.</summary>
        private static Color32[] Half(Texture2D tex)
        {
            var src = tex.GetPixels32();
            int w = tex.width;
            var dst = new Color32[ThumbW * ThumbH];
            for (int y = 0; y < ThumbH; y++)
                for (int x = 0; x < ThumbW; x++)
                    dst[y * ThumbW + x] = src[Mathf.Min(tex.height - 1, y * 2) * w + Mathf.Min(w - 1, x * 2)];
            return dst;
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        private static string Clean(string n) => n.Replace("(Clone)", "").Trim();

        /// <summary>Mean perceived brightness (0..1, gamma) of the middle third of a shot.</summary>
        public static float Brightness(Texture2D tex)
        {
            if (tex == null) return -1f;
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            double sum = 0; int n = 0;
            for (int y = h / 3; y < 2 * h / 3; y += 3)
                for (int x = w / 3; x < 2 * w / 3; x += 3)
                {
                    var c = px[y * w + x];
                    sum += (0.299 * c.r + 0.587 * c.g + 0.114 * c.b) / 255.0;
                    n++;
                }
            return n > 0 ? (float)(sum / n) : 0f;
        }
    }
}
