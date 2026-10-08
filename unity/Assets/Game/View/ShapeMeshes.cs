using System;
using UnityEngine;

namespace Game.View
{
    public enum WorldShape { Circle, Square, Diamond, Triangle, Sector90, Sector180 }

    public sealed class ShapeMeshes : IDisposable
    {
        private const int CircleSegments = 48;
        private readonly Mesh[] meshes = new Mesh[Enum.GetValues(typeof(WorldShape)).Length];
        public Mesh this[WorldShape shape] => meshes[(int)shape];

        public ShapeMeshes()
        {
            meshes[(int)WorldShape.Circle] = Sector("World circle", 360, CircleSegments);
            meshes[(int)WorldShape.Sector90] = Sector("World quarter sector", 90, CircleSegments / 4);
            meshes[(int)WorldShape.Sector180] = Sector("World half sector", 180, CircleSegments / 2);
            meshes[(int)WorldShape.Square] = Polygon("World square", new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) });
            meshes[(int)WorldShape.Diamond] = Polygon("World diamond", new[] { new Vector3(0, -1), new Vector3(1, 0), new Vector3(0, 1), new Vector3(-1, 0) });
            meshes[(int)WorldShape.Triangle] = Polygon("World triangle", new[] { new Vector3(1, 0), new Vector3(-0.7f, 0.7f), new Vector3(-0.7f, -0.7f) });
        }

        private static Mesh Sector(string name, float degrees, int segments)
        {
            var vertices = new Vector3[segments + 2]; var triangles = new int[segments * 3];
            for (var index = 0; index <= segments; index++)
            {
                var angle = (-degrees * 0.5f + degrees * index / segments) * Mathf.Deg2Rad;
                vertices[index + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle));
                if (index == segments) continue;
                triangles[index * 3] = 0; triangles[index * 3 + 1] = index + 1; triangles[index * 3 + 2] = index + 2;
            }
            return Create(name, vertices, triangles);
        }

        private static Mesh Polygon(string name, Vector3[] vertices)
        {
            var triangles = new int[(vertices.Length - 2) * 3];
            for (var index = 0; index < vertices.Length - 2; index++) { triangles[index * 3] = 0; triangles[index * 3 + 1] = index + 1; triangles[index * 3 + 2] = index + 2; }
            return Create(name, vertices, triangles);
        }

        private static Mesh Create(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds(); mesh.UploadMeshData(true); return mesh;
        }

        public void Dispose()
        {
            foreach (var mesh in meshes) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
