using UnityEngine;

namespace Thomas.TerrainFoliageSpawner
{
    [CreateAssetMenu(
        fileName = "Terrain Foliage Prototype Library",
        menuName = "Thomas/Terrain Foliage Prototype Library")]
    public sealed class TerrainFoliagePrototypeLibrary : ScriptableObject
    {
        [Tooltip("TerrainData used as the authoritative source for complete DetailPrototype and TreePrototype settings.")]
        [SerializeField] private TerrainData templateTerrainData;

        [Tooltip("When enabled, the spawner's assigned TerrainData can be used as a fallback source when a required prototype is missing from the template.")]
        [SerializeField] private bool allowSpawnerTerrainFallback = true;

        public TerrainData TemplateTerrainData => templateTerrainData;
        public bool AllowSpawnerTerrainFallback => allowSpawnerTerrainFallback;
    }
}
