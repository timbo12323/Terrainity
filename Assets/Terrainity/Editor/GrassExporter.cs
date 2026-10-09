using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Terrainity.Editor
{
    internal static class GrassExporter
    {
        internal static GameObject Generate(GrassRecipe source,out string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Generate grass outside Play mode.");
            var r = source.Copy(); r.Validate();
            var shader = Shader.Find("Terrainity/Grass");
            if (shader == null) throw new InvalidOperationException("Terrainity runtime shader is missing.");
            string root = "Assets/TerrainityGenerated/Grass";
            TreeExporter.EnsureFolder(root);
            string stem = TreeExporter.SafeName(r.assetName);
            folder = AssetDatabase.GenerateUniqueAssetPath(root+"/"+stem);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(root,Path.GetFileName(folder)))) throw new IOException("Cannot create grass output folder.");
            GameObject temporary = null;
            var previewScene = EditorSceneManager.NewPreviewScene();
            Mesh mesh = null; Texture2D ramp = null; Material material = null;
            try
            {
                foreach (string child in new[] { "Models","Materials","Prefabs","Recipes" }) AssetDatabase.CreateFolder(folder,child);
                mesh = GrassGenerator.Build(r,GrassStudy.Clump); mesh.name = stem;
                AssetDatabase.CreateAsset(mesh,folder+"/Models/"+stem+".asset");
                ramp = GrassGenerator.Ramp(r); AssetDatabase.CreateAsset(ramp,folder+"/Materials/BladeGradient.asset");
                material = new Material(shader) { name = stem+" Grass", enableInstancing = true };
                GrassGenerator.Configure(material,ramp,r); AssetDatabase.CreateAsset(material,folder+"/Materials/Grass.mat");
                temporary = new GameObject(stem);
                SceneManager.MoveGameObjectToScene(temporary,previewScene);
                temporary.AddComponent<MeshFilter>().sharedMesh = mesh;
                temporary.AddComponent<MeshRenderer>().sharedMaterial = material;
                var prefab = PrefabUtility.SaveAsPrefabAsset(temporary,folder+"/Prefabs/"+stem+".prefab");
                if (prefab == null) throw new IOException("Could not save grass prefab.");
                AssetDatabase.SetLabels(prefab,new[] { "TerrainityGenerated" });
                TreeJsonStorage.Write(folder+"/Recipes/"+stem+".json",GrassRecipeJson.From(r));
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); return prefab;
            }
            catch
            {
                AssetDatabase.DeleteAsset(folder);
                throw;
            }
            finally
            {
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                EditorSceneManager.ClosePreviewScene(previewScene);
                foreach (var obj in new UnityEngine.Object[] { mesh,ramp,material })
                    if (obj != null && !AssetDatabase.Contains(obj)) UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        internal static Bounds GeometryBounds(Mesh mesh)
        {
            var vertices = mesh.vertices;
            if (vertices.Length == 0) return mesh.bounds;
            var bounds = new Bounds(vertices[0],Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(vertex);
            return bounds;
        }

        internal static bool AddToTerrain(Terrain terrain,GameObject prefab)
        {
            if (terrain == null || terrain.terrainData == null || prefab == null) throw new InvalidOperationException("Choose a target Terrain and generate a grass clump first.");
            var data = terrain.terrainData; var existing = data.detailPrototypes;
            foreach (var item in existing) if (item.prototype == prefab) return false;
            var prototype = new DetailPrototype { prototype = prefab, usePrototypeMesh = true, useInstancing = true,
                renderMode = DetailRenderMode.VertexLit, minWidth = 1, maxWidth = 1, minHeight = 1, maxHeight = 1,
                healthyColor = Color.white, dryColor = Color.white, noiseSpread = .1f };
            if (!prototype.Validate(out string error)) throw new InvalidOperationException(error);
            Undo.RegisterCompleteObjectUndo(data,"Add Terrainity grass detail");
            var prototypes = new DetailPrototype[existing.Length+1]; Array.Copy(existing,prototypes,existing.Length);
            prototypes[existing.Length] = prototype; data.detailPrototypes = prototypes;
            EditorUtility.SetDirty(data); terrain.Flush(); return true;
        }
    }
}
