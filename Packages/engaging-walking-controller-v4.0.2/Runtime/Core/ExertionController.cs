using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// A light-weight exertion model. It does not require a visible stamina bar.
    /// Other systems can read NormalizedExertion to drive breathing, sway or audio.
    /// </summary>
    public sealed class ExertionController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float sprintGainPerSecond = 0.18f;
        [SerializeField, Min(0f)] private float uphillGainPerSecond = 0.16f;
        [SerializeField, Min(0f)] private float recoveryPerSecond = 0.12f;
        [SerializeField, Range(0.2f, 1f)] private float minimumSpeedMultiplier = 0.78f;

        public float NormalizedExertion { get; private set; }
        public float SpeedMultiplier => Mathf.Lerp(1f, minimumSpeedMultiplier, NormalizedExertion);

        public void Tick(bool moving, bool sprinting, float uphillAmount, float surfaceMultiplier, float deltaTime)
        {
            float gain = 0f;

            if (moving && sprinting)
                gain += sprintGainPerSecond;

            if (moving && uphillAmount > 0f)
                gain += uphillGainPerSecond * uphillAmount;

            gain *= Mathf.Max(0f, surfaceMultiplier);

            if (gain > 0f)
                NormalizedExertion += gain * deltaTime;
            else
                NormalizedExertion -= recoveryPerSecond * deltaTime;

            NormalizedExertion = Mathf.Clamp01(NormalizedExertion);
        }

        public void ResetExertion()
        {
            NormalizedExertion = 0f;
        }
    }
}
