using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreePreviewNavigationChecks
    {
        [MenuItem("Tools/Terrainity/Validate Preview Part Navigation")]
        static void Run()
        {
            try
            {
                using (var preview = new TreePreview())
                {
                    var recipe = new TreeRecipe { height = 5, trunkRadius = .4f, showFoliage = false };
                    recipe.Branches.limbs = 0; recipe.rootsEnabled = false;
                    preview.Build(recipe, 0);
                    Require(Pick(preview, new Vector3(0,2,0)) == TreePreviewPart.Trunk, "Trunk classification");
                    recipe.rootsEnabled = true; recipe.rootCount = 1; recipe.rootForks = 0;
                    preview.Build(recipe, 0);
                    var trunk = new TreeTrunkGrowth(recipe, Vector3.up * recipe.height);
                    var root = TreeRootGrowth.Generate(recipe, trunk, 1, 0)[0];
                    Require(Pick(preview, root.points[9]) == TreePreviewPart.Roots, "Root classification");
                    recipe.rootsEnabled = false; recipe.Branches.limbs = 1; recipe.Branches.depth = 0;
                    preview.Build(recipe, 0);
                    var branch = TreeBranchGrowth.Generate(recipe, new TreeTrunkGrowth(recipe, Vector3.up * recipe.height), 1, 0)[0];
                    Require(Pick(preview, branch.points[TreeBranchGrowth.Segments - 2]) == TreePreviewPart.Branches, "Branch classification");
                    recipe.showFoliage = true;
                    preview.Build(recipe, 0);
                    var foliageImage = preview.ReferenceSnapshot(512);
                    UnityEngine.Object.DestroyImmediate(foliageImage);
                    var camera = ((PreviewRenderUtility)typeof(TreePreview).GetField("renderer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview)).camera;
                    var leaf = preview.Meshes[1]; var positions = leaf.vertices; var indices = leaf.triangles;
                    bool foundFoliage = false;
                    for (int t = 0; t < indices.Length && !foundFoliage; t += 3)
                    {
                        Vector3 center = (positions[indices[t]] + positions[indices[t+1]] + positions[indices[t+2]]) / 3;
                        var projected = camera.WorldToViewportPoint(center);
                        if (projected.z <= 0 || projected.x < 0 || projected.x > 1 || projected.y < 0 || projected.y > 1) continue;
                        foundFoliage = preview.PickTreePart(new Vector2(projected.x * 512, (1-projected.y) * 512), new Vector2(512,512)) == TreePreviewPart.Foliage;
                    }
                    Require(foundFoliage, "Visible foliage classification");
                    Require(preview.PickTreePart(new Vector2(-1000,-1000), new Vector2(512,512)) == TreePreviewPart.None, "Empty click ignored");
                }
                File.WriteAllText("Temp/tree-navigation-checks.txt", "PASS: trunk, roots, branches, foliage, and empty-space preview picks.");
            }
            catch (Exception e) { File.WriteAllText("Temp/tree-navigation-checks.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static TreePreviewPart Pick(TreePreview preview, Vector3 point)
        {
            var screenshot = preview.ReferenceSnapshot(512);
            UnityEngine.Object.DestroyImmediate(screenshot);
            var utility = (PreviewRenderUtility)typeof(TreePreview).GetField("renderer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview);
            var pixel = utility.camera.WorldToViewportPoint(point);
            return preview.PickTreePart(new Vector2(pixel.x * 512, (1 - pixel.y) * 512), new Vector2(512,512));
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
