using UnityEngine;

namespace BasicRPG.Stats
{
    /// <summary>
    /// Stamina resource: drains while sprinting, regenerates after a short delay.
    /// Once exhausted, sprint stays locked out until the bar recovers above a threshold
    /// (prevents stutter/flicker at the bottom of the bar).
    /// </summary>
    public class Stamina : MonoBehaviour
    {
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float regenRate = 12f;          // per second
        [SerializeField] private float regenDelay = 1f;          // seconds of no-consume before regen starts
        [SerializeField] private float exhaustedThreshold = 0.15f; // fraction; must recover above this to sprint again

        public float Current { get; private set; }
        public float Max => maxStamina;
        public float Normalized => maxStamina > 0 ? Current / maxStamina : 0f;
        public bool IsExhausted { get; private set; }

        private float timeSinceConsume = Mathf.Infinity;

        // ── Pewter fatigue banking ─────────────────────────────────────────────────
        // While Pewter burns it sets SuppressDrain: stamina drains are banked into
        // suppressedFatigue instead of actually consuming the bar (so sprinting is "free"
        // during Pewter). When Pewter stops, the banked fatigue is dumped back — optionally
        // with interest. A drag crash clears the bank and forces exhaustion instead.
        public bool SuppressDrain;
        private float suppressedFatigue;

        void Awake()
        {
            Current = maxStamina;
        }

        void Update()
        {
            if (timeSinceConsume < regenDelay)
            {
                timeSinceConsume += Time.deltaTime;
            }
            else if (Current < maxStamina)
            {
                Current = Mathf.Min(maxStamina, Current + regenRate * Time.deltaTime);
                if (IsExhausted && Current >= maxStamina * exhaustedThreshold + 0.01f)
                    IsExhausted = false;
            }
        }

        /// <summary>
        /// Attempt to drain stamina. Returns true if there is enough to keep sprinting
        /// (and the player is not in exhausted lockout), false otherwise.
        /// </summary>
        public bool TryConsume(float amount)
        {
            if (IsExhausted) return false;

            // Pewter suppression: bank the drain instead of consuming — fatigue accumulates
            // silently and is dumped (with interest) when Pewter stops burning.
            if (SuppressDrain)
            {
                suppressedFatigue += amount;
                return true; // always allowed while Pewter suppresses drain
            }

            if (Current <= 0f)
            {
                IsExhausted = true;
                return false;
            }
            Current = Mathf.Max(0f, Current - amount);
            timeSinceConsume = 0f;
            if (Current <= 0f) IsExhausted = true;
            return true;
        }

        /// <summary>Release Pewter suppression: dump the banked fatigue, optionally with interest
        /// (1.5 = you owe 50% more than you actually spent). Clears the bank.</summary>
        public void DumpSuppressedFatigue(float interest)
        {
            if (suppressedFatigue <= 0f) { suppressedFatigue = 0f; return; }
            Current = Mathf.Max(0f, Current - suppressedFatigue * interest);
            suppressedFatigue = 0f;
            timeSinceConsume = 0f;
            if (Current <= maxStamina * exhaustedThreshold) IsExhausted = true;
        }

        /// <summary>Discard the banked fatigue without applying it (used on a drag crash — the
        /// crash exhaustion replaces it so fatigue isn't double-applied).</summary>
        public void ClearSuppressedFatigue() => suppressedFatigue = 0f;

        /// <summary>Force exhaustion for a Pewter drag crash: stamina to zero, locked out.</summary>
        public void TriggerCrashExhaustion()
        {
            Current = 0f;
            suppressedFatigue = 0f;
            IsExhausted = true;
            timeSinceConsume = 0f;
        }

        /// <summary>Restore stamina (e.g. from a consumable). Clears exhaustion above the threshold.</summary>
        public void Restore(float amount)
        {
            if (amount <= 0f) return;
            Current = Mathf.Min(maxStamina, Current + amount);
            if (IsExhausted && Current >= maxStamina * exhaustedThreshold + 0.01f)
                IsExhausted = false;
        }

        /// <summary>Restore to a saved current value (save/load). Sets exhaustion to match.</summary>
        public void LoadState(float current)
        {
            Current = Mathf.Clamp(current, 0f, maxStamina);
            IsExhausted = Current <= maxStamina * exhaustedThreshold;
        }
    }
}