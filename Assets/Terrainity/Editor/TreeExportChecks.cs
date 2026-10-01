using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeExportChecks
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        [MenuItem("Tools/Terrainity/Validate Tree Export")]
        static void Validate()
        {
            TreeExporter.Result first = null, second = null, changed = null;
            var existingShared = AssetDatabase.IsValidFolder(TreeSharedResources.Root)
                ? AssetDatabase.FindAssets("", new[] { TreeSharedResources.Root }) : Array.Empty<string>();
            TerrainData data = null;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var recipe = new TreeRecipe { assetName = "Export Validation", variantCount = 2, layers = 2 };
                first = TreeExporter.Generate(recipe, true, false);
                second = TreeExporter.Generate(recipe, false, false);
                Require(first.folder != second.folder && first.prefabs.Length == 2, "Unique family output failed");
                var firstMaterials = first.prefabs[0].GetComponent<MeshRenderer>().sharedMaterials;
                var secondMaterials = second.prefabs[0].GetComponent<MeshRenderer>().sharedMaterials;
                Require(firstMaterials.SequenceEqual(secondMaterials), "Identical exports did not share materials");
                Require(!AssetDatabase.IsValidFolder(first.folder + "/Materials") && !AssetDatabase.IsValidFolder(first.folder + "/Textures"), "Per-family resource copies remain");
                recipe.barkRoughness = .23f;
                changed = TreeExporter.Generate(recipe, false, false);
                var changedMaterials = changed.prefabs[0].GetComponent<MeshRenderer>().sharedMaterials;
                Require(changedMaterials[0] != firstMaterials[0] && changedMaterials[1] == firstMaterials[1], "Material settings not isolated");
                Require(changedMaterials[0].GetTexture("_BaseMap") == firstMaterials[0].GetTexture("_BaseMap"), "Roughness change duplicated bark texture");
                Require(AssetDatabase.GetAssetPath(firstMaterials[0]).StartsWith(TreeSharedResources.Root + "/Materials/"), "Material outside shared folder");
                foreach (var prefab in first.prefabs)
                {
                    var path = AssetDatabase.GetAssetPath(prefab);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var mesh = root.GetComponent<MeshFilter>().sharedMesh;
                        Require(mesh.subMeshCount == 2 && mesh.uv2.Length == mesh.vertexCount, "Mesh lost submeshes or gradient UVs");
                        Require(root.GetComponent<LODGroup>().GetLODs().Length == 1, "Missing Terrain LODGroup");
                        Require(root.GetComponent<CapsuleCollider>() != null, "Collider not saved");
                        foreach (var mat in root.GetComponent<MeshRenderer>().sharedMaterials)
                        {
                            Require(mat.shader.name == "Terrainity/Tree", "Wrong runtime shader");
                            Require(mat.SetPass(0), "Shader pass failed");
                            foreach (var property in new[] { "_BaseMap", "_TintRamp" })
                            {
                                var texture = mat.GetTexture(property);
                                Require(texture != null && EditorUtility.IsPersistent(texture) && texture.width > 0, "Missing saved texture");
                                var pixels = ((Texture2D)texture).GetPixels32();
                                Require(pixels.Any(p => p.r > 0 || p.g > 0 || p.b > 0), "Texture pixels were lost");
                                if (mat.name.Contains("Foliage") && property == "_BaseMap")
                                    Require(pixels.Any(p => p.a == 0) && pixels.Any(p => p.a > 128), "Foliage cutout lost");
                            }
                        }
                        Require(!AssetDatabase.GetDependencies(path, true).Any(p => p.Contains("/Editor/")), "Runtime prefab depends on Editor assets");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                Require(second.prefabs[0].GetComponent<Collider>() == null, "Unrequested collider");
                var preview = new PreviewRenderUtility();
                try
                {
                    var mesh = first.prefabs[0].GetComponent<MeshFilter>().sharedMesh;
                    var mats = first.prefabs[0].GetComponent<MeshRenderer>().sharedMaterials;
                    preview.BeginStaticPreview(new Rect(0, 0, 512, 512));
                    preview.camera.fieldOfView = 30;
                    var orbit = Quaternion.Euler(10, 35, 0);
                    preview.camera.transform.SetPositionAndRotation(mesh.bounds.center + orbit * new Vector3(0, 0, -mesh.bounds.size.magnitude * 1.8f), orbit);
                    preview.camera.farClipPlane = 200;
                    preview.lights[0].intensity = 1.3f;
                    preview.lights[0].transform.rotation = Quaternion.Euler(40, 40, 0);
                    preview.ambientColor = new Color(.35f, .35f, .35f);
                    for (int i = 0; i < mats.Length; i++) preview.DrawMesh(mesh, Matrix4x4.identity, mats[i], i);
                    preview.Render(true);
                    var picture = preview.EndStaticPreview();
                    File.WriteAllBytes("Temp/terrainity-export-preview.png", picture.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(picture);
                }
                finally { preview.Cleanup(); }
                var restored = TreeJsonStorage.Read<TreeRecipeJson>(first.folder + "/Recipes/Recipe.json").Restore(out var warning);
                Require(string.IsNullOrEmpty(warning) && restored.barkTexture != null && restored.foliageTexture != null, "Recipe lost exported textures");
                data = new TerrainData { heightmapResolution = 33, size = new Vector3(100, 10, 100) };
                var go = EditorUtility.CreateGameObjectWithHideFlags("Export test terrain", HideFlags.HideAndDontSave, typeof(Terrain));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
                var terrain = go.GetComponent<Terrain>(); terrain.terrainData = data;
                data.treePrototypes = new[] { new TreePrototype { prefab = second.prefabs[0] } };
                data.SetTreeInstances(new[] { new TreeInstance { prototypeIndex = 0, position = new Vector3(.5f, 0, .5f), widthScale = 1, heightScale = 1, color = Color.white, lightmapColor = Color.white } }, false);
                Require(TreeExporter.AddToTerrain(terrain, first.prefabs) == 2, "Prototype append failed");
                Require(data.treePrototypes.Length == 3 && data.treeInstanceCount == 1 && data.treeInstances[0].prototypeIndex == 0, "Existing Terrain trees changed");
                Require(TreeExporter.AddToTerrain(terrain, first.prefabs) == 0, "Duplicate prototypes added");
                TreeExporter.ExportPackage(first.folder, Path.GetFullPath("Temp/TerrainityExportCheck.unitypackage"));
                Require(File.Exists("Temp/TerrainityExportCheck.unitypackage"), "Package missing");
                var shader = Shader.Find("Terrainity/Tree");
                Require(!ShaderUtil.ShaderHasError(shader), string.Join("\n", ShaderUtil.GetShaderMessages(shader).Select(m => m.message)));
                AssetDatabase.DeleteAsset(first.folder);
                Require(second.prefabs[0].GetComponent<MeshRenderer>().sharedMaterials.All(m => m != null && m.GetTexture("_BaseMap") != null), "Deleting one family broke shared resources");
                File.WriteAllText("Temp/terrainity-export-check.txt", "PASS: cross-family material/texture reuse, different material settings isolated, family deletion preserves shared resources, persistent meshes, texture pixels, gradients, colliders, portable recipe, Terrain preservation, package and shader.");
                Debug.Log("[Terrainity] " + File.ReadAllText("Temp/terrainity-export-check.txt"));
            }
            catch (Exception e) { File.WriteAllText("Temp/terrainity-export-check.txt", e.ToString()); Debug.LogException(e); }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (data != null) { Undo.ClearUndo(data); UnityEngine.Object.DestroyImmediate(data); }
                if (first != null) AssetDatabase.DeleteAsset(first.folder);
                if (second != null) AssetDatabase.DeleteAsset(second.folder);
                if (changed != null) AssetDatabase.DeleteAsset(changed.folder);
                if (AssetDatabase.IsValidFolder(TreeSharedResources.Root))
                    foreach (string guid in AssetDatabase.FindAssets("", new[] { TreeSharedResources.Root }).Except(existingShared))
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.DeleteAsset(path);
                    }
            }
        }
    }
}
