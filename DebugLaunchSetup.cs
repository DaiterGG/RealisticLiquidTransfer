#if LIQUID_TRANSFER_DEBUG_LOGGING
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
        SandboxSetupPending = true;
        SaveSystem.loadedRun = false;
        PlayerPrefs.SetInt("tutorial", 1);
        PlayerPrefs.SetInt("radlinedisable", 0);
        WorldGeneration.runSettings = null;
        __instance.StartCoroutine(__instance.WaitLoad());
        Plugin.LogInfo("Debug startup: loading tutorial.");
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

        GiveContainer(body, "waterjug", fillWithWater: true);
        GiveContainer(body, "minibarrel", fillWithWater: false);
        GiveContainer(body, "waterbottle", fillWithWater: false);
        Plugin.LogInfo("Debug startup: equipped the main player with a full water jug, empty mini barrel, and empty water bottle.");
    }

    private static void GiveContainer(Body body, string itemId, bool fillWithWater)
    {
        var prefab = Resources.Load<GameObject>(itemId);
        if (!prefab)
        {
            Plugin.LogError($"Debug startup: item prefab '{itemId}' was not found.");
            return;
        }

        var instance = Object.Instantiate(prefab, body.transform.position, Quaternion.identity);
        var item = instance.GetComponent<Item>();
        var container = instance.GetComponent<WaterContainerItem>();
        if (!item || !container)
        {
            Object.Destroy(instance);
            Plugin.LogError($"Debug startup: '{itemId}' is missing its item or liquid-container component.");
            return;
        }

        container.DrainAll();
        if (fillWithWater && container.AddLiquid("water", container.Capacity) < container.Capacity)
        {
            Object.Destroy(instance);
            Plugin.LogError($"Debug startup: could not fill '{itemId}' with water.");
            return;
        }

        var slotIndex = FindEmptySlot(body, item.Stats.onlyHoldInHands);
        if (slotIndex < 0)
        {
            Object.Destroy(instance);
            Plugin.LogError($"Debug startup: no compatible empty inventory slot for '{itemId}'.");
            return;
        }

        body.PickUpItem(item, slotIndex, force: true);
        if (body.GetItem(slotIndex) != item)
        {
            Object.Destroy(instance);
            Plugin.LogError($"Debug startup: could not equip '{itemId}'.");
            return;
        }

        Plugin.LogInfo($"Debug startup: equipped '{itemId}' in slot {slotIndex} with {container.CurrentTotal:0} mL.");
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
}
#endif
