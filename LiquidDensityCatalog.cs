using System;
using System.Collections.Generic;
using System.Text;

namespace RealisticLiquidTransfer;

internal static class LiquidDensityCatalog
{
    internal const float DefaultDensity = 1f;

    private static readonly Dictionary<string, float> DensityByLiquidName = CreateDensityMap();

    internal static bool TryGetDensity(string liquidId, out float density)
    {
        density = DefaultDensity;
        if (string.IsNullOrWhiteSpace(liquidId))
        {
            return false;
        }

        return DensityByLiquidName.TryGetValue(NormalizeIdentifier(liquidId), out density);
    }

    internal static bool MatchesIdentifier(string liquidId, string requestedName)
    {
        if (string.IsNullOrWhiteSpace(liquidId) || string.IsNullOrWhiteSpace(requestedName))
        {
            return false;
        }

        var normalizedId = NormalizeIdentifier(liquidId);
        var normalizedName = NormalizeIdentifier(requestedName);
        return string.Equals(normalizedId, normalizedName, StringComparison.Ordinal)
            || (normalizedId == "water" && normalizedName == "cleanwater")
            || (normalizedId == "cleanwater" && normalizedName == "water");
    }

    private static Dictionary<string, float> CreateDensityMap()
    {
        var definitions = new (string Name, float Density)[]
        {
            ("Alcohol", 0.79f),
            ("Apple juice", 1.04f),
            ("Carbonated water", 1.00f),
            ("Chocolate milk", 1.05f),
            ("Clean water", 1.00f),
            ("Water", 1.00f),
            ("Coffee", 1.00f),
            ("Dirty water", 1.00f),
            ("Energy drink", 1.03f),
            ("Fat", 0.90f),
            ("Hot sauce", 1.05f),
            ("Ice cream", 0.95f),
            ("Iced tea", 1.00f),
            ("Ketchup", 1.15f),
            ("Lemonade", 1.04f),
            ("Milk", 1.03f),
            ("Olive oil", 0.91f),
            ("Oil", 0.90f),
            ("Orange juice", 1.05f),
            ("Powdered milk", 1.00f),
            ("Produce juice", 1.04f),
            ("Refined juice", 1.04f),
            ("Ringer's solution", 1.01f),
            ("Saline", 1.01f),
            ("Soap", 1.03f),
            ("Soda", 1.04f),
            ("Soup", 1.02f),
            ("Sports drink", 1.03f),
            ("Tree sap", 1.05f),
            ("Urine", 1.00f),
            ("Yogurt", 1.04f),
            ("Alien blood", 1.10f),
            ("Bio-chem fluid", 1.02f),
            ("Blood", 1.06f),
            ("Groundwater", 1.00f),
            ("Lumalgae", 1.02f),
            ("Mold", 1.10f),
            ("Red blood", 1.06f),
            ("Red sludge", 1.20f),
            ("Radwater", 1.00f),
            ("Amiodarone", 1.00f),
            ("Anti-rad", 1.00f),
            ("Antibiotics", 1.00f),
            ("Antidepressants", 1.00f),
            ("Antiseptic", 1.00f),
            ("Antiserum", 1.03f),
            ("Antivenom", 1.03f),
            ("Ceftriaxone", 1.00f),
            ("Epinephrine", 1.00f),
            ("Heroin", 1.00f),
            ("Keratin-booster", 1.02f),
            ("L.R.D. Serum", 1.03f),
            ("Local anesthetic", 1.00f),
            ("Morphine", 1.00f),
            ("Naloxone", 1.00f),
            ("Naltrexone", 1.00f),
            ("Opium", 1.05f),
            ("Oxyline", 1.00f),
            ("Painkillers", 1.00f),
            ("Procoagulant", 1.03f),
            ("Relief cream", 0.95f),
            ("Sodium nitroprusside", 1.00f),
            ("Streptokinase", 1.00f),
            ("Vasopressin", 1.00f),
            ("Wound glue", 1.10f),
            ("Braingrow", 1.20f),
            ("Cereal mix", 1.10f),
            ("Chloroform", 1.49f),
            ("Fentanyl", 1.00f),
            ("Hard stimulant", 1.00f),
            ("Medical-grade stimulant", 1.00f),
            ("Mercury", 13.55f),
            ("Mindwipe", 1.50f),
            ("Off-brand stimulant", 1.00f),
            ("Sleeping pills", 1.00f),
            ("Bleach", 1.08f)
        };

        var densities = new Dictionary<string, float>(definitions.Length, StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            densities.Add(NormalizeIdentifier(definition.Name), definition.Density);
        }

        return densities;
    }

    internal static string NormalizeIdentifier(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        var result = normalized.ToString();
        if (result.Length > 6
            && result.StartsWith("liquid", StringComparison.Ordinal))
        {
            return result.Substring(6);
        }

        return result;
    }
}

internal static class LiquidTransparencyCatalog
{
    private static readonly Dictionary<string, int> TransparencyPercentByLiquidId =
        CreateTransparencyPercentMap();

    internal static bool TryGetAlpha(string liquidId, out float alpha)
    {
        alpha = 1f;
        if (string.IsNullOrWhiteSpace(liquidId)
            || !TransparencyPercentByLiquidId.TryGetValue(
                LiquidDensityCatalog.NormalizeIdentifier(liquidId),
                out var transparencyPercent))
        {
            return false;
        }

        alpha = 1f - transparencyPercent * 0.008f;
        return true;
    }

    private static Dictionary<string, int> CreateTransparencyPercentMap()
    {
        var definitions = new (string LiquidId, int TransparencyPercent)[]
        {
            ("water", 100),
            ("carbonatedwater", 95),
            ("lrdserum", 35),
            ("morphine", 90),
            ("biochem", 15),
            ("opium", 5),
            ("painkillers", 90),
            ("heroin", 40),
            ("naloxone", 95),
            ("naltrexone", 95),
            ("ceftriaxone", 90),
            ("fentanyl", 90),
            ("ketchup", 0),
            ("milk", 5),
            ("chloroform", 100),
            ("highgradestimulant", 95),
            ("midgradestimulant", 40),
            ("lowgradestimulant", 90),
            ("applejuice", 65),
            ("orangejuice", 15),
            ("lemonade", 80),
            ("urine", 85),
            ("icetea", 75),
            ("soup", 10),
            ("chocolatemilk", 5),
            ("cereal", 0),
            ("energydrink", 75),
            ("sportsdrink", 80),
            ("oliveoil", 90),
            ("hotsauce", 15),
            ("icecream", 0),
            ("yogurt", 5),
            ("mold", 0),
            ("powderedmilk", 0),
            ("radwater", 65),
            ("mercury", 0),
            ("soda", 85),
            ("alcohol", 100),
            ("bleach", 95),
            ("reliefcream", 0),
            ("localanesthetic", 95),
            ("woundglue", 20),
            ("braingrow", 0),
            ("mindwipe", 0),
            ("antidepressants", 90),
            ("antibiotics", 90),
            ("antivenom", 65),
            ("antiserum", 65),
            ("keratinbooster", 25),
            ("antirad", 30),
            ("sleepingpills", 90),
            ("procoagulant", 15),
            ("epinephrine", 95),
            ("oxyline", 95),
            ("sodiumnitroprusside", 90),
            ("vasopressin", 95),
            ("amiodarone", 95),
            ("streptokinase", 95),
            ("saline", 100),
            ("ringersolution", 100),
            ("blood", 5),
            ("redblood", 5),
            ("alienblood", 0),
            ("disinfectant", 90),
            ("groundwater", 65),
            ("lumalgae", 10),
            ("redsludge", 0),
            ("oil", 90),
            ("sap", 35),
            ("dirtywater", 10),
            ("fat", 10),
            ("soap", 65),
            ("producejuice", 30),
            ("refinedjuice", 75)
        };

        var transparencies = new Dictionary<string, int>(
            definitions.Length,
            StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            transparencies.Add(
                LiquidDensityCatalog.NormalizeIdentifier(definition.LiquidId),
                definition.TransparencyPercent);
        }

        return transparencies;
    }
}
