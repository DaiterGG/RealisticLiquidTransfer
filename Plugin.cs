using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace RealisticLiquidTransfer;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.liquidtransfer.mod";
    public const string PluginName = "Realistic Liquid Transfer";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource LogSource { get; private set; }

    private Harmony _harmony;

    private void Awake()
    {
        LogSource = Logger;
        ModSprites.Load();
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        if (MinigameBase.main && MinigameBase.main.currentMinigame is LiquidTransferMinigame)
        {
            MinigameBase.main.EndMinigame();
        }

        _harmony?.UnpatchSelf();
        ModSprites.Unload();
        LogSource = null;
    }
}
