using UnityEditor;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner.Editor
{
    [CustomEditor(typeof(TerrainFoliageRule))]
    public sealed class TerrainFoliageRuleEditor : UnityEditor.Editor
    {
        private SerializedProperty terrainLayer;
        private SerializedProperty additionalTerrainLayers;
        private SerializedProperty minimumLayerWeight;
        private SerializedProperty prefabEntries;
        private SerializedProperty spawnChance;
        private SerializedProperty useCliffFormationDistribution;
        private SerializedProperty cliffSamplesPerCell;
        private SerializedProperty cliffMinimumSlope;
        private SerializedProperty cliffFullDensitySlope;
        private SerializedProperty cliffReliefRadius;
        private SerializedProperty cliffMinimumHeightDrop;
        private SerializedProperty cliffFullHeightDrop;
        private SerializedProperty cliffPatchSize;
        private SerializedProperty minimumCliffPatchDensity;
        private SerializedProperty maximumCliffPatchDensity;
        private SerializedProperty cliffPatchContrast;
        private SerializedProperty cliffPatchDomainWarp;
        private SerializedProperty buildContinuousCliffFaces;
        private SerializedProperty cliffFaceSpacing;
        private SerializedProperty cliffFaceMinimumWidthScale;
        private SerializedProperty cliffFaceMaximumWidthScale;
        private SerializedProperty cliffFaceMinimumHeightScale;
        private SerializedProperty cliffFaceMaximumHeightScale;
        private SerializedProperty cliffFaceMinimumDepthScale;
        private SerializedProperty cliffFaceMaximumDepthScale;
        private SerializedProperty cliffFaceEmbedDepth;
        private SerializedProperty cliffFaceYawJitter;
        private SerializedProperty useNaturalTreeDensityVariation;
        private SerializedProperty naturalTreePatchSize;
        private SerializedProperty minimumNaturalTreeDensity;
        private SerializedProperty maximumNaturalTreeDensity;
        private SerializedProperty naturalTreeDensityContrast;
        private SerializedProperty naturalTreeDomainWarp;
        private SerializedProperty followNaturalForestDensity;
        private SerializedProperty useUnderstoryThickets;
        private SerializedProperty understoryThicketSize;
        private SerializedProperty minimumUnderstoryThicketDensity;
        private SerializedProperty maximumUnderstoryThicketDensity;
        private SerializedProperty understoryThicketContrast;
        private SerializedProperty understoryThicketDomainWarp;
        private SerializedProperty useGentleGroundMeadows;
        private SerializedProperty meadowMaximumSlope;
        private SerializedProperty meadowRadiusBonus;
        private SerializedProperty meadowDensityMultiplier;
        private SerializedProperty additionalTerrainDetailSamplesPerCell;
        private SerializedProperty useSlopeLimit;
        private SerializedProperty minimumSlope;
        private SerializedProperty maximumSlope;
        private SerializedProperty minimumScale;
        private SerializedProperty maximumScale;

        private void OnEnable()
        {
            TerrainFoliageRule rule = (TerrainFoliageRule)target;

            if (rule.EnsureMigrated())
            {
                EditorUtility.SetDirty(rule);
                AssetDatabase.SaveAssetIfDirty(rule);
            }

            terrainLayer = serializedObject.FindProperty("terrainLayer");
            additionalTerrainLayers =
                serializedObject.FindProperty("additionalTerrainLayers");
            minimumLayerWeight =
                serializedObject.FindProperty("minimumLayerWeight");
            prefabEntries =
                serializedObject.FindProperty("prefabEntries");
            spawnChance = serializedObject.FindProperty("spawnChance");
            useCliffFormationDistribution =
                serializedObject.FindProperty(
                    "useCliffFormationDistribution");
            cliffSamplesPerCell =
                serializedObject.FindProperty("cliffSamplesPerCell");
            cliffMinimumSlope =
                serializedObject.FindProperty("cliffMinimumSlope");
            cliffFullDensitySlope =
                serializedObject.FindProperty("cliffFullDensitySlope");
            cliffReliefRadius =
                serializedObject.FindProperty("cliffReliefRadius");
            cliffMinimumHeightDrop =
                serializedObject.FindProperty("cliffMinimumHeightDrop");
            cliffFullHeightDrop =
                serializedObject.FindProperty("cliffFullHeightDrop");
            cliffPatchSize =
                serializedObject.FindProperty("cliffPatchSize");
            minimumCliffPatchDensity =
                serializedObject.FindProperty("minimumCliffPatchDensity");
            maximumCliffPatchDensity =
                serializedObject.FindProperty("maximumCliffPatchDensity");
            cliffPatchContrast =
                serializedObject.FindProperty("cliffPatchContrast");
            cliffPatchDomainWarp =
                serializedObject.FindProperty("cliffPatchDomainWarp");
            buildContinuousCliffFaces =
                serializedObject.FindProperty("buildContinuousCliffFaces");
            cliffFaceSpacing =
                serializedObject.FindProperty("cliffFaceSpacing");
            cliffFaceMinimumWidthScale =
                serializedObject.FindProperty("cliffFaceMinimumWidthScale");
            cliffFaceMaximumWidthScale =
                serializedObject.FindProperty("cliffFaceMaximumWidthScale");
            cliffFaceMinimumHeightScale =
                serializedObject.FindProperty("cliffFaceMinimumHeightScale");
            cliffFaceMaximumHeightScale =
                serializedObject.FindProperty("cliffFaceMaximumHeightScale");
            cliffFaceMinimumDepthScale =
                serializedObject.FindProperty("cliffFaceMinimumDepthScale");
            cliffFaceMaximumDepthScale =
                serializedObject.FindProperty("cliffFaceMaximumDepthScale");
            cliffFaceEmbedDepth =
                serializedObject.FindProperty("cliffFaceEmbedDepth");
            cliffFaceYawJitter =
                serializedObject.FindProperty("cliffFaceYawJitter");
            useNaturalTreeDensityVariation =
                serializedObject.FindProperty(
                    "useNaturalTreeDensityVariation");
            naturalTreePatchSize =
                serializedObject.FindProperty("naturalTreePatchSize");
            minimumNaturalTreeDensity =
                serializedObject.FindProperty(
                    "minimumNaturalTreeDensity");
            maximumNaturalTreeDensity =
                serializedObject.FindProperty(
                    "maximumNaturalTreeDensity");
            naturalTreeDensityContrast =
                serializedObject.FindProperty(
                    "naturalTreeDensityContrast");
            naturalTreeDomainWarp =
                serializedObject.FindProperty("naturalTreeDomainWarp");
            followNaturalForestDensity =
                serializedObject.FindProperty(
                    "followNaturalForestDensity");
            useUnderstoryThickets =
                serializedObject.FindProperty("useUnderstoryThickets");
            understoryThicketSize =
                serializedObject.FindProperty("understoryThicketSize");
            minimumUnderstoryThicketDensity =
                serializedObject.FindProperty(
                    "minimumUnderstoryThicketDensity");
            maximumUnderstoryThicketDensity =
                serializedObject.FindProperty(
                    "maximumUnderstoryThicketDensity");
            understoryThicketContrast =
                serializedObject.FindProperty(
                    "understoryThicketContrast");
            understoryThicketDomainWarp =
                serializedObject.FindProperty(
                    "understoryThicketDomainWarp");
            useGentleGroundMeadows =
                serializedObject.FindProperty("useGentleGroundMeadows");
            meadowMaximumSlope =
                serializedObject.FindProperty("meadowMaximumSlope");
            meadowRadiusBonus =
                serializedObject.FindProperty("meadowRadiusBonus");
            meadowDensityMultiplier =
                serializedObject.FindProperty("meadowDensityMultiplier");
            additionalTerrainDetailSamplesPerCell =
                serializedObject.FindProperty(
                    "additionalTerrainDetailSamplesPerCell");
            useSlopeLimit =
                serializedObject.FindProperty("useSlopeLimit");
            minimumSlope =
                serializedObject.FindProperty("minimumSlope");
            maximumSlope =
                serializedObject.FindProperty("maximumSlope");
            minimumScale =
                serializedObject.FindProperty("minimumScale");
            maximumScale =
                serializedObject.FindProperty("maximumScale");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField(
                "Terrain Layer",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(terrainLayer);
            EditorGUILayout.PropertyField(
                additionalTerrainLayers,
                new GUIContent("Additional Terrain Layers"),
                true);
            EditorGUILayout.PropertyField(minimumLayerWeight);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField(
                "Prefab Entries",
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Terrain Detail and Terrain Tree entries use their assigned prefabs. " +
                "Use Auto Find Details and Trees From Rules on the spawner to register missing Terrain prototypes automatically.",
                MessageType.Info);

            DrawEntries();

            EditorGUILayout.Space(8f);
            EditorGUILayout.PropertyField(spawnChance);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Cliff Formations",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                useCliffFormationDistribution,
                new GUIContent("Use Cliff Formation Distribution"));

            if (useCliffFormationDistribution.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    cliffSamplesPerCell,
                    new GUIContent("Samples Per Cell"));
                EditorGUILayout.PropertyField(
                    cliffMinimumSlope,
                    new GUIContent("Minimum Cliff Slope"));
                EditorGUILayout.PropertyField(
                    cliffFullDensitySlope,
                    new GUIContent("Full Density Slope"));
                EditorGUILayout.PropertyField(
                    cliffReliefRadius,
                    new GUIContent("Relief Sample Radius"));
                EditorGUILayout.PropertyField(
                    cliffMinimumHeightDrop,
                    new GUIContent("Minimum Height Drop"));
                EditorGUILayout.PropertyField(
                    cliffFullHeightDrop,
                    new GUIContent("Full Density Height Drop"));
                EditorGUILayout.PropertyField(
                    cliffPatchSize,
                    new GUIContent("Formation Patch Size"));
                EditorGUILayout.PropertyField(
                    minimumCliffPatchDensity,
                    new GUIContent("Density Between Formations"));
                EditorGUILayout.PropertyField(
                    maximumCliffPatchDensity,
                    new GUIContent("Formation Centre Density"));
                EditorGUILayout.PropertyField(
                    cliffPatchContrast,
                    new GUIContent("Formation Contrast"));
                EditorGUILayout.PropertyField(
                    cliffPatchDomainWarp,
                    new GUIContent("Boundary Irregularity"));
                EditorGUI.indentLevel--;

                EditorGUILayout.HelpBox(
                    "Candidates must satisfy both the slope and nearby downhill-drop tests. " +
                    "A warped world-space density field groups accepted rocks into continuous bands with open gaps.",
                    MessageType.Info);

                EditorGUILayout.Space(4f);
                EditorGUILayout.PropertyField(
                    buildContinuousCliffFaces,
                    new GUIContent("Build Continuous Cliff Faces"));
                if (buildContinuousCliffFaces.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(
                        cliffFaceSpacing,
                        new GUIContent("Panel Spacing"));
                    EditorGUILayout.PropertyField(
                        cliffFaceMinimumWidthScale,
                        new GUIContent("Minimum Width Scale"));
                    EditorGUILayout.PropertyField(
                        cliffFaceMaximumWidthScale,
                        new GUIContent("Maximum Width Scale"));
                    EditorGUILayout.PropertyField(
                        cliffFaceMinimumHeightScale,
                        new GUIContent("Minimum Height Scale"));
                    EditorGUILayout.PropertyField(
                        cliffFaceMaximumHeightScale,
                        new GUIContent("Maximum Height Scale"));
                    EditorGUILayout.PropertyField(
                        cliffFaceMinimumDepthScale,
                        new GUIContent("Minimum Depth Scale"));
                    EditorGUILayout.PropertyField(
                        cliffFaceMaximumDepthScale,
                        new GUIContent("Maximum Depth Scale"));
                    EditorGUILayout.PropertyField(
                        cliffFaceEmbedDepth,
                        new GUIContent("Embed Into Hillside"));
                    EditorGUILayout.PropertyField(
                        cliffFaceYawJitter,
                        new GUIContent("Yaw Variation"));
                    EditorGUI.indentLevel--;
                    EditorGUILayout.HelpBox(
                        "Only GameObject entries marked Use For Cliff Faces are selected. " +
                        "Keep structural cliff panels as Scene Objects so their silhouettes and collision do not stream out near the player.",
                        MessageType.Info);
                }
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Natural Tree Distribution",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                useNaturalTreeDensityVariation,
                new GUIContent("Use Natural Density Variation"));

            if (useNaturalTreeDensityVariation.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    naturalTreePatchSize,
                    new GUIContent("Patch Size"));
                EditorGUILayout.PropertyField(
                    minimumNaturalTreeDensity,
                    new GUIContent("Minimum Density"));
                EditorGUILayout.PropertyField(
                    maximumNaturalTreeDensity,
                    new GUIContent("Maximum Density"));
                EditorGUILayout.PropertyField(
                    naturalTreeDensityContrast,
                    new GUIContent("Density Contrast"));
                EditorGUILayout.PropertyField(
                    naturalTreeDomainWarp,
                    new GUIContent("Boundary Irregularity"));
                EditorGUI.indentLevel--;

                EditorGUILayout.HelpBox(
                    followNaturalForestDensity.boolValue
                        ? "The broad woodland field also controls this rule's non-tree entries. Regenerate foliage after changing it."
                        : "These settings affect Existing Terrain Tree entries. Regenerate foliage after changing them.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Forest Understory",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                followNaturalForestDensity,
                new GUIContent("Follow Forest Density"));

            if (followNaturalForestDensity.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    useUnderstoryThickets,
                    new GUIContent("Form Understory Thickets"));

                if (useUnderstoryThickets.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(
                        understoryThicketSize,
                        new GUIContent("Thicket Size"));
                    EditorGUILayout.PropertyField(
                        minimumUnderstoryThicketDensity,
                        new GUIContent("Density Between Thickets"));
                    EditorGUILayout.PropertyField(
                        maximumUnderstoryThicketDensity,
                        new GUIContent("Thicket Centre Density"));
                    EditorGUILayout.PropertyField(
                        understoryThicketContrast,
                        new GUIContent("Thicket Contrast"));
                    EditorGUILayout.PropertyField(
                        understoryThicketDomainWarp,
                        new GUIContent("Boundary Irregularity"));
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;

                EditorGUILayout.HelpBox(
                    "Use this for mid-height bushes and saplings. It multiplies the broad forest field by smaller irregular thickets, leaving navigable gaps.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Gentle Ground Meadows",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                useGentleGroundMeadows,
                new GUIContent("Build Meadows On Gentle Ground"));

            if (useGentleGroundMeadows.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    meadowMaximumSlope,
                    new GUIContent("Maximum Meadow Slope"));
                EditorGUILayout.PropertyField(
                    meadowRadiusBonus,
                    new GUIContent("Meadow Spread"));
                EditorGUILayout.PropertyField(
                    meadowDensityMultiplier,
                    new GUIContent("Meadow Fullness"));
                EditorGUILayout.PropertyField(
                    additionalTerrainDetailSamplesPerCell,
                    new GUIContent("Extra Grass Samples Per Cell"));
                EditorGUI.indentLevel--;

                EditorGUILayout.HelpBox(
                    "These settings affect Existing Terrain Detail entries on gentle ground. " +
                    "They overlap nearby stamps to create continuous fields.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "Placement Constraints",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(useSlopeLimit);

            if (useSlopeLimit.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(minimumSlope);
                EditorGUILayout.PropertyField(maximumSlope);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "GameObject Scale Range",
                EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(minimumScale);
            EditorGUILayout.PropertyField(maximumScale);

            if (serializedObject.ApplyModifiedProperties())
            {
                TerrainFoliageRule rule =
                    (TerrainFoliageRule)target;

                rule.ValidateEntries();
                EditorUtility.SetDirty(rule);
            }
        }

        private void DrawEntries()
        {
            for (int i = 0; i < prefabEntries.arraySize; i++)
            {
                SerializedProperty entry =
                    prefabEntries.GetArrayElementAtIndex(i);

                SerializedProperty prefab =
                    entry.FindPropertyRelative("prefab");

                string title = prefab.objectReferenceValue != null
                    ? prefab.objectReferenceValue.name
                    : $"Prefab Entry {i + 1}";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                entry.isExpanded = EditorGUILayout.Foldout(
                    entry.isExpanded,
                    title,
                    true);

                if (GUILayout.Button("Remove", GUILayout.Width(60f)))
                {
                    prefabEntries.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }

                EditorGUILayout.EndHorizontal();

                SerializedProperty outputMode =
                    entry.FindPropertyRelative("outputMode");

                bool terrainDetail =
                    outputMode.enumValueIndex ==
                    (int)TerrainFoliageOutputMode.TerrainDetail;

                EditorGUILayout.PropertyField(
                    prefab,
                    new GUIContent(
                        terrainDetail
                            ? "Existing Detail Prefab"
                            : "Prefab"));

                SerializedProperty weight =
                    entry.FindPropertyRelative("weight");

                weight.floatValue = Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField(
                        new GUIContent("Weight"),
                        weight.floatValue));

                outputMode.enumValueIndex =
                    EditorGUILayout.Popup(
                        "Output Mode",
                        outputMode.enumValueIndex,
                        new[]
                        {
                            "GameObject",
                            "Existing Terrain Detail",
                            "Existing Terrain Tree",
                            "Managed Instanced Grass"
                        });

                if (entry.isExpanded)
                {
                    terrainDetail =
                        outputMode.enumValueIndex ==
                        (int)TerrainFoliageOutputMode.TerrainDetail;

                    bool terrainTree =
                        outputMode.enumValueIndex ==
                        (int)TerrainFoliageOutputMode.TerrainTree;

                    bool instancedGrass =
                        outputMode.enumValueIndex ==
                        (int)TerrainFoliageOutputMode.InstancedGrass;

                    if (instancedGrass)
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField(
                            "Managed Instanced Grass",
                            EditorStyles.miniBoldLabel);
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassInstancesPerSample"),
                            new GUIContent("Blades Per Sample"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassSpread"),
                            new GUIContent("World Spread"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassMinimumWidthScale"),
                            new GUIContent("Minimum Width Scale"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassMaximumWidthScale"),
                            new GUIContent("Maximum Width Scale"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassMinimumHeightScale"),
                            new GUIContent("Minimum Height Scale"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassMaximumHeightScale"),
                            new GUIContent("Maximum Height Scale"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassAlignToTerrainNormal"),
                            new GUIContent("Align To Terrain"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassRandomYRotation"),
                            new GUIContent("Random Y Rotation"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("grassGroundOffset"),
                            new GUIContent("Ground Offset"));
                        EditorGUILayout.HelpBox(
                            "Grass is stored as exact world-space mesh instances. " +
                            "GameObject position and rotation offsets are intentionally ignored, " +
                            "and every final blade is tested against paths.",
                            MessageType.Info);
                    }
                    else if (!terrainDetail && !terrainTree)
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField(
                            "GameObject Placement",
                            EditorStyles.miniBoldLabel);

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("gameObjectStorageMode"),
                            new GUIContent("Storage"));

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("useForCliffFaces"),
                            new GUIContent("Use For Cliff Faces"));

                        if (entry.FindPropertyRelative("gameObjectStorageMode").enumValueIndex == (int)TerrainGameObjectStorageMode.RuntimeStreamed)
                        {
                            EditorGUILayout.HelpBox("Stores compact transform data instead of thousands of scene objects. Add a Placement Data asset to the spawner; nearby objects are pooled and streamed during play.", MessageType.Info);
                        }

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative(
                                "alignToTerrainNormal"));

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative(
                                "randomYRotation"));

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("positionOffset"));

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("rotationOffset"));

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("scaleMultiplier"));
                    }
                    else if (terrainDetail)
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField(
                            "Existing Terrain Detail",
                            EditorStyles.miniBoldLabel);

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("detailDensity"));

                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative(
                                "allowMeadowExpansion"),
                            new GUIContent("Include In Meadows"));

                        SerializedProperty concentration =
                            entry.FindPropertyRelative("detailConcentration");

                        EditorGUILayout.PropertyField(
                            concentration,
                            new GUIContent("Concentration"));

                        if (concentration.enumValueIndex ==
                            (int)TerrainDetailConcentration.Custom)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(
                                entry.FindPropertyRelative(
                                    "customDetailRadius"));
                            EditorGUILayout.PropertyField(
                                entry.FindPropertyRelative(
                                    "customDetailFalloff"));
                            EditorGUI.indentLevel--;
                        }

                        if (concentration.enumValueIndex ==
                            (int)TerrainDetailConcentration.VeryDense)
                        {
                            EditorGUILayout.HelpBox(
                                "Very Dense paints a five-cell-wide patch around each accepted placement. This can substantially increase grass density and generation time.",
                                MessageType.Info);
                        }

                        EditorGUILayout.HelpBox(
                            "The prefab must already appear in the assigned " +
                            "Terrain's Paint Details list.",
                            MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField(
                            "Existing Terrain Tree",
                            EditorStyles.miniBoldLabel);
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("treeRandomRotation"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("treeScaleMultiplier"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("treeColor"));
                        EditorGUILayout.PropertyField(
                            entry.FindPropertyRelative("treeLightmapColor"));
                        EditorGUILayout.HelpBox(
                            "The exact prefab must already appear in the Terrain's " +
                            "Paint Trees list. Tree prefabs may contain LODGroups.",
                            MessageType.None);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(3f);
            }

            if (GUILayout.Button("Add Prefab Entry"))
            {
                int index = prefabEntries.arraySize;
                prefabEntries.InsertArrayElementAtIndex(index);

                SerializedProperty entry =
                    prefabEntries.GetArrayElementAtIndex(index);

                entry.FindPropertyRelative("prefab").objectReferenceValue =
                    null;

                entry.FindPropertyRelative("weight").floatValue = 1f;

                entry.FindPropertyRelative("outputMode").enumValueIndex =
                    (int)TerrainFoliageOutputMode.GameObject;

                entry.FindPropertyRelative(
                    "alignToTerrainNormal").boolValue = true;

                entry.FindPropertyRelative(
                    "useForCliffFaces").boolValue = false;

                entry.FindPropertyRelative(
                    "randomYRotation").boolValue = true;

                entry.FindPropertyRelative(
                    "positionOffset").vector3Value = Vector3.zero;

                entry.FindPropertyRelative(
                    "rotationOffset").vector3Value = Vector3.zero;

                entry.FindPropertyRelative(
                    "scaleMultiplier").vector3Value = Vector3.one;

                entry.FindPropertyRelative(
                    "detailDensity").intValue = 1;
                entry.FindPropertyRelative(
                    "detailConcentration").enumValueIndex =
                    (int)TerrainDetailConcentration.Normal;
                entry.FindPropertyRelative(
                    "customDetailRadius").intValue = 2;
                entry.FindPropertyRelative(
                    "customDetailFalloff").floatValue = 0.35f;
                entry.FindPropertyRelative(
                    "allowMeadowExpansion").boolValue = true;
                entry.FindPropertyRelative(
                    "treeRandomRotation").boolValue = true;
                entry.FindPropertyRelative(
                    "treeScaleMultiplier").floatValue = 1f;
                entry.FindPropertyRelative(
                    "treeColor").colorValue = Color.white;
                entry.FindPropertyRelative(
                    "treeLightmapColor").colorValue = Color.white;
                entry.FindPropertyRelative(
                    "grassInstancesPerSample").intValue = 4;
                entry.FindPropertyRelative(
                    "grassSpread").floatValue = 2.25f;
                entry.FindPropertyRelative(
                    "grassMinimumWidthScale").floatValue = 0.8f;
                entry.FindPropertyRelative(
                    "grassMaximumWidthScale").floatValue = 1.25f;
                entry.FindPropertyRelative(
                    "grassMinimumHeightScale").floatValue = 0.85f;
                entry.FindPropertyRelative(
                    "grassMaximumHeightScale").floatValue = 1.4f;
                entry.FindPropertyRelative(
                    "grassAlignToTerrainNormal").boolValue = true;
                entry.FindPropertyRelative(
                    "grassRandomYRotation").boolValue = true;
                entry.FindPropertyRelative(
                    "grassGroundOffset").floatValue = 0f;

                entry.isExpanded = true;
            }
        }
    }
}
