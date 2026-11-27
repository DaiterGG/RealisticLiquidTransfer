using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class LiquidContainerLayout
{
    internal const float ContainerScale = 4f;
    internal const float ContainerGap = 40f;
    internal const float ScreenPadding = 40f;

    internal static bool TryGetAreaScaledScale(
        float targetCapacity,
        float referenceCapacity,
        double referenceArea,
        double spriteArea,
        out float scale)
    {
        scale = ContainerScale;
        if (targetCapacity <= 0f
            || referenceCapacity <= 0f
            || referenceArea <= 0d
            || spriteArea <= 0d)
        {
            return false;
        }

        var areaRatio = (targetCapacity / referenceCapacity) * (referenceArea / spriteArea);
        if (areaRatio <= 0d || double.IsNaN(areaRatio) || double.IsInfinity(areaRatio))
        {
            return false;
        }

        scale = ContainerScale * (float)System.Math.Sqrt(areaRatio);
        return !float.IsNaN(scale) && !float.IsInfinity(scale);
    }

    internal static Vector2 TransformOpeningPoint(RectTransform container, Vector2 point)
    {
        var transformed = container.localRotation * Vector3.Scale(point, container.localScale);
        return new Vector2(transformed.x, transformed.y);
    }

    internal static void PositionContainersSideBySide(
        RectTransform receiver,
        Vector2 receiverOpeningPoint,
        RectTransform source,
        Vector2 sourceOpeningPoint)
    {
        var receiverWidth = receiver.rect.width * receiver.localScale.x;
        var sourceWidth = source.rect.width * source.localScale.x;
        var totalWidth = receiverWidth + ContainerGap + sourceWidth;
        var receiverX = -totalWidth * 0.5f + receiverWidth * 0.5f;
        var sourceX = receiverX + receiverWidth * 0.5f + ContainerGap + sourceWidth * 0.5f;
        var receiverOpeningOffset = TransformOpeningPoint(receiver, receiverOpeningPoint);
        var sourceOpeningOffset = TransformOpeningPoint(source, sourceOpeningPoint);

        receiver.anchoredPosition = new Vector2(receiverX, -receiverOpeningOffset.y);
        source.anchoredPosition = new Vector2(sourceX, -sourceOpeningOffset.y);
    }

    internal static float GetFitScale(
        RectTransform screen,
        RectTransform layoutRoot,
        params RectTransform[] containers)
    {
        var available = screen.rect.size * 0.5f - Vector2.one * ScreenPadding;
        var maxX = 0f;
        var maxY = 0f;
        foreach (var container in containers)
        {
            IncludeExtent(layoutRoot, container, ref maxX, ref maxY);
        }

        var scaleX = maxX > 0f ? available.x / maxX : 1f;
        var scaleY = maxY > 0f ? available.y / maxY : 1f;
        return Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.01f, 1f);
    }

    private static void IncludeExtent(
        RectTransform reference,
        RectTransform target,
        ref float maxX,
        ref float maxY)
    {
        var minX = float.PositiveInfinity;
        var maxTargetX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var maxTargetY = float.NegativeInfinity;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var point = reference.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, point.x);
            maxTargetX = Mathf.Max(maxTargetX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxTargetY = Mathf.Max(maxTargetY, point.y);
        }

        maxX = Mathf.Max(maxX, Mathf.Abs(minX), Mathf.Abs(maxTargetX));
        maxY = Mathf.Max(maxY, Mathf.Abs(minY), Mathf.Abs(maxTargetY));
    }
}
