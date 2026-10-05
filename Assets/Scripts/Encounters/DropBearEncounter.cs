using System.Collections.Generic;
using GetLost.Player;
using GetLost.Wagon;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GetLost.Encounters
{
    /// <summary>
    /// A short wagon crisis: entering the territory sends the perched drop bear
    /// onto the wagon, where it spills gravel until the player removes and throws it.
    /// </summary>
    public sealed class DropBearEncounter : MonoBehaviour
    {
        private enum EncounterState
        {
            Perched, Leaping, Attached, AttachedToPlayer, Held, Thrown, Stunned,
            PursuingPlayer, BreakingFree, SprintingToTree, Climbing, Resolved
        }

        private enum LeapTarget { Wagon, Player }

        private static readonly List<DropBearEncounter> Active = new();

        [Header("Territory")]
        [Min(1f)] public float territoryRadius = 8f;
        [Min(1f)] public float maximumWagonRange = 18f;
        [Min(0.1f)] public float leapDuration = 0.65f;
        [Min(0f)] public float leapArcHeight = 2.5f;

        [Header("Wagon Crisis")]
        [Min(0f)] public float spillGracePeriod = 1f;
        [Min(0f)] public float gravelLostPerSecond = 4f;
        [Min(0.5f)] public float pickupRange = 3f;
        [Range(2f, 45f)] public float pickupLookAngle = 14f;

        [Header("Throw")]
        [Min(0.1f)] public float fullChargeSeconds = 1.25f;
        [Min(0f)] public float minimumThrowSpeed = 4f;
        [Min(0f)] public float maximumThrowSpeed = 17f;
        [Min(0f)] public float throwLift = 2.8f;
        [Min(0f)] public float landingStunSeconds = 2.5f;

        [Header("Player Attack")]
        [Min(0f)] public float heldScratchDelay = 1.8f;
        [Min(0.1f)] public float heldScratchInterval = 1.15f;
        [Min(0f)] public float heldScratchDamage = 6f;
        [Range(0f, 1f)] public float escapeFromHandsChance = 0.3f;
        [Min(0.1f)] public float pursuitDuration = 5f;
        [Min(0f)] public float pursuitSpeed = 5.8f;
        [Min(0f)] public float pursuitAttackDamage = 9f;
        [Min(0.1f)] public float pursuitAttackInterval = 1.1f;
        [Min(0.1f)] public float pursuitAttackRange = 1.35f;
        [Range(0f, 1f)] public float pursuitKnockdownChance = 0.25f;
        [Min(0f)] public float pursuitKnockdownForce = 4.5f;
        [Range(0f, 1f)] public float dropOnPlayerChance = 0.4f;
        [Min(0f)] public float treeDropPlayerDamage = 12f;
        [Range(0f, 1f)] public float treeDropKnockdownChance = 0.45f;

        [Header("Escape and Re-engage")]
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
        public AudioClip[] leapSounds;
        public AudioClip[] damageSounds;
        public AudioClip[] runAwaySounds;
        [Range(0f, 1f)] public float leapSoundVolume = 1f;
        [Range(0f, 1f)] public float damageSoundVolume = 0.85f;
        [Range(0f, 1f)] public float runAwaySoundVolume = 1f;
        [Min(0.1f)] public float wagonDamageSoundInterval = 0.8f;

        public bool IsResolved => state == EncounterState.Resolved;
        public bool IsAttached => state == EncounterState.Attached;
        public float Charge01 => charging ? Mathf.Clamp01((Time.time - chargeStartedAt) / fullChargeSeconds) : 0f;

        private EncounterState state;
        private LeapTarget leapTarget;
        private Transform player;
        private Camera playerCamera;
        private WagonController wagon;
        private WagonPlayerLink holder;
        private PlayerDamageController playerHealth;
        private Rigidbody bearBody;
        private Collider bearCollider;
        private Transform bearVisual;
        private TrailRenderer escapeTrail;
        private ParticleSystem gravelSpill;
        private AudioSource bearAudio;
        private Vector3 perchPosition;
        private Vector3 territoryCenter;
        private Vector3 movementTarget;
        private Vector3 climbStart;
        private Vector3 breakFreeStart;
        private Vector3 breakFreeEnd;
        private Vector3 leapStart;
        private Vector3 wagonAttachLocal;
        private float stateStartedAt;
        private float chargeStartedAt;
        private float nextAttackAt;
        private float nextWagonDamageSoundAt;
        private float lastThrowCharge;
        private float escapeNoticeUntil;
        private bool charging;
        private bool releasedPickupPress;
        private bool hasLanded;
        private bool resolveAfterClimb;
        private int attackRounds;
        private DropBearSettings sourceSettings;
        private float nextSettingsRefresh;

        public void ApplySettings(DropBearSettings settings)
        {
            if (settings == null)
                return;
            sourceSettings = settings;
            territoryRadius = settings.territoryRadius;
            maximumWagonRange = settings.maximumWagonRange;
            leapDuration = settings.leapDuration;
            leapArcHeight = settings.leapArcHeight;
            dropOnPlayerChance = settings.dropOnPlayerChance;
            treeDropPlayerDamage = settings.treeDropPlayerDamage;
            treeDropKnockdownChance = settings.treeDropKnockdownChance;
            spillGracePeriod = settings.spillGracePeriod;
            gravelLostPerSecond = settings.gravelLostPerSecond;
            pickupRange = settings.pickupRange;
            pickupLookAngle = settings.pickupLookAngle;
            heldScratchDelay = settings.heldScratchDelay;
            heldScratchInterval = settings.heldScratchInterval;
            heldScratchDamage = settings.heldScratchDamage;
            escapeFromHandsChance = settings.escapeFromHandsChance;
            pursuitDuration = settings.pursuitDuration;
            pursuitSpeed = settings.pursuitSpeed;
            pursuitAttackDamage = settings.pursuitAttackDamage;
            pursuitAttackInterval = settings.pursuitAttackInterval;
            pursuitAttackRange = settings.pursuitAttackRange;
            pursuitKnockdownChance = settings.pursuitKnockdownChance;
            pursuitKnockdownForce = settings.pursuitKnockdownForce;
            fullChargeSeconds = settings.fullChargeSeconds;
            minimumThrowSpeed = settings.minimumThrowSpeed;
            maximumThrowSpeed = settings.maximumThrowSpeed;
            throwLift = settings.throwLift;
            landingStunSeconds = settings.landingStunSeconds;
            retreatAfterThrowChance = settings.retreatAfterThrowChance;
            sprintSpeed = settings.sprintSpeed;
            breakFreeDuration = settings.breakFreeDuration;
            breakFreeLeapDistance = settings.breakFreeLeapDistance;
            climbDuration = settings.climbDuration;
            perchedReengageDelay = settings.perchedReengageDelay;
            maximumAttackRounds = settings.maximumAttackRounds;
            minimumEscapeTreeDistance = settings.minimumEscapeTreeDistance;
            maximumEscapeTreeDistance = Mathf.Max(minimumEscapeTreeDistance, settings.maximumEscapeTreeDistance);
            leapSounds = settings.leapSounds;
            damageSounds = settings.damageSounds;
            runAwaySounds = settings.runAwaySounds;
            leapSoundVolume = settings.leapSoundVolume;
            damageSoundVolume = settings.damageSoundVolume;
            runAwaySoundVolume = settings.runAwaySoundVolume;
            wagonDamageSoundInterval = settings.wagonDamageSoundInterval;
        }

        public void Initialize(Vector3 branchPosition, float radius = 8f)
        {
            territoryRadius = Mathf.Max(1f, radius);
            territoryCenter = transform.position;
            perchPosition = branchPosition;
            BuildBearIfNeeded();
            SetPerchedPose();
        }

        private void Awake()
        {
            Active.Add(this);
            territoryCenter = transform.position;
            perchPosition = transform.position + Vector3.up * 4f;
            BuildBearIfNeeded();
            SetPerchedPose();
        }

        private void OnDestroy()
        {
            Active.Remove(this);
            if (gravelSpill != null)
                Destroy(gravelSpill.gameObject);
        }

        /// <summary>Called by WagonPlayerLink before its normal E interaction.</summary>
        public static bool TryHandleInteract(WagonPlayerLink link)
        {
            if (link == null)
                return false;

            for (int i = Active.Count - 1; i >= 0; i--)
            {
                DropBearEncounter encounter = Active[i];
                if (encounter == null)
                    continue;
                if (encounter.state == EncounterState.Held && encounter.holder == link)
                    return true;
                if (encounter.state == EncounterState.AttachedToPlayer &&
                    encounter.player == link.transform)
                {
                    encounter.PickUp(link);
                    return true;
                }
                if (encounter.state != EncounterState.Attached || encounter.wagon != link.Wagon)
                    continue;
                if (encounter.wagon == null || encounter.wagon.Body == null ||
                    Vector3.Distance(link.transform.position, encounter.wagon.Body.position) > encounter.pickupRange)
                    continue;
                if (!encounter.IsPlayerLookingAtBear(link))
                    continue;

                encounter.PickUp(link);
                return true;
            }
            return false;
        }

        private void Update()
        {
            if (sourceSettings != null && Time.unscaledTime >= nextSettingsRefresh)
            {
                nextSettingsRefresh = Time.unscaledTime + 0.25f;
                ApplySettings(sourceSettings);
            }
            if (Time.timeScale <= 0f)
                return;

            switch (state)
            {
                case EncounterState.Perched:
                    if (Time.time - stateStartedAt >= perchedReengageDelay)
                        FindTargetsAndCheckTerritory();
                    break;
                case EncounterState.Leaping:
                    UpdateLeap();
                    break;
                case EncounterState.Attached:
                    UpdateAttached();
                    break;
                case EncounterState.AttachedToPlayer:
                    UpdateAttachedToPlayer();
                    break;
                case EncounterState.Held:
                    UpdateHeld();
                    break;
                case EncounterState.Thrown:
                    if (!hasLanded && Time.time - stateStartedAt > 0.2f &&
                        (bearBody.IsSleeping() || bearBody.linearVelocity.sqrMagnitude < 0.2f))
                        BeginStun();
                    else if (!hasLanded && Time.time - stateStartedAt > 4f)
                        BeginStun();
                    break;
                case EncounterState.Stunned:
                    if (Time.time - stateStartedAt >= landingStunSeconds)
                        FinishStun();
                    break;
                case EncounterState.PursuingPlayer:
                    UpdatePlayerPursuit();
                    break;
                case EncounterState.BreakingFree:
                    UpdateBreakFree();
                    break;
                case EncounterState.SprintingToTree:
                    UpdateSprintToTree();
                    break;
                case EncounterState.Climbing:
                    UpdateClimb();
                    break;
            }
        }

        private void FindTargetsAndCheckTerritory()
        {
            if (player == null)
            {
                WalkingMotor motor = FindAnyObjectByType<WalkingMotor>();
                if (motor == null)
                    return;
                player = motor.transform;
                playerCamera = motor.GetComponentInChildren<Camera>(true);
                playerHealth = motor.GetComponent<PlayerDamageController>() ??
                               motor.gameObject.AddComponent<PlayerDamageController>();
            }
            if (wagon == null)
            {
                WagonWorld world = FindAnyObjectByType<WagonWorld>();
                wagon = world != null ? world.Wagon : FindAnyObjectByType<WagonController>();
            }
            Vector3 flatPlayerOffset = Vector3.ProjectOnPlane(player.position - territoryCenter, Vector3.up);
            if (flatPlayerOffset.sqrMagnitude > territoryRadius * territoryRadius)
                return;

            bool wagonAvailable = wagon != null && wagon.Body != null &&
                                  Vector3.Distance(player.position, wagon.Body.position) <= maximumWagonRange;
            BeginLeap(!wagonAvailable || Random.value < dropOnPlayerChance);
        }

        private void BeginLeap(bool targetPlayer = false)
        {
            leapTarget = targetPlayer ? LeapTarget.Player : LeapTarget.Wagon;
            state = EncounterState.Leaping;
            stateStartedAt = Time.time;
            leapStart = bearBody.position;
            if (leapTarget == LeapTarget.Wagon)
                wagonAttachLocal = new Vector3(Random.Range(-0.48f, 0.48f), 0.82f, Random.Range(-0.55f, 0.5f));
            bearBody.isKinematic = true;
            bearCollider.enabled = false;
            PlayRandomSound(leapSounds, leapSoundVolume);
        }

        private void UpdateLeap()
        {
            if ((leapTarget == LeapTarget.Wagon && (wagon == null || wagon.Body == null)) ||
                (leapTarget == LeapTarget.Player && player == null))
            {
                Resolve();
                return;
            }
            float t = Mathf.Clamp01((Time.time - stateStartedAt) / leapDuration);
            Vector3 target = leapTarget == LeapTarget.Player
                ? player.position + Vector3.up * 0.9f
                : wagon.Body.transform.TransformPoint(wagonAttachLocal);
            bearBody.position = Vector3.Lerp(leapStart, target, t) + Vector3.up * (4f * t * (1f - t) * leapArcHeight);
            Vector3 direction = target - bearBody.position;
            if (direction.sqrMagnitude > 0.001f)
                bearBody.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            if (t >= 1f)
            {
                if (leapTarget == LeapTarget.Player)
                    LandOnPlayer();
                else
                    AttachToWagon();
            }
        }

        private void LandOnPlayer()
        {
            DamagePlayer(treeDropPlayerDamage);
            bool knockedDown = false;
            if (player != null && Random.value < treeDropKnockdownChance)
            {
                PlayerFallController fall = player.GetComponent<PlayerFallController>();
                if (fall != null && !fall.IsUncontrolled)
                {
                    Vector3 awayFromTree = Vector3.ProjectOnPlane(
                        player.position - territoryCenter, Vector3.up).normalized;
                    if (awayFromTree.sqrMagnitude < 0.01f)
                        awayFromTree = player.forward;
                    fall.ForceLoseControl(awayFromTree * pursuitKnockdownForce + Vector3.up * 0.8f);
                    knockedDown = true;
                }
            }
            if (knockedDown)
                BeginPlayerPursuit();
            else
                AttachToPlayer();
        }

        private void AttachToPlayer()
        {
            state = EncounterState.AttachedToPlayer;
            stateStartedAt = Time.time;
            nextAttackAt = Time.time + heldScratchDelay;
            charging = false;
            holder = null;
            bearBody.isKinematic = true;
            bearCollider.enabled = false;
            bearBody.transform.SetParent(null, true);
            UpdatePlayerAttachmentPose(true);
        }

        private void UpdateAttachedToPlayer()
        {
            if (player == null || playerHealth == null || playerHealth.IsDead)
            {
                BeginPlayerPursuit();
                return;
            }
            UpdatePlayerAttachmentPose(false);
            if (Time.time < nextAttackAt)
                return;
            DamagePlayer(heldScratchDamage);
            nextAttackAt = Time.time + heldScratchInterval;
        }

        private void UpdatePlayerAttachmentPose(bool snap)
        {
            Transform view = playerCamera != null ? playerCamera.transform : player;
            if (view == null)
                return;
            Vector3 target = view.position + view.forward * 0.78f + view.right * 0.36f - view.up * 0.08f;
            float follow = snap ? 1f : 1f - Mathf.Exp(-22f * Time.deltaTime);
            bearBody.position = Vector3.Lerp(bearBody.position, target, follow);
            float wriggle = Mathf.Sin(Time.time * 13f) * 11f;
            Quaternion facing = Quaternion.LookRotation(-view.forward, view.up) *
                                Quaternion.Euler(wriggle * 0.35f, 0f, wriggle);
            bearBody.rotation = Quaternion.Slerp(bearBody.rotation, facing, follow);
        }

        private void AttachToWagon()
        {
            state = EncounterState.Attached;
            stateStartedAt = Time.time;
            nextWagonDamageSoundAt = Time.time + spillGracePeriod;
            bearBody.isKinematic = true;
            bearBody.transform.SetParent(wagon.Body.transform, false);
            bearBody.transform.localPosition = wagonAttachLocal;
            bearBody.transform.localRotation = Quaternion.Euler(8f, 180f, -12f);
            StartGravelSpill();
        }

        private void UpdateAttached()
        {
            if (wagon == null || wagon.Body == null)
            {
                Resolve();
                return;
            }
            if (Time.time - stateStartedAt >= spillGracePeriod && wagon.gravel > 0f)
            {
                wagon.gravel = Mathf.Max(0f, wagon.gravel - gravelLostPerSecond * Time.deltaTime);
                if (Time.time >= nextWagonDamageSoundAt)
                {
                    PlayRandomSound(damageSounds, damageSoundVolume);
                    nextWagonDamageSoundAt = Time.time + Mathf.Max(0.1f, wagonDamageSoundInterval);
                }
            }
        }

        private void PickUp(WagonPlayerLink link)
        {
            if (link.IsHolding)
                link.Release();
            holder = link;
            player = link.transform;
            if (playerCamera == null)
                playerCamera = link.GetComponentInChildren<Camera>(true);
            if (gravelSpill != null)
                gravelSpill.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            state = EncounterState.Held;
            stateStartedAt = Time.time;
            nextAttackAt = Time.time + heldScratchDelay;
            charging = false;
            releasedPickupPress = false;
            bearBody.transform.SetParent(null, true);
            bearBody.isKinematic = true;
            bearCollider.enabled = false;
        }

        private void UpdateHeld()
        {
            if (holder == null || !holder.CanDrive)
            {
                Resolve();
                return;
            }
            Transform view = playerCamera != null ? playerCamera.transform : holder.transform;
            Vector3 holdPosition = view.position + view.forward * 1.05f - view.up * 0.32f;
            bearBody.position = Vector3.Lerp(bearBody.position, holdPosition, 18f * Time.deltaTime);
            bearBody.rotation = Quaternion.LookRotation(-view.forward, view.up);

            if (Time.time >= nextAttackAt)
            {
                DamagePlayer(heldScratchDamage);
                nextAttackAt = Time.time + heldScratchInterval;
                if (Random.value < escapeFromHandsChance)
                {
                    BeginSprintToTree(false, true);
                    return;
                }
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (!releasedPickupPress)
            {
                releasedPickupPress = !keyboard.eKey.isPressed;
                return;
            }
            if (!charging && keyboard.eKey.wasPressedThisFrame)
            {
                charging = true;
                chargeStartedAt = Time.time;
            }
            if (charging && keyboard.eKey.wasReleasedThisFrame)
                Throw(view.forward);
        }

        private void Throw(Vector3 aimDirection)
        {
            float charge = Charge01;
            lastThrowCharge = charge;
            charging = false;
            state = EncounterState.Thrown;
            stateStartedAt = Time.time;
            hasLanded = false;
            holder = null;
            bearBody.transform.SetParent(null, true);
            bearBody.isKinematic = false;
            bearCollider.enabled = true;
            Vector3 direction = aimDirection.sqrMagnitude > 0.01f ? aimDirection.normalized : transform.forward;
            bearBody.linearVelocity = direction * Mathf.Lerp(minimumThrowSpeed, maximumThrowSpeed, charge) + Vector3.up * throwLift;
            bearBody.angularVelocity = Random.onUnitSphere * Mathf.Lerp(4f, 12f, charge);
        }

        private void BeginStun()
        {
            if (state != EncounterState.Thrown)
                return;
            hasLanded = true;
            state = EncounterState.Stunned;
            stateStartedAt = Time.time;
            bearBody.linearVelocity *= 0.25f;
            bearBody.angularVelocity *= 0.25f;
        }

        private void FinishStun()
        {
            attackRounds++;
            if (attackRounds >= maximumAttackRounds)
            {
                BeginSprintToTree(true);
                return;
            }

            // A harder throw makes retreat more likely, but even a weak throw can
            // send it looking for another tree instead of immediately re-attacking.
            float retreatChance = Mathf.Clamp01(retreatAfterThrowChance + lastThrowCharge * 0.25f);
            if (Random.value < retreatChance)
                BeginSprintToTree(false);
            else
                BeginPlayerPursuit();
        }

        private void BeginPlayerPursuit()
        {
            holder = null;
            charging = false;
            state = EncounterState.PursuingPlayer;
            stateStartedAt = Time.time;
            nextAttackAt = Time.time + 0.35f;
            bearBody.transform.SetParent(null, true);
            bearBody.isKinematic = true;
            bearCollider.enabled = false;
        }

        private void UpdatePlayerPursuit()
        {
            if (player == null)
            {
                Resolve();
                return;
            }

            Vector3 flatOffset = Vector3.ProjectOnPlane(player.position - bearBody.position, Vector3.up);
            float distance = flatOffset.magnitude;
            if (distance > 0.05f)
            {
                Vector3 direction = flatOffset / distance;
                MoveBearAlongGround(direction, pursuitSpeed);
                bearBody.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }

            if (distance <= pursuitAttackRange && Time.time >= nextAttackAt)
            {
                DamagePlayer(pursuitAttackDamage);
                nextAttackAt = Time.time + pursuitAttackInterval;
                if (Random.value < pursuitKnockdownChance)
                {
                    PlayerFallController fall = player.GetComponent<PlayerFallController>();
                    if (fall != null && !fall.IsUncontrolled)
                    {
                        Vector3 knockback = flatOffset.sqrMagnitude > 0.01f
                            ? flatOffset.normalized
                            : player.forward;
                        fall.ForceLoseControl(knockback * pursuitKnockdownForce + Vector3.up * 1.2f);
                    }
                }
            }

            if (Time.time - stateStartedAt < pursuitDuration)
                return;

            if (wagon != null && wagon.Body != null &&
                Vector3.Distance(player.position, wagon.Body.position) <= maximumWagonRange)
                BeginLeap();
            else
                BeginSprintToTree(false);
        }

        private void BeginSprintToTree(bool resolveAtTop, bool brokeFromHands = false)
        {
            if (!resolveAtTop && (state == EncounterState.Held || state == EncounterState.PursuingPlayer))
                attackRounds++;
            holder = null;
            charging = false;
            resolveAfterClimb = resolveAtTop || attackRounds >= maximumAttackRounds;
            if (gravelSpill != null)
                gravelSpill.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            bearBody.transform.SetParent(null, true);
            bearBody.isKinematic = true;
            bearCollider.enabled = false;
            PlayRandomSound(runAwaySounds, runAwaySoundVolume);

            if (TryFindEscapeTree(out Vector3 treeBase, out Vector3 treePerch))
            {
                territoryCenter = treeBase;
                movementTarget = treeBase;
                perchPosition = treePerch;
            }
            else
            {
                movementTarget = territoryCenter;
            }

            if (brokeFromHands)
            {
                Transform view = playerCamera != null ? playerCamera.transform : player;
                Vector3 forward = view != null ? view.forward : bearBody.transform.forward;
                Vector3 right = view != null ? view.right : bearBody.transform.right;
                breakFreeStart = bearBody.position;
                breakFreeEnd = breakFreeStart + forward * breakFreeLeapDistance +
                               right * Random.Range(-0.45f, 0.45f) - Vector3.up * 0.35f;
                state = EncounterState.BreakingFree;
                escapeNoticeUntil = Time.time + 2.2f;
                SetEscapeTrailVisible(true);
            }
            else
            {
                state = EncounterState.SprintingToTree;
                SetEscapeTrailVisible(true);
            }
            stateStartedAt = Time.time;
        }

        private void UpdateBreakFree()
        {
            float t = Mathf.Clamp01((Time.time - stateStartedAt) / breakFreeDuration);
            bearBody.position = Vector3.Lerp(breakFreeStart, breakFreeEnd, t) +
                                Vector3.up * (4f * t * (1f - t) * 0.8f);
            bearBody.rotation = Quaternion.Euler(t * 220f, t * 360f, t * -160f);
            float pulse = 1f + Mathf.Sin(t * Mathf.PI) * 0.22f;
            bearVisual.localScale = Vector3.one * pulse;
            if (t < 1f)
                return;

            bearVisual.localScale = Vector3.one;
            state = EncounterState.SprintingToTree;
            stateStartedAt = Time.time;
        }

        private bool TryFindEscapeTree(out Vector3 treeBase, out Vector3 treePerch)
        {
            treeBase = territoryCenter;
            treePerch = perchPosition;
            float bestSqrDistance = float.PositiveInfinity;
            bool found = false;

            foreach (DropBearEncounter candidate in Active)
            {
                if (candidate == null || candidate == this || candidate.state == EncounterState.Resolved)
                    continue;
                Vector2 offset = new(candidate.territoryCenter.x - bearBody.position.x,
                    candidate.territoryCenter.z - bearBody.position.z);
                float sqrDistance = offset.sqrMagnitude;
                if (sqrDistance < minimumEscapeTreeDistance * minimumEscapeTreeDistance ||
                    sqrDistance > maximumEscapeTreeDistance * maximumEscapeTreeDistance ||
                    sqrDistance >= bestSqrDistance)
                    continue;
                treeBase = candidate.territoryCenter;
                treePerch = candidate.perchPosition;
                bestSqrDistance = sqrDistance;
                found = true;
            }

            if (DropBearTerrainTreeRegistry.TryFindNearest(bearBody.position,
                    minimumEscapeTreeDistance, maximumEscapeTreeDistance,
                    out DropBearTerrainTreeRegistry.TreeSite terrainTree))
            {
                Vector2 offset = new(terrainTree.Base.x - bearBody.position.x,
                    terrainTree.Base.z - bearBody.position.z);
                if (offset.sqrMagnitude < bestSqrDistance)
                {
                    treeBase = terrainTree.Base;
                    treePerch = terrainTree.Perch;
                    found = true;
                }
            }
            return found;
        }

        private void UpdateSprintToTree()
        {
            Vector3 flatOffset = Vector3.ProjectOnPlane(movementTarget - bearBody.position, Vector3.up);
            if (flatOffset.magnitude <= 0.8f)
            {
                climbStart = bearBody.position;
                SetEscapeTrailVisible(false);
                state = EncounterState.Climbing;
                stateStartedAt = Time.time;
                return;
            }
            Vector3 direction = flatOffset.normalized;
            MoveBearAlongGround(direction, sprintSpeed);
            bearBody.rotation = Quaternion.LookRotation(direction, Vector3.up);
            bearVisual.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 18f) * 9f);
        }

        private void UpdateClimb()
        {
            float t = Mathf.Clamp01((Time.time - stateStartedAt) / climbDuration);
            bearBody.position = Vector3.Lerp(climbStart, perchPosition, t);
            bearBody.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            if (t < 1f)
                return;
            bearVisual.localRotation = Quaternion.identity;
            if (resolveAfterClimb)
                Resolve();
            else
                SetPerchedPose();
        }

        private void MoveBearAlongGround(Vector3 direction, float speed)
        {
            Vector3 next = bearBody.position + direction * speed * Time.deltaTime;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;
                Vector3 local = next - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                    continue;
                next.y = terrain.SampleHeight(next) + terrain.transform.position.y + 0.48f;
                break;
            }
            bearBody.position = next;
        }

        private void SetEscapeTrailVisible(bool visible)
        {
            if (escapeTrail == null && visible)
            {
                escapeTrail = bearBody.gameObject.AddComponent<TrailRenderer>();
                escapeTrail.time = 0.65f;
                escapeTrail.minVertexDistance = 0.05f;
                escapeTrail.startWidth = 0.2f;
                escapeTrail.endWidth = 0f;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                escapeTrail.material = new Material(shader) { color = new Color(1f, 0.26f, 0.04f, 0.95f) };
                escapeTrail.startColor = new Color(1f, 0.72f, 0.1f, 1f);
                escapeTrail.endColor = new Color(1f, 0.05f, 0.01f, 0f);
                escapeTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                escapeTrail.receiveShadows = false;
            }
            if (escapeTrail == null)
                return;
            escapeTrail.emitting = visible;
            if (visible)
                escapeTrail.Clear();
        }

        private void DamagePlayer(float amount)
        {
            if (player == null || amount <= 0f)
                return;
            if (playerHealth == null)
                playerHealth = player.GetComponent<PlayerDamageController>() ??
                               player.gameObject.AddComponent<PlayerDamageController>();
            float healthBefore = playerHealth.CurrentHealth;
            playerHealth.TakeDamage(amount);
            if (playerHealth.CurrentHealth < healthBefore)
                PlayRandomSound(damageSounds, damageSoundVolume);
        }

        private void PlayRandomSound(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0 || bearAudio == null || volume <= 0f)
                return;

            int populatedCount = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    populatedCount++;
            }
            if (populatedCount == 0)
                return;

            int selected = Random.Range(0, populatedCount);
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null)
                    continue;
                if (selected-- != 0)
                    continue;
                bearAudio.PlayOneShot(clips[i], Mathf.Clamp01(volume));
                return;
            }
        }

        private void Resolve()
        {
            state = EncounterState.Resolved;
            Active.Remove(this);
            if (gravelSpill != null)
                gravelSpill.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (bearBody != null)
                Destroy(bearBody.gameObject, 0.25f);
            enabled = false;
        }

        private void BuildBearIfNeeded()
        {
            if (bearBody != null)
                return;

            GameObject root = new("Drop Bear");
            root.transform.SetParent(transform, false);
            bearBody = root.AddComponent<Rigidbody>();
            bearBody.mass = 8f;
            bearBody.isKinematic = true;
            bearBody.interpolation = RigidbodyInterpolation.Interpolate;
            bearBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            bearCollider = root.AddComponent<CapsuleCollider>();
            ((CapsuleCollider)bearCollider).radius = 0.34f;
            ((CapsuleCollider)bearCollider).height = 0.95f;
            bearAudio = root.AddComponent<AudioSource>();
            bearAudio.playOnAwake = false;
            bearAudio.spatialBlend = 1f;
            bearAudio.rolloffMode = AudioRolloffMode.Linear;
            bearAudio.minDistance = 2f;
            bearAudio.maxDistance = 28f;

            bearVisual = new GameObject("Koala visual").transform;
            bearVisual.SetParent(root.transform, false);
            Material fur = MakeMaterial("Drop bear grey", new Color(0.38f, 0.42f, 0.43f));
            Material dark = MakeMaterial("Drop bear nose", new Color(0.055f, 0.06f, 0.06f));
            Material eye = MakeMaterial("Drop bear eyes", new Color(0.72f, 0.12f, 0.08f));
            AddPart("Body", bearVisual, PrimitiveType.Sphere, new Vector3(0f, -0.12f, 0f), new Vector3(0.68f, 0.82f, 0.55f), fur);
            AddPart("Head", bearVisual, PrimitiveType.Sphere, new Vector3(0f, 0.35f, 0.03f), new Vector3(0.64f, 0.58f, 0.55f), fur);
            AddPart("Left ear", bearVisual, PrimitiveType.Sphere, new Vector3(-0.31f, 0.53f, 0.02f), Vector3.one * 0.31f, fur);
            AddPart("Right ear", bearVisual, PrimitiveType.Sphere, new Vector3(0.31f, 0.53f, 0.02f), Vector3.one * 0.31f, fur);
            AddPart("Nose", bearVisual, PrimitiveType.Sphere, new Vector3(0f, 0.32f, 0.31f), new Vector3(0.2f, 0.25f, 0.16f), dark);
            AddPart("Left eye", bearVisual, PrimitiveType.Sphere, new Vector3(-0.15f, 0.46f, 0.29f), Vector3.one * 0.075f, eye);
            AddPart("Right eye", bearVisual, PrimitiveType.Sphere, new Vector3(0.15f, 0.46f, 0.29f), Vector3.one * 0.075f, eye);
            AddPart("Left arm", bearVisual, PrimitiveType.Capsule, new Vector3(-0.3f, -0.04f, 0.07f), new Vector3(0.19f, 0.58f, 0.19f), fur, Quaternion.Euler(0f, 0f, -28f));
            AddPart("Right arm", bearVisual, PrimitiveType.Capsule, new Vector3(0.3f, -0.04f, 0.07f), new Vector3(0.19f, 0.58f, 0.19f), fur, Quaternion.Euler(0f, 0f, 28f));
        }

        private void SetPerchedPose()
        {
            state = EncounterState.Perched;
            stateStartedAt = Time.time;
            bearBody.transform.SetParent(transform, true);
            bearBody.position = perchPosition;
            bearBody.rotation = Quaternion.Euler(8f, Random.Range(0f, 360f), 0f);
            bearBody.isKinematic = true;
            bearCollider.enabled = false;
        }

        private void StartGravelSpill()
        {
            if (gravelSpill == null)
            {
                GameObject particles = new("Drop Bear Gravel Spill");
                particles.transform.SetParent(wagon.Body.transform, false);
                particles.transform.localPosition = wagonAttachLocal + new Vector3(0f, 0.15f, -0.35f);
                gravelSpill = particles.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = gravelSpill.main;
                main.startLifetime = 1.1f;
                main.startSpeed = 1.8f;
                main.startSize = 0.055f;
                main.startColor = new Color(0.36f, 0.32f, 0.25f);
                main.gravityModifier = 1.2f;
                main.maxParticles = 80;
                ParticleSystem.EmissionModule emission = gravelSpill.emission;
                emission.rateOverTime = 28f;
                ParticleSystem.ShapeModule shape = gravelSpill.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 20f;
                shape.rotation = new Vector3(90f, 0f, 0f);
            }
            gravelSpill.transform.localPosition = wagonAttachLocal + new Vector3(0f, 0.15f, -0.35f);
            gravelSpill.Play();
        }

        private bool IsPlayerLookingAtBear(WagonPlayerLink link)
        {
            Camera view = link.GetComponentInChildren<Camera>(true);
            if (view == null)
                view = Camera.main;
            if (view == null || bearBody == null)
                return false;

            Vector3 toBear = bearBody.worldCenterOfMass - view.transform.position;
            if (toBear.sqrMagnitude < 0.001f)
                return true;
            return Vector3.Angle(view.transform.forward, toBear) <= pickupLookAngle;
        }

        private static Material MakeMaterial(string label, Color colour)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            return new Material(shader) { name = label, color = colour };
        }

        private static void AddPart(string label, Transform parent, PrimitiveType primitive,
            Vector3 localPosition, Vector3 localScale, Material material, Quaternion rotation = default)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = label;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = rotation == default ? Quaternion.identity : rotation;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
        }

        private void OnGUI()
        {
            if (Time.time < escapeNoticeUntil)
            {
                Color oldColour = GUI.color;
                GUI.color = new Color(1f, 0.42f, 0.24f, 1f);
                GUI.Box(new Rect(Screen.width * 0.5f - 285f, 92f, 570f, 58f),
                    "THE DROP BEAR BROKE FREE!\nFollow the orange trail to the next tree");
                GUI.color = oldColour;
            }
            if (state == EncounterState.Attached && wagon != null && player != null &&
                Vector3.Distance(player.position, wagon.Body.position) <= 8f)
            {
                WagonPlayerLink link = player.GetComponent<WagonPlayerLink>();
                bool closeEnough = Vector3.Distance(player.position, wagon.Body.position) <= pickupRange;
                string prompt = !closeEnough
                    ? "DROP BEAR!  Get to the wagon before it spills the gravel"
                    : link != null && IsPlayerLookingAtBear(link)
                        ? "DROP BEAR!  Tap E to pull it off the wagon"
                        : "Look at the drop bear to remove it";
                GUI.Box(new Rect(Screen.width * 0.5f - 255f, 28f, 510f, 58f),
                    $"{prompt}\nGravel remaining: {wagon.gravel:0.0}");
            }
            else if (state == EncounterState.Held)
            {
                string prompt = releasedPickupPress
                    ? "Aim, hold E to charge, then release — throw it before it scratches free!"
                    : "Drop bear removed! Release E, then hold it again to throw";
                GUI.Box(new Rect(Screen.width * 0.5f - 255f, 28f, 510f, 58f), prompt);
                if (charging)
                {
                    Rect background = new(Screen.width * 0.5f - 150f, 68f, 300f, 16f);
                    GUI.Box(background, GUIContent.none);
                    GUI.Box(new Rect(background.x + 2f, background.y + 2f,
                        (background.width - 4f) * Charge01, background.height - 4f), GUIContent.none);
                }
            }
            else if (state == EncounterState.AttachedToPlayer)
            {
                Color oldColour = GUI.color;
                GUI.color = new Color(1f, 0.55f, 0.38f, 1f);
                GUI.Box(new Rect(Screen.width * 0.5f - 255f, 28f, 510f, 58f),
                    "DROP BEAR ON YOU! Tap E to grab it\nThen release E, hold it again, and release to throw");
                GUI.color = oldColour;
            }
            else if (state == EncounterState.PursuingPlayer)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 255f, 28f, 510f, 42f),
                    "DROP BEAR ATTACK! Keep moving — its lunge can knock you over");
            }
            else if (state == EncounterState.SprintingToTree || state == EncounterState.Climbing)
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 255f, 28f, 510f, 42f),
                    resolveAfterClimb
                        ? "The drop bear is retreating!"
                        : "The drop bear is escaping to another tree — it may attack again");
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.2f, 0.1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, territoryRadius);
        }
    }

}
