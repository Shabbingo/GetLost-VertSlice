using UnityEngine;

namespace Tom.WalkingController
{
    [DisallowMultipleComponent]
    public sealed class GroundProbe : MonoBehaviour
    {
        [SerializeField] private CharacterController characterController;
        [SerializeField, Min(0.05f)] private float probeDistance = 0.45f;
        [SerializeField, Min(0.01f)] private float sphereRadius = 0.28f;
        [SerializeField] private LayerMask groundLayers = ~0;

        public bool IsGrounded { get; private set; }
        public RaycastHit Hit { get; private set; }
        public Vector3 GroundNormal => IsGrounded ? Hit.normal : Vector3.up;
        public float SlopeAngle => Vector3.Angle(GroundNormal, Vector3.up);

        private void Reset()
        {
            characterController = GetComponent<CharacterController>();
        }

        public bool Probe()
        {
            if (characterController == null)
            {
                IsGrounded = false;
                return false;
            }

            Vector3 origin = transform.TransformPoint(characterController.center);
            float halfHeight = Mathf.Max(characterController.height * 0.5f, characterController.radius);
            origin += Vector3.down * (halfHeight - characterController.radius);

            IsGrounded = Physics.SphereCast(
                origin,
                sphereRadius,
                Vector3.down,
                out RaycastHit hit,
                probeDistance,
                groundLayers,
                QueryTriggerInteraction.Ignore);

            Hit = hit;
            return IsGrounded;
        }

        private void OnDrawGizmosSelected()
        {
            if (characterController == null)
                return;

            Gizmos.color = IsGrounded ? Color.green : Color.yellow;
            Vector3 origin = transform.TransformPoint(characterController.center);
            float halfHeight = Mathf.Max(characterController.height * 0.5f, characterController.radius);
            origin += Vector3.down * (halfHeight - characterController.radius);
            Gizmos.DrawWireSphere(origin + Vector3.down * probeDistance, sphereRadius);
        }
    }
}
