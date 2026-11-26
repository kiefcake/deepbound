using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Player
{
    public sealed class ViewGesture
    {
        private struct Key
        {
            public float T;
            public Vector3 MainPos, MainRot, OffPos, OffRot;
            public float Flare;
        }

        private struct Shape
        {
            public Key[] Keys;
            public float FlareMid, ShakeAt, Shake, Tremble;
        }

        private static Key K(float t, Vector3 mainPos, Vector3 mainRot, Vector3 offPos, Vector3 offRot, float flare = 0f) =>
            new Key { T = t, MainPos = mainPos, MainRot = mainRot, OffPos = offPos, OffRot = offRot, Flare = flare };

        private static readonly Vector3 O = Vector3.zero;

        public static bool Handles(RigAction a) => ShapeOf(a).Keys != null;

        private static Shape ShapeOf(RigAction a)
        {
            switch (a)
            {
                case RigAction.Cast:
                    return new Shape { Keys = new[] {
                        K(0.25f, new Vector3(0f, 0.02f, -0.02f), O, new Vector3(0.08f, 0.06f, -0.05f), new Vector3(-20f, 0f, 0f), 0.4f),
                        K(0.45f, new Vector3(0f, 0.03f, 0.05f), new Vector3(-8f, 0f, 0f), new Vector3(0.14f, 0.09f, 0.28f), new Vector3(-45f, 0f, 10f), 1f),
                        K(0.75f, new Vector3(0f, 0.03f, 0.04f), new Vector3(-6f, 0f, 0f), new Vector3(0.13f, 0.08f, 0.25f), new Vector3(-40f, 0f, 10f), 0.6f) } };
                case RigAction.Thrust:
                    return new Shape { Keys = new[] {
                        K(0.2f, new Vector3(0.02f, 0.05f, -0.08f), new Vector3(15f, 0f, 0f), new Vector3(0.04f, 0.02f, -0.05f), O, 0.3f),
                        K(0.45f, new Vector3(-0.06f, 0.06f, 0.25f), new Vector3(-30f, 0f, 0f), new Vector3(0.12f, 0.06f, 0.25f), new Vector3(-40f, 0f, 0f), 1f),
                        K(0.7f, new Vector3(-0.05f, 0.05f, 0.2f), new Vector3(-25f, 0f, 0f), new Vector3(0.1f, 0.05f, 0.2f), new Vector3(-35f, 0f, 0f), 0.5f) },
                        ShakeAt = 0.45f, Shake = 0.08f };
                case RigAction.Pray:
                    return new Shape { Keys = new[] {
                        K(0.35f, new Vector3(-0.14f, 0.14f, 0.02f), new Vector3(-35f, 0f, -35f), new Vector3(0.14f, 0.14f, 0.02f), new Vector3(-35f, 0f, 35f), 0.8f),
                        K(0.8f, new Vector3(-0.14f, 0.17f, 0.04f), new Vector3(-40f, 0f, -35f), new Vector3(0.14f, 0.17f, 0.04f), new Vector3(-40f, 0f, 35f), 1f) },
                        FlareMid = 0.5f };
                case RigAction.Point:
                    return new Shape { Keys = new[] {
                        K(0.3f, new Vector3(0f, -0.03f, 0f), O, new Vector3(0.12f, 0.16f, 0.3f), new Vector3(-70f, 0f, 0f), 1f),
                        K(0.8f, new Vector3(0f, -0.03f, 0f), O, new Vector3(0.12f, 0.15f, 0.29f), new Vector3(-66f, 0f, 0f), 0.7f) } };
                case RigAction.Siphon:
                    return new Shape { Keys = new[] {
                        K(0.25f, O, O, new Vector3(0.12f, 0.08f, 0.32f), new Vector3(-50f, 0f, 0f), 1f),
                        K(0.85f, new Vector3(0f, 0f, -0.03f), O, new Vector3(0.06f, 0.04f, 0.08f), new Vector3(-30f, 0f, 25f), 0.8f) },
                        Tremble = 0.006f };
                case RigAction.Lob:
                    return new Shape { Keys = new[] {
                        K(0.35f, new Vector3(0.06f, 0.18f, -0.14f), new Vector3(-60f, 0f, 0f), new Vector3(0f, -0.04f, 0f), O),
                        K(0.55f, new Vector3(-0.04f, 0.05f, 0.3f), new Vector3(40f, 0f, 0f), new Vector3(0f, -0.02f, 0f), O) } };
                case RigAction.Hurl:
                    return new Shape { Keys = new[] {
                        K(0.4f, new Vector3(0.08f, 0.25f, -0.18f), new Vector3(-80f, 0f, -10f), new Vector3(0.04f, 0.04f, 0.1f), new Vector3(-20f, 0f, 0f)),
                        K(0.6f, new Vector3(-0.08f, -0.02f, 0.32f), new Vector3(50f, 0f, 0f), new Vector3(0f, -0.04f, 0f), O) },
                        ShakeAt = 0.6f, Shake = 0.12f };
                case RigAction.Drink:
                    return new Shape { Keys = new[] {
                        K(0.3f, new Vector3(-0.18f, 0.2f, -0.08f), new Vector3(-70f, 0f, -40f), new Vector3(0f, -0.06f, 0f), O),
                        K(0.8f, new Vector3(-0.18f, 0.22f, -0.1f), new Vector3(-80f, 0f, -40f), new Vector3(0f, -0.06f, 0f), O) } };
                case RigAction.Plant:
                    return new Shape { Keys = new[] {
                        K(0.35f, new Vector3(0f, 0.2f, 0.05f), new Vector3(-40f, 0f, 0f), new Vector3(0f, 0.12f, 0f), new Vector3(-15f, 0f, 0f), 0.4f),
                        K(0.55f, new Vector3(-0.05f, -0.22f, 0.25f), new Vector3(70f, 0f, 0f), new Vector3(0f, -0.1f, 0.1f), new Vector3(20f, 0f, 0f), 1f),
                        K(0.85f, new Vector3(-0.05f, -0.2f, 0.24f), new Vector3(68f, 0f, 0f), new Vector3(0f, -0.09f, 0.09f), new Vector3(18f, 0f, 0f), 0.5f) },
                        ShakeAt = 0.55f, Shake = 0.35f };
                case RigAction.Roar:
                    return new Shape { Keys = new[] {
                        K(0.3f, new Vector3(-0.03f, 0.12f, 0.04f), new Vector3(-30f, 0f, -15f), new Vector3(0.03f, 0.09f, 0.06f), new Vector3(-20f, 0f, 15f), 0.6f),
                        K(0.85f, new Vector3(-0.03f, 0.13f, 0.05f), new Vector3(-32f, 0f, -15f), new Vector3(0.03f, 0.1f, 0.06f), new Vector3(-22f, 0f, 15f), 0.4f) },
                        ShakeAt = 0.3f, Shake = 0.4f, Tremble = 0.008f };
                case RigAction.Leap:
                    return new Shape { Keys = new[] {
                        K(0.25f, new Vector3(0.04f, -0.14f, -0.04f), new Vector3(20f, 0f, 0f), new Vector3(-0.04f, -0.14f, -0.04f), new Vector3(20f, 0f, 0f)),
                        K(0.6f, new Vector3(0f, 0.1f, 0.06f), new Vector3(-20f, 0f, 0f), new Vector3(0f, 0.1f, 0.06f), new Vector3(-20f, 0f, 0f)) } };
                case RigAction.Vanish:
                    return new Shape { Keys = new[] {
                        K(0.35f, new Vector3(-0.14f, 0.1f, 0.06f), new Vector3(-20f, 0f, -40f), new Vector3(0.14f, 0.1f, 0.06f), new Vector3(-20f, 0f, 40f), 0.7f),
                        K(0.7f, new Vector3(-0.15f, 0.11f, 0.05f), new Vector3(-22f, 0f, -42f), new Vector3(0.15f, 0.11f, 0.05f), new Vector3(-22f, 0f, 42f), 0.3f) },
                        FlareMid = 0.5f };
                case RigAction.Fan:
                    return new Shape { Keys = new[] {
                        K(0.3f, new Vector3(0.1f, 0.06f, -0.06f), new Vector3(0f, 40f, 0f), O, O),
                        K(0.55f, new Vector3(-0.2f, 0.04f, 0.25f), new Vector3(0f, -60f, 0f), new Vector3(0f, -0.03f, 0f), O) } };
                case RigAction.Lash:
                    return new Shape { Keys = new[] {
                        K(0.35f, new Vector3(0.06f, 0.26f, -0.14f), new Vector3(-75f, 0f, 0f), new Vector3(0f, -0.04f, 0f), O),
                        K(0.55f, new Vector3(-0.04f, -0.12f, 0.3f), new Vector3(55f, 0f, 0f), new Vector3(0f, -0.04f, 0f), O) },
                        ShakeAt = 0.55f, Shake = 0.15f };
                case RigAction.Summon:
                case RigAction.Channel:
                    return new Shape { Keys = new[] {
                        K(0.3f, new Vector3(-0.08f, 0.1f, 0.22f), new Vector3(-40f, 0f, -20f), new Vector3(0.08f, 0.1f, 0.22f), new Vector3(-40f, 0f, 20f), 1f),
                        K(0.85f, new Vector3(-0.08f, 0.12f, 0.24f), new Vector3(-44f, 0f, -20f), new Vector3(0.08f, 0.12f, 0.24f), new Vector3(-44f, 0f, 20f), 1f) },
                        FlareMid = 0.5f, Tremble = 0.005f };
                case RigAction.Draw:
                    return new Shape { Keys = new[] {
                        K(0.4f, new Vector3(0f, 0.05f, -0.1f), new Vector3(-6f, 0f, 0f), new Vector3(0.02f, 0.05f, 0f), new Vector3(-12f, 0f, 0f), 0.6f),
                        K(0.85f, new Vector3(0f, 0.055f, -0.11f), new Vector3(-7f, 0f, 0f), new Vector3(0.02f, 0.055f, 0f), new Vector3(-13f, 0f, 0f), 1f) },
                        Tremble = 0.003f };
                default:
                    return default;
            }
        }

        private readonly CharacterArt.ViewModel vm;
        private readonly Color color;
        private readonly Renderer flare;
        private Shape shape;
        private float start = -10f, length = 1f;
        private bool shook;

        public ViewGesture(CharacterArt.ViewModel vm, Color color)
        {
            this.vm = vm;
            this.color = color;
            var go = MeshKit.Part(vm.Root, "GestureFlare", MeshKit.Ball(0.035f, 8, 6), Mat.Glow, Vector3.zero);
            flare = go.GetComponent<Renderer>();
            flare.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
        }

        private RigAction current;

        public bool Recent(RigAction a) => current == a && Time.time - start < 0.6f;

        public void Play(RigAction a, float seconds)
        {
            var s = ShapeOf(a);
            if (s.Keys == null) return;
            shape = s;
            current = a;
            start = Time.time;
            length = Mathf.Max(0.2f, seconds);
            shook = false;
        }

        public float Apply()
        {
            float t = (Time.time - start) / length;
            if (shape.Keys == null || t >= 1f || vm.SwordPivot == null || vm.ShieldPivot == null)
            {
                if (flare.gameObject.activeSelf) flare.gameObject.SetActive(false);
                return 0f;
            }
            Sample(t, out var mp, out var mr, out var op, out var or, out float glow);
            if (shape.Tremble > 0f)
            {
                var j = new Vector3(Mathf.Sin(Time.time * 53f), Mathf.Sin(Time.time * 47f + 1f), 0f) * shape.Tremble;
                mp += j;
                op -= j;
            }
            vm.SwordPivot.localPosition += mp;
            vm.SwordPivot.localRotation = Quaternion.Euler(mr) * vm.SwordPivot.localRotation;
            vm.ShieldPivot.localPosition += op;
            vm.ShieldPivot.localRotation = Quaternion.Euler(or) * vm.ShieldPivot.localRotation;

            bool lit = glow > 0.02f;
            if (flare.gameObject.activeSelf != lit) flare.gameObject.SetActive(lit);
            if (lit)
            {
                var off = vm.Root.InverseTransformPoint(vm.ShieldPivot.position) + new Vector3(0f, 0.04f, 0.08f);
                var main = vm.Root.InverseTransformPoint(vm.SwordPivot.position) + new Vector3(0f, 0.04f, 0.08f);
                flare.transform.localPosition = Vector3.Lerp(off, main, shape.FlareMid);
                float flick = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 9f, 0.4f);
                flare.transform.localScale = Vector3.one * (0.3f + 0.45f * glow) * flick;
                ArtLibrary.SetGlow(flare, color * (0.5f + 1.1f * glow) * flick);
            }

            if (!shook && shape.Shake > 0f && t >= shape.ShakeAt)
            {
                shook = true;
                return shape.Shake;
            }
            return 0f;
        }

        private void Sample(float t, out Vector3 mp, out Vector3 mr, out Vector3 op, out Vector3 or, out float glow)
        {
            var keys = shape.Keys;
            Key a = default, b = keys[0];
            for (int i = 0; i <= keys.Length; i++)
            {
                b = i < keys.Length ? keys[i] : new Key { T = 1f };
                if (t <= b.T) break;
                a = b;
            }
            float span = b.T - a.T;
            float k = span > 0f ? Mathf.Clamp01((t - a.T) / span) : 1f;
            k = k * k * (3f - 2f * k);
            mp = Vector3.LerpUnclamped(a.MainPos, b.MainPos, k);
            mr = Vector3.LerpUnclamped(a.MainRot, b.MainRot, k);
            op = Vector3.LerpUnclamped(a.OffPos, b.OffPos, k);
            or = Vector3.LerpUnclamped(a.OffRot, b.OffRot, k);
            glow = Mathf.Lerp(a.Flare, b.Flare, k);
        }
    }
}
