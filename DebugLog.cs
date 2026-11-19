#if LIQUID_TRANSFER_DEBUG_LOGGING
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace RealisticLiquidTransfer;

internal static class DebugLog
{
    private static ManualLogSource fallbackLogger;
    private static StreamWriter writer;
    private static string filePath;

    internal static string FilePath => filePath;
    internal static bool IsFileEnabled => writer != null;

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void Initialize(ManualLogSource logger)
    {
        fallbackLogger = logger;
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        filePath = Path.Combine(
            Paths.BepInExRootPath,
            "LogOutput",
            $"RealisticLiquidTransfer-debug-{timestamp}.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(fileStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            Write($"--- Session started: {DateTime.Now:O} ---");
            Write($"Debug log: {filePath}");
        }
        catch (IOException exception)
        {
            fallbackLogger.LogError($"Could not open debug log '{filePath}': {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            fallbackLogger.LogError($"Could not open debug log '{filePath}': {exception}");
        }
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        if (writer == null)
        {
            fallbackLogger?.LogInfo(line);
            return;
        }

        try
        {
            writer.WriteLine(line);
        }
        catch (IOException exception)
        {
            fallbackLogger?.LogError($"Writing to debug log '{filePath}' failed: {exception}");
            CloseWriter();
        }
        catch (ObjectDisposedException exception)
        {
            fallbackLogger?.LogError($"Writing to debug log '{filePath}' failed: {exception}");
            CloseWriter();
        }
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void Close()
    {
        Write("--- Session ended ---");
        CloseWriter();
        fallbackLogger = null;
    }

    private static void CloseWriter()
    {
        writer?.Dispose();
        writer = null;
    }
}
#else
using System.Diagnostics;
using BepInEx.Logging;

namespace RealisticLiquidTransfer;

internal static class DebugLog
{
    internal static bool IsFileEnabled => false;

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void Initialize(ManualLogSource logger)
    {
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void Write(string message)
    {
    }

    [Conditional("LIQUID_TRANSFER_DEBUG_LOGGING")]
    internal static void Close()
    {
    }
}
#endif
