using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
#if RLT_TOGETHER_API
using Together;
#endif

namespace RealisticLiquidTransfer;

internal static class TogetherCompatibility
{
    private const string PluginGuid = "CasualtiesMP";

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    internal static void LogSetupStatus()
    {
        if (!Chainloader.PluginInfos.ContainsKey(PluginGuid))
        {
            DebugLog.Write("Together setup: soft dependency is not loaded; typed API access was skipped.");
            return;
        }

#if RLT_TOGETHER_API
        try
        {
            DebugLog.Write($"Together setup: typed API access succeeded ({ReadNetworkState()}).");
        }
        catch (Exception exception)
        {
            DebugLog.Write($"Together setup: typed API probe failed: {exception}");
        }
#else
        DebugLog.Write("Together setup: plugin is loaded, but no compile-time API reference was available.");
#endif
    }

#if RLT_TOGETHER_API
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static string ReadNetworkState()
    {
        return $"Together.Net loaded; running={Net.IsRunning}, server={Net.IsServer}, client={Net.IsClient}";
    }
#endif
}
