using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class HandAlphaGradient : BaseMeshEffect
{
    private const int FadeSteps = 16;
    private const float TransparentHeight = 0.45f;
    private const float FadeHeight = 0.3f;
    private readonly List<UIVertex> sourceVertices = new();
    private readonly List<UIVertex> lowerClip = new(6);
    private readonly List<UIVertex> clippedPolygon = new(6);

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount < 3)
        {
            return;
        }

        var rect = graphic.rectTransform.rect;
        var transparentHeight = rect.height * TransparentHeight;
        var fadeHeight = rect.height * FadeHeight;
        if (fadeHeight <= 0f)
        {
            return;
        }

        sourceVertices.Clear();
        vertexHelper.GetUIVertexStream(sourceVertices);
        vertexHelper.Clear();

        var stepHeight = fadeHeight / FadeSteps;
        for (var triangle = 0; triangle + 2 < sourceVertices.Count; triangle += 3)
        {
            for (var step = 0; step <= FadeSteps + 1; step++)
            {
                var lower = step == 0
                    ? rect.yMin
                    : transparentHeight + rect.yMin + (step - 1) * stepHeight;
                var upper = step == 0
                    ? rect.yMin + transparentHeight
                    : step == FadeSteps + 1
                        ? rect.yMax
                        : lower + stepHeight;

                clippedPolygon.Clear();
                clippedPolygon.Add(sourceVertices[triangle]);
                clippedPolygon.Add(sourceVertices[triangle + 1]);
                clippedPolygon.Add(sourceVertices[triangle + 2]);

                ClipAtHeight(clippedPolygon, lowerClip, lower, keepAbove: true);
                ClipAtHeight(lowerClip, clippedPolygon, upper, keepAbove: false);
                if (clippedPolygon.Count < 3)
                {
                    continue;
                }

                var startIndex = vertexHelper.currentVertCount;
                foreach (var originalVertex in clippedPolygon)
                {
                    var vertex = originalVertex;
                    var fade = Mathf.Clamp01((vertex.position.y - rect.yMin - transparentHeight) / fadeHeight);
                    fade = fade * fade * (3f - 2f * fade);
                    var color = (Color)vertex.color;
                    color.a *= fade;
                    vertex.color = color;
                    vertexHelper.AddVert(vertex);
                }

                for (var index = 1; index < clippedPolygon.Count - 1; index++)
                {
                    vertexHelper.AddTriangle(startIndex, startIndex + index, startIndex + index + 1);
                }
            }
        }
    }

    private static void ClipAtHeight(
        List<UIVertex> source,
        List<UIVertex> destination,
        float height,
        bool keepAbove)
    {
        destination.Clear();
        if (source.Count == 0)
        {
            return;
        }

        var previous = source[source.Count - 1];
        var previousInside = IsInside(previous.position.y, height, keepAbove);

        foreach (var current in source)
        {
            var currentInside = IsInside(current.position.y, height, keepAbove);
            if (currentInside != previousInside)
            {
                var amount = (height - previous.position.y) / (current.position.y - previous.position.y);
                destination.Add(Interpolate(previous, current, amount));
            }

            if (currentInside)
            {
                destination.Add(current);
            }

            previous = current;
            previousInside = currentInside;
        }
    }

    private static bool IsInside(float y, float height, bool keepAbove)
    {
        return keepAbove ? y >= height : y <= height;
    }

    private static UIVertex Interpolate(UIVertex from, UIVertex to, float amount)
    {
        return new UIVertex
        {
            position = Vector3.LerpUnclamped(from.position, to.position, amount),
            normal = Vector3.LerpUnclamped(from.normal, to.normal, amount),
            tangent = Vector4.LerpUnclamped(from.tangent, to.tangent, amount),
            color = Color32.Lerp(from.color, to.color, amount),
            uv0 = Vector4.LerpUnclamped(from.uv0, to.uv0, amount),
            uv1 = Vector4.LerpUnclamped(from.uv1, to.uv1, amount),
            uv2 = Vector4.LerpUnclamped(from.uv2, to.uv2, amount),
            uv3 = Vector4.LerpUnclamped(from.uv3, to.uv3, amount)
        };
    }
}
