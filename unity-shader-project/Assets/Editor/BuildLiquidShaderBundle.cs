using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RealisticLiquidTransfer.Editor
{
    public static class BuildLiquidShaderBundle
    {
        private const string ShaderPath = "Assets/Shaders/LiquidSurface.shader";
        private const string RendererTemplatePath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset";
        private const string RendererPath = "Assets/Generated/UniversalRendererData.asset";
        private const string PipelinePath = "Assets/Generated/UniversalRenderPipelineAsset.asset";
        private const string MaterialPath = "Assets/Generated/LiquidSurfaceMaterial.mat";
        private const string BundleName = "liquidshaders.bundle";
        private const string MaterialAddress = "LiquidSurfaceMaterial";

        public static void Build()
        {
            if (Application.unityVersion != "2022.3.62f3")
            {
                throw new InvalidOperationException(
                    $"The shader bundle must be built with Unity 2022.3.62f3, not {Application.unityVersion}.");
            }

            Directory.CreateDirectory("Assets/Generated");
            EnsureUniversalPipeline();
            AssetDatabase.Refresh();

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader || !shader.isSupported)
            {
                throw new InvalidOperationException($"The URP liquid shader is missing or unsupported: {ShaderPath}");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader)
                {
                    name = MaterialAddress
                };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AssetBundles"));
            Directory.CreateDirectory(outputDirectory);
            var bundleBuild = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = new[] { MaterialPath },
                addressableNames = new[] { MaterialAddress }
            };
            var manifest = UnityEditor.BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { bundleBuild },
                BuildAssetBundleOptions.ChunkBasedCompression
                    | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);
            var bundlePath = Path.Combine(outputDirectory, BundleName);
            if (manifest == null || !File.Exists(bundlePath))
            {
                throw new InvalidOperationException($"Unity failed to produce the shader bundle: {bundlePath}");
            }

            var validationBundle = AssetBundle.LoadFromFile(bundlePath);
            if (!validationBundle)
            {
                throw new InvalidOperationException(
                    $"The Unity Editor could not load its generated AssetBundle: {bundlePath}");
            }

            try
            {
                var bundledMaterial = validationBundle.LoadAsset<Material>(MaterialAddress);
                if (!bundledMaterial
                    || !bundledMaterial.shader
                    || bundledMaterial.shader.name != "RealisticLiquidTransfer/LiquidSurface"
                    || !bundledMaterial.shader.isSupported)
                {
                    throw new InvalidOperationException(
                        "The generated bundle does not contain a supported RealisticLiquidTransfer/LiquidSurface material.");
                }
            }
            finally
            {
                validationBundle.Unload(true);
            }

            Debug.Log(
                $"Built and loaded URP liquid shader bundle with Unity {Application.unityVersion}: {bundlePath}");
        }

        private static void EnsureUniversalPipeline()
        {
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset)
            {
                return;
            }

            if (!File.Exists(RendererPath)
                && !AssetDatabase.CopyAsset(RendererTemplatePath, RendererPath))
            {
                throw new InvalidOperationException(
                    $"Could not copy the default URP renderer from '{RendererTemplatePath}'.");
            }

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (!renderer)
            {
                throw new InvalidOperationException($"Could not load URP renderer data: {RendererPath}");
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (!pipeline)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
        }
    }
}
