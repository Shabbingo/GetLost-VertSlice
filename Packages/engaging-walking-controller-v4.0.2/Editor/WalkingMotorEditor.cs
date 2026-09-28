using UnityEditor;
using UnityEngine;

namespace Tom.WalkingController.Editor
{
    [CustomEditor(typeof(WalkingMotor))]
    public sealed class WalkingMotorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                $"Engaging Walking Controller v{WalkingControllerVersion.Version}. Updates preserve component references and serialized settings.",
                MessageType.Info);

            DrawDefaultInspector();

            if (Application.isPlaying)
            {
                WalkingMotor motor = (WalkingMotor)target;
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Hidden Footing Debug", EditorStyles.boldLabel);
                Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                EditorGUI.ProgressBar(rect, motor.CurrentFooting, $"Footing {motor.CurrentFooting:0.00}");
                Repaint();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Validate and Repair References"))
                WalkingControllerUpgradeUtility.UpgradeAndValidate((WalkingMotor)target, true);
        }
    }
}
