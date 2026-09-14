using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Deepbound.EditorTools
{
    /// <summary>
    /// One-shot project configuration, run from the command line via
    /// <c>-executeMethod Deepbound.EditorTools.ProjectBootstrap.Configure</c>.
    ///
    /// This exists because a Unity project created with <c>-createProject</c> is always a
    /// Built-in Render Pipeline project — there is no CLI flag to create a URP one. The
    /// URP asset and its renderer are ScriptableObjects that have to be instantiated and
    /// registered from inside the editor, so this is the only way to get a correctly
    /// configured URP project without a human clicking through the Hub.
    ///
    /// It is idempotent: re-running it reuses the existing assets rather than creating
    /// duplicates, so it is safe to invoke again after a package upgrade.
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string SettingsDir = "Assets/Settings";
        private const string RendererPath = SettingsDir + "/DeepboundRenderer.asset";
        private const string PipelinePath = SettingsDir + "/DeepboundURP.asset";

        public static void Configure()
        {
            Debug.Log("[Bootstrap] Starting project configuration.");

            ConfigureRenderPipeline();
            ConfigurePlayerSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Bootstrap] Project configuration complete.");
        }

        private static void ConfigureRenderPipeline()
        {
            if (!Directory.Exists(SettingsDir))
            {
                Directory.CreateDirectory(SettingsDir);
                AssetDatabase.Refresh();
            }

            // The renderer data has to exist as a saved asset before the pipeline asset is
            // created from it -- UniversalRenderPipelineAsset.Create stores a reference to
            // it, and a reference to an unsaved in-memory ScriptableObject serialises as
            // null.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Bootstrap] Created renderer data at " + RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[Bootstrap] Created URP asset at " + PipelinePath);
            }

            // Both of these matter and they are not the same setting. defaultRenderPipeline
            // is the project-wide fallback; QualitySettings.renderPipeline overrides it
            // per quality level and, if left null on a level, silently drops that level
            // back to the Built-in pipeline.
            GraphicsSettings.defaultRenderPipeline = pipeline;

            int originalQualityLevel = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalQualityLevel, false);

            Debug.Log("[Bootstrap] URP assigned to GraphicsSettings and all " +
                      QualitySettings.names.Length + " quality levels.");
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "Deepbound";
            PlayerSettings.productName = "Deepbound";

            // Linear is required for URP's lighting to behave correctly. Gamma is the
            // legacy default and makes lit scenes read washed out and flat.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

            Debug.Log("[Bootstrap] Player settings configured (Linear color space, 1920x1080).");
        }

        // Deliberately NOT configured here: the physics fixed timestep.
        //
        // Time.fixedDeltaTime and Physics.defaultSolverIterations are *runtime* properties.
        // Setting them from an editor script changes the running editor session and is then
        // thrown away -- they do not write back to ProjectSettings/TimeManager.asset or
        // PhysicsManager.asset, so the change silently does not survive. An earlier version
        // of this file did exactly that and logged "Physics configured (60Hz)" while the
        // project stayed at Unity's 50Hz default.
        //
        // Those files are committed to git, so the correct place for the value IS the file:
        // TimeManager.asset holds Fixed Timestep as a rational (m_Count / m_Rate), set to
        // 2352000 / 141120000 = exactly 60Hz. A fresh clone inherits it. Change it there,
        // not here.
    }
}
