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
        Draw = 8,     // bow raised and drawn — the archer's telegraph
    }

    /// <summary>The emote wheel's gestures (2026-11-01), played on a player's rig for everyone to see.</summary>
    public enum Emote : byte { None = 0, Wave = 1, Cheer = 2, Bow = 3, Laugh = 4, Dance = 5, Point = 6, Beckon = 7, Sit = 8, Salute = 9, Kneel = 10, Shrug = 11, Clap = 12 }

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

        /// <summary>Something held in the off hand that re-orients for an action (the archer's bow).</summary>
        public Transform OffhandItem;

        /// <summary>Renderers that glow (eyes, gems). Brightened during wind-ups.</summary>
        public Renderer[] GlowRenderers;
        public Color GlowColor = new Color(1f, 0.2f, 0.1f);

        /// <summary>Metres per walk cycle; bigger creatures take longer strides.</summary>
        public float StrideLength = 1.4f;

        public bool Blocking;
        /// <summary>A low guard: the shield held at the hip, angled forward (the class select Knight; the user: "lower the shield").</summary>
        public bool LowGuard;
        public bool Downed;
        /// <summary>0-3: how much mead is in them. The body sways and lists with it.</summary>
        public int Drunk;

        /// <summary>The gesture being played (an emote), until <see cref="emoteUntil"/>; walking off ends most of them.</summary>
        public Emote EmoteNow { get; private set; }
        private float emoteStart, emoteUntil;

        public void PlayEmote(Emote e, float seconds)
        {
            EmoteNow = e;
            emoteStart = Time.time;
            emoteUntil = Time.time + seconds;
        }

        /// <summary>Turn the head toward the local camera when it is near (enemies: they watch you).</summary>
        public bool WatchViewer;

        // Smoothing: each joint eases toward its target pose instead of snapping to it.
        private readonly Quaternion[] current = new Quaternion[6];
        private bool poseKnown;
        private Vector3 flinch;          // a decaying jolt from the last hit (torso pitch/yaw/roll)
        private float headYaw;           // the watching turn, eased
        private float seed;              // per-creature variety: idle rhythm, death direction
        private float nextTwitch, twitchUntil;
        private Vector3 twitch;

        /// <summary>A hit lands: jolt the body away from <paramref name="from"/> (world). Strength 0..1.</summary>
        public void Flinch(Vector3 from, float strength)
        {
            var local = transform.InverseTransformDirection(transform.position - from);
            local.y = 0f;
            if (local.sqrMagnitude < 0.001f) local = Vector3.back;
            local.Normalize();
            strength = Mathf.Clamp01(strength);
            // Pushed back: pitch away; hit from a side: roll and twist away from it.
            flinch += new Vector3(-local.z * 14f, local.x * 10f, -local.x * 12f) * (0.45f + strength);
        }

        private RigAction action;
        private float actionTime, actionDuration;
        private float swingSide = 1f;

        private Vector3 lastPosition;
        private float walkPhase, walkAmount;
        private Vector3 hipsRest;

        private void Start()
        {
            seed = (GetInstanceID() * 0.6180339f) % 1f;
            if (seed < 0f) seed += 1f;
            nextTwitch = Time.time + 2f + seed * 4f;
            lastPosition = transform.position;
            if (Hips != null) hipsRest = Hips.localPosition;
            ApplyGlow(1f);
        }

        private void Footfall(float speed)
        {
            float size = transform.lossyScale.y;
            bool heavy = size >= 1.4f;
            if (!heavy && speed < 5.5f) return;
            var cam = ViewCamera.Current;
            if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > (heavy ? 26f * 26f : 14f * 14f)) return;
            var foot = transform.position + Vector3.up * 0.05f;
            var dust = new Color(0.36f, 0.31f, 0.26f);
            Vfx.Embers(foot, heavy ? 5 : 2, dust, heavy ? 1.6f : 0.9f, heavy ? 0.07f : 0.045f, 0.6f);
            if (heavy) Sfx.Play(Sound.Step, foot, Mathf.Clamp01(0.25f * size), Mathf.Lerp(0.8f, 0.45f, Mathf.InverseLerp(1.4f, 2.2f, size)));
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
            float lastPhase = walkPhase;
            walkPhase += speed * dt / StrideLength * Mathf.PI * 2f;
            // A footfall every half cycle: heavy things thud and raise dust, runners scuff up a little.
            if (Mathf.Floor(walkPhase / Mathf.PI) != Mathf.Floor(lastPhase / Mathf.PI) && walkAmount > 0.5f) Footfall(speed);

            float swing = Mathf.Sin(walkPhase) * 35f * walkAmount;
            float bob = Mathf.Abs(Mathf.Cos(walkPhase)) * 0.05f * walkAmount;
            // Idle life: breathing, a slow weight shift, and (for the dead) the odd twitch.
            float time = Time.time + seed * 10f;
            float breath = Mathf.Sin(time * 1.6f) * (1f - walkAmount);
            float sway = Mathf.Sin(time * 0.45f) * (1f - walkAmount);
            if (Time.time >= nextTwitch)
            {
                nextTwitch = Time.time + 3f + Random.value * 6f;
                twitchUntil = Time.time + 0.18f;
                twitch = new Vector3(Random.Range(-12f, 12f), Random.Range(-25f, 25f), Random.Range(-15f, 15f));
            }
            var tic = Time.time < twitchUntil && action == RigAction.None && !Downed ? twitch : Vector3.zero;

            if (action != RigAction.None)
            {
                actionTime += dt;
                if (actionTime >= actionDuration && action != RigAction.Death) action = RigAction.None;
            }

            float t = actionDuration > 0f ? Mathf.Clamp01(actionTime / actionDuration) : 1f;

            // Rest pose plus walk.
            // Walking: arms counter the legs, the shoulders twist against the hips, the body leans into
            // its speed, and the head holds steady against the bob. At rest: breathing and sway.
            float twist = Mathf.Sin(walkPhase) * 7f * walkAmount;
            Vector3 armR = new Vector3(-swing * 0.65f - 4f * breath, 0f, 6f + 2f * breath);
            Vector3 armL = new Vector3(swing * 0.65f - 4f * breath, 0f, -6f - 2f * breath);
            Vector3 torso = new Vector3(4f * walkAmount + 6f * Mathf.Clamp01(speed / 6f) * walkAmount - 1.5f * breath, -twist, 2f * sway);
            Vector3 head = new Vector3(-3f * walkAmount + 1f * breath, twist * 0.8f, -1.5f * sway) + tic;
            Vector3 legL = new Vector3(swing, 0f, 0f);
            Vector3 legR = new Vector3(-swing, 0f, 0f);
            Vector3 hipsOffset = new Vector3(0.02f * sway, bob + 0.008f * breath, 0f);
            float glow = 1f;

            switch (action)
            {
                case RigAction.Windup:
                    // Weapon arm hauled up and back, body twisted away, knees sinking: the readable tell.
                    armR = Vector3.Lerp(armR, new Vector3(-150f, -30f * swingSide, 30f), Ease(t * 1.4f));
                    armL = Vector3.Lerp(armL, new Vector3(-35f, 20f, -25f), Ease(t * 1.4f));
                    torso += new Vector3(-10f, -24f, 0f) * Ease(t);
                    head += new Vector3(6f, 18f, 0f) * Ease(t);
                    hipsOffset += new Vector3(0f, -0.05f, -0.03f) * Ease(t);
                    // A tremble at full draw, just before it lets go.
                    if (t > 0.75f) armR += new Vector3(Mathf.Sin(Time.time * 60f) * 2.5f, 0f, 0f);
                    glow = 1f + 3f * t;
                    break;

                case RigAction.Swing:
                    // Fast arc across the body, overshooting a little before it settles: down and through.
                    float s = Ease(Mathf.Clamp01(t * 1.6f));
                    float over = Mathf.Sin(Mathf.Clamp01((t - 0.55f) / 0.45f) * Mathf.PI) * 0.18f;
                    armR = Vector3.LerpUnclamped(new Vector3(-150f, -30f * swingSide, 30f), new Vector3(-40f, 50f * swingSide, -10f), s + over);
                    torso += Vector3.LerpUnclamped(new Vector3(-10f, -24f, 0f), new Vector3(14f, 28f, 0f), s + over * 0.6f);
                    head += new Vector3(-4f, -10f, 0f) * s;
                    hipsOffset += new Vector3(0f, -0.04f, 0.05f) * Mathf.Sin(s * Mathf.PI);
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
                    // Rocked back on its heels, arms flung out, then catching itself.
                    float k = Mathf.Sin(t * Mathf.PI) * (1f - 0.3f * t);
                    torso += new Vector3(-25f, 0f, 6f * swingSide) * k;
                    head += new Vector3(-22f, 8f * swingSide, 0f) * k;
                    armR += new Vector3(-35f, 0f, 35f) * k;
                    armL += new Vector3(-35f, 0f, -35f) * k;
                    hipsOffset += new Vector3(0f, -0.03f, -0.08f) * k;
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

                case RigAction.Draw:
                    // Bow arm straight out at the target; string hand hauled back to the jaw.
                    float dr = Ease(t * 2f);
                    armL = Vector3.Lerp(armL, new Vector3(-88f, 0f, 0f), dr);
                    armR = Vector3.Lerp(armR, new Vector3(-95f, -60f, 0f), Ease(t * 1.3f));
                    torso += new Vector3(0f, -25f, 0f) * dr;
                    head += new Vector3(0f, 20f, 0f) * dr;
                    glow = 1f + 3f * t;
                    break;

                case RigAction.Death:
                    // Crumple: the knees go, the torso folds (forward for most, back for some), the head
                    // lolls, the arms go slack; then the body sinks.
                    float d = Ease(t);
                    float fold = seed < 0.35f ? -40f : 55f;
                    legL = new Vector3(-70f, 0f, -6f) * d;
                    legR = new Vector3(-62f, 0f, 8f) * d;
                    torso += new Vector3(fold, 10f * (seed - 0.5f), 12f * (seed - 0.5f)) * d;
                    head += new Vector3(fold > 0 ? 30f : -35f, 25f * (seed - 0.5f), 20f) * d;
                    armR = new Vector3(-20f, 0f, 35f) * d;
                    armL = new Vector3(-10f, 0f, -40f) * d;
                    hipsOffset = new Vector3(0f, -0.55f, 0f) * d * (hipsRest.y > 0f ? hipsRest.y / 0.95f : 1f);
                    glow = 1f - d;
                    break;
            }

            if (Blocking && action == RigAction.None)
            {
                armL = new Vector3(-75f, 35f, 0f);
                torso += new Vector3(6f, 10f, 0f);
            }
            else if (LowGuard && action == RigAction.None)
            {
                armL = new Vector3(-28f, 30f, 0f);
                torso += new Vector3(2f, 6f, 0f);
            }

            // An emote: a gesture over the walk (sitting and bowing hold still; walking off ends them, bar the dance).
            if (EmoteNow != Emote.None)
            {
                if (Time.time > emoteUntil || Downed || action == RigAction.Death || (walkAmount > 0.45f && EmoteNow != Emote.Dance)) EmoteNow = Emote.None;
                else PoseEmote(Time.time - emoteStart, ref armL, ref armR, ref torso, ref head, ref legL, ref legR, ref hipsOffset);
            }
            // Mead: a slow list from side to side, the head lolling after it.
            if (Drunk > 0 && !Downed)
            {
                float k = Drunk / 3f, tt = Time.time + seed * 7f;
                torso += new Vector3(3f * Mathf.Sin(tt * 0.7f) * k, 0f, 9f * Mathf.Sin(tt * 1.1f) * k);
                head += new Vector3(6f * Mathf.Sin(tt * 0.9f + 1f) * k, 0f, 12f * Mathf.Sin(tt * 1.1f - 0.6f) * k);
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

            // The bow hangs upright at rest; while drawing it turns level with the
            // raised arm so it faces the target.
            if (OffhandItem != null) OffhandItem.localRotation = Quaternion.Euler(action == RigAction.Draw ? 90f * Ease(t * 2f) : 0f, 0f, 0f);

            // A hit's jolt, decaying fast.
            torso += flinch;
            head += flinch * 0.6f;
            flinch = Vector3.Lerp(flinch, Vector3.zero, 1f - Mathf.Exp(-dt * 9f));

            // Watching: the head turns toward the local camera when it is near, within the neck's reach.
            float yawTarget = 0f;
            if (WatchViewer && action != RigAction.Death && !Downed)
            {
                var cam = ViewCamera.Current;
                if (cam != null)
                {
                    var to = cam.transform.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude < 14f * 14f && to.sqrMagnitude > 0.25f)
                        yawTarget = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, Vector3.up), -55f, 55f);
                }
            }
            headYaw = Mathf.Lerp(headYaw, yawTarget, 1f - Mathf.Exp(-dt * 4f));
            head.y += headYaw;

            // Ease each joint toward its target: quick for strikes, softer otherwise.
            float rate = action == RigAction.Swing || action == RigAction.Slam || action == RigAction.Bash ? 28f : action == RigAction.Death ? 10f : 14f;
            float a = poseKnown ? 1f - Mathf.Exp(-dt * rate) : 1f;
            Ease(ArmR, armR, 0, a); Ease(ArmL, armL, 1, a); Ease(Torso, torso, 2, a); Ease(Head, head, 3, a);
            Ease(LegL, legL, 4, a); Ease(LegR, legR, 5, a);
            if (Hips != null) Hips.localPosition = poseKnown ? Vector3.Lerp(Hips.localPosition, hipsRest + hipsOffset, Mathf.Max(a, 0.5f)) : hipsRest + hipsOffset;
            poseKnown = true;

            ApplyGlow(glow);
        }

        /// <summary>The emote gestures, as offsets and replacements of the walk pose. <paramref name="e"/> seconds in.</summary>
        private void PoseEmote(float e, ref Vector3 armL, ref Vector3 armR, ref Vector3 torso, ref Vector3 head, ref Vector3 legL, ref Vector3 legR, ref Vector3 hips)
        {
            float into = Ease(Mathf.Clamp01(e * 4f));
            switch (EmoteNow)
            {
                case Emote.Wave:
                    armR = Vector3.Lerp(armR, new Vector3(-150f, 0f, 30f + 22f * Mathf.Sin(e * 12f)), into);
                    head += new Vector3(-6f, 0f, 0f) * into;
                    break;
                case Emote.Cheer:
                {
                    float pump = Mathf.Abs(Mathf.Sin(e * 7f));
                    armR = Vector3.Lerp(armR, new Vector3(-165f + 25f * pump, 0f, 18f), into);
                    armL = Vector3.Lerp(armL, new Vector3(-165f + 25f * pump, 0f, -18f), into);
                    head += new Vector3(-22f, 0f, 0f) * into;
                    torso += new Vector3(-8f, 0f, 0f) * into;
                    hips += new Vector3(0f, 0.07f * pump, 0f) * into;
                    break;
                }
                case Emote.Bow:
                {
                    float b = Mathf.Sin(Mathf.Clamp01(e / 1.6f) * Mathf.PI);
                    torso += new Vector3(52f, 0f, 0f) * b;
                    head += new Vector3(20f, 0f, 0f) * b;
                    armR = Vector3.Lerp(armR, new Vector3(-45f, 0f, -55f), b);
                    armL = Vector3.Lerp(armL, new Vector3(25f, 0f, -10f), b);
                    break;
                }
                case Emote.Laugh:
                {
                    float shake = Mathf.Sin(e * 26f);
                    torso += new Vector3(-14f + 4f * shake, 0f, 0f) * into;
                    head += new Vector3(-26f + 6f * shake, 0f, 0f) * into;
                    armR = Vector3.Lerp(armR, new Vector3(-30f, 0f, -40f), into);
                    armL = Vector3.Lerp(armL, new Vector3(-30f, 0f, 40f), into);
                    break;
                }
                case Emote.Dance:
                {
                    // A tavern jig: arms up in turn, hips swinging, feet kicking out on the beat.
                    float beat = e * 7.2f;
                    float s = Mathf.Sin(beat);
                    armR = new Vector3(-120f - 40f * s, 0f, 30f);
                    armL = new Vector3(-120f + 40f * s, 0f, -30f);
                    torso += new Vector3(0f, 18f * s, 10f * s);
                    head += new Vector3(-8f, -10f * s, 0f);
                    legL = new Vector3(-35f * Mathf.Max(0f, s), 0f, -8f);
                    legR = new Vector3(-35f * Mathf.Max(0f, -s), 0f, 8f);
                    hips += new Vector3(0.05f * s, 0.06f * Mathf.Abs(Mathf.Cos(beat)), 0f);
                    break;
                }
                case Emote.Point:
                    armR = Vector3.Lerp(armR, new Vector3(-92f, 0f, 0f), into);
                    head += new Vector3(-4f, 0f, 0f) * into;
                    torso += new Vector3(4f, 0f, 0f) * into;
                    break;
                case Emote.Beckon:
                {
                    float curl = Mathf.Abs(Mathf.Sin(e * 6f));
                    armR = Vector3.Lerp(armR, new Vector3(-80f + 40f * curl, -20f, 0f), into);
                    torso += new Vector3(-10f, 0f, 0f) * into;
                    head += new Vector3(-10f, 0f, 6f) * into;
                    break;
                }
                case Emote.Sit:
                    legL = Vector3.Lerp(legL, new Vector3(-85f, 0f, -10f), into);
                    legR = Vector3.Lerp(legR, new Vector3(-85f, 0f, 10f), into);
                    torso += new Vector3(-6f, 0f, 0f) * into;
                    armR = Vector3.Lerp(armR, new Vector3(-40f, 0f, 10f), into);
                    armL = Vector3.Lerp(armL, new Vector3(-40f, 0f, -10f), into);
                    hips += new Vector3(0f, -0.5f * (hipsRest.y > 0f ? hipsRest.y / 0.95f : 1f), 0.12f) * into;
                    break;
                case Emote.Salute:
                    // Fist to the brow, chin up.
                    armR = Vector3.Lerp(armR, new Vector3(-150f, 30f, 75f), into);
                    head += new Vector3(-8f, 0f, 0f) * into;
                    torso += new Vector3(-4f, 0f, 0f) * into;
                    break;
                case Emote.Kneel:
                    // Down on one knee, head bowed, a hand on the raised knee.
                    legL = Vector3.Lerp(legL, new Vector3(-88f, 0f, -6f), into);
                    legR = Vector3.Lerp(legR, new Vector3(30f, 0f, 6f), into);
                    hips += new Vector3(0f, -0.38f * (hipsRest.y > 0f ? hipsRest.y / 0.95f : 1f), 0.05f) * into;
                    torso += new Vector3(14f, 0f, 0f) * into;
                    head += new Vector3(24f, 0f, 0f) * into;
                    armL = Vector3.Lerp(armL, new Vector3(-50f, 0f, -12f), into);
                    armR = Vector3.Lerp(armR, new Vector3(10f, 0f, 8f), into);
                    break;
                case Emote.Shrug:
                {
                    float up = Mathf.Sin(Mathf.Clamp01(e / 1.4f) * Mathf.PI);
                    armR = Vector3.Lerp(armR, new Vector3(-35f, 0f, 55f), up);
                    armL = Vector3.Lerp(armL, new Vector3(-35f, 0f, -55f), up);
                    head += new Vector3(0f, 0f, 14f) * up;
                    hips += new Vector3(0f, 0.04f, 0f) * up;
                    break;
                }
                case Emote.Clap:
                {
                    float c = Mathf.Abs(Mathf.Sin(e * 9f));
                    armR = Vector3.Lerp(armR, new Vector3(-78f, -30f + 22f * c, 0f), into);
                    armL = Vector3.Lerp(armL, new Vector3(-78f, 30f - 22f * c, 0f), into);
                    head += new Vector3(-6f, 0f, 0f) * into;
                    break;
                }
            }
        }

        private void Ease(Transform t, Vector3 euler, int slot, float a)
        {
            if (t == null) return;
            var target = Quaternion.Euler(euler);
            current[slot] = a >= 1f ? target : Quaternion.Slerp(current[slot], target, a);
            t.localRotation = current[slot];
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private float lastGlow = -1f;

        /// <summary>Re-apply the glow after <see cref="GlowColor"/> changes (enraged eyes).</summary>
        public void RefreshGlow() { float l = lastGlow; lastGlow = -1f; ApplyGlow(l < 0f ? 1f : l); }

        private void ApplyGlow(float intensity)
        {
            if (GlowRenderers == null || Mathf.Abs(intensity - lastGlow) < 0.02f) return;
            lastGlow = intensity;
            foreach (var r in GlowRenderers)
            {
                if (r != null) ArtLibrary.SetGlow(r, GlowColor * (1.5f * intensity));
            }
        }
    }
}
