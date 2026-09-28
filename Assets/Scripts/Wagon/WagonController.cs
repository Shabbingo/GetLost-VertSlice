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
        public float pullSpring = 3800f;
        public float pullDamping = 180f;
        public float maximumPullForce = 1900f;
        public float handleSlack = 0.38f;
        public float gripReach = 0.75f;
        [Header("Automatic Spawn")]
        [Min(0f)] public float preferredSpawnDistance = 5f;
        [Min(0f)] public float fallbackSpawnDistance = 8f;
        [Range(0f, 89f)] public float maximumSpawnSlope = 30f;
        [Min(0f)] public float spawnGroundClearance = 0.08f;

        public Rigidbody Body { get; private set; }
        public Rigidbody FrontAxle { get; private set; }
        public Transform Grip { get; private set; }
        public Transform Dispenser { get; private set; }
        public bool IsHeld => holder != null;
        public bool CanDeposit => holder != null && holder.CanDrive;
        public bool GateOpen { get; private set; }
        public bool HasBeenUsed { get; private set; }
        public float Resistance { get; private set; }
        public Rigidbody[] Bodies { get; private set; }
        public Collider[] Colliders { get; private set; }
        public float LoadFraction => capacity > 0f ? Mathf.Clamp01(gravel / capacity) : 0f;

        private WagonPlayerLink holder;
        private Rigidbody[] wheels;
        private Transform loadVisual;
        private Transform gateVisual;
        private Material wood, metal, gravelMaterial;
        private PhysicsMaterial wheelMaterial;
        private PhysicsMaterial frameMaterial;

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
            Dispenser = new GameObject("Gravel outlet").transform;
            Dispenser.SetParent(Body.transform, false);
            Dispenser.localPosition = new Vector3(0f, -0.12f, -1.35f);
            wheels = new Rigidbody[4];
            wheels[0] = Wheel("Front left", FrontAxle, new Vector3(-0.94f, 0f, 0f));
            wheels[1] = Wheel("Front right", FrontAxle, new Vector3(0.94f, 0f, 0f));
            wheels[2] = Wheel("Rear left", Body, new Vector3(-0.94f, -0.28f, -0.87f));
            wheels[3] = Wheel("Rear right", Body, new Vector3(0.94f, -0.28f, -0.87f));
            Box("Rear axle beam", Body.transform, new Vector3(0f, -0.28f, -0.87f),
                new Vector3(1.85f, 0.12f, 0.12f), metal);
            Bodies = GetComponentsInChildren<Rigidbody>();
            Colliders = GetComponentsInChildren<Collider>();
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
            return body;
        }

        private Rigidbody Wheel(string label, Rigidbody axle, Vector3 anchor)
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

        public void SetHolder(WagonPlayerLink value)
        {
            holder = value;
            if (holder != null) HasBeenUsed = true;
            else GateOpen = false;
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
                joint.useMotor = parked;
                if (parked)
                    joint.motor = new JointMotor { targetVelocity = 0f, force = 1000f, freeSpin = false };
            }
            Resistance = 0f;
            if (parked) return;
            Vector3 error = Vector3.ProjectOnPlane(holder.transform.position - Grip.position, Vector3.up);
            Vector3 force = error * pullSpring - Vector3.ProjectOnPlane(
                FrontAxle.GetPointVelocity(Grip.position), Vector3.up) * pullDamping;
            force = Vector3.ClampMagnitude(force, maximumPullForce);
            Resistance = Mathf.Clamp01(force.magnitude / maximumPullForce);
            FrontAxle.AddForceAtPosition(force, Grip.position);
            // Dampen steering oscillation without forcing the front axle to a chosen pose.
            float yawRate = Vector3.Dot(FrontAxle.angularVelocity - Body.angularVelocity, Body.transform.up);
            FrontAxle.AddTorque(-Body.transform.up * yawRate * 45f);
        }

        private void LateUpdate()
        {
            if (loadVisual == null) return;
            float height = Mathf.Lerp(0.015f, 0.46f, LoadFraction);
            loadVisual.localScale = new Vector3(1.28f, height, 2.1f);
            loadVisual.localPosition = new Vector3(0f, 0.1f + height * 0.5f, 0f);
            gateVisual.localPosition = new Vector3(0f, GateOpen ? 0.59f : 0.36f, -1.12f);
        }

        public void PlaceBody(Vector3 position, Quaternion rotation)
        {
            holder?.Release();
            Quaternion change = rotation * Quaternion.Inverse(Body.rotation);
            Vector3 oldPosition = Body.position;
            foreach (Rigidbody part in Bodies)
            {
                part.position = position + change * (part.position - oldPosition);
                part.rotation = change * part.rotation;
                part.linearVelocity = Vector3.zero;
                part.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        private void OnDestroy()
        {
            holder?.Release();
            Destroy(wood); Destroy(metal); Destroy(gravelMaterial);
            Destroy(wheelMaterial); Destroy(frameMaterial);
        }
    }
}
