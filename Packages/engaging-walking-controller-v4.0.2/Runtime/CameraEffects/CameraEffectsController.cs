using System.Collections.Generic;
using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Mixes every ICameraEffect into one non-accumulating position and rotation offset.
    /// Mouse look should rotate the camera or its pitch pivot; this controller exclusively
    /// owns the separate Effects Pivot placed immediately above the Camera.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CameraEffectsController : MonoBehaviour
    {
        [Header("Hierarchy")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Transform effectsPivot;
        [SerializeField] private bool automaticallyCreatePivot = true;

        [Header("Mixer")]
        [SerializeField, Min(0f)] private float positionSpringFrequency = 11f;
        [SerializeField, Range(0f, 1f)] private float positionDamping = 0.82f;
        [SerializeField, Min(0f)] private float rotationSpringFrequency = 13f;
        [SerializeField, Range(0f, 1f)] private float rotationDamping = 0.86f;
        [SerializeField] private Vector3 maximumPositionOffset = new(0.2f, 0.2f, 0.2f);
        [SerializeField] private Vector3 maximumRotationOffset = new(15f, 15f, 18f);
        [SerializeField] private bool useUnscaledTime;

        private readonly List<MonoBehaviour> effectBehaviours = new();
        private Vector3 neutralLocalPosition;
        private Quaternion neutralLocalRotation = Quaternion.identity;
        private Vector3 currentPositionOffset;
        private Vector3 positionVelocity;
        private Vector3 currentRotationOffset;
        private Vector3 rotationVelocity;

        public Transform EffectsPivot => effectsPivot;
        public Transform CameraTransform => cameraTransform;
        public Vector3 CurrentPositionOffset => currentPositionOffset;
        public Vector3 CurrentRotationOffset => currentRotationOffset;

        private void Reset()
        {
            Camera camera = GetComponentInChildren<Camera>(true);
            cameraTransform = camera != null ? camera.transform : null;
        }

        private void Awake()
        {
            ResolveCamera();
            if (effectsPivot == null && automaticallyCreatePivot)
                EnsureEffectsPivot();

            CaptureNeutralPose();
            RefreshEffects();
        }

        private void OnEnable()
        {
            RefreshEffects();
        }

        private void OnDisable()
        {
            ResetImmediately();
        }

        private void LateUpdate()
        {
            if (effectsPivot == null)
                return;

            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            CameraEffectFrame combined = CameraEffectFrame.None;

            for (int i = effectBehaviours.Count - 1; i >= 0; i--)
            {
                MonoBehaviour behaviour = effectBehaviours[i];
                if (behaviour == null)
                {
                    effectBehaviours.RemoveAt(i);
                    continue;
                }

                if (!behaviour.isActiveAndEnabled || behaviour is not ICameraEffect effect || !effect.CameraEffectEnabled)
                    continue;

                combined += effect.EvaluateCameraEffect(deltaTime);
            }

            Vector3 targetPosition = ClampPerAxis(combined.Position, maximumPositionOffset);
            Vector3 targetRotation = ClampPerAxis(combined.RotationEuler, maximumRotationOffset);

            StableSpring(ref currentPositionOffset, ref positionVelocity, targetPosition, positionSpringFrequency, positionDamping, deltaTime);
            StableSpring(ref currentRotationOffset, ref rotationVelocity, targetRotation, rotationSpringFrequency, rotationDamping, deltaTime);

            // A frame hitch must never allow the spring to overshoot outside the configured limits.
            currentPositionOffset = ClampPerAxis(currentPositionOffset, maximumPositionOffset);
            currentRotationOffset = ClampPerAxis(currentRotationOffset, maximumRotationOffset);

            Vector3 finalPosition = neutralLocalPosition + currentPositionOffset;
            Quaternion finalRotation = neutralLocalRotation * Quaternion.Euler(currentRotationOffset);

            // Invalid camera transforms can cause Unity's SendMouseEvents system to report
            // "Screen position out of view frustum" repeatedly. Recover immediately instead.
            if (!IsFinite(finalPosition) || !IsFinite(finalRotation))
            {
                ResetImmediately();
                return;
            }

            // Always rebuild from the captured neutral pose. Nothing can accumulate or remain stuck.
            effectsPivot.localPosition = finalPosition;
            effectsPivot.localRotation = finalRotation;
        }

        public void RefreshEffects()
        {
            effectBehaviours.Clear();
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour != this && behaviour is ICameraEffect)
                    effectBehaviours.Add(behaviour);
            }
        }

        public void Configure(Transform camera, Transform pivot)
        {
            cameraTransform = camera;
            effectsPivot = pivot;
            CaptureNeutralPose();
            RefreshEffects();
        }

        public Transform EnsureEffectsPivot()
        {
            ResolveCamera();
            if (cameraTransform == null)
                return null;

            if (effectsPivot != null)
                return effectsPivot;

            Transform parent = cameraTransform.parent;
            GameObject pivotObject = new("Camera Effects Pivot");
            effectsPivot = pivotObject.transform;
            effectsPivot.SetParent(parent, false);
            effectsPivot.localPosition = cameraTransform.localPosition;
            effectsPivot.localRotation = Quaternion.identity;
            effectsPivot.localScale = Vector3.one;

            Quaternion cameraRotation = cameraTransform.localRotation;
            Vector3 cameraScale = cameraTransform.localScale;
            cameraTransform.SetParent(effectsPivot, false);
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = cameraRotation;
            cameraTransform.localScale = cameraScale;

            CaptureNeutralPose();
            return effectsPivot;
        }

        public void CaptureNeutralPose()
        {
            if (effectsPivot == null)
                return;

            neutralLocalPosition = effectsPivot.localPosition - currentPositionOffset;
            neutralLocalRotation = effectsPivot.localRotation * Quaternion.Inverse(Quaternion.Euler(currentRotationOffset));
        }

        public void ResetImmediately()
        {
            currentPositionOffset = Vector3.zero;
            positionVelocity = Vector3.zero;
            currentRotationOffset = Vector3.zero;
            rotationVelocity = Vector3.zero;

            if (effectsPivot != null)
            {
                effectsPivot.localPosition = neutralLocalPosition;
                effectsPivot.localRotation = neutralLocalRotation;
            }
        }

        private void ResolveCamera()
        {
            if (cameraTransform != null)
                return;

            Camera camera = GetComponentInChildren<Camera>(true);
            if (camera != null)
                cameraTransform = camera.transform;
        }

        private static Vector3 ClampPerAxis(Vector3 value, Vector3 maximum)
        {
            return new Vector3(
                Mathf.Clamp(value.x, -Mathf.Abs(maximum.x), Mathf.Abs(maximum.x)),
                Mathf.Clamp(value.y, -Mathf.Abs(maximum.y), Mathf.Abs(maximum.y)),
                Mathf.Clamp(value.z, -Mathf.Abs(maximum.z), Mathf.Abs(maximum.z)));
        }

        private static void StableSpring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float frequency, float damping, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            if (frequency <= 0f)
            {
                value = target;
                velocity = Vector3.zero;
                return;
            }

            // Explicit spring integration becomes unstable during editor stalls or frame hitches.
            // Sub-stepping keeps the response consistent and prevents extreme finite values.
            float remaining = Mathf.Min(deltaTime, 0.1f);
            const float maximumStep = 1f / 120f;
            float angularFrequency = frequency * Mathf.PI * 2f;
            float dampingCoefficient = 2f * Mathf.Clamp01(damping) * angularFrequency;

            while (remaining > 0f)
            {
                float step = Mathf.Min(maximumStep, remaining);
                Vector3 acceleration =
                    (target - value) * (angularFrequency * angularFrequency)
                    - velocity * dampingCoefficient;

                velocity += acceleration * step;
                value += velocity * step;
                remaining -= step;
            }

            if (!IsFinite(value) || !IsFinite(velocity))
            {
                value = target;
                velocity = Vector3.zero;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y)
                && float.IsFinite(value.z) && float.IsFinite(value.w);
        }
    }
}
