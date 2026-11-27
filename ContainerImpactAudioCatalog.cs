using System.Collections.Generic;
using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class ContainerImpactAudioCatalog
{
    internal const float DefaultWeakVolumeMultiplier = 1f;
    internal const float DefaultStrongVolumeMultiplier = 1f;
    internal const float DefaultStrongMinimumVolume = 0.7f;
    internal const float DefaultFullVolumeVelocity = 2000f;
    internal const float DefaultMasterVolumeMultiplier = 1f;
    internal const float MaximumFullVolumeVelocity = 5000f;

    private static readonly Dictionary<ContainerMaterial, int> WeakClipCounts =
        new Dictionary<ContainerMaterial, int>
        {
            { ContainerMaterial.Ceramic, 11 },
            { ContainerMaterial.Plastic, 4 },
            { ContainerMaterial.Glass, 5 },
            { ContainerMaterial.Metal, 3 },
            { ContainerMaterial.Cardboard, 3 }
        };

    private static readonly Dictionary<ContainerMaterial, int> StrongClipCounts =
        new Dictionary<ContainerMaterial, int>
        {
            { ContainerMaterial.Ceramic, 2 },
            { ContainerMaterial.Plastic, 5 },
            { ContainerMaterial.Glass, 2 },
            { ContainerMaterial.Metal, 2 },
            { ContainerMaterial.Cardboard, 3 }
        };

    internal static bool TryPlay(
        ContainerMaterial material,
        float velocity,
        float strongThreshold,
        float weakVolumeMultiplier,
        float strongVolumeMultiplier,
        float strongMinimumVolume,
        float fullVolumeVelocity,
        float masterVolumeMultiplier,
        float pitchVariationPercent,
        bool forceWeak,
        AudioSource source)
    {
        if (!source || velocity <= 0f)
        {
            return false;
        }

        if (material == ContainerMaterial.Aluminium)
        {
            material = ContainerMaterial.Metal;
        }

        var strong = !forceWeak && velocity >= strongThreshold;
        var counts = strong ? StrongClipCounts : WeakClipCounts;
        if (!counts.TryGetValue(material, out var count) || count <= 0)
        {
            return false;
        }

        var index = Random.Range(1, count + 1);
        var addressName =
            $"ContainerImpact.{material}.{(strong ? "Strong" : "Weak")}.{index}";
        if (!LiquidShaderAssets.TryGetAudioClip(addressName, out var clip))
        {
            DebugLog.Write(
                $"Container impact clip not loaded: material={material}, "
                + $"address={addressName}.");
            return false;
        }

        var volume = GetVolume(
            velocity,
            strongThreshold,
            weakVolumeMultiplier,
            strongVolumeMultiplier,
            strongMinimumVolume,
            fullVolumeVelocity,
            masterVolumeMultiplier);
        var pitchOffset = Mathf.Clamp(pitchVariationPercent, 0f, 50f) / 100f;
        var pitch = Random.Range(1f - pitchOffset, 1f + pitchOffset);
        var playbackSource = source.gameObject.AddComponent<AudioSource>();
        playbackSource.playOnAwake = false;
        playbackSource.loop = false;
        playbackSource.spatialBlend = 0f;
        playbackSource.volume = 1f;
        playbackSource.dopplerLevel = 0f;
        playbackSource.pitch = pitch;
        playbackSource.PlayOneShot(clip, volume);
        Object.Destroy(playbackSource, clip.length / pitch + 0.1f);
        DebugLog.Write(
            $"Container impact: material={material}, velocity={velocity:F1}px/s, "
            + $"volume={volume * 100f:F0}%, pitch={pitch:F2}, "
            + $"clip={addressName}, duration={clip.length:F3}s.");
        Debug.Log(
            $"[RealisticLiquidTransfer] Impact audio: volume={volume * 100f:F0}%, "
            + $"pitch={pitch:F2} (variation=±{pitchOffset * 100f:F0}%).");
        return true;
    }

    internal static float GetVolume(
        float velocity,
        float strongThreshold,
        float weakVolumeMultiplier,
        float strongVolumeMultiplier,
        float strongMinimumVolume,
        float fullVolumeVelocity,
        float masterVolumeMultiplier)
    {
        weakVolumeMultiplier = Mathf.Clamp(weakVolumeMultiplier, 0f, 2f);
        strongVolumeMultiplier = Mathf.Clamp(strongVolumeMultiplier, 0f, 2f);
        strongMinimumVolume = Mathf.Clamp01(strongMinimumVolume);
        fullVolumeVelocity = Mathf.Max(strongThreshold + 1f, fullVolumeVelocity);
        masterVolumeMultiplier = Mathf.Clamp(masterVolumeMultiplier, 0f, 2f);

        float volume;
        if (velocity < strongThreshold)
        {
            volume = Mathf.InverseLerp(0f, strongThreshold, velocity) * weakVolumeMultiplier;
        }
        else
        {
            var strongImpactProgress = Mathf.InverseLerp(
                strongThreshold,
                fullVolumeVelocity,
                velocity);
            volume = Mathf.Lerp(strongMinimumVolume, 1f, strongImpactProgress)
                * strongVolumeMultiplier;
        }

        return volume * masterVolumeMultiplier;
    }
}
