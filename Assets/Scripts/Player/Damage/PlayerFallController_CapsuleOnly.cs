using System;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GetLost.Player
{
    /// <summary>
    /// Capsule-only loss-of-control controller.
    /// Normal movement uses CharacterController/WalkingMotor.
    /// During a tumble, Rigidbody + CapsuleCollider take over.
    ///
    /// Damage is applied PER IMPACT while uncontrolled using Collision.relativeVelocity.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PlayerFallController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WalkingMotor walkingMotor;
        [SerializeField] private CharacterController characterController;
        [SerializeField] private Rigidbody tumbleBody;
        [SerializeField] private CapsuleCollider tumbleCollider;
        [SerializeField] private PlayerDamageController damageController;

        [SerializeField] private Transform playerCamera;
        [SerializeField] private Behaviour lookController;
        [SerializeField] private Behaviour[] disableWhileUncontrolled = Array.Empty<Behaviour>();

        [Header("Loss Of Control")]
        [SerializeField, Min(0.1f)] private float loseControlSpeed = 7.5f;
        [SerializeField, Min(0f)] private float loseControlDelay = 0.08f;
        [SerializeField] private bool requireSliding = true;
        [SerializeField, Range(0.02f, 0.2f)] private float walkingCheckInterval = 0.05f;

        [Header("Hard Landings")]
        [Tooltip("Allows a hard landing to immediately put the player back into the uncontrolled tumble state.")]
        [SerializeField] private bool tumbleFromHardLandings = true;

        [Tooltip("Minimum downward speed before landing on a steep surface can cause loss of control.")]
        [SerializeField, Min(0f)] private float hardLandingDownwardSpeed = 5.5f;

        [Tooltip("A landing on a surface at or above this slope angle causes a tumble when falling fast enough.")]
        [SerializeField, Range(0f, 89f)] private float hardLandingSlopeAngle = 30f;

        [Tooltip("If falling faster than this, tumble even when landing on a relatively gentle surface.")]
        [SerializeField, Min(0f)] private float severeLandingDownwardSpeed = 9f;

        [Tooltip("Prevents a hard-landing tumble from retriggering immediately after recovery.")]
        [SerializeField, Min(0f)] private float hardLandingGraceAfterRecovery = 0.2f;

        [Header("Capsule Tumble")]
        [SerializeField, Min(0.1f)] private float tumbleMass = 75f;
        [SerializeField, Min(0f)] private float initialTumbleAngularSpeed = 7f;
        [SerializeField, Min(0f)] private float randomTwist = 1.5f;
        [SerializeField, Min(1f)] private float maximumAngularSpeed = 20f;
        [SerializeField, Min(0f)] private float tumbleLinearDamping = 0.08f;
        [SerializeField, Min(0f)] private float tumbleAngularDamping = 0.35f;
        [SerializeField, Range(0f, 1f)] private float tumbleFriction = 0.35f;
        [SerializeField, Range(0f, 1f)] private float tumbleBounciness = 0.03f;

        [Header("Impact Damage")]
        [Tooltip("No impact damage is dealt below this relative collision speed.")]
        [SerializeField, Min(0f)] private float minimumDamageImpactSpeed = 4.5f;

        [Tooltip("Damage dealt per m/s above Minimum Damage Impact Speed.")]
        [SerializeField, Min(0f)] private float damagePerImpactSpeed = 4f;

        [Tooltip("Maximum damage one individual collision can deal.")]
        [SerializeField, Min(0f)] private float maximumDamagePerImpact = 30f;

        [Tooltip("Minimum time between damage events. Prevents one sustained contact from damaging every physics step.")]
        [SerializeField, Min(0f)] private float impactDamageCooldown = 0.12f;

        [Tooltip("Ignore contacts whose impact direction is mostly sideways/glancing. 0 = count everything, 1 = only perfectly head-on.")]
        [SerializeField, Range(0f, 1f)] private float minimumImpactDirectness = 0.15f;

        [Tooltip("Optional layers that are allowed to cause impact damage.")]
        [SerializeField] private LayerMask impactDamageLayers = ~0;

        [Header("Camera During Fall")]
        [SerializeField] private bool lockCameraToCapsule = true;
        [SerializeField] private Vector3 tumbleCameraLocalPosition = new Vector3(0f, 0.65f, 0f);
        [SerializeField] private bool resetCameraRotationOnFall = true;

        [Header("Fall Spectator Camera")]
        [Tooltip("Optional second camera used to spectate the uncontrolled fall.")]
        [SerializeField] private Camera spectatorCamera;

        [Tooltip("Optional AudioListener on the spectator camera. If left empty, it is auto-found on the spectator camera GameObject.")]
        [SerializeField] private AudioListener spectatorAudioListener;

        [Tooltip("If enabled, Space can toggle spectator mode while the player is alive and uncontrolled.")]
        [SerializeField] private bool allowAliveSpectatorToggle = true;

        [Tooltip("Distance of the orbit camera from the player.")]
        [SerializeField, Min(0.5f)] private float spectatorDistance = 7f;

        [Tooltip("Initial orbit height angle when spectator mode starts.")]
        [SerializeField, Range(-80f, 80f)] private float spectatorStartPitch = 20f;

        [Tooltip("Mouse sensitivity for orbiting around the falling player.")]
        [SerializeField, Min(0.01f)] private float spectatorMouseSensitivity = 0.18f;

        [Tooltip("Vertical orbit limits.")]
        [SerializeField] private Vector2 spectatorPitchLimits = new Vector2(-20f, 70f);

        [Tooltip("How quickly the spectator camera follows the tumbling player.")]
        [SerializeField, Min(0f)] private float spectatorFollowSpeed = 12f;

        [Tooltip("Vertical point on the capsule that the spectator camera looks toward.")]
        [SerializeField] private float spectatorLookHeight = 0.4f;

        [Header("Recovery")]
        [SerializeField, Min(0f)] private float minimumUncontrolledTime = 0.8f;
        [SerializeField, Min(0f)] private float recoverySpeed = 1f;
        [SerializeField, Min(0f)] private float recoveryAngularSpeed = 1.25f;
        [SerializeField, Min(0f)] private float recoveryHoldTime = 0.6f;
        [SerializeField, Min(0f)] private float recoveryLift = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = true;
        [SerializeField] private bool logImpactDamage;

        private PhysicsMaterial tumbleMaterial;

        private Transform originalCameraParent;
        private Vector3 originalCameraLocalPosition;
        private Quaternion originalCameraLocalRotation;

        private bool originalLookEnabled;
        private bool[] extraBehaviourStates;
        private AudioListener playerAudioListener;
        private bool spectatorActive;
        private bool playerDead;
        private float spectatorYaw;
        private float spectatorPitch;

        private float nextWalkingCheck;
        private float overSpeedTimer;
        private float uncontrolledStartedAt;
        private float recoveryTimer;
        private float nextImpactDamageTime;

        private bool wasGrounded;
        private float fastestDownwardSpeedWhileAirborne;
        private float hardLandingCheckAllowedTime;

        public bool IsUncontrolled { get; private set; }
        public float CurrentTumbleSpeed =>
            tumbleBody != null ? tumbleBody.linearVelocity.magnitude : 0f;

        public event Action LostControl;
        public event Action RecoveredControl;
        public event Action<float> ImpactDamageDealt;

        public void ConfigureForPlayer(Transform cameraTransform, Behaviour playerLookController,
            params Behaviour[] additionalBehaviours)
        {
            playerCamera = cameraTransform;
            lookController = playerLookController;
            disableWhileUncontrolled = additionalBehaviours ?? Array.Empty<Behaviour>();
            extraBehaviourStates = new bool[disableWhileUncontrolled.Length];
        }

        public void ConfigureSpectatorCamera(Camera camera, AudioListener listener)
        {
            spectatorCamera = camera;
            spectatorAudioListener = listener;
            if (spectatorCamera != null)
                spectatorCamera.enabled = false;
            if (spectatorAudioListener != null)
                spectatorAudioListener.enabled = false;
        }

        private void Reset()
        {
            EnsureRequiredComponents();

            if (Camera.main != null)
                playerCamera = Camera.main.transform;

            SyncPhysicsCapsuleToCharacterController();
            ConfigureDormantPhysics();
        }

        private void Awake()
        {
            EnsureRequiredComponents();

            if (playerCamera == null && Camera.main != null)
                playerCamera = Camera.main.transform;

            SetupTumblePhysics();
            SetTumblePhysicsEnabled(false);

            wasGrounded = characterController != null && characterController.isGrounded;
            fastestDownwardSpeedWhileAirborne = 0f;

            if (playerCamera != null)
            {
                Camera firstPersonCamera = playerCamera.GetComponent<Camera>();

                if (firstPersonCamera != null)
                    firstPersonCamera.enabled = true;

                playerAudioListener = playerCamera.GetComponent<AudioListener>();

                if (playerAudioListener != null)
                    playerAudioListener.enabled = true;
            }

            if (spectatorCamera != null)
            {
                if (spectatorAudioListener == null)
                    spectatorAudioListener = spectatorCamera.GetComponent<AudioListener>();

                spectatorCamera.enabled = false;

                if (spectatorAudioListener != null)
                    spectatorAudioListener.enabled = false;
            }

            spectatorActive = false;
            playerDead = false;

            if (damageController != null)
                damageController.Died += HandlePlayerDied;

            extraBehaviourStates = new bool[
                disableWhileUncontrolled != null
                    ? disableWhileUncontrolled.Length
                    : 0
            ];
        }

        private void EnsureRequiredComponents()
        {
            if (characterController == null)
                characterController = GetComponent<CharacterController>();

            if (tumbleBody == null)
                tumbleBody = GetComponent<Rigidbody>();

            if (tumbleCollider == null)
                tumbleCollider = GetComponent<CapsuleCollider>();

            if (walkingMotor == null)
                walkingMotor = GetComponent<WalkingMotor>();

            if (damageController == null)
                damageController = GetComponent<PlayerDamageController>();
        }

        private void SetupTumblePhysics()
        {
            SyncPhysicsCapsuleToCharacterController();

            tumbleBody.mass = tumbleMass;
            tumbleBody.useGravity = false;
            tumbleBody.isKinematic = true;
            tumbleBody.detectCollisions = false;
            tumbleBody.interpolation = RigidbodyInterpolation.None;
            tumbleBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            tumbleBody.linearDamping = tumbleLinearDamping;
            tumbleBody.angularDamping = tumbleAngularDamping;
            tumbleBody.maxAngularVelocity = maximumAngularSpeed;
            tumbleBody.constraints = RigidbodyConstraints.None;

            tumbleMaterial = new PhysicsMaterial("Player Tumble Material")
            {
                dynamicFriction = tumbleFriction,
                staticFriction = tumbleFriction,
                bounciness = tumbleBounciness,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Maximum
            };

            tumbleCollider.material = tumbleMaterial;
        }

        private void ConfigureDormantPhysics()
        {
            if (tumbleBody != null)
            {
                tumbleBody.useGravity = false;
                tumbleBody.isKinematic = true;
                tumbleBody.detectCollisions = false;
                tumbleBody.interpolation = RigidbodyInterpolation.None;
            }

            if (tumbleCollider != null)
                tumbleCollider.enabled = false;
        }

        private void SyncPhysicsCapsuleToCharacterController()
        {
            if (characterController == null || tumbleCollider == null)
                return;

            tumbleCollider.center = characterController.center;
            tumbleCollider.radius = characterController.radius;
            tumbleCollider.height = characterController.height;
            tumbleCollider.direction = 1;
            tumbleCollider.isTrigger = false;
        }

        private void Update()
        {
            if (IsUncontrolled)
            {
                UpdateSpectatorInput();
                UpdateRecovery();
                return;
            }

            UpdateHardLandingDetection();

            // Hard-landing detection may have started a tumble.
            if (IsUncontrolled)
                return;

            if (Time.unscaledTime < nextWalkingCheck)
                return;

            float interval = Mathf.Max(0.02f, walkingCheckInterval);
            nextWalkingCheck = Time.unscaledTime + interval;

            CheckForLossOfControl(interval);
        }

        private void LateUpdate()
        {
            if (!IsUncontrolled ||
                !spectatorActive ||
                spectatorCamera == null)
            {
                return;
            }

            UpdateSpectatorCamera();
        }

        private void UpdateSpectatorInput()
        {
            if (spectatorCamera == null ||
                Keyboard.current == null)
            {
                return;
            }

            // Once dead, spectator mode is forced on and Space cannot disable it.
            if (playerDead)
            {
                if (!spectatorActive)
                    SetSpectatorCameraActive(true);

                return;
            }

            if (!allowAliveSpectatorToggle)
                return;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                SetSpectatorCameraActive(!spectatorActive);
        }

        private void HandlePlayerDied()
        {
            playerDead = true;

            if (!IsUncontrolled)
            {
                Vector3 deathVelocity =
                    walkingMotor != null
                        ? walkingMotor.Velocity
                        : Vector3.zero;

                BeginTumble(deathVelocity);
            }

            SetSpectatorCameraActive(true);
        }

        private void SetSpectatorCameraActive(bool active)
        {
            spectatorActive = active;

            if (spectatorCamera != null)
            {
                spectatorCamera.enabled = active;

                if (spectatorAudioListener == null)
                    spectatorAudioListener = spectatorCamera.GetComponent<AudioListener>();

                if (spectatorAudioListener != null)
                    spectatorAudioListener.enabled = active;
            }

            if (playerCamera != null)
            {
                Camera firstPersonCamera =
                    playerCamera.GetComponent<Camera>();

                if (firstPersonCamera != null)
                    firstPersonCamera.enabled = !active;

                if (playerAudioListener == null)
                    playerAudioListener = playerCamera.GetComponent<AudioListener>();

                if (playerAudioListener != null)
                    playerAudioListener.enabled = !active;
            }

            if (active)
            {
                Vector3 flatForward =
                    Vector3.ProjectOnPlane(
                        transform.forward,
                        Vector3.up);

                if (flatForward.sqrMagnitude < 0.001f)
                    flatForward = Vector3.forward;

                spectatorYaw =
                    Quaternion.LookRotation(
                        flatForward.normalized,
                        Vector3.up).eulerAngles.y;

                spectatorPitch = spectatorStartPitch;

                SnapSpectatorCamera();
            }

            if (logStateChanges)
            {
                Debug.Log(
                    $"[PlayerFallController] Spectator camera {(active ? "ENABLED" : "DISABLED")}.",
                    this);
            }
        }

        private void UpdateSpectatorCamera()
        {
            if (Mouse.current != null)
            {
                Vector2 mouseDelta =
                    Mouse.current.delta.ReadValue();

                spectatorYaw +=
                    mouseDelta.x *
                    spectatorMouseSensitivity;

                spectatorPitch -=
                    mouseDelta.y *
                    spectatorMouseSensitivity;

                spectatorPitch =
                    Mathf.Clamp(
                        spectatorPitch,
                        spectatorPitchLimits.x,
                        spectatorPitchLimits.y);
            }

            Vector3 lookPoint =
                transform.position +
                Vector3.up * spectatorLookHeight;

            Quaternion orbitRotation =
                Quaternion.Euler(
                    spectatorPitch,
                    spectatorYaw,
                    0f);

            Vector3 desiredPosition =
                lookPoint +
                orbitRotation *
                (Vector3.back * spectatorDistance);

            Transform camTransform =
                spectatorCamera.transform;

            if (spectatorFollowSpeed <= 0f)
            {
                camTransform.position =
                    desiredPosition;
            }
            else
            {
                camTransform.position =
                    Vector3.Lerp(
                        camTransform.position,
                        desiredPosition,
                        1f - Mathf.Exp(
                            -spectatorFollowSpeed *
                            Time.unscaledDeltaTime));
            }

            Vector3 direction =
                lookPoint -
                camTransform.position;

            if (direction.sqrMagnitude > 0.001f)
            {
                camTransform.rotation =
                    Quaternion.LookRotation(
                        direction.normalized,
                        Vector3.up);
            }
        }

        private void SnapSpectatorCamera()
        {
            if (spectatorCamera == null)
                return;

            Vector3 lookPoint =
                transform.position +
                Vector3.up * spectatorLookHeight;

            Quaternion orbitRotation =
                Quaternion.Euler(
                    spectatorPitch,
                    spectatorYaw,
                    0f);

            spectatorCamera.transform.position =
                lookPoint +
                orbitRotation *
                (Vector3.back * spectatorDistance);

            Vector3 direction =
                lookPoint -
                spectatorCamera.transform.position;

            if (direction.sqrMagnitude > 0.001f)
            {
                spectatorCamera.transform.rotation =
                    Quaternion.LookRotation(
                        direction.normalized,
                        Vector3.up);
            }
        }

        private void UpdateHardLandingDetection()
        {
            if (!tumbleFromHardLandings ||
                characterController == null ||
                walkingMotor == null ||
                !walkingMotor.enabled ||
                Time.time < hardLandingCheckAllowedTime)
            {
                return;
            }

            // No physics casts here.
            // We only remember how fast the player has been falling.
            // The actual landing surface is supplied for free by
            // CharacterController.OnControllerColliderHit.
            if (!characterController.isGrounded)
            {
                float downwardSpeed =
                    Mathf.Max(
                        0f,
                        -walkingMotor.Velocity.y);

                if (downwardSpeed > fastestDownwardSpeedWhileAirborne)
                    fastestDownwardSpeedWhileAirborne = downwardSpeed;
            }

            wasGrounded = characterController.isGrounded;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (IsUncontrolled ||
                !tumbleFromHardLandings ||
                walkingMotor == null ||
                !walkingMotor.enabled ||
                Time.time < hardLandingCheckAllowedTime ||
                hit == null)
            {
                return;
            }

            float downwardSpeed =
                fastestDownwardSpeedWhileAirborne;

            if (downwardSpeed < hardLandingDownwardSpeed)
                return;

            // Ignore near-vertical walls as "landing" surfaces.
            if (hit.normal.y <= 0.05f)
                return;

            float slopeAngle =
                Vector3.Angle(
                    hit.normal,
                    Vector3.up);

            bool steepLanding =
                slopeAngle >= hardLandingSlopeAngle;

            bool severeLanding =
                downwardSpeed >= severeLandingDownwardSpeed;

            if (!steepLanding && !severeLanding)
                return;

            BeginHardLandingTumble(
                downwardSpeed,
                slopeAngle);
        }

        private void BeginHardLandingTumble(
            float downwardSpeed,
            float slopeAngle)
        {
            Vector3 inheritedVelocity =
                walkingMotor != null
                    ? walkingMotor.Velocity
                    : Vector3.zero;

            inheritedVelocity.y =
                -Mathf.Max(
                    downwardSpeed,
                    hardLandingDownwardSpeed);

            Vector3 horizontal =
                Vector3.ProjectOnPlane(
                    inheritedVelocity,
                    Vector3.up);

            if (horizontal.sqrMagnitude < 0.25f)
            {
                Vector3 forward =
                    Vector3.ProjectOnPlane(
                        transform.forward,
                        Vector3.up);

                if (forward.sqrMagnitude > 0.001f)
                {
                    inheritedVelocity +=
                        forward.normalized * 1.5f;
                }
            }

            if (logStateChanges)
            {
                Debug.Log(
                    $"[PlayerFallController] Hard landing: downward {downwardSpeed:F2} m/s, slope {slopeAngle:F1}°. Losing control.",
                    this);
            }

            fastestDownwardSpeedWhileAirborne = 0f;
            BeginTumble(inheritedVelocity);
        }

        private void CheckForLossOfControl(float elapsed)
        {
            if (walkingMotor == null || !walkingMotor.enabled)
            {
                overSpeedTimer = 0f;
                return;
            }

            if (requireSliding && !walkingMotor.IsSliding)
            {
                overSpeedTimer = 0f;
                return;
            }

            Vector3 velocity = walkingMotor.Velocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;

            if (horizontalSpeed < loseControlSpeed)
            {
                overSpeedTimer = 0f;
                return;
            }

            overSpeedTimer += elapsed;

            if (overSpeedTimer >= loseControlDelay)
                BeginTumble(velocity);
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryApplyImpactDamage(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            // Useful when the capsule continues slamming/bouncing against uneven terrain.
            // The cooldown prevents this from becoming damage every FixedUpdate.
            TryApplyImpactDamage(collision);
        }

        private void TryApplyImpactDamage(Collision collision)
        {
            if (!IsUncontrolled ||
                damageController == null ||
                collision == null ||
                Time.time < nextImpactDamageTime)
            {
                return;
            }

            int otherLayer = collision.gameObject.layer;

            if ((impactDamageLayers.value & (1 << otherLayer)) == 0)
                return;

            float impactSpeed = collision.relativeVelocity.magnitude;

            if (impactSpeed < minimumDamageImpactSpeed)
                return;

            // Try to reject very glancing contacts.
            float directness = 1f;

            if (collision.contactCount > 0 &&
                collision.relativeVelocity.sqrMagnitude > 0.001f)
            {
                Vector3 velocityDirection =
                    collision.relativeVelocity.normalized;

                Vector3 normal =
                    collision.GetContact(0).normal;

                directness =
                    Mathf.Abs(
                        Vector3.Dot(
                            velocityDirection,
                            normal));
            }

            if (directness < minimumImpactDirectness)
                return;

            float damage =
                Mathf.Min(
                    (impactSpeed - minimumDamageImpactSpeed) *
                    damagePerImpactSpeed,
                    maximumDamagePerImpact);

            // Scale glancing blows down rather than treating all contacts equally.
            damage *= Mathf.Lerp(0.35f, 1f, directness);

            if (damage <= 0f)
                return;

            nextImpactDamageTime =
                Time.time + impactDamageCooldown;

            damageController.TakeDamage(damage);
            ImpactDamageDealt?.Invoke(damage);

            if (logImpactDamage)
            {
                Debug.Log(
                    $"[PlayerFallController] Impact with '{collision.gameObject.name}' at {impactSpeed:F2} m/s, directness {directness:F2}: {damage:F1} damage.",
                    this);
            }
        }

        [ContextMenu("TEST - Force Tumble")]
        private void TestForceTumble()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Enter Play Mode first.", this);
                return;
            }

            BeginTumble(
                transform.forward * Mathf.Max(loseControlSpeed, 6f) +
                Vector3.up * 1.5f);
        }

        public void ForceLoseControl()
        {
            Vector3 velocity =
                walkingMotor != null
                    ? walkingMotor.Velocity
                    : transform.forward * loseControlSpeed;

            if (velocity.sqrMagnitude < 1f)
                velocity = transform.forward * loseControlSpeed;

            BeginTumble(velocity);
        }

        public void ForceLoseControl(Vector3 startingVelocity)
        {
            BeginTumble(startingVelocity);
        }

        private void BeginTumble(Vector3 inheritedVelocity)
        {
            if (IsUncontrolled)
                return;

            EnsureRequiredComponents();

            IsUncontrolled = true;
            overSpeedTimer = 0f;
            recoveryTimer = 0f;
            nextImpactDamageTime = 0f;
            uncontrolledStartedAt = Time.time;

            DisableGameplayControl();

            if (!playerDead)
                SetSpectatorCameraActive(false);

            LockCameraForFall();

            characterController.enabled = false;
            SetTumblePhysicsEnabled(true);

            tumbleBody.linearVelocity = inheritedVelocity;

            Vector3 horizontalTravel =
                Vector3.ProjectOnPlane(
                    inheritedVelocity,
                    Vector3.up);

            Vector3 travelDirection =
                horizontalTravel.sqrMagnitude > 0.01f
                    ? horizontalTravel.normalized
                    : transform.forward;

            Vector3 rollAxis =
                Vector3.Cross(
                    Vector3.up,
                    travelDirection).normalized;

            if (rollAxis.sqrMagnitude < 0.01f)
                rollAxis = transform.right;

            tumbleBody.angularVelocity =
                rollAxis * initialTumbleAngularSpeed +
                UnityEngine.Random.onUnitSphere * randomTwist;

            tumbleBody.WakeUp();

            if (logStateChanges)
            {
                Debug.Log(
                    $"[PlayerFallController] Capsule tumble started at {inheritedVelocity.magnitude:F2} m/s.",
                    this);
            }

            LostControl?.Invoke();
        }

        private void UpdateRecovery()
        {
            if (playerDead)
                return;

            float linearSpeed = tumbleBody.linearVelocity.magnitude;
            float angularSpeed = tumbleBody.angularVelocity.magnitude;

            if (Time.time - uncontrolledStartedAt < minimumUncontrolledTime)
                return;

            if (linearSpeed > recoverySpeed ||
                angularSpeed > recoveryAngularSpeed)
            {
                recoveryTimer = 0f;
                return;
            }

            recoveryTimer += Time.deltaTime;

            if (recoveryTimer >= recoveryHoldTime)
                RecoverControl();
        }

        private void RecoverControl()
        {
            Vector3 flatForward =
                Vector3.ProjectOnPlane(
                    transform.forward,
                    Vector3.up);

            if (flatForward.sqrMagnitude < 0.001f)
                flatForward = Vector3.forward;

            Vector3 recoveryPosition =
                transform.position +
                Vector3.up * recoveryLift;

            Quaternion recoveryRotation =
                Quaternion.LookRotation(
                    flatForward.normalized,
                    Vector3.up);

            SetTumblePhysicsEnabled(false);

            transform.SetPositionAndRotation(
                recoveryPosition,
                recoveryRotation);

            if (!playerDead)
                SetSpectatorCameraActive(false);

            RestoreFallCamera();

            characterController.enabled = true;

            if (walkingMotor != null)
            {
                walkingMotor.ResetMotion();
                walkingMotor.enabled = true;
            }

            RestoreGameplayControl();

            IsUncontrolled = false;
            recoveryTimer = 0f;

            wasGrounded = characterController.isGrounded;
            fastestDownwardSpeedWhileAirborne = 0f;
            hardLandingCheckAllowedTime =
                Time.time + hardLandingGraceAfterRecovery;

            if (logStateChanges)
                Debug.Log("[PlayerFallController] Control recovered.", this);

            RecoveredControl?.Invoke();
        }

        private void SetTumblePhysicsEnabled(bool enabled)
        {
            if (tumbleBody == null || tumbleCollider == null)
                return;

            if (enabled)
            {
                tumbleCollider.enabled = true;

                tumbleBody.isKinematic = false;
                tumbleBody.useGravity = true;
                tumbleBody.detectCollisions = true;

                // Physics owns the transform while tumbling, so interpolate it.
                tumbleBody.interpolation =
                    RigidbodyInterpolation.Interpolate;

                tumbleBody.WakeUp();
            }
            else
            {
                // Unity 6 does not allow velocity assignment after the body
                // has become kinematic. Clear motion first.
                if (!tumbleBody.isKinematic)
                {
                    tumbleBody.linearVelocity = Vector3.zero;
                    tumbleBody.angularVelocity = Vector3.zero;
                }

                tumbleBody.useGravity = false;
                tumbleBody.detectCollisions = false;

                // WalkingMotor / camera scripts drive the player directly while
                // controlled. Kinematic interpolation here can cause camera jitter.
                tumbleBody.interpolation =
                    RigidbodyInterpolation.None;

                tumbleBody.isKinematic = true;
                tumbleCollider.enabled = false;

                tumbleBody.Sleep();
            }
        }

        private void DisableGameplayControl()
        {
            if (walkingMotor != null)
                walkingMotor.enabled = false;

            if (lookController != null)
            {
                originalLookEnabled = lookController.enabled;
                lookController.enabled = false;
            }

            if (disableWhileUncontrolled == null)
                return;

            if (extraBehaviourStates == null ||
                extraBehaviourStates.Length != disableWhileUncontrolled.Length)
            {
                extraBehaviourStates =
                    new bool[disableWhileUncontrolled.Length];
            }

            for (int i = 0; i < disableWhileUncontrolled.Length; i++)
            {
                Behaviour behaviour =
                    disableWhileUncontrolled[i];

                if (behaviour == null ||
                    behaviour == this ||
                    behaviour == walkingMotor ||
                    behaviour == lookController)
                {
                    continue;
                }

                extraBehaviourStates[i] =
                    behaviour.enabled;

                behaviour.enabled = false;
            }
        }

        private void RestoreGameplayControl()
        {
            if (lookController != null)
                lookController.enabled = originalLookEnabled;

            if (disableWhileUncontrolled == null ||
                extraBehaviourStates == null)
            {
                return;
            }

            int count =
                Mathf.Min(
                    disableWhileUncontrolled.Length,
                    extraBehaviourStates.Length);

            for (int i = 0; i < count; i++)
            {
                Behaviour behaviour =
                    disableWhileUncontrolled[i];

                if (behaviour == null ||
                    behaviour == this ||
                    behaviour == walkingMotor ||
                    behaviour == lookController)
                {
                    continue;
                }

                behaviour.enabled =
                    extraBehaviourStates[i];
            }
        }

        private void LockCameraForFall()
        {
            if (!lockCameraToCapsule ||
                playerCamera == null)
            {
                return;
            }

            originalCameraParent =
                playerCamera.parent;

            originalCameraLocalPosition =
                playerCamera.localPosition;

            originalCameraLocalRotation =
                playerCamera.localRotation;

            playerCamera.SetParent(
                transform,
                false);

            playerCamera.localPosition =
                tumbleCameraLocalPosition;

            if (resetCameraRotationOnFall)
                playerCamera.localRotation =
                    Quaternion.identity;
        }

        private void RestoreFallCamera()
        {
            if (!lockCameraToCapsule ||
                playerCamera == null)
            {
                return;
            }

            playerCamera.SetParent(
                originalCameraParent,
                false);

            playerCamera.localPosition =
                originalCameraLocalPosition;

            playerCamera.localRotation =
                originalCameraLocalRotation;
        }

        private void OnDisable()
        {
            // Safety: never leave the spectator camera active if this controller
            // is disabled during normal gameplay.
            if (!playerDead)
                SetSpectatorCameraActive(false);
        }

        private void OnDestroy()
        {
            if (damageController != null)
                damageController.Died -= HandlePlayerDied;

            if (tumbleMaterial != null)
                Destroy(tumbleMaterial);
        }

        private void OnValidate()
        {
            loseControlSpeed =
                Mathf.Max(
                    0.1f,
                    loseControlSpeed);

            walkingCheckInterval =
                Mathf.Clamp(
                    walkingCheckInterval,
                    0.02f,
                    0.2f);

            minimumDamageImpactSpeed =
                Mathf.Max(
                    0f,
                    minimumDamageImpactSpeed);

            damagePerImpactSpeed =
                Mathf.Max(
                    0f,
                    damagePerImpactSpeed);

            maximumDamagePerImpact =
                Mathf.Max(
                    0f,
                    maximumDamagePerImpact);

            impactDamageCooldown =
                Mathf.Max(
                    0f,
                    impactDamageCooldown);

            hardLandingDownwardSpeed =
                Mathf.Max(
                    0f,
                    hardLandingDownwardSpeed);

            severeLandingDownwardSpeed =
                Mathf.Max(
                    hardLandingDownwardSpeed,
                    severeLandingDownwardSpeed);

            hardLandingGraceAfterRecovery =
                Mathf.Max(
                    0f,
                    hardLandingGraceAfterRecovery);
        }
    }
}
