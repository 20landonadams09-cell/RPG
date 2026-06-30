using UnityEngine;
using BasicRPG.Items;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Core allomancy component on the player. Owns the per-metal reserve pool, the burn
    /// toggle, the drain-over-time / passive-refill loop, active-metal selection (number
    /// keys 1–8), and the drink-from-inventory action. Adapted from Ashwalker's
    /// Metallurgist.cs — self-contained (no FlareManager/MetalSelector/skill-tree deps).
    /// </summary>
    public class Allomancer : MonoBehaviour
    {
        [SerializeField] private Inventory inventory;
        [SerializeField] private AllomancyHUD hud;
        [Tooltip("Inventory metal item per MetalType (indexed by (int)MetalType). Wired by the scene builder; only metals with an item can be drunk.")]
        [SerializeField] private ItemSO[] metalItems = new ItemSO[Metals.Count];

        [Header("Reserves")]
        public float MaxReserve = MetallurgyConstants.DefaultMaxReserve;
        [Tooltip("Reserve regen per second when not burning. 0 = fuel only comes from drinking inventory metals.")]
        public float PassiveRecovery = 0f;

        private readonly float[] reserves = new float[Metals.Count];
        private readonly bool[] unlocked = new bool[Metals.Count];
        private bool isBurning;
        private MetalType activeMetal = MetalType.Pewter;

        // Flare ("burn harder"): hold Keybinds.Flare while burning to ramp the active metal's
        // intensity (read by effect components) at the cost of draining its reserve faster.
        private const float FlareMax = 1.8f;     // peak intensity multiplier while flaring
        private const float FlareRamp = 4f;       // how fast flare climbs; falls back at 2x this
        private float flare = 1f;

        public MetalType ActiveMetal => activeMetal;
        public bool IsBurning => isBurning;
        /// <summary>Active flare multiplier (1 = normal burn, up to ~1.8 while holding Flare).</summary>
        public float FlareMultiplier => flare;

        void Awake()
        {
            // Starter charge + unlock for the 8 basic metals; higher metals start empty/locked.
            for (int i = 0; i < Metals.Selectable.Length; i++)
            {
                int idx = (int)Metals.Selectable[i];
                reserves[idx] = MaxReserve;
                unlocked[idx] = true;
            }
        }

        void Update()
        {
            // Dialogue/inventory own input while open — pause allomancy.
            if (InteractionLock.IsLocked) return;

            if (Input.GetKeyDown(Keybinds.BurnToggle))
                ToggleBurning();

            // Number keys 1–8 select the active metal.
            for (int i = 0; i < Keybinds.SelectMetal.Length; i++)
            {
                if (Input.GetKeyDown(Keybinds.SelectMetal[i]))
                {
                    SetCurrentMetal(Metals.Selectable[i]);
                    break;
                }
            }

            // Drink/refill are not part of any tutorial step (test scenes start at full reserve) —
            // hold them while the tutorial runs so the player can't waste ingots or debug-refill
            // mid-step. Burn/select/flare above are intentionally NOT gated (the tutorial needs them).
            if (Input.GetKeyDown(Keybinds.DrinkVial) && !InteractionLock.TutorialActive) Drink();
            if (Input.GetKeyDown(Keybinds.RefillAll) && !InteractionLock.TutorialActive) RefillAll();

            // Flare ramps up while holding Flare + burning with reserve left; otherwise settles
            // back to 1. Flaring multiplies the active metal's drain (the cost of burning harder)
            // and is read by effect components to scale their intensity.
            bool flaring = isBurning && Input.GetKey(Keybinds.Flare) && GetReserve(activeMetal) > 0f;
            flare = Mathf.MoveTowards(flare, flaring ? FlareMax : 1f,
                (flaring ? FlareRamp : FlareRamp * 2f) * Time.deltaTime);

            // Drain while burning; auto-stop when the active reserve runs dry.
            if (isBurning)
            {
                if (GetReserve(activeMetal) <= 0f)
                {
                    StopBurning();
                }
                else
                {
                    MetalDefinition def = MetalDatabase.Get(activeMetal);
                    float drain = def != null ? def.drainRate : 0f;
                    DrainMetal(activeMetal, drain * flare * Time.deltaTime);
                }
            }
            else if (PassiveRecovery > 0f)
            {
                RefillMetal(activeMetal, PassiveRecovery * Time.deltaTime);
            }

            UpdateHUD();
        }

        // ── Public API ─────────────────────────────────────────────────────────────

        public void StartBurning()
        {
            if (!unlocked[(int)activeMetal]) { isBurning = false; return; }
            isBurning = true;
        }

        public void StopBurning() => isBurning = false;

        public void ToggleBurning() { if (isBurning) StopBurning(); else StartBurning(); }

        public bool IsMetalBurning(MetalType metal) => isBurning && activeMetal == metal;

        public void SetCurrentMetal(MetalType metal)
        {
            if (!unlocked[(int)metal]) return;
            activeMetal = metal;
        }

        public MetalType GetCurrentMetal() => activeMetal;

        public float GetReserve(MetalType metal) => reserves[(int)metal];

        public void DrainMetal(MetalType metal, float amount)
        {
            int i = (int)metal;
            reserves[i] = Mathf.Max(0f, reserves[i] - amount);
        }

        public void RefillMetal(MetalType metal, float amount)
        {
            int i = (int)metal;
            reserves[i] = Mathf.Min(MaxReserve, reserves[i] + amount);
        }

        public void RefillAll()
        {
            for (int i = 0; i < reserves.Length; i++) reserves[i] = MaxReserve;
        }

        public bool IsUnlocked(MetalType metal) => unlocked[(int)metal];

        public ItemSO GetMetalItem(MetalType metal)
        {
            int i = (int)metal;
            return i < metalItems.Length ? metalItems[i] : null;
        }

        // ── Save / load ───────────────────────────────────────────────────────────────

        /// <summary>Snapshot of the reserve pool (indexed by (int)MetalType).</summary>
        public float[] SaveReserves()
        {
            float[] copy = new float[reserves.Length];
            System.Array.Copy(reserves, copy, reserves.Length);
            return copy;
        }

        /// <summary>Restore reserves, the active metal (if unlocked), and the burn toggle.</summary>
        public void LoadReserves(float[] saved, MetalType active, bool burning)
        {
            if (saved != null)
            {
                for (int i = 0; i < reserves.Length && i < saved.Length; i++)
                    reserves[i] = Mathf.Clamp(saved[i], 0f, MaxReserve);
            }
            if (unlocked[(int)active]) activeMetal = active;
            isBurning = burning && unlocked[(int)activeMetal] && GetReserve(activeMetal) > 0f;
            UpdateHUD();
        }

        // ── Drink from inventory ────────────────────────────────────────────────────

        /// <summary>Consume one ingot of the active metal from the inventory to refill its reserve.</summary>
        public void Drink()
        {
            ItemSO item = GetMetalItem(activeMetal);
            if (item == null)
            {
                NotificationUI.Show("No metal to drink");
                return;
            }
            if (inventory == null || !inventory.Consume(item, 1))
            {
                NotificationUI.Show($"Out of {item.displayName}");
                return;
            }
            MetalDefinition def = MetalDatabase.Get(activeMetal);
            float amount = def != null ? def.drinkAmount : 50f;
            RefillMetal(activeMetal, amount);
            NotificationUI.Show($"Drank {item.displayName} (+{Mathf.RoundToInt(amount)} reserve)");
        }

        // ── HUD ─────────────────────────────────────────────────────────────────────

        void UpdateHUD()
        {
            if (hud != null) hud.UpdateDisplay(reserves, activeMetal, isBurning, flare);
        }
    }
}