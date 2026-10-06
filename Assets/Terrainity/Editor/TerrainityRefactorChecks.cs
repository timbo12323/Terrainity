using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    // Regression checks for shared settings, detached snapshots and corrected lifecycle behavior.
    internal static class TerrainityRefactorChecks
    {
        [MenuItem("Tools/Terrainity/Validate Refactor")]
        internal static void Run()
        {
            try
            {
                CheckRecipes();
                CheckJson();
                CheckAlignedLod();
                CheckWindCleanup();
                Require(TreeExporter.IsFamilyFolderPath(TreeExporter.OutputRoot + "/Test"), "Tree package folder");
                Require(TreeExporter.IsFamilyFolderPath(RockExporter.OutputRoot + "/Test"), "Rock package folder");
                Require(!TreeExporter.IsFamilyFolderPath("Assets/Other/Test")
                    && !TreeExporter.IsFamilyFolderPath(TreeExporter.OutputRoot + "/../Other"), "Package folder boundary");
                File.WriteAllText("Temp/terrainity-refactor-checks.txt", "PASS: inherited recipe fields and legacy defaults, deep copies, immutable JSON snapshots, profile/family caches, nested numeric validation, root JSON envelopes, file size limits, aligned LOD stem anchors, tree/rock preview reuse, wind cleanup and package paths.");
                Debug.Log("[Terrainity] " + File.ReadAllText("Temp/terrainity-refactor-checks.txt"));
            }
            catch (Exception e)
            {
                File.WriteAllText("Temp/terrainity-refactor-checks.txt", "FAIL: " + e);
                throw;
            }
        }

        static void CheckRecipes()
        {
            var recipe = new TreeRecipe();
            Require(recipe.foliageSizeMin == .8f && new TreeRecipeSettings().foliageSizeMin == 0, "New/legacy size defaults");
            var profile = recipe.Profile;
            Require(ReferenceEquals(profile, recipe.Profile), "Legacy profile reuse");
            recipe.species = "Oak";
            Require(recipe.Profile.name == "Oak" && !ReferenceEquals(profile, recipe.Profile), "Species invalidates profile");
            recipe.useFamilyProfile = true;
            recipe.familyProfile = TreeFamilyProfile.Legacy("Oak");
            recipe.customBranches = recipe.familyProfile.branches.Copy();
            recipe.prunedFoliageCards.Add(17);
            var copy = recipe.Copy();
            Require(JsonUtility.ToJson(copy) == JsonUtility.ToJson(recipe), "Copy retains serialized settings");
            copy.customBranches.limbs++;
            copy.familyProfile.branches.limbs++;
            copy.prunedFoliageCards.Clear();
            copy.barkGradient.SetKeys(new[] { new GradientColorKey(Color.red, 0), new GradientColorKey(Color.blue, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            Require(copy.customBranches.limbs != recipe.customBranches.limbs
                && copy.familyProfile.branches.limbs != recipe.familyProfile.branches.limbs
                && recipe.prunedFoliageCards.Count == 1 && recipe.barkGradient.Evaluate(0) != Color.red, "Copy owns mutable fields");
            var document = TreeRecipeJson.From(recipe, 0);
            string snapshot = JsonUtility.ToJson(document);
            recipe.familyProfile.name = "Edited";
            recipe.customBranches.limbs++;
            recipe.prunedFoliageCards.Clear();
            Require(JsonUtility.ToJson(document) == snapshot, "JSON snapshot aliases the builder");
            document.settings.foliageSizeMin = 0;
            var legacy = document.Restore(out _);
            Require(legacy.foliageSizeMin == legacy.foliageSize, "Legacy fixed card size migration");
            TreeJsonStorage.ReloadFamilies();
            var family = TreeJsonStorage.Families.First(f => f.defaults != null);
            TreeJsonStorage.ReloadFamilies();
            Require(ReferenceEquals(family, TreeJsonStorage.Families.First(f => f.profile.id == family.profile.id)), "Unchanged family cache");
            using (var preview = new TreePreview())
            {
                preview.Build(new TreeRecipe(), 0);
                string shader = preview.MaterialForMesh(0).shader.name;
                preview.BuildRock(new RockRecipe { subdivisions = 1 }, 0);
                preview.Build(new TreeRecipe(), 0);
                Require(preview.MaterialForMesh(0).shader.name == shader, "Tree shader after rock preview");
                preview.Dispose(); // Closing and rebuilding windows may dispose the same owner twice.
            }
        }

        static void CheckJson()
        {
            Reject(() => TreeJsonStorage.ValidateNumbers(new List<float> { 1, float.NaN }), "NaN list element");
            Reject(() => TreeJsonStorage.ValidateNumbers(new[] { Vector3.one, new Vector3(0, float.PositiveInfinity, 0) }), "Infinite array element");
            string path = "Temp/terrainity-refactor-input.json";
            try
            {
                foreach (string json in new[] {
                    "{}", "{\"nested\":{\"format\":\"terrainity.recipe\",\"version\":1}}",
                    "{\"format\":\"terrainity.recipe\",\"nested\":{\"version\":1}}"
                })
                {
                    File.WriteAllText(path, json);
                    Reject(() => TreeJsonStorage.Read<TreeRecipeJson>(path), "Missing root recipe header");
                }
                File.WriteAllText(path, "{\"assetName\":\"TerrainityRock\",\"version\":1}");
                Reject(() => RockRecipe.Read(path), "Rock kind must be a root field");
                File.WriteAllText(path, "{\"kind\":\"TerrainityRock\",\"version\":1}");
                Require(RockRecipe.Read(path).uvMode == RockUvMode.Spherical, "Legacy rock migration");
                File.WriteAllText(path, new string(' ', 1024 * 1024 + 1));
                Reject(() => TreeJsonStorage.ReadText(path), "JSON file size limit");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        static void CheckAlignedLod()
        {
            var recipe = new TreeRecipe { foliageAligned = true, foliageStemAttached = true,
                foliageStemPivot = new Vector2(.2f, .8f), foliageSubdivisions = 4, layers = 6 };
            using (var preview = new TreePreview())
            {
                preview.Build(recipe, 0);
                var source = preview.Meshes[1];
                var sourceVertices = source.vertices;
                var sourceIds = new List<Vector4>(); source.GetUVs(3, sourceIds);
                var starts = new Dictionary<int, int>();
                const int stride = 16;
                for (int i = 0; i < source.vertexCount; i += stride) starts.Add(Mathf.RoundToInt(sourceIds[i].w), i);
                var reduced = TreeLodGenerator.ReduceFoliage(source, recipe, 2, new TreeLodSettings());
                try
                {
                    var vertices = reduced.vertices;
                    var ids = new List<Vector4>(); reduced.GetUVs(3, ids);
                    for (int i = 0; i < vertices.Length; i += stride)
                    {
                        int original = starts[Mathf.RoundToInt(ids[i].w)];
                        var before = (sourceVertices[original] + sourceVertices[original + 1]) * .5f;
                        var after = (vertices[i] + vertices[i + 1]) * .5f;
                        Require(Vector3.Distance(before, after) < .00001f, "Aligned LOD card moved off its stem");
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(reduced); }
            }
        }

        static void CheckWindCleanup()
        {
            var type = typeof(TreeWindController);
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var field = type.GetField("instance", flags);
            object previousInstance = field.GetValue(null);
            Vector4 previousDirection = Shader.GetGlobalVector("_TerrainityWindDirection");
            Vector4 previousMotion = Shader.GetGlobalVector("_TerrainityWindMotion");
            float previousCount = Shader.GetGlobalFloat("_TerrainityWindSphereCount");
            var go = new GameObject("Terrainity wind validation") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                type.GetMethod("ResetDriver", flags).Invoke(null, null);
                var driver = go.AddComponent<TreeWindController>();
                type.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, null);
                Shader.SetGlobalVector("_TerrainityWindDirection", Vector4.one);
                Shader.SetGlobalVector("_TerrainityWindMotion", Vector4.one);
                Shader.SetGlobalFloat("_TerrainityWindSphereCount", 4);
                type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(driver, null);
                Require(Shader.GetGlobalVector("_TerrainityWindDirection") == Vector4.zero
                    && Shader.GetGlobalVector("_TerrainityWindMotion") == Vector4.zero
                    && Shader.GetGlobalFloat("_TerrainityWindSphereCount") == 0, "Disabled wind left shader globals active");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                field.SetValue(null, previousInstance);
                Shader.SetGlobalVector("_TerrainityWindDirection", previousDirection);
                Shader.SetGlobalVector("_TerrainityWindMotion", previousMotion);
                Shader.SetGlobalFloat("_TerrainityWindSphereCount", previousCount);
            }
        }

        static void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Accepted invalid input: " + message);
        }

        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
