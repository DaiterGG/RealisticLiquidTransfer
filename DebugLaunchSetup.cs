#if LIQUID_TRANSFER_DEBUG_LAUNCH
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RealisticLiquidTransfer;

[HarmonyPatch(typeof(PreRunScript), "Start")]
internal static class DebugLaunchMenuPatch
{
    private static bool _launchStarted;
    internal static bool BasicTrainingSetupPending { get; private set; }

    [HarmonyPostfix]
    private static void AfterStart(PreRunScript __instance)
    {
        if (_launchStarted || !__instance)
        {
            return;
        }

        _launchStarted = true;
        if (MultiplayerTransferBridge.IsMultiplayerSessionRunning)
        {
            Plugin.LogInfo("Debug startup: skipped tutorial because a multiplayer session is active.");
            return;
        }

        if (__instance.contentWarning)
        {
            __instance.contentWarning.SetActive(false);
        }

        if (__instance.tutorialWarning)
        {
            __instance.tutorialWarning.SetActive(false);
        }

        BasicTrainingSetupPending = true;
        SaveSystem.loadedRun = false;
        PlayerPrefs.SetInt("tutorial", 1);
        PlayerPrefs.SetInt("radlinedisable", 0);
        WorldGeneration.runSettings = null;
        __instance.StartCoroutine(__instance.WaitLoad());
        Plugin.LogInfo("Debug startup: loading tutorial because multiplayer is not running.");
    }

    internal static void ConsumeBasicTrainingSetup()
    {
        BasicTrainingSetupPending = false;
    }
}

[HarmonyPatch(typeof(TutorialHandler), "Start")]
internal static class DebugLaunchTutorialPatch
{
    private static int overflowItemCount;

    [HarmonyPostfix]
    private static void AfterStart(TutorialHandler __instance)
    {
        if (!DebugLaunchMenuPatch.BasicTrainingSetupPending)
        {
            return;
        }

        if (!__instance)
        {
            Plugin.LogError("Debug startup: tutorial handler is unavailable.");
            return;
        }

        if (!SelectBasicTrainingCourse(__instance))
        {
            return;
        }

        var body = PlayerCamera.main ? PlayerCamera.main.body : null;
        if (!body)
        {
            return;
        }

        DebugLaunchMenuPatch.ConsumeBasicTrainingSetup();
        SpawnDebugLoadout(body);
    }

    internal static bool SelectBasicTrainingCourse(TutorialHandler tutorialHandler)
    {
        if (!tutorialHandler)
        {
            return false;
        }

        var alreadySelected = tutorialHandler.activeCourse is BasicCourse;
        if (!alreadySelected)
        {
            if (tutorialHandler.activeCourse)
            {
                tutorialHandler.EndCourse();
            }

            tutorialHandler.StartCourse(typeof(BasicCourse));
        }

        if (tutorialHandler.activeCourse is not BasicCourse)
        {
            Plugin.LogError("Debug startup: could not start Basic Training.");
            return false;
        }

        if (tutorialHandler.courseSelectScreen)
        {
            tutorialHandler.courseSelectScreen.SetActive(false);
        }

        if (!alreadySelected)
        {
            Plugin.LogInfo("Debug startup: selected Basic Training.");
        }

        return true;
    }

    internal static void SpawnDebugLoadout(Body body)
    {
        overflowItemCount = 0;
        var spawnedCount = 0;
        if (GiveFunnel(body))
        {
            spawnedCount++;
        }

        if (GiveContainer(body, "syringe", System.Array.Empty<LiquidMix>(), "syringe"))
        {
            spawnedCount++;
        }

        if (GiveContainer(body, "waterjug", new[] { new LiquidMix("water", 1f) }, "waterjug"))
        {
            spawnedCount++;
        }

        if (GiveContainer(body, "waterbottle", System.Array.Empty<LiquidMix>(), "waterbottle"))
        {
            spawnedCount++;
        }

        foreach (var mixture in TestLiquidMixtures)
        {
            if (GiveContainer(body, "minibarrel", mixture, "minibarreltop"))
            {
                spawnedCount++;
            }
        }

        foreach (var mixture in TestLiquidMixtures)
        {
            if (GiveContainer(body, "waterbottle", mixture))
            {
                spawnedCount++;
            }
        }

        foreach (var preview in AdditionalContainerPreviews)
        {
            if (GiveContainerPreview(body, preview))
            {
                spawnedCount++;
            }
        }

        Plugin.LogInfo(
            $"Debug startup: spawned {spawnedCount} of "
            + $"{4 + TestLiquidMixtures.Length * 2 + AdditionalContainerPreviews.Length} requested "
            + $"funnel, syringe, container, and sprite-preview items ({spawnedCount - overflowItemCount} equipped, "
            + $"{overflowItemCount} placed beside the player).");
    }

    private static readonly DebugContainerPreview[] AdditionalContainerPreviews =
    {
        new DebugContainerPreview("alcohol", "alcohol"),
        new DebugContainerPreview("applejuice", "applejuice"),
        new DebugContainerPreview("bleach", "bleach"),
        new DebugContainerPreview("bowlofcereal", "bowlofcereal"),
        new DebugContainerPreview("canteen", "canteen"),
        new DebugContainerPreview("chocolatemilk", "chocolatemilk"),
        new DebugContainerPreview("coffee", "coffee"),
        new DebugContainerPreview("waterbottle", "default"),
        new DebugContainerPreview("waterbottle", "default0-150"),
        new DebugContainerPreview("waterbottle", "default151-350"),
        new DebugContainerPreview("waterbottle", "default2001-5000"),
        new DebugContainerPreview("waterbottle", "default351-750"),
        new DebugContainerPreview("waterbottle", "default5001-999999"),
        new DebugContainerPreview("waterbottle", "default751-2000"),
        new DebugContainerPreview("disinfectant", "disinfectant"),
        new DebugContainerPreview("alcohol", "emptyjar"),
        new DebugContainerPreview("energydrink", "energydrink"),
        new DebugContainerPreview("icetea", "icetea"),
        new DebugContainerPreview("ketchup", "ketchup"),
        new DebugContainerPreview("liquidcentrifuge", "liquidcentrifuge"),
        new DebugContainerPreview("lrd", "lrd"),
        new DebugContainerPreview("makeshiftlrd", "makeshiftlrd"),
        new DebugContainerPreview("milk", "milk"),
        new DebugContainerPreview("sodacan", "sodacan"),
        new DebugContainerPreview("soup", "soup"),
        new DebugContainerPreview("spraybottle", "spraybottle"),
        new DebugContainerPreview("liquidpouch", "waterpouch")
    };

    private static readonly LiquidMix[][] TestLiquidMixtures =
    {
        new[]
        {
            new LiquidMix("Clean water", 0.34f),
            new LiquidMix("Coffee", 0.33f),
            new LiquidMix("Dirty water", 0.33f)
        },
        new[]
        {
            new LiquidMix("Alcohol", 0.30f),
            new LiquidMix("Oil", 0.30f),
            new LiquidMix("Clean water", 0.40f)
        },
        new[]
        {
            new LiquidMix("Coffee", 0.35f),
            new LiquidMix("Milk", 0.35f),
            new LiquidMix("Red blood", 0.30f)
        },
        new[]
        {
            new LiquidMix("Clean water", 0.60f),
            new LiquidMix("Red sludge", 0.20f),
            new LiquidMix("Mercury", 0.20f)
        }
    };

    private static bool GiveContainerPreview(Body body, DebugContainerPreview preview)
    {
        var prefab = Resources.Load<GameObject>(preview.ItemId);
        if (!prefab || !prefab.GetComponent<WaterContainerItem>())
        {
            Plugin.LogError(
                $"Debug startup: sprite preview '{preview.SpriteName}' has no liquid-container prefab "
                + $"'{preview.ItemId}'.");
            return false;
        }

        return GiveContainer(
            body,
            preview.ItemId,
            System.Array.Empty<LiquidMix>(),
            preview.SpriteName);
    }

    private static bool GiveContainer(
        Body body,
        string itemId,
        LiquidMix[] mixture,
        string spriteName = null)
    {
        var prefab = Resources.Load<GameObject>(itemId);
        if (!prefab)
        {
            Plugin.LogError($"Debug startup: item prefab '{itemId}' was not found.");
            return false;
        }

        var instance = Object.Instantiate(prefab, body.transform.position, Quaternion.identity);
        var item = instance.GetComponent<Item>();
        var container = instance.GetComponent<WaterContainerItem>();
        if (!item || !container)
        {
            Object.Destroy(instance);
            Plugin.LogError($"Debug startup: '{itemId}' is missing its item or liquid-container component.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(spriteName))
        {
            var spriteRenderer = instance.GetComponent<SpriteRenderer>();
            if (!spriteRenderer)
            {
                spriteRenderer = instance.GetComponentInChildren<SpriteRenderer>();
            }

            if (!spriteRenderer)
            {
                Object.Destroy(instance);
                Plugin.LogError(
                    $"Debug startup: '{itemId}' has no sprite renderer for preview '{spriteName}'.");
                return false;
            }

            spriteRenderer.sprite = ModSprites.GetSprite(spriteName);
        }

        var slotIndex = FindEmptySlot(body, item.Stats.onlyHoldInHands);
        var inventorySlotAvailable = slotIndex >= 0;

        container.DrainAll();
        foreach (var portion in mixture)
        {
            if (!TryResolveLiquidId(portion.LiquidName, out var liquidId))
            {
                Object.Destroy(instance);
                return false;
            }

            var amount = container.Capacity * portion.Fraction;
            var added = container.AddLiquid(liquidId, amount);
            if (Mathf.Abs(added - amount) > 0.001f)
            {
                Object.Destroy(instance);
                Plugin.LogError(
                    $"Debug startup: '{itemId}' accepted {added:0.###} of {amount:0.###} mL "
                    + $"of '{liquidId}'.");
                return false;
            }
        }

        if (!inventorySlotAvailable)
        {
            PlaceOverflowItem(body, instance);
            Plugin.LogWarning(
                $"Debug startup: inventory is full; spawned '{itemId}' beside the player.");
            Plugin.LogInfo(
                $"Debug startup: spawned '{itemId}' beside the player with {container.CurrentTotal:0} mL.");
            return true;
        }

        body.PickUpItem(item, slotIndex, force: true);
        if (body.GetItem(slotIndex) != item)
        {
            Object.Destroy(instance);
            Plugin.LogError($"Debug startup: could not equip '{itemId}'.");
            return false;
        }

        Plugin.LogInfo(
            $"Debug startup: equipped '{itemId}' in slot {slotIndex} with {container.CurrentTotal:0} mL.");
        return true;
    }

    private static bool GiveFunnel(Body body)
    {
        var instance = FunnelItem.Create(body.transform.position, 0f);
        var item = instance.GetComponent<Item>();
        if (!item)
        {
            Object.Destroy(instance);
            Plugin.LogError("Debug startup: the funnel is missing its item component.");
            return false;
        }

        var slotIndex = FindEmptySlot(body, item.Stats.onlyHoldInHands);
        if (slotIndex < 0)
        {
            PlaceOverflowItem(body, instance);
            Plugin.LogWarning(
                "Debug startup: inventory is full; spawned the funnel beside the player.");
            return true;
        }

        body.PickUpItem(item, slotIndex, force: true);
        if (body.GetItem(slotIndex) != item)
        {
            Object.Destroy(instance);
            Plugin.LogError("Debug startup: could not equip the funnel.");
            return false;
        }

        Plugin.LogInfo($"Debug startup: equipped '{FunnelItem.Id}' in slot {slotIndex}.");
        return true;
    }

    private static void PlaceOverflowItem(Body body, GameObject instance)
    {
        const int ItemsPerRow = 6;
        instance.transform.position = body.transform.position
            + new Vector3(
                3f + 2f * (overflowItemCount % ItemsPerRow),
                2f * (overflowItemCount / ItemsPerRow),
                0f);
        overflowItemCount++;
    }

    private static bool TryResolveLiquidId(string liquidName, out string liquidId)
    {
        liquidId = null;
        if (Liquids.Registry == null)
        {
            Plugin.LogError("Debug startup: liquid registry is unavailable.");
            return false;
        }

        if (Liquids.Registry.ContainsKey(liquidName))
        {
            liquidId = liquidName;
            return true;
        }

        foreach (var entry in Liquids.Registry)
        {
            if (LiquidDensityCatalog.MatchesIdentifier(entry.Key, liquidName))
            {
                liquidId = entry.Key;
                return true;
            }
        }

        Plugin.LogError($"Debug startup: liquid '{liquidName}' was not found in the game registry.");
        return false;
    }

    private static int FindEmptySlot(Body body, bool handsOnly)
    {
        for (var i = 0; i < body.slots.Length; i++)
        {
            var slot = body.slots[i];
            if (slot && slot.canPickUp && !body.HoldingItem(i) && (!handsOnly || slot.isHand))
            {
                return i;
            }
        }

        return -1;
    }

    private readonly struct LiquidMix
    {
        internal LiquidMix(string liquidName, float fraction)
        {
            LiquidName = liquidName;
            Fraction = fraction;
        }

        internal string LiquidName { get; }
        internal float Fraction { get; }
    }

    private readonly struct DebugContainerPreview
    {
        internal DebugContainerPreview(string itemId, string spriteName)
        {
            ItemId = itemId;
            SpriteName = spriteName;
        }

        internal string ItemId { get; }
        internal string SpriteName { get; }
    }
}

[HarmonyPatch(typeof(PlayerCamera), "Update")]
internal static class DebugLaunchPlayerUpdatePatch
{
    [HarmonyPostfix]
    private static void AfterUpdate(PlayerCamera __instance)
    {
        if (!DebugLaunchMenuPatch.BasicTrainingSetupPending
            || SceneManager.GetActiveScene().name != "SampleScene")
        {
            return;
        }

        var body = __instance ? __instance.body : null;
        var tutorialHandler = TutorialHandler.main;
        if (!body || !DebugLaunchTutorialPatch.SelectBasicTrainingCourse(tutorialHandler))
        {
            return;
        }

        DebugLaunchMenuPatch.ConsumeBasicTrainingSetup();
        Plugin.LogInfo("Debug startup: gameplay player is ready; spawning the requested loadout.");
        DebugLaunchTutorialPatch.SpawnDebugLoadout(body);
    }
}
#endif
