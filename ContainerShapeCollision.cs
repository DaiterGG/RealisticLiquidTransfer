using System;
using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class ContainerShapeCollision
{
    private const float Epsilon = 0.001f;
    private static Vector2[] firstPoints = Array.Empty<Vector2>();
    private static Vector2[] secondPoints = Array.Empty<Vector2>();

    internal static bool Overlaps(
        Vector2[] firstOutline,
        Vector2 firstPosition,
        float firstScale,
        float firstAngle,
        Vector2[] secondOutline,
        Vector2 secondPosition,
        float secondScale,
        float secondAngle)
    {
        if (firstOutline == null || secondOutline == null
            || firstOutline.Length < 3 || secondOutline.Length < 3)
        {
            return false;
        }

        EnsureBuffer(ref firstPoints, firstOutline.Length);
        EnsureBuffer(ref secondPoints, secondOutline.Length);
        TransformPoints(firstOutline, firstPosition, firstScale, firstAngle, firstPoints);
        TransformPoints(secondOutline, secondPosition, secondScale, secondAngle, secondPoints);
        if (!BoundsOverlap(firstPoints, secondPoints))
        {
            return false;
        }

        for (var firstIndex = 0; firstIndex < firstPoints.Length; firstIndex++)
        {
            var firstStart = firstPoints[firstIndex];
            var firstEnd = firstPoints[(firstIndex + 1) % firstPoints.Length];
            for (var secondIndex = 0; secondIndex < secondPoints.Length; secondIndex++)
            {
                var secondStart = secondPoints[secondIndex];
                var secondEnd = secondPoints[(secondIndex + 1) % secondPoints.Length];
                if (SegmentsIntersect(firstStart, firstEnd, secondStart, secondEnd))
                {
                    return true;
                }
            }
        }

        return IsPointInside(firstPoints[0], secondPoints) || IsPointInside(secondPoints[0], firstPoints);
    }

    internal static bool OverlapsSegments(
        Vector2[] outline,
        Vector2 position,
        float scale,
        float angle,
        Vector2[] segments,
        float collisionRadius)
    {
        if (outline == null || outline.Length < 3 || segments == null || segments.Length < 2)
        {
            return false;
        }

        EnsureBuffer(ref firstPoints, outline.Length);
        TransformPoints(outline, position, scale, angle, firstPoints);
        var collisionRadiusSquared = collisionRadius * collisionRadius;
        for (var segmentIndex = 0; segmentIndex + 1 < segments.Length; segmentIndex += 2)
        {
            var segmentStart = segments[segmentIndex];
            var segmentEnd = segments[segmentIndex + 1];
            if (IsPointInside(segmentStart, firstPoints) || IsPointInside(segmentEnd, firstPoints))
            {
                return true;
            }

            for (var pointIndex = 0; pointIndex < firstPoints.Length; pointIndex++)
            {
                var edgeStart = firstPoints[pointIndex];
                var edgeEnd = firstPoints[(pointIndex + 1) % firstPoints.Length];
                if (SegmentsIntersect(segmentStart, segmentEnd, edgeStart, edgeEnd)
                    || PointSegmentDistanceSquared(edgeStart, segmentStart, segmentEnd) <= collisionRadiusSquared
                    || PointSegmentDistanceSquared(edgeEnd, segmentStart, segmentEnd) <= collisionRadiusSquared
                    || PointSegmentDistanceSquared(segmentStart, edgeStart, edgeEnd) <= collisionRadiusSquared
                    || PointSegmentDistanceSquared(segmentEnd, edgeStart, edgeEnd) <= collisionRadiusSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool OverlapsPolylineSegments(
        Vector2[] outline,
        Vector2 position,
        float scale,
        float angle,
        Vector2[] segments,
        float collisionRadius)
    {
        if (outline == null || outline.Length < 2 || segments == null || segments.Length < 2)
        {
            return false;
        }

        EnsureBuffer(ref firstPoints, outline.Length);
        TransformPoints(outline, position, scale, angle, firstPoints);
        var collisionRadiusSquared = collisionRadius * collisionRadius;
        for (var segmentIndex = 0; segmentIndex + 1 < segments.Length; segmentIndex += 2)
        {
            var segmentStart = segments[segmentIndex];
            var segmentEnd = segments[segmentIndex + 1];
            for (var pointIndex = 0; pointIndex + 1 < firstPoints.Length; pointIndex++)
            {
                var edgeStart = firstPoints[pointIndex];
                var edgeEnd = firstPoints[pointIndex + 1];
                if (SegmentsIntersect(segmentStart, segmentEnd, edgeStart, edgeEnd)
                    || PointSegmentDistanceSquared(edgeStart, segmentStart, segmentEnd) <= collisionRadiusSquared
                    || PointSegmentDistanceSquared(edgeEnd, segmentStart, segmentEnd) <= collisionRadiusSquared
                    || PointSegmentDistanceSquared(segmentStart, edgeStart, edgeEnd) <= collisionRadiusSquared
                    || PointSegmentDistanceSquared(segmentEnd, edgeStart, edgeEnd) <= collisionRadiusSquared)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void EnsureBuffer(ref Vector2[] buffer, int length)
    {
        if (buffer.Length != length)
        {
            buffer = new Vector2[length];
        }
    }

    private static void TransformPoints(
        Vector2[] points,
        Vector2 position,
        float scale,
        float angle,
        Vector2[] transformed)
    {
        var rotation = Quaternion.Euler(0f, 0f, angle);
        for (var i = 0; i < points.Length; i++)
        {
            var point = rotation * (points[i] * scale);
            transformed[i] = position + new Vector2(point.x, point.y);
        }
    }

    private static bool BoundsOverlap(Vector2[] first, Vector2[] second)
    {
        GetBounds(first, out var firstMin, out var firstMax);
        GetBounds(second, out var secondMin, out var secondMax);
        return firstMin.x <= secondMax.x + Epsilon
            && firstMax.x + Epsilon >= secondMin.x
            && firstMin.y <= secondMax.y + Epsilon
            && firstMax.y + Epsilon >= secondMin.y;
    }

    private static void GetBounds(Vector2[] points, out Vector2 minimum, out Vector2 maximum)
    {
        minimum = points[0];
        maximum = points[0];
        for (var i = 1; i < points.Length; i++)
        {
            minimum = Vector2.Min(minimum, points[i]);
            maximum = Vector2.Max(maximum, points[i]);
        }
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var abC = Cross(b - a, c - a);
        var abD = Cross(b - a, d - a);
        var cdA = Cross(d - c, a - c);
        var cdB = Cross(d - c, b - c);
        if (((abC > Epsilon && abD < -Epsilon) || (abC < -Epsilon && abD > Epsilon))
            && ((cdA > Epsilon && cdB < -Epsilon) || (cdA < -Epsilon && cdB > Epsilon)))
        {
            return true;
        }

        return Mathf.Abs(abC) <= Epsilon && IsOnSegment(c, a, b)
            || Mathf.Abs(abD) <= Epsilon && IsOnSegment(d, a, b)
            || Mathf.Abs(cdA) <= Epsilon && IsOnSegment(a, c, d)
            || Mathf.Abs(cdB) <= Epsilon && IsOnSegment(b, c, d);
    }

    private static bool IsOnSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        return point.x >= Mathf.Min(start.x, end.x) - Epsilon
            && point.x <= Mathf.Max(start.x, end.x) + Epsilon
            && point.y >= Mathf.Min(start.y, end.y) - Epsilon
            && point.y <= Mathf.Max(start.y, end.y) + Epsilon;
    }

    private static float PointSegmentDistanceSquared(Vector2 point, Vector2 start, Vector2 end)
    {
        var direction = end - start;
        var lengthSquared = direction.sqrMagnitude;
        if (lengthSquared <= Epsilon * Epsilon)
        {
            return (point - start).sqrMagnitude;
        }

        var projection = Mathf.Clamp01(Vector2.Dot(point - start, direction) / lengthSquared);
        return (point - (start + direction * projection)).sqrMagnitude;
    }

    private static bool IsPointInside(Vector2 point, Vector2[] polygon)
    {
        var inside = false;
        for (int i = 0, previous = polygon.Length - 1; i < polygon.Length; previous = i++)
        {
            var currentPoint = polygon[i];
            var previousPoint = polygon[previous];
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

    private static float Cross(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }
}
