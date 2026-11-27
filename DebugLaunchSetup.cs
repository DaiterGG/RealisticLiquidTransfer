#if LIQUID_TRANSFER_DEBUG_LAUNCH
using HarmonyLib;
using UnityEngine;

namespace RealisticLiquidTransfer;

[HarmonyPatch(typeof(PreRunScript), "Start")]
internal static class DebugLaunchMenuPatch
{
    private static bool _launchStarted;
    internal static bool SandboxSetupPending { get; private set; }

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

        SandboxSetupPending = true;
        SaveSystem.loadedRun = false;
        PlayerPrefs.SetInt("tutorial", 1);
        PlayerPrefs.SetInt("radlinedisable", 0);
        WorldGeneration.runSettings = null;
        __instance.StartCoroutine(__instance.WaitLoad());
        Plugin.LogInfo("Debug startup: loading tutorial because multiplayer is not running.");
    }

    internal static void ConsumeSandboxSetup()
    {
        SandboxSetupPending = false;
    }
}

[HarmonyPatch(typeof(TutorialHandler), "Start")]
internal static class DebugLaunchTutorialPatch
{
    [HarmonyPostfix]
    private static void AfterStart(TutorialHandler __instance)
    {
        if (!DebugLaunchMenuPatch.SandboxSetupPending)
        {
            return;
        }

        DebugLaunchMenuPatch.ConsumeSandboxSetup();
        if (!__instance)
        {
            Plugin.LogError("Debug startup: tutorial handler is unavailable.");
            return;
        }

        __instance.StartCourse(typeof(SandboxCourse));
        if (__instance.activeCourse is not SandboxCourse)
        {
            Plugin.LogError("Debug startup: could not start the sandbox course.");
            return;
        }

        if (__instance.courseSelectScreen)
        {
            __instance.courseSelectScreen.SetActive(false);
        }

        var body = PlayerCamera.main ? PlayerCamera.main.body : null;
        if (!body)
        {
            Plugin.LogError("Debug startup: main player body is unavailable; containers were not spawned.");
            return;
        }

        var equippedCount = 0;
        if (GiveFunnel(body))
        {
            equippedCount++;
        }

        if (GiveContainer(body, "waterjug", new[] { new LiquidMix("water", 1f) }))
        {
            equippedCount++;
        }

        if (GiveContainer(body, "waterbottle", System.Array.Empty<LiquidMix>()))
        {
            equippedCount++;
        }

        foreach (var mixture in TestLiquidMixtures)
        {
            if (GiveContainer(body, "minibarrel", mixture))
            {
                equippedCount++;
            }
        }

        foreach (var mixture in TestLiquidMixtures)
        {
            if (GiveContainer(body, "waterbottle", mixture))
            {
                equippedCount++;
            }
        }

        Plugin.LogInfo(
            $"Debug startup: equipped {equippedCount} of {3 + TestLiquidMixtures.Length * 2} "
            + "requested funnel, jug, barrel, and bottle test items.");
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
        if (slotIndex < 0)
        {
            Object.Destroy(instance);
            Plugin.LogWarning($"Debug startup: no compatible inventory slot remains for '{itemId}'.");
            return false;
        }

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
            Object.Destroy(instance);
            Plugin.LogWarning("Debug startup: no compatible inventory slot remains for the funnel.");
            return false;
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
#endif
