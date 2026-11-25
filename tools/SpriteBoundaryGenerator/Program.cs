using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SpriteBoundaryGenerator;

internal static class Program
{
    private const byte MinimumVisibleAlpha = 32;
    private const int MaximumWallWidthSamples = 24;
    private const float WallWidthScanStep = 0.25f;
    private const float MaximumWallWidthScanDistance = 8f;
    private const float DiagonalCost = 1.41421356f;

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: SpriteBoundaryGenerator <sprite-directory> <generated-csharp-file>");
            return 2;
        }

        var spriteDirectory = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);
        if (!Directory.Exists(spriteDirectory))
        {
            Console.Error.WriteLine($"Sprite directory not found: {spriteDirectory}");
            return 2;
        }

        try
        {
            var sprites = Directory.GetFiles(spriteDirectory, "*.png")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(Generate)
                .ToArray();
            if (sprites.Length == 0)
            {
                throw new InvalidDataException($"No PNG sprites found in '{spriteDirectory}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var catalog = WriteCatalog(sprites).Replace("\r\n", "\n");
            File.WriteAllText(outputPath, catalog, new UTF8Encoding(false));
            Console.WriteLine(
                $"Generated boundaries for {sprites.Length} sprites: {outputPath}");
            foreach (var sprite in sprites)
            {
                Console.WriteLine(
                    $"  {sprite.Name}: opening={(sprite.HasOpening ? "yes" : "no")}, "
                    + $"wall points={sprite.Centerline.Length}, "
                    + $"interior walls={sprite.InteriorWalls.Length}");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static SpriteBoundary Generate(string path)
    {
        using var bitmap = new Bitmap(path);
        var bounds = GetVisibleBounds(bitmap);
        if (bounds.Width == 0 || bounds.Height == 0)
        {
            throw new InvalidDataException($"Sprite contains no visible pixels: {path}");
        }

        var width = bounds.Width;
        var height = bounds.Height;
        var pivot = new Vec2(width * 0.5f, height * 0.5f);
        var opaque = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                opaque[y * width + x] =
                    bitmap.GetPixel(bounds.X + x, bounds.Bottom - 1 - y).A >= MinimumVisibleAlpha;
            }
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (!TryFindOpening(opaque, width, height, pivot, out var opening, out var openingWidth))
        {
            return new SpriteBoundary(
                name,
                false,
                Vec2.Zero,
                0f,
                Array.Empty<Vec2>(),
                Array.Empty<Vec2[]>(),
                Array.Empty<Vec2>());
        }

        if (!TryFindCenterline(
                opaque,
                width,
                height,
                pivot,
                opening,
                openingWidth,
                out var centerline,
                out var halfWidth))
        {
            return new SpriteBoundary(
                name,
                true,
                opening,
                openingWidth,
                Array.Empty<Vec2>(),
                Array.Empty<Vec2[]>(),
                Array.Empty<Vec2>());
        }

        var interiorWalls = FindInteriorWalls(opaque, width, height, pivot, opening, openingWidth);
        var collisionBand = CreateWallCollisionBand(centerline, halfWidth);
        return new SpriteBoundary(
            name,
            true,
            opening,
            openingWidth,
            centerline,
            interiorWalls,
            collisionBand);
    }

    private static Rectangle GetVisibleBounds(Bitmap bitmap)
    {
        var minimumX = bitmap.Width;
        var minimumY = bitmap.Height;
        var maximumX = -1;
        var maximumY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A < MinimumVisibleAlpha)
                {
                    continue;
                }

                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            }
        }

        return maximumX < minimumX || maximumY < minimumY
            ? Rectangle.Empty
            : Rectangle.FromLTRB(minimumX, minimumY, maximumX + 1, maximumY + 1);
    }

    private static bool TryFindOpening(
        bool[] opaque,
        int width,
        int height,
        Vec2 pivot,
        out Vec2 opening,
        out float openingWidth)
    {
        opening = Vec2.Zero;
        openingWidth = 0f;
        var runs = new List<AlphaRun>();
        var stableRuns = new List<AlphaRun>();
        var searchBottom = Math.Max(0, height - (int)Math.Ceiling(height * 0.4f));
        var minimumGap = (int)Math.Ceiling(width * 0.1f);
        var tolerance = Math.Max(3f, width * 0.06f);
        for (var y = height - 1; y >= searchBottom; y--)
        {
            FindRuns(opaque, width, y, runs);
            for (var leftIndex = 0; leftIndex < runs.Count; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < runs.Count; rightIndex++)
                {
                    var left = runs[leftIndex];
                    var right = runs[rightIndex];
                    if (right.Start - left.End - 1 < minimumGap
                        || !IsStablePair(
                            opaque,
                            width,
                            height,
                            y,
                            left,
                            right,
                            minimumGap,
                            tolerance,
                            stableRuns))
                    {
                        continue;
                    }

                    openingWidth = right.Start - left.End - 1;
                    opening = new Vec2(
                        (left.End + right.Start) * 0.5f - pivot.X,
                        y + 0.5f - pivot.Y);
                    return openingWidth > 0f;
                }
            }
        }

        return false;
    }

    private static void FindRuns(bool[] opaque, int width, int y, List<AlphaRun> runs)
    {
        runs.Clear();
        var runStart = -1;
        var rowStart = y * width;
        for (var x = 0; x < width; x++)
        {
            if (opaque[rowStart + x] && runStart < 0)
            {
                runStart = x;
            }
            else if (!opaque[rowStart + x] && runStart >= 0)
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

    private static bool IsStablePair(
        bool[] opaque,
        int width,
        int height,
        int y,
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
            if (row < 0 || row >= height)
            {
                return false;
            }

            FindRuns(opaque, width, row, runs);
            var foundLeft = false;
            var foundRight = false;
            var leftRun = default(AlphaRun);
            var rightRun = default(AlphaRun);
            foreach (var run in runs)
            {
                var center = (run.Start + run.End) * 0.5f;
                if (Math.Abs(center - leftCenter) <= tolerance)
                {
                    leftRun = run;
                    foundLeft = true;
                }

                if (Math.Abs(center - rightCenter) <= tolerance)
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

    private static bool TryFindCenterline(
        bool[] opaque,
        int width,
        int height,
        Vec2 pivot,
        Vec2 opening,
        float openingWidth,
        out Vec2[] centerline,
        out float halfWidth)
    {
        centerline = Array.Empty<Vec2>();
        halfWidth = 0f;
        var leftOpening = opening - new Vec2(openingWidth * 0.5f, 0f);
        var rightOpening = opening + new Vec2(openingWidth * 0.5f, 0f);
        var start = FindNearestOpaquePixel(opaque, width, height, leftOpening + pivot);
        var end = FindNearestOpaquePixel(opaque, width, height, rightOpening + pivot);
        if (start < 0
            || end < 0
            || !TryFindPixelPath(opaque, width, height, start, end, out var pixelPath))
        {
            return false;
        }

        var rawPoints = new List<Vec2>(pixelPath.Count + 2) { leftOpening };
        var widthSamples = new List<float>();
        for (var index = 0; index < pixelPath.Count; index++)
        {
            var pixel = pixelPath[index];
            var center = PixelCenter(pixel, width);
            if (index > 0 && index < pixelPath.Count - 1)
            {
                var tangent = PixelCenter(pixelPath[index + 1], width)
                    - PixelCenter(pixelPath[index - 1], width);
                if (tangent.LengthSquared > 0.000001f)
                {
                    var normal = new Vec2(-tangent.Y, tangent.X).Normalized;
                    var positiveWidth = DistanceToVisibleEdge(opaque, width, height, center, normal);
                    var negativeWidth = DistanceToVisibleEdge(opaque, width, height, center, -normal);
                    center += normal * ((positiveWidth - negativeWidth) * 0.5f);
                    widthSamples.Add((positiveWidth + negativeWidth) * 0.5f);
                }
            }

            rawPoints.Add(center - pivot);
        }

        rawPoints.Add(rightOpening);
        halfWidth = widthSamples.Count == 0 ? 0.5f : widthSamples.Average();
        var tolerance = Math.Max(1f, width * 0.012f);
        centerline = SimplifyToMinimumSegments(rawPoints, tolerance);
        return centerline.Length >= 2;
    }

    private static float DistanceToVisibleEdge(
        bool[] opaque,
        int width,
        int height,
        Vec2 center,
        Vec2 direction)
    {
        var lastVisibleDistance = 0f;
        for (var distance = WallWidthScanStep;
             distance <= MaximumWallWidthScanDistance;
             distance += WallWidthScanStep)
        {
            var sample = center + direction * distance;
            var x = (int)Math.Floor(sample.X);
            var y = (int)Math.Floor(sample.Y);
            if (x < 0 || x >= width || y < 0 || y >= height || !opaque[y * width + x])
            {
                return (lastVisibleDistance + distance) * 0.5f;
            }

            lastVisibleDistance = distance;
        }

        return lastVisibleDistance;
    }

    private static Vec2[] SimplifyToMinimumSegments(List<Vec2> points, float tolerance)
    {
        if (points.Count <= 2)
        {
            return points.ToArray();
        }

        var segmentCounts = Enumerable.Repeat(int.MaxValue, points.Count).ToArray();
        var parents = Enumerable.Repeat(-1, points.Count).ToArray();
        segmentCounts[0] = 0;
        var toleranceSquared = tolerance * tolerance;
        for (var end = 1; end < points.Count; end++)
        {
            for (var start = 0; start < end; start++)
            {
                if (segmentCounts[start] == int.MaxValue
                    || segmentCounts[start] + 1 >= segmentCounts[end]
                    || !CanReplaceWithSegment(points, start, end, toleranceSquared))
                {
                    continue;
                }

                segmentCounts[end] = segmentCounts[start] + 1;
                parents[end] = start;
            }
        }

        var result = new List<Vec2>();
        for (var index = points.Count - 1; index >= 0; index = parents[index])
        {
            result.Add(points[index]);
            if (index == 0)
            {
                break;
            }
        }

        result.Reverse();
        return result.ToArray();
    }

    private static bool CanReplaceWithSegment(
        List<Vec2> points,
        int start,
        int end,
        float toleranceSquared)
    {
        for (var index = start + 1; index < end; index++)
        {
            if (DistanceToSegmentSquared(points[index], points[start], points[end]) > toleranceSquared)
            {
                return false;
            }
        }

        return true;
    }

    private static float DistanceToSegmentSquared(Vec2 point, Vec2 start, Vec2 end)
    {
        var segment = end - start;
        if (segment.LengthSquared <= 0.000001f)
        {
            return (point - start).LengthSquared;
        }

        var along = Math.Max(0f, Math.Min(
            1f,
            Vec2.Dot(point - start, segment) / segment.LengthSquared));
        return (point - (start + segment * along)).LengthSquared;
    }

    private static int FindNearestOpaquePixel(bool[] opaque, int width, int height, Vec2 point)
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

                var distance = (new Vec2(x + 0.5f, y + 0.5f) - point).LengthSquared;
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
        path = null!;
        var distances = Enumerable.Repeat(float.PositiveInfinity, opaque.Length).ToArray();
        var parents = Enumerable.Repeat(-1, opaque.Length).ToArray();
        var closed = new bool[opaque.Length];
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

                    var stepCost = offsetX != 0 && offsetY != 0 ? DiagonalCost : 1f;
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
        return (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static Vec2[][] FindInteriorWalls(
        bool[] opaque,
        int width,
        int height,
        Vec2 pivot,
        Vec2 opening,
        float openingWidth)
    {
        var leftOpening = opening - new Vec2(openingWidth * 0.5f, 0f);
        var mainWallPixel = FindNearestOpaquePixel(opaque, width, height, leftOpening + pivot);
        if (mainWallPixel < 0)
        {
            return Array.Empty<Vec2[]>();
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
                        if (!opaque[next] || componentIds[next] != 0)
                        {
                            continue;
                        }

                        componentIds[next] = componentId;
                        frontier.Add(next);
                    }
                }

                if (frontierIndex + 1 == frontier.Count)
                {
                    break;
                }
            }

            components.Add(component);
            frontier.Clear();
        }

        var mainComponentId = componentIds[mainWallPixel];
        var walls = new List<Vec2[]>();
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            if (index + 1 == mainComponentId
                || !HasEnclosedRegion(opaque, component, width, height))
            {
                continue;
            }

            var hull = CreateConvexHull(component, width, pivot);
            if (hull.Count < 3)
            {
                continue;
            }

            hull.Add(hull[0]);
            walls.Add(hull.ToArray());
        }

        return walls.ToArray();
    }

    private static bool HasEnclosedRegion(bool[] opaque, List<int> component, int width, int height)
    {
        var minimumX = width;
        var minimumY = height;
        var maximumX = -1;
        var maximumY = -1;
        foreach (var pixel in component)
        {
            minimumX = Math.Min(minimumX, pixel % width);
            minimumY = Math.Min(minimumY, pixel / width);
            maximumX = Math.Max(maximumX, pixel % width);
            maximumY = Math.Max(maximumY, pixel / width);
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

        for (var index = 0; index < frontier.Count; index++)
        {
            var current = frontier[index];
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
                var localIndex = y * boundsWidth + x;
                var pixelIndex = (minimumY + y) * width + minimumX + x;
                if (!outside[localIndex] && !opaque[pixelIndex])
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

    private static List<Vec2> CreateConvexHull(List<int> component, int width, Vec2 pivot)
    {
        var points = component.Select(pixel => new Vec2(
            pixel % width + 0.5f - pivot.X,
            pixel / width + 0.5f - pivot.Y)).ToList();
        points.Sort((first, second) =>
        {
            var xOrder = first.X.CompareTo(second.X);
            return xOrder != 0 ? xOrder : first.Y.CompareTo(second.Y);
        });

        var hull = new List<Vec2>();
        foreach (var point in points)
        {
            while (hull.Count >= 2
                && CrossProduct(hull[hull.Count - 1] - hull[hull.Count - 2],
                    point - hull[hull.Count - 1]) <= 0f)
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
                && CrossProduct(hull[hull.Count - 1] - hull[hull.Count - 2],
                    point - hull[hull.Count - 1]) <= 0f)
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

    private static float CrossProduct(Vec2 first, Vec2 second)
    {
        return first.X * second.Y - first.Y * second.X;
    }

    private static Vec2[] CreateWallCollisionBand(Vec2[] centerline, float halfWidth)
    {
        if (centerline.Length < 2)
        {
            return Array.Empty<Vec2>();
        }

        var closed = (centerline[0] - centerline[centerline.Length - 1]).LengthSquared
            <= 0.000001f;
        var pointCount = closed ? centerline.Length - 1 : centerline.Length;
        var outward = OffsetPolyline(centerline, pointCount, halfWidth, closed);
        var inward = OffsetPolyline(centerline, pointCount, -halfWidth, closed);
        var band = new Vec2[outward.Length + inward.Length];
        Array.Copy(outward, band, outward.Length);
        for (var index = 0; index < inward.Length; index++)
        {
            band[outward.Length + index] = inward[inward.Length - index - 1];
        }

        return band;
    }

    private static Vec2[] OffsetPolyline(Vec2[] points, int pointCount, float offset, bool closed)
    {
        var offsetPoints = new Vec2[pointCount];
        var maximumMiter = Math.Max(Math.Abs(offset) * 4f, 0.01f);
        for (var index = 0; index < pointCount; index++)
        {
            var previousIndex = closed
                ? (index - 1 + pointCount) % pointCount
                : Math.Max(0, index - 1);
            var nextIndex = closed
                ? (index + 1) % pointCount
                : Math.Min(pointCount - 1, index + 1);
            var previousDirection = index > 0 || closed
                ? (points[index] - points[previousIndex]).Normalized
                : (points[1] - points[0]).Normalized;
            var nextDirection = index < pointCount - 1 || closed
                ? (points[nextIndex] - points[index]).Normalized
                : previousDirection;
            var previousNormal = new Vec2(previousDirection.Y, -previousDirection.X);
            var nextNormal = new Vec2(nextDirection.Y, -nextDirection.X);
            if (!closed && index == 0)
            {
                offsetPoints[index] = points[index] + nextNormal * offset;
                continue;
            }

            if (!closed && index == pointCount - 1)
            {
                offsetPoints[index] = points[index] + previousNormal * offset;
                continue;
            }

            var miter = (previousNormal + nextNormal).Normalized;
            if (miter.LengthSquared <= 0.000001f)
            {
                miter = nextNormal;
            }

            var denominator = Math.Max(Vec2.Dot(miter, nextNormal), 0.25f);
            var miterLength = Math.Max(-maximumMiter, Math.Min(maximumMiter, offset / denominator));
            offsetPoints[index] = points[index] + miter * miterLength;
        }

        return offsetPoints;
    }

    private static Vec2 PixelCenter(int index, int width)
    {
        return new Vec2(index % width + 0.5f, index / width + 0.5f);
    }

    private static string WriteCatalog(SpriteBoundary[] sprites)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using UnityEngine;");
        builder.AppendLine();
        builder.AppendLine("namespace RealisticLiquidTransfer;");
        builder.AppendLine();
        builder.AppendLine("internal static class GeneratedContainerBoundaryCatalog");
        builder.AppendLine("{");
        builder.AppendLine("    private static readonly Dictionary<string, ContainerBoundaryData> Boundaries =");
        builder.AppendLine("        new Dictionary<string, ContainerBoundaryData>(System.StringComparer.OrdinalIgnoreCase)");
        builder.AppendLine("        {");
        foreach (var sprite in sprites)
        {
            builder.Append("            [")
                .Append(ToLiteral(sprite.Name))
                .Append("] = new ContainerBoundaryData(")
                .Append(sprite.HasOpening ? "true" : "false")
                .Append(", ")
                .Append(Format(sprite.Opening.X))
                .Append("f, ")
                .Append(Format(sprite.Opening.Y))
                .Append("f, ")
                .Append(Format(sprite.OpeningWidth))
                .Append("f, ")
                .Append(FormatPoints(sprite.Centerline))
                .Append(", ")
                .Append(FormatWalls(sprite.InteriorWalls))
                .Append(", ")
                .Append(FormatPoints(sprite.CollisionBand))
                .AppendLine("),");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.AppendLine("    internal static bool TryGet(string spriteName, out ContainerBoundaryData boundary)");
        builder.AppendLine("    {");
        builder.AppendLine("        return Boundaries.TryGetValue(spriteName, out boundary);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string FormatWalls(Vec2[][] walls)
    {
        return "new Vector2[][] { "
            + string.Join(", ", walls.Select(FormatPoints))
            + " }";
    }

    private static string FormatPoints(Vec2[] points)
    {
        return "new Vector2[] { "
            + string.Join(", ", points.Select(point =>
                $"new Vector2({Format(point.X)}f, {Format(point.Y)}f)"))
            + " }";
    }

    private static string Format(float value)
    {
        if (Math.Abs(value) < 0.0000005f)
        {
            value = 0f;
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string ToLiteral(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private readonly struct SpriteBoundary
    {
        internal SpriteBoundary(
            string name,
            bool hasOpening,
            Vec2 opening,
            float openingWidth,
            Vec2[] centerline,
            Vec2[][] interiorWalls,
            Vec2[] collisionBand)
        {
            Name = name;
            HasOpening = hasOpening;
            Opening = opening;
            OpeningWidth = openingWidth;
            Centerline = centerline;
            InteriorWalls = interiorWalls;
            CollisionBand = collisionBand;
        }

        internal string Name { get; }
        internal bool HasOpening { get; }
        internal Vec2 Opening { get; }
        internal float OpeningWidth { get; }
        internal Vec2[] Centerline { get; }
        internal Vec2[][] InteriorWalls { get; }
        internal Vec2[] CollisionBand { get; }
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

    private readonly struct Vec2
    {
        internal Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        internal static Vec2 Zero => new Vec2(0f, 0f);
        internal float X { get; }
        internal float Y { get; }
        internal float LengthSquared => X * X + Y * Y;
        internal Vec2 Normalized
        {
            get
            {
                var length = (float)Math.Sqrt(LengthSquared);
                return length > 0.000001f ? this * (1f / length) : Zero;
            }
        }

        internal static float Dot(Vec2 first, Vec2 second)
        {
            return first.X * second.X + first.Y * second.Y;
        }

        public static Vec2 operator +(Vec2 first, Vec2 second)
        {
            return new Vec2(first.X + second.X, first.Y + second.Y);
        }

        public static Vec2 operator -(Vec2 first, Vec2 second)
        {
            return new Vec2(first.X - second.X, first.Y - second.Y);
        }

        public static Vec2 operator -(Vec2 value)
        {
            return new Vec2(-value.X, -value.Y);
        }

        public static Vec2 operator *(Vec2 value, float scalar)
        {
            return new Vec2(value.X * scalar, value.Y * scalar);
        }
    }
}
