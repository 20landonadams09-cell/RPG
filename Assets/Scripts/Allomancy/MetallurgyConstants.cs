using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Centralized allomancy constants and tuning — ported verbatim from Ashwalker's
    /// MetallurgyConstants.cs. Burn durations are real-world seconds at DefaultMaxReserve=100;
    /// drain rate = DefaultMaxReserve / burnDuration.
    /// </summary>
    public static class MetallurgyConstants
    {
        // General Settings
        public const float DefaultMaxReserve = 100f;
        public const float PassiveRecoveryRate = 2f;
        public const float BaseBurnRate = 1.0f;

        // Enhancement Metals
        public const float DuraluminBurnDuration = 1.2f;
        public const float NicroburstMultiplier = 3.0f;
        public const float NicroburstDuration = 1.5f;

        // ── MAG Burn Durations (canonical source) ─────────────────────────────────
        // Real-world gameplay durations at DefaultMaxReserve = 100.
        // drain rate = DefaultMaxReserve / duration_in_seconds.
        // Instant metals (Aluminum, Duralumin, Chromium, Nicrosil) drain on activation.

        public const float AluminumBurnDuration  = 0f;    // instant
        public const float BendalloyBurnDuration = 300f;
        public const float BrassBurnDuration     = 1200f;
        public const float BronzeBurnDuration    = 1800f;
        public const float CadmiumBurnDuration   = 1800f;
        public const float ChromiumBurnDuration  = 0f;    // instant
        public const float CopperBurnDuration    = 2400f;
        public const float ElectrumBurnDuration  = 600f;
        public const float GoldBurnDuration      = 600f;
        public const float IronBurnDuration      = 1200f;
        public const float NicrosilBurnDuration  = 0f;    // instant
        public const float PewterBurnDuration    = 300f;
        public const float SteelBurnDuration     = 1200f;
        public const float TinBurnDuration       = 3600f;
        public const float ZincBurnDuration      = 1200f;

        // Derived drain rates at DefaultMaxReserve = 100.
        public const float BendalloyDrainRate = DefaultMaxReserve / BendalloyBurnDuration; // ≈ 0.333/s
        public const float BrassDrainRate     = DefaultMaxReserve / BrassBurnDuration;     // ≈ 0.0833/s
        public const float BronzeDrainRate    = DefaultMaxReserve / BronzeBurnDuration;    // ≈ 0.0556/s
        public const float CadmiumDrainRate   = DefaultMaxReserve / CadmiumBurnDuration;   // ≈ 0.0556/s
        public const float CopperDrainRate    = DefaultMaxReserve / CopperBurnDuration;    // ≈ 0.0417/s
        public const float ElectrumDrainRate  = DefaultMaxReserve / ElectrumBurnDuration;  // ≈ 0.1667/s
        public const float GoldDrainRate      = DefaultMaxReserve / GoldBurnDuration;      // ≈ 0.1667/s
        public const float IronDrainRate      = DefaultMaxReserve / IronBurnDuration;      // ≈ 0.0833/s
        public const float PewterDrainRate    = DefaultMaxReserve / PewterBurnDuration;    // ≈ 0.333/s
        public const float SteelDrainRate     = DefaultMaxReserve / SteelBurnDuration;     // ≈ 0.0833/s
        public const float TinDrainRate       = DefaultMaxReserve / TinBurnDuration;       // ≈ 0.0278/s
        public const float ZincDrainRate      = DefaultMaxReserve / ZincBurnDuration;      // ≈ 0.0833/s
    }
}