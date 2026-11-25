using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RealisticLiquidTransfer
{
public static class LiquidSimulationDiagnosticsRunner
{
    private const float FixedStep = 1f / 30f;
    private const int SimulationFrames = 900;
    private const int SettlingWindowFrames = 30;
    private const float WaterBottleCapacity = 500f;
    private const float ReferenceCapacity = 250f;
    private const float BaseContainerScale = 4f;
    private const float MaximumSettledSpeed = 8f;
    private static StreamWriter log;

    public static void Run()
    {
        GameObject screenObject = null;
        var exitCode = 0;
        try
        {
            var scenario = GetArgument("-scenario");
            var logPath = GetArgument("-diagnosticLog");
            if (scenario != "water-bottle-full-idle" || string.IsNullOrWhiteSpace(logPath))
            {
                throw new ArgumentException("Expected scenario and diagnostic-log arguments.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            log = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false))
            {
                AutoFlush = true
            };
            DebugLog.Sink = WriteLine;
            Plugin.LogSink = WriteLine;
            WriteLine($"Scenario '{scenario}' started at {DateTime.Now:O}.");

            if (!GeneratedContainerBoundaryCatalog.TryGet("coffee", out var referenceBoundary)
                || !GeneratedContainerBoundaryCatalog.TryGet("waterbottle", out var bottleBoundary)
                || !referenceBoundary.HasOpening
                || !bottleBoundary.HasOpening)
            {
                throw new InvalidOperationException("Generated coffee or water-bottle boundaries are missing.");
            }

            var referenceArea = EstimateArea(
                referenceBoundary.WallCenterlinePoints,
                referenceBoundary.InteriorWalls);
            var bottleArea = EstimateArea(
                bottleBoundary.WallCenterlinePoints,
                bottleBoundary.InteriorWalls);
            if (referenceArea <= 0d || bottleArea <= 0d)
            {
                throw new InvalidOperationException("Could not calculate gameplay container areas.");
            }

            var bottleScale = BaseContainerScale
                * (float)Math.Sqrt(
                    (WaterBottleCapacity / ReferenceCapacity)
                    * (referenceArea / bottleArea));

            screenObject = new GameObject("LiquidDiagnosticsScreen", typeof(RectTransform));
            var screen = screenObject.GetComponent<RectTransform>();
            ConfigureRect(screen, null, new Vector2(1024f, 1024f), Vector2.zero);
            var rootObject = new GameObject("LiquidDiagnosticsRoot", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            ConfigureRect(root, screen, new Vector2(1024f, 1024f), Vector2.zero);
            var receiver = CreateContainer(
                root,
                "EmptyReceiver",
                new Vector2(1000f, 0f),
                new Vector2(100f, 100f));
            var source = CreateContainer(
                root,
                "FullWaterBottle",
                Vector2.zero,
                new Vector2(80f, 156f));
            source.localScale = Vector3.one * bottleScale;

            var water = new[]
            {
                new LiquidProfileEntry("water", WaterBottleCapacity, new Color(0.16f, 0.5f, 0.95f))
            };
            var simulation = new LiquidSimulation(
                root,
                receiver,
                CreateOpenBoxOutline(50f, 50f),
                Array.Empty<Vector2[]>(),
                250f,
                source,
                bottleBoundary.WallCenterlinePoints,
                bottleBoundary.InteriorWalls,
                WaterBottleCapacity,
                0f,
                WaterBottleCapacity,
                Array.Empty<LiquidProfileEntry>(),
                water,
                LiquidSimulation.DefaultPackingTenths / 100f,
                headless: true);

            var initialDrops = simulation.CaptureDropSnapshots();
            if (simulation.GetInitialContainerDropCount(0) != 0
                || simulation.GetInitialContainerDropCount(1) != WaterBottleCapacity)
            {
                throw new InvalidOperationException(
                    $"Fixture did not seed 500 ml into the bottle; seeded "
                    + $"{simulation.GetInitialContainerDropCount(1)} units.");
            }

            WriteLine(
                $"Fixture: full 500ml water bottle, scale={bottleScale:F3}, "
                + $"particles={initialDrops.Length}, initial units={simulation.Count}.");
            WriteLine(
                $"Simulation: fixedStep={FixedStep:F6}s, duration={SimulationFrames * FixedStep:F1}s, "
                + $"settling window={SettlingWindowFrames * FixedStep:F1}s.");

            var framesWithEscapedDrops = 0;
            var framesWithOwnershipMismatch = 0;
            var framesWithUnitMismatch = 0;
            var maximumEscapedUnits = 0;
            var maximumSettlingWindowSpeed = 0f;
            var finalMaximumSpeed = 0f;
            for (var frame = 0; frame <= SimulationFrames; frame++)
            {
                if (frame > 0)
                {
                    simulation.Tick(FixedStep, false, Vector2.zero);
                }

                var drops = simulation.CaptureDropSnapshots();
                var escapedUnits = 0;
                var ownershipMismatches = 0;
                var frameUnits = 0;
                var frameMaximumSpeed = 0f;
                foreach (var drop in drops)
                {
                    frameUnits += drop.Volume;
                    frameMaximumSpeed = Mathf.Max(frameMaximumSpeed, drop.Velocity.magnitude);
                    if (!simulation.ContainsContainer(1, drop.Position))
                    {
                        escapedUnits += drop.Volume;
                    }

                    if (drop.ContainerAssignment != 2)
                    {
                        ownershipMismatches += drop.Volume;
                    }
                }

                if (escapedUnits > 0)
                {
                    framesWithEscapedDrops++;
                    maximumEscapedUnits = Mathf.Max(maximumEscapedUnits, escapedUnits);
                }

                if (ownershipMismatches > 0)
                {
                    framesWithOwnershipMismatch++;
                }

                if (frameUnits != WaterBottleCapacity || simulation.Count != WaterBottleCapacity)
                {
                    framesWithUnitMismatch++;
                }

                if (frame >= SimulationFrames - SettlingWindowFrames)
                {
                    maximumSettlingWindowSpeed = Mathf.Max(
                        maximumSettlingWindowSpeed,
                        frameMaximumSpeed);
                }

                if (frame == SimulationFrames)
                {
                    finalMaximumSpeed = frameMaximumSpeed;
                }

                if (frame % 30 == 0 || frame == SimulationFrames)
                {
                    WriteLine(
                        $"frame={frame} t={frame * FixedStep:F1}s units={frameUnits}/500, "
                        + $"particles={drops.Length}, escapedUnits={escapedUnits}, "
                        + $"maxSpeed={frameMaximumSpeed:F3}px/s.");
                }
            }

            WriteLine(
                $"Assertion summary: escapedFrames={framesWithEscapedDrops}, "
                + $"maxEscapedUnits={maximumEscapedUnits}, "
                + $"ownershipMismatchFrames={framesWithOwnershipMismatch}, "
                + $"unitMismatchFrames={framesWithUnitMismatch}, "
                + $"settlingWindowMaxSpeed={maximumSettlingWindowSpeed:F3}px/s, "
                + $"finalMaxSpeed={finalMaximumSpeed:F3}px/s.");

            if (framesWithEscapedDrops > 0
                || framesWithOwnershipMismatch > 0
                || framesWithUnitMismatch > 0)
            {
                throw new InvalidOperationException(
                    "Liquid escaped the bottle, changed accounting owner, or the simulation lost units.");
            }

            if (finalMaximumSpeed > MaximumSettledSpeed
                || maximumSettlingWindowSpeed > MaximumSettledSpeed)
            {
                throw new InvalidOperationException(
                    $"Drops did not settle below {MaximumSettledSpeed:F1}px/s; "
                    + $"final={finalMaximumSpeed:F3}px/s, "
                    + $"last-second maximum={maximumSettlingWindowSpeed:F3}px/s.");
            }

            WriteLine("PASS: full water bottle retained all liquid and settled.");
        }
        catch (Exception exception)
        {
            WriteLine($"FAIL: {exception}");
            exitCode = 1;
        }
        finally
        {
            DebugLog.Sink = null;
            Plugin.LogSink = null;
            if (screenObject)
            {
                UnityEngine.Object.DestroyImmediate(screenObject);
            }

            if (log != null)
            {
                log.Flush();
                log.Dispose();
                log = null;
            }
        }

        EditorApplication.Exit(exitCode);
    }

    private static double EstimateArea(Vector2[] outline, Vector2[][] interiorWalls)
    {
        var area = EstimatePolygonArea(outline);
        foreach (var wall in interiorWalls)
        {
            area -= EstimatePolygonArea(wall);
        }

        return Math.Max(0d, area);
    }

    private static double EstimatePolygonArea(Vector2[] points)
    {
        if (points == null || points.Length < 3)
        {
            return 0d;
        }

        var area = 0d;
        for (var index = 0; index < points.Length; index++)
        {
            var next = points[(index + 1) % points.Length];
            area += points[index].x * next.y - next.x * points[index].y;
        }

        return Math.Abs(area) * 0.5d;
    }

    private static RectTransform CreateContainer(
        RectTransform parent,
        string name,
        Vector2 position,
        Vector2 size)
    {
        var containerObject = new GameObject(name, typeof(RectTransform));
        var container = containerObject.GetComponent<RectTransform>();
        ConfigureRect(container, parent, size, position);
        return container;
    }

    private static void ConfigureRect(
        RectTransform rect,
        RectTransform parent,
        Vector2 size,
        Vector2 position)
    {
        if (parent)
        {
            rect.SetParent(parent, false);
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static Vector2[] CreateOpenBoxOutline(float halfWidth, float halfHeight)
    {
        return new[]
        {
            new Vector2(-halfWidth, halfHeight),
            new Vector2(-halfWidth, -halfHeight),
            new Vector2(halfWidth, -halfHeight),
            new Vector2(halfWidth, halfHeight)
        };
    }

    private static string GetArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void WriteLine(string message)
    {
        if (log == null)
        {
            Debug.Log(message);
            return;
        }

        log.WriteLine(message);
    }
}

internal static class DebugLog
{
    internal static Action<string> Sink { get; set; }
    internal static bool IsFileEnabled => false;

    internal static void Write(string message)
    {
        Sink?.Invoke(message);
    }
}

internal static class Plugin
{
    internal static Action<string> LogSink { get; set; }

    internal static void LogWarning(string message)
    {
        LogSink?.Invoke(message);
    }
}

internal static class LiquidShaderAssets
{
    internal static Material SurfaceMaterial => null;
    internal static ComputeShader DensityComputeShader => null;
}
}
