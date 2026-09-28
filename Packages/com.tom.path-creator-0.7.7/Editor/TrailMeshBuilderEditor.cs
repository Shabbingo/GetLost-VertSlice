using UnityEditor;
using UnityEngine;

namespace Tom.PathCreator.Editor
{
    [CustomEditor(typeof(TrailMeshBuilder))]
    public sealed class TrailMeshBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();

            TrailMeshBuilder builder = (TrailMeshBuilder)target;

            if (builder.ProjectionMode ==
                TrailProjectionMode.AssignedTerrain &&
                builder.Terrain == null)
            {
                EditorGUILayout.HelpBox(
                    "Assigned Terrain projection is selected, but no Terrain is assigned.",
                    MessageType.Warning);
            }

            if (builder.ProjectionMode ==
                TrailProjectionMode.PhysicsLayers)
            {
                EditorGUILayout.HelpBox(
                    "Physics Layers projection can hit trees, rocks, props, and other colliders. " +
                    "Use Assigned Terrain for normal Unity Terrain trails.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Rebuild Trail Mesh"))
            {
                builder.Rebuild();
                EditorUtility.SetDirty(builder);
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Clear Trail Mesh"))
            {
                builder.ClearMesh();
                EditorUtility.SetDirty(builder);
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                "For Unity Terrain, set Projection Mode to Assigned Terrain and drag " +
                "the Terrain component into the Terrain field. This samples the terrain " +
                "height directly and ignores trees and object colliders.",
                MessageType.Info);
        }
    }
}
