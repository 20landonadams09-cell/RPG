using UnityEngine;
using BasicRPG.UI;
using BasicRPG.Interaction;

namespace BasicRPG.Stats
{
    /// <summary>
    /// Glue on the player: owns the Health + Stamina links, drives the HUD bars every
    /// frame, and exposes test inputs (K = take 10 damage, H = heal 10) so the bars are
    /// immediately observable without enemies.
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Stamina stamina;
        [SerializeField] private StatBar healthBar;
        [SerializeField] private StatBar staminaBar;

        void Reset()
        {
            health = GetComponent<Health>();
            stamina = GetComponent<Stamina>();
        }

        void Update()
        {
            if (health != null && healthBar != null)
                healthBar.SetFill(health.Normalized);
            if (stamina != null && staminaBar != null)
                staminaBar.SetFill(stamina.Normalized);

            // Test inputs — disabled while the tutorial is running so they can't disrupt a step.
            if (Input.GetKeyDown(KeyCode.K) && health != null && !InteractionLock.TutorialActive) health.TakeDamage(10);
            if (Input.GetKeyDown(KeyCode.H) && health != null && !InteractionLock.TutorialActive) health.Heal(10);
        }
    }
}