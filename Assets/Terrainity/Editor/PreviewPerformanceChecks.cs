using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class PreviewPerformanceChecks
    {
        [MenuItem("Tools/Terrainity/Validate Preview Performance")]
        static void Run()
        {
            try
            {
                var recipe = new RockRecipe { subdivisions = 5 };
                using (var preview = new TreePreview())
                {
                    preview.BuildRock(recipe, 0);
                    var mesh = preview.Meshes[0]; var material = preview.MaterialForMesh(0);
                    var timer = Stopwatch.StartNew();
                    for (int i = 0; i < 20; i++)
                    {
                        var rebuilt = RockGenerator.Build(recipe, 0);
                        UnityEngine.Object.DestroyImmediate(rebuilt);
                    }
                    double rebuildTime = timer.Elapsed.TotalMilliseconds;
                    timer.Restart();
                    for (int i = 0; i < 20; i++)
                    {
                        recipe.roughness = i / 20f; preview.BuildRock(recipe, 0);
                        if (preview.Meshes[0] != mesh || preview.MaterialForMesh(0) != material) throw new Exception("Appearance edit recreated mesh or material.");
                    }
                    double appearanceTime = timer.Elapsed.TotalMilliseconds;
                    recipe.width += 1; preview.BuildRock(recipe, 0);
                    if (ReferenceEquals(preview.Meshes[0], mesh)) throw new Exception("Geometry edit failed to rebuild.");
                    var expected = RockGenerator.Build(recipe, 0);
                    try
                    {
                        if (expected.bounds != preview.Meshes[0].bounds || expected.vertexCount != preview.Meshes[0].vertexCount) throw new Exception("Preview geometry differs from export geometry.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(expected); }
                    File.WriteAllText("Temp/terrainity-performance-result.txt", $"PASS: 20 high-resolution geometry rebuilds: {rebuildTime:F1} ms; 20 appearance updates: {appearanceTime:F1} ms. Mesh/material reuse, shape invalidation and export geometry agreement verified.");
                }
            }
            catch (Exception e) { File.WriteAllText("Temp/terrainity-performance-result.txt", "FAIL: " + e); }
        }
    }
}
