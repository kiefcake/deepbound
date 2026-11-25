using Deepbound.Art;
using UnityEngine;

namespace Deepbound.World
{
    public class PyreFire : MonoBehaviour
    {
        public const float Period = 9f, Warn = 2.2f, CoreRadius = 2.3f, EruptRadius = 7f;

        private DungeonLayout.Room room;
        private Torch flame;
        private Transform rear;
        private Light glow;
        private float lastPhase;

        public static float Phase(DungeonLayout.Room room, double time) => (float)((time + (room.DecorSeed & 1023) * 0.37) % Period);

        public static bool Warning(float phase) => phase >= Period - Warn;

        public static void Build(Transform parent, DungeonLayout.Room room)
        {
            var go = new GameObject("Pyre");
            go.transform.SetParent(parent, false);
            go.transform.position = room.Center;
            var p = go.AddComponent<PyreFire>();
            p.room = room;
            var t = go.transform;

            for (int i = 0; i < 14; i++)
            {
                float a = i * 360f / 14f * Mathf.Deg2Rad;
                MeshKit.Part(t, "Kerb", MeshKit.Box(new Vector3(0.9f, 0.45f, 0.5f)), Mat.StoneWall, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * CoreRadius + Vector3.up * 0.22f, new Vector3(0f, -i * 360f / 14f + 90f, 0f));
            }
            MeshKit.Part(t, "Ash", MeshKit.Cylinder(CoreRadius - 0.2f, CoreRadius - 0.2f, 0.06f, 16), Mat.Dark, Vector3.up * 0.03f);
            for (int layer = 0; layer < 4; layer++)
                for (int k = -1; k <= 1; k++)
                {
                    var along = layer % 2 == 0 ? new Vector3(k * 0.5f, 0.2f + layer * 0.3f, 0f) : new Vector3(0f, 0.2f + layer * 0.3f, k * 0.5f);
                    MeshKit.Part(t, "Log", MeshKit.Cylinder(0.14f, 0.14f, 2.2f - layer * 0.3f, 7), Mat.Wood, along, layer % 2 == 0 ? new Vector3(90f, 0f, 0f) : new Vector3(0f, 0f, 90f));
                }
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + 20f) * Mathf.Deg2Rad;
                MeshKit.Part(t, "AshSkull", MeshKit.Ball(0.14f, 7, 5), Mat.Bone, new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * 1.4f);
            }
            MeshKit.MergeParts(t);
            p.rear = MeshKit.Pivot(t, "Rear", Vector3.up * 0.9f);
            p.flame = Props.AddFlame(p.rear, Vector3.zero, 7f, new Color(1f, 0.5f, 0.15f), 3f, 14f, false);
            p.glow = p.flame != null ? p.flame.GetComponentInChildren<Light>() : null;
        }

        private void Update()
        {
            if (room == null) return;
            float ph = Phase(room, Pendulum.Clock);

            if (Warning(ph) && !Warning(lastPhase))
                Combat.Telegraph.Show(room.Center + Vector3.up * 0.05f, EruptRadius, Period - ph, this, Combat.Telegraph.Harm);
            if (ph < lastPhase)
            {
                Vfx.Ring(room.Center + Vector3.up * 0.2f, EruptRadius, new Color(1f, 0.45f, 0.1f), 0.6f);
                Vfx.Fireball(room.Center + Vector3.up * 1.2f, 3.2f, new Color(1f, 0.45f, 0.12f));
                Vfx.Flash(room.Center + Vector3.up * 2f, new Color(1f, 0.5f, 0.15f), 7f, 18f, 0.5f);
                Vfx.Embers(room.Center + Vector3.up, 30, new Color(1f, 0.55f, 0.2f), 9f, 0.08f, 1.1f);
                Sfx.Play(Sound.Fire, room.Center + Vector3.up, 1f, 0.55f);
                Sfx.Play(Sound.Slam, room.Center, 0.8f, 0.7f);
            }
            lastPhase = ph;
            float rear = Warning(ph) ? (ph - (Period - Warn)) / Warn : 0f;
            if (this.rear != null) this.rear.localScale = Vector3.one * (1f + rear * 0.8f);
            if (glow != null) glow.intensity = 3f + rear * 5f;
        }
    }
}
