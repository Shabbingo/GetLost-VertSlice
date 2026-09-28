using UnityEngine;

namespace Tom.WalkingController
{
    /// <summary>
    /// Place this on non-Terrain colliders such as mud meshes, wooden bridges,
    /// rocks or custom path objects.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainSurface : MonoBehaviour
    {
        [SerializeField] private TerrainSurfaceProfile profile;
        public TerrainSurfaceProfile Profile => profile;
    }
}
