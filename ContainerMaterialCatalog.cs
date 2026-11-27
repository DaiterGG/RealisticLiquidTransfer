using System;
using System.Collections.Generic;

namespace RealisticLiquidTransfer;

internal enum ContainerMaterial
{
    Unknown,
    Plastic,
    Glass,
    Metal,
    Cardboard,
    Ceramic,
    Aluminium
}

internal static class ContainerMaterialCatalog
{
    private static readonly Dictionary<string, ContainerMaterial> MaterialByContainerId =
        CreateMaterialMap();

    internal static ContainerMaterial GetMaterial(WaterContainerItem container)
    {
        if (!container)
        {
            return ContainerMaterial.Unknown;
        }

        if (container.TryGetComponent<Item>(out var item))
        {
            return GetMaterial(item.id);
        }

        return ContainerMaterial.Unknown;
    }

    internal static ContainerMaterial GetMaterial(string containerId)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return ContainerMaterial.Unknown;
        }

        return MaterialByContainerId.TryGetValue(
            LiquidDensityCatalog.NormalizeIdentifier(containerId),
            out var material)
            ? material
            : ContainerMaterial.Unknown;
    }

    private static Dictionary<string, ContainerMaterial> CreateMaterialMap()
    {
        var materialById = new Dictionary<string, ContainerMaterial>(StringComparer.Ordinal);

        Add(materialById, ContainerMaterial.Plastic,
            "syringe",
            "morphine",
            "opium",
            "heroin",
            "naloxone",
            "streptokinase",
            "fentanyl",
            "ceftriaxone",
            "saline",
            "ringersolution",
            "paincream",
            "woundglue",
            "painkillers",
            "antidepressants",
            "sleepingpills",
            "combatpen",
            "bloodbag",
            "bloodbaghuman",
            "ivbag",
            "smallivbag",
            "redbloodbag",
            "antiseptic",
            "spraybottle",
            "waterbottle",
            "chocolatemilk",
            "ketchup",
            "waterjug",
            "bleach",
            "coffee",
            "sodabottle",
            "icetea",
            "liquidpouch",
            "canteen",
            "craftingbottle",
            "temporarybottle");

        Add(materialById, ContainerMaterial.Glass,
            "reliefcream",
            "naltrexone",
            "sodiumnitroprusside",
            "vasopressin",
            "amiodarone",
            "keratinbooster",
            "braingrow",
            "antibiotics",
            "mindwipe",
            "antirad",
            "lemonade",
            "alcohol");

        Add(materialById, ContainerMaterial.Metal,
            "makeshiftlrd",
            "lrd",
            "lrdserum",
            "minibarrel",
            "liquidcentrifuge");

        Add(materialById, ContainerMaterial.Cardboard,
            "milk",
            "applejuice");

        Add(materialById, ContainerMaterial.Ceramic,
            "bowlofcereal",
            "soup");

        Add(materialById, ContainerMaterial.Aluminium,
            "energydrink",
            "sodacan");

        return materialById;
    }

    private static void Add(
        Dictionary<string, ContainerMaterial> materialById,
        ContainerMaterial material,
        params string[] containerIds)
    {
        foreach (var containerId in containerIds)
        {
            materialById[LiquidDensityCatalog.NormalizeIdentifier(containerId)] = material;
        }
    }
}
