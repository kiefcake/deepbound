using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A stunned creature, on every peer (2026-10-20): three little stars wheel round its head, it sags
    /// and sways on its feet, and a dull ring pulses on the floor under it, so "it can't act right now"
    /// reads at a glance. Driven by the synced <see cref="Enemies.EnemyBrain.Status"/> bit 1.
    /// </summary>
    public class StunDaze : MonoBehaviour
    {
        private Enemies.EnemyBrain brain;
        private Combat.Health health;
        private Transform stars;
        private Renderer[] starRenderers;
        private Quaternion baseRot;
        private bool haveBase, wasStunned;
        private float since;
        private static readonly Color Star = new Color(1f, 0.92f, 0.5f);

        public void Init(Enemies.EnemyBrain owner)
        {
            brain = owner;
            health = owner.GetComponent<Combat.Health>();
        }

        private void Build()
        {
            stars = new GameObject("StunStars").transform;
            stars.SetParent(transform, false);
            starRenderers = new Renderer[6];
            for (int i = 0; i < 3; i++)
            {
                var star = MeshKit.Pivot(stars, "Star" + i, Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 0.42f);
                for (int k = 0; k < 2; k++)
                {
                    var p = MeshKit.Part(star, "Ray", MeshKit.Box(new Vector3(0.16f, 0.035f, 0.02f)), Mat.Glow, Vector3.zero, new Vector3(0f, 0f, 45f + k * 90f));
                    var r = p.GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    ArtLibrary.SetGlow(r, Star * 1.6f);
                    starRenderers[i * 2 + k] = r;
                }
            }
        }

        private void LateUpdate()
        {
            if (brain == null || health == null) return;
            bool stunned = (brain.Status.Value & 1) != 0 && health.IsAlive;
            var rig = brain.Rig;
            if (stunned && !wasStunned)
            {
                since = Time.time;
                if (stars == null) Build();
                if (rig != null && !haveBase) { baseRot = rig.transform.localRotation; haveBase = true; }
                Vfx.Ring(transform.position + Vector3.up * 0.05f, Mathf.Max(0.8f, health.BodyRadius * 1.6f), Star, 0.4f);
            }
            if (!stunned && wasStunned && rig != null && haveBase) rig.transform.localRotation = baseRot;
            wasStunned = stunned;
            if (stars != null && stars.gameObject.activeSelf != stunned) stars.gameObject.SetActive(stunned);
            if (!stunned) return;

            float t = Time.time - since;
            float head = health.CenterHeight * 2f + 0.25f;
            float scale = Mathf.Max(1f, health.BodyRadius * 1.4f);
            stars.localPosition = Vector3.up * head;
            stars.localRotation = Quaternion.Euler(0f, t * 260f, 0f);
            stars.localScale = Vector3.one * scale * Mathf.Clamp01(t * 6f);
            for (int i = 0; i < starRenderers.Length; i++)
                starRenderers[i].transform.parent.localRotation = Quaternion.Euler(0f, 0f, t * 400f + i * 40f);
            // Sag and sway: a slow drunken circle, the body tipped forward.
            if (rig != null && haveBase)
                rig.transform.localRotation = baseRot * Quaternion.Euler(6f + Mathf.Sin(t * 3.1f) * 5f, 0f, Mathf.Cos(t * 2.3f) * 7f);
        }
    }
}
