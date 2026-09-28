using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GetLost.Trails
{
    /// <summary>
    /// Protects TerrainData assets while testing in the Unity Editor.
    /// Backs up heights, alphamaps, terrain trees and all terrain detail layers.
    /// </summary>
    public static class TrailTerrainEditorBackup
    {
#if UNITY_EDITOR

        private sealed class TerrainSnapshot
        {
            public TerrainData terrainData;
            public string terrainName;
            public float[,] heights;
            public float[,,] alphamaps;
            public TreeInstance[] treeInstances;
            public List<int[,]> detailLayers;
        }

        private static readonly Dictionary<TerrainData, TerrainSnapshot> snapshots = new();
        private static bool registeredForPlayModeChanges;

        public static void CaptureIfNeeded(
            Terrain terrain,
            TrailSystemSettings settings)
        {
            if (!Application.isPlaying ||
                terrain == null ||
                terrain.terrainData == null ||
                settings == null ||
                !settings.restoreTerrainAfterPlayMode)
            {
                return;
            }

            RegisterIfNeeded();

            TerrainData data = terrain.terrainData;

            // Terrain components are destroyed/recreated when the scene is
            // restarted during Play Mode, but the TerrainData asset survives.
            // Key the backup by TerrainData so the original pre-Play snapshot
            // remains valid across scene reloads.
            if (snapshots.ContainsKey(data))
                return;

            List<int[,]> detailLayers = new();

            int prototypeCount = data.detailPrototypes != null
                ? data.detailPrototypes.Length
                : 0;

            for (int i = 0; i < prototypeCount; i++)
            {
                detailLayers.Add(
                    data.GetDetailLayer(
                        0,
                        0,
                        data.detailWidth,
                        data.detailHeight,
                        i));
            }

            TerrainSnapshot snapshot = new()
            {
                terrainData = data,
                terrainName = terrain.name,

                heights = data.GetHeights(
                    0,
                    0,
                    data.heightmapResolution,
                    data.heightmapResolution),

                alphamaps = data.GetAlphamaps(
                    0,
                    0,
                    data.alphamapWidth,
                    data.alphamapHeight),

                treeInstances = data.treeInstances != null
                    ? (TreeInstance[])data.treeInstances.Clone()
                    : System.Array.Empty<TreeInstance>(),

                detailLayers = detailLayers
            };

            snapshots.Add(data, snapshot);

            Debug.Log(
                $"[Trail System] Backed up terrain '{terrain.name}' for Play Mode restore.");
        }

        private static void RegisterIfNeeded()
        {
            if (registeredForPlayModeChanges)
                return;

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            registeredForPlayModeChanges = true;
        }

        private static void OnPlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                RestoreAll();
        }

        public static void RestoreAll()
        {
            foreach (TerrainSnapshot snapshot in snapshots.Values)
            {
                if (snapshot == null ||
                    snapshot.terrainData == null)
                {
                    continue;
                }

                TerrainData data = snapshot.terrainData;

                if (snapshot.heights != null)
                    data.SetHeights(0, 0, snapshot.heights);

                if (snapshot.alphamaps != null)
                    data.SetAlphamaps(0, 0, snapshot.alphamaps);

                if (snapshot.treeInstances != null)
                    data.treeInstances = snapshot.treeInstances;

                if (snapshot.detailLayers != null)
                {
                    int count = Mathf.Min(
                        snapshot.detailLayers.Count,
                        data.detailPrototypes != null
                            ? data.detailPrototypes.Length
                            : 0);

                    for (int i = 0; i < count; i++)
                    {
                        int[,] layer = snapshot.detailLayers[i];

                        if (layer != null)
                            data.SetDetailLayer(0, 0, i, layer);
                    }
                }

                Terrain[] activeTerrains = Terrain.activeTerrains;

                for (int i = 0; i < activeTerrains.Length; i++)
                {
                    Terrain activeTerrain = activeTerrains[i];

                    if (activeTerrain != null &&
                        activeTerrain.terrainData == data)
                    {
                        activeTerrain.Flush();
                    }
                }

                EditorUtility.SetDirty(data);

                Debug.Log(
                    $"[Trail System] Restored terrain '{snapshot.terrainName}' after Play Mode.");
            }

            snapshots.Clear();
        }

#else

        public static void CaptureIfNeeded(
            Terrain terrain,
            TrailSystemSettings settings)
        {
        }

#endif
    }
}