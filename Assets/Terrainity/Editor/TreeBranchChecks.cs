using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeBranchChecks
    {
        [MenuItem("Tools/Terrainity/Validate Family Library")]
        static void ValidateFamilyLibrary()
        {
            ValidateJsonRecipes();
            var report = new System.Text.StringBuilder();
            foreach (string name in new[] { "Aspen", "Birch", "Cedar", "Elm", "Fir", "Maple", "Oak", "Palm", "Pine", "Poplar", "Spruce", "Sycamore", "Willow" })
            {
                var family = TreeJsonStorage.ReadFamily("Assets/Terrainity/Families/" + name + ".json");
                Require(family.defaults != null, name + " missing default recipe");
                Require(JsonUtility.ToJson(family.profile) == JsonUtility.ToJson(family.defaults.family), name + " embedded profile differs from family");
                Require(JsonUtility.ToJson(family.profile.branches) == JsonUtility.ToJson(family.defaults.settings.customBranches), name + " branch defaults differ from profile");
                var recipe = family.defaults.Restore(out var warning);
                Require(warning.Length == 0, name + " missing textures");
                var copy = JsonUtility.FromJson<TreeRecipeJson>(JsonUtility.ToJson(TreeRecipeJson.From(recipe, 0))).Restore(out _);
                Require(copy.Profile.foliageForm == recipe.Profile.foliageForm && copy.Profile.crownEnd == recipe.Profile.crownEnd, name + " lost new profile fields");
                float foliageMin = float.MaxValue, max = 0, width = 0;
                int wood = 0, leaves = 0;
                using (var preview = new TreePreview())
                {
                    for (int sibling = 0; sibling < 5; sibling++)
                    {
                        preview.Build(recipe, sibling);
                        Require(preview.Meshes.Count == 2 && preview.FoliageTriangles > 0, name + " missing geometry");
                        foliageMin = Mathf.Min(foliageMin, preview.Meshes[1].bounds.min.y);
                        foreach (var mesh in preview.Meshes)
                        {
                            max = Mathf.Max(max, mesh.bounds.max.y);
                            width = Mathf.Max(width, mesh.bounds.size.x, mesh.bounds.size.z);
                            foreach (var vertex in mesh.vertices)
                                Require(float.IsFinite(vertex.x) && float.IsFinite(vertex.y) && float.IsFinite(vertex.z), name + " non-finite vertex");
                        }
                        if (sibling == 0)
                        {
                            wood = preview.WoodTriangles; leaves = preview.FoliageTriangles;
                            var picture = preview.ReferenceSnapshot();
                            System.IO.File.WriteAllBytes("Temp/terrainity-" + name + ".png", picture.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(picture);
                            var woodVertices = preview.Meshes[0].vertices;
                            var leafVertices = preview.Meshes[1].vertices;
                            preview.Build(copy, 0);
                            Require(preview.WoodTriangles == wood && preview.FoliageTriangles == leaves, name + " JSON changed triangle counts");
                            var restoredWood = preview.Meshes[0].vertices;
                            var restoredLeaves = preview.Meshes[1].vertices;
                            Require(woodVertices.Length == restoredWood.Length && leafVertices.Length == restoredLeaves.Length, name + " JSON changed vertex counts");
                            for (int i = 0; i < woodVertices.Length; i++) Require(woodVertices[i] == restoredWood[i], name + " JSON changed wood geometry");
                            for (int i = 0; i < leafVertices.Length; i++) Require(leafVertices[i] == restoredLeaves[i], name + " JSON changed foliage geometry");
                        }
                    }
                }
                report.AppendLine($"{name}: foliageMin={foliageMin:F2}, height={max:F2}, width={width:F2}, wood={wood}, foliage={leaves}");
                Require(foliageMin >= 0, name + " default foliage extends below ground");
            }
            System.IO.File.WriteAllText("Temp/terrainity-families.txt", report.ToString());
            Debug.Log("[Terrainity] PASS: Family JSON geometry round-trips, ground clearance and finite geometry across 65 sibling previews. " + report);
        }

        [MenuItem("Tools/Terrainity/Validate Willow Defaults")]
        static void ValidateWillowDefaults()
        {
            var family = TreeJsonStorage.ReadFamily("Assets/Terrainity/Families/Willow.json");
            var recipe = family.defaults.Restore(out var warning);
            ValidateJsonRecipes();
            float min = float.MaxValue, max = float.MinValue, radial = 0;
            int hanging = 0, terminal = 0;
            for (int variant = 0; variant < recipe.variantCount; variant++)
            {
                var sibling = new System.Random(unchecked(recipe.seed + variant * 7919));
                float scale = variant == 0 ? 1 : 1 + ((float)sibling.NextDouble() * 2 - 1) * recipe.variation;
                float spread = variant == 0 ? 1 : 1 + ((float)sibling.NextDouble() * 2 - 1) * recipe.variation;
                float height = recipe.height * scale;
                var trunk = new TreeTrunkGrowth(recipe, new Vector3(recipe.lean * height * .18f, height, 0));
                var limbs = TreeBranchGrowth.Generate(recipe, trunk, spread, variant);
                foreach (var limb in limbs)
                {
                    foreach (var point in limb.points) { min = Mathf.Min(min, point.y); max = Mathf.Max(max, point.y); radial = Mathf.Max(radial, new Vector2(point.x, point.z).magnitude); }
                    if (limb.terminal) { terminal++; if (limb.points[12].y < limb.points[11].y) hanging++; }
                }
            }
            using (var preview = new TreePreview())
            {
                preview.Build(recipe, 0);
                Require(min > .25f, "Willow default branches extend below ground clearance");
                Require(hanging > terminal * .95f, "Willow default lost its weeping tips");
                for (int variant = 0; variant < recipe.variantCount; variant++)
                {
                    preview.Build(recipe, variant);
                    // The tilted trunk base ring can straddle y=0; check foliage separately.
                    Require(preview.Meshes.Count == 2 && preview.Meshes[1].bounds.min.y >= 0,
                        "Willow default foliage extends below ground");
                }
                preview.Build(recipe, 0);
                Debug.Log($"[Terrainity] PASS: Willow defaults. minY={min}, maxY={max}, width={radial * 2}, hanging={hanging}/{terminal}, wood={preview.WoodTriangles}, foliage={preview.FoliageTriangles}, warning={warning}");
            }
        }

        [MenuItem("Tools/Terrainity/Validate JSON Recipes")]
        static void ValidateJsonRecipes()
        {
            TreeJsonStorage.ReloadFamilies();
            Require(TreeJsonStorage.Families.Count >= 4, "Missing built-in families");
            foreach (string species in new[] { "Pine", "Oak", "Birch", "Maple" })
            {
                var original = new TreeRecipe { species = species, foliageGradientEnabled = true, barkGradientEnabled = true };
                original.Branches.subBranches = 3;
                var document = TreeRecipeJson.From(original, 2);
                var json = JsonUtility.ToJson(document);
                var loaded = JsonUtility.FromJson<TreeRecipeJson>(json).Restore(out var warning);
                Require(warning == "", "Unexpected missing texture");
                Require(loaded.Branches.subBranches == 3 && loaded.species == species, "Lost recipe settings");
                Require(loaded.foliageGradient.colorKeys.Length == original.foliageGradient.colorKeys.Length, "Lost gradient stops");
                Require(loaded.foliageGradient.Evaluate(.4f) == original.foliageGradient.Evaluate(.4f), "Changed gradient");
                var a = TreeBranchGrowth.Generate(original, Vector3.up * original.height, 1, 0);
                var b = TreeBranchGrowth.Generate(loaded, Vector3.up * loaded.height, 1, 0);
                Require(a.Count == b.Count, "JSON changed branch count");
                for (int i = 0; i < a.Count; i++) for (int j = 0; j < a[i].points.Length; j++)
                    Require(Vector3.Distance(a[i].points[j], b[i].points[j]) < .00001f, "JSON changed geometry");
            }
            var custom = TreeFamilyProfile.Legacy("Pine"); custom.id = "test-family"; custom.name = "Test Family";
            var customRecipe = new TreeRecipe { useFamilyProfile = true, familyProfile = custom, species = custom.name, customBranches = custom.branches };
            var roundtrip = JsonUtility.FromJson<TreeRecipeJson>(JsonUtility.ToJson(TreeRecipeJson.From(customRecipe, 0))).Restore(out _);
            Require(roundtrip.Profile.id == "test-family" && roundtrip.Profile.limbsPerWhorl == 4, "Custom family was lost");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/DreamForestTree/Textures/Trees/leaf 1.psd");
            if (texture != null)
            {
                customRecipe.foliageTexture = texture;
                var textureDoc = TreeRecipeJson.From(customRecipe, 1);
                TreeJsonStorage.Write("Temp/terrainity-roundtrip.json", textureDoc);
                TreeJsonStorage.Write("Temp/terrainity-roundtrip.json", textureDoc);
                var diskDoc = TreeJsonStorage.Read<TreeRecipeJson>("Temp/terrainity-roundtrip.json");
                Require(diskDoc.Restore(out _).foliageTexture == texture, "Texture GUID did not restore");
                diskDoc.foliageTextureGuid = "missing"; diskDoc.foliageTexturePath = "Assets/missing.png";
                Require(diskDoc.Restore(out var missing).foliageTexture == null && missing.Length > 0, "Missing texture was not reported");
            }
            foreach (var family in TreeJsonStorage.Families)
            {
                var loadedFamily = JsonUtility.FromJson<TreeFamilyJson>(JsonUtility.ToJson(family));
                TreeJsonStorage.ValidateFamily(loadedFamily.profile);
            }
            bool rejected = false;
            try { new TreeRecipeJson { version = 999 }.Restore(out _); } catch (System.IO.InvalidDataException) { rejected = true; }
            Require(rejected, "Unsupported version accepted");
            Debug.Log("[Terrainity] PASS: JSON recipe round-trips, gradients, family snapshots and deterministic geometry.");
        }

        [MenuItem("Tools/Terrainity/Validate Material Appearance")]
        static void ValidateMaterialAppearance()
        {
            var shader = Shader.Find("Hidden/Terrainity/Tint Preview");
            Require(shader != null, "Missing preview shader");
            var material = new Material(shader);
            try
            {
                var recipe = new TreeRecipe();
                Require(recipe.foliageRoughness == 1 && !recipe.foliageHighlights && !recipe.foliageReflections, "Foliage must default to matte");
                for (int flags = 0; flags < 4; flags++)
                {
                    bool highlights = (flags & 1) != 0, reflections = (flags & 2) != 0;
                    TreePreview.ConfigureSurface(material, .75f, highlights, reflections);
                    Require(Mathf.Approximately(material.GetFloat("_Smoothness"), .25f), "Incorrect roughness conversion");
                    Require(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF") == !highlights, "Incorrect highlight state");
                    Require(material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF") == !reflections, "Incorrect reflection state");
                    ShaderUtil.CompilePass(material, 0, true);
                }
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            ValidateGradientPreview();
            Debug.Log("[Terrainity] PASS: matte defaults, material toggles, roughness and shader variants.");
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void CheckMaterialRequest()
        {
            const string request = "Temp/terrainity-material-check.request";
            if (!System.IO.File.Exists(request)) return;
            System.IO.File.Delete(request);
            EditorApplication.delayCall += () =>
            {
                try { ValidateMaterialAppearance(); System.IO.File.WriteAllText("Temp/terrainity-material-check.txt", "PASS: material controls, shader variants and gradient regression checks."); }
                catch (Exception e) { System.IO.File.WriteAllText("Temp/terrainity-material-check.txt", "FAIL: " + e); }
            };
        }

        [MenuItem("Tools/Terrainity/Validate Gradient Tints")]
        static void ValidateGradientPreview()
        {
            var shader = Shader.Find("Hidden/Terrainity/Tint Preview");
            Require(shader != null, "Missing tint preview shader");
            var material = new Material(shader);
            try { ShaderUtil.CompilePass(material, 0, true); }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                Require(message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error, message.message);
            using (var preview = new TreePreview())
            {
                var recipe = new TreeRecipe { barkGradientEnabled = true, foliageGradientEnabled = true };
                foreach (FoliageTintMode mode in Enum.GetValues(typeof(FoliageTintMode)))
                {
                    recipe.foliageTintMode = mode;
                    preview.Build(recipe, 0);
                    foreach (var mesh in preview.Meshes)
                    {
                        Require(mesh.uv2.Length == mesh.vertexCount, "Missing gradient coordinates");
                        foreach (var uv in mesh.uv2) Require(uv.x >= 0 && uv.x <= 1, "Invalid gradient coordinate");
                    }
                    if (mode == FoliageTintMode.PerCard)
                    {
                        var uv = preview.Meshes[1].uv2;
                        for (int i = 0; i < uv.Length; i += 4)
                            Require(uv[i] == uv[i + 1] && uv[i] == uv[i + 2] && uv[i] == uv[i + 3], "Card tint is not uniform");
                    }
                }
            }
            Debug.Log("[Terrainity] PASS: tint shader compiled; all three placement modes have valid gradient coordinates and card colors.");
        }

        [MenuItem("Tools/Terrainity/Validate Branch Growth")]
        internal static void Run()
        {
            int checks = 0;
            foreach (string species in new[] { "Pine", "Oak", "Birch", "Maple" })
            {
                var recipe = new TreeRecipe { species = species };
                Vector3 tip = new Vector3(.1f, 7, 0);
                var settings = recipe.Branches;
                settings.limbs = 8;
                for (int depth = 0; depth <= 3; depth++)
                {
                    settings.depth = depth;
                    var limbs = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                    Require(limbs.Count >= 8 && limbs.Count <= 360, "Invalid branch count");
                    int mainCount = 0;
                    for (int i = 0; i < limbs.Count; i++)
                    {
                        if (limbs[i].level != 0) continue;
                        mainCount++;
                        int forks = 0;
                        for (int j = i + 1; j < limbs.Count; j++)
                            if (limbs[j].parentIndex == i) forks++;
                        Require(depth == 0 ? forks == 0 : forks >= 2 && forks <= 4,
                            "Main limb did not fork two to four times");
                    }
                    Require(mainCount == 8, "Incorrect main limb count");
                    foreach (var limb in limbs)
                        foreach (var point in limb.points)
                            Require(!float.IsNaN(point.sqrMagnitude) && !float.IsInfinity(point.sqrMagnitude), "Non-finite geometry");
                    Same(limbs, TreeBranchGrowth.Generate(recipe, tip, 1, 0));
                    recipe.variation = 0;
                    Same(limbs, TreeBranchGrowth.Generate(recipe, tip, 1, 4));
                    checks += 3;
                }
                settings.bend = 0;
                var straight = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                foreach (var limb in straight)
                    foreach (var point in limb.points)
                        Require(Vector3.Cross(point - limb.points[0], limb.points[limb.points.Length - 1] - limb.points[0]).magnitude < .0001f, "Zero bend is not straight");
                settings.bend = 1;
                var bent = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require((bent[0].points[TreeBranchGrowth.Segments] - straight[0].points[TreeBranchGrowth.Segments]).sqrMagnitude > .0001f, "Bend has no effect");
                settings.limbs = 0;
                Require(TreeBranchGrowth.Generate(recipe, tip, 1, 0).Count == 0, "Zero limbs is not empty");
                settings.limbs = 24;
                Require(TreeBranchGrowth.Generate(recipe, tip, 1, 0).Count <= 360, "Maximum branch budget exceeded");
                settings.limbs = 8;
                settings.depth = 0;
                settings.bend = 0;
                settings.spread = .5f;
                var narrow = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                settings.spread = 1;
                var wide = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require(Mathf.Abs(Vector3.Distance(wide[0].points[0], wide[0].points[TreeBranchGrowth.Segments]) /
                    Vector3.Distance(narrow[0].points[0], narrow[0].points[TreeBranchGrowth.Segments]) - 2) < .0001f, "Spread does not scale reach");
                checks += 5;
                recipe.irregularity = 0;
                settings.depth = 0;
                settings.angle = 85;
                settings.bend = 0;
                settings.upperAngleBias = 30;
                var rising = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                var low = rising[0].points[TreeBranchGrowth.Segments] - rising[0].points[0];
                var high = rising[rising.Count - 1].points[TreeBranchGrowth.Segments] - rising[rising.Count - 1].points[0];
                Require(Vector3.Angle(high, rising[rising.Count - 1].parentDirection) < Vector3.Angle(low, rising[0].parentDirection), "Upper limbs are not more upright");
                settings.upperAngleBias = 0;
                var level = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require(Mathf.Abs(Vector3.Angle(level[0].points[TreeBranchGrowth.Segments] - level[0].points[0], level[0].parentDirection) -
                    Vector3.Angle(level[level.Count - 1].points[TreeBranchGrowth.Segments] - level[level.Count - 1].points[0], level[level.Count - 1].parentDirection)) < .001f,
                    "Zero upper lift changed limb angle");
                recipe.crownWidth = 5;
                recipe.trunkRadius = .7f;
                settings.spread = 2;
                settings.thickness = .9f;
                settings.angle = 90;
                settings.droop = 1;
                var unweighted = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                settings.bend = 1;
                var weighted = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require(weighted[0].points[TreeBranchGrowth.Segments].y < unweighted[0].points[TreeBranchGrowth.Segments].y,
                    "Large limb does not droop");
                settings.thickness = .1f;
                var thin = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require(weighted[0].points[TreeBranchGrowth.Segments].y < thin[0].points[TreeBranchGrowth.Segments].y,
                    "Limb thickness has no effect on droop");
                settings.thickness = .9f;
                settings.spread = .5f;
                var shortLimbs = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require(weighted[0].points[TreeBranchGrowth.Segments].y < shortLimbs[0].points[TreeBranchGrowth.Segments].y,
                    "Limb length has no effect on droop");
                settings.spread = 2;
                checks += 5;
                settings.sharpness = 1;
                settings.bend = 0;
                Same(unweighted, TreeBranchGrowth.Generate(recipe, tip, 1, 0));
                settings.bend = 1;
                var sharp = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                settings.sharpness = 0;
                var smooth = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                Require(sharp[0].points[TreeBranchGrowth.Segments] == smooth[0].points[TreeBranchGrowth.Segments], "Sharpness changed the main limb endpoint");
                Require((sharp[0].points[TreeBranchGrowth.Segments / 2] - smooth[0].points[TreeBranchGrowth.Segments / 2]).sqrMagnitude > .0001f, "Sharpness has no effect");
                Require(MaxTurn(sharp[0]) > MaxTurn(smooth[0]) * 1.5f, "Sharpness did not concentrate the bend");
                checks += 4;
                recipe.taper = .95f;
                recipe.trunkRadius = .7f;
                settings.thickness = .9f;
                settings.limbs = 24;
                settings.depth = 3;
                foreach (float sharpness in new[] { 0f, .5f, 1f })
                {
                    settings.sharpness = sharpness;
                    var joined = TreeBranchGrowth.Generate(recipe, tip, 1, 0);
                    foreach (var limb in joined)
                    {
                        float expectedRadius = limb.parentIndex < 0
                            ? recipe.trunkRadius * (1 - recipe.taper * limb.attachment)
                            : TreeBranchGrowth.RadiusAt(joined[limb.parentIndex].radius, limb.attachment);
                        Require(Mathf.Abs(limb.parentRadius - expectedRadius) < .00001f, "Incorrect parent radius at socket");
                        Require(limb.radius > 0 && limb.radius <= expectedRadius * .901f, "Child is wider than its attachment");
                        if (limb.parentIndex >= 0)
                            Require(limb.points[0] == joined[limb.parentIndex].points[Mathf.RoundToInt(limb.attachment * TreeBranchGrowth.Segments)], "Disconnected branch origin");
                    }
                    checks++;
                }
            }
            var memory = new TreeRecipe();
            memory.Branches.limbs = 7;
            memory.species = "Oak"; memory.Branches.limbs = 11;
            memory.species = "Pine";
            memory.species = "Maple"; memory.Branches.limbs = 14;
            memory.species = "Pine";
            Require(memory.Branches.limbs == 7, "Species settings were overwritten");
            memory.species = "Maple";
            Require(memory.Branches.limbs == 14, "Maple settings were overwritten");
            checks += CheckTrunk();
            checks += CheckMeshes();
            checks += CheckForkRanges();
            Debug.Log($"[Terrainity] PASS: {checks + 2} branch, trunk and mesh checks — growth, determinism, twist, sharpness, connectivity, mesh reduction, foliage cards and UVs.");
        }

        static int CheckForkRanges()
        {
            int checks = 0;
            foreach (string species in new[] { "Pine", "Oak", "Birch", "Maple" })
            {
                var recipe = new TreeRecipe { species = species, variation = 0 };
                var settings = recipe.Branches;
                settings.limbs = 24; settings.minDepth = 1; settings.depth = 3;
                foreach (int count in new[] { 1, 2, 3, 4 })
                {
                    settings.subBranches = count;
                    var limbs = TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0);
                    Same(limbs, TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0));
                    Same(limbs, TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 4));
                    var depths = new HashSet<int>();
                    for (int i = 0; i < limbs.Count; i++)
                    {
                        var limb = limbs[i];
                        var directChildren = limbs.FindAll(l => l.parentIndex == i);
                        int children = directChildren.Count;
                        Require(children == (limb.terminal ? 0 : count), "Incorrect sub-branch count");
                        for (int child = 0; child < children; child++)
                            Require(Mathf.Abs(directChildren[child].attachment - (1 - child * .25f)) < .00001f,
                                "Sub-branches did not fill from tip toward root");
                        if (limb.parentIndex >= 0)
                            Require(limb.points[0] == limbs[limb.parentIndex].points[Mathf.RoundToInt(limb.attachment * TreeBranchGrowth.Segments)], "Fork missed parent socket");
                        if (limb.terminal)
                        {
                            Require(limb.level >= 1 && limb.level <= 3, "Random fork depth outside range");
                            depths.Add(limb.level);
                        }
                    }
                    Require(depths.Count > 1, "Depth range did not produce variation");
                    checks += 5;
                }
                settings.minDepth = settings.depth = 1;
                settings.subBranches = 1;
                var sparse = TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0);
                settings.subBranches = 4;
                var dense = TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0);
                for (int root = 0; root < 24; root++)
                    for (int point = 0; point <= TreeBranchGrowth.Segments; point++)
                    {
                        Require(sparse[root * 2].points[point] == dense[root * 5].points[point], "Density moved main limb");
                        Require(sparse[root * 2 + 1].points[point] == dense[root * 5 + 1].points[point], "Density moved existing child");
                    }
                checks += 2;
                settings.minDepth = settings.depth = 3;
                var maximum = TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0);
                Require(maximum.Count == 2040, "Incorrect maximum fork count");
                settings.minDepth = settings.depth = 0;
                Require(TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0).Count == 24, "Zero range still forked");
                checks += 2;
            }
            return checks;
        }

        static int CheckMeshes()
        {
            int checks = 0;
            using (var preview = new TreePreview())
            foreach (string species in new[] { "Pine", "Oak", "Birch", "Maple" })
            {
                var recipe = new TreeRecipe { species = species, trunkBend = .8f, trunkSharpness = 1, trunkBends = 3, trunkTwist = 240 };
                preview.Build(recipe, 0);
                int normalWood = preview.WoodTriangles;
                var foliage = preview.Meshes[1].vertices;
                recipe.trunkSides = 3; recipe.trunkSegments = 2; recipe.branchSides = 3; recipe.branchSegments = 2;
                preview.Build(recipe, 0);
                Require(preview.WoodTriangles < normalWood, "Low detail did not reduce wood triangles");
                var lowFoliage = preview.Meshes[1].vertices;
                Require(foliage.Length == lowFoliage.Length, "Mesh detail changed foliage count");
                for (int i = 0; i < foliage.Length; i++) Require(foliage[i] == lowFoliage[i], "Mesh detail moved foliage");
                foreach (var mesh in preview.Meshes)
                {
                    foreach (var p in mesh.vertices) Require(!float.IsNaN(p.sqrMagnitude) && !float.IsInfinity(p.sqrMagnitude), "Nonfinite mesh vertex");
                    foreach (var index in mesh.triangles) Require(index >= 0 && index < mesh.vertexCount, "Invalid mesh index");
                    var vertices = mesh.vertices; var indices = mesh.triangles;
                    for (int i = 0; i < indices.Length; i += 3)
                        Require(Vector3.Cross(vertices[indices[i + 1]] - vertices[indices[i]], vertices[indices[i + 2]] - vertices[indices[i]]).sqrMagnitude > 1e-20f, "Degenerate triangle");
                }
                var limbs = TreeBranchGrowth.Generate(recipe, new Vector3(recipe.lean * recipe.height * .18f, recipe.height, 0), 1, 0);
                int terminal = limbs.FindAll(l => l.terminal).Count;
                Require(preview.Meshes[1].vertexCount == terminal * 2 * recipe.foliageCards * 4, "Unexpected foliage card count");
                Require(preview.Meshes[1].uv.Length == preview.Meshes[1].vertexCount, "Missing foliage UVs");
                for (int parent = -1; parent < limbs.Count; parent++)
                {
                    var samples = TreePreview.RingSamples(2, limbs, parent);
                    foreach (var child in limbs)
                        if (child.parentIndex == parent) Require(samples.Exists(t => Mathf.Abs(t - child.attachment) < .00001f), "Missing socket ring");
                }
                int lowWood = preview.WoodTriangles;
                var wood = preview.Meshes[0];
                Require(wood.uv.Length == wood.vertexCount, "Missing bark UVs");
                foreach (var coordinate in wood.uv)
                    Require(!float.IsNaN(coordinate.sqrMagnitude) && !float.IsInfinity(coordinate.sqrMagnitude), "Invalid bark UV");
                Require(wood.vertices[0] == wood.vertices[recipe.trunkSides], "Bark seam vertices do not coincide");
                Require(wood.uv[0].x == 0 && wood.uv[recipe.trunkSides].x == 1, "Bark seam does not span the texture");
                var beforeTiling = wood.vertices;
                recipe.barkTilingAround = 4; recipe.barkTilingLength = 3;
                preview.Build(recipe, 0);
                Require(preview.WoodTriangles == lowWood, "Bark tiling changed polygon count");
                var afterTiling = preview.Meshes[0].vertices;
                for (int i = 0; i < beforeTiling.Length; i++) Require(beforeTiling[i] == afterTiling[i], "Bark tiling changed geometry");
                checks += 5;
                recipe.trunkSides = 12; recipe.trunkSegments = 48; recipe.branchSides = 10; recipe.branchSegments = 12;
                preview.Build(recipe, 0);
                Require(preview.WoodTriangles > normalWood, "High detail did not increase wood triangles");
                Debug.Log($"[Terrainity] {species} wood triangles: low {lowWood}, default {normalWood}, high {preview.WoodTriangles}; foliage {preview.FoliageTriangles}.");
                recipe.showFoliage = false;
                preview.Build(recipe, 0);
                Require(preview.FoliageTriangles == 0 && preview.Meshes.Count == 1, "Hidden foliage retained geometry");
                recipe.Branches.limbs = 0;
                preview.Build(recipe, 0);
                Require(preview.WoodTriangles > 0 && preview.Meshes.Count == 1, "Bare trunk failed");
                checks += 10;
            }
            return checks;
        }

        static int CheckTrunk()
        {
            var recipe = new TreeRecipe { variation = 0 };
            Vector3 tip = new Vector3(.2f, 7, 0);
            var straight = new TreeTrunkGrowth(recipe, tip);
            for (int i = 0; i <= TreeTrunkGrowth.Segments; i++)
                Require((straight.points[i] - tip * ((float)i / TreeTrunkGrowth.Segments)).sqrMagnitude < .0000001f,
                    "Zero trunk bend is not straight");
            int checks = 1;
            recipe.trunkBend = .75f;
            for (int bends = 1; bends <= 3; bends++)
                foreach (float twist in new[] { -360f, 0f, 360f })
                foreach (float sharpness in new[] { 0f, .5f, 1f })
                {
                    recipe.trunkBends = bends;
                    recipe.trunkTwist = twist;
                    recipe.trunkSharpness = sharpness;
                    var trunk = new TreeTrunkGrowth(recipe, tip);
                    Require(trunk.points[0] == Vector3.zero && trunk.points[TreeTrunkGrowth.Segments] == tip,
                        "Trunk bending moved its base or tip");
                    Require((trunk.points[8] - straight.points[8]).sqrMagnitude > .001f, "Trunk bend has no effect");
                    for (int i = 0; i <= TreeTrunkGrowth.Segments; i++)
                    {
                        Require(!float.IsNaN(trunk.points[i].sqrMagnitude) && !float.IsInfinity(trunk.points[i].sqrMagnitude),
                            "Non-finite trunk geometry");
                        Require(Mathf.Abs((trunk.frames[i] * Vector3.up).magnitude - 1) < .0001f, "Invalid trunk frame");
                    }
                    var limbs = TreeBranchGrowth.Generate(recipe, trunk, 1, 0);
                    foreach (var limb in limbs)
                    {
                        if (limb.parentIndex >= 0) continue;
                        trunk.Sample(limb.attachment, out var origin, out var frame);
                        Require((limb.points[0] - origin).sqrMagnitude < .0000001f, "Branch missed the curved trunk");
                        Require(Vector3.Dot(limb.parentDirection, frame * Vector3.up) > .9999f, "Branch socket lost trunk orientation");
                    }
                    Same(limbs, TreeBranchGrowth.Generate(recipe, tip, 1, 4));
                    checks += 5;
                }
            recipe.trunkBends = 1;
            recipe.trunkTwist = 0;
            recipe.trunkSharpness = 0;
            var smoothTrunk = new TreeTrunkGrowth(recipe, tip);
            recipe.trunkSharpness = 1;
            var angularTrunk = new TreeTrunkGrowth(recipe, tip);
            Require((smoothTrunk.points[12] - angularTrunk.points[12]).sqrMagnitude > .001f, "Trunk sharpness has no effect");
            Require(smoothTrunk.points[24] == angularTrunk.points[24], "Sharpness changed the trunk bend amplitude");
            Require(MaxTurn(new TreeBranchGrowth.Limb { points = angularTrunk.points }) >
                MaxTurn(new TreeBranchGrowth.Limb { points = smoothTrunk.points }) * 2, "Trunk sharpness did not concentrate the bend");
            recipe.trunkTwist = 180;
            var clockwise = new TreeTrunkGrowth(recipe, Vector3.up * 7);
            recipe.trunkTwist = -180;
            var counterclockwise = new TreeTrunkGrowth(recipe, Vector3.up * 7);
            Require(clockwise.points[12].z * counterclockwise.points[12].z < 0, "Twist direction does not reverse");
            recipe.trunkBend = 0;
            recipe.trunkTwist = 0;
            var noTwist = TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0);
            recipe.trunkTwist = 180;
            var twisted = TreeBranchGrowth.Generate(recipe, Vector3.up * 7, 1, 0);
            Require(noTwist[0].points[0] == twisted[0].points[0], "Twist moved a straight trunk attachment");
            Require((noTwist[0].points[TreeBranchGrowth.Segments] - twisted[0].points[TreeBranchGrowth.Segments]).sqrMagnitude > .001f,
                "Twist did not rotate branch placement");
            return checks + 6;
        }

        static float MaxTurn(TreeBranchGrowth.Limb limb)
        {
            float maximum = 0;
            for (int i = 1; i < limb.points.Length - 1; i++)
                maximum = Mathf.Max(maximum, Vector3.Angle(limb.points[i] - limb.points[i - 1], limb.points[i + 1] - limb.points[i]));
            return maximum;
        }

        static void Same(List<TreeBranchGrowth.Limb> a, List<TreeBranchGrowth.Limb> b)
        {
            Require(a.Count == b.Count, "Geometry counts differ");
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < a[i].points.Length; j++)
                    Require(a[i].points[j] == b[i].points[j], "Repeated geometry differs");
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Terrainity] FAIL: " + message);
        }
    }
}
