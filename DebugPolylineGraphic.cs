using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class DebugPolylineGraphic : MaskableGraphic
{
    private const float LineThickness = 1.5f;
    private static readonly Vector2[] NoPoints = new Vector2[0];
    private Vector2[] points = NoPoints;

    internal void SetPoints(Vector2[] pathPoints)
    {
        points = pathPoints ?? NoPoints;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (points.Length < 2)
        {
            return;
        }

        var halfThickness = LineThickness * 0.5f;
        for (var i = 1; i < points.Length; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            var direction = (end - start).normalized;
            var normal = new Vector2(-direction.y, direction.x) * halfThickness;
            var firstVertex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, start + normal);
            AddVertex(vertexHelper, start - normal);
            AddVertex(vertexHelper, end - normal);
            AddVertex(vertexHelper, end + normal);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
        }

        for (var i = 0; i < points.Length; i++)
        {
            AddRoundJoin(vertexHelper, points[i], halfThickness);
        }
    }

    private void AddVertex(VertexHelper vertexHelper, Vector2 position)
    {
        var vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = position;
        vertexHelper.AddVert(vertex);
    }

    private void AddRoundJoin(VertexHelper vertexHelper, Vector2 center, float radius)
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
            var next = (segment + 1) % segments;
            vertexHelper.AddTriangle(centerIndex, firstRingVertex + segment, firstRingVertex + next);
        }
    }
}
