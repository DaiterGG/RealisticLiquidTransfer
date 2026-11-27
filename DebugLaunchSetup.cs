#if LIQUID_TRANSFER_DEBUG_LAUNCH
using System;
using System.Collections.Generic;
using HarmonyLib;
using Object = UnityEngine.Object;
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
    private static readonly string[] ContainerSpriteNames =
    {
        "alcohol",
        "applejuice",
        "bleach",
        "bowlofcereal",
        "canteen",
        "chocolatemilk",
        "coffee",
        "default",
        "default0-150",
        "default151-350",
        "default2001-5000",
        "default351-750",
        "default5001-999999",
        "default751-2000",
        "disinfectant",
        "emptyjar",
        "energydrink",
        "funnel",
        "icetea",
        "ketchup",
        "liquidcentrifuge",
        "lrd",
        "makeshiftlrd",
        "milk",
        "minibarreltop",
        "sodacan",
        "soup",
        "spraybottle",
        "syringe",
        "syringehandle",
        "waterbottle",
        "waterjug",
        "waterpouch"
    };
    private static readonly ContainerMaterial[] ContainerMaterials =
    {
        ContainerMaterial.Plastic,
        ContainerMaterial.Glass,
        ContainerMaterial.Metal,
        ContainerMaterial.Cardboard,
        ContainerMaterial.Ceramic,
        ContainerMaterial.Aluminium
    };

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
        var coveredSpriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var coveredMaterials = new HashSet<ContainerMaterial>();
        if (GiveFunnel(body))
        {
            spawnedCount++;
            coveredSpriteNames.Add("funnel");
        }

        if (GiveContainer(body, "syringe", Array.Empty<LiquidMix>()))
        {
            spawnedCount++;
            RecordContainerCoverage(
                "syringe",
                "syringehandle",
                "syringe",
                coveredSpriteNames,
                coveredMaterials);
        }

        if (GiveContainer(body, "waterjug", new[] { new LiquidMix("water", 1f) }))
        {
            spawnedCount++;
            RecordContainerCoverage(
                "waterjug",
                null,
                "waterjug",
                coveredSpriteNames,
                coveredMaterials);
        }

        if (GiveContainer(body, "waterbottle", Array.Empty<LiquidMix>()))
        {
            spawnedCount++;
            RecordContainerCoverage(
                "waterbottle",
                null,
                "waterbottle",
                coveredSpriteNames,
                coveredMaterials);
        }

        foreach (var mixture in TestLiquidMixtures)
        {
            if (GiveContainer(body, "minibarrel", mixture))
            {
                spawnedCount++;
                RecordContainerCoverage(
                    "minibarreltop",
                    null,
                    "minibarrel",
                    coveredSpriteNames,
                    coveredMaterials);
            }
        }

        foreach (var mixture in TestLiquidMixtures)
        {
            if (GiveContainer(body, "waterbottle", mixture))
            {
                spawnedCount++;
                RecordContainerCoverage(
                    "waterbottle",
                    null,
                    "waterbottle",
                    coveredSpriteNames,
                    coveredMaterials);
            }
        }

        var previewCount = SpawnContainerPreviews(body, coveredSpriteNames, coveredMaterials);
        spawnedCount += previewCount;

        Plugin.LogInfo(
            $"Debug startup: spawned {spawnedCount} game items, including {previewCount} additional "
            + $"actual-container previews ({spawnedCount - overflowItemCount} equipped, "
            + $"{overflowItemCount} placed beside the player).");
    }

    private static void RecordContainerCoverage(
        string spriteName,
        string additionalSpriteName,
        string itemId,
        HashSet<string> coveredSpriteNames,
        HashSet<ContainerMaterial> coveredMaterials)
    {
        coveredSpriteNames.Add(spriteName);
        if (!string.IsNullOrWhiteSpace(additionalSpriteName))
        {
            coveredSpriteNames.Add(additionalSpriteName);
        }

        var material = ContainerMaterialCatalog.GetMaterial(itemId);
        if (material != ContainerMaterial.Unknown)
        {
            coveredMaterials.Add(material);
        }
    }

    private static int SpawnContainerPreviews(
        Body body,
        HashSet<string> coveredSpriteNames,
        HashSet<ContainerMaterial> coveredMaterials)
    {
        if (Item.GlobalItems == null)
        {
            Plugin.LogError("Debug startup: item definitions are unavailable; cannot discover containers.");
            return 0;
        }

        var expectedSpriteNames = new HashSet<string>(
            ContainerSpriteNames,
            StringComparer.OrdinalIgnoreCase);
        var itemIds = new List<string>(Item.GlobalItems.Keys);
        itemIds.Sort(StringComparer.OrdinalIgnoreCase);
        var spawnedCount = 0;
        foreach (var itemId in itemIds)
        {
            var prefab = Resources.Load<GameObject>(itemId);
            if (!prefab)
            {
                continue;
            }

            var item = prefab.GetComponent<Item>();
            var container = prefab.GetComponent<WaterContainerItem>();
            var spriteRenderer = prefab.GetComponent<SpriteRenderer>();
            if (!spriteRenderer)
            {
                spriteRenderer = prefab.GetComponentInChildren<SpriteRenderer>();
            }

            if (!item || !container || !spriteRenderer || !spriteRenderer.sprite)
            {
                continue;
            }

            var originalSpriteName =
                StartLiquidTransferPatch.GetContainerSpriteName(container, $"debug item '{itemId}'");
            float capacity;
            try
            {
                capacity = container.Capacity;
            }
            catch (NullReferenceException exception)
            {
                Plugin.LogError(
                    $"Debug startup: failed to read capacity for container prefab '{itemId}' "
                    + $"(prefab name='{prefab.name}'): {exception}");
                continue;
            }

            var minigameSpriteName =
                ModSprites.ResolveContainerSprite(originalSpriteName, capacity).name;
            if (!expectedSpriteNames.Contains(minigameSpriteName))
            {
                continue;
            }

            var material = ContainerMaterialCatalog.GetMaterial(container);
            var needsSprite = !coveredSpriteNames.Contains(minigameSpriteName);
            var needsMaterial = material != ContainerMaterial.Unknown
                && !coveredMaterials.Contains(material);
            if (!needsSprite && !needsMaterial)
            {
                continue;
            }

            if (!GiveContainer(body, itemId, Array.Empty<LiquidMix>()))
            {
                continue;
            }

            spawnedCount++;
            coveredSpriteNames.Add(minigameSpriteName);
            if (material != ContainerMaterial.Unknown)
            {
                coveredMaterials.Add(material);
            }
            Plugin.LogInfo(
                $"Debug startup: added game container '{itemId}' for sprite '{minigameSpriteName}' "
                + $"and material '{material}'.");
        }

        foreach (var spriteName in ContainerSpriteNames)
        {
            if (!coveredSpriteNames.Contains(spriteName))
            {
                Plugin.LogWarning(
                    $"Debug startup: no game container was found for minigame sprite '{spriteName}'.");
            }
        }

        foreach (var material in ContainerMaterials)
        {
            if (!coveredMaterials.Contains(material))
            {
                Plugin.LogWarning(
                    $"Debug startup: no game container was found for material '{material}'.");
            }
        }

        return spawnedCount;
    }

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

    private static bool GiveContainer(Body body, string itemId, LiquidMix[] mixture)
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
