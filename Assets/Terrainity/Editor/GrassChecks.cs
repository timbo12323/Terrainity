using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    internal static class GrassChecks
    {
        static int assertions;
        static void Check(bool success,string message)
        {
            if (!success) throw new InvalidOperationException(message);
            assertions++;
        }
        static void Valid(Mesh mesh)
        {
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            Check(vertices.Length > 0 && triangles.Length > 0,"Nonempty grass mesh");
            Check(vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)),"Finite grass vertices");
            Check(triangles.All(i => i >= 0 && i < vertices.Length),"Valid triangle indices");
            for (int i=0;i<triangles.Length;i+=3)
                Check(Vector3.Cross(vertices[triangles[i+1]]-vertices[triangles[i]],vertices[triangles[i+2]]-vertices[triangles[i]]).sqrMagnitude > 1e-20f,"No degenerate triangles");
            Check(mesh.uv2.Length == vertices.Length && mesh.uv2.All(v => v.x >= 0 && v.x <= 1),"Root-to-tip gradient coordinates");
        }
        [MenuItem("Tools/Terrainity/Validate Grass Generator")]
        internal static void Run()
        {
            assertions = 0;
            var meshes = new List<Mesh>(); string generated = null;
            TerrainData data = null; GameObject terrainObject = null; TerrainityWindow window = null;
            string report = Path.GetFullPath("TerrainityReports/Grass"); Directory.CreateDirectory(report);
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                Mesh Build(GrassRecipe recipe,GrassStudy study) { var m = GrassGenerator.Build(recipe,study); meshes.Add(m); return m; }
                foreach (string style in GrassRecipe.Styles)
                {
                    var r = new GrassRecipe(); r.ApplyStyle(style);
                    var a = Build(r,GrassStudy.Blade); Valid(a);
                    Check(a.vertices.Where((v,i) => a.uv[i].y == 0).All(v => Mathf.Abs(v.y) < 1e-6f),"Blade root stays on ground");
                    Check(a.uv2.Any(v => v.x == 0) && a.uv2.Any(v => v.x == 1),"Gradient includes both endpoints");
                    var clump = Build(r,GrassStudy.Clump); Valid(clump);
                    Check(clump.vertexCount == a.vertexCount*r.bladeCount,"Clump contains requested blade count");
                    var repeat = Build(r,GrassStudy.Clump);
                    Check(clump.vertices.SequenceEqual(repeat.vertices),"Seed is deterministic");
                    r.lengthSegments *= 2;
                    var fine = Build(r,GrassStudy.Clump);
                    var coarsePositions = clump.vertices; var finePositions = fine.vertices;
                    int widthStride = Mathf.Max(2,r.widthSegments+r.widthSegments%2)+1;
                    for (int blade=0;blade<r.bladeCount;blade++)
                        for (int row=0;row<=r.lengthSegments/2;row++)
                            for (int col=0;col<widthStride;col++)
                                Check((coarsePositions[blade*(r.lengthSegments/2+1)*widthStride+row*widthStride+col]
                                    -finePositions[blade*(r.lengthSegments+1)*widthStride+row*2*widthStride+col]).sqrMagnitude < 1e-12f,"Quality retains shape and placement samples");
                    using (var preview = new TreePreview())
                    {
                        preview.BuildGrass(r,GrassStudy.Blade);
                        var image = preview.ReferenceSnapshot(512);
                        File.WriteAllBytes(Path.Combine(report,style.Replace(' ','-')+".png"),image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        preview.BuildGrass(r,GrassStudy.RepeatedPatch);
                        image = preview.ReferenceSnapshot(512);
                        File.WriteAllBytes(Path.Combine(report,style.Replace(' ','-')+"-patch.png"),image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        preview.Build(new TreeRecipe { showFoliage = false },0);
                        Check(preview.Meshes.Count > 0,"Grass preview can switch back to trees");
                    }
                    r.thickness = .003f; r.tipWidth = .1f; r.twist = -120; r.bend = -1;
                    Valid(Build(r,GrassStudy.Blade));
                }
                var recipe = new GrassRecipe { bladeCount = 8, patchCount = 2, gap = .8f, irregularity = 0, lengthSegments = 1, widthSegments = 1, fold = 0, belly = 0 };
                var bladeMesh = Build(recipe,GrassStudy.Blade); var clumpMesh = Build(recipe,GrassStudy.Clump);
                var patchMesh = Build(recipe,GrassStudy.RepeatedPatch);
                Check(patchMesh.vertexCount == clumpMesh.vertexCount*4,"Patch repetition count");
                for (int blade=0;blade<recipe.bladeCount;blade++)
                {
                    var root = (clumpMesh.vertices[blade*bladeMesh.vertexCount]+clumpMesh.vertices[blade*bladeMesh.vertexCount+1])*.5f;
                    Check(root.magnitude >= recipe.gap*recipe.radius-1e-6f,"Center gap respected by blade roots");
                }
                Valid(bladeMesh);
                var randomState = UnityEngine.Random.state;
                Build(recipe,GrassStudy.Clump);
                Check(JsonUtility.ToJson(randomState) == JsonUtility.ToJson(UnityEngine.Random.state),"Generator preserves global random state");
                string path = Path.Combine(report,"roundtrip.json");
                recipe.gradient.mode = GradientMode.Fixed;
                TreeJsonStorage.Write(path,GrassRecipeJson.From(recipe),false);
                var loaded = GrassRecipeJson.Read(path);
                Check(JsonUtility.ToJson(TreeGradientJson.From(loaded.gradient)) == JsonUtility.ToJson(TreeGradientJson.From(recipe.gradient)),"Gradient stop and mode JSON round-trip");
                Check(loaded.Copy().gradient.mode == GradientMode.Fixed,"Gradient copied independently");
                var invalid = recipe.Copy(); invalid.height = float.NaN;
                bool rejected = false; try { invalid.Validate(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected,"Reject non-finite recipe inputs");
                recipe.assetName = "Grass_Validation_Temporary";
                var prefab = GrassExporter.Generate(recipe,out generated);
                Check(prefab != null && prefab.GetComponent<MeshFilter>().sharedMesh.vertexCount == clumpMesh.vertexCount,"Export saves a clump regardless of study mode");
                var mat = prefab.GetComponent<MeshRenderer>().sharedMaterial;
                Check(mat.enableInstancing && mat.GetTexture("_TintRamp") != null,"Persistent instanced gradient material");
                Check(AssetDatabase.GetLabels(prefab).Contains("TerrainityGenerated"),"Grass appears in generated library");
                Check(GrassRecipeJson.Read(generated+"/Recipes/"+prefab.name+".json").gradient.mode == GradientMode.Fixed,"Export recipe retains gradient mode");
                data = new TerrainData(); data.SetDetailResolution(32,8); terrainObject = Terrain.CreateTerrainGameObject(data);
                var terrain = terrainObject.GetComponent<Terrain>();
                Check(GrassExporter.AddToTerrain(terrain,prefab),"Register Terrain details");
                Check(data.detailPrototypes.Length == 1 && data.detailPrototypes[0].useInstancing,"GPU instanced detail prototype");
                Check(!GrassExporter.AddToTerrain(terrain,prefab),"Detail registration is idempotent");
                Undo.PerformUndo(); Check(data.detailPrototypes.Length == 0,"Undo restores detail palette");

                window = ScriptableObject.CreateInstance<TerrainityWindow>(); window.CreateGUI();
                var serialized = new SerializedObject(window); serialized.FindProperty("category").stringValue = "Grass";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                typeof(TerrainityWindow).GetMethod("ShowTab",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[] { 1 });
                Check(window.rootVisualElement.Query<GradientField>().ToList().Count == 1,"Grass builder contains gradient editor");
                Check(window.rootVisualElement.Query<SliderInt>().ToList().Any(s => s.label == "Length subdivisions"),"Grass builder contains quality controls");
                Check(window.rootVisualElement.Query<Button>().ToList().Any(b => b.text == "Generate clump prefab"),"Grass builder contains export action");
                foreach (var shader in new[] { Shader.Find("Terrainity/Grass"), Shader.Find("Terrainity/Tree") })
                    Check(shader != null && !ShaderUtil.ShaderHasError(shader),"Grass preview and runtime shaders compile");
                File.WriteAllText(Path.Combine(report,"validation.txt"),$"PASS: {assertions} assertions. Four styles; valid ribbon/solid topology; anchored roots; deterministic placement; quality-independent samples; gap and repetition counts; shared gradient coordinates and JSON; exported resources; Terrain detail registration/Undo; builder controls; shaders and rendered previews.");
                Debug.Log($"Grass validation PASS: {assertions} assertions.");
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(report,"validation.txt"),e.ToString()); Debug.LogException(e); throw; }
            finally
            {
                foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
                if (terrainObject != null) UnityEngine.Object.DestroyImmediate(terrainObject);
                if (data != null) UnityEngine.Object.DestroyImmediate(data);
                if (!string.IsNullOrEmpty(generated)) AssetDatabase.DeleteAsset(generated);
            }
        }
    }
}
