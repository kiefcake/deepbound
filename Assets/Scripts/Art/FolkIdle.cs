using UnityEngine;

namespace Deepbound.Art
{
    public class FolkIdle : MonoBehaviour
    {
        public enum WorkKind { None, Hammer, Sew, Drill, Trace, Wipe, Drink }

        private Transform neck;
        private float seed, yaw, pitch, nextGlance, glanceYaw;
        private Vector3 baseScale;

        public float RestPitch;

        public bool Prays;
        public Vector3 PrayAt, IncenseAt;

        public World.FolkId? Who;
        public string DisplayName, Trade;

        public bool TurnsBody = true;
        public WorkKind Work;
        public Transform ArmR, ArmL;

        public Transform Striker;
        private float nextMote, nextSmoke;
        private float jolt, bodyYaw, workBlend, lastCycle;
        private Quaternion awayRot;
        private bool awayRotSet;
        private Vector3 joltAway;
        private Quaternion baseRot;
        private Vector3 homeLocal;

        private Vector3 HomePos => transform.parent != null ? transform.parent.TransformPoint(homeLocal) : homeLocal;
        private bool away;
        public static readonly System.Collections.Generic.List<FolkIdle> All = new System.Collections.Generic.List<FolkIdle>();

        public const float AttendRange = 3.8f, BodyTurnRange = 4.5f;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public static FolkIdle Near(Vector3 at)
        {
            FolkIdle best = null; float bd = 9f;
            foreach (var f in All)
            {
                if (f == null) continue;
                var d = f.transform.position - at; d.y = 0f;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = f; }
            }
            return best;
        }

        public static FolkIdle Of(World.FolkId who)
        {
            foreach (var f in All) if (f != null && f.Who == who) return f;
            return null;
        }

        public Vector3 HeadTop => (neck != null ? neck.position : transform.position + Vector3.up * 1.5f) + Vector3.up * 0.55f;

        public void Flinch(Vector3 from, float strength = 1f)
        {
            var away = transform.position - from; away.y = 0f;
            joltAway = away.sqrMagnitude > 0.01f ? away.normalized : -transform.forward;
            jolt = Mathf.Max(jolt, strength);
        }

        public static FolkIdle Attach(Transform root, float neckY, params string[] headParts)
        {
            var neck = new GameObject("Neck").transform;
            neck.SetParent(root, false);
            neck.localPosition = new Vector3(0f, neckY, 0f);
            var move = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in root)
                foreach (var n in headParts)
                    if (c != neck && c.name == n) { move.Add(c); break; }
            foreach (var c in move) c.SetParent(neck, true);
            var idle = root.gameObject.AddComponent<FolkIdle>();
            idle.neck = neck;
            return idle;
        }

        private void Start()
        {
            seed = Random.value * 20f;
            baseScale = transform.localScale;
            baseRot = transform.localRotation;
            homeLocal = transform.localPosition;
            nextGlance = Time.time + Random.Range(1f, 4f);
        }

        private const float TalkFaceRange = 6f;

        private static Vector3 NearestPlayer(Vector3 from)
        {
            Vector3 best = Vector3.zero;
            float bestD = TalkFaceRange * TalkFaceRange;
            foreach (var p in Player.PlayerLife.All)
            {
                if (p == null) continue;
                var to = p.transform.position - from;
                to.y = 0f;
                if (to.sqrMagnitude < bestD) { bestD = to.sqrMagnitude; best = to; }
            }
            return best;
        }

        private void Update()
        {
            float t = Time.time + seed;
            var cam = ViewCamera.Current;

            var pose = default(World.FolkRounds.Pose);
            if (Who.HasValue && World.DungeonDirector.InLobby && World.FolkRounds.Walks(Who.Value))
            {
                pose = World.FolkRounds.At(Who.Value);
                World.FolkRounds.Chatter(Who.Value, pose);
            }
            if (pose.Away && Who.HasValue && World.DungeonDirector.Instance != null && World.DungeonDirector.Instance.IsFolkHeld(Who.Value))
            {
                pose.Walking = false;
                var talker = NearestPlayer(pose.Position);
                if (talker.sqrMagnitude > 0.01f) pose.Facing = talker;
            }
            away = pose.Away;
            if (away) transform.position = new Vector3(pose.Position.x, HomePos.y + (pose.Walking ? Mathf.Abs(Mathf.Sin(t * 6.5f)) * 0.035f : 0f), pose.Position.z);
            else transform.localPosition = homeLocal;
            float camDist = cam != null ? Flat(cam.transform.position - transform.position) : 99f;

            transform.localScale = new Vector3(baseScale.x * (1f + Mathf.Sin(t * 1.4f) * 0.006f), baseScale.y * (1f + Mathf.Sin(t * 1.4f) * 0.009f), baseScale.z);

            float wantBody = 0f;
            if (TurnsBody && !away && cam != null && camDist < BodyTurnRange)
            {
                var restFwd = (transform.parent != null ? transform.parent.rotation : Quaternion.identity) * baseRot * Vector3.forward;
                var to = cam.transform.position - transform.position; to.y = 0f;
                float a = Vector3.SignedAngle(restFwd, to, Vector3.up);
                if (Mathf.Abs(a) < 120f) wantBody = Mathf.Clamp(a, -70f, 70f);
            }
            bodyYaw = Mathf.Lerp(bodyYaw, wantBody, 1f - Mathf.Exp(-Time.deltaTime * 2.5f));

            jolt = Mathf.MoveTowards(jolt, 0f, Time.deltaTime * 2.5f);
            var axis = Vector3.Cross(Vector3.up, joltAway);
            var lean = jolt > 0f ? Quaternion.AngleAxis(Mathf.Sin(jolt * Mathf.PI) * 14f * jolt, transform.parent != null ? transform.parent.InverseTransformDirection(axis) : axis) : Quaternion.identity;
            var restRot = baseRot;
            if (away && pose.Facing.sqrMagnitude > 0.01f)
                restRot = (transform.parent != null ? Quaternion.Inverse(transform.parent.rotation) : Quaternion.identity) * Quaternion.LookRotation(pose.Facing);
            if (!awayRotSet) { awayRot = restRot; awayRotSet = true; }
            awayRot = Quaternion.Slerp(awayRot, restRot, 1f - Mathf.Exp(-Time.deltaTime * 5f));
            transform.localRotation = lean * awayRot * Quaternion.Euler(0f, bodyYaw, 0f);
            TickWork(t, away ? 0f : camDist);
            if (away && pose.Walking)
            {
                float swing = Mathf.Sin(t * 6.5f) * 20f;
                if (ArmR != null) ArmR.localRotation = Quaternion.Euler(swing, 0f, 0f);
                if (ArmL != null) ArmL.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            }
            if (neck == null) return;

            float wantYaw = glanceYaw, wantPitch = RestPitch;
            bool watching = false;
            if (cam != null)
            {
                var to = cam.transform.position - neck.position;
                if (to.sqrMagnitude < 9f * 9f)
                {
                    var local = transform.InverseTransformDirection(to);
                    float y = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    if (Mathf.Abs(y) < 100f)
                    {
                        watching = true;
                        wantYaw = Mathf.Clamp(y, -60f, 60f);
                        wantPitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -20f, 20f);
                    }
                }
            }

            if (!watching && workBlend > 0.5f) { wantYaw = 0f; wantPitch = Work == WorkKind.Trace ? -8f : Work == WorkKind.Drink ? -6f : 24f; }
            if (!watching && Time.time >= nextGlance)
            {
                nextGlance = Time.time + Random.Range(2.5f, 6f);
                glanceYaw = Random.Range(-35f, 35f);
            }
            float nod = Mathf.Max(0f, Mathf.Sin(t * 0.7f) - 0.93f) * 60f;
            if (Prays)
            {
                nod = watching ? 0f : Mathf.Sin(t * 1.1f) * 6f;
                if (cam != null && (cam.transform.position - PrayAt).sqrMagnitude < 18f * 18f)
                {
                    if (Time.time >= nextMote)
                    {
                        nextMote = Time.time + Random.Range(1.2f, 2.4f);
                        Vfx.Embers(PrayAt + Random.insideUnitSphere * 0.03f, 1, new Color(1f, 0.85f, 0.45f), 0.35f, 0.025f, 1.4f);
                    }
                    if (Time.time >= nextSmoke)
                    {
                        nextSmoke = Time.time + 0.35f;
                        Vfx.Incense(IncenseAt);
                    }
                }
            }
            yaw = Mathf.Lerp(yaw, wantYaw, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            pitch = Mathf.Lerp(pitch, wantPitch, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            neck.localRotation = Quaternion.Euler(pitch + nod, yaw, Mathf.Sin(t * 0.5f) * 2f);
        }

        private void TickWork(float t, float camDist)
        {
            if (Work == WorkKind.None || (ArmR == null && ArmL == null)) return;
            bool attend = camDist < AttendRange;
            bool far = camDist > 40f;
            workBlend = Mathf.MoveTowards(workBlend, attend ? 0f : 1f, Time.deltaTime * (attend ? 2.5f : 0.8f));
            if (far && workBlend >= 1f) return;
            var r = Vector3.zero; var l = Vector3.zero;
            switch (Work)
            {
                case WorkKind.Hammer:
                {
                    float c = Mathf.Repeat(t, 1.3f);
                    if (c < 0.85f) r.x = -Mathf.Lerp(38f, 112f, Smooth(c / 0.85f));
                    else if (c < 0.97f) r.x = -Mathf.Lerp(112f, 32f, (c - 0.85f) / 0.12f);
                    else r.x = -32f;
                    l = new Vector3(-30f, 12f, 0f);
                    if (lastCycle < 0.97f && c >= 0.97f && workBlend > 0.8f && camDist < 22f && Striker != null)
                    {
                        Vfx.Embers(Striker.position, 6, new Color(1f, 0.65f, 0.25f), 2.4f, 0.03f, 0.5f);
                        Sfx.Play(Sound.Block, Striker.position, 0.32f, Random.Range(1.25f, 1.45f));
                    }
                    lastCycle = c;
                    break;
                }
                case WorkKind.Sew:
                    r = new Vector3(-64f + Mathf.Sin(t * 7f) * 7f, -16f + Mathf.Sin(t * 3.5f) * 8f, 0f);
                    l = new Vector3(-58f, 18f, 0f);
                    break;
                case WorkKind.Drill:
                {
                    float c = Mathf.Repeat(t, 3f);
                    if (c < 0.5f) r = new Vector3(-Mathf.Lerp(30f, 150f, Smooth(c / 0.5f)), 0f, 20f);
                    else if (c < 0.75f) r = new Vector3(-Mathf.Lerp(150f, 25f, (c - 0.5f) / 0.25f), 0f, Mathf.Lerp(20f, -10f, (c - 0.5f) / 0.25f));
                    else if (c < 1.6f) r = new Vector3(-25f, 0f, -10f);
                    else if (c < 1.9f) r = new Vector3(-Mathf.Lerp(25f, 85f, Smooth((c - 1.6f) / 0.3f)), 0f, 0f);
                    else r = new Vector3(-Mathf.Lerp(85f, 30f, Smooth(Mathf.Clamp01((c - 1.9f) / 0.8f))), 0f, 0f);
                    l = new Vector3(-20f, -6f, -12f);
                    break;
                }
                case WorkKind.Trace:
                    r = new Vector3(-88f + Mathf.Cos(t * 1.6f) * 16f, Mathf.Sin(t * 1.6f) * 24f, 0f);
                    l = new Vector3(-40f + Mathf.Sin(t * 0.8f) * 6f, 10f, 0f);
                    break;
                case WorkKind.Drink:
                {
                    float cyc = Mathf.Repeat(t, 7f);
                    float lift = Smooth(cyc / 0.8f) * (1f - Smooth((cyc - 2.4f) / 0.8f));
                    r = new Vector3(-30f - lift * 68f, -lift * 26f, 0f);
                    l = new Vector3(-50f, 10f, 0f);
                    break;
                }
                case WorkKind.Wipe:
                    r = new Vector3(-56f + Mathf.Sin(t * 3f) * 6f, Mathf.Sin(t * 3f) * 24f, 0f);
                    l = new Vector3(-30f, 8f, 0f);
                    break;
            }
            if (ArmR != null) ArmR.localRotation = Quaternion.Euler(r * workBlend);
            if (ArmL != null) ArmL.localRotation = Quaternion.Euler(l * workBlend);
        }

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
