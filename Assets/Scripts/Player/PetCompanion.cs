using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Loot;
using Deepbound.Progression;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// A companion (2026-11-07, the user's batch 2: "pets / companions (earned at camp, follow you down, fetch loot, help a
    /// little)"). Chosen at Mistress Vey's (<see cref="Cosmetics.Kind.Pet"/>), synced as <see cref="NetworkPlayer.PetKind"/>,
    /// and built and moved LOCALLY on every peer from its owner's position: it has no health, nobody can hit it, and it
    /// costs no bandwidth. What it does:
    /// <list type="bullet">
    /// <item>FETCH: gold and embers lying within <see cref="FetchRadius"/> of you (in sight) are brought to you after
    /// <see cref="FetchDelay"/> (server side in <see cref="LootDrop"/>; here the pet runs out to them). It never adds loot.</item>
    /// <item>WARN: a hidden spike plate within <see cref="WarnRadius"/> makes it run over, sit on it and call out.</item>
    /// <item>TRICKS (2026-11-08, the user's list: "pets do things at camp"), driven by your emotes so everyone sees them:
    /// POINT = it runs out to where you point and brings back a stick (dropped at your feet); BECKON = it comes and sits
    /// before you; DANCE / CLAP / CHEER = it spins and hops. At camp it now and then CHASES the castle cat for a few
    /// seconds, and when you sit by the fire a while it curls up ASLEEP beside you (<see cref="SleepAfter"/>).</item>
    /// </list>
    /// </summary>
    public class PetCompanion : MonoBehaviour
    {
        public const float FetchRadius = 8f, FetchDelay = 0.9f, WarnRadius = 4.5f;

        /// <summary>This owner's fetch radius: <see cref="Legacy"/>'s Keen Companion stretches it.</summary>
        public static float FetchRadiusOf(NetworkPlayer owner) => Legacy.Has(owner, Boon.KeenCompanion) ? Legacy.KeenFetchRadius : FetchRadius;

        /// <summary>Index into <see cref="Cosmetics.Pets"/>; append only.</summary>
        public enum Kind { None, Raven, Rat, BoneHound, Kitten, Wisp }

        private NetworkPlayer owner;
        private Transform body, head, tail;
        private readonly List<Transform> legs = new List<Transform>();
        private Kind kind;
        private Vector3 pos, vel;
        private float gait, nextScan, sitUntil, nextCall;
        private bool sitOnArrival;
        private Vector3? errand;
        private float errandUntil;
        /// <summary>Where it waits by you: a spot in the WORLD, kept until you walk off from it (it used to hang a fixed
        /// offset behind your view, so turning to look at it swung it behind you again).</summary>
        private Vector3? perch;
        private Vector3 lastOwner;
        private bool ownerSits;
        /// <summary>Pets read better a size up (the user: "barely visible").</summary>
        public const float Size = 1.35f;
        private readonly HashSet<SpikeTrap> warned = new HashSet<SpikeTrap>();

        private bool Flies => kind == Kind.Raven || kind == Kind.Wisp;

        // Tricks.
        public enum Trick { None, Fetch, Chase, Spin, Come }
        public const float SleepAfter = 7f, FetchReach = 6f, ChaseSeconds = 3.5f, SpinSeconds = 1.2f;
        public Trick Doing { get; private set; }
        public bool Sleeping { get; private set; }
        private float trickStart, trickUntil, sitSince = -1f, nextChase = 25f;
        private int fetchStage;
        private Vector3 trickAt;
        private Transform stick;
        private Emote lastEmote;
        private World.CampCat cat;

        public void Init(NetworkPlayer who)
        {
            owner = who;
            owner.PetKind.OnValueChanged += (_, now) => Rebuild((Kind)now);
            Rebuild((Kind)owner.PetKind.Value);
        }

        private void OnDestroy()
        {
            if (body != null) Destroy(body.gameObject);
        }

        private void Rebuild(Kind k)
        {
            kind = k;
            if (body != null) Destroy(body.gameObject);
            body = head = tail = null;
            stick = null;
            Doing = Trick.None;
            legs.Clear();
            warned.Clear();
            if (k == Kind.None || Application.isBatchMode || owner == null) return;
            var root = new GameObject("Pet_" + k).transform;
            body = root;
            pos = owner.transform.position - owner.transform.forward * 1.4f;
            root.position = pos;
            switch (k)
            {
                case Kind.Raven: BuildRaven(root); break;
                case Kind.Rat: BuildRat(root); break;
                case Kind.BoneHound: BuildHound(root); break;
                case Kind.Kitten: BuildKitten(root); break;
                case Kind.Wisp: BuildWisp(root); break;
            }
            // A size up, a shadow under it, and (your own pet) a faint warm glow so it is never a dark lump at your feet.
            root.localScale = Vector3.one * Size;
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (owner.IsOwner && k != Kind.Wisp)
            {
                var glow = new GameObject("PetGlow").AddComponent<Light>();
                glow.transform.SetParent(root, false);
                glow.transform.localPosition = new Vector3(0f, 0.9f, 0.3f);
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.82f, 0.6f);
                glow.range = 2.2f;
                glow.intensity = 0.7f;
                glow.shadows = LightShadows.None;
            }
            perch = null;
        }

        // ---------------------------------------------------------------- looks

        private static void Tint(GameObject go, Color c) => ArtLibrary.Tint(go.GetComponent<Renderer>(), c);

        private Transform Pivot(Transform parent, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        /// <summary>Four legs on hip pivots (swung by the gait), a body, a head pivot and a tail pivot.</summary>
        private void Quadruped(Transform root, string mat, Color c, float len, float hip, float legLen, float legW)
        {
            var torso = MeshKit.Part(root, "Torso", MeshKit.Ball(0.5f, 8, 6), mat, Vector3.up * hip, default, new Vector3(len * 0.5f, len * 0.42f, len));
            Tint(torso, c);
            foreach (var (x, z) in new[] { (-1f, 1f), (1f, 1f), (-1f, -1f), (1f, -1f) })
            {
                var p = Pivot(root, "Hip", new Vector3(x * len * 0.17f, hip, z * len * 0.33f));
                var leg = MeshKit.Part(p, "Leg", MeshKit.Box(new Vector3(legW, legLen, legW)), mat, Vector3.down * legLen / 2f);
                Tint(leg, c);
                legs.Add(p);
            }
            head = Pivot(root, "Head", new Vector3(0f, hip + len * 0.22f, len * 0.55f));
            tail = Pivot(root, "Tail", new Vector3(0f, hip + len * 0.05f, -len * 0.5f));
        }

        private void BuildRaven(Transform root)
        {
            var black = new Color(0.07f, 0.07f, 0.09f);
            Tint(MeshKit.Part(root, "Body", MeshKit.Ball(0.13f, 8, 6), Mat.Cloth, Vector3.up * 0.2f, default, new Vector3(0.9f, 0.85f, 1.4f)), black);
            head = Pivot(root, "Head", new Vector3(0f, 0.32f, 0.14f));
            Tint(MeshKit.Part(head, "Skull", MeshKit.Ball(0.08f, 8, 6), Mat.Cloth, Vector3.zero), black);
            Tint(MeshKit.Part(head, "Beak", MeshKit.Cylinder(0.025f, 0.002f, 0.12f, 6), Mat.Dark, new Vector3(0f, -0.01f, 0.08f), new Vector3(90f, 0f, 0f)), new Color(0.15f, 0.14f, 0.13f));
            foreach (float s in new[] { -1f, 1f })
            {
                var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.013f, 5, 4), Mat.Glow, new Vector3(s * 0.05f, 0.025f, 0.04f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.9f, 0.75f, 0.3f) * 0.8f);
                var wing = Pivot(root, "Wing", new Vector3(s * 0.09f, 0.26f, 0f));
                Tint(MeshKit.Part(wing, "Feathers", MeshKit.Box(new Vector3(0.24f, 0.02f, 0.2f)), Mat.Cloth, new Vector3(s * 0.11f, 0f, -0.03f)), black);
                legs.Add(wing);
            }
            tail = Pivot(root, "Tail", new Vector3(0f, 0.2f, -0.17f));
            Tint(MeshKit.Part(tail, "Fan", MeshKit.Box(new Vector3(0.12f, 0.02f, 0.18f)), Mat.Cloth, new Vector3(0f, 0f, -0.08f)), black);
        }

        private void BuildRat(Transform root)
        {
            var fur = new Color(0.32f, 0.29f, 0.26f);
            Quadruped(root, Mat.Cloth, fur, 0.32f, 0.08f, 0.08f, 0.025f);
            Tint(MeshKit.Part(head, "Snout", MeshKit.Ball(0.06f, 6, 5), Mat.Cloth, Vector3.zero, default, new Vector3(0.9f, 0.85f, 1.5f)), fur);
            Tint(MeshKit.Part(head, "Nose", MeshKit.Ball(0.015f, 5, 4), Mat.Cloth, new Vector3(0f, 0f, 0.09f)), new Color(0.8f, 0.5f, 0.5f));
            foreach (float s in new[] { -1f, 1f })
                Tint(MeshKit.Part(head, "Ear", MeshKit.Cylinder(0.03f, 0.03f, 0.005f, 8), Mat.Cloth, new Vector3(s * 0.04f, 0.05f, -0.02f), new Vector3(70f, s * 20f, 0f)), new Color(0.7f, 0.5f, 0.5f));
            Tint(MeshKit.Part(tail, "Tail", MeshKit.Cylinder(0.012f, 0.004f, 0.3f, 5), Mat.Cloth, new Vector3(0f, 0f, -0.15f), new Vector3(-80f, 0f, 0f)), new Color(0.7f, 0.5f, 0.5f));
        }

        private void BuildHound(Transform root)
        {
            var bone = new Color(0.86f, 0.82f, 0.72f);
            // Ribs and a spine rather than a hide: the Ossuary's own dog.
            Quadruped(root, Mat.Bone, bone, 0.75f, 0.45f, 0.42f, 0.05f);
            var torso = root.Find("Torso");
            if (torso != null) torso.localScale = new Vector3(0.12f, 0.12f, 0.75f);
            for (int i = 0; i < 4; i++)
                MeshKit.Part(root, "Rib", MeshKit.Box(new Vector3(0.3f, 0.22f, 0.03f)), Mat.Bone, new Vector3(0f, 0.4f, 0.18f - i * 0.11f));
            MeshKit.Part(head, "Skull", MeshKit.Ball(0.1f, 8, 6), Mat.Bone, Vector3.zero, default, new Vector3(1f, 0.9f, 1.1f));
            MeshKit.Part(head, "Muzzle", MeshKit.Box(new Vector3(0.09f, 0.08f, 0.17f)), Mat.Bone, new Vector3(0f, -0.03f, 0.13f));
            foreach (float s in new[] { -1f, 1f })
            {
                var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.022f, 5, 4), Mat.Glow, new Vector3(s * 0.045f, 0.02f, 0.08f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.45f, 0.9f, 1f) * 0.9f);
                MeshKit.Part(head, "Ear", MeshKit.Box(new Vector3(0.03f, 0.09f, 0.05f)), Mat.Bone, new Vector3(s * 0.06f, 0.09f, -0.03f), new Vector3(0f, 0f, s * -15f));
            }
            for (int i = 0; i < 4; i++)
                MeshKit.Part(tail, "Vertebra", MeshKit.Ball(0.025f, 5, 4), Mat.Bone, new Vector3(0f, i * 0.04f, -i * 0.06f));
        }

        private void BuildKitten(Transform root)
        {
            // The castle cat's litter: the same tabby grey, much smaller.
            var fur = new Color(0.42f, 0.38f, 0.34f);
            Quadruped(root, Mat.Cloth, fur, 0.36f, 0.14f, 0.14f, 0.035f);
            Tint(MeshKit.Part(head, "Skull", MeshKit.Ball(0.075f, 8, 6), Mat.Cloth, Vector3.zero), fur);
            foreach (float s in new[] { -1f, 1f })
            {
                Tint(MeshKit.Part(head, "Ear", MeshKit.Cylinder(0.03f, 0.002f, 0.06f, 4), Mat.Cloth, new Vector3(s * 0.045f, 0.07f, -0.01f)), fur);
                var eye = MeshKit.Part(head, "Eye", MeshKit.Ball(0.014f, 5, 4), Mat.Glow, new Vector3(s * 0.03f, 0.015f, 0.065f));
                ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(0.75f, 0.9f, 0.3f) * 0.7f);
            }
            Tint(MeshKit.Part(tail, "Tail", MeshKit.Cylinder(0.02f, 0.012f, 0.24f, 6), Mat.Cloth, new Vector3(0f, 0.1f, -0.06f), new Vector3(-30f, 0f, 0f)), fur);
        }

        private void BuildWisp(Transform root)
        {
            var core = MeshKit.Part(root, "Core", MeshKit.Ball(0.09f, 8, 6), Mat.Glow, Vector3.zero);
            ArtLibrary.SetGlow(core.GetComponent<Renderer>(), new Color(0.6f, 0.85f, 1f) * 1.3f);
            head = core.transform;
            for (int i = 0; i < 3; i++)
            {
                var p = Pivot(root, "Orbit", Vector3.zero);
                var mote = MeshKit.Part(p, "Mote", MeshKit.Ball(0.025f, 5, 4), Mat.Glow, new Vector3(0.18f, 0f, 0f));
                ArtLibrary.SetGlow(mote.GetComponent<Renderer>(), new Color(0.5f, 0.75f, 1f));
                p.localRotation = Quaternion.Euler(i * 60f, i * 120f, 0f);
                legs.Add(p);
            }
        }

        // ---------------------------------------------------------------- moves

        private void Update()
        {
            if (body == null || owner == null) return;
            float dt = Time.deltaTime, t = Time.time;
            var o = owner.transform.position;
            var fwd = owner.transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);

            if (t >= nextScan) { nextScan = t + 0.3f; Scan(o); }
            if (errand.HasValue && (t > errandUntil || (owner.transform.position - errand.Value).sqrMagnitude > 18f * 18f)) errand = null;

            // How the owner moves (local position change: works for everyone's pets).
            var ownerVel = dt > 0f ? (o - lastOwner) / dt : Vector3.zero; ownerVel.y = 0f;
            lastOwner = o;
            bool moving = ownerVel.magnitude > 1.2f;
            ownerSits = owner.Rig != null && owner.Rig.EmoteNow == Art.Emote.Sit;
            // Keep to the side it is already on, so it does not cross in front of you every time.
            var fromOwner = pos - o; fromOwner.y = 0f;
            float side = Vector3.Dot(fromOwner, right) >= 0f ? 1f : -1f;
            if (ownerSits)
            {
                // Sit beside you: on your side of the log, facing the way you face.
                perch = o + right * side * 0.75f + fwd * 0.15f;
            }
            else if (moving)
            {
                // Trot alongside, a little ahead, where you can see it.
                var dir = ownerVel.normalized;
                var r2 = Vector3.Cross(Vector3.up, dir);
                var ahead = o + dir * 1.4f + r2 * side * 1.0f;
                if (perch == null || Flat(perch.Value - ahead) > 1.4f) perch = ahead;
            }
            else if (perch == null || Flat(perch.Value - o) > 3.4f || Flat(perch.Value - o) < 0.6f)
            {
                // Standing: settle in front and to the side - in view - and stay there however you turn.
                // Not on a table or a counter: try the other side, then closer in at your side.
                perch = o + fwd * 1.3f + right * side * 1.1f;
                if (!Flies && Raised(perch.Value, o.y)) perch = o + fwd * 1.3f - right * side * 1.1f;
                if (!Flies && Raised(perch.Value, o.y)) perch = o + right * side * 0.8f - fwd * 0.2f;
            }
            TickTricks(t, o, fwd, moving);
            Vector3 want = errand ?? perch.Value;
            if (Flies) want += Vector3.up * (errand.HasValue ? 0.6f : ownerSits ? (kind == Kind.Wisp ? 0.5f : 0f) : 1.5f + Mathf.Sin(t * 1.7f) * 0.15f);

            // Far behind (a lift, a fall, a new floor): it is simply there again.
            if ((pos - o).sqrMagnitude > 16f * 16f) { pos = want; vel = Vector3.zero; }

            var to = want - pos; to.y = Flies ? to.y : 0f;
            float dist = to.magnitude;
            if (sitOnArrival && errand.HasValue && dist < 0.45f) { sitOnArrival = false; sitUntil = t + 2.4f; errandUntil = sitUntil; }
            if (ownerSits && !errand.HasValue && dist < 0.4f) sitUntil = t + 0.3f;
            bool sitting = t < sitUntil;
            float speed = sitting ? 0f : dist > 5f ? 9f : dist > 1.2f ? 4.5f : dist > 0.35f ? 2f : 0f;
            var target = dist > 0.01f ? to / dist * speed : Vector3.zero;
            vel = Vector3.Lerp(vel, target, 1f - Mathf.Exp(-8f * dt));
            pos += vel * dt;
            if (!Flies)
            {
                // Feet on the floor (props and steps included).
                if (Physics.Raycast(pos + Vector3.up * 1.2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<NetworkPlayer>() == null
                    && hit.point.y < o.y + 0.55f)
                    pos.y = Mathf.Lerp(pos.y, hit.point.y, 1f - Mathf.Exp(-14f * dt));
                else pos.y = Mathf.Lerp(pos.y, o.y, 1f - Mathf.Exp(-6f * dt));
            }
            body.position = pos;

            var flat = new Vector3(vel.x, 0f, vel.z);
            var face = flat.sqrMagnitude > 0.05f ? flat : ownerSits ? fwd : (sitting || errand.HasValue ? flat : (o - pos));
            face.y = 0f;
            if (face.sqrMagnitude > 0.001f) body.rotation = Quaternion.Slerp(body.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-8f * dt));
            if (Doing == Trick.Spin)
            {
                // Two turns and a little hop on each.
                float k = Mathf.Clamp01((t - trickStart) / SpinSeconds);
                body.rotation *= Quaternion.Euler(0f, k * 720f, 0f);
                body.position = pos + Vector3.up * Mathf.Abs(Mathf.Sin(k * Mathf.PI * 2f)) * 0.18f * Size;
            }
            if (Sleeping && !Flies) body.position = pos + Vector3.down * 0.05f * Size;
            Animate(flat.magnitude, sitting || Sleeping, t, dt);
        }

        // ---------------------------------------------------------------- tricks

        private void StartTrick(Trick k, float seconds)
        {
            Doing = k;
            trickStart = Time.time;
            trickUntil = Time.time + seconds;
            fetchStage = 0;
            sitUntil = 0f;
            errand = null;
        }

        private void EndTrick()
        {
            if (Doing == Trick.Fetch && stick != null) Destroy(stick.gameObject);
            stick = null;
            Doing = Trick.None;
            errand = null;
        }

        /// <summary>Emotes start tricks; at camp it chases the cat now and then; sitting a while, it sleeps.</summary>
        private void TickTricks(float t, Vector3 o, Vector3 fwd, bool moving)
        {
            var emote = owner.Rig != null ? owner.Rig.EmoteNow : Emote.None;
            if (emote != lastEmote)
            {
                lastEmote = emote;
                if (emote == Emote.Point)
                {
                    StartTrick(Trick.Fetch, 9f);
                    var aim = o + fwd * FetchReach;
                    trickAt = Physics.Raycast(aim + Vector3.up * 2f, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore) ? hit.point : new Vector3(aim.x, o.y, aim.z);
                    Sfx.Play(CallSound, pos + Vector3.up * 0.4f, 0.6f, 1.2f);
                }
                else if (emote == Emote.Beckon) { StartTrick(Trick.Come, 4f); sitOnArrival = true; }
                else if (emote == Emote.Dance || emote == Emote.Clap || emote == Emote.Cheer) { StartTrick(Trick.Spin, SpinSeconds); Sfx.Play(CallSound, pos + Vector3.up * 0.4f, 0.5f, 1.3f); }
            }
            if (ownerSits) { if (sitSince < 0f) sitSince = t; } else sitSince = -1f;
            Sleeping = ownerSits && sitSince >= 0f && t - sitSince > SleepAfter && Doing == Trick.None;

            if (Doing != Trick.None && (t > trickUntil || moving && Doing != Trick.Fetch)) { EndTrick(); return; }
            switch (Doing)
            {
                case Trick.Fetch:
                    if (fetchStage == 0)
                    {
                        errand = trickAt; errandUntil = trickUntil;
                        if (Flat(pos - trickAt) < 0.5f)
                        {
                            fetchStage = 1;
                            if (kind != Kind.Wisp && head != null)
                            {
                                // A stick in its mouth (the raven in its beak).
                                stick = MeshKit.Part(head, "Stick", MeshKit.Box(new Vector3(0.36f, 0.035f, 0.035f)), Mat.Wood, new Vector3(0f, -0.02f, 0.14f), new Vector3(0f, 8f, 4f)).transform;
                                ArtLibrary.Tint(stick.GetComponent<Renderer>(), new Color(0.45f, 0.32f, 0.18f));
                            }
                        }
                    }
                    else
                    {
                        var drop = o + fwd * 0.9f;
                        errand = drop; errandUntil = trickUntil;
                        if (Flat(pos - drop) < 0.6f)
                        {
                            if (stick != null)
                            {
                                // Dropped at your feet; it lies there a while.
                                stick.SetParent(null, true);
                                stick.position = new Vector3(pos.x, o.y + 0.03f, pos.z) + fwd * 0.2f;
                                stick.rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 90f);
                                Destroy(stick.gameObject, 10f);
                                stick = null;
                            }
                            Sfx.Play(CallSound, pos + Vector3.up * 0.4f, 0.6f, 1.15f);
                            Doing = Trick.None;
                            errand = null;
                            sitUntil = t + 1.6f;
                        }
                    }
                    break;
                case Trick.Come:
                    errand = o + fwd * 0.9f; errandUntil = trickUntil;
                    break;
                case Trick.Chase:
                    if (cat == null || cat.HeldBy.Value != ulong.MaxValue) { EndTrick(); break; }
                    var c = cat.transform.position;
                    var off = pos - c; off.y = 0f;
                    errand = c + (off.sqrMagnitude > 0.01f ? off.normalized : -fwd) * 0.7f; errandUntil = trickUntil;
                    break;
            }
            // The castle cat: a pet near it (and its owner standing about) goes after it now and then.
            if (Doing == Trick.None && DungeonDirector.InLobby && !ownerSits && !moving && t >= nextChase && kind != Kind.Wisp)
            {
                nextChase = t + Random.Range(35f, 60f);
                if (cat == null) cat = FindFirstObjectByType<World.CampCat>();
                if (cat != null && cat.HeldBy.Value == ulong.MaxValue && Vector3.Distance(cat.transform.position, o) < 9f)
                {
                    StartTrick(Trick.Chase, ChaseSeconds);
                    Sfx.Play(CallSound, pos + Vector3.up * 0.4f, 0.8f, 1.05f);
                }
            }
        }

        /// <summary>Shots: start a chase now if the cat is near.</summary>
        public void DebugChase()
        {
            if (cat == null) cat = FindFirstObjectByType<World.CampCat>();
            if (cat != null) StartTrick(Trick.Chase, ChaseSeconds);
        }

        private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>The ground at <paramref name="at"/> is a table top or a counter (well above the owner's feet).</summary>
        private static bool Raised(Vector3 at, float feet) =>
            Physics.Raycast(new Vector3(at.x, feet + 1.6f, at.z), Vector3.down, out var hit, 2.2f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<NetworkPlayer>() == null && hit.point.y > feet + 0.4f;

        private void Animate(float speed, bool sitting, float t, float dt)
        {
            gait += dt * (2f + speed * 3.2f);
            if (kind == Kind.Wisp)
            {
                for (int i = 0; i < legs.Count; i++) legs[i].Rotate(Vector3.up, (140f + i * 40f) * dt, Space.Self);
                if (head != null) head.localScale = Vector3.one * (1f + Mathf.Sin(t * 4f) * 0.08f);
                return;
            }
            if (kind == Kind.Raven)
            {
                if (Sleeping) { for (int i = 0; i < legs.Count; i++) legs[i].localRotation = Quaternion.Euler(0f, 0f, (i == 0 ? 1f : -1f) * -8f); if (head != null) head.localRotation = Quaternion.Euler(35f, 150f, 0f); return; }
                bool flying = speed > 1.2f || pos.y > owner.transform.position.y + 0.5f;
                float flap = flying ? Mathf.Sin(t * 22f) * 55f : 0f;
                for (int i = 0; i < legs.Count; i++) legs[i].localRotation = Quaternion.Euler(0f, 0f, (i == 0 ? 1f : -1f) * (flying ? flap : -8f));
                if (head != null) head.localRotation = Quaternion.Euler(Mathf.Sin(t * 3.1f) * 10f, Mathf.Sin(t * 1.3f) * 35f, 0f);
                return;
            }
            if (Sleeping)
            {
                for (int i = 0; i < legs.Count; i++) legs[i].localRotation = Quaternion.Euler(i >= 2 ? -80f : 70f, 0f, 0f);
                if (head != null) head.localRotation = Quaternion.Euler(28f + Mathf.Sin(t * 1.4f) * 2f, 18f, 0f);
                if (tail != null) tail.localRotation = Quaternion.Euler(0f, 60f, 0f);
                return;
            }
            float swing = speed > 0.1f ? Mathf.Sin(gait) * Mathf.Clamp(speed * 9f, 0f, 38f) : 0f;
            for (int i = 0; i < legs.Count; i++)
            {
                float phase = (i == 0 || i == 3) ? 1f : -1f;
                float sit = sitting && i >= 2 ? -70f : 0f;
                legs[i].localRotation = Quaternion.Euler(swing * phase + sit, 0f, 0f);
            }
            if (head != null) head.localRotation = Quaternion.Euler(sitting ? -20f : Mathf.Sin(gait * 0.5f) * 4f, speed < 0.1f ? Mathf.Sin(t * 0.7f) * 25f : 0f, 0f);
            if (tail != null) tail.localRotation = Quaternion.Euler(kind == Kind.BoneHound ? 20f : 0f, Mathf.Sin(t * (kind == Kind.BoneHound ? 9f : 3f)) * (kind == Kind.BoneHound ? 30f : 18f), 0f);
        }

        /// <summary>Fetch or warn: loot to bring back first, then a hidden plate to sit on.</summary>
        private void Scan(Vector3 o)
        {
            if (errand.HasValue) return;
            if (DungeonDirector.InLobby) return;
            var ownerLife = owner.GetComponent<PlayerLife>();
            if (ownerLife != null && ownerLife.Downed.Value) return;
            LootDrop best = null;
            float bd = FetchRadiusOf(owner);
            foreach (var d in LootDrop.All)
            {
                if (d == null || !d.IsMoney) continue;
                float dd = Vector3.Distance(d.transform.position, o);
                if (dd < bd && dd > 2.4f && LootDrop.PetCanReach(o, d.transform.position)) { bd = dd; best = d; }
            }
            if (best != null) { errand = best.transform.position; errandUntil = Time.time + FetchDelay + 0.3f; return; }
            foreach (var trap in SpikeTrap.All)
            {
                if (trap == null || !trap.Hidden.Value || warned.Contains(trap)) continue;
                if (Vector3.Distance(trap.transform.position, o) > WarnRadius) continue;
                warned.Add(trap);
                errand = trap.transform.position + (o - trap.transform.position).normalized * 0.7f;
                errandUntil = Time.time + 5f;
                sitOnArrival = true;
                if (Time.time >= nextCall)
                {
                    nextCall = Time.time + 2f;
                    Sfx.Play(CallSound, trap.transform.position + Vector3.up * 0.4f, 0.8f, 1.1f);
                    UI.GameHud.ShowCombo(trap.transform.position + Vector3.up * 0.3f, "TRAP!", new Color(1f, 0.65f, 0.3f));
                }
                return;
            }
        }

        private Sound CallSound => kind switch
        {
            Kind.Raven => Sound.Caw,
            Kind.Rat => Sound.Squeak,
            Kind.Kitten => Sound.Meow,
            Kind.BoneHound => Sound.Growl,
            _ => Sound.Graze,
        };
    }
}
