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
            var player = BuildPlayerPrefab();
            var skeleton = BuildEnemyPrefab("Skeleton", EnemyType.Skeleton, loot);
            var archer = BuildEnemyPrefab("SkeletonArcher", EnemyType.Archer, loot);
            var warden = BuildEnemyPrefab("Gravewarden", EnemyType.Gravewarden, loot);
            var lich = BuildEnemyPrefab("TideLich", EnemyType.TideLich, loot);
            var king = BuildEnemyPrefab("SkeletonKing", EnemyType.HollowKing, loot);
            var dummy = BuildDummyPrefab();
            var director = BuildDirectorPrefab(skeleton, archer, dummy, new[] { warden, lich, king });

            RegisterNetworkPrefabs(player, skeleton, archer, warden, lich, king, director, loot, dummy);
            BuildScene(player, director);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] Game built: " + ScenePath);
        }

        [MenuItem("Deepbound/Build Game + Windows Player")]
        public static void BuildWithPlayer()
        {
            Build();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = StandalonePath,
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

        private static GameObject BuildPlayerPrefab()
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
            root.AddComponent<KnockbackReceiver>();

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

        private enum EnemyType { Skeleton, Archer, Gravewarden, TideLich, HollowKing }

        private static GameObject BuildEnemyPrefab(string name, EnemyType type, GameObject loot)
        {
            float scale = type == EnemyType.HollowKing ? 1.75f : type == EnemyType.Gravewarden ? 2.0f : type == EnemyType.TideLich ? 1.45f : 1f;
            var root = new GameObject(name);

            root.AddComponent<NetworkObject>();
            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Server;
            YawOnly(netTransform);

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f * scale;
            capsule.radius = 0.35f * scale;
            capsule.center = new Vector3(0f, 0.9f * scale, 0f);

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
                    Set("maxHealth", 50f); Set("damage", 14f); Set("attackRange", 15f); Set("windup", 1.0f);
                    Set("recovery", 1.5f); Set("aggroRange", 17f); Set("moveSpeed", 3.2f);
                    break;
                case EnemyType.Gravewarden:
                    Kind(Art.CharacterArt.SkeletonKind.Gravewarden);
                    Set("maxHealth", 1100f); Set("damage", 26f); Set("defense", 4f); Set("moveSpeed", 2.9f);
                    Set("attackRange", 3.6f); Set("recovery", 1.2f); Set("aggroRange", 20f); Set("leashRange", 60f);
                    break;
                case EnemyType.TideLich:
                    // Squishier than the others; the difficulty is catching it.
                    Kind(Art.CharacterArt.SkeletonKind.Lich);
                    Set("maxHealth", 950f); Set("damage", 18f); Set("defense", 2f); Set("moveSpeed", 3.4f);
                    Set("attackRange", 2.8f); Set("recovery", 1.0f); Set("aggroRange", 22f); Set("leashRange", 60f);
                    break;
                case EnemyType.HollowKing:
                    Kind(Art.CharacterArt.SkeletonKind.King);
                    Set("maxHealth", 1300f); Set("damage", 30f); Set("defense", 6f); Set("moveSpeed", 3.1f);
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

        private static GameObject BuildDirectorPrefab(GameObject skeleton, GameObject archer, GameObject dummy, GameObject[] bossesByDepth)
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
