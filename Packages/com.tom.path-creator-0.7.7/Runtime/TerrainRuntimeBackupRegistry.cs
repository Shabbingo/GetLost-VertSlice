using System.Collections.Generic;
using UnityEngine;

namespace Tom.PathCreator
{
    /// <summary>
    /// Stores runtime Terrain height and foliage changes, then restores them
    /// before Play Mode exits.
    /// </summary>
    public static class TerrainRuntimeBackupRegistry
    {
        private sealed class HeightPatch
        {
            public int x;
            public int z;
            public float[,] values;
        }

        private sealed class DetailPatch
        {
            public int layer;
            public int x;
            public int z;
            public int[,] values;
        }

        private sealed class TerrainBackup
        {
            public TerrainData terrainData;
            public int heightResolution;
            public int detailWidth;
            public int detailHeight;

            public readonly List<HeightPatch> heightPatches =
                new List<HeightPatch>();

            public readonly List<DetailPatch> detailPatches =
                new List<DetailPatch>();

            public TreeInstance[] originalTrees;
            public bool treesCaptured;
        }

        private sealed class DisabledObjectBackup
        {
            public GameObject gameObject;
            public bool wasActive;
        }

        private sealed class RestoreDriver : MonoBehaviour
        {
            private bool restored;

            private void OnDisable()
            {
                RestoreOnce();
            }

            private void OnDestroy()
            {
                RestoreOnce();
            }

            private void RestoreOnce()
            {
                if (restored)
                {
                    return;
                }

                restored = true;
                RestoreAll();
            }
        }

        private static readonly Dictionary<TerrainData, TerrainBackup>
            Backups = new Dictionary<TerrainData, TerrainBackup>();

        private static readonly Dictionary<GameObject, DisabledObjectBackup>
            DisabledObjects =
                new Dictionary<GameObject, DisabledObjectBackup>();

        private static RestoreDriver restoreDriver;

        public static bool HasBackups =>
            Backups.Count > 0 || DisabledObjects.Count > 0;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Backups.Clear();
            DisabledObjects.Clear();
            restoreDriver = null;
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterApplicationQuitFallback()
        {
            Application.quitting -= RestoreAll;
            Application.quitting += RestoreAll;
        }

        public static void CaptureHeightPatch(
            Terrain terrain,
            int x,
            int z,
            int width,
            int height)
        {
            if (!TryGetBackup(terrain, out TerrainBackup backup))
            {
                return;
            }

            ClampPatch(
                backup.heightResolution,
                backup.heightResolution,
                ref x,
                ref z,
                ref width,
                ref height);

            if (width <= 0 || height <= 0)
            {
                return;
            }

            backup.heightPatches.Add(
                new HeightPatch
                {
                    x = x,
                    z = z,
                    values = backup.terrainData.GetHeights(
                        x,
                        z,
                        width,
                        height)
                });

            EnsureRestoreDriver();
        }

        // Legacy method name retained for package compatibility.
        public static void CapturePatch(
            Terrain terrain,
            int x,
            int z,
            int width,
            int height)
        {
            CaptureHeightPatch(terrain, x, z, width, height);
        }

        public static void CaptureTrees(Terrain terrain)
        {
            if (!TryGetBackup(terrain, out TerrainBackup backup) ||
                backup.treesCaptured)
            {
                return;
            }

            TreeInstance[] trees = backup.terrainData.treeInstances;

            backup.originalTrees = trees != null
                ? (TreeInstance[])trees.Clone()
                : System.Array.Empty<TreeInstance>();

            backup.treesCaptured = true;
            EnsureRestoreDriver();
        }

        public static void CaptureDetailPatch(
            Terrain terrain,
            int layer,
            int x,
            int z,
            int width,
            int height)
        {
            if (!TryGetBackup(terrain, out TerrainBackup backup))
            {
                return;
            }

            TerrainData data = backup.terrainData;

            if (layer < 0 || layer >= data.detailPrototypes.Length)
            {
                return;
            }

            ClampPatch(
                backup.detailWidth,
                backup.detailHeight,
                ref x,
                ref z,
                ref width,
                ref height);

            if (width <= 0 || height <= 0)
            {
                return;
            }

            backup.detailPatches.Add(
                new DetailPatch
                {
                    layer = layer,
                    x = x,
                    z = z,
                    values = data.GetDetailLayer(
                        x,
                        z,
                        width,
                        height,
                        layer)
                });

            EnsureRestoreDriver();
        }

        public static void CaptureDisabledObject(GameObject target)
        {
            if (!Application.isPlaying || target == null)
            {
                return;
            }

            if (!DisabledObjects.ContainsKey(target))
            {
                DisabledObjects.Add(
                    target,
                    new DisabledObjectBackup
                    {
                        gameObject = target,
                        wasActive = target.activeSelf
                    });
            }

            EnsureRestoreDriver();
        }

        public static void RestoreAll()
        {
            foreach (TerrainBackup backup in Backups.Values)
            {
                RestoreTerrainBackup(backup);
            }

            foreach (DisabledObjectBackup backup in DisabledObjects.Values)
            {
                if (backup != null && backup.gameObject != null)
                {
                    backup.gameObject.SetActive(backup.wasActive);
                }
            }

            Terrain[] activeTerrains = Terrain.activeTerrains;

            for (int i = 0; i < activeTerrains.Length; i++)
            {
                if (activeTerrains[i] != null)
                {
                    activeTerrains[i].Flush();
                }
            }

            Backups.Clear();
            DisabledObjects.Clear();
            restoreDriver = null;
        }

        private static void RestoreTerrainBackup(TerrainBackup backup)
        {
            if (backup == null || backup.terrainData == null)
            {
                return;
            }

            TerrainData data = backup.terrainData;

            if (backup.treesCaptured)
            {
                data.treeInstances =
                    backup.originalTrees ??
                    System.Array.Empty<TreeInstance>();
            }

            if (data.detailWidth == backup.detailWidth &&
                data.detailHeight == backup.detailHeight)
            {
                for (int i = backup.detailPatches.Count - 1;
                    i >= 0;
                    i--)
                {
                    DetailPatch patch = backup.detailPatches[i];

                    if (patch == null ||
                        patch.values == null ||
                        patch.layer < 0 ||
                        patch.layer >= data.detailPrototypes.Length)
                    {
                        continue;
                    }

                    data.SetDetailLayer(
                        patch.x,
                        patch.z,
                        patch.layer,
                        patch.values);
                }
            }
            else if (backup.detailPatches.Count > 0)
            {
                Debug.LogWarning(
                    "Could not restore Terrain detail patches because the " +
                    "detail resolution changed during Play Mode.",
                    data);
            }

            if (data.heightmapResolution == backup.heightResolution)
            {
                for (int i = backup.heightPatches.Count - 1;
                    i >= 0;
                    i--)
                {
                    HeightPatch patch = backup.heightPatches[i];

                    if (patch == null || patch.values == null)
                    {
                        continue;
                    }

                    data.SetHeightsDelayLOD(
                        patch.x,
                        patch.z,
                        patch.values);
                }

                if (backup.heightPatches.Count > 0)
                {
                    data.SyncHeightmap();
                }
            }
            else if (backup.heightPatches.Count > 0)
            {
                Debug.LogWarning(
                    "Could not restore Terrain height patches because the " +
                    "heightmap resolution changed during Play Mode.",
                    data);
            }
        }

        private static bool TryGetBackup(
            Terrain terrain,
            out TerrainBackup backup)
        {
            backup = null;

            if (!Application.isPlaying ||
                terrain == null ||
                terrain.terrainData == null)
            {
                return false;
            }

            TerrainData data = terrain.terrainData;

            if (!Backups.TryGetValue(data, out backup))
            {
                backup = new TerrainBackup
                {
                    terrainData = data,
                    heightResolution = data.heightmapResolution,
                    detailWidth = data.detailWidth,
                    detailHeight = data.detailHeight
                };

                Backups.Add(data, backup);
            }

            return true;
        }

        private static void ClampPatch(
            int maxWidth,
            int maxHeight,
            ref int x,
            ref int z,
            ref int width,
            ref int height)
        {
            int requestedMaxX = x + width;
            int requestedMaxZ = z + height;

            x = Mathf.Clamp(x, 0, Mathf.Max(0, maxWidth - 1));
            z = Mathf.Clamp(z, 0, Mathf.Max(0, maxHeight - 1));

            int maxX = Mathf.Clamp(requestedMaxX, x, maxWidth);
            int maxZ = Mathf.Clamp(requestedMaxZ, z, maxHeight);

            width = maxX - x;
            height = maxZ - z;
        }

        private static void EnsureRestoreDriver()
        {
            if (restoreDriver != null)
            {
                return;
            }

            var driverObject =
                new GameObject(
                    "[Tom Path Creator] Terrain Backup Restore");

            driverObject.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(driverObject);

            restoreDriver =
                driverObject.AddComponent<RestoreDriver>();
        }
    }
}
