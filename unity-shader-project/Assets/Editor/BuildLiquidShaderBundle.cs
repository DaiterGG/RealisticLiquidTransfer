using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RealisticLiquidTransfer.Editor
{
    public static class BuildLiquidShaderBundle
    {
        private const string ShaderPath = "Assets/Shaders/LiquidSurface.shader";
        private const string ComputeShaderPath = "Assets/Shaders/LiquidDensity.compute";
        private const string RendererTemplatePath =
            "Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset";
        private const string RendererPath = "Assets/Generated/UniversalRendererData.asset";
        private const string PipelinePath = "Assets/Generated/UniversalRenderPipelineAsset.asset";
        private const string AudioDirectory = "Assets/Audio";
        private const string MaterialPath = "Assets/Generated/LiquidSurfaceMaterial.mat";
        private const string BundleName = "liquidshaders.bundle";
        private const string MaterialAddress = "LiquidSurfaceMaterial";
        private const string ComputeShaderAddress = "LiquidDensityCompute";
        private const string FillLoopAudioAssetPath = "Assets/Audio/Liquid/DrippingLoop.mp3";
        private const string FillLoopAudioAddress = "Liquid.DrippingLoop";
        private const string SplashAudioDirectory = "Assets/Audio/Liquid";
        private const int SplashAudioClipCount = 8;

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

            var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            if (!computeShader)
            {
                throw new InvalidOperationException($"The liquid density compute shader is missing: {ComputeShaderPath}");
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
            if (!Directory.Exists(AudioDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Container impact audio directory was not found: {AudioDirectory}");
            }

            var audioAssetNames = new List<string>();
            var audioAddressNames = new List<string>();
            foreach (var materialDirectory in Directory.GetDirectories(AudioDirectory))
            {
                var materialName = Path.GetFileName(materialDirectory);
                foreach (var impactKind in new[] { "Weak", "Strong" })
                {
                    var files = Directory.GetFiles(materialDirectory)
                        .Where(path => Path.GetExtension(path).Equals(
                            ".mp3",
                            StringComparison.OrdinalIgnoreCase))
                        .Where(path => Path.GetFileNameWithoutExtension(path)
                            .IndexOf(impactKind, StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    for (var index = 0; index < files.Length; index++)
                    {
                        var audioAssetName = files[index].Replace('\\', '/');
                        var importer = AssetImporter.GetAtPath(audioAssetName) as AudioImporter;
                        if (importer == null)
                        {
                            continue;
                        }

                        importer.loadInBackground = false;
                        importer.forceToMono = true;
                        EditorUtility.SetDirty(importer);
                        audioAssetNames.Add(audioAssetName);
                        audioAddressNames.Add(
                            $"ContainerImpact.{materialName}.{impactKind}.{index + 1}");
                    }
                }
            }

            if (audioAssetNames.Count == 0)
            {
                throw new InvalidOperationException(
                    "The liquid bundle contains no weak or strong container impact audio.");
            }

            var fillLoopImporter = AssetImporter.GetAtPath(FillLoopAudioAssetPath) as AudioImporter;
            if (fillLoopImporter == null)
            {
                throw new InvalidOperationException(
                    $"The liquid fill loop audio is missing or could not be imported: {FillLoopAudioAssetPath}");
            }

            fillLoopImporter.loadInBackground = false;
            EditorUtility.SetDirty(fillLoopImporter);
            audioAssetNames.Add(FillLoopAudioAssetPath);
            audioAddressNames.Add(FillLoopAudioAddress);

            var splashAudioFiles = Directory.GetFiles(SplashAudioDirectory)
                .Where(path => Path.GetExtension(path).Equals(
                    ".mp3",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .StartsWith("Splash", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (splashAudioFiles.Length != SplashAudioClipCount)
            {
                throw new InvalidOperationException(
                    $"Expected {SplashAudioClipCount} liquid splash sounds in {SplashAudioDirectory}; "
                    + $"found {splashAudioFiles.Length}.");
            }

            foreach (var splashAudioFile in splashAudioFiles)
            {
                var assetPath = splashAudioFile.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                if (importer == null)
                {
                    throw new InvalidOperationException(
                        $"Liquid splash audio is missing or could not be imported: {assetPath}");
                }

                importer.loadInBackground = false;
                EditorUtility.SetDirty(importer);
                audioAssetNames.Add(assetPath);
                audioAddressNames.Add(
                    $"Liquid.{Path.GetFileNameWithoutExtension(splashAudioFile)}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var bundledAssetNames = new List<string> { MaterialPath, ComputeShaderPath };
            bundledAssetNames.AddRange(audioAssetNames);
            var bundledAddressNames = new List<string> { MaterialAddress, ComputeShaderAddress };
            bundledAddressNames.AddRange(audioAddressNames);
            var bundleBuild = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = bundledAssetNames.ToArray(),
                addressableNames = bundledAddressNames.ToArray()
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
                var bundledComputeShader = validationBundle.LoadAsset<ComputeShader>(ComputeShaderAddress);
                if (!bundledMaterial
                    || !bundledMaterial.shader
                    || bundledMaterial.shader.name != "RealisticLiquidTransfer/LiquidSurface"
                    || !bundledMaterial.shader.isSupported)
                {
                    throw new InvalidOperationException(
                        "The generated bundle does not contain a supported RealisticLiquidTransfer/LiquidSurface material.");
                }

                if (!bundledComputeShader)
                {
                    throw new InvalidOperationException(
                        "The generated bundle does not contain the LiquidDensityCompute compute shader.");
                }

                foreach (var addressName in audioAddressNames)
                {
                    if (!validationBundle.LoadAsset<AudioClip>(addressName))
                    {
                        throw new InvalidOperationException(
                            $"The generated bundle does not contain audio '{addressName}'.");
                    }
                }
            }
            finally
            {
                validationBundle.Unload(true);
            }

            Debug.Log(
                $"Built URP liquid shader and container impact audio bundle with "
                + $"Unity {Application.unityVersion}: {bundlePath}");
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
