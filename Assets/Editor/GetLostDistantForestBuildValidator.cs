#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GetLost.EnvironmentSystem.Editor
{
    public sealed class GetLostDistantForestBuildValidator :
        IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            GetLostDistantForestRenderer[] renderers =
                Object.FindObjectsByType<GetLostDistantForestRenderer>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            int missingCount = 0;
            bool changedAny = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                GetLostDistantForestRenderer renderer = renderers[i];

                if (renderer == null)
                    continue;

                renderer.ResolveShaderReferences();

                if (renderer.HasBuildSafeShaderReferences())
                {
                    EditorUtility.SetDirty(renderer);
                    changedAny = true;
                }
                else
                {
                    missingCount++;

                    Debug.LogError(
                        "[Distant Forest Build Check] " +
                        renderer.name +
                        " is missing one or both explicit Distant Forest shader references. " +
                        "Assign GetLostDistantForestSourceMesh.shader and GetLostDistantForestProxy.shader on the component.",
                        renderer);
                }
            }

            if (changedAny)
            {
                Scene scene = SceneManager.GetActiveScene();

                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.MarkSceneDirty(scene);
            }

            if (missingCount > 0)
            {
                throw new BuildFailedException(
                    "Get Lost Distant Forest build check failed. " +
                    missingCount +
                    " renderer(s) are missing explicit shader references. " +
                    "This would allow Unity to strip the Hidden shaders from the player.");
            }
        }
    }
}

#endif
