using UnityEngine;

namespace GetLost.EnvironmentSystem
{
    internal static class GetLostDistantForestProxyMesh
    {
        public static Mesh Create()
        {
            const int planeCount = 3;
            const int vertsPerPlane = 4;
            const int indicesPerPlane = 6;

            Vector3[] vertices = new Vector3[planeCount * vertsPerPlane];
            Vector2[] uvs = new Vector2[planeCount * vertsPerPlane];
            int[] triangles = new int[planeCount * indicesPerPlane];

            for (int plane = 0; plane < planeCount; plane++)
            {
                float angle = plane * 60f * Mathf.Deg2Rad;
                Vector3 right = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.5f;

                int v = plane * vertsPerPlane;
                vertices[v + 0] = -right;
                vertices[v + 1] = right;
                vertices[v + 2] = right + Vector3.up;
                vertices[v + 3] = -right + Vector3.up;

                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(1f, 0f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(0f, 1f);

                int t = plane * indicesPerPlane;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 0;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            Mesh mesh = new Mesh
            {
                name = "GetLost Distant Forest Fallback Proxy",
                hideFlags = HideFlags.HideAndDontSave
            };

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(1.5f, 1.1f, 1.5f));
            return mesh;
        }
    }
}
