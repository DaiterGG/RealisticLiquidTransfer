using System;
using System.IO;
using System.Reflection;
using System.Text;
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

    internal static Material SurfaceMaterial => surfaceMaterial;
    internal static ComputeShader DensityComputeShader => densityComputeShader;

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
                var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 8));
                Debug.Log(
                    $"[RealisticLiquidTransfer] Loading shader bundle: Unity={Application.unityVersion}, "
                    + $"bytes={bytes.Length}, header='{header}'.");
                bundle = AssetBundle.LoadFromMemory(bytes);
            }
        }

        if (!bundle)
        {
            throw new InvalidDataException(
                $"Unity {Application.unityVersion} rejected the embedded liquid shader bundle.");
        }

        Debug.Log(
            $"[RealisticLiquidTransfer] Shader bundle loaded; assets={string.Join(", ", bundle.GetAllAssetNames())}.");
        surfaceMaterial = bundle.LoadAsset<Material>(MaterialAssetName);
        if (!surfaceMaterial || !surfaceMaterial.shader || !surfaceMaterial.shader.isSupported)
        {
            Unload();
            throw new InvalidDataException(
                $"The liquid surface shader material '{MaterialAssetName}' is missing or unsupported.");
        }

        Debug.Log(
            $"[RealisticLiquidTransfer] Loaded material '{surfaceMaterial.name}' with shader "
            + $"'{surfaceMaterial.shader.name}' (supported={surfaceMaterial.shader.isSupported}).");
        densityComputeShader = bundle.LoadAsset<ComputeShader>(DensityComputeAssetName);
        if (densityComputeShader)
        {
            Debug.Log(
                $"[RealisticLiquidTransfer] Loaded compute shader '{densityComputeShader.name}'.");
        }
        else
        {
            Debug.LogWarning(
                $"[RealisticLiquidTransfer] Compute shader '{DensityComputeAssetName}' is missing; "
                + "the CPU liquid renderer will be used.");
        }
    }

    internal static void Unload()
    {
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
