using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Thomas.TerrainFoliageSpawner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Editor bridge from one generated world to its per-tile foliage spawners.</summary>
public static class GetLostTerrainFoliageWorkflow
{
    private const string DefaultRuleFolder =
        "Assets/TerrainSurfaces/Terrain Vegetation Rules";

    [Serializable]
    public sealed class LayerMapping
    {
        public TerrainLayer source;
        public TerrainLayer target;
    }

    public static TerrainFoliageSpawner FindTemplate()
    {
        var selected = Selection.activeGameObject
            ? Selection.activeGameObject.GetComponent<TerrainFoliageSpawner>() : null;
        if (selected && selected.Rules.Any(r => r)) return selected;
        return Object.FindObjectsByType<TerrainFoliageSpawner>(FindObjectsInactive.Include)
            .Where(s => s.gameObject.scene.IsValid() && s.hideFlags == HideFlags.None && s.Rules.Any(r => r))
            .OrderByDescending(s => s.Rules.Count(r => r)).ThenBy(s => s.name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static TerrainFoliageSpawner EnsureSettingsSource(
        GameObject root,
        TerrainFoliageSpawner preferred)
    {
        if (!root || !root.scene.IsValid())
            throw new InvalidOperationException("Choose a generated terrain world first.");

        Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true)
            .OrderBy(t => t.transform.position.z)
            .ThenBy(t => t.transform.position.x)
            .ToArray();
        if (terrains.Length == 0)
            throw new InvalidOperationException("The selected world has no Terrain chunks.");

        TerrainFoliageSpawner source = preferred;
        if (!source || !source.gameObject.scene.IsValid())
        {
            source = terrains.Select(t => t.GetComponent<TerrainFoliageSpawner>())
                .FirstOrDefault(s => s && s.Rules.Any(r => r));
        }

        if (!source)
            source = Undo.AddComponent<TerrainFoliageSpawner>(terrains[0].gameObject);

        TerrainFoliageRule[] rules = CollectSourceRules(source);
        if (rules.Length == 0)
            throw new InvalidOperationException($"No foliage rule assets were found in '{DefaultRuleFolder}'.");

        Undo.RecordObject(source, "Create Foliage Settings Source");
        var serialized = new SerializedObject(source);
        serialized.FindProperty("terrain").objectReferenceValue = terrains[0];
        var ruleList = serialized.FindProperty("rules");
        ruleList.arraySize = rules.Length;
        for (int i = 0; i < rules.Length; i++)
            ruleList.GetArrayElementAtIndex(i).objectReferenceValue = rules[i];
        serialized.FindProperty("synchronisePrototypesBeforeGeneration").boolValue = true;
        serialized.FindProperty("autoFindPrototypesFromRules").boolValue = true;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(source);
        EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
        return source;
    }

    public static void UpdateMappings(TerrainFoliageSpawner template, TerrainLayer[] targets, List<LayerMapping> mappings)
    {
        var sources = template ? CollectSourceRules(template).Where(r => r && r.terrainLayer)
            .Select(r => r.terrainLayer).Distinct().ToArray() : Array.Empty<TerrainLayer>();
        mappings.RemoveAll(m => !sources.Contains(m.source));
        foreach (TerrainLayer source in sources)
        {
            var mapping = mappings.FirstOrDefault(m => m.source == source);
            if (mapping == null)
            {
                mapping = new LayerMapping { source = source, target = SuggestLayer(source, targets) };
                mappings.Add(mapping);
            }
            else if (mapping.target && !targets.Contains(mapping.target))
                mapping.target = SuggestLayer(source, targets);
        }
    }

    static TerrainLayer SuggestLayer(TerrainLayer source, TerrainLayer[] targets)
    {
        if (targets.Contains(source)) return source;
        string name = source.name.ToLowerInvariant();
        var exact = targets.FirstOrDefault(t => string.Equals(t.name, source.name, StringComparison.OrdinalIgnoreCase));
        if (exact) return exact;
        string token = name.Contains("grass") ? "grass" : name.Contains("rock") ? "rock" :
            name.Contains("mud") || name.Contains("pebble") || name.Contains("soil") ? "dirt" : null;
        return token == null ? null : targets.FirstOrDefault(t => t.name.ToLowerInvariant().Contains(token));
    }

    static TerrainFoliageRule[] CollectSourceRules(TerrainFoliageSpawner template)
    {
        var result = template
            ? template.Rules.Where(r => r).Distinct().ToList()
            : new List<TerrainFoliageRule>();
        var names = new HashSet<string>(
            result.Select(r => RuleIdentity(r.name)),
            StringComparer.OrdinalIgnoreCase);

        string[] guids = AssetDatabase.FindAssets(
            "t:TerrainFoliageRule",
            new[] { DefaultRuleFolder });
        foreach (string path in guids.Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => string.Equals(
                Path.GetDirectoryName(path)?.Replace('\\', '/'),
                DefaultRuleFolder,
                StringComparison.Ordinal))
            .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith(
                "JUST", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.IndexOf(
                "LOD TESTING", StringComparison.OrdinalIgnoreCase) < 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var rule = AssetDatabase.LoadAssetAtPath<TerrainFoliageRule>(path);
            if (rule && names.Add(RuleIdentity(rule.name))) result.Add(rule);
        }

        return result.ToArray();
    }

    static string RuleIdentity(string name)
    {
        const string suffix = " (World)";
        return name != null && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name.Substring(0, name.Length - suffix.Length)
            : name ?? string.Empty;
    }

    public static string CreateAndSynchronise(
        GameObject root,
        TerrainFoliageSpawner template,
        List<LayerMapping> mappings)
    {
        return Run(root, template, mappings, false);
    }

    public static string Generate(GameObject root, TerrainFoliageSpawner template, List<LayerMapping> mappings)
    {
        return Run(root, template, mappings, true);
    }

    static string Run(
        GameObject root,
        TerrainFoliageSpawner template,
        List<LayerMapping> mappings,
        bool generatePlacements)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Set up foliage in Edit Mode.");
        if (!root || !root.scene.IsValid() || !template)
            throw new InvalidOperationException("Choose a terrain world in the scene and a configured foliage settings source.");
        Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true)
            .OrderBy(t => t.transform.position.z).ThenBy(t => t.transform.position.x).ToArray();
        TerrainFoliageRule[] sourceRules = CollectSourceRules(template);
        if (terrains.Length == 0 || sourceRules.Length == 0)
            throw new InvalidOperationException("The world needs Terrain tiles and the settings source needs assigned foliage rules.");
        foreach (var rule in sourceRules)
        {
            if (!rule.terrainLayer || !mappings.Any(m => m.source == rule.terrainLayer && m.target))
                throw new InvalidOperationException($"Choose a generated surface for rule '{rule.name}'.");
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(rule)))
                throw new InvalidOperationException($"Save foliage rule '{rule.name}' as an asset first.");
        }
        var dataSet = new HashSet<TerrainData>();
        string terrainFolder = null;
        foreach (Terrain terrain in terrains)
        {
            if (!terrain.terrainData || !dataSet.Add(terrain.terrainData))
                throw new InvalidOperationException("Each generated tile must have its own TerrainData.");
            string path = AssetDatabase.GetAssetPath(terrain.terrainData);
            string directory = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory) || !directory.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Save the world's TerrainData assets under Assets before generating foliage.");
            if (terrainFolder != null && terrainFolder != directory)
                throw new InvalidOperationException("Select one generated world whose TerrainData assets share an output folder.");
            terrainFolder = directory;
            foreach (var mapping in mappings)
                if (!terrain.terrainData.terrainLayers.Contains(mapping.target))
                    throw new InvalidOperationException($"'{terrain.name}' does not contain mapped surface '{mapping.target?.name}'.");
        }
        if (Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include)
            .Any(t => !terrains.Contains(t) && dataSet.Contains(t.terrainData)))
            throw new InvalidOperationException("This world's TerrainData is also used outside the selected root. Give the world independent TerrainData first.");

        string folder = terrainFolder + "/Foliage";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(terrainFolder, "Foliage");
        List<TerrainFoliageRule> worldRules = new();
        foreach (var source in sourceRules)
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string guid = AssetDatabase.AssetPathToGUID(sourcePath);
            string path = sourcePath.StartsWith(folder + "/Rule-", StringComparison.Ordinal)
                ? sourcePath : $"{folder}/Rule-{guid}.asset";
            var copy = AssetDatabase.LoadAssetAtPath<TerrainFoliageRule>(path);
            if (!copy)
            {
                copy = ScriptableObject.CreateInstance<TerrainFoliageRule>();
                AssetDatabase.CreateAsset(copy, path);
            }
            Undo.RecordObject(copy, "Configure World Foliage Rule");
            if (source != copy)
            {
                EditorUtility.CopySerialized(source, copy);
                copy.name = source.name + " (World)";
            }
            copy.terrainLayer = mappings.First(m => m.source == source.terrainLayer).target;
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            worldRules.Add(copy);
        }

        var prototypeLibrary = template.PrototypeLibrary;
        if (!prototypeLibrary && template.Terrain && template.Terrain.terrainData)
        {
            string path = folder + "/Prototype Library.asset";
            prototypeLibrary = AssetDatabase.LoadAssetAtPath<TerrainFoliagePrototypeLibrary>(path);
            if (!prototypeLibrary)
            {
                prototypeLibrary = ScriptableObject.CreateInstance<TerrainFoliagePrototypeLibrary>();
                AssetDatabase.CreateAsset(prototypeLibrary, path);
            }
            var librarySettings = new SerializedObject(prototypeLibrary);
            librarySettings.FindProperty("templateTerrainData").objectReferenceValue = template.Terrain.terrainData;
            librarySettings.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(prototypeLibrary);
        }

        // Freeze settings: the selected source can itself be a tile in this world.
        GameObject snapshotObject = new("Foliage Settings Snapshot") { hideFlags = HideFlags.HideAndDontSave };
        StringBuilder report = new();
        int completed = 0, placements = 0;
        bool cancelled = false;
        try
        {
            var snapshot = snapshotObject.AddComponent<TerrainFoliageSpawner>();
            EditorUtility.CopySerialized(template, snapshot);
            var settings = new SerializedObject(snapshot);
            bool needsDetails = worldRules.Any(r => r.prefabEntries != null && r.prefabEntries.Any(e =>
                e != null && e.prefab && e.weight > 0f && e.outputMode == TerrainFoliageOutputMode.TerrainDetail));
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (EditorUtility.DisplayCancelableProgressBar("World Foliage", $"Preparing {terrain.name}", i / (float)terrains.Length))
                { cancelled = true; break; }
                Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Generate World Foliage");
                if (needsDetails && terrain.terrainData.detailResolution == 0)
                    terrain.terrainData.SetDetailResolution(512, 16);
                var spawner = terrain.GetComponent<TerrainFoliageSpawner>();
                bool existing = spawner;
                if (!spawner) spawner = Undo.AddComponent<TerrainFoliageSpawner>(terrain.gameObject);
                Undo.RecordObject(spawner, "Configure World Foliage");
                var destination = new SerializedObject(spawner);
                var property = settings.GetIterator();
                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (property.name.StartsWith("m_", StringComparison.Ordinal) || property.name == "terrain" ||
                        property.name == "rules" || property.name == "runtimePlacementData" ||
                        property.name == "prototypeTargetTerrains" ||
                        (existing && property.name == "generatedRootName")) continue;
                    destination.CopyFromSerializedProperty(property);
                }
                destination.FindProperty("terrain").objectReferenceValue = terrain;
                destination.FindProperty("prototypeLibrary").objectReferenceValue = prototypeLibrary;
                destination.FindProperty("automaticallyFindActiveTerrains").boolValue = false;
                var prototypeTargets = destination.FindProperty("prototypeTargetTerrains");
                prototypeTargets.arraySize = terrains.Length;
                for (int targetIndex = 0; targetIndex < terrains.Length; targetIndex++)
                    prototypeTargets.GetArrayElementAtIndex(targetIndex).objectReferenceValue = terrains[targetIndex];
                destination.FindProperty("synchronisePrototypesBeforeGeneration").boolValue = true;
                destination.FindProperty("autoFindPrototypesFromRules").boolValue = true;
                destination.FindProperty("createRuntimeStreamer").boolValue = true;
                destination.FindProperty("createManagedGrassRenderer").boolValue = true;
                destination.FindProperty("showPlacementPreview").boolValue = false;
                destination.FindProperty("previewRule").objectReferenceValue = null;
                var rules = destination.FindProperty("rules");
                rules.arraySize = worldRules.Count;
                for (int r = 0; r < worldRules.Count; r++) rules.GetArrayElementAtIndex(r).objectReferenceValue = worldRules[r];
                string terrainGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(terrain.terrainData));
                string placementPath = $"{folder}/Placements-{terrainGuid}.asset";
                var data = AssetDatabase.LoadAssetAtPath<TerrainFoliagePlacementData>(placementPath);
                if (!data)
                {
                    data = ScriptableObject.CreateInstance<TerrainFoliagePlacementData>();
                    AssetDatabase.CreateAsset(data, placementPath);
                }
                Undo.RegisterCompleteObjectUndo(data, "Generate World Foliage Placements");
                destination.FindProperty("runtimePlacementData").objectReferenceValue = data;
                destination.ApplyModifiedProperties();
                try
                {
                    if (!generatePlacements)
                    {
                        if (i == 0)
                        {
                            var sync = spawner.SynchroniseRequiredPrototypes(true);
                            if (sync.HasErrors)
                                throw new InvalidOperationException(sync.ToSummary());
                            report.AppendLine(sync.ToSummary());
                        }
                        completed++;
                        continue;
                    }

                    var result = spawner.Generate((progress, message) => EditorUtility.DisplayCancelableProgressBar(
                        "World Foliage", $"Tile {i + 1}/{terrains.Length} ({terrain.name}): {message}", (i + progress) / terrains.Length));
                    placements += result.Spawned;
                    report.AppendLine($"{terrain.name}:\n{result.ToSummary()}\n");
                    if (result.Cancelled) { cancelled = true; break; }
                    completed++;
                }
                finally
                {
                    EditorUtility.SetDirty(spawner);
                    EditorUtility.SetDirty(terrain.terrainData);
                    EditorUtility.SetDirty(data);
                    AssetDatabase.SaveAssetIfDirty(terrain.terrainData);
                    AssetDatabase.SaveAssetIfDirty(data);
                    EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
                }
            }
            string summary = generatePlacements
                ? $"{(cancelled ? "Cancelled" : "Foliage complete")}: {completed}/{terrains.Length} tiles completed; {placements:N0} placements. " +
                  (cancelled ? "Partial results were kept; click again to regenerate this world." : "All chunk spawners and prototypes are synchronized; save the scene to keep them.")
                : $"{(cancelled ? "Foliage setup cancelled" : "Foliage ready")}: " +
                  $"{worldRules.Count} rules are assigned to {completed}/{terrains.Length} " +
                  "terrain chunk spawners, with prototypes synchronized across the whole world.";
            if (generatePlacements)
            {
                File.WriteAllText(folder + "/Foliage Report.txt", summary + "\n\n" + report);
                AssetDatabase.ImportAsset(folder + "/Foliage Report.txt");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Get Lost Terrain] " + summary, root);
            return summary;
        }
        finally
        {
            Object.DestroyImmediate(snapshotObject);
            EditorUtility.ClearProgressBar();
            SceneView.RepaintAll();
        }
    }
}
