using UnityEngine;

namespace GetLost.Trails
{
    public static class TrailTerrainUtility
    {
        public static Terrain FindTerrainAt(Vector3 worldPosition)
        {
            Terrain[] terrains = Terrain.activeTerrains;

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;

                if (worldPosition.x >= origin.x &&
                    worldPosition.x <= origin.x + size.x &&
                    worldPosition.z >= origin.z &&
                    worldPosition.z <= origin.z + size.z)
                {
                    return terrain;
                }
            }

            return null;
        }

        public static string GetTerrainId(Terrain terrain)
        {
            if (terrain == null)
                return string.Empty;

            // Scene/name based ID keeps JSON readable. Give terrain GameObjects unique names.
            return terrain.gameObject.scene.name + "/" + terrain.gameObject.name;
        }

        public static float GetSlopeDegrees(Terrain terrain, Vector3 worldPosition)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0f;

            Vector3 local = worldPosition - terrain.transform.position;
            Vector3 size = terrain.terrainData.size;

            float nx = Mathf.Clamp01(local.x / size.x);
            float nz = Mathf.Clamp01(local.z / size.z);

            Vector3 normal = terrain.terrainData.GetInterpolatedNormal(nx, nz);
            return Vector3.Angle(normal, Vector3.up);
        }
    }
}
