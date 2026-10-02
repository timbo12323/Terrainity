using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeSimplificationChecks
    {
        [MenuItem("Tools/Terrainity/Validate Live Simplification")]
        static void Run()
        {
            try
            {
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var samples = new List<float>();
                for (int ring = 0; ring <= 10; ring++)
                {
                    samples.Add(ring / 10f);
                    for (int side = 0; side <= 8; side++)
                    {
                        float angle = (side % 8) * Mathf.PI / 4;
                        var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                        v.Add(Vector3.up * ring + radial); n.Add(radial); uv.Add(new Vector2(side / 8f, ring));
                    }
                }
                int rings = TreeMeshSimplifier.Simplify(v, n, uv, 8, samples, new List<float> { .5f }, .001f);
                Require(rings == 3 && uv[9].y == 5 && uv[18].y == 10, "Redundant rings removed; socket and endpoint retained");
                Require(v[0] == v[8] && uv[0].x == 0 && uv[8].x == 1, "UV seam retained");
                var recipe = new TreeRecipe { trunkSegments = 40, branchSegments = 24, trunkBend = .3f };
                recipe.Branches.limbs = 5; recipe.Branches.depth = 1;
                using (var preview = new TreePreview())
                {
                    preview.Build(recipe, 0);
                    int full = preview.WoodTriangles;
                    int fullVertices = preview.Meshes[0].vertexCount;
                    var leaves = preview.Meshes[1].vertices;
                    recipe.simplifyWood = true; recipe.simplificationTolerance = .002f;
                    var restored = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(restored.simplifyWood && restored.simplificationTolerance == .002f, "Recipe round-trip");
                    preview.Build(recipe, 0);
                    Require(preview.WoodTriangles < full, "Live triangle reduction");
                    Require(preview.WoodTriangles + preview.RemovedWoodTriangles == full, "Accurate before/after statistics");
                    Require(preview.Meshes[0].vertexCount + preview.RemovedWoodVertices == fullVertices,
                        "Accurate removed vertex statistics");
                    Require(leaves.SequenceEqual(preview.Meshes[1].vertices), "Foliage unchanged");
                    foreach (var mesh in preview.Meshes)
                    {
                        Require(mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount), "Valid indices");
                        Require(mesh.vertices.All(p => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z)), "Finite vertices");
                    }
                    int smooth = preview.WoodTriangles;
                    recipe.barkSurfaceDetail = .7f;
                    preview.Build(recipe, 0);
                    for (int level = 1; level <= 2; level++)
                    {
                        var lod = TreeLodGenerator.Build(recipe, 0, level, preview.Meshes[1], new TreeLodSettings());
                        try { Require(lod.vertexCount > 0 && lod.triangles.All(i => i < lod.vertexCount && i >= 0), "Valid simplified LOD"); }
                        finally { UnityEngine.Object.DestroyImmediate(lod); }
                    }
                    File.WriteAllText("Temp/tree-simplification-checks.txt", $"PASS: socket/end/seam preservation, recipe round-trip, mesh validity, unchanged foliage, accurate statistics, and bark/LOD compatibility. Test wood triangles: {full} -> {smooth}.");
                }
            }
            catch (Exception e) { File.WriteAllText("Temp/tree-simplification-checks.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
