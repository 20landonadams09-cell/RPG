using System.Collections.Generic;
using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Per-metal data (display name, drain rate, drink amount, HUD color, one-line description).
    /// Plain C# — no ScriptableObject — populated in code from MetallurgyConstants. Ported/condensed
    /// from Ashwalker's MetalDatabase.cs. The descriptions state each metal's Allomantic effect:
    /// the 8 basic metals describe the real in-game behavior (verified against the Mistborn lore —
    /// see the README "Allomantic metals" codex); the 8 higher metals state the lore effect and are
    /// marked "Locked" until implemented in a later chunk.
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
        /// <summary>One-line Allomantic effect, shown under the metal name in the wheel.</summary>
        public string description;
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
            // ── 8 basic metals: implemented. Descriptions match the actual in-game effect + keys. ──
            var d = new Dictionary<MetalType, MetalDefinition>
            {
                [MetalType.Iron] = New(MetalType.Iron, "Iron", MetallurgyConstants.IronDrainRate, 50f, false, new Color(0.5f, 0.5f, 0.8f),
                    "Ironpull — hold Q to yank yourself toward a nearby metal anchor."),
                [MetalType.Steel] = New(MetalType.Steel, "Steel", MetallurgyConstants.SteelDrainRate, 50f, false, new Color(0.4f, 0.6f, 1f),
                    "Steelpush — hold F to launch yourself off a nearby metal anchor."),
                [MetalType.Pewter] = New(MetalType.Pewter, "Pewter", MetallurgyConstants.PewterDrainRate, 50f, false, new Color(0.75f, 0.5f, 0.35f),
                    "Physical enhancement — burn for strength, speed, leaps, and toughness. Burn too long then stop and you crash."),
                [MetalType.Tin] = New(MetalType.Tin, "Tin", MetallurgyConstants.TinDrainRate, 50f, false, new Color(0.8f, 0.85f, 0.9f),
                    "Enhanced senses — night vision, hearing, scent, and tremorsense. Bright light or loud noise overloads you."),
                [MetalType.Copper] = New(MetalType.Copper, "Copper", MetallurgyConstants.CopperDrainRate, 50f, false, new Color(0.7f, 0.5f, 0.3f),
                    "Coppercloud — burn to hide your allomancy and suppress nearby enemy allomancers."),
                [MetalType.Bronze] = New(MetalType.Bronze, "Bronze", MetallurgyConstants.BronzeDrainRate, 50f, false, new Color(0.75f, 0.5f, 0.2f),
                    "Seeking — burn to hear allomantic pulses from enemy allomancers, even through walls."),
                [MetalType.Zinc] = New(MetalType.Zinc, "Zinc", MetallurgyConstants.ZincDrainRate, 50f, false, new Color(1f, 0.85f, 0.4f),
                    "Riot — burn to inflame nearby enemies' emotions; they turn hyper-aggressive and swarm."),
                [MetalType.Brass] = New(MetalType.Brass, "Brass", MetallurgyConstants.BrassDrainRate, 50f, false, new Color(0.85f, 0.7f, 0.3f),
                    "Soothe — burn to dampen nearby enemies' emotions; they go calm, slow, and barely notice you."),

                // ── 8 higher metals: framework only (locked in the wheel). Descriptions state the
                //    lore Allomantic effect, verified against the Mistborn canon, and mark them
                //    "Locked" so the player knows they're not burnable yet. ──
                [MetalType.Gold] = New(MetalType.Gold, "Gold", MetallurgyConstants.GoldDrainRate, 50f, false, new Color(1f, 0.85f, 0.1f),
                    "Gold — burn to see a vision of your alternate past self. (Locked — not yet burnable.)"),
                [MetalType.Electrum] = New(MetalType.Electrum, "Electrum", MetallurgyConstants.ElectrumDrainRate, 50f, false, new Color(0.95f, 0.95f, 0.75f),
                    "Electrum — burn to see shadows of your own near future. (Locked — not yet burnable.)"),
                [MetalType.Cadmium] = New(MetalType.Cadmium, "Cadmium", MetallurgyConstants.CadmiumDrainRate, 50f, false, new Color(0.6f, 0.6f, 0.85f),
                    "Cadmium — burn to slow time inside a bubble around you. (Locked — not yet burnable.)"),
                [MetalType.Bendalloy] = New(MetalType.Bendalloy, "Bendalloy", MetallurgyConstants.BendalloyDrainRate, 50f, false, new Color(0.4f, 0.85f, 0.55f),
                    "Bendalloy — burn to speed up time inside a bubble around you. (Locked — not yet burnable.)"),
                [MetalType.Aluminum] = New(MetalType.Aluminum, "Aluminum", 0f, 0f, true, new Color(0.9f, 0.9f, 0.9f),
                    "Aluminum — burn to instantly wipe your own metal reserves. (Locked — not yet burnable.)"),
                [MetalType.Duralumin] = New(MetalType.Duralumin, "Duralumin", 0f, 0f, true, new Color(0.85f, 0.85f, 0.95f),
                    "Duralumin — burn to flash-consume every burning metal in one enormous burst. (Locked — not yet burnable.)"),
                [MetalType.Chromium] = New(MetalType.Chromium, "Chromium", 0f, 0f, true, new Color(0.6f, 0.6f, 0.65f),
                    "Chromium — by touch, wipe another allomancer's metal reserves. (Locked — not yet burnable.)"),
                [MetalType.Nicrosil] = New(MetalType.Nicrosil, "Nicrosil", 0f, 0f, true, new Color(0.65f, 0.65f, 0.7f),
                    "Nicrosil — by touch, surge another allomancer's burning metals. (Locked — not yet burnable.)"),
            };
            return d;
        }

        static MetalDefinition New(MetalType type, string name, float drain, float drink, bool instant, Color color, string description)
            => new MetalDefinition { type = type, displayName = name, drainRate = drain, drinkAmount = drink, isInstant = instant, hudColor = color, description = description };
    }
}