using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GetLost.Environment;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainMicroErosion))]
public sealed class TerrainMicroErosionEditor : Editor
{
    private const string BackupMagic = "GET_LOST_TERRAIN_HEIGHTS";
    private const int BackupVersion = 1;
    private const int BackupsToKeep = 6;
    private static readonly string BackupFolder = Path.GetFullPath(
        "Library/GetLostTerrainErosionBackups");

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        TerrainMicroErosion erosion = (TerrainMicroErosion)target;
        List<Terrain> terrains = erosion.GetTargets();
        string latestBackup = GetLatestBackupPath();

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            $"Connected terrain targets: {terrains.Count}\n" +
            "The simulation treats all targets as one catchment. Runoff carves converging channels, " +
            "transports sediment downhill, and deposits it on gentler lower ground.\n\n" +
            "A height-only backup is written before every application. Unity Undo is not used as the safety system.",
            terrains.Count > 0 ? MessageType.Info : MessageType.Warning);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Load Recommended Visible Preset"))
            {
                Undo.RecordObject(erosion, "Load Terrain Erosion Preset");
                erosion.ApplyNaturalPreset();
                EditorUtility.SetDirty(erosion);
            }

            if (GUILayout.Button("Load Stronger Natural Preset"))
            {
                Undo.RecordObject(erosion, "Load Stronger Terrain Erosion Preset");
                erosion.ApplyStrongerNaturalPreset();
                EditorUtility.SetDirty(erosion);
            }
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(
                   Application.isPlaying || terrains.Count == 0))
        {
            if (GUILayout.Button(
                    "Apply Micro-Terrain Erosion",
                    GUILayout.Height(36f)))
            {
                ApplyErosion(erosion, terrains);
            }
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(
                   Application.isPlaying || string.IsNullOrEmpty(latestBackup)))
        {
            if (GUILayout.Button(
                    "Restore Most Recent Height Backup",
                    GUILayout.Height(28f)))
            {
                RestoreBackup(latestBackup);
            }
        }

        if (!string.IsNullOrEmpty(latestBackup))
        {
            EditorGUILayout.LabelField(
                "Latest backup",
                File.GetLastWriteTime(latestBackup).ToString("g"));
        }
        else
        {
            EditorGUILayout.LabelField("Latest backup", "None yet");
        }
    }

    private static void ApplyErosion(
        TerrainMicroErosion erosion,
        List<Terrain> terrains)
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Apply Micro-Terrain Erosion",
            $"Run connected erosion across {terrains.Count} TerrainData asset(s)?\n\n" +
            "A height-only backup will be created first. The operation changes heightmaps, " +
            "so terrain splats and foliage may need regenerating afterward.",
            "Back Up and Apply",
            "Cancel");

        if (!confirmed)
            return;

        string backupPath = null;
        try
        {
            backupPath = SaveHeightBackup(terrains);
            erosion.Apply((progress, message) =>
                EditorUtility.DisplayProgressBar(
                    "Micro-Terrain Erosion",
                    message,
                    progress));

            for (int i = 0; i < terrains.Count; i++)
            {
                EditorUtility.SetDirty(terrains[i].terrainData);
                EditorUtility.SetDirty(terrains[i]);
            }
            EditorUtility.SetDirty(erosion);
            SceneView.RepaintAll();
            Debug.Log(
                $"Micro-terrain erosion modified {terrains.Count} terrain(s). " +
                $"Height backup: {backupPath}. Review before saving, then regenerate terrain splats and foliage.",
                erosion);
            EditorUtility.DisplayDialog(
                "Micro-Terrain Erosion Complete",
                $"Eroded {terrains.Count} terrain(s).\n\n" +
                "Review the terrain before saving. Use Restore Most Recent Height Backup if the result is not useful.",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, erosion);
            string recoveryMessage = string.IsNullOrEmpty(backupPath)
                ? "No backup was completed, so no terrain processing was started."
                : "A backup was completed and can be restored from the erosion inspector.";
            EditorUtility.DisplayDialog(
                "Micro-Terrain Erosion Failed",
                exception.Message + "\n\n" + recoveryMessage,
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static string SaveHeightBackup(List<Terrain> terrains)
    {
        Directory.CreateDirectory(BackupFolder);
        string path = Path.Combine(
            BackupFolder,
            $"TerrainHeights_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bin");

        List<TerrainData> dataAssets = terrains
            .Where(terrain => terrain != null && terrain.terrainData != null)
            .Select(terrain => terrain.terrainData)
            .Distinct()
            .ToList();

        using (FileStream stream = new FileStream(
                   path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(BackupMagic);
            writer.Write(BackupVersion);
            writer.Write(DateTime.UtcNow.ToBinary());
            writer.Write(dataAssets.Count);
            for (int dataIndex = 0; dataIndex < dataAssets.Count; dataIndex++)
            {
                TerrainData data = dataAssets[dataIndex];
                string assetPath = AssetDatabase.GetAssetPath(data);
                if (string.IsNullOrEmpty(assetPath))
                    throw new InvalidOperationException(
                        $"TerrainData '{data.name}' is not a saved project asset.");

                int resolution = data.heightmapResolution;
                float[,] heights = data.GetHeights(0, 0, resolution, resolution);
                writer.Write(assetPath);
                writer.Write(AssetDatabase.AssetPathToGUID(assetPath));
                writer.Write(resolution);
                for (int z = 0; z < resolution; z++)
                {
                    for (int x = 0; x < resolution; x++)
                        writer.Write(heights[z, x]);
                }
            }
        }

        TrimOldBackups();
        return path;
    }

    private static void RestoreBackup(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            EditorUtility.DisplayDialog(
                "Restore Terrain Heights",
                "No erosion height backup was found.",
                "OK");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Restore Terrain Heights",
            $"Restore the heightmaps saved on {File.GetLastWriteTime(path):g}?\n\n" +
            "Only heights will be restored. Trees, details, terrain layers and textures are left unchanged.",
            "Restore Heights",
            "Cancel");
        if (!confirmed)
            return;

        int restored = 0;
        try
        {
            using FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new BinaryReader(stream);
            string magic = reader.ReadString();
            int version = reader.ReadInt32();
            reader.ReadInt64();
            if (magic != BackupMagic || version != BackupVersion)
                throw new InvalidDataException("This is not a supported Get Lost terrain-height backup.");

            int terrainCount = reader.ReadInt32();
            for (int dataIndex = 0; dataIndex < terrainCount; dataIndex++)
            {
                string assetPath = reader.ReadString();
                string expectedGuid = reader.ReadString();
                int resolution = reader.ReadInt32();
                float[,] heights = new float[resolution, resolution];
                for (int z = 0; z < resolution; z++)
                {
                    for (int x = 0; x < resolution; x++)
                        heights[z, x] = reader.ReadSingle();
                }

                TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(assetPath);
                if (data == null)
                    throw new InvalidOperationException("Could not load TerrainData: " + assetPath);
                if (AssetDatabase.AssetPathToGUID(assetPath) != expectedGuid)
                    throw new InvalidOperationException("TerrainData identity changed: " + assetPath);
                if (data.heightmapResolution != resolution)
                {
                    throw new InvalidOperationException(
                        $"Heightmap resolution changed for {assetPath}: " +
                        $"current {data.heightmapResolution}, backup {resolution}.");
                }

                EditorUtility.DisplayProgressBar(
                    "Restoring Terrain Heights",
                    Path.GetFileName(assetPath),
                    dataIndex / Mathf.Max(1f, terrainCount));
                Undo.RegisterCompleteObjectUndo(data, "Restore Terrain Erosion Backup");
                data.SetHeightsDelayLOD(0, 0, heights);
                data.SyncHeightmap();
                EditorUtility.SetDirty(data);
                restored++;
            }

            foreach (Terrain terrain in UnityEngine.Object.FindObjectsByType<Terrain>(
                         FindObjectsInactive.Include))
            {
                if (terrain != null)
                    terrain.Flush();
            }
            SceneView.RepaintAll();
            Debug.Log($"Restored {restored} TerrainData heightmaps from {path}.");
            EditorUtility.DisplayDialog(
                "Terrain Heights Restored",
                $"Restored {restored} heightmaps. Non-height TerrainData was preserved.",
                "OK");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Restore Terrain Heights Failed",
                $"Stopped after restoring {restored} terrain(s).\n\n{exception.Message}",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static string GetLatestBackupPath()
    {
        if (!Directory.Exists(BackupFolder))
            return null;
        return Directory.GetFiles(BackupFolder, "TerrainHeights_*.bin")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void TrimOldBackups()
    {
        string[] backups = Directory.GetFiles(BackupFolder, "TerrainHeights_*.bin")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        for (int i = BackupsToKeep; i < backups.Length; i++)
            File.Delete(backups[i]);
    }

    [MenuItem("Tools/Get Lost/Terrain/Restore Latest Erosion Height Backup")]
    private static void RestoreLatestBackupFromMenu()
    {
        RestoreBackup(GetLatestBackupPath());
    }

    [MenuItem("Tools/Get Lost/Terrain/Create Micro Terrain Erosion Tool")]
    private static void CreateTerrainErosionTool()
    {
        GameObject erosionObject = new GameObject("Terrain Micro Erosion");
        Undo.RegisterCreatedObjectUndo(
            erosionObject,
            "Create Terrain Micro Erosion Tool");
        erosionObject.AddComponent<TerrainMicroErosion>();
        Selection.activeGameObject = erosionObject;
        EditorGUIUtility.PingObject(erosionObject);
    }
}