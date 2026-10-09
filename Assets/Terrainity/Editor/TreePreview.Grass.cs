using UnityEngine;

namespace Terrainity.Editor
{
    internal sealed partial class TreePreview
    {
        bool grassStudy;
        internal void BuildGrass(GrassRecipe recipe, GrassStudy study)
        {
            ClearMeshes(); previousRock = null; grassStudy = true;
            if (materials[0] == null) return;
            materials[0].shader = Shader.Find("Terrainity/Grass") ?? treeShader;
            var mesh = GrassGenerator.Build(recipe,study);
            meshes.Add(mesh); materialIndices.Add(0); bounds = mesh.bounds;
            WoodTriangles = TerrainityMeshUtility.TriangleCount(mesh); FoliageTriangles = 0;
            RemovedWoodTriangles = RemovedWoodVertices = 0;
            tintRamps[0] = GrassGenerator.Ramp(recipe,tintRamps[0]);
            tintRamps[0].hideFlags = HideFlags.HideAndDontSave;
            GrassGenerator.Configure(materials[0],tintRamps[0],recipe);
        }
    }
}
