using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class LiquidTransferMinigame : Minigame
{
    private const float HandScale = 0.85f;
    private const float ContainerScale = 4f;
    private const float ContainerGap = 40f;
    private const float ScreenPadding = 40f;
    private const float CollisionStep = 2f;
    private const float PivotSwitchHysteresis = 2f;

    private Image handImage;
    private HandAlphaGradient handAlphaGradient;
    private Vector2 originalHandSize;
    private bool originalPreserveAspect;
    private ContainerVisual receivingContainer;
    private ContainerVisual sourceContainer;
    private RectTransform heldContainer;
    private ContainerVisual heldVisual;
    private Vector2 heldOffset;
    private ContainerVisual activeRotationContainer;
    private bool rotatingWithRightMouse;
    private float rotationAngle;
    private Vector2 rotationStartPointer;
    private Vector2 rotationStartCenter;
    private Vector2 rotationPivotPosition;
    private float rotationLastPointerAngle;
    private int rotationVerticalSector;
    private bool rotationTrackingStarted;
    private float fitScale;

    public override HandSpriteType HandType()
    {
        return HandSpriteType.Grasp;
    }

    public override float HandRotOffset()
    {
        return Minigame.game.handPos.x * 0.02f;
    }

    public override string GuideLocaleString()
    {
        return string.Empty;
    }

    public override bool NeedsItem()
    {
        return false;
    }

    public override bool CanExit()
    {
        return false;
    }

    public override void Start()
    {
        var game = Minigame.game;
        var screenRoot = new GameObject("RealisticLiquidTransferScreen", typeof(RectTransform))
            .GetComponent<RectTransform>();
        screenRoot.SetParent(game.minigameScreen, false);
        screenRoot.anchorMin = new Vector2(0.5f, 0.5f);
        screenRoot.anchorMax = new Vector2(0.5f, 0.5f);
        screenRoot.pivot = new Vector2(0.5f, 0.5f);
        screenRoot.sizeDelta = game.minigameScreen.rect.size;
        screenRoot.SetSiblingIndex(0);
        game.spawnedMiniGame = screenRoot;
        CreateControlsText(game.minigameScreen);

        receivingContainer = CreateContainer(
            screenRoot,
            "ReceivingContainer",
            ModSprites.ReceivingContainer,
            Vector2.zero,
            0f);
        sourceContainer = CreateContainer(
            screenRoot,
            "SourceContainer",
            ModSprites.SourceContainer,
            Vector2.zero,
            0f);
        PositionContainersSideBySide(receivingContainer, sourceContainer);

        handImage = game.handTransform.GetComponent<Image>();
        originalHandSize = handImage.rectTransform.sizeDelta;
        originalPreserveAspect = handImage.preserveAspect;

        fitScale = GetFitScale(game.minigameScreen, screenRoot, receivingContainer, sourceContainer);
        screenRoot.localScale = Vector3.one * fitScale;

        handImage.preserveAspect = true;
        handImage.rectTransform.sizeDelta = originalHandSize * (HandScale * fitScale);
        handAlphaGradient = handImage.gameObject.AddComponent<HandAlphaGradient>();
    }

    private static void CreateControlsText(RectTransform parent)
    {
        var textObject = new GameObject("LiquidTransferControls", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var controls = textObject.GetComponent<Text>();
        var rect = controls.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 20f);
        rect.sizeDelta = new Vector2(parent.rect.width - 80f, 32f);

        controls.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        controls.fontSize = 18;
        controls.alignment = TextAnchor.MiddleCenter;
        controls.text = "Left-click: move   Right-click: rotate   Esc: exit";
        controls.color = Color.white;
        controls.raycastTarget = false;
    }

    public override void Update(List<RaycastResult> uiCasts)
    {
        var game = Minigame.game;
        if (game.handStartedClicking)
        {
            heldContainer = null;
            heldVisual = null;
            activeRotationContainer = null;
            rotatingWithRightMouse = false;
            var container = GetContainerUnderPointer(uiCasts);
            if (container != null)
            {
                StartDragging(container, game.handPos);
            }
        }

        if (Input.GetMouseButtonDown(1))
        {
            var container = GetContainerUnderPointer(uiCasts);
            if (container != null)
            {
                heldContainer = null;
                heldVisual = null;
                StartRotating(container, game.handPos);
                rotatingWithRightMouse = true;
            }
        }

        if (Input.GetMouseButtonUp(1) && rotatingWithRightMouse)
        {
            activeRotationContainer = null;
            rotatingWithRightMouse = false;
        }

        if (game.handStoppedClicking && !rotatingWithRightMouse)
        {
            heldContainer = null;
            heldVisual = null;
            activeRotationContainer = null;
        }

        if (activeRotationContainer != null
            && rotatingWithRightMouse
            && Input.GetMouseButton(1))
        {
            RotateContainer(game.handPos);
        }
        else if (heldContainer && game.handClicking)
        {
            var previousPosition = heldContainer.anchoredPosition;
            heldContainer.anchoredPosition = game.handPos / fitScale + heldOffset;
            var targetPosition = heldContainer.anchoredPosition;
            heldContainer.anchoredPosition = previousPosition;
            MoveContainer(heldVisual, targetPosition);
        }
    }

    private ContainerVisual GetContainerUnderPointer(List<RaycastResult> uiCasts)
    {
        foreach (var uiCast in uiCasts)
        {
            if (uiCast.gameObject == receivingContainer.Transform.gameObject)
            {
                return receivingContainer;
            }

            if (uiCast.gameObject == sourceContainer.Transform.gameObject)
            {
                return sourceContainer;
            }
        }

        return null;
    }

    internal void RestoreHand()
    {
        if (!handImage)
        {
            return;
        }

        if (handAlphaGradient)
        {
            handAlphaGradient.enabled = false;
            handImage.SetVerticesDirty();
            Object.Destroy(handAlphaGradient);
            handAlphaGradient = null;
        }

        handImage.rectTransform.sizeDelta = originalHandSize;
        handImage.preserveAspect = originalPreserveAspect;
        handImage.SetVerticesDirty();
        Minigame.game.UpdateHandSprite(reset: true);
    }

    private static ContainerVisual CreateContainer(
        RectTransform parent,
        string name,
        Sprite sprite,
        Vector2 position,
        float rotation)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
            .GetComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        rect.localScale = Vector3.one * ContainerScale;

        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = true;

        var hasOpening = ModSprites.TryFindOpening(sprite, out var openingPosition, out var openingWidth);
        var openingLeft = Vector2.zero;
        var openingRight = Vector2.zero;
        var outlinePoints = System.Array.Empty<Vector2>();
        if (!hasOpening)
        {
            Plugin.LogSource.LogWarning($"Could not detect the open rim of container sprite '{sprite.name}'.");
        }
        else
        {
            openingLeft = openingPosition - new Vector2(openingWidth * 0.5f, 0f);
            openingRight = openingPosition + new Vector2(openingWidth * 0.5f, 0f);
            if (ModSprites.TryFindOutline(sprite, openingPosition, openingWidth, out outlinePoints))
            {
                var outline = new GameObject(
                    $"{name}OutlineDebugLine",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(DebugPolylineGraphic)).GetComponent<DebugPolylineGraphic>();
                var outlineRect = outline.rectTransform;
                outlineRect.SetParent(rect, false);
                outlineRect.anchorMin = new Vector2(0.5f, 0.5f);
                outlineRect.anchorMax = new Vector2(0.5f, 0.5f);
                outlineRect.pivot = new Vector2(0.5f, 0.5f);
                outlineRect.sizeDelta = rect.sizeDelta;
                outline.color = Color.blue;
                outline.raycastTarget = false;
                outline.SetPoints(outlinePoints);
            }
            else
            {
                Plugin.LogSource.LogWarning($"Could not trace the outline of container sprite '{sprite.name}'.");
            }

            var debugLine = new GameObject($"{name}OpeningDebugLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                .GetComponent<Image>();
            var lineRect = debugLine.rectTransform;
            lineRect.SetParent(rect, false);
            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0.5f, 0.5f);
            lineRect.anchoredPosition = openingPosition;
            lineRect.sizeDelta = new Vector2(openingWidth, 3f);
            debugLine.color = Color.red;
            debugLine.raycastTarget = false;
        }

        return new ContainerVisual(
            rect,
            openingPosition,
            openingLeft,
            openingRight,
            hasOpening,
            outlinePoints);
    }

    private void StartDragging(ContainerVisual container, Vector2 handPosition)
    {
        heldVisual = container;
        heldContainer = container.Transform;
        heldOffset = container.Transform.anchoredPosition - handPosition / fitScale;
    }

    private void StartRotating(ContainerVisual container, Vector2 handPosition)
    {
        activeRotationContainer = container;
        rotationAngle = container.Transform.localEulerAngles.z;
        rotationStartPointer = handPosition / fitScale;
        rotationStartCenter = container.Transform.anchoredPosition;
        rotationTrackingStarted = false;
        rotationVerticalSector = Mathf.FloorToInt(rotationAngle / 180f);

        if (container.HasRotationPivot && !IsAtVerticalPosition(rotationAngle))
        {
            StartPivotTracking(rotationStartPointer);
        }
    }

    private void RotateContainer(Vector2 handPosition)
    {
        var pointer = handPosition / fitScale;
        if (!rotationTrackingStarted)
        {
            var startVector = rotationStartPointer - rotationStartCenter;
            var currentVector = pointer - rotationStartCenter;
            var initialDirection = Vector2.SignedAngle(startVector, currentVector);
            if (Mathf.Abs(initialDirection) < 0.1f)
            {
                return;
            }

            activeRotationContainer.PivotIsRight = initialDirection < 0f;
            activeRotationContainer.HasRotationPivot = true;
            if (IsAtVerticalPosition(rotationAngle))
            {
                var verticalIndex = Mathf.RoundToInt(rotationAngle / 180f);
                rotationVerticalSector = initialDirection < 0f
                    ? verticalIndex - 1
                    : verticalIndex;
            }

            StartPivotTracking(rotationStartPointer);
        }

        var pointerVector = pointer - rotationPivotPosition;
        if (pointerVector.sqrMagnitude < 0.01f)
        {
            return;
        }

        var pointerAngle = Mathf.Atan2(pointerVector.y, pointerVector.x) * Mathf.Rad2Deg;
        var deltaAngle = Mathf.DeltaAngle(rotationLastPointerAngle, pointerAngle);
        rotationLastPointerAngle = pointerAngle;
        if (Mathf.Abs(deltaAngle) < 0.01f)
        {
            return;
        }

        var visual = activeRotationContainer;
        var pivotPoint = visual.PivotIsRight ? visual.RightOpeningPoint : visual.LeftOpeningPoint;
        var maxPivotDistance = 0f;
        foreach (var point in visual.OutlinePoints)
        {
            maxPivotDistance = Mathf.Max(
                maxPivotDistance,
                Vector2.Distance(point, pivotPoint) * visual.Transform.localScale.x);
        }

        var arcDistance = maxPivotDistance * Mathf.Abs(deltaAngle) * Mathf.Deg2Rad;
        var steps = Mathf.Max(
            Mathf.CeilToInt(Mathf.Abs(deltaAngle) / 2f),
            Mathf.CeilToInt(arcDistance / CollisionStep));
        var angleStep = deltaAngle / Mathf.Max(1, steps);
        for (var step = 0; step < steps; step++)
        {
            var previousAngle = rotationAngle;
            var candidateAngle = previousAngle + angleStep;
            ApplyRotationPose(visual, candidateAngle);
            if (WouldOverlap(visual))
            {
                ApplyRotationPose(visual, previousAngle);
                break;
            }

            rotationAngle = candidateAngle;
            if (TrySwitchRotationPivot(visual, previousAngle, rotationAngle, pointer))
            {
                break;
            }
        }

    }

    private void MoveContainer(ContainerVisual visual, Vector2 targetPosition)
    {
        var transform = visual.Transform;
        var startPosition = transform.anchoredPosition;
        var distance = Vector2.Distance(startPosition, targetPosition);
        var steps = Mathf.Max(1, Mathf.CeilToInt(distance / CollisionStep));
        for (var step = 1; step <= steps; step++)
        {
            var candidatePosition = Vector2.Lerp(startPosition, targetPosition, (float)step / steps);
            transform.anchoredPosition = candidatePosition;
            if (WouldOverlap(visual))
            {
                transform.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, (float)(step - 1) / steps);
                break;
            }
        }
    }

    private void ApplyRotationPose(ContainerVisual visual, float angle)
    {
        var pivotPoint = visual.PivotIsRight ? visual.RightOpeningPoint : visual.LeftOpeningPoint;
        visual.Transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        visual.Transform.anchoredPosition = rotationPivotPosition
            - TransformOpeningPoint(visual.Transform, pivotPoint);
    }

    private bool WouldOverlap(ContainerVisual moving)
    {
        var other = moving == receivingContainer ? sourceContainer : receivingContainer;
        var movingTransform = moving.Transform;
        var otherTransform = other.Transform;
        return ContainerShapeCollision.Overlaps(
            moving.OutlinePoints,
            movingTransform.anchoredPosition,
            movingTransform.localScale.x,
            movingTransform.localEulerAngles.z,
            other.OutlinePoints,
            otherTransform.anchoredPosition,
            otherTransform.localScale.x,
            otherTransform.localEulerAngles.z);
    }

    private void StartPivotTracking(Vector2 pointer)
    {
        var visual = activeRotationContainer;
        var pivotPoint = visual.PivotIsRight ? visual.RightOpeningPoint : visual.LeftOpeningPoint;
        rotationPivotPosition = visual.Transform.anchoredPosition
            + TransformOpeningPoint(visual.Transform, pivotPoint);
        var pointerVector = pointer - rotationPivotPosition;
        rotationLastPointerAngle = Mathf.Atan2(pointerVector.y, pointerVector.x) * Mathf.Rad2Deg;
        rotationTrackingStarted = true;
    }

    private bool TrySwitchRotationPivot(
        ContainerVisual visual,
        float previousAngle,
        float currentAngle,
        Vector2 pointer)
    {
        var crossed = false;
        if (currentAngle > previousAngle)
        {
            var boundary = (rotationVerticalSector + 1) * 180f;
            if (currentAngle >= boundary + PivotSwitchHysteresis)
            {
                rotationVerticalSector++;
                crossed = true;
            }
        }
        else if (currentAngle < previousAngle)
        {
            var boundary = rotationVerticalSector * 180f;
            if (currentAngle <= boundary - PivotSwitchHysteresis)
            {
                rotationVerticalSector--;
                crossed = true;
            }
        }

        if (!crossed)
        {
            return false;
        }

        visual.PivotIsRight = !visual.PivotIsRight;
        var pivotPoint = visual.PivotIsRight ? visual.RightOpeningPoint : visual.LeftOpeningPoint;
        rotationPivotPosition = visual.Transform.anchoredPosition
            + TransformOpeningPoint(visual.Transform, pivotPoint);
        var rebasedPointer = pointer - rotationPivotPosition;
        rotationLastPointerAngle = Mathf.Atan2(rebasedPointer.y, rebasedPointer.x) * Mathf.Rad2Deg;
        return true;
    }

    private static bool IsAtVerticalPosition(float angle)
    {
        var nearestVerticalAngle = Mathf.Round(angle / 180f) * 180f;
        return Mathf.Abs(Mathf.DeltaAngle(angle, nearestVerticalAngle)) <= PivotSwitchHysteresis;
    }

    private static void PositionContainersSideBySide(
        ContainerVisual receiver,
        ContainerVisual source)
    {
        var receiverWidth = receiver.Transform.rect.width * receiver.Transform.localScale.x;
        var sourceWidth = source.Transform.rect.width * source.Transform.localScale.x;
        var totalWidth = receiverWidth + ContainerGap + sourceWidth;
        var receiverX = -totalWidth * 0.5f + receiverWidth * 0.5f;
        var sourceX = receiverX + receiverWidth * 0.5f + ContainerGap + sourceWidth * 0.5f;
        var receiverOpeningOffset = TransformOpeningPoint(receiver.Transform, receiver.OpeningCenterPoint);
        var sourceOpeningOffset = TransformOpeningPoint(source.Transform, source.OpeningCenterPoint);

        receiver.Transform.anchoredPosition = new Vector2(receiverX, -receiverOpeningOffset.y);
        source.Transform.anchoredPosition = new Vector2(sourceX, -sourceOpeningOffset.y);
    }

    private static float GetFitScale(
        RectTransform screen,
        RectTransform layoutRoot,
        params ContainerVisual[] containers)
    {
        var available = screen.rect.size * 0.5f - Vector2.one * ScreenPadding;
        var maxX = 0f;
        var maxY = 0f;
        foreach (var container in containers)
        {
            IncludeExtent(layoutRoot, container.Transform, ref maxX, ref maxY);
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
        IncludeBounds(reference, target, ref minX, ref maxTargetX, ref minY, ref maxTargetY);
        maxX = Mathf.Max(maxX, Mathf.Abs(minX), Mathf.Abs(maxTargetX));
        maxY = Mathf.Max(maxY, Mathf.Abs(minY), Mathf.Abs(maxTargetY));
    }

    private static void IncludeBounds(
        RectTransform reference,
        RectTransform target,
        ref float minX,
        ref float maxX,
        ref float minY,
        ref float maxY)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        foreach (var corner in corners)
        {
            var point = reference.InverseTransformPoint(corner);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }
    }

    private static Vector2 TransformOpeningPoint(RectTransform container, Vector2 point)
    {
        var transformed = container.localRotation * Vector3.Scale(point, container.localScale);
        return new Vector2(transformed.x, transformed.y);
    }

    private sealed class ContainerVisual
    {
        internal ContainerVisual(
            RectTransform transform,
            Vector2 openingCenterPoint,
            Vector2 leftOpeningPoint,
            Vector2 rightOpeningPoint,
            bool hasOpening,
            Vector2[] outlinePoints)
        {
            Transform = transform;
            OpeningCenterPoint = openingCenterPoint;
            LeftOpeningPoint = leftOpeningPoint;
            RightOpeningPoint = rightOpeningPoint;
            HasOpening = hasOpening;
            OutlinePoints = outlinePoints;
        }

        internal RectTransform Transform { get; }
        internal Vector2 OpeningCenterPoint { get; }
        internal Vector2 LeftOpeningPoint { get; }
        internal Vector2 RightOpeningPoint { get; }
        internal bool HasOpening { get; }
        internal Vector2[] OutlinePoints { get; }
        internal bool HasRotationPivot { get; set; }
        internal bool PivotIsRight { get; set; }
    }
}
