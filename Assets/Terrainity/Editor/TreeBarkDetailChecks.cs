using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeBarkDetailChecks
    {
        [MenuItem("Tools/Terrainity/Validate Bark Detail")]
        static void Run()
        {
            try
            {
                var recipe = new TreeRecipe { height = 6, trunkRadius = .5f, trunkBend = .6f, trunkTwist = 80, showFoliage = false, barkDetailScale = 1, barkDetailResolution = 3 };
                recipe.Branches.limbs = 3; recipe.Branches.depth = 1;
                using (var builder = new TreePreview())
                {
                    builder.Build(recipe, 0);
                    int baselineCount = builder.WoodTriangles;
                    var baseline = builder.Meshes[0].vertices;
                    Snapshot(builder, "Temp/bark-detail-off.png");
                    recipe.barkSurfaceDetail = .8f;
                    foreach (bool trunk in new[] { true, false })
                    {
                        int sidesA = 6, segmentsA = 8, sidesB = 7, segmentsB = 9;
                        TreeBarkDetail.Resolution(recipe, .1f, trunk, ref sidesA, ref segmentsA);
                        TreeBarkDetail.Resolution(recipe, .1f, trunk, ref sidesB, ref segmentsB);
                        Require(sidesB == sidesA + 1 && segmentsB == segmentsA + 1, "Base subdivisions remain effective with relief");
                    }
                    Require(Enumerable.Range(0, 16).Any(i => Mathf.Abs(TreeBarkDetail.Relief(recipe, i * .3f, .5f, .006f, 1)) > .00001f), "Small branches receive surface detail");
                    var roundTrip = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(roundTrip.barkSurfaceDetail == .8f && roundTrip.barkDetailResolution == 3 && roundTrip.barkDetailScale == 1, "JSON round-trip");
                    builder.Build(recipe, 0);
                    int detailedCount = builder.WoodTriangles;
                    Require(detailedCount > baselineCount, "Additional surface geometry");
                    var detailed = builder.Meshes[0].vertices;
                    Require(detailed.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), "Finite vertices");
                    Require(builder.Meshes[0].normals.All(n => n.sqrMagnitude > .9f && n.sqrMagnitude < 1.1f), "Unit surface normals");
                    Snapshot(builder, "Temp/bark-detail-on.png");
                    builder.Build(recipe, 0);
                    Require(detailed.SequenceEqual(builder.Meshes[0].vertices), "Repeatable surface");
                    recipe.barkDetailScale = 2;
                    builder.Build(recipe, 0);
                    Require(!detailed.SequenceEqual(builder.Meshes[0].vertices), "Scale changes surface");
                    recipe.barkDetailScale = 1;
                    int previous = detailedCount;
                    for (int level = 1; level <= 2; level++)
                    {
                        var lod = TreeLodGenerator.Build(recipe, 0, level, null, new TreeLodSettings());
                        try { int triangles = (int)lod.GetIndexCount(0) / 3; Require(triangles < previous, "LOD triangle reduction"); previous = triangles; }
                        finally { UnityEngine.Object.DestroyImmediate(lod); }
                    }
                    for (int i = 0; i < 20; i++)
                    {
                        float distance = i * .35f;
                        float a = TreeBarkDetail.Relief(recipe, 0, distance, .4f, 1);
                        float b = TreeBarkDetail.Relief(recipe, Mathf.PI * 2, distance, .4f, 1);
                        Require(Mathf.Abs(a - b) < .00001f, "Seam continuity");
                        Require(TreeBarkDetail.Relief(recipe, 1, distance, .4f, 0) == 0, "Socket protection");
                    }
                    recipe.barkSurfaceDetail = 0;
                    builder.Build(recipe, 0);
                    Require(baseline.SequenceEqual(builder.Meshes[0].vertices), "Disabling detail restores original geometry");
                    File.WriteAllText("Temp/bark-detail-checks.txt", $"PASS: recipe round-trip, deterministic relief, scale changes, finite vertices/normals, seam continuity, protected sockets, LOD reductions, and exact disabled geometry. Wood triangles: {baselineCount} off, {detailedCount} detailed, {previous} LOD2.");
                }
            }
            catch (Exception e) { File.WriteAllText("Temp/bark-detail-checks.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static void Snapshot(TreePreview preview, string path)
        {
            var image = preview.ReferenceSnapshot(768);
            try { File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
