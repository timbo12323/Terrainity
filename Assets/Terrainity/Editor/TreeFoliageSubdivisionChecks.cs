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
                    recipe.Branches.limbs = 1;
                    recipe.layers = 9;
                    var limb = TreeBranchGrowth.Generate(recipe,
                        new Vector3(recipe.lean * recipe.height * .18f, recipe.height, 0), 1, 0)[0];
                    preview.Build(recipe, 0);
                    var bunched = preview.Meshes[1].vertices;
                    int clusterStride = recipe.foliageCards * 4;
                    Vector3 ClusterCenter(Vector3[] vertices, int cluster)
                    {
                        int first = cluster * clusterStride;
                        return (vertices[first] + vertices[first + 1]) * .5f;
                    }
                    Require(Vector3.Distance(ClusterCenter(bunched, 0), ClusterCenter(bunched, 2)) < .0001f,
                        "Zero distance bunches clusters at the terminal tip");
                    recipe.foliageClusterDistance = 1;
                    preview.Build(recipe, 0);
                    var distributed = preview.Meshes[1].vertices;
                    Require(Vector3.Distance(ClusterCenter(distributed, 0), limb.points[limb.points.Length - 1]) < .0001f
                        && Vector3.Distance(ClusterCenter(distributed, 1), limb.points[limb.points.Length / 2]) < .0001f
                        && Vector3.Distance(ClusterCenter(distributed, 2), limb.points[0]) < .0001f,
                        "Full distance spans only the terminal branch");
                    int twoSidedTriangles = preview.FoliageTriangles;
                    var foliageMaterial = preview.MaterialForMesh(1);
                    if (foliageMaterial.HasProperty("_Cull"))
                        Require(foliageMaterial.GetFloat("_Cull") == (float)UnityEngine.Rendering.CullMode.Off,
                            "Foliage defaults to two-sided rendering");
                    recipe.foliageBackfaceCulling = true;
                    preview.Build(recipe, 0);
                    if (foliageMaterial.HasProperty("_Cull"))
                        Require(foliageMaterial.GetFloat("_Cull") == (float)UnityEngine.Rendering.CullMode.Back,
                            "Backface culling configures the foliage material");
                    else Require(preview.FoliageTriangles * 2 == twoSidedTriangles,
                        "Backface culling removes fallback reverse faces");
                    recipe.foliageBackfaceCulling = false;
                    recipe.foliageClusterDistance = 0;
                    recipe.Branches.limbs = 3;
                    recipe.layers = 6;
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
                    var flatFace = Vector3.Cross(flat[1] - flat[0], flat[3] - flat[0]).normalized;
                    Require(Mathf.Abs(Vector3.Dot(v[8] - flat[8], flatFace)) > .01f,
                        "Card bend rises out of the original flat plane");
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
                    var positiveNormals = mesh.normals;
                    recipe.foliageBend = -.8f;
                    preview.Build(recipe, 0);
                    var mirrored = preview.Meshes[1]; var negative = mirrored.vertices; var negativeNormals = mirrored.normals;
                    for (int i = 0; i < v.Length; i++)
                    {
                        int cardStart = i / 24 * 24;
                        var cardFace = Vector3.Cross(flat[cardStart + 1] - flat[cardStart],
                            flat[cardStart + 3] - flat[cardStart]).normalized;
                        Require(Mathf.Abs(Vector3.Dot((v[i] + negative[i]) * .5f - flat[i], cardFace)) < .0001f,
                            "Positive and negative arcs mirror across the original card plane");
                        int segmentStart = i / 4 * 4;
                        var positiveFace = Vector3.Cross(v[segmentStart + 1] - v[segmentStart],
                            v[segmentStart + 3] - v[segmentStart]).normalized;
                        var negativeFace = Vector3.Cross(negative[segmentStart + 1] - negative[segmentStart],
                            negative[segmentStart + 3] - negative[segmentStart]).normalized;
                        Require(Vector3.Dot(positiveNormals[i], positiveFace) > 0
                            && Vector3.Dot(negativeNormals[i], negativeFace) > 0,
                            "Draped cards keep their outward face orientation");
                    }
                    recipe.foliageBend = .8f;
                    recipe.foliageSizeMin = recipe.foliageSize;
                    preview.Build(recipe, 0);
                    var fixedSize = preview.Meshes[1].vertices;
                    recipe.foliageSizeMin = .5f;
                    preview.Build(recipe, 0);
                    var variedSize = preview.Meshes[1].vertices;
                    Require(!fixedSize.SequenceEqual(variedSize), "Card size range changes geometry");
                    preview.Build(recipe, 0);
                    Require(variedSize.SequenceEqual(preview.Meshes[1].vertices), "Card size variation is seeded");
                    recipe.foliageClusterDistance = .65f;
                    recipe.foliageBackfaceCulling = true;
                    var restored = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(restored.foliageSubdivisions == 6 && restored.foliageBend == .8f
                        && restored.foliageSizeMin == .5f && restored.foliageSize == recipe.foliageSize
                        && restored.foliageClusterDistance == recipe.foliageClusterDistance
                        && restored.foliageBackfaceCulling, "JSON round-trip");
                    var oldRecipe = TreeRecipeJson.From(recipe, 0);
                    oldRecipe.settings.foliageSizeMin = 0;
                    var oldRestored = oldRecipe.Restore(out _);
                    Require(oldRestored.foliageSizeMin == oldRestored.foliageSize, "Legacy fixed card size");

                    recipe.Branches.limbs = 1; recipe.Branches.depth = 0;
                    recipe.layers = 9; recipe.foliageSubdivisions = 1; recipe.foliageBend = 0;
                    recipe.foliageAligned = true; recipe.foliageAlignSides = 2;
                    recipe.foliageClusterDistance = 1; recipe.foliageSizeMin = recipe.foliageSize;
                    recipe.foliageAlignOffsetMin = recipe.foliageAlignOffsetMax = 0;
                    recipe.foliageCardTaper = 0;
                    preview.Build(recipe, 0);
                    var aligned = preview.Meshes[1].vertices;
                    Require(aligned.Length == 3 * 2 * 4, "Aligned rows and side count");
                    Vector3 Socket(int card) => (aligned[card * 4] + aligned[card * 4 + 1]) * .5f;
                    Require(Vector3.Distance(Socket(0), limb.points[0]) < .0001f
                        && Vector3.Distance(Socket(2), limb.points[limb.points.Length / 2]) < .0001f
                        && Vector3.Distance(Socket(4), limb.points[limb.points.Length - 1]) < .0001f,
                        "Aligned cards follow the branch from base to tip");
                    var firstDirection = ((aligned[2] + aligned[3]) * .5f - Socket(0)).normalized;
                    var oppositeDirection = ((aligned[6] + aligned[7]) * .5f - Socket(1)).normalized;
                    Require(Vector3.Dot(firstDirection, oppositeDirection) < -.99f, "Aligned sides are evenly spaced");
                    recipe.foliageCardToBranchSize = 1;
                    preview.Build(recipe, 0);
                    var branchSized = preview.Meshes[1].vertices;
                    Require(branchSized.SequenceEqual(aligned),
                        "Cards on the only branch keep their size at every cluster");
                    recipe.foliageCardToBranchSize = 0;
                    recipe.foliageBend = .8f;
                    preview.Build(recipe, 0);
                    var bentSides = preview.Meshes[1].vertices;
                    var branchAxis = (limb.points[limb.points.Length - 1] - limb.points[0]).normalized;
                    var sweepAxis = Vector3.Cross(branchAxis, firstDirection).normalized;
                    float firstSweep = Vector3.Dot(bentSides[2] - aligned[2], sweepAxis);
                    float oppositeSweep = Vector3.Dot(bentSides[6] - aligned[6], sweepAxis);
                    Require(firstSweep > .01f && oppositeSweep > .01f,
                        "Aligned cards curve out of plane in the same direction on both branch sides");
                    recipe.foliageBend = 0;
                    recipe.foliageClusterDistance = .5f;
                    preview.Build(recipe, 0);
                    var partialUntapered = preview.Meshes[1].vertices;
                    recipe.foliageCardTaper = 1;
                    preview.Build(recipe, 0);
                    var partialTapered = preview.Meshes[1].vertices;
                    Require(partialUntapered.Take(8).SequenceEqual(partialTapered.Take(8)),
                        "The first placed row keeps its chosen card sizes when spacing is partial");
                    recipe.foliageClusterDistance = 1;
                    recipe.foliageAlignOffsetMin = recipe.foliageAlignOffsetMax = .2f;
                    preview.Build(recipe, 0);
                    var tapered = preview.Meshes[1].vertices;
                    Require(Vector3.Distance((tapered[0] + tapered[1]) * .5f, limb.points[0]) < .0001f
                        && Vector3.Distance((tapered[4] + tapered[5]) * .5f, limb.points[0]) < .0001f
                        && Vector3.Distance((tapered[8] + tapered[9]) * .5f, limb.points[limb.points.Length / 2] + Vector3.up * .2f) < .0001f
                        && Vector3.Distance((tapered[12] + tapered[13]) * .5f, limb.points[limb.points.Length / 2] + Vector3.up * .2f) < .0001f,
                        "Alternating rows offset cards on both sides");
                    Require(Vector3.Distance(tapered[18], tapered[16]) < Vector3.Distance(tapered[2], tapered[0]) * .25f,
                        "Tip cards taper toward the branch end");
                    var alignedAgain = preview.Meshes[1].vertices;
                    preview.Build(recipe, 0);
                    Require(alignedAgain.SequenceEqual(preview.Meshes[1].vertices), "Aligned foliage is deterministic");
                    var alignedLod = TreeLodGenerator.ReduceFoliage(preview.Meshes[1], recipe, 2, new TreeLodSettings());
                    try { Require(alignedLod.vertexCount == 3 * 2 * 4, "Aligned LOD retains complete cards on both sides"); }
                    finally { UnityEngine.Object.DestroyImmediate(alignedLod); }
                    recipe.foliageCardToBranchSize = .7f;
                    var alignedRestored = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(alignedRestored.foliageAligned && alignedRestored.foliageAlignSides == 2
                        && alignedRestored.foliageAlignOffsetMin == .2f && alignedRestored.foliageAlignOffsetMax == .2f
                        && alignedRestored.foliageCardTaper == 1 && alignedRestored.foliageCardToBranchSize == .7f,
                        "Aligned foliage JSON round-trip");
                    recipe.Branches.depth = 1; recipe.Branches.minDepth = 1;
                    var nested = TreeBranchGrowth.Generate(recipe,
                        new Vector3(recipe.lean * recipe.height * .18f, recipe.height, 0), 1, 0);
                    var pathStarts = TreePreview.FoliagePathStarts(nested, out float furthest);
                    int childIndex = nested.FindIndex(branch => branch.parentIndex >= 0);
                    Require(childIndex >= 0 && pathStarts[childIndex] > 0
                        && Mathf.Abs(pathStarts[childIndex] - TreePreview.ArcLength(nested[nested[childIndex].parentIndex].points,
                            nested[childIndex].attachment)) < .0001f
                        && furthest + .0001f >= pathStarts[childIndex] + TreePreview.ArcLength(nested[childIndex].points, 1),
                        "Card size distance includes parent branch path");
                    recipe.Branches.limbs = 3; recipe.Branches.depth = 0; recipe.Branches.minDepth = 0;
                    recipe.layers = 3; recipe.foliageCardTaper = 0;
                    recipe.foliageAlignOffsetMin = recipe.foliageAlignOffsetMax = 0;
                    recipe.foliageCardToBranchSize = 0;
                    preview.Build(recipe, 0);
                    var unscaledBranches = preview.Meshes[1].vertices;
                    recipe.foliageCardToBranchSize = 1;
                    preview.Build(recipe, 0);
                    var scaledBranches = preview.Meshes[1].vertices;
                    var sampleBranches = TreeBranchGrowth.Generate(recipe,
                        new Vector3(recipe.lean * recipe.height * .18f, recipe.height, 0), 1, 0);
                    var branchStarts = TreePreview.FoliagePathStarts(sampleBranches, out float longestBranch);
                    Require(sampleBranches.Count == 3, "Expected three terminal branches for size comparison");
                    bool reducedShorterBranch = false;
                    for (int branch = 0; branch < sampleBranches.Count; branch++)
                    {
                        float totalLength = branchStarts[branch] + TreePreview.ArcLength(sampleBranches[branch].points, 1);
                        float expected = 1f - .8f * (1f - totalLength / longestBranch);
                        int start = branch * 8;
                        float originalWidth = Vector3.Distance(unscaledBranches[start], unscaledBranches[start + 1]);
                        float scaledWidth = Vector3.Distance(scaledBranches[start], scaledBranches[start + 1]);
                        Require(Mathf.Abs(scaledWidth / originalWidth - expected) < .001f,
                            "All cards on a branch use its total path length");
                        if (expected < .99f) reducedShorterBranch = true;
                    }
                    Require(reducedShorterBranch, "Shorter branches should have smaller cards");
                }
                File.WriteAllText("Temp/tree-foliage-checks.txt", "PASS: cluster placement, aligned foliage spacing/sides/offset/taper/LOD, total-branch-length card sizing, foliage backface culling, triangle counts, original corners, three-dimensional draped card arcs and face normals, stem attachment, seamless subdivisions, consistent card tint, seeded card size range, JSON round-trip.");
            }
            catch (Exception e) { File.WriteAllText("Temp/tree-foliage-checks.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
