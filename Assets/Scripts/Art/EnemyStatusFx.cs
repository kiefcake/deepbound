using Deepbound.Combat;
using Deepbound.Enemies;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// What is wrong with an enemy, shown on its body and not only in its tag (2026-10-24, the user: "visually show as
    /// much of the effects enemies can have... visuals for things like speedy, enraged"). Client-side, one per enemy,
    /// built lazily and only for what is in effect:
    /// <list type="bullet">
    /// <item>STUN: three sparks circling the head. MARK: a red diamond turning over it. TAUNT: an orange ring at its feet.</item>
    /// <item>CHILL: a frost ring at its feet. BLIGHT: green motes (scorch and chill motes come from <see cref="Health"/>).</item>
    /// <item>Elite traits: WARDED a violet bubble, SWIFT pale streaks while it runs, FRENZIED/ENRAGED a red pulse,
    ///   steam and red eyes, VAMPIRIC blood motes, VOLATILE a warning ring that beats faster as it weakens,
    ///   THORNED a ring of thorns, SPLITTING bone shards circling, GRAVEBOUND grave-wisps.</item>
    /// </list>
    /// </summary>
    public class EnemyStatusFx : MonoBehaviour
    {
        private EnemyBrain brain;
        private Health health;
        private Transform stars, mark, tauntRing, frostRing, bubble, volatileRing, thorns, shards, rageRing, thread, severRing;
        private Renderer tauntR, frostR, bubbleR, volatileR, rageR, threadR, severR;
        private Health twinHealth;
        private ulong twinFor;
        private float nextMote, nextStreak;
        private string championTitle;
        private Color? championMote;
        private Vector3 lastPos;
        private Color baseEyes;
        private bool eyesRed, hidden = true;

        private void Awake()
        {
            brain = GetComponent<EnemyBrain>();
            health = GetComponent<Health>();
            lastPos = transform.position;
        }

        private float Scale => brain != null && brain.Rig != null ? brain.Rig.transform.lossyScale.y : 1f;
        private Vector3 HeadTop => brain != null && brain.Rig != null && brain.Rig.Head != null ? brain.Rig.Head.position + Vector3.up * 0.32f * Scale : transform.position + Vector3.up * (health.CenterHeight * 2f + 0.2f);

        private void Update()
        {
            if (brain == null || health == null) return;
            bool alive = health.IsAlive && !brain.IsDead;
            var cam = ViewCamera.Current;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 900f;
            if (!alive || !near) { HideAll(); return; }
            hidden = false;

            byte st = brain.Status.Value;
            var affix = brain.Elite.Value ? brain.AffixKind : EliteAffix.None;
            bool enraged = brain.Enraged || brain.Frenzied;
            float t = Time.time;

            // Stunned: sparks wheeling round the head.
            if ((st & 1) != 0)
            {
                if (stars == null) stars = BuildStars();
                stars.gameObject.SetActive(true);
                stars.position = HeadTop;
                stars.rotation = Quaternion.Euler(0f, t * 360f, 0f);
                stars.localScale = Vector3.one * Scale;
            }
            else if (stars != null) stars.gameObject.SetActive(false);

            // Marked: a red diamond over the head, turning and bobbing.
            if (health.Marked.Value)
            {
                if (mark == null) mark = BuildMark();
                mark.gameObject.SetActive(true);
                mark.position = HeadTop + Vector3.up * (0.35f + 0.06f * Mathf.Sin(t * 4f)) * Scale;
                mark.rotation = Quaternion.Euler(0f, t * 120f, 0f);
            }
            else if (mark != null) mark.gameObject.SetActive(false);

            // Taunted: an orange ring beating at its feet. Chilled: a pale frost ring.
            Ring(ref tauntRing, ref tauntR, (st & 2) != 0, new Color(1f, 0.5f, 0.2f), 0.9f, 1f + 0.1f * Mathf.Sin(t * 8f));
            Ring(ref frostRing, ref frostR, health.Chilled.Value, new Color(0.6f, 0.88f, 1f), 0.75f, 1f);
            // Enraged / frenzied: a red pulse at its feet, red eyes, steam rising.
            Ring(ref rageRing, ref rageR, enraged, new Color(1f, 0.15f, 0.1f), 1.05f, 1f + 0.18f * Mathf.Abs(Mathf.Sin(t * 6f)));
            SetEyes(enraged);
            // Volatile: a warning ring that beats faster the weaker it is.
            float beat = Mathf.Lerp(10f, 3f, health.Fraction);
            Ring(ref volatileRing, ref volatileR, affix == EliteAffix.Volatile, new Color(1f, 0.55f, 0.1f), 1.25f, 1f + 0.12f * Mathf.Sin(t * beat));

            // Warded: a violet bubble round the body while the ward holds.
            if (brain.WardedNow)
            {
                if (bubble == null)
                {
                    bubble = MeshKit.Part(transform, "WardBubble", MeshKit.Ball(1f, 16, 10), Mat.Telegraph, Vector3.zero).transform;
                    bubbleR = bubble.GetComponent<Renderer>();
                    bubbleR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                bubble.gameObject.SetActive(true);
                float r = Mathf.Max(0.8f, health.BodyRadius * 1.6f) * 1f;
                bubble.localPosition = Vector3.up * health.CenterHeight;
                bubble.localScale = new Vector3(r * 2f, health.CenterHeight * 2.4f, r * 2f);
                ArtLibrary.Tint(bubbleR, new Color(0.7f, 0.5f, 1f, 0.16f + 0.05f * Mathf.Sin(t * 3f)));
            }
            else if (bubble != null) bubble.gameObject.SetActive(false);

            // Thorned: a ring of thorns about its feet.
            if (affix == EliteAffix.Thorned)
            {
                if (thorns == null) thorns = BuildThorns();
                thorns.gameObject.SetActive(true);
                thorns.position = transform.position;
                thorns.rotation = Quaternion.Euler(0f, t * 25f, 0f);
                thorns.localScale = Vector3.one * Mathf.Max(1f, Scale);
            }
            else if (thorns != null) thorns.gameObject.SetActive(false);

            // Splitting: two bone shards circling the waist.
            if (affix == EliteAffix.Splitting)
            {
                if (shards == null) shards = BuildShards();
                shards.gameObject.SetActive(true);
                shards.position = transform.position + Vector3.up * health.CenterHeight;
                shards.rotation = Quaternion.Euler(0f, -t * 200f, 0f);
                shards.localScale = Vector3.one * Scale;
            }
            else if (shards != null) shards.gameObject.SetActive(false);

            // Soulbound: the thread to its twin, and a pale ring under one that lies severed.
            UpdateThread(t);
            Ring(ref severRing, ref severR, brain.SeveredNow, new Color(0.75f, 0.85f, 1f), 1.15f, 1f + 0.1f * Mathf.Sin(t * 10f));

            // Motes for the rest (a few a second, near the camera only).
            var moved = transform.position - lastPos;
            float speed = moved.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
            lastPos = transform.position;
            if (t >= nextMote)
            {
                nextMote = t + 0.2f;
                if (health.Blighted.Value) Vfx.Mote(transform.position, new Color(0.55f, 1f, 0.3f));
                if (affix == EliteAffix.Vampiric) Vfx.Mote(transform.position, new Color(0.65f, 0.05f, 0.1f));
                if (affix == EliteAffix.Gravebound) Vfx.Mote(transform.position, new Color(0.6f, 0.95f, 0.75f));
                if (affix == EliteAffix.Hushing) Vfx.Mote(transform.position, new Color(0.78f, 0.78f, 1f));
                if (affix == EliteAffix.Concussive) Vfx.Mote(transform.position, new Color(0.95f, 0.85f, 0.55f));
                if (enraged) Vfx.Embers(HeadTop, 2, new Color(1f, 0.35f, 0.25f), 1.4f, 0.05f, 0.6f);
                // A champion wears its trait (2026-10-27): the Pale in frost, Ashenhand in cinders, and so on.
                if (brain != null && brain.IsChampion)
                {
                    if (championTitle != brain.Title.Value.ToString()) { championTitle = brain.Title.Value.ToString(); championMote = Enemies.ChampionTraits.MoteColor(championTitle); }
                    if (championMote.HasValue) Vfx.Mote(transform.position, championMote.Value);
                }
            }
            // Swift: pale streaks behind it whenever it runs.
            if (affix == EliteAffix.Swift && speed > 2.5f && t >= nextStreak)
            {
                nextStreak = t + 0.06f;
                var back = -moved.normalized;
                Vfx.Mote(transform.position + back * 0.5f, new Color(0.85f, 0.95f, 1f));
                Vfx.Mote(transform.position + back * 0.9f + Vector3.up * 0.3f, new Color(0.7f, 0.85f, 1f));
            }
        }

        private void SetEyes(bool red)
        {
            var rig = brain.Rig;
            if (rig == null || red == eyesRed) return;
            if (!eyesRed) baseEyes = rig.GlowColor;
            eyesRed = red;
            rig.GlowColor = red ? new Color(1f, 0.08f, 0.04f) * 1.4f : baseEyes;
            rig.RefreshGlow();
        }

        private void Ring(ref Transform ring, ref Renderer rend, bool on, Color c, float radius, float pulse)
        {
            if (!on) { if (ring != null) ring.gameObject.SetActive(false); return; }
            if (ring == null)
            {
                ring = MeshKit.Part(transform, "StatusRing", MeshKit.Lathe("statusRing", new[] { new Vector2(0.86f, 0f), new Vector2(1f, 0.015f), new Vector2(0.86f, 0.03f) }, 28, false), Mat.Glow, Vector3.up * 0.06f).transform;
                rend = ring.GetComponent<Renderer>();
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            ring.gameObject.SetActive(true);
            float r = Mathf.Max(radius, health.BodyRadius * 1.6f) * pulse;
            ring.localScale = new Vector3(r, 1f, r);
            ArtLibrary.SetGlow(rend, c * (0.6f + 0.2f * Mathf.Sin(Time.time * 5f)));
        }

        private Transform BuildStars()
        {
            var root = new GameObject("StunStars").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                var s = MeshKit.Part(root, "Star" + i, MeshKit.Ball(0.06f, 6, 4), Mat.Glow, Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 0.32f);
                ArtLibrary.SetGlow(s.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.3f) * 1.4f);
                s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        private Transform BuildMark()
        {
            var root = new GameObject("MarkDiamond").transform;
            root.SetParent(transform, false);
            var d = MeshKit.Part(root, "Diamond", MeshKit.Box(new Vector3(0.26f, 0.26f, 0.26f)), Mat.Glow, Vector3.zero, new Vector3(45f, 0f, 45f), new Vector3(0.7f, 1.3f, 0.7f));
            ArtLibrary.SetGlow(d.GetComponent<Renderer>(), new Color(1f, 0.2f, 0.15f) * 1.6f);
            d.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        private Transform BuildThorns()
        {
            var root = new GameObject("Thorns").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 8; i++)
            {
                var th = MeshKit.Part(root, "Thorn" + i, MeshKit.Cylinder(0.05f, 0f, 0.4f, 4), Mat.Wood, Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 0.85f + Vector3.up * 0.15f,
                    new Vector3(Random.Range(-25f, 25f), i * 45f, Random.Range(-20f, 20f)));
                ArtLibrary.Tint(th.GetComponent<Renderer>(), new Color(0.25f, 0.4f, 0.18f));
            }
            return root;
        }

        private Transform BuildShards()
        {
            var root = new GameObject("BoneShards").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 2; i++)
            {
                var b = MeshKit.Part(root, "Shard" + i, MeshKit.Box(new Vector3(0.05f, 0.22f, 0.05f)), Mat.Bone, Quaternion.Euler(0f, i * 180f, 0f) * Vector3.forward * 0.75f, new Vector3(30f, 0f, 40f));
                b.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        /// <summary>Soulbound: a pale thread from this twin to the other (drawn by the one with the lower id), thinning and flickering while one lies severed.</summary>
        private void UpdateThread(float t)
        {
            ulong id = brain.TwinId.Value;
            if (id == 0 || brain.NetworkObjectId > id) { if (thread != null) thread.gameObject.SetActive(false); return; }
            if (twinFor != id)
            {
                twinFor = id;
                twinHealth = null;
                var net = Unity.Netcode.NetworkManager.Singleton;
                if (net != null && net.SpawnManager != null && net.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)) twinHealth = obj.GetComponent<Health>();
            }
            if (twinHealth == null || !twinHealth.IsAlive) { if (thread != null) thread.gameObject.SetActive(false); return; }
            if (thread == null)
            {
                thread = MeshKit.Part(null, "SoulThread", MeshKit.Box(new Vector3(1f, 1f, 1f)), Mat.Glow, Vector3.zero).transform;
                threadR = thread.GetComponent<Renderer>();
                threadR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var a = health.Center;
            var b = twinHealth.Center;
            var d = b - a;
            if (d.sqrMagnitude < 0.01f) { thread.gameObject.SetActive(false); return; }
            bool severed = brain.SeveredNow || (twinHealth.TryGetComponent<EnemyBrain>(out var tb) && tb.SeveredNow);
            thread.gameObject.SetActive(true);
            thread.position = (a + b) / 2f;
            thread.rotation = Quaternion.LookRotation(d);
            float w = severed ? 0.018f : 0.03f;
            thread.localScale = new Vector3(w, w, d.magnitude);
            float flick = severed ? 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(t * 14f)) : 0.75f + 0.15f * Mathf.Sin(t * 3f);
            ArtLibrary.SetGlow(threadR, new Color(0.7f, 0.82f, 1f) * flick);
            if (t >= nextMote - 0.1f && Random.value < 0.3f) Vfx.Mote(Vector3.Lerp(a, b, Random.value) + Vector3.down * 0.6f, new Color(0.75f, 0.85f, 1f));
        }

        private void OnDestroy()
        {
            if (thread != null) Destroy(thread.gameObject);
        }

        private void HideAll()
        {
            // Every far or dead enemy lands here each frame: only do the work once.
            if (hidden) return;
            hidden = true;
            foreach (var x in new[] { stars, mark, tauntRing, frostRing, bubble, volatileRing, thorns, shards, rageRing, thread, severRing })
                if (x != null && x.gameObject.activeSelf) x.gameObject.SetActive(false);
            SetEyes(false);
        }
    }
}
