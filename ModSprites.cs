using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class ModSprites
{
    private const string ResourcePrefix = "RealisticLiquidTransfer.Resources.";
    private const string DefaultContainerName = "default";
    private const string ReferenceSpriteName = "coffee";
    private const byte MinimumVisibleAlpha = 32;
    private static readonly Dictionary<string, Sprite> ContainerSprites =
        new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

    private static Sprite defaultContainer;

    internal static void Load()
    {
        Unload();
        try
        {
            foreach (var resourceName in typeof(ModSprites).Assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                    || !resourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var nameLength = resourceName.Length - ResourcePrefix.Length - ".png".Length;
                var spriteName = resourceName.Substring(ResourcePrefix.Length, nameLength);
                var sprite = LoadSprite(resourceName);
                ContainerSprites.Add(spriteName, sprite);
                if (string.Equals(spriteName, DefaultContainerName, StringComparison.OrdinalIgnoreCase))
                {
                    defaultContainer = sprite;
                }
            }

            if (!defaultContainer)
            {
                throw new InvalidDataException(
                    $"Required embedded fallback sprite '{ResourcePrefix}{DefaultContainerName}.png' was not found.");
            }

            if (!ContainerSprites.ContainsKey(ReferenceSpriteName))
            {
                throw new InvalidDataException(
                    $"Required embedded calibration sprite '{ResourcePrefix}{ReferenceSpriteName}.png' was not found.");
            }
        }
        catch
        {
            Unload();
            throw;
        }
    }

    internal static Sprite GetContainer(string originalSpriteName)
    {
        return GetContainer(originalSpriteName, float.NaN);
    }

    internal static Sprite GetContainer(string originalSpriteName, float capacity)
    {
        if (!string.IsNullOrWhiteSpace(originalSpriteName)
            && ContainerSprites.TryGetValue(originalSpriteName, out var matchingSprite))
        {
            return matchingSprite;
        }

        var baseSpriteName = RemoveTrailingStateNumber(originalSpriteName);
        if (!string.Equals(baseSpriteName, originalSpriteName, StringComparison.Ordinal)
            && ContainerSprites.TryGetValue(baseSpriteName, out matchingSprite))
        {
            return matchingSprite;
        }

        var capacitySpriteName = GetDefaultContainerNameForCapacity(capacity);
        if (ContainerSprites.TryGetValue(capacitySpriteName, out matchingSprite))
        {
            Plugin.LogWarning(
                $"No container sprite resource matched '{originalSpriteName ?? "null"}'; "
                + $"using {capacitySpriteName}.png for capacity {capacity:F2} ml.");
            return matchingSprite;
        }

        Plugin.LogWarning(
            $"No container sprite resource matched '{originalSpriteName ?? "null"}' "
            + $"or capacity default '{capacitySpriteName}.png'; using {DefaultContainerName}.png.");
        return defaultContainer;
    }

    private static string GetDefaultContainerNameForCapacity(float capacity)
    {
        if (float.IsNaN(capacity) || float.IsInfinity(capacity) || capacity < 0f || capacity > 999999f)
        {
            return DefaultContainerName;
        }

        if (capacity <= 150f)
        {
            return "default0-150";
        }

        if (capacity <= 350f)
        {
            return "default151-350";
        }

        if (capacity <= 750f)
        {
            return "default351-750";
        }

        if (capacity <= 2000f)
        {
            return "default751-2000";
        }

        if (capacity <= 5000f)
        {
            return "default2001-5000";
        }

        return "default5001-999999";
    }

    private static string RemoveTrailingStateNumber(string spriteName)
    {
        if (string.IsNullOrWhiteSpace(spriteName))
        {
            return spriteName;
        }

        var end = spriteName.Length;
        while (end > 0 && char.IsDigit(spriteName[end - 1]))
        {
            end--;
        }

        if (end == spriteName.Length)
        {
            return spriteName;
        }

        if (end > 0 && spriteName[end - 1] == ')')
        {
            end--;
            if (end > 0 && spriteName[end - 1] == '(')
            {
                end--;
            }
        }

        return spriteName.Substring(0, end).TrimEnd(' ', '_', '-');
    }

    internal static void Unload()
    {
        foreach (var sprite in ContainerSprites.Values)
        {
            DestroySprite(sprite);
        }

        ContainerSprites.Clear();
        defaultContainer = null;
    }

    internal static bool TryFindOpening(Sprite sprite, out Vector2 position, out float width)
    {
        position = Vector2.zero;
        width = 0f;
        if (!sprite || !sprite.texture)
        {
            return false;
        }

        var spriteWidth = Mathf.RoundToInt(sprite.rect.width);
        var spriteHeight = Mathf.RoundToInt(sprite.rect.height);
        var originX = Mathf.RoundToInt(sprite.rect.x);
        var originY = Mathf.RoundToInt(sprite.rect.y);
        var textureWidth = sprite.texture.width;
        var pixels = sprite.texture.GetPixels32();
        var runs = new List<AlphaRun>();
        var stableRuns = new List<AlphaRun>();
        var searchBottom = Mathf.Max(0, spriteHeight - Mathf.CeilToInt(spriteHeight * 0.4f));
        var minimumGap = Mathf.CeilToInt(spriteWidth * 0.1f);
        var tolerance = Mathf.Max(3f, spriteWidth * 0.06f);

        for (var y = spriteHeight - 1; y >= searchBottom; y--)
        {
            FindRuns(pixels, textureWidth, originX, originY + y, spriteWidth, runs);
            for (var leftIndex = 0; leftIndex < runs.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < runs.Count; rightIndex++)
                {
                    var left = runs[leftIndex];
                    var right = runs[rightIndex];
                    if (right.Start - left.End - 1 < minimumGap
                        || !IsStablePair(
                            pixels,
                            textureWidth,
                            originX,
                            originY,
                            spriteWidth,
                            y,
                            spriteHeight,
                            left,
                            right,
                            minimumGap,
                            tolerance,
                            stableRuns))
                    {
                        continue;
                    }

                    width = right.Start - left.End - 1;
                    position = new Vector2(
                        (left.End + right.Start) * 0.5f - sprite.pivot.x,
                        y + 0.5f - sprite.pivot.y);
                    return width > 0f;
                }
            }
        }

        return false;
    }

    internal static bool TryFindOutline(
        Sprite sprite,
        Vector2 openingPosition,
        float openingWidth,
        out Vector2[] points)
    {
        points = Array.Empty<Vector2>();
        if (!sprite || !sprite.texture)
        {
            return false;
        }

        var spriteWidth = Mathf.RoundToInt(sprite.rect.width);
        var spriteHeight = Mathf.RoundToInt(sprite.rect.height);
        var originX = Mathf.RoundToInt(sprite.rect.x);
        var originY = Mathf.RoundToInt(sprite.rect.y);
        var textureWidth = sprite.texture.width;
        var pixels = sprite.texture.GetPixels32();
        var opaque = new bool[spriteWidth * spriteHeight];
        for (var y = 0; y < spriteHeight; y++)
        {
            var rowStart = (originY + y) * textureWidth + originX;
            var maskRowStart = y * spriteWidth;
            for (var x = 0; x < spriteWidth; x++)
            {
                opaque[maskRowStart + x] = pixels[rowStart + x].a >= MinimumVisibleAlpha;
            }
        }

        var leftOpening = openingPosition - new Vector2(openingWidth * 0.5f, 0f);
        var rightOpening = openingPosition + new Vector2(openingWidth * 0.5f, 0f);
        var start = FindNearestOpaquePixel(
            opaque,
            spriteWidth,
            spriteHeight,
            leftOpening + sprite.pivot);
        var end = FindNearestOpaquePixel(
            opaque,
            spriteWidth,
            spriteHeight,
            rightOpening + sprite.pivot);
        if (start < 0
            || end < 0
            || !TryFindPixelPath(opaque, spriteWidth, spriteHeight, start, end, out var pixelPath))
        {
            return false;
        }

        var rawPoints = new List<Vector2>(pixelPath.Count + 2)
        {
            leftOpening
        };
        foreach (var pixelIndex in pixelPath)
        {
            rawPoints.Add(new Vector2(
                pixelIndex % spriteWidth + 0.5f - sprite.pivot.x,
                pixelIndex / spriteWidth + 0.5f - sprite.pivot.y));
        }

        rawPoints.Add(rightOpening);
        points = SimplifyPath(rawPoints, Mathf.Max(1f, spriteWidth * 0.012f));
        return points.Length >= 2;
    }

    internal static Vector2[][] FindInteriorWalls(
        Sprite sprite,
        Vector2 openingPosition,
        float openingWidth)
    {
        if (!sprite || !sprite.texture)
        {
            return Array.Empty<Vector2[]>();
        }

        var spriteWidth = Mathf.RoundToInt(sprite.rect.width);
        var spriteHeight = Mathf.RoundToInt(sprite.rect.height);
        var originX = Mathf.RoundToInt(sprite.rect.x);
        var originY = Mathf.RoundToInt(sprite.rect.y);
        var textureWidth = sprite.texture.width;
        var pixels = sprite.texture.GetPixels32();
        var opaque = new bool[spriteWidth * spriteHeight];
        for (var y = 0; y < spriteHeight; y++)
        {
            var rowStart = (originY + y) * textureWidth + originX;
            var maskRowStart = y * spriteWidth;
            for (var x = 0; x < spriteWidth; x++)
            {
                opaque[maskRowStart + x] = pixels[rowStart + x].a >= MinimumVisibleAlpha;
            }
        }

        var leftOpening = openingPosition - new Vector2(openingWidth * 0.5f, 0f);
        var mainWallPixel = FindNearestOpaquePixel(
            opaque,
            spriteWidth,
            spriteHeight,
            leftOpening + sprite.pivot);
        if (mainWallPixel < 0)
        {
            return Array.Empty<Vector2[]>();
        }

        var componentIds = new int[opaque.Length];
        var components = new List<List<int>>();
        var frontier = new List<int>();
        var componentId = 0;
        for (var start = 0; start < opaque.Length; start++)
        {
            if (!opaque[start] || componentIds[start] != 0)
            {
                continue;
            }

            componentId++;
            var component = new List<int>();
            componentIds[start] = componentId;
            frontier.Add(start);
            for (var frontierIndex = 0; frontierIndex < frontier.Count; frontierIndex++)
            {
                var current = frontier[frontierIndex];
                component.Add(current);
                var currentX = current % spriteWidth;
                var currentY = current / spriteWidth;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    var nextY = currentY + offsetY;
                    if (nextY < 0 || nextY >= spriteHeight)
                    {
                        continue;
                    }

                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX == 0 && offsetY == 0)
                        {
                            continue;
                        }

                        var nextX = currentX + offsetX;
                        if (nextX < 0 || nextX >= spriteWidth)
                        {
                            continue;
                        }

                        var next = nextY * spriteWidth + nextX;
                        if (!opaque[next] || componentIds[next] != 0)
                        {
                            continue;
                        }

                        componentIds[next] = componentId;
                        frontier.Add(next);
                    }
                }
            }

            components.Add(component);
            frontier.Clear();
        }

        var mainComponentId = componentIds[mainWallPixel];
        var walls = new List<Vector2[]>();
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            if (index + 1 == mainComponentId
                || !HasEnclosedRegion(opaque, component, spriteWidth, spriteHeight))
            {
                continue;
            }

            var hull = CreateConvexHull(component, spriteWidth, sprite.pivot);
            if (hull.Count < 3)
            {
                continue;
            }

            hull.Add(hull[0]);
            walls.Add(hull.ToArray());
        }

        return walls.ToArray();
    }

    private static bool HasEnclosedRegion(
        bool[] opaque,
        List<int> component,
        int width,
        int height)
    {
        var minimumX = width;
        var minimumY = height;
        var maximumX = -1;
        var maximumY = -1;
        foreach (var pixel in component)
        {
            minimumX = Mathf.Min(minimumX, pixel % width);
            minimumY = Mathf.Min(minimumY, pixel / width);
            maximumX = Mathf.Max(maximumX, pixel % width);
            maximumY = Mathf.Max(maximumY, pixel / width);
        }

        var boundsWidth = maximumX - minimumX + 1;
        var boundsHeight = maximumY - minimumY + 1;
        var outside = new bool[boundsWidth * boundsHeight];
        var frontier = new List<int>();
        for (var x = 0; x < boundsWidth; x++)
        {
            EnqueueOutside(x, 0);
            EnqueueOutside(x, boundsHeight - 1);
        }

        for (var y = 0; y < boundsHeight; y++)
        {
            EnqueueOutside(0, y);
            EnqueueOutside(boundsWidth - 1, y);
        }

        for (var frontierIndex = 0; frontierIndex < frontier.Count; frontierIndex++)
        {
            var current = frontier[frontierIndex];
            var x = current % boundsWidth;
            var y = current / boundsWidth;
            EnqueueOutside(x - 1, y);
            EnqueueOutside(x + 1, y);
            EnqueueOutside(x, y - 1);
            EnqueueOutside(x, y + 1);
        }

        for (var y = 0; y < boundsHeight; y++)
        {
            for (var x = 0; x < boundsWidth; x++)
            {
                var index = y * boundsWidth + x;
                if (!outside[index] && !opaque[(minimumY + y) * width + minimumX + x])
                {
                    return true;
                }
            }
        }

        return false;

        void EnqueueOutside(int x, int y)
        {
            if (x < 0 || x >= boundsWidth || y < 0 || y >= boundsHeight)
            {
                return;
            }

            var localIndex = y * boundsWidth + x;
            var pixelIndex = (minimumY + y) * width + minimumX + x;
            if (outside[localIndex] || opaque[pixelIndex])
            {
                return;
            }

            outside[localIndex] = true;
            frontier.Add(localIndex);
        }
    }

    private static List<Vector2> CreateConvexHull(
        List<int> component,
        int width,
        Vector2 pivot)
    {
        var points = new List<Vector2>(component.Count);
        foreach (var pixel in component)
        {
            points.Add(new Vector2(
                pixel % width + 0.5f - pivot.x,
                pixel / width + 0.5f - pivot.y));
        }

        points.Sort((first, second) =>
        {
            var xOrder = first.x.CompareTo(second.x);
            return xOrder != 0 ? xOrder : first.y.CompareTo(second.y);
        });

        var hull = new List<Vector2>();
        foreach (var point in points)
        {
            while (hull.Count >= 2
                && CrossProduct(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        var lowerHullCount = hull.Count;
        for (var index = points.Count - 2; index >= 0; index--)
        {
            var point = points[index];
            while (hull.Count > lowerHullCount
                && CrossProduct(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
            {
                hull.RemoveAt(hull.Count - 1);
            }

            hull.Add(point);
        }

        if (hull.Count > 1)
        {
            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    private static float CrossProduct(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    private static Sprite LoadSprite(string resourceName)
    {
        using var resource = typeof(ModSprites).Assembly.GetManifestResourceStream(resourceName);
        if (resource == null)
        {
            throw new InvalidDataException($"Embedded sprite resource '{resourceName}' was not found.");
        }

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(buffer.ToArray()))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException($"Embedded sprite resource '{resourceName}' is not a valid PNG.");
        }

        if (!TryFindVisiblePixelBounds(
                texture.GetPixels32(),
                texture.width,
                texture.height,
                out var visibleBounds))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException(
                $"Embedded sprite resource '{resourceName}' contains no visible pixels.");
        }

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(
            texture,
            new Rect(visibleBounds.x, visibleBounds.y, visibleBounds.width, visibleBounds.height),
            new Vector2(0.5f, 0.5f),
            100f);
    }

    private static bool TryFindVisiblePixelBounds(
        Color32[] pixels,
        int width,
        int height,
        out RectInt bounds)
    {
        var minimumX = width;
        var minimumY = height;
        var maximumX = -1;
        var maximumY = -1;

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            for (var x = 0; x < width; x++)
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
            bounds = default;
            return false;
        }

        bounds = new RectInt(
            minimumX,
            minimumY,
            maximumX - minimumX + 1,
            maximumY - minimumY + 1);
        return true;
    }

    private static void FindRuns(
        Color32[] pixels,
        int textureWidth,
        int originX,
        int y,
        int width,
        List<AlphaRun> runs)
    {
        runs.Clear();
        var runStart = -1;
        var rowStart = y * textureWidth + originX;
        for (var x = 0; x < width; x++)
        {
            var opaque = pixels[rowStart + x].a >= MinimumVisibleAlpha;
            if (opaque && runStart < 0)
            {
                runStart = x;
            }
            else if (!opaque && runStart >= 0)
            {
                runs.Add(new AlphaRun(runStart, x - 1));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            runs.Add(new AlphaRun(runStart, width - 1));
        }
    }

    private static int FindNearestOpaquePixel(bool[] opaque, int width, int height, Vector2 point)
    {
        var nearest = -1;
        var nearestDistance = float.PositiveInfinity;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (!opaque[index])
                {
                    continue;
                }

                var distance = (new Vector2(x + 0.5f, y + 0.5f) - point).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = index;
                    nearestDistance = distance;
                }
            }
        }

        return nearest;
    }

    private static bool TryFindPixelPath(
        bool[] opaque,
        int width,
        int height,
        int start,
        int end,
        out List<int> path)
    {
        path = null;
        var distances = new float[opaque.Length];
        var parents = new int[opaque.Length];
        var closed = new bool[opaque.Length];
        for (var i = 0; i < distances.Length; i++)
        {
            distances[i] = float.PositiveInfinity;
            parents[i] = -1;
        }

        var frontier = new MinPriorityQueue();
        distances[start] = 0f;
        frontier.Enqueue(start, PixelDistance(start, end, width));
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            if (closed[current])
            {
                continue;
            }

            if (current == end)
            {
                path = new List<int>();
                for (var pixel = end; pixel >= 0; pixel = parents[pixel])
                {
                    path.Add(pixel);
                    if (pixel == start)
                    {
                        path.Reverse();
                        return true;
                    }
                }

                return false;
            }

            closed[current] = true;
            var currentX = current % width;
            var currentY = current / width;
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            {
                var nextY = currentY + offsetY;
                if (nextY < 0 || nextY >= height)
                {
                    continue;
                }

                for (var offsetX = -1; offsetX <= 1; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                    {
                        continue;
                    }

                    var nextX = currentX + offsetX;
                    if (nextX < 0 || nextX >= width)
                    {
                        continue;
                    }

                    var next = nextY * width + nextX;
                    if (!opaque[next] || closed[next])
                    {
                        continue;
                    }

                    var stepCost = offsetX != 0 && offsetY != 0 ? 1.41421356f : 1f;
                    var candidateDistance = distances[current] + stepCost;
                    if (candidateDistance >= distances[next])
                    {
                        continue;
                    }

                    distances[next] = candidateDistance;
                    parents[next] = current;
                    frontier.Enqueue(next, candidateDistance + PixelDistance(next, end, width));
                }
            }
        }

        return false;
    }

    private static float PixelDistance(int first, int second, int width)
    {
        var deltaX = first % width - second % width;
        var deltaY = first / width - second / width;
        return Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static Vector2[] SimplifyPath(List<Vector2> points, float tolerance)
    {
        if (points.Count <= 2)
        {
            return points.ToArray();
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;
        var pending = new List<PathRange> { new PathRange(0, points.Count - 1) };
        var toleranceSquared = tolerance * tolerance;
        while (pending.Count > 0)
        {
            var lastRange = pending.Count - 1;
            var range = pending[lastRange];
            pending.RemoveAt(lastRange);
            var maximumDistance = toleranceSquared;
            var furthestIndex = -1;
            for (var i = range.Start + 1; i < range.End; i++)
            {
                var distance = DistanceToSegmentSquared(
                    points[i],
                    points[range.Start],
                    points[range.End]);
                if (distance > maximumDistance)
                {
                    maximumDistance = distance;
                    furthestIndex = i;
                }
            }

            if (furthestIndex < 0)
            {
                continue;
            }

            keep[furthestIndex] = true;
            pending.Add(new PathRange(range.Start, furthestIndex));
            pending.Add(new PathRange(furthestIndex, range.End));
        }

        var simplified = new List<Vector2>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified.ToArray();
    }

    private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var segment = end - start;
        var lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            return (point - start).sqrMagnitude;
        }

        var along = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return (point - (start + segment * along)).sqrMagnitude;
    }

    private static bool IsStablePair(
        Color32[] pixels,
        int textureWidth,
        int originX,
        int originY,
        int spriteWidth,
        int y,
        int spriteHeight,
        AlphaRun left,
        AlphaRun right,
        int minimumGap,
        float tolerance,
        List<AlphaRun> runs)
    {
        var leftCenter = (left.Start + left.End) * 0.5f;
        var rightCenter = (right.Start + right.End) * 0.5f;
        for (var offset = 1; offset <= 3; offset++)
        {
            var row = y - offset;
            if (row < 0 || row >= spriteHeight)
            {
                return false;
            }

            FindRuns(pixels, textureWidth, originX, originY + row, spriteWidth, runs);
            var foundLeft = false;
            var foundRight = false;
            var leftRun = default(AlphaRun);
            var rightRun = default(AlphaRun);
            foreach (var run in runs)
            {
                var center = (run.Start + run.End) * 0.5f;
                if (Mathf.Abs(center - leftCenter) <= tolerance)
                {
                    leftRun = run;
                    foundLeft = true;
                }

                if (Mathf.Abs(center - rightCenter) <= tolerance)
                {
                    rightRun = run;
                    foundRight = true;
                }
            }

            if (!foundLeft || !foundRight || rightRun.Start - leftRun.End - 1 < minimumGap)
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct AlphaRun
    {
        internal AlphaRun(int start, int end)
        {
            Start = start;
            End = end;
        }

        internal int Start { get; }
        internal int End { get; }
    }

    private readonly struct PathRange
    {
        internal PathRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        internal int Start { get; }
        internal int End { get; }
    }

    private sealed class MinPriorityQueue
    {
        private readonly List<QueueEntry> entries = new List<QueueEntry>();

        internal int Count => entries.Count;

        internal void Enqueue(int pixel, float priority)
        {
            var index = entries.Count;
            entries.Add(new QueueEntry(pixel, priority));
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (entries[parent].Priority <= priority)
                {
                    break;
                }

                entries[index] = entries[parent];
                index = parent;
            }

            entries[index] = new QueueEntry(pixel, priority);
        }

        internal int Dequeue()
        {
            var result = entries[0].Pixel;
            var last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            if (entries.Count == 0)
            {
                return result;
            }

            var index = 0;
            while (true)
            {
                var left = index * 2 + 1;
                if (left >= entries.Count)
                {
                    break;
                }

                var right = left + 1;
                var child = right < entries.Count && entries[right].Priority < entries[left].Priority
                    ? right
                    : left;
                if (entries[child].Priority >= last.Priority)
                {
                    break;
                }

                entries[index] = entries[child];
                index = child;
            }

            entries[index] = last;
            return result;
        }

        private readonly struct QueueEntry
        {
            internal QueueEntry(int pixel, float priority)
            {
                Pixel = pixel;
                Priority = priority;
            }

            internal int Pixel { get; }
            internal float Priority { get; }
        }
    }

    private static void DestroySprite(Sprite sprite)
    {
        if (!sprite)
        {
            return;
        }

        UnityEngine.Object.Destroy(sprite.texture);
        UnityEngine.Object.Destroy(sprite);
    }
}
