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

    private static string NormalizeIdentifier(string value)
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
