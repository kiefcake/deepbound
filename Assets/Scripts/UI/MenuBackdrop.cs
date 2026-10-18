using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The title screen's backdrop: two little sets, built at runtime from the game's own art far
    /// below the dungeon, that the menu camera cuts between (a slow fade through black every ~15 s):
    ///
    /// <list type="bullet">
    /// <item><b>Rest</b>: a quiet corner of camp at night. The Mage sits against a log by a small
    ///   fire with the castle cat curled asleep in their lap (it breathes; little z's drift up).</item>
    /// <item><b>Battle</b>: the Knight braced under his shield, the Ranger loosing arrows, the Rogue
    ///   cutting at a flank and the Cleric's beam on the Knight, against the Hollow King: his slam
    ///   circle fills and breaks into a shockwave, and his crown of blades wheels out.</item>
    /// </list>
    /// Torn down (hidden) once the local player spawns. Sits on the menu camera, which it positions.
    /// </summary>
    public class MenuBackdrop : MonoBehaviour
    {
        private static readonly Vector3 Origin = new Vector3(0f, -500f, 0f);
        private static readonly Vector3 RestAt = Origin + new Vector3(-80f, 0f, 0f), BattleAt = Origin + new Vector3(80f, 0f, 0f);
        private const float SceneSeconds = 15f, FadeSeconds = 1.1f;

        private GameObject set;
        private int scene;               // 0 rest, 1 battle
        private float sceneStart, cutAt = -1f;

        // Rest
        private Transform cat, catBody, catHead;
        private HumanoidRig mage;
        // Battle
        private HumanoidRig knight, rogue, ranger, cleric, king;
        private Transform beam;
        private float nextSlam, slamAt = -1f, nextArrow, nextStab, nextBlades, bladesUntil, nextBlade, bladeAngle, nextCast, nextCleave, cleaveAt = -1f, shake;
        private readonly List<(Vector3 at, float when)> arrowHits = new List<(Vector3, float)>();

        private void Start()
        {
            set = new GameObject("MenuBackdrop");
            BuildRest();
            BuildBattle();
            scene = Random.value < 0.5f ? 0 : 1;
            Show(scene);
        }

        // ------------------------------------------------------------------
        // The rest: a fire, a log, the Mage and the cat
        // ------------------------------------------------------------------

        private void BuildRest()
        {
            var o = RestAt;
            var b = new MeshKit.Batcher();
            b.AddBox(o + Vector3.down * 0.2f, new Vector3(20f, 0.4f, 20f), Mat.Flagstone);
            b.AddBox(o + new Vector3(0f, 4f, 4.5f), new Vector3(20f, 8f, 1f), Mat.StoneWall);
            b.AddBox(o + new Vector3(-3.4f, 2.2f, 3.6f), new Vector3(1.1f, 4.4f, 1.1f), Mat.StoneWall);
            // The log they lean on, and a few stones round the fire.
            b.Add(MeshKit.Cylinder(0.28f, 0.3f, 2.4f, 10), Mat.Wood, Matrix4x4.TRS(o + new Vector3(0f, 0.28f, 0.75f), Quaternion.Euler(0f, 8f, 90f), Vector3.one));
            var fire = o + new Vector3(0.9f, 0f, -1.4f);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                b.AddBox(fire + new Vector3(Mathf.Cos(a), 0.08f, Mathf.Sin(a)) * 0.55f, new Vector3(0.22f, 0.16f, 0.2f), Mat.Rubble, Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f));
            }
            for (int i = 0; i < 3; i++)
                b.Add(MeshKit.Cylinder(0.06f, 0.06f, 0.8f, 6), Mat.Wood, Matrix4x4.TRS(fire + Vector3.up * 0.1f, Quaternion.Euler(0f, i * 60f, 80f), Vector3.one));
            // Hay, a barrel and a crate; a lantern on the wall.
            b.Add(MeshKit.Cylinder(0.55f, 0.55f, 1f, 10), Mat.Cloth, Matrix4x4.TRS(o + new Vector3(-2.2f, 0.55f, 1.6f), Quaternion.Euler(0f, 20f, 90f), Vector3.one));
            Props.Barrel(b, o + new Vector3(2.4f, 0f, 2.2f));
            Props.Crate(b, o + new Vector3(2.9f, 0f, 1.1f), 0.8f, 18f);
            b.Build(set.transform, "RestSet", collide: false);
            var flame = MeshKit.Pivot(set.transform, "Campfire", fire);
            Props.AddFlame(flame, new Vector3(0f, 0.1f, 0f), 1.8f, new Color(1f, 0.55f, 0.25f), 9f, 9f, true);
            Props.WallTorch(set.transform, o + new Vector3(-2.2f, 2.3f, 3.98f), Vector3.back, false, new Color(1f, 0.65f, 0.35f));
            // Firelight on the lap, so the black cat reads against the robe.
            AddLight(o + new Vector3(0.1f, 0.9f, -0.5f), new Color(1f, 0.7f, 0.42f), 2.2f, 3.2f);

            // The Mage, sitting on the ground against the log, legs out and crossed, head bowed.
            var mageRoot = new GameObject("RestingMage").transform;
            mageRoot.SetParent(set.transform, false);
            mageRoot.SetPositionAndRotation(o + new Vector3(0f, 0f, 0.35f), Quaternion.Euler(0f, 215f, 0f));
            mage = CharacterArt.BuildMage(mageRoot, NetworkPlayer.SlotColors[1]);
            mage.enabled = false; // posed by hand below, not animated
            mage.Hips.localPosition = new Vector3(0f, 0.22f, 0f);
            mage.Hips.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            // Cross-legged: each leg folded forward and across the other, shortened under the robe.
            mage.LegL.localRotation = Quaternion.Euler(-90f, 58f, 0f);
            mage.LegR.localRotation = Quaternion.Euler(-88f, -58f, 0f);
            mage.LegL.localScale = mage.LegR.localScale = new Vector3(1f, 0.62f, 1f);
            mage.Torso.localRotation = Quaternion.Euler(10f, 0f, 0f);
            mage.Head.localRotation = Quaternion.Euler(24f, 8f, 4f);
            mage.ArmL.localRotation = Quaternion.Euler(-48f, 0f, -24f);   // resting on the cat
            mage.ArmR.localRotation = Quaternion.Euler(-30f, 0f, 14f);
            foreach (var r in mage.GetComponentsInChildren<Renderer>()) if (r.name.Contains("Skirt") || r.name.Contains("Trim")) r.enabled = false;

            // The cat, curled up in the lap: a black loaf, head tucked on its paws, tail round its nose.
            cat = MeshKit.Pivot(mageRoot, "SleepingCat", new Vector3(0f, 0.43f, 0.36f), new Vector3(0f, 25f, 0f));
            cat.localScale = Vector3.one * 1.25f;
            var fur = new Color(0.26f, 0.24f, 0.25f);
            void Fur(GameObject g) => ArtLibrary.Tint(g.GetComponent<Renderer>(), fur);
            catBody = MeshKit.Pivot(cat, "Body", Vector3.zero);
            Fur(MeshKit.Part(catBody, "Loaf", MeshKit.Ball(0.13f, 12, 9), Mat.Cloth, Vector3.zero, default, new Vector3(1.25f, 0.62f, 1.05f)));
            Fur(MeshKit.Part(catBody, "Haunch", MeshKit.Ball(0.09f, 10, 8), Mat.Cloth, new Vector3(-0.08f, 0.01f, -0.05f), default, new Vector3(1f, 0.75f, 1f)));
            catHead = MeshKit.Pivot(cat, "Head", new Vector3(0.12f, 0.02f, 0.08f), new Vector3(10f, 40f, -12f));
            Fur(MeshKit.Part(catHead, "Skull", MeshKit.Ball(0.07f, 10, 8), Mat.Cloth, Vector3.zero, default, new Vector3(1.1f, 0.92f, 1f)));
            Fur(MeshKit.Part(catHead, "Muzzle", MeshKit.Ball(0.032f, 8, 6), Mat.Cloth, new Vector3(0f, -0.02f, 0.058f)));
            foreach (float s in new[] { -1f, 1f })
            {
                Fur(MeshKit.Part(catHead, "Ear", MeshKit.Lathe("catEar", new[] { new Vector2(0.026f, 0f), new Vector2(0f, 0.06f) }, 4), Mat.Cloth, new Vector3(s * 0.038f, 0.05f, -0.005f), new Vector3(-20f, 0f, s * -22f)));
                // Closed eyes: two soft dark arcs.
                MeshKit.Part(catHead, "Lid", MeshKit.Box(new Vector3(0.022f, 0.004f, 0.004f)), Mat.Dark, new Vector3(s * 0.028f, 0.012f, 0.066f), new Vector3(0f, 0f, s * 12f));
                Fur(MeshKit.Part(catBody, "Paw", MeshKit.Ball(0.026f, 6, 5), Mat.Cloth, new Vector3(0.14f, -0.045f, 0.06f + s * 0.025f)));
            }
            // A white chest and muzzle and pink inner ears: it reads as a cat even in the firelight.
            var white = new Color(0.9f, 0.88f, 0.84f);
            ArtLibrary.Tint(MeshKit.Part(catHead, "WhiteMuzzle", MeshKit.Ball(0.034f, 8, 6), Mat.Cloth, new Vector3(0f, -0.024f, 0.06f)).GetComponent<Renderer>(), white);
            ArtLibrary.Tint(MeshKit.Part(catBody, "WhiteChest", MeshKit.Ball(0.05f, 8, 6), Mat.Cloth, new Vector3(0.12f, -0.02f, 0.06f), default, new Vector3(1f, 0.8f, 1.2f)).GetComponent<Renderer>(), white);
            foreach (float s2 in new[] { -1f, 1f })
                ArtLibrary.Tint(MeshKit.Part(catHead, "InnerEar", MeshKit.Lathe("catEarIn", new[] { new Vector2(0.016f, 0f), new Vector2(0f, 0.04f) }, 4), Mat.Cloth, new Vector3(s2 * 0.038f, 0.054f, 0.006f), new Vector3(-20f, 0f, s2 * -22f)).GetComponent<Renderer>(), new Color(0.8f, 0.5f, 0.55f));
            var nose = MeshKit.Part(catHead, "Nose", MeshKit.Ball(0.008f, 5, 4), Mat.Leather, new Vector3(0f, -0.008f, 0.088f));
            ArtLibrary.Tint(nose.GetComponent<Renderer>(), new Color(0.45f, 0.25f, 0.28f));
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Deg2Rad * (200f + i * 22f);
                Fur(MeshKit.Part(catBody, "Tail" + i, MeshKit.Ball(0.026f - i * 0.0012f, 6, 5), Mat.Cloth,
                    new Vector3(Mathf.Cos(a) * 0.16f, -0.04f + i * 0.002f, Mathf.Sin(a) * 0.15f)));
            }
        }

        // ------------------------------------------------------------------
        // The battle: four heroes and the Hollow King
        // ------------------------------------------------------------------

        private void AddLight(Vector3 at, Color c, float intensity, float range)
        {
            var l = new GameObject("TitleLight").AddComponent<Light>();
            l.transform.SetParent(set.transform, false);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
        }

        private void BuildBattle()
        {
            var o = BattleAt;
            // Key light on the heroes, a cold rim behind the King, a warm fill from the side.
            AddLight(o + new Vector3(-1f, 3.6f, -0.5f), new Color(1f, 0.82f, 0.6f), 3.2f, 11f);
            AddLight(o + new Vector3(0.5f, 4f, 6.5f), new Color(0.45f, 0.65f, 1f), 4f, 10f);
            AddLight(o + new Vector3(3.5f, 2f, 1f), new Color(1f, 0.55f, 0.3f), 2f, 8f);
            var b = new MeshKit.Batcher();
            b.AddBox(o + Vector3.down * 0.2f, new Vector3(30f, 0.4f, 30f), Mat.Flagstone);
            b.AddBox(o + new Vector3(0f, 6f, 9f), new Vector3(30f, 12f, 0.8f), Mat.StoneWall);
            foreach (float x in new[] { -6f, 6f }) Props.Pillar(b, o + new Vector3(x, 0f, 6.5f), 11f);
            Props.Throne(b, o + new Vector3(0f, 0f, 7.6f), Quaternion.identity);
            Props.BonePile(b, o + new Vector3(-4f, 0f, 3.5f), new System.Random(3));
            Props.Rubble(b, o + new Vector3(4.6f, 0f, 1.5f), new System.Random(4));
            b.Build(set.transform, "BattleSet", collide: false);
            Props.Brazier(set.transform, o + new Vector3(-5f, 0f, 3f), new Color(1f, 0.55f, 0.25f), true);
            Props.Brazier(set.transform, o + new Vector3(5f, 0f, 4f), new Color(0.35f, 0.65f, 1f), true);
            foreach (var x in new[] { -6f, 6f }) Props.Banner(set.transform, o + new Vector3(x, 7f, 8.55f), Vector3.back, new Color(0.3f, 0.07f, 0.34f));

            HumanoidRig Hero(System.Func<Transform, Color, HumanoidRig> build, Vector3 at, Vector3 lookAt, int slot)
            {
                var root = new GameObject("Hero").transform;
                root.SetParent(set.transform, false);
                root.position = o + at;
                var to = lookAt - at; to.y = 0f;
                root.rotation = Quaternion.LookRotation(to);
                return build(root, NetworkPlayer.SlotColors[slot % NetworkPlayer.SlotColors.Length]);
            }
            var kingAt = new Vector3(0f, 0f, 4.2f);
            knight = Hero(CharacterArt.BuildKnight, new Vector3(-0.3f, 0f, 1.4f), kingAt, 0);
            knight.Blocking = true;
            rogue = Hero(CharacterArt.BuildRogue, new Vector3(2.3f, 0f, 4.6f), kingAt, 2);
            ranger = Hero(CharacterArt.BuildRanger, new Vector3(-3.4f, 0f, -0.8f), kingAt, 3);
            cleric = Hero(CharacterArt.BuildCleric, new Vector3(-1.5f, 0f, -1.4f), new Vector3(-0.3f, 0f, 1.4f), 1);

            var kingRoot = new GameObject("King").transform;
            kingRoot.SetParent(set.transform, false);
            kingRoot.SetPositionAndRotation(o + kingAt, Quaternion.LookRotation(new Vector3(-0.3f, 0f, 1.4f) - kingAt));
            king = CharacterArt.BuildSkeleton(kingRoot, CharacterArt.SkeletonKind.King);

            // The Cleric's beam on the Knight: a stretched glowing cylinder, pulsing.
            beam = MeshKit.Part(set.transform, "ClericBeam", MeshKit.Cylinder(1f, 1f, 1f, 8), Mat.Glow, Vector3.zero).transform;
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------

        /// <summary>Tests: cut to a scene now (0 rest, 1 battle).</summary>
        public static void DebugShow(int s)
        {
            var m = FindAnyObjectByType<MenuBackdrop>();
            if (m != null) { m.cutAt = -1f; m.Show(s); m.sceneStart = Time.time - 6f; }
        }

        private void Show(int s)
        {
            scene = s;
            sceneStart = Time.time;
            if (scene == 0)
            {
                Lobby.ApplyCastleAtmosphere();
                TickRest(0f);
            }
            else
            {
                DungeonBuilder.ApplyAtmosphere();
                RenderSettings.ambientSkyColor = new Color(0.24f, 0.24f, 0.32f);
                RenderSettings.ambientEquatorColor = new Color(0.16f, 0.14f, 0.16f);
                RenderSettings.ambientGroundColor = new Color(0.08f, 0.07f, 0.07f);
                nextSlam = Time.time + 1.5f;
                nextArrow = Time.time + 0.6f;
                nextStab = Time.time + 0.3f;
                nextBlades = Time.time + 5f;
                nextCast = Time.time + 1f;
            }
        }

        private void Update()
        {
            // Hidden while playing, back when you leave a session.
            bool show = NetworkPlayer.Local == null;
            if (set.activeSelf != show)
            {
                set.SetActive(show);
                if (show) Show(scene);
            }
            if (!show) return;

            float t = Time.time - sceneStart;
            if (cutAt < 0f && t >= SceneSeconds) cutAt = Time.time;
            if (cutAt > 0f && Time.time - cutAt >= FadeSeconds) { cutAt = -1f; Show(1 - scene); t = 0f; }

            if (scene == 0) TickRest(t); else TickBattle(t);
        }

        private void TickRest(float t)
        {
            // The camera eases in, the cat breathes, the Mage nods.
            // From over the fire's shoulder toward the pair; the subject sits right of centre, clear of the menu.
            var from = RestAt + new Vector3(-0.55f, 1.05f, -2.3f);
            var to = RestAt + new Vector3(-0.35f, 0.85f, -1.6f);
            var pos = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / SceneSeconds));
            var look = RestAt + new Vector3(0.05f, 0.5f, 0.35f);
            var rot = Quaternion.LookRotation(look - pos);
            transform.SetPositionAndRotation(pos, rot * Quaternion.Euler(0f, -14f, 0f));
            // The cat's sleeping face turned three-quarters to the camera (its head is along local +x).
            if (cat != null)
            {
                var toCam = transform.position - cat.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.01f) cat.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, 35f, 0f) * toCam) * Quaternion.Euler(0f, -90f, 0f);
            }
            if (catBody != null) catBody.localScale = new Vector3(1f, 1f + Mathf.Sin(Time.time * 2.1f) * 0.05f, 1f + Mathf.Sin(Time.time * 2.1f) * 0.02f);
            if (catHead != null) catHead.localRotation = Quaternion.Euler(10f + Mathf.Sin(Time.time * 2.1f) * 1.5f, 40f, -12f);
            if (mage != null) mage.Head.localRotation = Quaternion.Euler(24f + Mathf.Sin(Time.time * 0.6f) * 3f, 8f, 4f);
            if (Random.value < Time.deltaTime * 3f) Vfx.Embers(RestAt + new Vector3(0.9f, 0.3f, -1.4f), 2, new Color(1f, 0.6f, 0.25f), 1.2f, 0.03f, 1.4f);
        }

        private void TickBattle(float t)
        {
            var o = BattleAt;
            // A slow arc round the fight, low and from behind the heroes.
            float a = Mathf.Lerp(-32f, 12f, t / SceneSeconds) * Mathf.Deg2Rad;
            var cam = o + new Vector3(Mathf.Sin(a) * 6.8f - 0.6f, 1.55f, -Mathf.Cos(a) * 6.8f + 1.4f);
            var focus = o + new Vector3(0f, 1.8f, 2.4f);
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
            var jolt = shake > 0f ? new Vector3(Mathf.Sin(Time.time * 53f), Mathf.Sin(Time.time * 41f), 0f) * shake * 0.6f : Vector3.zero;
            transform.SetPositionAndRotation(cam, Quaternion.LookRotation(focus - cam) * Quaternion.Euler(-4f + jolt.y, -16f + jolt.x, 0f));
            // Embers and grit always drifting through the hall.
            if (Random.value < Time.deltaTime * 10f)
                Vfx.Embers(o + new Vector3(Random.Range(-4f, 4f), 0.2f, Random.Range(-1f, 6f)), 1, new Color(1f, 0.55f, 0.25f), 0.8f, 0.03f, 2f);
            // The King cleaves at the Knight, who takes it on the shield: sparks and a flash.
            if (cleaveAt < 0f && Time.time >= nextCleave && slamAt < 0f)
            {
                nextCleave = Time.time + 3.1f;
                cleaveAt = Time.time + 0.75f;
                king.Play(RigAction.Windup, 0.75f);
                Telegraph.ShowCone(king.transform.position, king.transform.forward, 150f, 4.2f, 0.75f, null);
            }
            if (cleaveAt > 0f && Time.time >= cleaveAt)
            {
                cleaveAt = -1f;
                king.Play(RigAction.Swing, 0.35f);
                knight.Play(RigAction.Stagger, 0.3f);
                var shieldAt = knight.transform.position + Vector3.up * 1.3f + knight.transform.forward * 0.5f;
                Vfx.Embers(shieldAt, 16, new Color(1f, 0.85f, 0.5f), 5f, 0.04f, 0.5f);
                Vfx.Flash(shieldAt, new Color(1f, 0.85f, 0.55f), 3f, 4f, 0.15f);
                shake = Mathf.Max(shake, 0.35f);
            }
            for (int i = arrowHits.Count - 1; i >= 0; i--)
                if (Time.time >= arrowHits[i].when)
                {
                    Vfx.Embers(arrowHits[i].at, 6, new Color(1f, 0.9f, 0.7f), 2.5f, 0.03f, 0.35f);
                    king.Flinch(arrowHits[i].at - king.transform.forward, 0.4f);
                    arrowHits.RemoveAt(i);
                }

            var kingPos = king.transform.position;
            // The slam: a red circle fills under the King, then breaks into a shockwave.
            if (slamAt < 0f && Time.time >= nextSlam)
            {
                slamAt = Time.time + 1.3f;
                nextSlam = Time.time + 6.5f;
                king.Play(RigAction.Slam, 1.6f);
                Telegraph.Show(kingPos, 5.5f, 1.3f, null);
            }
            if (slamAt > 0f && Time.time >= slamAt)
            {
                slamAt = -1f;
                ShockwaveRing.Spawn(kingPos, 11f, 9f, new Color(0.18f, 0.32f, 0.55f));
                Vfx.Ring(kingPos, 5.5f, new Color(1f, 0.6f, 0.3f), 0.4f);
                Vfx.Flash(kingPos + Vector3.up, new Color(1f, 0.6f, 0.3f), 4f, 9f, 0.3f);
                Vfx.Smoke(kingPos, 5, 2.5f, 1.4f);
                knight.Flinch(kingPos, 0.8f);
                shake = 1f;
                rogue.Flinch(kingPos, 0.5f);
            }
            // The Crown of Blades: golden bolts wheeling out of him.
            if (Time.time >= nextBlades) { nextBlades = Time.time + 7f; bladesUntil = Time.time + 1.8f; king.Play(RigAction.Cast, 1.8f); }
            if (Time.time < bladesUntil && Time.time >= nextBlade)
            {
                nextBlade = Time.time + 0.11f;
                bladeAngle += 11f;
                for (int i = 0; i < 5; i++)
                    Arrows.SpawnVisual(kingPos + Vector3.up * 1.6f, Quaternion.Euler(0f, bladeAngle + i * 72f, 0f) * Vector3.forward, ProjectileKind.Smite, 11f, 18f);
            }
            // The Ranger looses at the King's chest.
            if (Time.time >= nextArrow)
            {
                nextArrow = Time.time + 2.1f;
                ranger.Play(RigAction.Draw, 0.9f);
                var from = ranger.transform.position + Vector3.up * 1.5f + ranger.transform.forward * 0.5f;
                var hitAt = kingPos + Vector3.up * 2.4f;
                Arrows.SpawnVisual(from, hitAt - from, ProjectileKind.Arrow, 34f, Vector3.Distance(from, hitAt) - 0.3f);
                arrowHits.Add((hitAt, Time.time + Vector3.Distance(from, hitAt) / 34f));
            }
            // The Rogue cuts at his flank.
            if (Time.time >= nextStab)
            {
                nextStab = Time.time + 0.9f;
                rogue.Play(RigAction.Swing, 0.3f);
                Vfx.Embers(kingPos + Vector3.up * 1.5f + (rogue.transform.position - kingPos).normalized * 0.7f, 4, new Color(1f, 0.85f, 0.6f), 3f, 0.03f, 0.3f);
            }
            if (Time.time >= nextCast)
            {
                nextCast = Time.time + 3f;
                cleric.Play(RigAction.Cast, 1.2f);
                // The Aegis: a gold ring blooms round the Knight.
                Vfx.Ring(knight.transform.position + Vector3.up * 0.1f, 1.4f, new Color(1f, 0.85f, 0.45f), 0.5f);
            }
            // The beam from the Cleric's hand to the Knight's chest.
            var b0 = cleric.transform.position + Vector3.up * 1.3f + cleric.transform.forward * 0.4f;
            var b1 = knight.transform.position + Vector3.up * 1.2f;
            beam.position = (b0 + b1) * 0.5f;
            beam.rotation = Quaternion.FromToRotation(Vector3.up, b1 - b0);
            float thick = 0.035f + Mathf.Sin(Time.time * 9f) * 0.008f;
            beam.localScale = new Vector3(thick, Vector3.Distance(b0, b1), thick);
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), new Color(1f, 0.85f, 0.45f) * (1.6f + Mathf.Sin(Time.time * 6f) * 0.3f));
            if (Random.value < Time.deltaTime * 6f) Vfx.Mote(knight.transform.position, new Color(1f, 0.9f, 0.55f));
        }

        private void OnGUI()
        {
            if (set == null || !set.activeSelf) return;
            GUI.depth = 5; // behind the title and menu
            var cam = GetComponent<Camera>();
            // z's drifting up from the sleeping cat.
            if (scene == 0 && cat != null && cam != null)
            {
                var head = (catHead != null ? catHead.position : cat.position) + Vector3.up * 0.12f;
                for (int i = 0; i < 3; i++)
                {
                    float k = Mathf.Repeat(Time.time * 0.35f + i / 3f, 1f);
                    var p = cam.WorldToScreenPoint(head + new Vector3(k * 0.25f, k * 0.45f, 0f));
                    if (p.z <= 0f) continue;
                    float size = 14f + k * 18f;
                    var style = new GUIStyle(UiSkin.Heading) { fontSize = Mathf.RoundToInt(size), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow, normal = { textColor = new Color(0.95f, 0.92f, 1f, Mathf.Sin(k * Mathf.PI) * 0.9f) } };
                    var zr = new Rect(p.x - 60f, Screen.height - p.y - 50f, 120f, 100f);
                    UiSkin.Shadowed(zr, i == 2 ? "Z" : "z", style, Mathf.Sin(k * Mathf.PI));
                }
            }
            // The cut between scenes: a fade through black.
            float fade = 0f;
            float since = Time.time - sceneStart;
            if (cutAt > 0f) fade = Mathf.Clamp01((Time.time - cutAt) / FadeSeconds * 2f);
            else if (since < FadeSeconds * 0.6f) fade = 1f - since / (FadeSeconds * 0.6f);
            if (fade > 0f) UiSkin.Rect(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade)));
        }
    }
}
