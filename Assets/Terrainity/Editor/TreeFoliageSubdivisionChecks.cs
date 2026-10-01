using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeFoliageSubdivisionChecks
    {
        [MenuItem("Tools/Terrainity/Validate Foliage Subdivisions")]
        static void Run()
        {
            try
            {
                var recipe = new TreeRecipe { showFoliage = true, foliageStemAttached = true, foliageStemPivot = new Vector2(.5f, 0) };
                recipe.Branches.limbs = 3; recipe.Branches.depth = 0;
                using (var preview = new TreePreview())
                {
                    preview.Build(recipe, 0);
                    var original = preview.Meshes[1].vertices;
                    int triangles = preview.FoliageTriangles;
                    recipe.foliageSubdivisions = 6;
                    preview.Build(recipe, 0);
                    var flat = preview.Meshes[1].vertices;
                    Require(flat.Length == original.Length * 6 && preview.FoliageTriangles == triangles * 6, "Segment counts");
                    for (int card = 0; card < original.Length / 4; card++)
                    {
                        int n = card * 24, old = card * 4;
                        Require(flat[n] == original[old] && flat[n + 1] == original[old + 1] && flat[n + 22] == original[old + 2] && flat[n + 23] == original[old + 3], "Unbent card corners unchanged");
                    }
                    recipe.foliageBend = .8f;
                    preview.Build(recipe, 0);
                    var mesh = preview.Meshes[1]; var v = mesh.vertices; var tint = mesh.uv2;
                    Require(!flat.SequenceEqual(v), "Bend deforms cards");
                    for (int card = 0; card < v.Length; card += 24)
                    {
                        Require(v[card] == flat[card] && v[card + 1] == flat[card + 1], "Stem edge stays attached");
                        for (int i = 1; i < 24; i++) Require(tint[card + i].x == tint[card].x, "Per-card tint remains uniform");
                        for (int row = 0; row < 5; row++) Require(v[card + row * 4 + 3] == v[card + (row + 1) * 4] && v[card + row * 4 + 2] == v[card + (row + 1) * 4 + 1], "No subdivision cracks");
                    }
                    for (int level = 1; level <= 2; level++)
                    {
                        var lod = TreeLodGenerator.ReduceFoliage(mesh, recipe, level, new TreeLodSettings());
                        try { Require(lod.vertexCount % 24 == 0 && lod.triangles.Length / 3 == lod.vertexCount / 2 && lod.uv.All(p=>p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1), "LODs retain complete subdivided cards"); }
                        finally { UnityEngine.Object.DestroyImmediate(lod); }
                    }
                    var restored = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(restored.foliageSubdivisions == 6 && restored.foliageBend == .8f, "JSON round-trip");
                }
                File.WriteAllText("Temp/tree-foliage-checks.txt", "PASS: triangle counts, original corners, curved geometry, stem attachment, seamless subdivisions, consistent card tint, complete LOD cards, JSON round-trip.");
            }
            catch (Exception e) { File.WriteAllText("Temp/tree-foliage-checks.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
