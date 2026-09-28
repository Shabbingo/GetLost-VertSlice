using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Non-diegetic feedback for the hidden footing system. It adds subtle camera
    /// instability, a peripheral vignette and directional stumble kicks without
    /// requiring URP post-processing or a UI canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FootingFeedback : MonoBehaviour, ICameraEffect
    {
        [Header("References")]
        [SerializeField] private WalkingMotor motor;
        [Tooltip("Usually the first-person camera or its pitch pivot. Feedback is added in LateUpdate so normal mouse look remains in control.")]
        [SerializeField] private CameraEffectsController cameraEffects;
        [SerializeField] private AudioSource audioSource;

        [Header("Feedback Thresholds")]
        [Tooltip("Feedback starts once footing falls below this value.")]
        [SerializeField, Range(0f, 1f)] private float feedbackStartFooting = 0.6f;
        [Tooltip("Critical feedback reaches full strength at or below this footing value.")]
        [SerializeField, Range(0f, 1f)] private float criticalFooting = 0.15f;
        [SerializeField, Min(0.1f)] private float feedbackSmoothing = 7f;

        [Header("Camera Instability")]
        [SerializeField] private bool useCameraInstability = true;
        [Tooltip("Maximum slow balance roll at critically low footing.")]
        [SerializeField, Range(0f, 8f)] private float maximumSwayRoll = 2.1f;
        [Tooltip("Maximum side-to-side balance yaw at critically low footing.")]
        [SerializeField, Range(0f, 5f)] private float maximumSwayYaw = 0.65f;
        [SerializeField, Min(0.05f)] private float swayFrequency = 0.85f;

        [Header("Slip Body Drop")]
        [SerializeField] private bool useDirectionalStumble = true;
        [Tooltip("A smaller body-drop can trigger when footing is being lost faster than this amount per second.")]
        [SerializeField, Min(0f)] private float stumbleLossThreshold = 0.22f;
        [Tooltip("Maximum local downward movement when a full slip begins.")]
        [SerializeField, Range(0f, 0.4f)] private float slipVerticalDip = 0.18f;
        [Tooltip("Small local forward movement. Keep this lower than the vertical dip.")]
        [SerializeField, Range(-0.2f, 0.2f)] private float slipForwardDip = 0.07f;
        [Tooltip("Small movement toward the downhill side.")]
        [SerializeField, Range(0f, 0.15f)] private float slipLateralShift = 0.025f;
        [Tooltip("Forward/downward pitch caused by the player's legs giving way.")]
        [SerializeField, Range(0f, 18f)] private float stumblePitch = 9f;
        [Tooltip("Directional roll toward the downhill side.")]
        [SerializeField, Range(0f, 12f)] private float stumbleRoll = 4.5f;
        [Tooltip("Time taken to drop into the slip pose.")]
        [SerializeField, Range(0.02f, 0.3f)] private float slipDropTime = 0.09f;
        [Tooltip("Brief pause at the bottom of the drop.")]
        [SerializeField, Range(0f, 0.2f)] private float slipHoldTime = 0.03f;
        [Tooltip("Time taken to rise back to the neutral camera pose.")]
        [SerializeField, Range(0.1f, 1.5f)] private float slipRecoveryTime = 0.45f;
        [Tooltip("Strength used for a footing-loss stumble that has not yet become a full slide.")]
        [SerializeField, Range(0f, 1f)] private float minorStumbleStrength = 0.35f;
        [SerializeField, Min(0f)] private float stumbleCooldown = 0.3f;

        [Header("Peripheral Vignette")]
        [SerializeField] private bool useVignette = true;
        [SerializeField] private Color vignetteColour = new(0.025f, 0.03f, 0.035f, 1f);
        [Tooltip("Maximum opacity of the screen-edge vignette at critically low footing.")]
        [SerializeField, Range(0f, 1f)] private float maximumVignetteOpacity = 0.42f;
        [Tooltip("Makes the clear centre larger or smaller.")]
        [SerializeField, Range(0.1f, 0.95f)] private float vignetteInnerRadius = 0.53f;
        [SerializeField, Range(0.55f, 1.5f)] private float vignetteOuterRadius = 0.96f;
        [Tooltip("Small pulse added while footing is actively falling.")]
        [SerializeField, Range(0f, 0.5f)] private float lossPulseStrength = 0.13f;
        [SerializeField, Min(0.1f)] private float lossPulseSpeed = 7f;
        [SerializeField, Range(64, 512)] private int vignetteTextureSize = 256;

        [Header("Optional Stumble Audio")]
        [SerializeField] private AudioClip[] stumbleClips;
        [SerializeField, Range(0f, 1f)] private float stumbleVolume = 0.35f;
        [SerializeField] private Vector2 stumblePitchRange = new(0.94f, 1.06f);

        private Texture2D vignetteTexture;
        private float smoothedStrength;
        private float stumbleTimer;
        private float slipElapsed = -1f;
        private float slipStrength;
        private Vector2 stumbleDirection;
        private bool wasSliding;
        private int lastClipIndex = -1;

        public float FeedbackStrength => smoothedStrength;
        public bool CameraEffectEnabled => useCameraInstability && motor != null;

        private void Reset()
        {
            motor = GetComponentInParent<WalkingMotor>();
            cameraEffects = GetComponentInParent<CameraEffectsController>();
            audioSource = GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (motor == null)
                motor = GetComponentInParent<WalkingMotor>();
            if (cameraEffects == null)
                cameraEffects = GetComponentInParent<CameraEffectsController>();

            if (useVignette)
                RebuildVignetteTexture();
        }

        private void OnEnable()
        {
            cameraEffects?.RefreshEffects();
        }

        private void OnDestroy()
        {
            if (vignetteTexture == null)
                return;

            if (Application.isPlaying)
                Destroy(vignetteTexture);
            else
                DestroyImmediate(vignetteTexture);
        }

        private void LateUpdate()
        {
            if (motor == null)
                return;

            float targetStrength = EvaluateStrength(motor.CurrentFooting);
            smoothedStrength = Mathf.Lerp(
                smoothedStrength,
                targetStrength,
                1f - Mathf.Exp(-feedbackSmoothing * Time.deltaTime));

            stumbleTimer -= Time.deltaTime;
            bool slideStarted = motor.IsSliding && !wasSliding;
            bool suddenLoss = motor.FootingLossPerSecond >= stumbleLossThreshold && smoothedStrength > 0.05f;

            if (useDirectionalStumble && stumbleTimer <= 0f && (slideStarted || suddenLoss))
            {
                float lossStrength = Mathf.InverseLerp(
                    stumbleLossThreshold,
                    stumbleLossThreshold * 3f,
                    motor.FootingLossPerSecond);
                TriggerStumble(slideStarted, lossStrength);
            }

            wasSliding = motor.IsSliding;
            if (slipElapsed >= 0f)
                slipElapsed += Time.deltaTime;

        }

        private float EvaluateStrength(float footing)
        {
            if (footing >= feedbackStartFooting)
                return 0f;

            return 1f - Mathf.InverseLerp(criticalFooting, feedbackStartFooting, footing);
        }

        private void TriggerStumble(bool fullSlip, float strength)
        {
            stumbleTimer = stumbleCooldown;
            slipElapsed = 0f;
            slipStrength = fullSlip
                ? 1f
                : Mathf.Lerp(minorStumbleStrength, Mathf.Min(0.75f, minorStumbleStrength + 0.3f), Mathf.Clamp01(strength));

            Vector3 downhill = motor.CurrentDownhillDirection;
            Transform reference = cameraEffects != null && cameraEffects.CameraTransform != null ? cameraEffects.CameraTransform : transform;
            if (downhill.sqrMagnitude > 0.001f)
            {
                Vector3 localDownhill = reference.InverseTransformDirection(downhill.normalized);
                stumbleDirection = new Vector2(
                    Mathf.Clamp(localDownhill.x, -1f, 1f),
                    Mathf.Clamp(localDownhill.z, -1f, 1f));
            }
            else
            {
                stumbleDirection = new Vector2(Random.Range(-0.6f, 0.6f), 1f).normalized;
            }

            PlayStumbleAudio();
        }

        public CameraEffectFrame EvaluateCameraEffect(float deltaTime)
        {
            if (!useCameraInstability || motor == null)
                return CameraEffectFrame.None;

            float time = Time.time * swayFrequency * Mathf.PI * 2f;
            float swayRoll = Mathf.Sin(time) * maximumSwayRoll * smoothedStrength;
            float swayYaw = Mathf.Sin(time * 0.73f + 1.1f) * maximumSwayYaw * smoothedStrength;

            float slipEnvelope = EvaluateSlipEnvelope();
            float activeSlip = slipEnvelope * slipStrength;

            float kickRoll = -stumbleDirection.x * stumbleRoll * activeSlip;
            float kickPitch = stumblePitch * activeSlip;
            Vector3 bodyDrop = new(
                stumbleDirection.x * slipLateralShift * activeSlip,
                -slipVerticalDip * activeSlip,
                slipForwardDip * activeSlip);

            return new CameraEffectFrame(
                bodyDrop,
                new Vector3(kickPitch, swayYaw, swayRoll + kickRoll));
        }


        private float EvaluateSlipEnvelope()
        {
            if (slipElapsed < 0f)
                return 0f;

            float drop = Mathf.Max(0.001f, slipDropTime);
            float holdEnd = drop + slipHoldTime;
            float end = holdEnd + Mathf.Max(0.001f, slipRecoveryTime);

            if (slipElapsed < drop)
            {
                float t = Mathf.Clamp01(slipElapsed / drop);
                return t * t * (3f - 2f * t);
            }

            if (slipElapsed <= holdEnd)
                return 1f;

            if (slipElapsed < end)
            {
                float t = Mathf.Clamp01((slipElapsed - holdEnd) / Mathf.Max(0.001f, slipRecoveryTime));
                float smooth = t * t * (3f - 2f * t);
                return 1f - smooth;
            }

            slipElapsed = -1f;
            slipStrength = 0f;
            return 0f;
        }

        private void OnGUI()
        {
            if (!useVignette || smoothedStrength <= 0.001f || Event.current.type != EventType.Repaint)
                return;

            if (vignetteTexture == null)
                RebuildVignetteTexture();
            if (vignetteTexture == null)
                return;

            float pulse = motor != null && motor.FootingLossPerSecond > 0.01f
                ? (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * lossPulseSpeed)) * lossPulseStrength
                : 0f;

            float opacity = Mathf.Clamp01(smoothedStrength * maximumVignetteOpacity + pulse * smoothedStrength);
            Color oldColour = GUI.color;
            GUI.color = new Color(vignetteColour.r, vignetteColour.g, vignetteColour.b, vignetteColour.a * opacity);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignetteTexture, ScaleMode.StretchToFill, true);
            GUI.color = oldColour;
        }

        private void RebuildVignetteTexture()
        {
            int size = Mathf.Clamp(vignetteTextureSize, 64, 512);
            vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Footing Vignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[size * size];
            float inner = Mathf.Clamp01(vignetteInnerRadius);
            float outer = Mathf.Max(inner + 0.01f, vignetteOuterRadius);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size * 2f - 1f;
                    float ny = (y + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(nx * nx + ny * ny);
                    float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, radius));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            vignetteTexture.SetPixels(pixels);
            vignetteTexture.Apply(false, true);
        }

        private void PlayStumbleAudio()
        {
            if (audioSource == null || stumbleClips == null || stumbleClips.Length == 0)
                return;

            int index = Random.Range(0, stumbleClips.Length);
            if (stumbleClips.Length > 1 && index == lastClipIndex)
                index = (index + 1) % stumbleClips.Length;
            lastClipIndex = index;

            AudioClip clip = stumbleClips[index];
            if (clip == null)
                return;

            audioSource.pitch = Random.Range(
                Mathf.Min(stumblePitchRange.x, stumblePitchRange.y),
                Mathf.Max(stumblePitchRange.x, stumblePitchRange.y));
            audioSource.PlayOneShot(clip, stumbleVolume);
        }

        private void OnValidate()
        {
            if (criticalFooting > feedbackStartFooting)
                criticalFooting = feedbackStartFooting;
            if (vignetteOuterRadius <= vignetteInnerRadius)
                vignetteOuterRadius = vignetteInnerRadius + 0.01f;
        }
    }
}
