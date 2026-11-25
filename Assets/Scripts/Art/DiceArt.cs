using UnityEngine;
using Deepbound.UI;

namespace Deepbound.Art
{
    public static class DiceArt
    {
        private static readonly Vector2[][] Pips =
        {
            new Vector2[0],
            new[] { Vector2.zero },
            new[] { new Vector2(-1, -1), new Vector2(1, 1) },
            new[] { new Vector2(-1, -1), Vector2.zero, new Vector2(1, 1) },
            new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) },
            new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1), Vector2.zero },
            new[] { new Vector2(-1, -1), new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(1, 0), new Vector2(1, 1) },
        };

        public static Vector2[] PipsOf(int value) => Pips[Mathf.Clamp(value, 0, 6)];

        public static Transform Build(Transform parent, string name, float size, Color? tint = null, bool red = false)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var body = MeshKit.Part(root, "Body", MeshKit.Box(Vector3.one * size), Mat.Bone, Vector3.zero);
            if (tint.HasValue) ArtLibrary.Tint(body.GetComponent<Renderer>(), tint.Value);
            float h = size * 0.5f + 0.0015f, step = size * 0.26f;
            var pipMesh = MeshKit.Box(new Vector3(size * 0.17f, size * 0.17f, 0.004f));
            void Face(int value, Vector3 normal, Vector3 u, Vector3 v)
            {
                foreach (var p in Pips[value])
                {
                    var pos = normal * h + (u * p.x + v * p.y) * step;
                    var pip = MeshKit.Part(root, "Pip", pipMesh, red && value == 1 ? Mat.Gold : Mat.Dark, pos, Quaternion.LookRotation(normal, v).eulerAngles);
                    if (red && value == 1) ArtLibrary.Tint(pip.GetComponent<Renderer>(), new Color(0.7f, 0.08f, 0.08f));
                }
            }
            Face(1, Vector3.up, Vector3.right, Vector3.forward);
            Face(6, Vector3.down, Vector3.right, Vector3.forward);
            Face(2, Vector3.forward, Vector3.right, Vector3.up);
            Face(5, Vector3.back, Vector3.right, Vector3.up);
            Face(3, Vector3.right, Vector3.forward, Vector3.up);
            Face(4, Vector3.left, Vector3.forward, Vector3.up);
            MeshKit.MergeParts(root);
            return root;
        }

        public static Quaternion FaceUp(int value, float yaw = 0f)
        {
            var face = value switch
            {
                6 => Quaternion.Euler(180f, 0f, 0f),
                2 => Quaternion.Euler(-90f, 0f, 0f),
                5 => Quaternion.Euler(90f, 0f, 0f),
                3 => Quaternion.Euler(0f, 0f, 90f),
                4 => Quaternion.Euler(0f, 0f, -90f),
                _ => Quaternion.identity,
            };
            return Quaternion.Euler(0f, yaw, 0f) * face;
        }

        public static void DrawHud(Rect r, int value, bool held, bool hidden = false)
        {
            UI.UiSkin.Rect(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), held ? UI.UiSkin.Gold : new Color(0f, 0f, 0f, 0.6f));
            UI.UiSkin.Rect(r, hidden ? new Color(0.25f, 0.2f, 0.18f, 0.95f) : new Color(0.9f, 0.86f, 0.76f, 1f));
            if (hidden) { GUI.Label(r, "?", UiSkin.Derive(UI.UiSkin.Heading, UI.UiSkin.Heading.normal.textColor, fontSize: (int)(r.height * 0.5f), align: TextAnchor.MiddleCenter)); return; }
            float pip = r.width * 0.17f, step = r.width * 0.27f;
            var c = r.center;
            foreach (var p in PipsOf(value))
                UI.UiSkin.Rect(new Rect(c.x + p.x * step - pip / 2f, c.y - p.y * step - pip / 2f, pip, pip), value == 1 ? new Color(0.65f, 0.08f, 0.08f) : new Color(0.1f, 0.07f, 0.06f));
        }
    }
}
