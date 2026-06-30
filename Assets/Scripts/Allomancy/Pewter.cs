using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Stats;
using BasicRPG.Player;
using BasicRPG.Combat;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Pewter (Thug) — physical enhancement: strength, speed, jump, durability, mending, and
    /// stamina-fatigue banking. Adapted from Ashwalker's Pewter.cs for BasicRPG's
    /// CharacterController player (multipliers read by PlayerController/PlayerCombat/Stamina
    /// instead of direct Rigidbody writes) and ugui vignette (instead of an HDRP Volume).
    ///
    /// Lore beats implemented:
    /// • Strength / speed / jump — a Pewterarm hits, runs, and leaps far beyond a human, scaled up
    ///   further while flaring (hold Flare/R).
    /// • Endurance — burning pewter suppresses stamina drain (sprinting is "free" while it burns);
    ///   the spent fatigue is banked and dumped back, with interest, when the burn stops.
    /// • The Drag / Dragging — burn continuously and the banked fatigue + a systemic crash build;
    ///   burn past `dragCrashThreshold` then stop and you crash (damage, exhaustion, slowness).
    /// • Held wounds — while burning, a fraction of each incoming hit is held at bay (banked by
    ///   PlayerCombat), not applied. The instant pewter stops (toggled off, runs dry, or crashes)
    ///   that banked shock lands as one hit — "the wounds were always real; pewter just hid them."
    /// • Mend — a small flare-scaled heal while burning (pewter knits minor wounds faster).
    ///
    /// Burn gate: Allomancer.IsMetalBurning(Pewter) (the single B toggle on the active metal).
    /// Pewter Push (F knockback) is deferred — no Rigidbody/EnemyKnockback enemies in BasicRPG.
    /// </summary>
    public class Pewter : MonoBehaviour
    {
        [SerializeField] private Allomancer allomancer;
        [SerializeField] private PlayerController mover;
        [SerializeField] private PlayerCombat combat;
        [SerializeField] private Stamina stamina;
        [SerializeField] private Health health;
        [Tooltip("Full-screen red vignette overlay (built by the scene builder).")]
        [SerializeField] private Image vignette;

        [Header("Strength / Speed / Jump (base burn; flaring multiplies these)")]
        public float baseStrengthMultiplier = 2.5f;   // Thug melee hits land far harder
        public float baseSpeedMultiplier = 1.6f;      // longer stride, faster sprint
        public float baseJumpMultiplier = 1.7f;        // clears gaps a normal person can't

        [Header("Mend (Healing)")]
        public float baseHealRate = 1.5f; // hp per second — pewter knits minor wounds faster

        [Header("Drain — fastest of the 8 basic metals")]
        public float baseDrainPerSecond = MetallurgyConstants.PewterDrainRate;

        [Header("Pewter Drag Crash")]
        public float dragCrashThreshold = 30f;
        public float crashDamage = 25f;
        [Range(0.1f, 0.9f)] public float crashSpeedPenalty = 0.4f;
        public float crashDuration = 5f;

        [Header("Endurance (Stamina)")]
        [Range(1f, 3f)] public float fatigueInterestMultiplier = 1.5f;

        [Header("Held Wounds (lore — pewter holds the shock at bay)")]
        [Tooltip("While burning, this fraction of each incoming hit is banked instead of applied. The banked shock is released as one hit the instant pewter stops (toggled off, runs dry, or drag-crashes). 0.5 = half of every wound is held until you drop pewter.")]
        [Range(0f, 0.9f)] public float deferWoundFraction = 0.5f;

        [Header("Vignette")]
        [Range(0f, 0.35f)] public float vignetteIntensity = 0.13f;
        public Color vignetteColor = new Color(0.8f, 0.2f, 0.2f, 1f);

        [Header("Damage Reduction (toughness — the wound is still real, but you're harder to hurt)")]
        [Range(0.2f, 1f)] public float baseDamageMultiplier = 0.8f; // fraction taken

        private bool isBurning;
        private float dragTimer;
        private float crashTimer;
        private bool isCrashing;
        private float healBuffer;

        public bool IsBurningPewter => isBurning;
        public bool IsCrashing => isCrashing;

        void Start()
        {
            if (vignette != null)
            {
                vignette.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);
                vignette.raycastTarget = false;
            }
        }

        void Update()
        {
            bool wasBurning = isBurning;
            float reserve = allomancer != null ? allomancer.GetReserve(MetalType.Pewter) : 0f;
            isBurning = allomancer != null && allomancer.IsMetalBurning(MetalType.Pewter) && reserve > 0f;

            // Crash recovery tick
            if (isCrashing)
            {
                crashTimer -= Time.deltaTime;
                if (crashTimer <= 0f)
                {
                    isCrashing = false;
                    if (mover != null) mover.SetAllomancyScale(1f, 1f);
                }
            }

            UpdateVignette();

            if (isBurning)
            {
                dragTimer += Time.deltaTime; // all burn time counts toward the crash (flaring included)
                ApplyEffects();
                HandleHealing();
                DrainReserve();
            }
            else if (wasBurning)
            {
                // Burn just stopped (or reserve ran dry) — crash if we dragged long enough.
                if (dragTimer >= dragCrashThreshold && !isCrashing)
                    TriggerDragCrash();
                else
                    ResetEffects();
                dragTimer = 0f;
            }
        }

        // ── Effects ────────────────────────────────────────────────────────────────

        void ApplyEffects()
        {
            // Flare scales intensity (held Keybinds.Flare while burning). 1 = normal burn.
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            if (mover != null) mover.SetAllomancyScale(baseSpeedMultiplier * f, baseJumpMultiplier * f);
            if (combat != null)
            {
                combat.SetPewterStrength(baseStrengthMultiplier * f);
                combat.SetDamageReduction(baseDamageMultiplier);
                combat.SetPewterDeferFraction(deferWoundFraction); // hold part of each wound at bay
            }
            if (stamina != null) stamina.SuppressDrain = true;
        }

        void ResetEffects()
        {
            if (mover != null) mover.SetAllomancyScale(1f, 1f);
            if (combat != null)
            {
                combat.SetPewterStrength(1f);
                combat.SetDamageReduction(1f);
                combat.SetPewterDeferFraction(0f);
                combat.ReleasePewterDeferredWounds(); // held wounds hit the instant pewter drops
            }
            if (stamina != null)
            {
                stamina.SuppressDrain = false;
                stamina.DumpSuppressedFatigue(fatigueInterestMultiplier); // banked fatigue hits with interest
            }
        }

        void TriggerDragCrash()
        {
            isCrashing = true;
            crashTimer = crashDuration;

            if (health != null) health.TakeDamage(Mathf.RoundToInt(crashDamage));
            if (stamina != null)
            {
                stamina.SuppressDrain = false;
                stamina.ClearSuppressedFatigue();       // crash exhaustion replaces it (no double-apply)
                stamina.TriggerCrashExhaustion();
            }
            if (mover != null) mover.SetAllomancyScale(crashSpeedPenalty, 1f);
            if (combat != null)
            {
                combat.SetPewterStrength(1f);
                combat.SetDamageReduction(1f);
                combat.SetPewterDeferFraction(0f);
                combat.ReleasePewterDeferredWounds(); // at the crash, every held wound lands at once
            }
            NotificationUI.Show("Pewter drag crash!");
        }

        void HandleHealing()
        {
            if (health == null || health.IsDead) return;
            if (health.CurrentHealth >= health.MaxHealth) { healBuffer = 0f; return; }
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            healBuffer += baseHealRate * f * Time.deltaTime;
            if (healBuffer >= 1f)
            {
                int h = Mathf.FloorToInt(healBuffer);
                health.Heal(h);
                healBuffer -= h;
            }
        }

        void DrainReserve()
        {
            if (allomancer == null) return;
            // Flaring burns Pewter harder — drain its reserve faster (the cost of the boost).
            float f = allomancer.FlareMultiplier;
            allomancer.DrainMetal(MetalType.Pewter, baseDrainPerSecond * f * Time.deltaTime);
        }

        void UpdateVignette()
        {
            if (vignette == null) return;
            float target = isBurning ? vignetteIntensity : 0f;
            Color c = vignette.color;
            c.a = Mathf.Lerp(c.a, target, Time.deltaTime * 5f);
            vignette.color = c;
        }

        void OnDestroy()
        {
            // Restore baselines if destroyed mid-burn (scene unload).
            if (isBurning || isCrashing) ResetEffects();
        }
    }
}