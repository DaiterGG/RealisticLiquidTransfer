using HarmonyLib;
using System;
using UnityEngine;

namespace RealisticLiquidTransfer;

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.StartLiquidTransfer))]
internal static class StartLiquidTransferPatch
{
    [HarmonyPrefix]
    private static bool Prefix(WaterContainerItem wat1, WaterContainerItem wat2)
    {
        if (!MinigameBase.main)
        {
            Plugin.LogError("Cannot open liquid-transfer minigame because MinigameBase is unavailable.");
            return false;
        }

        if (MinigameBase.main.currentMinigame)
        {
            Plugin.LogWarning("Cannot open liquid-transfer minigame while another minigame is active.");
            return false;
        }

        var firstSpriteName = GetContainerSpriteName(wat1, "first");
        var secondSpriteName = GetContainerSpriteName(wat2, "second");
        var firstCapacity = GetContainerCapacity(wat1, "first");
        var secondCapacity = GetContainerCapacity(wat2, "second");
        var firstAmount = GetContainerAmount(wat1, "first");
        var secondAmount = GetContainerAmount(wat2, "second");
        MinigameBase.main.StartMinigame(
            new LiquidTransferMinigame(
                wat1,
                firstSpriteName,
                firstCapacity,
                firstAmount,
                wat2,
                secondSpriteName,
                secondCapacity,
                secondAmount),
            null);
        return false;
    }

    private static float GetContainerAmount(WaterContainerItem container, string containerLabel)
    {
        if (!container)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container amount.");
            return 0f;
        }

        return container.CurrentTotal;
    }

    private static float GetContainerCapacity(WaterContainerItem container, string containerLabel)
    {
        if (!container)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container capacity.");
            return 0f;
        }

        return container.Capacity;
    }

    private static string GetContainerSpriteName(WaterContainerItem container, string containerLabel)
    {
        if (!container)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container sprite: container is unavailable.");
            return "Unavailable";
        }

        var spriteRenderer = container.GetComponent<SpriteRenderer>();
        if (!spriteRenderer || !spriteRenderer.sprite)
        {
            spriteRenderer = container.GetComponentInChildren<SpriteRenderer>();
        }

        if (!spriteRenderer || !spriteRenderer.sprite)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container sprite.");
            return "Unavailable";
        }

        return spriteRenderer.sprite.name;
    }
}

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.HandleInput))]
internal static class LiquidTransferEscapePatch
{
    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (Input.GetKeyDown(KeyCode.Escape)
            && MinigameBase.main
            && MinigameBase.main.currentMinigame is LiquidTransferMinigame minigame)
        {
            try
            {
                minigame.CommitTransfer();
            }
            catch (Exception exception)
            {
                Plugin.LogError($"Could not commit the liquid-transfer minigame: {exception}");
                DebugLog.Write($"Could not commit the liquid-transfer minigame: {exception}");
            }

            MinigameBase.main.EndMinigame();
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(MinigameBase), nameof(MinigameBase.EndMinigame))]
internal static class RestoreLiquidTransferHandPatch
{
    [HarmonyPrefix]
    private static void Prefix(MinigameBase __instance)
    {
        if (__instance.currentMinigame is LiquidTransferMinigame minigame)
        {
            minigame.RestoreHand();
        }
    }
}
