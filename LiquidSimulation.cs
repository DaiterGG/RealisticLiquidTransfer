using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class LiquidSimulation
{
    private const int MaximumDrops = 20000;
    private const byte AggregateVolume = 7;
    private const float DropRadius = 12f * 0.7368063f;
    private const float AggregateRadius = DropRadius * 2.6457513f;
    private const float DensityTexturePixelsPerUnit = 0.5f;
    private const int MaximumDensityTextureDimension = 1024;
    private const float DensityContribution = 1.2f;
    private const float TailSpeedForMaximumLength = 600f;
    private const float MaximumEncodedVelocity = 800f;
    private const int DropsPerGraphic = 4096;
    private const float IndividualDropRadius = DropRadius * 0.88f;
    private const float SpawnRate = 1000f;
    private const float Gravity = 420f;
    private const float GravityMultiplier = 2f;
    private const float FixedStep = 1f / 30f;
    private const int DropCollisionIterations = 6;
    private const float AggregateRingTolerance = DropRadius * 0.45f;
    private const int AggregateMinimumSupport = 3;
    private const int AggregateMinimumSplitSupport = 5;
    private static readonly Vector2[] AggregateRingDirections =
    {
        new Vector2(1f, 0f),
        new Vector2(0.5f, 0.8660254f),
        new Vector2(-0.5f, 0.8660254f),
        new Vector2(-1f, 0f),
        new Vector2(-0.5f, -0.8660254f),
        new Vector2(0.5f, -0.8660254f)
    };

    private readonly RectTransform root;
    private readonly ContainerShape[] containers;
    private readonly bool[] openingTransitions;
    private readonly int[] initialContainerDropCounts;
    private Vector2[] positions = new Vector2[MaximumDrops];
    private Vector2[] previousPositions = new Vector2[MaximumDrops];
    private Vector2[] velocities = new Vector2[MaximumDrops];
    private int[] dropIds = new int[MaximumDrops];
    private byte[] dropVolumes = new byte[MaximumDrops];
    private byte[] containerAssignments = new byte[MaximumDrops];
    private Vector2[] reorderedPositions = new Vector2[MaximumDrops];
    private Vector2[] reorderedPreviousPositions = new Vector2[MaximumDrops];
    private Vector2[] reorderedVelocities = new Vector2[MaximumDrops];
    private int[] reorderedDropIds = new int[MaximumDrops];
    private byte[] reorderedDropVolumes = new byte[MaximumDrops];
    private byte[] reorderedContainerAssignments = new byte[MaximumDrops];
    private readonly bool[] aggregationConsumed = new bool[MaximumDrops];
    private readonly int[] aggregateMembers = new int[AggregateVolume];
    private readonly List<LiquidDropGraphic> individualDropGraphics = new List<LiquidDropGraphic>();
    private LiquidSurfaceGraphic surfaceGraphic;
    private Texture2D densityTexture;
    private Color32[] densityPixels;
    private Vector3[] densityVelocityFields;
    private int[] activeDensityPixelIndices;
    private int activeDensityPixelCount;
    private int densityTextureWidth;
    private int densityTextureHeight;
    private readonly float minimumScreenX;
    private readonly float maximumScreenX;
    private readonly float minimumScreenY;
    private readonly float maximumScreenY;
    private readonly int gridColumns;
    private readonly int gridRows;
    private readonly int[] cellCounts;
    private readonly int[] cellOffsets;
    private readonly int[] cellWriteOffsets;
    private readonly int[] cellParticleIndices;
    private readonly float cellSize = DropRadius * 2f;
    private int count;
    private int totalLiquidUnits;
    private int simulationStepsUntilSplitCheck = 4;
    private bool graphicsDirty = true;
    private bool surfaceShaderEnabled;
    private int nextDropId = 1;
    private float simulationTime;
    private float spawnAccumulator;
    private float diagnosticTimer;
    private long assignmentCorrections;
    private int offscreenDeleted;
    private int offscreenDeletedThisInterval;
    private int offscreenUnitsDeletedThisInterval;
    private int offscreenSamplesThisInterval;
    private int mergedAggregatesThisInterval;
    private int splitAggregatesThisInterval;
    private int largestSplitBatchThisInterval;
    private int aggregateOpeningBypassesThisInterval;
    private int openingBypassSamplesThisInterval;
    private uint randomState = 0xA341316Cu;

    internal LiquidSimulation(
        RectTransform root,
        RectTransform firstContainer,
        Vector2[] firstOutline,
        RectTransform secondContainer,
        Vector2[] secondOutline,
        float firstAmount,
        float secondAmount)
    {
        this.root = root;
        var screen = root.parent as RectTransform;
        if (!screen)
        {
            throw new InvalidOperationException("The liquid simulation root must be parented to a RectTransform.");
        }

        var screenCorners = new Vector3[4];
        screen.GetWorldCorners(screenCorners);
        var firstCorner = root.InverseTransformPoint(screenCorners[0]);
        minimumScreenX = firstCorner.x;
        maximumScreenX = firstCorner.x;
        minimumScreenY = firstCorner.y;
        maximumScreenY = firstCorner.y;
        foreach (var corner in screenCorners)
        {
            var localCorner = root.InverseTransformPoint(corner);
            minimumScreenX = Mathf.Min(minimumScreenX, localCorner.x);
            maximumScreenX = Mathf.Max(maximumScreenX, localCorner.x);
            minimumScreenY = Mathf.Min(minimumScreenY, localCorner.y);
            maximumScreenY = Mathf.Max(maximumScreenY, localCorner.y);
        }

        gridColumns = Mathf.Max(1, Mathf.CeilToInt((maximumScreenX - minimumScreenX) / cellSize));
        gridRows = Mathf.Max(1, Mathf.CeilToInt((maximumScreenY - minimumScreenY) / cellSize));
        var cellCount = gridColumns * gridRows;
        cellCounts = new int[cellCount];
        cellOffsets = new int[cellCount + 1];
        cellWriteOffsets = new int[cellCount];
        cellParticleIndices = new int[MaximumDrops];
        containers = new[]
        {
            new ContainerShape(root, firstContainer, firstOutline),
            new ContainerShape(root, secondContainer, secondOutline)
        };
        openingTransitions = new bool[containers.Length];
        CreateGraphics();
        SeedContainer(containers[0], GetInitialDropCount(firstAmount));
        SeedContainer(containers[1], GetInitialDropCount(secondAmount));
        BuildSpatialGrid();
        AggregatePackedDrops();
        initialContainerDropCounts = new[]
        {
            GetContainerDropCount(0),
            GetContainerDropCount(1)
        };
        DebugLog.Write(
            $"Simulation initialized: seeded "
            + $"{GetContainerDropCount(0)} receiver and {GetContainerDropCount(1)} source drops; "
            + $"drop radius={DropRadius:F1}, screen bounds=({minimumScreenX:F1},{minimumScreenY:F1})"
            + $"-({maximumScreenX:F1},{maximumScreenY:F1}).");
    }

    internal int Count => totalLiquidUnits;

    internal void SetSurfaceShaderEnabled(bool enabled)
    {
        surfaceShaderEnabled = enabled && LiquidShaderAssets.SurfaceMaterial;
        graphicsDirty = true;
        surfaceGraphic.enabled = surfaceShaderEnabled;
        foreach (var graphic in individualDropGraphics)
        {
            graphic.enabled = !surfaceShaderEnabled;
        }

        DebugLog.Write(
            $"Liquid surface shader {(surfaceShaderEnabled ? "enabled" : "disabled")}; "
            + $"individual drop rendering {(surfaceShaderEnabled ? "disabled" : "enabled")}.");
    }

    internal int GetContainerDropCount(int containerIndex)
    {
        if ((uint)containerIndex >= containers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(containerIndex));
        }

        var assignment = (byte)(containerIndex + 1);
        var assignedCount = 0;
        for (var i = 0; i < count; i++)
        {
            if (containerAssignments[i] == assignment)
            {
                assignedCount += dropVolumes[i];
            }
        }

        return assignedCount;
    }

    internal int GetInitialContainerDropCount(int containerIndex)
    {
        if ((uint)containerIndex >= initialContainerDropCounts.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(containerIndex));
        }

        return initialContainerDropCounts[containerIndex];
    }

    internal int[] GetFinalContainerDropCounts()
    {
        var finalCounts = new int[containers.Length];
        for (var i = 0; i < count; i++)
        {
            var position = positions[i];
            var inReceiver = containers[0].Contains(position);
            var inSource = containers[1].Contains(position);
            if (inReceiver && inSource)
            {
                var assignment = containerAssignments[i];
                finalCounts[assignment == 2 ? 1 : 0] += dropVolumes[i];
            }
            else if (inReceiver)
            {
                finalCounts[0] += dropVolumes[i];
            }
            else if (inSource || !IsOutsideScreen(position, GetDropRadius(i)))
            {
                finalCounts[1] += dropVolumes[i];
            }
        }

        return finalCounts;
    }

    internal bool IsInsideContainer(Vector2 point)
    {
        foreach (var container in containers)
        {
            if (container.Contains(point))
            {
                return true;
            }
        }

        return false;
    }

    internal void TranslateContainerDrops(
        RectTransform transform,
        Vector2 translation,
        Vector2 movementInfluence)
    {
        translation = Vector2.Scale(
            translation,
            new Vector2(
                Mathf.Clamp01(movementInfluence.x),
                Mathf.Clamp01(movementInfluence.y)));
        if (translation.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
        {
            if (containers[containerIndex].Transform != transform)
            {
                continue;
            }

            var assignment = (byte)(containerIndex + 1);
            for (var i = 0; i < count; i++)
            {
                if (containerAssignments[i] != assignment)
                {
                    continue;
                }

                positions[i] += translation;
                previousPositions[i] += translation;
            }

            graphicsDirty = true;
            return;
        }
    }

    internal void ApplyContainerPoseChange(
        RectTransform transform,
        Vector2 previousPosition,
        float previousAngle,
        Vector2 currentPosition,
        float currentAngle,
        Vector2 movementInfluence)
    {
        var oldRadians = previousAngle * Mathf.Deg2Rad;
        var oldCos = Mathf.Cos(oldRadians);
        var oldSin = Mathf.Sin(oldRadians);
        var newRadians = currentAngle * Mathf.Deg2Rad;
        var newCos = Mathf.Cos(newRadians);
        var newSin = Mathf.Sin(newRadians);
        var influence = new Vector2(
            Mathf.Clamp01(movementInfluence.x),
            Mathf.Clamp01(movementInfluence.y));

        for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
        {
            if (containers[containerIndex].Transform != transform)
            {
                continue;
            }

            var assignment = (byte)(containerIndex + 1);
            for (var i = 0; i < count; i++)
            {
                if (containerAssignments[i] != assignment)
                {
                    continue;
                }

                var previousOffset = positions[i] - previousPosition;
                var localOffset = new Vector2(
                    previousOffset.x * oldCos + previousOffset.y * oldSin,
                    -previousOffset.x * oldSin + previousOffset.y * oldCos);
                var currentOffset = new Vector2(
                    localOffset.x * newCos - localOffset.y * newSin,
                    localOffset.x * newSin + localOffset.y * newCos);
                var targetPosition = currentPosition + currentOffset;
                var appliedDelta = Vector2.Scale(targetPosition - positions[i], influence);
                positions[i] += appliedDelta;
                previousPositions[i] += appliedDelta;
            }

            graphicsDirty = true;
            return;
        }
    }

    internal void Tick(float deltaTime, bool spawn, Vector2 spawnPosition)
    {
        deltaTime = Mathf.Clamp(deltaTime, 0f, 0.1f);
        var profilingStart = SimulationProfiler.Begin();
        if (spawn)
        {
            spawnAccumulator += deltaTime * SpawnRate;
            var spawnCount = Mathf.Min(Mathf.FloorToInt(spawnAccumulator), MaximumDrops - totalLiquidUnits);
            spawnAccumulator -= spawnCount;
            for (var i = 0; i < spawnCount; i++)
            {
                Spawn(spawnPosition);
            }
        }
        else
        {
            spawnAccumulator = 0f;
        }
        SimulationProfiler.End(SimulationProfileStage.Spawning, profilingStart);

        if (totalLiquidUnits >= MaximumDrops)
        {
            spawnAccumulator = 0f;
        }

        simulationTime = Mathf.Min(simulationTime + deltaTime, FixedStep * 3f);
        var mergesRemaining = 1;
        var steps = 0;
        while (simulationTime >= FixedStep && steps < 3)
        {
            simulationStepsUntilSplitCheck--;
            var runSplitter = simulationStepsUntilSplitCheck == 0;
            if (runSplitter)
            {
                simulationStepsUntilSplitCheck = 4;
            }

            Step(FixedStep, ref mergesRemaining, runSplitter);
            simulationTime -= FixedStep;
            steps++;
        }

        diagnosticTimer += deltaTime;
        if (diagnosticTimer >= 1f)
        {
            var particleUnits = CountParticleUnits(
                out var singleParticles,
                out var aggregateParticles,
                out var invalidVolumeParticles);
            DebugLog.Write(
                $"Simulation: particles={count}, tracked units={totalLiquidUnits}/{MaximumDrops}, "
                + $"particle units={particleUnits} ({singleParticles} singles, {aggregateParticles} aggregates, "
                + $"{invalidVolumeParticles} invalid), merged/split this interval="
                + $"{mergedAggregatesThisInterval}/{splitAggregatesThisInterval} "
                + $"(largest split batch={largestSplitBatchThisInterval}), "
                + $"rim bypasses={aggregateOpeningBypassesThisInterval}, "
                + $"off-screen deleted this interval={offscreenDeletedThisInterval} particles/"
                + $"{offscreenUnitsDeletedThisInterval} units (total={offscreenDeleted}), "
                + $"assignment corrections={assignmentCorrections}.");
            SimulationProfiler.Report(count);
            offscreenDeletedThisInterval = 0;
            offscreenUnitsDeletedThisInterval = 0;
            offscreenSamplesThisInterval = 0;
            mergedAggregatesThisInterval = 0;
            splitAggregatesThisInterval = 0;
            largestSplitBatchThisInterval = 0;
            aggregateOpeningBypassesThisInterval = 0;
            openingBypassSamplesThisInterval = 0;
            diagnosticTimer %= 1f;
        }

        if (graphicsDirty)
        {
            profilingStart = SimulationProfiler.Begin();
            UpdateGraphics();
            SimulationProfiler.End(SimulationProfileStage.RenderScheduling, profilingStart);
            graphicsDirty = false;
        }
    }

    private void SeedContainer(ContainerShape container, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (container.Outline.Length < 3)
        {
            Plugin.LogWarning("Initial liquid seeding skipped: container outline is unavailable.");
            return;
        }

        var firstPoint = container.ToRootPoint(container.Outline[0]);
        var minimum = firstPoint;
        var maximum = firstPoint;
        foreach (var outlinePoint in container.Outline)
        {
            var point = container.ToRootPoint(outlinePoint);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        var containerIndex = Array.IndexOf(containers, container);
        if (containerIndex < 0)
        {
            throw new InvalidOperationException("Cannot seed liquid into an unknown container.");
        }

        var seeded = 0;
        var rowSpacing = DropRadius * 1.7320508f;
        var minimumDistanceSquared = DropRadius * DropRadius;
        var row = 0;
        for (var y = minimum.y + DropRadius;
            y <= maximum.y - DropRadius && seeded < amount && count < MaximumDrops;
            y += rowSpacing, row++)
        {
            var xOffset = (row & 1) == 0 ? 0f : DropRadius;
            for (var x = minimum.x + DropRadius + xOffset;
                x <= maximum.x - DropRadius && seeded < amount && count < MaximumDrops;
                x += DropRadius * 2f)
            {
                var candidate = new Vector2(x, y);
                var classification = container.Classify(candidate);
                if (!classification.IsInside || classification.DistanceSquared < minimumDistanceSquared)
                {
                    continue;
                }

                AddDrop(candidate, Vector2.zero, (byte)(containerIndex + 1));
                seeded++;
            }
        }

        if (seeded < amount)
        {
            Plugin.LogWarning(
                $"Could only seed {seeded} of {amount} initial drops in container {containerIndex + 1}.");
        }
    }

    private static int GetInitialDropCount(float amount)
    {
        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
        {
            return 0;
        }

        return amount >= MaximumDrops
            ? MaximumDrops
            : Mathf.RoundToInt(amount);
    }

    private void Spawn(Vector2 position)
    {
        if (totalLiquidUnits >= MaximumDrops)
        {
            return;
        }

        var angle = NextRandom() * Mathf.PI * 2f;
        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (NextRandom() * DropRadius);
        AddDrop(position + offset, new Vector2(
            (NextRandom() * 2f - 1f) * 24f,
            (NextRandom() * 2f - 1f) * 12f));
    }

    private void AddDrop(Vector2 position, Vector2 velocity, byte containerAssignment = 0)
    {
        if (count >= MaximumDrops)
        {
            return;
        }

        positions[count] = position;
        previousPositions[count] = position;
        velocities[count] = velocity;
        dropIds[count] = nextDropId++;
        dropVolumes[count] = 1;
        containerAssignments[count] = containerAssignment == 0
            ? GetContainerAssignment(position, DropRadius)
            : containerAssignment;
        count++;
        totalLiquidUnits++;
        graphicsDirty = true;
    }

    private void Step(float deltaTime, ref int mergesRemaining, bool runSplitter)
    {
        var profilingStart = SimulationProfiler.Begin();
        RefreshContainerGeometry();
        for (var i = 0; i < count; i++)
        {
            previousPositions[i] = positions[i];
            velocities[i].y -= Gravity * GravityMultiplier * deltaTime;
            positions[i] += velocities[i] * deltaTime;
        }
        SimulationProfiler.End(SimulationProfileStage.GeometryRefreshAndIntegration, profilingStart);
        ResolveDropCollisions(ref mergesRemaining, runSplitter);

        profilingStart = SimulationProfiler.Begin();
        for (var i = 0; i < count; i++)
        {
            velocities[i] = (positions[i] - previousPositions[i]) / deltaTime;
        }
        SimulationProfiler.End(SimulationProfileStage.VelocityReconstruction, profilingStart);

        profilingStart = SimulationProfiler.Begin();
        ResolveDropVelocities();
        SimulationProfiler.End(SimulationProfileStage.VelocityConstraints, profilingStart);

        profilingStart = SimulationProfiler.Begin();
        LogBottomSourceDrop();
        RemoveOffscreenDrops();
        SimulationProfiler.End(SimulationProfileStage.OffscreenCleanup, profilingStart);
        graphicsDirty = true;
    }

    private void RefreshContainerGeometry()
    {
        foreach (var container in containers)
        {
            container.RefreshBoundaries();
        }
    }

    private void RemoveOffscreenDrops()
    {
        for (var i = 0; i < count;)
        {
            var position = positions[i];
            var fullyOutsideScreen = IsOutsideScreen(position, GetDropRadius(i));
            if (!fullyOutsideScreen || IsInsideContainer(position))
            {
                i++;
                continue;
            }

            var deletedVolume = dropVolumes[i];
            if (DebugLog.IsFileEnabled)
            {
                offscreenDeletedThisInterval++;
                offscreenUnitsDeletedThisInterval += deletedVolume;
                if (offscreenSamplesThisInterval < 3)
                {
                    DebugLog.Write(
                        $"Off-screen removal: id={dropIds[i]}, volume={deletedVolume}, "
                        + $"radius={GetDropRadius(i):F2}, position=({position.x:F2},{position.y:F2}), "
                        + $"assignment={containerAssignments[i]}.");
                    offscreenSamplesThisInterval++;
                }
            }

            totalLiquidUnits -= deletedVolume;
            count--;
            positions[i] = positions[count];
            previousPositions[i] = previousPositions[count];
            velocities[i] = velocities[count];
            dropIds[i] = dropIds[count];
            dropVolumes[i] = dropVolumes[count];
            containerAssignments[i] = containerAssignments[count];
            offscreenDeleted++;
        }
    }

    private int CountParticleUnits(
        out int singleParticles,
        out int aggregateParticles,
        out int invalidVolumeParticles)
    {
        var units = 0;
        singleParticles = 0;
        aggregateParticles = 0;
        invalidVolumeParticles = 0;
        for (var i = 0; i < count; i++)
        {
            var volume = dropVolumes[i];
            units += volume;
            if (volume == 1)
            {
                singleParticles++;
            }
            else if (volume == AggregateVolume)
            {
                aggregateParticles++;
            }
            else
            {
                invalidVolumeParticles++;
            }
        }

        return units;
    }

    private bool IsOutsideScreen(Vector2 position, float radius)
    {
        return position.x < minimumScreenX - radius
            || position.x > maximumScreenX + radius
            || position.y < minimumScreenY - radius
            || position.y > maximumScreenY + radius;
    }

    private void LogBottomSourceDrop()
    {
        if (!DebugLog.IsFileEnabled || containers.Length < 2)
        {
            return;
        }

        var source = containers[1];
        var minimumX = source.Minimum.x - DropRadius;
        var maximumX = source.Maximum.x + DropRadius;
        var bottomIndex = -1;
        var bottomY = float.PositiveInfinity;
        for (var i = 0; i < count; i++)
        {
            var position = positions[i];
            if (position.x < minimumX || position.x > maximumX || position.y >= bottomY)
            {
                continue;
            }

            bottomY = position.y;
            bottomIndex = i;
        }

        if (bottomIndex >= 0)
        {
            var position = positions[bottomIndex];
            DebugLog.Write(
                $"Bottom source drop id={dropIds[bottomIndex]} position=({position.x:F2},{position.y:F2}).");
        }
    }

    private byte GetContainerAssignment(Vector2 position, float radius)
    {
        for (var i = 0; i < containers.Length; i++)
        {
            if ((radius <= DropRadius && containers[i].IsInsidePaddedRectangle(position))
                || containers[i].Contains(position))
            {
                return (byte)(i + 1);
            }
        }

        return 0;
    }

    private void EnforceContainerAssignments()
    {
        for (var i = 0; i < count; i++)
        {
            var position = positions[i];
            var radius = GetDropRadius(i);
            var previousPosition = previousPositions[i];
            var velocity = velocities[i];
            var assignment = containerAssignments[i];
            var anyOpeningTransition = false;
            for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
            {
                var container = containers[containerIndex];
                var crossesOpening = container.CrossesOpening(previousPosition, position);
                var nearOpeningBefore = container.IsNearOpening(previousPosition, radius * 2f);
                var nearOpeningAfter = container.IsNearOpening(position, radius * 2f);
                openingTransitions[containerIndex] =
                    crossesOpening || nearOpeningBefore || nearOpeningAfter;
                if (dropVolumes[i] == AggregateVolume
                    && !crossesOpening
                    && (nearOpeningBefore || nearOpeningAfter)
                    && DebugLog.IsFileEnabled)
                {
                    aggregateOpeningBypassesThisInterval++;
                    if (openingBypassSamplesThisInterval < 2)
                    {
                        DebugLog.Write(
                            $"Aggregate near-rim exemption: id={dropIds[i]}, "
                            + $"container={containerIndex + 1}, position=({position.x:F2},{position.y:F2}), "
                            + $"previous=({previousPosition.x:F2},{previousPosition.y:F2}), "
                            + $"radius={radius:F2}, openingDistance={container.GetOpeningDistance(position):F2}.");
                        openingBypassSamplesThisInterval++;
                    }
                }
                anyOpeningTransition |= openingTransitions[containerIndex];
            }

            var assignedContainerOpeningTransition =
                assignment > 0
                && assignment <= containers.Length
                && openingTransitions[assignment - 1];
            if ((assignment == 0 && anyOpeningTransition) || assignedContainerOpeningTransition)
            {
                assignment = GetContainerAssignment(position, radius);
            }

            for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
            {
                var container = containers[containerIndex];
                if (openingTransitions[containerIndex])
                {
                    continue;
                }

                var shouldBeInside = assignment == containerIndex + 1;
                if (shouldBeInside && radius <= DropRadius && container.IsInsidePaddedRectangle(position))
                {
                    continue;
                }

                if (!shouldBeInside && !container.IsNear(position, radius))
                {
                    continue;
                }

                var classification = container.Classify(position);
                if (container.IsNearOpenRimEndpoint(classification, radius))
                {
                    continue;
                }

                var isInside = classification.IsInside;
                if (isInside != shouldBeInside)
                {
                    if (container.TryGetPositionOnSide(position, shouldBeInside, radius, out var correctedPosition))
                    {
                        var correctionNormal = (correctedPosition - position).normalized;
                        position = correctedPosition;
                        velocity = RemoveInwardBoundaryVelocity(velocity, correctionNormal);
                        assignmentCorrections++;
                    }

                    continue;
                }

                if (classification.DistanceSquared >= radius * radius)
                {
                    continue;
                }

                var normal = container.GetSideNormal(position, classification, shouldBeInside);
                var distance = Mathf.Sqrt(classification.DistanceSquared);
                position += normal * (radius - distance + 0.01f);
                velocity = RemoveInwardBoundaryVelocity(velocity, normal);
                assignmentCorrections++;
            }

            positions[i] = position;
            velocities[i] = velocity;
            containerAssignments[i] = assignment;
        }
    }

    private static Vector2 RemoveInwardBoundaryVelocity(Vector2 velocity, Vector2 normal)
    {
        var normalVelocity = Vector2.Dot(velocity, normal);
        if (normalVelocity < 0f)
        {
            velocity -= normalVelocity * normal;
            velocity *= 0.995f;
        }

        return velocity;
    }

    private void BuildSpatialGrid()
    {
        Array.Clear(cellCounts, 0, cellCounts.Length);
        for (var i = 0; i < count; i++)
        {
            cellCounts[GetCell(positions[i])]++;
        }

        var particleOffset = 0;
        for (var cell = 0; cell < cellCounts.Length; cell++)
        {
            cellOffsets[cell] = particleOffset;
            cellWriteOffsets[cell] = particleOffset;
            particleOffset += cellCounts[cell];
        }
        cellOffsets[cellCounts.Length] = particleOffset;

        for (var i = 0; i < count; i++)
        {
            var cell = GetCell(positions[i]);
            cellParticleIndices[cellWriteOffsets[cell]++] = i;
        }

        ReorderParticlesByCell();
    }

    private int AggregatePackedDrops(int maximumMerges = int.MaxValue)
    {
        if (maximumMerges <= 0)
        {
            return 0;
        }

        Array.Clear(aggregationConsumed, 0, count);
        var mergedCount = 0;
        var ringToleranceSquared = AggregateRingTolerance * AggregateRingTolerance;
        for (var center = 0; center < count; center++)
        {
            if (dropVolumes[center] != 1 || aggregationConsumed[center])
            {
                continue;
            }

            var centerPosition = positions[center];
            aggregateMembers[0] = center;
            var completeRing = true;
            for (var directionIndex = 0; directionIndex < AggregateRingDirections.Length; directionIndex++)
            {
                var target = centerPosition
                    + AggregateRingDirections[directionIndex] * (DropRadius * 2f);
                var targetCell = GetCell(target);
                var targetCellX = targetCell % gridColumns;
                var targetCellY = targetCell / gridColumns;
                var nearest = -1;
                var nearestDistanceSquared = ringToleranceSquared;
                for (var cellY = Mathf.Max(0, targetCellY - 1);
                     cellY <= Mathf.Min(gridRows - 1, targetCellY + 1);
                     cellY++)
                {
                    for (var cellX = Mathf.Max(0, targetCellX - 1);
                         cellX <= Mathf.Min(gridColumns - 1, targetCellX + 1);
                         cellX++)
                    {
                        var cell = cellY * gridColumns + cellX;
                        for (var index = cellOffsets[cell]; index < cellOffsets[cell + 1]; index++)
                        {
                            if (index == center
                                || dropVolumes[index] != 1
                                || aggregationConsumed[index]
                                || containerAssignments[index] != containerAssignments[center])
                            {
                                continue;
                            }

                            var alreadySelected = false;
                            for (var member = 0; member <= directionIndex; member++)
                            {
                                alreadySelected |= aggregateMembers[member] == index;
                            }

                            if (alreadySelected)
                            {
                                continue;
                            }

                            var distanceSquared = (positions[index] - target).sqrMagnitude;
                            if (distanceSquared < nearestDistanceSquared)
                            {
                                nearestDistanceSquared = distanceSquared;
                                nearest = index;
                            }
                        }
                    }
                }

                if (nearest < 0)
                {
                    completeRing = false;
                    break;
                }

                aggregateMembers[directionIndex + 1] = nearest;
            }

            if (!completeRing)
            {
                continue;
            }

            var containerAssignment = containerAssignments[center];
            if (containerAssignment > 0)
            {
                var classification = containers[containerAssignment - 1].Classify(centerPosition);
                if (!classification.IsInside
                    || classification.DistanceSquared < AggregateRadius * AggregateRadius)
                {
                    continue;
                }
            }

            if (CountAggregateSupport(
                    centerPosition,
                    AggregateRadius,
                    -1,
                    aggregateMembers,
                    AggregateVolume,
                    AggregateMinimumSupport) < AggregateMinimumSupport)
            {
                continue;
            }

            var aggregatePosition = Vector2.zero;
            var aggregatePreviousPosition = Vector2.zero;
            var aggregateVelocity = Vector2.zero;
            for (var member = 0; member < AggregateVolume; member++)
            {
                var index = aggregateMembers[member];
                aggregationConsumed[index] = true;
                aggregatePosition += positions[index];
                aggregatePreviousPosition += previousPositions[index];
                aggregateVelocity += velocities[index];
            }

            positions[center] = aggregatePosition / AggregateVolume;
            previousPositions[center] = aggregatePreviousPosition / AggregateVolume;
            velocities[center] = aggregateVelocity / AggregateVolume;
            dropVolumes[center] = AggregateVolume;
            for (var member = 1; member < AggregateVolume; member++)
            {
                dropVolumes[aggregateMembers[member]] = 0;
            }
            if (DebugLog.IsFileEnabled)
            {
                mergedAggregatesThisInterval++;
            }
            mergedCount++;
            if (mergedCount >= maximumMerges)
            {
                break;
            }
        }

        if (mergedCount == 0)
        {
            return 0;
        }

        CompactRemovedParticles();
        graphicsDirty = true;
        return mergedCount;
    }

    private int SplitUnsupportedAggregates(int maximumSplits)
    {
        if (maximumSplits <= 0)
        {
            return 0;
        }

        var originalCount = count;
        var splitCount = 0;
        Array.Clear(aggregationConsumed, 0, originalCount);
        for (var aggregate = 0; aggregate < originalCount; aggregate++)
        {
            if (dropVolumes[aggregate] != AggregateVolume)
            {
                continue;
            }

            var aggregatePosition = positions[aggregate];
            var supportCount = CountAggregateSupport(
                aggregatePosition,
                GetDropRadius(aggregate),
                aggregate,
                null,
                0,
                AggregateMinimumSplitSupport);
            if (supportCount >= AggregateMinimumSplitSupport
                || count + AggregateVolume - 1 > MaximumDrops)
            {
                continue;
            }

            aggregationConsumed[aggregate] = true;
            splitCount++;
            if (splitCount >= maximumSplits)
            {
                break;
            }
        }

        var selectedSplitCount = splitCount;
        splitCount = 0;
        for (var aggregate = 0;
             aggregate < originalCount && splitCount < selectedSplitCount;
             aggregate++)
        {
            if (!aggregationConsumed[aggregate])
            {
                continue;
            }

            var aggregatePosition = positions[aggregate];
            var aggregatePreviousPosition = previousPositions[aggregate];
            dropVolumes[aggregate] = 1;
            for (var member = 0; member < AggregateRingDirections.Length; member++)
            {
                var childPosition = aggregatePosition
                    + AggregateRingDirections[member] * (DropRadius * 2f);
                var index = count++;
                positions[index] = childPosition;
                previousPositions[index] = aggregatePreviousPosition
                    + AggregateRingDirections[member] * (DropRadius * 2f);
                velocities[index] = velocities[aggregate];
                dropIds[index] = nextDropId++;
                dropVolumes[index] = 1;
                containerAssignments[index] = containerAssignments[aggregate];
            }

            splitCount++;
        }

        if (splitCount > 0)
        {
            if (DebugLog.IsFileEnabled)
            {
                splitAggregatesThisInterval += splitCount;
                largestSplitBatchThisInterval = Mathf.Max(largestSplitBatchThisInterval, splitCount);
                if (splitCount >= 25)
                {
                    DebugLog.Write(
                        $"Large aggregate breakup batch: split={splitCount}, "
                        + $"particles={originalCount}->{count}, liquid units={totalLiquidUnits}.");
                }
            }
            graphicsDirty = true;
        }

        return splitCount;
    }

    private int CountAggregateSupport(
        Vector2 position,
        float radius,
        int ignoredIndex,
        int[] excludedIndices,
        int excludedCount,
        int requiredSupport)
    {
        var centerCell = GetCell(position);
        var centerCellX = centerCell % gridColumns;
        var centerCellY = centerCell / gridColumns;
        var supportCount = 0;
        for (var cellY = Mathf.Max(0, centerCellY - 3);
             cellY <= Mathf.Min(gridRows - 1, centerCellY + 3)
                && supportCount < requiredSupport;
             cellY++)
        {
            for (var cellX = Mathf.Max(0, centerCellX - 3);
                 cellX <= Mathf.Min(gridColumns - 1, centerCellX + 3)
                    && supportCount < requiredSupport;
                 cellX++)
            {
                var cell = cellY * gridColumns + cellX;
                for (var index = cellOffsets[cell]; index < cellOffsets[cell + 1]; index++)
                {
                    if (index == ignoredIndex || dropVolumes[index] == 0)
                    {
                        continue;
                    }

                    var isExcluded = false;
                    for (var excluded = 0; excluded < excludedCount; excluded++)
                    {
                        isExcluded |= excludedIndices[excluded] == index;
                    }

                    if (isExcluded)
                    {
                        continue;
                    }

                    var supportDistance = radius + GetDropRadius(index) + DropRadius * 0.5f;
                    if ((positions[index] - position).sqrMagnitude <= supportDistance * supportDistance)
                    {
                        supportCount++;
                        if (supportCount >= requiredSupport)
                        {
                            break;
                        }
                    }
                }
            }
        }

        return supportCount;
    }

    private void CompactRemovedParticles()
    {
        for (var i = 0; i < count;)
        {
            if (dropVolumes[i] != 0)
            {
                i++;
                continue;
            }

            count--;
            positions[i] = positions[count];
            previousPositions[i] = previousPositions[count];
            velocities[i] = velocities[count];
            dropIds[i] = dropIds[count];
            dropVolumes[i] = dropVolumes[count];
            containerAssignments[i] = containerAssignments[count];
        }
    }

    private float GetDropRadius(int index)
    {
        return dropVolumes[index] == AggregateVolume ? AggregateRadius : DropRadius;
    }

    private void ReorderParticlesByCell()
    {
        for (var sortedIndex = 0; sortedIndex < count; sortedIndex++)
        {
            var sourceIndex = cellParticleIndices[sortedIndex];
            reorderedPositions[sortedIndex] = positions[sourceIndex];
            reorderedPreviousPositions[sortedIndex] = previousPositions[sourceIndex];
            reorderedVelocities[sortedIndex] = velocities[sourceIndex];
            reorderedDropIds[sortedIndex] = dropIds[sourceIndex];
            reorderedDropVolumes[sortedIndex] = dropVolumes[sourceIndex];
            reorderedContainerAssignments[sortedIndex] = containerAssignments[sourceIndex];
        }

        SwapBuffers(ref positions, ref reorderedPositions);
        SwapBuffers(ref previousPositions, ref reorderedPreviousPositions);
        SwapBuffers(ref velocities, ref reorderedVelocities);
        SwapBuffers(ref dropIds, ref reorderedDropIds);
        SwapBuffers(ref dropVolumes, ref reorderedDropVolumes);
        SwapBuffers(ref containerAssignments, ref reorderedContainerAssignments);
    }

    private static void SwapBuffers<T>(ref T[] current, ref T[] reordered)
    {
        var previous = current;
        current = reordered;
        reordered = previous;
    }

    private void ResolveDropVelocities()
    {
        BuildSpatialGrid();
        ResolveSpatialGridPairs(true);
    }

    private void ResolveDropCollisions(ref int mergesRemaining, bool runSplitter)
    {
        for (var iteration = 0; iteration < DropCollisionIterations; iteration++)
        {
            var profilingStart = SimulationProfiler.Begin();
            BuildSpatialGrid();
            if (iteration == 0)
            {
                var mergedCount = AggregatePackedDrops(mergesRemaining);
                mergesRemaining -= mergedCount;
                if (mergedCount > 0)
                {
                    BuildSpatialGrid();
                }

                var splitCount = runSplitter ? SplitUnsupportedAggregates(int.MaxValue) : 0;
                if (splitCount > 0)
                {
                    BuildSpatialGrid();
                }
            }
            SimulationProfiler.End(SimulationProfileStage.SpatialGrid, profilingStart);

            profilingStart = SimulationProfiler.Begin();
            ResolveDropCollisionPass();
            SimulationProfiler.End(SimulationProfileStage.DropCollisions, profilingStart);

            if (iteration == 0 || iteration == DropCollisionIterations - 1)
            {
                profilingStart = SimulationProfiler.Begin();
                EnforceContainerAssignments();
                SimulationProfiler.End(SimulationProfileStage.AssignmentChecks, profilingStart);
            }
        }
    }

    private void ResolveDropCollisionPass()
    {
        ResolveSpatialGridPairs(false);
    }

    private void ResolveSpatialGridPairs(bool velocityOnly)
    {
        for (var cellY = 0; cellY < gridRows; cellY++)
        {
            for (var cellX = 0; cellX < gridColumns; cellX++)
            {
                var cell = cellY * gridColumns + cellX;
                ResolveCellPair(cell, cell, velocityOnly);

                if (cellX + 1 < gridColumns)
                {
                    ResolveCellPair(cell, cell + 1, velocityOnly);
                }

                if (cellY + 1 >= gridRows)
                {
                    continue;
                }

                if (cellX > 0)
                {
                    ResolveCellPair(
                        cell,
                        cell + gridColumns - 1,
                        velocityOnly);
                }

                ResolveCellPair(cell, cell + gridColumns, velocityOnly);
                if (cellX + 1 < gridColumns)
                {
                    ResolveCellPair(
                        cell,
                        cell + gridColumns + 1,
                        velocityOnly);
                }
            }
        }

        ResolveAggregatePairs(velocityOnly);
    }

    private void ResolveAggregatePairs(bool velocityOnly)
    {
        var cellRadius = Mathf.CeilToInt(
            (AggregateRadius * 2f + (velocityOnly ? 0.5f : 0f)) / cellSize);
        for (var first = 0; first < count; first++)
        {
            if (dropVolumes[first] != AggregateVolume)
            {
                continue;
            }

            var centerCell = GetCell(positions[first]);
            var centerCellX = centerCell % gridColumns;
            var centerCellY = centerCell / gridColumns;
            for (var cellY = Mathf.Max(0, centerCellY - cellRadius);
                 cellY <= Mathf.Min(gridRows - 1, centerCellY + cellRadius);
                 cellY++)
            {
                for (var cellX = Mathf.Max(0, centerCellX - cellRadius);
                     cellX <= Mathf.Min(gridColumns - 1, centerCellX + cellRadius);
                     cellX++)
                {
                    var cell = cellY * gridColumns + cellX;
                    for (var secondOffset = cellOffsets[cell]; secondOffset < cellOffsets[cell + 1]; secondOffset++)
                    {
                        var second = secondOffset;
                        if (second == first
                            || (dropVolumes[second] == AggregateVolume && second < first))
                        {
                            continue;
                        }

                        if (velocityOnly)
                        {
                            ResolvePairVelocity(first, second);
                        }
                        else
                        {
                            ResolvePair(first, second);
                        }
                    }
                }
            }
        }
    }

    private void ResolveCellPair(
        int firstCell,
        int secondCell,
        bool velocityOnly)
    {
        if (firstCell == secondCell)
        {
            for (var firstIndex = cellOffsets[firstCell];
                 firstIndex < cellOffsets[firstCell + 1];
                 firstIndex++)
            {
                var first = firstIndex;
                for (var secondIndex = firstIndex + 1;
                     secondIndex < cellOffsets[firstCell + 1];
                     secondIndex++)
                {
                    ResolvePairForPass(
                        first,
                        secondIndex,
                        velocityOnly);
                }
            }

            return;
        }

        for (var firstIndex = cellOffsets[firstCell];
             firstIndex < cellOffsets[firstCell + 1];
             firstIndex++)
        {
            var first = firstIndex;
            for (var secondIndex = cellOffsets[secondCell];
                 secondIndex < cellOffsets[secondCell + 1];
                 secondIndex++)
            {
                ResolvePairForPass(
                    first,
                    secondIndex,
                    velocityOnly);
            }
        }
    }

    private void ResolvePairForPass(
        int first,
        int second,
        bool velocityOnly)
    {
        if (dropVolumes[first] == AggregateVolume || dropVolumes[second] == AggregateVolume)
        {
            return;
        }

        if (velocityOnly)
        {
            ResolvePairVelocity(first, second);
        }
        else
        {
            ResolvePair(first, second);
        }
    }

    private void ResolvePair(int first, int second)
    {
        var difference = positions[second] - positions[first];
        var distanceSquared = difference.sqrMagnitude;
        var firstRadius = GetDropRadius(first);
        var secondRadius = GetDropRadius(second);
        var minimumDistance = firstRadius + secondRadius;
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        if (distanceSquared >= minimumDistanceSquared)
        {
            return;
        }

        var distance = Mathf.Sqrt(distanceSquared);
        var normal = distance > 0.0001f
            ? difference / distance
            : new Vector2(1f, 0f);
        var overlap = minimumDistance - distance;
        var firstVolume = dropVolumes[first];
        var secondVolume = dropVolumes[second];
        var totalVolume = firstVolume + secondVolume;
        positions[first] -= normal * (overlap * secondVolume / totalVolume);
        positions[second] += normal * (overlap * firstVolume / totalVolume);
    }

    private void ResolvePairVelocity(int first, int second)
    {
        var difference = positions[second] - positions[first];
        var distanceSquared = difference.sqrMagnitude;
        var minimumDistance = GetDropRadius(first) + GetDropRadius(second) + 0.5f;
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        if (distanceSquared > minimumDistanceSquared)
        {
            return;
        }

        var distance = Mathf.Sqrt(distanceSquared);
        var normal = distance > 0.0001f
            ? difference / distance
            : new Vector2(1f, 0f);
        var relativeVelocity = velocities[second] - velocities[first];
        var separatingSpeed = Vector2.Dot(relativeVelocity, normal);
        if (separatingSpeed >= 0f)
        {
            return;
        }

        var firstVolume = dropVolumes[first];
        var secondVolume = dropVolumes[second];
        var impulse = -separatingSpeed / (1f / firstVolume + 1f / secondVolume);
        velocities[first] -= normal * (impulse / firstVolume);
        velocities[second] += normal * (impulse / secondVolume);
    }

    private int GetCell(Vector2 position)
    {
        var x = Mathf.Clamp(
            Mathf.FloorToInt((position.x - minimumScreenX) / cellSize),
            0,
            gridColumns - 1);
        var y = Mathf.Clamp(
            Mathf.FloorToInt((position.y - minimumScreenY) / cellSize),
            0,
            gridRows - 1);
        return y * gridColumns + x;
    }

    private void CreateGraphics()
    {
        var width = maximumScreenX - minimumScreenX;
        var height = maximumScreenY - minimumScreenY;
        densityTextureWidth = Mathf.Clamp(
            Mathf.CeilToInt(width * DensityTexturePixelsPerUnit),
            128,
            MaximumDensityTextureDimension);
        densityTextureHeight = Mathf.Clamp(
            Mathf.CeilToInt(height * DensityTexturePixelsPerUnit),
            128,
            MaximumDensityTextureDimension);
        densityPixels = new Color32[densityTextureWidth * densityTextureHeight];
        densityVelocityFields = new Vector3[densityPixels.Length];
        activeDensityPixelIndices = new int[densityPixels.Length];
        for (var i = 0; i < densityPixels.Length; i++)
        {
            densityPixels[i] = new Color32(0, 128, 128, 0);
        }

        densityTexture = new Texture2D(
            densityTextureWidth,
            densityTextureHeight,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "LiquidDensity",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        densityTexture.SetPixels32(densityPixels);
        densityTexture.Apply(false, false);

        surfaceGraphic = new GameObject(
            "LiquidSurface",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(LiquidSurfaceGraphic)).GetComponent<LiquidSurfaceGraphic>();
        var rect = surfaceGraphic.rectTransform;
        rect.SetParent(root, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(
            (minimumScreenX + maximumScreenX) * 0.5f,
            (minimumScreenY + maximumScreenY) * 0.5f);
        surfaceGraphic.color = new Color(0.16f, 0.5f, 0.95f, 1f);
        surfaceGraphic.SetDensityTexture(densityTexture);
        if (LiquidShaderAssets.SurfaceMaterial)
        {
            surfaceGraphic.material = LiquidShaderAssets.SurfaceMaterial;
        }

        surfaceGraphic.raycastTarget = false;
        rect.SetAsFirstSibling();
        var graphicCount = Mathf.CeilToInt((float)MaximumDrops / DropsPerGraphic);
        var screenCenter = new Vector2(
            (minimumScreenX + maximumScreenX) * 0.5f,
            (minimumScreenY + maximumScreenY) * 0.5f);
        for (var i = 0; i < graphicCount; i++)
        {
            var graphic = new GameObject(
                $"IndividualDrops{i}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(LiquidDropGraphic)).GetComponent<LiquidDropGraphic>();
            var graphicRect = graphic.rectTransform;
            graphicRect.SetParent(root, false);
            graphicRect.anchorMin = new Vector2(0.5f, 0.5f);
            graphicRect.anchorMax = new Vector2(0.5f, 0.5f);
            graphicRect.pivot = new Vector2(0.5f, 0.5f);
            graphicRect.sizeDelta = new Vector2(width, height);
            graphicRect.anchoredPosition = screenCenter;
            graphic.color = new Color(0.16f, 0.5f, 0.95f, 1f);
            graphic.raycastTarget = false;
            graphic.enabled = false;
            graphicRect.SetAsFirstSibling();
            individualDropGraphics.Add(graphic);
        }

        DebugLog.Write(
            $"Created liquid density surface ({densityTextureWidth}x{densityTextureHeight}) with material "
            + $"'{(LiquidShaderAssets.SurfaceMaterial ? LiquidShaderAssets.SurfaceMaterial.name : "Unity UI default")}'.");
    }

    private void UpdateGraphics()
    {
        if (!surfaceShaderEnabled)
        {
            UpdateIndividualDropGraphics();
            return;
        }

        for (var index = 0; index < activeDensityPixelCount; index++)
        {
            var pixelIndex = activeDensityPixelIndices[index];
            densityPixels[pixelIndex] = new Color32(0, 128, 128, 0);
            densityVelocityFields[pixelIndex] = Vector3.zero;
        }
        activeDensityPixelCount = 0;

        var xScale = (densityTextureWidth - 1) / (maximumScreenX - minimumScreenX);
        var yScale = (densityTextureHeight - 1) / (maximumScreenY - minimumScreenY);
        for (var drop = 0; drop < count; drop++)
        {
            var position = positions[drop];
            var supportRadius = GetDropRadius(drop) * 2.2f;
            var supportRadiusSquared = supportRadius * supportRadius;
            var velocity = velocities[drop];
            var speed = velocity.magnitude;
            var direction = speed > 0.001f ? velocity / speed : Vector2.zero;
            var tailLength = direction.sqrMagnitude > 0f
                ? GetDropRadius(drop) * 2.5f * Mathf.Clamp01(speed / TailSpeedForMaximumLength)
                : 0f;
            var centerX = Mathf.RoundToInt((position.x - minimumScreenX) * xScale);
            var centerY = Mathf.RoundToInt((position.y - minimumScreenY) * yScale);
            var radiusX = (supportRadius + tailLength) * xScale;
            var radiusY = (supportRadius + tailLength) * yScale;
            var majorRadius = supportRadius + tailLength * 0.5f;
            var majorRadiusSquared = majorRadius * majorRadius;
            var firstX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusX));
            var lastX = Mathf.Min(densityTextureWidth - 1, Mathf.CeilToInt(centerX + radiusX));
            var firstY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusY));
            var lastY = Mathf.Min(densityTextureHeight - 1, Mathf.CeilToInt(centerY + radiusY));
            for (var y = firstY; y <= lastY; y++)
            {
                var dy = (y - centerY) / yScale;
                var row = y * densityTextureWidth;
                for (var x = firstX; x <= lastX; x++)
                {
                    var dx = (x - centerX) / xScale;
                    var along = dx * direction.x + dy * direction.y + tailLength * 0.5f;
                    var across = -dx * direction.y + dy * direction.x;
                    var normalizedDistanceSquared = direction.sqrMagnitude > 0f
                        ? along * along / majorRadiusSquared + across * across / supportRadiusSquared
                        : (dx * dx + dy * dy) / supportRadiusSquared;
                    var baseRemaining = Mathf.Max(0f, 1f - normalizedDistanceSquared);
                    var contribution = DensityContribution * baseRemaining * baseRemaining;

                    if (contribution <= 0f)
                    {
                        continue;
                    }

                    var pixelIndex = row + x;
                    var velocityField = densityVelocityFields[pixelIndex];
                    if (velocityField.x <= 0f)
                    {
                        activeDensityPixelIndices[activeDensityPixelCount++] = pixelIndex;
                    }

                    var density = Mathf.Min(
                        byte.MaxValue,
                        densityPixels[pixelIndex].r + Mathf.RoundToInt(contribution * byte.MaxValue));
                    densityPixels[pixelIndex].r = (byte)density;

                    velocityField.x += contribution;
                    velocityField.y += velocity.x * contribution;
                    velocityField.z += velocity.y * contribution;
                    densityVelocityFields[pixelIndex] = velocityField;
                }
            }
        }

        for (var index = 0; index < activeDensityPixelCount; index++)
        {
            var i = activeDensityPixelIndices[index];
            var velocityWeight = densityVelocityFields[i].x;
            var averageVelocityX = Mathf.Clamp(
                densityVelocityFields[i].y / velocityWeight / MaximumEncodedVelocity,
                -1f,
                1f);
            var averageVelocityY = Mathf.Clamp(
                densityVelocityFields[i].z / velocityWeight / MaximumEncodedVelocity,
                -1f,
                1f);
            densityPixels[i].g = (byte)Mathf.RoundToInt((averageVelocityX * 0.5f + 0.5f) * byte.MaxValue);
            densityPixels[i].b = (byte)Mathf.RoundToInt((averageVelocityY * 0.5f + 0.5f) * byte.MaxValue);
            densityPixels[i].a = byte.MaxValue;
        }

        densityTexture.SetPixels32(densityPixels);
        densityTexture.Apply(false, false);
    }

    private void UpdateIndividualDropGraphics()
    {
        var screenCenter = new Vector2(
            (minimumScreenX + maximumScreenX) * 0.5f,
            (minimumScreenY + maximumScreenY) * 0.5f);
        for (var i = 0; i < individualDropGraphics.Count; i++)
        {
            var start = i * DropsPerGraphic;
            var length = Mathf.Clamp(count - start, 0, DropsPerGraphic);
            individualDropGraphics[i].SetDrops(
                positions,
                dropVolumes,
                start,
                length,
                IndividualDropRadius,
                screenCenter);
        }
    }

    private float NextRandom()
    {
        randomState ^= randomState << 13;
        randomState ^= randomState >> 17;
        randomState ^= randomState << 5;
        return (randomState & 0x00FFFFFFu) / 16777216f;
    }

    private sealed class ContainerShape
    {
     private const float DistanceFieldCellSize = 8f;
     private const float DistanceFieldPadding = 64f;
     private readonly RectTransform root;
     private readonly RectTransform transform;
     private readonly BoundarySegment[] boundaries;
     private readonly float interiorOrientation;
     private readonly Vector2 interiorFallback;
     private readonly Vector2 distanceFieldMinimum;
     private readonly int distanceFieldColumns;
     private readonly int distanceFieldRows;
     private readonly float[] signedDistanceField;
     private readonly Vector2[] distanceFieldGradients;
     private Matrix4x4 rootToContainerMatrix;
     private Rect paddedInteriorRectangle;
     private float paddedInteriorRectanglePadding = -1f;
     private Vector2 openingStart;
     private Vector2 openingEnd;
     private Vector2 openingSegment;
     private float openingLengthSquared;
     private float rootUnitsPerLocalUnit = 1f;

     internal ContainerShape(RectTransform root, RectTransform transform, Vector2[] outline)
     {
         this.root = root;
         this.transform = transform;
         Outline = outline ?? Array.Empty<Vector2>();
         boundaries = new BoundarySegment[Mathf.Max(0, Outline.Length - 1)];

         var area = 0f;
         for (var i = 0; i < Outline.Length; i++)
         {
             var next = Outline[(i + 1) % Outline.Length];
             area += Outline[i].x * next.y - next.x * Outline[i].y;
         }

         interiorOrientation = area >= 0f ? 1f : -1f;
         if (Outline.Length < 3)
         {
             distanceFieldMinimum = Vector2.zero;
             distanceFieldColumns = 0;
             distanceFieldRows = 0;
             signedDistanceField = Array.Empty<float>();
             distanceFieldGradients = Array.Empty<Vector2>();
         }
         else
         {
             var minimum = Outline[0];
             var maximum = minimum;
             foreach (var point in Outline)
             {
                 minimum = Vector2.Min(minimum, point);
                 maximum = Vector2.Max(maximum, point);
             }

             distanceFieldMinimum = minimum - Vector2.one * DistanceFieldPadding;
             var fieldSize = maximum - minimum + Vector2.one * (DistanceFieldPadding * 2f);
             distanceFieldColumns = Mathf.CeilToInt(fieldSize.x / DistanceFieldCellSize) + 1;
             distanceFieldRows = Mathf.CeilToInt(fieldSize.y / DistanceFieldCellSize) + 1;
             signedDistanceField = new float[distanceFieldColumns * distanceFieldRows];
             distanceFieldGradients = new Vector2[signedDistanceField.Length];
             BuildDistanceField();
         }

         interiorFallback = FindInteriorFallback();
         RefreshBoundaries();
     }

     internal Vector2[] Outline { get; }
     internal RectTransform Transform => transform;
     internal Vector2 Minimum { get; private set; }
     internal Vector2 Maximum { get; private set; }

     internal Vector2 ToRootPoint(Vector2 localPoint)
     {
         return root.InverseTransformPoint(transform.TransformPoint(localPoint));
     }

     internal void RefreshBoundaries()
     {
         rootToContainerMatrix = transform.worldToLocalMatrix * root.localToWorldMatrix;
         if (Outline.Length == 0)
         {
             return;
         }

         var rootOrigin = ToRootPoint(Vector2.zero);
         var rootUnitsX = (ToRootPoint(Vector2.right) - rootOrigin).magnitude;
         var rootUnitsY = (ToRootPoint(Vector2.up) - rootOrigin).magnitude;
         rootUnitsPerLocalUnit = Mathf.Max(Mathf.Min(rootUnitsX, rootUnitsY), 0.0001f);
         var rectanglePadding = (DropRadius + 0.01f) / rootUnitsPerLocalUnit;
         if (Mathf.Abs(rectanglePadding - paddedInteriorRectanglePadding) > 0.01f)
         {
             paddedInteriorRectangle = FindLargestPaddedInteriorRectangle(rectanglePadding);
             paddedInteriorRectanglePadding = rectanglePadding;
         }

         Minimum = ToRootPoint(Outline[0]);
         Maximum = Minimum;
         openingStart = Minimum;
         openingEnd = ToRootPoint(Outline[Outline.Length - 1]);
         openingSegment = openingEnd - openingStart;
         openingLengthSquared = openingSegment.sqrMagnitude;
         for (var i = 0; i < boundaries.Length; i++)
         {
             var start = ToRootPoint(Outline[i]);
             var end = ToRootPoint(Outline[i + 1]);
             Minimum = Vector2.Min(Minimum, start);
             Maximum = Vector2.Max(Maximum, start);
             boundaries[i] = new BoundarySegment(start, end, interiorOrientation);
         }

         var lastPoint = ToRootPoint(Outline[Outline.Length - 1]);
         Minimum = Vector2.Min(Minimum, lastPoint);
         Maximum = Vector2.Max(Maximum, lastPoint);
     }

     internal bool IsInsidePaddedRectangle(Vector2 rootPoint)
     {
         if (paddedInteriorRectangle.width <= 0f || paddedInteriorRectangle.height <= 0f)
         {
             return false;
         }

         var localPoint = ToLocalPoint(rootPoint);
         return localPoint.x >= paddedInteriorRectangle.xMin
             && localPoint.x <= paddedInteriorRectangle.xMax
             && localPoint.y >= paddedInteriorRectangle.yMin
             && localPoint.y <= paddedInteriorRectangle.yMax;
     }

     private Rect FindLargestPaddedInteriorRectangle(float padding)
     {
         if (distanceFieldColumns < 2 || distanceFieldRows < 2)
         {
             return new Rect();
         }

         var minimumSafeDistance = padding + DistanceFieldCellSize * 0.70710678f;
         var minimumSafeDistanceSquared = minimumSafeDistance * minimumSafeDistance;
         var openingStart = Outline[0];
         var openingSegment = Outline[Outline.Length - 1] - openingStart;
         var openingLengthSquared = openingSegment.sqrMagnitude;
         var heights = new int[distanceFieldColumns];
         var stack = new int[distanceFieldColumns];
         var largestArea = 0L;
         var largestRectangle = new Rect();

         for (var y = 0; y < distanceFieldRows; y++)
         {
             var rowOffset = y * distanceFieldColumns;
             for (var x = 0; x < distanceFieldColumns; x++)
             {
                 var point = distanceFieldMinimum
                     + new Vector2(x * DistanceFieldCellSize, y * DistanceFieldCellSize);
                 var hasEdgePadding = signedDistanceField[rowOffset + x] >= minimumSafeDistance;
                 if (hasEdgePadding && openingLengthSquared > Mathf.Epsilon)
                 {
                     var amount = Mathf.Clamp01(
                         Vector2.Dot(point - openingStart, openingSegment) / openingLengthSquared);
                     var nearestOpeningPoint = openingStart + openingSegment * amount;
                     hasEdgePadding =
                         (point - nearestOpeningPoint).sqrMagnitude >= minimumSafeDistanceSquared;
                 }

                 heights[x] = hasEdgePadding ? heights[x] + 1 : 0;
             }

             var stackCount = 0;
             for (var x = 0; x <= distanceFieldColumns; x++)
             {
                 var currentHeight = x == distanceFieldColumns ? 0 : heights[x];
                 while (stackCount > 0 && heights[stack[stackCount - 1]] > currentHeight)
                 {
                     var height = heights[stack[--stackCount]];
                     var firstColumn = stackCount > 0 ? stack[stackCount - 1] + 1 : 0;
                     var columnCount = x - firstColumn;
                     var area = (long)height * columnCount;
                     if (area <= largestArea)
                     {
                         continue;
                     }

                     var firstRow = y - height + 1;
                     largestArea = area;
                     largestRectangle = Rect.MinMaxRect(
                         distanceFieldMinimum.x + (firstColumn - 0.5f) * DistanceFieldCellSize,
                         distanceFieldMinimum.y + (firstRow - 0.5f) * DistanceFieldCellSize,
                         distanceFieldMinimum.x + (x - 0.5f) * DistanceFieldCellSize,
                         distanceFieldMinimum.y + (y + 0.5f) * DistanceFieldCellSize);
                 }

                 if (x < distanceFieldColumns)
                 {
                     stack[stackCount++] = x;
                 }
             }
         }

         return largestRectangle;
     }

     internal bool IsNear(Vector2 point, float margin)
     {
         return point.x >= Minimum.x - margin
             && point.x <= Maximum.x + margin
             && point.y >= Minimum.y - margin
             && point.y <= Maximum.y + margin;
     }

     internal bool CrossesOpening(Vector2 start, Vector2 end)
     {
         if (Outline.Length < 2)
         {
             return false;
         }

         var movement = end - start;
         var opening = openingEnd - openingStart;
         var denominator = Cross(movement, opening);
         if (Mathf.Abs(denominator) <= Mathf.Epsilon)
         {
             return false;
         }

         var offset = openingStart - start;
         var movementAmount = Cross(offset, opening) / denominator;
         var openingAmount = Cross(offset, movement) / denominator;
         return movementAmount >= 0f
             && movementAmount <= 1f
             && openingAmount > 0f
             && openingAmount < 1f;
     }

     internal bool IsNearOpening(Vector2 point, float margin)
     {
         if (Outline.Length < 2)
         {
             return false;
         }

         var amount = openingLengthSquared <= Mathf.Epsilon
             ? 0f
             : Mathf.Clamp01(Vector2.Dot(point - openingStart, openingSegment) / openingLengthSquared);
         var nearestPoint = openingStart + openingSegment * amount;
         return (point - nearestPoint).sqrMagnitude <= margin * margin;
     }

     internal float GetOpeningDistance(Vector2 point)
     {
         if (Outline.Length < 2)
         {
             return float.PositiveInfinity;
         }

         var amount = openingLengthSquared <= Mathf.Epsilon
             ? 0f
             : Mathf.Clamp01(Vector2.Dot(point - openingStart, openingSegment) / openingLengthSquared);
         var nearestPoint = openingStart + openingSegment * amount;
         return (point - nearestPoint).magnitude;
     }

     private static float Cross(Vector2 first, Vector2 second)
     {
         return first.x * second.y - first.y * second.x;
     }

     internal PointClassification Classify(Vector2 point)
     {
         var localPoint = ToLocalPoint(point);
         if (!TrySampleDistanceField(localPoint, out var signedDistance, out var gradient))
         {
             return ClassifyExact(point);
         }

         var isInside = signedDistance >= 0f;
         var distance = Mathf.Abs(signedDistance) * rootUnitsPerLocalUnit;
         if (distance < AggregateRadius + DistanceFieldCellSize * rootUnitsPerLocalUnit)
         {
             return ClassifyExact(point);
         }

         if (gradient.sqrMagnitude <= 0.0001f)
         {
             return new PointClassification(
                 isInside,
                 point,
                 Vector2.up,
                 distance * distance,
                 -1);
         }

         var localNormal = gradient.normalized;
         var nearestLocalPoint = localPoint - localNormal * signedDistance;
         var nearestPoint = ToRootPoint(nearestLocalPoint);
         var difference = point - nearestPoint;
         var distanceSquared = difference.sqrMagnitude;
         if (distanceSquared <= 0.000001f)
         {
             return ClassifyExact(point);
         }

         var interiorNormal = difference.normalized * (isInside ? 1f : -1f);
         return new PointClassification(
             isInside,
             nearestPoint,
             interiorNormal,
             distanceSquared,
             -1);
     }

     private PointClassification ClassifyExact(Vector2 point)
     {
         var inside = Contains(point);
         var nearestDistanceSquared = float.PositiveInfinity;
         var nearestPoint = Vector2.zero;
         var nearestNormal = Vector2.up;
         var nearestBoundaryIndex = -1;
         for (var boundaryIndex = 0; boundaryIndex < boundaries.Length; boundaryIndex++)
         {
             var boundary = boundaries[boundaryIndex];
             var segment = boundary.End - boundary.Start;
             var lengthSquared = segment.sqrMagnitude;
             if (lengthSquared <= Mathf.Epsilon)
             {
                 continue;
             }

             var along = Mathf.Clamp01(Vector2.Dot(point - boundary.Start, segment) / lengthSquared);
             var candidate = boundary.Start + segment * along;
             var distanceSquared = (point - candidate).sqrMagnitude;
             if (distanceSquared < nearestDistanceSquared)
             {
                 nearestDistanceSquared = distanceSquared;
                 nearestPoint = candidate;
                 nearestNormal = boundary.InteriorNormal;
                 nearestBoundaryIndex = boundaryIndex;
             }
         }

         return new PointClassification(
             inside,
             nearestPoint,
             nearestNormal,
             nearestDistanceSquared,
             nearestBoundaryIndex);
     }

     internal bool IsNearOpenRimEndpoint(PointClassification classification, float margin)
     {
         if (boundaries.Length == 0 || classification.BoundaryIndex < 0)
         {
             return false;
         }

         if (classification.BoundaryIndex == 0
             && (classification.NearestPoint - openingStart).sqrMagnitude <= margin * margin)
         {
             return true;
         }

         return classification.BoundaryIndex == boundaries.Length - 1
             && (classification.NearestPoint - openingEnd).sqrMagnitude <= margin * margin;
     }

     private bool TrySampleDistanceField(
         Vector2 point,
         out float signedDistance,
         out Vector2 gradient)
     {
         var x = (point.x - distanceFieldMinimum.x) / DistanceFieldCellSize;
         var y = (point.y - distanceFieldMinimum.y) / DistanceFieldCellSize;
         if (x < 0f || y < 0f || x >= distanceFieldColumns - 1 || y >= distanceFieldRows - 1)
         {
             signedDistance = 0f;
             gradient = Vector2.zero;
             return false;
         }

         var cellX = Mathf.FloorToInt(x);
         var cellY = Mathf.FloorToInt(y);
         var amountX = x - cellX;
         var amountY = y - cellY;
         var bottomLeft = cellY * distanceFieldColumns + cellX;
         var bottomRight = bottomLeft + 1;
         var topLeft = bottomLeft + distanceFieldColumns;
         var topRight = topLeft + 1;
         signedDistance = Mathf.Lerp(
             Mathf.Lerp(signedDistanceField[bottomLeft], signedDistanceField[bottomRight], amountX),
             Mathf.Lerp(signedDistanceField[topLeft], signedDistanceField[topRight], amountX),
             amountY);
         gradient = Vector2.Lerp(
             Vector2.Lerp(distanceFieldGradients[bottomLeft], distanceFieldGradients[bottomRight], amountX),
             Vector2.Lerp(distanceFieldGradients[topLeft], distanceFieldGradients[topRight], amountX),
             amountY);
         return true;
     }

     private void BuildDistanceField()
     {
         for (var y = 0; y < distanceFieldRows; y++)
         {
             for (var x = 0; x < distanceFieldColumns; x++)
             {
                 var point = distanceFieldMinimum
                     + new Vector2(x * DistanceFieldCellSize, y * DistanceFieldCellSize);
                 var inside = false;
                 var nearestDistanceSquared = float.PositiveInfinity;
                 for (int i = 0, previous = Outline.Length - 1; i < Outline.Length; previous = i++)
                 {
                     var start = Outline[previous];
                     var end = Outline[i];
                     if ((end.y > point.y) != (start.y > point.y)
                         && point.x < (start.x - end.x)
                         * (point.y - end.y)
                         / (start.y - end.y)
                         + end.x)
                     {
                         inside = !inside;
                     }

                     if (i == 0)
                     {
                         continue;
                     }

                     var segment = end - start;
                     var lengthSquared = segment.sqrMagnitude;
                     if (lengthSquared <= Mathf.Epsilon)
                     {
                         continue;
                     }

                     var along = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
                     var nearestPoint = start + segment * along;
                     nearestDistanceSquared = Mathf.Min(
                         nearestDistanceSquared,
                         (point - nearestPoint).sqrMagnitude);
                 }

                 var index = y * distanceFieldColumns + x;
                 var distance = Mathf.Sqrt(nearestDistanceSquared);
                 signedDistanceField[index] = inside ? distance : -distance;
             }
         }

         for (var y = 0; y < distanceFieldRows; y++)
         {
             var previousY = Mathf.Max(0, y - 1);
             var nextY = Mathf.Min(distanceFieldRows - 1, y + 1);
             for (var x = 0; x < distanceFieldColumns; x++)
             {
                 var previousX = Mathf.Max(0, x - 1);
                 var nextX = Mathf.Min(distanceFieldColumns - 1, x + 1);
                 var horizontalDistance =
                     (nextX - previousX) * DistanceFieldCellSize;
                 var verticalDistance =
                     (nextY - previousY) * DistanceFieldCellSize;
                 var horizontalGradient = (
                     signedDistanceField[y * distanceFieldColumns + nextX]
                     - signedDistanceField[y * distanceFieldColumns + previousX])
                     / horizontalDistance;
                 var verticalGradient = (
                     signedDistanceField[nextY * distanceFieldColumns + x]
                     - signedDistanceField[previousY * distanceFieldColumns + x])
                     / verticalDistance;
                 var gradient = new Vector2(horizontalGradient, verticalGradient);
                 distanceFieldGradients[y * distanceFieldColumns + x] =
                     gradient.sqrMagnitude > Mathf.Epsilon ? gradient.normalized : Vector2.zero;
             }
         }
     }

     internal Vector2 GetSideNormal(
         Vector2 point,
         PointClassification classification,
         bool shouldBeInside)
     {
         var normal = point - classification.NearestPoint;
         if (normal.sqrMagnitude <= Mathf.Epsilon)
         {
             normal = classification.InteriorNormal * (shouldBeInside ? 1f : -1f);
         }
         else
         {
             normal.Normalize();
             var inwardDot = Vector2.Dot(normal, classification.InteriorNormal);
             if (shouldBeInside && inwardDot < 0f)
             {
                 normal = classification.InteriorNormal;
             }
             else if (!shouldBeInside && inwardDot > 0f)
             {
                 normal = -classification.InteriorNormal;
             }
         }

         return normal;
     }

     internal bool TryGetPositionOnSide(
         Vector2 point,
         bool shouldBeInside,
         float radius,
         out Vector2 corrected)
     {
         var bestDistanceSquared = float.PositiveInfinity;
         var bestPosition = Vector2.zero;
         var found = false;
         for (var offsetPass = 0; offsetPass < 4; offsetPass++)
         {
             var offset = offsetPass == 0
                 ? radius + 0.02f
                 : offsetPass == 1
                     ? radius * 0.75f
                     : offsetPass == 2
                         ? radius * 0.5f
                         : 0.1f;
             foreach (var boundary in boundaries)
             {
                 var segment = boundary.End - boundary.Start;
                 var lengthSquared = segment.sqrMagnitude;
                 if (lengthSquared <= Mathf.Epsilon)
                 {
                     continue;
                 }

                 var along = Mathf.Clamp01(Vector2.Dot(point - boundary.Start, segment) / lengthSquared);
                 var closest = boundary.Start + segment * along;
                 var direction = shouldBeInside
                     ? boundary.InteriorNormal
                     : -boundary.InteriorNormal;
                 var candidate = closest + direction * offset;
                 if (Contains(candidate) != shouldBeInside)
                 {
                     continue;
                 }

                 var distanceSquared = (candidate - point).sqrMagnitude;
                 if (distanceSquared < bestDistanceSquared)
                 {
                     bestDistanceSquared = distanceSquared;
                     bestPosition = candidate;
                     found = true;
                 }
             }

             if (found)
             {
                 corrected = bestPosition;
                 return true;
             }
         }

         if (shouldBeInside)
         {
             corrected = ToRootPoint(interiorFallback);
             return Contains(corrected);
         }

         corrected = Vector2.zero;
         return false;
     }

     internal bool Contains(Vector2 rootPoint)
     {
         if (Outline.Length < 3)
         {
             return false;
         }

         return ContainsLocal(ToLocalPoint(rootPoint));
     }

     private Vector2 ToLocalPoint(Vector2 rootPoint)
     {
         var localPoint = rootToContainerMatrix.MultiplyPoint3x4(
             new Vector3(rootPoint.x, rootPoint.y, 0f));
         return new Vector2(localPoint.x, localPoint.y);
     }

     private Vector2 FindInteriorFallback()
     {
         if (Outline.Length == 0)
         {
             return Vector2.zero;
         }

         var centroid = Vector2.zero;
         foreach (var point in Outline)
         {
             centroid += point;
         }

         centroid /= Outline.Length;
         if (ContainsLocal(centroid))
         {
             return centroid;
         }

         var minimum = Outline[0];
         var maximum = minimum;
         foreach (var point in Outline)
         {
             minimum = Vector2.Min(minimum, point);
             maximum = Vector2.Max(maximum, point);
         }

         const int samplesPerAxis = 24;
         for (var y = 0; y < samplesPerAxis; y++)
         {
             for (var x = 0; x < samplesPerAxis; x++)
             {
                 var sample = new Vector2(
                     Mathf.Lerp(minimum.x, maximum.x, (x + 0.5f) / samplesPerAxis),
                     Mathf.Lerp(minimum.y, maximum.y, (y + 0.5f) / samplesPerAxis));
                 if (ContainsLocal(sample))
                 {
                     return sample;
                 }
             }
         }

         return centroid;
     }

     private bool ContainsLocal(Vector2 point)
     {
         var inside = false;
         for (int i = 0, previous = Outline.Length - 1; i < Outline.Length; previous = i++)
         {
             var currentPoint = Outline[i];
             var previousPoint = Outline[previous];
             if ((currentPoint.y > point.y) != (previousPoint.y > point.y)
                 && point.x < (previousPoint.x - currentPoint.x)
                 * (point.y - currentPoint.y)
                 / (previousPoint.y - currentPoint.y)
                 + currentPoint.x)
             {
                 inside = !inside;
             }
         }

         return inside;
     }
    }

    private readonly struct PointClassification
    {
     internal PointClassification(
         bool isInside,
         Vector2 nearestPoint,
         Vector2 interiorNormal,
         float distanceSquared,
         int boundaryIndex)
     {
         IsInside = isInside;
         NearestPoint = nearestPoint;
         InteriorNormal = interiorNormal;
         DistanceSquared = distanceSquared;
         BoundaryIndex = boundaryIndex;
     }

     internal bool IsInside { get; }
     internal Vector2 NearestPoint { get; }
     internal Vector2 InteriorNormal { get; }
     internal float DistanceSquared { get; }
     internal int BoundaryIndex { get; }
    }

    private readonly struct BoundarySegment
    {
     internal BoundarySegment(Vector2 start, Vector2 end, float interiorOrientation)
     {
         Start = start;
         End = end;
         var segment = end - start;
         InteriorNormal = new Vector2(-segment.y, segment.x).normalized * interiorOrientation;
     }

     internal Vector2 Start { get; }
     internal Vector2 End { get; }
     internal Vector2 InteriorNormal { get; }
    }
}

internal sealed class LiquidSurfaceGraphic : MaskableGraphic
{
    private Texture2D densityTexture;

    public override Texture mainTexture => densityTexture ? densityTexture : s_WhiteTexture;

    internal void SetDensityTexture(Texture2D texture)
    {
        densityTexture = texture;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        var rect = rectTransform.rect;
        var vertices = new UIVertex[4];
        vertices[0] = CreateVertex(new Vector2(rect.xMin, rect.yMin), new Vector2(0f, 0f));
        vertices[1] = CreateVertex(new Vector2(rect.xMin, rect.yMax), new Vector2(0f, 1f));
        vertices[2] = CreateVertex(new Vector2(rect.xMax, rect.yMax), new Vector2(1f, 1f));
        vertices[3] = CreateVertex(new Vector2(rect.xMax, rect.yMin), new Vector2(1f, 0f));
        vertexHelper.AddUIVertexQuad(vertices);
    }

    private UIVertex CreateVertex(Vector2 position, Vector2 uv)
    {
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = position;
        vertex.uv0 = uv;
        return vertex;
    }

    protected override void OnDestroy()
    {
        if (densityTexture)
        {
            Destroy(densityTexture);
            densityTexture = null;
        }

        base.OnDestroy();
    }
}

internal sealed class LiquidDropGraphic : MaskableGraphic
{
    private const int CircleSegmentCount = 12;
    private static readonly Vector2[] UnitCircle = CreateUnitCircle();
    private Vector2[] positions;
    private Vector2 localOffset;
    private int start;
    private int count;
    private float radius;
    private byte[] volumes;

    internal void SetDrops(
        Vector2[] dropPositions,
        byte[] dropVolumes,
        int first,
        int length,
        float dropRadius,
        Vector2 offset)
    {
        positions = dropPositions;
        volumes = dropVolumes;
        start = first;
        count = length;
        radius = dropRadius;
        localOffset = offset;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (positions == null || count <= 0)
        {
            return;
        }

        for (var drop = 0; drop < count; drop++)
        {
            var center = positions[start + drop] - localOffset;
            var dropRadius = radius * (volumes[start + drop] > 1 ? 2.6457513f : 1f);
            var centerIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, center);
            for (var segment = 0; segment < UnitCircle.Length; segment++)
            {
                AddVertex(vertexHelper, center + UnitCircle[segment] * dropRadius);
            }

            for (var segment = 0; segment < UnitCircle.Length; segment++)
            {
                var next = (segment + 1) % UnitCircle.Length;
                vertexHelper.AddTriangle(centerIndex, centerIndex + 1 + segment, centerIndex + 1 + next);
            }
        }
    }

    private void AddVertex(VertexHelper vertexHelper, Vector2 position)
    {
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = position;
        vertex.uv0 = new Vector2(0.5f, 0.5f);
        vertexHelper.AddVert(vertex);
    }

    private static Vector2[] CreateUnitCircle()
    {
        var points = new Vector2[CircleSegmentCount];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i * Mathf.PI * 2f / points.Length;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        return points;
    }
}
