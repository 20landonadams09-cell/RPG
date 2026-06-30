using UnityEngine;

namespace BasicRPG.Interaction
{
    /// <summary>
    /// Static pause flags. <see cref="IsLocked"/> is set while a dialogue or inventory is open —
    /// PlayerController zeroes movement input while locked (gravity still applies so the player
    /// doesn't float), and allomancy/interaction input pauses too. Reset by the UI that set it.
    ///
    /// <see cref="TutorialActive"/> is a SEPARATE flag set while the guided tutorial overlay is
    /// active. The tutorial freezes the world via <c>Time.timeScale = 0</c> but must leave
    /// allomancy input (Tab/B/F/Q) flowing so its steps can be completed — so it deliberately does
    /// NOT hold <see cref="IsLocked"/>. Instead it sets <see cref="TutorialActive"/>, which the
    /// menu/interaction consumers (inventory, interact, save, debug damage, drink/refill) check
    /// to keep the player from wandering out of the tutorial into menus while the world is paused.
    /// </summary>
    public static class InteractionLock
    {
        public static bool IsLocked;
        public static bool TutorialActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { IsLocked = false; TutorialActive = false; }
    }
}