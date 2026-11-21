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
    private const string ReferenceSpriteName = "coffee";
    private const float ReferenceCapacity = 250f;
    private static float referenceCapacity = ReferenceCapacity;
    private const string ShaderEnabledPref = "RealisticLiquidTransfer.ShaderEnabled";
    private const string ClickSpawnEnabledPref = "RealisticLiquidTransfer.ClickSpawnEnabled";
    private const string CollisionPassesPref = "RealisticLiquidTransfer.CollisionPasses";
    private const string CollisionCorrectionPref = "RealisticLiquidTransfer.CollisionCorrection";
    private const string VelocityContactMarginPref = "RealisticLiquidTransfer.VelocityContactMargin";
    private const string DropRadiusPref = "RealisticLiquidTransfer.DropRadiusTenths.v3";
    private const string FreezeSimulationPref = "RealisticLiquidTransfer.FreezeSimulation";
    private const string InitialPackingPref = "RealisticLiquidTransfer.InitialPackingTenths";

    private static bool savedShaderEnabled = true;
    private static bool savedClickSpawnEnabled;
    private static int savedCollisionPasses = LiquidSimulation.DefaultDropCollisionPasses;
    private static int savedCollisionCorrection = LiquidSimulation.DefaultCollisionCorrectionPercent;
    private static int savedVelocityContactMargin =
        LiquidSimulation.DefaultVelocityContactMarginTenths;
    private static int savedDropRadiusTenths = LiquidSimulation.DefaultDropRadiusTenths;
    private static bool savedFreezeSimulation;
    private static int savedInitialPackingTenths = LiquidSimulation.DefaultPackingTenths;

    private readonly WaterContainerItem receivingGameContainer;
    private readonly WaterContainerItem sourceGameContainer;
    private readonly string firstOriginalContainerSpriteName;
    private readonly string secondOriginalContainerSpriteName;
    private readonly float firstOriginalContainerCapacity;
    private readonly float secondOriginalContainerCapacity;
    private readonly float firstOriginalContainerAmount;
    private readonly float secondOriginalContainerAmount;
    private Image handImage;
    private Text controlsText;
    private Text volumeText;
    private RectTransform screenRoot;
    private Toggle shaderToggle;
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

    internal LiquidTransferMinigame(
        WaterContainerItem receivingGameContainer,
        string firstOriginalContainerSpriteName,
        float firstOriginalContainerCapacity,
        float firstOriginalContainerAmount,
        WaterContainerItem sourceGameContainer,
        string secondOriginalContainerSpriteName,
        float secondOriginalContainerCapacity,
        float secondOriginalContainerAmount)
    {
        this.receivingGameContainer = receivingGameContainer;
        this.sourceGameContainer = sourceGameContainer;
        this.firstOriginalContainerSpriteName = firstOriginalContainerSpriteName;
        this.firstOriginalContainerCapacity = firstOriginalContainerCapacity;
        this.firstOriginalContainerAmount = firstOriginalContainerAmount;
        this.secondOriginalContainerSpriteName = secondOriginalContainerSpriteName;
        this.secondOriginalContainerCapacity = secondOriginalContainerCapacity;
        this.secondOriginalContainerAmount = secondOriginalContainerAmount;
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
        LoadPersistentSettings();
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
        volumeText = CreateVolumeText(debugTextRoot);
        var rightControlsX = Mathf.Max(24f, debugTextRoot.rect.width - 210f);
        shaderToggle = CreateToggle(
            debugTextRoot,
            "LiquidShaderToggle",
            "Liquid shader",
            new Vector2(rightControlsX, -24f));
        shaderToggle.isOn = savedShaderEnabled;
        spawnToggle = CreateToggle(
            debugTextRoot,
            "ClickSpawnToggle",
            "Spawn drops on click",
            new Vector2(rightControlsX, -52f));
        spawnToggle.isOn = savedClickSpawnEnabled;
        freezeToggle = CreateToggle(
            debugTextRoot,
            "FreezeSimulationToggle",
            "Freeze simulation",
            new Vector2(rightControlsX, -298f));
        freezeToggle.isOn = savedFreezeSimulation;
        collisionPassSlider = CreateTuningSlider(
            debugTextRoot,
            "CollisionPasses",
            "Collision passes",
            new Vector2(rightControlsX, -78f),
            new Vector2(rightControlsX, -104f),
            LiquidSimulation.MinimumDropCollisionPasses,
            LiquidSimulation.MaximumDropCollisionPasses,
            savedCollisionPasses,
            true,
            out collisionPassText);
        collisionCorrectionSlider = CreateTuningSlider(
            debugTextRoot,
            "CollisionCorrection",
            "Position correction",
            new Vector2(rightControlsX, -132f),
            new Vector2(rightControlsX, -158f),
            LiquidSimulation.MinimumCollisionCorrectionPercent,
            LiquidSimulation.MaximumCollisionCorrectionPercent,
            savedCollisionCorrection,
            true,
            out collisionCorrectionText);
        velocityContactMarginSlider = CreateTuningSlider(
            debugTextRoot,
            "VelocityContactMargin",
            "Velocity contact margin",
            new Vector2(rightControlsX, -186f),
            new Vector2(rightControlsX, -212f),
            LiquidSimulation.MinimumVelocityContactMarginTenths,
            LiquidSimulation.MaximumVelocityContactMarginTenths,
            savedVelocityContactMargin,
            true,
            out velocityContactMarginText);
        dropRadiusSlider = CreateTuningSlider(
            debugTextRoot,
            "DropRadius",
            "Base drop radius",
            new Vector2(rightControlsX, -240f),
            new Vector2(rightControlsX, -266f),
            LiquidSimulation.MinimumDropRadiusTenths,
            LiquidSimulation.MaximumDropRadiusTenths,
            savedDropRadiusTenths,
            false,
            out dropRadiusText);
        initialPackingSlider = CreateTuningSlider(
            debugTextRoot,
            "InitialPacking",
            "Initial packing",
            new Vector2(rightControlsX, -320f),
            new Vector2(rightControlsX, -346f),
            LiquidSimulation.MinimumPackingTenths,
            LiquidSimulation.MaximumPackingTenths,
            savedInitialPackingTenths,
            false,
            out initialPackingText);

        receivingContainer = CreateContainer(
            screenRoot,
            "ReceivingContainer",
            ModSprites.GetContainer(firstOriginalContainerSpriteName),
            Vector2.zero,
            0f);
        sourceContainer = CreateContainer(
            screenRoot,
            "SourceContainer",
            ModSprites.GetContainer(secondOriginalContainerSpriteName),
            Vector2.zero,
            0f);
        var referenceArea = EstimateSpriteArea(ModSprites.GetContainer(ReferenceSpriteName));
        ApplyAreaScale(
            receivingContainer,
            firstOriginalContainerCapacity,
            referenceArea,
            firstOriginalContainerSpriteName);
        ApplyAreaScale(
            sourceContainer,
            secondOriginalContainerCapacity,
            referenceArea,
            secondOriginalContainerSpriteName);
        PositionContainersSideBySide(receivingContainer, sourceContainer);

        handImage = game.handTransform.GetComponent<Image>();
        originalHandSize = handImage.rectTransform.sizeDelta;
        originalPreserveAspect = handImage.preserveAspect;

        fitScale = GetFitScale(game.minigameScreen, screenRoot, receivingContainer, sourceContainer);
        screenRoot.localScale = Vector3.one * fitScale;
        liquidSimulation = new LiquidSimulation(
            screenRoot,
            receivingContainer.Transform,
            receivingContainer.OutlinePoints,
            firstOriginalContainerCapacity,
            sourceContainer.Transform,
            sourceContainer.OutlinePoints,
            secondOriginalContainerCapacity,
            firstOriginalContainerAmount,
            secondOriginalContainerAmount,
            savedInitialPackingTenths / 100f);
        shaderToggle.onValueChanged.AddListener(SetLiquidShaderEnabled);
        SetLiquidShaderEnabled(shaderToggle.isOn);
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

    private static void LoadPersistentSettings()
    {
        savedShaderEnabled = PlayerPrefs.GetInt(ShaderEnabledPref, 1) != 0;
        savedClickSpawnEnabled = PlayerPrefs.GetInt(ClickSpawnEnabledPref, 0) != 0;
        savedCollisionPasses = Mathf.Clamp(
            PlayerPrefs.GetInt(CollisionPassesPref, LiquidSimulation.DefaultDropCollisionPasses),
            LiquidSimulation.MinimumDropCollisionPasses,
            LiquidSimulation.MaximumDropCollisionPasses);
        savedCollisionCorrection = Mathf.Clamp(
            PlayerPrefs.GetInt(CollisionCorrectionPref, LiquidSimulation.DefaultCollisionCorrectionPercent),
            LiquidSimulation.MinimumCollisionCorrectionPercent,
            LiquidSimulation.MaximumCollisionCorrectionPercent);
        savedVelocityContactMargin = Mathf.Clamp(
            PlayerPrefs.GetInt(
                VelocityContactMarginPref,
                LiquidSimulation.DefaultVelocityContactMarginTenths),
            LiquidSimulation.MinimumVelocityContactMarginTenths,
            LiquidSimulation.MaximumVelocityContactMarginTenths);
        savedDropRadiusTenths = Mathf.Clamp(
            PlayerPrefs.GetInt(DropRadiusPref, LiquidSimulation.DefaultDropRadiusTenths),
            LiquidSimulation.MinimumDropRadiusTenths,
            LiquidSimulation.MaximumDropRadiusTenths);
        savedInitialPackingTenths = Mathf.Clamp(
            PlayerPrefs.GetInt(InitialPackingPref, LiquidSimulation.DefaultPackingTenths),
            LiquidSimulation.MinimumPackingTenths,
            LiquidSimulation.MaximumPackingTenths);
        savedFreezeSimulation = PlayerPrefs.GetInt(FreezeSimulationPref, 0) != 0;
    }

    private void SetLiquidShaderEnabled(bool enabled)
    {
        savedShaderEnabled = enabled;
        PlayerPrefs.SetInt(ShaderEnabledPref, enabled ? 1 : 0);
        PlayerPrefs.Save();
        liquidSimulation.SetSurfaceShaderEnabled(enabled);
    }

    private void SetCollisionPasses(float passes)
    {
        var passCount = Mathf.RoundToInt(passes);
        savedCollisionPasses = passCount;
        PlayerPrefs.SetInt(CollisionPassesPref, passCount);
        PlayerPrefs.Save();
        liquidSimulation.SetDropCollisionPasses(passCount);
        collisionPassText.text = $"Collision passes: {passCount}";
    }

    private void SetCollisionCorrection(float percent)
    {
        var correction = Mathf.RoundToInt(percent);
        savedCollisionCorrection = correction;
        PlayerPrefs.SetInt(CollisionCorrectionPref, correction);
        PlayerPrefs.Save();
        liquidSimulation.SetCollisionCorrectionPercent(correction);
        collisionCorrectionText.text = $"Position correction: {correction}%";
    }

    private void SetVelocityContactMargin(float tenths)
    {
        var marginTenths = Mathf.RoundToInt(tenths);
        var margin = marginTenths / 10f;
        savedVelocityContactMargin = marginTenths;
        PlayerPrefs.SetInt(VelocityContactMarginPref, marginTenths);
        PlayerPrefs.Save();
        liquidSimulation.SetVelocityContactMarginTenths(marginTenths);
        velocityContactMarginText.text = $"Velocity contact margin: {margin:F1}px";
    }

    private void SetDropRadius(float tenths)
    {
        var radiusTenths = Mathf.RoundToInt(tenths);
        var radius = radiusTenths / 10f;
        savedDropRadiusTenths = radiusTenths;
        PlayerPrefs.SetInt(DropRadiusPref, radiusTenths);
        PlayerPrefs.Save();
        liquidSimulation.SetDropRadius(radius);
        dropRadiusText.text = $"Base drop radius: {radius:F1}px";
    }

    private void SetInitialPacking(float tenths)
    {
        var packingTenths = Mathf.RoundToInt(tenths);
        var packing = packingTenths / 100f;
        savedInitialPackingTenths = packingTenths;
        PlayerPrefs.SetInt(InitialPackingPref, packingTenths);
        PlayerPrefs.Save();
        liquidSimulation.SetInitialPacking(packing);
        initialPackingText.text = $"Initial packing: {packing:F2}x";
    }

    private void SetFreezeSimulation(bool enabled)
    {
        savedFreezeSimulation = enabled;
        PlayerPrefs.SetInt(FreezeSimulationPref, enabled ? 1 : 0);
        PlayerPrefs.Save();
        liquidSimulation.SetSimulationFrozen(enabled);
    }

    private void SetClickSpawnEnabled(bool enabled)
    {
        savedClickSpawnEnabled = enabled;
        PlayerPrefs.SetInt(ClickSpawnEnabledPref, enabled ? 1 : 0);
        PlayerPrefs.Save();
        clickSpawnEnabled = enabled;
    }

    private bool IsPointerOverInteractionControl(List<RaycastResult> uiCasts)
    {
        foreach (var uiCast in uiCasts)
        {
            var selectable = uiCast.gameObject.GetComponentInParent<Selectable>();
            if (selectable == shaderToggle
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

        var spawnDrops = clickSpawnEnabled
            && (Input.GetMouseButton(0) || game.handClicking)
            && heldContainer == null
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
                controlsText.text =
                    $"Hold left-click outside containers to spawn drops ({displayedDropCount}/20000) | Right-click and drag to rotate | Esc: exit";
            }

            UpdateVolumeReadout();
            controlsUpdateTimer = 0.25f;
        }

    }

    private void UpdateVolumeReadout()
    {
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
        return EstimateOutlineArea(container.OutlinePoints) * scaleArea;
    }

    private static void ApplyAreaScale(
        ContainerVisual container,
        float targetCapacity,
        double referenceArea,
        string originalSpriteName)
    {
        var spriteArea = EstimateOutlineArea(container.OutlinePoints);
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

        return EstimateOutlineArea(outline);
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
                && liquidSimulation.IsInsideContainer(pointerPosition))
            {
                return receivingContainer;
            }

            if (uiCast.gameObject == sourceContainer.Transform.gameObject
                && liquidSimulation.IsInsideContainer(pointerPosition))
            {
                return sourceContainer;
            }
        }

        return null;
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

        if (!receivingGameContainer || !sourceGameContainer)
        {
            throw new System.InvalidOperationException(
                "Cannot commit liquid transfer because one or both game containers are unavailable.");
        }

        var receiverAmount = receivingGameContainer.CurrentTotal;
        var sourceAmount = sourceGameContainer.CurrentTotal;
        var receiverCapacity = receivingGameContainer.Capacity;
        var sourceCapacity = sourceGameContainer.Capacity;
        ValidateAmount(receiverAmount, "receiver amount");
        ValidateAmount(sourceAmount, "source amount");
        ValidateCapacity(receiverCapacity, "receiver capacity");
        ValidateCapacity(sourceCapacity, "source capacity");

        var finalDropCounts = liquidSimulation.GetFinalContainerDropCounts();
        var targetReceiverAmount = receiverAmount
            + finalDropCounts[0]
            - liquidSimulation.GetInitialContainerDropCount(0);
        var targetSourceAmount = sourceAmount
            + finalDropCounts[1]
            - liquidSimulation.GetInitialContainerDropCount(1);
        var discardedOverflow = RouteCapacityOverflow(
            ref targetReceiverAmount,
            receiverCapacity,
            ref targetSourceAmount,
            sourceCapacity);

        ApplyLiquidAmounts(
            receivingGameContainer,
            sourceGameContainer,
            targetReceiverAmount,
            targetSourceAmount);
        transferCommitted = true;
        DebugLog.Write(
            $"Committed liquid transfer: drops receiver={finalDropCounts[0]}, source={finalDropCounts[1]}; "
            + $"amounts receiver={receivingGameContainer.CurrentTotal:F2}, "
            + $"source={sourceGameContainer.CurrentTotal:F2}, "
            + $"capacity overflow discarded={discardedOverflow:F2} ml.");
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
