using System;
using System.Collections;
using System.Collections.Generic;
using Tom.WalkingController;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GetLost.Encounters
{
    /// <summary>
    /// Gives every eligible terrain tree an independent chance to contain a drop bear.
    /// Dedicated Red Gums are only used as a fallback when a map has no terrain trees.
    /// </summary>
    public sealed class DropBearEncounterBootstrap : MonoBehaviour
    {
        private const string ResourcePath = "GetLost/DropBearRedGum";
        private const string SettingsResourcePath = "GetLost/DropBearSettings";
        private const string EditorRedGumPath = "Assets/My Assets/Prefabs/OldTrees/Redgum/Med_RedGum.prefab";
        private const int DesiredEncounterCount = 6;
        private const float MinimumDistanceFromPlayer = 38f;
        private const float MaximumDistanceFromPlayer = 105f;
        private const float MinimumSpacing = 32f;
        private const int PlacementAttempts = 48;
        private readonly List<Vector3> placed = new();
        private DropBearSettings settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (FindAnyObjectByType<DropBearEncounterBootstrap>() == null)
                new GameObject("Drop Bear Encounter Setup").AddComponent<DropBearEncounterBootstrap>();
        }

        private IEnumerator Start()
        {
            WalkingMotor player = null;
            float giveUpAt = Time.realtimeSinceStartup + 10f;
            while ((player == null || Terrain.activeTerrains.Length == 0) &&
                   Time.realtimeSinceStartup < giveUpAt)
            {
                player = FindAnyObjectByType<WalkingMotor>();
                if (player == null || Terrain.activeTerrains.Length == 0)
                    yield return null;
            }

            if (player == null || Terrain.activeTerrains.Length == 0)
            {
                Debug.LogWarning("[Drop Bear] Could not create encounters: player or terrain was unavailable.");
                yield break;
            }

            settings = Resources.Load<DropBearSettings>(SettingsResourcePath);
            IReadOnlyList<DropBearTerrainTreeRegistry.TreeSite> terrainTrees =
                DropBearTerrainTreeRegistry.GetAllSites();
            if (terrainTrees.Count > 0)
                SpawnTerrainTreeEncounters(player.transform.position, terrainTrees);
            else
            {
                GameObject redGumPrefab = FindRedGumPrefab();
                if (redGumPrefab != null)
                    SpawnEncounterTrees(player.transform.position, redGumPrefab);
                else
                    Debug.LogWarning("[Drop Bear] No eligible terrain trees or fallback Red Gum prefab were found.");
            }
        }

        private void SpawnTerrainTreeEncounters(Vector3 playerPosition,
            IReadOnlyList<DropBearTerrainTreeRegistry.TreeSite> terrainTrees)
        {
            float chance = settings != null ? Mathf.Clamp01(settings.bearChancePerTree) : 0.01f;
            int maximum = settings != null ? Mathf.Max(0, settings.maximumInitialBears) : 24;
            float playerClearance = settings != null ? settings.minimumSpawnDistanceFromPlayer : 20f;
            float spacing = settings != null ? settings.minimumBearSpacing : 25f;
            var candidates = new List<TreeCandidate>();

            foreach (DropBearTerrainTreeRegistry.TreeSite site in terrainTrees)
            {
                uint hash = HashTreePosition(site.Base);
                float roll = (hash & 0x00ffffffu) / 16777215f;
                if (roll <= chance)
                    candidates.Add(new TreeCandidate(site, hash));
            }
            candidates.Sort((left, right) => left.Hash.CompareTo(right.Hash));

            foreach (TreeCandidate candidate in candidates)
            {
                if (placed.Count >= maximum)
                    break;
                if (Vector3.Distance(playerPosition, candidate.Site.Base) < playerClearance ||
                    !HasSpacing(candidate.Site.Base, spacing))
                    continue;
                CreateEncounterAtTree(candidate.Site.Base, candidate.Site.Perch);
            }

            Debug.Log($"[Drop Bear] Rolled {terrainTrees.Count} eligible terrain trees at " +
                      $"{chance * 100f:0.##}% and spawned {placed.Count} bears (cap {maximum}).");
        }

        private void CreateEncounterAtTree(Vector3 treePosition, Vector3 branchPosition)
        {
            GameObject territory = new($"Drop Bear Terrain Tree {placed.Count + 1}");
            territory.transform.SetParent(transform, true);
            territory.transform.position = treePosition;
            DropBearEncounter encounter = territory.AddComponent<DropBearEncounter>();
            encounter.ApplySettings(settings);
            encounter.Initialize(branchPosition,
                settings != null ? settings.territoryRadius : 8f);
            placed.Add(treePosition);
        }

        private void SpawnEncounterTrees(Vector3 playerPosition, GameObject redGumPrefab)
        {
            var random = new System.Random(1977);
            int existing = FindObjectsByType<DropBearEncounter>(FindObjectsInactive.Exclude).Length;
            int required = Mathf.Max(0, DesiredEncounterCount - existing);
            for (int attempt = 0; attempt < PlacementAttempts && placed.Count < required; attempt++)
            {
                float angle = (float)random.NextDouble() * 360f;
                float distance = Mathf.Lerp(MinimumDistanceFromPlayer, MaximumDistanceFromPlayer,
                    (float)random.NextDouble());
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 candidate = playerPosition + direction * distance;
                if (!TryGetGround(candidate, out Vector3 ground, out Vector3 normal) ||
                    Vector3.Angle(normal, Vector3.up) > 24f || !HasSpacing(ground) || IsBlocked(ground))
                    continue;

                SpawnTreeEncounter(redGumPrefab, ground, (float)random.NextDouble());
            }

            if (placed.Count == 0)
                Debug.LogWarning("[Drop Bear] Red Gum prefab was found, but no clear encounter-tree positions were available.");
            else
                Debug.Log($"[Drop Bear] Spawned {placed.Count} Red Gum encounter trees.");
        }

        private void SpawnTreeEncounter(GameObject redGumPrefab, Vector3 position, float variation)
        {
            GameObject territory = new($"Drop Bear Red Gum {placed.Count + 1}");
            territory.transform.SetParent(transform, true);
            territory.transform.position = position;

            GameObject tree = Instantiate(redGumPrefab, position,
                Quaternion.Euler(0f, variation * 360f, 0f), territory.transform);
            tree.name = redGumPrefab.name;
            tree.transform.localScale *= Mathf.Lerp(0.9f, 1.12f, variation);

            Bounds bounds = CalculateRendererBounds(tree);
            Vector3 branchPosition = bounds.size.y > 1f
                ? new Vector3(bounds.center.x, Mathf.Lerp(bounds.min.y, bounds.max.y, 0.64f), bounds.center.z)
                : position + Vector3.up * 5f;

            DropBearEncounter encounter = territory.AddComponent<DropBearEncounter>();
            encounter.ApplySettings(settings);
            float radius = settings != null
                ? settings.territoryRadius
                : Mathf.Lerp(7f, 8.5f, variation);
            encounter.Initialize(branchPosition, radius);
            placed.Add(position);
        }

        private static GameObject FindRedGumPrefab()
        {
            GameObject resource = Resources.Load<GameObject>(ResourcePath);
            if (resource != null)
                return resource;

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;
                foreach (TreePrototype prototype in terrain.terrainData.treePrototypes)
                {
                    if (prototype.prefab != null && IsRedGumName(prototype.prefab.name))
                        return prototype.prefab;
                }
            }

#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GameObject>(EditorRedGumPath);
#else
            return null;
#endif
        }

        private static bool IsRedGumName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            string compact = value.Replace("_", string.Empty).Replace(" ", string.Empty);
            return compact.IndexOf("RedGum", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetGround(Vector3 point, out Vector3 ground, out Vector3 normal)
        {
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;
                Vector3 local = point - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z)
                    continue;
                ground = new Vector3(point.x,
                    terrain.SampleHeight(point) + terrain.transform.position.y, point.z);
                normal = terrain.terrainData.GetInterpolatedNormal(local.x / size.x, local.z / size.z);
                return true;
            }
            ground = default;
            normal = Vector3.up;
            return false;
        }

        private bool HasSpacing(Vector3 position)
        {
            return HasSpacing(position, MinimumSpacing);
        }

        private bool HasSpacing(Vector3 position, float spacing)
        {
            foreach (Vector3 other in placed)
            {
                Vector2 offset = new(position.x - other.x, position.z - other.z);
                if (offset.sqrMagnitude < spacing * spacing)
                    return false;
            }
            return true;
        }

        private static bool IsBlocked(Vector3 position)
        {
            foreach (Collider collider in Physics.OverlapSphere(position + Vector3.up * 2f, 2.2f,
                         ~0, QueryTriggerInteraction.Ignore))
            {
                if (collider is TerrainCollider)
                    continue;
                return true;
            }
            return false;
        }

        private static Bounds CalculateRendererBounds(GameObject tree)
        {
            Renderer[] renderers = tree.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(tree.transform.position + Vector3.up * 2.5f, Vector3.up * 5f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static uint HashTreePosition(Vector3 position)
        {
            unchecked
            {
                uint value = (uint)Mathf.RoundToInt(position.x * 10f);
                value ^= (uint)Mathf.RoundToInt(position.z * 10f) * 0x9e3779b9u;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }
        }

        private readonly struct TreeCandidate
        {
            internal readonly DropBearTerrainTreeRegistry.TreeSite Site;
            internal readonly uint Hash;

            internal TreeCandidate(DropBearTerrainTreeRegistry.TreeSite site, uint hash)
            {
                Site = site;
                Hash = hash;
            }
        }
    }
}
