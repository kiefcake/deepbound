using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Deepbound.EditorTools
{
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

            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

            Debug.Log("[Bootstrap] Player settings configured (Linear color space, 1920x1080).");
        }
    }
}
