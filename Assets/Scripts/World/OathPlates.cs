using Deepbound.Art;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Oath Room (2026-10-20, a co-op side room): two worn pressure plates at either end of the room
    /// and a sealed coffer at the back. Both plates must be held down at once for <see cref="HoldSeconds"/>:
    /// two players, or one player and the <see cref="OathStone"/> set down on the other plate (a solo
    /// player carries it over). Kept, the coffer opens (a chest and a Sigil, from the director).
    /// <para>Server checks the plates; every peer draws them, lit while pressed, with a line of light
    /// between them that fills as the oath is held.</para>
    /// </summary>
    public class OathPlates : NetworkBehaviour
    {
        public const float PlateRadius = 1.15f, HoldSeconds = 1.6f;

        public readonly NetworkVariable<Vector3> PlateA = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<Vector3> PlateB = new NetworkVariable<Vector3>();
        public readonly NetworkVariable<Vector3> Coffer = new NetworkVariable<Vector3>();
        /// <summary>Bit 0: plate A pressed, bit 1: plate B.</summary>
        public readonly NetworkVariable<byte> Pressed = new NetworkVariable<byte>();
        public readonly NetworkVariable<float> Held = new NetworkVariable<float>();
        public readonly NetworkVariable<bool> Kept = new NetworkVariable<bool>();

        public int RoomId { get; set; } = -1;

        private Transform vis, line;
        private Renderer[] runeA, runeB;
        private Renderer lineRenderer;
        private bool built;
        private byte lastPressed;
        private static readonly Color Oath = new Color(0.55f, 0.75f, 1f);

        /// <summary>Server: place the plates and the coffer (after spawn).</summary>
        public void ServerSetup(Vector3 a, Vector3 b, Vector3 coffer)
        {
            PlateA.Value = Vfx.Floor(a);
            PlateB.Value = Vfx.Floor(b);
            Coffer.Value = coffer;
        }

        public static bool OnPlate(Vector3 plate, Vector3 at)
        {
            var d = at - plate;
            if (Mathf.Abs(d.y) > 1.4f) return false;
            d.y = 0f;
            return d.magnitude <= PlateRadius;
        }

        private static bool Weighed(Vector3 plate)
        {
            foreach (var p in PlayerLife.All)
                if (p != null && !p.Soul.Value && OnPlate(plate, p.transform.position)) return true;
            foreach (var c in Carryable.All)
                if (c is OathStone && !c.Carried && OnPlate(plate, c.Rest.Value)) return true;
            return false;
        }

        private void Update()
        {
            if (IsServer && !Kept.Value && PlateA.Value != Vector3.zero) ServerTick();
            Draw();
        }

        private void ServerTick()
        {
            byte p = (byte)((Weighed(PlateA.Value) ? 1 : 0) | (Weighed(PlateB.Value) ? 2 : 0));
            if (p != Pressed.Value) Pressed.Value = p;
            float held = p == 3 ? Held.Value + Time.deltaTime : Mathf.Max(0f, Held.Value - Time.deltaTime * 2f);
            Held.Value = Mathf.Min(HoldSeconds, held);
            if (Held.Value < HoldSeconds) return;
            Kept.Value = true;
            KeptRpc(Coffer.Value);
            DungeonDirector.Instance?.ServerOathKept(this);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void KeptRpc(Vector3 at)
        {
            Sfx.Play(Sound.Relic, at, 1f, 0.8f);
            Vfx.Ring(at + Vector3.up * 0.05f, 3f, Oath, 0.8f);
            UI.GameHud.Feed("The oath is kept: the coffer opens.", Oath);
        }

        private void Draw()
        {
            if (PlateA.Value == Vector3.zero) return;
            if (!built) Build();
            byte p = Pressed.Value;
            if (p != lastPressed)
            {
                // A stone click as a plate goes down.
                if ((p & 1) != 0 && (lastPressed & 1) == 0) Sfx.Play(Sound.Creak, PlateA.Value, 0.7f, 1.5f);
                if ((p & 2) != 0 && (lastPressed & 2) == 0) Sfx.Play(Sound.Creak, PlateB.Value, 0.7f, 1.5f);
                lastPressed = p;
            }
            float pulse = 0.85f + Mathf.Sin(Time.time * 3f) * 0.15f;
            Glow(runeA, Kept.Value ? 1.2f : (p & 1) != 0 ? 1.8f * pulse : 0.35f);
            Glow(runeB, Kept.Value ? 1.2f : (p & 2) != 0 ? 1.8f * pulse : 0.35f);
            float k = Kept.Value ? 1f : Held.Value / HoldSeconds;
            line.gameObject.SetActive(k > 0.01f && !Kept.Value);
            if (k > 0.01f)
            {
                var a = PlateA.Value + Vector3.up * 0.12f;
                var b = PlateB.Value + Vector3.up * 0.12f;
                var end = Vector3.Lerp(a, b, k);
                line.position = (a + end) / 2f;
                var dir = end - a;
                if (dir.sqrMagnitude > 1e-4f) line.rotation = Quaternion.LookRotation(dir);
                line.localScale = new Vector3(1f, 1f, dir.magnitude);
                ArtLibrary.SetGlow(lineRenderer, Oath * (1.2f + k));
            }
            // The plates sink a finger's width when pressed.
            vis.GetChild(0).position = PlateA.Value + Vector3.up * ((p & 1) != 0 ? -0.03f : 0f);
            vis.GetChild(1).position = PlateB.Value + Vector3.up * ((p & 2) != 0 ? -0.03f : 0f);
        }

        private static void Glow(Renderer[] rs, float k)
        {
            if (rs == null) return;
            foreach (var r in rs) if (r != null) ArtLibrary.SetGlow(r, Oath * k);
        }

        private void Build()
        {
            built = true;
            vis = new GameObject("OathVis").transform;
            vis.SetParent(transform, false);
            runeA = BuildPlate(PlateA.Value, "A");
            runeB = BuildPlate(PlateB.Value, "B");
            line = MeshKit.Part(vis, "OathLine", MeshKit.Box(new Vector3(0.06f, 0.03f, 1f)), Mat.Glow, Vector3.zero).transform;
            lineRenderer = line.GetComponent<Renderer>();
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.gameObject.SetActive(false);
        }

        private Renderer[] BuildPlate(Vector3 at, string tag)
        {
            var plate = new GameObject("Plate" + tag).transform;
            plate.SetParent(vis, false);
            plate.position = at;
            // A round, worn stone plate in a stone ring, a glyph ring and two hands carved in the face.
            MeshKit.Part(plate, "Ring", MeshKit.Cylinder(1.25f, 1.2f, 0.08f, 20), Mat.StoneWall, Vector3.zero);
            MeshKit.Part(plate, "Face", MeshKit.Cylinder(1.02f, 1f, 0.1f, 20), Mat.Marble, Vector3.zero);
            var runes = new Renderer[9];
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var g = MeshKit.Part(plate, "Glyph", MeshKit.Box(new Vector3(0.1f, 0.02f, 0.24f)), Mat.Glow,
                    Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.78f + Vector3.up * 0.105f, new Vector3(0f, a + (i % 2 == 0 ? 0f : 30f), 0f));
                g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                runes[i] = g.GetComponent<Renderer>();
            }
            var hand = MeshKit.Part(plate, "Palm", MeshKit.Cylinder(0.26f, 0.26f, 0.02f, 12), Mat.Glow, Vector3.up * 0.105f);
            hand.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            runes[8] = hand.GetComponent<Renderer>();
            Glow(runes, 0.35f);
            return runes;
        }
    }
}
