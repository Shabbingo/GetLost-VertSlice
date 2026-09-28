using UnityEditor;
using UnityEngine;

namespace Tom.PathCreator.Editor
{
    [CustomEditor(typeof(TerrainFoliageClearance))]
    public sealed class TerrainFoliageClearanceEditor :
        UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            TerrainFoliageClearance clearance =
                (TerrainFoliageClearance)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Clearance Preview",
                EditorStyles.boldLabel);

            int previewCount =
                clearance.PreviewGeneratedFoliageCount();

            EditorGUILayout.LabelField(
                "Generated Instances Matching",
                previewCount.ToString());

            if (clearance.GeneratedFoliageRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "No Generated Foliage root was found. " +
                    "Assign it manually or use the find button below.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "The direct children of Generated Foliage are treated as foliage-rule holders. " +
                    "The direct children of each rule are treated as removable generated instances.",
                    MessageType.Info);
            }

            if (GUILayout.Button(
                "Find Generated Foliage Child"))
            {
                Undo.RecordObject(
                    clearance,
                    "Find Generated Foliage Child");

                clearance.TryFindGeneratedFoliageRoot();
                EditorUtility.SetDirty(clearance);
            }

            if (previewCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{previewCount} generated foliage instance(s) are currently inside the path clearance area.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Terrain Backup",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                clearance.HasBackup
                    ? "A TerrainData backup is stored on this component."
                    : "No backup exists yet. Applying clearance creates one automatically.",
                clearance.HasBackup
                    ? MessageType.Info
                    : MessageType.Warning);

            EditorGUILayout.LabelField(
                "Disabled Generated Instances",
                clearance.DisabledObjectCount.ToString());

            if (GUILayout.Button("Apply Path Clearance"))
            {
                Object undoTarget =
                    clearance.Terrain != null
                        ? clearance.Terrain.terrainData
                        : clearance;

                Undo.RegisterCompleteObjectUndo(
                    undoTarget,
                    "Apply Path Clearance");

                clearance.ApplyClearance();
                EditorUtility.SetDirty(clearance);

                if (clearance.Terrain != null)
                {
                    EditorUtility.SetDirty(
                        clearance.Terrain.terrainData);
                }

                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(
                !clearance.HasBackup))
            {
                if (GUILayout.Button(
                    "Restore Terrain and Disabled Instances"))
                {
                    Object undoTarget =
                        clearance.Terrain != null
                            ? clearance.Terrain.terrainData
                            : clearance;

                    Undo.RegisterCompleteObjectUndo(
                        undoTarget,
                        "Restore Path Clearance");

                    clearance.RestoreBackup();
                    EditorUtility.SetDirty(clearance);

                    if (clearance.Terrain != null)
                    {
                        EditorUtility.SetDirty(
                            clearance.Terrain.terrainData);
                    }

                    SceneView.RepaintAll();
                }
            }

            using (new EditorGUI.DisabledScope(
                clearance.DisabledObjectCount == 0))
            {
                if (GUILayout.Button(
                    "Restore Disabled Instances Only"))
                {
                    clearance.RestoreDisabledObjects();
                    EditorUtility.SetDirty(clearance);
                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button(
                "Refresh Backup From Current Terrain"))
            {
                clearance.RefreshBackup();
                EditorUtility.SetDirty(clearance);
            }

            using (new EditorGUI.DisabledScope(
                !clearance.HasBackup))
            {
                if (GUILayout.Button("Clear Stored Backup"))
                {
                    clearance.ClearBackup();
                    EditorUtility.SetDirty(clearance);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Use Disable mode while testing. Destroy mode permanently removes generated instances.",
                MessageType.Warning);
        }
    }
}
