using GetLost.Encounters;
using GetLost.Player;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GetLost.Wagon
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(WalkingMotor), typeof(CharacterController))]
    public sealed class WagonPlayerLink : MonoBehaviour, ICameraEffect
    {
        public WagonController Wagon { get; private set; }
        public bool IsHolding { get; private set; }
        public bool IsRiding { get; private set; }
        public bool IsReturningToWalk { get; private set; }
        public float RideSteeringInput { get; private set; }
        public bool FeetBraking { get; private set; }
        public bool CanDrive => isActiveAndEnabled && motor != null && motor.enabled &&
            Time.timeScale > 0f;
        public Vector3 PullVelocity => motor != null ? motor.Velocity : Vector3.zero;
        public bool CameraEffectEnabled => Wagon != null &&
            (IsRiding || IsReturningToWalk || Mathf.Abs(RideSteeringInput) > 0.001f);

        private WalkingMotor motor;
        private WalkingInputReader input;
        private CameraEffectsController cameraEffects;
        private Transform playerView;
        private CharacterController character;
        private System.Func<Vector3, Vector3> previousFilter;
        private float previousSpeed;
        private bool previousJump;
        private Vector3 rideMountStart;
        private float rideMountStartedAt;
        private Vector3 rideReturnStart;
        private float rideReturnStartedAt;

        public void Initialize(WagonController wagon)
        {
            Wagon = wagon;
            motor = GetComponent<WalkingMotor>();
            input = GetComponent<WalkingInputReader>();
            cameraEffects = GetComponent<CameraEffectsController>();
            character = GetComponent<CharacterController>();
            foreach (Camera camera in GetComponentsInChildren<Camera>(true))
            {
                if (camera.name == "FallSpectator Camera")
                    continue;
                if (playerView == null || camera.enabled)
                    playerView = camera.transform;
            }
            if (playerView == null)
                playerView = transform;
            Wagon?.RegisterPlayer(this);
            cameraEffects?.RefreshEffects();
        }

        private void Update()
        {
            if (Wagon == null || motor == null) return;
            if (IsHolding && !motor.enabled) Release();
            if (!CanDrive) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            Wagon.UpdateWheelRepairTarget(
                playerView,
                !IsHolding && keyboard.eKey.isPressed);
            if (keyboard.eKey.wasPressedThisFrame)
            {
                // A drop bear on this wagon takes interaction priority while it is
                // close enough to remove, and while the player is holding it.
                if (!DropBearEncounter.TryHandleInteract(this))
                {
                    if (IsHolding) Release();
                    else if (Wagon.HandleWheelInteract(playerView)) { }
                    else if (Wagon.LookedAtRepairWheel == null) TryGrab();
                }
            }
            if (IsHolding && keyboard.gKey.wasPressedThisFrame) Wagon.ToggleGate();
            // Temporary supply interaction for testing, available at the parked wagon.
            if (!IsHolding && keyboard.rKey.wasPressedThisFrame &&
                Vector3.Distance(transform.position, Wagon.Body.position) < 3f)
                Wagon.Refill();

            if (IsHolding && !IsRiding && !IsReturningToWalk && keyboard.spaceKey.isPressed)
                BeginRide();

            if (IsRiding)
            {
                float steeringTarget = input != null
                    ? Mathf.Clamp(input.Move.x, -1f, 1f)
                    : (keyboard.aKey.isPressed ? -1f : keyboard.dKey.isPressed ? 1f : 0f);
                AccumulateRideWeightShift(steeringTarget);
                motor.ExternalSpeedMultiplier = 0f;
                if (!keyboard.spaceKey.isPressed)
                    BeginReturnToWalking();
            }
            else if (IsReturningToWalk)
            {
                ReturnRideWeightToCenter();
                FeetBraking = true;
                motor.ExternalSpeedMultiplier = 0f;
                if (Time.time - rideReturnStartedAt >= Mathf.Max(0.05f, Wagon.rideReturnDuration))
                    FinishReturnToWalking();
            }
            else if (IsHolding)
            {
                RideSteeringInput = 0f;
                motor.ExternalSpeedMultiplier = previousSpeed * Mathf.Lerp(
                    Wagon.emptyPullSpeedMultiplier,
                    Wagon.loadedPullSpeedMultiplier,
                    Wagon.LoadFraction);

            }

        }

        public bool TryGrab()
        {
            if (Wagon == null || !CanDrive || IsHolding || Wagon.IsHeld) return false;
            Transform chosenGrip = Wagon.GetClosestGrip(transform.position, out bool useRearGrip);
            Vector3 offset = Vector3.ProjectOnPlane(transform.position - chosenGrip.position, Vector3.up);
            if (offset.magnitude > Wagon.gripReach ||
                Mathf.Abs(transform.TransformPoint(character.center).y - chosenGrip.position.y) > 1.2f)
                return false;
            // Refuse grabs through an obstacle between the hands and selected grip.
            Vector3 hand = transform.TransformPoint(character.center);
            Vector3 delta = chosenGrip.position - hand;
            foreach (RaycastHit hit in Physics.RaycastAll(hand, delta.normalized,
                         delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == character || hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.transform.IsChildOf(Wagon.transform)) continue;
                return false;
            }
            previousFilter = motor.DisplacementFilter;
            previousSpeed = motor.ExternalSpeedMultiplier;
            previousJump = motor.SuppressJump;
            motor.DisplacementFilter = ConstrainMovement;
            motor.SuppressJump = true;
            foreach (Collider collider in Wagon.Colliders)
                if (collider != null && collider.enabled) Physics.IgnoreCollision(character, collider, true);
            IsHolding = true;
            Wagon.SetHolder(this, useRearGrip);
            // Start laying when taking the handles; G closes the gate for transport.
            if (Wagon.gravel > 0f && !Wagon.GateOpen) Wagon.ToggleGate();
            return true;
        }

        private void BeginRide()
        {
            if (!IsHolding || IsRiding || Wagon == null || Wagon.RiderMount == null)
                return;

            IsRiding = true;
            FeetBraking = false;
            RideSteeringInput = 0f;
            rideMountStart = transform.position;
            rideMountStartedAt = Time.time;
            motor.ExternalSpeedMultiplier = 0f;
            motor.ResetMotion();
        }

        private void BeginReturnToWalking()
        {
            if (!IsRiding || Wagon == null)
                return;

            IsRiding = false;
            IsReturningToWalk = true;
            FeetBraking = true;
            rideReturnStart = transform.position;
            rideReturnStartedAt = Time.time;
        }

        private void FinishReturnToWalking()
        {
            IsReturningToWalk = false;
            FeetBraking = false;
            RideSteeringInput = 0f;
            motor.ExternalSpeedMultiplier = previousSpeed * Mathf.Lerp(
                Wagon.emptyPullSpeedMultiplier,
                Wagon.loadedPullSpeedMultiplier,
                Wagon.LoadFraction);
            motor.ResetMotion();
        }

        private void AccumulateRideWeightShift(float direction)
        {
            // A/D move a persistent balance point instead of selecting an immediate
            // steering target. With no input, the rider gradually settles back toward
            // the centre; changing direction still requires crossing the current lean.
            if (Mathf.Abs(direction) < 0.01f)
            {
                RideSteeringInput = Mathf.MoveTowards(
                    RideSteeringInput,
                    0f,
                    Wagon.rideWeightReturnRate * Time.deltaTime);
                return;
            }

            RideSteeringInput = Mathf.Clamp(
                RideSteeringInput + direction * Wagon.rideWeightShiftRate * Time.deltaTime,
                -1f,
                1f);
        }

        private void ReturnRideWeightToCenter()
        {
            RideSteeringInput = Mathf.MoveTowards(
                RideSteeringInput,
                0f,
                Wagon.rideWeightShiftRate * 2f * Time.deltaTime);
        }

        private Vector3 ConstrainMovement(Vector3 displacement)
        {
            if (IsRiding && Wagon != null)
            {
                float duration = Mathf.Max(0.05f, Wagon.rideMountDuration);
                float progress = Mathf.Clamp01((Time.time - rideMountStartedAt) / duration);
                float eased = progress * progress * (3f - 2f * progress);
                Vector3 target = Vector3.Lerp(
                    rideMountStart,
                    Wagon.GetRiderWorldPosition(RideSteeringInput),
                    eased);
                target += Vector3.up * (Mathf.Sin(progress * Mathf.PI) * Wagon.rideMountJumpHeight);
                return target - transform.position;
            }

            if (IsReturningToWalk && Wagon != null)
            {
                float duration = Mathf.Max(0.05f, Wagon.rideReturnDuration);
                float progress = Mathf.Clamp01((Time.time - rideReturnStartedAt) / duration);
                float eased = progress * progress * (3f - 2f * progress);
                Vector3 target = Vector3.Lerp(rideReturnStart, GetWalkingPosition(), eased);
                target += Vector3.up * (Mathf.Sin(progress * Mathf.PI) * Wagon.rideReturnHopHeight);
                return target - transform.position;
            }

            if (previousFilter != null) displacement = previousFilter(displacement);
            if (!IsHolding || Wagon == null) return displacement;
            if (!CanDrive) return Vector3.zero;
            // Looking around does not change the grip constraint. CharacterController.Move
            // still collision-tests the resulting displacement against the player's obstacles.
            return WagonMath.LimitHandleDisplacement(transform.position, Wagon.ActiveGrip.position,
                displacement, Wagon.handleSlack);
        }

        public void Release()
        {
            ReleaseInternal(true);
        }

        public void ThrowFromWagon(Vector3 startingVelocity)
        {
            if (!IsRiding)
                return;

            ReleaseInternal(false);
            PlayerFallController fall = GetComponent<PlayerFallController>();
            if (fall != null)
                fall.ForceLoseControl(startingVelocity);
        }

        private void ReleaseInternal(bool moveRiderBeside)
        {
            if (!IsHolding) return;
            bool wasRiding = IsRiding || IsReturningToWalk;
            IsHolding = false;
            IsRiding = false;
            IsReturningToWalk = false;
            RideSteeringInput = 0f;
            FeetBraking = false;
            if (motor != null)
            {
                if (motor.DisplacementFilter == ConstrainMovement) motor.DisplacementFilter = previousFilter;
                motor.ExternalSpeedMultiplier = previousSpeed;
                motor.SuppressJump = previousJump;
                motor.ResetMotion();
            }
            if (wasRiding && moveRiderBeside)
                MovePlayerBesideWagon();
            if (Wagon != null)
            {
                Wagon.SetHolder(null);
                foreach (Collider collider in Wagon.Colliders)
                    if (collider != null && collider.enabled && character != null)
                        Physics.IgnoreCollision(character, collider, false);
            }
        }

        private Vector3 GetWalkingPosition()
        {
            return ProjectToGround(Wagon.GetWalkingPositionAtActiveGrip());
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Wagon == null || IsHolding || !CanDrive || hit == null ||
                hit.rigidbody == null || hit.rigidbody.isKinematic ||
                !Wagon.ContainsBody(hit.rigidbody))
                return;

            Vector3 pushDirection = Vector3.ProjectOnPlane(hit.moveDirection, Vector3.up);
            if (pushDirection.sqrMagnitude < 0.01f)
                return;

            Vector3 applicationPoint = hit.point + Vector3.up * Wagon.playerPushLeverHeight;
            hit.rigidbody.AddForceAtPosition(
                pushDirection.normalized * Wagon.playerBodyPushForce,
                applicationPoint,
                ForceMode.Force);
        }

        public CameraEffectFrame EvaluateCameraEffect(float deltaTime)
        {
            if (Wagon == null)
                return CameraEffectFrame.None;

            return new CameraEffectFrame(
                new Vector3(RideSteeringInput * Wagon.rideCameraShift, 0f, 0f),
                new Vector3(0f, 0f, -RideSteeringInput * Wagon.rideCameraTilt));
        }

        private void MovePlayerBesideWagon()
        {
            if (Wagon == null || character == null)
                return;

            Vector3 target = ProjectToGround(Wagon.GetDismountPosition());
            bool wasEnabled = character.enabled;
            character.enabled = false;
            transform.position = target;
            character.enabled = wasEnabled;
        }

        private Vector3 ProjectToGround(Vector3 target)
        {
            Vector3 rayOrigin = target + Vector3.up * 2f;
            float bestDistance = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(
                         rayOrigin,
                         Vector3.down,
                         5f,
                         ~0,
                         QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == character || hit.collider.transform.IsChildOf(transform) ||
                    hit.collider.transform.IsChildOf(Wagon.transform) || hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                target.y = hit.point.y;
            }
            return target;
        }

        private void OnDisable() => Release();

        private void OnGUI()
        {
            if (Wagon == null || !CanDrive) return;
            bool near = Vector3.Distance(transform.position, Wagon.Body.position) < 5f;
            if (!near && !IsHolding && Wagon.CarriedWheel == null &&
                Wagon.LookedAtLooseWheel == null) return;
            string text;
            if (Wagon.ActiveRepairWheel != null)
            {
                WagonWheel wheel = Wagon.ActiveRepairWheel;
                text = $"WRENCH — tightening {wheel.Label} wheel  {wheel.Tightness * 100f:0}%" +
                       (wheel.IsDetached ? "\nHolding wheel at the hub for reattachment" :
                           "\nWheel nuts are visibly tightening");
            }
            else if (Wagon.CarriedWheel != null)
            {
                WagonWheel wheel = Wagon.CarriedWheel;
                text = Wagon.CanPlaceCarriedWheel()
                    ? $"Carrying {wheel.Label} wheel\nPress E to position it on the hub"
                    : $"Carrying {wheel.Label} wheel\nTake it back to its empty hub";
            }
            else if (Wagon.LookedAtLooseWheel != null)
            {
                WagonWheel wheel = Wagon.LookedAtLooseWheel;
                text = $"Press E — pick up detached {wheel.Label} wheel";
            }
            else if (Wagon.LookedAtRepairWheel != null)
            {
                WagonWheel wheel = Wagon.LookedAtRepairWheel;
                text = $"Hold E — tighten {wheel.Label} wheel  {wheel.Tightness * 100f:0}%";
            }
            else if (IsRiding)
                text = $"RIDING  A/D balance weight ({RideSteeringInput * 100f:+0;-0;0}%)    Hold SPACE to stay aboard\n" +
                    $"Release SPACE to return to walking    E dismount    Speed {Wagon.Body.linearVelocity.magnitude:0.0} m/s";
            else if (IsReturningToWalk)
                text = "FEET DOWN — braking and returning to the handles";
            else if (IsHolding)
                text = $"E  Release    G  {(Wagon.GateOpen ? "Close" : "Open")} gravel gate\n" +
                    $"Gravel {Wagon.gravel:0.0} / {Wagon.capacity:0}    " +
                    (Wagon.gravel <= 0f ? "EMPTY" : Wagon.GateOpen ? "LAYING PATH" : "TRANSPORT") +
                    (Wagon.Resistance > 0.85f ? "\nHeavy resistance — back up or try another line" :
                        "\nPress SPACE to jump on and ride");
            else
                text = "Walk between the handles · E to pull\nR near wagon: refill gravel (prototype supply)";
            GUI.Box(new Rect(Screen.width * 0.5f - 235f, Screen.height - 105f, 470f, 85f), text);
        }
    }
}
