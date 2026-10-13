using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>What a rig is currently doing, beyond walking. Synced as a byte by enemies.</summary>
    public enum RigAction : byte
    {
        None = 0,
        Windup = 1,   // weapon drawn back — the telegraph players learn to read
        Swing = 2,    // the strike itself
        Slam = 3,     // two-handed overhead smash (boss)
        Stagger = 4,  // knocked back by a hit or a shield bash
        Bash = 5,     // shield thrust
        Death = 6,
        Cast = 7,     // arms raised (boss summon, war cry)
    }

    /// <summary>
    /// Pivot references for a procedurally built humanoid, and the procedural animation
    /// that drives them.
    ///
    /// <para>
    /// There are no animation clips: every motion is a rotation curve on a pivot. That is
    /// the only workable option for generated art (there is no skinned mesh to drive), and
    /// it has a real upside — timings such as wind-up length come straight from gameplay
    /// values, so the telegraph a player sees is always exactly as long as the attack's
    /// real wind-up.
    /// </para>
    ///
    /// <para>
    /// Walk speed is measured from the transform's own movement, so the same component
    /// animates the local player, a remote player driven by NetworkTransform, and a
    /// server-driven enemy without knowing which it is.
    /// </para>
    /// </summary>
    public class HumanoidRig : MonoBehaviour
    {
        public Transform Hips, Torso, Head, ArmL, ArmR, LegL, LegR;

        /// <summary>Renderers that glow (eyes, gems). Brightened during wind-ups.</summary>
        public Renderer[] GlowRenderers;
        public Color GlowColor = new Color(1f, 0.2f, 0.1f);

        /// <summary>Metres per walk cycle; bigger creatures take longer strides.</summary>
        public float StrideLength = 1.4f;

        public bool Blocking;
        public bool Downed;

        private RigAction action;
        private float actionTime, actionDuration;
        private float swingSide = 1f;

        private Vector3 lastPosition;
        private float walkPhase, walkAmount;
        private Vector3 hipsRest;

        private void Start()
        {
            lastPosition = transform.position;
            if (Hips != null) hipsRest = Hips.localPosition;
            ApplyGlow(1f);
        }

        /// <summary>Plays an action whose length matches the gameplay timing that triggered it.</summary>
        public void Play(RigAction kind, float duration)
        {
            if (action == RigAction.Death && kind != RigAction.None) return;

            if (kind == RigAction.Swing) swingSide = -swingSide;
            action = kind;
            actionTime = 0f;
            actionDuration = Mathf.Max(0.05f, duration);
        }

        public RigAction Current => action;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // --- walk cycle from measured velocity ---
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float speed = delta.magnitude / dt;
            if (speed > 30f) speed = 0f; // a teleport, not a sprint

            walkAmount = Mathf.MoveTowards(walkAmount, Mathf.Clamp01(speed / 4f), dt * 4f);
            walkPhase += speed * dt / StrideLength * Mathf.PI * 2f;

            float swing = Mathf.Sin(walkPhase) * 35f * walkAmount;
            float bob = Mathf.Abs(Mathf.Cos(walkPhase)) * 0.05f * walkAmount;

            if (action != RigAction.None)
            {
                actionTime += dt;
                if (actionTime >= actionDuration && action != RigAction.Death) action = RigAction.None;
            }

            float t = actionDuration > 0f ? Mathf.Clamp01(actionTime / actionDuration) : 1f;

            // Rest pose plus walk.
            Vector3 armR = new Vector3(-swing * 0.6f, 0f, 6f);
            Vector3 armL = new Vector3(swing * 0.6f, 0f, -6f);
            Vector3 torso = new Vector3(4f * walkAmount, 0f, 0f);
            Vector3 head = Vector3.zero;
            Vector3 legL = new Vector3(swing, 0f, 0f);
            Vector3 legR = new Vector3(-swing, 0f, 0f);
            Vector3 hipsOffset = new Vector3(0f, bob, 0f);
            float glow = 1f;

            switch (action)
            {
                case RigAction.Windup:
                    // Weapon arm hauled up and back, body twisted away: the readable tell.
                    armR = Vector3.Lerp(armR, new Vector3(-150f, -30f * swingSide, 30f), Ease(t * 1.4f));
                    torso += new Vector3(-8f, -20f, 0f) * Ease(t);
                    glow = 1f + 3f * t;
                    break;

                case RigAction.Swing:
                    // Fast arc across the body: down and through.
                    float s = Ease(t);
                    armR = Vector3.Lerp(new Vector3(-150f, -30f * swingSide, 30f), new Vector3(-40f, 50f * swingSide, -10f), s);
                    torso += Vector3.Lerp(new Vector3(-8f, -20f, 0f), new Vector3(12f, 25f, 0f), s);
                    break;

                case RigAction.Slam:
                    // Both arms overhead for the first 70%, then down.
                    float up = Mathf.Clamp01(t / 0.7f), down = Mathf.Clamp01((t - 0.7f) / 0.3f);
                    var overhead = new Vector3(-170f, 0f, 0f);
                    var smashed = new Vector3(-50f, 0f, 0f);
                    armR = armL = Vector3.Lerp(Vector3.Lerp(armR, overhead, Ease(up)), smashed, down);
                    torso += Vector3.Lerp(new Vector3(-15f, 0f, 0f) * up, new Vector3(30f, 0f, 0f), down);
                    glow = 1f + 4f * up * (1f - down);
                    break;

                case RigAction.Stagger:
                    float k = Mathf.Sin(t * Mathf.PI);
                    torso += new Vector3(-25f, 0f, 0f) * k;
                    head += new Vector3(-20f, 0f, 0f) * k;
                    armR += new Vector3(-30f, 0f, 30f) * k;
                    armL += new Vector3(-30f, 0f, -30f) * k;
                    break;

                case RigAction.Bash:
                    float b = Mathf.Sin(Mathf.Clamp01(t * 1.5f) * Mathf.PI);
                    armL = Vector3.Lerp(armL, new Vector3(-85f, 25f, 0f), b);
                    torso += new Vector3(10f, 15f, 0f) * b;
                    break;

                case RigAction.Cast:
                    float c = Mathf.Sin(t * Mathf.PI);
                    armR = Vector3.Lerp(armR, new Vector3(-160f, 0f, 25f), c);
                    armL = Vector3.Lerp(armL, new Vector3(-160f, 0f, -25f), c);
                    head += new Vector3(-25f, 0f, 0f) * c;
                    glow = 1f + 4f * c;
                    break;

                case RigAction.Death:
                    // Crumple: knees go, torso folds forward, then the body sinks.
                    float d = Ease(t);
                    legL = legR = new Vector3(-70f, 0f, 0f) * d;
                    torso += new Vector3(55f, 0f, 0f) * d;
                    head += new Vector3(30f, 0f, 0f) * d;
                    armR = armL = new Vector3(-20f, 0f, 0f) * d;
                    hipsOffset = new Vector3(0f, -0.55f, 0f) * d * (hipsRest.y > 0f ? hipsRest.y / 0.95f : 1f);
                    glow = 1f - d;
                    break;
            }

            if (Blocking && action == RigAction.None)
            {
                armL = new Vector3(-75f, 35f, 0f);
                torso += new Vector3(6f, 10f, 0f);
            }

            if (Downed)
            {
                // Kneeling, slumped: visibly "revive me", not dead.
                legL = new Vector3(-90f, 0f, 0f);
                legR = new Vector3(-60f, 0f, 10f);
                torso = new Vector3(30f, 0f, 0f);
                armR = armL = new Vector3(-10f, 0f, 0f);
                hipsOffset = new Vector3(0f, -0.4f, 0f);
            }

            Set(ArmR, armR); Set(ArmL, armL); Set(Torso, torso); Set(Head, head);
            Set(LegL, legL); Set(LegR, legR);
            if (Hips != null) Hips.localPosition = hipsRest + hipsOffset;

            ApplyGlow(glow);
        }

        private static void Set(Transform t, Vector3 euler)
        {
            if (t != null) t.localRotation = Quaternion.Euler(euler);
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private float lastGlow = -1f;

        private void ApplyGlow(float intensity)
        {
            if (GlowRenderers == null || Mathf.Abs(intensity - lastGlow) < 0.02f) return;
            lastGlow = intensity;
            foreach (var r in GlowRenderers)
            {
                if (r != null) ArtLibrary.SetGlow(r, GlowColor * (2f * intensity));
            }
        }
    }
}
