using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// A quest's CARGO (2026-11-08, the user: quests "way too easy ... be more creative, you can involve new mechanics or
    /// assets that can be done in the dungeon"; picked "Fetch &amp; carry"). Somewhere far from the start lies the thing a
    /// camp person asked for; carry it (F) back to the start room's waystone. Each one handles differently:
    /// <list type="bullet">
    /// <item>Hobb's father's hammer: heavy (x0.5 speed).</item>
    /// <item>Silas's strongbox of a dead merchant: x0.62, and every blow that knocks it loose spills some of its gold (the reward shrinks).</item>
    /// <item>Marta's cask of Deepcellar Black and Vey's bolt of deep silk: FRAGILE - every blow that knocks it loose
    /// cracks it (three and it is ruined, the quest failed).</item>
    /// <item>Corwin's old coach lamp: light (x0.95) and it lights the way (a carried light, like the hand torch).</item>
    /// </list>
    /// </summary>
    public class QuestCargo : Carryable
    {
        /// <summary>Whose cargo this is (a <see cref="FolkId"/>); set before spawning.</summary>
        public readonly NetworkVariable<byte> Giver = new NetworkVariable<byte>();
        /// <summary>Fragile cargo: cracks so far (3 = ruined).</summary>
        public readonly NetworkVariable<byte> Cracks = new NetworkVariable<byte>();
        public const int MaxCracks = 3;

        public FolkId Who => (FolkId)Giver.Value;
        public bool Fragile => Who == FolkId.Marta || Who == FolkId.Vey;
        private Light lamp;

        public static string NameOf(FolkId who) => who switch
        {
            FolkId.Hobb => "Hobb's father's hammer",
            FolkId.Silas => "a dead merchant's strongbox",
            FolkId.Marta => "a cask of Deepcellar Black",
            FolkId.Vey => "a bolt of deep silk",
            FolkId.Corwin => "Corwin's old coach lamp",
            _ => "the parcel",
        };

        public override string ItemName => NameOf(Who);
        public override float CarrySpeed => Who switch { FolkId.Hobb => 0.5f, FolkId.Silas => 0.62f, FolkId.Marta => 0.6f, FolkId.Vey => 0.85f, FolkId.Corwin => 0.95f, _ => 0.72f };
        protected override float DropFraction => Fragile ? 0.08f : 0.14f;
        public override Color MarkerColor => new Color(0.95f, 0.8f, 0.45f);
        protected override Vector3 HoldOffset => Who == FolkId.Corwin ? new Vector3(0.35f, 1.45f, 0.4f) : new Vector3(0f, 1.1f, 0.5f);

        public override string Prompt(PlayerProgress who)
        {
            string p = base.Prompt(who);
            if (p == null) return null;
            if (Fragile && Cracks.Value > 0) p += "  (cracked " + Cracks.Value + " / " + MaxCracks + ")";
            return p + "  -  bring it to the waystone";
        }

        protected override void ServerCarrierHurt(float amount, bool knockedLoose)
        {
            if (!knockedLoose) return;
            if (Fragile)
            {
                Cracks.Value++;
                CrackFxRpc(Cracks.Value);
                if (Cracks.Value >= MaxCracks)
                {
                    DungeonDirector.Instance?.ServerCargoRuined(Who);
                    NetworkObject.Despawn();
                }
            }
            else if (Who == FolkId.Silas) DungeonDirector.Instance?.ServerCargoSpill(transform.position);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CrackFxRpc(byte cracks)
        {
            Sfx.Play(Who == FolkId.Marta ? Sound.Shatter : Sound.Creak, transform.position + Vector3.up * 0.4f, 0.9f, 1.1f);
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 0.9f, cracks >= MaxCracks ? "RUINED" : "CRACKED " + cracks + "/" + MaxCracks, new Color(1f, 0.5f, 0.35f));
        }

        protected override void ServerDelivered(NetworkObject carrier, DungeonLayout layout)
        {
            DungeonDirector.Instance?.ServerCargoDelivered(Who);
            NetworkObject.Despawn();
        }

        protected override void VisualTick(bool carried)
        {
            if (lamp != null) lamp.intensity = 2f + Mathf.Sin(Time.time * 9f) * 0.15f;
        }

        protected override void BuildVisual()
        {
            visual = new GameObject("Cargo").transform;
            visual.SetParent(transform, false);
            switch (Who)
            {
                case FolkId.Hobb:
                    // A great smith's hammer, head down, haft up, a leather grip.
                    MeshKit.Part(visual, "Head", MeshKit.Box(new Vector3(0.42f, 0.2f, 0.2f)), Mat.Steel, new Vector3(0f, 0.1f, 0f));
                    MeshKit.Part(visual, "Haft", MeshKit.Cylinder(0.035f, 0.03f, 0.95f, 7), Mat.Wood, new Vector3(0f, 0.2f, 0f));
                    ArtLibrary.Tint(MeshKit.Part(visual, "Grip", MeshKit.Cylinder(0.042f, 0.042f, 0.25f, 7), Mat.Leather, new Vector3(0f, 0.85f, 0f)).GetComponent<Renderer>(), new Color(0.35f, 0.22f, 0.12f));
                    MeshKit.Part(visual, "Mark", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.01f)), Mat.Gold, new Vector3(0f, 0.1f, 0.101f));
                    break;
                case FolkId.Silas:
                    MeshKit.Part(visual, "Box", MeshKit.Box(new Vector3(0.55f, 0.36f, 0.38f)), Mat.Wood, new Vector3(0f, 0.18f, 0f));
                    foreach (float x in new[] { -0.2f, 0.2f }) MeshKit.Part(visual, "Band", MeshKit.Box(new Vector3(0.05f, 0.38f, 0.4f)), Mat.Steel, new Vector3(x, 0.18f, 0f));
                    MeshKit.Part(visual, "Lock", MeshKit.Box(new Vector3(0.1f, 0.12f, 0.04f)), Mat.Gold, new Vector3(0f, 0.22f, 0.2f));
                    break;
                case FolkId.Marta:
                    MeshKit.Part(visual, "Cask", MeshKit.Lathe("questCask", new[] { new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.25f, 0.25f), new Vector2(0.2f, 0.5f), new Vector2(0f, 0.5f) }, 12), Mat.Wood, Vector3.zero);
                    foreach (float y in new[] { 0.08f, 0.42f }) MeshKit.Part(visual, "Hoop", MeshKit.Cylinder(0.225f, 0.225f, 0.03f, 12), Mat.Steel, new Vector3(0f, y, 0f));
                    ArtLibrary.Tint(MeshKit.Part(visual, "Seal", MeshKit.Box(new Vector3(0.12f, 0.12f, 0.02f)), Mat.Cloth, new Vector3(0f, 0.25f, 0.245f)).GetComponent<Renderer>(), new Color(0.6f, 0.08f, 0.08f));
                    break;
                case FolkId.Vey:
                    var bolt = MeshKit.Part(visual, "Silk", MeshKit.Cylinder(0.13f, 0.13f, 0.9f, 12), Mat.Cloth, new Vector3(0f, 0.13f, -0.45f), new Vector3(90f, 0f, 0f));
                    ArtLibrary.Tint(bolt.GetComponent<Renderer>(), new Color(0.12f, 0.05f, 0.22f));
                    var sheen = MeshKit.Part(visual, "Sheen", MeshKit.Cylinder(0.132f, 0.132f, 0.1f, 12), Mat.Glow, new Vector3(0f, 0.13f, -0.05f), new Vector3(90f, 0f, 0f));
                    ArtLibrary.SetGlow(sheen.GetComponent<Renderer>(), new Color(0.5f, 0.3f, 0.9f) * 0.5f);
                    break;
                default: // Corwin's lamp
                    MeshKit.Part(visual, "Base", MeshKit.Box(new Vector3(0.22f, 0.05f, 0.22f)), Mat.Steel, new Vector3(0f, 0.025f, 0f));
                    var glass = MeshKit.Part(visual, "Glass", MeshKit.Box(new Vector3(0.18f, 0.26f, 0.18f)), Mat.Glow, new Vector3(0f, 0.18f, 0f));
                    ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), new Color(1f, 0.75f, 0.4f) * 1.3f);
                    MeshKit.Part(visual, "Roof", MeshKit.Lathe("coachLampRoof", new[] { new Vector2(0.16f, 0f), new Vector2(0f, 0.12f) }, 4), Mat.Steel, new Vector3(0f, 0.31f, 0f));
                    MeshKit.Part(visual, "Ring", MeshKit.Cylinder(0.03f, 0.03f, 0.08f, 6), Mat.Steel, new Vector3(0f, 0.42f, 0f));
                    lamp = new GameObject("CoachLampLight").AddComponent<Light>();
                    lamp.transform.SetParent(visual, false);
                    lamp.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                    lamp.type = LightType.Point;
                    lamp.color = new Color(1f, 0.74f, 0.42f);
                    lamp.range = 9f;
                    lamp.intensity = 2f;
                    lamp.shadows = LightShadows.None;
                    break;
            }
            // A thin column of light over it while it lies waiting, so a searching party can spot it down a hall.
            var beacon = MeshKit.Part(visual, "Beacon", MeshKit.Cylinder(0.02f, 0.06f, 3.2f, 6), Mat.Glow, new Vector3(0f, 0.5f, 0f));
            ArtLibrary.SetGlow(beacon.GetComponent<Renderer>(), new Color(1f, 0.8f, 0.45f) * 0.45f);
            beacon.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        protected override void Update()
        {
            base.Update();
            var beacon = visual != null ? visual.Find("Beacon") : null;
            if (beacon != null) beacon.gameObject.SetActive(!Carried);
        }
    }
}
