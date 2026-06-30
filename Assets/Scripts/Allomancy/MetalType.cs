namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Canonical allomantic metals. The first eight are the basic physical/mental metals
    /// and are the ones selectable with number keys 1–8. Array indices elsewhere (reserves,
    /// unlocked, metalItems) use (int)MetalType, so this order is load-bearing.
    /// </summary>
    public enum MetalType
    {
        // ── 8 basic metals (selectable via 1–8) ──
        Iron, Steel, Pewter, Tin, Copper, Bronze, Zinc, Brass,
        // ── 8 higher metals (framework only for now; effects in later chunks) ──
        Gold, Electrum, Cadmium, Bendalloy, Aluminum, Duralumin, Chromium, Nicrosil
    }

    /// <summary>Static helpers for metal enumeration and selection order.</summary>
    public static class Metals
    {
        public const int Count = 16;

        /// <summary>The 8 basic metals in number-key (1–8) selection order.</summary>
        public static readonly MetalType[] Selectable =
        {
            MetalType.Iron, MetalType.Steel, MetalType.Pewter, MetalType.Tin,
            MetalType.Copper, MetalType.Bronze, MetalType.Zinc, MetalType.Brass
        };
    }
}