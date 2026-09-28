using UnityEngine;
using UnityEngine.InputSystem;

namespace Tom.WalkingController
{
    /// <summary>
    /// Traditional FPS mouse/gamepad look.
    /// Rotate the player object for yaw and a child camera pivot for pitch.
    ///
    /// Hold Left Alt to temporarily release the cursor for UI interaction.
    /// </summary>
    public sealed class FirstPersonLook : MonoBehaviour
    {
        [SerializeField] private WalkingInputReader input;
        [SerializeField] private Transform yawTarget;
        [SerializeField] private Transform pitchTarget;

        [Header("Look")]
        [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(1f)] private float gamepadSensitivity = 120f;
        [SerializeField, Range(10f, 89f)] private float maximumPitch = 85f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursorOnEnable = true;
        [SerializeField] private bool holdAltToReleaseCursor = true;

        private float pitch;
        private bool cursorReleased;

        private void Reset()
        {
            input = GetComponentInParent<WalkingInputReader>();
            yawTarget = transform.root;
            pitchTarget = transform;
        }

        private void OnEnable()
        {
            cursorReleased = false;

            if (lockCursorOnEnable)
                SetCursorLocked(true);
        }

        private void OnDisable()
        {
            if (lockCursorOnEnable)
                SetCursorLocked(false);
        }

        private void Update()
        {
            HandleCursorRelease();

            if (input == null || yawTarget == null || pitchTarget == null)
                return;

            // Do not rotate the camera while the cursor is released.
            if (cursorReleased)
                return;

            Vector2 look = input.Look;

            bool usingMouse =
                Mouse.current != null &&
                Mouse.current.delta.ReadValue().sqrMagnitude > 0.001f;

            float multiplier = usingMouse
                ? mouseSensitivity
                : gamepadSensitivity * Time.deltaTime;

            yawTarget.Rotate(
                Vector3.up,
                look.x * multiplier,
                Space.World);

            pitch = Mathf.Clamp(
                pitch - look.y * multiplier,
                -maximumPitch,
                maximumPitch);

            pitchTarget.localRotation =
                Quaternion.Euler(pitch, 0f, 0f);
        }

        private void HandleCursorRelease()
        {
            if (!holdAltToReleaseCursor)
                return;

            bool altHeld =
                Keyboard.current != null &&
                Keyboard.current.leftAltKey.isPressed;

            if (altHeld == cursorReleased)
                return;

            cursorReleased = altHeld;
            SetCursorLocked(!cursorReleased);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked
                ? CursorLockMode.Locked
                : CursorLockMode.None;

            Cursor.visible = !locked;
        }
    }
}

