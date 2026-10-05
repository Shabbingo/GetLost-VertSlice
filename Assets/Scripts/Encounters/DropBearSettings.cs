using UnityEngine;

namespace GetLost.Encounters
{
    /// <summary>Shared tuning values used by every runtime drop bear encounter.</summary>
    [CreateAssetMenu(fileName = "DropBearSettings", menuName = "Get Lost/Encounters/Drop Bear Settings")]
    public sealed class DropBearSettings : ScriptableObject
    {
        [Header("Initial Spawning")]
        [Tooltip("Independent chance that each eligible terrain tree starts with a drop bear.")]
        [Range(0f, 1f)] public float bearChancePerTree = 0.01f;
        [Min(0)] public int maximumInitialBears = 24;
        [Min(0f)] public float minimumBearSpacing = 25f;
        [Min(0f)] public float minimumSpawnDistanceFromPlayer = 20f;

        [Header("Territory and Tree Drop")]
        [Min(1f)] public float territoryRadius = 8f;
        [Min(1f)] public float maximumWagonRange = 18f;
        [Min(0.1f)] public float leapDuration = 0.65f;
        [Min(0f)] public float leapArcHeight = 2.5f;
        [Range(0f, 1f)] public float dropOnPlayerChance = 0.4f;
        [Min(0f)] public float treeDropPlayerDamage = 12f;
        [Range(0f, 1f)] public float treeDropKnockdownChance = 0.45f;

        [Header("Wagon Attack")]
        [Min(0f)] public float spillGracePeriod = 1f;
        [Min(0f)] public float gravelLostPerSecond = 4f;
        [Min(0.5f)] public float pickupRange = 3f;
        [Range(2f, 45f)] public float pickupLookAngle = 14f;

        [Header("Player Scratches")]
        [Min(0f)] public float heldScratchDelay = 1.8f;
        [Min(0.1f)] public float heldScratchInterval = 1.15f;
        [Min(0f)] public float heldScratchDamage = 6f;
        [Range(0f, 1f)] public float escapeFromHandsChance = 0.3f;

        [Header("Ground Pursuit")]
        [Min(0.1f)] public float pursuitDuration = 5f;
        [Min(0f)] public float pursuitSpeed = 5.8f;
        [Min(0f)] public float pursuitAttackDamage = 9f;
        [Min(0.1f)] public float pursuitAttackInterval = 1.1f;
        [Min(0.1f)] public float pursuitAttackRange = 1.35f;
        [Range(0f, 1f)] public float pursuitKnockdownChance = 0.25f;
        [Min(0f)] public float pursuitKnockdownForce = 4.5f;

        [Header("Player Throw")]
        [Min(0.1f)] public float fullChargeSeconds = 1.25f;
        [Min(0f)] public float minimumThrowSpeed = 4f;
        [Min(0f)] public float maximumThrowSpeed = 17f;
        [Min(0f)] public float throwLift = 2.8f;
        [Min(0f)] public float landingStunSeconds = 2.5f;

        [Header("Escape and Re-engagement")]
        [Range(0f, 1f)] public float retreatAfterThrowChance = 0.55f;
        [Min(0f)] public float sprintSpeed = 7f;
        [Min(0.1f)] public float breakFreeDuration = 0.7f;
        [Min(0f)] public float breakFreeLeapDistance = 2.6f;
        [Min(0.1f)] public float climbDuration = 1.7f;
        [Min(0f)] public float perchedReengageDelay = 2.2f;
        [Min(1)] public int maximumAttackRounds = 2;
        [Min(0f)] public float minimumEscapeTreeDistance = 8f;
        [Min(1f)] public float maximumEscapeTreeDistance = 70f;

        [Header("Sound Effects")]
        [Tooltip("Played from the bear when it launches out of a tree at the player or wagon.")]
        public AudioClip[] leapSounds;
        [Tooltip("Played when the bear hurts the player, and periodically while it spills wagon gravel.")]
        public AudioClip[] damageSounds;
        [Tooltip("Played when the bear breaks free or retreats toward another tree.")]
        public AudioClip[] runAwaySounds;
        [Range(0f, 1f)] public float leapSoundVolume = 1f;
        [Range(0f, 1f)] public float damageSoundVolume = 0.85f;
        [Range(0f, 1f)] public float runAwaySoundVolume = 1f;
        [Min(0.1f)] public float wagonDamageSoundInterval = 0.8f;
    }
}
