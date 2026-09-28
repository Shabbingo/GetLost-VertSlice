#if UNITY_EDITOR
using UnityEditor;

namespace Tom.PathCreator.Editor
{
    /// <summary>
    /// Restores runtime Terrain height patches before Unity finishes leaving
    /// Play Mode. This is more reliable than depending only on MonoBehaviour
    /// destruction callbacks during editor teardown.
    /// </summary>
    [InitializeOnLoad]
    internal static class TerrainRuntimeBackupEditorHook
    {
        static TerrainRuntimeBackupEditorHook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(
            PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            TerrainRuntimeBackupRegistry.RestoreAll();
        }
    }
}
#endif
