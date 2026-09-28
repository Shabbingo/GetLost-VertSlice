using UnityEditor;
using UnityEngine;

namespace Tom.PathCreator.Editor
{
    [CustomEditor(typeof(PathCreator))]
    public sealed class PathCreatorEditor : UnityEditor.Editor
    {
        private int selectedAnchorIndex = -1;
        private PathCreator Path => (PathCreator)target;

        private void OnEnable()
        {
            Undo.undoRedoPerformed += HandleUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("closed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("previewResolutionPerSegment"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lengthResolutionPerSegment"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("drawGizmos"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("pathColor"));

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Path Information", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Anchors", Path.AnchorCount.ToString());
            EditorGUILayout.LabelField("Segments", Path.SegmentCount.ToString());
            EditorGUILayout.LabelField("Approx. Length", $"{Path.ApproximateLength:0.00} m");

            EditorGUILayout.Space();

            if (selectedAnchorIndex >= 0 && selectedAnchorIndex < Path.AnchorCount)
            {
                DrawSelectedAnchorInspector();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Select an anchor in the Scene view to edit its handle mode.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Add Anchor At End"))
            {
                Undo.RecordObject(Path, "Add Path Anchor");
                selectedAnchorIndex = Path.AddAnchorAfterEnd();
                EditorUtility.SetDirty(Path);
                SceneView.RepaintAll();
            }

            using (new EditorGUI.DisabledScope(
                selectedAnchorIndex < 0 || selectedAnchorIndex >= Path.AnchorCount))
            {
                if (GUILayout.Button("Insert Anchor After Selected"))
                {
                    Undo.RecordObject(Path, "Insert Path Anchor");
                    selectedAnchorIndex = Path.InsertAnchorAfter(selectedAnchorIndex);
                    EditorUtility.SetDirty(Path);
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Delete Selected Anchor"))
                {
                    Undo.RecordObject(Path, "Delete Path Anchor");
                    int deletedIndex = selectedAnchorIndex;
                    Path.DeleteAnchor(deletedIndex);
                    selectedAnchorIndex = Mathf.Clamp(deletedIndex - 1, -1, Path.AnchorCount - 1);
                    EditorUtility.SetDirty(Path);
                    SceneView.RepaintAll();
                }
            }

            if (GUILayout.Button("Reset Path"))
            {
                Undo.RecordObject(Path, "Reset Path");
                Path.ResetPath();
                selectedAnchorIndex = -1;
                EditorUtility.SetDirty(Path);
                SceneView.RepaintAll();
            }
        }

        private void DrawSelectedAnchorInspector()
        {
            PathAnchor anchor = Path.GetAnchor(selectedAnchorIndex);

            EditorGUILayout.LabelField(
                $"Selected Anchor {selectedAnchorIndex}",
                EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            PathHandleMode newMode = (PathHandleMode)EditorGUILayout.EnumPopup(
                "Handle Mode",
                anchor.mode);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Path, "Change Handle Mode");
                Path.SetHandleMode(selectedAnchorIndex, newMode);
                EditorUtility.SetDirty(Path);
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                "Free: handles move independently.\n" +
                "Aligned: opposite handle stays collinear.\n" +
                "Mirrored: opposite handle stays collinear and equal length.\n" +
                "Automatic: handles are calculated from neighbouring anchors.",
                MessageType.None);
        }

        private void OnSceneGUI()
        {
            if (Path.AnchorCount == 0)
            {
                return;
            }

            for (int i = 0; i < Path.AnchorCount; i++)
            {
                DrawAnchor(i);
            }
        }

        private void DrawAnchor(int index)
        {
            PathAnchor anchor = Path.GetAnchor(index);
            Vector3 anchorWorld = Path.GetAnchorWorldPosition(index);
            Vector3 inWorld = Path.GetHandleInWorldPosition(index);
            Vector3 outWorld = Path.GetHandleOutWorldPosition(index);
            float anchorSize = HandleUtility.GetHandleSize(anchorWorld) * 0.11f;
            float handleSize = anchorSize * 0.7f;

            Handles.color = index == selectedAnchorIndex
                ? new Color(1f, 0.95f, 0.2f)
                : new Color(1f, 0.45f, 0.05f);

            if (Handles.Button(
                anchorWorld,
                Quaternion.identity,
                anchorSize,
                anchorSize,
                Handles.SphereHandleCap))
            {
                selectedAnchorIndex = index;
                Repaint();
                SceneView.RepaintAll();
            }

            if (index != selectedAnchorIndex)
            {
                return;
            }

            Handles.color = new Color(1f, 1f, 1f, 0.35f);
            Handles.DrawLine(anchorWorld, inWorld);
            Handles.DrawLine(anchorWorld, outWorld);

            EditorGUI.BeginChangeCheck();
            Vector3 movedAnchor = Handles.PositionHandle(
                anchorWorld,
                Tools.pivotRotation == PivotRotation.Local
                    ? Path.transform.rotation
                    : Quaternion.identity);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Path, "Move Path Anchor");
                Path.SetAnchorWorldPosition(index, movedAnchor);
                EditorUtility.SetDirty(Path);
            }

            if (anchor.mode != PathHandleMode.Automatic)
            {
                Handles.color = new Color(1f, 0.85f, 0.25f);

                EditorGUI.BeginChangeCheck();
                Vector3 movedIn = Handles.FreeMoveHandle(
                    inWorld,
                    handleSize,
                    Vector3.zero,
                    Handles.SphereHandleCap);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Path, "Move Incoming Handle");
                    Path.SetHandleInWorldPosition(index, movedIn);
                    EditorUtility.SetDirty(Path);
                }

                EditorGUI.BeginChangeCheck();
                Vector3 movedOut = Handles.FreeMoveHandle(
                    outWorld,
                    handleSize,
                    Vector3.zero,
                    Handles.SphereHandleCap);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Path, "Move Outgoing Handle");
                    Path.SetHandleOutWorldPosition(index, movedOut);
                    EditorUtility.SetDirty(Path);
                }
            }

            Handles.Label(
                anchorWorld + Vector3.up * HandleUtility.GetHandleSize(anchorWorld) * 0.15f,
                $"Anchor {index} ({anchor.mode})");
        }

        private void HandleUndoRedo()
        {
            selectedAnchorIndex = Mathf.Clamp(
                selectedAnchorIndex,
                -1,
                Path.AnchorCount - 1);

            Repaint();
            SceneView.RepaintAll();
        }
    }
}
