using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;

namespace RealisticLiquidTransfer;

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.StartLiquidTransfer))]
internal static class StartLiquidTransferPatch
{
    private static readonly HashSet<string> SyringeMinigameItemIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "morphine",
            "syringe",
            "opium",
            "heroin",
            "naloxone",
            "fentanyl",
            "ceftriaxone",
            "saline",
            "ringersolution"
        };

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
        var firstComposition = GetContainerComposition(wat1, "first");
        var secondComposition = GetContainerComposition(wat2, "second");
        MinigameBase.main.StartMinigame(
            new LiquidTransferMinigame(
                wat1,
                firstSpriteName,
                firstCapacity,
                firstAmount,
                firstComposition,
                wat2,
                secondSpriteName,
                secondCapacity,
                secondAmount,
                secondComposition),
            null);
        return false;
    }

    internal static float GetContainerAmount(WaterContainerItem container, string containerLabel)
    {
        if (!container)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container amount.");
            return 0f;
        }

        return container.CurrentTotal;
    }

    internal static LiquidProfileEntry[] GetContainerComposition(
        WaterContainerItem container,
        string containerLabel)
    {
        if (!container || container.stack == null || container.stack.Count == 0)
        {
            return Array.Empty<LiquidProfileEntry>();
        }

        var validCount = 0;
        foreach (var stack in container.stack)
        {
            if (stack != null
                && !string.IsNullOrWhiteSpace(stack.liquidId)
                && stack.amount > 0f
                && !float.IsNaN(stack.amount)
                && !float.IsInfinity(stack.amount))
            {
                validCount++;
            }
        }

        var composition = new LiquidProfileEntry[validCount];
        var index = 0;
        foreach (var stack in container.stack)
        {
            if (stack == null
                || string.IsNullOrWhiteSpace(stack.liquidId)
                || stack.amount <= 0f
                || float.IsNaN(stack.amount)
                || float.IsInfinity(stack.amount))
            {
                continue;
            }

            var color = Color.white;
            if (Liquids.Registry == null
                || !Liquids.Registry.TryGetValue(stack.liquidId, out var liquidType)
                || liquidType == null)
            {
                Plugin.LogWarning(
                    $"Could not resolve liquid '{stack.liquidId}' in the {containerLabel} container; "
                    + "using white for its liquid color.");
            }
            else
            {
                color = liquidType.color;
            }

            composition[index++] = new LiquidProfileEntry(stack.liquidId, stack.amount, color);
        }

        return composition;
    }

    internal static float GetContainerCapacity(WaterContainerItem container, string containerLabel)
    {
        if (!container)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container capacity.");
            return 0f;
        }

        return container.Capacity;
    }

    internal static string GetContainerSpriteName(WaterContainerItem container, string containerLabel)
    {
        if (!container)
        {
            Plugin.LogWarning($"Could not read the {containerLabel} liquid-transfer container sprite: container is unavailable.");
            return "Unavailable";
        }

        if (container.TryGetComponent<Item>(out var item)
            && SyringeMinigameItemIds.Contains(item.id))
        {
            return "syringe";
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

[HarmonyPatch(typeof(PlayerCamera), "HandleWhileDragging")]
internal static class StartLiquidDrainMinigamePatch
{
    internal static bool BlockDraggingUntilRelease;

    [HarmonyPrefix]
    private static bool Prefix(PlayerCamera __instance, List<RaycastResult> uiCasts)
    {
        if (BlockDraggingUntilRelease)
        {
            return false;
        }

        foreach (var uiCast in uiCasts)
        {
            if (uiCast.gameObject != __instance.liquidDrainObject)
            {
                continue;
            }

            var item = __instance.dragItem;
            if (!item
                || !item.TryGetComponent<WaterContainerItem>(out var container)
                || container.CurrentTotal <= 0f
                || FluidManager.main.HasLiquid(WorldGeneration.world.WorldToBlockPos(item.transform.position))
                || !__instance.body.DoPickupCheck(item, noAlerts: true))
            {
                return true;
            }

            if (!MinigameBase.main)
            {
                Plugin.LogError("Cannot open liquid-drain minigame because MinigameBase is unavailable.");
                return false;
            }

            if (MinigameBase.main.currentMinigame)
            {
                return false;
            }

            BlockDraggingUntilRelease = true;
            __instance.dragImage.enabled = false;
            MinigameBase.main.StartMinigame(
                new LiquidTransferMinigame(
                    container,
                    StartLiquidTransferPatch.GetContainerSpriteName(container, "drain"),
                    StartLiquidTransferPatch.GetContainerCapacity(container, "drain"),
                    StartLiquidTransferPatch.GetContainerAmount(container, "drain"),
                    StartLiquidTransferPatch.GetContainerComposition(container, "drain")),
                null);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(PlayerCamera), "HandleReleaseDragging")]
internal static class LiquidDrainReleasePatch
{
    [HarmonyPrefix]
    private static bool BeforeReleaseDragging(PlayerCamera __instance)
    {
        if (!StartLiquidDrainMinigamePatch.BlockDraggingUntilRelease)
        {
            return true;
        }

        StartLiquidDrainMinigamePatch.BlockDraggingUntilRelease = false;
        __instance.dragItem = null;
        __instance.dragImage.enabled = false;
        return false;
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
