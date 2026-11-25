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
            var player = BuildPlayerPrefab();
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
            var entling = BuildEnemyPrefab("Entling", EnemyType.Entling, loot);
            var orbweaver = BuildEnemyPrefab("Orbweaver", EnemyType.Orbweaver, loot);
            var acolyte = BuildEnemyPrefab("ChoirAcolyte", EnemyType.ChoirAcolyte, loot);
            var abyssEye = BuildEnemyPrefab("AbyssEye", EnemyType.AbyssEye, loot);
            var bonePiper = BuildEnemyPrefab("BonePiper", EnemyType.BonePiper, loot);
            var cantor = BuildEnemyPrefab("Cantor", EnemyType.Cantor, loot);
            var magus = BuildEnemyPrefab("RimeMagus", EnemyType.RimeMagus, loot);
            var lamprey = BuildEnemyPrefab("InkLamprey", EnemyType.InkLamprey, loot);
            var hag = BuildEnemyPrefab("WitheringHag", EnemyType.WitheringHag, loot);
            var ringer = BuildEnemyPrefab("Bellringer", EnemyType.Bellringer, loot);

            var wardens = new GameObject[5];
            for (int a = 0; a < wardens.Length; a++) wardens[a] = BuildEnemyPrefab("Warden" + a, EnemyType.Warden, loot, a);

            var lairs = new[] { BuildEnemyPrefab("MotherMarrow", EnemyType.MotherMarrow, loot), BuildEnemyPrefab("LanternFisher", EnemyType.LanternFisher, loot),
                BuildEnemyPrefab("TwiceCrowned", EnemyType.TwiceCrowned, loot), BuildEnemyPrefab("MirrorNun", EnemyType.MirrorNun, loot) };

            var vharoth = BuildEnemyPrefab("Vharoth", EnemyType.Vharoth, loot);
            var rift = BuildObjectPrefab<EmberRift>("EmberRift", Vector3.zero);
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
            var sideShrine = BuildObjectPrefab<SideShrine>("SideShrine", new Vector3(0.9f, 1.1f, 0.9f));
            var arenaEngine = BuildObjectPrefab<ArenaEngine>("ArenaEngine", new Vector3(1.2f, 1.4f, 1.4f));
            var tallyman = BuildObjectPrefab<Tallyman>("Tallyman", Vector3.zero);
            var oathPlates = BuildObjectPrefab<OathPlates>("OathPlates", Vector3.zero);
            var oathStone = BuildObjectPrefab<OathStone>("OathStone", Vector3.zero);
            var handTorch = BuildObjectPrefab<HandTorch>("HandTorch", Vector3.zero);
            var questCargo = BuildObjectPrefab<QuestCargo>("QuestCargo", Vector3.zero);
            var questTrinket = BuildObjectPrefab<QuestTrinket>("QuestTrinket", Vector3.zero);
            var shaftCat = BuildObjectPrefab<ShaftCat>("ShaftCat", Vector3.zero);
            var remains = BuildObjectPrefab<ExpeditionRemains>("ExpeditionRemains", Vector3.zero);
            var soulSkull = BuildObjectPrefab<SoulSkull>("SoulSkull", Vector3.zero);
            var reliquary = BuildObjectPrefab<Reliquary>("Reliquary", Vector3.zero);
            var lorePage = BuildObjectPrefab<LorePage>("LorePage", new Vector3(0.6f, 1.1f, 0.5f));
            var sarcophagus = BuildObjectPrefab<Sarcophagus>("Sarcophagus", new Vector3(2.2f, 1.0f, 0.95f));
            var anvil = BuildObjectPrefab<Anvil>("Anvil", new Vector3(0.8f, 0.9f, 0.6f));
            var gearDrop = BuildObjectPrefab<GearDrop>("GearDrop", Vector3.zero);
            var crackedWall = BuildCrackedWallPrefab();
            var coffer = BuildObjectPrefab<MysteryCoffer>("MysteryCoffer", new Vector3(1.2f, 1f, 0.8f));
            var cat = BuildCatPrefab();
            var slots = BuildObjectPrefab<SlotMachine>("SlotMachine", new Vector3(1.1f, 1.9f, 0.8f));
            var blackjack = BuildObjectPrefab<BlackjackTable>("BlackjackTable", new Vector3(1.8f, 0.95f, 1.1f));
            var poker = BuildObjectPrefab<PokerDiceTable>("PokerDiceTable", new Vector3(1.8f, 0.95f, 1.8f));
            var oilSlick = BuildObjectPrefab<OilSlick>("OilSlick", Vector3.zero);
            var chandelier = BuildObjectPrefab<Chandelier>("Chandelier", Vector3.zero);
            var vigilCandle = BuildObjectPrefab<VigilCandle>("VigilCandle", new Vector3(0.35f, 1.4f, 0.35f));
            var hearth = BuildObjectPrefab<HearthBrazier>("HearthBrazier", new Vector3(1.6f, 1.4f, 1.6f));
            var prisoner = BuildPrisonerPrefab();
            var puzzleRoom = BuildObjectPrefab<PuzzleRoom>("PuzzleRoom", new Vector3(1.3f, 1.1f, 1.3f));
            var puzzleStatue = BuildObjectPrefab<PuzzleStatue>("PuzzleStatue", new Vector3(0.9f, 2.1f, 0.9f));
            var puzzleMirror = BuildObjectPrefab<PuzzleMirror>("PuzzleMirror", new Vector3(0.7f, 1.6f, 0.7f));
            var scaleWeight = BuildObjectPrefab<ScaleWeight>("ScaleWeight", Vector3.zero);
            var statDraught = BuildObjectPrefab<StatDraught>("StatDraught", new Vector3(0.4f, 0.6f, 0.4f));
            var bargainAltar = BuildObjectPrefab<BargainAltar>("BargainAltar", new Vector3(1.2f, 1.1f, 0.7f));
            var soulRift = BuildSoulRiftPrefab();
            var cellarHatch = BuildCellarHatchPrefab();
            var wellFishing = BuildObjectPrefab<WellFishing>("WellFishing", Vector3.zero);
            var knifeBoard = BuildObjectPrefab<KnifeBoard>("KnifeBoard", Vector3.zero);
            var bellFrame = BuildObjectPrefab<BellFrame>("BellFrame", Vector3.zero);
            var armWrestle = BuildObjectPrefab<ArmWrestle>("ArmWrestle", new Vector3(0.72f, 0.95f, 0.72f));
            var tavernKeep = BuildObjectPrefab<TavernKeep>("TavernKeep", Vector3.zero);
            var tavernBand = BuildObjectPrefab<TavernBand>("TavernBand", Vector3.zero);
            var gong = BuildGongPrefab();
            var shrine = BuildObjectPrefab<Shrine>("Shrine", new Vector3(1.75f, 1f, 0.9f));
            var fateWheel = BuildObjectPrefab<FateWheel>("FateWheel", new Vector3(3f, 4f, 1f));
            var bountyNotice = BuildObjectPrefab<BountyNotice>("BountyNotice", Vector3.zero);
            var omenStone = BuildObjectPrefab<OmenStone>("OmenStone", new Vector3(1f, 2.6f, 0.7f));
            var rangeTarget = BuildRangeTargetPrefab();
            var phoenix = BuildPhoenixPrefab();
            var dso = new SerializedObject(director.GetComponent<DungeonDirector>());
            dso.FindProperty("portcullisPrefab").objectReferenceValue = gate.GetComponent<NetworkObject>();
            dso.FindProperty("leverPrefab").objectReferenceValue = lever.GetComponent<NetworkObject>();
            dso.FindProperty("fountainPrefab").objectReferenceValue = fountain.GetComponent<NetworkObject>();
            dso.FindProperty("sideShrinePrefab").objectReferenceValue = sideShrine.GetComponent<NetworkObject>();
            dso.FindProperty("arenaEnginePrefab").objectReferenceValue = arenaEngine.GetComponent<NetworkObject>();
            dso.FindProperty("tallymanPrefab").objectReferenceValue = tallyman.GetComponent<NetworkObject>();
            dso.FindProperty("oathPlatesPrefab").objectReferenceValue = oathPlates.GetComponent<NetworkObject>();
            dso.FindProperty("oathStonePrefab").objectReferenceValue = oathStone.GetComponent<NetworkObject>();
            dso.FindProperty("handTorchPrefab").objectReferenceValue = handTorch.GetComponent<NetworkObject>();
            dso.FindProperty("questCargoPrefab").objectReferenceValue = questCargo.GetComponent<NetworkObject>();
            dso.FindProperty("questTrinketPrefab").objectReferenceValue = questTrinket.GetComponent<NetworkObject>();
            dso.FindProperty("shaftCatPrefab").objectReferenceValue = shaftCat.GetComponent<NetworkObject>();
            dso.FindProperty("remainsPrefab").objectReferenceValue = remains.GetComponent<NetworkObject>();
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
            dso.FindProperty("entlingPrefab").objectReferenceValue = entling.GetComponent<NetworkObject>();
            dso.FindProperty("orbweaverPrefab").objectReferenceValue = orbweaver.GetComponent<NetworkObject>();
            dso.FindProperty("acolytePrefab").objectReferenceValue = acolyte.GetComponent<NetworkObject>();
            dso.FindProperty("abyssEyePrefab").objectReferenceValue = abyssEye.GetComponent<NetworkObject>();
            dso.FindProperty("bonePiperPrefab").objectReferenceValue = bonePiper.GetComponent<NetworkObject>();
            dso.FindProperty("cantorPrefab").objectReferenceValue = cantor.GetComponent<NetworkObject>();
            dso.FindProperty("magusPrefab").objectReferenceValue = magus.GetComponent<NetworkObject>();
            dso.FindProperty("lampreyPrefab").objectReferenceValue = lamprey.GetComponent<NetworkObject>();
            dso.FindProperty("hagPrefab").objectReferenceValue = hag.GetComponent<NetworkObject>();
            dso.FindProperty("ringerPrefab").objectReferenceValue = ringer.GetComponent<NetworkObject>();
            var wardenList = dso.FindProperty("wardenPrefabs");
            wardenList.arraySize = wardens.Length;
            for (int a = 0; a < wardens.Length; a++) wardenList.GetArrayElementAtIndex(a).objectReferenceValue = wardens[a].GetComponent<NetworkObject>();
            dso.FindProperty("pitBossPrefab").objectReferenceValue = vharoth.GetComponent<NetworkObject>();
            dso.FindProperty("riftPrefab").objectReferenceValue = rift.GetComponent<NetworkObject>();
            var lairList = dso.FindProperty("lairPrefabs");
            lairList.arraySize = lairs.Length;
            for (int a = 0; a < lairs.Length; a++) lairList.GetArrayElementAtIndex(a).objectReferenceValue = lairs[a].GetComponent<NetworkObject>();
            dso.FindProperty("reliquaryPrefab").objectReferenceValue = reliquary.GetComponent<NetworkObject>();
            dso.FindProperty("lorePagePrefab").objectReferenceValue = lorePage.GetComponent<NetworkObject>();
            dso.FindProperty("crackedWallPrefab").objectReferenceValue = crackedWall.GetComponent<NetworkObject>();
            dso.FindProperty("cofferPrefab").objectReferenceValue = coffer.GetComponent<NetworkObject>();
            dso.FindProperty("catPrefab").objectReferenceValue = cat.GetComponent<NetworkObject>();
            dso.FindProperty("slotMachinePrefab").objectReferenceValue = slots.GetComponent<NetworkObject>();
            dso.FindProperty("blackjackPrefab").objectReferenceValue = blackjack.GetComponent<NetworkObject>();
            dso.FindProperty("pokerPrefab").objectReferenceValue = poker.GetComponent<NetworkObject>();
            dso.FindProperty("oilSlickPrefab").objectReferenceValue = oilSlick.GetComponent<NetworkObject>();
            dso.FindProperty("chandelierPrefab").objectReferenceValue = chandelier.GetComponent<NetworkObject>();
            dso.FindProperty("vigilCandlePrefab").objectReferenceValue = vigilCandle.GetComponent<NetworkObject>();
            dso.FindProperty("hearthPrefab").objectReferenceValue = hearth.GetComponent<NetworkObject>();
            dso.FindProperty("prisonerPrefab").objectReferenceValue = prisoner.GetComponent<NetworkObject>();
            dso.FindProperty("puzzleRoomPrefab").objectReferenceValue = puzzleRoom.GetComponent<NetworkObject>();
            dso.FindProperty("puzzleStatuePrefab").objectReferenceValue = puzzleStatue.GetComponent<NetworkObject>();
            dso.FindProperty("puzzleMirrorPrefab").objectReferenceValue = puzzleMirror.GetComponent<NetworkObject>();
            dso.FindProperty("scaleWeightPrefab").objectReferenceValue = scaleWeight.GetComponent<NetworkObject>();
            dso.FindProperty("statDraughtPrefab").objectReferenceValue = statDraught.GetComponent<NetworkObject>();
            dso.FindProperty("bargainAltarPrefab").objectReferenceValue = bargainAltar.GetComponent<NetworkObject>();
            dso.FindProperty("soulRiftPrefab").objectReferenceValue = soulRift.GetComponent<NetworkObject>();
            dso.FindProperty("cellarHatchPrefab").objectReferenceValue = cellarHatch.GetComponent<NetworkObject>();
            dso.FindProperty("wellFishingPrefab").objectReferenceValue = wellFishing.GetComponent<NetworkObject>();
            dso.FindProperty("knifeBoardPrefab").objectReferenceValue = knifeBoard.GetComponent<NetworkObject>();
            dso.FindProperty("bellFramePrefab").objectReferenceValue = bellFrame.GetComponent<NetworkObject>();
            dso.FindProperty("armWrestlePrefab").objectReferenceValue = armWrestle.GetComponent<NetworkObject>();
            dso.FindProperty("tavernKeepPrefab").objectReferenceValue = tavernKeep.GetComponent<NetworkObject>();
            dso.FindProperty("tavernBandPrefab").objectReferenceValue = tavernBand.GetComponent<NetworkObject>();
            dso.FindProperty("gongPrefab").objectReferenceValue = gong.GetComponent<NetworkObject>();
            dso.FindProperty("shrinePrefab").objectReferenceValue = shrine.GetComponent<NetworkObject>();
            var folkBody = BuildFolkPrefab();
            dso.FindProperty("folkPrefab").objectReferenceValue = folkBody.GetComponent<NetworkObject>();
            dso.FindProperty("fateWheelPrefab").objectReferenceValue = fateWheel.GetComponent<NetworkObject>();
            dso.FindProperty("bountyNoticePrefab").objectReferenceValue = bountyNotice.GetComponent<NetworkObject>();
            dso.FindProperty("omenStonePrefab").objectReferenceValue = omenStone.GetComponent<NetworkObject>();
            dso.FindProperty("rangeTargetPrefab").objectReferenceValue = rangeTarget.GetComponent<NetworkObject>();
            dso.FindProperty("phoenixPrefab").objectReferenceValue = phoenix.GetComponent<NetworkObject>();
            dso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(director);
            AssetDatabase.SaveAssets();

            RegisterNetworkPrefabs(lairs);
            RegisterNetworkPrefabs(vharoth, rift);
            RegisterNetworkPrefabs(wardens);
            RegisterNetworkPrefabs(player, skeleton, archer, warden, lich, king, director, loot, dummy, ghoul, brute, necro, chest, altar, trap, breakable, gate, lever, fountain, sarcophagus, anvil, gearDrop, crackedWall, coffer, gong, shrine, rangeTarget, wraith, cardinal, unsung, shieldbearer, mimic, hoarder, fateWheel, bountyNotice, gargoyle, omenStone, plaguebearer, frostWight, hexer, soulSkull, mourner, reliquary, cat, slots, blackjack, lorePage, folkBody, phoenix, handTorch, questCargo, questTrinket, poker, oilSlick, chandelier, vigilCandle, prisoner, puzzleRoom, puzzleStatue, wellFishing, knifeBoard, bellFrame, armWrestle, puzzleMirror, scaleWeight, statDraught, bargainAltar, soulRift, cellarHatch, hearth, remains);
            BuildScene(player, director);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] Game built: " + ScenePath);
        }

        [MenuItem("Deepbound/Build Game + Windows Player")]
        public static void BuildWithPlayer()
        {
            PlayerSettings.gcIncremental = true;

            PlayerSettings.enableFrameTimingStats = true;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            Build();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },

                locationPathName = System.Environment.GetEnvironmentVariable("DEEPBOUND_BUILD_PATH") ?? StandalonePath,
                target = BuildTarget.StandaloneWindows64,

                options = System.Environment.GetEnvironmentVariable("DEEPBOUND_DEV") == "1" ? BuildOptions.Development : BuildOptions.None,
            });

            Debug.Log("[Build] Player build " + report.summary.result + " -> " + StandalonePath);

            string exePath = System.Environment.GetEnvironmentVariable("DEEPBOUND_BUILD_PATH") ?? StandalonePath;
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(exePath)), "steam_appid.txt"), Deepbound.Networking.SteamPlatform.AppId.ToString());
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        private static GameObject BuildPlayerPrefab()
        {
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
            root.AddComponent<FlagellantKit>();
            root.AddComponent<PlagueDoctorKit>();
            root.AddComponent<Ultimates>();
            root.AddComponent<Kick>();
            root.AddComponent<Flasks>();
            root.AddComponent<Emotes>();
            root.AddComponent<GiveWheel>();
            root.AddComponent<Teamwork>();
            root.AddComponent<PlayerDebuffs>();
            root.AddComponent<RangerKit>();
            root.AddComponent<KnockbackReceiver>();
            root.AddComponent<PlayerPings>();
            root.AddComponent<PlayerVoice>();

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.62f, 0f);

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

        private enum EnemyType { Skeleton, Archer, Gravewarden, TideLich, HollowKing, Ghoul, Brute, Necromancer, Wraith, Cardinal, Shieldbearer, Mimic, Hoarder, Gargoyle, Plaguebearer, FrostWight, Hexer, Mourner, Unsung, Warden, Entling, Orbweaver, ChoirAcolyte, AbyssEye, BonePiper, Cantor, RimeMagus, InkLamprey, WitheringHag, Bellringer, MotherMarrow, LanternFisher, TwiceCrowned, MirrorNun, Vharoth }

        private static readonly Art.CharacterArt.SkeletonKind[] WardenKinds =
        {
            Art.CharacterArt.SkeletonKind.Brute, Art.CharacterArt.SkeletonKind.FrostWight, Art.CharacterArt.SkeletonKind.Shieldbearer,
            Art.CharacterArt.SkeletonKind.Hexer, Art.CharacterArt.SkeletonKind.Plaguebearer,
        };
        private static readonly float[] WardenScale = { 2.1f, 1.9f, 2.1f, 1.95f, 1.95f };

        private static GameObject BuildEnemyPrefab(string name, EnemyType type, GameObject loot, int act = 0)
        {
            float scale = type == EnemyType.Vharoth ? 1.8f : type == EnemyType.MotherMarrow ? 1.55f : type == EnemyType.LanternFisher ? 1.7f : type == EnemyType.TwiceCrowned ? 1.55f : type == EnemyType.MirrorNun ? 1.3f
                : type == EnemyType.Warden ? WardenScale[act] : type == EnemyType.HollowKing ? 1.75f : type == EnemyType.Gravewarden ? 2.0f : type == EnemyType.TideLich ? 1.45f : type == EnemyType.Cardinal ? 1.7f
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

            if (type == EnemyType.Mimic) { capsule.height = 1.1f; capsule.radius = 0.55f; capsule.center = new Vector3(0f, 0.55f, 0f); }
            if (type == EnemyType.Mourner) { capsule.height = 2.2f; capsule.radius = 0.38f; capsule.center = new Vector3(0f, 1.1f, 0f); }
            if (type == EnemyType.Gargoyle) { capsule.height = 2.7f; capsule.radius = 0.45f; capsule.center = new Vector3(0f, 1.35f, 0f); }
            if (type == EnemyType.Wraith) { capsule.height = 2.0f; capsule.center = new Vector3(0f, 1.1f, 0f); }
            if (type == EnemyType.Brute) capsule.radius = 0.45f * scale;
            if (type == EnemyType.Entling) { capsule.height = 1.35f; capsule.radius = 0.32f; capsule.center = new Vector3(0f, 0.68f, 0f); }

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
                case EnemyType.Warden: brain = root.AddComponent<WardenBrain>(); break;
                case EnemyType.Entling: brain = root.AddComponent<EntlingBrain>(); break;
                case EnemyType.Orbweaver: brain = root.AddComponent<OrbweaverBrain>(); break;
                case EnemyType.ChoirAcolyte: brain = root.AddComponent<ChoirAcolyteBrain>(); break;
                case EnemyType.AbyssEye: brain = root.AddComponent<AbyssEyeBrain>(); break;
                case EnemyType.BonePiper: brain = root.AddComponent<BonePiperBrain>(); break;
                case EnemyType.Cantor: brain = root.AddComponent<CantorBrain>(); break;
                case EnemyType.RimeMagus: brain = root.AddComponent<RimeMagusBrain>(); break;
                case EnemyType.InkLamprey: brain = root.AddComponent<InkLampreyBrain>(); break;
                case EnemyType.WitheringHag: brain = root.AddComponent<WitheringHagBrain>(); break;
                case EnemyType.Bellringer: brain = root.AddComponent<BellringerBrain>(); break;
                case EnemyType.MotherMarrow: brain = root.AddComponent<MotherMarrowBrain>(); break;
                case EnemyType.LanternFisher: brain = root.AddComponent<LanternFisherBrain>(); break;
                case EnemyType.TwiceCrowned: brain = root.AddComponent<TwiceCrownedBrain>(); break;
                case EnemyType.MirrorNun: brain = root.AddComponent<MirrorNunBrain>(); break;
                case EnemyType.Vharoth: brain = root.AddComponent<VharothBrain>(); break;
                default: brain = root.AddComponent<EnemyBrain>(); break;
            }

            var so = new SerializedObject(brain);
            so.FindProperty("lootPrefab").objectReferenceValue = loot.GetComponent<NetworkObject>();
            void Set(string field, float value) => so.FindProperty(field).floatValue = value;
            void Kind(Art.CharacterArt.SkeletonKind k) => so.FindProperty("visualKind").intValue = (int)k;

            switch (type)
            {
                case EnemyType.Archer:

                    Kind(Art.CharacterArt.SkeletonKind.Archer);
                    Set("maxHealth", 60f); Set("damage", 14f); Set("attackRange", 15f); Set("windup", 1.0f);
                    Set("recovery", 1.5f); Set("aggroRange", 17f); Set("moveSpeed", 3.2f);
                    break;
                case EnemyType.Ghoul:

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
                case EnemyType.AbyssEye:

                    Kind(Art.CharacterArt.SkeletonKind.AbyssEye);
                    Set("maxHealth", 120f); Set("damage", 9f); Set("defense", 2f); Set("moveSpeed", 2.6f);
                    Set("attackRange", 14f); Set("windup", 0.6f); Set("recovery", 1.8f); Set("aggroRange", 20f);
                    break;
                case EnemyType.ChoirAcolyte:

                    Kind(Art.CharacterArt.SkeletonKind.ChoirAcolyte);
                    Set("maxHealth", 100f); Set("damage", 7f); Set("defense", 0f); Set("moveSpeed", 3.1f);
                    Set("attackRange", 12f); Set("windup", 0.8f); Set("recovery", 2.2f); Set("aggroRange", 17f);
                    break;
                case EnemyType.Hexer:
                    Kind(Art.CharacterArt.SkeletonKind.Hexer);
                    Set("maxHealth", 95f); Set("damage", 10f); Set("defense", 0f); Set("moveSpeed", 3.3f);
                    Set("attackRange", 13f); Set("windup", 0.8f); Set("recovery", 1.9f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Mourner:

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

                    Kind(Art.CharacterArt.SkeletonKind.Hoarder);
                    Set("maxHealth", 170f); Set("damage", 0f); Set("defense", 2f); Set("moveSpeed", 4.6f);
                    Set("attackRange", 0.1f); Set("aggroRange", 15f); Set("leashRange", 80f);
                    break;
                case EnemyType.Mimic:

                    Kind(Art.CharacterArt.SkeletonKind.Mimic);
                    Set("maxHealth", 240f); Set("damage", 24f); Set("defense", 4f); Set("moveSpeed", 4.4f);
                    Set("attackRange", 1.9f); Set("windup", 0.4f); Set("recovery", 0.8f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Shieldbearer:

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

                    Kind(Art.CharacterArt.SkeletonKind.Wraith);
                    Set("maxHealth", 70f); Set("damage", 12f); Set("defense", 0f); Set("moveSpeed", 3.4f);
                    Set("attackRange", 12f); Set("windup", 0.8f); Set("recovery", 1.7f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Gravewarden:
                    Kind(Art.CharacterArt.SkeletonKind.Gravewarden);
                    Set("maxHealth", 1700f); Set("damage", 26f); Set("defense", 4f); Set("moveSpeed", 2.9f);
                    Set("attackRange", 3.6f); Set("recovery", 1.2f); Set("aggroRange", 20f); Set("leashRange", 60f);
                    break;
                case EnemyType.TideLich:

                    Kind(Art.CharacterArt.SkeletonKind.Lich);
                    Set("maxHealth", 2600f); Set("damage", 18f); Set("defense", 2f); Set("moveSpeed", 3.4f);
                    Set("attackRange", 2.8f); Set("recovery", 1.0f); Set("aggroRange", 22f); Set("leashRange", 60f);
                    break;
                case EnemyType.Unsung:

                    Kind(Art.CharacterArt.SkeletonKind.Unsung);
                    Set("maxHealth", 4150f); Set("damage", 34f); Set("defense", 7f); Set("moveSpeed", 1.7f);
                    Set("attackRange", 3.6f); Set("windup", 1.0f); Set("recovery", 1.2f); Set("aggroRange", 26f); Set("leashRange", 70f);
                    break;
                case EnemyType.Cardinal:
                    Kind(Art.CharacterArt.SkeletonKind.Cardinal);
                    Set("maxHealth", 3700f); Set("damage", 32f); Set("defense", 6f); Set("moveSpeed", 2.9f);
                    Set("attackRange", 3.4f); Set("recovery", 1.1f); Set("aggroRange", 22f); Set("leashRange", 60f);
                    break;
                case EnemyType.RimeMagus:

                    Kind(Art.CharacterArt.SkeletonKind.RimeMagus);
                    Set("maxHealth", 110f); Set("damage", 11f); Set("defense", 0f); Set("moveSpeed", 3.1f);
                    Set("attackRange", 13f); Set("windup", 0.8f); Set("recovery", 1.8f); Set("aggroRange", 19f);
                    break;
                case EnemyType.InkLamprey:

                    Kind(Art.CharacterArt.SkeletonKind.InkLamprey);
                    Set("maxHealth", 135f); Set("damage", 13f); Set("defense", 1f); Set("moveSpeed", 3.6f);
                    Set("attackRange", 2.2f); Set("windup", 0.6f); Set("recovery", 1.1f); Set("aggroRange", 18f);
                    break;
                case EnemyType.WitheringHag:
                    Kind(Art.CharacterArt.SkeletonKind.WitheringHag);
                    Set("maxHealth", 120f); Set("damage", 12f); Set("defense", 0f); Set("moveSpeed", 2.9f);
                    Set("attackRange", 12f); Set("windup", 0.8f); Set("recovery", 1.8f); Set("aggroRange", 18f);
                    break;

                case EnemyType.MotherMarrow:
                    Kind(Art.CharacterArt.SkeletonKind.WitheringHag);
                    Set("maxHealth", 800f); Set("damage", 22f); Set("defense", 3f); Set("moveSpeed", 2.7f);
                    Set("attackRange", 3f); Set("recovery", 1.1f); Set("aggroRange", 22f); Set("leashRange", 40f);
                    break;
                case EnemyType.LanternFisher:
                    Kind(Art.CharacterArt.SkeletonKind.FrostWight);
                    Set("maxHealth", 1300f); Set("damage", 25f); Set("defense", 4f); Set("moveSpeed", 2.6f);
                    Set("attackRange", 3.4f); Set("recovery", 1.2f); Set("aggroRange", 22f); Set("leashRange", 40f);
                    break;
                case EnemyType.TwiceCrowned:
                    Kind(Art.CharacterArt.SkeletonKind.King);
                    Set("maxHealth", 1700f); Set("damage", 27f); Set("defense", 5f); Set("moveSpeed", 3.0f);
                    Set("attackRange", 3f); Set("recovery", 1.1f); Set("aggroRange", 22f); Set("leashRange", 40f);
                    break;
                case EnemyType.Vharoth:

                    Kind(Art.CharacterArt.SkeletonKind.Gargoyle);
                    Set("maxHealth", 3600f); Set("damage", 31f); Set("defense", 5f); Set("moveSpeed", 3.0f);
                    Set("attackRange", 3.4f); Set("recovery", 1.1f); Set("aggroRange", 22f); Set("leashRange", 60f);
                    break;
                case EnemyType.MirrorNun:
                    Kind(Art.CharacterArt.SkeletonKind.ChoirAcolyte);
                    Set("maxHealth", 1900f); Set("damage", 29f); Set("defense", 4f); Set("moveSpeed", 2.8f);
                    Set("attackRange", 2.8f); Set("recovery", 1.1f); Set("aggroRange", 22f); Set("leashRange", 40f);
                    break;
                case EnemyType.Bellringer:

                    Kind(Art.CharacterArt.SkeletonKind.Bellringer);
                    Set("maxHealth", 180f); Set("damage", 20f); Set("defense", 2f); Set("moveSpeed", 2.8f);
                    Set("attackRange", 2.6f); Set("windup", 0.75f); Set("recovery", 1.2f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Cantor:

                    Kind(Art.CharacterArt.SkeletonKind.Cantor);
                    Set("maxHealth", 90f); Set("damage", 9f); Set("defense", 0f); Set("moveSpeed", 3.2f);
                    Set("attackRange", 12f); Set("windup", 0.8f); Set("recovery", 2f); Set("aggroRange", 18f);
                    break;
                case EnemyType.BonePiper:

                    Kind(Art.CharacterArt.SkeletonKind.BonePiper);
                    Set("maxHealth", 120f); Set("damage", 13f); Set("defense", 1f); Set("moveSpeed", 3.0f);
                    Set("attackRange", 13f); Set("windup", 0.6f); Set("recovery", 1.4f); Set("aggroRange", 21f);
                    break;
                case EnemyType.Orbweaver:

                    Kind(Art.CharacterArt.SkeletonKind.Orbweaver);
                    Set("maxHealth", 105f); Set("damage", 12f); Set("defense", 0f); Set("moveSpeed", 3.1f);
                    Set("attackRange", 14f); Set("windup", 0.7f); Set("recovery", 1.6f); Set("aggroRange", 20f);
                    break;
                case EnemyType.Entling:

                    Kind(Art.CharacterArt.SkeletonKind.Entling);
                    Set("maxHealth", 70f); Set("damage", 9f); Set("defense", 1f); Set("moveSpeed", 6f);
                    Set("attackRange", 2.0f); Set("windup", 0.35f); Set("recovery", 0.6f); Set("aggroRange", 18f);
                    break;
                case EnemyType.Warden:
                {
                    so.FindProperty("act").intValue = act;
                    Kind(WardenKinds[act]);

                    float[] hp = { 1250f, 2000f, 3000f, 3500f, 3500f }, dmg = { 25f, 28f, 30f, 32f, 34f }, def = { 4f, 4f, 6f, 5f, 6f }, spd = { 2.6f, 3.0f, 2.7f, 2.9f, 2.4f };
                    Set("maxHealth", hp[act]); Set("damage", dmg[act]); Set("defense", def[act]); Set("moveSpeed", spd[act]);
                    Set("attackRange", 3.3f); Set("recovery", 1.2f); Set("aggroRange", 20f); Set("leashRange", 60f);
                    break;
                }
                case EnemyType.HollowKing:
                    Kind(Art.CharacterArt.SkeletonKind.King);
                    Set("maxHealth", 3080f); Set("damage", 30f); Set("defense", 6f); Set("moveSpeed", 3.1f);
                    Set("attackRange", 3.5f); Set("recovery", 1.1f); Set("aggroRange", 20f); Set("leashRange", 60f);
                    break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, name);
        }

        private static GameObject BuildLootPrefab()
        {
            var root = new GameObject("LootDrop");
            root.AddComponent<NetworkObject>();
            root.AddComponent<LootDrop>();
            return SavePrefab(root, "LootDrop");
        }

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

        private static GameObject BuildSoulRiftPrefab()
        {
            var root = new GameObject("SoulRift");
            root.AddComponent<NetworkObject>();
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = 0.7f;
            capsule.height = 2.5f;
            capsule.center = new Vector3(0f, 1.25f, 0f);
            SetTeam(root.AddComponent<Health>(), Team.Enemies, 1.25f);
            root.AddComponent<SoulRift>();
            return SavePrefab(root, "SoulRift");
        }

        private static GameObject BuildCellarHatchPrefab()
        {
            var root = new GameObject("CellarHatch");
            root.AddComponent<NetworkObject>();
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(1.3f, 0.3f, 1.3f);
            box.center = new Vector3(0f, 0.15f, 0f);
            box.isTrigger = true;
            SetTeam(root.AddComponent<Health>(), Team.Props, 0.15f);
            root.AddComponent<CellarHatch>();
            return SavePrefab(root, "CellarHatch");
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

        private static GameObject BuildPrisonerPrefab()
        {
            var root = new GameObject("PrisonerCage");
            root.AddComponent<NetworkObject>();
            var body = root.AddComponent<CapsuleCollider>();
            body.radius = 0.4f; body.height = 1.8f; body.center = new Vector3(0f, 0.9f, 0f); body.isTrigger = true;
            SetTeam(root.AddComponent<Health>(), Team.Allies, 1f);
            root.AddComponent<PrisonerCage>();
            return SavePrefab(root, "PrisonerCage");
        }

        private static GameObject BuildPhoenixPrefab()
        {
            var root = new GameObject("Phoenix");
            root.AddComponent<NetworkObject>();
            var body = root.AddComponent<CapsuleCollider>();
            body.radius = 0.45f; body.height = 0.9f; body.center = Vector3.zero; body.isTrigger = true;
            SetTeam(root.AddComponent<Health>(), Team.Allies, 0f);
            root.AddComponent<Deepbound.Player.Phoenix>();
            return SavePrefab(root, "Phoenix");
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
