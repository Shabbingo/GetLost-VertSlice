using UnityEngine;

public class CompassNeedle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private RectTransform needle;

    [Header("Needle Physics")]
    [SerializeField] private float springStrength = 45f;
    [SerializeField] private float damping = 7f;
    [SerializeField] private float maxSpeed = 300f;
    [SerializeField] private float northOffset = 0f;

    [Header("Movement Bobble")]
    [SerializeField] private float walkBobbleAmount = 4f;
    [SerializeField] private float walkBobbleSpeed = 7f;

    [Header("Idle Drift")]
    [SerializeField] private float idleSwayAmount = 1f;
    [SerializeField] private float idleSwaySpeed = 0.5f;

    [Header("Movement Detection")]
    [SerializeField] private float movementThreshold = 0.05f;

    private float currentAngle;
    private float angularVelocity;

    private Vector3 lastPosition;
    private float noiseOffset;

    private void Start()
    {
        if (player == null)
            player = transform.root;

        if (needle == null)
            needle = transform as RectTransform;

        if (player == null || needle == null)
        {
            enabled = false;
            return;
        }

        currentAngle = needle.localEulerAngles.z;
        lastPosition = player.position;

        // Gives every compass a different wobble pattern
        noiseOffset = Random.Range(0f, 1000f);
    }

    private void Update()
    {
        if (player == null || needle == null)
            return;

        float heading = player.eulerAngles.y;

        // Detect movement
        float speed = (player.position - lastPosition).magnitude / Time.deltaTime;
        lastPosition = player.position;

        bool moving = speed > movementThreshold;

        //----------------------------------------
        // Idle sway
        //----------------------------------------

        float sway =
            Mathf.Sin(Time.time * idleSwaySpeed) *
            idleSwayAmount;

        //----------------------------------------
        // Walking wobble
        //----------------------------------------

        float bobble = 0f;

        if (moving)
        {
            float sin =
                Mathf.Sin(Time.time * walkBobbleSpeed);

            float noise =
                (Mathf.PerlinNoise(Time.time * 5f, noiseOffset) - 0.5f) * 2f;

            bobble =
                sin * walkBobbleAmount +
                noise * (walkBobbleAmount * 0.5f);
        }

        //----------------------------------------

        float targetAngle =
            -heading +
            northOffset +
            sway +
            bobble;

        float delta =
            Mathf.DeltaAngle(currentAngle, targetAngle);

        angularVelocity += delta * springStrength * Time.deltaTime;
        angularVelocity *= Mathf.Exp(-damping * Time.deltaTime);
        angularVelocity = Mathf.Clamp(angularVelocity, -maxSpeed, maxSpeed);

        currentAngle += angularVelocity * Time.deltaTime;

        needle.localRotation =
            Quaternion.Euler(0, 0, currentAngle);
    }
}
