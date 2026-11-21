using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal readonly struct LiquidProfileEntry
{
    internal LiquidProfileEntry(string liquidId, float amount, Color color)
    {
        LiquidId = liquidId;
        Amount = amount;
        Color = color;
    }

    internal string LiquidId { get; }
    internal float Amount { get; }
    internal Color Color { get; }
}

internal sealed class LiquidSimulation
{
    private const int MaximumDrops = 20000;
    private const int MillilitersPerDropTier = 500;
    private const int MaximumDropVolume = (MaximumDrops + MillilitersPerDropTier - 1)
        / MillilitersPerDropTier;
    internal const float DefaultDropRadius = 12f * 0.7368063f;
    internal const int MinimumDropRadiusTenths = 40;
    internal const int MaximumDropRadiusTenths = 160;
    internal const int DefaultDropRadiusTenths = 95;
    internal const int MinimumPackingTenths = 120;
    internal const int MaximumPackingTenths = 220;
    internal const int DefaultPackingTenths = 185;
    private const float DensityTexturePixelsPerUnit = 0.5f;
    private const int MaximumDensityTextureDimension = 1024;
    private const float DensityContribution = 1.2f;
    private const float TailSpeedForMaximumLength = 600f;
    private const float MaximumEncodedVelocity = 800f;
    private const int DropsPerGraphic = 4096;
    private const float SpawnRate = 1000f;
    private const float MergeDelaySeconds = 2f;
    private const float Gravity = 420f;
    private const float GravityMultiplier = 2f;
    private const float FixedStep = 1f / 30f;
    private const float DensitySwapCheckInterval = 0.1f;
    private const float DensitySwapDuration = 0.75f;
    private const float DensitySwapCooldown = 1f;
    private const float DensitySwapMaximumSpeed = 8f;
    private const float DensitySwapComparisonTolerance = 0.005f;
    private const int DensitySwapCandidateBudget = 96;
    private const int MaximumConcurrentDensitySwapDrops = 4;
    private const float VelocityRampDuration = 2f;
    private const float OutsideDropExtraBoundsPadding = 1f;
    internal const int MinimumDropCollisionPasses = 2;
    internal const int MaximumDropCollisionPasses = 6;
    internal const int DefaultDropCollisionPasses = 6;
    internal const int MinimumCollisionCorrectionPercent = 25;
    internal const int MaximumCollisionCorrectionPercent = 100;
    internal const int DefaultCollisionCorrectionPercent = 100;
    internal const int MinimumVelocityContactMarginTenths = 0;
    internal const int MaximumVelocityContactMarginTenths = 20;
    internal const int DefaultVelocityContactMarginTenths = 5;
    private readonly RectTransform root;
    private readonly ContainerShape[] containers;
    private readonly bool[] openingTransitions;
    private readonly int[] containerDropSizes;
    private readonly int[] initialContainerDropCounts;
    private readonly LiquidProfileEntry[][] liquidProfiles;
    private readonly int[][] liquidProfileTypeIndices;
    private readonly int[][] liquidProfileOrders;
    private readonly int liquidTypeCapacity;
    private readonly string[] liquidIds;
    private readonly Vector4[] liquidTypeColors;
    private readonly float[] liquidTypeDensities;
    private readonly float[] initialSimulatedLiquidAmounts;
    private readonly float[] remainingProfileAmounts;
    private readonly int[] liquidProfileCursors = new int[2];
    private int liquidTypeCount;
    private Vector2[] positions = new Vector2[MaximumDrops];
    private Vector2[] previousPositions = new Vector2[MaximumDrops];
    private Vector2[] velocities = new Vector2[MaximumDrops];
    private bool[] fastOutsideDrops = new bool[MaximumDrops];
    private float[] mergeDelayRemaining = new float[MaximumDrops];
    private int[] dropIds = new int[MaximumDrops];
    private byte[] dropVolumes = new byte[MaximumDrops];
    private byte[] containerAssignments = new byte[MaximumDrops];
    private byte[] dropSizeZoneAssignments = new byte[MaximumDrops];
    private Vector4[] dropColors = new Vector4[MaximumDrops];
    private Vector4[] densitySwapPaths = new Vector4[MaximumDrops];
    private float[] densitySwapProgress = new float[MaximumDrops];
    private int[] dropUnitHeads = new int[MaximumDrops];
    private int[] dropUnitTails = new int[MaximumDrops];
    private Vector2[] reorderedPositions = new Vector2[MaximumDrops];
    private Vector2[] reorderedPreviousPositions = new Vector2[MaximumDrops];
    private Vector2[] reorderedVelocities = new Vector2[MaximumDrops];
    private bool[] reorderedFastOutsideDrops = new bool[MaximumDrops];
    private float[] reorderedMergeDelayRemaining = new float[MaximumDrops];
    private int[] reorderedDropIds = new int[MaximumDrops];
    private byte[] reorderedDropVolumes = new byte[MaximumDrops];
    private byte[] reorderedContainerAssignments = new byte[MaximumDrops];
    private byte[] reorderedDropSizeZoneAssignments = new byte[MaximumDrops];
    private Vector4[] reorderedDropColors = new Vector4[MaximumDrops];
    private Vector4[] reorderedDensitySwapPaths = new Vector4[MaximumDrops];
    private float[] reorderedDensitySwapProgress = new float[MaximumDrops];
    private int[] reorderedDropUnitHeads = new int[MaximumDrops];
    private int[] reorderedDropUnitTails = new int[MaximumDrops];
    private int[] unitNext = new int[MaximumDrops];
    private int[] unitComponentHeads = new int[MaximumDrops];
    private int[] unitComponentTails = new int[MaximumDrops];
    private int[] componentTypeIndices;
    private float[] componentAmounts;
    private int[] componentNext;
    private float[] remainingSimulatedAmounts;
    private float[] spawnWeightTotals;
    private float[] spawnWeightPrefixes;
    private int freeUnitHead;
    private int freeComponentHead;
    private readonly List<LiquidDropGraphic> individualDropGraphics = new List<LiquidDropGraphic>();
    private readonly int[] pendingMergeIndices = new int[MaximumDropVolume];
    private LiquidSurfaceGraphic surfaceGraphic;
    private Texture2D densityTexture;
    private RenderTexture densityRenderTexture;
    private Texture2D densityColorTexture;
    private RenderTexture densityColorRenderTexture;
    private Texture2D densityUploadTexture;
    private Color32[] densityPixels;
    private Color32[] densityUploadPixels;
    private Vector3[] densityVelocityFields;
    private Vector4[] densityWeightedColors;
    private Color32[] densityColorPixels;
    private Vector4[] computeDropData;
    private float[] computeDropRadii;
    private Vector4[] computeDropColors;
    private int[] activeDensityPixelIndices;
    private int activeDensityPixelCount;
    private int densityTextureWidth;
    private int densityTextureHeight;
    private int densityUploadTextureWidth;
    private int densityUploadTextureHeight;
    private int densityComputePixelCount;
    private int clearAccumulationKernel;
    private int rasterizeDropsKernel;
    private int resolveDensityKernel;
    private ComputeBuffer densityAccumulationBuffer;
    private ComputeBuffer colorAccumulationBuffer;
    private ComputeBuffer computeDropDataBuffer;
    private ComputeBuffer computeDropRadiusBuffer;
    private ComputeBuffer computeDropColorBuffer;
    private ComputeBuffer computeDropSwapPathBuffer;
    private ComputeShader densityComputeShader;
    private bool supportsPartialTextureCopy;
    private bool useComputeRendering;
    private readonly float minimumScreenX;
    private readonly float maximumScreenX;
    private readonly float minimumScreenY;
    private readonly float maximumScreenY;
    private int gridColumns;
    private int gridRows;
    private int[] cellCounts;
    private int[] cellActiveDropCounts;
    private int[] cellOffsets;
    private int[] cellWriteOffsets;
    private int[] cellSafeWriteOffsets;
    private int[] cellParticleIndices;
    private int[] activeCells;
    private int activeCellCount;
    private int activeDropCount;
    private bool hasUnassignedAggregate;
    private float cellSize;
    private float dropRadius = DefaultDropRadius;
    private float initialPacking = DefaultPackingTenths / 100f;
    private float dropCollisionDistance;
    private float dropCollisionDistanceSquared;
    private int count;
    private int totalLiquidUnits;
    private int dropCollisionPasses = MinimumDropCollisionPasses;
    private float collisionCorrectionStrength = 1f;
    private float velocityContactMargin = 0.5f;
    private float velocityContactDistanceSquared;
    private bool graphicsDirty = true;
    private bool surfaceShaderEnabled;
    private bool simulationFrozen;
    private int nextDropId = 1;
    private float simulationTime;
    private float spawnAccumulator;
    private float velocityRampElapsed;
    private float diagnosticTimer;
    private int topSeededDropId;
    private long assignmentCorrections;
    private int offscreenDeleted;
    private int offscreenDeletedThisInterval;
    private int offscreenUnitsDeletedThisInterval;
    private int offscreenSamplesThisInterval;
    private int mergedAggregatesThisInterval;
    private int splitAggregatesThisInterval;
    private int largestSplitBatchThisInterval;
    private float densitySwapCheckTimer;
    private int densitySwapScanCursor;
    private int activeDensitySwapCount;
    private float densitySwapCooldownRemaining;
    private uint randomState = 0xA341316Cu;

    internal LiquidSimulation(
        RectTransform root,
        RectTransform firstContainer,
        Vector2[] firstOutline,
        float firstCapacity,
        RectTransform secondContainer,
        Vector2[] secondOutline,
        float secondCapacity,
        float firstAmount,
        float secondAmount,
        LiquidProfileEntry[] firstLiquidComposition,
        LiquidProfileEntry[] secondLiquidComposition,
        float initialPacking = 1.75f)
    {
        this.root = root;
        liquidProfiles = new[]
        {
            firstLiquidComposition ?? Array.Empty<LiquidProfileEntry>(),
            secondLiquidComposition ?? Array.Empty<LiquidProfileEntry>()
        };
        liquidTypeCapacity = liquidProfiles[0].Length + liquidProfiles[1].Length;
        liquidProfileTypeIndices = new[]
        {
            new int[liquidProfiles[0].Length],
            new int[liquidProfiles[1].Length]
        };
        liquidProfileOrders = new[]
        {
            new int[liquidProfiles[0].Length],
            new int[liquidProfiles[1].Length]
        };
        var profileEntryCapacity = liquidProfiles[0].Length + liquidProfiles[1].Length;
        liquidIds = new string[liquidTypeCapacity];
        liquidTypeColors = new Vector4[liquidTypeCapacity];
        liquidTypeDensities = new float[liquidTypeCapacity];
        initialSimulatedLiquidAmounts = new float[2 * liquidTypeCapacity];
        remainingProfileAmounts = new float[profileEntryCapacity];
        spawnWeightTotals = new float[3];
        spawnWeightPrefixes = new float[3 * liquidTypeCapacity];
        componentTypeIndices = new int[MaximumDrops + profileEntryCapacity];
        componentAmounts = new float[componentTypeIndices.Length];
        componentNext = new int[componentTypeIndices.Length];
        remainingSimulatedAmounts = new float[2];
        InitializeLiquidProfiles(firstAmount, secondAmount);
        InitializeLiquidNodePools();
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

        SetDropRadius(DefaultDropRadiusTenths / 10f);
        SetInitialPacking(initialPacking);
        containers = new[]
        {
            new ContainerShape(root, firstContainer, firstOutline),
            new ContainerShape(root, secondContainer, secondOutline)
        };
        openingTransitions = new bool[containers.Length];
        containerDropSizes = new[]
        {
            GetDropSizeForCapacity(firstCapacity),
            GetDropSizeForCapacity(secondCapacity)
        };
        CreateGraphics();
        SeedContainer(containers[0], GetInitialDropCount(firstAmount));
        SeedContainer(containers[1], GetInitialDropCount(secondAmount));
        initialContainerDropCounts = new[]
        {
            GetContainerDropCount(0),
            GetContainerDropCount(1)
        };
        DebugLog.Write(
            $"Simulation initialized: seeded "
            + $"{GetContainerDropCount(0)} receiver and {GetContainerDropCount(1)} source drops; "
            + $"drop radius={dropRadius:F1}, screen bounds=({minimumScreenX:F1},{minimumScreenY:F1})"
            + $"-({maximumScreenX:F1},{maximumScreenY:F1}).");
    }

    internal int Count => totalLiquidUnits;
    internal int LiquidTypeCount => liquidTypeCount;
    internal int LiquidTypeBufferLength => liquidTypeCapacity;

    internal string GetLiquidId(int typeIndex)
    {
        if ((uint)typeIndex >= liquidTypeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(typeIndex));
        }

        return liquidIds[typeIndex];
    }

    internal void GetInitialSimulatedLiquidAmounts(float[] amounts)
    {
        CopyLiquidAmounts(initialSimulatedLiquidAmounts, amounts);
    }

    internal void GetFinalSimulatedLiquidAmounts(float[] amounts)
    {
        if (amounts == null || amounts.Length < initialSimulatedLiquidAmounts.Length)
        {
            throw new ArgumentException("The liquid amount buffer is too small.", nameof(amounts));
        }

        Array.Clear(amounts, 0, initialSimulatedLiquidAmounts.Length);
        for (var drop = 0; drop < count; drop++)
        {
            var position = positions[drop];
            var inReceiver = containers[0].Contains(position);
            var inSource = containers[1].Contains(position);
            var containerIndex = inReceiver && inSource
                ? (containerAssignments[drop] == 2 ? 1 : 0)
                : inReceiver
                    ? 0
                    : inSource
                        ? 1
                        : -1;
            if (containerIndex < 0)
            {
                continue;
            }

            var outputOffset = containerIndex * liquidTypeCapacity;
            for (var unit = dropUnitHeads[drop]; unit >= 0; unit = unitNext[unit])
            {
                for (var component = unitComponentHeads[unit];
                    component >= 0;
                    component = componentNext[component])
                {
                    amounts[outputOffset + componentTypeIndices[component]] += componentAmounts[component];
                }
            }
        }
    }

    private void CopyLiquidAmounts(float[] source, float[] destination)
    {
        if (destination == null || destination.Length < source.Length)
        {
            throw new ArgumentException("The liquid amount buffer is too small.", nameof(destination));
        }

        Array.Copy(source, destination, source.Length);
    }

    private void InitializeLiquidProfiles(float firstAmount, float secondAmount)
    {
        var profileTotals = new float[2];
        for (var container = 0; container < liquidProfiles.Length; container++)
        {
            var profile = liquidProfiles[container];
            var profileTypeIndexes = liquidProfileTypeIndices[container];
            var profileOffset = container == 0 ? 0 : liquidProfiles[0].Length;
            for (var entry = 0; entry < profile.Length; entry++)
            {
                var liquid = profile[entry];
                if (string.IsNullOrWhiteSpace(liquid.LiquidId)
                    || liquid.Amount <= 0f
                    || float.IsNaN(liquid.Amount)
                    || float.IsInfinity(liquid.Amount))
                {
                    throw new InvalidOperationException(
                        $"Container {container + 1} has an invalid liquid profile entry at index {entry}.");
                }

                var typeIndex = FindLiquidType(liquid.LiquidId);
                if (typeIndex < 0)
                {
                    typeIndex = liquidTypeCount++;
                    liquidIds[typeIndex] = liquid.LiquidId;
                    liquidTypeColors[typeIndex] = new Vector4(
                        liquid.Color.r,
                        liquid.Color.g,
                        liquid.Color.b,
                        1f);
                    if (!LiquidDensityCatalog.TryGetDensity(liquid.LiquidId, out liquidTypeDensities[typeIndex]))
                    {
                        liquidTypeDensities[typeIndex] = LiquidDensityCatalog.DefaultDensity;
                        Plugin.LogWarning(
                            $"No density is defined for liquid '{liquid.LiquidId}'; "
                            + $"using {LiquidDensityCatalog.DefaultDensity:F2} g/ml.");
                    }
                }

                profileTypeIndexes[entry] = typeIndex;
                liquidProfileOrders[container][entry] = entry;
                remainingProfileAmounts[profileOffset + entry] = liquid.Amount;
                spawnWeightPrefixes[container * liquidTypeCapacity + typeIndex] += liquid.Amount;
                profileTotals[container] += liquid.Amount;
            }

            SortLiquidProfileByDensity(container);
        }

        var containerAmounts = new[] { firstAmount, secondAmount };
        for (var container = 0; container < 2; container++)
        {
            if (Mathf.Abs(profileTotals[container] - containerAmounts[container]) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"Liquid stack amounts in container {container + 1} sum to {profileTotals[container]:F3} ml, "
                    + $"but the game reports {containerAmounts[container]:F3} ml.");
            }

            remainingSimulatedAmounts[container] = Mathf.Min(
                profileTotals[container],
                GetInitialDropCount(containerAmounts[container]));
        }

        for (var container = 0; container < 2; container++)
        {
            var cumulative = 0f;
            var offset = container * liquidTypeCapacity;
            for (var type = 0; type < liquidTypeCount; type++)
            {
                cumulative += spawnWeightPrefixes[offset + type];
                spawnWeightPrefixes[offset + type] = cumulative;
            }

            spawnWeightTotals[container] = cumulative;
        }

        var allCumulative = 0f;
        var previousReceiverCumulative = 0f;
        var previousSourceCumulative = 0f;
        for (var type = 0; type < liquidTypeCount; type++)
        {
            var receiverCumulative = spawnWeightPrefixes[type];
            var sourceCumulative = spawnWeightPrefixes[liquidTypeCapacity + type];
            allCumulative += receiverCumulative - previousReceiverCumulative
                + sourceCumulative - previousSourceCumulative;
            spawnWeightPrefixes[2 * liquidTypeCapacity + type] = allCumulative;
            previousReceiverCumulative = receiverCumulative;
            previousSourceCumulative = sourceCumulative;
        }
        spawnWeightTotals[2] = allCumulative;
    }

    private void SortLiquidProfileByDensity(int containerIndex)
    {
        // Initial seeding traverses from the container bottom upward.
        var order = liquidProfileOrders[containerIndex];
        var typeIndices = liquidProfileTypeIndices[containerIndex];
        for (var index = 1; index < order.Length; index++)
        {
            var entry = order[index];
            var density = liquidTypeDensities[typeIndices[entry]];
            var insertion = index - 1;
            while (insertion >= 0
                && liquidTypeDensities[typeIndices[order[insertion]]] < density)
            {
                order[insertion + 1] = order[insertion];
                insertion--;
            }

            order[insertion + 1] = entry;
        }
    }

    private int FindLiquidType(string liquidId)
    {
        for (var type = 0; type < liquidTypeCount; type++)
        {
            if (string.Equals(liquidIds[type], liquidId, StringComparison.Ordinal))
            {
                return type;
            }
        }

        return -1;
    }

    private void InitializeLiquidNodePools()
    {
        for (var unit = 0; unit < MaximumDrops; unit++)
        {
            unitNext[unit] = unit + 1 < MaximumDrops ? unit + 1 : -1;
            dropUnitHeads[unit] = -1;
            dropUnitTails[unit] = -1;
            reorderedDropUnitHeads[unit] = -1;
            reorderedDropUnitTails[unit] = -1;
        }

        freeUnitHead = MaximumDrops > 0 ? 0 : -1;
        for (var component = 0; component < componentNext.Length; component++)
        {
            componentNext[component] = component + 1 < componentNext.Length
                ? component + 1
                : -1;
        }

        freeComponentHead = componentNext.Length > 0 ? 0 : -1;
    }

    private void CreateInitialDropUnits(int dropIndex, int containerIndex, int volume)
    {
        var head = -1;
        var tail = -1;
        for (var member = 0; member < volume; member++)
        {
            var unit = AllocateLiquidUnit();
            var remaining = Mathf.Min(1f, remainingSimulatedAmounts[containerIndex]);
            var profileOffset = containerIndex == 0 ? 0 : liquidProfiles[0].Length;
            var profile = liquidProfiles[containerIndex];
            var cursor = liquidProfileCursors[containerIndex];
            while (remaining > 0.00001f)
            {
                var entry = SelectInitialProfileEntry(containerIndex, ref cursor);
                if (entry < 0)
                {
                    break;
                }

                var amountIndex = profileOffset + entry;
                var available = remainingProfileAmounts[amountIndex];
                var amount = Mathf.Min(remaining, available);
                var typeIndex = liquidProfileTypeIndices[containerIndex][entry];
                AppendComponentToUnit(unit, typeIndex, amount);
                initialSimulatedLiquidAmounts[containerIndex * liquidTypeCapacity + typeIndex] += amount;
                remainingProfileAmounts[amountIndex] -= amount;
                remainingSimulatedAmounts[containerIndex] -= amount;
                remaining -= amount;
            }

            if (remaining > 0.0001f)
            {
                throw new InvalidOperationException(
                    $"Container {containerIndex + 1} has simulated liquid without a valid liquid ID.");
            }

            liquidProfileCursors[containerIndex] = cursor;
            AppendUnit(ref head, ref tail, unit);
        }

        dropUnitHeads[dropIndex] = head;
        dropUnitTails[dropIndex] = tail;
        dropColors[dropIndex] = GetColorForUnits(head);
    }

    private int SelectInitialProfileEntry(int containerIndex, ref int cursor)
    {
        var profile = liquidProfiles[containerIndex];
        var order = liquidProfileOrders[containerIndex];
        var typeIndices = liquidProfileTypeIndices[containerIndex];
        var profileOffset = containerIndex == 0 ? 0 : liquidProfiles[0].Length;
        while (cursor < profile.Length)
        {
            while (cursor < profile.Length
                && remainingProfileAmounts[profileOffset + order[cursor]] <= 0.00001f)
            {
                cursor++;
            }

            if (cursor >= profile.Length)
            {
                return -1;
            }

            var density = liquidTypeDensities[typeIndices[order[cursor]]];
            var groupEnd = cursor;
            var groupTotal = 0f;
            while (groupEnd < profile.Length
                && liquidTypeDensities[typeIndices[order[groupEnd]]] == density)
            {
                groupTotal += Mathf.Max(
                    0f,
                    remainingProfileAmounts[profileOffset + order[groupEnd]]);
                groupEnd++;
            }

            if (groupTotal <= 0f)
            {
                cursor = groupEnd;
                continue;
            }

            // Sampling by remaining volume interleaves liquids in the same density layer.
            var randomAmount = NextRandom() * groupTotal;
            var lastAvailableEntry = -1;
            for (var index = cursor; index < groupEnd; index++)
            {
                var entry = order[index];
                var available = remainingProfileAmounts[profileOffset + entry];
                if (available <= 0.00001f)
                {
                    continue;
                }

                lastAvailableEntry = entry;
                randomAmount -= available;
                if (randomAmount < 0f)
                {
                    return entry;
                }
            }

            return lastAvailableEntry;
        }

        return -1;
    }

    private void CreateSpawnDropUnits(int dropIndex, int assignment, int volume)
    {
        var head = -1;
        var tail = -1;
        for (var member = 0; member < volume; member++)
        {
            var typeIndex = SelectSpawnLiquidType(assignment);
            if (typeIndex < 0)
            {
                throw new InvalidOperationException(
                    "Cannot spawn simulated liquid because neither container has a liquid ID.");
            }

            var unit = AllocateLiquidUnit();
            AppendComponentToUnit(unit, typeIndex, 1f);
            AppendUnit(ref head, ref tail, unit);
        }

        dropUnitHeads[dropIndex] = head;
        dropUnitTails[dropIndex] = tail;
        dropColors[dropIndex] = GetColorForUnits(head);
    }

    private int SelectSpawnLiquidType(int assignment)
    {
        var profileIndex = assignment is 1 or 2 && spawnWeightTotals[assignment - 1] > 0f
            ? assignment - 1
            : 2;
        var total = spawnWeightTotals[profileIndex];
        if (total <= 0f)
        {
            return -1;
        }

        var randomWeight = NextRandom() * total;
        var offset = profileIndex * liquidTypeCapacity;
        var low = 0;
        var high = liquidTypeCount;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (spawnWeightPrefixes[offset + middle] <= randomWeight)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return Mathf.Min(low, liquidTypeCount - 1);
    }

    private int AllocateLiquidUnit()
    {
        if (freeUnitHead < 0)
        {
            throw new InvalidOperationException("The simulated liquid-unit pool is exhausted.");
        }

        var unit = freeUnitHead;
        freeUnitHead = unitNext[unit];
        unitNext[unit] = -1;
        unitComponentHeads[unit] = -1;
        unitComponentTails[unit] = -1;
        return unit;
    }

    private void AppendComponentToUnit(int unit, int typeIndex, float amount)
    {
        if (freeComponentHead < 0)
        {
            throw new InvalidOperationException("The simulated liquid-component pool is exhausted.");
        }

        var component = freeComponentHead;
        freeComponentHead = componentNext[component];
        componentTypeIndices[component] = typeIndex;
        componentAmounts[component] = amount;
        componentNext[component] = -1;
        if (unitComponentHeads[unit] < 0)
        {
            unitComponentHeads[unit] = component;
        }
        else
        {
            componentNext[unitComponentTails[unit]] = component;
        }

        unitComponentTails[unit] = component;
    }

    private void AppendUnit(ref int head, ref int tail, int unit)
    {
        if (head < 0)
        {
            head = unit;
        }
        else
        {
            unitNext[tail] = unit;
        }

        tail = unit;
    }

    private Vector4 GetColorForUnits(int head)
    {
        var color = Vector4.zero;
        var amountTotal = 0f;
        for (var unit = head; unit >= 0; unit = unitNext[unit])
        {
            for (var component = unitComponentHeads[unit];
                component >= 0;
                component = componentNext[component])
            {
                var amount = componentAmounts[component];
                color += liquidTypeColors[componentTypeIndices[component]] * amount;
                amountTotal += amount;
            }
        }

        return amountTotal > 0f
            ? new Vector4(color.x / amountTotal, color.y / amountTotal, color.z / amountTotal, 1f)
            : new Vector4(0.16f, 0.5f, 0.95f, 1f);
    }

    private void ReleaseDropLiquidUnits(int dropIndex)
    {
        var unit = dropUnitHeads[dropIndex];
        while (unit >= 0)
        {
            var nextUnit = unitNext[unit];
            var component = unitComponentHeads[unit];
            while (component >= 0)
            {
                var nextComponent = componentNext[component];
                componentNext[component] = freeComponentHead;
                freeComponentHead = component;
                component = nextComponent;
            }

            unitNext[unit] = freeUnitHead;
            freeUnitHead = unit;
            unit = nextUnit;
        }

        dropUnitHeads[dropIndex] = -1;
        dropUnitTails[dropIndex] = -1;
    }

    internal void SetDropRadius(float radius)
    {
        radius = Mathf.Clamp(
            radius,
            MinimumDropRadiusTenths / 10f,
            MaximumDropRadiusTenths / 10f);
        if (Mathf.Abs(dropRadius - radius) <= 0.001f)
        {
            return;
        }

        dropRadius = radius;
        dropCollisionDistance = dropRadius * 2f;
        dropCollisionDistanceSquared = dropCollisionDistance * dropCollisionDistance;
        var contactDistance = dropCollisionDistance + velocityContactMargin;
        velocityContactDistanceSquared = contactDistance * contactDistance;
        RebuildSpatialGrid();
        if (containers != null)
        {
            foreach (var container in containers)
            {
                container.SetMaximumDropRadius(dropRadius * 6.3245554f);
                container.RefreshBoundaries();
            }
        }
        graphicsDirty = true;
        DebugLog.Write($"Base drop radius changed to {dropRadius:F2}px.");
    }

    internal void SetInitialPacking(float packing)
    {
        initialPacking = Mathf.Clamp(
            packing,
            MinimumPackingTenths / 100f,
            MaximumPackingTenths / 100f);
        graphicsDirty = true;
        DebugLog.Write($"Initial drop packing changed to {initialPacking:F2}.");
    }

    private void RebuildSpatialGrid()
    {
        cellSize = dropRadius * 2f;
        gridColumns = Mathf.Max(1, Mathf.CeilToInt((maximumScreenX - minimumScreenX) / cellSize));
        gridRows = Mathf.Max(1, Mathf.CeilToInt((maximumScreenY - minimumScreenY) / cellSize));
        var cellCount = gridColumns * gridRows;
        cellCounts = new int[cellCount];
        cellActiveDropCounts = new int[cellCount];
        cellOffsets = new int[cellCount + 1];
        cellWriteOffsets = new int[cellCount];
        cellSafeWriteOffsets = new int[cellCount];
        cellParticleIndices = new int[MaximumDrops];
        activeCells = new int[cellCount];
        activeCellCount = 0;
    }

    internal void SetDropCollisionPasses(int passes)
    {
        dropCollisionPasses = Mathf.Clamp(
            passes,
            MinimumDropCollisionPasses,
            MaximumDropCollisionPasses);
    }

    internal void SetCollisionCorrectionPercent(int percent)
    {
        collisionCorrectionStrength = Mathf.Clamp(
            percent,
            MinimumCollisionCorrectionPercent,
            MaximumCollisionCorrectionPercent) / 100f;
    }

    internal void SetVelocityContactMarginTenths(int tenths)
    {
        var margin = Mathf.Clamp(
            tenths,
            MinimumVelocityContactMarginTenths,
            MaximumVelocityContactMarginTenths) / 10f;
        velocityContactMargin = margin;
        var contactDistance = dropCollisionDistance + margin;
        velocityContactDistanceSquared = contactDistance * contactDistance;
    }

    internal void SetSimulationFrozen(bool frozen)
    {
        simulationFrozen = frozen;
        spawnAccumulator = 0f;
        if (frozen)
        {
            simulationTime = 0f;
            Array.Clear(velocities, 0, velocities.Length);
        }
        DebugLog.Write($"Liquid simulation {(frozen ? "frozen" : "unfrozen")}.");
    }

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
            else if (inSource)
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
        if (simulationFrozen)
        {
            return;
        }
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
        if (simulationFrozen)
        {
            return;
        }
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
        if (simulationFrozen)
        {
            spawnAccumulator = 0f;
            simulationTime = 0f;
            if (graphicsDirty)
            {
                UpdateGraphics();
                graphicsDirty = false;
            }
            return;
        }
        velocityRampElapsed = Mathf.Min(
            VelocityRampDuration,
            velocityRampElapsed + deltaTime);
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
        var steps = 0;
        while (simulationTime >= FixedStep && steps < 3)
        {
            Step(FixedStep);
            simulationTime -= FixedStep;
            steps++;
        }

        if (topSeededDropId != 0 && DebugLog.IsFileEnabled)
        {
            for (var i = 0; i < count; i++)
            {
                if (dropIds[i] == topSeededDropId)
                {
                    var position = positions[i];
                    DebugLog.Write(
                        $"Tracked top seeded drop: id={topSeededDropId}, "
                        + $"container={containerAssignments[i]}, "
                        + $"position=({position.x:F2},{position.y:F2}), "
                        + $"radius={GetDropRadius(i):F2}.");
                    break;
                }
            }
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
        var remaining = amount;
        var targetVolume = GetContainerDropSize(containerIndex + 1);
        var row = 0;
        var topSeededPosition = Vector2.zero;
        for (var y = minimum.y;
            y <= maximum.y && remaining > 0 && count < MaximumDrops;
            y += dropRadius * Mathf.Sqrt(targetVolume) * initialPacking * 0.8660254f, row++)
        {
            var aggregateRadius = dropRadius * Mathf.Sqrt(targetVolume);
            var xOffset = (row & 1) == 0 ? 0f : aggregateRadius;
            for (var x = minimum.x + aggregateRadius + xOffset;
                x <= maximum.x - aggregateRadius
                    && remaining > 0
                    && count < MaximumDrops;
                x += aggregateRadius * initialPacking)
            {
                var candidate = new Vector2(x, y);
                var volume = Mathf.Min(targetVolume, remaining);
                var candidateRadius = dropRadius * Mathf.Sqrt(volume);
                var classification = container.Classify(candidate);
                if (!classification.IsInside
                    || classification.DistanceSquared < candidateRadius * candidateRadius)
                {
                    continue;
                }

                AddDrop(
                    candidate,
                    Vector2.zero,
                    (byte)(containerIndex + 1),
                    volume,
                    seedFromProfile: true);
                topSeededPosition = candidate;
                seeded += volume;
                remaining -= volume;
            }
        }

        if (topSeededDropId == 0 && seeded > 0)
        {
            topSeededDropId = nextDropId - 1;
            DebugLog.Write(
                $"Top seeded drop spawned: id={topSeededDropId}, container={containerIndex + 1}, "
                + $"position=({topSeededPosition.x:F2},{topSeededPosition.y:F2}), "
                + $"radius={dropRadius:F2}.");
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
        if (totalLiquidUnits >= MaximumDrops || liquidTypeCount == 0)
        {
            return;
        }

        var assignment = GetContainerAssignment(position, dropRadius);
        var volume = assignment > 0
            ? GetContainerDropSize(assignment)
            : 1;
        volume = Mathf.Min(volume, MaximumDrops - totalLiquidUnits);
        var aggregateRadius = dropRadius * Mathf.Sqrt(volume);
        var hexPosition = FindNearestEmptySpawnPosition(position, aggregateRadius);
        AddDrop(
            hexPosition,
            new Vector2(
                (NextRandom() * 2f - 1f) * 24f,
                (NextRandom() * 2f - 1f) * 12f),
            assignment,
            volume);
    }

    private Vector2 FindNearestEmptySpawnPosition(Vector2 position, float aggregateRadius)
    {
        var nearest = SnapToHexagonalDropPosition(position);
        if (IsPositionEmpty(nearest, aggregateRadius, -1))
        {
            return nearest;
        }

        for (var ring = 1; ring <= 12; ring++)
        {
            var ringStart = 1;
            for (var previousRing = 1; previousRing < ring; previousRing++)
            {
                ringStart += previousRing * 6;
            }
            for (var member = ringStart; member < ringStart + ring * 6; member++)
            {
                var candidate = nearest + GetSplitDropOffset(member) * (initialPacking * 0.5f);
                if (IsPositionEmpty(candidate, aggregateRadius, -1))
                {
                    return candidate;
                }
            }
        }

        return nearest;
    }

    private Vector2 SnapToHexagonalDropPosition(Vector2 position)
    {
        var horizontalSpacing = dropRadius * initialPacking;
        var rowSpacing = dropRadius * initialPacking * 0.8660254f;
        var row = Mathf.RoundToInt(position.y / rowSpacing);
        var xOffset = (Mathf.Abs(row) & 1) != 0 ? horizontalSpacing * 0.5f : 0f;
        var column = Mathf.RoundToInt((position.x - xOffset) / horizontalSpacing);
        return new Vector2(
            column * horizontalSpacing + xOffset,
            row * rowSpacing);
    }

    private Vector2 GetHexRingOffset(int member, int ring)
    {
        var axialX = ring;
        var axialY = 0;
        var ringIndex = member;
        for (var side = 0; side < 6 && ringIndex >= 0; side++)
        {
            var stepCount = Mathf.Min(ringIndex, ring);
            MoveAlongHexRingSide(side, stepCount, ref axialX, ref axialY);
            ringIndex -= stepCount;
        }

        return new Vector2(
            (axialX + axialY * 0.5f) * dropRadius * 2f,
            axialY * dropRadius * 1.7320508f);
    }

    private static void MoveAlongHexRingSide(
        int side,
        int steps,
        ref int axialX,
        ref int axialY)
    {
        switch (side)
        {
            case 0:
                axialX -= steps;
                axialY += steps;
                break;
            case 1:
                axialX -= steps;
                break;
            case 2:
                axialY -= steps;
                break;
            case 3:
                axialX += steps;
                axialY -= steps;
                break;
            case 4:
                axialX += steps;
                break;
            default:
                axialY += steps;
                break;
        }
    }

    private bool IsPositionEmpty(
        Vector2 position,
        float radius,
        int excludedIndex)
    {
        foreach (var container in containers)
        {
            var classification = container.Classify(position);
            if (!classification.IsInside)
            {
                continue;
            }

            if (classification.DistanceSquared >= radius * radius)
            {
                for (var i = 0; i < count; i++)
                {
                    if (i == excludedIndex)
                    {
                        continue;
                    }

                    var minimumDistance = radius + GetDropRadius(i);
                    if ((positions[i] - position).sqrMagnitude < minimumDistance * minimumDistance)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        return false;
    }

    private void AddDrop(Vector2 position, Vector2 velocity, byte containerAssignment = 0)
    {
        AddDrop(position, velocity, containerAssignment, 1);
    }

    private void AddDrop(
        Vector2 position,
        Vector2 velocity,
        byte containerAssignment,
        int volume,
        bool seedFromProfile = false)
    {
        if (count >= MaximumDrops)
        {
            return;
        }

        volume = Mathf.Clamp(volume, 1, MaximumDropVolume);
        if (totalLiquidUnits + volume > MaximumDrops)
        {
            volume = MaximumDrops - totalLiquidUnits;
            if (volume <= 0)
            {
                return;
            }
        }

        positions[count] = position;
        previousPositions[count] = position;
        velocities[count] = velocity;
        fastOutsideDrops[count] = false;
        dropIds[count] = nextDropId++;
        dropVolumes[count] = (byte)volume;
        var assignment = containerAssignment == 0
            ? GetContainerAssignment(position, dropRadius)
            : containerAssignment;
        containerAssignments[count] = assignment;
        dropSizeZoneAssignments[count] = assignment > 0
            && containers[assignment - 1].IsPastSizeZone(position)
                ? assignment
                : (byte)0;
        if (seedFromProfile)
        {
            CreateInitialDropUnits(count, assignment - 1, volume);
        }
        else
        {
            CreateSpawnDropUnits(count, assignment, volume);
        }
        densitySwapPaths[count] = new Vector4(position.x, position.y, position.x, position.y);
        densitySwapProgress[count] = -1f;
        mergeDelayRemaining[count] = volume == 1
            && assignment > 0
            && containers[assignment - 1].IsPastMergeSizeZone(position)
                ? MergeDelaySeconds
                : 0f;
        count++;
        totalLiquidUnits += volume;
        graphicsDirty = true;
    }

    private void Step(float deltaTime)
    {
        var profilingStart = SimulationProfiler.Begin();
        RefreshContainerGeometry();
        var velocityRampRatio = Mathf.Lerp(
            0f,
            1f,
            Mathf.Clamp01(velocityRampElapsed / VelocityRampDuration));
        UpdateOutsideDropFastPath(deltaTime, velocityRampRatio);
        densitySwapCooldownRemaining = Mathf.Max(
            0f,
            densitySwapCooldownRemaining - deltaTime);
        for (var i = 0; i < count; i++)
        {
            previousPositions[i] = positions[i];
            mergeDelayRemaining[i] = Mathf.Max(0f, mergeDelayRemaining[i] - deltaTime);
            if (activeDensitySwapCount > 0 && densitySwapProgress[i] >= 0f)
            {
                var progress = Mathf.Min(
                    1f,
                    densitySwapProgress[i] + deltaTime / DensitySwapDuration);
                densitySwapProgress[i] = progress;
                var easedProgress = progress * progress * (3f - 2f * progress);
                var path = densitySwapPaths[i];
                positions[i] = Vector2.Lerp(
                    new Vector2(path.x, path.y),
                    new Vector2(path.z, path.w),
                    easedProgress);
                if (progress >= 1f)
                {
                    positions[i] = new Vector2(path.z, path.w);
                    densitySwapProgress[i] = -1f;
                    densitySwapCooldownRemaining = DensitySwapCooldown;
                    activeDensitySwapCount--;
                }

                continue;
            }

            velocities[i].y -= Gravity * GravityMultiplier * deltaTime;
            velocities[i] *= velocityRampRatio;
            positions[i] += velocities[i] * deltaTime;
        }
        SimulationProfiler.End(SimulationProfileStage.GeometryRefreshAndIntegration, profilingStart);
        if (activeDropCount > 0)
        {
            ResolveDropCollisions();
        }

        profilingStart = SimulationProfiler.Begin();
        for (var i = 0; i < count; i++)
        {
            velocities[i] = (positions[i] - previousPositions[i]) / deltaTime;
        }
        SimulationProfiler.End(SimulationProfileStage.VelocityReconstruction, profilingStart);

        if (activeDropCount > 0)
        {
            profilingStart = SimulationProfiler.Begin();
            ResolveDropVelocities();
            SimulationProfiler.End(SimulationProfileStage.VelocityConstraints, profilingStart);
        }
        else
        {
            if (hasUnassignedAggregate)
            {
                NormalizeDropSizes();
            }
        }

        profilingStart = SimulationProfiler.Begin();
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

    private void UpdateOutsideDropFastPath(float deltaTime, float velocityRampRatio)
    {
        activeDropCount = 0;
        hasUnassignedAggregate = false;
        for (var i = 0; i < count; i++)
        {
            fastOutsideDrops[i] = false;
            if (containerAssignments[i] != 0)
            {
                activeDropCount++;
                continue;
            }
            if (dropVolumes[i] > 1)
            {
                hasUnassignedAggregate = true;
            }

            var position = positions[i];
            var radius = GetDropRadius(i);
            var predictedVelocity = velocities[i];
            predictedVelocity.y -= Gravity * GravityMultiplier * deltaTime;
            predictedVelocity *= velocityRampRatio;
            var predictedPosition = position + predictedVelocity * deltaTime;
            var boundsPadding = radius + dropRadius * OutsideDropExtraBoundsPadding;
            var safelyOutside = true;
            foreach (var container in containers)
            {
                if (container.SweptBoundsOverlap(position, predictedPosition, boundsPadding)
                    || Vector2.Dot(
                        position - container.Center,
                        predictedPosition - position) <= 0f)
                {
                    safelyOutside = false;
                    break;
                }
            }

            fastOutsideDrops[i] = safelyOutside;
            if (!safelyOutside)
            {
                activeDropCount++;
            }
        }
    }

    private void RemoveOffscreenDrops()
    {
        for (var i = 0; i < count;)
        {
            if (activeDensitySwapCount > 0 && densitySwapProgress[i] >= 0f)
            {
                i++;
                continue;
            }

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
            ReleaseDropLiquidUnits(i);
            if (i != count)
            {
                positions[i] = positions[count];
                previousPositions[i] = previousPositions[count];
                velocities[i] = velocities[count];
                dropIds[i] = dropIds[count];
                dropVolumes[i] = dropVolumes[count];
                containerAssignments[i] = containerAssignments[count];
                dropSizeZoneAssignments[i] = dropSizeZoneAssignments[count];
                dropColors[i] = dropColors[count];
                densitySwapProgress[i] = densitySwapProgress[count];
                if (activeDensitySwapCount > 0)
                {
                    densitySwapPaths[i] = densitySwapPaths[count];
                }
                dropUnitHeads[i] = dropUnitHeads[count];
                dropUnitTails[i] = dropUnitTails[count];
                mergeDelayRemaining[i] = mergeDelayRemaining[count];
                fastOutsideDrops[i] = fastOutsideDrops[count];
            }
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
            else if (volume > 1)
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

    private byte GetContainerAssignment(Vector2 position, float radius)
    {
        for (var i = 0; i < containers.Length; i++)
        {
            if ((radius <= dropRadius && containers[i].IsInsidePaddedRectangle(position))
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
            if (fastOutsideDrops[i]
                || (activeDensitySwapCount > 0 && densitySwapProgress[i] >= 0f))
            {
                continue;
            }

            var position = positions[i];
            var radius = GetDropRadius(i);
            var previousPosition = previousPositions[i];
            var velocity = velocities[i];
            var assignment = containerAssignments[i];
            var sizeZoneAssignment = dropSizeZoneAssignments[i];
            var anyOpeningTransition = false;
            var firstContainerIndex = assignment > 0 && assignment <= containers.Length
                ? assignment - 1
                : 0;
            for (var containerOffset = 0; containerOffset < containers.Length; containerOffset++)
            {
                var containerIndex = (firstContainerIndex + containerOffset) % containers.Length;
                var container = containers[containerIndex];
                if (container.CrossesSizeZone(previousPosition, position, out var enteringSizeZone))
                {
                    var containerAssignment = (byte)(containerIndex + 1);
                    if (enteringSizeZone)
                    {
                        sizeZoneAssignment = containerAssignment;
                    }
                    else if (sizeZoneAssignment == containerAssignment)
                    {
                        sizeZoneAssignment = 0;
                    }
                }

                if (container.CrossesMergeSizeZone(previousPosition, position, out var enteringMergeZone))
                {
                    if (enteringMergeZone)
                    {
                        mergeDelayRemaining[i] = MergeDelaySeconds;
                    }
                    else
                    {
                        mergeDelayRemaining[i] = 0f;
                    }
                }

                openingTransitions[containerIndex] =
                    container.CrossesOpening(previousPosition, position)
                    || container.IsNearOpening(previousPosition, radius)
                    || container.IsNearOpening(position, radius);
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
                if (shouldBeInside && radius <= dropRadius && container.IsInsidePaddedRectangle(position))
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
            dropSizeZoneAssignments[i] = sizeZoneAssignment;
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
        Array.Clear(cellActiveDropCounts, 0, cellActiveDropCounts.Length);
        for (var i = 0; i < count; i++)
        {
            var cell = GetCell(positions[i]);
            cellCounts[cell]++;
            if (!fastOutsideDrops[i])
            {
                cellActiveDropCounts[cell]++;
            }
        }

        var particleOffset = 0;
        activeCellCount = 0;
        for (var cell = 0; cell < cellCounts.Length; cell++)
        {
            cellOffsets[cell] = particleOffset;
            cellWriteOffsets[cell] = particleOffset;
            cellSafeWriteOffsets[cell] = particleOffset + cellActiveDropCounts[cell];
            if (cellCounts[cell] > 0)
            {
                activeCells[activeCellCount++] = cell;
            }
            particleOffset += cellCounts[cell];
        }
        cellOffsets[cellCounts.Length] = particleOffset;

        for (var i = 0; i < count; i++)
        {
            var cell = GetCell(positions[i]);
            if (fastOutsideDrops[i])
            {
                cellParticleIndices[cellSafeWriteOffsets[cell]++] = i;
            }
            else
            {
                cellParticleIndices[cellWriteOffsets[cell]++] = i;
            }
        }

        ReorderParticlesByCell();
    }

    private Vector2 FindNearestEmptyAggregatePosition(
        int[] pendingIndices,
        int pendingCount,
        byte containerAssignment,
        int targetVolume)
    {
        var aggregateRadius = dropRadius * Mathf.Sqrt(targetVolume);
        var container = containers[containerAssignment - 1];
        var centroid = Vector2.zero;
        for (var i = 0; i < pendingCount; i++)
        {
            var position = positions[pendingIndices[i]];
            centroid += position;
        }
        centroid /= Mathf.Max(1, pendingCount);

        for (var i = 0; i < pendingCount; i++)
        {
            var position = positions[pendingIndices[i]];
            if (IsAggregatePositionEmpty(
                    position,
                    aggregateRadius,
                    container,
                    pendingIndices,
                    pendingCount))
            {
                return position;
            }
        }

        if (IsAggregatePositionEmpty(
                centroid,
                aggregateRadius,
                container,
                pendingIndices,
                pendingCount))
        {
            return centroid;
        }

        for (var ring = 1; ring <= 16; ring++)
        {
            var ringStart = 1;
            for (var previousRing = 1; previousRing < ring; previousRing++)
            {
                ringStart += previousRing * 6;
            }

            for (var member = ringStart; member < ringStart + ring * 6; member++)
            {
                var candidate = centroid + GetSplitDropOffset(member);
                if (IsAggregatePositionEmpty(
                        candidate,
                        aggregateRadius,
                        container,
                        pendingIndices,
                        pendingCount))
                {
                    return candidate;
                }
            }
        }

        return centroid;
    }

    private bool IsAggregatePositionEmpty(
        Vector2 position,
        float radius,
        ContainerShape container,
        int[] pendingIndices,
        int pendingCount)
    {
        var classification = container.Classify(position);
        if (!classification.IsInside
            || classification.DistanceSquared < radius * radius)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var isPending = false;
            for (var pending = 0; pending < pendingCount; pending++)
            {
                if (pendingIndices[pending] == i)
                {
                    isPending = true;
                    break;
                }
            }

            if (isPending)
            {
                continue;
            }

            var minimumDistance = radius + GetDropRadius(i);
            if ((positions[i] - position).sqrMagnitude < minimumDistance * minimumDistance)
            {
                return false;
            }
        }

        return true;
    }

    private bool NormalizeDropSizes()
    {
        var requiresNormalization = false;
        for (var assignment = 0; assignment <= containers.Length; assignment++)
        {
            var targetVolume = assignment == 0
                ? 1
                : GetContainerDropSize(assignment);
            var eligibleSingleDropCount = 0;
            for (var i = 0; i < count; i++)
            {
                if (dropSizeZoneAssignments[i] != assignment)
                {
                    continue;
                }

                var volume = dropVolumes[i];
                if (targetVolume == 1)
                {
                    if (volume != 1)
                    {
                        requiresNormalization = true;
                        break;
                    }
                    continue;
                }

                if (mergeDelayRemaining[i] > 0f)
                {
                    if (volume > 1)
                    {
                        requiresNormalization = true;
                        break;
                    }
                    continue;
                }

                if (volume == targetVolume)
                {
                    continue;
                }

                if (volume == 1)
                {
                    eligibleSingleDropCount++;
                    continue;
                }

                if (volume > targetVolume)
                {
                    requiresNormalization = true;
                    break;
                }
            }

            if (eligibleSingleDropCount >= targetVolume)
            {
                requiresNormalization = true;
            }
        }

        if (!requiresNormalization)
        {
            return false;
        }

        var originalDropCount = count;
        var outputCount = 0;
        for (var assignment = 0; assignment <= containers.Length; assignment++)
        {
            var targetVolume = assignment == 0
                ? 1
                : GetContainerDropSize(assignment);
            if (assignment == 0)
            {
                for (var i = 0; i < originalDropCount; i++)
                {
                    if (dropSizeZoneAssignments[i] != 0)
                    {
                        continue;
                    }

                    if (dropVolumes[i] == 1)
                    {
                        CopyNormalizedDrop(i, outputCount++);
                        continue;
                    }

                    WriteSplitDrops(i, 0, ref outputCount);
                }

                continue;
            }

            var pendingCount = 0;
            for (var i = 0; i < originalDropCount; i++)
            {
                if (dropSizeZoneAssignments[i] != assignment)
                {
                    continue;
                }

                if (activeDensitySwapCount > 0 && densitySwapProgress[i] >= 0f)
                {
                    CopyNormalizedDrop(i, outputCount++);
                    continue;
                }

                if (dropVolumes[i] == targetVolume && mergeDelayRemaining[i] <= 0f)
                {
                    CopyNormalizedDrop(i, outputCount++);
                    continue;
                }

                if (dropVolumes[i] > 1
                    && dropVolumes[i] < targetVolume
                    && mergeDelayRemaining[i] <= 0f)
                {
                    CopyNormalizedDrop(i, outputCount++);
                    continue;
                }

                if (dropVolumes[i] == 1 && mergeDelayRemaining[i] > 0f)
                {
                    CopyNormalizedDrop(i, outputCount++);
                    continue;
                }

                if (dropVolumes[i] == 1
                    && targetVolume > 1
                    && mergeDelayRemaining[i] <= 0f
                    && containers[assignment - 1].IsPastMergeSizeZone(positions[i]))
                {
                    pendingMergeIndices[pendingCount++] = i;
                    if (pendingCount == targetVolume)
                    {
                        var first = pendingMergeIndices[0];
                        var aggregatePosition = FindNearestEmptyAggregatePosition(
                            pendingMergeIndices,
                            pendingCount,
                            (byte)assignment,
                            targetVolume);
                        WriteNormalizedDrop(
                            outputCount++,
                            targetVolume,
                            containerAssignments[first],
                            (byte)assignment,
                            0f,
                            aggregatePosition,
                            aggregatePosition,
                            velocities[first]);
                        MergePendingDropUnits(outputCount - 1, pendingCount);
                        pendingCount = 0;
                    }

                    continue;
                }

                WriteSplitDrops(i, (byte)assignment, ref outputCount);
            }

            for (var pending = 0; pending < pendingCount; pending++)
            {
                CopyNormalizedDrop(pendingMergeIndices[pending], outputCount++);
            }
        }

        count = outputCount;
        if (DebugLog.IsFileEnabled)
        {
            var particleCountChange = originalDropCount - outputCount;
            if (particleCountChange > 0)
            {
                mergedAggregatesThisInterval += particleCountChange;
            }
            else if (particleCountChange < 0)
            {
                var splitCount = -particleCountChange;
                splitAggregatesThisInterval += splitCount;
                largestSplitBatchThisInterval = Mathf.Max(largestSplitBatchThisInterval, splitCount);
            }
        }

        SwapBuffers(ref positions, ref reorderedPositions);
        SwapBuffers(ref previousPositions, ref reorderedPreviousPositions);
        SwapBuffers(ref velocities, ref reorderedVelocities);
        SwapBuffers(ref mergeDelayRemaining, ref reorderedMergeDelayRemaining);
        SwapBuffers(ref dropIds, ref reorderedDropIds);
        SwapBuffers(ref dropVolumes, ref reorderedDropVolumes);
        SwapBuffers(ref containerAssignments, ref reorderedContainerAssignments);
        SwapBuffers(ref dropSizeZoneAssignments, ref reorderedDropSizeZoneAssignments);
        SwapBuffers(ref dropColors, ref reorderedDropColors);
        SwapBuffers(ref densitySwapProgress, ref reorderedDensitySwapProgress);
        if (activeDensitySwapCount > 0)
        {
            SwapBuffers(ref densitySwapPaths, ref reorderedDensitySwapPaths);
        }
        SwapBuffers(ref dropUnitHeads, ref reorderedDropUnitHeads);
        SwapBuffers(ref dropUnitTails, ref reorderedDropUnitTails);
        SwapBuffers(ref fastOutsideDrops, ref reorderedFastOutsideDrops);
        graphicsDirty = true;
        return true;
    }

    private void WriteSplitDrops(int source, byte sizeZoneAssignment, ref int outputCount)
    {
        var unit = dropUnitHeads[source];
        for (var member = 0; member < dropVolumes[source]; member++)
        {
            if (unit < 0)
            {
                throw new InvalidOperationException("A merged liquid drop lost one of its constituent units.");
            }

            var nextUnit = unitNext[unit];
            unitNext[unit] = -1;
            var offset = GetSplitDropOffset(member);
            WriteNormalizedDrop(
                outputCount++,
                1,
                containerAssignments[source],
                sizeZoneAssignment,
                mergeDelayRemaining[source],
                positions[source] + offset,
                previousPositions[source] + offset,
                velocities[source]);
            var output = outputCount - 1;
            reorderedDropUnitHeads[output] = unit;
            reorderedDropUnitTails[output] = unit;
            reorderedDropColors[output] = GetColorForUnits(unit);
            unit = nextUnit;
        }

        if (unit >= 0)
        {
            throw new InvalidOperationException("A merged liquid drop has more constituent units than its volume.");
        }
    }

    private void CopyNormalizedDrop(int source, int destination)
    {
        reorderedPositions[destination] = positions[source];
        reorderedPreviousPositions[destination] = previousPositions[source];
        reorderedVelocities[destination] = velocities[source];
        reorderedMergeDelayRemaining[destination] = mergeDelayRemaining[source];
        reorderedDropIds[destination] = dropIds[source];
        reorderedDropVolumes[destination] = dropVolumes[source];
        reorderedContainerAssignments[destination] = containerAssignments[source];
        reorderedDropSizeZoneAssignments[destination] = dropSizeZoneAssignments[source];
        reorderedDropColors[destination] = dropColors[source];
        reorderedDensitySwapProgress[destination] = densitySwapProgress[source];
        if (activeDensitySwapCount > 0)
        {
            reorderedDensitySwapPaths[destination] = densitySwapPaths[source];
        }
        reorderedDropUnitHeads[destination] = dropUnitHeads[source];
        reorderedDropUnitTails[destination] = dropUnitTails[source];
        reorderedFastOutsideDrops[destination] = fastOutsideDrops[source];
    }

    private void MergePendingDropUnits(int destination, int pendingCount)
    {
        var first = pendingMergeIndices[0];
        var head = dropUnitHeads[first];
        var tail = dropUnitTails[first];
        var next = unitNext[tail];
        if (next >= 0)
        {
            throw new InvalidOperationException("A single liquid drop contains more than one constituent unit.");
        }

        for (var pending = 1; pending < pendingCount; pending++)
        {
            var source = pendingMergeIndices[pending];
            var sourceHead = dropUnitHeads[source];
            var sourceTail = dropUnitTails[source];
            if (sourceHead < 0 || sourceHead != sourceTail || unitNext[sourceTail] >= 0)
            {
                throw new InvalidOperationException("A single liquid drop has invalid constituent-unit data.");
            }

            unitNext[tail] = sourceHead;
            tail = sourceTail;
        }

        reorderedDropUnitHeads[destination] = head;
        reorderedDropUnitTails[destination] = tail;
        reorderedDropColors[destination] = GetColorForUnits(head);
    }

    private Vector2 GetSplitDropOffset(int member)
    {
        if (member == 0)
        {
            return Vector2.zero;
        }

        var ring = 1;
        var firstRingMember = 1;
        while (member >= firstRingMember + ring * 6)
        {
            firstRingMember += ring * 6;
            ring++;
        }

        var ringIndex = member - firstRingMember;
        var side = ringIndex / ring;
        var stepsAlongSide = ringIndex % ring;
        var axialX = ring;
        var axialY = 0;
        for (var currentSide = 0; currentSide < side; currentSide++)
        {
            MoveAlongSplitRingSide(currentSide, ring, ref axialX, ref axialY);
        }
        MoveAlongSplitRingSide(side, stepsAlongSide, ref axialX, ref axialY);

        return new Vector2(
            (axialX + axialY * 0.5f) * dropRadius * 2f,
            axialY * dropRadius * 1.7320508f);
    }

    private static void MoveAlongSplitRingSide(
        int side,
        int steps,
        ref int axialX,
        ref int axialY)
    {
        switch (side)
        {
            case 0:
                axialX -= steps;
                axialY += steps;
                break;
            case 1:
                axialX -= steps;
                break;
            case 2:
                axialY -= steps;
                break;
            case 3:
                axialX += steps;
                axialY -= steps;
                break;
            case 4:
                axialX += steps;
                break;
            default:
                axialY += steps;
                break;
        }
    }

    private int GetContainerDropSize(int assignment)
    {
        return containerDropSizes[assignment - 1];
    }

    private static int GetDropSizeForCapacity(float capacity)
    {
        if (float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        return Mathf.Clamp(
            Mathf.FloorToInt(capacity / MillilitersPerDropTier) + 1,
            1,
            MaximumDropVolume);
    }

    private void WriteNormalizedDrop(
        int index,
        int volume,
        byte containerAssignment,
        byte sizeZoneAssignment,
        float mergeDelay,
        Vector2 position,
        Vector2 previousPosition,
        Vector2 velocity)
    {
        reorderedPositions[index] = position;
        reorderedPreviousPositions[index] = previousPosition;
        reorderedVelocities[index] = velocity;
        reorderedMergeDelayRemaining[index] = mergeDelay;
        reorderedDropIds[index] = nextDropId++;
        reorderedDropVolumes[index] = (byte)volume;
        reorderedContainerAssignments[index] = containerAssignment;
        reorderedDropSizeZoneAssignments[index] = sizeZoneAssignment;
        reorderedDensitySwapPaths[index] = new Vector4(
            position.x,
            position.y,
            position.x,
            position.y);
        reorderedDensitySwapProgress[index] = -1f;
        reorderedDropUnitHeads[index] = -1;
        reorderedDropUnitTails[index] = -1;
        reorderedFastOutsideDrops[index] = false;
    }

    private float GetDropRadius(int index)
    {
        return dropRadius * Mathf.Sqrt(Mathf.Max(1, dropVolumes[index]));
    }

    private void ReorderParticlesByCell()
    {
        for (var sortedIndex = 0; sortedIndex < count; sortedIndex++)
        {
            var sourceIndex = cellParticleIndices[sortedIndex];
            reorderedPositions[sortedIndex] = positions[sourceIndex];
            reorderedPreviousPositions[sortedIndex] = previousPositions[sourceIndex];
            reorderedVelocities[sortedIndex] = velocities[sourceIndex];
            reorderedMergeDelayRemaining[sortedIndex] = mergeDelayRemaining[sourceIndex];
            reorderedDropIds[sortedIndex] = dropIds[sourceIndex];
            reorderedDropVolumes[sortedIndex] = dropVolumes[sourceIndex];
            reorderedContainerAssignments[sortedIndex] = containerAssignments[sourceIndex];
            reorderedDropSizeZoneAssignments[sortedIndex] = dropSizeZoneAssignments[sourceIndex];
            reorderedDropColors[sortedIndex] = dropColors[sourceIndex];
            reorderedDensitySwapProgress[sortedIndex] = densitySwapProgress[sourceIndex];
            if (activeDensitySwapCount > 0)
            {
                reorderedDensitySwapPaths[sortedIndex] = densitySwapPaths[sourceIndex];
            }
            reorderedDropUnitHeads[sortedIndex] = dropUnitHeads[sourceIndex];
            reorderedDropUnitTails[sortedIndex] = dropUnitTails[sourceIndex];
            reorderedFastOutsideDrops[sortedIndex] = fastOutsideDrops[sourceIndex];
        }

        SwapBuffers(ref positions, ref reorderedPositions);
        SwapBuffers(ref previousPositions, ref reorderedPreviousPositions);
        SwapBuffers(ref velocities, ref reorderedVelocities);
        SwapBuffers(ref mergeDelayRemaining, ref reorderedMergeDelayRemaining);
        SwapBuffers(ref dropIds, ref reorderedDropIds);
        SwapBuffers(ref dropVolumes, ref reorderedDropVolumes);
        SwapBuffers(ref containerAssignments, ref reorderedContainerAssignments);
        SwapBuffers(ref dropSizeZoneAssignments, ref reorderedDropSizeZoneAssignments);
        SwapBuffers(ref dropColors, ref reorderedDropColors);
        SwapBuffers(ref densitySwapProgress, ref reorderedDensitySwapProgress);
        if (activeDensitySwapCount > 0)
        {
            SwapBuffers(ref densitySwapPaths, ref reorderedDensitySwapPaths);
        }
        SwapBuffers(ref dropUnitHeads, ref reorderedDropUnitHeads);
        SwapBuffers(ref dropUnitTails, ref reorderedDropUnitTails);
        SwapBuffers(ref fastOutsideDrops, ref reorderedFastOutsideDrops);
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
        TryStartDensitySwaps();
    }

    private void TryStartDensitySwaps()
    {
        if (count == 0
            || activeDensitySwapCount >= MaximumConcurrentDensitySwapDrops
            || densitySwapCooldownRemaining > 0f)
        {
            return;
        }

        densitySwapCheckTimer += FixedStep;
        if (densitySwapCheckTimer < DensitySwapCheckInterval)
        {
            return;
        }

        densitySwapCheckTimer %= DensitySwapCheckInterval;
        densitySwapScanCursor %= count;
        var candidatesToCheck = Mathf.Min(count, DensitySwapCandidateBudget);
        var maximumSpeedSquared = DensitySwapMaximumSpeed * DensitySwapMaximumSpeed;
        for (var candidateIndex = 0; candidateIndex < candidatesToCheck; candidateIndex++)
        {
            var drop = densitySwapScanCursor++;
            if (densitySwapScanCursor >= count)
            {
                densitySwapScanCursor = 0;
            }

            if (activeDensitySwapCount >= MaximumConcurrentDensitySwapDrops)
            {
                break;
            }

            if (densitySwapProgress[drop] >= 0f
                || dropVolumes[drop] != 1
                || containerAssignments[drop] == 0
                || containerAssignments[drop] > containers.Length
                || velocities[drop].sqrMagnitude > maximumSpeedSquared)
            {
                continue;
            }

            var containerIndex = containerAssignments[drop] - 1;
            var position = positions[drop];
            if (!containers[containerIndex].Contains(position))
            {
                continue;
            }

            var dropDensity = GetDensityForDrop(drop);
            if (!TryFindHigherDensityDrop(
                    drop,
                    containerIndex,
                    dropDensity,
                    out var higherDensityDrop))
            {
                continue;
            }

            var radius = GetDropRadius(drop);
            if (!HasDenseNeighbors(drop, higherDensityDrop, containerAssignments[drop], radius)
                || !HasDenseNeighbors(
                    higherDensityDrop,
                    drop,
                    containerAssignments[drop],
                    radius)
                || !IsDensitySwapPathClear(
                    containerIndex,
                    position,
                    positions[higherDensityDrop],
                    radius))
            {
                continue;
            }

            var higherPosition = positions[higherDensityDrop];
            densitySwapPaths[drop] = new Vector4(
                position.x,
                position.y,
                higherPosition.x,
                higherPosition.y);
            densitySwapPaths[higherDensityDrop] = new Vector4(
                higherPosition.x,
                higherPosition.y,
                position.x,
                position.y);
            densitySwapProgress[drop] = 0f;
            densitySwapProgress[higherDensityDrop] = 0f;
            activeDensitySwapCount += 2;
        }
    }

    private bool TryFindHigherDensityDrop(
        int drop,
        int containerIndex,
        float dropDensity,
        out int higherDensityDrop)
    {
        higherDensityDrop = -1;
        var radius = GetDropRadius(drop);
        var maximumHorizontalOffset = radius * 0.65f;
        var minimumVerticalOffset = radius * 1.35f;
        var maximumVerticalOffset = radius * 2.15f;
        var maximumDistanceSquared = radius * radius * 2.05f * 2.05f;
        var searchRadius = radius * 2.15f;
        var cellRadius = Mathf.CeilToInt(searchRadius / cellSize) + 1;
        var centerCell = GetCell(positions[drop]);
        var centerCellX = centerCell % gridColumns;
        var centerCellY = centerCell / gridColumns;
        var assignment = (byte)(containerIndex + 1);
        var higherDensity = dropDensity + DensitySwapComparisonTolerance;

        for (var cellY = Mathf.Max(0, centerCellY - cellRadius);
             cellY <= Mathf.Min(gridRows - 1, centerCellY + cellRadius);
             cellY++)
        {
            for (var cellX = Mathf.Max(0, centerCellX - cellRadius);
                 cellX <= Mathf.Min(gridColumns - 1, centerCellX + cellRadius);
                 cellX++)
            {
                var cell = cellY * gridColumns + cellX;
                var cellEnd = cellOffsets[cell] + cellActiveDropCounts[cell];
                for (var neighbor = cellOffsets[cell]; neighbor < cellEnd; neighbor++)
                {
                    if (neighbor == drop
                        || containerAssignments[neighbor] != assignment
                        || dropVolumes[neighbor] != dropVolumes[drop]
                        || densitySwapProgress[neighbor] >= 0f
                        || velocities[neighbor].sqrMagnitude > DensitySwapMaximumSpeed * DensitySwapMaximumSpeed)
                    {
                        continue;
                    }

                    var difference = positions[neighbor] - positions[drop];
                    if (difference.y < minimumVerticalOffset
                        || difference.y > maximumVerticalOffset
                        || Mathf.Abs(difference.x) > maximumHorizontalOffset
                        || difference.sqrMagnitude > maximumDistanceSquared
                        || !containers[containerIndex].Contains(positions[neighbor]))
                    {
                        continue;
                    }

                    var density = GetDensityForDrop(neighbor);
                    if (density <= higherDensity)
                    {
                        continue;
                    }

                    higherDensity = density;
                    higherDensityDrop = neighbor;
                }
            }
        }

        return higherDensityDrop >= 0;
    }

    private bool HasDenseNeighbors(int drop, int excludedDrop, byte assignment, float radius)
    {
        const int AllSectors = (1 << 8) - 1;
        var supportRadius = radius * 3f;
        var supportRadiusSquared = supportRadius * supportRadius;
        var cellRadius = Mathf.CeilToInt(supportRadius / cellSize) + 1;
        var center = positions[drop];
        var centerCell = GetCell(center);
        var centerCellX = centerCell % gridColumns;
        var centerCellY = centerCell / gridColumns;
        var sectorMask = 0;
        var neighborCount = 0;
        for (var cellY = Mathf.Max(0, centerCellY - cellRadius);
             cellY <= Mathf.Min(gridRows - 1, centerCellY + cellRadius);
             cellY++)
        {
            for (var cellX = Mathf.Max(0, centerCellX - cellRadius);
                 cellX <= Mathf.Min(gridColumns - 1, centerCellX + cellRadius);
                 cellX++)
            {
                var cell = cellY * gridColumns + cellX;
                var cellEnd = cellOffsets[cell] + cellActiveDropCounts[cell];
                for (var neighbor = cellOffsets[cell]; neighbor < cellEnd; neighbor++)
                {
                    if (neighbor == drop
                        || neighbor == excludedDrop
                        || containerAssignments[neighbor] != assignment)
                    {
                        continue;
                    }

                    var difference = positions[neighbor] - center;
                    if (difference.sqrMagnitude <= 0.0001f
                        || difference.sqrMagnitude > supportRadiusSquared
                        || !containers[assignment - 1].Contains(positions[neighbor]))
                    {
                        continue;
                    }

                    sectorMask |= 1 << GetDensitySwapSector(difference.x, difference.y);
                    neighborCount++;
                    if (sectorMask == AllSectors && neighborCount >= 8)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsDensitySwapPathClear(
        int containerIndex,
        Vector2 start,
        Vector2 end,
        float radius)
    {
        var container = containers[containerIndex];
        for (var sample = 1; sample <= 7; sample++)
        {
            var point = Vector2.Lerp(start, end, sample * 0.125f);
            var classification = container.Classify(point);
            if (!classification.IsInside || classification.DistanceSquared < radius * radius)
            {
                return false;
            }
        }

        return true;
    }

    private float GetDensityForDrop(int drop)
    {
        var weightedDensity = 0f;
        var totalAmount = 0f;
        for (var unit = dropUnitHeads[drop]; unit >= 0; unit = unitNext[unit])
        {
            for (var component = unitComponentHeads[unit];
                component >= 0;
                component = componentNext[component])
            {
                var amount = componentAmounts[component];
                weightedDensity += liquidTypeDensities[componentTypeIndices[component]] * amount;
                totalAmount += amount;
            }
        }

        return totalAmount > 0f
            ? weightedDensity / totalAmount
            : LiquidDensityCatalog.DefaultDensity;
    }

    private static int GetDensitySwapSector(float x, float y)
    {
        var absoluteX = Mathf.Abs(x);
        var absoluteY = Mathf.Abs(y);
        if (absoluteY > absoluteX * 2f)
        {
            return y > 0f ? 0 : 4;
        }

        if (absoluteX > absoluteY * 2f)
        {
            return x > 0f ? 2 : 6;
        }

        if (x > 0f)
        {
            return y > 0f ? 1 : 3;
        }

        return y > 0f ? 7 : 5;
    }

    private void ResolveDropCollisions()
    {
        for (var iteration = 0; iteration < dropCollisionPasses; iteration++)
        {
            var profilingStart = SimulationProfiler.Begin();
            BuildSpatialGrid();
            SimulationProfiler.End(SimulationProfileStage.SpatialGrid, profilingStart);

            profilingStart = SimulationProfiler.Begin();
            ResolveDropCollisionPass();
            SimulationProfiler.End(SimulationProfileStage.DropCollisions, profilingStart);

            if (iteration == 0 || iteration == dropCollisionPasses - 1)
            {
                profilingStart = SimulationProfiler.Begin();
                EnforceContainerAssignments();
                if (iteration == dropCollisionPasses - 1)
                {
                    NormalizeDropSizes();
                }
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
        for (var activeCellIndex = 0; activeCellIndex < activeCellCount; activeCellIndex++)
        {
            var cell = activeCells[activeCellIndex];
            var cellX = cell % gridColumns;
            var cellY = cell / gridColumns;
            if (cellActiveDropCounts[cell] > 0 && cellCounts[cell] > 1)
            {
                ResolveCellPair(cell, cell, velocityOnly);
            }

            if (cellX + 1 < gridColumns
                && cellCounts[cell + 1] > 0
                && cellActiveDropCounts[cell] > 0
                && cellActiveDropCounts[cell + 1] > 0)
            {
                ResolveCellPair(cell, cell + 1, velocityOnly);
            }

            if (cellY + 1 >= gridRows)
            {
                continue;
            }

            var downLeftCell = cell + gridColumns - 1;
            if (cellX > 0
                && cellCounts[downLeftCell] > 0
                && cellActiveDropCounts[cell] > 0
                && cellActiveDropCounts[downLeftCell] > 0)
            {
                ResolveCellPair(
                    cell,
                    downLeftCell,
                    velocityOnly);
            }

            var belowCell = cell + gridColumns;
            if (cellCounts[belowCell] > 0
                && cellActiveDropCounts[cell] > 0
                && cellActiveDropCounts[belowCell] > 0)
            {
                ResolveCellPair(cell, belowCell, velocityOnly);
            }

            var downRightCell = cell + gridColumns + 1;
            if (cellX + 1 < gridColumns
                && cellCounts[downRightCell] > 0
                && cellActiveDropCounts[cell] > 0
                && cellActiveDropCounts[downRightCell] > 0)
            {
                ResolveCellPair(
                    cell,
                    downRightCell,
                    velocityOnly);
            }
        }

        ResolveLargeDropPairs(velocityOnly);
    }

    private void ResolveLargeDropPairs(bool velocityOnly)
    {
        var maximumDropRadius = dropRadius;
        for (var i = 0; i < count; i++)
        {
            if (!fastOutsideDrops[i])
            {
                maximumDropRadius = Mathf.Max(maximumDropRadius, GetDropRadius(i));
            }
        }

        for (var first = 0; first < count; first++)
        {
            if (fastOutsideDrops[first]
                || densitySwapProgress[first] >= 0f
                || dropVolumes[first] <= 1)
            {
                continue;
            }

            var cellRadius = Mathf.CeilToInt(
                (GetDropRadius(first) + maximumDropRadius
                    + (velocityOnly ? velocityContactMargin : 0f)) / cellSize);
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
                    if (cellActiveDropCounts[cell] == 0)
                    {
                        continue;
                    }

                    for (var secondOffset = cellOffsets[cell];
                         secondOffset < cellOffsets[cell] + cellActiveDropCounts[cell];
                         secondOffset++)
                    {
                        var second = secondOffset;
                        if (fastOutsideDrops[second]
                            || second == first
                            || densitySwapProgress[second] >= 0f
                            || (dropVolumes[second] > 1 && second < first))
                        {
                            continue;
                        }

                        if (velocityOnly)
                        {
                            ResolveVariableSizePairVelocity(first, second);
                        }
                        else
                        {
                            ResolveVariableSizePair(first, second);
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
                 firstIndex < cellOffsets[firstCell] + cellActiveDropCounts[firstCell];
                 firstIndex++)
            {
                var first = firstIndex;
                for (var secondIndex = firstIndex + 1;
                     secondIndex < cellOffsets[firstCell] + cellActiveDropCounts[firstCell];
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
             firstIndex < cellOffsets[firstCell] + cellActiveDropCounts[firstCell];
             firstIndex++)
        {
            var first = firstIndex;
            for (var secondIndex = cellOffsets[secondCell];
                 secondIndex < cellOffsets[secondCell] + cellActiveDropCounts[secondCell];
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
        if (fastOutsideDrops[first]
            || fastOutsideDrops[second]
            || (activeDensitySwapCount > 0
                && (densitySwapProgress[first] >= 0f || densitySwapProgress[second] >= 0f))
            || dropVolumes[first] > 1
            || dropVolumes[second] > 1)
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
        var firstPosition = positions[first];
        var secondPosition = positions[second];
        var differenceX = secondPosition.x - firstPosition.x;
        var differenceY = secondPosition.y - firstPosition.y;
        if (Mathf.Abs(differenceX) >= dropCollisionDistance
            || Mathf.Abs(differenceY) >= dropCollisionDistance)
        {
            return;
        }

        var distanceSquared = differenceX * differenceX + differenceY * differenceY;
        if (distanceSquared >= dropCollisionDistanceSquared)
        {
            return;
        }

        var distance = Mathf.Sqrt(distanceSquared);
        var difference = new Vector2(differenceX, differenceY);
        var normal = distance > 0.0001f
            ? difference / distance
            : new Vector2(1f, 0f);
        var correction = (dropCollisionDistance - distance)
            * collisionCorrectionStrength
            * 0.5f;
        positions[first] = firstPosition - normal * correction;
        positions[second] = secondPosition + normal * correction;
    }

    private void ResolvePairVelocity(int first, int second)
    {
        var firstPosition = positions[first];
        var secondPosition = positions[second];
        var differenceX = secondPosition.x - firstPosition.x;
        var differenceY = secondPosition.y - firstPosition.y;
        var distanceSquared = differenceX * differenceX + differenceY * differenceY;
        if (distanceSquared > velocityContactDistanceSquared)
        {
            return;
        }

        var distance = Mathf.Sqrt(distanceSquared);
        var difference = new Vector2(differenceX, differenceY);
        var normal = distance > 0.0001f
            ? difference / distance
            : new Vector2(1f, 0f);
        var relativeVelocity = velocities[second] - velocities[first];
        var separatingSpeed = Vector2.Dot(relativeVelocity, normal);
        if (separatingSpeed >= 0f)
        {
            return;
        }

        var impulse = -separatingSpeed * 0.5f;
        velocities[first] -= normal * impulse;
        velocities[second] += normal * impulse;
    }

    private void ResolveVariableSizePair(int first, int second)
    {
        var difference = positions[second] - positions[first];
        var distanceSquared = difference.sqrMagnitude;
        var minimumDistance = GetDropRadius(first) + GetDropRadius(second);
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        if (distanceSquared >= minimumDistanceSquared)
        {
            return;
        }

        var distance = Mathf.Sqrt(distanceSquared);
        var normal = distance > 0.0001f
            ? difference / distance
            : new Vector2(1f, 0f);
        var correction = (minimumDistance - distance) * collisionCorrectionStrength;
        var firstVolume = dropVolumes[first];
        var secondVolume = dropVolumes[second];
        var totalVolume = firstVolume + secondVolume;
        positions[first] -= normal * (correction * secondVolume / totalVolume);
        positions[second] += normal * (correction * firstVolume / totalVolume);
    }

    private void ResolveVariableSizePairVelocity(int first, int second)
    {
        var difference = positions[second] - positions[first];
        var distanceSquared = difference.sqrMagnitude;
        var minimumDistance =
            GetDropRadius(first) + GetDropRadius(second) + velocityContactMargin;
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
        densityComputePixelCount = densityTextureWidth * densityTextureHeight;
        densityComputeShader = LiquidShaderAssets.DensityComputeShader;
        useComputeRendering = densityComputeShader
            && SystemInfo.supportsComputeShaders
            && SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGB32);
        if (densityComputeShader && !useComputeRendering)
        {
            Debug.LogWarning(
                "[RealisticLiquidTransfer] This device cannot run the liquid compute renderer; "
                + "the CPU liquid renderer will be used.");
        }

        if (useComputeRendering)
        {
            densityRenderTexture = new RenderTexture(
                densityTextureWidth,
                densityTextureHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear)
            {
                name = "LiquidDensity",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                enableRandomWrite = true,
                useMipMap = false,
                autoGenerateMips = false
            };
            if (!densityRenderTexture.Create())
            {
                Debug.LogWarning(
                    "[RealisticLiquidTransfer] Could not create the compute density texture; "
                    + "falling back to the CPU liquid renderer.");
                UnityEngine.Object.Destroy(densityRenderTexture);
                densityRenderTexture = null;
                useComputeRendering = false;
            }
            if (useComputeRendering)
            {
                densityColorRenderTexture = new RenderTexture(
                    densityTextureWidth,
                    densityTextureHeight,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear)
                {
                    name = "LiquidColor",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    enableRandomWrite = true,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!densityColorRenderTexture.Create())
                {
                    Debug.LogWarning(
                        "[RealisticLiquidTransfer] Could not create the compute liquid color texture; "
                        + "falling back to the CPU liquid renderer.");
                    UnityEngine.Object.Destroy(densityRenderTexture);
                    densityRenderTexture = null;
                    UnityEngine.Object.Destroy(densityColorRenderTexture);
                    densityColorRenderTexture = null;
                    useComputeRendering = false;
                }
            }
        }

        if (useComputeRendering)
        {
            InitializeComputeRendering();
        }
        else
        {
            InitializeCpuRendering();
        }

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
        surfaceGraphic.SetDensityTexture(useComputeRendering ? densityRenderTexture : densityTexture);
        surfaceGraphic.SetLiquidColorTexture(
            useComputeRendering ? densityColorRenderTexture : densityColorTexture);
        surfaceGraphic.SetDensityUploadTexture(densityUploadTexture);
        if (useComputeRendering)
        {
            surfaceGraphic.SetComputeBuffers(
                densityAccumulationBuffer,
                computeDropDataBuffer,
                computeDropRadiusBuffer,
                computeDropColorBuffer,
                computeDropSwapPathBuffer,
                colorAccumulationBuffer);
        }
        if (LiquidShaderAssets.SurfaceMaterial)
        {
            surfaceGraphic.SetSurfaceMaterial(new Material(LiquidShaderAssets.SurfaceMaterial));
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
            + $"'{(LiquidShaderAssets.SurfaceMaterial ? LiquidShaderAssets.SurfaceMaterial.name : "Unity UI default")}', "
            + $"compute rendering={(useComputeRendering ? "enabled" : "unavailable")}.");
    }

    private void InitializeCpuRendering()
    {
        densityPixels = new Color32[densityComputePixelCount];
        densityVelocityFields = new Vector3[densityPixels.Length];
        densityWeightedColors = new Vector4[densityPixels.Length];
        densityColorPixels = new Color32[densityPixels.Length];
        activeDensityPixelIndices = new int[densityPixels.Length];
        for (var i = 0; i < densityPixels.Length; i++)
        {
            densityPixels[i] = new Color32(0, 128, 128, 0);
            densityColorPixels[i] = new Color32(41, 128, 243, 255);
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
        supportsPartialTextureCopy = SystemInfo.copyTextureSupport != CopyTextureSupport.None;
        densityTexture.SetPixels32(densityPixels);
        densityTexture.Apply(false, false);

        densityColorTexture = new Texture2D(
            densityTextureWidth,
            densityTextureHeight,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "LiquidColor",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        densityColorTexture.SetPixels32(densityColorPixels);
        densityColorTexture.Apply(false, false);
    }

    private void InitializeComputeRendering()
    {
        clearAccumulationKernel = densityComputeShader.FindKernel("ClearAccumulation");
        rasterizeDropsKernel = densityComputeShader.FindKernel("RasterizeDrops");
        resolveDensityKernel = densityComputeShader.FindKernel("ResolveDensity");
        densityAccumulationBuffer = new ComputeBuffer(
            densityComputePixelCount,
            sizeof(int) * 4,
            ComputeBufferType.Structured);
        colorAccumulationBuffer = new ComputeBuffer(
            densityComputePixelCount,
            sizeof(int) * 3,
            ComputeBufferType.Structured);
        computeDropDataBuffer = new ComputeBuffer(MaximumDrops, sizeof(float) * 4, ComputeBufferType.Structured);
        computeDropRadiusBuffer = new ComputeBuffer(MaximumDrops, sizeof(float), ComputeBufferType.Structured);
        computeDropColorBuffer = new ComputeBuffer(MaximumDrops, sizeof(float) * 4, ComputeBufferType.Structured);
        computeDropSwapPathBuffer = new ComputeBuffer(
            MaximumDrops,
            sizeof(float) * 4,
            ComputeBufferType.Structured);
        computeDropData = new Vector4[MaximumDrops];
        computeDropRadii = new float[MaximumDrops];
        computeDropColors = new Vector4[MaximumDrops];

        densityComputeShader.SetBuffer(clearAccumulationKernel, "_Accumulation", densityAccumulationBuffer);
        densityComputeShader.SetBuffer(rasterizeDropsKernel, "_Accumulation", densityAccumulationBuffer);
        densityComputeShader.SetBuffer(clearAccumulationKernel, "_ColorAccumulation", colorAccumulationBuffer);
        densityComputeShader.SetBuffer(rasterizeDropsKernel, "_ColorAccumulation", colorAccumulationBuffer);
        densityComputeShader.SetBuffer(rasterizeDropsKernel, "_DropData", computeDropDataBuffer);
        densityComputeShader.SetBuffer(rasterizeDropsKernel, "_DropRadii", computeDropRadiusBuffer);
        densityComputeShader.SetBuffer(rasterizeDropsKernel, "_DropColors", computeDropColorBuffer);
        densityComputeShader.SetBuffer(rasterizeDropsKernel, "_DropSwapPaths", computeDropSwapPathBuffer);
        densityComputeShader.SetBuffer(resolveDensityKernel, "_Accumulation", densityAccumulationBuffer);
        densityComputeShader.SetBuffer(resolveDensityKernel, "_ColorAccumulation", colorAccumulationBuffer);
        densityComputeShader.SetTexture(resolveDensityKernel, "_DensityOutput", densityRenderTexture);
        densityComputeShader.SetTexture(resolveDensityKernel, "_ColorOutput", densityColorRenderTexture);
        densityComputeShader.SetInt("_TextureWidth", densityTextureWidth);
        densityComputeShader.SetInt("_TextureHeight", densityTextureHeight);
        densityComputeShader.SetInt("_PixelCount", densityComputePixelCount);
        densityComputeShader.SetVector(
            "_ScreenBounds",
            new Vector4(
                minimumScreenX,
                minimumScreenY,
                maximumScreenX - minimumScreenX,
                maximumScreenY - minimumScreenY));
        densityComputeShader.SetFloat(
            "_XScale",
            (densityTextureWidth - 1) / (maximumScreenX - minimumScreenX));
        densityComputeShader.SetFloat(
            "_YScale",
            (densityTextureHeight - 1) / (maximumScreenY - minimumScreenY));
        densityComputeShader.SetFloat("_DensityContribution", DensityContribution);
        densityComputeShader.SetFloat("_TailSpeedForMaximumLength", TailSpeedForMaximumLength);
    }

    private void UpdateGraphics()
    {
        if (!surfaceShaderEnabled)
        {
            UpdateIndividualDropGraphics();
            return;
        }

        if (useComputeRendering)
        {
            UpdateComputeGraphics();
            return;
        }

        var minimumX = densityTextureWidth;
        var minimumY = densityTextureHeight;
        var maximumX = -1;
        var maximumY = -1;
        for (var index = 0; index < activeDensityPixelCount; index++)
        {
            var pixelIndex = activeDensityPixelIndices[index];
            densityPixels[pixelIndex] = new Color32(0, 128, 128, 0);
            densityVelocityFields[pixelIndex] = Vector3.zero;
            densityWeightedColors[pixelIndex] = Vector4.zero;
            densityColorPixels[pixelIndex] = new Color32(41, 128, 243, 255);
            IncludeDensityPixel(pixelIndex, ref minimumX, ref minimumY, ref maximumX, ref maximumY);
        }
        activeDensityPixelCount = 0;

        var xScale = (densityTextureWidth - 1) / (maximumScreenX - minimumScreenX);
        var yScale = (densityTextureHeight - 1) / (maximumScreenY - minimumScreenY);
        var inverseXScale = 1f / xScale;
        var inverseYScale = 1f / yScale;
        for (var drop = 0; drop < count; drop++)
        {
            var position = positions[drop];
            var dropRadius = GetDropRadius(drop);
            var supportRadius = dropRadius * 2.2f;
            var supportRadiusSquared = supportRadius * supportRadius;
            var inverseSupportRadiusSquared = 1f / supportRadiusSquared;
            var velocity = velocities[drop];
            var speed = velocity.magnitude;
            var hasDirection = speed > 0.001f;
            var direction = hasDirection ? velocity / speed : Vector2.zero;
            var tailLength = hasDirection
                ? dropRadius * 2.5f * Mathf.Clamp01(speed / TailSpeedForMaximumLength)
                : 0f;
            var centerX = Mathf.RoundToInt((position.x - minimumScreenX) * xScale);
            var centerY = Mathf.RoundToInt((position.y - minimumScreenY) * yScale);
            var radiusX = (supportRadius + tailLength) * xScale;
            var radiusY = (supportRadius + tailLength) * yScale;
            var majorRadius = supportRadius + tailLength * 0.5f;
            var inverseMajorRadiusSquared = 1f / (majorRadius * majorRadius);
            var firstX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusX));
            var lastX = Mathf.Min(densityTextureWidth - 1, Mathf.CeilToInt(centerX + radiusX));
            var firstY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusY));
            var lastY = Mathf.Min(densityTextureHeight - 1, Mathf.CeilToInt(centerY + radiusY));
            for (var y = firstY; y <= lastY; y++)
            {
                var dy = (y - centerY) * inverseYScale;
                var row = y * densityTextureWidth;
                for (var x = firstX; x <= lastX; x++)
                {
                    var dx = (x - centerX) * inverseXScale;
                    var along = dx * direction.x + dy * direction.y + tailLength * 0.5f;
                    var across = -dx * direction.y + dy * direction.x;
                    var normalizedDistanceSquared = hasDirection
                        ? along * along * inverseMajorRadiusSquared
                            + across * across * inverseSupportRadiusSquared
                        : (dx * dx + dy * dy) * inverseSupportRadiusSquared;
                    if (normalizedDistanceSquared >= 1f)
                    {
                        continue;
                    }

                    var baseRemaining = 1f - normalizedDistanceSquared;
                    var contribution = DensityContribution * baseRemaining * baseRemaining;

                    var pixelIndex = row + x;
                    var velocityField = densityVelocityFields[pixelIndex];
                    if (velocityField.x <= 0f)
                    {
                        activeDensityPixelIndices[activeDensityPixelCount++] = pixelIndex;
                        IncludeDensityPixel(pixelIndex, ref minimumX, ref minimumY, ref maximumX, ref maximumY);
                    }

                    var density = Mathf.Min(
                        byte.MaxValue,
                        densityPixels[pixelIndex].r + Mathf.RoundToInt(contribution * byte.MaxValue));
                    densityPixels[pixelIndex].r = (byte)density;

                    velocityField.x += contribution;
                    velocityField.y += velocity.x * contribution;
                    velocityField.z += velocity.y * contribution;
                    densityVelocityFields[pixelIndex] = velocityField;
                    var weightedColor = densityWeightedColors[pixelIndex];
                    weightedColor.x += dropColors[drop].x * contribution;
                    weightedColor.y += dropColors[drop].y * contribution;
                    weightedColor.z += dropColors[drop].z * contribution;
                    weightedColor.w += contribution;
                    densityWeightedColors[pixelIndex] = weightedColor;
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
            var weightedColor = densityWeightedColors[i];
            if (weightedColor.w > 0.0001f)
            {
                densityColorPixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(weightedColor.x / weightedColor.w) * byte.MaxValue),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(weightedColor.y / weightedColor.w) * byte.MaxValue),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(weightedColor.z / weightedColor.w) * byte.MaxValue),
                    byte.MaxValue);
            }
        }

        if (maximumX >= minimumX && maximumY >= minimumY)
        {
            UploadDensityRegion(minimumX, minimumY, maximumX, maximumY);
            UploadLiquidColorRegion(minimumX, minimumY, maximumX, maximumY);
        }
    }

    private void UpdateComputeGraphics()
    {
        for (var i = 0; i < count; i++)
        {
            var position = positions[i];
            var velocity = velocities[i];
            computeDropData[i] = new Vector4(position.x, position.y, velocity.x, velocity.y);
            computeDropRadii[i] = GetDropRadius(i);
            computeDropColors[i] = dropColors[i];
            if (densitySwapProgress[i] >= 0f)
            {
                computeDropColors[i].w = densitySwapProgress[i];
                computeDropSwapPathBuffer.SetData(densitySwapPaths, i, i, 1);
            }
            else
            {
                computeDropColors[i].w = 1f;
            }
        }

        if (count > 0)
        {
            computeDropDataBuffer.SetData(computeDropData, 0, 0, count);
            computeDropRadiusBuffer.SetData(computeDropRadii, 0, 0, count);
            computeDropColorBuffer.SetData(computeDropColors, 0, 0, count);
        }

        densityComputeShader.SetInt("_DropCount", count);
        densityComputeShader.Dispatch(clearAccumulationKernel, (densityComputePixelCount + 255) / 256, 1, 1);
        if (count > 0)
        {
            densityComputeShader.Dispatch(rasterizeDropsKernel, count, 1, 1);
        }

        densityComputeShader.Dispatch(
            resolveDensityKernel,
            (densityTextureWidth + 7) / 8,
            (densityTextureHeight + 7) / 8,
            1);
    }

    private void IncludeDensityPixel(
        int pixelIndex,
        ref int minimumX,
        ref int minimumY,
        ref int maximumX,
        ref int maximumY)
    {
        var x = pixelIndex % densityTextureWidth;
        var y = pixelIndex / densityTextureWidth;
        minimumX = Mathf.Min(minimumX, x);
        minimumY = Mathf.Min(minimumY, y);
        maximumX = Mathf.Max(maximumX, x);
        maximumY = Mathf.Max(maximumY, y);
    }

    private void UploadDensityRegion(int minimumX, int minimumY, int maximumX, int maximumY)
    {
        var width = maximumX - minimumX + 1;
        var height = maximumY - minimumY + 1;
        var textureArea = densityTextureWidth * densityTextureHeight;
        if (!supportsPartialTextureCopy || width * height >= textureArea)
        {
            densityTexture.SetPixels32(densityPixels);
            densityTexture.Apply(false, false);
            return;
        }

        EnsureDensityUploadTexture(width, height);
        if (densityUploadPixels.Length >= textureArea)
        {
            densityTexture.SetPixels32(densityPixels);
            densityTexture.Apply(false, false);
            return;
        }

        for (var y = 0; y < height; y++)
        {
            Array.Copy(
                densityPixels,
                (minimumY + y) * densityTextureWidth + minimumX,
                densityUploadPixels,
                y * densityUploadTextureWidth,
                width);
        }

        densityUploadTexture.SetPixels32(densityUploadPixels);
        densityUploadTexture.Apply(false, false);
        Graphics.CopyTexture(
            densityUploadTexture,
            0,
            0,
            0,
            0,
            width,
            height,
            densityTexture,
            0,
            0,
            minimumX,
            minimumY);
    }

    private void UploadLiquidColorRegion(
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY)
    {
        var width = maximumX - minimumX + 1;
        var height = maximumY - minimumY + 1;
        var textureArea = densityTextureWidth * densityTextureHeight;
        if (!supportsPartialTextureCopy || width * height >= textureArea)
        {
            densityColorTexture.SetPixels32(densityColorPixels);
            densityColorTexture.Apply(false, false);
            return;
        }

        EnsureDensityUploadTexture(width, height);
        if (densityUploadPixels.Length >= textureArea)
        {
            densityColorTexture.SetPixels32(densityColorPixels);
            densityColorTexture.Apply(false, false);
            return;
        }

        for (var y = 0; y < height; y++)
        {
            Array.Copy(
                densityColorPixels,
                (minimumY + y) * densityTextureWidth + minimumX,
                densityUploadPixels,
                y * densityUploadTextureWidth,
                width);
        }

        densityUploadTexture.SetPixels32(densityUploadPixels);
        densityUploadTexture.Apply(false, false);
        Graphics.CopyTexture(
            densityUploadTexture,
            0,
            0,
            0,
            0,
            width,
            height,
            densityColorTexture,
            0,
            0,
            minimumX,
            minimumY);
    }

    private void EnsureDensityUploadTexture(int width, int height)
    {
        if (densityUploadTextureWidth >= width && densityUploadTextureHeight >= height)
        {
            return;
        }

        densityUploadTextureWidth = Mathf.Max(densityUploadTextureWidth, width);
        densityUploadTextureHeight = Mathf.Max(densityUploadTextureHeight, height);
        densityUploadPixels = new Color32[densityUploadTextureWidth * densityUploadTextureHeight];
        if (densityUploadTexture)
        {
            UnityEngine.Object.Destroy(densityUploadTexture);
        }

        densityUploadTexture = new Texture2D(
            densityUploadTextureWidth,
            densityUploadTextureHeight,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = "LiquidDensityUpload",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        surfaceGraphic.SetDensityUploadTexture(densityUploadTexture);
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
                dropColors,
                start,
                length,
                dropRadius * 0.88f,
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
     private readonly BoundaryNode[] boundaryNodes;
     private readonly int[] boundaryOrder;
     private readonly int[] boundaryQueryStack;
     private readonly float interiorOrientation;
     private readonly Vector2 interiorFallback;
     private readonly Vector2 distanceFieldMinimum;
     private readonly int distanceFieldColumns;
     private readonly int distanceFieldRows;
     private readonly float[] signedDistanceField;
     private readonly Vector2[] distanceFieldGradients;
     private float maximumDropRadius = DefaultDropRadius * 6.3245554f;
     private Matrix4x4 rootToContainerMatrix;
     private Rect paddedInteriorRectangle;
     private float paddedInteriorRectanglePadding = -1f;
     private Vector2 openingStart;
     private Vector2 openingEnd;
     private Vector2 openingSegment;
     private float openingLengthSquared;
     private readonly float splitSizeZoneDepth;
     private readonly float mergeSizeZoneDepth;
     private Vector2 sizeZoneStart;
     private Vector2 sizeZoneEnd;
     private Vector2 sizeZoneSegment;
     private float sizeZoneLengthSquared;
     private Vector2 mergeSizeZoneStart;
     private Vector2 mergeSizeZoneEnd;
     private Vector2 mergeSizeZoneSegment;
     private float mergeSizeZoneLengthSquared;
     private float rootUnitsPerLocalUnit = 1f;
     private int boundaryNodeCount;

     internal ContainerShape(RectTransform root, RectTransform transform, Vector2[] outline)
     {
         this.root = root;
         this.transform = transform;
         Outline = outline ?? Array.Empty<Vector2>();
         var minimumLocalY = Outline.Length > 0 ? Outline[0].y : 0f;
         var maximumLocalY = minimumLocalY;
         foreach (var point in Outline)
         {
             minimumLocalY = Mathf.Min(minimumLocalY, point.y);
             maximumLocalY = Mathf.Max(maximumLocalY, point.y);
         }
         splitSizeZoneDepth = (maximumLocalY - minimumLocalY) * 0.05f;
         mergeSizeZoneDepth = (maximumLocalY - minimumLocalY) * 0.07f;

         boundaries = new BoundarySegment[Mathf.Max(0, Outline.Length - 1)];
         boundaryNodes = new BoundaryNode[Mathf.Max(0, boundaries.Length * 2 - 1)];
         boundaryOrder = new int[boundaries.Length];
         boundaryQueryStack = new int[boundaryNodes.Length];

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
         BuildBoundaryTree();
     }

     internal Vector2[] Outline { get; }
     internal RectTransform Transform => transform;
     internal Vector2 Minimum { get; private set; }
     internal Vector2 Maximum { get; private set; }
     internal Vector2 Center { get; private set; }

     internal Vector2 ToRootPoint(Vector2 localPoint)
     {
         return root.InverseTransformPoint(transform.TransformPoint(localPoint));
     }

     internal void SetMaximumDropRadius(float radius)
     {
         maximumDropRadius = radius;
     }

     internal void RefreshBoundaries()
     {
         rootToContainerMatrix = transform.worldToLocalMatrix * root.localToWorldMatrix;
         Center = ToRootPoint(Vector2.zero);
         if (Outline.Length == 0)
         {
             return;
         }

         var rootOrigin = ToRootPoint(Vector2.zero);
         var rootUnitsX = (ToRootPoint(Vector2.right) - rootOrigin).magnitude;
         var rootUnitsY = (ToRootPoint(Vector2.up) - rootOrigin).magnitude;
         rootUnitsPerLocalUnit = Mathf.Max(Mathf.Min(rootUnitsX, rootUnitsY), 0.0001f);
         var rectanglePadding = (maximumDropRadius + 0.01f) / rootUnitsPerLocalUnit;
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
         sizeZoneStart = ToRootPoint(Outline[0] + Vector2.down * splitSizeZoneDepth);
         sizeZoneEnd = ToRootPoint(Outline[Outline.Length - 1] + Vector2.down * splitSizeZoneDepth);
         sizeZoneSegment = sizeZoneEnd - sizeZoneStart;
         sizeZoneLengthSquared = sizeZoneSegment.sqrMagnitude;
         mergeSizeZoneStart = ToRootPoint(Outline[0] + Vector2.down * mergeSizeZoneDepth);
         mergeSizeZoneEnd = ToRootPoint(Outline[Outline.Length - 1] + Vector2.down * mergeSizeZoneDepth);
         mergeSizeZoneSegment = mergeSizeZoneEnd - mergeSizeZoneStart;
         mergeSizeZoneLengthSquared = mergeSizeZoneSegment.sqrMagnitude;
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
         if (boundaryNodeCount > 0)
         {
             RefitBoundaryTree();
         }
     }

     internal bool SweptBoundsOverlap(Vector2 start, Vector2 end, float padding)
     {
         var pathMinimum = Vector2.Min(start, end);
         var pathMaximum = Vector2.Max(start, end);
         return pathMinimum.x <= Maximum.x + padding
             && pathMaximum.x >= Minimum.x - padding
             && pathMinimum.y <= Maximum.y + padding
             && pathMaximum.y >= Minimum.y - padding;
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

         var startSide = Cross(openingSegment, start - openingStart);
         var endSide = Cross(openingSegment, end - openingStart);
         return startSide <= 0f && endSide > 0f
             || startSide >= 0f && endSide < 0f;
     }

     internal bool IsNearOpening(Vector2 point, float margin)
     {
         if (Outline.Length < 2 || openingLengthSquared <= Mathf.Epsilon)
         {
             return false;
         }

         var amount = Mathf.Clamp01(
             Vector2.Dot(point - openingStart, openingSegment) / openingLengthSquared);
         var nearestPoint = openingStart + openingSegment * amount;
         return (point - nearestPoint).sqrMagnitude <= margin * margin;
     }

     internal bool IsPastSizeZone(Vector2 point)
     {
         return sizeZoneLengthSquared > Mathf.Epsilon
             && Cross(sizeZoneSegment, point - sizeZoneStart) * -interiorOrientation >= 0f;
     }

     internal bool IsPastMergeSizeZone(Vector2 point)
     {
         return mergeSizeZoneLengthSquared > Mathf.Epsilon
             && Cross(mergeSizeZoneSegment, point - mergeSizeZoneStart) * -interiorOrientation >= 0f;
     }

     internal bool CrossesSizeZone(Vector2 start, Vector2 end, out bool entering)
     {
         entering = false;
         if (sizeZoneLengthSquared <= Mathf.Epsilon)
         {
             return false;
         }

         var movement = end - start;
         var denominator = Cross(movement, sizeZoneSegment);
         if (Mathf.Abs(denominator) <= Mathf.Epsilon)
         {
             return false;
         }

         var offset = sizeZoneStart - start;
         var movementAmount = Cross(offset, sizeZoneSegment) / denominator;
         var zoneAmount = Cross(offset, movement) / denominator;
         if (movementAmount < 0f || movementAmount > 1f || zoneAmount < 0f || zoneAmount > 1f)
         {
             return false;
         }

         var startSide = Cross(sizeZoneSegment, start - sizeZoneStart) * -interiorOrientation;
         var endSide = Cross(sizeZoneSegment, end - sizeZoneStart) * -interiorOrientation;
         entering = endSide > startSide;
         return true;
     }

     internal bool CrossesMergeSizeZone(Vector2 start, Vector2 end, out bool entering)
     {
         entering = false;
         if (mergeSizeZoneLengthSquared <= Mathf.Epsilon)
         {
             return false;
         }

         var movement = end - start;
         var denominator = Cross(movement, mergeSizeZoneSegment);
         if (Mathf.Abs(denominator) <= Mathf.Epsilon)
         {
             return false;
         }

         var offset = mergeSizeZoneStart - start;
         var movementAmount = Cross(offset, mergeSizeZoneSegment) / denominator;
         var zoneAmount = Cross(offset, movement) / denominator;
         if (movementAmount < 0f || movementAmount > 1f || zoneAmount < 0f || zoneAmount > 1f)
         {
             return false;
         }

         var startSide = Cross(mergeSizeZoneSegment, start - mergeSizeZoneStart) * -interiorOrientation;
         var endSide = Cross(mergeSizeZoneSegment, end - mergeSizeZoneStart) * -interiorOrientation;
         entering = endSide > startSide;
         return true;
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
         if (distance < maximumDropRadius + DistanceFieldCellSize * rootUnitsPerLocalUnit)
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
         if (boundaryNodeCount > 0)
         {
             var stackCount = 0;
             boundaryQueryStack[stackCount++] = 0;
             while (stackCount > 0)
             {
                 var nodeIndex = boundaryQueryStack[--stackCount];
                 var node = boundaryNodes[nodeIndex];
                 if (IsOutsideNearestRange(point, node, nearestDistanceSquared))
                 {
                     continue;
                 }

                 if (node.BoundaryIndex >= 0)
                 {
                     var boundaryIndex = node.BoundaryIndex;
                     var boundary = boundaries[boundaryIndex];
                     var lengthSquared = boundary.LengthSquared;
                     if (lengthSquared <= Mathf.Epsilon)
                     {
                         continue;
                     }

                     var along = Mathf.Clamp01(
                         Vector2.Dot(point - boundary.Start, boundary.Segment) / lengthSquared);
                     var candidate = boundary.Start + boundary.Segment * along;
                     var distanceSquared = (point - candidate).sqrMagnitude;
                     if (distanceSquared < nearestDistanceSquared
                         || (distanceSquared == nearestDistanceSquared
                             && boundaryIndex < nearestBoundaryIndex))
                     {
                         nearestDistanceSquared = distanceSquared;
                         nearestPoint = candidate;
                         nearestNormal = boundary.InteriorNormal;
                         nearestBoundaryIndex = boundaryIndex;
                     }
                     continue;
                 }

                 var left = node.LeftChild;
                 var right = node.RightChild;
                 var leftDistanceSquared = GetBoundaryBoundsDistanceSquared(point, boundaryNodes[left]);
                 var rightDistanceSquared = GetBoundaryBoundsDistanceSquared(point, boundaryNodes[right]);
                 if (leftDistanceSquared <= rightDistanceSquared)
                 {
                     boundaryQueryStack[stackCount++] = right;
                     boundaryQueryStack[stackCount++] = left;
                 }
                 else
                 {
                     boundaryQueryStack[stackCount++] = left;
                     boundaryQueryStack[stackCount++] = right;
                 }
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
                 var segment = boundary.Segment;
                 var lengthSquared = boundary.LengthSquared;
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

     private void BuildBoundaryTree()
     {
         if (boundaries.Length == 0)
         {
             return;
         }

         for (var i = 0; i < boundaryOrder.Length; i++)
         {
             boundaryOrder[i] = i;
         }

         boundaryNodeCount = 0;
         BuildBoundaryTreeNode(0, boundaryOrder.Length);
         RefitBoundaryTree();
     }

     private int BuildBoundaryTreeNode(int first, int count)
     {
         var nodeIndex = boundaryNodeCount++;
         if (count == 1)
         {
             boundaryNodes[nodeIndex] = new BoundaryNode(-1, -1, boundaryOrder[first]);
             return nodeIndex;
         }

         var firstCenter = boundaries[boundaryOrder[first]].Center;
         var minimumCenter = firstCenter;
         var maximumCenter = firstCenter;
         for (var i = first + 1; i < first + count; i++)
         {
             var center = boundaries[boundaryOrder[i]].Center;
             minimumCenter = Vector2.Min(minimumCenter, center);
             maximumCenter = Vector2.Max(maximumCenter, center);
         }

         var sortByX = maximumCenter.x - minimumCenter.x >= maximumCenter.y - minimumCenter.y;
         SortBoundaryOrder(first, count, sortByX);
         var leftCount = count / 2;
         var left = BuildBoundaryTreeNode(first, leftCount);
         var right = BuildBoundaryTreeNode(first + leftCount, count - leftCount);
         boundaryNodes[nodeIndex] = new BoundaryNode(left, right, -1);
         return nodeIndex;
     }

     private void SortBoundaryOrder(int first, int count, bool sortByX)
     {
         var end = first + count;
         for (var i = first + 1; i < end; i++)
         {
             var boundaryIndex = boundaryOrder[i];
             var center = boundaries[boundaryIndex].Center;
             var coordinate = sortByX ? center.x : center.y;
             var insertAt = i;
             while (insertAt > first)
             {
                 var previousIndex = boundaryOrder[insertAt - 1];
                 var previousCenter = boundaries[previousIndex].Center;
                 var previousCoordinate = sortByX ? previousCenter.x : previousCenter.y;
                 if (previousCoordinate < coordinate
                     || (previousCoordinate == coordinate && previousIndex < boundaryIndex))
                 {
                     break;
                 }

                 boundaryOrder[insertAt] = previousIndex;
                 insertAt--;
             }

             boundaryOrder[insertAt] = boundaryIndex;
         }
     }

     private void RefitBoundaryTree()
     {
         for (var nodeIndex = boundaryNodeCount - 1; nodeIndex >= 0; nodeIndex--)
         {
             var node = boundaryNodes[nodeIndex];
             if (node.BoundaryIndex >= 0)
             {
                 var boundary = boundaries[node.BoundaryIndex];
                 node.Minimum = Vector2.Min(boundary.Start, boundary.End);
                 node.Maximum = Vector2.Max(boundary.Start, boundary.End);
             }
             else
             {
                 var left = boundaryNodes[node.LeftChild];
                 var right = boundaryNodes[node.RightChild];
                 node.Minimum = Vector2.Min(left.Minimum, right.Minimum);
                 node.Maximum = Vector2.Max(left.Maximum, right.Maximum);
             }

             boundaryNodes[nodeIndex] = node;
         }
     }

     private static bool IsOutsideNearestRange(Vector2 point, BoundaryNode node, float nearestDistanceSquared)
     {
         if (float.IsPositiveInfinity(nearestDistanceSquared))
         {
             return false;
         }

         var margin = Mathf.Max(
             0.001f,
             Mathf.Max(
                 Mathf.Max(Mathf.Abs(point.x), Mathf.Abs(point.y)),
                 Mathf.Max(
                     Mathf.Max(Mathf.Abs(node.Minimum.x), Mathf.Abs(node.Minimum.y)),
                     Mathf.Max(Mathf.Abs(node.Maximum.x), Mathf.Abs(node.Maximum.y))))
             * 0.000001f);
         var deltaX = point.x < node.Minimum.x
             ? Mathf.Max(0f, node.Minimum.x - point.x - margin)
             : point.x > node.Maximum.x
                 ? Mathf.Max(0f, point.x - node.Maximum.x - margin)
                 : 0f;
         var deltaY = point.y < node.Minimum.y
             ? Mathf.Max(0f, node.Minimum.y - point.y - margin)
             : point.y > node.Maximum.y
                 ? Mathf.Max(0f, point.y - node.Maximum.y - margin)
                 : 0f;
         return deltaX * deltaX + deltaY * deltaY > nearestDistanceSquared;
     }

     private static float GetBoundaryBoundsDistanceSquared(Vector2 point, BoundaryNode node)
     {
         var deltaX = point.x < node.Minimum.x
             ? node.Minimum.x - point.x
             : point.x > node.Maximum.x
                 ? point.x - node.Maximum.x
                 : 0f;
         var deltaY = point.y < node.Minimum.y
             ? node.Minimum.y - point.y
             : point.y > node.Maximum.y
                 ? point.y - node.Maximum.y
                 : 0f;
         return deltaX * deltaX + deltaY * deltaY;
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
         var outline = Outline;
         var inside = false;
         for (int i = 0, previous = outline.Length - 1; i < outline.Length; previous = i++)
         {
             var currentPoint = outline[i];
             var previousPoint = outline[previous];
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
         Segment = end - start;
         LengthSquared = Segment.sqrMagnitude;
         Center = (start + end) * 0.5f;
         InteriorNormal = new Vector2(-Segment.y, Segment.x).normalized * interiorOrientation;
     }

     internal readonly Vector2 Start;
     internal readonly Vector2 End;
     internal readonly Vector2 Segment;
     internal readonly Vector2 Center;
     internal readonly float LengthSquared;
     internal readonly Vector2 InteriorNormal;
    }

    private struct BoundaryNode
    {
     internal BoundaryNode(int leftChild, int rightChild, int boundaryIndex)
     {
         LeftChild = leftChild;
         RightChild = rightChild;
         BoundaryIndex = boundaryIndex;
         Minimum = Vector2.zero;
         Maximum = Vector2.zero;
     }

     internal int LeftChild;
     internal int RightChild;
     internal int BoundaryIndex;
     internal Vector2 Minimum;
     internal Vector2 Maximum;
    }
}

internal sealed class LiquidSurfaceGraphic : MaskableGraphic
{
    private Texture densityTexture;
    private Texture liquidColorTexture;
    private Texture2D densityUploadTexture;
    private RenderTexture densityRenderTexture;
    private ComputeBuffer densityAccumulationBuffer;
    private ComputeBuffer colorAccumulationBuffer;
    private ComputeBuffer computeDropDataBuffer;
    private ComputeBuffer computeDropRadiusBuffer;
    private ComputeBuffer computeDropColorBuffer;
    private ComputeBuffer computeDropSwapPathBuffer;
    private Material surfaceMaterial;

    public override Texture mainTexture => densityTexture ? densityTexture : s_WhiteTexture;

    internal void SetDensityTexture(Texture texture)
    {
        densityTexture = texture;
        densityRenderTexture = texture as RenderTexture;
        SetVerticesDirty();
    }

    internal void SetLiquidColorTexture(Texture texture)
    {
        liquidColorTexture = texture;
        if (surfaceMaterial)
        {
            surfaceMaterial.SetTexture("_LiquidColorTex", texture);
        }
    }

    internal void SetSurfaceMaterial(Material material)
    {
        surfaceMaterial = material;
        this.material = material;
        if (liquidColorTexture)
        {
            surfaceMaterial.SetTexture("_LiquidColorTex", liquidColorTexture);
        }
    }

    internal void SetDensityUploadTexture(Texture2D texture)
    {
        densityUploadTexture = texture;
    }

    internal void SetComputeBuffers(
        ComputeBuffer accumulation,
        ComputeBuffer dropData,
        ComputeBuffer dropRadii,
        ComputeBuffer dropColors,
        ComputeBuffer dropSwapPaths,
        ComputeBuffer colorAccumulation)
    {
        densityAccumulationBuffer = accumulation;
        computeDropDataBuffer = dropData;
        computeDropRadiusBuffer = dropRadii;
        computeDropColorBuffer = dropColors;
        computeDropSwapPathBuffer = dropSwapPaths;
        colorAccumulationBuffer = colorAccumulation;
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
        if (densityRenderTexture)
        {
            densityRenderTexture.Release();
            Destroy(densityRenderTexture);
            densityRenderTexture = null;
            densityTexture = null;
        }
        else if (densityTexture)
        {
            Destroy(densityTexture);
            densityTexture = null;
        }

        if (densityUploadTexture)
        {
            Destroy(densityUploadTexture);
            densityUploadTexture = null;
        }
        if (liquidColorTexture is RenderTexture colorRenderTexture)
        {
            colorRenderTexture.Release();
            Destroy(colorRenderTexture);
        }
        else if (liquidColorTexture)
        {
            Destroy(liquidColorTexture);
        }
        liquidColorTexture = null;

        densityAccumulationBuffer?.Release();
        densityAccumulationBuffer = null;
        colorAccumulationBuffer?.Release();
        colorAccumulationBuffer = null;
        computeDropDataBuffer?.Release();
        computeDropDataBuffer = null;
        computeDropRadiusBuffer?.Release();
        computeDropRadiusBuffer = null;
        computeDropColorBuffer?.Release();
        computeDropColorBuffer = null;
        computeDropSwapPathBuffer?.Release();
        computeDropSwapPathBuffer = null;
        if (surfaceMaterial)
        {
            Destroy(surfaceMaterial);
            surfaceMaterial = null;
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
    private Vector4[] colors;

    internal void SetDrops(
        Vector2[] dropPositions,
        byte[] dropVolumes,
        Vector4[] dropColors,
        int first,
        int length,
        float dropRadius,
        Vector2 offset)
    {
        positions = dropPositions;
        volumes = dropVolumes;
        colors = dropColors;
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
            var dropRadius = radius * Mathf.Sqrt(Mathf.Max(1, volumes[start + drop]));
            var dropColor = colors[start + drop];
            var centerIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, center, dropColor);
            for (var segment = 0; segment < UnitCircle.Length; segment++)
            {
                AddVertex(vertexHelper, center + UnitCircle[segment] * dropRadius, dropColor);
            }

            for (var segment = 0; segment < UnitCircle.Length; segment++)
            {
                var next = (segment + 1) % UnitCircle.Length;
                vertexHelper.AddTriangle(centerIndex, centerIndex + 1 + segment, centerIndex + 1 + next);
            }
        }
    }

    private void AddVertex(VertexHelper vertexHelper, Vector2 position, Vector4 dropColor)
    {
        var vertex = UIVertex.simpleVert;
        vertex.color = new Color(dropColor.x, dropColor.y, dropColor.z, dropColor.w) * color;
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
