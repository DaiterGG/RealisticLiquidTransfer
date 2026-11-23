using UnityEngine;

namespace RealisticLiquidTransfer;

internal sealed class ContainerBoundaryData
{
    internal ContainerBoundaryData(
        bool hasOpening,
        float openingX,
        float openingY,
        float openingWidth,
        Vector2[] wallCenterlinePoints,
        Vector2[][] interiorWalls,
        Vector2[] collisionPoints)
    {
        HasOpening = hasOpening;
        OpeningPosition = new Vector2(openingX, openingY);
        OpeningWidth = openingWidth;
        WallCenterlinePoints = wallCenterlinePoints;
        InteriorWalls = interiorWalls;
        CollisionPoints = collisionPoints;
    }

    internal bool HasOpening { get; }
    internal Vector2 OpeningPosition { get; }
    internal float OpeningWidth { get; }
    internal Vector2[] WallCenterlinePoints { get; }
    internal Vector2[][] InteriorWalls { get; }
    internal Vector2[] CollisionPoints { get; }
}
