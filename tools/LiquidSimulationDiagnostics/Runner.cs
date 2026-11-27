using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RealisticLiquidTransfer
{
public static class LiquidSimulationDiagnosticsRunner
{
    private const int GameUpdatesPerSecond = 60;
    private const int SimulationDurationSeconds = 30;
    private const float UpdateDeltaTime = 1f / GameUpdatesPerSecond;
    private const int SimulationFrames = SimulationDurationSeconds * GameUpdatesPerSecond;
    private const int SettlingWindowFrames = GameUpdatesPerSecond;
    private const float WaterBottleCapacity = 500f;
    private const float MiniBarrelCapacity = 5000f;
    private const float ReferenceCapacity = 250f;
    private const float BottleLoweringDurationSeconds = 8f;
    private const float MaximumSettledSpeed = 8f;
    private const float FunnelMaximumFrameMovement = 30f;
    private const float FunnelMaximumSourceRelativeDrift = 120f;
    private const float FunnelMaximumTrackedSpeed = 900f;
    private const float FunnelMinimumWallClearance = -0.02f;
    private const float CenterBottleSyringeCapacity = 100f;
    private const float CenterBottleSyringeIntakeRate = 25.2f;
    private const float CenterBottleSyringePlungerRate = CenterBottleSyringeIntakeRate * 0.8f;
    private const float CenterBottleSyringeScaleMultiplier = 1.2f;
    private const float CenterBottleSyringeSourceDistanceMultiplier = 4f;
    private const byte MinimumVisibleAlpha = 32;
    private const string SingleDropScenario = "water-bottle-single-drop";
    private const string FunnelWallSingleDropScenario = "funnel-wall-single-drop";
    private const string SyringeCenterBottleScenario = "syringe-center-bottle-intake";
    private const string WaterBottleIntoMiniBarrelScenario = "water-bottle-into-minibarrel";
    private static StreamWriter log;

    public static void Run()
    {
        GameObject screenObject = null;
        var exitCode = 0;
        try
        {
            var scenario = GetArgument("-scenario");
            if (scenario == WaterBottleIntoMiniBarrelScenario)
            {
                RunWaterBottleIntoMiniBarrelScenario();
                return;
            }

            if (scenario == SyringeCenterBottleScenario)
            {
                RunSyringeCenterBottleIntakeScenario();
                return;
            }

            var logPath = GetArgument("-diagnosticLog");
            var repositoryRoot = GetArgument("-repositoryRoot");
            var funnelWallScenario = scenario == FunnelWallSingleDropScenario;
            var singleDropScenario = scenario == SingleDropScenario || funnelWallScenario;
            if ((scenario != "water-bottle-full-idle"
                    && scenario != SingleDropScenario
                    && !funnelWallScenario)
                || string.IsNullOrWhiteSpace(logPath)
                || string.IsNullOrWhiteSpace(repositoryRoot))
            {
                throw new ArgumentException(
                    "Expected scenario, diagnostic-log, and repository-root arguments.");
            }

            void RunSyringeCenterBottleIntakeScenario()
            {
                GameObject screenObject = null;
                var exitCode = 0;
                try
                {
                    var logPath = GetArgument("-diagnosticLog");
                    var repositoryRoot = GetArgument("-repositoryRoot");
                    if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(repositoryRoot))
                    {
                        throw new ArgumentException("Expected diagnostic-log and repository-root arguments.");
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    log = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false))
                    {
                        AutoFlush = true
                    };
                    DebugLog.Sink = WriteLine;
                    Plugin.LogSink = WriteLine;
                    WriteLine($"Scenario '{SyringeCenterBottleScenario}' started at {DateTime.Now:O}.");

                    if (!GeneratedContainerBoundaryCatalog.TryGet("coffee", out var referenceBoundary)
                        || !GeneratedContainerBoundaryCatalog.TryGet("waterbottle", out var bottleBoundary)
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

                    var screenPixelSize = new Vector2(Screen.width, Screen.height);
                    var uiScale = screenPixelSize.y / 1080f;
                    if (screenPixelSize.x <= 0f || screenPixelSize.y <= 0f || uiScale <= 0f)
                    {
                        throw new InvalidOperationException($"Invalid Unity screen dimensions: {screenPixelSize}.");
                    }

                    var viewportSize = screenPixelSize / uiScale;
                    screenObject = new GameObject("SyringeIntakeDiagnosticsCanvas", typeof(RectTransform));
                    var canvasRoot = screenObject.GetComponent<RectTransform>();
                    ConfigureRect(canvasRoot, null, viewportSize, Vector2.zero);
                    canvasRoot.localScale = Vector3.one * uiScale;
                    var screenGameObject = new GameObject("SyringeIntakeDiagnosticsScreen", typeof(RectTransform));
                    var screen = screenGameObject.GetComponent<RectTransform>();
                    ConfigureRect(screen, canvasRoot, viewportSize, Vector2.zero);
                    var rootObject = new GameObject("SyringeIntakeDiagnosticsRoot", typeof(RectTransform));
                    var root = rootObject.GetComponent<RectTransform>();
                    ConfigureRect(root, screen, viewportSize, Vector2.zero);

                    var bottleSpriteSize = LoadVisibleSpriteSize(
                        Path.Combine(repositoryRoot, "resources", "sprites", "containers", "waterbottle.png"));
                    var syringeSpriteSize = new Vector2(30f, 141f);
                    var syringe = CreateContainer(root, "Syringe", syringeSpriteSize);
                    var bottle = CreateContainer(root, "WaterBottle", bottleSpriteSize);
                    var bottleScale = LiquidContainerLayout.TryGetAreaScaledScale(
                        WaterBottleCapacity,
                        ReferenceCapacity,
                        referenceArea,
                        bottleArea,
                        out var calculatedBottleScale)
                        ? calculatedBottleScale
                        : throw new InvalidOperationException("Could not calculate the water-bottle scale.");
                    var syringeScale = LiquidContainerLayout.ContainerScale * CenterBottleSyringeScaleMultiplier;
                    syringe.localScale = Vector3.one * syringeScale;
                    bottle.localScale = Vector3.one * bottleScale;
                    bottle.anchoredPosition = Vector2.zero;

                    Vector2 SyringeFromTop(float x, float y)
                    {
                        return new Vector2(
                            x - syringeSpriteSize.x * 0.5f,
                            syringeSpriteSize.y * 0.5f - y);
                    }

                    var syringeFluidBottom = SyringeFromTop(6f, 87f).y;
                    var syringeFluidTop = SyringeFromTop(6f, 12f).y;
                    var syringeNeedle = SyringeFromTop(15f, 140f);
                    var syringeOutline = new[]
                    {
                        SyringeFromTop(6f, 12f),
                        SyringeFromTop(24f, 12f),
                        SyringeFromTop(24f, 87f),
                        SyringeFromTop(6f, 87f),
                        SyringeFromTop(6f, 12f)
                    };
                    var syringeCollisionOutline = new[]
                    {
                        SyringeFromTop(2f, 8f),
                        SyringeFromTop(28f, 8f),
                        SyringeFromTop(26f, 87f),
                        SyringeFromTop(4f, 87f),
                        SyringeFromTop(2f, 8f)
                    };

                    var bottleBounds = GetBounds(bottleBoundary.WallCenterlinePoints);
                    var bottleCenter = (Vector2)root.InverseTransformPoint(
                        bottle.TransformPoint(bottleBounds.center));
                    var bottleCollisionBounds = GetBounds(bottleBoundary.CollisionPoints);
                    var bottleTop = root.InverseTransformPoint(
                        bottle.TransformPoint(new Vector2(bottleBounds.center.x, bottleCollisionBounds.yMax))).y;
                    var syringeBodyBottom = SyringeFromTop(15f, 87f).y;
                    var barrelClearance = 0.1f;
                    var syringePositionY = bottleTop + barrelClearance - syringeBodyBottom * syringeScale;
                    syringe.anchoredPosition = new Vector2(
                        bottleCenter.x - syringeNeedle.x * syringeScale,
                        syringePositionY);
                    var fitScale = LiquidContainerLayout.GetFitScale(screen, root, syringe, bottle);
                    root.localScale = Vector3.one * fitScale;
                    var needlePoint = syringe.anchoredPosition
                        + Vector2.Scale(syringeNeedle, new Vector2(syringeScale, syringeScale));
                    var needleOffsetFromBottleCenterX = Mathf.Abs(needlePoint.x - bottleCenter.x);
                    var syringeBarrelOverlapsBottle = ContainerShapeCollision.Overlaps(
                        syringeCollisionOutline,
                        syringe.anchoredPosition,
                        syringeScale,
                        0f,
                        bottleBoundary.CollisionPoints,
                        bottle.anchoredPosition,
                        bottleScale,
                        0f);

                    var water = new[]
                    {
                        new LiquidProfileEntry("water", WaterBottleCapacity, new Color(0.16f, 0.5f, 0.95f))
                    };
                    var simulation = new LiquidSimulation(
                        root,
                        syringe,
                        syringeOutline,
                        Array.Empty<Vector2[]>(),
                        CenterBottleSyringeCapacity,
                        bottle,
                        bottleBoundary.WallCenterlinePoints,
                        bottleBoundary.InteriorWalls,
                        WaterBottleCapacity,
                        0f,
                        WaterBottleCapacity,
                        Array.Empty<LiquidProfileEntry>(),
                        water,
                        LiquidSimulation.DefaultPackingTenths / 100f,
                        headless: true);
                    simulation.SetSyringePistonPosition(0, syringeFluidBottom);
                    simulation.ConfigureForMinigame(false);

                    var needleInsideBottle = simulation.ContainsContainer(1, needlePoint);
                    var sourceNearNeedle = simulation.HasContainerDropNear(
                        1,
                        needlePoint,
                        LiquidSimulation.DefaultDropRadius * 3f);
                    var initialDrops = simulation.CaptureDropSnapshots();
                    var liquidSurfaceY = float.NegativeInfinity;
                    foreach (var drop in initialDrops)
                    {
                        if (simulation.ContainsContainer(1, drop.Position))
                        {
                            liquidSurfaceY = Mathf.Max(liquidSurfaceY, drop.Position.y);
                        }
                    }

                    var needleDepthBelowSurface = liquidSurfaceY - needlePoint.y;
                    WriteLine(
                        $"Fixture: syringe capacity={CenterBottleSyringeCapacity:F0}ml, "
                        + $"stroke=({syringeFluidBottom:F2},"
                        + $"{syringeFluidTop:F2}), scale={syringeScale:F3}; "
                        + $"bottle={WaterBottleCapacity:F0}ml, scale={bottleScale:F3}; "
                        + $"needle=({needlePoint.x:F3},{needlePoint.y:F3}), "
                        + $"bottleCenter=({bottleCenter.x:F3},{bottleCenter.y:F3}), "
                        + $"horizontalCenterOffset={needleOffsetFromBottleCenterX:F6}, "
                        + $"depthBelowLiquidSurface={needleDepthBelowSurface:F2}, "
                        + $"insideBottle={needleInsideBottle}, sourceNearNeedle={sourceNearNeedle}, "
                        + $"barrelOverlapsBottle={syringeBarrelOverlapsBottle}, "
                        + $"barrelClearance={barrelClearance:F2}.");
                    WriteLine(
                        $"Simulation: updates={GameUpdatesPerSecond}Hz, duration={SimulationDurationSeconds}s, "
                        + $"liftRate={CenterBottleSyringePlungerRate:F2}ml/s, "
                        + $"intakeRate={CenterBottleSyringeIntakeRate:F2}ml/s, expected lift time="
                        + $"{CenterBottleSyringeCapacity / CenterBottleSyringePlungerRate:F3}s, "
                        + $"seeded source amount={simulation.GetContainerLiquidAmount(1):F1}ml.");
                    if (needleOffsetFromBottleCenterX > 0.001f
                        || needleDepthBelowSurface < 2f * LiquidSimulation.DefaultDropRadius
                        || !needleInsideBottle
                        || !sourceNearNeedle
                        || syringeBarrelOverlapsBottle)
                    {
                        throw new InvalidOperationException(
                            "Syringe placement did not put the needle deep in the liquid without barrel overlap.");
                    }

                    var requests = new List<float>();
                    var movementDeltaTime = Mathf.Min(UpdateDeltaTime, 1f / 30f);
                    var strokeHeight = syringeFluidTop - syringeFluidBottom;
                    var maximumUpwardSpeed = Mathf.Min(
                        420f,
                        strokeHeight * CenterBottleSyringePlungerRate / CenterBottleSyringeCapacity);
                    var handleY = syringeFluidBottom;
                    var lastRequestedAmount = 0f;
                    var intakeCredit = 0f;
                    var totalTransferred = 0f;
                    var framesWithRequests = 0;
                    var framesWithTransfer = 0;
                    var framesWithSourceNearNeedle = 0;
                    var framesWithRequestsButNoTransfer = 0;
                    var liftFrames = 0;
                    var consecutiveLiftFramesWithoutTransfer = 0;
                    var longestLiftGapWithoutTransfer = 0;
                    var amountAtTop = -1f;
                    var firstFullFrame = -1;
                    var maximumSyringeAmount = 0f;
                    var maximumUnaccountedVolume = 0f;
                    var firstUnaccountedFrame = -1;
                    var transferredDropIds = new HashSet<int>();
                    var endedTransferredDropIds = new HashSet<int>();
                    var syringeOwnedDropsOutside = new HashSet<int>();
                    for (var frame = 0; frame <= SimulationFrames; frame++)
                    {
                        var liftingThisFrame = frame > 0 && handleY < syringeFluidTop;
                        if (frame > 0)
                        {
                            if (handleY < syringeFluidTop)
                            {
                                handleY = Mathf.Min(
                                    syringeFluidTop,
                                    handleY + maximumUpwardSpeed * movementDeltaTime);
                            }

                            simulation.SetSyringePistonPosition(0, handleY);
                            var requestedAmount = Mathf.InverseLerp(
                                syringeFluidBottom,
                                syringeFluidTop,
                                handleY) * CenterBottleSyringeCapacity;
                            var requestedDelta = requestedAmount - lastRequestedAmount;
                            if (requestedDelta > 0f)
                            {
                                requests.Add(requestedDelta);
                                framesWithRequests++;
                                liftFrames++;
                            }

                            lastRequestedAmount = requestedAmount;
                        }

                        var pendingVolume = 0f;
                        foreach (var request in requests)
                        {
                            pendingVolume += request;
                        }

                        var currentAmount = simulation.GetContainerLiquidAmount(0);
                        var remainingCapacity = Mathf.Max(0f, CenterBottleSyringeCapacity - currentAmount);
                        var allowedPendingVolume = Mathf.Min(pendingVolume, remainingCapacity);
                        var sourceNearThisFrame = simulation.HasContainerDropNear(
                            1,
                            needlePoint,
                            LiquidSimulation.DefaultDropRadius * CenterBottleSyringeSourceDistanceMultiplier);
                        if (sourceNearThisFrame)
                        {
                            framesWithSourceNearNeedle++;
                        }

                        var frameTransferred = 0;
                        if (allowedPendingVolume <= 0f)
                        {
                            intakeCredit = 0f;
                        }
                        else
                        {
                            intakeCredit = Mathf.Min(
                                intakeCredit + CenterBottleSyringeIntakeRate * movementDeltaTime,
                                allowedPendingVolume);
                            var remainingUnits = Mathf.Min(
                                Mathf.FloorToInt(allowedPendingVolume + 0.001f),
                                Mathf.FloorToInt(intakeCredit + 0.001f));
                            if (remainingUnits > 0)
                            {
                                var localDropRadius = LiquidSimulation.DefaultDropRadius / syringeScale;
                                var fillRatio = Mathf.Clamp01(currentAmount / CenterBottleSyringeCapacity);
                                var surfaceY = Mathf.Lerp(syringeFluidBottom, syringeFluidTop, fillRatio);
                                var destinationY = Mathf.Clamp(
                                    Mathf.Min(surfaceY + localDropRadius, handleY - localDropRadius),
                                    syringeFluidBottom + localDropRadius,
                                    syringeFluidTop - localDropRadius);
                                var destinationPoint = syringe.anchoredPosition
                                    + Vector2.up * destinationY * syringeScale;
                                while (remainingUnits > 0)
                                {
                                    var beforeDrops = simulation.CaptureDropSnapshots();
                                    var beforeOwners = new Dictionary<int, byte>();
                                    foreach (var drop in beforeDrops)
                                    {
                                        beforeOwners[drop.Id] = drop.ContainerAssignment;
                                    }

                                    var moved = simulation.TryTransferNearestDrop(
                                        1,
                                        0,
                                        needlePoint,
                                        LiquidSimulation.DefaultDropRadius
                                            * CenterBottleSyringeSourceDistanceMultiplier,
                                        destinationPoint,
                                        remainingUnits,
                                        Vector2.zero);
                                    if (moved <= 0)
                                    {
                                        break;
                                    }

                                    var afterDrops = simulation.CaptureDropSnapshots();
                                    foreach (var drop in afterDrops)
                                    {
                                        if (drop.ContainerAssignment != 1
                                            || beforeOwners.TryGetValue(drop.Id, out var previousOwner)
                                                && previousOwner == 1
                                            || !transferredDropIds.Add(drop.Id))
                                        {
                                            continue;
                                        }

                                        var insideSyringe = simulation.ContainsContainer(0, drop.Position);
                                        var insideBottle = simulation.ContainsContainer(1, drop.Position);
                                        var syringeLocal = GetSourceLocalPosition(root, syringe, drop.Position);
                                        WriteLine(
                                            $"transferDrop id={drop.Id} volume={drop.Volume} "
                                            + $"owner={drop.ContainerAssignment} "
                                            + $"insideSyringe={insideSyringe} insideBottle={insideBottle} "
                                            + $"position=({drop.Position.x:F3},{drop.Position.y:F3}) "
                                            + $"velocity=({drop.Velocity.x:F3},{drop.Velocity.y:F3}) "
                                            + $"syringeLocal=({syringeLocal.x:F3},{syringeLocal.y:F3}).");
                                    }

                                    remainingUnits -= moved;
                                    frameTransferred += moved;
                                    pendingVolume = Mathf.Max(0f, pendingVolume - moved);
                                    ConsumeIntakeRequests(requests, moved);
                                    intakeCredit = Mathf.Max(0f, intakeCredit - moved);
                                }
                            }
                        }

                        if (frameTransferred > 0)
                        {
                            framesWithTransfer++;
                        }
                        else if (pendingVolume > 0f && frame <= liftFrames + GameUpdatesPerSecond)
                        {
                            framesWithRequestsButNoTransfer++;
                        }

                        totalTransferred += frameTransferred;
                        if (liftingThisFrame)
                        {
                            if (frameTransferred > 0)
                            {
                                consecutiveLiftFramesWithoutTransfer = 0;
                            }
                            else
                            {
                                consecutiveLiftFramesWithoutTransfer++;
                                longestLiftGapWithoutTransfer = Mathf.Max(
                                    longestLiftGapWithoutTransfer,
                                    consecutiveLiftFramesWithoutTransfer);
                            }
                        }

                        if (frame > 0)
                        {
                            simulation.Tick(UpdateDeltaTime, false, needlePoint);
                        }

                        var actualSyringeAmount = simulation.GetContainerLiquidAmount(0);
                        var actualBottleAmount = simulation.GetContainerLiquidAmount(1);
                        maximumSyringeAmount = Mathf.Max(maximumSyringeAmount, actualSyringeAmount);
                        if (firstFullFrame < 0
                            && actualSyringeAmount >= CenterBottleSyringeCapacity - 0.001f)
                        {
                            firstFullFrame = frame;
                        }

                        if (amountAtTop < 0f && handleY >= syringeFluidTop - 0.001f)
                        {
                            amountAtTop = actualSyringeAmount;
                        }

                        var unaccountedVolume = simulation.Count - actualSyringeAmount - actualBottleAmount;
                        if (unaccountedVolume > maximumUnaccountedVolume)
                        {
                            maximumUnaccountedVolume = unaccountedVolume;
                            firstUnaccountedFrame = frame;
                        }

                        if (frame <= liftFrames || frame % GameUpdatesPerSecond == 0)
                        {
                            var snapshots = simulation.CaptureDropSnapshots();
                            var liveDropIds = new HashSet<int>();
                            var uncontainedVolume = 0;
                            var nearestSourceDropDistance = float.PositiveInfinity;
                            foreach (var drop in snapshots)
                            {
                                liveDropIds.Add(drop.Id);
                                if (drop.ContainerAssignment == 2)
                                {
                                    nearestSourceDropDistance = Mathf.Min(
                                        nearestSourceDropDistance,
                                        Vector2.Distance(drop.Position, needlePoint));
                                }

                                var insideSyringe = simulation.ContainsContainer(0, drop.Position);
                                var insideBottle = simulation.ContainsContainer(1, drop.Position);
                                var syringeLocal = GetSourceLocalPosition(root, syringe, drop.Position);
                                var bottleLocal = GetSourceLocalPosition(root, bottle, drop.Position);
                                if (!insideSyringe && !insideBottle)
                                {
                                    uncontainedVolume += drop.Volume;
                                    WriteLine(
                                        $"lostDrop trace frame={frame} t={frame * UpdateDeltaTime:F3}s "
                                        + $"id={drop.Id} volume={drop.Volume} "
                                        + $"owner={drop.ContainerAssignment} "
                                        + $"position=({drop.Position.x:F3},{drop.Position.y:F3}) "
                                        + $"velocity=({drop.Velocity.x:F3},{drop.Velocity.y:F3}) "
                                        + $"syringeLocal=({syringeLocal.x:F3},{syringeLocal.y:F3}) "
                                        + $"bottleLocal=({bottleLocal.x:F3},{bottleLocal.y:F3}) removed=false.");
                                }

                                if (drop.ContainerAssignment == 1 && !insideSyringe)
                                {
                                    syringeOwnedDropsOutside.Add(drop.Id);
                                    WriteLine(
                                        $"takenDrop outsideSyringe frame={frame} t={frame * UpdateDeltaTime:F3}s "
                                        + $"id={drop.Id} volume={drop.Volume} "
                                        + $"insideSyringe={insideSyringe} insideBottle={insideBottle} "
                                        + $"owner={drop.ContainerAssignment} "
                                        + $"position=({drop.Position.x:F3},{drop.Position.y:F3}) "
                                        + $"velocity=({drop.Velocity.x:F3},{drop.Velocity.y:F3}) "
                                        + $"syringeLocal=({syringeLocal.x:F3},{syringeLocal.y:F3}) "
                                        + $"bottleLocal=({bottleLocal.x:F3},{bottleLocal.y:F3}) removed=false.");
                                }
                            }

                            foreach (var id in transferredDropIds)
                            {
                                if (!liveDropIds.Contains(id) && endedTransferredDropIds.Add(id))
                                {
                                    WriteLine(
                                        $"takenDrop identityEnded frame={frame} t={frame * UpdateDeltaTime:F3}s "
                                        + $"id={id}.");
                                }
                            }

                            WriteLine(
                                $"membership frame={frame} t={frame * UpdateDeltaTime:F3}s "
                                + $"syringe={actualSyringeAmount:F1}ml bottle={actualBottleAmount:F1}ml "
                                + $"uncontained={uncontainedVolume}ml.");
                            WriteLine(
                                $"trace frame={frame} t={frame * UpdateDeltaTime:F3}s "
                                + $"needleInsideBottle={simulation.ContainsContainer(1, needlePoint)} "
                                + $"sourceNearNeedle={sourceNearThisFrame} "
                                + $"nearestSourceDropDistance="
                                + $"{(float.IsPositiveInfinity(nearestSourceDropDistance) ? -1f : nearestSourceDropDistance):F2} "
                                + $"needle=({needlePoint.x:F3},{needlePoint.y:F3}) "
                                + $"handleY={handleY:F3}/{syringeFluidTop:F3} "
                                + $"requested={lastRequestedAmount:F2}ml pending={pendingVolume:F2}ml "
                                + $"intakeCredit={intakeCredit:F2}ml transferredFrame={frameTransferred}ml "
                                + $"syringe={actualSyringeAmount:F1}ml bottle={actualBottleAmount:F1}ml "
                                + $"unfilled={Mathf.Max(0f, lastRequestedAmount - actualSyringeAmount - pendingVolume):F2}ml.");
                        }
                    }

                    var finalSyringeAmount = simulation.GetContainerLiquidAmount(0);
                    var finalBottleAmount = simulation.GetContainerLiquidAmount(1);
                    var finalNeedleCenterOffset = Mathf.Abs(needlePoint.x - bottleCenter.x);
                    WriteLine(
                        $"Syringe intake observations: transferred={totalTransferred:F1}ml, "
                        + $"finalSyringe={finalSyringeAmount:F1}ml, finalBottle={finalBottleAmount:F1}ml, "
                        + $"requested={lastRequestedAmount:F1}ml, "
                        + $"liftRequestFrames={framesWithRequests}, transferFrames={framesWithTransfer}, "
                        + $"sourceNearNeedleFrames={framesWithSourceNearNeedle}, "
                        + $"requestNoTransferFrames={framesWithRequestsButNoTransfer}, "
                        + $"liftFrames={liftFrames}, finalHandleY={handleY:F3}/{syringeFluidTop:F3}, "
                        + $"amountAtTop={amountAtTop:F1}ml, maximumSyringeAmount={maximumSyringeAmount:F1}ml, "
                        + $"firstFullFrame={firstFullFrame}, "
                        + $"longestLiftGapWithoutTransfer={longestLiftGapWithoutTransfer} frames, "
                        + $"needleCenterOffset={finalNeedleCenterOffset:F6}, "
                        + $"finalUnaccounted={simulation.Count - finalSyringeAmount - finalBottleAmount:F1}ml, "
                        + $"maximumUnaccounted={maximumUnaccountedVolume:F1}ml "
                        + $"atFrame={firstUnaccountedFrame}, "
                        + $"trackedTakenDrops={transferredDropIds.Count}, "
                        + $"endedTakenDrops={endedTransferredDropIds.Count}, "
                        + $"syringeOwnedDropsOutside={syringeOwnedDropsOutside.Count}, "
                        + $"totalUnits={simulation.Count}.");
                    var finalUnaccountedVolume = simulation.Count - finalSyringeAmount - finalBottleAmount;
                    WriteLine(
                        $"Full-stroke intake result: shortfallAtTop="
                        + $"{CenterBottleSyringeCapacity - amountAtTop:F1}ml, "
                        + $"finalShortfall={CenterBottleSyringeCapacity - finalSyringeAmount:F1}ml, "
                        + $"finalUnaccounted={finalUnaccountedVolume:F1}ml.");
                    if (amountAtTop < CenterBottleSyringeCapacity - 0.001f
                        || finalSyringeAmount < CenterBottleSyringeCapacity - 0.001f
                        || Mathf.Abs(finalUnaccountedVolume) > 0.001f
                        || maximumUnaccountedVolume > 0.001f
                        || syringeOwnedDropsOutside.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"Syringe intake failed full-stroke containment/accounting: expected "
                            + $"{CenterBottleSyringeCapacity:F1}ml by end, observed "
                            + $"{finalSyringeAmount:F1}ml (at top={amountAtTop:F1}ml, "
                            + $"peak={maximumSyringeAmount:F1}ml), final unaccounted="
                            + $"{finalUnaccountedVolume:F1}ml, maximum unaccounted="
                            + $"{maximumUnaccountedVolume:F1}ml at frame {firstUnaccountedFrame}, "
                            + $"syringe-owned drops outside="
                            + $"{syringeOwnedDropsOutside.Count}, transferred={totalTransferred:F1}ml.");
                    }

                    WriteLine("PASS: full-stroke syringe intake filled the syringe and conserved liquid.");
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

            Rect GetBounds(Vector2[] points)
            {
                var minimum = points[0];
                var maximum = points[0];
                foreach (var point in points)
                {
                    minimum = Vector2.Min(minimum, point);
                    maximum = Vector2.Max(maximum, point);
                }

                return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            }

            void ConsumeIntakeRequests(List<float> requests, int volume)
            {
                var remainingVolume = (float)volume;
                while (requests.Count > 0 && remainingVolume > 0f)
                {
                    var request = requests[0];
                    var consumedVolume = Mathf.Min(request, remainingVolume);
                    request -= consumedVolume;
                    remainingVolume -= consumedVolume;
                    if (request <= 0.001f)
                    {
                        requests.RemoveAt(0);
                    }
                    else
                    {
                        requests[0] = request;
                    }
                }
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

            var screenPixelSize = new Vector2(Screen.width, Screen.height);
            var uiScale = screenPixelSize.y / 1080f;
            if (screenPixelSize.x <= 0f || screenPixelSize.y <= 0f || uiScale <= 0f)
            {
                throw new InvalidOperationException($"Invalid Unity screen dimensions: {screenPixelSize}.");
            }
            var viewportSize = screenPixelSize / uiScale;
            screenObject = new GameObject("LiquidDiagnosticsCanvas", typeof(RectTransform));
            var canvasRoot = screenObject.GetComponent<RectTransform>();
            ConfigureRect(canvasRoot, null, viewportSize, Vector2.zero);
            canvasRoot.localScale = Vector3.one * uiScale;
            var screenGameObject = new GameObject("LiquidDiagnosticsScreen", typeof(RectTransform));
            var screen = screenGameObject.GetComponent<RectTransform>();
            ConfigureRect(screen, canvasRoot, viewportSize, Vector2.zero);
            var rootObject = new GameObject("LiquidDiagnosticsRoot", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            ConfigureRect(root, screen, viewportSize, Vector2.zero);
            var coffeeSpriteSize = LoadVisibleSpriteSize(
                Path.Combine(repositoryRoot, "resources", "sprites", "containers", "coffee.png"));
            var bottleSpriteSize = LoadVisibleSpriteSize(
                Path.Combine(repositoryRoot, "resources", "sprites", "containers", "waterbottle.png"));
            var receiver = CreateContainer(
                root,
                "EmptyCoffeeReceiver",
                coffeeSpriteSize);
            var source = CreateContainer(
                root,
                "FullWaterBottle",
                bottleSpriteSize);

            var receiverScale = LiquidContainerLayout.TryGetAreaScaledScale(
                ReferenceCapacity,
                ReferenceCapacity,
                referenceArea,
                referenceArea,
                out var calculatedReceiverScale)
                ? calculatedReceiverScale
                : throw new InvalidOperationException("Could not calculate the coffee receiver scale.");
            var bottleScale = LiquidContainerLayout.TryGetAreaScaledScale(
                WaterBottleCapacity,
                ReferenceCapacity,
                referenceArea,
                bottleArea,
                out var calculatedBottleScale)
                ? calculatedBottleScale
                : throw new InvalidOperationException("Could not calculate the water-bottle scale.");
            receiver.localScale = Vector3.one * receiverScale;
            source.localScale = Vector3.one * bottleScale;
            if (funnelWallScenario)
            {
                source.localRotation = Quaternion.Euler(0f, 0f, 180f);
                var receiverOpening = (Vector2)root.InverseTransformPoint(
                    receiver.TransformPoint(referenceBoundary.OpeningPosition));
                var sourceOpening = (Vector2)root.InverseTransformPoint(
                    source.TransformPoint(bottleBoundary.OpeningPosition));
                source.anchoredPosition += receiverOpening
                    + Vector2.right * (LiquidSimulation.DefaultDropRadius * 2f)
                    + Vector2.up * 60f
                    - sourceOpening;
            }
            else
            {
                LiquidContainerLayout.PositionContainersSideBySide(
                    receiver,
                    referenceBoundary.OpeningPosition,
                    source,
                    bottleBoundary.OpeningPosition);
            }

            var fitScale = LiquidContainerLayout.GetFitScale(screen, root, receiver, source);
            root.localScale = Vector3.one * fitScale;
            Vector2[] funnelWallPoints = null;
            var funnelScale = 0f;
            var actualOutletGap = 0f;
            if (funnelWallScenario)
            {
                var leftFunnelSide = CreateContainer(
                    root,
                    "DiagnosticFunnelLeftSide",
                    new Vector2(59f, 88f));
                var rightFunnelSide = CreateContainer(
                    root,
                    "DiagnosticFunnelRightSide",
                    new Vector2(59f, 88f));
                var funnelSpriteSize = new Vector2(59f, 88f);
                var openingCenterWorld = receiver.TransformPoint(referenceBoundary.OpeningPosition);
                var openingCenter = root.InverseTransformPoint(openingCenterWorld);
                var targetTopWidth = Mathf.Max(
                    coffeeSpriteSize.x * receiverScale,
                    bottleSpriteSize.x * bottleScale);
                var expectedOutletGap = referenceBoundary.OpeningWidth
                    * receiverScale
                    * FunnelGeometry.OutletGapRatio;
                var initialScale = FunnelGeometry.PositionSides(
                    leftFunnelSide,
                    rightFunnelSide,
                    funnelSpriteSize,
                    openingCenter,
                    targetTopWidth,
                    expectedOutletGap);
                var funnelCenter = FunnelGeometry.GetInitialCenter(
                    root,
                    receiver,
                    funnelSpriteSize,
                    initialScale,
                    expectedOutletGap,
                    LiquidContainerLayout.ContainerGap);
                funnelScale = FunnelGeometry.PositionSides(
                    leftFunnelSide,
                    rightFunnelSide,
                    funnelSpriteSize,
                    funnelCenter,
                    targetTopWidth,
                    expectedOutletGap);
                if (leftFunnelSide.localScale != new Vector3(-funnelScale, funnelScale, 1f)
                    || rightFunnelSide.localScale != new Vector3(funnelScale, funnelScale, 1f)
                    || funnelScale <= 1f)
                {
                    throw new InvalidOperationException("Funnel fixture did not scale proportionally to the container.");
                }

                funnelWallPoints = new Vector2[FunnelGeometry.WallPointCount];
                FunnelGeometry.WriteWallPoints(
                    root,
                    leftFunnelSide,
                    rightFunnelSide,
                    funnelSpriteSize,
                    funnelWallPoints);
                actualOutletGap = Vector2.Distance(funnelWallPoints[3], funnelWallPoints[7]);
                if (Mathf.Abs(actualOutletGap - expectedOutletGap) > 0.01f)
                {
                    throw new InvalidOperationException(
                        $"Funnel outlet gap was {actualOutletGap:F2}px; expected "
                        + $"{FunnelGeometry.OutletGapRatio:P0} of the opening "
                        + $"({expectedOutletGap:F2}px).");
                }

                var receiverLeft = root.InverseTransformPoint(
                    receiver.TransformPoint(new Vector2(-receiver.rect.width * 0.5f, 0f))).x;
                var receiverTop = root.InverseTransformPoint(
                    receiver.TransformPoint(new Vector2(0f, receiver.rect.height * 0.5f))).y;
                var funnelRight = Mathf.Max(
                    leftFunnelSide.anchoredPosition.x
                        + funnelSpriteSize.x * Mathf.Abs(leftFunnelSide.localScale.x) * 0.5f,
                    rightFunnelSide.anchoredPosition.x
                        + funnelSpriteSize.x * Mathf.Abs(rightFunnelSide.localScale.x) * 0.5f);
                var funnelTop = Mathf.Max(
                    leftFunnelSide.anchoredPosition.y
                        + funnelSpriteSize.y * leftFunnelSide.localScale.y * 0.5f,
                    rightFunnelSide.anchoredPosition.y
                        + funnelSpriteSize.y * rightFunnelSide.localScale.y * 0.5f);
                if (Mathf.Abs(receiverLeft - funnelRight - LiquidContainerLayout.ContainerGap) > 0.01f
                    || Mathf.Abs(receiverTop - funnelTop) > 0.01f)
                {
                    throw new InvalidOperationException(
                        "Funnel fixture failed its left-side spacing or top-edge alignment check.");
                }

                var funnelCollisionRadius = FunnelGeometry.WallCollisionHalfWidth * funnelScale;
                var blocksAtOpening = ContainerShapeCollision.OverlapsPolylineSegments(
                    referenceBoundary.WallCenterlinePoints,
                    receiver.anchoredPosition,
                    receiverScale,
                    receiver.localEulerAngles.z,
                    funnelWallPoints,
                    funnelCollisionRadius);
                var blocksAtWall = false;
                for (var shift = 2f; shift <= 600f && !blocksAtWall; shift += 2f)
                {
                    blocksAtWall = ContainerShapeCollision.OverlapsPolylineSegments(
                        referenceBoundary.WallCenterlinePoints,
                        receiver.anchoredPosition + Vector2.left * shift,
                        receiverScale,
                        receiver.localEulerAngles.z,
                        funnelWallPoints,
                        funnelCollisionRadius);
                }

                if (blocksAtOpening || !blocksAtWall)
                {
                    throw new InvalidOperationException(
                        $"Funnel collision fixture at opening={blocksAtOpening}, at wall={blocksAtWall}.");
                }

                var testOutline = new[]
                {
                    new Vector2(-5f, -5f),
                    new Vector2(5f, -5f),
                    new Vector2(5f, 5f),
                    new Vector2(-5f, 5f)
                };
                var hitboxCenter = (funnelWallPoints[0] + funnelWallPoints[1]) * 0.5f;
                if (!ContainerShapeCollision.OverlapsSegments(
                        testOutline,
                        hitboxCenter,
                        1f,
                        0f,
                        funnelWallPoints,
                        2f)
                    || ContainerShapeCollision.OverlapsSegments(
                        testOutline,
                        hitboxCenter + Vector2.one * 100f,
                        1f,
                        0f,
                        funnelWallPoints,
                        2f))
                {
                    throw new InvalidOperationException("Funnel-to-container collision fixture failed.");
                }

                var openingOutline = new[]
                {
                    new Vector2(-10f, 10f),
                    new Vector2(-10f, -10f),
                    new Vector2(10f, -10f),
                    new Vector2(10f, 10f)
                };
                var funnelInOpening = new[]
                {
                    new Vector2(-1f, 12f),
                    new Vector2(-1f, 8f),
                    new Vector2(1f, 12f),
                    new Vector2(1f, 8f)
                };
                var funnelAgainstInnerWall = new[]
                {
                    new Vector2(9f, 5f),
                    new Vector2(9f, 0f)
                };
                if (ContainerShapeCollision.OverlapsPolylineSegments(
                        openingOutline,
                        Vector2.zero,
                        1f,
                        0f,
                        funnelInOpening,
                        1f)
                    || !ContainerShapeCollision.OverlapsPolylineSegments(
                        openingOutline,
                        Vector2.zero,
                        1f,
                        0f,
                        funnelAgainstInnerWall,
                        1f))
                {
                    throw new InvalidOperationException(
                        "Funnel collision fixture did not allow rim insertion while blocking the inner wall.");
                }
            }

            var water = new[]
            {
                new LiquidProfileEntry(
                    "water",
                    singleDropScenario ? 1f : WaterBottleCapacity,
                    new Color(0.16f, 0.5f, 0.95f))
            };
            var expectedUnits = singleDropScenario ? 1f : WaterBottleCapacity;
            var simulation = new LiquidSimulation(
                root,
                receiver,
                referenceBoundary.WallCenterlinePoints,
                referenceBoundary.InteriorWalls,
                250f,
                source,
                bottleBoundary.WallCenterlinePoints,
                bottleBoundary.InteriorWalls,
                WaterBottleCapacity,
                0f,
                expectedUnits,
                Array.Empty<LiquidProfileEntry>(),
                water,
                LiquidSimulation.DefaultPackingTenths / 100f,
                headless: true,
                funnelWallPoints: funnelWallPoints);
            simulation.ConfigureForMinigame(false);

            var initialDrops = simulation.CaptureDropSnapshots();
            if (simulation.GetInitialContainerDropCount(0) != 0
                || simulation.GetInitialContainerDropCount(1) != expectedUnits
                || (singleDropScenario && initialDrops.Length != 1))
            {
                throw new InvalidOperationException(
                    $"Fixture seeded {simulation.GetInitialContainerDropCount(1)} units into the bottle; expected "
                    + $"{expectedUnits} units and "
                    + (singleDropScenario ? "exactly one drop." : "all 500 ml."));
            }

            WriteLine(
                $"Fixture: 250ml coffee receiver {coffeeSpriteSize.x:F0}x{coffeeSpriteSize.y:F0} "
                + $"at ({receiver.anchoredPosition.x:F1},{receiver.anchoredPosition.y:F1}) "
                + $"scale={receiverScale:F3}; water bottle capacity={WaterBottleCapacity:F0}ml, "
                + $"seeded={expectedUnits:F0}ml "
                + $"{bottleSpriteSize.x:F0}x{bottleSpriteSize.y:F0} "
                + $"at ({source.anchoredPosition.x:F1},{source.anchoredPosition.y:F1}) "
                + $"scale={bottleScale:F3}.");
            if (funnelWallScenario)
            {
                WriteLine(
                    $"Funnel fixture: top width={FunnelGeometry.RimFitRatio:P0} of larger container, "
                    + $"sprite scale={funnelScale:F3}, "
                    + $"outlet gap={actualOutletGap:F2}px ({FunnelGeometry.OutletGapRatio:P0} of opening), "
                    + $"insertion margin={FunnelGeometry.RimInsertionMargin:F2}px, "
                    + $"wall endpoints={string.Join("; ", Array.ConvertAll(funnelWallPoints, point => $"({point.x:F2},{point.y:F2})"))}.");
            }
            WriteLine(
                $"ScreenPixels={screenPixelSize.x:F0}x{screenPixelSize.y:F0}, "
                + $"gameUiScale={uiScale:F4}, minigameScreen={viewportSize.x:F0}x{viewportSize.y:F0}, "
                + $"simulationArea={root.rect.width:F0}x{root.rect.height:F0}, "
                + $"simulationAreaScale={root.localScale.x:F4}.");
            WriteLine(
                $"Simulation: updateDelta={UpdateDeltaTime:F6}s, "
                + $"physicsStep={LiquidSimulation.FixedStep:F6}s, "
                + $"duration={SimulationFrames * UpdateDeltaTime:F1}s, "
                + $"settling window={SettlingWindowFrames * UpdateDeltaTime:F1}s.");
            WriteLine(
                $"Seed: particles={initialDrops.Length}, initial units={simulation.Count}.");
            var trackedDropId = singleDropScenario ? initialDrops[0].Id : -1;
            var trackedDropPosition = initialDrops.Length > 0
                ? initialDrops[0].Position
                : Vector2.zero;
            var trackedDropVelocity = initialDrops.Length > 0
                ? initialDrops[0].Velocity
                : Vector2.zero;
            var trackedSourceOffset = initialDrops.Length > 0
                ? GetSourceLocalPosition(root, source, initialDrops[0].Position)
                : Vector2.zero;
            var initialSourceOffset = trackedSourceOffset;
            var previousTrackedPosition = trackedDropPosition;
            var maximumFrameMovement = 0f;
            var maximumSourceRelativeDrift = 0f;
            var maximumTrackedSpeed = 0f;
            var trackedDropRemoved = false;
            var trackedDropInsideReceiver = false;
            byte trackedDropOwner = 0;
            var minimumFunnelWallClearance = float.PositiveInfinity;
            var funnelWallContactFrames = 0;
            var trackedDropRadius = LiquidSimulation.DefaultDropRadiusTenths / 10f;

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
                    simulation.Tick(UpdateDeltaTime, false, Vector2.zero);
                }

                var drops = simulation.CaptureDropSnapshots();
                var escapedUnits = 0;
                var ownershipMismatches = 0;
                var frameUnits = 0;
                var frameMaximumSpeed = 0f;
                var trackedDropFound = false;
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

                    if (singleDropScenario && drop.Id == trackedDropId)
                    {
                        trackedDropFound = true;
                        trackedDropInsideReceiver = simulation.ContainsContainer(0, drop.Position);
                        trackedDropOwner = drop.ContainerAssignment;
                        var sourceLocalPosition = GetSourceLocalPosition(root, source, drop.Position);
                        var frameMovement = Vector2.Distance(previousTrackedPosition, drop.Position);
                        var sourceRelativeDrift = Vector2.Distance(initialSourceOffset, sourceLocalPosition);
                        maximumFrameMovement = Mathf.Max(maximumFrameMovement, frameMovement);
                        maximumSourceRelativeDrift = Mathf.Max(
                            maximumSourceRelativeDrift,
                            sourceRelativeDrift);
                        maximumTrackedSpeed = Mathf.Max(maximumTrackedSpeed, drop.Velocity.magnitude);
                        var wallClearance = funnelWallScenario
                            ? GetFunnelWallClearance(drop.Position, trackedDropRadius, funnelWallPoints)
                            : float.NaN;
                        if (funnelWallScenario)
                        {
                            minimumFunnelWallClearance = Mathf.Min(minimumFunnelWallClearance, wallClearance);
                            if (wallClearance <= 0.02f)
                            {
                                funnelWallContactFrames++;
                            }
                        }

                        WriteLine(
                            $"trace frame={frame} t={frame * UpdateDeltaTime:F3}s id={drop.Id} "
                            + $"insideReceiver={simulation.ContainsContainer(0, drop.Position)} "
                            + $"insideSource={simulation.ContainsContainer(1, drop.Position)} "
                            + $"owner={drop.ContainerAssignment} position=({drop.Position.x:F3},"
                            + $"{drop.Position.y:F3}) velocity=({drop.Velocity.x:F3},"
                            + $"{drop.Velocity.y:F3}) sourceLocal=({sourceLocalPosition.x:F3},"
                            + $"{sourceLocalPosition.y:F3}) frameMove={frameMovement:F3} "
                            + $"sourceDrift={sourceRelativeDrift:F3} "
                            + (funnelWallScenario ? $"wallClearance={wallClearance:F3} " : string.Empty)
                            + "removed=false.");
                        trackedDropPosition = drop.Position;
                        trackedDropVelocity = drop.Velocity;
                        trackedSourceOffset = sourceLocalPosition;
                        previousTrackedPosition = drop.Position;
                    }
                }

                if (singleDropScenario && !trackedDropFound)
                {
                    trackedDropRemoved = true;
                    WriteLine(
                        $"trace frame={frame} t={frame * UpdateDeltaTime:F3}s id={trackedDropId} "
                        + $"insideReceiver=false insideSource=false owner=removed "
                        + $"position=({trackedDropPosition.x:F3},{trackedDropPosition.y:F3}) "
                        + $"velocity=({trackedDropVelocity.x:F3},{trackedDropVelocity.y:F3}) "
                        + $"sourceLocal=({trackedSourceOffset.x:F3},{trackedSourceOffset.y:F3}) "
                        + "frameMove=0.000 sourceDrift=0.000 "
                        + (funnelWallScenario ? "wallClearance=removed " : string.Empty)
                        + "removed=true.");
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

                if (frameUnits != expectedUnits || simulation.Count != expectedUnits)
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

                if (frame % SettlingWindowFrames == 0 || frame == SimulationFrames)
                {
                    WriteLine(
                        $"frame={frame} t={frame * UpdateDeltaTime:F1}s "
                        + $"units={frameUnits}/{expectedUnits:F0}, "
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
            if (singleDropScenario)
            {
                WriteLine(
                    $"Single-drop observed maxima: frameMovement={maximumFrameMovement:F3}px/frame, "
                    + $"sourceRelativeDrift={maximumSourceRelativeDrift:F3}px, "
                    + $"velocity={maximumTrackedSpeed:F3}px/s, removed={trackedDropRemoved}.");
            }
            if (funnelWallScenario)
            {
                WriteLine(
                    $"Funnel wall observations: minimumClearance={minimumFunnelWallClearance:F3}px "
                    + $"for drop radius={trackedDropRadius:F3}px, "
                    + $"contactFrames={funnelWallContactFrames}.");
                if (maximumFrameMovement > FunnelMaximumFrameMovement
                    || maximumSourceRelativeDrift > FunnelMaximumSourceRelativeDrift
                    || maximumTrackedSpeed > FunnelMaximumTrackedSpeed
                    || minimumFunnelWallClearance < FunnelMinimumWallClearance
                    || trackedDropRemoved
                    || !trackedDropInsideReceiver
                    || trackedDropOwner != 1)
                {
                    throw new InvalidOperationException(
                        "Funnel single-drop exceeded observed motion bounds, crossed a wall, "
                        + "or failed to settle in the receiver. "
                        + $"movement={maximumFrameMovement:F3}/{FunnelMaximumFrameMovement:F3}px/frame, "
                        + $"drift={maximumSourceRelativeDrift:F3}/{FunnelMaximumSourceRelativeDrift:F3}px, "
                        + $"speed={maximumTrackedSpeed:F3}/{FunnelMaximumTrackedSpeed:F3}px/s, "
                        + $"minimumClearance={minimumFunnelWallClearance:F3}px, "
                        + $"contactFrames={funnelWallContactFrames}, removed={trackedDropRemoved}, "
                        + $"receiver={trackedDropInsideReceiver}, owner={trackedDropOwner}.");
                }
            }

            if ((!funnelWallScenario && framesWithEscapedDrops > 0)
                || (!funnelWallScenario && framesWithOwnershipMismatch > 0)
                || framesWithUnitMismatch > 0)
            {
                throw new InvalidOperationException(
                    "The simulation lost units or violated the container idle-scenario invariants.");
            }

            if (!singleDropScenario
                && (finalMaximumSpeed > MaximumSettledSpeed
                    || maximumSettlingWindowSpeed > MaximumSettledSpeed))
            {
                throw new InvalidOperationException(
                    $"Drops did not settle below {MaximumSettledSpeed:F1}px/s; "
                    + $"final={finalMaximumSpeed:F3}px/s, "
                    + $"last-second maximum={maximumSettlingWindowSpeed:F3}px/s.");
            }

            WriteLine(singleDropScenario
                ? "PASS: single-drop diagnostic completed; inspect observed movement metrics above."
                : "PASS: full water bottle retained all liquid and settled.");
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

    private static void RunWaterBottleIntoMiniBarrelScenario()
    {
        GameObject screenObject = null;
        var exitCode = 0;
        try
        {
            var logPath = GetArgument("-diagnosticLog");
            var repositoryRoot = GetArgument("-repositoryRoot");
            if (string.IsNullOrWhiteSpace(logPath) || string.IsNullOrWhiteSpace(repositoryRoot))
            {
                throw new ArgumentException("Expected diagnostic-log and repository-root arguments.");
            }

            if (!GeneratedContainerBoundaryCatalog.TryGet("coffee", out var referenceBoundary)
                || !GeneratedContainerBoundaryCatalog.TryGet("waterbottle", out var bottleBoundary)
                || !GeneratedContainerBoundaryCatalog.TryGet("minibarreltop", out var barrelBoundary)
                || !bottleBoundary.HasOpening
                || !barrelBoundary.HasOpening)
            {
                throw new InvalidOperationException(
                    "Generated coffee, water-bottle, or minibarrel boundaries are missing.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            log = new StreamWriter(logPath, false, new System.Text.UTF8Encoding(false))
            {
                AutoFlush = true
            };
            DebugLog.Sink = WriteLine;
            Plugin.LogSink = WriteLine;
            WriteLine($"Scenario '{WaterBottleIntoMiniBarrelScenario}' started at {DateTime.Now:O}.");

            var referenceArea = EstimateArea(
                referenceBoundary.WallCenterlinePoints,
                referenceBoundary.InteriorWalls);
            var bottleArea = EstimateArea(
                bottleBoundary.WallCenterlinePoints,
                bottleBoundary.InteriorWalls);
            var barrelArea = EstimateArea(
                barrelBoundary.WallCenterlinePoints,
                barrelBoundary.InteriorWalls);
            if (referenceArea <= 0d || bottleArea <= 0d || barrelArea <= 0d)
            {
                throw new InvalidOperationException("Could not calculate gameplay container areas.");
            }

            var screenPixelSize = new Vector2(Screen.width, Screen.height);
            var uiScale = screenPixelSize.y / 1080f;
            if (screenPixelSize.x <= 0f || screenPixelSize.y <= 0f || uiScale <= 0f)
            {
                throw new InvalidOperationException($"Invalid Unity screen dimensions: {screenPixelSize}.");
            }

            var viewportSize = screenPixelSize / uiScale;
            screenObject = new GameObject("BottleIntoMiniBarrelDiagnosticsCanvas", typeof(RectTransform));
            var canvasRoot = screenObject.GetComponent<RectTransform>();
            ConfigureRect(canvasRoot, null, viewportSize, Vector2.zero);
            canvasRoot.localScale = Vector3.one * uiScale;
            var screenGameObject = new GameObject(
                "BottleIntoMiniBarrelDiagnosticsScreen",
                typeof(RectTransform));
            var screen = screenGameObject.GetComponent<RectTransform>();
            ConfigureRect(screen, canvasRoot, viewportSize, Vector2.zero);
            var rootObject = new GameObject("BottleIntoMiniBarrelDiagnosticsRoot", typeof(RectTransform));
            var root = rootObject.GetComponent<RectTransform>();
            ConfigureRect(root, screen, viewportSize, Vector2.zero);

            var bottleSpriteSize = LoadVisibleSpriteSize(
                Path.Combine(repositoryRoot, "resources", "sprites", "containers", "waterbottle.png"));
            var barrelSpriteSize = LoadVisibleSpriteSize(
                Path.Combine(repositoryRoot, "resources", "sprites", "containers", "minibarreltop.png"));
            var bottle = CreateContainer(root, "EmptyWaterBottle", bottleSpriteSize);
            var barrel = CreateContainer(root, "FullMiniBarrel", barrelSpriteSize);
            var bottleScale = LiquidContainerLayout.TryGetAreaScaledScale(
                WaterBottleCapacity,
                ReferenceCapacity,
                referenceArea,
                bottleArea,
                out var calculatedBottleScale)
                ? calculatedBottleScale
                : throw new InvalidOperationException("Could not calculate the water-bottle scale.");
            var barrelScale = LiquidContainerLayout.TryGetAreaScaledScale(
                MiniBarrelCapacity,
                ReferenceCapacity,
                referenceArea,
                barrelArea,
                out var calculatedBarrelScale)
                ? calculatedBarrelScale
                : throw new InvalidOperationException("Could not calculate the minibarrel scale.");
            bottle.localScale = Vector3.one * bottleScale;
            bottle.localRotation = Quaternion.Euler(0f, 0f, 180f);
            barrel.localScale = Vector3.one * barrelScale;
            barrel.anchoredPosition = Vector2.zero;

            var barrelOpening = (Vector2)root.InverseTransformPoint(
                barrel.TransformPoint(barrelBoundary.OpeningPosition));
            var bottleOpening = (Vector2)root.InverseTransformPoint(
                bottle.TransformPoint(bottleBoundary.OpeningPosition));
            var startOpening = barrelOpening
                + Vector2.up * LiquidSimulation.DefaultDropRadius * 2f;
            bottle.anchoredPosition += startOpening - bottleOpening;
            var fitScale = LiquidContainerLayout.GetFitScale(screen, root, bottle, barrel);
            root.localScale = Vector3.one * fitScale;

            var barrelMinimumY = float.PositiveInfinity;
            foreach (var point in barrelBoundary.WallCenterlinePoints)
            {
                barrelMinimumY = Mathf.Min(barrelMinimumY, point.y);
            }

            var barrelBottom = (Vector2)root.InverseTransformPoint(
                barrel.TransformPoint(new Vector2(0f, barrelMinimumY)));
            var endOpeningY = barrelBottom.y + LiquidSimulation.DefaultDropRadius * 2f;
            if (endOpeningY >= startOpening.y)
            {
                throw new InvalidOperationException(
                    $"Bottle lowering path is invalid: start={startOpening.y:F2}, end={endOpeningY:F2}.");
            }

            var water = new[]
            {
                new LiquidProfileEntry(
                    "water",
                    MiniBarrelCapacity,
                    new Color(0.16f, 0.5f, 0.95f))
            };
            var simulation = new LiquidSimulation(
                root,
                bottle,
                bottleBoundary.WallCenterlinePoints,
                bottleBoundary.InteriorWalls,
                WaterBottleCapacity,
                barrel,
                barrelBoundary.WallCenterlinePoints,
                barrelBoundary.InteriorWalls,
                MiniBarrelCapacity,
                0f,
                MiniBarrelCapacity,
                Array.Empty<LiquidProfileEntry>(),
                water,
                LiquidSimulation.DefaultPackingTenths / 100f,
                headless: true);
            simulation.ConfigureForMinigame(false);

            var initialDrops = simulation.CaptureDropSnapshots();
            if (simulation.GetInitialContainerDropCount(0) != 0
                || simulation.GetInitialContainerDropCount(1) != MiniBarrelCapacity
                || initialDrops.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Fixture did not seed a full minibarrel: bottle="
                    + $"{simulation.GetInitialContainerDropCount(0)}ml, barrel="
                    + $"{simulation.GetInitialContainerDropCount(1)}/{MiniBarrelCapacity:F0}ml, "
                    + $"particles={initialDrops.Length}.");
            }

            var trackedDrop = initialDrops[0];
            var trackedInitialOffset = barrel.InverseTransformPoint(root.TransformPoint(trackedDrop.Position));
            var trackedStartDistance = float.PositiveInfinity;
            foreach (var drop in initialDrops)
            {
                var distance = Vector2.Distance(drop.Position, startOpening);
                if (distance < trackedStartDistance)
                {
                    trackedDrop = drop;
                    trackedStartDistance = distance;
                }
            }

            trackedInitialOffset = barrel.InverseTransformPoint(
                root.TransformPoint(trackedDrop.Position));
            var trackedDropId = trackedDrop.Id;
            var trackedPosition = trackedDrop.Position;
            var trackedVelocity = trackedDrop.Velocity;
            var previousTrackedPosition = trackedPosition;
            var trackedBarrelLocal = (Vector2)trackedInitialOffset;
            var maximumFrameMovement = 0f;
            var maximumSourceRelativeDrift = 0f;
            var maximumTrackedSpeed = 0f;
            var framesWithUnassignedLiquid = 0;
            var maximumUnassignedUnits = 0;
            var maximumUnaccountedUnits = 0;
            var trackedDropRemoved = false;
            var firstTrackedDropMissingFrame = -1;
            var trackedDropEnteredBottle = false;
            var trackedOwner = (byte)2;
            var expectedUnits = simulation.Count;
            var loweringFrames = Mathf.CeilToInt(
                BottleLoweringDurationSeconds * GameUpdatesPerSecond);
            var movementDistance = startOpening.y - endOpeningY;
            WriteLine(
                $"Fixture: empty water bottle={bottleSpriteSize.x:F0}x{bottleSpriteSize.y:F0}, "
                + $"scale={bottleScale:F3}; full minibarrel={barrelSpriteSize.x:F0}x"
                + $"{barrelSpriteSize.y:F0}, capacity={MiniBarrelCapacity:F0}ml, "
                + $"scale={barrelScale:F3}; bottle rotated={bottle.localEulerAngles.z:F0}deg.");
            WriteLine(
                $"Motion: updates={GameUpdatesPerSecond}Hz, duration={SimulationDurationSeconds}s, "
                + $"loweringDuration={BottleLoweringDurationSeconds:F1}s, "
                + $"openingY={startOpening.y:F2}->{endOpeningY:F2}, "
                + $"distance={movementDistance:F2}px, "
                + $"initialBottleAmount={simulation.GetContainerLiquidAmount(0):F1}ml, "
                + $"initialBarrelAmount={simulation.GetContainerLiquidAmount(1):F1}ml, "
                + $"particles={initialDrops.Length}, trackedDrop={trackedDropId}, "
                + $"trackedDropVolume={trackedDrop.Volume}ml, "
                + $"trackedInitialBarrelLocal=({trackedInitialOffset.x:F3},"
                + $"{trackedInitialOffset.y:F3}), trackedStartDistance={trackedStartDistance:F3}px.");

            for (var frame = 0; frame <= SimulationFrames; frame++)
            {
                var loweringProgress = Mathf.Clamp01((float)frame / loweringFrames);
                if (frame > 0 && frame <= loweringFrames)
                {
                    var previousPosition = bottle.anchoredPosition;
                    var targetOpeningY = Mathf.Lerp(startOpening.y, endOpeningY, loweringProgress);
                    var currentOpeningY = root.InverseTransformPoint(
                        bottle.TransformPoint(bottleBoundary.OpeningPosition)).y;
                    bottle.anchoredPosition += Vector2.up * (targetOpeningY - currentOpeningY);
                    simulation.ApplyContainerPoseChange(
                        bottle,
                        previousPosition,
                        180f,
                        bottle.anchoredPosition,
                        180f,
                        Vector2.one);
                }

                if (frame > 0)
                {
                    simulation.Tick(UpdateDeltaTime, false, Vector2.zero);
                }

                var drops = simulation.CaptureDropSnapshots();
                var bottleOwnedUnits = 0;
                var barrelOwnedUnits = 0;
                var unassignedUnits = 0;
                var liveUnits = 0;
                var trackedDropFound = false;
                foreach (var drop in drops)
                {
                    liveUnits += drop.Volume;
                    if (drop.ContainerAssignment == 1)
                    {
                        bottleOwnedUnits += drop.Volume;
                    }
                    else if (drop.ContainerAssignment == 2)
                    {
                        barrelOwnedUnits += drop.Volume;
                    }
                    else
                    {
                        unassignedUnits += drop.Volume;
                    }

                    if (drop.Id != trackedDropId)
                    {
                        continue;
                    }

                    trackedDropFound = true;
                    var insideBottle = simulation.ContainsContainer(0, drop.Position);
                    var insideBarrel = simulation.ContainsContainer(1, drop.Position);
                    var barrelLocal = (Vector2)barrel.InverseTransformPoint(
                        root.TransformPoint(drop.Position));

                    var frameMovement = Vector2.Distance(previousTrackedPosition, drop.Position);
                    var sourceRelativeDrift = Vector2.Distance(trackedInitialOffset, barrelLocal);
                    maximumFrameMovement = Mathf.Max(maximumFrameMovement, frameMovement);
                    maximumSourceRelativeDrift = Mathf.Max(
                        maximumSourceRelativeDrift,
                        sourceRelativeDrift);
                    maximumTrackedSpeed = Mathf.Max(maximumTrackedSpeed, drop.Velocity.magnitude);
                    trackedDropEnteredBottle |= insideBottle;
                    trackedOwner = drop.ContainerAssignment;
                    WriteLine(
                        $"trace frame={frame} t={frame * UpdateDeltaTime:F3}s "
                        + $"id={drop.Id} volume={drop.Volume} insideBottle={insideBottle} "
                        + $"insideBarrel={insideBarrel} owner={drop.ContainerAssignment} "
                        + $"position=({drop.Position.x:F3},{drop.Position.y:F3}) "
                        + $"velocity=({drop.Velocity.x:F3},{drop.Velocity.y:F3}) "
                        + $"barrelLocal=({barrelLocal.x:F3},{barrelLocal.y:F3}) "
                        + $"frameMove={frameMovement:F3} sourceDrift={sourceRelativeDrift:F3} "
                        + "removed=false.");
                    trackedPosition = drop.Position;
                    trackedVelocity = drop.Velocity;
                    trackedBarrelLocal = barrelLocal;
                    previousTrackedPosition = drop.Position;
                }

                if (!trackedDropFound)
                {
                    trackedDropRemoved = true;
                    if (firstTrackedDropMissingFrame < 0)
                    {
                        firstTrackedDropMissingFrame = frame;
                    }

                    WriteLine(
                        $"trace frame={frame} t={frame * UpdateDeltaTime:F3}s "
                        + $"id={trackedDropId} insideBottle=false insideBarrel=false "
                        + $"owner=removed position=({trackedPosition.x:F3},{trackedPosition.y:F3}) "
                        + $"velocity=({trackedVelocity.x:F3},{trackedVelocity.y:F3}) "
                        + $"barrelLocal=({trackedBarrelLocal.x:F3},{trackedBarrelLocal.y:F3}) "
                        + "frameMove=0.000 sourceDrift=0.000 removed=true.");
                }

                framesWithUnassignedLiquid += unassignedUnits > 0 ? 1 : 0;
                maximumUnassignedUnits = Mathf.Max(maximumUnassignedUnits, unassignedUnits);
                var unaccountedUnits = Mathf.Abs(expectedUnits - liveUnits);
                maximumUnaccountedUnits = Mathf.Max(maximumUnaccountedUnits, unaccountedUnits);
                if (frame % GameUpdatesPerSecond == 0 || frame == SimulationFrames)
                {
                    WriteLine(
                        $"membership frame={frame} t={frame * UpdateDeltaTime:F1}s "
                        + $"bottleOwned={bottleOwnedUnits}ml barrelOwned={barrelOwnedUnits}ml "
                        + $"unassigned={unassignedUnits}ml live={liveUnits}/{expectedUnits}ml "
                        + $"unaccounted={unaccountedUnits}ml particles={drops.Length}.");
                }
            }

            var finalBottleAmount = simulation.GetContainerLiquidAmount(0);
            WriteLine(
                $"Bottle-into-minibarrel observations: finalBottle="
                + $"{finalBottleAmount:F1}ml, "
                + $"finalBarrel={simulation.GetContainerLiquidAmount(1):F1}ml, "
                + $"liveUnits={simulation.Count}/{expectedUnits}ml, "
                + $"trackedDropEnteredBottle={trackedDropEnteredBottle}, "
                + $"trackedDropOwner={trackedOwner}, trackedDropRemoved={trackedDropRemoved} "
                + $"atFrame={firstTrackedDropMissingFrame}, "
                + $"unassignedFrames={framesWithUnassignedLiquid}, "
                + $"maximumUnassigned={maximumUnassignedUnits}ml, "
                + $"maximumUnaccounted={maximumUnaccountedUnits}ml, "
                + $"maximumFrameMovement={maximumFrameMovement:F3}px/frame, "
                + $"maximumSourceRelativeDrift={maximumSourceRelativeDrift:F3}px, "
                + $"maximumTrackedSpeed={maximumTrackedSpeed:F3}px/s.");
            if (finalBottleAmount <= 0f)
            {
                throw new InvalidOperationException("No liquid entered the lowered water bottle.");
            }

            WriteLine(
                "PASS: water-bottle-into-minibarrel diagnostic completed; "
                + "liquid loss is logged for observation.");
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

    private static float GetFunnelWallClearance(
        Vector2 position,
        float radius,
        Vector2[] wallPoints)
    {
        var minimumDistanceSquared = float.PositiveInfinity;
        for (var index = 0; index < wallPoints.Length; index += 2)
        {
            var start = wallPoints[index];
            var direction = wallPoints[index + 1] - start;
            var lengthSquared = direction.sqrMagnitude;
            if (lengthSquared <= 0.0001f)
            {
                continue;
            }

            var ratio = Mathf.Clamp01(Vector2.Dot(position - start, direction) / lengthSquared);
            var nearestPoint = start + direction * ratio;
            minimumDistanceSquared = Mathf.Min(
                minimumDistanceSquared,
                (position - nearestPoint).sqrMagnitude);
        }

        return Mathf.Sqrt(minimumDistanceSquared) - radius;
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

    private static RectTransform CreateContainer(RectTransform parent, string name, Vector2 size)
    {
        var containerObject = new GameObject(name, typeof(RectTransform));
        var container = containerObject.GetComponent<RectTransform>();
        ConfigureRect(container, parent, size, Vector2.zero);
        return container;
    }

    private static Vector2 LoadVisibleSpriteSize(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required game container sprite was not found.", path);
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
            {
                throw new InvalidDataException($"Could not decode game container sprite '{path}'.");
            }

            var pixels = texture.GetPixels32();
            var minimumX = texture.width;
            var minimumY = texture.height;
            var maximumX = -1;
            var maximumY = -1;
            for (var y = 0; y < texture.height; y++)
            {
                var rowStart = y * texture.width;
                for (var x = 0; x < texture.width; x++)
                {
                    if (pixels[rowStart + x].a < MinimumVisibleAlpha)
                    {
                        continue;
                    }

                    minimumX = Math.Min(minimumX, x);
                    minimumY = Math.Min(minimumY, y);
                    maximumX = Math.Max(maximumX, x);
                    maximumY = Math.Max(maximumY, y);
                }
            }

            if (maximumX < minimumX || maximumY < minimumY)
            {
                throw new InvalidDataException($"Game container sprite '{path}' is fully transparent.");
            }

            return new Vector2(maximumX - minimumX + 1, maximumY - minimumY + 1);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static Vector2 GetSourceLocalPosition(
        RectTransform root,
        RectTransform source,
        Vector2 rootPosition)
    {
        return source.InverseTransformPoint(root.TransformPoint(rootPosition));
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
