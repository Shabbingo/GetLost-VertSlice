using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Trigger-based vegetation provider for manually placed bushes, reeds and scrub volumes.
    /// Uses the same VegetationProfile system as painted Terrain details.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class VegetationResistanceVolume : MonoBehaviour
    {
        [Header("Unified Vegetation")]
        [SerializeField] private VegetationProfile vegetationProfile;
        [SerializeField, Range(0f, 1f)] private float interactionStrength = 1f;

        [Header("Optional Physical Push")]
        [Tooltip("Adds a small outward impulse while inside the volume. Leave at 0 for pure movement resistance.")]
        [SerializeField, Min(0f)] private float pushStrength = 0f;

        private readonly HashSet<WalkingMotor> motorsInside = new();

        private void Reset()
        {
            Collider volume = GetComponent<Collider>();
            volume.isTrigger = true;
        }

        private void OnValidate()
        {
            Collider volume = GetComponent<Collider>();
            if (volume != null)
                volume.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            WalkingMotor motor = other.GetComponentInParent<WalkingMotor>();
            if (motor == null)
                return;

            motorsInside.Add(motor);
            VegetationInteractionReceiver receiver = motor.GetComponent<VegetationInteractionReceiver>();
            if (receiver != null && vegetationProfile != null)
                receiver.SetPersistentSource(this, vegetationProfile, interactionStrength);
        }

        private void OnTriggerStay(Collider other)
        {
            WalkingMotor motor = other.GetComponentInParent<WalkingMotor>();
            if (motor == null)
                return;

            motorsInside.Add(motor);
            VegetationInteractionReceiver receiver = motor.GetComponent<VegetationInteractionReceiver>();
            if (receiver != null && vegetationProfile != null)
                receiver.SetPersistentSource(this, vegetationProfile, interactionStrength);
        }

        private void OnTriggerExit(Collider other)
        {
            WalkingMotor motor = other.GetComponentInParent<WalkingMotor>();
            if (motor == null)
                return;

            motorsInside.Remove(motor);
            VegetationInteractionReceiver receiver = motor.GetComponent<VegetationInteractionReceiver>();
            if (receiver != null)
                receiver.RemoveSource(this);
        }

        private void OnDisable()
        {
            foreach (WalkingMotor motor in motorsInside)
            {
                if (motor == null)
                    continue;
                VegetationInteractionReceiver receiver = motor.GetComponent<VegetationInteractionReceiver>();
                if (receiver != null)
                    receiver.RemoveSource(this);
            }
            motorsInside.Clear();
        }

        private void FixedUpdate()
        {
            if (pushStrength <= 0f || motorsInside.Count == 0)
                return;

            motorsInside.RemoveWhere(motor => motor == null);
            foreach (WalkingMotor motor in motorsInside)
            {
                Vector3 awayFromCentre = Vector3.ProjectOnPlane(
                    motor.transform.position - transform.position,
                    Vector3.up).normalized;
                motor.AddImpulse(awayFromCentre * pushStrength * Time.fixedDeltaTime);
            }
        }
    }
}
