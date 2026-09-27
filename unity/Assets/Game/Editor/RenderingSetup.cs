// RenderingSetup.cs - makes the project's URP assets from code: the
// pipeline asset with soft four-cascade shadows and HDR, and its renderer
// with ambient occlusion, then sets them for the project and every
// quality level. Run from OpenKingdoms > Rendering > Set Up URP, or in
// batchmode with -executeMethod OpenKingdomsUnity.Studio.RenderingSetup.Run.
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenKingdomsUnity.Studio
{
    public static class RenderingSetup
    {
        public const string Folder = "Assets/Game/Rendering";
        public const string PipelinePath = Folder + "/OkuPipeline.asset";
        public const string RendererPath = Folder + "/OkuRenderer.asset";

        [MenuItem("OpenKingdoms/Rendering/Set Up URP", priority = 40)]
        public static void Run()
        {
            System.IO.Directory.CreateDirectory(Folder);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");

            // Ambient occlusion from depth, so no extra normals pass is needed.
            ScreenSpaceAmbientOcclusion ssao = null;
            foreach (var f in renderer.rendererFeatures) if (f is ScreenSpaceAmbientOcclusion s) ssao = s;
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "Ambient Occlusion";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
            }
            var so = new SerializedObject(ssao);
            Set(so, "m_Settings.Source", 0);
            Set(so, "m_Settings.Intensity", 1.2f);
            Set(so, "m_Settings.Radius", 0.3f);
            Set(so, "m_Settings.DirectLightingStrength", 0.3f);
            so.ApplyModifiedPropertiesWithoutUndo();
            var rso = new SerializedObject(renderer);
            var map = rso.FindProperty("m_RendererFeatureMap");
            if (map != null && map.arraySize != renderer.rendererFeatures.Count)
            {
                map.arraySize = renderer.rendererFeatures.Count;
                for (int i = 0; i < renderer.rendererFeatures.Count; i++)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i], out string _, out long id);
                    map.GetArrayElementAtIndex(i).longValue = id;
                }
                rso.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(renderer);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var pso = new SerializedObject(pipeline);
            Set(pso, "m_SupportsHDR", true);
            Set(pso, "m_MSAA", 4);
            Set(pso, "m_MainLightShadowsSupported", true);
            Set(pso, "m_MainLightShadowmapResolution", 4096);
            Set(pso, "m_ShadowDistance", 150f);
            Set(pso, "m_ShadowCascadeCount", 4);
            Set(pso, "m_Cascade4Split", new Vector3(0.08f, 0.22f, 0.5f));
            Set(pso, "m_SoftShadowsSupported", true);
            Set(pso, "m_AdditionalLightsRenderingMode", 1);
            Set(pso, "m_RequireDepthTexture", false);
            Set(pso, "m_RequireOpaqueTexture", false);
            pso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            // Linear light, so lighting, bloom and grading work as meant.
            PlayerSettings.colorSpace = ColorSpace.Linear;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            Debug.Log("OpenKingdoms: URP set up at " + PipelinePath);
        }

        static void Set(SerializedObject so, string path, object value)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogWarning("RenderingSetup: no property " + path); return; }
            switch (value)
            {
                case bool b: p.boolValue = b; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case Vector3 v: p.vector3Value = v; break;
            }
        }
    }
}
