using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Allomancy keybinds for BasicRPG. Burning is a B / △ toggle; flaring is a hold (R for any
    /// metal, RMB while Iron/Steel is active). Push/pull is hold LMB (or F/Q as alternates) while
    /// Iron/Steel burns — LMB/RMB are CONTEXTUAL: while burning Iron/Steel, LMB = push/pull and
    /// RMB = flare (melee attack and block are suppressed by PlayerCombat); otherwise LMB = attack
    /// and RMB = block as usual. Freeze-time target aim is hold MMB (gamepad: L1, contextual).
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

        // ── Gamepad (DualSense / PS5) — classic Input Manager ONLY (no Input System package) ──
        // Button indices follow the common SDL/Linux joystick mapping. They are platform- and
        // backend-dependent, so VERIFY in a playtest and tweak these constants if a button is off
        // (e.g. L1/R1 swapped). The helpers below read keyboard OR gamepad, so a keyboard player
        // is unaffected if a pad isn't plugged in.
        public static KeyCode PadJump      = KeyCode.JoystickButton0;  // Cross      ✕  → Jump          (Space)
        public static KeyCode PadDodge     = KeyCode.JoystickButton1;  // Circle     ○  → Dodge         (C)
        public static KeyCode PadInteract  = KeyCode.JoystickButton2;  // Square     □  → Interact      (E)
        public static KeyCode PadBurn      = KeyCode.JoystickButton3;  // Triangle   △  → Burn toggle   (B)
        public static KeyCode PadBlock     = KeyCode.JoystickButton4;  // L1 (hold)     → Block         (RMB)
        public static KeyCode PadAttack    = KeyCode.JoystickButton5;  // R1            → Attack        (LMB)
        public static KeyCode PadPull       = KeyCode.JoystickButton6;  // L2 (hold)     → Ironpull      (Q)
        public static KeyCode PadPush      = KeyCode.JoystickButton7;  // R2 (hold)     → Steelpush     (F)
        public static KeyCode PadWheel     = KeyCode.JoystickButton8;  // Share         → Metal wheel   (Tab)
        public static KeyCode PadFlare     = KeyCode.JoystickButton9;  // Options (hold)→ Flare         (R)
        public static KeyCode PadSprint     = KeyCode.JoystickButton10; // L3 (hold)     → Sprint        (Shift)
        public static KeyCode PadInventory = KeyCode.JoystickButton11; // R3 (click)    → Inventory     (I)
        public static KeyCode PadDrink      = KeyCode.JoystickButton12; // Dpad Up       → Drink metal   (X)
        public static KeyCode PadSave       = KeyCode.JoystickButton14; // Dpad Left     → Save          (F5)
        public static KeyCode PadLoad      = KeyCode.JoystickButton15; // Dpad Right    → Load          (F9)

        // Movement: the default Input Manager "Horizontal"/"Vertical" already aggregate the left
        // stick, so Input.GetAxisRaw("Horizontal"/"Vertical") works for both keyboard and pad.
        // The right-stick camera needs two custom axes ("RightStickX"/"RightStickY") added to
        // InputManager.asset by the scene builder (see EnsureRightStickAxes in RPGSceneBuilder).

        // ── Combined helpers (keyboard OR gamepad) ─────────────────────────────────────────
        public static bool Down(KeyCode kb, KeyCode pad) =>
            Input.GetKeyDown(kb) || (pad != KeyCode.None && Input.GetKeyDown(pad));
        public static bool Held(KeyCode kb, KeyCode pad) =>
            Input.GetKey(kb) || (pad != KeyCode.None && Input.GetKey(pad));

        public static bool BurnDown()       => Down(BurnToggle, PadBurn);
        public static bool DrinkDown()      => Down(DrinkVial, PadDrink);
        public static bool RefillDown()     => Input.GetKeyDown(RefillAll); // keyboard-only (debug)
        public static bool WheelDown()      => Down(MetalWheel, PadWheel);
        // Push/pull: hold LMB (the primary binding) while burning Steel/Iron. F/Q remain as
        // keyboard alternates for accessibility; gamepad uses R2/L2. Only one of Steel/Iron burns
        // at a time (single active metal), so one LMB triggers whichever is active.
        public static bool PushHeld()       => Input.GetMouseButton(0) || Held(SteelPush, PadPush);
        public static bool PullHeld()       => Input.GetMouseButton(0) || Held(IronPull, PadPull);
        // Flare-to-max: R (any metal) + gamepad Options (hold). While held, Allomancer overrides
        // FlareMultiplier to its max (the scroll wheel still sets the resting intensity). This is a
        // hold shortcut — fine intensity 1..maxFlareSteps is set by the scroll wheel (ScrollWheelDelta).
        public static bool FlareHeld()      => Held(Flare, PadFlare);
        // Scroll-wheel flare intensity (the "flare wheel"): +1 step on scroll up, -1 on scroll down,
        // 0 when idle. Read in Allomancer.Update while burning. Classic Input Manager axis.
        public static float ScrollWheelDelta() => Input.GetAxis("Mouse ScrollWheel");
        // Freeze-time target aim: hold MMB (keyboard) or L1 (gamepad, contextual — while burning
        // Iron/Steel L1 = aim instead of block).
        public static bool AimHeld()        => Input.GetMouseButton(2)
            || (PadBlock != KeyCode.None && Input.GetKey(PadBlock));
        public static bool SaveDown()       => Down(SaveGame, PadSave);
        public static bool LoadDown()       => Down(LoadGame, PadLoad);
        public static bool SprintHeld()     => Held(KeyCode.LeftShift, PadSprint);
        public static bool DodgeDown()     => Down(KeyCode.C, PadDodge);
        public static bool InteractDown()   => Down(KeyCode.E, PadInteract);
        public static bool InventoryDown()  => Down(KeyCode.I, PadInventory);
        public static bool AttackDown()     => Input.GetMouseButtonDown(0) || (PadAttack != KeyCode.None && Input.GetKeyDown(PadAttack));
        public static bool BlockHeld()      => Input.GetMouseButton(1) || (PadBlock != KeyCode.None && Input.GetKey(PadBlock));
        public static bool JumpDown()       => Input.GetButtonDown("Jump") || (PadJump != KeyCode.None && Input.GetKeyDown(PadJump));

        // Dialogue advance: any of the usual keyboard/mouse keys OR the pad's "confirm" buttons.
        public static bool DialogueAdvance() =>
            Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Space)
            || Input.GetMouseButtonDown(0)
            || (PadInteract != KeyCode.None && Input.GetKeyDown(PadInteract)) // Square advances
            || (PadJump != KeyCode.None && Input.GetKeyDown(PadJump));        // Cross advances

        // Wheel: confirm-select = LMB OR pad face buttons; cancel is handled by WheelDown toggling
        // (Share re-pressed) plus Escape on keyboard.
        public static bool WheelConfirmDown() =>
            Input.GetMouseButtonDown(0)
            || (PadAttack != KeyCode.None && Input.GetKeyDown(PadAttack))
            || (PadJump != KeyCode.None && Input.GetKeyDown(PadJump))
            || (PadInteract != KeyCode.None && Input.GetKeyDown(PadInteract));
    }
}