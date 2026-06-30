using System;
using UnityEngine;

namespace BasicRPG.Stats
{
    /// <summary>
    /// Player health: track damage/healing, fire events, and handle death.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 100;

        public int CurrentHealth { get; private set; }
        public int MaxHealth => maxHealth;
        public float Normalized => maxHealth > 0 ? CurrentHealth / (float)maxHealth : 0f;
        public bool IsDead { get; private set; }

        /// <summary>(current, max) fired whenever damage is taken.</summary>
        public event Action<int, int> OnDamaged;
        /// <summary>(current, max) fired whenever health is restored.</summary>
        public event Action<int, int> OnHealed;
        /// <summary>Fired once when health reaches zero.</summary>
        public event Action OnDeath;

        void Awake()
        {
            CurrentHealth = maxHealth;
        }

        public void TakeDamage(int amount)
        {
            if (IsDead || amount <= 0) return;
            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            OnDamaged?.Invoke(CurrentHealth, maxHealth);
            if (CurrentHealth <= 0) Die();
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            OnHealed?.Invoke(CurrentHealth, maxHealth);
        }

        public void Kill()
        {
            if (IsDead) return;
            CurrentHealth = 0;
            OnDamaged?.Invoke(CurrentHealth, maxHealth);
            Die();
        }

        public void Revive()
        {
            IsDead = false;
            CurrentHealth = maxHealth;
            OnHealed?.Invoke(CurrentHealth, maxHealth);
        }

        /// <summary>Restore to a saved current HP (save/load). Clears death.</summary>
        public void LoadState(int current)
        {
            IsDead = false;
            CurrentHealth = Mathf.Clamp(current, 0, maxHealth);
            OnHealed?.Invoke(CurrentHealth, maxHealth);
        }

        void Die()
        {
            if (IsDead) return;
            IsDead = true;
            OnDeath?.Invoke();
            Debug.Log("[Health] Player died.");
        }
    }
}