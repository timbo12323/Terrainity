using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TerrainityMeshUtility
    {
        internal static int TriangleCount(Mesh mesh)
        {
            // Index counts avoid copying the entire triangle buffer just to show statistics.
            ulong indices = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                indices += mesh.GetIndexCount(submesh);
            return checked((int)(indices / 3));
        }

        internal static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            // Small AddRange arrays otherwise allocate once for every emitted face.
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }
}
