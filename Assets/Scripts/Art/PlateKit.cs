using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Art
{
    public static class PlateKit
    {
        public struct Look
        {
            public Color Plate, Trim, Glow;

            public bool ClosedHelm;
            public bool Horns;

            public bool NoHelm;
        }

        public static List<Renderer> Armour(HumanoidRig rig, Look look)
        {
            var glow = new List<Renderer>();
            if (rig == null || rig.Torso == null || rig.Head == null) return glow;
            Renderer Plate(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, look.Plate); return r; }
            Renderer Trim(GameObject g) { var r = g.GetComponent<Renderer>(); ArtLibrary.Tint(r, look.Trim); return r; }
            Renderer Glow(GameObject g, float k) { var r = g.GetComponent<Renderer>(); ArtLibrary.SetGlow(r, look.Glow * k); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; glow.Add(r); return r; }

            var t = rig.Torso;

            Plate(MeshKit.Part(t, "Cuirass", MeshKit.Lathe("plateCuirass", new[]
            {
                new Vector2(0.17f, 0.0f), new Vector2(0.2f, 0.08f), new Vector2(0.23f, 0.3f), new Vector2(0.26f, 0.45f), new Vector2(0.23f, 0.57f), new Vector2(0.1f, 0.64f),
            }, 12), Mat.Steel, new Vector3(0f, 0f, 0.01f), Vector3.zero, new Vector3(1.05f, 1f, 0.8f)));
            Trim(MeshKit.Part(t, "CuirassRim", MeshKit.Cylinder(0.205f, 0.205f, 0.035f, 12), Mat.Gold, new Vector3(0f, 0.0f, 0.01f), Vector3.zero, new Vector3(1.05f, 1f, 0.8f)));
            Trim(MeshKit.Part(t, "CuirassRidge", MeshKit.Box(new Vector3(0.03f, 0.42f, 0.03f)), Mat.Gold, new Vector3(0f, 0.3f, 0.205f), new Vector3(-8f, 0f, 0f)));
            Plate(MeshKit.Part(t, "Gorget", MeshKit.Cylinder(0.14f, 0.1f, 0.09f, 10), Mat.Steel, new Vector3(0f, 0.58f, 0f)));

            Glow(MeshKit.Part(t, "Wound", MeshKit.Box(new Vector3(0.05f, 0.16f, 0.02f)), Mat.Glow, new Vector3(-0.06f, 0.4f, 0.2f), new Vector3(-6f, 0f, 24f)), 1.6f);
            Glow(MeshKit.Part(t, "WoundB", MeshKit.Box(new Vector3(0.025f, 0.1f, 0.02f)), Mat.Glow, new Vector3(0.0f, 0.33f, 0.205f), new Vector3(-6f, 0f, -30f)), 1.3f);

            if (rig.Hips != null)
            {
                MeshKit.Part(rig.Hips, "WarBelt", MeshKit.Cylinder(0.21f, 0.21f, 0.07f, 12), Mat.Leather, new Vector3(0f, -0.04f, 0f), Vector3.zero, new Vector3(1.05f, 1f, 0.82f));
                for (int i = 0; i < 7; i++)
                {
                    float a = (-90f + i * 30f) * Mathf.Deg2Rad;
                    var at = new Vector3(Mathf.Sin(a) * 0.2f, -0.15f, Mathf.Cos(a) * 0.17f);
                    Plate(MeshKit.Part(rig.Hips, "Tasset", MeshKit.Box(new Vector3(0.12f, 0.2f, 0.025f)), Mat.Steel, at, new Vector3(-14f, a * Mathf.Rad2Deg, 0f)));
                }
                Trim(MeshKit.Part(rig.Hips, "BeltBuckle", MeshKit.Box(new Vector3(0.08f, 0.07f, 0.03f)), Mat.Gold, new Vector3(0f, -0.005f, 0.18f)));
            }

            foreach (var arm in new[] { rig.ArmL, rig.ArmR })
            {
                if (arm == null) continue;
                float s = arm == rig.ArmL ? -1f : 1f;
                Plate(MeshKit.Part(arm, "Pauldron", MeshKit.Lathe("platePauldron", new[]
                {
                    new Vector2(0.21f, -0.09f), new Vector2(0.21f, 0f), new Vector2(0.17f, 0.08f), new Vector2(0.09f, 0.13f), new Vector2(0f, 0.145f),
                }, 10), Mat.Steel, new Vector3(0.03f * s, 0.02f, 0f), new Vector3(0f, 0f, -18f * s), new Vector3(1f, 1f, 1.1f)));
                Trim(MeshKit.Part(arm, "PauldronRim", MeshKit.Cylinder(0.215f, 0.215f, 0.025f, 10), Mat.Gold, new Vector3(0.03f * s, -0.09f, 0f), new Vector3(0f, 0f, -18f * s), new Vector3(1f, 1f, 1.1f)));
                Plate(MeshKit.Part(arm, "PauldronLame", MeshKit.Cylinder(0.17f, 0.2f, 0.06f, 10), Mat.Steel, new Vector3(0.05f * s, -0.17f, 0f), new Vector3(0f, 0f, -14f * s)));
                for (int k = 0; k < 3; k++)
                    Plate(MeshKit.Part(arm, "PauldronSpike", MeshKit.Lathe("pauldronSpike", new[] { new Vector2(0.035f, 0f), new Vector2(0.02f, 0.1f), new Vector2(0f, 0.22f) }, 5), Mat.Steel,
                        new Vector3(0.09f * s, 0.08f, -0.08f + k * 0.08f), new Vector3((k - 1) * -14f, 0f, -38f * s)));
                Plate(MeshKit.Part(arm, "Rerebrace", MeshKit.Cylinder(0.075f, 0.085f, 0.14f, 8), Mat.Steel, new Vector3(0f, -0.3f, 0f)));
                Plate(MeshKit.Part(arm, "Couter", MeshKit.Ball(0.065f, 8, 5), Mat.Steel, new Vector3(0f, -0.33f, -0.01f)));
                Plate(MeshKit.Part(arm, "Vambrace", MeshKit.Cylinder(0.095f, 0.07f, 0.22f, 8), Mat.Steel, new Vector3(0f, -0.59f, 0f)));
                Trim(MeshKit.Part(arm, "VambraceCuff", MeshKit.Cylinder(0.1f, 0.1f, 0.025f, 8), Mat.Gold, new Vector3(0f, -0.59f, 0f)));
                Plate(MeshKit.Part(arm, "Gauntlet", MeshKit.Box(new Vector3(0.11f, 0.09f, 0.07f)), Mat.Steel, new Vector3(0f, -0.63f, 0.005f)));
            }

            foreach (var leg in new[] { rig.LegL, rig.LegR })
            {
                if (leg == null) continue;
                Plate(MeshKit.Part(leg, "Cuisse", MeshKit.Cylinder(0.08f, 0.1f, 0.3f, 8), Mat.Steel, new Vector3(0f, -0.4f, 0.005f)));
                Plate(MeshKit.Part(leg, "Poleyn", MeshKit.Ball(0.07f, 8, 5), Mat.Steel, new Vector3(0f, -0.44f, 0.035f)));
                Trim(MeshKit.Part(leg, "PoleynFan", MeshKit.Box(new Vector3(0.1f, 0.06f, 0.015f)), Mat.Gold, new Vector3(0f, -0.44f, 0.1f)));
                Plate(MeshKit.Part(leg, "Greave", MeshKit.Cylinder(0.075f, 0.09f, 0.32f, 8), Mat.Steel, new Vector3(0f, -0.83f, 0f)));
                Plate(MeshKit.Part(leg, "Sabaton", MeshKit.Box(new Vector3(0.12f, 0.075f, 0.22f)), Mat.Steel, new Vector3(0f, -0.855f, 0.05f)));
            }

            var h = rig.Head;
            if (look.NoHelm) { }
            else if (look.ClosedHelm)
            {
                Plate(MeshKit.Part(h, "GreatHelm", MeshKit.Lathe("plateGreatHelm", new[]
                {
                    new Vector2(0.15f, -0.04f), new Vector2(0.165f, 0.1f), new Vector2(0.16f, 0.21f), new Vector2(0.11f, 0.29f), new Vector2(0f, 0.31f),
                }, 12), Mat.Steel, Vector3.zero));

                MeshKit.Part(h, "VisorSlit", MeshKit.Box(new Vector3(0.2f, 0.035f, 0.02f)), Mat.Dark, new Vector3(0f, 0.13f, 0.158f));
                foreach (float x in new[] { -0.045f, 0.045f })
                    Glow(MeshKit.Part(h, "SlitEye", MeshKit.Box(new Vector3(0.045f, 0.014f, 0.01f)), Mat.Glow, new Vector3(x, 0.13f, 0.168f)), 2f);
                for (int i = 0; i < 4; i++)
                    MeshKit.Part(h, "Breath", MeshKit.Box(new Vector3(0.012f, 0.06f, 0.012f)), Mat.Dark, new Vector3(-0.045f + i * 0.03f, 0.04f, 0.163f));
                Trim(MeshKit.Part(h, "HelmCross", MeshKit.Box(new Vector3(0.025f, 0.24f, 0.02f)), Mat.Gold, new Vector3(0f, 0.12f, 0.17f)));
            }
            else
            {
                Plate(MeshKit.Part(h, "WarHelm", MeshKit.Lathe("plateOpenHelm", new[]
                {
                    new Vector2(0.15f, 0.02f), new Vector2(0.162f, 0.12f), new Vector2(0.155f, 0.21f), new Vector2(0.1f, 0.28f), new Vector2(0f, 0.3f),
                }, 12, true, 300f), Mat.Steel, Vector3.zero, new Vector3(0f, 210f, 0f)));
                Trim(MeshKit.Part(h, "Brow", MeshKit.Box(new Vector3(0.2f, 0.035f, 0.04f)), Mat.Gold, new Vector3(0f, 0.19f, 0.14f), new Vector3(-10f, 0f, 0f)));

                foreach (float s in new[] { -1f, 1f })
                    Plate(MeshKit.Part(h, "Cheek", MeshKit.Box(new Vector3(0.03f, 0.12f, 0.1f)), Mat.Steel, new Vector3(s * 0.1f, 0.06f, 0.11f), new Vector3(0f, s * -18f, 0f)));
                foreach (float x in new[] { -0.045f, 0.045f })
                    Glow(MeshKit.Part(h, "BurnEye", MeshKit.Ball(0.017f, 6, 4), Mat.Glow, new Vector3(x, 0.128f, 0.112f)), 2f);
            }
            if (look.Horns)
                foreach (float s in new[] { -1f, 1f })
                {
                    var a = MeshKit.Pivot(h, "Horn", new Vector3(s * 0.14f, 0.2f, -0.01f), new Vector3(-10f, 0f, -72f * s));
                    Plate(MeshKit.Part(a, "HornA", MeshKit.Cylinder(0.05f, 0.04f, 0.17f, 7), Mat.Steel, Vector3.zero));
                    var b = MeshKit.Pivot(a, "HornB", new Vector3(0f, 0.16f, 0f), new Vector3(0f, 0f, 48f * s));
                    Plate(MeshKit.Part(b, "HornB", MeshKit.Cylinder(0.04f, 0.026f, 0.17f, 7), Mat.Steel, Vector3.zero));
                    var c = MeshKit.Pivot(b, "HornC", new Vector3(0f, 0.16f, 0f), new Vector3(28f, 0f, 30f * s));
                    Trim(MeshKit.Part(c, "HornTip", MeshKit.Cylinder(0.026f, 0f, 0.16f, 6), Mat.Gold, Vector3.zero));
                }
            return glow;
        }
    }
}
