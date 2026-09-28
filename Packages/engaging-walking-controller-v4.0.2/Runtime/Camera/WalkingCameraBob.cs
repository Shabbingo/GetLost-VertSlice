using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Position-only walking bob module for CameraEffectsController.
    /// It never writes directly to a Transform, so it cannot fight mouse look or footing effects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WalkingCameraBob : MonoBehaviour, ICameraEffect
    {
        [SerializeField] private WalkingMotor motor;
        [SerializeField] private CameraEffectsController cameraEffects;
        [SerializeField] private bool effectEnabled = true;
        [SerializeField, Min(0f)] private float walkFrequency = 1.75f;
        [SerializeField, Min(0f)] private float horizontalAmplitude = 0.018f;
        [SerializeField, Min(0f)] private float verticalAmplitude = 0.025f;
        [SerializeField, Min(0.1f)] private float strengthResponse = 12f;

        private float phase;
        private float smoothedStrength;

        public bool CameraEffectEnabled => effectEnabled && motor != null;

        private void Reset()
        {
            motor = GetComponentInParent<WalkingMotor>();
            cameraEffects = GetComponentInParent<CameraEffectsController>();
        }

        private void Awake()
        {
            if (motor == null)
                motor = GetComponentInParent<WalkingMotor>();
            if (cameraEffects == null)
                cameraEffects = GetComponentInParent<CameraEffectsController>();
        }

        private void OnEnable()
        {
            cameraEffects?.RefreshEffects();
        }

        public CameraEffectFrame EvaluateCameraEffect(float deltaTime)
        {
            if (motor == null)
                return CameraEffectFrame.None;

            float speed01 = Mathf.InverseLerp(0.1f, 6f, motor.CurrentSpeed);
            float surfaceBob = motor.CurrentSurface != null ? motor.CurrentSurface.cameraBobMultiplier : 1f;
            float carefulMultiplier = motor.IsCarefulWalking ? 0.35f : 1f;
            float targetStrength = speed01 > 0.01f ? speed01 * surfaceBob * carefulMultiplier : 0f;

            smoothedStrength = Mathf.Lerp(
                smoothedStrength,
                targetStrength,
                1f - Mathf.Exp(-strengthResponse * deltaTime));

            if (smoothedStrength <= 0.001f)
                return CameraEffectFrame.None;

            phase += deltaTime * walkFrequency * Mathf.Lerp(0.7f, 1.8f, speed01) * Mathf.PI * 2f;
            Vector3 position = new(
                Mathf.Cos(phase * 0.5f) * horizontalAmplitude,
                Mathf.Sin(phase) * verticalAmplitude,
                0f);

            return new CameraEffectFrame(position * smoothedStrength, Vector3.zero);
        }
    }
}
