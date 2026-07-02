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
        [Tooltip("Inventory metal item per MetalType (indexed by (int)MetalType). Wired by the scene builder; only metals with an item can be drunk.")]
        [SerializeField] private ItemSO[] metalItems = new ItemSO[Metals.Count];

        [Header("Reserves")]
        public float MaxReserve = MetallurgyConstants.DefaultMaxReserve;
        [Tooltip("Reserve regen per second when not burning. 0 = fuel only comes from drinking inventory metals.")]
        public float PassiveRecovery = 0f;

        private readonly float[] reserves = new float[Metals.Count];
        private readonly bool[] unlocked = new bool[Metals.Count];

        // Multi-burn: a SET of metals can burn at once (canon — Mistborn allomancers regularly
        // burn 2+ metals simultaneously). `burning[i]` is the burn-set membership (what the Tab
        // metal-selection wheel toggles); `burnPaused` is the B-key global pause that stops all
        // effects+drain without clearing the set. `activeMetal` is the SELECTED metal (number-key
        // focus / drink target / passive-recovery target) — NOT necessarily a burning one.
        private readonly bool[] burning = new bool[Metals.Count];
        private bool burnPaused;
        private MetalType activeMetal = MetalType.Pewter;

        // Flare ("burn harder") — the flare wheel, integrated from Ashwalker's FlareManager. While
        // burning, the mouse scroll wheel sets a discrete flare intensity 1..maxFlareSteps; the
        // multiplier FlareMultiplier = Lerp(1, maxFlareMultiplier, (intensity-1)/(steps-1)) scales the
        // active metal's effect (read by every effect component) AND its drain (the cost of burning
        // harder). Intensity PERSISTS when burning is toggled off (Ashwalker behaviour). R / gamepad
        // Options (hold) = a flare-to-max override (jumps to maxFlareMultiplier while held, snaps back
        // to the scroll intensity on release) so there's a flare path without a scroll wheel.
        [Header("Flare wheel (scroll-wheel intensity 1..maxFlareSteps)")]
        [Tooltip("Discrete flare intensity steps the scroll wheel moves through (1 = standard burn).")]
        public int maxFlareSteps = 10;
        [Tooltip("Intensity change per scroll tick.")]
        public int scrollStepSize = 1;
        [Tooltip("Force/effect multiplier at the top flare step. F = k·m_metal·B/(r+δ)² scales linearly with B. 3.2 matches Ashwalker; raise/lower for feel.")]
        [Range(1f, 10f)]
        public float maxFlareMultiplier = 3.2f;

        private int flareIntensity = 1;        // 1..maxFlareSteps; persists across burn toggles
        private bool flareToMaxHeld;          // R / Options held → override FlareMultiplier to max

        public MetalType ActiveMetal => activeMetal;
        /// <summary>True if at least one metal is currently burning and not paused. (Replaces the
        /// old single-`isBurning` flag; kept under the same name so existing consumers — IronSteel
        /// debug readout, FlareIntensityHUD, TutorialOverlay, SaveSystem — work unchanged.)</summary>
        public bool IsBurning => AnyBurning;
        /// <summary>True while the B-key global pause holds (effects + drain stopped, burn-set
        /// membership retained). The flare ring reads this to show a "paused" state.</summary>
        public bool IsBurningPaused => burnPaused;
        /// <summary>Current scroll-wheel flare intensity (1..maxFlareSteps); for the HUD. Persists
        /// when not burning.</summary>
        public int FlareIntensity => flareIntensity;
        /// <summary>Active flare multiplier B (1 = standard burn). Dimensionless — it scales the
        /// allomantic force F and the drain. While burning: Lerp(1, maxFlareMultiplier,
        /// (flareIntensity-1)/(maxFlareSteps-1)); R/Options held → maxFlareMultiplier; not burning → 1.
        /// Flare is GLOBAL for v1 — one R-hold / scroll wheel flares every burning metal uniformly.
        /// UNIT MAPPING: F = k·m_metal·B/(r+δ)² is in SI/Unity units — F in Newtons (kg·m/s²), r & δ
        /// in metres, m in kg, B dimensionless (this property). k (m³/s², tuned empirically) converts
        /// those into a force in N; velocity caps in PlayerController/Enemy/ShoveRigidbody clamp the
        /// resulting motion. So B is a pure scale factor — no unit conversion is needed here.</summary>
        public float FlareMultiplier =>
            AnyBurning
                ? (flareToMaxHeld
                    ? maxFlareMultiplier
                    : Mathf.Lerp(1f, maxFlareMultiplier, (flareIntensity - 1f) / Mathf.Max(1, maxFlareSteps - 1)))
                : 1f;

        /// <summary>Any metal alight and not paused (the multi-burn generalization of the old
        /// single isBurning flag). Drives FlareMultiplier, the HUD flare ticks, and tutorials.</summary>
        bool AnyBurning
        {
            get
            {
                if (burnPaused) return false;
                for (int i = 0; i < burning.Length; i++) if (burning[i]) return true;
                return false;
            }
        }

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

            // B (or △) is the GLOBAL burn pause/resume — stops all effects + drain without
            // clearing the burn set (so the Tab wheel's selection survives a pause). The burn set
            // itself is toggled in the freeze-time MetalWheel (Tab).
            if (Keybinds.BurnDown())
                TogglePauseBurning();

            // Number keys 1–8 select the focused metal (drink target / passive-recovery target /
            // HUD focus) — NOT the burn set.
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
            if (Keybinds.DrinkDown() && !InteractionLock.TutorialActive) Drink();
            if (Keybinds.RefillDown() && !InteractionLock.TutorialActive) RefillAll();

            // Flare wheel (Ashwalker FlareManager model): while anything burns, the scroll wheel
            // steps the discrete flare intensity 1..maxFlareSteps (persists across pauses). R /
            // gamepad Options held = flare-to-max override (no scroll wheel on gamepad). Intensity 1
            // is the efficient standard rate; scrolling up flares — the accelerated, faster-draining
            // boost. Flare is GLOBAL (v1): one R-hold / scroll flares every burning metal. The drain
            // below scales with FlareMultiplier.
            flareToMaxHeld = Keybinds.FlareHeld();
            if (AnyBurning)
            {
                float scroll = Keybinds.ScrollWheelDelta();
                if (scroll != 0f)
                    flareIntensity = Mathf.Clamp(flareIntensity + (scroll > 0f ? scrollStepSize : -scrollStepSize),
                                                 1, maxFlareSteps);
            }

            // Multi-burn drain: every metal in the burn set drains its base rate * flare. Each metal
            // auto-stops INDEPENDENTLY when its own reserve runs dry (one running out doesn't kill
            // the others). Copper/Bronze/ZincBrass don't self-drain an active-use surcharge, so this
            // central loop is their ONLY drain — it's what stops them burning for free under
            // multi-burn. IronSteel/Pewter/Tin additionally self-drain an active-use surcharge on
            // top (the "burning harder while actively using it costs extra" model). When nothing is
            // burning, passive recovery refills the SELECTED metal.
            bool anyBurning = false;
            for (int i = 0; i < burning.Length; i++)
            {
                if (!burning[i]) continue;
                MetalType m = (MetalType)i;
                if (GetReserve(m) <= 0f) { StopBurning(m); continue; }
                MetalDefinition def = MetalDatabase.Get(m);
                float drain = def != null ? def.drainRate : 0f;
                DrainMetal(m, drain * FlareMultiplier * Time.deltaTime);
                anyBurning = true;
            }
            if (!anyBurning && !burnPaused && PassiveRecovery > 0f)
                RefillMetal(activeMetal, PassiveRecovery * Time.deltaTime);
        }

        // ── Public API ─────────────────────────────────────────────────────────────

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>Add `m` to the burn set (starts burning it, if unlocked + reserve left + not
        /// paused). Idempotent. Per-metal so the MetalWheel can build a multi-metal burn set.</summary>
        public void StartBurning(MetalType m)
        {
            int i = (int)m;
            if (!unlocked[i]) return;
            if (GetReserve(m) <= 0f) return;
            burning[i] = true;
        }

        /// <summary>Remove `m` from the burn set (stops burning it).</summary>
        public void StopBurning(MetalType m) => burning[(int)m] = false;

        /// <summary>Toggle `m` in/out of the burn set — what the MetalWheel confirm calls.</summary>
        public void ToggleBurning(MetalType m) { if (burning[(int)m]) StopBurning(m); else StartBurning(m); }

        /// <summary>Replace the whole burn set at once (used by the MetalWheel on a confirmed
        /// close: starts/stops metals to match the pending toggles). On-metals are gated on
        /// unlocked + reserve; off-metals are cleared unconditionally.</summary>
        public void SetBurningSet(bool[] set)
        {
            for (int i = 0; i < burning.Length; i++)
            {
                bool want = set != null && i < set.Length && set[i];
                burning[i] = want && unlocked[i] && reserves[i] > 0f;
            }
        }

        // Single-metal convenience wrappers (operate on the SELECTED metal). Kept for legacy
        // callers; the B key no longer uses these (it pauses — see TogglePauseBurning).
        public void StartBurning() => StartBurning(activeMetal);
        public void StopBurning()  => StopBurning(activeMetal);
        public void ToggleBurning() => ToggleBurning(activeMetal);

        /// <summary>True iff `metal` is in the burn set AND not globally paused AND has reserve.
        /// This is the per-metal burn query every effect component (IronSteel/Pewter/Tin/Copper/
        /// Bronze/ZincBrass) polls each frame; making it set-based is what makes them multi-burn-
        /// safe. Note: a paused burn (B) reads false here, so effects stop, but the set membership
        /// (`IsMetalSelectedForBurning`) is retained for resume.</summary>
        public bool IsMetalBurning(MetalType metal) =>
            !burnPaused && burning[(int)metal] && GetReserve(metal) > 0f;

        /// <summary>True iff `metal` is in the burn set, ignoring the global pause. Used by the
        /// MetalWheel to highlight the current selection across a B-pause.</summary>
        public bool IsMetalSelectedForBurning(MetalType metal) => burning[(int)metal];

        /// <summary>Global B-key pause: stops all effects + drain (IsMetalBurning → false) WITHOUT
        /// clearing the burn set, so the Tab wheel's selection survives. Toggle again to resume.</summary>
        public void TogglePauseBurning() => burnPaused = !burnPaused;
        public void SetPaused(bool paused) => burnPaused = paused;

        /// <summary>The metals currently in the burn set (ignoring pause) — one per burning metal.
        /// The flare ring (MetalRingDriver) splits into one arc per metal here, so this is the
        /// canonical multi-burn query. Under pause the set is unchanged; IsMetalBurning gates the
        /// actual effects.</summary>
        public System.Collections.Generic.IEnumerable<MetalType> BurningMetals
        {
            get
            {
                for (int i = 0; i < burning.Length; i++)
                    if (burning[i]) yield return (MetalType)i;
            }
        }

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

        /// <summary>Snapshot of the burn-set membership (indexed by (int)MetalType) — what the
        /// Tab wheel has toggled on. Captured as-is (ignores the B-pause) so a save/resume restores
        /// the player's selection even if they were paused.</summary>
        public bool[] SaveBurningSet()
        {
            bool[] copy = new bool[burning.Length];
            System.Array.Copy(burning, copy, burning.Length);
            return copy;
        }

        /// <summary>Restore reserves, the selected metal (if unlocked), and the burn set. The burn
        /// set is a per-metal bool array (multi-burn); only unlocked metals with reserve left are
        /// restored to burning. `paused` is the B-key global pause state.</summary>
        public void LoadReserves(float[] saved, MetalType active, bool[] burnSet, bool paused = false)
        {
            if (saved != null)
            {
                for (int i = 0; i < reserves.Length && i < saved.Length; i++)
                    reserves[i] = Mathf.Clamp(saved[i], 0f, MaxReserve);
            }
            if (unlocked[(int)active]) activeMetal = active;
            for (int i = 0; i < burning.Length; i++)
                burning[i] = burnSet != null && i < burnSet.Length && burnSet[i]
                             && unlocked[i] && reserves[i] > 0f;
            burnPaused = paused;
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
    }
}