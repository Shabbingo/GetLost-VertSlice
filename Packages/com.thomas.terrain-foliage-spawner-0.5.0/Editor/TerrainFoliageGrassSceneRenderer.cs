using UnityEditor;
using UnityEngine;

namespace Thomas.TerrainFoliageSpawner.Editor
{
    [InitializeOnLoad]
    internal static class TerrainFoliageGrassSceneRenderer
    {
        static TerrainFoliageGrassSceneRenderer()
        {
            SceneView.duringSceneGui += DrawSceneGrass;
        }

        private static void DrawSceneGrass(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint || sceneView.camera == null)
                return;

            TerrainFoliageGrassRenderer[] renderers =
                Object.FindObjectsByType<TerrainFoliageGrassRenderer>(
                    FindObjectsInactive.Exclude);

            for (int i = 0; i < renderers.Length; i++)
                renderers[i]?.Render(sceneView.camera);
        }
    }
}
