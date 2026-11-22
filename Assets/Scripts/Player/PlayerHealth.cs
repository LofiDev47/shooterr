using System;
using UnityEngine;

namespace Shooter.Player
{
    /// <summary>
    /// Health for the player. Raises events so UI, audio and the game manager can react
    /// without polling this component every frame.
    /// </summary>
    public sealed class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;

        public float MaxHealth => maxHealth;
        public float CurrentHealth { get; private set; }
        public bool IsDead => CurrentHealth <= 0f;

        /// <summary>(current, max) after any health change.</summary>
        public event Action<float, float> HealthChanged;

        /// <summary>Damage amount, fired for screen flash and hurt sounds.</summary>
        public event Action<float> Damaged;

        /// <summary>Fired exactly once when health reaches zero.</summary>
        public event Action Died;

        private void Awake()
        {
            ResetState();
        }

        /// <summary>Restores full health. Also used by tests, since Awake does not run in edit mode.</summary>
        public void ResetState()
        {
            CurrentHealth = maxHealth;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        /// <summary>
        /// Permanently raises the maximum (Titan Frame). The current health grows by
        /// the same amount so an augment pick never feels like it shrinks the bar.
        /// </summary>
        public void IncreaseMaxHealth(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            maxHealth += amount;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        /// <summary>Applies damage, clamping at zero and raising events once.</summary>
        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            // Augment damage reduction (Kevlar Weave), read live so stacking works.
            if (Upgrades.UpgradeManager.Instance != null)
            {
                amount *= Mathf.Max(0f, 1f - Upgrades.UpgradeManager.Instance.DamageReduction);
            }

            float previous = CurrentHealth;
            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);

            if (Mathf.Approximately(previous, CurrentHealth))
            {
                return;
            }

            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            Damaged?.Invoke(amount);

            if (CurrentHealth <= 0f)
            {
                Died?.Invoke();
            }
        }

        /// <summary>Restores health, clamped to the maximum.</summary>
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
            {
                return;
            }

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }
    }
}
