using UnityEditor;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner.Editor
{
    [CustomEditor(typeof(TerrainFoliageSplineClearance))]
    public sealed class TerrainFoliageSplineClearanceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            TerrainFoliageSplineClearance clearance =
                (TerrainFoliageSplineClearance)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Runtime Beta Test", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "After the player confirms a newly added spline section, call ActivateLatestSection(). " +
                "Only the affected Terrain Detail rectangle is read and written. Runtime TerrainData cloning " +
                "is enabled by default to protect the source asset.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Activate Latest Spline Section", GUILayout.Height(28f)))
                    clearance.ActivateLatestSection();

                if (GUILayout.Button("Restore Runtime Clearance"))
                    clearance.RestoreRuntimeClearance();

                if (GUILayout.Button("Commit Runtime Clearance"))
                    clearance.CommitRuntimeClearance();
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to use the runtime test controls.",
                    MessageType.None);
            }
        }
    }
}
