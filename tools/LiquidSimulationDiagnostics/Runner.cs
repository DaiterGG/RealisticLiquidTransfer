using System;
using UnityEditor;
using UnityEngine;

namespace RealisticLiquidTransfer
{
public static class LiquidSimulationDiagnosticsRunner
{
    public static void Run()
    {
        var scenario = GetArgument("-scenario");
        var logPath = GetArgument("-diagnosticLog");
        if (string.IsNullOrWhiteSpace(scenario) || string.IsNullOrWhiteSpace(logPath))
        {
            EditorApplication.Exit(2);
            return;
        }

        throw new InvalidOperationException(
            "The diagnostics system is installed, but no scenario has been registered.");
    }

    private static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
}
