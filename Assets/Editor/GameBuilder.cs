using System.IO;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Networking;
using Deepbound.Player;
using Deepbound.Progression;
using Deepbound.UI;
using Deepbound.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// Generates the game's prefabs, its one scene and the build settings — then,
    /// optionally, a Windows player. Menu: <c>Deepbound/Build Game</c>; command line:
    /// <c>-executeMethod Deepbound.EditorTools.GameBuilder.BuildWithPlayer</c>.
    ///
    /// <para>
    /// Everything is generated rather than hand-authored so it can be reviewed as code and
    /// rebuilt identically anywhere. Re-running overwrites the outputs: change this file,
    /// not the prefabs. Runs <see cref="ArtBaker"/> first, since prefabs and the scene
    /// reference its materials and post-processing profile.
    /// </para>
    /// </summary>
    public static class GameBuilder
    {
        private const string ScenePath = "Assets/Scenes/Deepbound.unity";
        private const string PrefabDir = "Assets/Prefabs";
        private const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string StandalonePath = "Builds/Deepbound/Deepbound.exe";

        [MenuItem("Deepbound/Build Game")]
        public static void Build()
        {
            ArtBaker.BakeAll();
            EnsureFolder("Assets/Scenes");
            EnsureFolder(PrefabDir);

            var loot = BuildLootPrefab();
            var bearTrap = BuildObjectPrefab<BearTrap>("BearTrap", Vector3.zero);
            var player = BuildPlayerPrefab(bearTrap);
            var skeleton = BuildEnemyPrefab("Skeleton", EnemyType.Skeleton, loot);
            var archer = BuildEnemyPrefab("SkeletonArcher", EnemyType.Archer, loot);
            var warden = BuildEnemyPrefab("Gravewarden", EnemyType.Gravewarden, loot);
            var lich = BuildEnemyPrefab("TideLich", EnemyType.TideLich, loot);
            var king = BuildEnemyPrefab("SkeletonKing", EnemyType.HollowKing, loot);
            var cardinal = BuildEnemyPrefab("PaleCardinal", EnemyType.Cardinal, loot);
            var unsung = BuildEnemyPrefab("TheUnsung", EnemyType.Unsung, loot);
            var ghoul = BuildEnemyPrefab("Ghoul", EnemyType.Ghoul, loot);
            var brute = BuildEnemyPrefab("BoneBrute", EnemyType.Brute, loot);
            var necro = BuildEnemyPrefab("Necromancer", EnemyType.Necromancer, loot);
            var wraith = BuildEnemyPrefab("Wraith", EnemyType.Wraith, loot);
            var shieldbearer = BuildEnemyPrefab("Shieldbearer", EnemyType.Shieldbearer, loot);
            var mimic = BuildEnemyPrefab("Mimic", EnemyType.Mimic, loot);
            var hoarder = BuildEnemyPrefab("BoneHoarder", EnemyType.Hoarder, loot);
            var gargoyle = BuildEnemyPrefab("Gargoyle", EnemyType.Gargoyle, loot);
            var plaguebearer = BuildEnemyPrefab("Plaguebearer", EnemyType.Plaguebearer, loot);
            var frostWight = BuildEnemyPrefab("FrostWight", EnemyType.FrostWight, loot);
            var hexer = BuildEnemyPrefab("Hexer", EnemyType.Hexer, loot);
            var mourner = BuildEnemyPrefab("Mourner", EnemyType.Mourner, loot);
            var dummy = BuildDummyPrefab();
            var chest = BuildObjectPrefab<Chest>("Chest", new Vector3(1.1f, 0.8f, 0.7f));
            var altar = BuildObjectPrefab<BloodAltar>("BloodAltar", new Vector3(1.4f, 0.9f, 1.0f));
            var trap = BuildObjectPrefab<SpikeTrap>("SpikeTrap", Vector3.zero);
            var breakable = BuildBreakablePrefab();
            var director = BuildDirectorPrefab(skeleton, archer, dummy, new[] { warden, lich, king, cardinal, unsung }, ghoul, brute, necro);
            WireDirectorObjects(director, chest, altar, breakable, trap, loot);
            var gate = BuildPortcullisPrefab();
            var lever = BuildObjectPrefab<Lever>("Lever", new Vector3(0.5f, 1.8f, 0.3f));
            var fountain = BuildObjectPrefab<Fountain>("Fountain", new Vector3(2.6f, 0.7f, 2.6f));
            var soulSkull = BuildObjectPrefab<SoulSkull>("SoulSkull", Vector3.zero);
            var reliquary = BuildObjectPrefab<Reliquary>("Reliquary", Vector3.zero);
            var lorePage = BuildObjectPrefab<LorePage>("LorePage", new Vector3(0.6f, 1.1f, 0.5f));
            var sarcophagus = BuildObjectPrefab<Sarcophagus>("Sarcophagus", new Vector3(2.2f, 1.0f, 0.95f));
            var anvil = BuildObjectPrefab<Anvil>("Anvil", new Vector3(0.8f, 0.9f, 0.6f));
            var gearDrop = BuildObjectPrefab<GearDrop>("GearDrop", Vector3.zero);
            var crackedWall = BuildCrackedWallPrefab();
            var diceTable = BuildObjectPrefab<DiceTable>("DiceTable", new Vector3(1.5f, 0.95f, 1.5f));
            var coffer = BuildObjectPrefab<MysteryCoffer>("MysteryCoffer", new Vector3(1.2f, 1f, 0.8f));
            var cat = BuildCatPrefab();
            var slots = BuildObjectPrefab<SlotMachine>("SlotMachine", new Vector3(1.1f, 1.9f, 0.8f));
            var blackjack = BuildObjectPrefab<BlackjackTable>("BlackjackTable", new Vector3(1.8f, 0.95f, 1.1f));
            var gong = BuildGongPrefab();
            var shrine = BuildObjectPrefab<Shrine>("Shrine", new Vector3(1.75f, 1f, 0.9f));
            var fateWheel = BuildObjectPrefab<FateWheel>("FateWheel", new Vector3(3f, 4f, 1f));
            var bountyNotice = BuildObjectPrefab<BountyNotice>("BountyNotice", Vector3.zero);
            var omenStone = BuildObjectPrefab<OmenStone>("OmenStone", new Vector3(1f, 2.6f, 0.7f));
            var rangeTarget = BuildRangeTargetPrefab();
            var dso = new SerializedObject(director.GetComponent<DungeonDirector>());
            dso.FindProperty("portcullisPrefab").objectReferenceValue = gate.GetComponent<NetworkObject>();
            dso.FindProperty("leverPrefab").objectReferenceValue = lever.GetComponent<NetworkObject>();
            dso.FindProperty("fountainPrefab").objectReferenceValue = fountain.GetComponent<NetworkObject>();
            dso.FindProperty("soulSkullPrefab").objectReferenceValue = soulSkull.GetComponent<NetworkObject>();
            dso.FindProperty("sarcophagusPrefab").objectReferenceValue = sarcophagus.GetComponent<NetworkObject>();
            dso.FindProperty("anvilPrefab").objectReferenceValue = anvil.GetComponent<NetworkObject>();
            dso.FindProperty("gearDropPrefab").objectReferenceValue = gearDrop.GetComponent<NetworkObject>();
            dso.FindProperty("wraithPrefab").objectReferenceValue = wraith.GetComponent<NetworkObject>();
            dso.FindProperty("shieldbearerPrefab").objectReferenceValue = shieldbearer.GetComponent<NetworkObject>();
            dso.FindProperty("mimicPrefab").objectReferenceValue = mimic.GetComponent<NetworkObject>();
            dso.FindProperty("hoarderPrefab").objectReferenceValue = hoarder.GetComponent<NetworkObject>();
            dso.FindProperty("gargoylePrefab").objectReferenceValue = gargoyle.GetComponent<NetworkObject>();
            dso.FindProperty("plaguebearerPrefab").objectReferenceValue = plaguebearer.GetComponent<NetworkObject>();
            dso.FindProperty("frostWightPrefab").objectReferenceValue = frostWight.GetComponent<NetworkObject>();
            dso.FindProperty("hexerPrefab").objectReferenceValue = hexer.GetComponent<NetworkObject>();
            dso.FindProperty("mournerPrefab").objectReferenceValue = mourner.GetComponent<NetworkObject>();
            dso.FindProperty("reliquaryPrefab").objectReferenceValue = reliquary.GetComponent<NetworkObject>();
            dso.FindProperty("lorePagePrefab").objectReferenceValue = lorePage.GetComponent<NetworkObject>();
            dso.FindProperty("crackedWallPrefab").objectReferenceValue = crackedWall.GetComponent<NetworkObject>();
            dso.FindProperty("diceTablePrefab").objectReferenceValue = diceTable.GetComponent<NetworkObject>();
            dso.FindProperty("cofferPrefab").objectReferenceValue = coffer.GetComponent<NetworkObject>();
            dso.FindProperty("catPrefab").objectReferenceValue = cat.GetComponent<NetworkObject>();
            dso.FindProperty("slotMachinePrefab").objectReferenceValue = slots.GetComponent<NetworkObject>();
            dso.FindProperty("blackjackPrefab").objectReferenceValue = blackjack.GetComponent<NetworkObject>();
            dso.FindProperty("gongPrefab").objectReferenceValue = gong.GetComponent<NetworkObject>();
            dso.FindProperty("shrinePrefab").objectReferenceValue = shrine.GetComponent<NetworkObject>();
            var folkBody = BuildFolkPrefab();
            dso.FindProperty("folkPrefab").objectReferenceValue = folkBody.GetComponent<NetworkObject>();
            dso.FindProperty("fateWheelPrefab").objectReferenceValue = fateWheel.GetComponent<NetworkObject>();
            dso.FindProperty("bountyNoticePrefab").objectReferenceValue = bountyNotice.GetComponent<NetworkObject>();
            dso.FindProperty("omenStonePrefab").objectReferenceValue = omenStone.GetComponent<NetworkObject>();
            dso.FindProperty("rangeTargetPrefab").objectReferenceValue = rangeTarget.GetComponent<NetworkObject>();
            dso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();

            RegisterNetworkPrefabs(bearTrap, player, skeleton, archer, warden, lich, king, director, loot, dummy, ghoul, brute, necro, chest, altar, trap, breakable, gate, lever, fountain, sarcophagus, anvil, gearDrop, crackedWall, diceTable, coffer, gong, shrine, rangeTarget, wraith, cardinal, unsung, shieldbearer, mimic, hoarder, fateWheel, bountyNotice, gargoyle, omenStone, plaguebearer, frostWight, hexer, soulSkull, mourner, reliquary, cat, slots, blackjack, lorePage, folkBody);
            BuildScene(player, director);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] Game built: " + ScenePath);
        }

        /// <summary>Batch check that the soundtrack synthesises: length, and that it is not silent.</summary>
        public static void TestMusic()
        {
            foreach (var clip in new[] { Art.MusicSynth.Explore(), Art.MusicSynth.Combat(), Art.MusicSynth.Boss() })
            {
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                float peak = 0f;
                foreach (var v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
                Debug.Log("[Music] " + clip.name + ": " + clip.length.ToString("F1") + "s, peak " + peak.ToString("F2"));
            }
        }

        [MenuItem("Deepbound/Build Game + Windows Player")]
        public static void BuildWithPlayer()
        {
            // Spread garbage collection over frames instead of stopping the world for it.
            PlayerSettings.gcIncremental = true;
            Build();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                // DEEPBOUND_BUILD_PATH lets a build go elsewhere while the usual copy is
                // running (Windows locks a running exe).
                locationPathName = System.Environment.GetEnvironmentVariable("DEEPBOUND_BUILD_PATH") ?? StandalonePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });

            Debug.Log("[Build] Player build " + report.summary.result + " -> " + StandalonePath);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------
        // Prefabs
        // ------------------------------------------------------------------

        private static GameObject BuildPlayerPrefab(GameObject bearTrap)
        {
            // Origin at the feet: spawn points and teleports mean "stand here".
            var root = new GameObject("Player");

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            YawOnly(netTransform);

            SetTeam(root.AddComponent<Health>(), Team.Players, 1f);
            root.AddComponent<PlayerLife>();
            root.AddComponent<PlayerProgress>();
            root.AddComponent<KnightKit>();
            root.AddComponent<MageKit>();
            root.AddComponent<ClericKit>();
            root.AddComponent<RogueKit>();
            root.AddComponent<Ultimates>();
            root.AddComponent<Kick>();
            root.AddComponent<Flasks>();
            root.AddComponent<Emotes>();
            root.AddComponent<PlayerDebuffs>();
            root.AddComponent<RangerKit>().SetTrapPrefab(bearTrap.GetComponent<NetworkObject>());
            root.AddComponent<KnockbackReceiver>();
            root.AddComponent<PlayerPings>();
            root.AddComponent<PlayerVoice>();

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.62f, 0f);

            // The camera is a child of the pivot, not the pivot itself: the motor moves the
            // pivot (look, kneeling height) and damage shake moves the camera, and each
            // needs its own transform so neither overwrites the other.
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(pivot, false);
            var camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            camera.fieldOfView = 78f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.018f, 0.02f, 0.028f);
            camera.enabled = false;
            camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            var listener = camGo.AddComponent<AudioListener>();
            listener.enabled = false;

            var motor = root.AddComponent<FirstPersonMotor>();
            motor.CameraPivot = pivot;
            motor.enabled = false;

            var player = root.AddComponent<NetworkPlayer>();
            var so = new SerializedObject(player);
            so.FindProperty("playerCamera").objectReferenceValue = camera;
            so.FindProperty("audioListener").objectReferenceValue = listener;
            so.FindProperty("motor").objectReferenceValue = motor;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, "Player");
        }

        private enum EnemyType { Skeleton, Archer, Gravewarden, TideLich, HollowKing, Ghoul, Brute, Necromancer, Wraith, Cardinal, Shieldbearer, Mimic, Hoarder, Gargoyle, Plaguebearer, FrostWight, Hexer, Mourner, Unsung }

        private static GameObject BuildEnemyPrefab(string name, EnemyType type, GameObject loot)
        {
            float scale = type == EnemyType.HollowKing ? 1.75f : type == EnemyType.Gravewarden ? 2.0f : type == EnemyType.TideLich ? 1.45f : type == EnemyType.Cardinal ? 1.7f
                : type == EnemyType.Brute ? 1.45f : type == EnemyType.Plaguebearer ? 1.15f : type == EnemyType.Ghoul ? 0.92f : 1f;
            var root = new GameObject(name);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            YawOnly(netTransform);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f * scale;
            capsule.radius = 0.35f * scale;
            capsule.center = new Vector3(0f, 0.9f * scale, 0f);
            // Shapes that are not a standing skeleton get a body to match.
            if (type == EnemyType.Mimic) { capsule.height = 1.1f; capsule.radius = 0.55f; capsule.center = new Vector3(0f, 0.55f, 0f); }
            if (type == EnemyType.Mourner) { capsule.height = 2.2f; capsule.radius = 0.38f; capsule.center = new Vector3(0f, 1.1f, 0f); }
            if (type == EnemyType.Gargoyle) { capsule.height = 2.7f; capsule.radius = 0.45f; capsule.center = new Vector3(0f, 1.35f, 0f); }
            if (type == EnemyType.Wraith) { capsule.height = 2.0f; capsule.center = new Vector3(0f, 1.1f, 0f); }
            if (type == EnemyType.Brute) capsule.radius = 0.45f * scale;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f * scale;
            agent.height = 1.8f * scale;
            agent.enabled = false;

            SetTeam(root.AddComponent<Health>(), Team.Enemies, 0.95f * scale);

            EnemyBrain brain;
            switch (type)
            {
                case EnemyType.Archer: brain = root.AddComponent<ArcherBrain>(); break;
                case EnemyType.Gravewarden: brain = root.AddComponent<GravewardenBrain>(); break;
                case EnemyType.TideLich: brain = root.AddComponent<TideLichBrain>(); break;
                case EnemyType.HollowKing: brain = root.AddComponent<HollowKingBrain>(); break;
                case EnemyType.Ghoul: brain = root.AddComponent<GhoulBrain>(); break;
                case EnemyType.Brute: brain = root.AddComponent<BruteBrain>(); break;
                case EnemyType.Necromancer: brain = root.AddComponent<NecromancerBrain>(); break;
                case EnemyType.Wraith: brain = root.AddComponent<WraithBrain>(); break;
                case EnemyType.Cardinal: brain = root.AddComponent<CardinalBrain>(); break;
                case EnemyType.Unsung: brain = root.AddComponent<UnsungBrain>(); break;
                case EnemyType.Shieldbearer: brain = root.AddComponent<ShieldbearerBrain>(); break;
                case EnemyType.Mimic: brain = root.AddComponent<MimicBrain>(); break;
                case EnemyType.Hoarder: brain = root.AddComponent<HoarderBrain>(); break;
                case EnemyType.Gargoyle: brain = root.AddComponent<GargoyleBrain>(); break;
                case EnemyType.Plaguebearer: brain = root.AddComponent<PlaguebearerBrain>(); break;
                case EnemyType.FrostWight: brain = root.AddComponent<FrostWightBrain>(); break;
                case EnemyType.Hexer: brain = root.AddComponent<HexerBrain>(); break;
                case EnemyType.Mourner: brain = root.AddComponent<MournerBrain>(); break;
                default: brain = root.AddComponent<EnemyBrain>(); break;
            }

            var so = new SerializedObject(brain);
            so.FindProperty("lootPrefab").objectReferenceValue = loot.GetComponent<NetworkObject>();
            void Set(string field, float value) => so.FindProperty(field).floatValue = value;
            void Kind(Art.CharacterArt.SkeletonKind k) => so.FindProperty("visualKind").intValue = (int)k;

            switch (type)
            {
                case EnemyType.Archer:
                    // Fragile and slow to fire, but it hurts from range: kill it first.
                    Kind(Art.CharacterArt.SkeletonKind.Archer);
                    Set("maxHealth", 60f); Set("damage", 14f); Set("attackRange", 15f); Set("windup", 1.0f);
                    Set("recovery", 1.5f); Set("aggroRange", 17f); Set("moveSpeed", 3.2f);
                    break;
                case EnemyType.Ghoul:
                    // Fast and fragile, in pairs. Dies to anything; dangerous in numbers.
                    Kind(Art.CharacterArt.SkeletonKind.Ghoul);
                    Set("maxHealth", 55f); Set("damage", 11f); Set("defense", 0f); Set("moveSpeed", 5.4f);
                    Set("attackRange", 1.8f); Set("windup", 0.35f); Set("recovery", 0.6f); Set("aggroRange", 15f);
                    break;
                case EnemyType.Plaguebearer:
                    Kind(Art.CharacterArt.SkeletonKind.Plaguebearer);
                    Set("maxHealth", 120f); Set("damage", 9f); Set("defense", 1f); Set("moveSpeed", 2.6f);
                    Set("attackRange", 11f); Set("windup", 0.9f); Set("recovery", 1.8f); Set("aggroRange", 16f);
                    break;
                case EnemyType.FrostWight:
                    Kind(Art.CharacterArt.SkeletonKind.FrostWight);
                    Set("maxHealth", 130f); Set("damage", 15f); Set("defense", 3f); Set("moveSpeed", 3.2f);
                    Set("attackRange", 2.2f); Set("windup", 0.6f); Set("recovery", 1.1f); Set("aggroRange", 15f);
                    break;
                case EnemyType.Hexer:
                    Kind(Art.CharacterArt.SkeletonKind.Hexer);
                    Set("maxHealth", 95f); Set("damage", 10f); Set("defense", 0f); Set("moveSpeed", 3.3f);
                    Set("attackRange", 13f); Set("windup", 0.8f); Set("recovery", 1.9f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Mourner:
                    // Moves only unseen, and then fast. Sturdy stone; hits hard.
                    Kind(Art.CharacterArt.SkeletonKind.Mourner);
                    Set("maxHealth", 170f); Set("damage", 24f); Set("defense", 3f); Set("moveSpeed", 7.5f);
                    Set("attackRange", 1.9f); Set("windup", 0.3f); Set("recovery", 1.2f); Set("aggroRange", 30f); Set("leashRange", 80f);
                    break;
                case EnemyType.Gargoyle:
                    Kind(Art.CharacterArt.SkeletonKind.Gargoyle);
                    Set("maxHealth", 210f); Set("damage", 20f); Set("defense", 4f); Set("moveSpeed", 4.2f);
                    Set("attackRange", 2.2f); Set("windup", 0.55f); Set("recovery", 1f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Hoarder:
                    // Quick, fragile-ish, never fights: a chase.
                    Kind(Art.CharacterArt.SkeletonKind.Hoarder);
                    Set("maxHealth", 170f); Set("damage", 0f); Set("defense", 2f); Set("moveSpeed", 4.6f);
                    Set("attackRange", 0.1f); Set("aggroRange", 15f); Set("leashRange", 80f);
                    break;
                case EnemyType.Mimic:
                    // Fast, bitey, tough for its size: a mini-fight that pays out.
                    Kind(Art.CharacterArt.SkeletonKind.Mimic);
                    Set("maxHealth", 240f); Set("damage", 24f); Set("defense", 4f); Set("moveSpeed", 4.4f);
                    Set("attackRange", 1.9f); Set("windup", 0.4f); Set("recovery", 0.8f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Shieldbearer:
                    // Slow, turns slowly, and very hard to hurt from the front.
                    Kind(Art.CharacterArt.SkeletonKind.Shieldbearer);
                    Set("maxHealth", 150f); Set("damage", 18f); Set("defense", 3f); Set("moveSpeed", 2.6f);
                    Set("attackRange", 2.2f); Set("windup", 0.75f); Set("recovery", 1.2f); Set("aggroRange", 14f);
                    break;
                case EnemyType.Brute:
                    Kind(Art.CharacterArt.SkeletonKind.Brute);
                    Set("maxHealth", 280f); Set("damage", 30f); Set("defense", 5f); Set("moveSpeed", 2.4f);
                    Set("attackRange", 2.6f); Set("windup", 1.1f); Set("recovery", 1.4f); Set("aggroRange", 14f);
                    break;
                case EnemyType.Necromancer:
                    Kind(Art.CharacterArt.SkeletonKind.Necromancer);
                    Set("maxHealth", 75f); Set("damage", 12f); Set("defense", 0f); Set("moveSpeed", 3.0f);
                    Set("attackRange", 14f); Set("windup", 0.7f); Set("recovery", 1.6f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Wraith:
                    // Fragile on paper, but Ethereal: needs pinning down to kill quickly.
                    Kind(Art.CharacterArt.SkeletonKind.Wraith);
                    Set("maxHealth", 70f); Set("damage", 12f); Set("defense", 0f); Set("moveSpeed", 3.4f);
                    Set("attackRange", 12f); Set("windup", 0.8f); Set("recovery", 1.7f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Gravewarden:
                    Kind(Art.CharacterArt.SkeletonKind.Gravewarden);
                    Set("maxHealth", 1450f); Set("damage", 26f); Set("defense", 4f); Set("moveSpeed", 2.9f);
                    Set("attackRange", 3.6f); Set("recovery", 1.2f); Set("aggroRange", 20f); Set("leashRange", 60f);
                    break;
                case EnemyType.TideLich:
                    // Squishier than the others; the difficulty is catching it.
                    Kind(Art.CharacterArt.SkeletonKind.Lich);
                    Set("maxHealth", 2600f); Set("damage", 18f); Set("defense", 2f); Set("moveSpeed", 3.4f);
                    Set("attackRange", 2.8f); Set("recovery", 1.0f); Set("aggroRange", 22f); Set("leashRange", 60f);
                    break;
                case EnemyType.Unsung:
                    // The secret final boss: patterns more than reach; the toughest fight in the game.
                    Kind(Art.CharacterArt.SkeletonKind.Unsung);
                    Set("maxHealth", 4150f); Set("damage", 34f); Set("defense", 7f); Set("moveSpeed", 1.7f);
                    Set("attackRange", 3.6f); Set("windup", 1.0f); Set("recovery", 1.2f); Set("aggroRange", 26f); Set("leashRange", 70f);
                    break;
                case EnemyType.Cardinal:
                    Kind(Art.CharacterArt.SkeletonKind.Cardinal);
                    Set("maxHealth", 3700f); Set("damage", 32f); Set("defense", 6f); Set("moveSpeed", 2.9f);
                    Set("attackRange", 3.4f); Set("recovery", 1.1f); Set("aggroRange", 22f); Set("leashRange", 60f);
                    break;
                case EnemyType.HollowKing:
                    Kind(Art.CharacterArt.SkeletonKind.King);
                    Set("maxHealth", 3080f); Set("damage", 30f); Set("defense", 6f); Set("moveSpeed", 3.1f);
                    Set("attackRange", 3.5f); Set("recovery", 1.1f); Set("aggroRange", 20f); Set("leashRange", 60f);
                    break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, name);
        }

        /// <summary>Loot never moves once dropped, so it needs no NetworkTransform — just an identity.</summary>
        private static GameObject BuildLootPrefab()
        {
            var root = new GameObject("LootDrop");
            root.AddComponent<NetworkObject>();
            root.AddComponent<LootDrop>();
            return SavePrefab(root, "LootDrop");
        }

        /// <summary>A static networked object; a box collider when it should block movement.</summary>
        private static GameObject BuildObjectPrefab<T>(string name, Vector3 collider) where T : Component
        {
            var root = new GameObject(name);
            root.AddComponent<NetworkObject>();
            if (collider != Vector3.zero)
            {
                var box = root.AddComponent<BoxCollider>();
                box.size = collider;
                box.center = new Vector3(0f, collider.y / 2f, 0f);
            }
            root.AddComponent<T>();
            return SavePrefab(root, name);
        }

        /// <summary>A doorway gate: blocks players with a box, enemies with a carving obstacle.</summary>
        private static GameObject BuildPortcullisPrefab()
        {
            var root = new GameObject("Portcullis");
            root.AddComponent<NetworkObject>();
            var size = new Vector3(DungeonLayout.CorridorWidth, DungeonLayout.CorridorHeight, 0.5f);
            var box = root.AddComponent<BoxCollider>();
            box.size = size;
            box.center = new Vector3(0f, size.y / 2f, 0f);
            box.enabled = false;
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = size;
            obstacle.center = box.center;
            obstacle.carving = true;
            obstacle.enabled = false;
            root.AddComponent<Portcullis>();
            return SavePrefab(root, "Portcullis");
        }

        /// <summary>The plug in a hollow wall: solid, carves the navmesh, and has health (Props team) to smash.</summary>
        private static GameObject BuildCrackedWallPrefab()
        {
            var root = new GameObject("CrackedWall");
            root.AddComponent<NetworkObject>();
            var size = new Vector3(WallHole.Width, WallHole.Height, 0.8f);
            var box = root.AddComponent<BoxCollider>();
            box.size = size;
            box.center = new Vector3(0f, size.y / 2f, 0.4f);
            var obstacle = root.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = size + new Vector3(0f, 0f, 0.4f);
            obstacle.center = box.center;
            obstacle.carving = true;
            SetTeam(root.AddComponent<Health>(), Team.Props, 1.4f);
            root.AddComponent<CrackedWall>();
            return SavePrefab(root, "CrackedWall");
        }

        /// <summary>The Test of Might: a hittable (Props team) gong that never breaks.</summary>
        private static GameObject BuildGongPrefab()
        {
            var root = new GameObject("Gong");
            root.AddComponent<NetworkObject>();
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(2.4f, 2.4f, 0.3f);
            box.center = new Vector3(0f, 1.9f, 0f);
            SetTeam(root.AddComponent<Health>(), Team.Props, 1.9f);
            root.AddComponent<Gong>();
            return SavePrefab(root, "Gong");
        }

        /// <summary>An archery target: a hittable (Props team) straw butt that never breaks.</summary>
        private static GameObject BuildRangeTargetPrefab()
        {
            var root = new GameObject("RangeTarget");
            root.AddComponent<NetworkObject>();
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(1.1f, 1.1f, 0.3f);
            box.center = new Vector3(0f, 1.45f, 0.1f);
            SetTeam(root.AddComponent<Health>(), Team.Props, 1.45f);
            root.AddComponent<RangeTarget>();
            return SavePrefab(root, "RangeTarget");
        }

        private static GameObject BuildBreakablePrefab()
        {
            var root = new GameObject("Breakable");
            root.AddComponent<NetworkObject>();
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.35f;
            capsule.height = 0.9f;
            capsule.center = new Vector3(0f, 0.45f, 0f);
            SetTeam(root.AddComponent<Health>(), Team.Props, 0.45f);
            root.AddComponent<Breakable>();
            return SavePrefab(root, "Breakable");
        }

        private static void WireDirectorObjects(GameObject director, GameObject chest, GameObject altar, GameObject breakable, GameObject trap, GameObject loot)
        {
            var so = new SerializedObject(director.GetComponent<DungeonDirector>());
            so.FindProperty("chestPrefab").objectReferenceValue = chest.GetComponent<NetworkObject>();
            so.FindProperty("altarPrefab").objectReferenceValue = altar.GetComponent<NetworkObject>();
            so.FindProperty("breakablePrefab").objectReferenceValue = breakable.GetComponent<NetworkObject>();
            so.FindProperty("trapPrefab").objectReferenceValue = trap.GetComponent<NetworkObject>();
            so.FindProperty("lootPrefab").objectReferenceValue = loot.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();
        }

        private static GameObject BuildCatPrefab()
        {
            var root = new GameObject("CampCat");
            root.AddComponent<NetworkObject>();
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.height = 0.55f;
            capsule.radius = 0.2f;
            capsule.center = new Vector3(0f, 0.27f, 0f);
            SetTeam(root.AddComponent<Health>(), Team.Props, 0.4f);
            root.AddComponent<CampCat>();
            return SavePrefab(root, "CampCat");
        }

        private static GameObject BuildFolkPrefab()
        {
            var root = new GameObject("CampFolk");
            root.AddComponent<NetworkObject>();
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = 0.32f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            SetTeam(root.AddComponent<Health>(), Team.Props, 1.2f);
            root.AddComponent<CampFolk>();
            return SavePrefab(root, "CampFolk");
        }

        private static GameObject BuildDummyPrefab()
        {
            var root = new GameObject("TrainingDummy");
            root.AddComponent<NetworkObject>();
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.9f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.95f, 0f);
            SetTeam(root.AddComponent<Health>(), Team.Enemies, 1.2f);
            root.AddComponent<TrainingDummy>();
            return SavePrefab(root, "TrainingDummy");
        }

        private static GameObject BuildDirectorPrefab(GameObject skeleton, GameObject archer, GameObject dummy, GameObject[] bossesByDepth,
            GameObject ghoul, GameObject brute, GameObject necro)
        {
            var root = new GameObject("DungeonDirector");
            root.AddComponent<NetworkObject>();
            var director = root.AddComponent<DungeonDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("skeletonPrefab").objectReferenceValue = skeleton.GetComponent<NetworkObject>();
            var bosses = so.FindProperty("bossPrefabs");
            bosses.arraySize = bossesByDepth.Length;
            for (int i = 0; i < bossesByDepth.Length; i++) bosses.GetArrayElementAtIndex(i).objectReferenceValue = bossesByDepth[i].GetComponent<NetworkObject>();
            so.FindProperty("dummyPrefab").objectReferenceValue = dummy.GetComponent<NetworkObject>();
            so.FindProperty("archerPrefab").objectReferenceValue = archer.GetComponent<NetworkObject>();
            so.FindProperty("ghoulPrefab").objectReferenceValue = ghoul.GetComponent<NetworkObject>();
            so.FindProperty("brutePrefab").objectReferenceValue = brute.GetComponent<NetworkObject>();
            so.FindProperty("necromancerPrefab").objectReferenceValue = necro.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(root, "DungeonDirector");
        }

        private static void YawOnly(NetworkTransform t)
        {
            // Bodies only ever turn about Y; pitch lives on the local camera.
            t.SyncRotAngleX = false;
            t.SyncRotAngleZ = false;
            t.SyncScaleX = false;
            t.SyncScaleY = false;
            t.SyncScaleZ = false;
        }

        private static void SetTeam(Health health, Team team, float centerHeight)
        {
            var so = new SerializedObject(health);
            so.FindProperty("team").intValue = (int)team;
            so.FindProperty("centerHeight").floatValue = centerHeight;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(root);
            AssignGlobalObjectIdHash(prefab.GetComponent<NetworkObject>());
            return prefab;
        }

        /// <summary>
        /// Gives a prefab's NetworkObject the id peers use to agree which prefab to spawn.
        ///
        /// NGO derives it from the asset GUID inside <c>NetworkObject.OnValidate</c>. When
        /// the component is added from script, OnValidate runs while the object is still an
        /// unsaved scene object with no GUID, and the hash is left at 0 — clients would then
        /// fail to spawn it. Calling it again once the asset exists fixes that. It is
        /// internal, hence the reflection; if a future NGO renames it, this throws here
        /// rather than failing quietly at runtime.
        /// </summary>
        private static void AssignGlobalObjectIdHash(NetworkObject networkObject)
        {
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            onValidate.Invoke(networkObject, null);

            if (networkObject.PrefabIdHash == 0)
            {
                throw new System.InvalidOperationException("[Build] " + networkObject.name + " still has GlobalObjectIdHash 0.");
            }

            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssets();
        }

        private static void RegisterNetworkPrefabs(params GameObject[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null) return;

            foreach (var prefab in prefabs)
            {
                bool present = false;
                foreach (var entry in list.PrefabList)
                {
                    if (entry.Prefab == prefab) { present = true; break; }
                }
                if (!present) list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        private static void BuildScene(GameObject playerPrefab, GameObject directorPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ArtBaker.VolumeProfilePath);

            var menuCamera = new GameObject("MenuCamera").AddComponent<Camera>();
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.backgroundColor = new Color(0.018f, 0.02f, 0.028f);
            menuCamera.fieldOfView = 55f;
            menuCamera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            menuCamera.gameObject.AddComponent<AudioListener>();
            menuCamera.gameObject.AddComponent<MenuBackdrop>();

            var netGo = new GameObject("NetworkManager");
            var transport = netGo.AddComponent<UnityTransport>();
            var manager = netGo.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                PlayerPrefab = playerPrefab,
            };
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list != null) manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);

            var launcher = netGo.AddComponent<NetworkLauncher>();
            var so = new SerializedObject(launcher);
            so.FindProperty("menuCamera").objectReferenceValue = menuCamera;
            so.FindProperty("directorPrefab").objectReferenceValue = directorPrefab.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }
}
