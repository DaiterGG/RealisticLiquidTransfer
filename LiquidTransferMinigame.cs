using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticLiquidTransfer;

internal sealed class LiquidTransferMinigame : Minigame
{
    private const float HandScale = 0.85f;
    private const float CollisionStep = 2f;
    private const float PivotSwitchHysteresis = 2f;
    private const float RotationMovementInfluence = 1f;
    private const float TranslationHorizontalInfluence = 1f;
    private const float TranslationVerticalInfluence = 1f;
    private const float SyringeFreePistonSpeed = 420f;
    private const float SyringeVolumeTransferRate = 24f;
    private const float SyringeEjectionSpeed = 1000f;
    private const float SyringeIntakeQueueLifetime = 0.5f;
    private const string ReferenceSpriteName = "coffee";
    private const float ReferenceCapacity = 250f;
    private static float referenceCapacity = ReferenceCapacity;

    private const bool SimulationFrozen = false;

    private readonly WaterContainerItem receivingGameContainer;
    private readonly WaterContainerItem sourceGameContainer;
    private readonly string firstOriginalContainerSpriteName;
    private readonly string secondOriginalContainerSpriteName;
    private readonly float firstOriginalContainerCapacity;
    private readonly float secondOriginalContainerCapacity;
    private readonly float firstOriginalContainerAmount;
    private readonly float secondOriginalContainerAmount;
    private readonly LiquidProfileEntry[] firstOriginalLiquidComposition;
    private readonly LiquidProfileEntry[] secondOriginalLiquidComposition;
    private readonly List<float> receivingDrainPlan;
    private readonly List<float> sourceDrainPlan;
    private readonly bool drainOnly;
    private readonly ushort receivingSyncId;
    private readonly byte receivingSyncVersion;
    private readonly ushort sourceSyncId;
    private readonly byte sourceSyncVersion;
    private Image handImage;
    private Image leftFunnelSide;
    private Image rightFunnelSide;
    private Image[] funnelDebugLines;
    private Text controlsText;
    private RectTransform screenRoot;
    private RectTransform overlayRoot;
    private LiquidSimulation liquidSimulation;
    private HandAlphaGradient handAlphaGradient;
    private Vector2 originalHandSize;
    private bool originalPreserveAspect;
    private ContainerVisual receivingContainer;
    private ContainerVisual sourceContainer;
    private RectTransform heldContainer;
    private ContainerVisual heldVisual;
    private Vector2 heldOffset;
    private bool draggingFunnel;
    private bool funnelPositionInitialized;
    private Vector2 funnelPositionOffset;
    private Vector2 funnelPointerOffset;
    private Vector2 funnelBaseOpeningCenter;
    private float funnelOpeningWidth;
    private SyringeVisual receivingSyringe;
    private SyringeVisual sourceSyringe;
    private SyringeVisual activeSyringe;
    private float syringeHandlePointerOffset;
    private ContainerVisual activeRotationContainer;
    private bool rotatingWithRightMouse;
    private float rotationAngle;
    private Vector2 rotationStartPointer;
    private Vector2 rotationStartCenter;
    private Vector2 rotationPivotPosition;
    private float rotationLastPointerAngle;
    private int rotationVerticalSector;
    private bool rotationTrackingStarted;
    private bool transferCommitted;
    private readonly bool hasFunnel;
    private Vector2[] funnelWallPoints;
    private bool warnedMissingFunnelOpening;
    private float fitScale;
    private float[] initialSimulatedLiquidAmounts;
    private float[] finalSimulatedLiquidAmounts;
    private float[] targetLiquidAmounts;
    private float[] unreturnedLiquidByType;

    internal LiquidTransferMinigame(
        WaterContainerItem receivingGameContainer,
        string firstOriginalContainerSpriteName,
        float firstOriginalContainerCapacity,
        float firstOriginalContainerAmount,
        LiquidProfileEntry[] firstOriginalLiquidComposition,
        WaterContainerItem sourceGameContainer,
        string secondOriginalContainerSpriteName,
        float secondOriginalContainerCapacity,
        float secondOriginalContainerAmount,
        LiquidProfileEntry[] secondOriginalLiquidComposition,
        bool hasFunnel = false)
    {
        this.receivingGameContainer = receivingGameContainer;
        this.sourceGameContainer = sourceGameContainer;
        this.hasFunnel = hasFunnel;
        receivingDrainPlan = CreateDrainPlan(receivingGameContainer);
        sourceDrainPlan = CreateDrainPlan(sourceGameContainer);
        this.firstOriginalContainerSpriteName = firstOriginalContainerSpriteName;
        this.firstOriginalContainerCapacity = firstOriginalContainerCapacity;
        this.firstOriginalContainerAmount = firstOriginalContainerAmount;
        this.firstOriginalLiquidComposition = firstOriginalLiquidComposition
            ?? System.Array.Empty<LiquidProfileEntry>();
        this.secondOriginalContainerSpriteName = secondOriginalContainerSpriteName;
        this.secondOriginalContainerCapacity = secondOriginalContainerCapacity;
        this.secondOriginalContainerAmount = secondOriginalContainerAmount;
        this.secondOriginalLiquidComposition = secondOriginalLiquidComposition
            ?? System.Array.Empty<LiquidProfileEntry>();
        if (MultiplayerTransferBridge.GetRoute() == TransferCommitRoute.Client)
        {
            if (MultiplayerTransferBridge.TryCaptureClientSyncIdentity(
                    receivingGameContainer,
                    out var capturedReceiverSyncId,
                    out var capturedReceiverSyncVersion))
            {
                receivingSyncId = capturedReceiverSyncId;
                receivingSyncVersion = capturedReceiverSyncVersion;
            }

            if (MultiplayerTransferBridge.TryCaptureClientSyncIdentity(
                    sourceGameContainer,
                    out var capturedSourceSyncId,
                    out var capturedSourceSyncVersion))
            {
                sourceSyncId = capturedSourceSyncId;
                sourceSyncVersion = capturedSourceSyncVersion;
            }
        }

        if (string.Equals(
                firstOriginalContainerSpriteName,
                ReferenceSpriteName,
                System.StringComparison.OrdinalIgnoreCase)
            && firstOriginalContainerCapacity > 0f)
        {
            referenceCapacity = firstOriginalContainerCapacity;
        }

        if (string.Equals(
                secondOriginalContainerSpriteName,
                ReferenceSpriteName,
                System.StringComparison.OrdinalIgnoreCase)
            && secondOriginalContainerCapacity > 0f)
        {
            referenceCapacity = secondOriginalContainerCapacity;
        }
    }

    internal LiquidTransferMinigame(
        WaterContainerItem container,
        string originalContainerSpriteName,
        float originalContainerCapacity,
        float originalContainerAmount,
        LiquidProfileEntry[] originalLiquidComposition)
    {
        receivingGameContainer = container;
        sourceGameContainer = null;
        receivingDrainPlan = CreateDrainPlan(container);
        sourceDrainPlan = CreateDrainPlan(null);
        firstOriginalContainerSpriteName = originalContainerSpriteName;
        secondOriginalContainerSpriteName = originalContainerSpriteName;
        firstOriginalContainerCapacity = originalContainerCapacity;
        secondOriginalContainerCapacity = originalContainerCapacity;
        firstOriginalContainerAmount = originalContainerAmount;
        secondOriginalContainerAmount = 0f;
        firstOriginalLiquidComposition = originalLiquidComposition
            ?? System.Array.Empty<LiquidProfileEntry>();
        secondOriginalLiquidComposition = System.Array.Empty<LiquidProfileEntry>();
        drainOnly = true;
        if (MultiplayerTransferBridge.GetRoute() == TransferCommitRoute.Client
            && MultiplayerTransferBridge.TryCaptureClientSyncIdentity(
                receivingGameContainer,
                out var capturedSyncId,
                out var capturedSyncVersion))
        {
            receivingSyncId = capturedSyncId;
            receivingSyncVersion = capturedSyncVersion;
        }

        if (string.Equals(
                originalContainerSpriteName,
                ReferenceSpriteName,
                System.StringComparison.OrdinalIgnoreCase)
            && originalContainerCapacity > 0f)
        {
            referenceCapacity = originalContainerCapacity;
        }
    }

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
        screenRoot = new GameObject("RealisticLiquidTransferScreen", typeof(RectTransform))
            .GetComponent<RectTransform>();
        screenRoot.SetParent(game.minigameScreen, false);
        screenRoot.anchorMin = new Vector2(0.5f, 0.5f);
        screenRoot.anchorMax = new Vector2(0.5f, 0.5f);
        screenRoot.pivot = new Vector2(0.5f, 0.5f);
        screenRoot.sizeDelta = game.minigameScreen.rect.size;
        screenRoot.SetSiblingIndex(0);
        game.spawnedMiniGame = screenRoot;
        overlayRoot = CreateOverlayRoot(game.minigameScreen);
        controlsText = CreateControlsText(overlayRoot);
        if (drainOnly)
        {
            controlsText.text = "Drag the container to pour | Right-click and drag to rotate | Esc: finish";
        }
        if (receivingSyringe != null || sourceSyringe != null)
        {
            controlsText.text =
                "Drag the syringe plunger up to draw liquid and down to inject through the needle | Esc: finish";
        }

        receivingContainer = CreateContainer(
            screenRoot,
            "ReceivingContainer",
            ModSprites.GetContainer(firstOriginalContainerSpriteName, firstOriginalContainerCapacity),
            Vector2.zero,
            0f,
            IsSyringeSprite(firstOriginalContainerSpriteName));
        sourceContainer = CreateContainer(
            screenRoot,
            "SourceContainer",
            ModSprites.GetContainer(secondOriginalContainerSpriteName, secondOriginalContainerCapacity),
            Vector2.zero,
            0f,
            IsSyringeSprite(secondOriginalContainerSpriteName));
        var referenceArea = EstimateSpriteArea(ModSprites.GetContainer(ReferenceSpriteName));
        if (!receivingContainer.IsSyringe)
        {
            ApplyAreaScale(
                receivingContainer,
                firstOriginalContainerCapacity,
                referenceArea,
                firstOriginalContainerSpriteName);
        }

        if (!sourceContainer.IsSyringe)
        {
            ApplyAreaScale(
                sourceContainer,
                secondOriginalContainerCapacity,
                referenceArea,
                secondOriginalContainerSpriteName);
        }
        if (drainOnly)
        {
            receivingContainer.Transform.anchoredPosition = -LiquidContainerLayout.TransformOpeningPoint(
                receivingContainer.Transform,
                receivingContainer.OpeningCenterPoint);
            sourceContainer.Transform.anchoredPosition = Vector2.one * 100000f;
            sourceContainer.Transform.gameObject.SetActive(false);
        }
        else
        {
            LiquidContainerLayout.PositionContainersSideBySide(
                receivingContainer.Transform,
                receivingContainer.OpeningCenterPoint,
                sourceContainer.Transform,
                sourceContainer.OpeningCenterPoint);
        }

        handImage = game.handTransform.GetComponent<Image>();
        originalHandSize = handImage.rectTransform.sizeDelta;
        originalPreserveAspect = handImage.preserveAspect;

        fitScale = drainOnly
            ? LiquidContainerLayout.GetFitScale(
                game.minigameScreen,
                screenRoot,
                receivingContainer.Transform)
            : LiquidContainerLayout.GetFitScale(
                game.minigameScreen,
                screenRoot,
                receivingContainer.Transform,
                sourceContainer.Transform);
        screenRoot.localScale = Vector3.one * fitScale;
        if (hasFunnel)
        {
            CreateFunnelVisuals();
            UpdateFunnelGeometry();
        }

        liquidSimulation = new LiquidSimulation(
            screenRoot,
            receivingContainer.Transform,
            receivingContainer.OutlinePoints,
            receivingContainer.InteriorWalls,
            firstOriginalContainerCapacity,
            sourceContainer.Transform,
            sourceContainer.OutlinePoints,
            sourceContainer.InteriorWalls,
            secondOriginalContainerCapacity,
            firstOriginalContainerAmount,
            secondOriginalContainerAmount,
            firstOriginalLiquidComposition,
            secondOriginalLiquidComposition,
            LiquidSimulation.DefaultPackingTenths / 100f,
            funnelWallPoints: funnelWallPoints);
        if (leftFunnelSide)
        {
            leftFunnelSide.transform.SetAsLastSibling();
            rightFunnelSide.transform.SetAsLastSibling();
        }
        var amountBufferLength = liquidSimulation.LiquidTypeBufferLength * 2;
        initialSimulatedLiquidAmounts = new float[amountBufferLength];
        finalSimulatedLiquidAmounts = new float[amountBufferLength];
        targetLiquidAmounts = new float[amountBufferLength];
        if (receivingContainer.IsSyringe)
        {
            receivingSyringe = CreateSyringeVisual(
                receivingContainer,
                0,
                firstOriginalContainerCapacity);
        }

        if (sourceContainer.IsSyringe)
        {
            sourceSyringe = CreateSyringeVisual(
                sourceContainer,
                1,
                secondOriginalContainerCapacity);
        }

        UpdateSyringeHandlePositions();
        liquidSimulation.ConfigureForMinigame(SimulationFrozen);
        DebugLog.Write(
            $"Minigame started: receiver outline points={receivingContainer.OutlinePoints.Length}, "
            + $"source outline points={sourceContainer.OutlinePoints.Length}, fitScale={fitScale:F3}, "
            + $"initial drops={liquidSimulation.Count} "
            + $"(receiver={liquidSimulation.GetContainerDropCount(0)}, "
            + $"source={liquidSimulation.GetContainerDropCount(1)}).");

        handImage.preserveAspect = true;
        handImage.rectTransform.sizeDelta = originalHandSize * (HandScale * fitScale);
        handAlphaGradient = handImage.gameObject.AddComponent<HandAlphaGradient>();
    }

    private static RectTransform CreateOverlayRoot(RectTransform parent)
    {
        var root = new GameObject("RealisticLiquidTransferOverlay", typeof(RectTransform))
            .GetComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = Vector2.zero;
        root.SetAsLastSibling();
        return root;
    }

    private static Text CreateControlsText(RectTransform parent)
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
        controls.fontSize = 16;
        controls.alignment = TextAnchor.MiddleCenter;
        controls.text = "Drag containers and funnel to pour | Right-click and drag to rotate | Esc: finish";
        controls.color = Color.white;
        controls.raycastTarget = false;
        return controls;
    }

    public override void Update(List<RaycastResult> uiCasts)
    {
        var game = Minigame.game;
        var pointerPosition = game.handPos / fitScale;
        if (game.handStartedClicking)
        {
            heldContainer = null;
            heldVisual = null;
            activeSyringe = null;
            activeRotationContainer = null;
            rotatingWithRightMouse = false;

            if (GetFunnelUnderPointer(uiCasts))
            {
                StartDraggingFunnel(pointerPosition);
            }
            else
            {
                draggingFunnel = false;
                var container = GetSyringeBodyUnderPointer(pointerPosition);
                if (container != null)
                {
                    StartDragging(container, game.handPos);
                }
                else
                {
                    activeSyringe = GetSyringeHandleUnderPointer(pointerPosition);
                    if (activeSyringe != null)
                    {
                        var localPointer = ToContainerLocalPoint(activeSyringe.Container, pointerPosition);
                        syringeHandlePointerOffset =
                            activeSyringe.HandleTransform.anchoredPosition.y - localPointer.y;
                    }
                    else
                    {
                        container = GetContainerUnderPointer(uiCasts);
                    }

                    if (container != null)
                    {
                        StartDragging(container, game.handPos);
                    }
                }
            }
        }

        if (Input.GetMouseButtonDown(1))
        {
            var container = GetSyringeBodyUnderPointer(pointerPosition)
                ?? GetContainerUnderPointer(uiCasts);
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
            draggingFunnel = false;
            if (activeSyringe != null)
            {
                activeSyringe.EjectionVolumeCredit = 0f;
                activeSyringe.IntakeVolumeCredit = 0f;
            }

            activeSyringe = null;
            activeRotationContainer = null;
        }

        if (activeRotationContainer != null
            && rotatingWithRightMouse
            && Input.GetMouseButton(1))
        {
            RotateContainer(game.handPos);
        }
        else if (draggingFunnel && game.handClicking)
        {
            funnelPositionOffset = pointerPosition + funnelPointerOffset - funnelBaseOpeningCenter;
        }
        else if (activeSyringe != null && game.handClicking)
        {
            UpdateSyringePlunger(activeSyringe, pointerPosition);
        }
        else if (heldContainer && game.handClicking)
        {
            var previousPosition = heldContainer.anchoredPosition;
            heldContainer.anchoredPosition = game.handPos / fitScale + heldOffset;
            var targetPosition = heldContainer.anchoredPosition;
            heldContainer.anchoredPosition = previousPosition;
            MoveContainer(heldVisual, targetPosition);
        }

        UpdateSyringeIntakeQueues();
        if (hasFunnel)
        {
            UpdateFunnelGeometry();
        }

        liquidSimulation.Tick(Time.unscaledDeltaTime, false, pointerPosition);
    }

    private void CreateFunnelVisuals()
    {
        var sprite = FunnelItem.TransferSprite;
        leftFunnelSide = CreateFunnelSide("FunnelLeftSide", sprite);
        rightFunnelSide = CreateFunnelSide("FunnelRightSide", sprite);
        funnelWallPoints = new Vector2[FunnelGeometry.WallPointCount];
        funnelDebugLines = new Image[FunnelGeometry.WallPointCount / 2];
        for (var index = 0; index < funnelDebugLines.Length; index++)
        {
            funnelDebugLines[index] = CreateFunnelDebugLine(index);
        }
    }

    private Image CreateFunnelSide(string name, Sprite sprite)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
            .GetComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(screenRoot, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = sprite.rect.size;
        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = true;
        return image;
    }

    private Image CreateFunnelDebugLine(int index)
    {
        var image = new GameObject(
                $"FunnelHitboxDebug{index}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image))
            .GetComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(screenRoot, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1f, 2f);
        image.color = Color.blue;
        image.raycastTarget = false;
        return image;
    }

    private void UpdateFunnelGeometry()
    {
        if (!receivingContainer.HasOpening && !warnedMissingFunnelOpening)
        {
            Plugin.LogWarning(
                "The receiving container has no detected opening; fitting the funnel to its upper rim.");
            warnedMissingFunnelOpening = true;
        }

        if (!funnelPositionInitialized)
        {
            var localCenter = receivingContainer.HasOpening
                ? receivingContainer.OpeningCenterPoint
                : new Vector2(0f, receivingContainer.Transform.rect.height * 0.5f);
            var localLeft = receivingContainer.HasOpening
                ? receivingContainer.LeftOpeningPoint
                : localCenter - new Vector2(receivingContainer.Transform.rect.width * 0.375f, 0f);
            var localRight = receivingContainer.HasOpening
                ? receivingContainer.RightOpeningPoint
                : localCenter + new Vector2(receivingContainer.Transform.rect.width * 0.375f, 0f);
            var openingCenterWorld = receivingContainer.Transform.TransformPoint(localCenter);
            var openingLeftWorld = receivingContainer.Transform.TransformPoint(localLeft);
            var openingRightWorld = receivingContainer.Transform.TransformPoint(localRight);
            funnelBaseOpeningCenter = (Vector2)screenRoot.InverseTransformPoint(openingCenterWorld);
            funnelOpeningWidth = Vector2.Distance(
                screenRoot.InverseTransformPoint(openingLeftWorld),
                screenRoot.InverseTransformPoint(openingRightWorld));
            funnelPositionOffset = Vector2.left * funnelOpeningWidth;
            funnelPositionInitialized = true;
        }

        FunnelGeometry.PositionSides(
            leftFunnelSide.rectTransform,
            rightFunnelSide.rectTransform,
            FunnelItem.TransferSprite.rect.size,
            funnelBaseOpeningCenter + funnelPositionOffset,
            funnelOpeningWidth);
        FunnelGeometry.WriteWallPoints(
            screenRoot,
            leftFunnelSide.rectTransform,
            rightFunnelSide.rectTransform,
            FunnelItem.TransferSprite.rect.size,
            funnelWallPoints);
        UpdateFunnelDebugLines();
    }

    private void UpdateFunnelDebugLines()
    {
        for (var index = 0; index < funnelDebugLines.Length; index++)
        {
            var start = funnelWallPoints[index * 2];
            var end = funnelWallPoints[index * 2 + 1];
            var direction = end - start;
            var rect = funnelDebugLines[index].rectTransform;
            rect.anchoredPosition = (start + end) * 0.5f;
            rect.sizeDelta = new Vector2(direction.magnitude, 2f);
            rect.localRotation = Quaternion.Euler(
                0f,
                0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }
    }

    private SyringeVisual CreateSyringeVisual(
        ContainerVisual container,
        int containerIndex,
        float capacity)
    {
        var sprite = ModSprites.GetContainer("syringehandle");
        var image = new GameObject(
            "SyringeHandle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)).GetComponent<Image>();
        var handle = image.rectTransform;
        handle.SetParent(container.Transform, false);
        handle.anchorMin = new Vector2(0.5f, 0.5f);
        handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0f);
        handle.anchoredPosition = new Vector2(0f, container.Geometry.FluidBottom.y);
        handle.sizeDelta = sprite.rect.size;
        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = false;
        handle.SetAsLastSibling();
        return new SyringeVisual(container, containerIndex, capacity, handle);
    }

    private void UpdateSyringeHandlePositions()
    {
        UpdateSyringeHandlePosition(receivingSyringe);
        UpdateSyringeHandlePosition(sourceSyringe);
    }

    private void UpdateSyringeHandlePosition(SyringeVisual syringe)
    {
        if (syringe == null)
        {
            return;
        }

        if (syringe == activeSyringe && Minigame.game.handClicking)
        {
            return;
        }

        var amount = liquidSimulation.GetContainerLiquidAmount(syringe.ContainerIndex);
        syringe.LastRequestedAmount = amount;
        var fillRatio = syringe.Capacity > 0f
            ? Mathf.Clamp01(amount / syringe.Capacity)
            : 0f;
        syringe.HandleTransform.anchoredPosition = new Vector2(
            0f,
            Mathf.Lerp(
                syringe.Container.Geometry.FluidBottom.y,
                syringe.Container.Geometry.FluidTop.y,
                fillRatio));
        liquidSimulation.SetSyringePistonPosition(
            syringe.ContainerIndex,
            syringe.HandleTransform.anchoredPosition.y);
    }

    private SyringeVisual GetSyringeHandleUnderPointer(Vector2 pointerPosition)
    {
        if (IsPointerOnSyringeHandle(receivingSyringe, pointerPosition))
        {
            return receivingSyringe;
        }

        return IsPointerOnSyringeHandle(sourceSyringe, pointerPosition)
            ? sourceSyringe
            : null;
    }

    private bool IsPointerOnSyringeHandle(SyringeVisual syringe, Vector2 pointerPosition)
    {
        if (syringe == null)
        {
            return false;
        }

        var localPointer = ToContainerLocalPoint(syringe.Container, pointerPosition);
        if (localPointer.y <= syringe.Container.Geometry.BodyHitbox.yMax)
        {
            return false;
        }

        var handlePoint = localPointer - syringe.HandleTransform.anchoredPosition;
        var hitbox = syringe.HandleTransform.rect;
        hitbox.xMin -= 8f;
        hitbox.xMax += 8f;
        hitbox.yMin -= 8f;
        hitbox.yMax += 8f;
        return hitbox.Contains(handlePoint);
    }

    private Vector2 ToContainerLocalPoint(ContainerVisual container, Vector2 rootPoint)
    {
        var local = Quaternion.Inverse(container.Transform.localRotation)
            * (rootPoint - container.Transform.anchoredPosition);
        var scale = container.Transform.localScale;
        return new Vector2(local.x / scale.x, local.y / scale.y);
    }

    private Vector2 ToRootPoint(ContainerVisual container, Vector2 localPoint)
    {
        var transformed = container.Transform.localRotation
            * Vector3.Scale(localPoint, container.Transform.localScale);
        return container.Transform.anchoredPosition + new Vector2(transformed.x, transformed.y);
    }

    private void UpdateSyringePlunger(SyringeVisual syringe, Vector2 pointerPosition)
    {
        var geometry = syringe.Container.Geometry;
        var pointerLocal = ToContainerLocalPoint(syringe.Container, pointerPosition);
        var requestedHandleY = Mathf.Clamp(
            pointerLocal.y + syringeHandlePointerOffset,
            geometry.FluidBottom.y,
            geometry.FluidTop.y);
        var currentHandleY = syringe.HandleTransform.anchoredPosition.y;
        var otherContainerIndex = drainOnly ? -1 : 1 - syringe.ContainerIndex;
        var needlePoint = ToRootPoint(syringe.Container, geometry.NeedlePoint);
        var localDropRadius = LiquidSimulation.DefaultDropRadius
            / Mathf.Max(Mathf.Abs(syringe.Container.Transform.localScale.y), 0.0001f);
        var outletPoint = ToRootPoint(
            syringe.Container,
            new Vector2(0f, geometry.FluidBottom.y + localDropRadius));
        var hasLiquidAtOutlet = liquidSimulation.HasContainerDropNear(
            syringe.ContainerIndex,
            outletPoint,
            LiquidSimulation.DefaultDropRadius * 2f);
        var deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        var movementDeltaTime = Mathf.Min(deltaTime, 1f / 30f);
        float handleY;
        if (requestedHandleY < currentHandleY)
        {
            var maximumDownwardSpeed = SyringeFreePistonSpeed;
            if (syringe.Capacity > 0f)
            {
                var strokeHeight = geometry.FluidTop.y - geometry.FluidBottom.y;
                maximumDownwardSpeed = Mathf.Min(
                    maximumDownwardSpeed,
                    strokeHeight * SyringeVolumeTransferRate / syringe.Capacity);
            }

            var maximumDownwardDistance = maximumDownwardSpeed * movementDeltaTime;
            handleY = Mathf.Max(requestedHandleY, currentHandleY - maximumDownwardDistance);
        }
        else if (requestedHandleY > currentHandleY)
        {
            var maximumUpwardSpeed = SyringeFreePistonSpeed;
            if (syringe.Capacity > 0f)
            {
                var strokeHeight = geometry.FluidTop.y - geometry.FluidBottom.y;
                maximumUpwardSpeed = Mathf.Min(
                    maximumUpwardSpeed,
                    strokeHeight * SyringeVolumeTransferRate / syringe.Capacity);
            }

            handleY = Mathf.Min(
                requestedHandleY,
                currentHandleY + maximumUpwardSpeed * movementDeltaTime);
        }
        else
        {
            handleY = currentHandleY;
        }

        syringe.HandleTransform.anchoredPosition = new Vector2(0f, handleY);
        liquidSimulation.SetSyringePistonPosition(syringe.ContainerIndex, handleY);
        var fillRatio = Mathf.InverseLerp(
            geometry.FluidBottom.y,
            geometry.FluidTop.y,
            handleY);
        var requestedAmount = fillRatio * syringe.Capacity;
        var requestedAmountDelta = requestedAmount - syringe.LastRequestedAmount;
        syringe.LastRequestedAmount = requestedAmount;
        if (requestedAmountDelta > 0f)
        {
            syringe.IntakeRequests.Add(
                new SyringeIntakeRequest(requestedAmountDelta, SyringeIntakeQueueLifetime));
        }
        else if (requestedAmountDelta < 0f)
        {
            CancelSyringeIntakeRequests(syringe, -requestedAmountDelta);
        }

        var currentAmount = liquidSimulation.GetContainerLiquidAmount(syringe.ContainerIndex);
        var difference = requestedAmount - currentAmount;
        var transferUnits = Mathf.FloorToInt(Mathf.Abs(difference) + 0.001f);
        var needleDirection3D = syringe.Container.Transform.localRotation
            * Vector3.Scale(Vector2.down, syringe.Container.Transform.localScale);
        var needleDirection = new Vector2(needleDirection3D.x, needleDirection3D.y).normalized;
        if (handleY <= geometry.FluidBottom.y + 0.01f)
        {
            syringe.EjectionVolumeCredit = 0f;
            syringe.IntakeVolumeCredit = 0f;
            syringe.IntakeRequests.Clear();
            var destinationIndex = otherContainerIndex >= 0
                && liquidSimulation.ContainsContainer(otherContainerIndex, needlePoint)
                    ? otherContainerIndex
                    : -1;
            var remainingUnits = liquidSimulation.GetContainerDropCount(syringe.ContainerIndex);
            while (remainingUnits > 0)
            {
                var moved = liquidSimulation.TryTransferNearestDrop(
                    syringe.ContainerIndex,
                    destinationIndex,
                    outletPoint,
                    float.PositiveInfinity,
                    needlePoint,
                    int.MaxValue,
                    needleDirection * SyringeEjectionSpeed,
                    destinationIndex >= 0);
                if (moved <= 0)
                {
                    break;
                }

                remainingUnits -= moved;
            }

            return;
        }

        if (difference < 0f)
        {
            syringe.IntakeVolumeCredit = 0f;
            if (hasLiquidAtOutlet)
            {
                syringe.EjectionVolumeCredit = Mathf.Min(
                    syringe.EjectionVolumeCredit + SyringeVolumeTransferRate * movementDeltaTime,
                    syringe.Capacity);
            }

            var ejectionUnits = Mathf.Min(
                transferUnits,
                Mathf.FloorToInt(syringe.EjectionVolumeCredit + 0.001f));
            var remainingUnits = ejectionUnits;
            var destinationIndex = otherContainerIndex >= 0
                && liquidSimulation.ContainsContainer(otherContainerIndex, needlePoint)
                    ? otherContainerIndex
                    : -1;
            while (remainingUnits > 0)
            {
                var moved = liquidSimulation.TryTransferNearestDrop(
                    syringe.ContainerIndex,
                    destinationIndex,
                    outletPoint,
                    LiquidSimulation.DefaultDropRadius * 2f,
                    needlePoint,
                    remainingUnits,
                    needleDirection * SyringeEjectionSpeed,
                    destinationIndex >= 0);
                if (moved <= 0)
                {
                    break;
                }

                remainingUnits -= moved;
                syringe.EjectionVolumeCredit = Mathf.Max(
                    0f,
                    syringe.EjectionVolumeCredit - moved);
            }
        }
        else
        {
            syringe.EjectionVolumeCredit = 0f;
        }
    }

    private void UpdateSyringeIntakeQueues()
    {
        var deltaTime = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
        var movementDeltaTime = Mathf.Min(deltaTime, 1f / 30f);
        UpdateSyringeIntakeQueue(receivingSyringe, deltaTime, movementDeltaTime);
        UpdateSyringeIntakeQueue(sourceSyringe, deltaTime, movementDeltaTime);
    }

    private void UpdateSyringeIntakeQueue(
        SyringeVisual syringe,
        float deltaTime,
        float movementDeltaTime)
    {
        if (syringe == null)
        {
            return;
        }

        var requests = syringe.IntakeRequests;
        var pendingVolume = 0f;
        var expiredAsAir = 0f;
        for (var index = requests.Count - 1; index >= 0; index--)
        {
            var request = requests[index];
            request.RemainingLifetime -= deltaTime;
            if (request.RemainingLifetime <= 0f || request.RemainingVolume <= 0f)
            {
                expiredAsAir += request.RemainingVolume;
                requests.RemoveAt(index);
            }
            else
            {
                pendingVolume += request.RemainingVolume;
            }
        }

        var geometry = syringe.Container.Geometry;
        var otherContainerIndex = drainOnly ? -1 : 1 - syringe.ContainerIndex;
        var needlePoint = ToRootPoint(syringe.Container, geometry.NeedlePoint);
        var maximumSourceDistance = LiquidSimulation.DefaultDropRadius * 3f;
        var sourceDropCount = -1;
        var sourceDropNearNeedle = false;
        if (DebugLog.IsFileEnabled && otherContainerIndex >= 0)
        {
            sourceDropCount = liquidSimulation.GetContainerDropCount(otherContainerIndex);
            sourceDropNearNeedle = liquidSimulation.HasContainerDropNear(
                otherContainerIndex,
                needlePoint,
                maximumSourceDistance);
        }

        var liquidTaken = 0;
        if (pendingVolume <= 0f)
        {
            syringe.IntakeVolumeCredit = 0f;
        }
        else
        {
            syringe.IntakeVolumeCredit = Mathf.Min(
                syringe.IntakeVolumeCredit + SyringeVolumeTransferRate * movementDeltaTime,
                Mathf.Min(syringe.Capacity, pendingVolume));
            var remainingUnits = Mathf.Min(
                Mathf.FloorToInt(pendingVolume + 0.001f),
                Mathf.FloorToInt(syringe.IntakeVolumeCredit + 0.001f));
            if (remainingUnits > 0)
            {
                var localDropRadius = LiquidSimulation.DefaultDropRadius
                    / Mathf.Max(Mathf.Abs(syringe.Container.Transform.localScale.y), 0.0001f);
                var liquidAmount = liquidSimulation.GetContainerLiquidAmount(syringe.ContainerIndex);
                var fillRatio = syringe.Capacity > 0f
                    ? Mathf.Clamp01(liquidAmount / syringe.Capacity)
                    : 0f;
                var surfaceY = Mathf.Lerp(
                    geometry.FluidBottom.y,
                    geometry.FluidTop.y,
                    fillRatio);
                var maximumY = syringe.HandleTransform.anchoredPosition.y - localDropRadius;
                var destinationY = Mathf.Clamp(
                    Mathf.Min(surfaceY + localDropRadius, maximumY),
                    geometry.FluidBottom.y + localDropRadius,
                    geometry.FluidTop.y - localDropRadius);
                var destinationPoint = ToRootPoint(
                    syringe.Container,
                    new Vector2(0f, destinationY));
                while (remainingUnits > 0)
                {
                    var moved = liquidSimulation.TryTransferNearestDrop(
                        otherContainerIndex,
                        syringe.ContainerIndex,
                        needlePoint,
                        maximumSourceDistance,
                        destinationPoint,
                        remainingUnits,
                        Vector2.zero);
                    if (moved <= 0)
                    {
                        break;
                    }

                    remainingUnits -= moved;
                    liquidTaken += moved;
                    pendingVolume = Mathf.Max(0f, pendingVolume - moved);
                    ConsumeSyringeIntakeRequests(syringe, moved);
                    syringe.IntakeVolumeCredit = Mathf.Max(
                        0f,
                        syringe.IntakeVolumeCredit - moved);
                }
            }
        }

        syringe.DiagnosticLiquidTaken += liquidTaken;
        syringe.DiagnosticAirExpired += expiredAsAir;
        if (DebugLog.IsFileEnabled && Time.unscaledTime >= syringe.NextDiagnosticLogTime)
        {
            var currentAmount = liquidSimulation.GetContainerLiquidAmount(syringe.ContainerIndex);
            var airGapVolume = Mathf.Max(
                0f,
                syringe.LastRequestedAmount - currentAmount - pendingVolume);
            DebugLog.Write(
                $"Syringe intake index={syringe.ContainerIndex}: capacity={syringe.Capacity:F1}, "
                + $"liquid={currentAmount:F1}, handleY={syringe.HandleTransform.anchoredPosition.y:F1}, "
                + $"requested={syringe.LastRequestedAmount:F1}, pending={pendingVolume:F1}, "
                + $"liquidTaken={syringe.DiagnosticLiquidTaken:F1}, "
                + $"airExpired={syringe.DiagnosticAirExpired:F1}, airGap={airGapVolume:F1}, "
                + $"sourceDrops={sourceDropCount}, sourceNearNeedle={sourceDropNearNeedle}, "
                + $"intakeCredit={syringe.IntakeVolumeCredit:F1}.");
            syringe.DiagnosticLiquidTaken = 0f;
            syringe.DiagnosticAirExpired = 0f;
            syringe.NextDiagnosticLogTime = Time.unscaledTime + 0.25f;
        }
    }

    private static void CancelSyringeIntakeRequests(SyringeVisual syringe, float volume)
    {
        var requests = syringe.IntakeRequests;
        for (var index = requests.Count - 1; index >= 0 && volume > 0f; index--)
        {
            var request = requests[index];
            var cancelledVolume = Mathf.Min(request.RemainingVolume, volume);
            request.RemainingVolume -= cancelledVolume;
            volume -= cancelledVolume;
            if (request.RemainingVolume <= 0f)
            {
                requests.RemoveAt(index);
            }
        }
    }

    private static void ConsumeSyringeIntakeRequests(SyringeVisual syringe, int volume)
    {
        var requests = syringe.IntakeRequests;
        var remainingVolume = (float)volume;
        while (requests.Count > 0 && remainingVolume > 0f)
        {
            var request = requests[0];
            var consumedVolume = Mathf.Min(request.RemainingVolume, remainingVolume);
            request.RemainingVolume -= consumedVolume;
            remainingVolume -= consumedVolume;
            if (request.RemainingVolume <= 0.001f)
            {
                requests.RemoveAt(0);
            }
        }
    }

    private static bool IsSyringeSprite(string spriteName)
    {
        return string.Equals(spriteName, "syringe", System.StringComparison.OrdinalIgnoreCase);
    }

    private static SyringeGeometry CreateSyringeGeometry(Sprite sprite)
    {
        Vector2 FromTop(float x, float y)
        {
            return new Vector2(
                x - sprite.pivot.x,
                sprite.rect.height - y - sprite.pivot.y);
        }

        var fluidTopLeft = FromTop(6f, 12f);
        var fluidTopRight = FromTop(24f, 12f);
        var fluidBottomRight = FromTop(24f, 83f);
        var fluidBottomLeft = FromTop(6f, 83f);
        var fluidOutline = new[]
        {
            fluidTopLeft,
            fluidTopRight,
            fluidBottomRight,
            fluidBottomLeft,
            fluidTopLeft
        };
        var bodyTopLeft = FromTop(2f, 8f);
        var bodyTopRight = FromTop(28f, 8f);
        var bodyBottomRight = FromTop(26f, 87f);
        var bodyBottomLeft = FromTop(4f, 87f);
        var collisionOutline = new[]
        {
            bodyTopLeft,
            bodyTopRight,
            bodyBottomRight,
            bodyBottomLeft,
            bodyTopLeft
        };
        var bodyHitbox = Rect.MinMaxRect(
            bodyTopLeft.x,
            bodyBottomLeft.y,
            bodyTopRight.x,
            bodyTopRight.y);
        return new SyringeGeometry(
            fluidOutline,
            collisionOutline,
            FromTop(15f, 9f),
            FromTop(3f, 9f),
            FromTop(27f, 9f),
            fluidBottomLeft.y,
            fluidTopLeft.y,
            bodyHitbox,
            FromTop(15f, 140f));
    }

    private static void ApplyAreaScale(
        ContainerVisual container,
        float targetCapacity,
        double referenceArea,
        string originalSpriteName)
    {
        var spriteArea = EstimateContainerArea(container.OutlinePoints, container.InteriorWalls);
        if (!LiquidContainerLayout.TryGetAreaScaledScale(
                targetCapacity,
                referenceCapacity,
                referenceArea,
                spriteArea,
                out var scale))
        {
            if (targetCapacity <= 0f || referenceCapacity <= 0f || referenceArea <= 0d || spriteArea <= 0d)
            {
                Plugin.LogWarning(
                    $"Could not area-scale container '{originalSpriteName}': capacity={targetCapacity:F2}, "
                    + $"reference area={referenceArea:F2}, sprite area={spriteArea:F2}; using base scale.");
            }
            else
            {
                Plugin.LogWarning($"Could not calculate a valid area scale for container '{originalSpriteName}'.");
            }

            return;
        }

        container.Transform.localScale = Vector3.one * scale;
    }

    private static double EstimateSpriteArea(Sprite sprite)
    {
        if (!sprite
            || !ModSprites.TryGetPrecomputedBoundary(sprite, out var boundary)
            || !boundary.HasOpening
            || boundary.WallCenterlinePoints.Length < 2)
        {
            Plugin.LogWarning($"Could not calculate the reference area from sprite '{ReferenceSpriteName}.png'.");
            return 0d;
        }

        return EstimateContainerArea(
            boundary.WallCenterlinePoints,
            boundary.InteriorWalls);
    }

    private static double EstimateContainerArea(Vector2[] outline, Vector2[][] interiorWalls)
    {
        var area = EstimateOutlineArea(outline);
        foreach (var wall in interiorWalls)
        {
            area -= EstimateOutlineArea(wall);
        }

        return System.Math.Max(0d, area);
    }

    private static double EstimateOutlineArea(Vector2[] outline)
    {
        if (outline.Length < 3)
        {
            return 0d;
        }

        var twiceArea = 0d;
        for (var i = 0; i < outline.Length; i++)
        {
            var current = outline[i];
            var next = outline[(i + 1) % outline.Length];
            twiceArea += (double)current.x * next.y - (double)next.x * current.y;
        }

        return System.Math.Abs(twiceArea) * 0.5d;
    }

    private bool GetFunnelUnderPointer(List<RaycastResult> uiCasts)
    {
        if (!hasFunnel)
        {
            return false;
        }

        foreach (var uiCast in uiCasts)
        {
            if (uiCast.gameObject == leftFunnelSide.gameObject
                || uiCast.gameObject == rightFunnelSide.gameObject)
            {
                return true;
            }
        }

        return false;
    }

    private void StartDraggingFunnel(Vector2 pointerPosition)
    {
        draggingFunnel = true;
        var funnelAnchor = funnelBaseOpeningCenter + funnelPositionOffset;
        funnelPointerOffset = funnelAnchor - pointerPosition;
    }

    private ContainerVisual GetContainerUnderPointer(List<RaycastResult> uiCasts)
    {
        var pointerPosition = Minigame.game.handPos / fitScale;
        foreach (var uiCast in uiCasts)
        {
            if (uiCast.gameObject == receivingContainer.Transform.gameObject
                && !receivingContainer.IsSyringe
                && liquidSimulation.ContainsContainer(0, pointerPosition))
            {
                return receivingContainer;
            }

            if (uiCast.gameObject == sourceContainer.Transform.gameObject
                && !sourceContainer.IsSyringe
                && liquidSimulation.ContainsContainer(1, pointerPosition))
            {
                return sourceContainer;
            }
        }

        return null;
    }

    private ContainerVisual GetSyringeBodyUnderPointer(Vector2 pointerPosition)
    {
        if (IsPointerOnSyringeBody(receivingContainer, pointerPosition))
        {
            return receivingContainer;
        }

        return IsPointerOnSyringeBody(sourceContainer, pointerPosition)
            ? sourceContainer
            : null;
    }

    private bool IsPointerOnSyringeBody(ContainerVisual container, Vector2 pointerPosition)
    {
        return container.IsSyringe
            && container.Geometry.BodyHitbox.Contains(
                ToContainerLocalPoint(container, pointerPosition));
    }

    internal void RestoreHand()
    {
        if (overlayRoot)
        {
            var root = overlayRoot.gameObject;
            root.SetActive(false);
            Object.Destroy(root);
            overlayRoot = null;
        }

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

    internal void CommitTransfer()
    {
        if (transferCommitted)
        {
            return;
        }

        var route = MultiplayerTransferBridge.GetRoute();
        if (route == TransferCommitRoute.Unavailable)
        {
            throw new System.InvalidOperationException(
                "The multiplayer liquid-transfer bridge is unavailable; the transfer was not applied.");
        }

        if (route != TransferCommitRoute.Client
            && (!receivingGameContainer || (!drainOnly && !sourceGameContainer)))
        {
            var message = !receivingGameContainer
                ? "The receiving game container became unavailable before commit."
                : "The source game container became unavailable before commit.";
            DebugLog.Write(
                $"Aborted liquid-transfer commit: {message} "
                + $"route={route}, drainOnly={drainOnly}, receiverSyncId={receivingSyncId}, "
                + $"sourceSyncId={sourceSyncId}.");
            throw new System.InvalidOperationException(message);
        }

        liquidSimulation.GetInitialSimulatedLiquidAmounts(initialSimulatedLiquidAmounts);
        liquidSimulation.GetFinalSimulatedLiquidAmounts(finalSimulatedLiquidAmounts);
        var typeCount = liquidSimulation.LiquidTypeCount;
        var stride = liquidSimulation.LiquidTypeBufferLength;
        var useOriginalSnapshot = route == TransferCommitRoute.Client;
        BuildTargetLiquidAmounts(
            receivingGameContainer,
            firstOriginalLiquidComposition,
            firstOriginalContainerAmount,
            0,
            typeCount,
            stride,
            useOriginalSnapshot);
        if (!drainOnly)
        {
            BuildTargetLiquidAmounts(
                sourceGameContainer,
                secondOriginalLiquidComposition,
                secondOriginalContainerAmount,
                1,
                typeCount,
                stride,
                useOriginalSnapshot);
        }

        var noLiquidLossEnabled = !drainOnly && NoLiquidLossSetting.IsEnabled;
        if (noLiquidLossEnabled)
        {
            CaptureUnreturnedLiquid(typeCount, stride);
        }

        var receiverCapacity = useOriginalSnapshot
            ? firstOriginalContainerCapacity
            : receivingGameContainer.Capacity;
        ValidateCapacity(receiverCapacity, "receiver capacity");
        var discardedOverflow = 0f;
        var sourceCapacity = 0f;
        if (drainOnly)
        {
            discardedOverflow = ClampLiquidAmountsToCapacity(
                targetLiquidAmounts,
                0,
                typeCount,
                receiverCapacity);
        }
        else
        {
            sourceCapacity = useOriginalSnapshot
                ? secondOriginalContainerCapacity
                : sourceGameContainer.Capacity;
            ValidateCapacity(sourceCapacity, "source capacity");
            discardedOverflow = RouteLiquidCapacityOverflow(
                targetLiquidAmounts,
                typeCount,
                stride,
                receiverCapacity,
                sourceCapacity);
        }

        if (noLiquidLossEnabled)
        {
            discardedOverflow += RedistributeUnreturnedLiquid(
                typeCount,
                stride,
                receiverCapacity,
                sourceCapacity);
        }

        if (route == TransferCommitRoute.Client)
        {
            var request = new LiquidTransferCommitRequest
            {
                RequestId = System.Guid.NewGuid(),
                DrainOnly = drainOnly,
                NoLiquidLoss = noLiquidLossEnabled
            };
            if (!MultiplayerTransferBridge.TryCreateChange(
                    receivingGameContainer,
                    receivingSyncId,
                    receivingSyncVersion,
                    CreateInitialComposition(firstOriginalLiquidComposition),
                    CreateTargetComposition(0, typeCount),
                    out request.Receiver))
            {
                throw new System.InvalidOperationException(
                    "Could not identify the receiving container in the multiplayer session; no transfer was applied.");
            }

            if (!drainOnly
                && !MultiplayerTransferBridge.TryCreateChange(
                    sourceGameContainer,
                    sourceSyncId,
                    sourceSyncVersion,
                    CreateInitialComposition(secondOriginalLiquidComposition),
                    CreateTargetComposition(stride, typeCount),
                    out request.Source))
            {
                throw new System.InvalidOperationException(
                    "Could not identify the source container in the multiplayer session; no transfer was applied.");
            }

            if (!MultiplayerTransferBridge.SendCommit(request))
            {
                throw new System.InvalidOperationException(
                    "Could not send the liquid-transfer result to the multiplayer host.");
            }

            transferCommitted = true;
            DebugLog.Write($"Submitted multiplayer liquid-transfer commit {request.RequestId:N}.");
            return;
        }

        ApplyLiquidTargets(receivingGameContainer, 0, typeCount, stride, receivingDrainPlan);
        if (!drainOnly)
        {
            ApplyLiquidTargets(sourceGameContainer, 1, typeCount, stride, sourceDrainPlan);
        }

        transferCommitted = true;
        DebugLog.Write(drainOnly
            ? $"Committed liquid drain: amount={receivingGameContainer.CurrentTotal:F2}, "
                + $"liquid not retained={discardedOverflow:F2} ml."
            : $"Committed liquid transfer: amounts receiver={receivingGameContainer.CurrentTotal:F2}, "
                + $"source={sourceGameContainer.CurrentTotal:F2}, "
                + $"liquid not retained={discardedOverflow:F2} ml.");
    }

    private List<LiquidAmount> CreateTargetComposition(
        int offset,
        int typeCount)
    {
        var composition = new List<LiquidAmount>(typeCount);
        for (var type = 0; type < typeCount; type++)
        {
            var amount = targetLiquidAmounts[offset + type];
            if (amount > 0f)
            {
                composition.Add(new LiquidAmount(liquidSimulation.GetLiquidId(type), amount));
            }
        }

        return composition;
    }

    private static List<LiquidAmount> CreateInitialComposition(LiquidProfileEntry[] profile)
    {
        var amounts = new Dictionary<string, float>(System.StringComparer.Ordinal);
        foreach (var entry in profile)
        {
            if (string.IsNullOrWhiteSpace(entry.LiquidId) || entry.Amount <= 0f)
            {
                continue;
            }

            amounts.TryGetValue(entry.LiquidId, out var currentAmount);
            amounts[entry.LiquidId] = currentAmount + entry.Amount;
        }

        var composition = new List<LiquidAmount>(amounts.Count);
        foreach (var entry in amounts)
        {
            composition.Add(new LiquidAmount(entry.Key, entry.Value));
        }

        return composition;
    }

    private void CaptureUnreturnedLiquid(int typeCount, int stride)
    {
        unreturnedLiquidByType = new float[typeCount];
        for (var type = 0; type < typeCount; type++)
        {
            var initialTotal = initialSimulatedLiquidAmounts[type]
                + initialSimulatedLiquidAmounts[stride + type];
            var targetTotal = targetLiquidAmounts[type]
                + (drainOnly ? 0f : targetLiquidAmounts[stride + type]);
            var unreturnedAmount = Mathf.Max(0f, initialTotal - targetTotal);
            unreturnedLiquidByType[type] = unreturnedAmount > 0.001f
                ? unreturnedAmount
                : 0f;
        }
    }

    private float RedistributeUnreturnedLiquid(
        int typeCount,
        int stride,
        float receiverCapacity,
        float sourceCapacity)
    {
        var receiverSpace = Mathf.Max(
            0f,
            receiverCapacity - SumLiquidAmounts(targetLiquidAmounts, 0, typeCount));
        var sourceSpace = drainOnly
            ? 0f
            : Mathf.Max(
                0f,
                sourceCapacity - SumLiquidAmounts(targetLiquidAmounts, stride, typeCount));
        var unreturnedTotal = SumLiquidAmounts(unreturnedLiquidByType, 0, typeCount);

        var sourceRestore = Mathf.Min(unreturnedTotal, sourceSpace);
        if (sourceRestore > 0f && unreturnedTotal > 0f)
        {
            var sourceRestoreRatio = sourceRestore / unreturnedTotal;
            for (var type = 0; type < typeCount; type++)
            {
                var amount = unreturnedLiquidByType[type] * sourceRestoreRatio;
                targetLiquidAmounts[stride + type] += amount;
                unreturnedLiquidByType[type] -= amount;
            }
        }

        unreturnedTotal = SumLiquidAmounts(unreturnedLiquidByType, 0, typeCount);
        var receiverRestore = Mathf.Min(unreturnedTotal, receiverSpace);
        if (receiverRestore > 0f && unreturnedTotal > 0f)
        {
            var receiverRestoreRatio = receiverRestore / unreturnedTotal;
            for (var type = 0; type < typeCount; type++)
            {
                var amount = unreturnedLiquidByType[type] * receiverRestoreRatio;
                targetLiquidAmounts[type] += amount;
                unreturnedLiquidByType[type] -= amount;
            }
        }

        return SumLiquidAmounts(unreturnedLiquidByType, 0, typeCount);
    }

    private void BuildTargetLiquidAmounts(
        WaterContainerItem container,
        LiquidProfileEntry[] originalComposition,
        float originalAmount,
        int containerIndex,
        int typeCount,
        int stride,
        bool useOriginalSnapshot)
    {
        var currentTotal = useOriginalSnapshot
            ? originalAmount
            : container.CurrentTotal;
        ValidateAmount(currentTotal, $"container {containerIndex + 1} amount");
        var offset = containerIndex * stride;
        var compositionTotal = 0f;
        for (var type = 0; type < typeCount; type++)
        {
            var liquidId = liquidSimulation.GetLiquidId(type);
            var currentAmount = useOriginalSnapshot
                ? GetLiquidAmount(originalComposition, liquidId)
                : GetLiquidAmount(container, liquidId);
            compositionTotal += currentAmount;
            var delta = finalSimulatedLiquidAmounts[offset + type]
                - initialSimulatedLiquidAmounts[offset + type];
            var targetAmount = currentAmount + delta;
            if (targetAmount < -0.001f)
            {
                throw new System.InvalidOperationException(
                    $"The simulation tried to remove more liquid '{liquidId}' than container "
                    + $"{containerIndex + 1} holds.");
            }

            targetLiquidAmounts[offset + type] = Mathf.Max(0f, targetAmount);
        }

        if (Mathf.Abs(compositionTotal - currentTotal) > 0.01f)
        {
            throw new System.InvalidOperationException(
                $"Container {containerIndex + 1} liquid stacks total {compositionTotal:F3} ml, "
                + $"but the game reports {currentTotal:F3} ml.");
        }

        var initialSimulatedTotal = SumLiquidAmounts(
            initialSimulatedLiquidAmounts,
            offset,
            typeCount);
        var finalSimulatedTotal = SumLiquidAmounts(
            finalSimulatedLiquidAmounts,
            offset,
            typeCount);
        var clearUnrepresentedRemainder = finalSimulatedTotal <= 0.001f
            && (drainOnly
                || (initialSimulatedTotal > 0.001f && !NoLiquidLossSetting.IsEnabled));
        if (clearUnrepresentedRemainder)
        {
            var unrepresentedRemainder = Mathf.Max(0f, currentTotal - initialSimulatedTotal);
            if (unrepresentedRemainder > 0.001f)
            {
                DebugLog.Write(
                    $"Clearing {unrepresentedRemainder:F2} ml below simulation resolution from "
                    + $"container {containerIndex + 1}, whose simulated liquid is fully emptied.");
            }

            System.Array.Clear(targetLiquidAmounts, offset, typeCount);
        }
    }

    private float GetLiquidAmount(WaterContainerItem container, string liquidId)
    {
        var amount = 0f;
        foreach (var stack in container.stack)
        {
            if (stack != null && string.Equals(stack.liquidId, liquidId, System.StringComparison.Ordinal))
            {
                amount += stack.amount;
            }
        }

        return amount;
    }

    private static float GetLiquidAmount(LiquidProfileEntry[] composition, string liquidId)
    {
        var amount = 0f;
        foreach (var entry in composition)
        {
            if (string.Equals(entry.LiquidId, liquidId, System.StringComparison.Ordinal))
            {
                amount += entry.Amount;
            }
        }

        return amount;
    }

    private float ClampLiquidAmountsToCapacity(
        float[] amounts,
        int offset,
        int typeCount,
        float capacity)
    {
        var total = SumLiquidAmounts(amounts, offset, typeCount);
        var overflow = Mathf.Max(0f, total - capacity);
        RemoveLiquidProportionally(amounts, offset, typeCount, overflow, total);
        return overflow;
    }

    private float RouteLiquidCapacityOverflow(
        float[] amounts,
        int typeCount,
        int stride,
        float receiverCapacity,
        float sourceCapacity)
    {
        var discarded = 0f;
        var receiverTotal = SumLiquidAmounts(amounts, 0, typeCount);
        var receiverOverflow = Mathf.Max(0f, receiverTotal - receiverCapacity);
        if (receiverOverflow > 0f)
        {
            var sourceTotal = SumLiquidAmounts(amounts, stride, typeCount);
            var moved = Mathf.Min(receiverOverflow, Mathf.Max(0f, sourceCapacity - sourceTotal));
            MoveLiquidProportionally(amounts, 0, stride, typeCount, moved, receiverTotal);
            RemoveLiquidProportionally(
                amounts,
                0,
                typeCount,
                receiverOverflow - moved,
                receiverTotal - moved);
            discarded += receiverOverflow - moved;
        }

        var sourceTotalAfterReceiverOverflow = SumLiquidAmounts(amounts, stride, typeCount);
        var sourceOverflow = Mathf.Max(0f, sourceTotalAfterReceiverOverflow - sourceCapacity);
        if (sourceOverflow > 0f)
        {
            var receiverTotalAfterRouting = SumLiquidAmounts(amounts, 0, typeCount);
            var moved = Mathf.Min(sourceOverflow, Mathf.Max(0f, receiverCapacity - receiverTotalAfterRouting));
            MoveLiquidProportionally(
                amounts,
                stride,
                0,
                typeCount,
                moved,
                sourceTotalAfterReceiverOverflow);
            RemoveLiquidProportionally(
                amounts,
                stride,
                typeCount,
                sourceOverflow - moved,
                sourceTotalAfterReceiverOverflow - moved);
            discarded += sourceOverflow - moved;
        }

        return discarded;
    }

    private static float SumLiquidAmounts(float[] amounts, int offset, int typeCount)
    {
        var total = 0f;
        for (var type = 0; type < typeCount; type++)
        {
            total += amounts[offset + type];
        }

        return total;
    }

    private static void MoveLiquidProportionally(
        float[] amounts,
        int sourceOffset,
        int destinationOffset,
        int typeCount,
        float moved,
        float sourceTotal)
    {
        if (moved <= 0f || sourceTotal <= 0f)
        {
            return;
        }

        var ratio = Mathf.Min(1f, moved / sourceTotal);
        for (var type = 0; type < typeCount; type++)
        {
            var amount = amounts[sourceOffset + type] * ratio;
            amounts[sourceOffset + type] -= amount;
            amounts[destinationOffset + type] += amount;
        }
    }

    private static void RemoveLiquidProportionally(
        float[] amounts,
        int offset,
        int typeCount,
        float removed,
        float total)
    {
        if (removed <= 0f || total <= 0f)
        {
            return;
        }

        var remainingRatio = Mathf.Clamp01(1f - removed / total);
        for (var type = 0; type < typeCount; type++)
        {
            amounts[offset + type] *= remainingRatio;
        }
    }

    private void ApplyLiquidTargets(
        WaterContainerItem container,
        int containerIndex,
        int typeCount,
        int stride,
        List<float> drainPlan)
    {
        var offset = containerIndex * stride;
        if (drainPlan.Count != container.stack.Count)
        {
            throw new System.InvalidOperationException(
                $"Container {containerIndex + 1} stack changed while the minigame was active.");
        }

        for (var stackIndex = 0; stackIndex < drainPlan.Count; stackIndex++)
        {
            drainPlan[stackIndex] = 0f;
        }

        for (var type = 0; type < typeCount; type++)
        {
            var liquidId = liquidSimulation.GetLiquidId(type);
            var currentAmount = GetLiquidAmount(container, liquidId);
            var drain = currentAmount - targetLiquidAmounts[offset + type];
            if (drain <= 0.001f)
            {
                continue;
            }

            var remaining = drain;
            for (var stackIndex = 0; stackIndex < container.stack.Count && remaining > 0.001f; stackIndex++)
            {
                var stack = container.stack[stackIndex];
                if (stack == null || !string.Equals(
                        stack.liquidId,
                        liquidId,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }

                var available = Mathf.Max(0f, stack.amount - drainPlan[stackIndex]);
                var portion = Mathf.Min(available, remaining);
                drainPlan[stackIndex] += portion;
                remaining -= portion;
            }

            if (remaining > 0.001f)
            {
                throw new System.InvalidOperationException(
                    $"The container could not drain {drain:F3} ml of liquid '{liquidId}'.");
            }
        }

        if (Sum(drainPlan) > 0.001f)
        {
            container.Drain(drainPlan);
        }

        for (var type = 0; type < typeCount; type++)
        {
            var liquidId = liquidSimulation.GetLiquidId(type);
            var currentAmount = GetLiquidAmount(container, liquidId);
            var addition = targetLiquidAmounts[offset + type] - currentAmount;
            if (addition <= 0.001f)
            {
                continue;
            }

            var added = container.AddLiquid(liquidId, addition);
            if (Mathf.Abs(added - addition) > 0.001f)
            {
                throw new System.InvalidOperationException(
                    $"The container accepted {added:F3} of {addition:F3} ml of liquid '{liquidId}'.");
            }
        }

    }

    private static List<float> CreateDrainPlan(WaterContainerItem container)
    {
        var plan = new List<float>(container && container.stack != null ? container.stack.Count : 0);
        if (container && container.stack != null)
        {
            for (var i = 0; i < container.stack.Count; i++)
            {
                plan.Add(0f);
            }
        }

        return plan;
    }

    private static void ApplySingleContainerAmount(
        WaterContainerItem container,
        float targetAmount)
    {
        const float AmountTolerance = 0.001f;
        var currentAmount = container.CurrentTotal;
        var change = targetAmount - currentAmount;
        if (change < -AmountTolerance)
        {
            DrainLiquid(container, -change);
        }
        else if (change > AmountTolerance)
        {
            AddLiquid(container, GetLiquidComposition(container), change);
        }
    }

    private static void ApplyLiquidAmounts(
        WaterContainerItem receiver,
        WaterContainerItem source,
        float targetReceiverAmount,
        float targetSourceAmount)
    {
        const float AmountTolerance = 0.001f;
        var receiverAmount = receiver.CurrentTotal;
        var sourceAmount = source.CurrentTotal;
        var receiverChange = targetReceiverAmount - receiverAmount;
        var sourceChange = targetSourceAmount - sourceAmount;
        var transferToReceiver = receiverChange > 0f && sourceChange < 0f
            ? Mathf.Min(receiverChange, -sourceChange)
            : 0f;
        var transferToSource = sourceChange > 0f && receiverChange < 0f
            ? Mathf.Min(sourceChange, -receiverChange)
            : 0f;
        var receiverAddition = Mathf.Max(0f, receiverChange - transferToReceiver);
        var sourceAddition = Mathf.Max(0f, sourceChange - transferToSource);
        var sourceComposition = GetLiquidComposition(source);
        var receiverComposition = GetLiquidComposition(receiver);
        var addedLiquidComposition = sourceComposition.Count > 0
            ? sourceComposition
            : receiverComposition;

        if (addedLiquidComposition.Count == 0
            && (receiverAddition > AmountTolerance || sourceAddition > AmountTolerance))
        {
            throw new System.InvalidOperationException(
                "Cannot add simulated liquid because neither game container has a liquid type to use.");
        }

        if (transferToReceiver > AmountTolerance)
        {
            TransferLiquid(source, receiver, transferToReceiver);
        }

        if (transferToSource > AmountTolerance)
        {
            TransferLiquid(receiver, source, transferToSource);
        }

        var excessReceiver = receiver.CurrentTotal - targetReceiverAmount;
        if (excessReceiver > AmountTolerance)
        {
            DrainLiquid(receiver, excessReceiver);
        }

        var excessSource = source.CurrentTotal - targetSourceAmount;
        if (excessSource > AmountTolerance)
        {
            DrainLiquid(source, excessSource);
        }

        var missingReceiver = targetReceiverAmount - receiver.CurrentTotal;
        if (missingReceiver > AmountTolerance)
        {
            AddLiquid(receiver, addedLiquidComposition, missingReceiver);
        }

        var missingSource = targetSourceAmount - source.CurrentTotal;
        if (missingSource > AmountTolerance)
        {
            AddLiquid(source, addedLiquidComposition, missingSource);
        }
    }

    private static List<LiquidPortion> GetLiquidComposition(WaterContainerItem container)
    {
        var composition = new List<LiquidPortion>(container.stack.Count);
        foreach (var stack in container.stack)
        {
            if (stack != null
                && !string.IsNullOrEmpty(stack.liquidId)
                && stack.amount > 0f
                && !float.IsNaN(stack.amount)
                && !float.IsInfinity(stack.amount))
            {
                composition.Add(new LiquidPortion(stack.liquidId, stack.amount));
            }
        }

        return composition;
    }

    private static void TransferLiquid(
        WaterContainerItem donor,
        WaterContainerItem receiver,
        float amount)
    {
        var liquidIds = new List<string>(donor.stack.Count);
        foreach (var stack in donor.stack)
        {
            liquidIds.Add(stack?.liquidId);
        }

        var drainedAmounts = donor.CalculateDrain(amount);
        if (drainedAmounts.Count != liquidIds.Count)
        {
            throw new System.InvalidOperationException(
                "The game's liquid stack changed while calculating the transfer.");
        }

        var drainedTotal = Sum(drainedAmounts);
        donor.Drain(drainedAmounts);
        for (var i = 0; i < drainedAmounts.Count; i++)
        {
            var drained = drainedAmounts[i];
            if (drained <= 0f)
            {
                continue;
            }

            var added = receiver.AddLiquid(liquidIds[i], drained);
            if (Mathf.Abs(added - drained) > 0.001f)
            {
                throw new System.InvalidOperationException(
                    $"The receiver accepted {added:F3} of {drained:F3} ml of liquid '{liquidIds[i]}'.");
            }
        }

        if (Mathf.Abs(drainedTotal - amount) > 0.001f)
        {
            throw new System.InvalidOperationException(
                $"The donor could provide {drainedTotal:F3} of {amount:F3} ml for transfer.");
        }
    }

    private static void DrainLiquid(WaterContainerItem container, float amount)
    {
        var drainedAmounts = container.CalculateDrain(amount);
        var drainedTotal = Sum(drainedAmounts);
        container.Drain(drainedAmounts);
        if (Mathf.Abs(drainedTotal - amount) > 0.001f)
        {
            throw new System.InvalidOperationException(
                $"The container could drain {drainedTotal:F3} of {amount:F3} ml.");
        }
    }

    private static void AddLiquid(
        WaterContainerItem container,
        List<LiquidPortion> composition,
        float amount)
    {
        const float AmountTolerance = 0.001f;
        var totalComposition = 0f;
        foreach (var portion in composition)
        {
            totalComposition += portion.Amount;
        }

        if (totalComposition <= 0f)
        {
            throw new System.InvalidOperationException("The selected liquid composition is empty.");
        }

        var remaining = amount;
        for (var i = 0; i < composition.Count && remaining > AmountTolerance; i++)
        {
            var portionAmount = i == composition.Count - 1
                ? remaining
                : amount * (composition[i].Amount / totalComposition);
            portionAmount = Mathf.Min(portionAmount, remaining);
            var added = container.AddLiquid(composition[i].LiquidId, portionAmount);
            remaining -= added;
        }

        if (remaining > AmountTolerance)
        {
            throw new System.InvalidOperationException(
                $"The container could not accept {remaining:F3} ml of simulated liquid.");
        }
    }

    private static float RouteCapacityOverflow(
        ref float receiverAmount,
        float receiverCapacity,
        ref float sourceAmount,
        float sourceCapacity)
    {
        receiverAmount = Mathf.Max(0f, receiverAmount);
        sourceAmount = Mathf.Max(0f, sourceAmount);
        var discarded = 0f;

        if (receiverAmount > receiverCapacity)
        {
            var overflow = receiverAmount - receiverCapacity;
            receiverAmount = receiverCapacity;
            var accepted = Mathf.Min(overflow, Mathf.Max(0f, sourceCapacity - sourceAmount));
            sourceAmount += accepted;
            discarded += overflow - accepted;
        }

        if (sourceAmount > sourceCapacity)
        {
            var overflow = sourceAmount - sourceCapacity;
            sourceAmount = sourceCapacity;
            var accepted = Mathf.Min(overflow, Mathf.Max(0f, receiverCapacity - receiverAmount));
            receiverAmount += accepted;
            discarded += overflow - accepted;
        }

        return discarded;
    }

    private static float Sum(List<float> amounts)
    {
        var total = 0f;
        foreach (var amount in amounts)
        {
            total += amount;
        }

        return total;
    }

    private static void ValidateAmount(float amount, string label)
    {
        if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount))
        {
            throw new System.InvalidOperationException($"The game reported an invalid {label}: {amount}.");
        }
    }

    private static void ValidateCapacity(float capacity, string label)
    {
        if (capacity < 0f || float.IsNaN(capacity) || float.IsInfinity(capacity))
        {
            throw new System.InvalidOperationException($"The game reported an invalid {label}: {capacity}.");
        }
    }

    private readonly struct LiquidPortion
    {
        internal LiquidPortion(string liquidId, float amount)
        {
            LiquidId = liquidId;
            Amount = amount;
        }

        internal string LiquidId { get; }
        internal float Amount { get; }
    }

    private static ContainerVisual CreateContainer(
        RectTransform parent,
        string name,
        Sprite sprite,
        Vector2 position,
        float rotation,
        bool isSyringe)
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
        rect.localScale = Vector3.one * LiquidContainerLayout.ContainerScale;

        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = true;

        var hasBoundary = ModSprites.TryGetPrecomputedBoundary(sprite, out var boundary);
        var hasOpening = hasBoundary && boundary.HasOpening;
        var openingPosition = hasOpening ? boundary.OpeningPosition : Vector2.zero;
        var openingWidth = hasOpening ? boundary.OpeningWidth : 0f;
        var openingLeft = Vector2.zero;
        var openingRight = Vector2.zero;
        var outlinePoints = System.Array.Empty<Vector2>();
        var interiorWalls = System.Array.Empty<Vector2[]>();
        var containerCollisionPoints = System.Array.Empty<Vector2>();
        var syringeGeometry = isSyringe ? CreateSyringeGeometry(sprite) : default;
        if (isSyringe)
        {
            openingPosition = syringeGeometry.OpeningCenter;
            openingLeft = syringeGeometry.OpeningLeft;
            openingRight = syringeGeometry.OpeningRight;
            openingWidth = Vector2.Distance(openingLeft, openingRight);
            hasOpening = true;
            outlinePoints = syringeGeometry.FluidOutline;
            containerCollisionPoints = syringeGeometry.CollisionOutline;
        }
        else if (!hasOpening)
        {
            Plugin.LogWarning($"Could not detect the open rim of container sprite '{sprite.name}'.");
        }
        else
        {
            openingLeft = openingPosition - new Vector2(openingWidth * 0.5f, 0f);
            openingRight = openingPosition + new Vector2(openingWidth * 0.5f, 0f);
            outlinePoints = boundary.WallCenterlinePoints;
            if (outlinePoints.Length >= 2)
            {
                interiorWalls = boundary.InteriorWalls;
                containerCollisionPoints = boundary.CollisionPoints;
            }
            else
            {
                Plugin.LogWarning($"Could not trace the outline of container sprite '{sprite.name}'.");
            }
        }

        return new ContainerVisual(
            rect,
            openingPosition,
            openingLeft,
            openingRight,
            hasOpening,
            outlinePoints,
            interiorWalls,
            containerCollisionPoints,
            isSyringe,
            syringeGeometry);
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

        if (container.IsSyringe)
        {
            rotationPivotPosition = container.Transform.anchoredPosition
                + LiquidContainerLayout.TransformOpeningPoint(
                    container.Transform,
                    container.Geometry.NeedlePoint);
            var pointerVector = rotationStartPointer - rotationPivotPosition;
            rotationLastPointerAngle = Mathf.Atan2(pointerVector.y, pointerVector.x) * Mathf.Rad2Deg;
            rotationTrackingStarted = true;
        }
        else if (container.HasRotationPivot && !IsAtVerticalPosition(rotationAngle))
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
        var pivotPoint = GetRotationPivotPoint(visual);
        var maxPivotDistance = 0f;
        var rotationOutline = visual.IsSyringe
            ? visual.CollisionOutlinePoints
            : visual.OutlinePoints;
        foreach (var point in rotationOutline)
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
        var previousPosition = visual.Transform.anchoredPosition;
        var startingAngle = visual.Transform.localEulerAngles.z;
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

        liquidSimulation.ApplyContainerPoseChange(
            visual.Transform,
            previousPosition,
            startingAngle,
            visual.Transform.anchoredPosition,
            visual.Transform.localEulerAngles.z,
            Vector2.one * RotationMovementInfluence);
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

        liquidSimulation.TranslateContainerDrops(
            transform,
            transform.anchoredPosition - startPosition,
            new Vector2(
                TranslationHorizontalInfluence,
                TranslationVerticalInfluence));
    }

    private void ApplyRotationPose(ContainerVisual visual, float angle)
    {
        var pivotPoint = GetRotationPivotPoint(visual);
        visual.Transform.localRotation = Quaternion.Euler(0f, 0f, angle);
        visual.Transform.anchoredPosition = rotationPivotPosition
            - LiquidContainerLayout.TransformOpeningPoint(visual.Transform, pivotPoint);
    }

    private static Vector2 GetRotationPivotPoint(ContainerVisual visual)
    {
        return visual.IsSyringe
            ? visual.Geometry.NeedlePoint
            : visual.PivotIsRight
                ? visual.RightOpeningPoint
                : visual.LeftOpeningPoint;
    }

    private bool WouldOverlap(ContainerVisual moving)
    {
        var other = moving == receivingContainer ? sourceContainer : receivingContainer;
        var movingTransform = moving.Transform;
        var otherTransform = other.Transform;
        var overlapsOtherContainer = ContainerShapeCollision.Overlaps(
            moving.CollisionOutlinePoints,
            movingTransform.anchoredPosition,
            movingTransform.localScale.x,
            movingTransform.localEulerAngles.z,
            other.CollisionOutlinePoints,
            otherTransform.anchoredPosition,
            otherTransform.localScale.x,
            otherTransform.localEulerAngles.z);
        return overlapsOtherContainer
            || hasFunnel && ContainerShapeCollision.OverlapsSegments(
                moving.CollisionOutlinePoints,
                movingTransform.anchoredPosition,
                movingTransform.localScale.x,
                movingTransform.localEulerAngles.z,
                funnelWallPoints,
                2f);
    }

    private void StartPivotTracking(Vector2 pointer)
    {
        var visual = activeRotationContainer;
        var pivotPoint = GetRotationPivotPoint(visual);
        rotationPivotPosition = visual.Transform.anchoredPosition
            + LiquidContainerLayout.TransformOpeningPoint(visual.Transform, pivotPoint);
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
        if (visual.IsSyringe)
        {
            return false;
        }

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
            + LiquidContainerLayout.TransformOpeningPoint(visual.Transform, pivotPoint);
        var rebasedPointer = pointer - rotationPivotPosition;
        rotationLastPointerAngle = Mathf.Atan2(rebasedPointer.y, rebasedPointer.x) * Mathf.Rad2Deg;
        return true;
    }

    private static bool IsAtVerticalPosition(float angle)
    {
        var nearestVerticalAngle = Mathf.Round(angle / 180f) * 180f;
        return Mathf.Abs(Mathf.DeltaAngle(angle, nearestVerticalAngle)) <= PivotSwitchHysteresis;
    }

    private sealed class ContainerVisual
    {
        internal ContainerVisual(
            RectTransform transform,
            Vector2 openingCenterPoint,
            Vector2 leftOpeningPoint,
            Vector2 rightOpeningPoint,
            bool hasOpening,
            Vector2[] outlinePoints,
            Vector2[][] interiorWalls,
            Vector2[] collisionOutlinePoints,
            bool isSyringe,
            SyringeGeometry geometry)
        {
            Transform = transform;
            OpeningCenterPoint = openingCenterPoint;
            LeftOpeningPoint = leftOpeningPoint;
            RightOpeningPoint = rightOpeningPoint;
            HasOpening = hasOpening;
            OutlinePoints = outlinePoints;
            InteriorWalls = interiorWalls;
            CollisionOutlinePoints = collisionOutlinePoints;
            IsSyringe = isSyringe;
            Geometry = geometry;
        }

        internal RectTransform Transform { get; }
        internal Vector2 OpeningCenterPoint { get; }
        internal Vector2 LeftOpeningPoint { get; }
        internal Vector2 RightOpeningPoint { get; }
        internal bool HasOpening { get; }
        internal Vector2[] OutlinePoints { get; }
        internal Vector2[][] InteriorWalls { get; }
        internal Vector2[] CollisionOutlinePoints { get; }
        internal bool IsSyringe { get; }
        internal SyringeGeometry Geometry { get; }
        internal bool HasRotationPivot { get; set; }
        internal bool PivotIsRight { get; set; }
    }

    private sealed class SyringeVisual
    {
        internal SyringeVisual(
            ContainerVisual container,
            int containerIndex,
            float capacity,
            RectTransform handleTransform)
        {
            Container = container;
            ContainerIndex = containerIndex;
            Capacity = capacity;
            HandleTransform = handleTransform;
            IntakeRequests = new List<SyringeIntakeRequest>();
        }

        internal ContainerVisual Container { get; }
        internal int ContainerIndex { get; }
        internal float Capacity { get; }
        internal RectTransform HandleTransform { get; }
        internal float EjectionVolumeCredit { get; set; }
        internal float IntakeVolumeCredit { get; set; }
        internal float LastRequestedAmount { get; set; }
        internal List<SyringeIntakeRequest> IntakeRequests { get; }
        internal float DiagnosticLiquidTaken { get; set; }
        internal float DiagnosticAirExpired { get; set; }
        internal float NextDiagnosticLogTime { get; set; }
    }

    private sealed class SyringeIntakeRequest
    {
        internal SyringeIntakeRequest(float remainingVolume, float remainingLifetime)
        {
            RemainingVolume = remainingVolume;
            RemainingLifetime = remainingLifetime;
        }

        internal float RemainingVolume { get; set; }
        internal float RemainingLifetime { get; set; }
    }

    private readonly struct SyringeGeometry
    {
        internal SyringeGeometry(
            Vector2[] fluidOutline,
            Vector2[] collisionOutline,
            Vector2 openingCenter,
            Vector2 openingLeft,
            Vector2 openingRight,
            float fluidBottomY,
            float fluidTopY,
            Rect bodyHitbox,
            Vector2 needlePoint)
        {
            FluidOutline = fluidOutline;
            CollisionOutline = collisionOutline;
            OpeningCenter = openingCenter;
            OpeningLeft = openingLeft;
            OpeningRight = openingRight;
            FluidBottom = new Vector2(0f, fluidBottomY);
            FluidTop = new Vector2(0f, fluidTopY);
            BodyHitbox = bodyHitbox;
            NeedlePoint = needlePoint;
        }

        internal Vector2[] FluidOutline { get; }
        internal Vector2[] CollisionOutline { get; }
        internal Vector2 OpeningCenter { get; }
        internal Vector2 OpeningLeft { get; }
        internal Vector2 OpeningRight { get; }
        internal Vector2 FluidBottom { get; }
        internal Vector2 FluidTop { get; }
        internal Rect BodyHitbox { get; }
        internal Vector2 NeedlePoint { get; }
    }
}
