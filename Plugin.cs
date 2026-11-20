using BepInEx;
#if LIQUID_TRANSFER_DEBUG_LOGGING
using BepInEx.Logging;
#endif
using HarmonyLib;
using System.Diagnostics;

namespace RealisticLiquidTransfer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.liquidtransfer.mod";
    public const string PluginName = "Realistic Liquid Transfer";
    public const string PluginVersion = "0.1.0";

#if LIQUID_TRANSFER_DEBUG_LOGGING
    internal static ManualLogSource LogSource { get; private set; }
#endif

    private Harmony _harmony;

    private void Awake()
    {
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource = Logger;
#endif
        DebugLog.Initialize(Logger);
        ModSprites.Load();
        try
        {
            LiquidShaderAssets.Load();
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Could not load the liquid shader bundle: {exception}");
        }

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        LogInfo($"{PluginName} {PluginVersion} loaded.");
        DebugLog.Write($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        if (MinigameBase.main && MinigameBase.main.currentMinigame is LiquidTransferMinigame)
        {
            MinigameBase.main.EndMinigame();
        }

        _harmony?.UnpatchSelf();
        LiquidShaderAssets.Unload();
        ModSprites.Unload();
        DebugLog.Close();
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource = null;
#endif
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void LogInfo(string message)
    {
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource?.LogInfo(message);
#endif
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void LogWarning(string message)
    {
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource?.LogWarning(message);
#endif
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void LogError(string message)
    {
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource?.LogError(message);
#endif
    }
}
