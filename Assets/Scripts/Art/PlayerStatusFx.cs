using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// SHOW, DON'T TELL for players (2026-11-14, the user: "clerics 'blessed' buff being a text box instead of an aura
    /// around the user"). The players' counterpart to <see cref="EnemyStatusFx"/>: what is on a player is worn on the body,
    /// on every peer, so a Cleric sees who stands blessed and who needs Holy Water without reading anything.
    /// Your own view gets the same states as edge pictures (<see cref="UI.AfflictionOverlay"/>); here your own body only
    /// shows what lies at your feet (rings and motes you see looking down), never things at head height.
    /// <list type="bullet">
    /// <item>BLESSED (consecration): a gold halo over the head, a gold ring at the feet, gold motes rising, a warm light.</item>
    /// <item>INSPIRED (the Cleric's beam): a gold star turning over the head, glints.</item>
    /// <item>ZEAL / MARTYRDOM / BLOODLUST / BLOODFURY: crimson rings and embers; Martyrdom a pale crown.</item>
    /// <item>IRONBARK: a bark-brown ring and leaf motes. PLAGUE MASK ward: a slow green ring. BROTHERS IN ARMS: warm sparks.
    ///   LAST STAND: a heavy red beat at the feet.</item>
    /// <item>Afflictions: poison drips green, frost rimes the feet, mania spins magenta, silence binds a grey-violet ring
    ///   at the throat, daze wheels stars round the head, the hag's curse hangs a green-black eye over it, weakness
    ///   drips violet, ink runs from the blinded.</item>
    /// </list>
    /// Added to every <see cref="PlayerLife"/> by a booted watcher; cheap: only within 35 m, motes a few per second.
    /// </summary>
    public class PlayerStatusFx : MonoBehaviour
    {
        public static readonly Color Gold = new Color(1f, 0.82f, 0.4f), Crimson = new Color(0.95f, 0.15f, 0.1f),
            Bark = new Color(0.6f, 0.42f, 0.2f), Plague = new Color(0.45f, 0.85f, 0.55f), Frost = new Color(0.6f, 0.88f, 1f),
            Poison = new Color(0.45f, 0.9f, 0.2f), Mania = new Color(0.95f, 0.3f, 0.85f), Hush = new Color(0.66f, 0.62f, 0.85f),
            Hex = new Color(0.4f, 0.6f, 0.1f), Weak = new Color(0.55f, 0.2f, 0.8f), Ink = new Color(0.08f, 0.05f, 0.12f);

        private PlayerLife life;
        private Health health;
        private PlayerDebuffs deb;
        private Flasks flasks;
        private FlagellantKit flag;
        private Emotes emotes;
        private PlayerProgress progress;

        private Transform halo, crown, star, stars, eye, gag;
        private Transform feetA, feetB;
        private Renderer haloR, crownR, starR, eyeR, gagR, feetAR, feetBR;
        private Light glow;
        private readonly System.Collections.Generic.Dictionary<Transform, Renderer> diamondLower = new System.Collections.Generic.Dictionary<Transform, Renderer>();
        private float nextMote;
        private bool hidden = true;
        private static Mesh speck, blob;

        /// <summary>For tests and shots: the states this body is showing right now, by name.</summary>
        public readonly System.Collections.Generic.List<string> Showing = new System.Collections.Generic.List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("PlayerStatusFxWatcher");
            DontDestroyOnLoad(go);
            go.AddComponent<Watcher>();
        }

        private class Watcher : MonoBehaviour
        {
            private float next;
            private void Update()
            {
                if (Time.time < next) return;
                next = Time.time + 0.5f;
                foreach (var l in PlayerLife.All)
                    if (l != null && l.GetComponent<PlayerStatusFx>() == null) l.gameObject.AddComponent<PlayerStatusFx>();
            }
        }

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            health = GetComponent<Health>();
            deb = GetComponent<PlayerDebuffs>();
            flasks = GetComponent<Flasks>();
            flag = GetComponent<FlagellantKit>();
            emotes = GetComponent<Emotes>();
            progress = GetComponent<PlayerProgress>();
        }

        private const float HeadTop = 1.95f;

        /// <summary>Shots and tests: states to show as if they held ("blessed", "dazed"...).</summary>
        public static readonly System.Collections.Generic.HashSet<string> DebugForce = new System.Collections.Generic.HashSet<string>();
        public static bool Forced(string s) => DebugForce.Count > 0 && DebugForce.Contains(s);

        private void Update()
        {
            var cam = ViewCamera.Current;
            bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 35f * 35f;
            if (life == null || !near || life.Downed.Value || (health != null && !health.IsAlive)) { HideAll(); return; }
            hidden = false;
            Showing.Clear();
            bool mine = life.IsOwner && !NetworkPlayer.DebugShowSelf;
            float t = Time.time;
            var feet = transform.position;

            bool blessed = ClericKit.InConsecration(feet) || Forced("blessed");
            bool inspired = (health != null && ClericKit.IsInspired(health)) || Forced("inspired");
            bool zeal = (flag != null && flag.Zealous) || Forced("zeal"), martyr = (flag != null && flag.Martyr) || Forced("martyr");
            bool fury = flasks != null && flasks.Bloodfury, bark = flasks != null && flasks.Ironbark;
            bool lust = progress != null && progress.Bloodlust;
            bool brothers = emotes != null && emotes.Brothers;
            bool lastStand = Teamwork.InLastStand(life);
            bool warded = deb != null && deb.Warded;
            bool poison = (deb != null && deb.Poisoned) || Forced("poison"), frost = deb != null && (deb.Frostbitten || (deb.Rooted && !deb.Entangled));
            bool mania = (deb != null && deb.Manic) || Forced("mania"), silenced = (deb != null && deb.Silenced) || Forced("silenced"), dazed = (deb != null && deb.Dazed) || Forced("dazed");
            bool hexed = (deb != null && deb.Accursed) || Forced("hexed"), weak = (deb != null && deb.Cursed) || Forced("weakened"), blind = (deb != null && deb.Blinded) || Forced("blinded");
            if (blessed) Showing.Add("blessed"); if (inspired) Showing.Add("inspired"); if (zeal) Showing.Add("zeal"); if (martyr) Showing.Add("martyr");
            if (fury) Showing.Add("bloodfury"); if (bark) Showing.Add("ironbark"); if (lust) Showing.Add("bloodlust"); if (brothers) Showing.Add("brothers");
            if (lastStand) Showing.Add("laststand"); if (warded) Showing.Add("warded"); if (poison) Showing.Add("poison"); if (frost) Showing.Add("frost");
            if (mania) Showing.Add("mania"); if (silenced) Showing.Add("silenced"); if (dazed) Showing.Add("dazed"); if (hexed) Showing.Add("hexed");
            if (weak) Showing.Add("weakened"); if (blind) Showing.Add("blinded");

            // Head height: only on others (your own camera sits there).
            bool head = !mine;
            Halo(ref halo, ref haloR, head && blessed, "BlessHalo", 0.34f, HeadTop - 0.25f + 0.03f * Mathf.Sin(t * 2f), Gold * (1.1f + 0.25f * Mathf.Sin(t * 3f)), 0f);
            Halo(ref crown, ref crownR, head && martyr, "MartyrCrown", 0.42f, HeadTop - 0.25f, new Color(1f, 0.35f, 0.3f) * (0.9f + 0.5f * Mathf.Abs(Mathf.Sin(t * 5f))), 0f);
            Halo(ref gag, ref gagR, head && silenced, "HushGag", 0.36f, 1.5f, new Color(0.5f, 0.35f, 0.95f) * 0.9f, 0.01f + 12f * Mathf.Sin(t * 1.5f));
            Diamond(ref star, ref starR, head && inspired, "InspireStar", HeadTop + 0.4f + 0.06f * Mathf.Sin(t * 4f), t * 150f, Gold * 1.1f, new Vector3(1f, 1f, 1f));
            Diamond(ref eye, ref eyeR, head && hexed, "HexEye", HeadTop + 0.3f, t * 40f, Hex * (0.9f + 0.6f * Mathf.Abs(Mathf.Sin(t * 2.4f))), new Vector3(1.5f, 0.7f, 0.5f));
            if (head && dazed)
            {
                if (stars == null) stars = BuildStars();
                stars.gameObject.SetActive(true);
                stars.position = feet + Vector3.up * (HeadTop + 0.05f);
                stars.rotation = Quaternion.Euler(0f, t * 360f, 0f);
            }
            else if (stars != null) stars.gameObject.SetActive(false);

            // The feet: the strongest blessing in ring A, the worst affliction in ring B (yours too).
            Color? a = blessed ? Gold : lastStand ? Crimson * (0.6f + 0.6f * Mathf.Pow(Mathf.Abs(Mathf.Sin(t * 2.2f)), 4f))
                : zeal || fury ? Crimson * (0.8f + 0.4f * Mathf.Abs(Mathf.Sin(t * 7f))) : bark ? Bark : warded ? Plague : (Color?)null;
            Color? b = mania ? Mania : frost ? Frost : poison ? Poison : weak ? Weak : hexed ? Hex : (Color?)null;
            FeetRing(ref feetA, ref feetAR, a, "BuffRing", 0.75f + 0.05f * Mathf.Sin(t * 3f), t * 25f);
            FeetRing(ref feetB, ref feetBR, b, "BaneRing", 0.6f, -t * (mania ? 200f : 15f));

            // A warm light round the blessed, so a consecrated party glows in a dark room.
            if (blessed || inspired)
            {
                if (glow == null)
                {
                    var go = new GameObject("BlessLight");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = Vector3.up * 1.2f;
                    glow = go.AddComponent<Light>();
                    glow.type = LightType.Point;
                    glow.shadows = LightShadows.None;
                    glow.color = Gold;
                    glow.range = 3.2f;
                }
                glow.enabled = true;
                glow.intensity = (inspired ? 1.6f : 1.1f) + 0.2f * Mathf.Sin(t * 3f);
            }
            else if (glow != null) glow.enabled = false;

            // Motes, a few a second, by state.
            if (t < nextMote) return;
            nextMote = t + 0.12f;
            float up = mine ? 0.15f : 1f;
            if (blessed) Vfx.Mote(feet + Vector3.up * 0.05f, Gold);
            if (inspired && Random.value < 0.6f) Vfx.Embers(feet + Vector3.up * (mine ? 0.3f : HeadTop + 0.3f), 1, Gold, 1.5f, 0.05f, 0.6f);
            if ((zeal || fury) && Random.value < 0.7f) Vfx.Embers(feet + Vector3.up * (mine ? 0.3f : 1.4f), 1, Crimson, 2f, 0.06f, 0.7f);
            if (lust && Random.value < 0.4f) Drip(feet + Vector3.up * (0.6f + Random.value * up), new Color(0.6f, 0.05f, 0.05f));
            if (bark && Random.value < 0.4f) Vfx.Mote(feet, new Color(0.45f, 0.6f, 0.25f));
            if (brothers && Random.value < 0.35f) Vfx.Embers(feet + Vector3.up * (mine ? 0.3f : 1.1f), 1, new Color(1f, 0.6f, 0.25f), 2.5f, 0.05f, 0.5f);
            if (warded && Random.value < 0.3f) Vfx.Mote(feet, Plague);
            if (poison && Random.value < 0.6f) Drip(feet + Vector3.up * (0.4f + Random.value * up), Poison);
            if (frost && Random.value < 0.4f) Vfx.Mote(feet, Frost);
            if (mania && Random.value < 0.6f) Vfx.Mote(feet, Mania);
            if (weak && Random.value < 0.5f) Drip(feet + Vector3.up * (0.5f + Random.value * up), Weak);
            if (blind && !mine && Random.value < 0.7f) Drip(feet + Vector3.up * 1.65f + transform.forward * 0.12f, Ink, 0.07f);
            if (silenced && !mine && Random.value < 0.3f) Drip(feet + Vector3.up * 1.6f, Hush);
        }

        /// <summary>A bead that falls off the body and is gone (poison, weakness, ink, blood).</summary>
        private static void Drip(Vector3 at, Color c, float size = 0.05f)
        {
            if (speck == null) speck = MeshKit.Box(Vector3.one);
            var p = at + new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            bool dark = c.maxColorComponent < 0.2f;
            Vfx.Spawn(dark ? (blob != null ? blob : blob = MeshKit.Ball(1f, 6, 4)) : speck, dark ? Mat.Smoke : Mat.Glow, p, Vector3.down * 0.3f, 0.9f, size, size * 0.4f, dark ? (Color?)null : c * 1.5f, 6f, 0.5f);
        }

        private static Mesh ringMesh;
        private static Mesh RingMesh => ringMesh != null ? ringMesh
            : ringMesh = MeshKit.Lathe("playerStatusRing", new[] { new Vector2(0.86f, 0f), new Vector2(1f, 0.02f), new Vector2(0.86f, 0.04f) }, 28, false);

        private Renderer MakeRing(string name, out Transform tr)
        {
            var go = MeshKit.Part(transform, name, RingMesh, Mat.Glow, Vector3.zero);
            tr = go.transform;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        private void Halo(ref Transform tr, ref Renderer r, bool on, string name, float radius, float height, Color c, float tilt)
        {
            if (!on) { if (tr != null) tr.gameObject.SetActive(false); return; }
            if (tr == null) r = MakeRing(name, out tr);
            tr.gameObject.SetActive(true);
            var cam = ViewCamera.Current;
            var top = transform.position + Vector3.up * height;
            if (tilt == 0f && cam != null)
            {
                // A halo stands up behind the head, facing whoever looks (from eye level a flat ring is only a line).
                var toCam = cam.transform.position - top; toCam.y *= 0.3f;
                if (toCam.sqrMagnitude < 0.01f) toCam = transform.forward;
                toCam.Normalize();
                tr.position = top - toCam * 0.22f;
                tr.rotation = Quaternion.FromToRotation(Vector3.up, toCam);
            }
            else
            {
                tr.localPosition = Vector3.up * height;
                tr.localRotation = Quaternion.Euler(tilt, 0f, 0f);
            }
            tr.localScale = new Vector3(radius, 1.4f, radius);
            ArtLibrary.SetGlow(r, c);
        }

        private void FeetRing(ref Transform tr, ref Renderer r, Color? c, string name, float radius, float spin)
        {
            if (c == null) { if (tr != null) tr.gameObject.SetActive(false); return; }
            if (tr == null) r = MakeRing(name, out tr);
            tr.gameObject.SetActive(true);
            tr.localPosition = Vector3.up * 0.06f;
            tr.localRotation = Quaternion.Euler(0f, spin, 0f);
            tr.localScale = new Vector3(radius, 1f, radius);
            ArtLibrary.SetGlow(r, c.Value * 0.9f);
        }

        private void Diamond(ref Transform tr, ref Renderer r, bool on, string name, float height, float spin, Color c, Vector3 shape)
        {
            if (!on) { if (tr != null) tr.gameObject.SetActive(false); return; }
            if (tr == null)
            {
                tr = new GameObject(name).transform;
                tr.SetParent(transform, false);
                var upper = MeshKit.Part(tr, "Up", MeshKit.Cylinder(0.11f, 0f, 0.16f, 4), Mat.Glow, Vector3.zero);
                var lower = MeshKit.Part(tr, "Down", MeshKit.Cylinder(0.11f, 0f, 0.2f, 4), Mat.Glow, Vector3.zero, new Vector3(180f, 0f, 0f));
                r = upper.GetComponent<Renderer>();
                r.shadowCastingMode = lower.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                diamondLower[tr] = lower.GetComponent<Renderer>();
            }
            tr.gameObject.SetActive(true);
            tr.localPosition = Vector3.up * height;
            tr.localRotation = Quaternion.Euler(0f, spin, 0f);
            tr.localScale = shape;
            ArtLibrary.SetGlow(r, c);
            if (diamondLower.TryGetValue(tr, out var lr)) ArtLibrary.SetGlow(lr, c * 0.8f);
        }

        private Transform BuildStars()
        {
            var root = new GameObject("DazeStars").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                var s = MeshKit.Part(root, "Star" + i, MeshKit.Ball(0.05f, 6, 4), Mat.Glow, Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 0.28f);
                ArtLibrary.SetGlow(s.GetComponent<Renderer>(), new Color(1f, 0.88f, 0.4f) * 1.4f);
                s.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        private void HideAll()
        {
            if (hidden) return;
            hidden = true;
            Showing.Clear();
            foreach (var x in new[] { halo, crown, star, stars, eye, gag, feetA, feetB })
                if (x != null) x.gameObject.SetActive(false);
            if (glow != null) glow.enabled = false;
        }
    }
}
