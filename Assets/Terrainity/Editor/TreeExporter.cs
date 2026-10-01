using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Terrainity.Editor
{
    internal static class TreeExporter
    {
        internal const string OutputRoot = "Assets/TerrainityGenerated/Trees";
        internal sealed class Result
        {
            internal string folder;
            internal GameObject[] prefabs;
        }

        internal static Result Generate(TreeRecipe source, bool collider, bool showProgress = true, TreeLodSettings lodSettings = null)
        {
            lodSettings = lodSettings ?? new TreeLodSettings { enabled = false };
            lodSettings.Validate();
            var lodReport = new System.Text.StringBuilder("Tree,LOD,Triangles\n");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Generate trees outside Play mode.");
            var shader = Shader.Find("Terrainity/Tree");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Terrainity/Tree shader is missing or has compilation errors.");
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && !pipeline.GetType().Name.Contains("Universal"))
                throw new InvalidOperationException("Tree export supports URP and the Built-in Render Pipeline.");

            // Validate and detach the export from the editable builder state.
            var recipe = TreeRecipeJson.From(source, 0).Restore(out _);
            recipe.showFoliage = true; // Show foliage is a structural inspection toggle in the preview.
            EnsureFolder(OutputRoot);
            string stem = SafeName(recipe.assetName);
            string folder = AssetDatabase.GenerateUniqueAssetPath(OutputRoot + "/" + stem);
            string ownedGuid = AssetDatabase.CreateFolder(OutputRoot, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(ownedGuid)) throw new IOException("Cannot create export folder.");
            TreeSharedResources shared = null;
            try
            {
                shared = new TreeSharedResources();
                foreach (string child in new[] { "Prefabs", "Models", "Recipes" })
                    AssetDatabase.CreateFolder(folder, child);
                var prefabs = new List<GameObject>();
                var savedMaterials = new Dictionary<int, Material>();
                var savedTextures = new Dictionary<Texture, Texture>();
                using (var builder = new TreePreview())
                {
                    for (int sibling = 0; sibling < recipe.variantCount; sibling++)
                    {
                        if (showProgress && EditorUtility.DisplayCancelableProgressBar("Generate tree family",
                            $"{recipe.assetName} — sibling {sibling + 1} of {recipe.variantCount}", sibling / (float)recipe.variantCount))
                            throw new OperationCanceledException("Tree export cancelled.");
                        builder.Build(recipe, sibling);
                        if (builder.Meshes.Count == 0) throw new InvalidOperationException("The tree generated no mesh.");
                        var parts = new CombineInstance[builder.Meshes.Count];
                        var materials = new Material[parts.Length];
                        for (int i = 0; i < parts.Length; i++)
                        {
                            parts[i] = new CombineInstance { mesh = builder.Meshes[i], transform = Matrix4x4.identity };
                            if (!savedMaterials.TryGetValue(i, out var material))
                            {
                                string label = i == 0 ? "Bark" : "Foliage";
                                material = new Material(shader) { name = stem + " " + label };
                                material.CopyPropertiesFromMaterial(builder.MaterialForMesh(i));
                                material.hideFlags = HideFlags.None;
                                material.enableInstancing = true;
                                material.SetColor("_TreeInstanceColor", Color.white);
                                foreach (string property in new[] { "_BaseMap", "_TintRamp" })
                                {
                                    var original = material.GetTexture(property);
                                    if (original == null) continue;
                                    if (!savedTextures.TryGetValue(original, out var texture))
                                    {
                                        texture = CopyTexture(original);
                                        texture = shared.Texture((Texture2D)texture, label + (property == "_BaseMap" ? " Texture" : " Tint"));
                                        savedTextures.Add(original, texture);
                                    }
                                    material.SetTexture(property, texture);
                                }
                                material = shared.Material(material, label);
                                savedMaterials.Add(i, material);
                            }
                            materials[i] = material;
                        }
                        string name = stem + "_" + (sibling + 1).ToString("00");
                        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                        mesh.CombineMeshes(parts, false, false);
                        mesh.RecalculateBounds();
                        AssetDatabase.CreateAsset(mesh, folder + "/Models/" + name + ".asset");
                        var lodMeshes = new List<Mesh> { mesh };
                        if (lodSettings.enabled)
                            for (int level = 1; level <= 2; level++)
                            {
                                var reduced = TreeLodGenerator.Build(recipe, sibling, level,
                                    builder.Meshes.Count > 1 ? builder.Meshes[1] : null, lodSettings);
                                reduced.name = name + "_LOD" + level;
                                AssetDatabase.CreateAsset(reduced, folder + "/Models/" + reduced.name + ".asset");
                                lodMeshes.Add(reduced);
                            }
                        for (int level = 0; level < lodMeshes.Count; level++)
                            lodReport.AppendLine($"{name},{level},{lodMeshes[level].triangles.Length / 3}");

                        // Preview scene keeps temporary export objects out of the user's scene and Undo history.
                        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
                        GameObject root = null;
                        try
                        {
                            root = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
                            SceneManager.MoveGameObjectToScene(root, scene);
                            root.hideFlags = HideFlags.None;
                            root.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var renderer = root.AddComponent<MeshRenderer>();
                            renderer.sharedMaterials = materials;
                            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                            renderer.receiveShadows = true;
                            var lod = root.AddComponent<LODGroup>();
                            var levels = new List<LOD>();
                            levels.Add(new LOD(lodSettings.enabled ? lodSettings.lod1Start : .005f, new Renderer[] { renderer }) { fadeTransitionWidth = .15f });
                            for (int level = 1; level < lodMeshes.Count; level++)
                            {
                                var child = new GameObject("LOD" + level);
                                SceneManager.MoveGameObjectToScene(child, scene);
                                child.transform.SetParent(root.transform, false);
                                child.AddComponent<MeshFilter>().sharedMesh = lodMeshes[level];
                                var reducedRenderer = child.AddComponent<MeshRenderer>();
                                reducedRenderer.sharedMaterials = materials;
                                reducedRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                                reducedRenderer.receiveShadows = true;
                                levels.Add(new LOD(level == 1 ? lodSettings.lod2Start : lodSettings.cull, new Renderer[] { reducedRenderer }) { fadeTransitionWidth = .15f });
                            }
                            lod.fadeMode = lodSettings.enabled && lodSettings.crossFade ? LODFadeMode.CrossFade : LODFadeMode.None;
                            lod.animateCrossFading = false;
                            lod.SetLODs(levels.ToArray());
                            lod.RecalculateBounds();
                            if (collider)
                            {
                                var capsule = root.AddComponent<CapsuleCollider>();
                                capsule.radius = recipe.trunkRadius;
                                capsule.height = Mathf.Max(recipe.height * recipe.crownStart, capsule.radius * 2);
                                capsule.center = new Vector3(0, capsule.height * .5f - Mathf.Clamp(recipe.floorHeight, -2, 2), 0);
                            }
                            var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Prefabs/" + name + ".prefab", out bool success);
                            if (!success || prefab == null) throw new IOException("Could not save " + name + ".");
                            AssetDatabase.SetLabels(prefab, new[] { "TerrainityGenerated", "TerrainityTree" });
                            prefabs.Add(prefab);
                        }
                        finally
                        {
                            if (root != null) UnityEngine.Object.DestroyImmediate(root);
                            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                        }
                    }
                }
                var document = TreeRecipeJson.From(recipe, 0);
                // Recipe references exported textures, so it also works when the package is moved.
                SetRecipeTexture(document, savedMaterials[0].GetTexture("_BaseMap"), false);
                if (savedMaterials.TryGetValue(1, out var leaves)) SetRecipeTexture(document, leaves.GetTexture("_BaseMap"), true);
                TreeJsonStorage.Write(folder + "/Recipes/Recipe.json", document);
                TreeJsonStorage.Write(folder + "/Recipes/LODSettings.json", lodSettings);
                File.WriteAllText(folder + "/LOD-Report.csv", lodReport.ToString());
                File.WriteAllText(folder + "/README.txt",
                    "Terrainity tree family\n\nSelect Terrain > Paint Trees > Edit Trees > Add Tree and choose a prefab from Prefabs.\n" +
                    "Alternatively use Add family to Terrain in the builder. It adds brush prototypes; it does not plant trees.\n\n" +
                    "Meshes and the recipe are local to this family. Materials and textures are reused from Assets/TerrainityGenerated/Shared when their contents match.\n" +
                    "Editing a shared resource affects every tree using it. Use Export Unity package to include all referenced shared resources and the Terrainity/Tree shader.\n" +
                    (lodSettings.enabled ? "Each prefab has three mesh LODs assigned automatically. See LOD-Report.csv for actual triangle counts.\n" : "Each prefab uses one mesh LOD.\n") +
                    "No distant whole-tree billboard or Wind Zone animation is generated. Cross-fading in URP requires LOD Cross Fade enabled in the pipeline asset.\n" +
                    "Foliage consists of fixed cards or curved palm fronds. Reduce builder Mesh detail and branch density for large forests.\n" +
                    (collider ? "The optional capsule approximates the lower trunk; review it for strongly bent trunks.\n" : "No physics collider was requested.\n"));
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return new Result { folder = folder, prefabs = prefabs.ToArray() };
            }
            catch
            {
                // Roll back only the new folder whose GUID this invocation created.
                if (AssetDatabase.AssetPathToGUID(folder) == ownedGuid) AssetDatabase.DeleteAsset(folder);
                shared?.Rollback();
                throw;
            }
            finally { if (showProgress) EditorUtility.ClearProgressBar(); }
        }

        static void SetRecipeTexture(TreeRecipeJson doc, Texture texture, bool foliage)
        {
            string path = AssetDatabase.GetAssetPath(texture), guid = AssetDatabase.AssetPathToGUID(path);
            if (foliage) { doc.foliageTexturePath = path; doc.foliageTextureGuid = guid; }
            else { doc.barkTexturePath = path; doc.barkTextureGuid = guid; }
        }

        // Preview textures and imported foliage can be non-readable. Read back a GPU copy
        // without changing source import settings or trying to clone discarded CPU pixels.
        static Texture2D CopyTexture(Texture source)
        {
            bool srgb = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat);
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            bool previousWrite = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = srgb && QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, source.mipmapCount > 1, !srgb);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply(true, false);
                copy.wrapModeU = source.wrapModeU; copy.wrapModeV = source.wrapModeV;
                copy.wrapModeW = source.wrapModeW; copy.mipMapBias = source.mipMapBias;
                copy.filterMode = source.filterMode; copy.anisoLevel = source.anisoLevel;
                return copy;
            }
            finally { GL.sRGBWrite = previousWrite; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }

        internal static int AddToTerrain(Terrain terrain, IEnumerable<GameObject> prefabs)
        {
            if (terrain == null || terrain.terrainData == null) throw new InvalidOperationException("Choose a Terrain with TerrainData first.");
            var incoming = prefabs?.Where(p => p != null).Distinct().ToArray() ?? Array.Empty<GameObject>();
            if (incoming.Length == 0) throw new InvalidOperationException("Generate a family first.");
            foreach (var prefab in incoming)
                if (!EditorUtility.IsPersistent(prefab) || prefab.GetComponent<LODGroup>() == null || prefab.GetComponent<MeshRenderer>() == null)
                    throw new InvalidOperationException("Expected a generated tree prefab: " + prefab.name);
            var data = terrain.terrainData;
            var prototypes = new List<TreePrototype>(data.treePrototypes);
            int before = prototypes.Count;
            foreach (var prefab in incoming)
                if (!prototypes.Any(p => p.prefab == prefab)) prototypes.Add(new TreePrototype { prefab = prefab, bendFactor = 0 });
            if (prototypes.Count == before) return 0;
            Undo.RegisterCompleteObjectUndo(data, "Add Terrainity tree family");
            data.treePrototypes = prototypes.ToArray();
            data.RefreshPrototypes();
            EditorUtility.SetDirty(data);
            terrain.Flush();
            return prototypes.Count - before;
        }

        internal static void ExportPackage(string folder, string destination)
        {
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith(OutputRoot + "/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(folder))
                throw new InvalidOperationException("Generate a family first.");
            // Include runtime assets explicitly; never embed read-only UPM shader includes.
            var paths = AssetDatabase.FindAssets("", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            paths = AssetDatabase.GetDependencies(paths, true).Concat(paths)
                .Concat(new[] { "Assets/Terrainity/Shaders/TreeSurface.hlsl", "Assets/Terrainity/Shaders/TreeTransmission.hlsl" })
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)).Distinct().ToArray();
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Export(new UnityEditor.AssetPackage.ExportPackageParameters(paths, destination));
#else
            AssetDatabase.ExportPackage(paths, destination, ExportPackageOptions.Default);
#endif
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Cannot create " + path);
        }

        internal static string SafeName(string name)
        {
            string safe = new string((name ?? "Tree").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').Take(64).ToArray()).Trim('_');
            if (string.IsNullOrEmpty(safe)) safe = "Tree";
            if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(safe.ToUpperInvariant())) safe = "Tree_" + safe;
            return safe;
        }
    }
}
