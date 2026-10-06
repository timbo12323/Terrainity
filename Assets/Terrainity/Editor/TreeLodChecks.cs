using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeLodChecks
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        [MenuItem("Tools/Terrainity/Validate Tree LODs")]
        static void Validate()
        {
            var report = new StringBuilder();
            var settings = new TreeLodSettings();
            var oldShared = AssetDatabase.IsValidFolder(TreeSharedResources.Root) ? AssetDatabase.FindAssets("", new[] { TreeSharedResources.Root }) : Array.Empty<string>();
            try
            {
                foreach (string family in new[] { "Birch", "Spruce", "Willow", "Palm" })
                {
                    var recipe = TreeJsonStorage.ReadFamily("Assets/Terrainity/Families/" + family + ".json").defaults.Restore(out _);
                    recipe.variantCount = 2;
                    recipe.assetName = "LOD Validation " + family;
                    var branches = TreeBranchGrowth.Generate(recipe,
                        new Vector3(recipe.lean * recipe.height * .18f, recipe.height, 0), 1, 0);
                    var nearScales = TreeLodGenerator.BranchScaleFactors(branches, recipe, 1);
                    var farScales = TreeLodGenerator.BranchScaleFactors(branches, recipe, 2);
                    Require(nearScales.SequenceEqual(TreeLodGenerator.BranchScaleFactors(branches, recipe, 1)),
                        family + " branch pruning is not deterministic");
                    for (int i = 0; i < branches.Count; i++)
                    {
                        Require(farScales[i] <= nearScales[i], family + " far LOD restored a pruned branch");
                        if (branches[i].parentIndex < 0) Require(nearScales[i] == 1 && farScales[i] == 1,
                            family + " pruned a main limb");
                    }
                    if (branches.Count(l => l.terminal && l.parentIndex >= 0) >= 4)
                        Require(nearScales.Any(scale => scale < 1) && farScales.Any(scale => scale == 0),
                            family + " did not shrink and cull interior branches");
                    using (var fullCanopy = new TreePreview())
                    using (var prunedCanopy = new TreePreview())
                    {
                        fullCanopy.Build(recipe, 0);
                        prunedCanopy.Build(recipe, 0, 2);
                        if (fullCanopy.Meshes.Count > 1)
                        {
                            Require(prunedCanopy.Meshes.Count > 1
                                && prunedCanopy.Meshes[1].vertexCount == fullCanopy.Meshes[1].vertexCount,
                                family + " pruning removed foliage clusters");
                            var fullFoliageBounds = fullCanopy.Meshes[1].bounds;
                            var prunedFoliageBounds = prunedCanopy.Meshes[1].bounds;
                            Require(Vector3.Distance(fullFoliageBounds.center, prunedFoliageBounds.center) < .001f
                                && Vector3.Distance(fullFoliageBounds.size, prunedFoliageBounds.size) < .001f,
                                family + " pruning contracted the foliage silhouette");
                        }
                    }
                    TreeExporter.Result export = null;
                    try
                    {
                        export = TreeExporter.Generate(recipe, false, false, settings);
                        var prefab = export.prefabs[0]; var group = prefab.GetComponent<LODGroup>(); var lods = group.GetLODs();
                        Require(lods.Length == 3 && group.fadeMode == LODFadeMode.CrossFade, "LODGroup setup failed");
                        int previous = int.MaxValue;
                        int previousWood = int.MaxValue;
                        var full = lods[0].renderers[0].GetComponent<MeshFilter>().sharedMesh;
                        for (int level = 0; level < 3; level++)
                        {
                            var renderer = lods[level].renderers[0];
                            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                            int triangles = mesh.triangles.Length / 3;
                            Require(triangles < previous, family + " LOD failed to reduce triangles"); previous = triangles;
                            int woodTriangles = (int)mesh.GetIndexCount(0) / 3;
                            Require(woodTriangles < previousWood, family + " LOD failed to reduce wood triangles");
                            previousWood = woodTriangles;
                            Require(mesh.subMeshCount == 2 && mesh.uv2.Length == mesh.vertexCount, "Missing material or tint coordinates");
                            var wind = new System.Collections.Generic.List<Vector4>();
                            mesh.GetUVs(3, wind);
                            Require(wind.Count == mesh.vertexCount && wind.Any(v => v.w > 0), "Missing LOD wind coordinates");
                            Require(renderer.sharedMaterials.SequenceEqual(lods[0].renderers[0].sharedMaterials), "LOD duplicated materials");
                            Require(mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount), "Invalid mesh index");
                            Require(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), "Non-finite vertices");
                            Require(Vector3.Distance(mesh.bounds.center, full.bounds.center) < full.bounds.size.magnitude * .15f, "Tree center drift");
                            if (level > 0)
                                using (var lodPreview = new TreePreview())
                                {
                                    lodPreview.AllowFarZoom(true);
                                    lodPreview.SetView(new Vector3(35, 10, 32), Vector3.zero);
                                    lodPreview.BuildLod(recipe, 0, level, settings);
                                    var previewMesh = lodPreview.Meshes[0];
                                    Require(lodPreview.ViewOrbit.z == 32 && previewMesh.vertexCount == mesh.vertexCount
                                        && previewMesh.subMeshCount == mesh.subMeshCount
                                        && Enumerable.Range(0, mesh.subMeshCount).All(i => previewMesh.GetIndexCount(i) == mesh.GetIndexCount(i)),
                                        family + " LOD preview differs from export");
                                }
                            report.AppendLine($"{family} LOD{level}: {triangles:N0} triangles ({100f * triangles / (full.triangles.Length / 3):F1}%), wood {woodTriangles:N0}");
                            Snapshot(mesh, renderer.sharedMaterials, full.bounds, family + "-LOD" + level, 35);
                            Snapshot(mesh, renderer.sharedMaterials, full.bounds, family + "-side-LOD" + level, 125);
                            foreach (var material in renderer.sharedMaterials)
                            {
                                var test = new Material(material);
                                test.EnableKeyword("LOD_FADE_CROSSFADE");
                                for (int pass = 0; pass < test.passCount; pass++) Require(test.SetPass(pass), "LOD shader pass failed");
                                UnityEngine.Object.DestroyImmediate(test);
                            }
                        }
                        var saved = JsonUtility.FromJson<TreeLodSettings>(File.ReadAllText(export.folder + "/Recipes/LODSettings.json"));
                        Require(saved.enabled && saved.lod2Start == settings.lod2Start, "LOD settings not persisted");
                        if (family == "Birch") TreeExporter.ExportPackage(export.folder, Path.GetFullPath("Temp/TerrainityLODCheck.unitypackage"));
                    }
                    finally { if (export != null) AssetDatabase.DeleteAsset(export.folder); }
                }
                Require(!ShaderUtil.ShaderHasError(Shader.Find("Terrainity/Tree")), "LOD shader compilation failed");
                report.AppendLine("PASS: deterministic nested branch pruning, preserved foliage sockets, LOD assignment, preview/export mesh parity, far zoom, monotonic reduction, valid geometry, shared materials, stable bounds, serialized settings, shader passes and package export.");
            }
            catch (Exception e) { report.AppendLine(e.ToString()); Debug.LogException(e); }
            finally
            {
                foreach (string guid in AssetDatabase.FindAssets("", new[] { TreeSharedResources.Root }).Except(oldShared))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
                }
                File.WriteAllText("Temp/terrainity-lod-check.txt", report.ToString());
                Debug.Log("[Terrainity LOD] " + report);
            }
        }
        static void Snapshot(Mesh mesh, Material[] materials, Bounds bounds, string label, float yaw)
        {
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.fieldOfView = 35;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.13f, .16f, .18f);
                preview.BeginStaticPreview(new Rect(0, 0, 384, 384));
                var rotation = Quaternion.Euler(10, yaw, 0);
                preview.camera.transform.SetPositionAndRotation(bounds.center + rotation * new Vector3(0, 0, -bounds.extents.magnitude / Mathf.Sin(17.5f * Mathf.Deg2Rad) * 1.15f), rotation);
                preview.camera.farClipPlane = 500;
                preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
                preview.ambientColor = new Color(.35f, .35f, .35f);
                for (int i = 0; i < materials.Length; i++) preview.DrawMesh(mesh, Matrix4x4.identity, materials[i], i);
                preview.Render(true); var image = preview.EndStaticPreview();
                File.WriteAllBytes("Temp/" + label + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            }
            finally { preview.Cleanup(); }
        }
    }
}

