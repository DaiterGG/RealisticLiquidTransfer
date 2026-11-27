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
        var playerBody = PlayerCamera.main ? PlayerCamera.main.body : null;
        var hasFunnel = playerBody && playerBody.FindByIdThorough(FunnelItem.Id, out _);
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
                secondComposition,
                hasFunnel),
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

internal static class UnlockQuickLiquidTransferSetting
{
    internal const string Name = "realliquidtransfer.unlockquickliquidtransfer";
    internal const string Label = "[Liquid] Unlock quick liquid transfer (with Shift)";
    internal const string Description =
        "Allow holding the expand descriptions key while combining containers to transfer liquid immediately.";

    internal static bool IsEnabled
    {
        get
        {
            var setting = Settings.Get<SettingBool>(Name);
            if (setting == null)
            {
                throw new InvalidOperationException(
                    $"The '{Name}' Gameplay setting was not registered.");
            }

            return setting.value;
        }
    }
}

[HarmonyPatch(typeof(Body), nameof(Body.CombineItems))]
internal static class CombineLiquidContainersPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Body __instance, Item it1, Item it2)
    {
        if (!it1
            || !it2
            || !it1.TryGetComponent<WaterContainerItem>(out var target)
            || !it2.TryGetComponent<WaterContainerItem>(out var source)
            || !__instance.CanCombine(it1, it2)
            || target.SpaceLeft <= 0f
            || it1.id == "craftingbottle"
            || (UnlockQuickLiquidTransferSetting.IsEnabled
                && Input.GetKey(KeyBinds.GetBind("expanddesc"))))
        {
            return true;
        }

        if (!PlayerCamera.main)
        {
            Plugin.LogError("Cannot open liquid-transfer minigame because PlayerCamera is unavailable.");
            return false;
        }

        PlayerCamera.main.StartLiquidTransfer(target, source);
        return false;
    }
}

internal static class DrainReleasePrompt
{
    internal const string LocaleKey = "realliquidtransfer.drainrelease";
    internal const string Text = "Release to drain";
}

[HarmonyPatch(typeof(PlayerCamera), "HandleWhileDragging")]
internal static class StartLiquidDrainMinigamePatch
{
    internal static bool TryGetDrainContainer(PlayerCamera camera, out WaterContainerItem container)
    {
        container = null;
        var item = camera.dragItem;
        if (!item
            || !item.TryGetComponent<WaterContainerItem>(out container)
            || container.CurrentTotal <= 0f
            || FluidManager.main.HasLiquid(WorldGeneration.world.WorldToBlockPos(item.transform.position))
            || !camera.body.DoPickupCheck(item, noAlerts: true))
        {
            container = null;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    private static bool Prefix(PlayerCamera __instance, List<RaycastResult> uiCasts)
    {
        foreach (var uiCast in uiCasts)
        {
            if (uiCast.gameObject != __instance.liquidDrainObject)
            {
                continue;
            }

            if (!TryGetDrainContainer(__instance, out var container))
            {
                return true;
            }

            __instance.dragImage.rectTransform.position = Input.mousePosition;
            __instance.dragImage.transform.localScale = Vector2.Lerp(
                __instance.dragImage.transform.localScale,
                Vector2.one,
                Time.unscaledDeltaTime * 10f);
            __instance.radialText.text = Locale.GetOther(DrainReleasePrompt.LocaleKey);
            __instance.radialCircle.color = new Color(0f, 0f, 0f, 0.66f);
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(PlayerCamera), "HandleReleaseDragging")]
internal static class LiquidDrainReleasePatch
{
    [HarmonyPrefix]
    private static bool BeforeReleaseDragging(
        PlayerCamera __instance,
        List<RaycastResult> uiCasts)
    {
        foreach (var uiCast in uiCasts)
        {
            if (uiCast.gameObject != __instance.liquidDrainObject
                || !StartLiquidDrainMinigamePatch.TryGetDrainContainer(__instance, out var container))
            {
                continue;
            }

            if (!MinigameBase.main)
            {
                Plugin.LogError("Cannot open liquid-drain minigame because MinigameBase is unavailable.");
                return true;
            }

            if (MinigameBase.main.currentMinigame)
            {
                return true;
            }

            __instance.dragImage.enabled = false;
            __instance.dragItem = null;
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

internal static class NoLiquidLossSetting
{
    internal const string Name = "realliquidtransfer.noliquidloss";
    internal const string Label = "[Liquid] No liquid loss";
    internal const string Description =
        "Return liquid outside or removed from the minigame to the source container, then the receiver if the source is full.";

    internal static bool IsEnabled
    {
        get
        {
            var setting = Settings.Get<SettingBool>(Name);
            if (setting == null)
            {
                throw new InvalidOperationException(
                    $"The '{Name}' Gameplay setting was not registered.");
            }

            return setting.value;
        }
    }
}

[HarmonyPatch(typeof(Settings), nameof(Settings.DefaultSettings))]
internal static class LiquidTransferSettingsPatch
{
    [HarmonyPostfix]
    private static void Postfix(List<Setting> __result)
    {
        if (__result == null)
        {
            throw new InvalidOperationException("The game returned no default settings list.");
        }

        if (!__result.Exists(setting => setting != null && setting.name == NoLiquidLossSetting.Name))
        {
            __result.Add(new SettingBool
            {
                name = NoLiquidLossSetting.Name,
                value = false,
                category = Setting.SettingCategory.Game
            });
        }

        if (!__result.Exists(
                setting => setting != null && setting.name == UnlockQuickLiquidTransferSetting.Name))
        {
            __result.Add(new SettingBool
            {
                name = UnlockQuickLiquidTransferSetting.Name,
                value = false,
                category = Setting.SettingCategory.Game
            });
        }
    }
}

[HarmonyPatch(typeof(Locale), nameof(Locale.GetOther), typeof(string))]
internal static class LiquidTransferSettingsLocalizationPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref string __result)
    {
        if (string.Equals(__result, DrainReleasePrompt.LocaleKey, StringComparison.Ordinal))
        {
            __result = DrainReleasePrompt.Text;
        }
        else if (string.Equals(__result, "gameset" + NoLiquidLossSetting.Name, StringComparison.Ordinal))
        {
            __result = NoLiquidLossSetting.Label;
        }
        else if (string.Equals(__result, "gameset" + NoLiquidLossSetting.Name + "dsc", StringComparison.Ordinal))
        {
            __result = NoLiquidLossSetting.Description;
        }
        else if (string.Equals(
                     __result,
                     "gameset" + UnlockQuickLiquidTransferSetting.Name,
                     StringComparison.Ordinal))
        {
            __result = UnlockQuickLiquidTransferSetting.Label;
        }
        else if (string.Equals(
                     __result,
                     "gameset" + UnlockQuickLiquidTransferSetting.Name + "dsc",
                     StringComparison.Ordinal))
        {
            __result = UnlockQuickLiquidTransferSetting.Description;
        }
    }
}
