using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class ArcRotationHandle : MaskableGraphic
{
    private const int ArcSegments = 24;
    private const float StartAngle = 200f;
    private const float EndAngle = 340f;
    private const float VerticalRadiusRatio = 0.35f;
    private const float ArrowHalfAngle = 38f;

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        var radius = Mathf.Min(rectTransform.rect.width * 0.42f, rectTransform.rect.height * 1.05f);
        var verticalRadius = radius * VerticalRadiusRatio;
        var centerY = verticalRadius * 0.342f;
        var thickness = Mathf.Max(1f, radius * 0.045f);
        var arrowLength = radius * 0.2f;
        var startTip = GetArcPoint(StartAngle, radius, verticalRadius, centerY);
        var endTip = GetArcPoint(EndAngle, radius, verticalRadius, centerY);
        var segmentAngle = (EndAngle - StartAngle) / ArcSegments;
        var startDirection = (startTip - GetArcPoint(
            StartAngle + segmentAngle,
            radius,
            verticalRadius,
            centerY)).normalized;
        var endDirection = (endTip - GetArcPoint(
            EndAngle - segmentAngle,
            radius,
            verticalRadius,
            centerY)).normalized;
        var previous = startTip;
        for (var segment = 1; segment <= ArcSegments; segment++)
        {
            var angle = Mathf.Lerp(StartAngle, EndAngle, (float)segment / ArcSegments);
            var next = GetArcPoint(angle, radius, verticalRadius, centerY);
            AddLine(vertexHelper, previous, next, thickness);
            previous = next;
        }

        AddArrow(
            vertexHelper,
            startTip,
            startDirection,
            arrowLength,
            thickness);
        AddArrow(
            vertexHelper,
            endTip,
            endDirection,
            arrowLength,
            thickness);
    }

    private static Vector2 GetArcPoint(
        float angle,
        float radius,
        float verticalRadius,
        float centerY)
    {
        var radians = angle * Mathf.Deg2Rad;
        return new Vector2(
            Mathf.Cos(radians) * radius,
            centerY + Mathf.Sin(radians) * verticalRadius);
    }

    private void AddLine(VertexHelper vertexHelper, Vector2 start, Vector2 end, float thickness)
    {
        var normal = (end - start).normalized;
        normal = new Vector2(-normal.y, normal.x) * (thickness * 0.5f);
        AddQuad(
            vertexHelper,
            start + normal,
            start - normal,
            end - normal,
            end + normal);
        AddRoundCap(vertexHelper, start, thickness * 0.5f);
        AddRoundCap(vertexHelper, end, thickness * 0.5f);
    }

    private void AddRoundCap(VertexHelper vertexHelper, Vector2 center, float radius)
    {
        const int segments = 8;
        var centerIndex = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, center);
        var firstRingVertex = vertexHelper.currentVertCount;
        for (var segment = 0; segment < segments; segment++)
        {
            var angle = segment * (Mathf.PI * 2f / segments);
            AddVertex(
                vertexHelper,
                center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }

        for (var segment = 0; segment < segments; segment++)
        {
            vertexHelper.AddTriangle(
                centerIndex,
                firstRingVertex + segment,
                firstRingVertex + (segment + 1) % segments);
        }
    }

    private void AddArrow(
        VertexHelper vertexHelper,
        Vector2 tip,
        Vector2 direction,
        float arrowLength,
        float thickness)
    {
        var backward = -direction.normalized;
        var firstEnd = tip + Rotate(backward, ArrowHalfAngle) * arrowLength;
        var secondEnd = tip + Rotate(backward, -ArrowHalfAngle) * arrowLength;
        AddLine(vertexHelper, tip, firstEnd, thickness);
        AddLine(vertexHelper, tip, secondEnd, thickness);
    }

    private static Vector2 Rotate(Vector2 vector, float angle)
    {
        var radians = angle * Mathf.Deg2Rad;
        var sine = Mathf.Sin(radians);
        var cosine = Mathf.Cos(radians);
        return new Vector2(
            vector.x * cosine - vector.y * sine,
            vector.x * sine + vector.y * cosine);
    }

    private void AddQuad(
        VertexHelper vertexHelper,
        Vector2 topLeft,
        Vector2 bottomLeft,
        Vector2 bottomRight,
        Vector2 topRight)
    {
        var first = vertexHelper.currentVertCount;
        AddVertex(vertexHelper, topLeft);
        AddVertex(vertexHelper, bottomLeft);
        AddVertex(vertexHelper, bottomRight);
        AddVertex(vertexHelper, topRight);
        vertexHelper.AddTriangle(first, first + 1, first + 2);
        vertexHelper.AddTriangle(first + 2, first + 3, first);
    }

    private void AddVertex(VertexHelper vertexHelper, Vector2 position)
    {
        var vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertexHelper.AddVert(vertex);
    }
}
