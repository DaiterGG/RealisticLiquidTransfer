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
    private const float RotationMovementInfluence = 1f;
    private const float TranslationHorizontalInfluence = 1f;
    private const float TranslationVerticalInfluence = 1f;
    private const float SyringeHandleSmoothTime = 0.04f;
    private const float SyringeFreePistonSpeed = 420f;
    private const float SyringeEjectionRate = 24f;
    private const float SyringeEjectionSpeed = 1000f;
    private const string ReferenceSpriteName = "coffee";
    private const float ReferenceCapacity = 250f;
    private static float referenceCapacity = ReferenceCapacity;

    private static bool sessionShaderEnabled = true;
    private static bool sessionSharpColorBordersEnabled;
    private static bool sessionClickSpawnEnabled;
    private static int sessionCollisionPasses = LiquidSimulation.DefaultDropCollisionPasses;
    private static int sessionCollisionCorrection = LiquidSimulation.DefaultCollisionCorrectionPercent;
    private static int sessionVelocityContactMargin =
        LiquidSimulation.DefaultVelocityContactMarginTenths;
    private static int sessionDropRadiusTenths = LiquidSimulation.DefaultDropRadiusTenths;
    private static bool sessionFreezeSimulation;
    private static int sessionInitialPackingTenths = LiquidSimulation.DefaultPackingTenths;

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
    private Image handImage;
    private Text controlsText;
    private Text volumeText;
    private RectTransform screenRoot;
    private Toggle shaderToggle;
    private Toggle sharpColorBordersToggle;
    private Toggle spawnToggle;
    private Slider collisionPassSlider;
    private Text collisionPassText;
    private Slider collisionCorrectionSlider;
    private Text collisionCorrectionText;
    private Slider velocityContactMarginSlider;
    private Text velocityContactMarginText;
    private Slider dropRadiusSlider;
    private Text dropRadiusText;
    private Slider initialPackingSlider;
    private Text initialPackingText;
    private Toggle freezeToggle;
    private Slider[] collisionTuningSliders;
    private RectTransform debugTextRoot;
    private LiquidSimulation liquidSimulation;
    private HandAlphaGradient handAlphaGradient;
    private Vector2 originalHandSize;
    private bool originalPreserveAspect;
    private ContainerVisual receivingContainer;
    private ContainerVisual sourceContainer;
    private RectTransform heldContainer;
    private ContainerVisual heldVisual;
    private Vector2 heldOffset;
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
    private bool clickSpawnEnabled;
    private bool transferCommitted;
    private float fitScale;
    private float controlsUpdateTimer;
    private int displayedDropCount = -1;
    private float[] initialSimulatedLiquidAmounts;
    private float[] finalSimulatedLiquidAmounts;
    private float[] targetLiquidAmounts;

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
        LiquidProfileEntry[] secondOriginalLiquidComposition)
    {
        this.receivingGameContainer = receivingGameContainer;
        this.sourceGameContainer = sourceGameContainer;
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
        debugTextRoot = CreateDebugTextRoot(game.minigameScreen);
        controlsText = CreateControlsText(debugTextRoot);
        if (drainOnly)
        {
            controlsText.text = "Hold left-click outside the container to spawn drops | Right-click and drag to rotate | Esc: exit";
        }
        if (receivingSyringe != null || sourceSyringe != null)
        {
            controlsText.text =
                "Drag the syringe plunger up to draw nearby liquid and down to inject through the needle | Esc: exit";
        }

        volumeText = CreateVolumeText(debugTextRoot);
        var rightControlsX = Mathf.Max(24f, debugTextRoot.rect.width - 210f);
        shaderToggle = CreateToggle(
            debugTextRoot,
            "LiquidShaderToggle",
            "Liquid shader",
            new Vector2(rightControlsX, -24f));
        shaderToggle.isOn = sessionShaderEnabled;
        spawnToggle = CreateToggle(
            debugTextRoot,
            "ClickSpawnToggle",
            "Spawn drops on click",
            new Vector2(rightControlsX, -52f));
        spawnToggle.isOn = sessionClickSpawnEnabled;
        sharpColorBordersToggle = CreateToggle(
            debugTextRoot,
            "SharpColorBordersToggle",
            "Sharp liquid borders",
            new Vector2(rightControlsX, -78f));
        sharpColorBordersToggle.isOn = sessionSharpColorBordersEnabled;
        freezeToggle = CreateToggle(
            debugTextRoot,
            "FreezeSimulationToggle",
            "Freeze simulation",
            new Vector2(rightControlsX, -326f));
        freezeToggle.isOn = sessionFreezeSimulation;
        collisionPassSlider = CreateTuningSlider(
            debugTextRoot,
            "CollisionPasses",
            "Collision passes",
            new Vector2(rightControlsX, -106f),
            new Vector2(rightControlsX, -132f),
            LiquidSimulation.MinimumDropCollisionPasses,
            LiquidSimulation.MaximumDropCollisionPasses,
            sessionCollisionPasses,
            true,
            out collisionPassText);
        collisionCorrectionSlider = CreateTuningSlider(
            debugTextRoot,
            "CollisionCorrection",
            "Position correction",
            new Vector2(rightControlsX, -160f),
            new Vector2(rightControlsX, -186f),
            LiquidSimulation.MinimumCollisionCorrectionPercent,
            LiquidSimulation.MaximumCollisionCorrectionPercent,
            sessionCollisionCorrection,
            true,
            out collisionCorrectionText);
        velocityContactMarginSlider = CreateTuningSlider(
            debugTextRoot,
            "VelocityContactMargin",
            "Velocity contact margin",
            new Vector2(rightControlsX, -214f),
            new Vector2(rightControlsX, -240f),
            LiquidSimulation.MinimumVelocityContactMarginTenths,
            LiquidSimulation.MaximumVelocityContactMarginTenths,
            sessionVelocityContactMargin,
            true,
            out velocityContactMarginText);
        dropRadiusSlider = CreateTuningSlider(
            debugTextRoot,
            "DropRadius",
            "Base drop radius",
            new Vector2(rightControlsX, -268f),
            new Vector2(rightControlsX, -294f),
            LiquidSimulation.MinimumDropRadiusTenths,
            LiquidSimulation.MaximumDropRadiusTenths,
            sessionDropRadiusTenths,
            false,
            out dropRadiusText);
        initialPackingSlider = CreateTuningSlider(
            debugTextRoot,
            "InitialPacking",
            "Initial packing",
            new Vector2(rightControlsX, -348f),
            new Vector2(rightControlsX, -374f),
            LiquidSimulation.MinimumPackingTenths,
            LiquidSimulation.MaximumPackingTenths,
            sessionInitialPackingTenths,
            false,
            out initialPackingText);

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
            receivingContainer.Transform.anchoredPosition = Vector2.zero;
            sourceContainer.Transform.anchoredPosition = Vector2.one * 100000f;
            sourceContainer.Transform.gameObject.SetActive(false);
        }
        else
        {
            PositionContainersSideBySide(receivingContainer, sourceContainer);
        }

        handImage = game.handTransform.GetComponent<Image>();
        originalHandSize = handImage.rectTransform.sizeDelta;
        originalPreserveAspect = handImage.preserveAspect;

        fitScale = drainOnly
            ? GetFitScale(game.minigameScreen, screenRoot, receivingContainer)
            : GetFitScale(game.minigameScreen, screenRoot, receivingContainer, sourceContainer);
        screenRoot.localScale = Vector3.one * fitScale;
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
            sessionInitialPackingTenths / 100f);
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
        shaderToggle.onValueChanged.AddListener(SetLiquidShaderEnabled);
        SetLiquidShaderEnabled(shaderToggle.isOn);
        sharpColorBordersToggle.onValueChanged.AddListener(SetSharpColorBordersEnabled);
        SetSharpColorBordersEnabled(sharpColorBordersToggle.isOn);
        spawnToggle.onValueChanged.AddListener(SetClickSpawnEnabled);
        SetClickSpawnEnabled(spawnToggle.isOn);
        freezeToggle.onValueChanged.AddListener(SetFreezeSimulation);
        SetFreezeSimulation(freezeToggle.isOn);
        collisionTuningSliders = new[]
        {
            collisionPassSlider,
            collisionCorrectionSlider,
            velocityContactMarginSlider,
            dropRadiusSlider,
            initialPackingSlider
        };
        collisionPassSlider.onValueChanged.AddListener(SetCollisionPasses);
        SetCollisionPasses(collisionPassSlider.value);
        collisionCorrectionSlider.onValueChanged.AddListener(SetCollisionCorrection);
        SetCollisionCorrection(collisionCorrectionSlider.value);
        velocityContactMarginSlider.onValueChanged.AddListener(SetVelocityContactMargin);
        SetVelocityContactMargin(velocityContactMarginSlider.value);
        dropRadiusSlider.onValueChanged.AddListener(SetDropRadius);
        SetDropRadius(dropRadiusSlider.value);
        initialPackingSlider.onValueChanged.AddListener(SetInitialPacking);
        SetInitialPacking(initialPackingSlider.value);
        UpdateVolumeReadout();
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

    private static RectTransform CreateDebugTextRoot(RectTransform parent)
    {
        var root = new GameObject("RealisticLiquidTransferDebugText", typeof(RectTransform))
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
        controls.text = "Hold left-click outside containers to spawn drops | Right-click and drag to rotate | Esc: exit";
        controls.color = Color.white;
        controls.raycastTarget = false;
        return controls;
    }

    private static Text CreateVolumeText(RectTransform parent)
    {
        var textObject = new GameObject("LiquidTransferContainerVolumes", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        var text = textObject.GetComponent<Text>();
        var rect = text.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-24f, 120f);
        rect.sizeDelta = new Vector2(Mathf.Min(260f, parent.rect.width * 0.32f), 160f);

        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 12;
        text.alignment = TextAnchor.UpperRight;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Toggle CreateToggle(
        RectTransform parent,
        string name,
        string labelText,
        Vector2 position)
    {
        var toggleObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Toggle));
        var toggle = toggleObject.GetComponent<Toggle>();
        var rect = toggle.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(20f, 20f);

        var background = toggle.GetComponent<Image>();
        background.color = new Color(0.06f, 0.08f, 0.12f, 0.9f);

        var checkmark = new GameObject(
            "Checkmark",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)).GetComponent<Image>();
        var checkmarkRect = checkmark.rectTransform;
        checkmarkRect.SetParent(rect, false);
        checkmarkRect.anchorMin = new Vector2(0.2f, 0.2f);
        checkmarkRect.anchorMax = new Vector2(0.8f, 0.8f);
        checkmarkRect.offsetMin = Vector2.zero;
        checkmarkRect.offsetMax = Vector2.zero;
        checkmark.color = new Color(0.15f, 0.75f, 0.35f, 1f);
        checkmark.raycastTarget = false;

        var labelObject = new GameObject(
            $"{name}Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        var label = labelObject.GetComponent<Text>();
        var labelRect = label.rectTransform;
        labelRect.SetParent(parent, false);
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = position + new Vector2(28f, 0f);
        labelRect.sizeDelta = new Vector2(170f, 20f);
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 14;
        label.alignment = TextAnchor.MiddleLeft;
        label.text = labelText;
        label.color = Color.white;
        label.raycastTarget = false;

        toggle.targetGraphic = background;
        toggle.graphic = checkmark;
        toggle.transition = Selectable.Transition.None;
        toggle.isOn = true;
        return toggle;
    }

    private static Text CreateTuningLabel(
        RectTransform parent,
        string name,
        string label,
        Vector2 position)
    {
        var textObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        var text = textObject.GetComponent<Text>();
        var rect = text.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(180f, 20f);
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 14;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;
        return text;
    }

    private static Slider CreateTuningSlider(
        RectTransform parent,
        string name,
        string label,
        Vector2 labelPosition,
        Vector2 sliderPosition,
        float minimum,
        float maximum,
        float initialValue,
        bool wholeNumbers,
        out Text labelText)
    {
        var sliderObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Slider));
        var slider = sliderObject.GetComponent<Slider>();
        var rect = slider.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = sliderPosition;
        rect.sizeDelta = new Vector2(180f, 18f);

        labelText = CreateTuningLabel(
            parent,
            $"{name}Label",
            label,
            labelPosition);
        slider.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.9f);

        var fillArea = new GameObject("FillArea", typeof(RectTransform)).GetComponent<RectTransform>();
        fillArea.SetParent(rect, false);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.pivot = new Vector2(0.5f, 0.5f);
        fillArea.sizeDelta = new Vector2(-18f, 6f);

        var fill = new GameObject(
            "Fill",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)).GetComponent<Image>();
        fill.rectTransform.SetParent(fillArea, false);
        fill.rectTransform.anchorMin = Vector2.zero;
        fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = Vector2.zero;
        fill.rectTransform.offsetMax = Vector2.zero;
        fill.color = new Color(0.15f, 0.75f, 0.35f, 1f);

        var handleArea = new GameObject("HandleSlideArea", typeof(RectTransform)).GetComponent<RectTransform>();
        handleArea.SetParent(rect, false);
        handleArea.anchorMin = Vector2.zero;
        handleArea.anchorMax = Vector2.one;
        handleArea.offsetMin = new Vector2(9f, 0f);
        handleArea.offsetMax = new Vector2(-9f, 0f);

        var handle = new GameObject(
            "Handle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)).GetComponent<Image>();
        var handleRect = handle.rectTransform;
        handleRect.SetParent(handleArea, false);
        handleRect.anchorMin = new Vector2(0f, 0.5f);
        handleRect.anchorMax = new Vector2(0f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.sizeDelta = new Vector2(14f, 14f);
        handle.color = Color.white;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handleRect;
        slider.targetGraphic = handle;
        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = wholeNumbers;
        slider.value = initialValue;
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;
        return slider;
    }

    private void SetLiquidShaderEnabled(bool enabled)
    {
        sessionShaderEnabled = enabled;
        liquidSimulation.SetSurfaceShaderEnabled(enabled);
    }

    private void SetSharpColorBordersEnabled(bool enabled)
    {
        sessionSharpColorBordersEnabled = enabled;
        liquidSimulation.SetSharpColorBordersEnabled(enabled);
    }

    private void SetCollisionPasses(float passes)
    {
        var passCount = Mathf.RoundToInt(passes);
        sessionCollisionPasses = passCount;
        liquidSimulation.SetDropCollisionPasses(passCount);
        collisionPassText.text = $"Collision passes: {passCount}";
    }

    private void SetCollisionCorrection(float percent)
    {
        var correction = Mathf.RoundToInt(percent);
        sessionCollisionCorrection = correction;
        liquidSimulation.SetCollisionCorrectionPercent(correction);
        collisionCorrectionText.text = $"Position correction: {correction}%";
    }

    private void SetVelocityContactMargin(float tenths)
    {
        var marginTenths = Mathf.RoundToInt(tenths);
        var margin = marginTenths / 10f;
        sessionVelocityContactMargin = marginTenths;
        liquidSimulation.SetVelocityContactMarginTenths(marginTenths);
        velocityContactMarginText.text = $"Velocity contact margin: {margin:F1}px";
    }

    private void SetDropRadius(float tenths)
    {
        var radiusTenths = Mathf.RoundToInt(tenths);
        var radius = radiusTenths / 10f;
        sessionDropRadiusTenths = radiusTenths;
        liquidSimulation.SetDropRadius(radius);
        dropRadiusText.text = $"Base drop radius: {radius:F1}px";
    }

    private void SetInitialPacking(float tenths)
    {
        var packingTenths = Mathf.RoundToInt(tenths);
        var packing = packingTenths / 100f;
        sessionInitialPackingTenths = packingTenths;
        liquidSimulation.SetInitialPacking(packing);
        initialPackingText.text = $"Initial packing: {packing:F2}x";
    }

    private void SetFreezeSimulation(bool enabled)
    {
        sessionFreezeSimulation = enabled;
        liquidSimulation.SetSimulationFrozen(enabled);
    }

    private void SetClickSpawnEnabled(bool enabled)
    {
        sessionClickSpawnEnabled = enabled;
        clickSpawnEnabled = enabled;
    }

    private bool IsPointerOverInteractionControl(List<RaycastResult> uiCasts)
    {
        foreach (var uiCast in uiCasts)
        {
            var selectable = uiCast.gameObject.GetComponentInParent<Selectable>();
            if (selectable == shaderToggle
                || selectable == sharpColorBordersToggle
                || selectable == spawnToggle
                || selectable == freezeToggle)
            {
                return true;
            }

            foreach (var slider in collisionTuningSliders)
            {
                if (selectable == slider)
                {
                    return true;
                }
            }
        }

        var canvas = shaderToggle.GetComponentInParent<Canvas>();
        var eventCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;
        var pointer = Input.mousePosition;
        return RectTransformUtility.RectangleContainsScreenPoint(
                shaderToggle.GetComponent<RectTransform>(),
                pointer,
                eventCamera)
            || RectTransformUtility.RectangleContainsScreenPoint(
                sharpColorBordersToggle.GetComponent<RectTransform>(),
                pointer,
                eventCamera)
            || RectTransformUtility.RectangleContainsScreenPoint(
                spawnToggle.GetComponent<RectTransform>(),
                pointer,
                eventCamera)
            || RectTransformUtility.RectangleContainsScreenPoint(
                freezeToggle.GetComponent<RectTransform>(),
                pointer,
                eventCamera)
            || IsPointerOverTuningSlider(pointer, eventCamera);
    }

    private bool IsPointerOverTuningSlider(Vector2 pointer, Camera eventCamera)
    {
        foreach (var slider in collisionTuningSliders)
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(
                    slider.GetComponent<RectTransform>(),
                    pointer,
                    eventCamera))
            {
                return true;
            }
        }

        return false;
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
                    activeSyringe.HandleVelocity = 0f;
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
            if (activeSyringe != null)
            {
                activeSyringe.HandleVelocity = 0f;
                activeSyringe.EjectionVolumeCredit = 0f;
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

        var spawnDrops = clickSpawnEnabled
            && (Input.GetMouseButton(0) || game.handClicking)
            && heldContainer == null
            && activeSyringe == null
            && activeRotationContainer == null
            && !IsPointerOverInteractionControl(uiCasts)
            && !liquidSimulation.IsInsideContainer(pointerPosition);
        var deltaTime = Time.unscaledDeltaTime;
        liquidSimulation.Tick(deltaTime, spawnDrops, pointerPosition);
        controlsUpdateTimer -= deltaTime;
        if (controlsUpdateTimer <= 0f)
        {
            if (displayedDropCount != liquidSimulation.Count)
            {
                displayedDropCount = liquidSimulation.Count;
                var dropScope = drainOnly ? "outside the container" : "outside containers";
                controlsText.text =
                    $"Hold left-click {dropScope} to spawn drops ({displayedDropCount}/20000) | Right-click and drag to rotate | Esc: exit";
                if (receivingSyringe != null || sourceSyringe != null)
                {
                    controlsText.text =
                        "Drag the syringe plunger up to draw nearby liquid and down to inject through the needle | Esc: exit";
                }
            }

            UpdateVolumeReadout();
            controlsUpdateTimer = 0.25f;
        }

    }

    private void UpdateVolumeReadout()
    {
        if (drainOnly)
        {
            volumeText.text =
                "Liquid amount (1 drop per ml)\n"
                + $"Container: {liquidSimulation.GetContainerDropCount(0):N0} ml\n\n"
                + "Liquid outside the container is lost on exit.";
            return;
        }

        volumeText.text =
            "Original container sprites\n"
            + $"Container 1: {firstOriginalContainerSpriteName}\n"
            + $"Container 2: {secondOriginalContainerSpriteName}\n\n"
            + "Liquid amount (1 drop per ml)\n"
            + $"Receiver: {liquidSimulation.GetContainerDropCount(0):N0} ml\n"
            + $"Source: {liquidSimulation.GetContainerDropCount(1):N0} ml\n\n"
            + "Estimated 2D area (container outline)\n"
            + $"Receiver: {EstimateArea(receivingContainer):N0} units^2\n"
            + $"Source: {EstimateArea(sourceContainer):N0} units^2";
    }

    private static double EstimateArea(ContainerVisual container)
    {
        var scale = container.Transform.localScale;
        var scaleArea = System.Math.Abs((double)scale.x * scale.y);
        return EstimateContainerArea(container.OutlinePoints, container.InteriorWalls) * scaleArea;
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
        var fillRatio = syringe.Capacity > 0f
            ? Mathf.Clamp01(amount / syringe.Capacity)
            : 0f;
        syringe.HandleTransform.anchoredPosition = new Vector2(
            0f,
            Mathf.Lerp(
                syringe.Container.Geometry.FluidBottom.y,
                syringe.Container.Geometry.FluidTop.y,
                fillRatio));
        syringe.HandleVelocity = 0f;
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
        float handleY;
        if (requestedHandleY < currentHandleY)
        {
            var movementDeltaTime = Mathf.Min(deltaTime, 1f / 30f);
            var maximumDownwardSpeed = SyringeFreePistonSpeed;
            if (hasLiquidAtOutlet && syringe.Capacity > 0f)
            {
                var strokeHeight = geometry.FluidTop.y - geometry.FluidBottom.y;
                maximumDownwardSpeed = Mathf.Min(
                    maximumDownwardSpeed,
                    strokeHeight * SyringeEjectionRate / syringe.Capacity);
            }

            var maximumDownwardDistance = maximumDownwardSpeed * movementDeltaTime;
            handleY = Mathf.Max(requestedHandleY, currentHandleY - maximumDownwardDistance);
            syringe.HandleVelocity = 0f;
        }
        else
        {
            var handleVelocity = syringe.HandleVelocity;
            handleY = Mathf.SmoothDamp(
                currentHandleY,
                requestedHandleY,
                ref handleVelocity,
                SyringeHandleSmoothTime,
                Mathf.Infinity,
                deltaTime);
            syringe.HandleVelocity = handleVelocity;
        }

        syringe.HandleTransform.anchoredPosition = new Vector2(0f, handleY);
        liquidSimulation.SetSyringePistonPosition(syringe.ContainerIndex, handleY);
        var fillRatio = Mathf.InverseLerp(
            geometry.FluidBottom.y,
            geometry.FluidTop.y,
            handleY);
        var requestedAmount = fillRatio * syringe.Capacity;
        var currentAmount = liquidSimulation.GetContainerLiquidAmount(syringe.ContainerIndex);
        var difference = requestedAmount - currentAmount;
        var transferUnits = Mathf.FloorToInt(Mathf.Abs(difference) + 0.001f);
        var needleDirection3D = syringe.Container.Transform.localRotation
            * Vector3.Scale(Vector2.down, syringe.Container.Transform.localScale);
        var needleDirection = new Vector2(needleDirection3D.x, needleDirection3D.y).normalized;
        if (handleY <= geometry.FluidBottom.y + 0.01f)
        {
            syringe.EjectionVolumeCredit = 0f;
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
            if (hasLiquidAtOutlet)
            {
                syringe.EjectionVolumeCredit = Mathf.Min(
                    syringe.EjectionVolumeCredit + SyringeEjectionRate * deltaTime,
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
            if (transferUnits > 0)
            {
                var sourceIndex = otherContainerIndex;
                var destinationPoint = ToRootPoint(
                    syringe.Container,
                    new Vector2(0f, geometry.FluidBottom.y + localDropRadius));
                var maximumSourceDistance = LiquidSimulation.DefaultDropRadius * 3f;
                var remainingUnits = transferUnits;
                while (remainingUnits > 0)
                {
                    var moved = liquidSimulation.TryTransferNearestDrop(
                        sourceIndex,
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
                }
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
        if (targetCapacity <= 0f || referenceArea <= 0d || spriteArea <= 0d)
        {
            Plugin.LogWarning(
                $"Could not area-scale container '{originalSpriteName}': capacity={targetCapacity:F2}, "
                + $"reference area={referenceArea:F2}, sprite area={spriteArea:F2}; using base scale.");
            return;
        }

        var areaRatio = (targetCapacity / referenceCapacity) * (referenceArea / spriteArea);
        if (areaRatio <= 0d || double.IsNaN(areaRatio) || double.IsInfinity(areaRatio))
        {
            Plugin.LogWarning($"Could not calculate a valid area scale for container '{originalSpriteName}'.");
            return;
        }

        var linearScale = (float)System.Math.Sqrt(areaRatio);
        container.Transform.localScale = Vector3.one * (ContainerScale * linearScale);
    }

    private static double EstimateSpriteArea(Sprite sprite)
    {
        if (!sprite
            || !ModSprites.TryFindOpening(sprite, out var openingPosition, out var openingWidth)
            || !ModSprites.TryFindOutline(sprite, openingPosition, openingWidth, out var outline))
        {
            Plugin.LogWarning($"Could not calculate the reference area from sprite '{ReferenceSpriteName}.png'.");
            return 0d;
        }

        return EstimateContainerArea(
            outline,
            ModSprites.FindInteriorWalls(sprite, openingPosition, openingWidth));
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
        if (debugTextRoot)
        {
            var root = debugTextRoot.gameObject;
            root.SetActive(false);
            Object.Destroy(root);
            debugTextRoot = null;
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

        if (!receivingGameContainer || (!drainOnly && !sourceGameContainer))
        {
            throw new System.InvalidOperationException(
                "Cannot commit liquid simulation because a required game container is unavailable.");
        }

        liquidSimulation.GetInitialSimulatedLiquidAmounts(initialSimulatedLiquidAmounts);
        liquidSimulation.GetFinalSimulatedLiquidAmounts(finalSimulatedLiquidAmounts);
        var typeCount = liquidSimulation.LiquidTypeCount;
        var stride = liquidSimulation.LiquidTypeBufferLength;
        BuildTargetLiquidAmounts(receivingGameContainer, 0, typeCount, stride);
        if (!drainOnly)
        {
            BuildTargetLiquidAmounts(sourceGameContainer, 1, typeCount, stride);
        }

        var receiverCapacity = receivingGameContainer.Capacity;
        ValidateCapacity(receiverCapacity, "receiver capacity");
        var discardedOverflow = 0f;
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
            var sourceCapacity = sourceGameContainer.Capacity;
            ValidateCapacity(sourceCapacity, "source capacity");
            discardedOverflow = RouteLiquidCapacityOverflow(
                targetLiquidAmounts,
                typeCount,
                stride,
                receiverCapacity,
                sourceCapacity);
        }

        ApplyLiquidTargets(receivingGameContainer, 0, typeCount, stride, receivingDrainPlan);
        if (!drainOnly)
        {
            ApplyLiquidTargets(sourceGameContainer, 1, typeCount, stride, sourceDrainPlan);
        }

        transferCommitted = true;
        DebugLog.Write(drainOnly
            ? $"Committed liquid drain: amount={receivingGameContainer.CurrentTotal:F2}, "
                + $"outside liquid lost, capacity overflow discarded={discardedOverflow:F2} ml."
            : $"Committed liquid transfer: amounts receiver={receivingGameContainer.CurrentTotal:F2}, "
                + $"source={sourceGameContainer.CurrentTotal:F2}, "
                + $"capacity overflow discarded={discardedOverflow:F2} ml.");
    }

    private void BuildTargetLiquidAmounts(
        WaterContainerItem container,
        int containerIndex,
        int typeCount,
        int stride)
    {
        var currentTotal = container.CurrentTotal;
        ValidateAmount(currentTotal, $"container {containerIndex + 1} amount");
        var offset = containerIndex * stride;
        var compositionTotal = 0f;
        for (var type = 0; type < typeCount; type++)
        {
            var liquidId = liquidSimulation.GetLiquidId(type);
            var currentAmount = GetLiquidAmount(container, liquidId);
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
        rect.localScale = Vector3.one * ContainerScale;

        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = true;

        var hasOpening = ModSprites.TryFindOpening(sprite, out var openingPosition, out var openingWidth);
        var openingLeft = Vector2.zero;
        var openingRight = Vector2.zero;
        var outlinePoints = System.Array.Empty<Vector2>();
        var interiorWalls = System.Array.Empty<Vector2[]>();
        var collisionOutlinePoints = outlinePoints;
        var syringeGeometry = isSyringe ? CreateSyringeGeometry(sprite) : default;
        if (isSyringe)
        {
            openingPosition = syringeGeometry.OpeningCenter;
            openingLeft = syringeGeometry.OpeningLeft;
            openingRight = syringeGeometry.OpeningRight;
            openingWidth = Vector2.Distance(openingLeft, openingRight);
            hasOpening = true;
            outlinePoints = syringeGeometry.FluidOutline;
            collisionOutlinePoints = syringeGeometry.CollisionOutline;
        }
        else if (!hasOpening)
        {
            Plugin.LogWarning($"Could not detect the open rim of container sprite '{sprite.name}'.");
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

                interiorWalls = ModSprites.FindInteriorWalls(sprite, openingPosition, openingWidth);
                for (var index = 0; index < interiorWalls.Length; index++)
                {
                    var wall = new GameObject(
                        $"{name}InteriorWallDebugLine{index}",
                        typeof(RectTransform),
                        typeof(CanvasRenderer),
                        typeof(DebugPolylineGraphic)).GetComponent<DebugPolylineGraphic>();
                    var wallRect = wall.rectTransform;
                    wallRect.SetParent(rect, false);
                    wallRect.anchorMin = new Vector2(0.5f, 0.5f);
                    wallRect.anchorMax = new Vector2(0.5f, 0.5f);
                    wallRect.pivot = new Vector2(0.5f, 0.5f);
                    wallRect.sizeDelta = rect.sizeDelta;
                    wall.color = Color.blue;
                    wall.raycastTarget = false;
                    wall.SetPoints(interiorWalls[index]);
                }
            }
            else
            {
                Plugin.LogWarning($"Could not trace the outline of container sprite '{sprite.name}'.");
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

        if (isSyringe)
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

        if (!isSyringe)
        {
            collisionOutlinePoints = outlinePoints;
        }

        return new ContainerVisual(
            rect,
            openingPosition,
            openingLeft,
            openingRight,
            hasOpening,
            outlinePoints,
            interiorWalls,
            collisionOutlinePoints,
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
                + TransformOpeningPoint(container.Transform, container.Geometry.NeedlePoint);
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
            - TransformOpeningPoint(visual.Transform, pivotPoint);
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
        return ContainerShapeCollision.Overlaps(
            moving.CollisionOutlinePoints,
            movingTransform.anchoredPosition,
            movingTransform.localScale.x,
            movingTransform.localEulerAngles.z,
            other.CollisionOutlinePoints,
            otherTransform.anchoredPosition,
            otherTransform.localScale.x,
            otherTransform.localEulerAngles.z);
    }

    private void StartPivotTracking(Vector2 pointer)
    {
        var visual = activeRotationContainer;
        var pivotPoint = GetRotationPivotPoint(visual);
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
        }

        internal ContainerVisual Container { get; }
        internal int ContainerIndex { get; }
        internal float Capacity { get; }
        internal RectTransform HandleTransform { get; }
        internal float HandleVelocity { get; set; }
        internal float EjectionVolumeCredit { get; set; }
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
