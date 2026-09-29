using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner.Editor
{
    public sealed class TerrainFoliagePlacementBrushWindow : EditorWindow
    {
        private enum BrushMode { Place, Erase }
        private enum PlacementStorage { StreamedFoliage, ManagedGrass }

        [SerializeField] private BrushMode mode;
        [SerializeField] private PlacementStorage placementStorage;
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0.5f)] private float radius = 8f;
        [SerializeField, Range(1, 30)] private int placementsPerStamp = 4;
        [SerializeField, Min(0f)] private float minimumSpacing = 2.5f;
        [SerializeField, Min(0.05f)] private float minimumScale = .85f;
        [SerializeField, Min(0.05f)] private float maximumScale = 1.2f;
        [SerializeField] private bool alignToTerrain = true;
        [SerializeField] private bool eraseAllStreamedFoliage;

        private TerrainFoliageSpawner[] spawners = Array.Empty<TerrainFoliageSpawner>();
        private GameObject[] availablePrefabs = Array.Empty<GameObject>();
        private Vector3 lastStampPosition;
        private bool hasLastStamp;
        private int editsSinceSave;

        [MenuItem("Tools/Get Lost/Terrain/Foliage Placement Brush")]
        public static void Open()
        {
            GetWindow<TerrainFoliagePlacementBrushWindow>("Foliage Brush").Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += DuringSceneGui;
            Undo.undoRedoPerformed += OnUndoRedo;
            RefreshTargets();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            Undo.undoRedoPerformed -= OnUndoRedo;
            SaveEditedAssets();
        }

        private void OnSelectionChange()
        {
            RefreshTargets();
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Paints the custom streamed foliage data used for bushes. Left-drag uses the selected mode; hold Shift while painting to erase temporarily. Terrain Detail tools do not affect this data.",
                MessageType.Info);
            EditorGUILayout.HelpBox(
                "Use this in Edit Mode. Regenerating foliage rebuilds the generated placement asset and will replace manual brush edits.",
                MessageType.Warning);

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Exit Play Mode before editing foliage placements.", MessageType.Warning);
                return;
            }

            mode = (BrushMode)GUILayout.Toolbar((int)mode, new[] { "Place", "Erase" });
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                prefab = (GameObject)EditorGUILayout.ObjectField("Foliage Prefab", prefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck())
                    SelectLikelyStorageForPrefab();
                if (GUILayout.Button("Refresh", GUILayout.Width(68f)))
                    RefreshTargets();
            }

            if (availablePrefabs.Length > 0)
            {
                int current = Array.IndexOf(availablePrefabs, prefab);
                string[] names = availablePrefabs.Select(item => item ? item.name : "Missing").ToArray();
                EditorGUI.BeginChangeCheck();
                int selected = EditorGUILayout.Popup("Existing Prefabs", Mathf.Max(0, current), names);
                if (EditorGUI.EndChangeCheck() && selected >= 0 && selected < availablePrefabs.Length)
                {
                    prefab = availablePrefabs[selected];
                    SelectLikelyStorageForPrefab();
                }
            }

            radius = EditorGUILayout.Slider("Brush Radius", radius, .5f, 60f);
            if (mode == BrushMode.Place)
            {
                placementStorage = (PlacementStorage)EditorGUILayout.EnumPopup("Placement Storage", placementStorage);
                placementsPerStamp = EditorGUILayout.IntSlider("Placements Per Stamp", placementsPerStamp, 1, 30);
                minimumSpacing = EditorGUILayout.Slider("Minimum Spacing", minimumSpacing, 0f, 20f);
                minimumScale = EditorGUILayout.FloatField("Minimum Scale", minimumScale);
                maximumScale = EditorGUILayout.FloatField("Maximum Scale", maximumScale);
                alignToTerrain = EditorGUILayout.Toggle("Align To Terrain", alignToTerrain);
            }
            else
            {
                eraseAllStreamedFoliage = EditorGUILayout.Toggle(
                    new GUIContent("Erase All Generated Foliage", "When disabled, the brush erases the selected prefab from both streamed foliage and managed grass."),
                    eraseAllStreamedFoliage);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Terrain tiles found", spawners.Length.ToString());
            EditorGUILayout.LabelField("Available foliage prefabs", availablePrefabs.Length.ToString());

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Selected Terrain World"))
                    RefreshTargets();
                if (GUILayout.Button("Save Placement Assets"))
                    SaveEditedAssets();
            }

            if (spawners.Length == 0)
                EditorGUILayout.HelpBox("Select the generated terrain world root (or one of its terrain tiles), then click Use Selected Terrain World.", MessageType.Warning);
            else if (prefab == null && (mode == BrushMode.Place || !eraseAllStreamedFoliage))
                EditorGUILayout.HelpBox("Choose a bush prefab before painting.", MessageType.Warning);
        }

        private void RefreshTargets()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected != null)
            {
                Transform root = selected.transform.root;
                spawners = root.GetComponentsInChildren<TerrainFoliageSpawner>(true)
                    .Where(item => item && item.Terrain && item.RuntimePlacementData)
                    .ToArray();
                if (spawners.Length == 0)
                {
                    TerrainFoliageSpawner own = selected.GetComponent<TerrainFoliageSpawner>();
                    spawners = own && own.RuntimePlacementData
                        ? new[] { own }
                        : Array.Empty<TerrainFoliageSpawner>();
                }
            }

            availablePrefabs = spawners
                .SelectMany(item => item.RuntimePlacementData.Prefabs
                    .Concat(item.RuntimePlacementData.GrassPrefabs))
                .Where(item => item)
                .Distinct()
                .OrderBy(item => item.name)
                .ToArray();
            if (prefab == null && availablePrefabs.Length > 0)
                prefab = availablePrefabs[0];
            SceneView.RepaintAll();
        }

        private void DuringSceneGui(SceneView sceneView)
        {
            Event current = Event.current;
            if (Application.isPlaying || spawners.Length == 0 || current.alt)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            if (!TryGetTerrainHit(ray, out RaycastHit hit, out Terrain terrain))
                return;

            TerrainFoliageSpawner target = spawners.FirstOrDefault(item => item.Terrain == terrain);
            if (!target || !target.RuntimePlacementData)
                return;

            bool erase = mode == BrushMode.Erase || current.shift;
            Color brushColor = erase ? new Color(1f, .25f, .2f, .9f) : new Color(.2f, 1f, .35f, .9f);
            Handles.color = brushColor;
            Handles.DrawWireDisc(hit.point, hit.normal, radius);
            DrawNearbyPlacements(target.RuntimePlacementData, hit.point, radius, erase ? brushColor : Color.white);

            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            bool painting = current.button == 0 &&
                            (current.type == EventType.MouseDown || current.type == EventType.MouseDrag);
            if (!painting)
            {
                if (current.type == EventType.MouseUp)
                {
                    hasLastStamp = false;
                    SaveEditedAssets();
                }
                return;
            }

            if (prefab == null && (!erase || !eraseAllStreamedFoliage))
                return;
            if (hasLastStamp && Vector3.Distance(lastStampPosition, hit.point) < Mathf.Max(.25f, radius * .3f))
                return;

            if (erase)
                EraseStamp(hit.point);
            else
                PlaceStamp(target, hit.point);

            lastStampPosition = hit.point;
            hasLastStamp = true;
            current.Use();
            sceneView.Repaint();
        }

        private void PlaceStamp(TerrainFoliageSpawner target, Vector3 centre)
        {
            TerrainFoliagePlacementData data = target.RuntimePlacementData;
            Undo.RecordObject(data, "Paint Bush Placements");
            int added = 0;
            HashSet<TerrainFoliageSpawner> changedSpawners = new HashSet<TerrainFoliageSpawner>();
            for (int i = 0; i < placementsPerStamp; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 position = new Vector3(centre.x + offset.x, centre.y, centre.z + offset.y);
                TerrainFoliageSpawner owner = FindSpawnerAt(position);
                if (!owner || owner.RuntimePlacementData == null)
                    continue;

                Terrain ownerTerrain = owner.Terrain;
                TerrainData terrainData = ownerTerrain.terrainData;
                Vector3 local = position - ownerTerrain.transform.position;
                float u = local.x / terrainData.size.x;
                float v = local.z / terrainData.size.z;
                if (u < 0f || u > 1f || v < 0f || v > 1f)
                    continue;

                position.y = ownerTerrain.SampleHeight(position) + ownerTerrain.transform.position.y;
                bool occupied = placementStorage == PlacementStorage.ManagedGrass
                    ? owner.RuntimePlacementData.HasGrassPlacementWithin(position, minimumSpacing, prefab)
                    : owner.RuntimePlacementData.HasPlacementWithin(position, minimumSpacing, prefab);
                if (minimumSpacing > 0f && occupied)
                    continue;

                Vector3 normal = terrainData.GetInterpolatedNormal(u, v);
                Quaternion rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                if (alignToTerrain)
                    rotation = Quaternion.FromToRotation(Vector3.up, normal) * rotation;
                float scale = UnityEngine.Random.Range(
                    Mathf.Max(.05f, Mathf.Min(minimumScale, maximumScale)),
                    Mathf.Max(.05f, Mathf.Max(minimumScale, maximumScale)));

                if (owner.RuntimePlacementData != data)
                {
                    data = owner.RuntimePlacementData;
                    Undo.RecordObject(data, "Paint Bush Placements");
                }
                if (placementStorage == PlacementStorage.ManagedGrass)
                    data.AddGrass(prefab, position, rotation, Vector3.one * scale);
                else
                    data.Add(prefab, position, rotation, Vector3.one * scale);
                EditorUtility.SetDirty(data);
                changedSpawners.Add(owner);
                added++;
            }
            foreach (TerrainFoliageSpawner changed in changedSpawners)
                RefreshStreamer(changed);
            editsSinceSave += added;
        }

        private void EraseStamp(Vector3 centre)
        {
            foreach (TerrainFoliageSpawner spawner in spawners)
            {
                TerrainFoliagePlacementData data = spawner.RuntimePlacementData;
                if (!data ||
                    (!HorizontalBoundsOverlap(data.WorldBounds, centre, radius) &&
                     !HorizontalBoundsOverlap(data.GrassWorldBounds, centre, radius)))
                    continue;
                Undo.RecordObject(data, "Erase Foliage Placements");
                GameObject filter = eraseAllStreamedFoliage ? null : prefab;
                int removed = data.RemovePlacementsInRadius(
                    centre,
                    radius,
                    filter);
                removed += data.RemoveGrassPlacementsInRadius(
                    centre,
                    radius,
                    filter);
                if (removed <= 0)
                    continue;
                EditorUtility.SetDirty(data);
                RefreshStreamer(spawner);
                editsSinceSave += removed;
            }
        }

        private TerrainFoliageSpawner FindSpawnerAt(Vector3 worldPosition)
        {
            foreach (TerrainFoliageSpawner spawner in spawners)
            {
                Terrain terrain = spawner.Terrain;
                Vector3 local = worldPosition - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x >= 0f && local.x <= size.x && local.z >= 0f && local.z <= size.z)
                    return spawner;
            }
            return null;
        }

        private static void RefreshStreamer(TerrainFoliageSpawner spawner)
        {
            TerrainFoliageRuntimeStreamer streamer = spawner.GetComponent<TerrainFoliageRuntimeStreamer>();
            if (streamer)
                streamer.RebuildIndex();
            TerrainFoliageGrassRenderer grassRenderer = spawner.GetComponent<TerrainFoliageGrassRenderer>();
            if (grassRenderer)
                grassRenderer.Rebuild();
        }

        private bool TryGetTerrainHit(Ray ray, out RaycastHit terrainHit, out Terrain terrain)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, 100000f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Terrain candidate = hits[i].collider.GetComponent<Terrain>();
                if (!candidate || !spawners.Any(item => item && item.Terrain == candidate))
                    continue;
                terrainHit = hits[i];
                terrain = candidate;
                return true;
            }
            terrainHit = default;
            terrain = null;
            return false;
        }

        private void OnUndoRedo()
        {
            foreach (TerrainFoliageSpawner spawner in spawners)
                if (spawner)
                    RefreshStreamer(spawner);
            SceneView.RepaintAll();
        }

        private void DrawNearbyPlacements(TerrainFoliagePlacementData data, Vector3 centre, float brushRadius, Color color)
        {
            float radiusSquared = brushRadius * brushRadius;
            Handles.color = new Color(color.r, color.g, color.b, .7f);
            foreach (TerrainFoliagePlacementData.Placement placement in data.Placements)
            {
                if (prefab != null && !eraseAllStreamedFoliage &&
                    (placement.prefabIndex < 0 || placement.prefabIndex >= data.Prefabs.Count ||
                     data.Prefabs[placement.prefabIndex] != prefab))
                    continue;
                Vector3 delta = placement.position - centre;
                if (delta.x * delta.x + delta.z * delta.z > radiusSquared)
                    continue;
                Handles.DotHandleCap(0, placement.position, Quaternion.identity,
                    HandleUtility.GetHandleSize(placement.position) * .045f, EventType.Repaint);
            }

        }

        private void SelectLikelyStorageForPrefab()
        {
            if (!prefab)
                return;
            bool usedAsStreamed = spawners.Any(item =>
                item.RuntimePlacementData.Prefabs.Contains(prefab));
            bool usedAsGrass = spawners.Any(item =>
                item.RuntimePlacementData.GrassPrefabs.Contains(prefab));
            if (usedAsGrass && !usedAsStreamed)
                placementStorage = PlacementStorage.ManagedGrass;
            else if (usedAsStreamed && !usedAsGrass)
                placementStorage = PlacementStorage.StreamedFoliage;
        }

        private static bool HorizontalBoundsOverlap(Bounds bounds, Vector3 point, float brushRadius)
        {
            if (bounds.size == Vector3.zero)
                return false;
            return point.x + brushRadius >= bounds.min.x && point.x - brushRadius <= bounds.max.x &&
                   point.z + brushRadius >= bounds.min.z && point.z - brushRadius <= bounds.max.z;
        }

        private void SaveEditedAssets()
        {
            if (editsSinceSave <= 0)
                return;
            foreach (TerrainFoliageSpawner spawner in spawners)
                if (spawner && spawner.RuntimePlacementData)
                    AssetDatabase.SaveAssetIfDirty(spawner.RuntimePlacementData);
            editsSinceSave = 0;
        }
    }
}
