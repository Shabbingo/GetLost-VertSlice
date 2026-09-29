using GetLost.Trails;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GetLost.Wagon
{
    /// <summary>Installs the first-iteration wagon in the existing world scene, without scene YAML edits.</summary>
    public sealed class WagonPrototypeBootstrap : MonoBehaviour
    {
        private const string WagonPrefabResource = "GetLost/WagonPrototype";
        private Transform player;
        private Vector3 lastPlayerPosition;
        private WagonWorld world;
        private float nextSpawnAttempt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<WagonPrototypeBootstrap>() == null)
                new GameObject("Wagon Prototype Setup").AddComponent<WagonPrototypeBootstrap>();
        }

        public static WagonWorld EnsureCreated(WagonSaveData saved = null)
        {
            var motor = FindAnyObjectByType<WalkingMotor>();
            if (motor == null) return null;
            WagonWorld existing = FindAnyObjectByType<WagonWorld>();
            if (existing != null)
            {
                WagonController placedWagon = existing.GetComponent<WagonController>();
                if (placedWagon == null) return null;
                placedWagon.EnsureBuilt();
                if (!existing.IsInitialized) existing.Initialize(placedWagon, null);
                LinkPlayer(motor, placedWagon);
                return existing;
            }

            WagonController prefab = Resources.Load<WagonController>(WagonPrefabResource);
            Vector3 forward = Vector3.ProjectOnPlane(motor.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            Vector3 location;
            Quaternion rotation;
            if (saved != null && saved.hasWagon)
            {
                rotation = saved.rotation;
                location = saved.position - rotation * (Vector3.up * 0.78f);
            }
            else if (!TryFindSpawn(motor.transform.position, forward, prefab,
                         out location, out rotation))
                return null;
            WagonController wagon = WagonController.Create(location, rotation, prefab);
            WagonWorld result = wagon.GetComponent<WagonWorld>();
            if (result == null) result = wagon.gameObject.AddComponent<WagonWorld>();
            result.Initialize(wagon, null);
            LinkPlayer(motor, wagon);
            return result;
        }

        private static void LinkPlayer(WalkingMotor motor, WagonController wagon)
        {
            WagonPlayerLink link = motor.GetComponent<WagonPlayerLink>();
            if (link == null) link = motor.gameObject.AddComponent<WagonPlayerLink>();
            link.Initialize(wagon);
        }

        /// <summary>Moves the runtime wagon to a clear, terrain-supported position near the target.</summary>
        public static bool CallWagonTo(Transform target)
        {
            if (target == null)
                return false;
            WagonWorld wagonWorld = EnsureCreated();
            if (wagonWorld == null || wagonWorld.Wagon == null)
                return false;

            Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f)
                forward = Vector3.forward;
            if (!TryFindSpawn(target.position, forward, wagonWorld.Wagon,
                    out Vector3 position, out Quaternion rotation))
                return false;

            wagonWorld.Wagon.PlaceBody(position + Vector3.up * 0.78f, rotation);
            return true;
        }

        private static bool TryFindSpawn(Vector3 playerPosition, Vector3 forward,
            WagonController settings,
            out Vector3 location, out Quaternion rotation)
        {
            float preferredDistance = settings != null ? settings.preferredSpawnDistance : 5f;
            float fallbackDistance = settings != null ? settings.fallbackSpawnDistance : 8f;
            float maximumSlope = settings != null ? settings.maximumSpawnSlope : 30f;
            float clearance = settings != null ? settings.spawnGroundClearance : 0.08f;
            for (int i = 0; i < 16; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, (i % 8) * 45f, 0f) * forward;
                location = playerPosition + direction * (i < 8 ? preferredDistance : fallbackDistance);
                rotation = Quaternion.LookRotation(-direction);
                Terrain terrain = TrailTerrainUtility.FindTerrainAt(location);
                if (terrain == null || terrain.terrainData == null) continue;
                Vector3 terrainLocal = location - terrain.transform.position;
                Vector3 normal = terrain.terrainData.GetInterpolatedNormal(
                    terrainLocal.x / terrain.terrainData.size.x, terrainLocal.z / terrain.terrainData.size.z);
                if (Vector3.Angle(normal, Vector3.up) > maximumSlope) continue;
                float height = terrain.SampleHeight(location) + terrain.transform.position.y;
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 wheel = location + rotation * new Vector3(x, 0f, z);
                        height = Mathf.Max(height, terrain.SampleHeight(wheel) + terrain.transform.position.y);
                    }
                location.y = height + clearance;
                bool blocked = false;
                Vector3 centre = location + rotation * new Vector3(0f, 0.9f, 0.95f);
                foreach (Collider collider in Physics.OverlapBox(centre,
                             new Vector3(1.2f, 0.85f, 2.55f), rotation, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (collider is TerrainCollider) continue;
                    blocked = true;
                    break;
                }
                if (!blocked) return true;
            }
            location = default;
            rotation = Quaternion.identity;
            return false;
        }
        private void Update()
        {
            if (world == null)
            {
                if (Time.unscaledTime < nextSpawnAttempt) return;
                nextSpawnAttempt = Time.unscaledTime + 1f;
                world = EnsureCreated();
                if (world == null) return;
                player = FindAnyObjectByType<WalkingMotor>().transform;
                lastPlayerPosition = player.position;
            }
            if (player == null) return;
            // Mission setup teleports the player. Bring the unused starter wagon along.
            // After first use it remains a world object wherever the player leaves it.
            if (!world.Wagon.HasBeenUsed &&
                Vector3.Distance(player.position, lastPlayerPosition) > 20f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.1f)
                    forward = Vector3.forward;
                WagonController settings = world.Wagon;
                if (TryFindSpawn(player.position, forward, settings,
                        out Vector3 position, out Quaternion rotation))
                    world.Wagon.PlaceBody(position + Vector3.up * 0.78f, rotation);
            }
            lastPlayerPosition = player.position;
        }
    }
}
