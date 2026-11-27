using System;
using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class FunnelGeometry
{
    internal const float RimFitRatio = 0.95f;
    internal const int WallPointCount = 8;

    private const float ReferenceSpriteWidth = 59f;
    private const float ReferenceSpriteHeight = 88f;
    private const float OutletGapPixels = 12f;

    private static readonly Vector2 TopPoint = new Vector2(27f, 43f);
    private static readonly Vector2 BendPoint = new Vector2(-27f, -12.5f);
    private static readonly Vector2 OutletPoint = new Vector2(-27f, -43f);

    internal static void PositionSides(
        RectTransform leftSide,
        RectTransform rightSide,
        Vector2 spriteSize,
        Vector2 openingCenter,
        float openingWidth)
    {
        if (!leftSide || !rightSide)
        {
            throw new ArgumentNullException(!leftSide ? nameof(leftSide) : nameof(rightSide));
        }

        if (spriteSize.x <= 0f || spriteSize.y <= 0f || openingWidth <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(openingWidth));
        }

        var topPoint = ToSpriteLocalPoint(TopPoint, spriteSize);
        var outletPoint = ToSpriteLocalPoint(OutletPoint, spriteSize);
        var outletGap = OutletGapPixels * spriteSize.x / ReferenceSpriteWidth;
        var fittedOpeningWidth = openingWidth * RimFitRatio;
        var scale = fittedOpeningWidth / (2f * (topPoint.x - outletPoint.x) + outletGap);
        var sideOffset = (-outletPoint.x + outletGap * 0.5f) * scale;
        var verticalOffset = openingCenter.y - topPoint.y * scale;

        leftSide.sizeDelta = spriteSize;
        leftSide.localRotation = Quaternion.identity;
        leftSide.localScale = new Vector3(-scale, scale, 1f);
        leftSide.anchoredPosition = new Vector2(openingCenter.x - sideOffset, verticalOffset);

        rightSide.sizeDelta = spriteSize;
        rightSide.localRotation = Quaternion.identity;
        rightSide.localScale = new Vector3(scale, scale, 1f);
        rightSide.anchoredPosition = new Vector2(openingCenter.x + sideOffset, verticalOffset);
    }

    internal static void WriteWallPoints(
        RectTransform root,
        RectTransform leftSide,
        RectTransform rightSide,
        Vector2 spriteSize,
        Vector2[] wallPoints)
    {
        if (!root || !leftSide || !rightSide)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (wallPoints == null || wallPoints.Length != WallPointCount)
        {
            throw new ArgumentException(
                $"Funnel wall points must contain exactly {WallPointCount} endpoints.",
                nameof(wallPoints));
        }

        var topPoint = ToSpriteLocalPoint(TopPoint, spriteSize);
        var bendPoint = ToSpriteLocalPoint(BendPoint, spriteSize);
        var outletPoint = ToSpriteLocalPoint(OutletPoint, spriteSize);

        WriteSideWallPoints(root, leftSide, topPoint, bendPoint, outletPoint, wallPoints, 0);
        WriteSideWallPoints(root, rightSide, topPoint, bendPoint, outletPoint, wallPoints, 4);
    }

    private static void WriteSideWallPoints(
        RectTransform root,
        RectTransform side,
        Vector2 topPoint,
        Vector2 bendPoint,
        Vector2 outletPoint,
        Vector2[] wallPoints,
        int index)
    {
        wallPoints[index] = ToRootPoint(root, side, topPoint);
        wallPoints[index + 1] = ToRootPoint(root, side, bendPoint);
        wallPoints[index + 2] = wallPoints[index + 1];
        wallPoints[index + 3] = ToRootPoint(root, side, outletPoint);
    }

    private static Vector2 ToRootPoint(RectTransform root, RectTransform side, Vector2 point)
    {
        var worldPoint = side.TransformPoint(point);
        var rootPoint = root.InverseTransformPoint(worldPoint);
        return new Vector2(rootPoint.x, rootPoint.y);
    }

    private static Vector2 ToSpriteLocalPoint(Vector2 referencePoint, Vector2 spriteSize)
    {
        return new Vector2(
            referencePoint.x * spriteSize.x / ReferenceSpriteWidth,
            referencePoint.y * spriteSize.y / ReferenceSpriteHeight);
    }
}
