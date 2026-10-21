using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The way out after a boss (reworked 2026-10-20, the user: "a new way out that fits the theme"):
    /// the GRAVE LIFT. An iron cage on chains comes down out of the dark above where the guardian
    /// stood, a lantern swinging inside it and a bell ringing on its crown. Gather in the cage; when
    /// the party is aboard and the count runs out, the chains take up, the cage grinds back up into
    /// the dark (<see cref="Ascend"/>) as the screen fades, and you climb out at camp. Local only
    /// (every peer builds its own); it lives under the level root.
    /// </summary>
    public class ExitPortal : MonoBehaviour
    {
        public const float DescendSeconds = 2.8f, AscendSeconds = 2.2f;
        private static ExitPortal current;
        private Transform cage, lantern;
        private Light glow;
        private float born, ascendAt = -1f, ceiling = 9f, nextClank;
        /// <summary>The local player is aboard and rising with the cage (pinned to its floor).</summary>
        private Player.FirstPersonMotor riding;

        /// <summary>For tests: is the local player riding the cage up?</summary>
        public static bool LocalRiding => current != null && current.riding != null;
        private static readonly Color Lamp = new Color(1f, 0.78f, 0.45f);

        public static void Spawn(Vector3 at, Transform levelRoot)
        {
            var go = new GameObject("GraveLift");
            go.transform.SetParent(levelRoot, true);
            go.transform.position = at;
            current = go.AddComponent<ExitPortal>();
            var room = World.DungeonDirector.Layout?.BossRoom;
            current.ceiling = room != null ? room.Height : 10f;
            current.Build();
            Sfx.Play(Sound.Rattle, at + Vector3.up * 4f, 1f, 0.5f);
        }

        /// <summary>Any peer: the chains take up and the cage climbs away.</summary>
        public static void Ascend()
        {
            if (current == null) return;
            current.ascendAt = Time.time;
            Sfx.Play(Sound.Rattle, current.transform.position + Vector3.up * 2f, 1f, 0.45f);
            Sfx.Play(Sound.Slam, current.transform.position + Vector3.up * 2f, 0.7f, 0.5f);
            var local = Player.NetworkPlayer.Local;
            if (local != null) local.AddShake(0.35f);
        }

        /// <summary>Owner: whoever stands in the cage when the chains take up rides up in it.</summary>
        private void RideUp()
        {
            var local = Player.NetworkPlayer.Local;
            if (local == null) return;
            var motor = local.GetComponent<Player.FirstPersonMotor>();
            if (motor == null) return;
            var flat = local.transform.position - transform.position;
            flat.y = 0f;
            if (riding == null)
            {
                if (flat.magnitude > World.DungeonDirector.ExitRadius + 0.3f) return;
                riding = motor;
            }
            // Stay inside the bars, standing on the planks as they climb.
            float r = World.DungeonDirector.ExitRadius - 0.5f;
            if (flat.magnitude > r) flat = flat.normalized * r;
            motor.PinTo(transform.position + flat + Vector3.up * (cage.localPosition.y + 0.15f));
        }

        private void OnDestroy()
        {
            // The level is gone (camp is being built): let go of the rider where they are.
            if (riding != null) riding.Unpin(riding.transform.position);
            riding = null;
            if (current == this) current = null;
        }

        private void Build()
        {
            float r = World.DungeonDirector.ExitRadius;
            born = Time.time;
            // A ring of light on the floor where it will land, so the way out reads from across the room.
            var mark = MeshKit.Part(transform, "LandingRing", MeshKit.Lathe("liftRing", new[] { new Vector2(r - 0.15f, 0f), new Vector2(r + 0.05f, 0.02f), new Vector2(r - 0.15f, 0.04f) }, 40, false), Mat.Glow, Vector3.up * 0.03f);
            ArtLibrary.SetGlow(mark.GetComponent<Renderer>(), Lamp * 0.9f);
            mark.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            cage = MeshKit.Pivot(transform, "Cage", Vector3.up * ceiling);
            float cr = r * 0.92f, h = 3.1f;
            // Floor plate, crown, bars, two bands, a gate gap toward the room, and a bell on top.
            MeshKit.Part(cage, "Floor", MeshKit.Cylinder(cr, cr, 0.12f, 18), Mat.Steel, Vector3.up * 0.06f);
            MeshKit.Part(cage, "Planks", MeshKit.Cylinder(cr - 0.1f, cr - 0.1f, 0.04f, 18), Mat.Wood, Vector3.up * 0.13f);
            MeshKit.Part(cage, "Crown", MeshKit.Lathe("liftCrown", new[] { new Vector2(cr, 0f), new Vector2(cr * 0.6f, 0.9f), new Vector2(0.2f, 1.3f), new Vector2(0f, 1.32f) }, 18, false), Mat.Steel, Vector3.up * h);
            for (int i = 0; i < 18; i++)
            {
                if (i == 0 || i == 17) continue; // the open gate, facing +Z
                float a = i * 20f * Mathf.Deg2Rad;
                MeshKit.Part(cage, "Bar", MeshKit.Cylinder(0.035f, 0.035f, h, 6), Mat.Steel, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * cr + Vector3.up * h / 2f);
            }
            foreach (float y in new[] { 1.1f, h - 0.05f })
                MeshKit.Part(cage, "Band", MeshKit.Lathe("liftBand", new[] { new Vector2(cr - 0.03f, -0.04f), new Vector2(cr + 0.03f, 0f), new Vector2(cr - 0.03f, 0.04f) }, 24, false), Mat.Steel, Vector3.up * y);
            MeshKit.Part(cage, "Bell", MeshKit.Lathe("liftBell", new[] { new Vector2(0f, 0.3f), new Vector2(0.12f, 0.28f), new Vector2(0.2f, 0.05f), new Vector2(0.24f, 0f) }, 12), Mat.Gold, Vector3.up * (h + 1.3f));
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                MeshKit.Part(cage, "Chain", MeshKit.Box(new Vector3(0.05f, 14f, 0.05f)), Mat.Steel, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.35f + Vector3.up * (h + 1.3f + 7f));
            }
            lantern = MeshKit.Pivot(cage, "Lantern", Vector3.up * (h - 0.4f));
            MeshKit.Part(lantern, "Cord", MeshKit.Box(new Vector3(0.02f, 0.5f, 0.02f)), Mat.Steel, Vector3.down * 0.25f);
            MeshKit.Part(lantern, "Frame", MeshKit.Box(new Vector3(0.22f, 0.3f, 0.22f)), Mat.Steel, Vector3.down * 0.6f);
            var lampCore = MeshKit.Part(lantern, "Glass", MeshKit.Box(new Vector3(0.17f, 0.24f, 0.17f)), Mat.Glow, Vector3.down * 0.6f);
            ArtLibrary.SetGlow(lampCore.GetComponent<Renderer>(), Lamp * 1.5f);
            glow = new GameObject("LiftLight").AddComponent<Light>();
            glow.transform.SetParent(lantern, false);
            glow.transform.localPosition = Vector3.down * 0.6f;
            glow.color = Lamp;
            glow.range = 9f;
            glow.intensity = 2.8f;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (cage == null) return;
            float t = Time.time - born;
            float y;
            if (ascendAt > 0f)
            {
                float k = Mathf.Clamp01((Time.time - ascendAt) / AscendSeconds);
                y = Mathf.Lerp(0f, ceiling + 2f, k * k);
            }
            else
            {
                // Down out of the dark, slowing, with a little bounce on the chains as it lands.
                float k = Mathf.Clamp01(t / DescendSeconds);
                y = Mathf.Lerp(ceiling, 0f, 1f - Mathf.Pow(1f - k, 3f));
                if (k >= 1f) y = Mathf.Abs(Mathf.Sin((t - DescendSeconds) * 9f)) * 0.06f * Mathf.Exp(-(t - DescendSeconds) * 3f);
                if (k < 1f && Time.time >= nextClank) { nextClank = Time.time + 0.45f; Sfx.Play(Sound.Rattle, cage.position + Vector3.up * 2f, 0.45f, 0.6f); }
            }
            cage.localPosition = Vector3.up * y;
            if (ascendAt > 0f) RideUp();
            if (lantern != null) lantern.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f) * 6f, 0f, Mathf.Cos(Time.time * 1.3f) * 5f);
            if (glow != null) glow.intensity = 2.6f + Mathf.Sin(Time.time * 9f) * 0.15f;
        }
    }
}
