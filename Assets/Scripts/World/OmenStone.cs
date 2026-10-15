using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Omen Stone by the carriage: before a run sets out, anyone can turn it to choose
    /// the run's omen — a twist that makes the descent harder in one particular way, and
    /// pays it back (a perk, and more Renown at the end).
    /// </summary>
    public class OmenStone : Interactable
    {
        private Renderer rune;
        private Light glow;
        public override float Range => 2.6f;

        public override string Prompt(PlayerProgress who)
        {
            var d = DungeonDirector.Instance;
            if (d == null || !DungeonDirector.InLobby || d.RunInProgress) return null;
            var next = (Omen)(((int)d.RunOmen + 1) % DungeonDirector.OmenCount);
            return "Turn the Omen Stone: " + DungeonDirector.OmenName(d.RunOmen) + "  ->  " + DungeonDirector.OmenName(next) +
                (next != Omen.None ? "  (" + DungeonDirector.OmenText(next) + ")" : "");
        }

        protected override void ServerUse(PlayerProgress who)
        {
            var d = DungeonDirector.Instance;
            if (d == null || d.RunInProgress) return;
            d.ServerSetOmen((Omen)(((int)d.RunOmen + 1) % DungeonDirector.OmenCount));
            TurnFxRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TurnFxRpc()
        {
            var d = DungeonDirector.Instance;
            Sfx.Play(Sound.Slam, transform.position + Vector3.up, 0.6f, 1.6f);
            if (d != null)
            {
                var c = DungeonDirector.OmenColor(d.RunOmen);
                Vfx.Embers(transform.position + Vector3.up * 1.8f, 14, c, 3f, 0.05f, 0.8f);
                UI.GameHud.Feed("The omen for this run: " + DungeonDirector.OmenName(d.RunOmen) +
                    (d.RunOmen != Omen.None ? " — " + DungeonDirector.OmenText(d.RunOmen) : ""), c);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            MeshKit.Part(transform, "Base", MeshKit.Box(new Vector3(1.2f, 0.3f, 1.2f)), Mat.StoneWall, Vector3.up * 0.15f);
            MeshKit.Part(transform, "Stone", MeshKit.Lathe("omenStone", new[]
            {
                new Vector2(0.5f, 0f), new Vector2(0.45f, 1.4f), new Vector2(0.32f, 2.1f), new Vector2(0f, 2.4f),
            }, 5), Mat.StoneWall, Vector3.up * 0.3f, Vector3.zero, new Vector3(1f, 1f, 0.6f));
            rune = MeshKit.Part(transform, "Rune", MeshKit.Box(new Vector3(0.1f, 0.9f, 0.04f)), Mat.Glow, new Vector3(0f, 1.5f, 0.28f)).GetComponent<Renderer>();
            MeshKit.Part(transform, "RuneBar", MeshKit.Box(new Vector3(0.45f, 0.08f, 0.04f)), Mat.Glow, new Vector3(0f, 1.75f, 0.28f));
            glow = new GameObject("OmenLight").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.6f, 0.9f);
            glow.type = LightType.Point;
            glow.range = 5f;
            glow.intensity = 1.6f;
            glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            var d = DungeonDirector.Instance;
            if (d == null || rune == null) return;
            var c = DungeonDirector.OmenColor(d.RunOmen);
            ArtLibrary.SetGlow(rune, c * (1f + 0.3f * Mathf.Sin(Time.time * 2f)));
            var bar = transform.Find("RuneBar");
            if (bar != null) ArtLibrary.SetGlow(bar.GetComponent<Renderer>(), c * (1f + 0.3f * Mathf.Sin(Time.time * 2f)));
            glow.color = c;
        }
    }
}
