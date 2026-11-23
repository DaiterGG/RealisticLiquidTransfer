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

    private static Plugin instance;

#if LIQUID_TRANSFER_DEBUG_LOGGING
    internal static ManualLogSource LogSource { get; private set; }
#endif

    private Harmony _harmony;
    private float nextMultiplayerBridgeCheck;

    private void Awake()
    {
        instance = this;
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource = Logger;
#endif
        DebugLog.Initialize(Logger);
        ModSprites.Load();
        DebugLog.Write("Container sprites loaded.");
        try
        {
            LiquidShaderAssets.Load();
            DebugLog.Write("Liquid shader assets loaded.");
        }
        catch (System.Exception exception)
        {
            Logger.LogError($"Could not load the liquid shader bundle: {exception}");
            DebugLog.Write($"Could not load the liquid shader bundle: {exception}");
        }

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        DebugLog.Write($"Harmony patches applied for {PluginGuid}.");
        MultiplayerTransferBridge.InitializeIfAvailable();
        LogInfo($"{PluginName} {PluginVersion} loaded.");
        DebugLog.Write($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (UnityEngine.Time.unscaledTime < nextMultiplayerBridgeCheck)
        {
            return;
        }

        nextMultiplayerBridgeCheck = UnityEngine.Time.unscaledTime + 1f;
#if LIQUID_TRANSFER_DEBUG_LOGGING
        MultiplayerTransferBridge.GetRoute();
#else
        MultiplayerTransferBridge.InitializeIfAvailable();
#endif
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
        if (instance == this)
        {
            instance = null;
        }
#if LIQUID_TRANSFER_DEBUG_LOGGING
        LogSource = null;
#endif
    }

    internal static void ReportNetworkWarning(string message)
    {
        DebugLog.Write($"Network warning: {message}");
        instance?.Logger.LogWarning(message);
    }

    internal static void ReportNetworkError(string message)
    {
        DebugLog.Write($"Network error: {message}");
        instance?.Logger.LogError(message);
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
