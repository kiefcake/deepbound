using System.IO;
using Deepbound.Networking;
using Deepbound.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// Generates the M0 network sandbox: the player prefab, a test scene, and the build
    /// settings entry. Runs from the menu (<c>Deepbound/Build M0 Sandbox</c>) or the
    /// command line (<c>-executeMethod Deepbound.EditorTools.M0SceneBuilder.Build</c>).
    ///
    /// <para>
    /// Generated rather than hand-authored so the scene and prefab can be reviewed as code
    /// and rebuilt identically on any machine. Re-running overwrites both, so do not
    /// hand-edit the outputs — change this file instead. That stops being true at M1, when
    /// real level art arrives and the scene becomes an authored asset.
    /// </para>
    /// </summary>
    public static class M0SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/M0_Sandbox.unity";
        private const string PrefabPath = "Assets/Prefabs/Player.prefab";
        private const string MaterialDir = "Assets/Art/Materials";
        private const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string StandalonePath = "Builds/M0/Deepbound.exe";

        [MenuItem("Deepbound/Build M0 Sandbox")]
        public static void Build()
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder(MaterialDir);

            var playerPrefab = BuildPlayerPrefab();
            RegisterNetworkPrefab(playerPrefab);
            BuildScene(playerPrefab);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Debug.Log("[M0] Sandbox built: " + ScenePath + ", " + PrefabPath);
        }

        /// <summary>
        /// Builds the sandbox, then a Windows standalone player, so the editor and the exe
        /// can be connected to each other on one machine.
        /// </summary>
        [MenuItem("Deepbound/Build M0 Sandbox + Windows Player")]
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

            Debug.Log("[M0] Player build " + report.summary.result + " -> " + StandalonePath);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------------
        // Player prefab
        // ------------------------------------------------------------------

        private static GameObject BuildPlayerPrefab()
        {
            var bodyMaterial = GetOrCreateMaterial("PlayerBody", Color.white);
            var visorMaterial = GetOrCreateMaterial("PlayerVisor", new Color(0.1f, 0.1f, 0.12f));

            // Origin at the feet: spawn points and teleports then mean "stand here".
            var root = new GameObject("Player");

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            root.AddComponent<NetworkObject>();

            var netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            // Yaw is the only rotation the body ever has; pitch lives on the local camera.
            netTransform.SyncRotAngleX = false;
            netTransform.SyncRotAngleZ = false;
            netTransform.SyncScaleX = false;
            netTransform.SyncScaleY = false;
            netTransform.SyncScaleZ = false;

            var body = CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform,
                new Vector3(0f, 0.9f, 0f), new Vector3(0.7f, 0.9f, 0.7f), bodyMaterial);

            // A dark visor on the front, so other players can see which way you face.
            var visor = CreatePrimitive(PrimitiveType.Cube, "Visor", root.transform,
                new Vector3(0f, 1.55f, 0.3f), new Vector3(0.45f, 0.12f, 0.12f), visorMaterial);

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);

            var camera = pivot.gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.fieldOfView = 80f;
            camera.enabled = false;

            var listener = pivot.gameObject.AddComponent<AudioListener>();
            listener.enabled = false;

            var motor = root.AddComponent<FirstPersonMotor>();
            motor.CameraPivot = pivot;
            motor.enabled = false;

            var player = root.AddComponent<NetworkPlayer>();
            var so = new SerializedObject(player);
            so.FindProperty("playerCamera").objectReferenceValue = camera;
            so.FindProperty("audioListener").objectReferenceValue = listener;
            so.FindProperty("motor").objectReferenceValue = motor;
            var renderers = so.FindProperty("bodyRenderers");
            renderers.arraySize = 2;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = body.GetComponent<Renderer>();
            renderers.GetArrayElementAtIndex(1).objectReferenceValue = visor.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssignGlobalObjectIdHash(prefab.GetComponent<NetworkObject>());
            return prefab;
        }

        /// <summary>
        /// Gives the prefab's NetworkObject the id that peers use to agree which prefab to
        /// spawn.
        ///
        /// NGO derives it from the asset's GUID inside <c>NetworkObject.OnValidate</c>. When
        /// the component is added from script, OnValidate runs while the object is still an
        /// unsaved scene object with no GUID, and the hash is left at 0 — every client would
        /// then fail to spawn the player. Calling it again once the asset exists fixes that.
        /// It is internal, hence the reflection; if a future NGO renames it, this throws
        /// loudly here rather than failing quietly at runtime.
        /// </summary>
        private static void AssignGlobalObjectIdHash(NetworkObject networkObject)
        {
            var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            onValidate.Invoke(networkObject, null);

            if (networkObject.PrefabIdHash == 0)
            {
                throw new System.InvalidOperationException("[M0] Player prefab still has GlobalObjectIdHash 0.");
            }

            EditorUtility.SetDirty(networkObject);
            AssetDatabase.SaveAssets();
        }

        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null) return;

            foreach (var entry in list.PrefabList)
            {
                if (entry.Prefab == prefab) return;
            }

            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        private static void BuildScene(GameObject playerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);

            BuildTestGeometry();

            var menuCamera = new GameObject("MenuCamera").AddComponent<Camera>();
            menuCamera.transform.SetPositionAndRotation(new Vector3(0f, 9f, -12f), Quaternion.Euler(35f, 0f, 0f));
            menuCamera.gameObject.AddComponent<AudioListener>();

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
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        /// <summary>
        /// Placeholder blockout: enough shapes to judge movement speed, jump height and
        /// whether other players' positions look right. Not level design.
        /// </summary>
        private static void BuildTestGeometry()
        {
            var floor = GetOrCreateMaterial("Floor", new Color(0.32f, 0.31f, 0.30f));
            var stone = GetOrCreateMaterial("Stone", new Color(0.50f, 0.47f, 0.43f));
            var accent = GetOrCreateMaterial("Accent", new Color(0.55f, 0.35f, 0.20f));

            var root = new GameObject("Blockout").transform;

            CreatePrimitive(PrimitiveType.Cube, "Floor", root, new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), floor);

            // Perimeter walls.
            CreatePrimitive(PrimitiveType.Cube, "Wall N", root, new Vector3(0f, 2f, 20f), new Vector3(40f, 4f, 1f), stone);
            CreatePrimitive(PrimitiveType.Cube, "Wall S", root, new Vector3(0f, 2f, -20f), new Vector3(40f, 4f, 1f), stone);
            CreatePrimitive(PrimitiveType.Cube, "Wall E", root, new Vector3(20f, 2f, 0f), new Vector3(1f, 4f, 40f), stone);
            CreatePrimitive(PrimitiveType.Cube, "Wall W", root, new Vector3(-20f, 2f, 0f), new Vector3(1f, 4f, 40f), stone);

            // Pillars, for line-of-sight and running circles around each other.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                CreatePrimitive(PrimitiveType.Cylinder, "Pillar " + i, root,
                    new Vector3(Mathf.Cos(a) * 9f, 2f, Mathf.Sin(a) * 9f), new Vector3(1.2f, 2f, 1.2f), stone);
            }

            // Jump-height test: 0.5m is reachable, 1.0m is reachable, 1.5m should not be.
            CreatePrimitive(PrimitiveType.Cube, "Step 0.5m", root, new Vector3(-6f, 0.25f, 14f), new Vector3(2f, 0.5f, 2f), accent);
            CreatePrimitive(PrimitiveType.Cube, "Step 1.0m", root, new Vector3(-3f, 0.5f, 14f), new Vector3(2f, 1f, 2f), accent);
            CreatePrimitive(PrimitiveType.Cube, "Step 1.5m", root, new Vector3(0f, 0.75f, 14f), new Vector3(2f, 1.5f, 2f), accent);

            // A ramp up to a platform, to check slopes and that height syncs.
            var ramp = CreatePrimitive(PrimitiveType.Cube, "Ramp", root, new Vector3(10f, 1f, -10f), new Vector3(3f, 0.3f, 7f), accent);
            ramp.transform.rotation = Quaternion.Euler(-17f, 0f, 0f);
            CreatePrimitive(PrimitiveType.Cube, "Platform", root, new Vector3(10f, 1.85f, -3.5f), new Vector3(5f, 0.3f, 5f), stone);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            // The player's own collider is the CharacterController; a capsule collider on
            // its body mesh would collide with the controller it belongs to.
            if (parent != null && parent.GetComponent<CharacterController>() != null)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = MaterialDir + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }
}
