using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Allomancy keybinds for BasicRPG — remapped from Ashwalker's Keybinds.cs to avoid
    /// conflicts with the existing controls (E=interact, C=dodge, I=inventory, LMB=attack,
    /// RMB=block). Allomancy uses only B / X / 1–8 / RightAlt, none of which clash.
    /// </summary>
    public static class Keybinds
    {
        public static KeyCode BurnToggle  = KeyCode.B;     // start/stop burning the active metal
        public static KeyCode DrinkVial   = KeyCode.X;     // drink one ingot of the active metal → refill reserve
        public static KeyCode RefillAll   = KeyCode.RightAlt; // debug: refill all reserves
        public static KeyCode MetalWheel  = KeyCode.Tab;   // open the radial metal-selection wheel
        public static KeyCode SteelPush   = KeyCode.F;     // Steelpush: launch away from a metal anchor (Steel burning)
        public static KeyCode IronPull    = KeyCode.Q;     // Ironpull: yank toward a metal anchor (Iron burning)
        public static KeyCode SaveGame    = KeyCode.F5;    // save to disk
        public static KeyCode LoadGame    = KeyCode.F9;    // load from disk
        public static KeyCode Flare       = KeyCode.R;     // hold to flare the burning metal (burn harder)

        // Number keys 1–8 select the active metal (see Metals.Selectable).
        public static readonly KeyCode[] SelectMetal =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8
        };
    }
}