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
    private const float ReferenceCapacity = 250f;
    private const float MaximumSettledSpeed = 8f;
    private const float SyringeMaximumFrameMovement = 18f;
    private const float SyringeMaximumSourceRelativeDrift = 45f;
    private const float SyringeMaximumTrackedSpeed = 500f;
    private const float SyringeMaximumSettledSpeed = 6f;
    private const float FunnelMaximumFrameMovement = 30f;
    private const float FunnelMaximumSourceRelativeDrift = 120f;
    private const float FunnelMaximumTrackedSpeed = 900f;
    private const float FunnelMinimumWallClearance = -0.02f;
    private const float CenterBottleSyringeCapacity = 100f;
    private const float CenterBottleSyringeIntakeRate = 25.2f;
    private const float CenterBottleSyringeScaleMultiplier = 1.1f;
    private const byte MinimumVisibleAlpha = 32;
    private const string SingleDropScenario = "water-bottle-single-drop";
    private const string FunnelWallSingleDropScenario = "funnel-wall-single-drop";
    private const string SyringeCenterBottleScenario = "syringe-center-bottle-intake";
    private const string SyringeCapacityScenario = "syringe-full-capacity";
    private const float SyringeCapacity = 100f;
    private const float SyringeTestScale = 1.3f;
    private const float SyringeDropRadiusMultiplier = 0.68f;
    private static readonly Vector2[] SyringeFluidOutline =
    {
        new Vector2(-9f, 58.5f),
        new Vector2(9f, 58.5f),
        new Vector2(9f, -12.5f),
        new Vector2(-9f, -12.5f),
        new Vector2(-9f, 58.5f)
    };
    private static StreamWriter log;

    public static void Run()
    {
        GameObject screenObject = null;
        var exitCode = 0;
        try
        {
            var scenario = GetArgument("-scenario");
            if (scenario == SyringeCenterBottleScenario)
            {
                RunSyringeCenterBottleIntakeScenario();
                return;
            }

            var logPath = GetArgument("-diagnosticLog");
            var repositoryRoot = GetArgument("-repositoryRoot");
            var funnelWallScenario = scenario == FunnelWallSingleDropScenario;
            var singleDropScenario = scenario == SingleDropScenario || funnelWallScenario;
            var syringeScenario = scenario == SyringeCapacityScenario;
            var traceDropScenario = singleDropScenario || syringeScenario;
            if ((scenario != "water-bottle-full-idle"
                    && scenario != SingleDropScenario
                    && !funnelWallScenario
                    && !syringeScenario)
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

                    var bottleBounds = GetBounds(bottleBoundary.WallCenterlinePoints);
                    var bottleCenter = (Vector2)root.InverseTransformPoint(
                        bottle.TransformPoint(bottleBounds.center));
                    syringe.anchoredPosition = bottleCenter
                        - Vector2.Scale(syringeNeedle, new Vector2(syringeScale, syringeScale));
                    var fitScale = LiquidContainerLayout.GetFitScale(screen, root, syringe, bottle);
                    root.localScale = Vector3.one * fitScale;
                    var needlePoint = syringe.anchoredPosition
                        + Vector2.Scale(syringeNeedle, new Vector2(syringeScale, syringeScale));
                    var needleOffsetFromBottleCenter = Vector2.Distance(needlePoint, bottleCenter);

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
                    WriteLine(
                        $"Fixture: syringe capacity={CenterBottleSyringeCapacity:F0}ml, "
                        + $"stroke=({syringeFluidBottom:F2},"
                        + $"{syringeFluidTop:F2}), scale={syringeScale:F3}; "
                        + $"bottle={WaterBottleCapacity:F0}ml, scale={bottleScale:F3}; "
                        + $"needle=({needlePoint.x:F3},{needlePoint.y:F3}), "
                        + $"bottleCenter=({bottleCenter.x:F3},{bottleCenter.y:F3}), "
                        + $"centerOffset={needleOffsetFromBottleCenter:F6}, insideBottle={needleInsideBottle}, "
                        + $"sourceNearNeedle={sourceNearNeedle}.");
                    WriteLine(
                        $"Simulation: updates={GameUpdatesPerSecond}Hz, duration={SimulationDurationSeconds}s, "
                        + $"liftRate={CenterBottleSyringeIntakeRate:F2}ml/s, expected lift time="
                        + $"{CenterBottleSyringeCapacity / CenterBottleSyringeIntakeRate:F3}s, "
                        + $"seeded source amount={simulation.GetContainerLiquidAmount(1):F1}ml.");
                    if (needleOffsetFromBottleCenter > 0.001f || !needleInsideBottle)
                    {
                        throw new InvalidOperationException(
                            "The syringe needle was not placed at the center of the bottle.");
                    }

                    var requests = new List<Vector2>();
                    var movementDeltaTime = Mathf.Min(UpdateDeltaTime, 1f / 30f);
                    var strokeHeight = syringeFluidTop - syringeFluidBottom;
                    var maximumUpwardSpeed = Mathf.Min(
                        420f,
                        strokeHeight * CenterBottleSyringeIntakeRate / CenterBottleSyringeCapacity);
                    var handleY = syringeFluidBottom;
                    var lastRequestedAmount = 0f;
                    var intakeCredit = 0f;
                    var totalTransferred = 0f;
                    var totalExpiredAsAir = 0f;
                    var framesWithRequests = 0;
                    var framesWithTransfer = 0;
                    var framesWithSourceNearNeedle = 0;
                    var framesWithRequestsButNoTransfer = 0;
                    var liftFrames = 0;
                    var consecutiveLiftFramesWithoutTransfer = 0;
                    var longestLiftGapWithoutTransfer = 0;
                    var amountAtTop = -1f;
                    var maximumSyringeAmount = 0f;
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
                                requests.Add(new Vector2(requestedDelta, 0.5f));
                                framesWithRequests++;
                                liftFrames++;
                            }

                            lastRequestedAmount = requestedAmount;
                        }

                        var pendingVolume = 0f;
                        var expiredAsAir = 0f;
                        for (var index = requests.Count - 1; index >= 0; index--)
                        {
                            var request = requests[index];
                            request.y -= UpdateDeltaTime;
                            if (request.y <= 0f || request.x <= 0f)
                            {
                                expiredAsAir += request.x;
                                requests.RemoveAt(index);
                            }
                            else
                            {
                                pendingVolume += request.x;
                                requests[index] = request;
                            }
                        }

                        totalExpiredAsAir += expiredAsAir;
                        var currentAmount = simulation.GetContainerLiquidAmount(0);
                        var remainingCapacity = Mathf.Max(0f, CenterBottleSyringeCapacity - currentAmount);
                        var allowedPendingVolume = Mathf.Min(pendingVolume, remainingCapacity);
                        var sourceNearThisFrame = simulation.HasContainerDropNear(
                            1,
                            needlePoint,
                            LiquidSimulation.DefaultDropRadius * 3f);
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
                                    var moved = simulation.TryTransferNearestDrop(
                                        1,
                                        0,
                                        needlePoint,
                                        LiquidSimulation.DefaultDropRadius * 3f,
                                        destinationPoint,
                                        remainingUnits,
                                        Vector2.zero);
                                    if (moved <= 0)
                                    {
                                        break;
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
                        var actualSyringeAmount = simulation.GetContainerLiquidAmount(0);
                        var actualBottleAmount = simulation.GetContainerLiquidAmount(1);
                        maximumSyringeAmount = Mathf.Max(maximumSyringeAmount, actualSyringeAmount);
                        if (amountAtTop < 0f && handleY >= syringeFluidTop - 0.001f)
                        {
                            amountAtTop = actualSyringeAmount;
                        }

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

                        if (frame <= liftFrames || frame % GameUpdatesPerSecond == 0)
                        {
                            WriteLine(
                                $"trace frame={frame} t={frame * UpdateDeltaTime:F3}s "
                                + $"needleInsideBottle={simulation.ContainsContainer(1, needlePoint)} "
                                + $"sourceNearNeedle={sourceNearThisFrame} "
                                + $"needle=({needlePoint.x:F3},{needlePoint.y:F3}) "
                                + $"handleY={handleY:F3}/{syringeFluidTop:F3} "
                                + $"requested={lastRequestedAmount:F2}ml pending={pendingVolume:F2}ml "
                                + $"intakeCredit={intakeCredit:F2}ml transferredFrame={frameTransferred}ml "
                                + $"syringe={actualSyringeAmount:F1}ml bottle={actualBottleAmount:F1}ml "
                                + $"expiredAsAir={expiredAsAir:F2}ml.");
                        }

                        if (frame > 0)
                        {
                            simulation.Tick(UpdateDeltaTime, false, needlePoint);
                        }
                    }

                    var finalSyringeAmount = simulation.GetContainerLiquidAmount(0);
                    var finalBottleAmount = simulation.GetContainerLiquidAmount(1);
                    var finalNeedleOffset = Vector2.Distance(needlePoint, bottleCenter);
                    WriteLine(
                        $"Syringe intake observations: transferred={totalTransferred:F1}ml, "
                        + $"finalSyringe={finalSyringeAmount:F1}ml, finalBottle={finalBottleAmount:F1}ml, "
                        + $"requested={lastRequestedAmount:F1}ml, expiredAsAir={totalExpiredAsAir:F1}ml, "
                        + $"liftRequestFrames={framesWithRequests}, transferFrames={framesWithTransfer}, "
                        + $"sourceNearNeedleFrames={framesWithSourceNearNeedle}, "
                        + $"requestNoTransferFrames={framesWithRequestsButNoTransfer}, "
                        + $"liftFrames={liftFrames}, finalHandleY={handleY:F3}/{syringeFluidTop:F3}, "
                        + $"amountAtTop={amountAtTop:F1}ml, maximumSyringeAmount={maximumSyringeAmount:F1}ml, "
                        + $"longestLiftGapWithoutTransfer={longestLiftGapWithoutTransfer} frames, "
                        + $"needleCenterOffset={finalNeedleOffset:F6}, totalUnits={simulation.Count}.");
                    var unaccountedVolume = simulation.Count - finalSyringeAmount - finalBottleAmount;
                    if (amountAtTop < CenterBottleSyringeCapacity - 0.001f
                        || Mathf.Abs(unaccountedVolume) > 0.001f)
                    {
                        throw new InvalidOperationException(
                            $"Syringe intake failed full-stroke accounting: expected "
                            + $"{CenterBottleSyringeCapacity:F1}ml at top, "
                            + $"observed {amountAtTop:F1}ml (peak={maximumSyringeAmount:F1}ml), "
                            + $"unaccounted={unaccountedVolume:F1}ml, transferred={totalTransferred:F1}ml.");
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

            void ConsumeIntakeRequests(List<Vector2> requests, int volume)
            {
                var remainingVolume = (float)volume;
                while (requests.Count > 0 && remainingVolume > 0f)
                {
                    var request = requests[0];
                    var consumedVolume = Mathf.Min(request.x, remainingVolume);
                    request.x -= consumedVolume;
                    remainingVolume -= consumedVolume;
                    if (request.x <= 0.001f)
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
                syringeScenario ? "FullSyringe" : "FullWaterBottle",
                syringeScenario ? new Vector2(30f, 141f) : bottleSpriteSize);

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
            var sourceScale = syringeScenario
                ? LiquidContainerLayout.ContainerScale * SyringeTestScale
                : bottleScale;
            source.localScale = Vector3.one * sourceScale;
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
                    syringeScenario ? Vector2.zero : bottleBoundary.OpeningPosition);
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
                    singleDropScenario
                        ? 1f
                        : syringeScenario
                            ? SyringeCapacity + 1f
                            : WaterBottleCapacity,
                    new Color(0.16f, 0.5f, 0.95f))
            };
            var expectedUnits = singleDropScenario
                ? 1f
                : syringeScenario
                    ? SyringeCapacity + 1f
                    : WaterBottleCapacity;
            var sourceOutline = syringeScenario
                ? SyringeFluidOutline
                : bottleBoundary.WallCenterlinePoints;
            var sourceInteriorWalls = syringeScenario
                ? Array.Empty<Vector2[]>()
                : bottleBoundary.InteriorWalls;
            var sourceCapacity = syringeScenario ? SyringeCapacity : WaterBottleCapacity;
            var firstAmount = syringeScenario ? expectedUnits : 0f;
            var secondAmount = syringeScenario ? 0f : expectedUnits;
            var firstComposition = syringeScenario ? water : Array.Empty<LiquidProfileEntry>();
            var secondComposition = syringeScenario ? Array.Empty<LiquidProfileEntry>() : water;
            var fullSyringeWater = new[]
            {
                new LiquidProfileEntry(
                    "water",
                    SyringeCapacity,
                    new Color(0.16f, 0.5f, 0.95f))
            };
            var initialSyringeAmount = 0f;
            if (syringeScenario)
            {
                var fullSyringeSimulation = new LiquidSimulation(
                    root,
                    receiver,
                    referenceBoundary.WallCenterlinePoints,
                    referenceBoundary.InteriorWalls,
                    250f,
                    source,
                    SyringeFluidOutline,
                    Array.Empty<Vector2[]>(),
                    SyringeCapacity,
                    0f,
                    SyringeCapacity,
                    Array.Empty<LiquidProfileEntry>(),
                    fullSyringeWater,
                    LiquidSimulation.DefaultPackingTenths / 100f,
                    headless: true,
                    secondContainerDropRadiusMultiplier: SyringeDropRadiusMultiplier);
                initialSyringeAmount = fullSyringeSimulation.GetContainerLiquidAmount(1);
                var initialHandleY = Mathf.Lerp(
                    SyringeFluidOutline[3].y,
                    SyringeFluidOutline[0].y,
                    Mathf.Clamp01(initialSyringeAmount / SyringeCapacity));
                WriteLine(
                    $"Full syringe startup fixture: requested={SyringeCapacity:F0}ml, "
                    + $"seeded={initialSyringeAmount:F0}ml, handleY={initialHandleY:F1}, "
                    + $"topY={SyringeFluidOutline[0].y:F1}.");
            }

            var simulation = new LiquidSimulation(
                root,
                receiver,
                referenceBoundary.WallCenterlinePoints,
                referenceBoundary.InteriorWalls,
                250f,
                source,
                sourceOutline,
                sourceInteriorWalls,
                sourceCapacity,
                firstAmount,
                secondAmount,
                firstComposition,
                secondComposition,
                LiquidSimulation.DefaultPackingTenths / 100f,
                headless: true,
                funnelWallPoints: funnelWallPoints,
                firstContainerDropRadiusMultiplier: 1f,
                secondContainerDropRadiusMultiplier: syringeScenario
                    ? SyringeDropRadiusMultiplier
                    : 1f);
            if (syringeScenario)
            {
                simulation.SetSyringePistonPosition(1, SyringeFluidOutline[0].y);
                var transferSourcePoint = root.InverseTransformPoint(
                    receiver.TransformPoint(Vector2.zero));
                var transferredUnits = 0;
                while (transferredUnits < SyringeCapacity + 1f)
                {
                    var fillRatio = Mathf.Clamp01(
                        simulation.GetContainerLiquidAmount(1) / SyringeCapacity);
                    var surfaceY = Mathf.Lerp(
                        SyringeFluidOutline[3].y,
                        SyringeFluidOutline[0].y,
                        fillRatio);
                    var localDropRadius = LiquidSimulation.DefaultDropRadiusTenths / 10f
                        * SyringeDropRadiusMultiplier
                        / Mathf.Max(Mathf.Abs(source.localScale.y), 0.0001f);
                    var maximumY = SyringeFluidOutline[0].y - localDropRadius;
                    var destinationY = Mathf.Clamp(
                        Mathf.Min(surfaceY + localDropRadius, maximumY),
                        SyringeFluidOutline[3].y + localDropRadius,
                        maximumY);
                    var transferDestinationPoint = root.InverseTransformPoint(
                        source.TransformPoint(new Vector2(0f, destinationY)));
                    var moved = simulation.TryTransferNearestDrop(
                        0,
                        1,
                        transferSourcePoint,
                        10000f,
                        transferDestinationPoint,
                        1,
                        Vector2.zero);
                    if (moved <= 0)
                    {
                        break;
                    }

                    transferredUnits += moved;
                }

                WriteLine(
                    $"Syringe intake fixture: requested={SyringeCapacity + 1f:F0}ml, "
                    + $"transferred={transferredUnits}ml, "
                    + $"inSyringe={simulation.GetContainerLiquidAmount(1):F0}ml, "
                    + $"leftOutside={simulation.GetContainerLiquidAmount(0):F0}ml.");
            }
            simulation.ConfigureForMinigame(false);

            var initialDrops = simulation.CaptureDropSnapshots();
            if (!syringeScenario
                && (simulation.GetInitialContainerDropCount(0) != 0
                    || simulation.GetInitialContainerDropCount(1) != expectedUnits
                    || (singleDropScenario && initialDrops.Length != 1)))
            {
                throw new InvalidOperationException(
                    $"Fixture seeded {simulation.GetInitialContainerDropCount(1)} units into the bottle; expected "
                    + $"{expectedUnits} units and "
                    + (singleDropScenario
                        ? "exactly one drop."
                        : syringeScenario
                            ? "all 100 ml."
                            : "all 500 ml."));
            }

            WriteLine(
                $"Fixture: 250ml coffee receiver {coffeeSpriteSize.x:F0}x{coffeeSpriteSize.y:F0} "
                + $"at ({receiver.anchoredPosition.x:F1},{receiver.anchoredPosition.y:F1}) "
                + $"scale={receiverScale:F3}; second container capacity={sourceCapacity:F0}ml, "
                + $"liquid={expectedUnits:F0}ml "
                + $"{(syringeScenario ? 30f : bottleSpriteSize.x):F0}x"
                + $"{(syringeScenario ? 141f : bottleSpriteSize.y):F0} "
                + $"at ({source.anchoredPosition.x:F1},{source.anchoredPosition.y:F1}) "
                + $"scale={sourceScale:F3}.");
            if (syringeScenario)
            {
                WriteLine(
                    $"Syringe fixture: scale={SyringeTestScale:F2}, "
                    + "fluid outline width=18px, height=71px.");
            }
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
            var trackedDrop = initialDrops.Length > 0 ? initialDrops[0] : default;
            if (syringeScenario)
            {
                foreach (var drop in initialDrops)
                {
                    if (drop.ContainerAssignment == 2)
                    {
                        trackedDrop = drop;
                        break;
                    }
                }
            }

            var trackedDropId = traceDropScenario ? trackedDrop.Id : -1;
            var trackedDropPosition = traceDropScenario && initialDrops.Length > 0
                ? trackedDrop.Position
                : Vector2.zero;
            var trackedDropVelocity = traceDropScenario && initialDrops.Length > 0
                ? trackedDrop.Velocity
                : Vector2.zero;
            var trackedSourceOffset = traceDropScenario && initialDrops.Length > 0
                ? GetSourceLocalPosition(root, source, trackedDrop.Position)
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
                    var insideReceiver = simulation.ContainsContainer(0, drop.Position);
                    var insideSource = simulation.ContainsContainer(1, drop.Position);
                    if (!(syringeScenario ? insideReceiver || insideSource : insideSource))
                    {
                        escapedUnits += drop.Volume;
                    }

                    if (syringeScenario
                        ? drop.ContainerAssignment != (insideReceiver ? 1 : (byte)2)
                        : drop.ContainerAssignment != 2)
                    {
                        ownershipMismatches += drop.Volume;
                    }

                    if (traceDropScenario && drop.Id == trackedDropId)
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

                if (traceDropScenario && !trackedDropFound)
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

            var maximumSettledSpeed = syringeScenario
                ? SyringeMaximumSettledSpeed
                : MaximumSettledSpeed;
            WriteLine(
                $"Assertion summary: escapedFrames={framesWithEscapedDrops}, "
                + $"maxEscapedUnits={maximumEscapedUnits}, "
                + $"ownershipMismatchFrames={framesWithOwnershipMismatch}, "
                + $"unitMismatchFrames={framesWithUnitMismatch}, "
                + $"settlingWindowMaxSpeed={maximumSettlingWindowSpeed:F3}px/s, "
                + $"finalMaxSpeed={finalMaximumSpeed:F3}px/s.");
            if (traceDropScenario)
            {
                WriteLine(
                    $"Tracked-drop observed maxima: frameMovement={maximumFrameMovement:F3}px/frame, "
                    + $"sourceRelativeDrift={maximumSourceRelativeDrift:F3}px, "
                    + $"velocity={maximumTrackedSpeed:F3}px/s, removed={trackedDropRemoved}.");
            }
            var syringeAmount = simulation.GetContainerLiquidAmount(1);
            if (syringeScenario
                && (Mathf.Abs(initialSyringeAmount - SyringeCapacity) > 0.001f
                    || Mathf.Abs(syringeAmount - SyringeCapacity) > 0.001f
                    || maximumFrameMovement > SyringeMaximumFrameMovement
                    || maximumSourceRelativeDrift > SyringeMaximumSourceRelativeDrift
                    || maximumTrackedSpeed > SyringeMaximumTrackedSpeed
                    || trackedDropRemoved
                    || !simulation.ContainsContainer(1, trackedDropPosition)
                    || trackedDropOwner != 2))
            {
                throw new InvalidOperationException(
                    "The syringe did not receive and retain 100 ml, or exceeded observed motion bounds. "
                    + $"liquid={syringeAmount:F3}/{SyringeCapacity:F3}ml, "
                    + $"movement={maximumFrameMovement:F3}/{SyringeMaximumFrameMovement:F3}px/frame, "
                    + $"drift={maximumSourceRelativeDrift:F3}/{SyringeMaximumSourceRelativeDrift:F3}px, "
                    + $"speed={maximumTrackedSpeed:F3}/{SyringeMaximumTrackedSpeed:F3}px/s, "
                    + $"removed={trackedDropRemoved}, owner={trackedDropOwner}.");
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
                && (finalMaximumSpeed > maximumSettledSpeed
                    || maximumSettlingWindowSpeed > maximumSettledSpeed))
            {
                throw new InvalidOperationException(
                    $"Drops did not settle below {maximumSettledSpeed:F1}px/s; "
                    + $"final={finalMaximumSpeed:F3}px/s, "
                    + $"last-second maximum={maximumSettlingWindowSpeed:F3}px/s.");
            }

            WriteLine(syringeScenario
                ? "PASS: full-capacity syringe diagnostic retained all requested liquid."
                : traceDropScenario
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
