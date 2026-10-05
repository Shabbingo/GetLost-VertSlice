using GetLost.Encounters;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GetLost.Wagon
{
    [DefaultExecutionOrder(-20)]
    [RequireComponent(typeof(WalkingMotor), typeof(CharacterController))]
    public sealed class WagonPlayerLink : MonoBehaviour
    {
        public WagonController Wagon { get; private set; }
        public bool IsHolding { get; private set; }
        public bool CanDrive => isActiveAndEnabled && motor != null && motor.enabled &&
            Time.timeScale > 0f;
        public Vector3 PullVelocity => motor != null ? motor.Velocity : Vector3.zero;

        private WalkingMotor motor;
        private CharacterController character;
        private System.Func<Vector3, Vector3> previousFilter;
        private float previousSpeed;
        private bool previousJump;

        public void Initialize(WagonController wagon)
        {
            Wagon = wagon;
            motor = GetComponent<WalkingMotor>();
            character = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (Wagon == null || motor == null) return;
            if (IsHolding && !motor.enabled) Release();
            if (!CanDrive) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.eKey.wasPressedThisFrame)
            {
                // A drop bear on this wagon takes interaction priority while it is
                // close enough to remove, and while the player is holding it.
                if (!DropBearEncounter.TryHandleInteract(this))
                {
                    if (IsHolding) Release();
                    else TryGrab();
                }
            }
            if (IsHolding && keyboard.gKey.wasPressedThisFrame) Wagon.ToggleGate();
            // Temporary supply interaction for testing, available at the parked wagon.
            if (!IsHolding && keyboard.rKey.wasPressedThisFrame &&
                Vector3.Distance(transform.position, Wagon.Body.position) < 3f)
                Wagon.Refill();

            if (IsHolding)
            {
                motor.ExternalSpeedMultiplier = previousSpeed * Mathf.Lerp(
                    Wagon.emptyPullSpeedMultiplier,
                    Wagon.loadedPullSpeedMultiplier,
                    Wagon.LoadFraction);

            }

        }

        public bool TryGrab()
        {
            if (Wagon == null || !CanDrive || IsHolding || Wagon.IsHeld) return false;
            Vector3 offset = Vector3.ProjectOnPlane(transform.position - Wagon.Grip.position, Vector3.up);
            if (offset.magnitude > Wagon.gripReach ||
                Mathf.Abs(transform.TransformPoint(character.center).y - Wagon.Grip.position.y) > 1.2f)
                return false;
            // Refuse grabs through an obstacle between the hands and drawbar.
            Vector3 hand = transform.TransformPoint(character.center);
            Vector3 delta = Wagon.Grip.position - hand;
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
            Wagon.SetHolder(this);
            // Start laying when taking the handles; G closes the gate for transport.
            if (Wagon.gravel > 0f && !Wagon.GateOpen) Wagon.ToggleGate();
            return true;
        }

        private Vector3 ConstrainMovement(Vector3 displacement)
        {
            if (previousFilter != null) displacement = previousFilter(displacement);
            if (!IsHolding || Wagon == null) return displacement;
            if (!CanDrive) return Vector3.zero;
            // Looking around does not change the grip constraint. CharacterController.Move
            // still collision-tests the resulting displacement against the player's obstacles.
            return WagonMath.LimitHandleDisplacement(transform.position, Wagon.Grip.position,
                displacement, Wagon.handleSlack);
        }

        public void Release()
        {
            if (!IsHolding) return;
            IsHolding = false;
            if (motor != null)
            {
                if (motor.DisplacementFilter == ConstrainMovement) motor.DisplacementFilter = previousFilter;
                motor.ExternalSpeedMultiplier = previousSpeed;
                motor.SuppressJump = previousJump;
                motor.ResetMotion();
            }
            if (Wagon != null)
            {
                Wagon.SetHolder(null);
                foreach (Collider collider in Wagon.Colliders)
                    if (collider != null && collider.enabled && character != null)
                        Physics.IgnoreCollision(character, collider, false);
            }
        }

        private void OnDisable() => Release();

        private void OnGUI()
        {
            if (Wagon == null || !CanDrive) return;
            bool near = Vector3.Distance(transform.position, Wagon.Body.position) < 5f;
            if (!near && !IsHolding) return;
            string text;
            if (IsHolding)
                text = $"E  Release    G  {(Wagon.GateOpen ? "Close" : "Open")} gravel gate\n" +
                    $"Gravel {Wagon.gravel:0.0} / {Wagon.capacity:0}    " +
                    (Wagon.gravel <= 0f ? "EMPTY" : Wagon.GateOpen ? "LAYING PATH" : "TRANSPORT") +
                    (Wagon.Resistance > 0.85f ? "\nHeavy resistance — back up or try another line" : "");
            else
                text = "Walk between the handles · E to pull\nR near wagon: refill gravel (prototype supply)";
            GUI.Box(new Rect(Screen.width * 0.5f - 235f, Screen.height - 105f, 470f, 85f), text);
        }
    }
}
