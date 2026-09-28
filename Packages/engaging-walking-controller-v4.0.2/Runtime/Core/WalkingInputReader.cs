using UnityEngine;
using UnityEngine.InputSystem;

namespace Tom.WalkingController
{
    /// <summary>
    /// Reads player input through InputActionReferences so the controller works
    /// with any Input Actions asset without requiring generated C# input classes.
    /// </summary>
    public sealed class WalkingInputReader : MonoBehaviour
    {
        [Header("Required Actions")]
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference lookAction;

        [Header("Optional Actions")]
        [SerializeField] private InputActionReference sprintAction;
        [SerializeField] private InputActionReference carefulWalkAction;
        [SerializeField] private InputActionReference jumpAction;

        public Vector2 Move => ReadVector2(moveAction);
        public Vector2 Look => ReadVector2(lookAction);
        public bool SprintHeld => IsPressed(sprintAction);
        public bool CarefulWalkHeld => IsPressed(carefulWalkAction);
        public bool JumpPressedThisFrame => WasPressedThisFrame(jumpAction);

        private void OnEnable()
        {
            SetEnabled(moveAction, true);
            SetEnabled(lookAction, true);
            SetEnabled(sprintAction, true);
            SetEnabled(carefulWalkAction, true);
            SetEnabled(jumpAction, true);
        }

        private void OnDisable()
        {
            SetEnabled(moveAction, false);
            SetEnabled(lookAction, false);
            SetEnabled(sprintAction, false);
            SetEnabled(carefulWalkAction, false);
            SetEnabled(jumpAction, false);
        }

        private static Vector2 ReadVector2(InputActionReference actionReference)
        {
            return actionReference != null && actionReference.action != null
                ? actionReference.action.ReadValue<Vector2>()
                : Vector2.zero;
        }

        private static bool IsPressed(InputActionReference actionReference)
        {
            return actionReference != null && actionReference.action != null && actionReference.action.IsPressed();
        }

        private static bool WasPressedThisFrame(InputActionReference actionReference)
        {
            return actionReference != null && actionReference.action != null && actionReference.action.WasPressedThisFrame();
        }

        private static void SetEnabled(InputActionReference actionReference, bool enabled)
        {
            if (actionReference == null || actionReference.action == null)
                return;

            if (enabled)
                actionReference.action.Enable();
            else
                actionReference.action.Disable();
        }
    }
}
