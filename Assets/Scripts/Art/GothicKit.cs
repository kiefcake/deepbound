using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Shared gothic detailing for the player classes: cloaks, collars, plate pieces,
    /// circlets. Each class builder in <see cref="CharacterArt"/> layers these on, so the
    /// whole party reads as one dark-medieval cast rather than five toys.
    /// </summary>
    public static class GothicKit
    {
        /// <summary>A cosmetic dye in effect while a player's body is being built (null = the class's own).</summary>
        public static Color? CloakOverride, TrimOverride;

        /// <summary>The cosmetic pattern sewn on the back while a player's body is being built (null = plain).</summary>
        public static Progression.Cosmetics.Item? PatternOverride;

        /// <summary>
        /// A cloak hung from the shoulders: a curved sheet behind the body that flares toward
        /// the hem, with gold clasps and (optionally) a trim band or a ragged, tattered hem.
        /// </summary>
        public static void Cloak(Transform torso, Color color, float length, float flare, Color? trim = null, bool tattered = false)
        {
            if (CloakOverride.HasValue)
            {
                color = CloakOverride.Value;
                trim = TrimOverride ?? trim;
            }
            float top = 0.56f, bottom = top - length;
            // Everything below hangs from a pivot at the shoulders so the cloak can move (CloakSway).
            var hang = MeshKit.Pivot(torso, "CloakHang", new Vector3(0f, top, -0.02f));
            var drop = new Vector3(0f, -top, 0.02f);
            var sheet = MeshKit.Part(hang, "Cloak", MeshKit.Lathe("cloak" + length.ToString("F2") + flare.ToString("F2"), new[]
            {
                new Vector2(0.27f, top), new Vector2(0.3f, top - 0.15f), new Vector2(0.31f + flare * 0.4f, top - length * 0.5f), new Vector2(0.3f + flare, bottom),
            }, 10, true, 170f), Mat.Cloth, drop, new Vector3(0f, 95f, 0f), new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(sheet.GetComponent<Renderer>(), color);
            // The lathe is a surface of revolution; flip normals isn't possible, so add a
            // second, slightly smaller sheet facing in — the cloak has an inside.
            var lining = MeshKit.Part(hang, "CloakLining", MeshKit.Lathe("cloakIn" + length.ToString("F2") + flare.ToString("F2"), new[]
            {
                new Vector2(0.3f + flare - 0.015f, bottom), new Vector2(0.31f + flare * 0.4f - 0.015f, top - length * 0.5f), new Vector2(0.285f, top - 0.15f), new Vector2(0.255f, top),
            }, 10, true, 170f), Mat.Cloth, drop, new Vector3(0f, 95f, 0f), new Vector3(1f, 1f, 0.82f));
            ArtLibrary.Tint(lining.GetComponent<Renderer>(), color * 0.55f);

            if (trim.HasValue)
            {
                var band = MeshKit.Part(hang, "CloakTrim", MeshKit.Lathe("cloakTrim" + length.ToString("F2") + flare.ToString("F2"), new[]
                {
                    new Vector2(0.3f + flare + 0.005f, bottom), new Vector2(0.3f + flare - 0.02f, bottom + 0.07f),
                }, 10, true, 170f), Mat.Cloth, drop, new Vector3(0f, 95f, 0f), new Vector3(1f, 1f, 0.82f));
                ArtLibrary.Tint(band.GetComponent<Renderer>(), trim.Value);
            }
            if (tattered)
            {
                // Ragged tongues of cloth below the hem.
                for (int i = 0; i < 7; i++)
                {
                    float a = (115f + i * 22f) * Mathf.Deg2Rad;
                    float r = 0.3f + flare;
                    var p = new Vector3(Mathf.Cos(a) * r, bottom - 0.06f - (i % 2) * 0.05f, -Mathf.Abs(Mathf.Sin(a)) * r * 0.82f);
                    var tongue = MeshKit.Part(hang, "Rag" + i, MeshKit.Box(new Vector3(0.09f, 0.14f + (i % 3) * 0.04f, 0.015f)), Mat.Cloth, p + drop,
                        new Vector3(8f, Mathf.Atan2(p.x, -p.z) * Mathf.Rad2Deg, (i % 2 == 0 ? 6f : -6f)));
                    ArtLibrary.Tint(tongue.GetComponent<Renderer>(), color * 0.9f);
                }
            }
            if (PatternOverride.HasValue && PatternOverride.Value.Motif != Progression.Cosmetics.Motif.None)
                SewPattern(hang, drop, top, length, flare, PatternOverride.Value);
            hang.gameObject.AddComponent<CloakSway>();
            foreach (float side in new[] { -1f, 1f })
                MeshKit.Part(torso, "Clasp", MeshKit.Ball(0.035f, 6, 4), Mat.Gold, new Vector3(0.17f * side, 0.55f, 0.13f));
            MeshKit.Part(torso, "ClaspChain", MeshKit.Box(new Vector3(0.32f, 0.012f, 0.012f)), Mat.Gold, new Vector3(0f, 0.53f, 0.15f));
        }

        // ---- Cloak patterns (2026-10-21) ----
        // The cloak is a lathe sheet; these find a point on its outside (y in the lathe's profile space,
        // phi = degrees round from the back centre) and a frame there whose +Z points out of the cloth,
        // so flat motifs lie on the curve and sway with it.

        private static float CloakRadius(float y, float top, float length, float flare)
        {
            float bottom = top - length;
            float[] ys = { top, top - 0.15f, top - length * 0.5f, bottom };
            float[] rs = { 0.27f, 0.3f, 0.31f + flare * 0.4f, 0.3f + flare };
            for (int i = 0; i < 3; i++)
                if (y <= ys[i] && y >= ys[i + 1]) return Mathf.Lerp(rs[i], rs[i + 1], Mathf.InverseLerp(ys[i], ys[i + 1], y));
            return y > top ? rs[0] : rs[3];
        }

        private static Vector3 CloakPoint(Vector3 drop, float y, float phi, float top, float length, float flare, float lift)
        {
            float r = CloakRadius(y, top, length, flare) + lift;
            float a = phi * Mathf.Deg2Rad;
            return drop + new Vector3(Mathf.Sin(a) * r, y, -Mathf.Cos(a) * r * 0.82f);
        }

        /// <summary>A pivot on the cloak's outside, +Z out of the cloth, +Y up along it.</summary>
        private static Transform OnCloak(Transform hang, Vector3 drop, float y, float phi, float top, float length, float flare, string name, float roll = 0f)
        {
            var p = CloakPoint(drop, y, phi, top, length, flare, 0.014f);
            var up = CloakPoint(drop, y + 0.02f, phi, top, length, flare, 0.014f) - p;
            var side = CloakPoint(drop, y, phi + 2f, top, length, flare, 0.014f) - p;
            var normal = Vector3.Cross(up, side).normalized;
            var horiz = new Vector3(p.x - drop.x, 0f, p.z - drop.z);
            if (Vector3.Dot(normal, horiz) < 0f) normal = -normal;
            var t = MeshKit.Pivot(hang, name, p);
            t.localRotation = Quaternion.LookRotation(normal, up.normalized) * Quaternion.Euler(0f, 0f, roll);
            return t;
        }

        private static void Paint(GameObject part, Progression.Cosmetics.Item pat, bool socket)
        {
            var r = part.GetComponent<Renderer>();
            if (socket) { ArtLibrary.Tint(r, pat.Trim); return; }
            if (pat.Glows) ArtLibrary.SetGlow(r, pat.Cloak * 1.6f);
            else ArtLibrary.Tint(r, pat.Cloak);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static string MotifMat(Progression.Cosmetics.Item pat) => pat.Glows ? Mat.Glow : pat.Gilded ? Mat.Gold : Mat.Bone;

        /// <summary>A flat skull (+Z out): cranium, cheekbones, jaw with teeth, two sockets and a nose.</summary>
        private static void FlatSkull(Transform at, float s, Progression.Cosmetics.Item pat)
        {
            string m = MotifMat(pat);
            Paint(MeshKit.Part(at, "PatSkull", MeshKit.Ball(0.5f * s, 10, 6), m, new Vector3(0f, 0.12f * s, 0f), Vector3.zero, new Vector3(1f, 0.92f, 0.16f)), pat, false);
            Paint(MeshKit.Part(at, "PatCheek", MeshKit.Box(new Vector3(0.72f * s, 0.3f * s, 0.05f * s)), m, new Vector3(0f, -0.22f * s, 0f)), pat, false);
            Paint(MeshKit.Part(at, "PatJaw", MeshKit.Box(new Vector3(0.5f * s, 0.2f * s, 0.05f * s)), m, new Vector3(0f, -0.42f * s, 0f)), pat, false);
            foreach (float side in new[] { -1f, 1f })
                Paint(MeshKit.Part(at, "PatEye", MeshKit.Ball(0.15f * s, 8, 5), Mat.Dark, new Vector3(0.19f * side, -0.02f * s, 0.06f * s), Vector3.zero, new Vector3(1f, 1.1f, 0.25f)), pat, true);
            Paint(MeshKit.Part(at, "PatNose", MeshKit.Box(new Vector3(0.09f * s, 0.09f * s, 0.02f * s)), Mat.Dark, new Vector3(0f, -0.2f * s, 0.035f * s), new Vector3(0f, 0f, 45f)), pat, true);
            for (int i = -1; i <= 1; i++)
                Paint(MeshKit.Part(at, "PatTooth", MeshKit.Box(new Vector3(0.025f * s, 0.16f * s, 0.02f * s)), Mat.Dark, new Vector3(i * 0.12f * s, -0.42f * s, 0.03f * s)), pat, true);
        }

        /// <summary>A flat bone along local Y: a shaft and a double knuckle at each end.</summary>
        private static void FlatBone(Transform at, string name, Vector3 pos, float roll, float len, float t, Progression.Cosmetics.Item pat)
        {
            string m = MotifMat(pat);
            var b = MeshKit.Pivot(at, name, pos, new Vector3(0f, 0f, roll));
            Paint(MeshKit.Part(b, "PatShaft", MeshKit.Box(new Vector3(t, len, t * 0.3f)), m, Vector3.zero), pat, false);
            foreach (float end in new[] { -1f, 1f })
                foreach (float side in new[] { -1f, 1f })
                    Paint(MeshKit.Part(b, "PatKnob", MeshKit.Ball(t * 0.72f, 7, 4), m, new Vector3(side * t * 0.5f, end * len * 0.5f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.35f)), pat, false);
        }

        private static void SewPattern(Transform hang, Vector3 drop, float top, float length, float flare, Progression.Cosmetics.Item pat)
        {
            float bottom = top - length;
            switch (pat.Motif)
            {
                case Progression.Cosmetics.Motif.Skull:
                {
                    float s = Mathf.Clamp(length * 0.3f, 0.24f, 0.34f);
                    FlatSkull(OnCloak(hang, drop, top - 0.2f - s * 0.6f, 0f, top, length, flare, "PatternSkull"), s, pat);
                    break;
                }
                case Progression.Cosmetics.Motif.Crossbones:
                {
                    float s = Mathf.Clamp(length * 0.22f, 0.19f, 0.26f);
                    float y = top - 0.18f - s * 0.6f;
                    FlatSkull(OnCloak(hang, drop, y, 0f, top, length, flare, "PatternSkull"), s, pat);
                    // The bones cross behind and below the skull, their knuckles showing at the corners.
                    var cross = OnCloak(hang, drop, y - s * 0.55f, 0f, top, length, flare, "PatternBones");
                    FlatBone(cross, "BoneA", new Vector3(0f, 0f, -0.006f), 52f, s * 1.75f, s * 0.13f, pat);
                    FlatBone(cross, "BoneB", new Vector3(0f, 0f, -0.006f), -52f, s * 1.75f, s * 0.13f, pat);
                    break;
                }
                case Progression.Cosmetics.Motif.BonePrint:
                {
                    // Little bones strewn in staggered rows over the whole back, alternating their tilt.
                    int row = 0;
                    for (float y = top - 0.2f; y > bottom + 0.1f; y -= 0.16f, row++)
                        for (float phi = -66f + (row % 2) * 13f; phi <= 66f; phi += 26f)
                        {
                            float roll = ((row + Mathf.RoundToInt(phi)) % 2 == 0 ? 38f : -38f) + (row * 17 % 11) - 5f;
                            FlatBone(OnCloak(hang, drop, y, phi, top, length, flare, "PatternPrint"), "Bone", Vector3.zero, roll, 0.085f, 0.016f, pat);
                        }
                    break;
                }
                case Progression.Cosmetics.Motif.Ribcage:
                {
                    // A spine down the back and ribs curving round from it.
                    float spineEnd = top - Mathf.Min(length * 0.72f, 0.85f);
                    for (float y = top - 0.12f; y > spineEnd; y -= 0.055f)
                        Paint(MeshKit.Part(OnCloak(hang, drop, y, 0f, top, length, flare, "PatternSpine"), "PatVert", MeshKit.Box(new Vector3(0.05f, 0.036f, 0.012f)), MotifMat(pat), Vector3.zero), pat, false);
                    for (int k = 0; k < 6; k++)
                    {
                        float y = top - 0.18f - k * 0.075f;
                        float reach = 46f - Mathf.Abs(k - 2f) * 5f;
                        foreach (float side in new[] { -1f, 1f })
                            for (int j = 0; j < 4; j++)
                            {
                                float phi = side * (7f + j * reach / 4f);
                                // Each rib droops as it wraps round.
                                var seg = OnCloak(hang, drop, y - j * j * 0.006f, phi, top, length, flare, "PatternRib", side * (78f - j * 9f));
                                Paint(MeshKit.Part(seg, "PatRib", MeshKit.Box(new Vector3(0.02f, 0.075f, 0.01f)), MotifMat(pat), Vector3.zero), pat, false);
                            }
                    }
                    // The pelvis at the base of the spine.
                    var pelvis = OnCloak(hang, drop, spineEnd - 0.03f, 0f, top, length, flare, "PatternPelvis");
                    foreach (float side in new[] { -1f, 1f })
                        Paint(MeshKit.Part(pelvis, "PatHip", MeshKit.Ball(0.06f, 8, 5), MotifMat(pat), new Vector3(side * 0.055f, 0f, 0f), Vector3.zero, new Vector3(1f, 0.7f, 0.2f)), pat, false);
                    break;
                }
                case Progression.Cosmetics.Motif.SkullHem:
                {
                    // A row of little skulls round the hem, with a bone between each pair.
                    float y = bottom + 0.15f;
                    for (float phi = -72f; phi <= 72.5f; phi += 24f)
                    {
                        FlatSkull(OnCloak(hang, drop, y, phi, top, length, flare, "PatternHemSkull"), 0.1f, pat);
                        if (phi < 72f) FlatBone(OnCloak(hang, drop, y - 0.01f, phi + 12f, top, length, flare, "PatternHemBone"), "Bone", Vector3.zero, 90f, 0.07f, 0.014f, pat);
                    }
                    break;
                }
            }
        }

        /// <summary>A tall standing collar rising behind the head (the sorcerer's silhouette).</summary>
        public static void HighCollar(Transform torso, Color color, float height = 0.22f)
        {
            var collar = MeshKit.Part(torso, "HighCollar", MeshKit.Lathe("highCollar" + height.ToString("F2"), new[]
            {
                new Vector2(0.13f, 0.58f), new Vector2(0.19f, 0.58f + height * 0.6f), new Vector2(0.24f, 0.58f + height),
            }, 10, true, 200f), Mat.Cloth, Vector3.zero, new Vector3(0f, 80f, 0f));
            ArtLibrary.Tint(collar.GetComponent<Renderer>(), color);
        }

        /// <summary>Plate gorget at the neck and tiered faulds below the belt.</summary>
        public static void PlateDetails(Transform torso, Transform hips, Color steelTint)
        {
            var gorget = MeshKit.Part(torso, "Gorget", MeshKit.Cylinder(0.16f, 0.12f, 0.1f, 10), Mat.Steel, new Vector3(0f, 0.58f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.85f));
            ArtLibrary.Tint(gorget.GetComponent<Renderer>(), steelTint);
            for (int i = 0; i < 3; i++)
            {
                var fauld = MeshKit.Part(hips, "Fauld" + i, MeshKit.Cylinder(0.24f + i * 0.02f, 0.25f + i * 0.025f, 0.08f, 10), Mat.Steel,
                    new Vector3(0f, -0.04f - i * 0.075f, 0f), Vector3.zero, new Vector3(1f, 1f, 0.8f));
                ArtLibrary.Tint(fauld.GetComponent<Renderer>(), steelTint * (1f - i * 0.06f));
            }
            // Fluting: three raised ridges fanning up the breastplate, the gothic armourer's mark.
            for (int i = -1; i <= 1; i++)
                MeshKit.Part(torso, "Flute" + i, MeshKit.Box(new Vector3(0.018f, 0.36f, 0.02f)), Mat.Gold, new Vector3(i * 0.07f, 0.3f, 0.215f), new Vector3(-8f, 0f, i * -9f));
        }

        /// <summary>A circlet with a glowing brow gem. Returns the gem so it can pulse with the rig.</summary>
        public static Renderer Circlet(Transform head, float radius, float height, Color gem)
        {
            MeshKit.Part(head, "Circlet", MeshKit.Cylinder(radius, radius, 0.03f, 12), Mat.Steel, new Vector3(0f, height, 0f), Vector3.zero, new Vector3(1f, 1f, 0.95f));
            for (int i = -2; i <= 2; i++)
                MeshKit.Part(head, "CircletSpike" + i, MeshKit.Lathe("circletSpike", new[] { new Vector2(0.012f, 0f), new Vector2(0f, 0.06f) }, 4), Mat.Steel,
                    Quaternion.Euler(0f, i * 24f, 0f) * new Vector3(0f, height + 0.015f, radius), Vector3.zero);
            var g = MeshKit.Part(head, "BrowGem", MeshKit.Ball(0.022f, 6, 4), Mat.Glow, new Vector3(0f, height, radius + 0.01f));
            ArtLibrary.SetGlow(g.GetComponent<Renderer>(), gem * 1.6f);
            return g.GetComponent<Renderer>();
        }

        /// <summary>Tint every steel part under a root: blackened plate rather than chrome.</summary>
        public static void BlackenSteel(Transform root, Color tint)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith(Mat.Steel)) ArtLibrary.Tint(r, tint);
        }
    }
}
