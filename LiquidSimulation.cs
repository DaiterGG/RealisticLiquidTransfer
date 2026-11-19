using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class LiquidSimulation
{
    internal const int CircleSegmentCount = 12;

    private const int MaximumDrops = 20000;
    private const int DropsPerGraphic = 4096;
    private const float DropRadius = 12f * 0.7368063f;
    private const float SpawnRate = 1000f;
    private const float Gravity = 420f;
    private const float DropRestitution = 0.05f;
    private const float WallRestitution = 0.32f;
    private const float FixedStep = 1f / 30f;
    private const int DropCollisionIterations = 6;
    private const int DensitySolverIterations = 1;
    private const float FluidSmoothingRadius = DropRadius * 2.5f;
    private const float DensityCorrectionStrength = 2f;
    private const float MaximumDensityCorrection = DropRadius;

    private readonly RectTransform root;
    private readonly ContainerShape[] containers;
    private Vector2[] positions = new Vector2[MaximumDrops];
    private Vector2[] previousPositions = new Vector2[MaximumDrops];
    private Vector2[] velocities = new Vector2[MaximumDrops];
    private Vector2[] positionCorrections = new Vector2[MaximumDrops];
    private float[] densityLambdas = new float[MaximumDrops];
    private int[] dropIds = new int[MaximumDrops];
    private byte[] containerAssignments = new byte[MaximumDrops];
    private Vector2[] reorderedPositions = new Vector2[MaximumDrops];
    private Vector2[] reorderedPreviousPositions = new Vector2[MaximumDrops];
    private Vector2[] reorderedVelocities = new Vector2[MaximumDrops];
    private Vector2[] reorderedPositionCorrections = new Vector2[MaximumDrops];
    private float[] reorderedDensityLambdas = new float[MaximumDrops];
    private int[] reorderedDropIds = new int[MaximumDrops];
    private byte[] reorderedContainerAssignments = new byte[MaximumDrops];
    private readonly List<LiquidDropGraphic> graphics = new List<LiquidDropGraphic>();
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
    private readonly int densityNeighborCellRange = Mathf.CeilToInt(FluidSmoothingRadius / (DropRadius * 2f));
    private static readonly float FluidKernelScale =
        4f / (Mathf.PI * Mathf.Pow(FluidSmoothingRadius, 8f));
    private static readonly float FluidRestDensity = CalculateFluidRestDensity();
    private int count;
    private int nextDropId = 1;
    private float simulationTime;
    private float spawnAccumulator;
    private float diagnosticTimer;
    private long assignmentCorrections;
    private int offscreenDeleted;
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
        CreateGraphics();
        SeedContainer(containers[0], GetInitialDropCount(firstAmount));
        SeedContainer(containers[1], GetInitialDropCount(secondAmount));
        DebugLog.Write(
            $"Simulation initialized: seeded "
            + $"{GetContainerDropCount(0)} receiver and {GetContainerDropCount(1)} source drops; "
            + $"drop radius={DropRadius:F1}, screen bounds=({minimumScreenX:F1},{minimumScreenY:F1})"
            + $"-({maximumScreenX:F1},{maximumScreenY:F1}).");
    }

    internal int Count => count;

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
                assignedCount++;
            }
        }

        return assignedCount;
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
            var spawnCount = Mathf.Min(Mathf.FloorToInt(spawnAccumulator), MaximumDrops - count);
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

        if (count >= MaximumDrops)
        {
            spawnAccumulator = 0f;
        }

        simulationTime = Mathf.Min(simulationTime + deltaTime, FixedStep * 3f);
        var steps = 0;
        while (simulationTime >= FixedStep && steps < 3)
        {
            Step(FixedStep);
            simulationTime -= FixedStep;
            steps++;
        }

        diagnosticTimer += deltaTime;
        if (diagnosticTimer >= 1f)
        {
            DebugLog.Write(
                $"Simulation: drops={count}/{MaximumDrops}, assignment corrections={assignmentCorrections}, "
                + $"off-screen drops deleted={offscreenDeleted}.");
            SimulationProfiler.Report(count);
            diagnosticTimer %= 1f;
        }

        profilingStart = SimulationProfiler.Begin();
        UpdateGraphics();
        SimulationProfiler.End(SimulationProfileStage.RenderScheduling, profilingStart);
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
        if (count >= MaximumDrops)
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
        containerAssignments[count] = containerAssignment == 0
            ? GetContainerAssignment(position)
            : containerAssignment;
        count++;
    }

    private void Step(float deltaTime)
    {
        var profilingStart = SimulationProfiler.Begin();
        RefreshContainerGeometry();
        for (var i = 0; i < count; i++)
        {
            previousPositions[i] = positions[i];
            velocities[i].y -= Gravity * deltaTime;
            positions[i] += velocities[i] * deltaTime;
        }
        SimulationProfiler.End(SimulationProfileStage.GeometryRefreshAndIntegration, profilingStart);

        SolveFluidDensity();
        ResolveDropCollisions();

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
        EnforceContainerAssignments();
        SimulationProfiler.End(SimulationProfileStage.AssignmentChecks, profilingStart);

        profilingStart = SimulationProfiler.Begin();
        LogBottomSourceDrop();
        SimulationProfiler.End(SimulationProfileStage.BottomDropLogging, profilingStart);

        profilingStart = SimulationProfiler.Begin();
        RemoveOffscreenDrops();
        SimulationProfiler.End(SimulationProfileStage.OffscreenCleanup, profilingStart);
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
            var fullyOutsideScreen = position.x < minimumScreenX - DropRadius
                || position.x > maximumScreenX + DropRadius
                || position.y < minimumScreenY - DropRadius
                || position.y > maximumScreenY + DropRadius;
            if (!fullyOutsideScreen || IsInsideContainer(position))
            {
                i++;
                continue;
            }

            count--;
            positions[i] = positions[count];
            previousPositions[i] = previousPositions[count];
            velocities[i] = velocities[count];
            dropIds[i] = dropIds[count];
            containerAssignments[i] = containerAssignments[count];
            offscreenDeleted++;
        }
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

    private byte GetContainerAssignment(Vector2 position)
    {
        for (var i = 0; i < containers.Length; i++)
        {
            if (containers[i].Contains(position))
            {
                return (byte)(i + 1);
            }
        }

        return 0;
    }

    private void EnforceContainerAssignments()
    {
        var minimumDistanceSquared = DropRadius * DropRadius;
        for (var i = 0; i < count; i++)
        {
            var position = positions[i];
            var previousPosition = previousPositions[i];
            var velocity = velocities[i];
            var assignment = containerAssignments[i];
            var openingTransition = false;
            for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
            {
                var container = containers[containerIndex];
                if (!container.CrossesOpening(previousPosition, position)
                    && !container.IsNearOpening(previousPosition, DropRadius * 2f)
                    && !container.IsNearOpening(position, DropRadius * 2f))
                {
                    continue;
                }

                openingTransition = true;
                break;
            }

            if (openingTransition)
            {
                assignment = GetContainerAssignment(position);
            }

            for (var containerIndex = 0; containerIndex < containers.Length; containerIndex++)
            {
                var container = containers[containerIndex];
                if (openingTransition)
                {
                    continue;
                }

                var shouldBeInside = assignment == containerIndex + 1;
                if (!shouldBeInside && !container.IsNear(position, DropRadius))
                {
                    continue;
                }

                var classification = container.Classify(position);
                var isInside = classification.IsInside;
                if (isInside != shouldBeInside)
                {
                    if (container.TryGetPositionOnSide(position, shouldBeInside, out var correctedPosition))
                    {
                        var correctionNormal = (correctedPosition - position).normalized;
                        position = correctedPosition;
                        velocity = BounceFromBoundary(velocity, correctionNormal);
                        assignmentCorrections++;
                    }

                    continue;
                }

                if (classification.DistanceSquared >= minimumDistanceSquared)
                {
                    continue;
                }

                var normal = container.GetSideNormal(position, classification, shouldBeInside);
                var distance = Mathf.Sqrt(classification.DistanceSquared);
                position += normal * (DropRadius - distance + 0.01f);
                velocity = BounceFromBoundary(velocity, normal);
                assignmentCorrections++;
            }

            positions[i] = position;
            velocities[i] = velocity;
            containerAssignments[i] = assignment;
        }
    }

    private static Vector2 BounceFromBoundary(Vector2 velocity, Vector2 normal)
    {
        var normalVelocity = Vector2.Dot(velocity, normal);
        if (normalVelocity < 0f)
        {
            velocity -= (1f + WallRestitution) * normalVelocity * normal;
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

    private void ReorderParticlesByCell()
    {
        for (var sortedIndex = 0; sortedIndex < count; sortedIndex++)
        {
            var sourceIndex = cellParticleIndices[sortedIndex];
            reorderedPositions[sortedIndex] = positions[sourceIndex];
            reorderedPreviousPositions[sortedIndex] = previousPositions[sourceIndex];
            reorderedVelocities[sortedIndex] = velocities[sourceIndex];
            reorderedPositionCorrections[sortedIndex] = positionCorrections[sourceIndex];
            reorderedDensityLambdas[sortedIndex] = densityLambdas[sourceIndex];
            reorderedDropIds[sortedIndex] = dropIds[sourceIndex];
            reorderedContainerAssignments[sortedIndex] = containerAssignments[sourceIndex];
        }

        SwapBuffers(ref positions, ref reorderedPositions);
        SwapBuffers(ref previousPositions, ref reorderedPreviousPositions);
        SwapBuffers(ref velocities, ref reorderedVelocities);
        SwapBuffers(ref positionCorrections, ref reorderedPositionCorrections);
        SwapBuffers(ref densityLambdas, ref reorderedDensityLambdas);
        SwapBuffers(ref dropIds, ref reorderedDropIds);
        SwapBuffers(ref containerAssignments, ref reorderedContainerAssignments);
    }

    private static void SwapBuffers<T>(ref T[] current, ref T[] reordered)
    {
        var previous = current;
        current = reordered;
        reordered = previous;
    }

    private void SolveFluidDensity()
    {
        for (var iteration = 0; iteration < DensitySolverIterations; iteration++)
        {
            var profilingStart = SimulationProfiler.Begin();
            BuildSpatialGrid();
            ComputeDensityLambdas();
            ComputeDensityCorrections();
            for (var i = 0; i < count; i++)
            {
                positions[i] += positionCorrections[i];
            }
            SimulationProfiler.End(SimulationProfileStage.FluidDensitySolver, profilingStart);
        }
    }

    private void ComputeDensityLambdas()
    {
        var restDensityInverse = 1f / FluidRestDensity;
        var smoothingRadiusSquared = FluidSmoothingRadius * FluidSmoothingRadius;
        for (var i = 0; i < count; i++)
        {
            GetNeighborCellBounds(
                positions[i],
                out var minimumCellX,
                out var maximumCellX,
                out var minimumCellY,
                out var maximumCellY);

            var density = 0f;
            var gradientSum = Vector2.zero;
            var gradientMagnitudeSum = 0f;
            for (var cellY = minimumCellY; cellY <= maximumCellY; cellY++)
            {
                for (var cellX = minimumCellX; cellX <= maximumCellX; cellX++)
                {
                    var cell = cellY * gridColumns + cellX;
                    for (var neighborIndex = cellOffsets[cell];
                         neighborIndex < cellOffsets[cell + 1];
                         neighborIndex++)
                    {
                        var neighbor = neighborIndex;
                        var difference = positions[i] - positions[neighbor];
                        var distanceSquared = difference.sqrMagnitude;
                        if (distanceSquared >= smoothingRadiusSquared)
                        {
                            continue;
                        }

                        density += EvaluateFluidKernel(distanceSquared);
                        if (neighbor == i)
                        {
                            continue;
                        }

                        var gradient = EvaluateFluidKernelGradient(difference) * restDensityInverse;
                        gradientSum += gradient;
                        gradientMagnitudeSum += gradient.sqrMagnitude;
                    }
                }
            }

            var compression = Mathf.Max(density * restDensityInverse - 1f, 0f);
            var denominator = gradientSum.sqrMagnitude + gradientMagnitudeSum + 0.000001f;
            densityLambdas[i] = compression > 0f ? -compression / denominator : 0f;
        }
    }

    private void ComputeDensityCorrections()
    {
        var smoothingRadiusSquared = FluidSmoothingRadius * FluidSmoothingRadius;
        var restDensityInverse = 1f / FluidRestDensity;
        var maximumCorrectionSquared = MaximumDensityCorrection * MaximumDensityCorrection;
        for (var i = 0; i < count; i++)
        {
            GetNeighborCellBounds(
                positions[i],
                out var minimumCellX,
                out var maximumCellX,
                out var minimumCellY,
                out var maximumCellY);

            var correction = Vector2.zero;
            for (var cellY = minimumCellY; cellY <= maximumCellY; cellY++)
            {
                for (var cellX = minimumCellX; cellX <= maximumCellX; cellX++)
                {
                    var cell = cellY * gridColumns + cellX;
                    for (var neighborIndex = cellOffsets[cell];
                         neighborIndex < cellOffsets[cell + 1];
                         neighborIndex++)
                    {
                        var neighbor = neighborIndex;
                        var difference = positions[i] - positions[neighbor];
                        if (difference.sqrMagnitude >= smoothingRadiusSquared)
                        {
                            continue;
                        }

                        var lambdaSum = densityLambdas[i] + densityLambdas[neighbor];
                        correction += lambdaSum * EvaluateFluidKernelGradient(difference);
                    }
                }
            }

            correction *= restDensityInverse * DensityCorrectionStrength;
            if (correction.sqrMagnitude > maximumCorrectionSquared)
            {
                correction = correction.normalized * MaximumDensityCorrection;
            }

            positionCorrections[i] = correction;
        }
    }

    private void ResolveDropVelocities()
    {
        BuildSpatialGrid();
        var minimumDistance = DropRadius * 2f + 0.5f;
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        ResolveSpatialGridPairs(minimumDistanceSquared, true);
    }

    private void GetNeighborCellBounds(
        Vector2 position,
        out int minimumCellX,
        out int maximumCellX,
        out int minimumCellY,
        out int maximumCellY)
    {
        var centerX = Mathf.Clamp(
            Mathf.FloorToInt((position.x - minimumScreenX) / cellSize),
            0,
            gridColumns - 1);
        var centerY = Mathf.Clamp(
            Mathf.FloorToInt((position.y - minimumScreenY) / cellSize),
            0,
            gridRows - 1);
        minimumCellX = Mathf.Max(0, centerX - densityNeighborCellRange);
        maximumCellX = Mathf.Min(gridColumns - 1, centerX + densityNeighborCellRange);
        minimumCellY = Mathf.Max(0, centerY - densityNeighborCellRange);
        maximumCellY = Mathf.Min(gridRows - 1, centerY + densityNeighborCellRange);
    }

    private static float EvaluateFluidKernel(float distanceSquared)
    {
        var remainder = FluidSmoothingRadius * FluidSmoothingRadius - distanceSquared;
        return FluidKernelScale * remainder * remainder * remainder;
    }

    private static Vector2 EvaluateFluidKernelGradient(Vector2 difference)
    {
        var remainder = FluidSmoothingRadius * FluidSmoothingRadius - difference.sqrMagnitude;
        return difference * (-6f * FluidKernelScale * remainder * remainder);
    }

    private static float CalculateFluidRestDensity()
    {
        var density = 0f;
        var spacing = DropRadius * 2f;
        var rowSpacing = spacing * 0.8660254f;
        var smoothingRadiusSquared = FluidSmoothingRadius * FluidSmoothingRadius;
        for (var row = -4; row <= 4; row++)
        {
            for (var column = -4; column <= 4; column++)
            {
                var offset = new Vector2(
                    (column + row * 0.5f) * spacing,
                    row * rowSpacing);
                var distanceSquared = offset.sqrMagnitude;
                if (distanceSquared < smoothingRadiusSquared)
                {
                    density += EvaluateFluidKernel(distanceSquared);
                }
            }
        }

        return density;
    }

    private void ResolveDropCollisions()
    {
        var minimumDistance = DropRadius * 2f;
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        for (var iteration = 0; iteration < DropCollisionIterations; iteration++)
        {
            var profilingStart = SimulationProfiler.Begin();
            BuildSpatialGrid();
            SimulationProfiler.End(SimulationProfileStage.SpatialGrid, profilingStart);

            profilingStart = SimulationProfiler.Begin();
            ResolveDropCollisionPass(minimumDistanceSquared);
            SimulationProfiler.End(SimulationProfileStage.DropCollisions, profilingStart);
        }
    }

    private void ResolveDropCollisionPass(float minimumDistanceSquared)
    {
        ResolveSpatialGridPairs(minimumDistanceSquared, false);
    }

    private void ResolveSpatialGridPairs(float minimumDistanceSquared, bool velocityOnly)
    {
        for (var cellY = 0; cellY < gridRows; cellY++)
        {
            for (var cellX = 0; cellX < gridColumns; cellX++)
            {
                var cell = cellY * gridColumns + cellX;
                ResolveCellPair(cell, cell, minimumDistanceSquared, velocityOnly);

                if (cellX + 1 < gridColumns)
                {
                    ResolveCellPair(cell, cell + 1, minimumDistanceSquared, velocityOnly);
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
                        minimumDistanceSquared,
                        velocityOnly);
                }

                ResolveCellPair(cell, cell + gridColumns, minimumDistanceSquared, velocityOnly);
                if (cellX + 1 < gridColumns)
                {
                    ResolveCellPair(
                        cell,
                        cell + gridColumns + 1,
                        minimumDistanceSquared,
                        velocityOnly);
                }
            }
        }
    }

    private void ResolveCellPair(
        int firstCell,
        int secondCell,
        float minimumDistanceSquared,
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
                        minimumDistanceSquared,
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
                    minimumDistanceSquared,
                    velocityOnly);
            }
        }
    }

    private void ResolvePairForPass(
        int first,
        int second,
        float minimumDistanceSquared,
        bool velocityOnly)
    {
        if (velocityOnly)
        {
            ResolvePairVelocity(first, second, minimumDistanceSquared);
        }
        else
        {
            ResolvePair(first, second, minimumDistanceSquared);
        }
    }

    private void ResolvePair(int first, int second, float minimumDistanceSquared)
    {
        var difference = positions[second] - positions[first];
        var distanceSquared = difference.sqrMagnitude;
        if (distanceSquared >= minimumDistanceSquared)
        {
            return;
        }

        var distance = Mathf.Sqrt(distanceSquared);
        var normal = distance > 0.0001f
            ? difference / distance
            : new Vector2(1f, 0f);
        var overlap = (DropRadius * 2f - distance) * 0.5f;
        positions[first] -= normal * overlap;
        positions[second] += normal * overlap;
    }

    private void ResolvePairVelocity(int first, int second, float minimumDistanceSquared)
    {
        var difference = positions[second] - positions[first];
        var distanceSquared = difference.sqrMagnitude;
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

        var impulse = -(1f + DropRestitution) * separatingSpeed * 0.5f;
        velocities[first] -= normal * impulse;
        velocities[second] += normal * impulse;
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
        var graphicCount = Mathf.CeilToInt((float)MaximumDrops / DropsPerGraphic);
        for (var i = 0; i < graphicCount; i++)
        {
            var graphic = new GameObject(
                $"LiquidDrops{i}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(LiquidDropGraphic)).GetComponent<LiquidDropGraphic>();
            var rect = graphic.rectTransform;
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = root.rect.size;
            graphic.color = new Color(0.16f, 0.5f, 0.95f, 0.9f);
            graphic.raycastTarget = false;
            rect.SetAsFirstSibling();
            graphics.Add(graphic);
        }
    }

    private void UpdateGraphics()
    {
        for (var i = 0; i < graphics.Count; i++)
        {
            var start = i * DropsPerGraphic;
            var length = Mathf.Clamp(count - start, 0, DropsPerGraphic);
            graphics[i].SetDrops(positions, start, length, DropRadius, CircleSegmentCount);
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
         if (Outline.Length == 0)
         {
             return;
         }

         var rootOrigin = ToRootPoint(Vector2.zero);
         var rootUnitsX = (ToRootPoint(Vector2.right) - rootOrigin).magnitude;
         var rootUnitsY = (ToRootPoint(Vector2.up) - rootOrigin).magnitude;
         rootUnitsPerLocalUnit = Mathf.Max(Mathf.Min(rootUnitsX, rootUnitsY), 0.0001f);
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

     internal bool IsNear(Vector2 point, float margin)
     {
         return point.x >= Minimum.x - margin
             && point.x <= Maximum.x + margin
             && point.y >= Minimum.y - margin
             && point.y <= Maximum.y + margin;
     }

     internal bool CrossesOpening(Vector2 start, Vector2 end)
     {
         const float OpeningEndpointTolerance = 0.001f;
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
             && openingAmount >= -OpeningEndpointTolerance
             && openingAmount <= 1f + OpeningEndpointTolerance;
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

     private static float Cross(Vector2 first, Vector2 second)
     {
         return first.x * second.y - first.y * second.x;
     }

     internal PointClassification Classify(Vector2 point)
     {
         var localPosition = transform.InverseTransformPoint(root.TransformPoint(point));
         var localPoint = new Vector2(localPosition.x, localPosition.y);
         if (!TrySampleDistanceField(localPoint, out var signedDistance, out var gradient))
         {
             return ClassifyExact(point);
         }

         var isInside = signedDistance >= 0f;
         if (gradient.sqrMagnitude <= 0.0001f)
         {
             var distance = Mathf.Abs(signedDistance) * rootUnitsPerLocalUnit;
             if (distance < DropRadius + DistanceFieldCellSize * rootUnitsPerLocalUnit)
             {
                 return ClassifyExact(point);
             }

             return new PointClassification(
                 isInside,
                 point,
                 Vector2.up,
                 distance * distance);
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
             distanceSquared);
     }

     private PointClassification ClassifyExact(Vector2 point)
     {
         var inside = Contains(point);
         var nearestDistanceSquared = float.PositiveInfinity;
         var nearestPoint = Vector2.zero;
         var nearestNormal = Vector2.up;
         foreach (var boundary in boundaries)
         {
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
             }
         }

         return new PointClassification(
             inside,
             nearestPoint,
             nearestNormal,
             nearestDistanceSquared);
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

     internal bool TryGetPositionOnSide(Vector2 point, bool shouldBeInside, out Vector2 corrected)
     {
         var bestDistanceSquared = float.PositiveInfinity;
         var bestPosition = Vector2.zero;
         var found = false;
         for (var offsetPass = 0; offsetPass < 4; offsetPass++)
         {
             var offset = offsetPass == 0
                 ? DropRadius + 0.02f
                 : offsetPass == 1
                     ? DropRadius * 0.75f
                     : offsetPass == 2
                         ? DropRadius * 0.5f
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

         var localPoint = transform.InverseTransformPoint(root.TransformPoint(rootPoint));
         var inside = false;
         for (int i = 0, previous = Outline.Length - 1; i < Outline.Length; previous = i++)
         {
             var currentPoint = Outline[i];
             var previousPoint = Outline[previous];
             if ((currentPoint.y > localPoint.y) != (previousPoint.y > localPoint.y)
                 && localPoint.x < (previousPoint.x - currentPoint.x)
                 * (localPoint.y - currentPoint.y)
                 / (previousPoint.y - currentPoint.y)
                 + currentPoint.x)
             {
                 inside = !inside;
             }
         }

         return inside;
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
         float distanceSquared)
     {
         IsInside = isInside;
         NearestPoint = nearestPoint;
         InteriorNormal = interiorNormal;
         DistanceSquared = distanceSquared;
     }

     internal bool IsInside { get; }
     internal Vector2 NearestPoint { get; }
     internal Vector2 InteriorNormal { get; }
     internal float DistanceSquared { get; }
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

internal sealed class LiquidDropGraphic : MaskableGraphic
{
    private const int MaximumVertices = 65535;
    private static readonly Vector2[] UnitCircle = CreateUnitCircle();
    private Vector2[] positions;
    private int start;
    private int count;
    private float radius;

    internal void SetDrops(Vector2[] dropPositions, int first, int length, float dropRadius, int segments)
    {
        if (segments != UnitCircle.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(segments));
        }

        positions = dropPositions;
        start = first;
        count = Mathf.Min(length, (MaximumVertices / (segments + 1)));
        radius = dropRadius;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        var profilingStart = SimulationProfiler.Begin();
        vertexHelper.Clear();
        if (positions == null || count <= 0)
        {
            SimulationProfiler.End(SimulationProfileStage.MeshGeneration, profilingStart);
            return;
        }

        for (var drop = 0; drop < count; drop++)
        {
            var center = positions[start + drop];
            var centerIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, center);
            for (var segment = 0; segment < UnitCircle.Length; segment++)
            {
                AddVertex(vertexHelper, center + UnitCircle[segment] * radius);
            }

            for (var segment = 0; segment < UnitCircle.Length; segment++)
            {
                var next = (segment + 1) % UnitCircle.Length;
                vertexHelper.AddTriangle(
                    centerIndex,
                    centerIndex + 1 + segment,
                    centerIndex + 1 + next);
            }
        }
        SimulationProfiler.End(SimulationProfileStage.MeshGeneration, profilingStart);
    }

    private void AddVertex(VertexHelper vertexHelper, Vector2 position)
    {
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = position;
        vertexHelper.AddVert(vertex);
    }

    private static Vector2[] CreateUnitCircle()
    {
        var points = new Vector2[LiquidSimulation.CircleSegmentCount];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i * Mathf.PI * 2f / points.Length;
            points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        return points;
    }
}
