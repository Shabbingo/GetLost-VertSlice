using UnityEngine;

namespace Tom.WalkingController
{
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class WalkingMotor : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CharacterController characterController;
        [SerializeField] private WalkingInputReader input;
        [SerializeField] private GroundProbe groundProbe;
        [SerializeField] private TerrainSurfaceResolver surfaceResolver;
        [SerializeField] private ExertionController exertion;
        [SerializeField] private VegetationInteractionReceiver vegetation;
        [SerializeField] private Transform cameraTransform;

        [Header("Base Speeds")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 4.2f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 6.4f;
        [SerializeField, Min(0.1f)] private float carefulWalkSpeed = 2.1f;
        [SerializeField, Min(0.1f)] private float acceleration = 18f;
        [SerializeField, Min(0.1f)] private float deceleration = 22f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.25f;

        [Header("Slope Behaviour")]
        [SerializeField, Range(0f, 1f)] private float maximumUphillSlowdown = 0.48f;
        [SerializeField, Range(0f, 0.5f)] private float maximumDownhillBoost = 0.16f;
        [SerializeField, Range(0f, 89f)] private float fullSlopeEffectAngle = 38f;

        [Header("Uncontrolled Sliding")]
        [Tooltip("Default angle at which an uncontrolled slide begins. A surface profile can override this.")]
        [SerializeField, Range(0f, 89f)] private float slideStartAngle = 42f;
        [SerializeField, Min(0f)] private float slideAcceleration = 12f;
        [SerializeField, Min(0f)] private float maximumSlideSpeed = 9f;
        [Tooltip("How much directional control remains during a slide. 0 is completely uncontrolled.")]
        [SerializeField, Range(0f, 1f)] private float slideControl = 0.12f;
        [Tooltip("How quickly slide speed disappears after reaching safe ground.")]
        [SerializeField, Min(0f)] private float slideRecovery = 10f;
        [SerializeField, Range(0f, 1f)] private float carefulWalkSlideReduction = 0.65f;
        [Tooltip("Makes sprinting more likely to start a slide by lowering the effective slide angle, and increases slide acceleration while sprint is held.")]
        [SerializeField, Range(0f, 1f)] private float sprintSlideIncrease = 0.5f;

        [Header("Hidden Footing")]
        [Tooltip("Enables a hidden grip meter. Sprinting, steep downhill movement, sharp turns and low-traction surfaces reduce footing until the player slips.")]
        [SerializeField] private bool useHiddenFooting = true;
        [Tooltip("Lowest slope angle on which depleted footing can cause a slip. Steeper slopes drain footing faster.")]
        [SerializeField, Range(0f, 60f)] private float minimumFootingSlipAngle = 14f;
        [Tooltip("Footing begins draining as the slope rises above this angle.")]
        [SerializeField, Range(0f, 60f)] private float footingRiskStartAngle = 8f;
        [Tooltip("Base footing lost per second under maximum risk before surface and movement multipliers.")]
        [SerializeField, Min(0f)] private float footingLossRate = 0.38f;
        [Tooltip("Footing recovered per second while moving safely or standing still.")]
        [SerializeField, Min(0f)] private float footingRecoveryRate = 0.55f;
        [Tooltip("Additional footing loss while sprinting. 0 adds none; 1 doubles the sprint contribution.")]
        [SerializeField, Range(0f, 2f)] private float sprintFootingLoss = 1f;
        [Tooltip("Additional footing loss while travelling downhill.")]
        [SerializeField, Range(0f, 2f)] private float downhillFootingLoss = 0.85f;
        [Tooltip("Additional footing loss from sudden direction changes while moving quickly.")]
        [SerializeField, Range(0f, 2f)] private float turningFootingLoss = 0.65f;
        [Tooltip("Multiplier applied to recovery while careful walking.")]
        [SerializeField, Min(1f)] private float carefulFootingRecoveryMultiplier = 2.25f;
        [Tooltip("A slip begins once footing falls to or below this value and the slope is steep enough.")]
        [SerializeField, Range(0f, 1f)] private float slipFootingThreshold = 0.08f;

        [Header("Vertical Movement")]
        [SerializeField] private float gravity = -24f;
        [SerializeField] private float groundedVerticalSpeed = -2f;
        [SerializeField, Min(0f)] private float jumpHeight = 1.1f;

        private Vector3 planarVelocity;
        private Vector3 slideVelocity;
        private Vector3 externalVelocity;
        private Vector3 previousDesiredDirection;
        private float verticalVelocity;
        private float currentFooting = 1f;
        private TerrainSurfaceProfile currentSurface;
        private float previousFooting = 1f;

        public Vector3 Velocity => planarVelocity + slideVelocity + externalVelocity + Vector3.up * verticalVelocity;
        public float CurrentSpeed => new Vector3(Velocity.x, 0f, Velocity.z).magnitude;
        public TerrainSurfaceProfile CurrentSurface => currentSurface;
        public float UphillAmount { get; private set; }
        public bool IsCarefulWalking { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsSliding { get; private set; }
        public bool IsGrounded { get; private set; }
        public float CurrentSlopeAngle { get; private set; }
        public float CurrentFooting => currentFooting;
        public float FootingDanger => 1f - currentFooting;
        public float FootingLossPerSecond { get; private set; }
        public Vector3 CurrentDownhillDirection { get; private set; }

        // The motor remains the only owner of CharacterController.Move.
        public System.Func<Vector3, Vector3> DisplacementFilter { get; set; }
        public float ExternalSpeedMultiplier { get; set; } = 1f;
        /// <summary>
        /// Optional debug/cheat multiplier applied after normal surface, exertion and
        /// wagon modifiers. Kept separate so cheats do not overwrite those systems.
        /// </summary>
        public float CheatSpeedMultiplier { get; set; } = 1f;
        /// <summary>Debug override that keeps footing full and prevents uncontrolled sliding.</summary>
        public bool CheatSuperStableLegs { get; set; }
        public bool SuppressJump { get; set; }

        private void Reset()
        {
            characterController = GetComponent<CharacterController>();
            input = GetComponent<WalkingInputReader>();
            groundProbe = GetComponent<GroundProbe>();
            surfaceResolver = GetComponent<TerrainSurfaceResolver>();
            exertion = GetComponent<ExertionController>();
            vegetation = GetComponent<VegetationInteractionReceiver>();
            cameraTransform = Camera.main != null ? Camera.main.transform : null;
        }

        private void Awake()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();
            if (vegetation == null)
                vegetation = GetComponent<VegetationInteractionReceiver>();

            currentFooting = 1f;
            previousFooting = currentFooting;
        }

        private void Update()
        {
            if (input == null || characterController == null)
                return;

            float dt = Time.deltaTime;
            bool grounded = groundProbe != null ? groundProbe.Probe() : characterController.isGrounded;
            IsGrounded = grounded;
            Vector3 groundNormal = grounded && groundProbe != null ? groundProbe.GroundNormal : Vector3.up;

            if (grounded && surfaceResolver != null && groundProbe != null)
                currentSurface = surfaceResolver.Resolve(groundProbe.Hit);
            else if (currentSurface == null && surfaceResolver != null)
                currentSurface = surfaceResolver.DefaultProfile;

            float speedMultiplier = currentSurface != null ? currentSurface.speedMultiplier : 1f;
            float accelerationMultiplier = currentSurface != null ? currentSurface.accelerationMultiplier : 1f;
            float traction = currentSurface != null ? currentSurface.traction : 1f;
            float exertionMultiplier = currentSurface != null ? currentSurface.exertionMultiplier : 1f;
            float footingLossMultiplier = currentSurface != null ? currentSurface.footingLossMultiplier : 1f;
            float vegetationSpeedMultiplier = vegetation != null ? vegetation.SpeedMultiplier : 1f;
            float vegetationExertionMultiplier = vegetation != null ? vegetation.ExertionMultiplier : 1f;
            float slideMultiplier = currentSurface != null ? currentSurface.downhillSlideMultiplier : 1f;
            float effectiveSlideAngle = currentSurface != null && currentSurface.overrideSlideStartAngle
                ? currentSurface.slideStartAngle
                : slideStartAngle;

            Vector2 moveInput = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 desiredDirection = GetCameraRelativeDirection(moveInput);

            if (grounded && desiredDirection.sqrMagnitude > 0.001f)
                desiredDirection = Vector3.ProjectOnPlane(desiredDirection, groundNormal).normalized;

            CurrentSlopeAngle = grounded ? Vector3.Angle(groundNormal, Vector3.up) : 0f;
            Vector3 downhillDirection = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
            CurrentDownhillDirection = grounded ? downhillDirection : Vector3.zero;
            Vector3 uphillDirection = -downhillDirection;

            float directionAlongSlope = desiredDirection.sqrMagnitude > 0.001f
                ? Vector3.Dot(desiredDirection, uphillDirection)
                : 0f;

            float slopeStrength = fullSlopeEffectAngle > 0f
                ? Mathf.Clamp01(CurrentSlopeAngle / fullSlopeEffectAngle)
                : 0f;

            UphillAmount = Mathf.Max(0f, directionAlongSlope) * slopeStrength;
            float downhillAmount = Mathf.Max(0f, -directionAlongSlope) * slopeStrength;

            IsCarefulWalking = input.CarefulWalkHeld;
            bool sprintingIntoSlide = input.SprintHeld && !IsCarefulWalking && moveInput.y > 0.1f;
            if (CheatSuperStableLegs)
            {
                currentFooting = 1f;
                previousFooting = 1f;
                FootingLossPerSecond = 0f;
                IsSliding = false;
                slideVelocity = Vector3.zero;
                previousDesiredDirection = desiredDirection;
            }
            else
            {
                UpdateFooting(
                    grounded,
                    desiredDirection,
                    moveInput.magnitude,
                    CurrentSlopeAngle,
                    downhillAmount,
                    traction,
                    footingLossMultiplier,
                    sprintingIntoSlide,
                    dt);

                bool footingTriggeredSlip = useHiddenFooting
                    && currentFooting <= slipFootingThreshold
                    && CurrentSlopeAngle >= minimumFootingSlipAngle;

                FootingLossPerSecond = dt > 0f
                    ? Mathf.Max(0f, previousFooting - currentFooting) / dt
                    : 0f;
                previousFooting = currentFooting;

                UpdateSliding(
                    grounded,
                    CurrentSlopeAngle,
                    effectiveSlideAngle,
                    downhillDirection,
                    slideMultiplier,
                    traction,
                    sprintingIntoSlide,
                    footingTriggeredSlip,
                    dt);
            }
            IsSprinting = sprintingIntoSlide && !IsSliding;

            float baseSpeed = IsCarefulWalking ? carefulWalkSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            float slopeSpeedMultiplier = 1f - UphillAmount * maximumUphillSlowdown + downhillAmount * maximumDownhillBoost;
            float exertionSpeedMultiplier = exertion != null ? exertion.SpeedMultiplier : 1f;
            float targetSpeed = baseSpeed * speedMultiplier * vegetationSpeedMultiplier *
                slopeSpeedMultiplier * exertionSpeedMultiplier * moveInput.magnitude *
                Mathf.Clamp01(ExternalSpeedMultiplier) * Mathf.Max(0f, CheatSpeedMultiplier);

            Vector3 targetPlanarVelocity = desiredDirection * targetSpeed;
            float control = grounded ? 1f : airControl;
            if (IsSliding)
                control *= slideControl;

            float rate = targetPlanarVelocity.sqrMagnitude > planarVelocity.sqrMagnitude ? acceleration : deceleration;
            rate *= accelerationMultiplier * Mathf.Max(0.05f, traction) * control;
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetPlanarVelocity, rate * dt);

            UpdateVerticalVelocity(grounded, dt);

            if (!SuppressJump && grounded && !IsSliding && input.JumpPressedThisFrame && jumpHeight > 0f)
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            Vector3 totalVelocity = planarVelocity + slideVelocity + externalVelocity + Vector3.up * verticalVelocity;
            Vector3 displacement = totalVelocity * dt;
            if (DisplacementFilter != null)
                displacement = DisplacementFilter(displacement);
            characterController.Move(displacement);

            externalVelocity = Vector3.MoveTowards(
                externalVelocity,
                Vector3.zero,
                deceleration * Mathf.Max(0.05f, traction) * dt);

            if (exertion != null)
            {
                bool moving = (moveInput.sqrMagnitude > 0.01f || IsSliding) && grounded;
                exertion.Tick(moving, IsSprinting, UphillAmount, exertionMultiplier * vegetationExertionMultiplier, dt);
            }
        }
        public void ResetMotion()
        {
            planarVelocity = Vector3.zero;
            slideVelocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            verticalVelocity = 0f;

            currentFooting = 1f;
            previousFooting = 1f;
            previousDesiredDirection = Vector3.zero;

            IsSliding = false;
            IsSprinting = false;
            IsCarefulWalking = false;
        }
        public void AddImpulse(Vector3 impulse)
        {
            externalVelocity += impulse;
        }

        private Vector3 GetCameraRelativeDirection(Vector2 moveInput)
        {
            Transform reference = cameraTransform != null ? cameraTransform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
            return (forward * moveInput.y + right * moveInput.x).normalized;
        }

        private void UpdateFooting(
            bool grounded,
            Vector3 desiredDirection,
            float inputMagnitude,
            float slopeAngle,
            float downhillAmount,
            float traction,
            float surfaceFootingLossMultiplier,
            bool sprinting,
            float dt)
        {
            if (!useHiddenFooting)
            {
                currentFooting = 1f;
                previousDesiredDirection = desiredDirection;
                return;
            }

            if (!grounded)
            {
                previousDesiredDirection = desiredDirection;
                return;
            }

            bool moving = inputMagnitude > 0.05f && desiredDirection.sqrMagnitude > 0.001f;
            float riskRange = Mathf.Max(1f, slideStartAngle - footingRiskStartAngle);
            float slopeRisk = Mathf.InverseLerp(footingRiskStartAngle, footingRiskStartAngle + riskRange, slopeAngle);

            float turnRisk = 0f;
            if (moving && previousDesiredDirection.sqrMagnitude > 0.001f)
            {
                float directionChange = Vector3.Angle(previousDesiredDirection, desiredDirection) / 180f;
                float speedFactor = sprinting ? 1f : Mathf.InverseLerp(0f, walkSpeed, CurrentSpeed);
                turnRisk = directionChange * speedFactor;
            }

            float tractionRisk = 1f / Mathf.Max(0.1f, traction);
            float sprintRisk = sprinting ? 1f + sprintFootingLoss : 1f;
            float downhillRisk = 1f + downhillAmount * downhillFootingLoss;
            float turningRisk = 1f + turnRisk * turningFootingLoss;
            float movementRisk = moving ? 1f : 0f;

            float totalRisk = slopeRisk
                * movementRisk
                * sprintRisk
                * downhillRisk
                * turningRisk
                * tractionRisk
                * Mathf.Max(0f, surfaceFootingLossMultiplier);

            if (IsSliding)
                totalRisk = Mathf.Max(totalRisk, 1f);

            if (totalRisk > 0.001f)
            {
                currentFooting = Mathf.MoveTowards(currentFooting, 0f, footingLossRate * totalRisk * dt);
            }
            else
            {
                float recoveryMultiplier = IsCarefulWalking ? carefulFootingRecoveryMultiplier : 1f;
                currentFooting = Mathf.MoveTowards(currentFooting, 1f, footingRecoveryRate * recoveryMultiplier * dt);
            }

            previousDesiredDirection = desiredDirection;
        }

        private void UpdateSliding(
            bool grounded,
            float slopeAngle,
            float effectiveSlideStartAngle,
            Vector3 downhillDirection,
            float surfaceSlideMultiplier,
            float traction,
            bool sprintingIntoSlide,
            bool footingTriggeredSlip,
            float dt)
        {
            float sprintAngleMultiplier = sprintingIntoSlide
                ? Mathf.Lerp(1f, 0.65f, sprintSlideIncrease)
                : 1f;
            float adjustedSlideStartAngle = effectiveSlideStartAngle * sprintAngleMultiplier;

            bool hardSlopeSlide = slopeAngle >= adjustedSlideStartAngle;
            bool continueFootingSlide = IsSliding && slopeAngle >= minimumFootingSlipAngle && currentFooting < 0.35f;
            bool canSlide = grounded
                && (hardSlopeSlide || footingTriggeredSlip || continueFootingSlide)
                && downhillDirection.sqrMagnitude > 0.001f
                && surfaceSlideMultiplier > 0f;

            IsSliding = canSlide;

            if (!canSlide)
            {
                slideVelocity = Vector3.MoveTowards(slideVelocity, Vector3.zero, slideRecovery * dt);
                return;
            }

            currentFooting = Mathf.MoveTowards(currentFooting, 0f, footingLossRate * 2f * dt);

            float slideReferenceAngle = hardSlopeSlide ? adjustedSlideStartAngle : minimumFootingSlipAngle;
            float upperAngle = Mathf.Max(slideReferenceAngle + 1f, characterController.slopeLimit + 15f);
            float steepness = Mathf.InverseLerp(slideReferenceAngle, upperAngle, slopeAngle);
            float carefulMultiplier = IsCarefulWalking ? 1f - carefulWalkSlideReduction : 1f;
            float sprintMultiplier = sprintingIntoSlide ? 1f + sprintSlideIncrease : 1f;
            float tractionResistance = 1f / Mathf.Max(0.1f, traction);

            float accelerationThisFrame = slideAcceleration
                * Mathf.Lerp(0.25f, 1f, steepness)
                * surfaceSlideMultiplier
                * carefulMultiplier
                * sprintMultiplier
                * tractionResistance;

            slideVelocity += downhillDirection * accelerationThisFrame * dt;
            slideVelocity = Vector3.ProjectOnPlane(slideVelocity, groundProbe != null ? groundProbe.GroundNormal : Vector3.up);
            slideVelocity = Vector3.ClampMagnitude(slideVelocity, maximumSlideSpeed);
        }

        private void UpdateVerticalVelocity(bool grounded, float dt)
        {
            if (grounded && verticalVelocity < 0f)
                verticalVelocity = groundedVerticalSpeed;
            else
                verticalVelocity += gravity * dt;
        }

        private void OnValidate()
        {
            if (footingRiskStartAngle > minimumFootingSlipAngle)
                footingRiskStartAngle = minimumFootingSlipAngle;
        }
    }
}
