using Deepbound.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Plays an enemy's way in on its rig (<see cref="EnemyBrain.Entrance"/>), from server time so every peer sees the
    /// same moment: up through the floor, out of a wall (low, pitched forward, then standing), or through a violet rift.
    /// Visual only: the body is already where it will stand, and the server holds it still until it is through.
    /// </summary>
    public class EnemyEntrance : MonoBehaviour
    {
        private EnemyBrain brain;
        private EnemyBrain.EntranceKind kind;
        private Vector3 from;
        private double at;
        private float seconds;
        private Vector3 restPos;
        private Quaternion restRot;
        private Transform portal, crack;
        private bool fxDone, near, hidden;
        private float tell;
        private Renderer[] bodies;
        private bool[] wasOn;

        private static readonly Color Dirt = new Color(0.32f, 0.26f, 0.2f), Rift = new Color(0.55f, 0.25f, 1f);

        public void Init(EnemyBrain b)
        {
            brain = b;
            kind = (EnemyBrain.EntranceKind)b.Entrance.Value;
            from = b.EntranceFrom.Value;
            at = b.EntranceAt.Value;
            seconds = EnemyBrain.EntranceSeconds(kind);
            tell = EnemyBrain.EntranceTell;
            restPos = transform.localPosition;
            restRot = transform.localRotation;
            var cam = ViewCamera.Current;
            near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < 40f * 40f;
            Hide(true);
            BuildTell();
            Apply(0f);
        }

        private float Progress()
        {
            var nm = NetworkManager.Singleton;
            double now = nm != null && nm.IsListening ? nm.ServerTime.Time : at + tell + seconds;
            return seconds <= 0f ? 1f : Mathf.Clamp01((float)((now - at - tell) / seconds));
        }

        /// <summary>0..1 through the tell (1 once it is over).</summary>
        private float TellProgress()
        {
            var nm = NetworkManager.Singleton;
            double now = nm != null && nm.IsListening ? nm.ServerTime.Time : at + tell;
            return tell <= 0f ? 1f : Mathf.Clamp01((float)((now - at) / tell));
        }

        private void Hide(bool on)
        {
            if (on == hidden) return;
            hidden = on;
            if (on)
            {
                bodies = GetComponentsInChildren<Renderer>(true);
                wasOn = new bool[bodies.Length];
                for (int i = 0; i < bodies.Length; i++) { wasOn[i] = bodies[i].enabled; bodies[i].enabled = false; }
            }
            else if (bodies != null)
                for (int i = 0; i < bodies.Length; i++) if (bodies[i] != null) bodies[i].enabled = wasOn[i];
        }

        private static readonly Color Ember = new Color(1f, 0.45f, 0.15f);

        /// <summary>The tell: a glowing crack with a bulge of stone on the wall, a split in the floor, or the rift opening.</summary>
        private void BuildTell()
        {
            var root = transform.parent != null ? transform.parent.position : transform.position;
            if (kind == EnemyBrain.EntranceKind.Portal) { BuildPortal(root); return; }
            crack = new GameObject("EntranceTell").transform;
            bool wall = kind == EnemyBrain.EntranceKind.Wall;
            var outward = from - root; outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.forward;
            outward.Normalize();
            if (wall) crack.SetPositionAndRotation(from - outward * 0.25f + Vector3.up * 0.9f, Quaternion.LookRotation(-outward));
            else crack.SetPositionAndRotation(root + Vector3.up * 0.03f, Quaternion.identity);
            // Jagged glowing seams.
            var rng = new System.Random((int)(at * 1000.0) & 0x7fffffff);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + (float)rng.NextDouble() * 30f, len = 0.35f + (float)rng.NextDouble() * 0.45f;
                var dir = wall ? new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad), 0f) : new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad));
                var seam = MeshKit.Part(crack, "Seam", MeshKit.Box(wall ? new Vector3(len, 0.035f, 0.03f) : new Vector3(len, 0.02f, 0.04f)), Mat.Glow, dir * len * 0.5f, wall ? new Vector3(0f, 0f, a) : new Vector3(0f, -a, 0f));
                var r = seam.GetComponent<Renderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ArtLibrary.SetGlow(r, Ember * 1.6f);
            }
            // The bulge: a lump of the wall (or floor) pushed out from behind.
            var lump = MeshKit.Part(crack, "Bulge", MeshKit.Ball(0.5f, 8, 6), Mat.Rubble, Vector3.zero, Vector3.zero, new Vector3(1.2f, wall ? 1.4f : 0.5f, wall ? 0.5f : 1.2f));
            lump.name = "Bulge";
            if (near) Sfx.Play(Sound.Creak, crack.position, 0.8f, 0.6f);
        }

        private void TickTell(float k)
        {
            if (crack == null) return;
            // Swells and shakes harder as it comes, the seams brighten.
            var lump = crack.Find("Bulge");
            if (lump != null)
            {
                float swell = 0.15f + 0.85f * k;
                bool wall = kind == EnemyBrain.EntranceKind.Wall;
                lump.localScale = new Vector3(1.2f, wall ? 1.4f : 0.5f, wall ? 0.5f : 1.2f) * swell;
                lump.localPosition = new Vector3(Mathf.Sin(Time.time * 47f), Mathf.Cos(Time.time * 39f), 0f) * 0.03f * k;
            }
            if (near && Random.value < Time.deltaTime * 6f) Vfx.Embers(crack.position, 2, new Color(0.45f, 0.42f, 0.38f), 1.5f, 0.06f, 0.6f);
        }

        private void LateUpdate()
        {
            if (brain == null) { Destroy(this); return; }
            float tk = TellProgress();
            if (tk < 1f)
            {
                TickTell(tk);
                if (portal != null) { float o = tk * 0.85f; portal.localScale = new Vector3(o, o, 1f); }
                Apply(0f);
                return;
            }
            Hide(false);
            if (crack != null) { Destroy(crack.gameObject); crack = null; }
            float t = Progress();
            if (!fxDone) { fxDone = true; StartFx(); }
            if (t >= 1f) { Finish(); return; }
            Apply(t);
        }

        private void StartFx()
        {
            var root = transform.parent != null ? transform.parent.position : transform.position;
            if (!near) return;
            if (kind == EnemyBrain.EntranceKind.Floor)
            {
                Vfx.Ring(root + Vector3.up * 0.05f, 1.1f, Dirt, 0.6f);
                for (int i = 0; i < 4; i++) Vfx.Embers(root + Random.insideUnitSphere * 0.4f, 5, Dirt, 3.5f, 0.07f, 0.9f);
                Sfx.Play(Sound.BoneBreak, root, 0.7f, 0.8f);
            }
            else if (kind == EnemyBrain.EntranceKind.Wall)
            {
                var w = from + Vector3.up * 0.9f;
                BoneBurst.Spawn(w, 0.8f, false, Mat.Rubble);
                for (int i = 0; i < 3; i++) Vfx.Embers(w + Random.insideUnitSphere * 0.5f, 6, new Color(0.45f, 0.42f, 0.38f), 3f, 0.08f, 1f);
                Sfx.Play(Sound.Slam, w, 0.6f, 0.7f);
                Sfx.Play(Sound.Rattle, w, 0.8f, 0.9f);
            }
            else
            {
                Vfx.Flash(root + Vector3.up * 1.1f, Rift, 2.5f, 6f, seconds);
                Sfx.Play(Sound.Shade, root, 0.8f, 0.7f);
            }
        }

        private void Apply(float t)
        {
            switch (kind)
            {
                case EnemyBrain.EntranceKind.Floor:
                {
                    // Lurching: fast at first, a stall, then the last heave.
                    float k = 1f - Mathf.Pow(1f - t, 2.4f) - Mathf.Sin(t * Mathf.PI * 3f) * 0.05f * (1f - t);
                    transform.localPosition = restPos + Vector3.down * 1.7f * (1f - k) + new Vector3(Mathf.Sin(t * 40f) * 0.02f * (1f - t), 0f, 0f);
                    transform.localRotation = restRot;
                    break;
                }
                case EnemyBrain.EntranceKind.Wall:
                {
                    // Out of the stone on all fours for the first two thirds, then up onto its feet.
                    var parent = transform.parent;
                    if (parent == null) break;
                    float crawl = Mathf.Clamp01(t / 0.7f), stand = Mathf.Clamp01((t - 0.55f) / 0.45f);
                    var start = parent.InverseTransformPoint(from);
                    start.y = restPos.y;
                    var pos = Vector3.Lerp(start, restPos, 1f - Mathf.Pow(1f - crawl, 2f));
                    pos.y = restPos.y - 0.55f * (1f - stand) + Mathf.Abs(Mathf.Sin(t * 22f)) * 0.04f * (1f - stand);
                    transform.localPosition = pos;
                    transform.localRotation = restRot * Quaternion.Euler(65f * (1f - stand), 0f, Mathf.Sin(t * 18f) * 6f * (1f - stand));
                    break;
                }
                case EnemyBrain.EntranceKind.Portal:
                {
                    // It walks out of the rift: from a pace behind it to where it stands.
                    float k = Mathf.Clamp01((t - 0.2f) / 0.8f);
                    transform.localPosition = restPos + Vector3.back * 1.1f * (1f - k);
                    transform.localRotation = restRot;
                    if (portal != null)
                    {
                        float open = Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(t / 0.2f)) * Mathf.Clamp01((1f - t) / 0.25f + 0.15f);
                        portal.localScale = new Vector3(open, open, 1f);
                    }
                    break;
                }
            }
        }

        private void BuildPortal(Vector3 root)
        {
            var parent = transform.parent;
            var face = parent != null ? parent.forward : Vector3.forward;
            portal = new GameObject("Rift").transform;
            portal.SetPositionAndRotation(root + Vector3.up * 1.05f - face * 0.25f, Quaternion.LookRotation(face));
            var disc = MeshKit.Part(portal, "Disc", MeshKit.Cylinder(0.75f, 0.75f, 0.04f, 20), Mat.Glow, Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 1.45f));
            var r = disc.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ArtLibrary.SetGlow(r, Rift * 1.6f);
            var rim = MeshKit.Part(portal, "Rim", MeshKit.Cylinder(0.85f, 0.85f, 0.02f, 20), Mat.Glow, Vector3.forward * -0.02f, new Vector3(90f, 0f, 0f), new Vector3(1f, 1f, 1.45f));
            ArtLibrary.SetGlow(rim.GetComponent<Renderer>(), new Color(0.15f, 0.05f, 0.3f) * 2f);
            rim.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            portal.localScale = new Vector3(0.01f, 0.01f, 1f);
            Destroy(portal.gameObject, tell + seconds + 0.6f);
        }

        private void Finish()
        {
            Hide(false);
            if (crack != null) Destroy(crack.gameObject);
            transform.localPosition = restPos;
            transform.localRotation = restRot;
            if (portal != null) Destroy(portal.gameObject);
            Destroy(this);
        }

        private void OnDestroy()
        {
            Hide(false);
            if (crack != null) Destroy(crack.gameObject);
            if (portal != null) Destroy(portal.gameObject);
        }
    }
}
