using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class LiquidShaderAssets
{
    private const string BundleResourceName = "RealisticLiquidTransfer.Resources.liquidshaders.bundle";
    private const string MaterialAssetName = "LiquidSurfaceMaterial";
    private const string DensityComputeAssetName = "LiquidDensityCompute";

    private static AssetBundle bundle;
    private static Material surfaceMaterial;
    private static ComputeShader densityComputeShader;
    private static readonly Dictionary<string, AudioClip> AudioClips =
        new Dictionary<string, AudioClip>(StringComparer.Ordinal);

    internal static Material SurfaceMaterial => surfaceMaterial;
    internal static ComputeShader DensityComputeShader => densityComputeShader;

    internal static bool TryGetAudioClip(string addressName, out AudioClip clip)
    {
        if (AudioClips.TryGetValue(addressName, out clip))
        {
            return true;
        }

        clip = bundle ? bundle.LoadAsset<AudioClip>(addressName) : null;
        if (clip)
        {
            AudioClips[addressName] = clip;
            return true;
        }

        clip = null;
        return false;
    }

    internal static void Load()
    {
        Unload();

        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundleResourceName))
        {
            if (stream == null)
            {
                throw new InvalidDataException($"Embedded shader bundle '{BundleResourceName}' was not found.");
            }

            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                bundle = AssetBundle.LoadFromMemory(bytes);
            }
        }

        if (!bundle)
        {
            throw new InvalidDataException(
                $"Unity {Application.unityVersion} rejected the embedded liquid shader bundle.");
        }

        surfaceMaterial = bundle.LoadAsset<Material>(MaterialAssetName);
        if (!surfaceMaterial || !surfaceMaterial.shader || !surfaceMaterial.shader.isSupported)
        {
            Unload();
            throw new InvalidDataException(
                $"The liquid surface shader material '{MaterialAssetName}' is missing or unsupported.");
        }

        densityComputeShader = bundle.LoadAsset<ComputeShader>(DensityComputeAssetName);
    }

    internal static void Unload()
    {
        foreach (var clip in AudioClips.Values)
        {
            if (clip)
            {
                UnityEngine.Object.Destroy(clip);
            }
        }
        AudioClips.Clear();

        if (surfaceMaterial)
        {
            UnityEngine.Object.Destroy(surfaceMaterial);
            surfaceMaterial = null;
        }

        if (densityComputeShader)
        {
            UnityEngine.Object.Destroy(densityComputeShader);
            densityComputeShader = null;
        }

        if (bundle)
        {
            bundle.Unload(false);
            bundle = null;
        }
    }
}
