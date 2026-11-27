using System;
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
    private const float FunnelMaximumFrameMovement = 30f;
    private const float FunnelMaximumSourceRelativeDrift = 120f;
    private const float FunnelMaximumTrackedSpeed = 900f;
    private const float FunnelMinimumWallClearance = -0.02f;
    private const byte MinimumVisibleAlpha = 32;
    private const string SingleDropScenario = "water-bottle-single-drop";
    private const string FunnelWallSingleDropScenario = "funnel-wall-single-drop";
    private static StreamWriter log;

    public static void Run()
    {
        GameObject screenObject = null;
        var exitCode = 0;
        try
        {
            var scenario = GetArgument("-scenario");
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
                var openingCenterWorld = receiver.TransformPoint(referenceBoundary.OpeningPosition);
                funnelScale = FunnelGeometry.PositionSides(
                    leftFunnelSide,
                    rightFunnelSide,
                    new Vector2(59f, 88f),
                    root.InverseTransformPoint(openingCenterWorld),
                    Mathf.Max(
                        coffeeSpriteSize.x * receiverScale,
                        bottleSpriteSize.x * bottleScale),
                    referenceBoundary.OpeningWidth
                        * receiverScale
                        * FunnelGeometry.OutletGapRatio);
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
                    new Vector2(59f, 88f),
                    funnelWallPoints);
                var expectedOutletGap = referenceBoundary.OpeningWidth
                    * receiverScale
                    * FunnelGeometry.OutletGapRatio;
                actualOutletGap = Vector2.Distance(funnelWallPoints[3], funnelWallPoints[7]);
                if (Mathf.Abs(actualOutletGap - expectedOutletGap) > 0.01f)
                {
                    throw new InvalidOperationException(
                        $"Funnel outlet gap was {actualOutletGap:F2}px; expected "
                        + $"{FunnelGeometry.OutletGapRatio:P0} of the opening "
                        + $"({expectedOutletGap:F2}px).");
                }
                var funnelCollisionRadius = FunnelGeometry.WallCollisionHalfWidth * funnelScale;
                var funnelAgainstReceiverWall = new Vector2[funnelWallPoints.Length];
                var wallContactShift = (referenceBoundary.OpeningWidth * receiverScale - expectedOutletGap) * 0.5f
                    + funnelCollisionRadius
                    + 1f;
                for (var index = 0; index < funnelWallPoints.Length; index++)
                {
                    funnelAgainstReceiverWall[index] = funnelWallPoints[index] + Vector2.right * wallContactShift;
                }

                var blocksAtOpening = ContainerShapeCollision.OverlapsPolylineSegments(
                    referenceBoundary.WallCenterlinePoints,
                    receiver.anchoredPosition,
                    receiverScale,
                    receiver.localEulerAngles.z,
                    funnelWallPoints,
                    funnelCollisionRadius);
                var blocksAtWall = ContainerShapeCollision.OverlapsPolylineSegments(
                    referenceBoundary.WallCenterlinePoints,
                    receiver.anchoredPosition,
                    receiverScale,
                    receiver.localEulerAngles.z,
                    funnelAgainstReceiverWall,
                    funnelCollisionRadius);
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
