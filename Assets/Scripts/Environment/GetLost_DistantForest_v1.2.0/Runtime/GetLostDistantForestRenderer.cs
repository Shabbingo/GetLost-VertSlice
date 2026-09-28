using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GetLost.EnvironmentSystem
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class GetLostDistantForestRenderer : MonoBehaviour
    {
        private const int MaxInstancesPerBatch = 1023;
        private const string SourceShaderName = "Hidden/GetLost/Distant Forest Source Mesh";
        private const string ProxyShaderName = "Hidden/GetLost/Distant Forest Proxy";

        [Header("Profile")]
        [SerializeField] private GetLostDistantForestProfile profile;

        [Header("Build-Safe Shader References")]
        [Tooltip(
            "Explicit reference to Hidden/GetLost/Distant Forest Source Mesh. " +
            "Keeping this serialized prevents Unity from stripping the shader from player builds.")]
        [SerializeField] private Shader sourceTreeShader;

        [Tooltip(
            "Explicit reference to Hidden/GetLost/Distant Forest Proxy. " +
            "Keeping this serialized prevents Unity from stripping the shader from player builds.")]
        [SerializeField] private Shader proxyTreeShader;

        [Tooltip(
            "In the Editor, automatically resolve missing shader references by name. " +
            "The resolved references are serialized into the scene/prefab for builds.")]
        [SerializeField] private bool autoResolveShaderReferences = true;

        [Header("Camera")]
        [Tooltip("Optional gameplay camera. If empty, Camera.main is used for Game cameras.")]
        [SerializeField] private Camera targetCamera;

        [Header("Editor Preview")]
        [Tooltip("Render the distant forest in the Scene View while not in Play Mode.")]
        [SerializeField] private bool previewInSceneView = true;

        [Tooltip("Continuously repaint the Scene View while this component is selected/tuned.")]
        [SerializeField] private bool repaintSceneViewWhileEditing = true;

        [Header("Terrains")]
        [Tooltip("Optional explicit Terrain list. If empty, all active Terrains are used.")]
        [SerializeField] private Terrain[] terrains;

        [Header("Refresh")]
        [SerializeField] private bool monitorTerrainTreeChanges = true;
        [Min(0.25f)] [SerializeField] private float terrainChangeCheckInterval = 3f;

        [Header("Debug")]
        [SerializeField] private bool logRebuilds = false;
        [SerializeField] private bool drawCellGizmos = false;

        private readonly List<ForestBatch> batches = new List<ForestBatch>();
        private readonly Dictionary<Terrain, TerrainSignature> terrainSignatures = new Dictionary<Terrain, TerrainSignature>();
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private readonly Dictionary<Material, Material> sourceMaterialMap = new Dictionary<Material, Material>();

        private Mesh fallbackProxyMesh;
        private Material fallbackProxyMaterial;
        private bool cacheDirty = true;
        private float nextTerrainCheckTime;
        private bool loggedMissingSourceShader;
        private bool loggedMissingProxyShader;
        private bool loggedInstancingUnsupported;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int SourceColourMultiplierId = Shader.PropertyToID("_SourceColourMultiplier");
        private static readonly int AtmosphereTintId = Shader.PropertyToID("_AtmosphereTint");
        private static readonly int AtmosphereTintStrengthId = Shader.PropertyToID("_AtmosphereTintStrength");
        private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
        private static readonly int ColourVariationId = Shader.PropertyToID("_ColourVariation");
        private static readonly int TextureMipBiasId = Shader.PropertyToID("_TextureMipBias");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int AlphaCutoffOffsetId = Shader.PropertyToID("_AlphaCutoffOffset");
        private static readonly int SceneLightingStrengthId = Shader.PropertyToID("_SceneLightingStrength");
        private static readonly int CustomFogIntegrationId = Shader.PropertyToID("_CustomFogIntegration");
        private static readonly int HeightFogSoftnessId = Shader.PropertyToID("_HeightFogSoftness");
        private static readonly int HeightFogBlendAboveTopId = Shader.PropertyToID("_HeightFogBlendAboveTop");
        private static readonly int TreeBaseFoggingId = Shader.PropertyToID("_TreeBaseFogging");
        private static readonly int TreeBaseFogHeightBiasId = Shader.PropertyToID("_TreeBaseFogHeightBias");
        private static readonly int HeightFogCompensationId = Shader.PropertyToID("_HeightFogCompensation");
        private static readonly int HeightFogCompensationLimitId = Shader.PropertyToID("_HeightFogCompensationLimit");
        private static readonly int HeightFogColourInfluenceId = Shader.PropertyToID("_HeightFogColourInfluence");
        private static readonly int HorizonFadeStartId = Shader.PropertyToID("_HorizonFadeStart");
        private static readonly int HorizonFadeStrengthId = Shader.PropertyToID("_HorizonFadeStrength");
        private static readonly int CanopyShapeId = Shader.PropertyToID("_CanopyShape");
        private static readonly int FadeInStartId = Shader.PropertyToID("_FadeInStart");
        private static readonly int FullyVisibleId = Shader.PropertyToID("_FullyVisibleDistance");
        private static readonly int FadeOutStartId = Shader.PropertyToID("_FadeOutStart");
        private static readonly int MaximumDistanceId = Shader.PropertyToID("_MaximumDistance");

        public int CachedBatchCount => batches.Count;
        public int CachedTreeCount { get; private set; }
        public int CachedSourceDrawCount { get; private set; }

        private void OnEnable()
        {
            ResolveShaderReferences();

            GetLostDistantForestProfile.ProfileValidated += OnProfileValidated;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            cacheDirty = true;
            RequestEditorRepaint();
        }

        private void OnDisable()
        {
            GetLostDistantForestProfile.ProfileValidated -= OnProfileValidated;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseResources();
        }

        private void OnDestroy()
        {
            GetLostDistantForestProfile.ProfileValidated -= OnProfileValidated;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnProfileValidated(GetLostDistantForestProfile changedProfile)
        {
            if (changedProfile == null || changedProfile != profile)
                return;

            // Profile assets are edited independently from this component, so OnValidate on the
            // renderer does not fire. Mark the cache dirty so scale/density/LOD changes also
            // refresh immediately, and push pure material settings on the next camera render.
            cacheDirty = true;
            ApplyAllMaterialSettings();
            RequestEditorRepaint();
        }

        private void OnValidate()
        {
            ResolveShaderReferences();

            terrainChangeCheckInterval = Mathf.Max(0.25f, terrainChangeCheckInterval);
            cacheDirty = true;
            ApplyAllMaterialSettings();
            RequestEditorRepaint();
        }

        private void Update()
        {
            if (profile == null)
                return;

            if (cacheDirty)
                Rebuild();

            if (monitorTerrainTreeChanges && Time.realtimeSinceStartup >= nextTerrainCheckTime)
            {
                nextTerrainCheckTime = Time.realtimeSinceStartup + terrainChangeCheckInterval;
                if (TerrainTreesChanged())
                    Rebuild();
            }

#if UNITY_EDITOR
            if (!Application.isPlaying && previewInSceneView && repaintSceneViewWhileEditing)
                UnityEditor.SceneView.RepaintAll();
#endif
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!isActiveAndEnabled || profile == null || camera == null)
                return;

            if (!ShouldRenderForCamera(camera))
                return;

            if (cacheDirty)
                Rebuild();

            Render(camera);
        }

        private bool ShouldRenderForCamera(Camera camera)
        {
            if (camera.cameraType == CameraType.SceneView)
                return previewInSceneView;

            if (camera.cameraType != CameraType.Game)
                return false;

            if (targetCamera != null)
                return camera == targetCamera;

            Camera main = Camera.main;
            return main == null || camera == main;
        }

        [ContextMenu("Resolve Distant Forest Shader References")]
        public void ResolveShaderReferences()
        {
            if (!autoResolveShaderReferences)
                return;

            if (sourceTreeShader == null)
                sourceTreeShader = Shader.Find(SourceShaderName);

            if (proxyTreeShader == null)
                proxyTreeShader = Shader.Find(ProxyShaderName);
        }

        public bool HasBuildSafeShaderReferences()
        {
            return sourceTreeShader != null &&
                   proxyTreeShader != null;
        }

        public void Rebuild()
        {
            cacheDirty = false;
            batches.Clear();
            terrainSignatures.Clear();
            CachedTreeCount = 0;
            CachedSourceDrawCount = 0;

            ReleaseRuntimeMaterials();

            if (profile == null)
                return;

            EnsureFallbackResources();

            Terrain[] sourceTerrains = ResolveTerrains();
            if (sourceTerrains == null || sourceTerrains.Length == 0)
                return;

            float cellSize = Mathf.Max(32f, profile.cellSize);
            Dictionary<BatchBuildKey, List<Matrix4x4>> matrixBuckets = new Dictionary<BatchBuildKey, List<Matrix4x4>>(512);
            Dictionary<BatchBuildKey, Bounds> boundsBuckets = new Dictionary<BatchBuildKey, Bounds>(512);
            Dictionary<int, PrototypeDraw> drawLookup = new Dictionary<int, PrototypeDraw>(128);
            int nextDrawRuntimeId = 1;

            for (int terrainIndex = 0; terrainIndex < sourceTerrains.Length; terrainIndex++)
            {
                Terrain terrain = sourceTerrains[terrainIndex];
                if (terrain == null || terrain.terrainData == null || !terrain.isActiveAndEnabled)
                    continue;

                TerrainData data = terrain.terrainData;
                TreeInstance[] instances = data.treeInstances;
                TreePrototype[] prototypes = data.treePrototypes;

                terrainSignatures[terrain] = new TerrainSignature(instances.Length, prototypes != null ? prototypes.Length : 0);

                if (instances.Length == 0 || prototypes == null || prototypes.Length == 0)
                    continue;

                int terrainSeed = GetTerrainSeed(terrain);
                PrototypeRenderData[] prototypeData = BuildPrototypeRenderData(prototypes, drawLookup, ref nextDrawRuntimeId);
                Vector3 terrainSize = data.size;

                for (int i = 0; i < instances.Length; i++)
                {
                    TreeInstance tree = instances[i];
                    if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypeData.Length)
                        continue;

                    if (!PassesDensityTest(terrainSeed, i, tree.prototypeIndex, profile.density))
                        continue;

                    PrototypeRenderData proto = prototypeData[tree.prototypeIndex];
                    if (proto.draws == null || proto.draws.Count == 0)
                        continue;

                    Vector3 localPosition = new Vector3(
                        tree.position.x * terrainSize.x,
                        tree.position.y * terrainSize.y + profile.verticalOffset,
                        tree.position.z * terrainSize.z);

                    float variation = GetSignedVariation(terrainSeed, i, tree.prototypeIndex) * profile.sizeVariation;
                    float widthScale = tree.widthScale * profile.widthMultiplier * (1f - variation * 0.30f);
                    float heightScale = tree.heightScale * profile.heightMultiplier * (1f + variation);

                    Matrix4x4 treeLocalMatrix = Matrix4x4.TRS(
                        localPosition,
                        Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f),
                        new Vector3(widthScale, heightScale, widthScale));

                    Matrix4x4 terrainToWorld = terrain.transform.localToWorldMatrix;
                    Matrix4x4 baseWorldMatrix = terrainToWorld * treeLocalMatrix;
                    Vector3 worldPosition = terrain.transform.TransformPoint(localPosition);

                    int cellX = Mathf.FloorToInt(worldPosition.x / cellSize);
                    int cellZ = Mathf.FloorToInt(worldPosition.z / cellSize);

                    for (int drawIndex = 0; drawIndex < proto.draws.Count; drawIndex++)
                    {
                        PrototypeDraw draw = proto.draws[drawIndex];
                        Matrix4x4 worldMatrix = baseWorldMatrix * draw.localMatrix;

                        BatchBuildKey key = new BatchBuildKey(cellX, cellZ, draw.runtimeId);

                        List<Matrix4x4> matrices;
                        if (!matrixBuckets.TryGetValue(key, out matrices))
                        {
                            matrices = new List<Matrix4x4>(128);
                            matrixBuckets.Add(key, matrices);
                        }
                        matrices.Add(worldMatrix);

                        Bounds worldBounds = TransformBounds(draw.localBounds, worldMatrix);
                        Bounds existing;
                        if (boundsBuckets.TryGetValue(key, out existing))
                        {
                            existing.Encapsulate(worldBounds);
                            boundsBuckets[key] = existing;
                        }
                        else
                        {
                            boundsBuckets.Add(key, worldBounds);
                        }
                    }

                    CachedTreeCount++;
                }
            }

            CachedSourceDrawCount = drawLookup.Count;

            foreach (KeyValuePair<BatchBuildKey, List<Matrix4x4>> pair in matrixBuckets)
            {
                PrototypeDraw draw;
                if (!drawLookup.TryGetValue(pair.Key.drawRuntimeId, out draw))
                    continue;

                List<Matrix4x4> matrices = pair.Value;
                Bounds bounds = boundsBuckets[pair.Key];

                for (int start = 0; start < matrices.Count; start += MaxInstancesPerBatch)
                {
                    int count = Mathf.Min(MaxInstancesPerBatch, matrices.Count - start);
                    Matrix4x4[] chunk = new Matrix4x4[count];
                    matrices.CopyTo(start, chunk, 0, count);
                    batches.Add(new ForestBatch(draw.mesh, draw.subMeshIndex, draw.material, chunk, bounds));
                }
            }

            ApplyAllMaterialSettings();

            if (logRebuilds)
            {
                Debug.Log(
                    "[Distant Forest] Cached " + CachedTreeCount.ToString("N0") +
                    " Terrain trees into " + batches.Count.ToString("N0") +
                    " draw batches using source tree LOD meshes.", this);
            }

            RequestEditorRepaint();
        }

        private PrototypeRenderData[] BuildPrototypeRenderData(
            TreePrototype[] prototypes,
            Dictionary<int, PrototypeDraw> drawLookup,
            ref int nextDrawRuntimeId)
        {
            PrototypeRenderData[] result = new PrototypeRenderData[prototypes.Length];

            for (int prototypeIndex = 0; prototypeIndex < prototypes.Length; prototypeIndex++)
            {
                GameObject prefab = prototypes[prototypeIndex] != null ? prototypes[prototypeIndex].prefab : null;
                List<PrototypeDraw> draws = new List<PrototypeDraw>();

                if (profile.useSourceTreeMeshes && prefab != null)
                    CollectSourceMeshDraws(prefab, draws, drawLookup, ref nextDrawRuntimeId);

                if (draws.Count == 0 && profile.fallbackToProceduralProxy)
                    AddFallbackProxyDraw(prefab, draws, drawLookup, ref nextDrawRuntimeId);

                result[prototypeIndex] = new PrototypeRenderData(draws);
            }

            return result;
        }

        private void CollectSourceMeshDraws(
            GameObject prefab,
            List<PrototypeDraw> draws,
            Dictionary<int, PrototypeDraw> drawLookup,
            ref int nextDrawRuntimeId)
        {
            List<MeshRenderer> selectedRenderers = SelectLowestMeshLodRenderers(prefab);
            if (selectedRenderers.Count == 0)
                return;

            Matrix4x4 rootWorldToLocal = prefab.transform.worldToLocalMatrix;

            for (int r = 0; r < selectedRenderers.Count; r++)
            {
                MeshRenderer renderer = selectedRenderers[r];
                if (renderer == null)
                    continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                Mesh mesh = filter.sharedMesh;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 localMatrix = rootWorldToLocal * renderer.transform.localToWorldMatrix;

                int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                for (int sub = 0; sub < subMeshCount; sub++)
                {
                    Material sourceMaterial = materials != null && materials.Length > 0
                        ? materials[Mathf.Min(sub, materials.Length - 1)]
                        : null;

                    Material runtimeMaterial = GetOrCreateSourceRuntimeMaterial(sourceMaterial);
                    if (runtimeMaterial == null)
                        continue;

                    int runtimeId = nextDrawRuntimeId++;
                    PrototypeDraw draw = new PrototypeDraw(
                        runtimeId,
                        mesh,
                        sub,
                        runtimeMaterial,
                        localMatrix,
                        mesh.bounds);
                    draws.Add(draw);
                    drawLookup[runtimeId] = draw;
                }
            }
        }

        private List<MeshRenderer> SelectLowestMeshLodRenderers(GameObject prefab)
        {
            List<MeshRenderer> result = new List<MeshRenderer>();
            if (prefab == null)
                return result;

            LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>(true);
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                int usableSeen = 0;

                for (int lodIndex = lods.Length - 1; lodIndex >= 0; lodIndex--)
                {
                    List<MeshRenderer> candidates = new List<MeshRenderer>();
                    Renderer[] renderers = lods[lodIndex].renderers;

                    for (int r = 0; r < renderers.Length; r++)
                    {
                        MeshRenderer meshRenderer = renderers[r] as MeshRenderer;
                        if (meshRenderer == null)
                            continue;

                        MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                        if (filter != null && filter.sharedMesh != null)
                            candidates.Add(meshRenderer);
                    }

                    if (candidates.Count > 0)
                    {
                        if (usableSeen >= profile.lodStepsFromLowest)
                            return candidates;

                        usableSeen++;
                    }
                }
            }

            MeshRenderer[] all = prefab.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                MeshFilter filter = all[i].GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    result.Add(all[i]);
            }

            return result;
        }

        private void AddFallbackProxyDraw(
            GameObject prefab,
            List<PrototypeDraw> draws,
            Dictionary<int, PrototypeDraw> drawLookup,
            ref int nextDrawRuntimeId)
        {
            EnsureFallbackResources();
            if (fallbackProxyMesh == null || fallbackProxyMaterial == null)
                return;

            Vector2 size = CalculatePrefabSize(prefab);
            float height = Mathf.Clamp(size.y, profile.minimumTreeHeight, profile.maximumTreeHeight);
            float width = Mathf.Max(0.5f, size.x);
            Matrix4x4 local = Matrix4x4.Scale(new Vector3(width, height, width));

            int runtimeId = nextDrawRuntimeId++;
            PrototypeDraw draw = new PrototypeDraw(
                runtimeId,
                fallbackProxyMesh,
                0,
                fallbackProxyMaterial,
                local,
                fallbackProxyMesh.bounds);
            draws.Add(draw);
            drawLookup[runtimeId] = draw;
        }

        private Vector2 CalculatePrefabSize(GameObject prefab)
        {
            if (prefab == null)
                return new Vector2(profile.fallbackTreeWidth, profile.fallbackTreeHeight);

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return new Vector2(profile.fallbackTreeWidth, profile.fallbackTreeHeight);

            bool hasBounds = false;
            Bounds combined = new Bounds();
            Matrix4x4 rootWorldToLocal = prefab.transform.worldToLocalMatrix;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                MeshRenderer meshRenderer = renderer as MeshRenderer;
                if (meshRenderer == null)
                    continue;

                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;

                Bounds b = TransformBounds(filter.sharedMesh.bounds, rootWorldToLocal * renderer.transform.localToWorldMatrix);
                if (!hasBounds)
                {
                    combined = b;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(b);
                }
            }

            if (!hasBounds)
                return new Vector2(profile.fallbackTreeWidth, profile.fallbackTreeHeight);

            return new Vector2(Mathf.Max(combined.size.x, combined.size.z), combined.size.y);
        }

        private Material GetOrCreateSourceRuntimeMaterial(Material source)
        {
            if (source != null)
            {
                Material cached;
                if (sourceMaterialMap.TryGetValue(source, out cached) && cached != null)
                    return cached;
            }

            Shader shader = sourceTreeShader;

            if (shader == null)
                shader = Shader.Find(SourceShaderName);

            if (shader == null)
            {
                if (!loggedMissingSourceShader)
                {
                    loggedMissingSourceShader = true;

                    Debug.LogError(
                        "[Distant Forest] Source tree shader is missing. " +
                        "Assign GetLostDistantForestSourceMesh.shader to the Source Tree Shader field. " +
                        "Player builds can strip Hidden shaders that are only accessed through Shader.Find.",
                        this);
                }

                return null;
            }

            Material material = new Material(shader)
            {
                name = source != null ? "Distant " + source.name : "Distant Tree Material",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };

            Texture baseTexture = null;
            Color baseColor = Color.white;
            float cutoff = profile != null ? profile.fallbackAlphaCutoff : 0.35f;

            string sourceTextureProperty = null;

            if (source != null)
            {
                string[] textureProperties = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo", "_DiffuseTex" };
                for (int i = 0; i < textureProperties.Length; i++)
                {
                    if (!source.HasProperty(textureProperties[i]))
                        continue;

                    Texture candidate = source.GetTexture(textureProperties[i]);
                    if (candidate != null)
                    {
                        baseTexture = candidate;
                        sourceTextureProperty = textureProperties[i];
                        break;
                    }
                }

                if (source.HasProperty("_BaseColor"))
                    baseColor = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color"))
                    baseColor = source.GetColor("_Color");

                if (source.HasProperty("_Cutoff"))
                    cutoff = source.GetFloat("_Cutoff");
                else if (source.HasProperty("_AlphaClipThreshold"))
                    cutoff = source.GetFloat("_AlphaClipThreshold");
                else if (source.HasProperty("_AlphaCutoff"))
                    cutoff = source.GetFloat("_AlphaCutoff");
            }

            if (baseTexture != null)
            {
                material.SetTexture(BaseMapId, baseTexture);
                if (source != null && !string.IsNullOrEmpty(sourceTextureProperty))
                {
                    material.SetTextureScale("_BaseMap", source.GetTextureScale(sourceTextureProperty));
                    material.SetTextureOffset("_BaseMap", source.GetTextureOffset(sourceTextureProperty));
                }
            }
            material.SetColor(BaseColorId, baseColor);
            material.SetFloat(CutoffId, Mathf.Clamp(cutoff, 0.01f, 0.95f));

            runtimeMaterials.Add(material);
            if (source != null)
                sourceMaterialMap[source] = material;

            ApplyMaterialSettings(material, false);
            return material;
        }

        private void EnsureFallbackResources()
        {
            if (fallbackProxyMesh == null)
                fallbackProxyMesh = GetLostDistantForestProxyMesh.Create();

            if (fallbackProxyMaterial == null)
            {
                Shader shader = proxyTreeShader;

                if (shader == null)
                    shader = Shader.Find(ProxyShaderName);

                if (shader != null)
                {
                    fallbackProxyMaterial = new Material(shader)
                    {
                        name = "GetLost Distant Forest Fallback Material",
                        hideFlags = HideFlags.HideAndDontSave,
                        enableInstancing = true
                    };
                }
                else if (!loggedMissingProxyShader)
                {
                    loggedMissingProxyShader = true;

                    Debug.LogError(
                        "[Distant Forest] Fallback proxy shader is missing. " +
                        "Assign GetLostDistantForestProxy.shader to the Proxy Tree Shader field so Unity includes it in player builds.",
                        this);
                }
            }

            ApplyMaterialSettings(fallbackProxyMaterial, true);
        }

        private void ApplyAllMaterialSettings()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
                ApplyMaterialSettings(runtimeMaterials[i], false);

            ApplyMaterialSettings(fallbackProxyMaterial, true);
        }

        private void ApplyMaterialSettings(Material material, bool fallback)
        {
            if (material == null || profile == null)
                return;

            material.SetColor(SourceColourMultiplierId, profile.sourceColourMultiplier);
            material.SetColor(AtmosphereTintId, profile.atmosphereTint);
            material.SetFloat(AtmosphereTintStrengthId, profile.atmosphereTintStrength);
            material.SetFloat(BrightnessId, profile.brightness);
            material.SetFloat(ColourVariationId, profile.colourVariation);
            material.SetFloat(TextureMipBiasId, profile.textureMipBias);
            material.SetFloat(EdgeSoftnessId, profile.edgeSoftness);
            material.SetFloat(AlphaCutoffOffsetId, profile.alphaCutoffOffset);
            material.SetFloat(SceneLightingStrengthId, profile.sceneLightingStrength);
            float forestFogStrength = profile.customFogIntegration <= 0.001f
                ? 1f
                : profile.customFogIntegration;

            material.SetFloat(CustomFogIntegrationId, forestFogStrength);
            material.SetFloat(HeightFogSoftnessId, profile.heightFogSoftness);
            material.SetFloat(HeightFogBlendAboveTopId, profile.heightFogBlendAboveTop);
            material.SetFloat(TreeBaseFoggingId, profile.treeBaseFogging);
            material.SetFloat(TreeBaseFogHeightBiasId, profile.treeBaseFogHeightBias);
            material.SetFloat(HeightFogCompensationId, profile.heightFogCompensation);
            material.SetFloat(HeightFogCompensationLimitId, profile.heightFogCompensationLimit);
            material.SetFloat(HeightFogColourInfluenceId, profile.heightFogColourInfluence);
            material.SetFloat(HorizonFadeStartId, profile.horizonFadeStart);
            material.SetFloat(HorizonFadeStrengthId, profile.horizonFadeStrength);
            material.SetFloat(CanopyShapeId, profile.fallbackCanopyShape);
            material.SetFloat(FadeInStartId, profile.fadeInStart);
            material.SetFloat(FullyVisibleId, Mathf.Max(profile.fadeInStart + 0.01f, profile.fullyVisibleDistance));
            material.SetFloat(FadeOutStartId, Mathf.Max(profile.fullyVisibleDistance + 0.01f, profile.fadeOutStart));
            material.SetFloat(MaximumDistanceId, Mathf.Max(profile.fadeOutStart + 0.01f, profile.maximumDistance));
        }

        private void Render(Camera camera)
        {
            if (profile == null || batches.Count == 0)
                return;

            if (!SystemInfo.supportsInstancing)
            {
                if (!loggedInstancingUnsupported)
                {
                    loggedInstancingUnsupported = true;

                    Debug.LogError(
                        "[Distant Forest] GPU instancing is not supported by the current graphics device. " +
                        "The distant forest renderer requires GPU instancing.",
                        this);
                }

                return;
            }

            ApplyAllMaterialSettings();

            Vector3 cameraPosition = camera.transform.position;
            float nearDistance = Mathf.Max(0f, profile.fadeInStart);
            float farDistance = Mathf.Max(nearDistance + 1f, profile.maximumDistance);

            for (int i = 0; i < batches.Count; i++)
            {
                ForestBatch batch = batches[i];
                if (batch.mesh == null || batch.material == null || batch.matrices == null || batch.matrices.Length == 0)
                    continue;

                float radius = batch.bounds.extents.magnitude;
                float centerDistance = Vector3.Distance(cameraPosition, batch.bounds.center);

                if (centerDistance + radius < nearDistance)
                    continue;
                if (centerDistance - radius > farDistance)
                    continue;

                RenderParams renderParams = new RenderParams(batch.material)
                {
                    camera = camera,
                    layer = gameObject.layer,
                    shadowCastingMode = profile.castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = profile.receiveShadows,
                    renderingLayerMask = uint.MaxValue,
                    worldBounds = batch.bounds
                };

                Graphics.RenderMeshInstanced(
                    renderParams,
                    batch.mesh,
                    batch.subMeshIndex,
                    batch.matrices,
                    batch.matrices.Length);
            }
        }

        private Terrain[] ResolveTerrains()
        {
            if (terrains != null && terrains.Length > 0)
                return terrains;
            return Terrain.activeTerrains;
        }

        private bool TerrainTreesChanged()
        {
            Terrain[] sourceTerrains = ResolveTerrains();
            if (sourceTerrains == null)
                return terrainSignatures.Count > 0;

            int validTerrainCount = 0;
            for (int i = 0; i < sourceTerrains.Length; i++)
            {
                Terrain terrain = sourceTerrains[i];
                if (terrain == null || terrain.terrainData == null || !terrain.isActiveAndEnabled)
                    continue;

                validTerrainCount++;
                TerrainSignature current = new TerrainSignature(
                    terrain.terrainData.treeInstanceCount,
                    terrain.terrainData.treePrototypes != null ? terrain.terrainData.treePrototypes.Length : 0);

                TerrainSignature cached;
                if (!terrainSignatures.TryGetValue(terrain, out cached) || !current.Equals(cached))
                    return true;
            }

            return validTerrainCount != terrainSignatures.Count;
        }

        private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;

            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));

            Vector3 worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));

            return new Bounds(center, worldExtents * 2f);
        }

        private static int GetTerrainSeed(Terrain terrain)
        {
            if (terrain == null)
                return 0;

            Vector3 position = terrain.transform.position;
            Vector3 size = terrain.terrainData != null ? terrain.terrainData.size : Vector3.zero;

            uint positionHash = Hash(
                unchecked((uint)Mathf.RoundToInt(position.x * 10f)),
                unchecked((uint)Mathf.RoundToInt(position.y * 10f)),
                unchecked((uint)Mathf.RoundToInt(position.z * 10f)));

            uint sizeHash = Hash(
                unchecked((uint)Mathf.RoundToInt(size.x * 10f)),
                unchecked((uint)Mathf.RoundToInt(size.y * 10f)),
                unchecked((uint)Mathf.RoundToInt(size.z * 10f)));

            return unchecked((int)Hash(positionHash, sizeHash, 0x474C464Fu));
        }

        private static bool PassesDensityTest(int terrainId, int treeIndex, int prototypeIndex, float density)
        {
            if (density >= 0.999f)
                return true;

            uint hash = Hash((uint)terrainId, (uint)treeIndex, (uint)prototypeIndex);
            float value = (hash & 0x00FFFFFFu) / 16777215f;
            return value <= Mathf.Clamp01(density);
        }

        private static float GetSignedVariation(int terrainId, int treeIndex, int prototypeIndex)
        {
            uint hash = Hash((uint)(terrainId * 31), (uint)(treeIndex * 17), (uint)(prototypeIndex * 13 + 7));
            float value = (hash & 0x00FFFFFFu) / 16777215f;
            return value * 2f - 1f;
        }

        private static uint Hash(uint a, uint b, uint c)
        {
            uint h = 2166136261u;
            h = (h ^ a) * 16777619u;
            h = (h ^ b) * 16777619u;
            h = (h ^ c) * 16777619u;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return h;
        }

        private void ReleaseRuntimeMaterials()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                Material material = runtimeMaterials[i];
                if (material == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(material);
                else
                    DestroyImmediate(material);
            }

            runtimeMaterials.Clear();
            sourceMaterialMap.Clear();
        }

        private void ReleaseResources()
        {
            ReleaseRuntimeMaterials();

            if (fallbackProxyMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(fallbackProxyMaterial);
                else
                    DestroyImmediate(fallbackProxyMaterial);
                fallbackProxyMaterial = null;
            }

            if (fallbackProxyMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(fallbackProxyMesh);
                else
                    DestroyImmediate(fallbackProxyMesh);
                fallbackProxyMesh = null;
            }
        }

        private void RequestEditorRepaint()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.SceneView.RepaintAll();
#endif
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawCellGizmos)
                return;

            for (int i = 0; i < batches.Count; i++)
                Gizmos.DrawWireCube(batches[i].bounds.center, batches[i].bounds.size);
        }

        private sealed class PrototypeRenderData
        {
            public readonly List<PrototypeDraw> draws;
            public PrototypeRenderData(List<PrototypeDraw> draws) { this.draws = draws; }
        }

        private sealed class PrototypeDraw
        {
            public readonly int runtimeId;
            public readonly Mesh mesh;
            public readonly int subMeshIndex;
            public readonly Material material;
            public readonly Matrix4x4 localMatrix;
            public readonly Bounds localBounds;

            public PrototypeDraw(int runtimeId, Mesh mesh, int subMeshIndex, Material material, Matrix4x4 localMatrix, Bounds localBounds)
            {
                this.runtimeId = runtimeId;
                this.mesh = mesh;
                this.subMeshIndex = subMeshIndex;
                this.material = material;
                this.localMatrix = localMatrix;
                this.localBounds = localBounds;
            }
        }

        private readonly struct BatchBuildKey : IEquatable<BatchBuildKey>
        {
            public readonly int cellX;
            public readonly int cellZ;
            public readonly int drawRuntimeId;

            public BatchBuildKey(int cellX, int cellZ, int drawRuntimeId)
            {
                this.cellX = cellX;
                this.cellZ = cellZ;
                this.drawRuntimeId = drawRuntimeId;
            }

            public bool Equals(BatchBuildKey other)
            {
                return cellX == other.cellX && cellZ == other.cellZ && drawRuntimeId == other.drawRuntimeId;
            }

            public override bool Equals(object obj)
            {
                return obj is BatchBuildKey && Equals((BatchBuildKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + cellX;
                    h = h * 31 + cellZ;
                    h = h * 31 + drawRuntimeId;
                    return h;
                }
            }
        }

        private sealed class ForestBatch
        {
            public readonly Mesh mesh;
            public readonly int subMeshIndex;
            public readonly Material material;
            public readonly Matrix4x4[] matrices;
            public readonly Bounds bounds;

            public ForestBatch(Mesh mesh, int subMeshIndex, Material material, Matrix4x4[] matrices, Bounds bounds)
            {
                this.mesh = mesh;
                this.subMeshIndex = subMeshIndex;
                this.material = material;
                this.matrices = matrices;
                this.bounds = bounds;
            }
        }

        private readonly struct TerrainSignature : IEquatable<TerrainSignature>
        {
            private readonly int treeCount;
            private readonly int prototypeCount;

            public TerrainSignature(int treeCount, int prototypeCount)
            {
                this.treeCount = treeCount;
                this.prototypeCount = prototypeCount;
            }

            public bool Equals(TerrainSignature other)
            {
                return treeCount == other.treeCount && prototypeCount == other.prototypeCount;
            }
        }
    }
}
