using System;
using System.Diagnostics;

namespace RealisticLiquidTransfer;

internal enum SimulationProfileStage
{
    Spawning,
    GeometryRefreshAndIntegration,
    OffscreenCleanup,
    SpatialGrid,
    DropCollisions,
    VelocityReconstruction,
    VelocityConstraints,
    AssignmentChecks,
    BottomDropLogging,
    RenderScheduling,
    MeshGeneration,
    Count
}

internal static class SimulationProfiler
{
#if LIQUID_TRANSFER_DEBUG_LOGGING
    private static readonly long[] totalTicks = new long[(int)SimulationProfileStage.Count];
    private static readonly long[] maximumTicks = new long[(int)SimulationProfileStage.Count];
    private static readonly int[] sampleCounts = new int[(int)SimulationProfileStage.Count];

    internal static long Begin()
    {
        return Stopwatch.GetTimestamp();
    }

    internal static void End(SimulationProfileStage stage, long start)
    {
        var index = (int)stage;
        var elapsed = Stopwatch.GetTimestamp() - start;
        totalTicks[index] += elapsed;
        maximumTicks[index] = Math.Max(maximumTicks[index], elapsed);
        sampleCounts[index]++;
    }

    internal static void Report(int dropCount)
    {
        var slowestStage = SimulationProfileStage.Count;
        var slowestTicks = 0L;
        var report = new System.Text.StringBuilder();
        for (var i = 0; i < (int)SimulationProfileStage.Count; i++)
        {
            if (sampleCounts[i] == 0)
            {
                continue;
            }

            if (report.Length > 0)
            {
                report.Append(", ");
            }

            var totalMilliseconds = TicksToMilliseconds(totalTicks[i]);
            var maximumMilliseconds = TicksToMilliseconds(maximumTicks[i]);
            report.Append(((SimulationProfileStage)i).ToString());
            report.Append('=');
            report.Append(totalMilliseconds.ToString("F2"));
            report.Append("ms total/");
            report.Append(maximumMilliseconds.ToString("F2"));
            report.Append("ms max");
            if (totalTicks[i] > slowestTicks)
            {
                slowestTicks = totalTicks[i];
                slowestStage = (SimulationProfileStage)i;
            }
        }

        if (slowestStage != SimulationProfileStage.Count)
        {
            DebugLog.Write(
                $"Profile drops={dropCount}: slowest={slowestStage} "
                + $"{TicksToMilliseconds(slowestTicks):F2}ms total; {report}.");
        }

        Array.Clear(totalTicks, 0, totalTicks.Length);
        Array.Clear(maximumTicks, 0, maximumTicks.Length);
        Array.Clear(sampleCounts, 0, sampleCounts.Length);
    }

    private static double TicksToMilliseconds(long ticks)
    {
        return ticks * 1000d / Stopwatch.Frequency;
    }
#else
    internal static long Begin()
    {
        return 0L;
    }

    internal static void End(SimulationProfileStage stage, long start)
    {
    }

    internal static void Report(int dropCount)
    {
    }
#endif
}
