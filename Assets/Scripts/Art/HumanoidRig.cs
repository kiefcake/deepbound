using UnityEngine;

namespace Deepbound.Art
{
    public enum RigAction : byte
    {
        None = 0,
        Windup = 1,
        Swing = 2,
        Slam = 3,
        Stagger = 4,
        Bash = 5,
        Death = 6,
        Cast = 7,
        Draw = 8,

        WindHeavy = 9,
        Cleave = 10,
        WindSweep = 11,
        SweepStrike = 12,
        Recover = 13,

        Charge = 14,
        Hurl = 15,
        Summon = 16,
        Channel = 17,

        Lob = 18,
        Thrust = 19,
        Pray = 20,
        Siphon = 21,
        Plant = 22,
        Leap = 23,
        Vanish = 24,
        Fan = 25,
        Roar = 26,
        Drink = 27,
        Lash = 28,
        Point = 29,
    }

    public enum Emote : byte { None = 0, Wave = 1, Cheer = 2, Bow = 3, Laugh = 4, Dance = 5, Point = 6, Beckon = 7, Sit = 8, Salute = 9, Kneel = 10, Shrug = 11, Clap = 12 }

    public class HumanoidRig : MonoBehaviour
    {
        public Transform Hips, Torso, Head, ArmL, ArmR, LegL, LegR;

        public Transform OffhandItem;

        public Renderer[] GlowRenderers;
        public Color GlowColor = new Color(1f, 0.2f, 0.1f);

        public float StrideLength = 1.4f;

        public bool Blocking;

        public bool LowGuard;

        public Transform GuardShield;
        public Vector3 GuardPos = new Vector3(-0.12f, 0.28f, 0.6f), GuardEuler = new Vector3(-4f, 12f, 0f);
        private Vector3 guardRestPos;
        private Quaternion guardRestRot;
        private float guardK;

        public void SetGuardShield(Transform mount, Vector3 pos, Vector3 euler)
        {
            GuardShield = mount;
            GuardPos = pos;
            GuardEuler = euler;
            guardRestPos = mount.localPosition;
            guardRestRot = mount.localRotation;
        }

        private void PoseGuardShield(float dt, bool raised)
        {
            if (GuardShield == null || Torso == null) return;
            guardK = Mathf.MoveTowards(guardK, raised ? 1f : 0f, dt * 7f);
            GuardShield.localPosition = guardRestPos;
            GuardShield.localRotation = guardRestRot;
            if (guardK <= 0f) return;
            float k = guardK * guardK * (3f - 2f * guardK);
            GuardShield.position = Vector3.Lerp(GuardShield.position, Torso.TransformPoint(GuardPos), k);
            GuardShield.rotation = Quaternion.Slerp(GuardShield.rotation, Torso.rotation * Quaternion.Euler(GuardEuler), k);
        }
        public bool Downed;

        public int Drunk;

        public Emote EmoteNow { get; private set; }
        private float emoteStart, emoteUntil;

        public bool Emoting => EmoteNow != Emote.None && Time.time < emoteUntil;

        public void PlayEmote(Emote e, float seconds)
        {
            EmoteNow = e;
            emoteStart = Time.time;
            emoteUntil = Time.time + seconds;
        }

        public bool WatchViewer;

        private readonly Quaternion[] current = new Quaternion[6];
        private bool poseKnown;
        private Vector3 flinch;
        private float headYaw;
        private float seed;
        private float nextTwitch, twitchUntil;
        private Vector3 twitch;

        public void Flinch(Vector3 from, float strength)
        {
            var local = transform.InverseTransformDirection(transform.position - from);
            local.y = 0f;
            if (local.sqrMagnitude < 0.001f) local = Vector3.back;
            local.Normalize();
            strength = Mathf.Clamp01(strength);

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
            var cam = ViewCamera.Current;
            if (cam == null) return;
            float far = (cam.transform.position - transform.position).sqrMagnitude;

            if (!heavy && far < StepEarshot * StepEarshot) SoftStep(size, speed);
            if (!heavy && speed < 5.5f) return;
            if (far > (heavy ? 26f * 26f : 14f * 14f)) return;
            var foot = transform.position + Vector3.up * 0.05f;
            var dust = new Color(0.36f, 0.31f, 0.26f);
            Vfx.Embers(foot, heavy ? 5 : 2, dust, heavy ? 1.6f : 0.9f, heavy ? 0.07f : 0.045f, 0.6f);
            if (heavy) Sfx.Play(Sound.Step, foot, Mathf.Clamp01(0.25f * size), Mathf.Lerp(0.8f, 0.45f, Mathf.InverseLerp(1.4f, 2.2f, size)));
        }

        public const float StepEarshot = 20f;

        public static int SoftSteps;
        private int stepper = -1;
        private Player.NetworkPlayer stepPlayer;

        private void SoftStep(float size, float speed)
        {
            if (stepper < 0)
            {
                stepPlayer = GetComponentInParent<Player.NetworkPlayer>();
                stepper = stepPlayer != null ? 2 : GetComponentInParent<Enemies.EnemyBrain>() != null ? 1 : 0;
            }
            if (stepper == 0 || speed < 1.2f) return;
            if (stepper == 2)
            {
                if (stepPlayer == null || stepPlayer.IsOwner) return;
                var life = stepPlayer.GetComponent<Player.PlayerLife>();
                if (life != null && (life.Hidden.Value || life.Downed.Value)) return;
            }
            bool camp = World.DungeonDirector.InLobby;
            float vol = (stepper == 2 ? 0.2f : 0.16f) * Mathf.Lerp(0.8f, 1.25f, Mathf.InverseLerp(4f, 7f, speed));
            float pitch = Mathf.Lerp(1.2f, 0.85f, Mathf.InverseLerp(0.7f, 1.35f, size));
            Sfx.Play(camp ? Sound.StepSoft : Sound.Step, transform.position + Vector3.up * 0.05f, vol, pitch);
            SoftSteps++;
        }

        public void Play(RigAction kind, float duration)
        {
            if (action == RigAction.Death && kind != RigAction.None) return;

            if (kind == RigAction.Swing || kind == RigAction.WindSweep) swingSide = -swingSide;
            action = kind;
            actionTime = 0f;
            actionDuration = Mathf.Max(0.05f, duration);
        }

        public RigAction Current => action;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float speed = delta.magnitude / dt;
            if (speed > 30f) speed = 0f;

            walkAmount = Mathf.MoveTowards(walkAmount, Mathf.Clamp01(speed / 4f), dt * 4f);
            float lastPhase = walkPhase;
            walkPhase += speed * dt / StrideLength * Mathf.PI * 2f;

            if (Mathf.Floor(walkPhase / Mathf.PI) != Mathf.Floor(lastPhase / Mathf.PI) && walkAmount > 0.5f) Footfall(speed);

            float swing = Mathf.Sin(walkPhase) * 35f * walkAmount;
            float bob = Mathf.Abs(Mathf.Cos(walkPhase)) * 0.05f * walkAmount;

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

                    armR = Vector3.Lerp(armR, new Vector3(-150f, -30f * swingSide, 30f), Ease(t * 1.4f));
                    armL = Vector3.Lerp(armL, new Vector3(-35f, 20f, -25f), Ease(t * 1.4f));
                    torso += new Vector3(-10f, -24f, 0f) * Ease(t);
                    head += new Vector3(6f, 18f, 0f) * Ease(t);
                    hipsOffset += new Vector3(0f, -0.05f, -0.03f) * Ease(t);

                    if (t > 0.75f) armR += new Vector3(Mathf.Sin(Time.time * 60f) * 2.5f, 0f, 0f);
                    glow = 1f + 3f * t;
                    break;

                case RigAction.Swing:

                    float s = Ease(Mathf.Clamp01(t * 1.6f));
                    float over = Mathf.Sin(Mathf.Clamp01((t - 0.55f) / 0.45f) * Mathf.PI) * 0.18f;
                    armR = Vector3.LerpUnclamped(new Vector3(-150f, -30f * swingSide, 30f), new Vector3(-40f, 50f * swingSide, -10f), s + over);
                    torso += Vector3.LerpUnclamped(new Vector3(-10f, -24f, 0f), new Vector3(14f, 28f, 0f), s + over * 0.6f);
                    head += new Vector3(-4f, -10f, 0f) * s;
                    hipsOffset += new Vector3(0f, -0.04f, 0.05f) * Mathf.Sin(s * Mathf.PI);
                    break;

                case RigAction.Slam:

                    float up = Mathf.Clamp01(t / 0.7f), down = Mathf.Clamp01((t - 0.7f) / 0.3f);
                    var overhead = new Vector3(-170f, 0f, 0f);
                    var smashed = new Vector3(-50f, 0f, 0f);
                    armR = armL = Vector3.Lerp(Vector3.Lerp(armR, overhead, Ease(up)), smashed, down);
                    torso += Vector3.Lerp(new Vector3(-15f, 0f, 0f) * up, new Vector3(30f, 0f, 0f), down);
                    glow = 1f + 4f * up * (1f - down);
                    break;

                case RigAction.WindHeavy:
                {
                    float h = Ease(Mathf.Clamp01(t * 1.25f));
                    var high = new Vector3(-175f, 0f, 0f);
                    armR = Vector3.Lerp(armR, high + new Vector3(0f, 0f, 8f), h);
                    armL = Vector3.Lerp(armL, high + new Vector3(0f, 0f, -8f), h);
                    torso += new Vector3(-18f, 0f, 0f) * h;
                    head += new Vector3(-12f, 0f, 0f) * h;
                    hipsOffset += new Vector3(0f, 0.04f, -0.04f) * h;
                    if (t > 0.7f) { armR.x += Mathf.Sin(Time.time * 55f) * 3f; armL.x = armR.x; }
                    glow = 1f + 4f * t;
                    break;
                }

                case RigAction.Cleave:
                {
                    float c2 = Ease(Mathf.Clamp01(t * 2.2f));
                    float settle = Mathf.Clamp01((t - 0.55f) / 0.45f);
                    var cleaveDown = new Vector3(-35f, 0f, 0f);
                    armR = Vector3.LerpUnclamped(new Vector3(-175f, 0f, 8f), cleaveDown + new Vector3(0f, 0f, 6f), c2);
                    armL = Vector3.LerpUnclamped(new Vector3(-175f, 0f, -8f), cleaveDown + new Vector3(0f, 0f, -6f), c2);
                    torso += Vector3.Lerp(new Vector3(-18f, 0f, 0f), new Vector3(34f, 0f, 0f), c2) * (1f - 0.5f * settle);
                    head += new Vector3(-10f, 0f, 0f) * c2;
                    legL += new Vector3(-22f, 0f, 0f) * c2 * (1f - settle);
                    legR += new Vector3(-22f, 0f, 0f) * c2 * (1f - settle);
                    hipsOffset += new Vector3(0f, -0.12f, 0.06f) * c2 * (1f - 0.6f * settle);
                    glow = 1f + 3f * (1f - c2);
                    break;
                }

                case RigAction.WindSweep:
                {
                    float w2 = Ease(Mathf.Clamp01(t * 1.3f));
                    armR = Vector3.Lerp(armR, new Vector3(-80f, -95f * swingSide, 55f), w2);
                    armL = Vector3.Lerp(armL, new Vector3(-60f, -40f * swingSide, -30f), w2);
                    torso += new Vector3(12f, -55f * swingSide, 0f) * w2;
                    head += new Vector3(-6f, 45f * swingSide, 0f) * w2;
                    legL += new Vector3(-18f, 0f, -6f) * w2;
                    legR += new Vector3(-18f, 0f, 6f) * w2;
                    hipsOffset += new Vector3(0f, -0.1f, 0f) * w2;
                    if (t > 0.75f) torso.y += Mathf.Sin(Time.time * 50f) * 2f;
                    glow = 1f + 3f * t;
                    break;
                }

                case RigAction.SweepStrike:
                {
                    float s2 = Ease(Mathf.Clamp01(t * 1.7f));
                    float over2 = Mathf.Sin(Mathf.Clamp01((t - 0.5f) / 0.5f) * Mathf.PI) * 0.15f;
                    armR = Vector3.LerpUnclamped(new Vector3(-80f, -95f * swingSide, 55f), new Vector3(-80f, 105f * swingSide, -20f), s2 + over2);
                    armL = Vector3.Lerp(new Vector3(-60f, -40f * swingSide, -30f), armL, s2);
                    torso += Vector3.LerpUnclamped(new Vector3(12f, -55f * swingSide, 0f), new Vector3(8f, 60f * swingSide, 0f), s2 + over2 * 0.7f);
                    head += Vector3.Lerp(new Vector3(-6f, 45f * swingSide, 0f), new Vector3(0f, -20f * swingSide, 0f), s2);
                    hipsOffset += new Vector3(0f, -0.1f, 0f) * (1f - s2) + new Vector3(0f, -0.03f, 0.04f) * Mathf.Sin(s2 * Mathf.PI);
                    break;
                }

                case RigAction.Charge:
                {
                    float c3 = Ease(Mathf.Clamp01(t * 2f));
                    armL = Vector3.Lerp(armL, new Vector3(-70f, 25f, -10f), c3);
                    armR = Vector3.Lerp(armR, new Vector3(-35f, -10f, 25f), c3);
                    torso += new Vector3(32f, -10f, 0f) * c3;
                    head += new Vector3(-26f, 8f, 0f) * c3;
                    legL += new Vector3(-30f, 0f, 0f) * c3;
                    legR += new Vector3(25f, 0f, 0f) * c3 + new Vector3(Mathf.Sin(Time.time * 14f) * 8f * Mathf.Clamp01(t * 3f - 1f), 0f, 0f);
                    hipsOffset += new Vector3(0f, -0.12f, -0.04f) * c3;
                    if (t > 0.7f) torso.z += Mathf.Sin(Time.time * 45f) * 1.5f;
                    glow = 1f + 3.5f * t;
                    break;
                }

                case RigAction.Hurl:
                {
                    float draw = Ease(Mathf.Clamp01(t / 0.62f)), fling = Ease(Mathf.Clamp01((t - 0.62f) / 0.18f));
                    var back = new Vector3(-155f, -45f, 35f);
                    var thrown = new Vector3(-88f, 15f, 0f);
                    armR = Vector3.LerpUnclamped(Vector3.Lerp(armR, back, draw), thrown, fling);
                    armL = Vector3.Lerp(armL, new Vector3(-75f, 20f, -10f), draw) * (1f - 0.5f * fling);
                    torso += Vector3.Lerp(new Vector3(-8f, -32f, 0f) * draw, new Vector3(16f, 26f, 0f), fling);
                    head += new Vector3(4f, 22f, 0f) * draw * (1f - fling);
                    hipsOffset += new Vector3(0f, -0.04f, 0.05f * fling) * draw;
                    glow = 1f + 3f * draw * (1f - fling);
                    break;
                }

                case RigAction.Summon:
                {
                    float low = Ease(Mathf.Clamp01(t / 0.55f)), rise = Ease(Mathf.Clamp01((t - 0.55f) / 0.35f));
                    var pull = new Vector3(-25f, 0f, 45f);
                    var raisedUp = new Vector3(-165f, 0f, 30f);
                    armR = Vector3.Lerp(Vector3.Lerp(armR, pull, low), raisedUp, rise);
                    armL = Vector3.Lerp(Vector3.Lerp(armL, new Vector3(pull.x, 0f, -pull.z), low), new Vector3(raisedUp.x, 0f, -raisedUp.z), rise);
                    torso += Vector3.Lerp(new Vector3(30f, 0f, 0f) * low, new Vector3(-14f, 0f, 0f), rise);
                    head += Vector3.Lerp(new Vector3(20f, 0f, 0f) * low, new Vector3(-24f, 0f, 0f), rise);
                    legL += new Vector3(-35f, 0f, 0f) * low * (1f - rise);
                    legR += new Vector3(-35f, 0f, 0f) * low * (1f - rise);
                    hipsOffset += new Vector3(0f, -0.18f * low * (1f - rise) + 0.04f * rise, 0f);
                    glow = 1f + 4f * Mathf.Max(low * 0.5f, rise);
                    break;
                }

                case RigAction.Channel:
                {
                    float ch = Ease(Mathf.Clamp01(t * Mathf.Max(1f, actionDuration / 0.6f))) * (1f - Ease(Mathf.Clamp01((t - 0.92f) / 0.08f)));
                    float sway2 = Mathf.Sin(Time.time * 2.2f);
                    armR = Vector3.Lerp(armR, new Vector3(-20f + 6f * sway2, 0f, 78f), ch);
                    armL = Vector3.Lerp(armL, new Vector3(-20f - 6f * sway2, 0f, -78f), ch);
                    torso += new Vector3(-8f, 0f, 4f * sway2) * ch;
                    head += new Vector3(-22f, 0f, 0f) * ch;
                    hipsOffset += new Vector3(0f, 0.06f + 0.03f * Mathf.Sin(Time.time * 3f), 0f) * ch;
                    glow = 1f + (2.5f + 1.5f * Mathf.Sin(Time.time * 6f)) * ch;
                    break;
                }

                case RigAction.Recover:
                {
                    float r = Ease(Mathf.Clamp01(t * 5f)) * (1f - Ease(Mathf.Clamp01((t - 0.85f) / 0.15f)));
                    float heave = Mathf.Sin(Time.time * 7f) * 3f;
                    armR = Vector3.Lerp(armR, new Vector3(-4f, 18f * swingSide, 22f), r);
                    armL = Vector3.Lerp(armL, new Vector3(-30f, 10f, -6f), r);
                    torso += new Vector3(34f + heave, 10f * swingSide, 5f * swingSide) * r;
                    head += new Vector3(22f - heave, -6f * swingSide, 0f) * r;
                    legL += new Vector3(-16f, 0f, 0f) * r;
                    legR += new Vector3(-16f, 0f, 0f) * r;
                    hipsOffset += new Vector3(0f, -0.1f, -0.03f) * r;
                    glow = Mathf.Lerp(1f, 0.3f, r);
                    break;
                }

                case RigAction.Stagger:

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

                    float dr = Ease(t * 2f);
                    armL = Vector3.Lerp(armL, new Vector3(-88f, 0f, 0f), dr);
                    armR = Vector3.Lerp(armR, new Vector3(-95f, -60f, 0f), Ease(t * 1.3f));
                    torso += new Vector3(0f, -25f, 0f) * dr;
                    head += new Vector3(0f, 20f, 0f) * dr;
                    glow = 1f + 3f * t;
                    break;

                case RigAction.Lob:
                {
                    float back = Ease(Mathf.Clamp01(t / 0.4f)), fling = Ease(Mathf.Clamp01((t - 0.4f) / 0.35f)), settle = Mathf.Clamp01((t - 0.75f) / 0.25f);
                    var cocked = new Vector3(-165f, -15f, 35f);
                    var thrown = new Vector3(-55f, 20f, 5f);
                    armR = Vector3.Lerp(Vector3.Lerp(armR, cocked, back), thrown, fling);
                    armR = Vector3.Lerp(armR, new Vector3(-20f, 0f, 8f), settle * 0.6f);
                    armL = Vector3.Lerp(armL, new Vector3(-70f, -10f, -10f), back * (1f - settle));
                    torso += new Vector3(-12f * back + 22f * fling, -18f * back + 22f * fling, 0f) * (1f - 0.5f * settle);
                    head += new Vector3(4f * back - 6f * fling, 10f * back - 10f * fling, 0f);
                    legL += new Vector3(-18f, 0f, 0f) * fling * (1f - settle);
                    break;
                }

                case RigAction.Thrust:
                {
                    float push = Ease(Mathf.Clamp01(t * 2.5f)), hold = 1f - Ease(Mathf.Clamp01((t - 0.55f) / 0.45f));
                    float k2 = push * hold;
                    armR = Vector3.Lerp(armR, new Vector3(-92f, 12f, -4f), k2);
                    armL = Vector3.Lerp(armL, new Vector3(-92f, -12f, 4f), k2);
                    torso += new Vector3(14f, 0f, 0f) * k2;
                    head += new Vector3(-6f, 0f, 0f) * k2;
                    legL += new Vector3(-24f, 0f, 0f) * k2;
                    legR += new Vector3(14f, 0f, 0f) * k2;
                    hipsOffset += new Vector3(0f, -0.06f, 0.08f) * k2;
                    glow = 1f + 3f * k2;
                    break;
                }

                case RigAction.Pray:
                {
                    float pr = Ease(Mathf.Clamp01(t * 3f)) * (1f - Ease(Mathf.Clamp01((t - 0.8f) / 0.2f)));
                    armR = Vector3.Lerp(armR, new Vector3(-70f, -42f, -12f), pr);
                    armL = Vector3.Lerp(armL, new Vector3(-70f, 42f, 12f), pr);
                    torso += new Vector3(10f, 0f, 0f) * pr;
                    head += new Vector3(28f, 0f, 0f) * pr;
                    legL += new Vector3(-10f, 0f, 0f) * pr;
                    legR += new Vector3(-10f, 0f, 0f) * pr;
                    hipsOffset += new Vector3(0f, -0.06f, 0f) * pr;
                    glow = 1f + 2.5f * pr;
                    break;
                }

                case RigAction.Siphon:
                {
                    float reach = Ease(Mathf.Clamp01(t / 0.3f)), haul = Ease(Mathf.Clamp01((t - 0.3f) / 0.45f)), let = Ease(Mathf.Clamp01((t - 0.8f) / 0.2f));
                    var outR = new Vector3(-95f, -10f, 40f); var outL = new Vector3(-95f, 10f, -40f);
                    var inR = new Vector3(-55f, -48f, -5f); var inL = new Vector3(-55f, 48f, 5f);
                    armR = Vector3.Lerp(armR, Vector3.Lerp(outR, inR, haul), reach * (1f - let));
                    armL = Vector3.Lerp(armL, Vector3.Lerp(outL, inL, haul), reach * (1f - let));

                    if (haul < 1f) { armR.x += Mathf.Sin(Time.time * 40f) * 3f * reach; armL.x -= Mathf.Sin(Time.time * 40f) * 3f * reach; }
                    torso += new Vector3(-6f * reach + 26f * haul, 0f, 0f) * (1f - let);
                    head += new Vector3(-10f * reach + 18f * haul, 0f, 0f) * (1f - let);
                    hipsOffset += new Vector3(0f, -0.08f, 0f) * haul * (1f - let);
                    glow = 1f + 3f * haul * (1f - let);
                    break;
                }

                case RigAction.Plant:
                {
                    float lift = Ease(Mathf.Clamp01(t / 0.35f)), drive = Ease(Mathf.Clamp01((t - 0.35f) / 0.2f)), rise = Ease(Mathf.Clamp01((t - 0.75f) / 0.25f));
                    var high = new Vector3(-170f, 0f, 8f);
                    var low = new Vector3(-60f, 0f, 4f);
                    var arm = Vector3.Lerp(Vector3.Lerp(armR, high, lift), low, drive);
                    armR = Vector3.Lerp(arm, armR, rise);
                    armL = Vector3.Lerp(Vector3.Lerp(armL, high + new Vector3(0f, 0f, -16f), lift), low + new Vector3(0f, 0f, -8f), drive);
                    armL = Vector3.Lerp(armL, new Vector3(-10f, 0f, -6f), rise);
                    float kneel = drive * (1f - rise);
                    torso += new Vector3(-8f * lift * (1f - drive) + 24f * kneel, 0f, 0f);
                    legL += new Vector3(-80f, 0f, 0f) * kneel;
                    legR += new Vector3(10f, 0f, 0f) * kneel;
                    hipsOffset += new Vector3(0f, -0.32f, 0f) * kneel;
                    glow = 1f + 3f * kneel;
                    break;
                }

                case RigAction.Leap:
                {
                    float crouch = Ease(Mathf.Clamp01(t / 0.35f)), spring = Ease(Mathf.Clamp01((t - 0.35f) / 0.3f)), land = Mathf.Clamp01((t - 0.75f) / 0.25f);
                    float c2 = crouch * (1f - spring);
                    armR = Vector3.Lerp(Vector3.Lerp(armR, new Vector3(40f, 0f, 20f), c2), new Vector3(-165f, 0f, 15f), spring * (1f - land));
                    armL = Vector3.Lerp(Vector3.Lerp(armL, new Vector3(40f, 0f, -20f), c2), new Vector3(-165f, 0f, -15f), spring * (1f - land));
                    torso += new Vector3(30f * c2 - 10f * spring * (1f - land), 0f, 0f);
                    head += new Vector3(-20f * c2 - 15f * spring * (1f - land), 0f, 0f);
                    legL += new Vector3(-60f, 0f, 0f) * c2 + new Vector3(-20f, 0f, 0f) * spring * (1f - land);
                    legR += new Vector3(-60f, 0f, 0f) * c2 + new Vector3(15f, 0f, 0f) * spring * (1f - land);
                    hipsOffset += new Vector3(0f, -0.28f * c2 + 0.06f * spring * (1f - land), 0f);
                    break;
                }

                case RigAction.Vanish:
                {
                    float v = Ease(Mathf.Clamp01(t * 3f)) * (1f - Ease(Mathf.Clamp01((t - 0.75f) / 0.25f)));
                    armL = Vector3.Lerp(armL, new Vector3(-100f, 55f, 0f), v);
                    armR = Vector3.Lerp(armR, new Vector3(20f, 0f, 25f), v);
                    torso += new Vector3(30f, 10f, 0f) * v;
                    head += new Vector3(18f, -15f, 0f) * v;
                    legL += new Vector3(-55f, 0f, 0f) * v;
                    legR += new Vector3(-40f, 0f, 0f) * v;
                    hipsOffset += new Vector3(0f, -0.3f, 0f) * v;
                    break;
                }

                case RigAction.Fan:
                {
                    float coil = Ease(Mathf.Clamp01(t / 0.35f)), loose = Ease(Mathf.Clamp01((t - 0.35f) / 0.25f)), rest = Ease(Mathf.Clamp01((t - 0.75f) / 0.25f));
                    var cocked = new Vector3(-80f, 70f, -20f);
                    var flung = new Vector3(-85f, -60f, 40f);
                    armR = Vector3.Lerp(Vector3.Lerp(Vector3.Lerp(armR, cocked, coil), flung, loose), armR, rest);
                    torso += new Vector3(6f, 26f * coil - 52f * loose, 0f) * (1f - rest);
                    head += new Vector3(0f, -18f * coil + 26f * loose, 0f) * (1f - rest);
                    hipsOffset += new Vector3(0f, -0.04f, 0f) * coil * (1f - rest);
                    break;
                }

                case RigAction.Roar:
                {
                    float r2 = Ease(Mathf.Clamp01(t * 3.5f)) * (1f - Ease(Mathf.Clamp01((t - 0.7f) / 0.3f)));
                    float shake = Mathf.Sin(Time.time * 50f) * 2f * r2;
                    armR = Vector3.Lerp(armR, new Vector3(30f, 0f, 45f), r2);
                    armL = Vector3.Lerp(armL, new Vector3(30f, 0f, -45f), r2);
                    torso += new Vector3(-20f + shake, 0f, 0f) * r2;
                    head += new Vector3(-30f + shake, 0f, 0f) * r2;
                    legL += new Vector3(-14f, 0f, -8f) * r2;
                    legR += new Vector3(10f, 0f, 8f) * r2;
                    hipsOffset += new Vector3(0f, -0.08f, 0f) * r2;
                    glow = 1f + 3.5f * r2;
                    break;
                }

                case RigAction.Drink:
                {
                    float up2 = Ease(Mathf.Clamp01(t / 0.3f)) * (1f - Ease(Mathf.Clamp01((t - 0.8f) / 0.2f)));
                    float tip = Ease(Mathf.Clamp01((t - 0.25f) / 0.3f)) * (1f - Ease(Mathf.Clamp01((t - 0.8f) / 0.2f)));
                    armR = Vector3.Lerp(armR, new Vector3(-130f, -55f, -15f), up2);
                    head += new Vector3(-30f, 0f, 0f) * tip;
                    torso += new Vector3(-8f, 0f, 0f) * tip;
                    break;
                }

                case RigAction.Lash:
                {
                    float wind = Ease(Mathf.Clamp01(t / 0.35f)), strike = Ease(Mathf.Clamp01((t - 0.35f) / 0.2f)), ease2 = Ease(Mathf.Clamp01((t - 0.7f) / 0.3f));
                    var overhead2 = new Vector3(-175f, 20f, -10f);
                    var across = new Vector3(-150f, 60f, -40f);
                    armR = Vector3.Lerp(Vector3.Lerp(Vector3.Lerp(armR, overhead2, wind), across, strike), armR, ease2);
                    float wince = strike * (1f - ease2);
                    torso += new Vector3(-6f * wind + 22f * wince, 0f, -8f * wince);
                    head += new Vector3(20f, 14f, 0f) * wince;
                    hipsOffset += new Vector3(0f, -0.05f, 0f) * wince;
                    glow = 1f + 2f * wince;
                    break;
                }

                case RigAction.Point:
                {
                    float pt = Ease(Mathf.Clamp01(t * 3.5f)) * (1f - Ease(Mathf.Clamp01((t - 0.8f) / 0.2f)));
                    armR = Vector3.Lerp(armR, new Vector3(-90f, -5f, 0f), pt);
                    armL = Vector3.Lerp(armL, new Vector3(-20f, 0f, -25f), pt);
                    torso += new Vector3(0f, -28f, 0f) * pt;
                    head += new Vector3(0f, 24f, 0f) * pt;
                    glow = 1f + 2f * pt;
                    break;
                }

                case RigAction.Death:

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
                armL = new Vector3(-70f, 16f, 0f);
                torso += new Vector3(5f, 8f, 0f);
            }
            else if (LowGuard && action == RigAction.None)
            {
                armL = new Vector3(-28f, 30f, 0f);
                torso += new Vector3(2f, 6f, 0f);
            }

            if (EmoteNow != Emote.None)
            {
                if (Time.time > emoteUntil || Downed || action == RigAction.Death || (walkAmount > 0.45f && EmoteNow != Emote.Dance)) EmoteNow = Emote.None;
                else PoseEmote(Time.time - emoteStart, ref armL, ref armR, ref torso, ref head, ref legL, ref legR, ref hipsOffset);
            }

            if (Drunk > 0 && !Downed)
            {
                float k = Drunk / 3f, tt = Time.time + seed * 7f;
                torso += new Vector3(3f * Mathf.Sin(tt * 0.7f) * k, 0f, 9f * Mathf.Sin(tt * 1.1f) * k);
                head += new Vector3(6f * Mathf.Sin(tt * 0.9f + 1f) * k, 0f, 12f * Mathf.Sin(tt * 1.1f - 0.6f) * k);
            }

            if (Downed)
            {
                legL = new Vector3(-90f, 0f, 0f);
                legR = new Vector3(-60f, 0f, 10f);
                torso = new Vector3(30f, 0f, 0f);
                armR = armL = new Vector3(-10f, 0f, 0f);
                hipsOffset = new Vector3(0f, -0.4f, 0f);
            }

            if (OffhandItem != null) OffhandItem.localRotation = Quaternion.Euler(action == RigAction.Draw ? 90f * Ease(t * 2f) : 0f, 0f, 0f);

            torso += flinch;
            head += flinch * 0.6f;
            flinch = Vector3.Lerp(flinch, Vector3.zero, 1f - Mathf.Exp(-dt * 9f));

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

            float rate = action == RigAction.Swing || action == RigAction.Slam || action == RigAction.Bash || action == RigAction.Lob || action == RigAction.Thrust || action == RigAction.Fan || action == RigAction.Lash ? 28f : action == RigAction.Death ? 10f : 14f;
            float a = poseKnown ? 1f - Mathf.Exp(-dt * rate) : 1f;
            Ease(ArmR, armR, 0, a); Ease(ArmL, armL, 1, a); Ease(Torso, torso, 2, a); Ease(Head, head, 3, a);
            Ease(LegL, legL, 4, a); Ease(LegR, legR, 5, a);
            if (Hips != null) Hips.localPosition = poseKnown ? Vector3.Lerp(Hips.localPosition, hipsRest + hipsOffset, Mathf.Max(a, 0.5f)) : hipsRest + hipsOffset;
            poseKnown = true;
            PoseGuardShield(dt, Blocking && action == RigAction.None && !Downed);

            ApplyGlow(glow);
        }

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

                    armR = Vector3.Lerp(armR, new Vector3(-150f, 30f, 75f), into);
                    head += new Vector3(-8f, 0f, 0f) * into;
                    torso += new Vector3(-4f, 0f, 0f) * into;
                    break;
                case Emote.Kneel:

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
