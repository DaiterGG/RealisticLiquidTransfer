using HarmonyLib;
using UnityEngine;

namespace RealisticLiquidTransfer;

[HarmonyPatch(typeof(PlayerCamera), nameof(PlayerCamera.StartLiquidTransfer))]
internal static class StartLiquidTransferPatch
{
    [HarmonyPrefix]
    private static bool Prefix()
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

        MinigameBase.main.StartMinigame(new LiquidTransferMinigame(), null);
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
            && MinigameBase.main.currentMinigame is LiquidTransferMinigame)
        {
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
