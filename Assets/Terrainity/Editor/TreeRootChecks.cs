using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeRootChecks
    {
        [MenuItem("Tools/Terrainity/Validate Roots and Ground Placement")]
        static void Run()
        {
            try
            {
                CheckIndependentBends();
                var socketRecipe = new TreeRecipe { branchSides = 8, branchSegments = 12, barkSurfaceDetail = 0 };
                socketRecipe.Branches.attachmentThickness = 2;
                foreach (float angle in new[] { 45f, 90f, 115f, 160f })
                using (var socketPreview = new TreePreview())
                {
                    var direction = Quaternion.Euler(0, 0, angle) * Vector3.up;
                    var socketPoints = Enumerable.Range(0, 13).Select(i => direction * (i / 6f)).ToArray();
                    var limb = new TreeBranchGrowth.Limb { points = socketPoints, radius = .2f, parentRadius = .5f, parentDirection = Vector3.up, parentIndex = -1, level = 0 };
                    typeof(TreePreview).GetMethod("CurvedLimb", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(socketPreview, new object[] { socketRecipe, new System.Collections.Generic.List<TreeBranchGrowth.Limb> { limb }, 0 });
                    var mesh = socketPreview.Meshes[0];
                    var vertices = mesh.vertices;
                    // Every ring remains perpendicular to a straight limb, including downward sockets.
                    int stride = 9;
                    int rings = vertices.Length / stride - 2;
                    for (int ring = 0; ring < rings; ring++)
                        for (int side = 1; side < stride; side++)
                            Require(Mathf.Abs(Vector3.Dot(vertices[ring * stride + side] - vertices[ring * stride], direction)) < .0001f, "Socket ring stays perpendicular to limb");
                    for (int ring = 1; ring < rings; ring++)
                        for (int side = 0; side < stride; side++)
                            Require(Vector3.Dot(vertices[ring * stride + side] - vertices[(ring - 1) * stride + side], direction) > 0, "Socket faces never fold backward");
                }
                var signed = new TreeRecipe { trunkBend = .7f, lean = .6f, trunkBends = 9, trunkTwist = 720 };
                var positive = new TreeTrunkGrowth(signed, new Vector3(signed.lean * 7 * .18f, 7, 0));
                signed.trunkBend = -.7f; signed.lean = -.6f;
                var negative = new TreeTrunkGrowth(signed, new Vector3(signed.lean * 7 * .18f, 7, 0));
                for (int i = 0; i <= TreeTrunkGrowth.Segments; i++)
                    Require((negative.points[i] - new Vector3(-positive.points[i].x, positive.points[i].y, -positive.points[i].z)).sqrMagnitude < 1e-9f, "Signed bend and lean mirror correctly");
                signed.trunkBend = .7f; signed.lean = 0; signed.trunkBends = 1;
                var twoTurns = new TreeTrunkGrowth(signed, Vector3.up * 7);
                signed.trunkTwist = 360;
                var oneTurn = new TreeTrunkGrowth(signed, Vector3.up * 7);
                Require((twoTurns.points[12] - oneTurn.points[12]).sqrMagnitude > .01f, "720-degree twist is not clamped to 360");
                signed.trunkTwist = -720; signed.trunkBends = 9; signed.trunkBend = -.7f; signed.lean = -.6f;
                var signedRestored = TreeRecipeJson.From(signed, 0).Restore(out _);
                Require(signedRestored.trunkBend == -.7f && signedRestored.lean == -.6f && signedRestored.trunkBends == 9 && signedRestored.trunkTwist == -720, "Extended trunk settings survive JSON");
                var recipe = new TreeRecipe { height = 5, trunkRadius = .5f, trunkBend = .5f, rootCount = 7, rootAttachmentHeight = .6f };
                var elbow = new[] { Vector3.zero, Vector3.down, new Vector3(.15f, -.9f, 0), Vector3.right };
                for (int ring = 0; ring < elbow.Length; ring++)
                {
                    float radius = Mathf.Min(1, TreePreview.RootSafeRadius(elbow, ring));
                    var axis = TreePreview.RootAxis(elbow, ring);
                    for (int n = Mathf.Max(0, ring - 1); n <= Mathf.Min(elbow.Length - 1, ring + 1); n++)
                    {
                        if (n == ring) continue;
                        var edge = elbow[n] - elbow[ring];
                        Require(radius * 1.4f * Vector3.Cross(axis, edge.normalized).magnitude <= edge.magnitude * .421f, "Elbow rings retain separation including bark relief");
                    }
                }
                recipe.Branches.limbs = 3; recipe.Branches.depth = 1;
                var profile = TreePreview.RootRadiusProfile(elbow, 1, 0);
                for (int i = 0; i < profile.Length; i++)
                {
                    Require(profile[i] > 0 && profile[i] <= TreePreview.RootSafeRadius(elbow, i), "Continuous profile respects curvature limits");
                    if (i > 0) Require(Mathf.Abs(profile[i] - profile[i - 1]) <= .601f * Vector3.Distance(elbow[i], elbow[i - 1]), "No isolated ring neck");
                }
                var straight = new[] { Vector3.zero, Vector3.down, Vector3.down * 2 };
                var straightProfile = TreePreview.RootRadiusProfile(straight, .5f, .8f);
                for (int i = 0; i < straight.Length; i++) Require(Mathf.Approximately(straightProfile[i], TreeBranchGrowth.RadiusAt(.5f, i / 2f, .8f)), "Unaffected straight-root taper");
                using (var preview = new TreePreview())
                {
                    preview.Build(recipe, 0);
                    var foliage = preview.Meshes[1].vertices;
                    int triangles = preview.WoodTriangles;
                    recipe.rootsEnabled = true;
                    var trunk = new TreeTrunkGrowth(recipe, Vector3.up * recipe.height);
                    var unforked = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    recipe.rootForks = 5;
                    var forked = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    Require(forked.Count == unforked.Count + 5, "Exact fork count");
                    for (int i = 0; i < unforked.Count; i++)
                        Require(unforked[i].points.SequenceEqual(forked[i].points), "Forks preserve main root paths");
                    foreach (var fork in forked.Skip(unforked.Count))
                    {
                        Require(fork.parentIndex >= 10 && fork.parentIndex < 10 + unforked.Count && !fork.terminal && fork.isRoot, "Root parent offset and foliage exclusion");
                        var parent = forked[fork.parentIndex - 10];
                        float sample = fork.attachment * TreeBranchGrowth.Segments;
                        int segment = (int)sample;
                        Require((fork.points[0] - Vector3.Lerp(parent.points[segment], parent.points[segment + 1], sample - segment)).sqrMagnitude < 1e-10f, "Attached fork socket");
                    }
                    recipe.rootMinForkDepth = recipe.rootMaxForkDepth = 0;
                    Require(TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10).Count == unforked.Count, "Zero depth disables forks");
                    recipe.rootMinForkDepth = recipe.rootMaxForkDepth = 3;
                    var deep = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    Require(deep.Count == unforked.Count + 5 * 7, "Three fork generations");
                    recipe.rootBend = .8f; recipe.rootSharpness = 0;
                    var bent = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    Require(!bent[0].points.SequenceEqual(deep[0].points), "Bend changes root shape");
                    Require(bent[0].points[0] == deep[0].points[0] && bent[0].points.Last() == deep[0].points.Last(), "Bend preserves root endpoints");
                    recipe.rootSharpness = 1;
                    var sharp = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    Require(!sharp[0].points.SequenceEqual(bent[0].points), "Sharpness changes root shape");
                    foreach (var fork in sharp.Skip(unforked.Count))
                    {
                        var parent = sharp[fork.parentIndex - 10];
                        float sample = fork.attachment * TreeBranchGrowth.Segments;
                        int segment = (int)sample;
                        Require((fork.points[0] - Vector3.Lerp(parent.points[segment], parent.points[segment + 1], sample - segment)).sqrMagnitude < 1e-10f, "Bent recursive fork socket");
                    }
                    recipe.rootMinForkDepth = 1; recipe.rootMaxForkDepth = 3;
                    var ranged = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    var repeated = TreeRootGrowth.Generate(recipe, trunk, 1, 0, 10);
                    Require(ranged.Count >= unforked.Count + 5 && ranged.Count <= deep.Count && ranged.SelectMany(x=>x.points).SequenceEqual(repeated.SelectMany(x=>x.points)), "Deterministic inclusive depth range");
                    preview.Build(recipe, 0);
                    Require(preview.WoodTriangles > triangles, "Roots add wood");
                    Require(foliage.SequenceEqual(preview.Meshes[1].vertices), "Roots leave foliage unchanged");
                    var wood = preview.Meshes[0].vertices;
                    preview.Build(recipe, 0);
                    Require(wood.SequenceEqual(preview.Meshes[0].vertices), "Deterministic roots");
                    recipe.floorHeight = .7f;
                    var restored = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(restored.rootsEnabled && restored.rootCount == 7 && restored.rootForks == 5 && restored.rootAttachmentHeight == .6f && restored.floorHeight == .7f, "JSON round-trip");
                    Require(restored.rootBend == .8f && restored.rootSharpness == 1 && restored.rootMinForkDepth == 1 && restored.rootMaxForkDepth == 3, "Root shape/range JSON round-trip");
                    preview.Build(recipe, 0);
                    Require(wood.Zip(preview.Meshes[0].vertices, (a,b) => (a - Vector3.up * .7f - b).sqrMagnitude < 1e-10f).All(x=>x), "Wood ground offset");
                    Require(foliage.Zip(preview.Meshes[1].vertices, (a,b) => (a - Vector3.up * .7f - b).sqrMagnitude < 1e-10f).All(x=>x), "Foliage ground offset");
                    for (int level = 1; level <= 2; level++)
                    {
                        var shifted = TreeLodGenerator.Build(recipe, 0, level, new TreeLodSettings());
                        recipe.floorHeight = 0;
                        preview.Build(recipe, 0);
                        var original = TreeLodGenerator.Build(recipe, 0, level, new TreeLodSettings());
                        Require(original.vertices.Zip(shifted.vertices, (a,b)=>(a - Vector3.up * .7f - b).sqrMagnitude < 1e-9f).All(x=>x), "LOD ground offset");
                        UnityEngine.Object.DestroyImmediate(shifted); UnityEngine.Object.DestroyImmediate(original);
                        recipe.floorHeight = .7f; preview.Build(recipe, 0);
                    }
                    recipe.floorHeight = 0; recipe.showFoliage = false; recipe.barkSurfaceDetail = .5f;
                    recipe.trunkBend = -1; recipe.lean = -.5f; recipe.trunkSharpness = 1; recipe.trunkBends = 9; recipe.trunkTwist = -720;
                    recipe.rootThickness = .9f;
                    recipe.simplifyWood = true;
                    preview.Build(recipe, 0);
                    Require(preview.Meshes[0].vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)), "Finite detailed roots");
                    var image = preview.ReferenceSnapshot(1024);
                    File.WriteAllBytes("Temp/tree-roots-preview.png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                }
                File.WriteAllText("Temp/tree-root-checks.txt", "PASS: independently varied root bend profiles and peak locations, fixed sockets/tips/depth, zero-bend compatibility, random bend corners retained in simplified meshes, 45/90/115/160-degree limb sockets, sharp bent/twisted trunk and detailed roots, recursive sockets, fork ranges, determinism, JSON, unchanged foliage, floor/LOD alignment.");
            }
            catch (Exception e) { File.WriteAllText("Temp/tree-root-checks.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        static void CheckIndependentBends()
        {
            var recipe = new TreeRecipe { rootsEnabled = true, rootCount = 12, rootForks = 0,
                height = 5, rootSpread = 2, rootAttachmentHeight = .6f, rootDepth = .5f };
            var trunk = new TreeTrunkGrowth(recipe, Vector3.up * recipe.height);
            var original = TreeRootGrowth.Generate(recipe, trunk, 1, 0);
            recipe.rootBend = .8f;
            var bent = TreeRootGrowth.Generate(recipe, trunk, 1, 0);
            var repeated = TreeRootGrowth.Generate(recipe, trunk, 1, 0);
            var signatures = new System.Collections.Generic.List<float[]>();
            var peaks = new System.Collections.Generic.HashSet<int>();
            for (int root = 0; root < bent.Count; root++)
            {
                var before = original[root].points;
                var after = bent[root].points;
                Require(after.SequenceEqual(repeated[root].points), "Repeatable independent root bends");
                Require(after[0] == before[0] && after.Last() == before.Last(), "Random bends preserve socket and tip");
                var side = Vector3.Cross(Vector3.up, before.Last() - before[0]).normalized;
                float length = Vector3.Distance(before[0], before.Last());
                var signature = new float[after.Length];
                int peak = 0;
                for (int point = 0; point < after.Length; point++)
                {
                    Require(after[point].y == before[point].y, "Random bending preserves root depth");
                    signature[point] = Vector3.Dot(after[point] - before[point], side) / length;
                    if (Mathf.Abs(signature[point]) > Mathf.Abs(signature[peak])) peak = point;
                }
                // Compare absolute profiles: a sign flip alone still counts as a mirrored bend.
                foreach (var previous in signatures)
                    Require(signature.Zip(previous, (a,b) => Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(b))).Sum() > .01f,
                        "Roots share a mirrored bend profile");
                signatures.Add(signature); peaks.Add(peak);
            }
            Require(peaks.Count >= 3, "Root bends concentrate at the same distance from the trunk");
            var meshRecipe = recipe.Copy();
            meshRecipe.rootSharpness = 1;
            meshRecipe.branchSegments = 2;
            meshRecipe.simplifyWood = meshRecipe.lodAggressiveWood = true;
            meshRecipe.simplificationTolerance = .15f;
            var sharp = TreeRootGrowth.Generate(meshRecipe, trunk, 1, 0);
            using (var preview = new TreePreview())
            {
                var emit = typeof(TreePreview).GetMethod("CurvedLimb", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (int root = 0; root < sharp.Count; root++)
                {
                    emit.Invoke(preview, new object[] { meshRecipe, sharp, root });
                    var mesh = preview.Meshes[root];
                    int stride = meshRecipe.branchSides + 1;
                    int rings = mesh.vertexCount / stride - 2;
                    var uv = mesh.uv;
                    // Check emitted rings, including aggressive simplification, at the seeded corners.
                    foreach (float bendPeak in new[] { sharp[root].rootBendPeaks.x, sharp[root].rootBendPeaks.y })
                    {
                        float ring = bendPeak * TreeBranchGrowth.Segments;
                        foreach (float t in new[] { Mathf.Floor(ring) / TreeBranchGrowth.Segments, Mathf.Ceil(ring) / TreeBranchGrowth.Segments })
                        {
                            float distance = TreePreview.ArcLength(sharp[root].points, t);
                            Require(Enumerable.Range(0, rings).Any(i => Mathf.Abs(uv[i * stride].y - distance) < .00001f),
                                "Simplified root mesh lost a random bend corner");
                        }
                    }
                }
            }
            var sibling = TreeRootGrowth.Generate(recipe, trunk, 1, 1);
            Require(!bent[0].points.SequenceEqual(sibling[0].points), "Sibling changes root variation");
            recipe.rootBend = 0;
            var disabled = TreeRootGrowth.Generate(recipe, trunk, 1, 0);
            for (int root = 0; root < original.Count; root++)
                Require(original[root].points.SequenceEqual(disabled[root].points), "Zero bend restores original root paths");
        }
    }
}
