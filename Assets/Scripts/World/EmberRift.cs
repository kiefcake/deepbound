using Deepbound.Art;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public class EmberRift : Interactable
    {
        private Transform look;
        private Light glow;
        private float nextMote;

        public override float Range => 2.8f;

        public override string Prompt(PlayerProgress who) => "Step into the ember rift  (the whole party drops; this floor is left behind)";

        protected override void ServerUse(PlayerProgress who)
        {
            if (who == null || DungeonDirector.Instance == null) return;
            DungeonDirector.Instance.ServerEnterPit(who.name);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (Application.isBatchMode) return;
            look = new GameObject("RiftLook").transform;
            look.SetParent(transform, false);

            var scorch = MeshKit.Part(look, "Scorch", MeshKit.Cylinder(1.6f, 1.5f, 0.03f, 18), Mat.Rubble, Vector3.up * 0.01f);
            ArtLibrary.Tint(scorch.GetComponent<Renderer>(), new Color(0.08f, 0.05f, 0.04f));
            for (int i = 0; i < 5; i++)
            {
                var crack = MeshKit.Part(look, "Crack", MeshKit.Box(new Vector3(0.18f + (i % 2) * 0.1f, 0.04f, 1.1f - i * 0.12f)), Mat.Glow, new Vector3((i - 2) * 0.16f, 0.03f, (i % 2) * 0.2f - 0.1f), new Vector3(0f, -20f + i * 11f, 0f));
                ArtLibrary.SetGlow(crack.GetComponent<Renderer>(), new Color(1f, 0.45f, 0.12f) * 2.2f);
                crack.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            glow = new GameObject("RiftLight").AddComponent<Light>();
            glow.transform.SetParent(look, false);
            glow.transform.localPosition = Vector3.up * 0.9f;
            glow.type = LightType.Point; glow.color = new Color(1f, 0.45f, 0.15f); glow.range = 7f; glow.intensity = 3f; glow.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (glow != null) glow.intensity = 2.6f + 0.8f * Mathf.Abs(Mathf.Sin(Time.time * 2.3f));
            if (look == null || Time.time < nextMote) return;
            nextMote = Time.time + 0.12f;
            Vfx.Embers(transform.position + new Vector3(Random.Range(-0.4f, 0.4f), 0.1f, Random.Range(-0.5f, 0.5f)), 1, new Color(1f, 0.5f, 0.15f), 2.8f, 0.05f, 1f);
            if (Random.value < 0.1f) Vfx.Smoke(transform.position + Vector3.up * 0.4f, 1, 0.3f, 2f);
        }
    }
}
