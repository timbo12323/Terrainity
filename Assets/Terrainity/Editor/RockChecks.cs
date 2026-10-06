using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Terrainity.Editor
{
    internal static class RockChecks
    {
        [MenuItem("Tools/Terrainity/Validate Rocks")]
        static void Run()
        {
            const string folder = "TerrainityReports/Rocks";
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            var exports = new List<string>();
            var sharedBefore = new HashSet<string>(AssetDatabase.IsValidFolder(TreeSharedResources.Root) ? AssetDatabase.FindAssets("", new[] { TreeSharedResources.Root }) : Array.Empty<string>());
            try
            {
                int roots = SceneManager.GetActiveScene().rootCount;
                Require(new RockRecipe().uvMode == RockUvMode.Box, "New rocks use seamless box projection");
                foreach (string preset in RockRecipe.Presets)
                {
                    var recipe = new RockRecipe(); recipe.ApplyPreset(preset);
                    using (var preview = new TreePreview())
                    {
                        preview.BuildRock(recipe, 0);
                        var picture = preview.ReferenceSnapshot(384);
                        File.WriteAllBytes(folder + "/" + preset.Replace(' ', '-') + ".png", picture.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(picture);
                    }
                    var full = RockGenerator.Build(recipe, 0);
                    try
                    {
                        Require(Mathf.Abs(full.bounds.min.y) < .0001f, "Base pivot");
                        int previousCount = int.MaxValue;
                        for (int level = 0; level <= Math.Min(2, recipe.subdivisions); level++)
                        {
                            var mesh = RockGenerator.Build(recipe, 0, level);
                            try
                            {
                                var v = mesh.vertices; var t = mesh.triangles;
                                Require(t.Length < previousCount, "LOD must reduce triangles"); previousCount = t.Length;
                                if (!recipe.flatShading) Require(mesh.vertexCount < t.Length, "Smooth mesh should share vertices");
                                var coordinates = mesh.uv;
                                Require(coordinates.Length == v.Length, "Every rock vertex has UV0");
                                foreach (var coordinate in coordinates) Require(float.IsFinite(coordinate.x) && float.IsFinite(coordinate.y), "Invalid UV");
                                Require((v[0] - full.vertices[0]).sqrMagnitude < .000001f, "LOD pivots must align");
                                for (int i = 0; i < t.Length; i += 3)
                                    Require(Vector3.Cross(v[t[i+1]] - v[t[i]], v[t[i+2]] - v[t[i]]).sqrMagnitude > 1e-15f, "Degenerate face");
                                foreach (var n in mesh.normals) Require(float.IsFinite(n.x) && n.sqrMagnitude > .9f, "Invalid normal");
                                report.AppendLine($"{preset} LOD{level}: {t.Length / 3} triangles");
                            }
                            finally { UnityEngine.Object.DestroyImmediate(mesh); }
                        }
                        var repeat = RockGenerator.Build(recipe, 0); var sibling = RockGenerator.Build(recipe, 1);
                        try { Require(full.vertices.SequenceEqual(repeat.vertices), "Seed reproducibility"); Require(!full.vertices.SequenceEqual(sibling.vertices), "Sibling variation"); }
                        finally { UnityEngine.Object.DestroyImmediate(repeat); UnityEngine.Object.DestroyImmediate(sibling); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(full); }
                    string json = folder + "/Recipe.json"; File.WriteAllText(json, JsonUtility.ToJson(recipe));
                    Require(JsonUtility.ToJson(recipe) == JsonUtility.ToJson(RockRecipe.Read(json)), "JSON round-trip");
                }
                File.WriteAllText(folder + "/LegacyRecipe.json", "{\"kind\":\"TerrainityRock\",\"version\":1,\"assetName\":\"Legacy rock\"}");
                var legacy = RockRecipe.Read(folder + "/LegacyRecipe.json");
                Require(legacy.variantCount == 1 && legacy.crossFade && legacy.lod1Start > legacy.lod2Start && legacy.lod2Start > legacy.cull, "Legacy recipe defaults");
                Require(legacy.uvMode == RockUvMode.Spherical && legacy.textureScaleV == legacy.textureScale, "Legacy UV mapping");
                var tiled = new RockRecipe { textureScale = 2, textureScaleV = 3 };
                var tiledMaterial = RockGenerator.Material(tiled);
                try
                {
                    Require(tiledMaterial.shader.name == "Terrainity/Rock Triplanar" && tiledMaterial.shader.isSupported, "Seamless box shader");
                    Require(tiledMaterial.GetTextureScale("_BaseMap") == new Vector2(2, 3), "Independent UV tiling");
                    Require(Mathf.Approximately(tiledMaterial.GetFloat("_BlendSharpness"), tiled.projectionBlend), "Projection blend setting");
                }
                finally { UnityEngine.Object.DestroyImmediate(tiledMaterial); }
                var spherical = tiled.Copy(); spherical.uvMode = RockUvMode.Spherical;
                var sphericalMaterial = RockGenerator.Material(spherical);
                try { Require(sphericalMaterial.shader.name != "Terrainity/Rock Triplanar", "Spherical UV material"); }
                finally { UnityEngine.Object.DestroyImmediate(sphericalMaterial); }
                var boxMesh = RockGenerator.Build(tiled, 0);
                var sphericalMesh = RockGenerator.Build(spherical, 0);
                try { Require(boxMesh.uv.SequenceEqual(sphericalMesh.uv), "Projection mode must not change mesh UV0"); }
                finally { UnityEngine.Object.DestroyImmediate(boxMesh); UnityEngine.Object.DestroyImmediate(sphericalMesh); }
                var exportRecipe = new RockRecipe { assetName = "__RockValidation", variantCount = 2 };
                var first = RockExporter.Generate(exportRecipe, false); exports.Add(first.folder);
                var second = RockExporter.Generate(exportRecipe, false); exports.Add(second.folder);
                Require(first.prefabs.Length == 2, "Family export count");
                Require(AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(first.prefabs[0]), true)
                    .Contains("Assets/Terrainity/Shaders/RockTriplanar.shader"), "Exported prefab must depend on the runtime box shader");
                Material shared = first.prefabs[0].GetComponent<MeshRenderer>().sharedMaterial;
                TreeExporter.ExportPackage(first.folder, Path.GetFullPath("Temp/TerrainityRockExportCheck.unitypackage"));
                Require(File.Exists("Temp/TerrainityRockExportCheck.unitypackage"), "Rock package export");
                foreach (var prefab in first.prefabs.Concat(second.prefabs))
                {
                    var lodGroup = prefab.GetComponent<LODGroup>();
                    Require(lodGroup.GetLODs().Length == 3, "Assigned LODs");
                    Require(lodGroup.fadeMode == LODFadeMode.CrossFade, "Assigned LOD fade");
                    Require(Mathf.Abs(lodGroup.GetLODs()[0].screenRelativeTransitionHeight - exportRecipe.lod1Start) < .0001f, "LOD1 threshold");
                    Require(prefab.GetComponent<MeshCollider>().sharedMesh != null, "Assigned collider");
                    Require(prefab.GetComponent<MeshRenderer>().sharedMaterial == shared, "Shared materials");
                    Require(AssetDatabase.GetLabels(prefab).Contains("TerrainityRock"), "Library labels");
                }
                exportRecipe.generateLods = false; exportRecipe.collider = false; exportRecipe.variantCount = 1;
                var simple = RockExporter.Generate(exportRecipe, false); exports.Add(simple.folder);
                Require(simple.prefabs[0].GetComponent<LODGroup>() == null && simple.prefabs[0].GetComponent<Collider>() == null, "Optional export controls");
                Require(SceneManager.GetActiveScene().rootCount == roots, "Export changed active scene");
                report.AppendLine("PASS: six presets; seamless box material; spherical UV0; projection blend; independent texture tiling; legacy spherical UV recipes; shared smooth vertices; deterministic seeds; sibling variation; LOD reductions and pivot alignment; prefab LOD thresholds and fade; shared materials; optional LODs/colliders; library labels; scene isolation.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); Debug.LogException(e); }
            finally
            {
                foreach (string path in exports) AssetDatabase.DeleteAsset(path);
                if (AssetDatabase.IsValidFolder(TreeSharedResources.Root))
                    foreach (string guid in AssetDatabase.FindAssets("", new[] { TreeSharedResources.Root }))
                        if (!sharedBefore.Contains(guid)) { string path = AssetDatabase.GUIDToAssetPath(guid); if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path); }
                AssetDatabase.SaveAssets(); File.WriteAllText(folder + "/validation.txt", report.ToString());
            }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
