using UnityEngine;

namespace GetLost.Wagon
{
    /// <summary>
    /// Independent rigid bodies: chassis, steerable/rocking front axle and four freely
    /// rolling wheels. The player supplies a capped drawbar force, never a position teleport.
    /// </summary>
    public sealed class WagonController : MonoBehaviour
    {
        [Header("Load")]
        [Min(1f)] public float capacity = 120f;
        [Min(0f)] public float gravel = 120f;
        [Min(1f)] public float emptyMass = 65f;
        [Min(0f)] public float kilogramsPerUnit = 0.6f;
        [Header("Pulling")]
        public float pullSpring = 4400f;
        public float pullDamping = 220f;
        public float maximumPullForce = 2400f;
        public float handleSlack = 0.38f;
        public float gripReach = 0.75f;
        [Tooltip("Player walking-speed multiplier while pulling an empty wagon.")]
        [Range(0.1f, 1f)] public float emptyPullSpeedMultiplier = 0.98f;
        [Tooltip("Player walking-speed multiplier while pulling a fully loaded wagon. Physical mass is unaffected.")]
        [Range(0.1f, 1f)] public float loadedPullSpeedMultiplier = 0.82f;
        [Tooltip("Continuous force applied when the walking player shoulders into a wagon part.")]
        [Min(0f)] public float playerBodyPushForce = 5200f;
        [Tooltip("Raises the effective shoulder contact to give side pushes realistic roll leverage.")]
        [Min(0f)] public float playerPushLeverHeight = 0.55f;

        [Header("Riding")]
        [Tooltip("Player root position on the wagon bed, in chassis-local space.")]
        public Vector3 riderLocalPosition = new(0f, 0.12f, -0.42f);
        [Min(0.05f)] public float rideMountDuration = 0.38f;
        [Min(0f)] public float rideMountJumpHeight = 0.55f;
        [Min(0.05f)] public float rideReturnDuration = 0.42f;
        [Min(0f)] public float rideReturnHopHeight = 0.12f;
        [Min(0f)] public float riderWeightShiftDistance = 0.38f;
        [Min(0f)] public float riderMass = 75f;
        [Tooltip("Yaw acceleration applied to the front axle from left/right weight shift.")]
        [Min(0f)] public float rideSteeringAcceleration = 16f;
        [Tooltip("Braking force applied while riding with Space released.")]
        [Min(0f)] public float rideBrakeForce = 850f;
        [Tooltip("Fraction of the wagon width crossed per second while holding A or D.")]
        [Min(0.01f)] public float rideWeightShiftRate = 0.55f;
        [Tooltip("Fraction of the wagon width returned toward centre per second when A and D are released.")]
        [Min(0f)] public float rideWeightReturnRate = 0.16f;
        [Min(0f)] public float rideCameraShift = 0.075f;
        [Min(0f)] public float rideCameraTilt = 4.5f;
        [Min(0f)] public float rideDismountSideDistance = 1.45f;

        [Header("Wheel Damage & Repair")]
        [Tooltip("Normalised tightness lost per minute. Each wheel can be tuned independently.")]
        [Min(0f)] public float frontLeftWheelLoosenPerMinute = 0.035f;
        [Min(0f)] public float frontRightWheelLoosenPerMinute = 0.042f;
        [Min(0f)] public float rearLeftWheelLoosenPerMinute = 0.028f;
        [Min(0f)] public float rearRightWheelLoosenPerMinute = 0.032f;
        [Min(0.1f)] public float wheelRepairDistance = 1.65f;
        [Range(5f, 45f)] public float wheelRepairLookAngle = 18f;
        [Min(0.01f)] public float wheelRepairPerSecond = 0.42f;
        [Min(0.25f)] public float wheelPlacementDistance = 1.8f;
        public Vector3 carriedWheelViewOffset = new(0.38f, -0.42f, 0.85f);
        [Min(0f)] public float wheelDamageImpactSpeed = 3.5f;
        [Min(0f)] public float wheelDamagePerImpactSpeed = 0.11f;
        [Header("Crash Ejection")]
        [Min(0f)] public float riderEjectImpactSpeed = 7f;
        [Min(0f)] public float riderEjectUpwardSpeed = 2.4f;
        [Min(0f)] public float riderEjectForwardSpeed = 1.5f;
        [Min(0f)] public float riderEjectCooldown = 0.75f;
        [Header("Automatic Spawn")]
        [Min(0f)] public float preferredSpawnDistance = 5f;
        [Min(0f)] public float fallbackSpawnDistance = 8f;
        [Range(0f, 89f)] public float maximumSpawnSlope = 30f;
        [Min(0f)] public float spawnGroundClearance = 0.08f;

        public Rigidbody Body { get; private set; }
        public Rigidbody FrontAxle { get; private set; }
        public Transform Grip { get; private set; }
        public Transform RearGrip { get; private set; }
        public Transform ActiveGrip { get; private set; }
        public bool IsRearGripActive { get; private set; }
        public Transform Dispenser { get; private set; }
        public Transform RiderMount { get; private set; }
        public bool IsHeld => holder != null;
        public bool CanDeposit => holder != null && holder.CanDrive;
        public bool GateOpen { get; private set; }
        public bool HasBeenUsed { get; private set; }
        public float Resistance { get; private set; }
        public Rigidbody[] Bodies { get; private set; }
        public Collider[] Colliders { get; private set; }
        public float LoadFraction => capacity > 0f ? Mathf.Clamp01(gravel / capacity) : 0f;
        public WagonWheel ActiveRepairWheel { get; private set; }
        public WagonWheel LookedAtRepairWheel { get; private set; }
        public WagonWheel LookedAtLooseWheel { get; private set; }
        public WagonWheel CarriedWheel { get; private set; }
        public Transform LinkedPlayer => playerLink != null ? playerLink.transform : null;
        public bool PlayerCanRepair => playerLink != null && playerLink.CanDrive && !playerLink.IsHolding;

        private WagonPlayerLink holder;
        private WagonPlayerLink playerLink;
        private Rigidbody[] wheels;
        private WagonWheel[] wheelSystems;
        private Transform loadVisual;
        private Transform gateVisual;
        private Material wood, metal, gravelMaterial;
        private PhysicsMaterial wheelMaterial;
        private PhysicsMaterial frameMaterial;
        private float nextRiderEjectTime;
        private bool wheelRepairInputHeld;

        private void Awake()
        {
            if (Application.isPlaying)
                EnsureBuilt();
        }

        public static WagonController Create(Vector3 groundPosition, Quaternion rotation,
            WagonController prefab = null)
        {
            WagonController wagon;
            if (prefab != null)
                wagon = Instantiate(prefab, groundPosition, rotation);
            else
            {
                var root = new GameObject("Wagon Prototype");
                root.transform.SetPositionAndRotation(groundPosition, rotation);
                wagon = root.AddComponent<WagonController>();
            }
            wagon.EnsureBuilt();
            return wagon;
        }

        public void EnsureBuilt()
        {
            if (Body == null)
                Build();
        }

        private void Build()
        {
            wood = MakeMaterial("Wagon wood", new Color(0.32f, 0.19f, 0.09f));
            metal = MakeMaterial("Wagon iron", new Color(0.12f, 0.14f, 0.15f));
            gravelMaterial = MakeMaterial("Wagon gravel", new Color(0.43f, 0.42f, 0.38f));
            wheelMaterial = new PhysicsMaterial("Wagon wheel grip")
            {
                dynamicFriction = 0.65f, staticFriction = 0.8f,
                frictionCombine = PhysicsMaterialCombine.Average, bounciness = 0f
            };
            frameMaterial = new PhysicsMaterial("Wagon chassis")
            {
                dynamicFriction = 0.45f, staticFriction = 0.55f, bounciness = 0f
            };
            Body = MakeBody("Chassis", new Vector3(0f, 0.78f, 0f), emptyMass);
            Body.centerOfMass = new Vector3(0f, -0.15f, 0f);
            Box("Bed", Body.transform, Vector3.zero, new Vector3(1.45f, 0.18f, 2.35f), wood);
            Box("Left side", Body.transform, new Vector3(-0.72f, 0.36f, 0f),
                new Vector3(0.09f, 0.65f, 2.35f), wood);
            Box("Right side", Body.transform, new Vector3(0.72f, 0.36f, 0f),
                new Vector3(0.09f, 0.65f, 2.35f), wood);
            Box("Front wall", Body.transform, new Vector3(0f, 0.36f, 1.12f),
                new Vector3(1.4f, 0.65f, 0.09f), wood);
            gateVisual = Box("Rear gravel gate", Body.transform, new Vector3(0f, 0.36f, -1.12f),
                new Vector3(1.4f, 0.65f, 0.09f), wood).transform;
            loadVisual = Box("Gravel load", Body.transform, new Vector3(0f, 0.18f, 0f),
                new Vector3(1.28f, 0.22f, 2.1f), gravelMaterial, false).transform;

            FrontAxle = MakeBody("Pivoting front axle", new Vector3(0f, 0.5f, 0.87f), 12f);
            Box("Front axle beam", FrontAxle.transform, Vector3.zero,
                new Vector3(1.85f, 0.12f, 0.12f), metal);
            var pivot = FrontAxle.gameObject.AddComponent<ConfigurableJoint>();
            pivot.connectedBody = Body;
            pivot.autoConfigureConnectedAnchor = false;
            pivot.anchor = Vector3.zero;
            pivot.connectedAnchor = Body.transform.InverseTransformPoint(FrontAxle.position);
            pivot.xMotion = pivot.yMotion = pivot.zMotion = ConfigurableJointMotion.Locked;
            pivot.angularXMotion = ConfigurableJointMotion.Locked;
            pivot.angularYMotion = pivot.angularZMotion = ConfigurableJointMotion.Limited;
            pivot.angularYLimit = new SoftJointLimit { limit = 48f };
            pivot.angularZLimit = new SoftJointLimit { limit = 16f };
            pivot.projectionMode = JointProjectionMode.PositionAndRotation;
            pivot.projectionDistance = 0.03f;
            pivot.enableCollision = false;
            for (int side = -1; side <= 1; side += 2)
                Box("Pulling bar", FrontAxle.transform, new Vector3(side * 0.51f, 0.38f, 1.1f),
                    new Vector3(0.08f, 0.09f, 2.6f), wood);
            Grip = new GameObject("Handle grip").transform;
            Grip.SetParent(FrontAxle.transform, false);
            Grip.localPosition = new Vector3(0f, 0.38f, 2.25f);
            RearGrip = new GameObject("Rear handle grip").transform;
            RearGrip.SetParent(Body.transform, false);
            RearGrip.localPosition = new Vector3(0f, 0.48f, -1.42f);
            ActiveGrip = Grip;
            Dispenser = new GameObject("Gravel outlet").transform;
            Dispenser.SetParent(Body.transform, false);
            Dispenser.localPosition = new Vector3(0f, -0.12f, -1.35f);
            RiderMount = new GameObject("Rider Mount").transform;
            RiderMount.SetParent(Body.transform, false);
            RiderMount.localPosition = riderLocalPosition;
            wheels = new Rigidbody[4];
            wheels[0] = Wheel("Front left", FrontAxle, new Vector3(-0.94f, 0f, 0f),
                -1f, frontLeftWheelLoosenPerMinute);
            wheels[1] = Wheel("Front right", FrontAxle, new Vector3(0.94f, 0f, 0f),
                1f, frontRightWheelLoosenPerMinute);
            wheels[2] = Wheel("Rear left", Body, new Vector3(-0.94f, -0.28f, -0.87f),
                -1f, rearLeftWheelLoosenPerMinute);
            wheels[3] = Wheel("Rear right", Body, new Vector3(0.94f, -0.28f, -0.87f),
                1f, rearRightWheelLoosenPerMinute);
            Box("Rear axle beam", Body.transform, new Vector3(0f, -0.28f, -0.87f),
                new Vector3(1.85f, 0.12f, 0.12f), metal);
            Bodies = GetComponentsInChildren<Rigidbody>();
            Colliders = GetComponentsInChildren<Collider>();
            wheelSystems = GetComponentsInChildren<WagonWheel>();
            // Internal collisions are handled by joint constraints, not overlapping assembly parts.
            for (int i = 0; i < Colliders.Length; i++)
                for (int j = i + 1; j < Colliders.Length; j++)
                    Physics.IgnoreCollision(Colliders[i], Colliders[j]);
        }

        private Rigidbody MakeBody(string label, Vector3 localPosition, float mass)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.solverIterations = 12;
            body.solverVelocityIterations = 8;
            body.maxAngularVelocity = 35f;
            WagonImpactRelay relay = go.AddComponent<WagonImpactRelay>();
            relay.Initialize(this, body);
            return body;
        }

        private Rigidbody Wheel(string label, Rigidbody axle, Vector3 anchor,
            float outerSide, float loosenPerMinute)
        {
            Vector3 local = transform.InverseTransformPoint(axle.transform.TransformPoint(anchor));
            Rigidbody wheel = MakeBody(label, local, 5f);
            // A convex cylinder gives a real rolling contact and finite wheel width.
            var shape = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shape.name = "Wheel";
            shape.transform.SetParent(wheel.transform, false);
            shape.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            shape.transform.localScale = new Vector3(0.92f, 0.11f, 0.92f);
            Collider primitive = shape.GetComponent<Collider>();
            primitive.enabled = false;
            Destroy(primitive);
            var collider = shape.AddComponent<MeshCollider>();
            collider.sharedMesh = shape.GetComponent<MeshFilter>().sharedMesh;
            collider.convex = true;
            collider.sharedMaterial = wheelMaterial;
            shape.GetComponent<Renderer>().sharedMaterial = metal;
            var hinge = wheel.gameObject.AddComponent<HingeJoint>();
            hinge.connectedBody = axle;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.anchor = Vector3.zero;
            hinge.connectedAnchor = anchor;
            hinge.axis = Vector3.right;
            hinge.enableCollision = false;
            WagonWheel damage = wheel.gameObject.AddComponent<WagonWheel>();
            damage.Initialize(this, label, wheel, hinge, axle, anchor, outerSide,
                loosenPerMinute, metal);
            return wheel;
        }

        private GameObject Box(string label, Transform parent, Vector3 localPosition,
            Vector3 size, Material material, bool collision = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            collider.sharedMaterial = frameMaterial;
            if (!collision) { collider.enabled = false; Destroy(collider); }
            return go;
        }

        private static Material MakeMaterial(string label, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            return new Material(shader) { name = label, color = color };
        }

        public void SetHolder(WagonPlayerLink value, bool useRearGrip = false)
        {
            holder = value;
            IsRearGripActive = holder != null && useRearGrip;
            ActiveGrip = IsRearGripActive ? RearGrip : Grip;
            if (holder != null)
                HasBeenUsed = true;
            else
                GateOpen = false;
        }

        public Transform GetClosestGrip(Vector3 playerPosition, out bool rear)
        {
            float frontDistance = Vector3.Distance(playerPosition, Grip.position);
            float rearDistance = Vector3.Distance(playerPosition, RearGrip.position);
            rear = rearDistance < frontDistance;
            return rear ? RearGrip : Grip;
        }

        public Vector3 GetWalkingPositionAtActiveGrip()
        {
            float side = IsRearGripActive ? -1f : 1f;
            return ActiveGrip.position + Body.transform.forward *
                   (side * handleSlack * 0.55f);
        }

        public bool ContainsBody(Rigidbody body)
        {
            if (body == null || Bodies == null)
                return false;
            foreach (Rigidbody part in Bodies)
                if (part == body)
                    return true;
            return false;
        }

        public void ToggleGate() => GateOpen = !GateOpen;
        public void CloseGate() => GateOpen = false;
        public void Refill() => gravel = capacity;
        public void RestoreUsage(bool used) => HasBeenUsed = used;

        private void FixedUpdate()
        {
            if (Body == null) return;
            gravel = Mathf.Clamp(gravel, 0f, capacity);
            Body.mass = emptyMass + gravel * kilogramsPerUnit;
            bool parked = holder == null || !holder.CanDrive;
            foreach (Rigidbody wheel in wheels)
            {
                var joint = wheel.GetComponent<HingeJoint>();
                if (joint == null)
                    continue;
                joint.useMotor = parked;
                if (parked)
                    joint.motor = new JointMotor { targetVelocity = 0f, force = 1000f, freeSpin = false };
            }
            Resistance = 0f;
            if (parked) return;

            if (holder.IsRiding || holder.IsReturningToWalk)
            {
                ApplyRidingForces();
                return;
            }

            Transform pullGrip = ActiveGrip != null ? ActiveGrip : Grip;
            Rigidbody pullBody = IsRearGripActive ? Body : FrontAxle;
            Vector3 error = Vector3.ProjectOnPlane(holder.transform.position - pullGrip.position, Vector3.up);
            Vector3 gripVelocity = Vector3.ProjectOnPlane(
                pullBody.GetPointVelocity(pullGrip.position), Vector3.up);
            Vector3 holderVelocity = Vector3.ProjectOnPlane(holder.PullVelocity, Vector3.up);
            // Dampen movement relative to the player rather than braking the wagon's
            // absolute world speed. This preserves weight without imposing a low
            // artificial terminal velocity while pulling.
            Vector3 force = error * pullSpring + (holderVelocity - gripVelocity) * pullDamping;
            force = Vector3.ClampMagnitude(force, maximumPullForce);
            Resistance = Mathf.Clamp01(force.magnitude / maximumPullForce);
            pullBody.AddForceAtPosition(force, pullGrip.position);
            if (!IsRearGripActive)
            {
                // Dampen steering oscillation without forcing the front axle to a chosen pose.
                float yawRate = Vector3.Dot(
                    FrontAxle.angularVelocity - Body.angularVelocity, Body.transform.up);
                FrontAxle.AddTorque(-Body.transform.up * yawRate * 45f);
            }
        }

        public void RegisterPlayer(WagonPlayerLink value)
        {
            playerLink = value;
        }

        internal void SetActiveRepairWheel(WagonWheel wheel, bool active)
        {
            if (active)
                ActiveRepairWheel = wheel;
            else if (ActiveRepairWheel == wheel)
                ActiveRepairWheel = null;
        }

        internal bool CanRepairWheel(WagonWheel candidate)
        {
            return PlayerCanRepair && wheelRepairInputHeld &&
                   candidate != null && candidate == LookedAtRepairWheel;
        }

        public void UpdateWheelRepairTarget(Transform view, bool repairHeld)
        {
            wheelRepairInputHeld = false;
            LookedAtRepairWheel = null;
            LookedAtLooseWheel = null;
            if (!PlayerCanRepair || view == null || wheelSystems == null)
                return;

            if (CarriedWheel != null)
                return;

            WagonWheel best = null;
            float bestAngle = wheelRepairLookAngle;
            foreach (WagonWheel wheel in wheelSystems)
            {
                if (wheel == null || (!wheel.CanBePickedUp && !wheel.CanBeTightened) ||
                    Vector3.Distance(LinkedPlayer.position, wheel.transform.position) > wheelRepairDistance)
                    continue;

                Vector3 toWheel = wheel.transform.position - view.position;
                float angle = Vector3.Angle(view.forward, toWheel);
                if (angle <= bestAngle && HasClearRepairView(view.position, wheel, toWheel))
                {
                    best = wheel;
                    bestAngle = angle;
                }
            }

            if (best != null && best.CanBePickedUp)
                LookedAtLooseWheel = best;
            else
                LookedAtRepairWheel = best;
            wheelRepairInputHeld = LookedAtRepairWheel != null && repairHeld;
        }

        public bool HandleWheelInteract(Transform view)
        {
            if (!PlayerCanRepair || view == null)
                return false;

            if (CarriedWheel != null)
            {
                if (CarriedWheel.IsPlayerNearHub(LinkedPlayer.position, wheelPlacementDistance))
                {
                    CarriedWheel.PositionAtHub();
                    CarriedWheel = null;
                }
                return true;
            }

            if (LookedAtLooseWheel == null)
                return false;

            CarriedWheel = LookedAtLooseWheel;
            CarriedWheel.PickUp(view, carriedWheelViewOffset);
            LookedAtLooseWheel = null;
            return true;
        }

        public bool CanPlaceCarriedWheel()
        {
            return CarriedWheel != null && LinkedPlayer != null &&
                   CarriedWheel.IsPlayerNearHub(LinkedPlayer.position, wheelPlacementDistance);
        }

        private bool HasClearRepairView(Vector3 origin, WagonWheel wheel, Vector3 toWheel)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                origin, toWheel.normalized, toWheel.magnitude + 0.35f,
                ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (LinkedPlayer != null && hit.collider.transform.IsChildOf(LinkedPlayer))
                    continue;
                return hit.collider.transform.IsChildOf(wheel.transform);
            }
            return false;
        }

        internal void ReportImpact(Collision collision, Rigidbody struckBody)
        {
            if (collision == null || holder == null || !holder.IsRiding ||
                Time.time < nextRiderEjectTime)
                return;

            float impactSpeed = WagonWheel.GetNormalImpactSpeed(collision);
            if (impactSpeed < riderEjectImpactSpeed)
                return;

            nextRiderEjectTime = Time.time + riderEjectCooldown;
            Vector3 travel = Body != null ? Body.linearVelocity : struckBody.linearVelocity;
            Vector3 forward = travel.sqrMagnitude > 0.25f
                ? Vector3.ProjectOnPlane(travel, Vector3.up).normalized
                : Body.transform.forward;
            Vector3 throwVelocity = travel + Vector3.up * riderEjectUpwardSpeed +
                                    forward * riderEjectForwardSpeed;
            holder.ThrowFromWagon(throwVelocity);
        }

        private void ApplyRidingForces()
        {
            float steer = holder.RideSteeringInput;
            Vector3 up = Body.transform.up;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(Body.linearVelocity, up);
            float speedWeight = Mathf.InverseLerp(0.25f, 3.5f, planarVelocity.magnitude);

            FrontAxle.AddTorque(
                up * steer * rideSteeringAcceleration * speedWeight,
                ForceMode.Acceleration);

            Vector3 shiftedWeightPoint = RiderMount.position +
                                         Body.transform.right * steer * riderWeightShiftDistance;
            Body.AddForceAtPosition(Physics.gravity * riderMass, shiftedWeightPoint, ForceMode.Force);

            if (holder.FeetBraking && planarVelocity.sqrMagnitude > 0.0001f)
            {
                Vector3 brake = -planarVelocity.normalized * rideBrakeForce;
                Body.AddForceAtPosition(brake, RiderMount.position, ForceMode.Force);
                Resistance = Mathf.Clamp01(brake.magnitude / Mathf.Max(1f, rideBrakeForce));
            }

            float yawRate = Vector3.Dot(FrontAxle.angularVelocity - Body.angularVelocity, up);
            FrontAxle.AddTorque(-up * yawRate * 30f);
        }

        public Vector3 GetRiderWorldPosition(float weightShift)
        {
            if (RiderMount == null)
                return Body != null ? Body.position : transform.position;

            return RiderMount.position +
                   Body.transform.right * Mathf.Clamp(weightShift, -1f, 1f) *
                   riderWeightShiftDistance;
        }

        public Vector3 GetDismountPosition()
        {
            if (Body == null)
                return transform.position;

            return Body.position - Body.transform.right * rideDismountSideDistance + Vector3.up * 0.25f;
        }

        private void LateUpdate()
        {
            if (CarriedWheel != null && (playerLink == null || !playerLink.CanDrive))
            {
                CarriedWheel.DropFromCarry();
                CarriedWheel = null;
            }
            if (loadVisual == null) return;
            float height = Mathf.Lerp(0.015f, 0.46f, LoadFraction);
            loadVisual.localScale = new Vector3(1.28f, height, 2.1f);
            loadVisual.localPosition = new Vector3(0f, 0.1f + height * 0.5f, 0f);
            gateVisual.localPosition = new Vector3(0f, GateOpen ? 0.59f : 0.36f, -1.12f);
        }

        public void PlaceBody(Vector3 position, Quaternion rotation)
        {
            holder?.Release();
            Quaternion bodyRotation = NormalizeSafe(Body.rotation, Quaternion.identity);
            rotation = NormalizeSafe(rotation, bodyRotation);
            Quaternion change = NormalizeSafe(
                rotation * Quaternion.Inverse(bodyRotation),
                Quaternion.identity);
            Vector3 oldPosition = Body.position;
            foreach (Rigidbody part in Bodies)
            {
                if (!part)
                    continue;
                part.position = position + change * (part.position - oldPosition);
                Quaternion partRotation = NormalizeSafe(part.rotation, bodyRotation);
                part.rotation = NormalizeSafe(change * partRotation, rotation);
                part.linearVelocity = Vector3.zero;
                part.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        private static Quaternion NormalizeSafe(Quaternion value, Quaternion fallback)
        {
            float sqrMagnitude = value.x * value.x + value.y * value.y +
                                 value.z * value.z + value.w * value.w;
            if (float.IsNaN(sqrMagnitude) || float.IsInfinity(sqrMagnitude) ||
                sqrMagnitude < 0.000001f)
            {
                value = fallback;
                sqrMagnitude = value.x * value.x + value.y * value.y +
                               value.z * value.z + value.w * value.w;
            }
            if (float.IsNaN(sqrMagnitude) || float.IsInfinity(sqrMagnitude) ||
                sqrMagnitude < 0.000001f)
                return Quaternion.identity;
            float inverseMagnitude = 1f / Mathf.Sqrt(sqrMagnitude);
            return new Quaternion(
                value.x * inverseMagnitude,
                value.y * inverseMagnitude,
                value.z * inverseMagnitude,
                value.w * inverseMagnitude);
        }

        private void OnDestroy()
        {
            holder?.Release();
            Destroy(wood); Destroy(metal); Destroy(gravelMaterial);
            Destroy(wheelMaterial); Destroy(frameMaterial);
        }
    }

    /// <summary>Runtime wheel wear, collision damage, visible nuts, and proximity repair.</summary>
    public sealed class WagonWheel : MonoBehaviour
    {
        [Range(0f, 1f)] public float tightness = 1f;
        [Min(0f)] public float loosenPerMinute;

        public string Label { get; private set; }
        public float Tightness => tightness;
        public bool IsDetached { get; private set; }
        public bool IsCarried { get; private set; }
        public bool IsPositioned { get; private set; }
        public bool IsRepairing { get; private set; }
        public bool CanBePickedUp => IsDetached && !IsCarried && !IsPositioned;
        public bool CanBeTightened => tightness < 0.999f && (!IsDetached || IsPositioned);

        private WagonController owner;
        private Rigidbody wheelBody;
        private HingeJoint wheelJoint;
        private Rigidbody connectedBody;
        private Vector3 connectedAnchor;
        private Quaternion connectedLocalRotation;
        private float outerSide;
        private Transform[] nuts;
        private GameObject wrenchRoot;
        private float wrenchAngle;
        private Transform carryView;
        private Vector3 carryOffset;

        public void Initialize(WagonController wagon, string label, Rigidbody body,
            HingeJoint joint, Rigidbody axle, Vector3 anchor, float side,
            float passiveLoosenPerMinute, Material metal)
        {
            owner = wagon;
            Label = label;
            wheelBody = body;
            wheelJoint = joint;
            connectedBody = axle;
            connectedAnchor = anchor;
            connectedLocalRotation = Quaternion.Inverse(axle.rotation) * body.rotation;
            outerSide = Mathf.Sign(side);
            loosenPerMinute = passiveLoosenPerMinute;
            CreateNuts(metal);
            CreateWrench(metal);
            UpdateRepairVisuals();
        }

        private void FixedUpdate()
        {
            if (owner == null || wheelBody == null)
                return;

            if (IsCarried)
            {
                FollowCarryTarget();
                return;
            }

            bool shouldRepair = owner.CanRepairWheel(this);
            if (shouldRepair != IsRepairing)
            {
                IsRepairing = shouldRepair;
                owner.SetActiveRepairWheel(this, shouldRepair);
                if (wrenchRoot != null)
                    wrenchRoot.SetActive(shouldRepair);
            }

            if (IsRepairing)
            {
                tightness = Mathf.MoveTowards(
                    tightness, 1f, owner.wheelRepairPerSecond * Time.fixedDeltaTime);
                if (IsPositioned && tightness >= 0.999f)
                    Reattach();
            }
            else if (!IsDetached)
            {
                tightness = Mathf.MoveTowards(
                    tightness, 0f, loosenPerMinute / 60f * Time.fixedDeltaTime);
                if (tightness <= 0.0001f)
                    Detach();
            }
        }

        private void FollowCarryTarget()
        {
            if (carryView == null)
            {
                DropFromCarry();
                return;
            }

            Vector3 targetPosition = carryView.position +
                                     carryView.right * carryOffset.x +
                                     Vector3.up * carryOffset.y +
                                     carryView.forward * carryOffset.z;
            Vector3 flatForward = Vector3.ProjectOnPlane(carryView.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = Vector3.forward;
            Quaternion targetRotation = Quaternion.LookRotation(flatForward, Vector3.up) *
                                        connectedLocalRotation;
            float positionBlend = 1f - Mathf.Exp(-14f * Time.fixedDeltaTime);
            float rotationBlend = 1f - Mathf.Exp(-12f * Time.fixedDeltaTime);
            wheelBody.MovePosition(Vector3.Lerp(wheelBody.position, targetPosition, positionBlend));
            wheelBody.MoveRotation(Quaternion.Slerp(wheelBody.rotation, targetRotation, rotationBlend));
        }

        public void PickUp(Transform view, Vector3 viewOffset)
        {
            if (!CanBePickedUp || view == null)
                return;
            IsCarried = true;
            carryView = view;
            carryOffset = viewOffset;
            wheelBody.linearVelocity = Vector3.zero;
            wheelBody.angularVelocity = Vector3.zero;
            wheelBody.isKinematic = true;
            wheelBody.detectCollisions = false;
        }

        public bool IsPlayerNearHub(Vector3 playerPosition, float maximumDistance)
        {
            return Vector3.Distance(playerPosition,
                connectedBody.transform.TransformPoint(connectedAnchor)) <= maximumDistance;
        }

        public void PositionAtHub()
        {
            if (!IsCarried)
                return;
            IsCarried = false;
            IsPositioned = true;
            carryView = null;
            wheelBody.position = connectedBody.transform.TransformPoint(connectedAnchor);
            wheelBody.rotation = connectedBody.rotation * connectedLocalRotation;
            wheelBody.linearVelocity = Vector3.zero;
            wheelBody.angularVelocity = Vector3.zero;
            wheelBody.isKinematic = true;
            // Keep query detection enabled so the positioned wheel can be aimed at
            // with the crosshair. Internal wagon collisions were already ignored.
            wheelBody.detectCollisions = true;
        }

        public void DropFromCarry()
        {
            if (!IsCarried)
                return;
            IsCarried = false;
            carryView = null;
            wheelBody.isKinematic = false;
            wheelBody.detectCollisions = true;
        }

        private void Detach()
        {
            if (IsDetached)
                return;
            IsDetached = true;
            IsCarried = false;
            IsPositioned = false;
            if (wheelJoint != null)
            {
                Destroy(wheelJoint);
                wheelJoint = null;
            }
            wheelBody.AddForce(Vector3.up * 0.35f + transform.right * outerSide * 0.65f,
                ForceMode.VelocityChange);
        }

        private void Reattach()
        {
            Vector3 targetPosition = connectedBody.transform.TransformPoint(connectedAnchor);
            Quaternion targetRotation = connectedBody.rotation * connectedLocalRotation;
            wheelBody.position = targetPosition;
            wheelBody.rotation = targetRotation;
            wheelBody.isKinematic = false;
            wheelBody.detectCollisions = true;
            wheelJoint = wheelBody.gameObject.AddComponent<HingeJoint>();
            wheelJoint.connectedBody = connectedBody;
            wheelJoint.autoConfigureConnectedAnchor = false;
            wheelJoint.anchor = Vector3.zero;
            wheelJoint.connectedAnchor = connectedAnchor;
            wheelJoint.axis = Vector3.right;
            wheelJoint.enableCollision = false;
            IsDetached = false;
            IsCarried = false;
            IsPositioned = false;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (owner == null || IsDetached || collision == null)
                return;

            float impactSpeed = GetNormalImpactSpeed(collision);
            if (impactSpeed >= owner.wheelDamageImpactSpeed)
            {
                float damage = (impactSpeed - owner.wheelDamageImpactSpeed) *
                               owner.wheelDamagePerImpactSpeed;
                tightness = Mathf.Max(0f, tightness - damage);
                if (tightness <= 0.0001f)
                    Detach();
            }
            owner.ReportImpact(collision, wheelBody);
        }

        internal static float GetNormalImpactSpeed(Collision collision)
        {
            if (collision == null || collision.contactCount == 0)
                return collision != null ? collision.relativeVelocity.magnitude : 0f;

            float greatest = 0f;
            for (int i = 0; i < collision.contactCount; i++)
                greatest = Mathf.Max(greatest,
                    Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
            return greatest;
        }

        private void LateUpdate()
        {
            UpdateRepairVisuals();
            if (!IsRepairing || wrenchRoot == null)
                return;

            wrenchAngle += Time.deltaTime * 210f;
            wrenchRoot.transform.position = transform.TransformPoint(
                new Vector3(outerSide * 0.2f, 0.18f, 0.24f));
            wrenchRoot.transform.rotation = transform.rotation *
                Quaternion.AngleAxis(outerSide * (25f + Mathf.Sin(wrenchAngle * Mathf.Deg2Rad) * 32f),
                    Vector3.right);
        }

        private void UpdateRepairVisuals()
        {
            if (nuts == null)
                return;
            float loosened = 1f - tightness;
            for (int i = 0; i < nuts.Length; i++)
            {
                float angle = i * 90f * Mathf.Deg2Rad;
                nuts[i].localPosition = new Vector3(
                    outerSide * Mathf.Lerp(0.125f, 0.225f, loosened),
                    Mathf.Cos(angle) * 0.18f,
                    Mathf.Sin(angle) * 0.18f);
                nuts[i].localRotation = Quaternion.AngleAxis(
                    outerSide * loosened * 720f, Vector3.right) *
                    Quaternion.Euler(0f, 0f, 90f);
            }
        }

        private void CreateNuts(Material metal)
        {
            nuts = new Transform[4];
            for (int i = 0; i < nuts.Length; i++)
            {
                GameObject nut = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                nut.name = $"Wheel nut {i + 1}";
                nut.transform.SetParent(transform, false);
                nut.transform.localScale = new Vector3(0.07f, 0.025f, 0.07f);
                nut.GetComponent<Renderer>().sharedMaterial = metal;
                Collider collider = nut.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                nuts[i] = nut.transform;
            }
        }

        private void CreateWrench(Material metal)
        {
            wrenchRoot = new GameObject($"{Label} repair wrench");
            wrenchRoot.transform.SetParent(owner.transform, false);
            CreateWrenchPart("Handle", Vector3.zero, new Vector3(0.055f, 0.42f, 0.045f), metal);
            CreateWrenchPart("Jaw left", new Vector3(-0.055f, 0.22f, 0f),
                new Vector3(0.055f, 0.14f, 0.045f), metal, -28f);
            CreateWrenchPart("Jaw right", new Vector3(0.055f, 0.22f, 0f),
                new Vector3(0.055f, 0.14f, 0.045f), metal, 28f);
            wrenchRoot.SetActive(false);
        }

        private void CreateWrenchPart(string partName, Vector3 localPosition,
            Vector3 scale, Material material, float zRotation = 0f)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(wrenchRoot.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, zRotation);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
        }
    }

    internal sealed class WagonImpactRelay : MonoBehaviour
    {
        private WagonController owner;
        private Rigidbody body;

        internal void Initialize(WagonController wagon, Rigidbody source)
        {
            owner = wagon;
            body = source;
        }

        private void OnCollisionEnter(Collision collision)
        {
            owner?.ReportImpact(collision, body);
        }
    }
}
