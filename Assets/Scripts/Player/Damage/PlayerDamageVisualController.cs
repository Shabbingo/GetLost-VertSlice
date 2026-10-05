using System;
using System.Reflection;
using UnityEngine;

namespace GetLost.Player
{
    /// <summary>
    /// Blends a dedicated damage Volume over the existing Volume stack by controlling
    /// only its weight. Ported from Get-Lost-Prototype without a rendering dependency.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDamageVisualController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerDamageController damageController;
        [Tooltip("Drag the Volume COMPONENT from your Player Damage Volume GameObject into this field.")]
        [SerializeField] private Component damageVolume;

        [Header("Health Blend")]
        [SerializeField, Min(0.01f)] private float effectExponent = 1.35f;
        [SerializeField, Range(0f, 1f)] private float maximumWeight = 1f;
        [SerializeField, Min(0f)] private float smoothSpeed = 5f;

        [Header("Damage Hit Flash")]
        [SerializeField, Range(0f, 1f)] private float minimumHitFlashWeight = 0.34f;
        [SerializeField, Min(0f)] private float hitFlashWeightPerDamage = 0.025f;
        [SerializeField, Min(0f)] private float hitFlashFadeSpeed = 1.8f;

        [Header("Debug")]
        [SerializeField] private bool logSetupProblems = true;
        [SerializeField] private float currentDamageWeight;

        private PropertyInfo weightProperty;
        private FieldInfo weightField;
        private PlayerDamageController subscribedController;
        private float hitFlashWeight;

        public void Initialize(PlayerDamageController controller, Component volume)
        {
            UnsubscribeFromDamage();
            damageController = controller;
            damageVolume = volume;
            SubscribeToDamage();
            CacheVolumeWeightMember();
            currentDamageWeight = 0f;
            SetVolumeWeight(0f);
        }

        private void Reset() => damageController = GetComponent<PlayerDamageController>();

        private void Awake()
        {
            if (damageController == null)
                damageController = GetComponent<PlayerDamageController>();
            if (damageVolume == null)
            {
                Transform volumeTransform = transform.Find("HealthEffectsVolume");
                if (volumeTransform != null)
                {
                    foreach (Component component in volumeTransform.GetComponents<Component>())
                    {
                        if (component != null && component.GetType().FullName == "UnityEngine.Rendering.Volume")
                        {
                            damageVolume = component;
                            break;
                        }
                    }
                }
            }
            CacheVolumeWeightMember();
            SubscribeToDamage();
            currentDamageWeight = 0f;
            SetVolumeWeight(0f);
        }

        private void Update()
        {
            if (damageController == null || damageVolume == null)
                return;
            float injury01 = 1f - Mathf.Clamp01(damageController.Health01);
            float healthWeight = Mathf.Pow(injury01, Mathf.Max(0.01f, effectExponent)) * maximumWeight;
            hitFlashWeight = Mathf.MoveTowards(hitFlashWeight, 0f,
                hitFlashFadeSpeed * Time.unscaledDeltaTime);
            float targetWeight = Mathf.Max(healthWeight, hitFlashWeight);
            currentDamageWeight = smoothSpeed <= 0f
                ? targetWeight
                : Mathf.MoveTowards(currentDamageWeight, targetWeight, smoothSpeed * Time.unscaledDeltaTime);
            SetVolumeWeight(currentDamageWeight);
        }

        private void SubscribeToDamage()
        {
            if (damageController == null || subscribedController == damageController)
                return;
            subscribedController = damageController;
            subscribedController.Damaged += HandleDamaged;
        }

        private void UnsubscribeFromDamage()
        {
            if (subscribedController == null)
                return;
            subscribedController.Damaged -= HandleDamaged;
            subscribedController = null;
        }

        private void HandleDamaged(float amount)
        {
            hitFlashWeight = Mathf.Clamp01(Mathf.Max(minimumHitFlashWeight,
                minimumHitFlashWeight + amount * hitFlashWeightPerDamage));
        }

        private void CacheVolumeWeightMember()
        {
            weightProperty = null;
            weightField = null;
            if (damageVolume == null)
            {
                if (logSetupProblems)
                    Debug.LogWarning("[PlayerDamageVisualController] No Damage Volume assigned.", this);
                return;
            }

            Type volumeType = damageVolume.GetType();
            weightField = volumeType.GetField("weight", BindingFlags.Instance | BindingFlags.Public);
            weightProperty = volumeType.GetProperty("weight", BindingFlags.Instance | BindingFlags.Public);
            if (weightField == null || weightField.FieldType != typeof(float))
                weightField = null;
            if (weightProperty == null || weightProperty.PropertyType != typeof(float) || !weightProperty.CanWrite)
                weightProperty = null;
            if (weightField == null && weightProperty == null)
            {
                Debug.LogError($"[PlayerDamageVisualController] '{damageVolume.GetType().FullName}' " +
                               "has no writable float field/property named 'weight'.", this);
            }
        }

        private void SetVolumeWeight(float value)
        {
            if (damageVolume == null)
                return;
            float clamped = Mathf.Clamp01(value);
            if (weightField != null)
                weightField.SetValue(damageVolume, clamped);
            else if (weightProperty != null && weightProperty.CanWrite)
                weightProperty.SetValue(damageVolume, clamped);
        }

        private void OnDisable()
        {
            UnsubscribeFromDamage();
            SetVolumeWeight(0f);
        }

        private void OnEnable() => SubscribeToDamage();

        private void OnValidate()
        {
            effectExponent = Mathf.Max(0.01f, effectExponent);
            maximumWeight = Mathf.Clamp01(maximumWeight);
            minimumHitFlashWeight = Mathf.Clamp01(minimumHitFlashWeight);
        }
    }
}
