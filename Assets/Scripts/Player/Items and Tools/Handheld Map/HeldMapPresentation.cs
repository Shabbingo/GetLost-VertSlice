using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gives the generated map a Minecraft-style held-item presentation. The map
/// remains camera-relative, rises from a hidden pose when equipped, moves
/// closer while RMB focus is active, and adds restrained walking bob and
/// mouse-driven inertia. Every pose and motion value is Inspector editable.
/// </summary>
[DefaultExecutionOrder(11000)]
[DisallowMultipleComponent]
public sealed class HeldMapPresentation : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private WorldMapTool mapTool;
    [SerializeField] private Transform movementSource;
    [SerializeField] private Transform viewTransform;
    [SerializeField] private GameObject mapVisualRoot;

    [Header("Hidden Pose")]
    [SerializeField] private Vector3 hiddenLocalPosition = new(0f, -0.78f, 0.9f);
    [SerializeField] private Vector3 hiddenLocalEuler = new(28f, 0f, 0f);

    [Header("Held Pose")]
    [SerializeField] private Vector3 heldLocalPosition = new(0f, -0.38f, 0.82f);
    [SerializeField] private Vector3 heldLocalEuler = new(10f, 0f, 0f);

    [Header("Close Reading Pose")]
    [SerializeField] private Vector3 focusedLocalPosition = new(0f, -0.055f, 0.56f);
    [SerializeField] private Vector3 focusedLocalEuler = Vector3.zero;

    [Tooltip("Keeps the page square to the camera while RMB inspection is active, regardless of the camera's world pitch.")]
    [SerializeField] private bool alignFocusedMapToCamera = true;

    [Tooltip("Usually disabled: the normal look-down reveal can make the page oblique during close reading.")]
    [SerializeField] private bool useLookDownRevealWhileFocused;

    [Header("Physical Size")]
    [SerializeField] private float heldScale = 0.0012f;
    [SerializeField] private float focusedScale = 0.00105f;

    [Header("Raise / Lower")]
    [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.12f;
    [SerializeField, Min(0.1f)] private float rotationFollowSpeed = 14f;
    [SerializeField, Min(0.1f)] private float scaleFollowSpeed = 14f;
    [SerializeField, Min(0.001f)] private float hideDistance = 0.025f;

    [Header("Walking Motion")]
    [SerializeField, Min(0f)] private float bobHorizontal = 0.008f;
    [SerializeField, Min(0f)] private float bobVertical = 0.014f;
    [SerializeField, Min(0f)] private float bobRollDegrees = 1.2f;
    [SerializeField, Min(0.1f)] private float bobFrequency = 7.5f;
    [SerializeField, Min(0.01f)] private float fullBobAtSpeed = 3.5f;
    [SerializeField, Range(0f, 1f)] private float focusedBobMultiplier = 0.2f;

    [Header("Look Inertia")]
    [SerializeField, Min(0f)] private float lookSwayDegrees = 0.035f;
    [SerializeField, Min(0f)] private float maximumLookSway = 3f;
    [SerializeField, Min(0.1f)] private float swayFollowSpeed = 12f;

    [Header("Look Down Reveal")]
    [Tooltip("How strongly the map resists following downward camera pitch. One keeps it almost body-relative; zero follows the camera completely.")]
    [SerializeField, Range(0f, 1f)] private float downwardPitchCompensation = 0.82f;
    [SerializeField, Min(0f)] private float revealStartsAtPitch = 5f;
    [SerializeField, Min(1f)] private float fullRevealAtPitch = 65f;
    [SerializeField, Min(0f)] private float lookDownLift = 0.38f;
    [SerializeField, Min(0f)] private float lookDownMoveCloser = 0.045f;

    private Vector3 positionVelocity;
    private Vector3 lastMovementPosition;
    private Vector2 currentSway;
    private bool heldRequested;

    private void Awake()
    {
        if (mapTool == null)
            mapTool = FindAnyObjectByType<WorldMapTool>();

        if (movementSource == null && Camera.main != null)
            movementSource = Camera.main.transform.root;

        if (viewTransform == null && Camera.main != null)
            viewTransform = Camera.main.transform;

        lastMovementPosition = movementSource != null
            ? movementSource.position
            : transform.position;

        SnapToHiddenPose();
        if (mapVisualRoot != null)
            mapVisualRoot.SetActive(false);
    }

    public void SetHeld(bool held)
    {
        heldRequested = held;
        if (held && mapVisualRoot != null)
            mapVisualRoot.SetActive(true);
    }

    private void LateUpdate()
    {
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        bool focused = heldRequested && mapTool != null && mapTool.IsFocused;

        Vector3 targetPosition = heldRequested
            ? focused ? focusedLocalPosition : heldLocalPosition
            : hiddenLocalPosition;
        Vector3 targetEuler = heldRequested
            ? focused ? focusedLocalEuler : heldLocalEuler
            : hiddenLocalEuler;
        float targetScale = focused ? focusedScale : heldScale;

        // The map is parented to the camera for stable first-person placement,
        // but a held object should not rigidly inherit downward pitch. Cancel
        // most of that pitch and lift the pose so looking down reveals the
        // lower edge of the paper, like a Minecraft-style held map.
        float downwardPitch = 0f;
        if (heldRequested && viewTransform != null)
        {
            downwardPitch = Mathf.Max(
                0f,
                Mathf.Asin(Mathf.Clamp(-viewTransform.forward.y, -1f, 1f)) * Mathf.Rad2Deg);
        }

        bool applyLookDownReveal = !focused || useLookDownRevealWhileFocused;
        if (applyLookDownReveal)
        {
            float revealAmount = Mathf.InverseLerp(
                revealStartsAtPitch,
                Mathf.Max(revealStartsAtPitch + 1f, fullRevealAtPitch),
                downwardPitch);
            targetPosition.y += lookDownLift * revealAmount;
            targetPosition.z -= lookDownMoveCloser * revealAmount;
            targetEuler.x -= downwardPitch * downwardPitchCompensation;
        }

        if (focused && alignFocusedMapToCamera)
            targetEuler = focusedLocalEuler;

        float movementSpeed = 0f;
        if (movementSource != null)
        {
            movementSpeed = Vector3.Distance(movementSource.position, lastMovementPosition) / deltaTime;
            lastMovementPosition = movementSource.position;
        }

        float motionWeight = heldRequested
            ? Mathf.Clamp01(movementSpeed / fullBobAtSpeed)
            : 0f;
        if (focused)
            motionWeight *= focusedBobMultiplier;

        float phase = Time.time * bobFrequency;
        targetPosition += new Vector3(
            Mathf.Sin(phase * 0.5f) * bobHorizontal,
            Mathf.Abs(Mathf.Sin(phase)) * bobVertical,
            0f) * motionWeight;

        // RMB inspection owns the mouse and pans the flat page through
        // WorldMapTool. Do not also rotate the held-item root.
        Vector2 mouseDelta = heldRequested && !focused && Mouse.current != null
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;
        Vector2 swayTarget = new(
            Mathf.Clamp(-mouseDelta.x * lookSwayDegrees, -maximumLookSway, maximumLookSway),
            Mathf.Clamp(mouseDelta.y * lookSwayDegrees, -maximumLookSway, maximumLookSway));
        currentSway = focused
            ? Vector2.zero
            : Vector2.Lerp(
                currentSway,
                swayTarget,
                1f - Mathf.Exp(-swayFollowSpeed * deltaTime));

        targetEuler += new Vector3(
            currentSway.y,
            currentSway.x,
            Mathf.Sin(phase * 0.5f) * bobRollDegrees * motionWeight);

        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            targetPosition,
            ref positionVelocity,
            positionSmoothTime,
            Mathf.Infinity,
            deltaTime);
        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            Quaternion.Euler(targetEuler),
            1f - Mathf.Exp(-rotationFollowSpeed * deltaTime));
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            Vector3.one * targetScale,
            1f - Mathf.Exp(-scaleFollowSpeed * deltaTime));

        if (!heldRequested && mapVisualRoot != null &&
            Vector3.Distance(transform.localPosition, hiddenLocalPosition) <= hideDistance)
        {
            mapVisualRoot.SetActive(false);
        }
    }

    [ContextMenu("Preview Hidden Pose")]
    private void SnapToHiddenPose()
    {
        transform.localPosition = hiddenLocalPosition;
        transform.localRotation = Quaternion.Euler(hiddenLocalEuler);
        transform.localScale = Vector3.one * heldScale;
        positionVelocity = Vector3.zero;
        currentSway = Vector2.zero;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        fullRevealAtPitch = Mathf.Max(revealStartsAtPitch + 1f, fullRevealAtPitch);
        heldScale = Mathf.Max(0.00001f, heldScale);
        focusedScale = Mathf.Max(0.00001f, focusedScale);
    }
#endif
}
