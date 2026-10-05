using System;
using GetLost.Cheats;
using UnityEngine;

namespace GetLost.Player
{
    /// <summary>
    /// Handles player health only.
    /// Visual effects are handled separately by PlayerDamageVisualController.
    /// Ported from Get-Lost-Prototype for the vertical slice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDamageController : MonoBehaviour
    {
        [Header("Health")]
        [SerializeField, Min(1f)] private float maximumHealth = 100f;
        [SerializeField] private bool startAtFullHealth = true;
        [SerializeField, Min(0f)] private float startingHealth = 100f;

        [Header("Debug")]
        [SerializeField] private bool logDamage;

        private float currentHealth;

        public float CurrentHealth => currentHealth;
        public float MaximumHealth => maximumHealth;
        public float Health01 => maximumHealth > 0f ? currentHealth / maximumHealth : 0f;
        public bool IsDead => currentHealth <= 0f;

        public event Action<float> Damaged;
        public event Action<float> Healed;
        public event Action<float> HealthChanged;
        public event Action Died;

        private void Awake()
        {
            currentHealth = startAtFullHealth
                ? maximumHealth
                : Mathf.Clamp(startingHealth, 0f, maximumHealth);
            HealthChanged?.Invoke(Health01);
        }

        public void TakeDamage(float amount)
        {
            if (amount <= 0f || IsDead || PlayerCheatState.PreventsDamage(this))
                return;

            float previousHealth = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth - amount, 0f, maximumHealth);
            float actualDamage = previousHealth - currentHealth;
            if (actualDamage <= 0f)
                return;

            if (logDamage)
            {
                Debug.Log($"[PlayerDamageController] Took {actualDamage:F1} damage. " +
                          $"Health: {currentHealth:F1}/{maximumHealth:F1}", this);
            }

            Damaged?.Invoke(actualDamage);
            HealthChanged?.Invoke(Health01);
            if (currentHealth <= 0f)
                Died?.Invoke();
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return;
            float previousHealth = currentHealth;
            currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maximumHealth);
            float actualHeal = currentHealth - previousHealth;
            if (actualHeal <= 0f)
                return;
            Healed?.Invoke(actualHeal);
            HealthChanged?.Invoke(Health01);
        }

        public void RestoreFullHealth()
        {
            currentHealth = maximumHealth;
            HealthChanged?.Invoke(Health01);
        }

        public void SetHealth(float value)
        {
            bool wasAlive = currentHealth > 0f;
            currentHealth = Mathf.Clamp(value, 0f, maximumHealth);
            HealthChanged?.Invoke(Health01);
            if (wasAlive && currentHealth <= 0f)
                Died?.Invoke();
        }

        [ContextMenu("TEST - Take 25 Damage")]
        private void TestTakeDamage()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Enter Play Mode first, then use TEST - Take 25 Damage.", this);
                return;
            }
            TakeDamage(25f);
        }

        [ContextMenu("TEST - Restore Full Health")]
        private void TestRestoreFullHealth()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Enter Play Mode first, then use TEST - Restore Full Health.", this);
                return;
            }
            RestoreFullHealth();
        }

        private void OnValidate()
        {
            maximumHealth = Mathf.Max(1f, maximumHealth);
            startingHealth = Mathf.Clamp(startingHealth, 0f, maximumHealth);
        }
    }
}
