using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tom.WalkingController.Editor
{
    public static class WalkingControllerUpgradeUtility
    {
        private const string MenuRoot = "Tools/Walking Controller/";

        [MenuItem(MenuRoot + "Validate Selected Player", priority = 1)]
        private static void ValidateSelectedPlayer()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Walking Controller", "Select the player GameObject or one of its children first.", "OK");
                return;
            }

            WalkingMotor motor = selected.GetComponentInParent<WalkingMotor>() ?? selected.GetComponentInChildren<WalkingMotor>(true);
            if (motor == null)
            {
                EditorUtility.DisplayDialog("Walking Controller", "No WalkingMotor was found on the selected hierarchy.", "OK");
                return;
            }

            UpgradeAndValidate(motor, true);
        }

        [MenuItem(MenuRoot + "Validate All Players In Open Scenes", priority = 2)]
        private static void ValidateAllPlayers()
        {
            WalkingMotor[] motors = Object.FindObjectsByType<WalkingMotor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int changed = 0;
            int warnings = 0;

            foreach (WalkingMotor motor in motors)
            {
                UpgradeResult result = UpgradeAndValidate(motor, false);
                if (result.Changed)
                    changed++;
                warnings += result.Warnings.Count;
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog(
                "Walking Controller",
                $"Checked {motors.Length} player controller(s).\nUpdated: {changed}\nWarnings: {warnings}",
                "OK");
        }

        internal static UpgradeResult UpgradeAndValidate(WalkingMotor motor, bool showDialog)
        {
            UpgradeResult result = new();
            if (motor == null)
                return result;

            GameObject root = motor.gameObject;
            Undo.RecordObject(motor, "Upgrade Walking Controller");

            CharacterController characterController = root.GetComponent<CharacterController>();
            WalkingInputReader input = root.GetComponent<WalkingInputReader>();
            GroundProbe groundProbe = root.GetComponent<GroundProbe>();
            TerrainSurfaceResolver surfaceResolver = root.GetComponent<TerrainSurfaceResolver>();
            ExertionController exertion = root.GetComponent<ExertionController>();
            VegetationInteractionReceiver vegetation = root.GetComponent<VegetationInteractionReceiver>();
            FootingFeedback footingFeedback = root.GetComponent<FootingFeedback>();
            CameraEffectsController cameraEffects = root.GetComponent<CameraEffectsController>();
            if (vegetation == null)
            {
                vegetation = Undo.AddComponent<VegetationInteractionReceiver>(root);
                result.Changed = true;
            }
            if (footingFeedback == null)
            {
                footingFeedback = Undo.AddComponent<FootingFeedback>(root);
                result.Changed = true;
            }
            if (cameraEffects == null)
            {
                cameraEffects = Undo.AddComponent<CameraEffectsController>(root);
                result.Changed = true;
            }

            if (characterController == null)
                result.Warnings.Add("CharacterController is missing.");
            if (input == null)
                result.Warnings.Add("WalkingInputReader is missing.");
            if (groundProbe == null)
                result.Warnings.Add("GroundProbe is missing.");
            if (surfaceResolver == null)
                result.Warnings.Add("TerrainSurfaceResolver is missing. Surface modifiers will not be used.");
            if (exertion == null)
                result.Warnings.Add("ExertionController is missing. Exertion will be disabled.");

            SerializedObject motorObject = new(motor);
            result.Changed |= AssignIfMissing(motorObject, "characterController", characterController);
            result.Changed |= AssignIfMissing(motorObject, "input", input);
            result.Changed |= AssignIfMissing(motorObject, "groundProbe", groundProbe);
            result.Changed |= AssignIfMissing(motorObject, "surfaceResolver", surfaceResolver);
            result.Changed |= AssignIfMissing(motorObject, "exertion", exertion);
            result.Changed |= AssignIfMissing(motorObject, "vegetation", vegetation);

            SerializedProperty cameraProperty = motorObject.FindProperty("cameraTransform");
            if (cameraProperty != null && cameraProperty.objectReferenceValue == null)
            {
                Camera childCamera = root.GetComponentInChildren<Camera>(true);
                if (childCamera != null)
                {
                    cameraProperty.objectReferenceValue = childCamera.transform;
                    result.Changed = true;
                }
                else
                {
                    result.Warnings.Add("No child Camera was found. Camera-relative movement may use the player transform instead.");
                }
            }

            Camera childCameraForEffects = root.GetComponentInChildren<Camera>(true);
            Transform effectsPivot = null;
            if (cameraEffects != null)
            {
                SerializedObject effectsObject = new(cameraEffects);
                SerializedProperty effectsCamera = effectsObject.FindProperty("cameraTransform");
                SerializedProperty effectsPivotProperty = effectsObject.FindProperty("effectsPivot");

                if (effectsCamera != null && effectsCamera.objectReferenceValue == null && childCameraForEffects != null)
                {
                    effectsCamera.objectReferenceValue = childCameraForEffects.transform;
                    result.Changed = true;
                }

                effectsPivot = effectsPivotProperty != null ? effectsPivotProperty.objectReferenceValue as Transform : null;
                if (effectsPivot == null && childCameraForEffects != null)
                {
                    effectsPivot = CreateEffectsPivot(childCameraForEffects.transform);
                    if (effectsPivotProperty != null)
                        effectsPivotProperty.objectReferenceValue = effectsPivot;
                    result.Changed = true;
                }
                else if (childCameraForEffects == null)
                {
                    result.Warnings.Add("CameraEffectsController could not be configured because no child Camera was found.");
                }

                if (effectsObject.hasModifiedProperties)
                {
                    effectsObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(cameraEffects);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(cameraEffects);
                }
            }

            if (footingFeedback != null)
            {
                SerializedObject feedbackObject = new(footingFeedback);
                result.Changed |= AssignIfMissing(feedbackObject, "motor", motor);
                result.Changed |= AssignIfMissing(feedbackObject, "cameraEffects", cameraEffects);

                if (feedbackObject.hasModifiedProperties)
                {
                    feedbackObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(footingFeedback);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(footingFeedback);
                }
            }

            WalkingCameraBob cameraBob = root.GetComponentInChildren<WalkingCameraBob>(true);
            if (cameraBob != null)
            {
                SerializedObject bobObject = new(cameraBob);
                result.Changed |= AssignIfMissing(bobObject, "motor", motor);
                result.Changed |= AssignIfMissing(bobObject, "cameraEffects", cameraEffects);
                if (bobObject.hasModifiedProperties)
                {
                    bobObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(cameraBob);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(cameraBob);
                }
            }

            if (result.Changed)
            {
                motorObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(motor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
            }

            ValidateInput(input, result.Warnings);
            ValidateGroundProbe(groundProbe, result.Warnings);

            TerrainDetailVegetationDetector legacyDetector = root.GetComponent<TerrainDetailVegetationDetector>();
            if (legacyDetector != null)
                result.Warnings.Add("Legacy TerrainDetailVegetationDetector found. Replace it with TerrainDetailVegetationProvider and assign a Terrain Detail Vegetation Mapping asset.");

            TerrainDetailVegetationProvider provider = root.GetComponent<TerrainDetailVegetationProvider>();
            if (provider == null)
                result.Warnings.Add("TerrainDetailVegetationProvider is not installed. Painted Terrain details will not affect movement until it is added and mapped.");

            if (showDialog)
            {
                string warningText = result.Warnings.Count == 0
                    ? "No problems found. Existing settings were preserved."
                    : string.Join("\n• ", result.Warnings);

                EditorUtility.DisplayDialog(
                    "Walking Controller v" + WalkingControllerVersion.Version,
                    (result.Changed ? "Missing references were repaired.\n\n" : "No reference changes were required.\n\n") +
                    (result.Warnings.Count > 0 ? "• " : string.Empty) + warningText,
                    "OK");
            }

            return result;
        }

        private static Transform CreateEffectsPivot(Transform cameraTransform)
        {
            Transform oldParent = cameraTransform.parent;
            Vector3 oldLocalPosition = cameraTransform.localPosition;
            Quaternion oldLocalRotation = cameraTransform.localRotation;
            Vector3 oldLocalScale = cameraTransform.localScale;

            GameObject pivotObject = new("Camera Effects Pivot");
            Undo.RegisterCreatedObjectUndo(pivotObject, "Create Camera Effects Pivot");
            Transform pivot = pivotObject.transform;
            Undo.SetTransformParent(pivot, oldParent, "Parent Camera Effects Pivot");
            pivot.localPosition = oldLocalPosition;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            Undo.SetTransformParent(cameraTransform, pivot, "Move Camera Under Effects Pivot");
            cameraTransform.localPosition = Vector3.zero;
            cameraTransform.localRotation = oldLocalRotation;
            cameraTransform.localScale = oldLocalScale;

            return pivot;
        }

        private static bool AssignIfMissing(SerializedObject serializedObject, string propertyName, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue != null || value == null)
                return false;

            property.objectReferenceValue = value;
            return true;
        }

        private static void ValidateInput(WalkingInputReader input, List<string> warnings)
        {
            if (input == null)
                return;

            SerializedObject inputObject = new(input);
            CheckReference(inputObject, "moveAction", "Move Input Action is not assigned.", warnings);
            CheckReference(inputObject, "lookAction", "Look Input Action is not assigned.", warnings);
        }

        private static void ValidateGroundProbe(GroundProbe probe, List<string> warnings)
        {
            if (probe == null)
                return;

            SerializedObject probeObject = new(probe);
            SerializedProperty controller = probeObject.FindProperty("characterController");
            if (controller != null && controller.objectReferenceValue == null)
            {
                CharacterController component = probe.GetComponent<CharacterController>();
                if (component != null)
                {
                    Undo.RecordObject(probe, "Repair Ground Probe Reference");
                    controller.objectReferenceValue = component;
                    probeObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(probe);
                }
                else
                {
                    warnings.Add("GroundProbe has no CharacterController reference.");
                }
            }
        }

        private static void CheckReference(SerializedObject serializedObject, string name, string warning, List<string> warnings)
        {
            SerializedProperty property = serializedObject.FindProperty(name);
            if (property != null && property.objectReferenceValue == null)
                warnings.Add(warning);
        }

        internal sealed class UpgradeResult
        {
            public bool Changed;
            public readonly List<string> Warnings = new();
        }
    }
}
