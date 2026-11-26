using Deepbound.Classes;
using Deepbound.Progression;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deepbound.Art
{
    public static class Regalia
    {
        private const float ShoulderHeight = 0.5f, ShoulderSpan = 0.24f;

        public static void Dress(HumanoidRig rig, ClassId cls, int index)
        {
            if (rig == null || rig.Torso == null) return;
            var item = Cosmetics.RegaliaFor(cls, index);
            if (item == null) return;
            var root = MeshKit.Pivot(rig.Torso, "Regalia", Vector3.zero);
            var it = item.Value;
            switch (cls)
            {
                case ClassId.Knight: Mantle(root, it); break;
                case ClassId.Mage: Sigils(root, it); break;
                case ClassId.Cleric: Halo(rig.Head != null ? rig.Head : root, it); break;
                case ClassId.Rogue: Scarf(root, it); break;
                case ClassId.Ranger: Feathers(root, it); break;
                case ClassId.Flagellant: Thorns(rig.Head != null ? rig.Head : root, it); break;
                case ClassId.PlagueDoctor: Censer(root, it); break;
            }
        }

        private static GameObject Tinted(Transform parent, string name, Mesh mesh, string mat, Vector3 at, Vector3 euler, Vector3 scale, Color tint)
        {
            var go = MeshKit.Part(parent, name, mesh, mat, at, euler, scale);
            ArtLibrary.Tint(go.GetComponent<Renderer>(), tint);
            return go;
        }

        private static GameObject Glowing(Transform parent, string name, Mesh mesh, Vector3 at, Vector3 euler, Vector3 scale, Color glow)
        {
            var go = MeshKit.Part(parent, name, mesh, Mat.Glow, at, euler, scale);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            ArtLibrary.SetGlow(r, glow);
            return go;
        }

        private static void Mantle(Transform root, Cosmetics.Item it)
        {
            string trim = it.Gilded ? Mat.Gold : Mat.Steel;
            for (int s = -1; s <= 1; s += 2)
            {
                var at = new Vector3(s * (ShoulderSpan + 0.06f), ShoulderHeight + 0.02f, 0f);
                Tinted(root, "Pauldron", MeshKit.Ball(0.15f, 10, 6), trim, at, new Vector3(0f, 0f, s * -18f), new Vector3(1.15f, 0.7f, 1.05f), it.Trim);
                Tinted(root, "PauldronRim", MeshKit.Cylinder(0.16f, 0.17f, 0.03f, 12), Mat.Gold, at + new Vector3(s * 0.03f, -0.07f, 0f), new Vector3(0f, 0f, s * -18f), Vector3.one, it.Gilded ? new Color(1f, 0.85f, 0.4f) : it.Trim * 1.2f);
            }
            Tinted(root, "Tabard", MeshKit.Box(new Vector3(0.36f, 0.62f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.2f, -0.17f), new Vector3(6f, 0f, 0f), Vector3.one, it.Cloak);
            Tinted(root, "TabardCrest", MeshKit.Box(new Vector3(0.12f, 0.16f, 0.01f)), Mat.Gold, new Vector3(0f, 0.3f, -0.185f), new Vector3(6f, 0f, 45f), Vector3.one, it.Trim);
        }

        private static void Sigils(Transform root, Cosmetics.Item it)
        {
            var ring = MeshKit.Pivot(root, "SigilRing", new Vector3(0f, ShoulderHeight + 0.42f, -0.05f));
            ring.gameObject.AddComponent<RegaliaSpin>().DegreesPerSecond = 40f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(i * 2.1f) * 0.05f, Mathf.Sin(a) * 0.42f);
                Glowing(ring, "Sigil", MeshKit.Box(new Vector3(0.07f, 0.1f, 0.02f)), at, new Vector3(0f, -i * 120f, 45f), Vector3.one, it.Cloak * 1.8f);
            }
        }

        private static void Halo(Transform head, Cosmetics.Item it)
        {
            var go = Glowing(head, "Halo", Shapes.Hoop(0.06f), new Vector3(0f, 0.2f, -0.2f), new Vector3(80f, 0f, 0f), Vector3.one * 0.22f, it.Cloak * 1.6f);
            go.AddComponent<RegaliaSpin>().DegreesPerSecond = 12f;
        }

        private static void Scarf(Transform root, Cosmetics.Item it)
        {
            Tinted(root, "Wrap", MeshKit.Cylinder(0.15f, 0.17f, 0.09f, 12), Mat.Cloth, new Vector3(0f, ShoulderHeight + 0.08f, 0f), Vector3.zero, Vector3.one, it.Cloak);
            for (int s = -1; s <= 1; s += 2)
            {
                var tail = MeshKit.Pivot(root, "Tail", new Vector3(s * 0.06f, ShoulderHeight + 0.06f, -0.15f));
                tail.localRotation = Quaternion.Euler(28f, s * 12f, 0f);
                Tinted(tail, "Cloth", MeshKit.Box(new Vector3(0.09f, 0.7f, 0.015f)), Mat.Cloth, new Vector3(0f, -0.35f, 0f), Vector3.zero, Vector3.one, it.Cloak);
                Tinted(tail, "Hem", MeshKit.Box(new Vector3(0.1f, 0.04f, 0.02f)), Mat.Cloth, new Vector3(0f, -0.7f, 0f), Vector3.zero, Vector3.one, it.Trim);
            }
        }

        private static void Feathers(Transform root, Cosmetics.Item it)
        {
            for (int s = -1; s <= 1; s += 2)
                for (int i = 0; i < 5; i++)
                {
                    var at = new Vector3(s * (0.1f + i * 0.05f), ShoulderHeight + 0.04f - i * 0.02f, -0.06f - i * 0.015f);
                    var euler = new Vector3(20f + i * 6f, s * (10f + i * 8f), s * (60f + i * 10f));
                    var colour = i % 2 == 0 ? it.Cloak : it.Trim;
                    Tinted(root, "Feather", MeshKit.Ball(0.06f, 6, 4), Mat.Cloth, at, euler, new Vector3(0.45f, 2.6f, 0.18f), colour);
                }
        }

        private static void Thorns(Transform head, Cosmetics.Item it)
        {
            var crown = MeshKit.Pivot(head, "ThornCrown", new Vector3(0f, 0.26f, 0f));
            Tinted(crown, "Band", MeshKit.Cylinder(0.15f, 0.15f, 0.03f, 14), Mat.Wood, Vector3.zero, Vector3.zero, Vector3.one, it.Cloak);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var thorn = Tinted(crown, "Thorn", MeshKit.Cylinder(0.012f, 0f, 0.09f, 5), Mat.Wood, dir * 0.15f, new Vector3(0f, a, 0f) + new Vector3(-55f, 0f, 0f), Vector3.one, it.Trim);
                if (it.Glows && i % 3 == 0) Glowing(crown, "Blood", MeshKit.Ball(0.012f, 5, 4), dir * 0.155f + Vector3.down * 0.03f, Vector3.zero, Vector3.one, it.Trim * 1.4f);
                thorn.name = "Thorn" + i;
            }
        }

        private static void Censer(Transform root, Cosmetics.Item it)
        {
            var hang = MeshKit.Pivot(root, "Censer", new Vector3(0.24f, 0.02f, 0.05f));
            hang.gameObject.AddComponent<RegaliaSwing>();
            Tinted(hang, "Chain", MeshKit.Cylinder(0.006f, 0.006f, 0.22f, 4), Mat.Steel, new Vector3(0f, -0.11f, 0f), Vector3.zero, Vector3.one, it.Cloak * 0.8f);
            Tinted(hang, "Bowl", MeshKit.Ball(0.07f, 10, 7), Mat.Gold, new Vector3(0f, -0.27f, 0f), Vector3.zero, new Vector3(1f, 0.8f, 1f), it.Cloak);
            Tinted(hang, "Lid", MeshKit.Cylinder(0.05f, 0.01f, 0.07f, 10), Mat.Gold, new Vector3(0f, -0.2f, 0f), Vector3.zero, Vector3.one, it.Cloak);
            Glowing(hang, "Ember", MeshKit.Ball(0.035f, 6, 4), new Vector3(0f, -0.25f, 0f), Vector3.zero, Vector3.one, it.Trim * 1.5f);
        }
    }
}
