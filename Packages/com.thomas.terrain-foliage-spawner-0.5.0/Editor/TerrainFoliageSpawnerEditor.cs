using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner.Editor
{
    [CustomEditor(typeof(TerrainFoliageSpawner))]
    public sealed class TerrainFoliageSpawnerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            TerrainFoliageSpawner spawner =
                (TerrainFoliageSpawner)target;

            DrawPrototypeSynchronisation(spawner);
            DrawValidationPanel(spawner);
            DrawTerrainDebug(spawner);

            EditorGUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Generate Foliage", GUILayout.Height(32f)))
                {
                    try
                    {
                        TerrainFoliageSpawner.GenerationReport report;
                        try
                        {
                            report = spawner.Generate((progress, message) =>
                                EditorUtility.DisplayCancelableProgressBar(
                                    "Terrain Foliage Spawner",
                                    message,
                                    progress));
                        }
                        finally
                        {
                            EditorUtility.ClearProgressBar();
                        }

                        EditorUtility.SetDirty(spawner);
                        if (spawner.RuntimePlacementData != null)
                        {
                            EditorUtility.SetDirty(spawner.RuntimePlacementData);
                            AssetDatabase.SaveAssetIfDirty(spawner.RuntimePlacementData);
                        }
                        SceneView.RepaintAll();

                        if (report.Spawned > 0)
                        {
                            Debug.Log(
                                $"Terrain Foliage Spawner generated " +
                                $"{report.Spawned:N0} placements.\n{report.ToSummary()}",
                                spawner);
                        }
                        else
                        {
                            string debugReport = spawner.GetDebugSampleReport();

                            Debug.LogWarning(
                                "Terrain Foliage Spawner generated 0 placements.\n" +
                                report.ToSummary() +
                                "\nDebug sample:\n" +
                                debugReport,
                                spawner);

                            EditorUtility.DisplayDialog(
                                "No Foliage Generated",
                                report.ToSummary() +
                                "\nDebug sample:\n" +
                                debugReport,
                                "OK");
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception, spawner);
                        EditorUtility.DisplayDialog(
                            "Terrain Foliage Spawner",
                            exception.Message,
                            "OK");
                    }
                }

                if (GUILayout.Button(
                        "Generate All Active Terrain Foliage",
                        GUILayout.Height(28f)))
                {
                    GenerateAllActiveSpawners();
                }

                if (GUILayout.Button("Clear Generated Foliage and Details"))
                {
                    spawner.Clear();
                    EditorUtility.SetDirty(spawner);
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "Managed Instanced Grass is generated and rendered directly by this " +
                "foliage system; it does not need to be added to Terrain Paint Details. " +
                "Terrain Detail and Terrain Tree prototypes can be collected automatically from the assigned rules.",
                MessageType.Info);
        }

        private static void DrawPrototypeSynchronisation(TerrainFoliageSpawner spawner)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Prototype Synchronisation", EditorStyles.boldLabel);

            int terrainCount = spawner.GetPrototypeTargetTerrains().Count;
            string sourceName = spawner.PrototypeLibrary != null &&
                                spawner.PrototypeLibrary.TemplateTerrainData != null
                ? spawner.PrototypeLibrary.TemplateTerrainData.name
                : "Assigned Terrain fallback";

            EditorGUILayout.HelpBox(
                $"Target terrains: {terrainCount:N0}\n" +
                $"Prototype settings source: {sourceName}\n" +
                $"Auto-find missing prototypes from rules: {(spawner.AutoFindPrototypesFromRules ? "enabled" : "disabled")}\n" +
                "Synchronization is append-only: existing prototype indices are preserved and nothing is removed or reordered.",
                MessageType.None);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                bool autoFind = GUILayout.Button("Auto Find Details and Trees From Rules");
                bool synchronise = GUILayout.Button("Synchronise Required Prototypes Now");
                if (autoFind || synchronise)
                {
                    Undo.RecordObjects(
                        spawner.GetPrototypeTargetTerrains()
                            .FindAll(item => item != null && item.terrainData != null)
                            .ConvertAll(item => (UnityEngine.Object)item.terrainData)
                            .ToArray(),
                        "Synchronise Terrain Foliage Prototypes");

                    TerrainFoliageSpawner.PrototypeSyncReport report =
                        spawner.SynchroniseRequiredPrototypes(autoFind);

                    List<Terrain> terrains = spawner.GetPrototypeTargetTerrains();
                    for (int i = 0; i < terrains.Count; i++)
                    {
                        if (terrains[i] == null || terrains[i].terrainData == null) continue;
                        EditorUtility.SetDirty(terrains[i].terrainData);
                        EditorUtility.SetDirty(terrains[i]);
                    }

                    AssetDatabase.SaveAssets();
                    SceneView.RepaintAll();

                    if (report.HasErrors)
                        Debug.LogWarning(report.ToSummary(), spawner);
                    else
                        Debug.Log(report.ToSummary(), spawner);

                    EditorUtility.DisplayDialog(
                        "Prototype Synchronisation",
                        report.ToSummary(),
                        "OK");
                }
            }
        }

        private static void GenerateAllActiveSpawners()
        {
            TerrainFoliageSpawner[] spawners =
                UnityEngine.Object.FindObjectsByType<TerrainFoliageSpawner>(
                    FindObjectsInactive.Include);

            if (spawners == null || spawners.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Terrain Foliage Spawner",
                    "No Terrain Foliage Spawners were found in the loaded scenes.",
                    "OK");
                return;
            }

            Array.Sort(
                spawners,
                (left, right) =>
                    string.CompareOrdinal(
                        left != null ? left.name : string.Empty,
                        right != null ? right.name : string.Empty));

            int generatedPlacements = 0;

            try
            {
                for (int i = 0; i < spawners.Length; i++)
                {
                    TerrainFoliageSpawner spawner = spawners[i];

                    if (spawner == null ||
                        !spawner.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    TerrainFoliageSpawner.GenerationReport report =
                        spawner.Generate(
                            (progress, message) =>
                                EditorUtility.DisplayCancelableProgressBar(
                                    "Generating All Terrain Foliage",
                                    $"Terrain {i + 1:N0} of {spawners.Length:N0}: {message}",
                                    (i + progress) / spawners.Length));

                    generatedPlacements += report.Spawned;
                    EditorUtility.SetDirty(spawner);

                    if (spawner.Terrain != null &&
                        spawner.Terrain.terrainData != null)
                    {
                        EditorUtility.SetDirty(spawner.Terrain.terrainData);
                    }

                    if (spawner.RuntimePlacementData != null)
                        EditorUtility.SetDirty(spawner.RuntimePlacementData);

                    if (report.Cancelled)
                        break;
                }

                AssetDatabase.SaveAssets();
                SceneView.RepaintAll();

                Debug.Log(
                    $"Terrain Foliage Spawner generated {generatedPlacements:N0} placements across " +
                    $"{spawners.Length:N0} loaded terrain spawners.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Generate All Terrain Foliage",
                    exception.Message,
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void DrawTerrainDebug(TerrainFoliageSpawner spawner)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Terrain Debug Sample",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                spawner.GetDebugSampleReport(),
                MessageType.None);

            if (GUILayout.Button("Frame Debug Sample"))
            {
                SceneView.lastActiveSceneView?.Frame(
                    new Bounds(
                        spawner.GetDebugSampleWorldPosition(),
                        Vector3.one * Mathf.Max(1f, spawner.DebugMarkerSize * 4f)),
                    false);
            }
        }

        private static void DrawValidationPanel(TerrainFoliageSpawner spawner)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Validation and Cost Estimate", EditorStyles.boldLabel);

            if (spawner.Terrain == null)
            {
                EditorGUILayout.HelpBox("Assign a Terrain before generating.", MessageType.Error);
                return;
            }

            if (spawner.Terrain.terrainData == null)
            {
                EditorGUILayout.HelpBox("The assigned Terrain has no TerrainData.", MessageType.Error);
                return;
            }

            TerrainFoliageSpawner.GenerationEstimate estimate = spawner.GetGenerationEstimate();
            EditorGUILayout.HelpBox(
                $"Estimated grid cells: {estimate.GridCells:N0}\n" +
                $"Estimated rule evaluations: {estimate.RuleEvaluations:N0}\n" +
                $"Managed grass entries: {estimate.InstancedGrassEntries:N0}\n" +
                $"Detail layers written in batches: {estimate.DetailLayersWritten:N0}\n" +
                $"Detail resolution: {estimate.DetailResolution:N0}\n" +
                $"Scene masks: {(spawner.AutomaticallyFindSceneMasks ? "auto-discovered" : "manual lists")}",
                MessageType.None);

            if (spawner.Rules == null || spawner.Rules.Count == 0)
            {
                EditorGUILayout.HelpBox("Add at least one foliage rule.", MessageType.Error);
                return;
            }

            TerrainLayer[] terrainLayers =
                spawner.Terrain.terrainData != null
                    ? spawner.Terrain.terrainData.terrainLayers
                    : null;

            for (int i = 0; i < spawner.Rules.Count; i++)
            {
                TerrainFoliageRule rule = spawner.Rules[i];

                if (rule == null)
                {
                    EditorGUILayout.HelpBox(
                        $"Rule element {i} is empty.",
                        MessageType.Warning);
                    continue;
                }

                if (rule.terrainLayer == null)
                {
                    EditorGUILayout.HelpBox(
                        $"Rule '{rule.name}' has no Terrain Layer assigned.",
                        MessageType.Warning);
                    continue;
                }

                bool found = false;
                if (terrainLayers != null)
                {
                    for (int layerIndex = 0;
                         layerIndex < terrainLayers.Length;
                         layerIndex++)
                    {
                        if (terrainLayers[layerIndex] == rule.terrainLayer)
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    EditorGUILayout.HelpBox(
                        $"Rule '{rule.name}' uses '{rule.terrainLayer.name}', " +
                        "but that exact Terrain Layer asset is not assigned to the Terrain.",
                        MessageType.Error);
                }

                if (rule.useSlopeLimit &&
                    rule.minimumSlope > rule.maximumSlope)
                {
                    EditorGUILayout.HelpBox(
                        $"Rule '{rule.name}' has Minimum Slope above Maximum Slope.",
                        MessageType.Error);
                }

                if (rule.prefabEntries == null || rule.prefabEntries.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Rule '{rule.name}' has no prefabs.",
                        MessageType.Warning);
                }
                else
                {
                    bool hasUsablePrefab = false;
                    DetailPrototype[] detailPrototypes =
                        spawner.Terrain.terrainData != null
                            ? spawner.Terrain.terrainData.detailPrototypes
                            : null;
                    TreePrototype[] treePrototypes =
                        spawner.Terrain.terrainData != null
                            ? spawner.Terrain.terrainData.treePrototypes
                            : null;

                    for (int prefabIndex = 0;
                         prefabIndex < rule.prefabEntries.Count;
                         prefabIndex++)
                    {
                        TerrainFoliagePrefabEntry entry =
                            rule.prefabEntries[prefabIndex];

                        if (entry == null ||
                            entry.prefab == null ||
                            entry.weight <= 0f)
                        {
                            continue;
                        }

                        hasUsablePrefab = true;

                        if (entry.outputMode ==
                            TerrainFoliageOutputMode.TerrainDetail &&
                            TerrainFoliageSpawner.FindDetailPrototypeIndex(
                                detailPrototypes,
                                entry.prefab) < 0)
                        {
                            EditorGUILayout.HelpBox(
                                $"Rule '{rule.name}' uses '{entry.prefab.name}' " +
                                "as an Existing Terrain Detail, but that exact prefab " +
                                "is not registered in this Terrain's Paint Details list.",
                                MessageType.Error);
                        }

                        if (entry.outputMode ==
                            TerrainFoliageOutputMode.TerrainTree &&
                            TerrainFoliageSpawner.FindTreePrototypeIndex(
                                treePrototypes, entry.prefab) < 0)
                        {
                            EditorGUILayout.HelpBox(
                                $"Rule '{rule.name}' uses '{entry.prefab.name}' " +
                                "as an Existing Terrain Tree, but that exact prefab " +
                                "is not registered in this Terrain's Paint Trees list.",
                                MessageType.Error);
                        }
                    }

                    if (!hasUsablePrefab)
                    {
                        EditorGUILayout.HelpBox(
                            $"Rule '{rule.name}' has no prefab with a weight above zero.",
                            MessageType.Warning);
                    }
                }
            }
        }

        private void OnSceneGUI()
        {
            TerrainFoliageSpawner spawner =
                (TerrainFoliageSpawner)target;

            DrawDebugSample(spawner);
            DrawPlacementPreview(spawner);
        }

        private static void DrawDebugSample(
            TerrainFoliageSpawner spawner)
        {
            if (!spawner.ShowDebugSample ||
                spawner.Terrain == null ||
                spawner.Terrain.terrainData == null)
            {
                return;
            }

            Vector3 position = spawner.GetDebugSampleWorldPosition();
            float size = Mathf.Max(0.05f, spawner.DebugMarkerSize);

            Handles.DrawWireDisc(position, Vector3.up, size);
            Handles.DrawLine(
                position,
                position + Vector3.up * size * 2f);

            Handles.Label(
                position + Vector3.up * size * 2.2f,
                "Terrain Debug Sample");
        }

        private static void DrawPlacementPreview(
            TerrainFoliageSpawner spawner)
        {
            if (!spawner.ShowPlacementPreview)
            {
                return;
            }

            List<TerrainFoliageSpawner.PreviewPoint> points =
                spawner.BuildPlacementPreview();

            float size = Mathf.Max(0.01f, spawner.PreviewMarkerSize);

            foreach (TerrainFoliageSpawner.PreviewPoint point in points)
            {
                switch (point.Status)
                {
                    case TerrainFoliageSpawner.PreviewStatus.Valid:
                    {
                        float density = spawner.PreviewRule != null &&
                                        spawner.PreviewRule.useCliffFormationDistribution
                            ? point.CliffFormationDensity
                            : point.NaturalTreeDensity;
                        Handles.color = Color.Lerp(
                            new Color(1f, 0.38f, 0.08f),
                            new Color(0.1f, 0.9f, 0.2f),
                            density);
                        break;
                    }

                    case TerrainFoliageSpawner.PreviewStatus.RejectedBySlope:
                        Handles.color = Color.red;
                        break;

                    case TerrainFoliageSpawner.PreviewStatus.RejectedByLayerWeight:
                        Handles.color = Color.blue;
                        break;

                    case TerrainFoliageSpawner.PreviewStatus.RejectedByCliffFormation:
                        Handles.color = Color.cyan;
                        break;

                    default:
                        Handles.color = Color.magenta;
                        break;
                }

                Handles.DotHandleCap(
                    0,
                    point.Position,
                    Quaternion.identity,
                    size,
                    EventType.Repaint);
            }

            Handles.color = Color.white;
        }
    }
}
