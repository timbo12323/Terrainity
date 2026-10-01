using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Terrainity.Editor
{
    internal static class RockExporter
    {
        internal static TreeExporter.Result Generate(RockRecipe source, bool showProgress = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Generate rocks outside Play mode.");
            var recipe = source.Copy(); recipe.Validate();
            const string output = "Assets/TerrainityGenerated/Rocks";
            TreeExporter.EnsureFolder(output);
            string stem = TreeExporter.SafeName(recipe.assetName);
            string folder = AssetDatabase.GenerateUniqueAssetPath(output + "/" + stem);
            string ownedGuid = AssetDatabase.CreateFolder(output, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(ownedGuid)) throw new IOException("Could not create the rock export folder.");
            TreeSharedResources shared = null;
            try
            {
                shared = new TreeSharedResources();
                foreach (string child in new[] { "Prefabs", "Models", "Recipes" }) AssetDatabase.CreateFolder(folder, child);
                var material = RockGenerator.Material(recipe); material.hideFlags = HideFlags.None;
                material = shared.Material(material, "Rock");
                var prefabs = new List<GameObject>();
                var report = new StringBuilder("Rock,LOD,Triangles\n");
                for (int sibling = 0; sibling < recipe.variantCount; sibling++)
                {
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Generate rock family", $"Sibling {sibling + 1} of {recipe.variantCount}", sibling / (float)recipe.variantCount)) throw new OperationCanceledException();
                    string name = stem + "_" + (sibling + 1).ToString("00");
                    int count = recipe.generateLods ? Mathf.Min(3, recipe.subdivisions + 1) : 1;
                    var meshes = new List<Mesh>();
                    for (int level = 0; level < count; level++)
                    {
                        var mesh = RockGenerator.Build(recipe, sibling, level); mesh.name = name + "_LOD" + level;
                        try { AssetDatabase.CreateAsset(mesh, folder + "/Models/" + mesh.name + ".asset"); }
                        catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
                        meshes.Add(mesh); report.AppendLine($"{name},{level},{mesh.triangles.Length / 3}");
                    }
                    var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null;
                    try
                    {
                        root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
                        SceneManager.MoveGameObjectToScene(root, scene); root.hideFlags = HideFlags.None;
                        var levels = new List<LOD>();
                        for (int level = 0; level < count; level++)
                        {
                            GameObject part = root;
                            if (level > 0) { part = new GameObject("LOD" + level); SceneManager.MoveGameObjectToScene(part, scene); part.transform.SetParent(root.transform, false); }
                            part.AddComponent<MeshFilter>().sharedMesh = meshes[level];
                            var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                            float threshold = level == count - 1 ? recipe.cull : level == 0 ? recipe.lod1Start : recipe.lod2Start;
                            levels.Add(new LOD(threshold, new Renderer[] { renderer }) { fadeTransitionWidth = .15f });
                        }
                        if (recipe.generateLods)
                        {
                            var group = root.AddComponent<LODGroup>();
                            group.SetLODs(levels.ToArray());
                            group.fadeMode = recipe.crossFade ? LODFadeMode.CrossFade : LODFadeMode.None;
                            group.RecalculateBounds();
                        }
                        if (recipe.collider) root.AddComponent<MeshCollider>().sharedMesh = meshes[0];
                        var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/" + name + ".prefab", out bool success);
                        if (!success || prefab == null) throw new IOException("Could not save rock prefab " + name);
                        AssetDatabase.SetLabels(prefab, new[] { "TerrainityGenerated", "TerrainityRock" }); prefabs.Add(prefab);
                    }
                    finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
                }
                File.WriteAllText(folder + "/Recipes/Recipe.json", JsonUtility.ToJson(recipe, true));
                File.WriteAllText(folder + "/LOD-Report.csv", report.ToString());
                File.WriteAllText(folder + "/README.txt", "Terrainity rock family\n\nDrag a prefab into your scene. Its pivot is at the base. Optional non-convex mesh colliders are for static scenery.\nLOD meshes sample the same seeded surface at progressively lower resolutions. Review transitions for highly detailed shapes. Dithered transitions require LOD Cross Fade in the URP pipeline asset.\nMaterials are shared under TerrainityGenerated/Shared. Assigned textures are referenced without duplication; Unity package export includes these dependencies.\nRecipe texture GUIDs resolve in projects containing the same texture assets.\n");
                AssetDatabase.Refresh(); AssetDatabase.SaveAssets();
                return new TreeExporter.Result { folder = folder, prefabs = prefabs.ToArray() };
            }
            catch { if (AssetDatabase.AssetPathToGUID(folder) == ownedGuid) AssetDatabase.DeleteAsset(folder); shared?.Rollback(); throw; }
            finally { if (showProgress) EditorUtility.ClearProgressBar(); }
        }
    }
}
