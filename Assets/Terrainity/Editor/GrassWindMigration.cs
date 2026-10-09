using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    [InitializeOnLoad]
    internal static class GrassWindMigration
    {
        static GrassWindMigration()
        {
            EditorApplication.delayCall += UpgradeWhenReady;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Upgrade(false);
            };
        }
        static void UpgradeWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += UpgradeWhenReady; return; }
            Upgrade(false);
        }

        [MenuItem("Tools/Terrainity/Upgrade Grass Runtime Wind")]
        internal static void UpgradeMenu() => Upgrade(true);

        internal static int Upgrade(bool report)
        {
            const string root = "Assets/TerrainityGenerated/Grass";
            if (!AssetDatabase.IsValidFolder(root)) return 0;
            int changed = 0;
            var processed = new HashSet<Mesh>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab",new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || Array.IndexOf(AssetDatabase.GetLabels(prefab),"TerrainityGenerated") < 0) continue;
                var filter = prefab.GetComponent<MeshFilter>(); var renderer = prefab.GetComponent<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null
                    || renderer.sharedMaterial.shader == null || renderer.sharedMaterial.shader.name != "Terrainity/Grass") continue;
                var mesh = filter.sharedMesh;
                if (!processed.Add(mesh)) continue;
                Mesh rebuilt = null;
                try
                {
                    var weights = new List<Vector4>(); var roots = new List<Vector4>();
                    mesh.GetUVs(3,weights); mesh.GetUVs(4,roots);
                    bool missing = weights.Count != mesh.vertexCount || roots.Count != mesh.vertexCount;
                    if (missing)
                    {
                        string folder = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\','/');
                        string recipePath = folder+"/Recipes/"+prefab.name+".json";
                        if (!File.Exists(recipePath)) { Debug.LogWarning("Grass wind upgrade needs its saved recipe: "+path); continue; }
                        rebuilt = GrassGenerator.Build(GrassRecipeJson.Read(recipePath),GrassStudy.Clump);
                        var oldVertices = mesh.vertices; var newVertices = rebuilt.vertices;
                        bool same = oldVertices.Length == newVertices.Length;
                        for (int i=0;same && i<oldVertices.Length;i++) same = (oldVertices[i]-newVertices[i]).sqrMagnitude <= 1e-12f;
                        if (!same) { Debug.LogWarning("Grass mesh was edited; wind upgrade preserved its geometry: "+path); continue; }
                        weights.Clear(); roots.Clear(); rebuilt.GetUVs(3,weights); rebuilt.GetUVs(4,roots);
                        Undo.RecordObject(mesh,"Upgrade grass wind metadata");
                        mesh.SetUVs(3,weights); mesh.SetUVs(4,roots);
                    }
                    var bounds = GrassExporter.GeometryBounds(mesh);
                    float height = 0; foreach (var rootData in roots) height = Mathf.Max(height,rootData.z);
                    var padded = bounds; padded.Expand(new Vector3(height*2.4f,0,height*2.4f));
                    // Restore only exact padding produced by the initial runtime upgrade.
                    // Coverage scatter uses bounds for density; preserve authored bounds.
                    bool oldPadding = mesh.bounds == padded;
                    if (missing || oldPadding)
                    {
                        if (!missing) Undo.RecordObject(mesh,"Preserve grass coverage density");
                        if (oldPadding) mesh.bounds = bounds;
                        EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); changed++;
                    }
                    var material = renderer.sharedMaterial;
                    if (material.HasProperty("_GrassPreview") && material.GetFloat("_GrassPreview") > .5f)
                    {
                        Undo.RecordObject(material,"Use runtime grass wind"); material.SetFloat("_GrassPreview",0); EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
                    }
                }
                catch (Exception e) { Debug.LogWarning("Cannot upgrade grass wind for "+path+": "+e.Message); }
                finally { if (rebuilt != null) UnityEngine.Object.DestroyImmediate(rebuilt); }
            }
            if (changed > 0 || report) Debug.Log("Terrainity grass runtime wind: upgraded "+changed+" meshes in place. Prefab and Terrain references are retained.");
            return changed;
        }
    }
}
