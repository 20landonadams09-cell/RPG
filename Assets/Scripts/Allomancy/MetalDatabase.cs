using System.Collections.Generic;
using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Per-metal data (display name, drain rate, drink amount, HUD color). Plain C# — no
    /// ScriptableObject — populated in code from MetallurgyConstants. Ported/condensed from
    /// Ashwalker's MetalDatabase.cs (dropped lore/density/category fields not needed here).
    /// </summary>
    [System.Serializable]
    public class MetalDefinition
    {
        public MetalType type;
        public string displayName;
        public float drainRate;     // reserve units drained per second while burning
        public float drinkAmount;   // reserve restored per ingot drunk
        public bool isInstant;      // instant metals drain on activation (Aluminum, Duralumin, …)
        public Color hudColor;
    }

    public static class MetalDatabase
    {
        private static readonly Dictionary<MetalType, MetalDefinition> Defs = BuildDefaults();

        public static MetalDefinition Get(MetalType metal)
        {
            Defs.TryGetValue(metal, out MetalDefinition def);
            return def;
        }

        static Dictionary<MetalType, MetalDefinition> BuildDefaults()
        {
            var d = new Dictionary<MetalType, MetalDefinition>
            {
                [MetalType.Iron] = New(MetalType.Iron, "Iron", MetallurgyConstants.IronDrainRate, 50f, false, new Color(0.5f, 0.5f, 0.8f)),
                [MetalType.Steel] = New(MetalType.Steel, "Steel", MetallurgyConstants.SteelDrainRate, 50f, false, new Color(0.4f, 0.6f, 1f)),
                [MetalType.Pewter] = New(MetalType.Pewter, "Pewter", MetallurgyConstants.PewterDrainRate, 50f, false, new Color(0.75f, 0.5f, 0.35f)),
                [MetalType.Tin] = New(MetalType.Tin, "Tin", MetallurgyConstants.TinDrainRate, 50f, false, new Color(0.8f, 0.85f, 0.9f)),
                [MetalType.Copper] = New(MetalType.Copper, "Copper", MetallurgyConstants.CopperDrainRate, 50f, false, new Color(0.7f, 0.5f, 0.3f)),
                [MetalType.Bronze] = New(MetalType.Bronze, "Bronze", MetallurgyConstants.BronzeDrainRate, 50f, false, new Color(0.75f, 0.5f, 0.2f)),
                [MetalType.Zinc] = New(MetalType.Zinc, "Zinc", MetallurgyConstants.ZincDrainRate, 50f, false, new Color(1f, 0.85f, 0.4f)),
                [MetalType.Brass] = New(MetalType.Brass, "Brass", MetallurgyConstants.BrassDrainRate, 50f, false, new Color(0.85f, 0.7f, 0.3f)),

                [MetalType.Gold] = New(MetalType.Gold, "Gold", MetallurgyConstants.GoldDrainRate, 50f, false, new Color(1f, 0.85f, 0.1f)),
                [MetalType.Electrum] = New(MetalType.Electrum, "Electrum", MetallurgyConstants.ElectrumDrainRate, 50f, false, new Color(0.95f, 0.95f, 0.75f)),
                [MetalType.Cadmium] = New(MetalType.Cadmium, "Cadmium", MetallurgyConstants.CadmiumDrainRate, 50f, false, new Color(0.6f, 0.6f, 0.85f)),
                [MetalType.Bendalloy] = New(MetalType.Bendalloy, "Bendalloy", MetallurgyConstants.BendalloyDrainRate, 50f, false, new Color(0.4f, 0.85f, 0.55f)),
                [MetalType.Aluminum] = New(MetalType.Aluminum, "Aluminum", 0f, 0f, true, new Color(0.9f, 0.9f, 0.9f)),
                [MetalType.Duralumin] = New(MetalType.Duralumin, "Duralumin", 0f, 0f, true, new Color(0.85f, 0.85f, 0.95f)),
                [MetalType.Chromium] = New(MetalType.Chromium, "Chromium", 0f, 0f, true, new Color(0.6f, 0.6f, 0.65f)),
                [MetalType.Nicrosil] = New(MetalType.Nicrosil, "Nicrosil", 0f, 0f, true, new Color(0.65f, 0.65f, 0.7f)),
            };
            return d;
        }

        static MetalDefinition New(MetalType type, string name, float drain, float drink, bool instant, Color color)
            => new MetalDefinition { type = type, displayName = name, drainRate = drain, drinkAmount = drink, isInstant = instant, hudColor = color };
    }
}