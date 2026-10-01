using System;
using UnityEngine;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class TreeRecipe
    {
        public string assetName = "Woodland Pine";
        public string species = "Pine";
        public TreeFamilyProfile familyProfile;
        public bool useFamilyProfile;
        public BranchSettings customBranches;
        public TreeFamilyProfile Profile => useFamilyProfile && familyProfile != null ? familyProfile : TreeFamilyProfile.Legacy(species);
        public int seed = 1842;
        public float height = 7f;
        public float trunkRadius = .2f;
        public bool rootsEnabled = false;
        public int rootCount = 6;
        public int rootForks = 0;
        public int rootMinForkDepth = 1;
        public int rootMaxForkDepth = 1;
        public float rootBend = 0;
        public float rootSharpness = 0;
        public float rootSpread = 1.5f;
        public float rootThickness = .65f;
        public float rootTaper = .95f;
        public float rootAttachmentHeight = .4f;
        public float rootDepth = .25f;
        public float floorHeight = 0;
        public float taper = .7f;
        public float lean = .12f;
        public float trunkBend;
        public int trunkBends = 1;
        public float trunkSharpness;
        public float trunkTwist;
        public float crownWidth = 3.8f;
        public float crownStart = .25f;
        public int layers = 6;
        public int trunkSides = 8;
        public int trunkSegments = 12;
        public int branchSides = 6;
        public int branchSegments = 4;
        public float barkSurfaceDetail = 0;
        [NonSerialized] internal float barkDetailLodScale = 1;
        public float barkDetailScale = 1;
        public int barkDetailResolution = 2;
        public bool simplifyWood = false;
        public float simplificationTolerance = .002f;
        public int foliageCards = 6;
        public int foliageSubdivisions = 1;
        public float foliageBend = 0;
        public float foliageSize = 1;
        public float foliageWidthScale = 1;
        public float foliageHeightScale = 1;
        public float foliageSpread = .5f;
        public float foliageCutoff = .5f;
        public float foliageFeathering = 0;
        public float foliageSoftness = 0;
        public bool foliageAlphaToCoverage = false;
        public bool foliageTransmissionEnabled = false;
        public float foliageTransmission = .5f;
        public float foliageOcclusion = .6f;
        public float barkOcclusion = .35f;
        public Texture2D foliageTexture;
        public bool foliageStemAttached;
        public Vector2 foliageStemPivot = new Vector2(.5f, 0);
        public Vector3 foliageRotation;
        public float foliageFanAngle = 20;
        public float irregularity = .22f;
        public Color bark = new Color(.32f, .21f, .12f);
        public Texture2D barkTexture;
        public float barkTilingAround = 1;
        public float barkTilingLength = 1;
        public Color leaves = new Color(.27f, .45f, .20f);
        public float foliageRoughness = 1;
        public bool foliageHighlights;
        public bool foliageReflections;
        public float barkRoughness = .88f;
        public bool barkHighlights = true;
        public bool barkReflections = true;
        public bool barkGradientEnabled;
        public bool foliageGradientEnabled;
        public Gradient barkGradient = TintGradient(new Color(.20f, .12f, .07f), new Color(.58f, .43f, .25f));
        public Gradient foliageGradient = TintGradient(new Color(.12f, .3f, .08f), new Color(.8f, .65f, .16f));
        public FoliageTintMode foliageTintMode = FoliageTintMode.PerCard;

        static Gradient TintGradient(Color from, Color to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }
        public int variantCount = 1;
        public float variation = .12f;
        public bool showFoliage = true;
        public BranchSettings pineBranches = BranchSettings.ForSpecies("Pine");
        public BranchSettings oakBranches = BranchSettings.ForSpecies("Oak");
        public BranchSettings birchBranches = BranchSettings.ForSpecies("Birch");
        public BranchSettings mapleBranches = BranchSettings.ForSpecies("Maple");

        public BranchSettings Branches
        {
            get
            {
                if (useFamilyProfile && familyProfile != null) return customBranches ?? (customBranches = BranchSettings.ForSpecies(species));
                if (species == "Oak") return oakBranches ?? (oakBranches = BranchSettings.ForSpecies("Oak"));
                if (species == "Birch") return birchBranches ?? (birchBranches = BranchSettings.ForSpecies("Birch"));
                if (species == "Maple") return mapleBranches ?? (mapleBranches = BranchSettings.ForSpecies("Maple"));
                return pineBranches ?? (pineBranches = BranchSettings.ForSpecies("Pine"));
            }
        }

        public void ResetBranches()
        {
            if (useFamilyProfile && familyProfile != null) { customBranches = JsonUtility.FromJson<BranchSettings>(JsonUtility.ToJson(familyProfile.branches)); return; }
            if (species == "Oak") oakBranches = BranchSettings.ForSpecies(species);
            else if (species == "Birch") birchBranches = BranchSettings.ForSpecies(species);
            else if (species == "Maple") mapleBranches = BranchSettings.ForSpecies(species);
            else pineBranches = BranchSettings.ForSpecies(species);
        }
    }

    internal enum FoliageTintMode { Height, PerCard, StemToTip }

    [Serializable]
    internal sealed class BranchSettings
    {
        public int limbs = 16;
        public int depth = 3;
        // -1 migrates existing recipes to a fixed range at their saved depth.
        public int minDepth = -1;
        public int subBranches = 2;
        public float spread = 1;
        public float bend = .25f;
        public float sharpness = 0;
        public float angle = 75;
        public float upperAngleBias = 20;
        public float droop = .4f;
        public float thickness = .4f;
        public float taper = .8f;
        public float attachmentThickness = 1;

        public static BranchSettings ForSpecies(string species)
        {
            if (species == "Oak") return new BranchSettings { limbs = 9, depth = 3, spread = 1.15f, bend = .55f, angle = 58, upperAngleBias = 24, droop = .65f, thickness = .65f };
            if (species == "Birch") return new BranchSettings { limbs = 13, depth = 3, spread = .7f, bend = .65f, angle = 42, upperAngleBias = 18, droop = .8f, thickness = .3f };
            if (species == "Maple") return new BranchSettings { limbs = 9, depth = 3, spread = .9f, bend = .45f, angle = 52, upperAngleBias = 23, droop = .5f, thickness = .55f };
            return new BranchSettings();
        }
    }
}

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class TreeFamilyProfile
    {
        public string id = "custom";
        public string name = "Custom";
        public string description = "Custom tree family";
        public int limbsPerWhorl = 1;
        public float whorlRotation = 31, branchRotation = 137.508f;
        public float crownTipScale = .55f, lift = .38f, liftExponent = 2;
        public float forkAngle = 38, childLengthScale = .62f, foliageAspect = 1;
        // Zero preserves the original crown endpoint for older JSON recipes.
        public float crownEnd = 0;
        public TreeFoliageForm foliageForm = TreeFoliageForm.Broadleaf;
        public BranchSettings branches = new BranchSettings();

        internal static TreeFamilyProfile Legacy(string species)
        {
            var p = new TreeFamilyProfile { id = species.ToLowerInvariant(), name = species, branches = BranchSettings.ForSpecies(species) };
            if (species == "Pine") { p.limbsPerWhorl = 4; p.crownTipScale = .18f; p.lift = .22f; p.forkAngle = 48; p.childLengthScale = .48f; p.foliageAspect = .65f; }
            if (species == "Birch") { p.lift = -.42f; p.liftExponent = 3; p.forkAngle = 27; p.childLengthScale = .55f; }
            if (species == "Maple") { p.lift = .23f; p.forkAngle = 65; p.childLengthScale = .45f; }
            return p;
        }
    }

    internal enum TreeFoliageForm { Broadleaf, Needles, PalmFrond }

    [Serializable]
    internal sealed class TreeGradientJson
    {
        [Serializable] internal struct ColorStop { public Color color; public float time; }
        [Serializable] internal struct AlphaStop { public float alpha, time; }
        public ColorStop[] colors;
        public AlphaStop[] alpha;
        public GradientMode mode;
        internal static TreeGradientJson From(Gradient g)
        {
            var result = new TreeGradientJson { colors = new ColorStop[g.colorKeys.Length], alpha = new AlphaStop[g.alphaKeys.Length], mode = g.mode };
            for (int i = 0; i < result.colors.Length; i++) result.colors[i] = new ColorStop { color = g.colorKeys[i].color, time = g.colorKeys[i].time };
            for (int i = 0; i < result.alpha.Length; i++) result.alpha[i] = new AlphaStop { alpha = g.alphaKeys[i].alpha, time = g.alphaKeys[i].time };
            return result;
        }
        internal Gradient ToGradient()
        {
            if (colors == null || colors.Length < 2 || colors.Length > 8 || alpha == null || alpha.Length < 2 || alpha.Length > 8)
                throw new System.IO.InvalidDataException("Gradients require 2–8 color and alpha keys.");
            var colorKeys = new GradientColorKey[colors.Length];
            var alphaKeys = new GradientAlphaKey[alpha.Length];
            for (int i = 0; i < colors.Length; i++) { TreeJsonStorage.ValidateNumbers(colors[i]); colorKeys[i] = new GradientColorKey(colors[i].color, colors[i].time); }
            for (int i = 0; i < alpha.Length; i++) { TreeJsonStorage.ValidateNumbers(alpha[i]); alphaKeys[i] = new GradientAlphaKey(alpha[i].alpha, alpha[i].time); }
            var g = new Gradient { mode = mode }; g.SetKeys(colorKeys, alphaKeys); return g;
        }
    }

    [Serializable]
    internal sealed class TreeRecipeSettings
    {
        public string assetName = "Woodland Pine";
        public string species = "Pine";
        public BranchSettings customBranches;
        public int seed = 1842;
        public float height = 7f;
        public float trunkRadius = .2f;
        public bool rootsEnabled = false;
        public int rootCount = 6;
        public int rootForks = 0;
        public int rootMinForkDepth = 1;
        public int rootMaxForkDepth = 1;
        public float rootBend = 0;
        public float rootSharpness = 0;
        public float rootSpread = 1.5f;
        public float rootThickness = .65f;
        public float rootTaper = .95f;
        public float rootAttachmentHeight = .4f;
        public float rootDepth = .25f;
        public float floorHeight = 0;
        public float taper = .7f;
        public float lean = .12f;
        public float trunkBend = 0;
        public int trunkBends = 1;
        public float trunkSharpness = 0;
        public float trunkTwist = 0;
        public float crownWidth = 3.8f;
        public float crownStart = .25f;
        public int layers = 6;
        public int trunkSides = 8;
        public int trunkSegments = 12;
        public int branchSides = 6;
        public int branchSegments = 4;
        public float barkSurfaceDetail = 0;
        public float barkDetailScale = 1;
        public int barkDetailResolution = 2;
        public bool simplifyWood = false;
        public float simplificationTolerance = .002f;
        public int foliageCards = 6;
        public int foliageSubdivisions = 1;
        public float foliageBend = 0;
        public float foliageSize = 1;
        public float foliageWidthScale = 1;
        public float foliageHeightScale = 1;
        public float foliageSpread = .5f;
        public float foliageCutoff = .5f;
        public float foliageFeathering = 0;
        public float foliageSoftness = 0;
        public bool foliageAlphaToCoverage = false;
        public bool foliageTransmissionEnabled = false;
        public float foliageTransmission = .5f;
        public float foliageOcclusion = .6f;
        public float barkOcclusion = .35f;
        public bool foliageStemAttached = false;
        public Vector2 foliageStemPivot = new Vector2(.5f, 0);
        public Vector3 foliageRotation = Vector3.zero;
        public float foliageFanAngle = 20;
        public float irregularity = .22f;
        public Color bark = new Color(.32f, .21f, .12f);
        public float barkTilingAround = 1;
        public float barkTilingLength = 1;
        public Color leaves = new Color(.27f, .45f, .20f);
        public float foliageRoughness = 1;
        public bool foliageHighlights = false;
        public bool foliageReflections = false;
        public float barkRoughness = .88f;
        public bool barkHighlights = true;
        public bool barkReflections = true;
        public bool barkGradientEnabled = false;
        public bool foliageGradientEnabled = false;
        public FoliageTintMode foliageTintMode = FoliageTintMode.PerCard;
        public int variantCount = 1;
        public float variation = .12f;
        public bool showFoliage = true;
        public BranchSettings pineBranches = BranchSettings.ForSpecies("Pine");
        public BranchSettings oakBranches = BranchSettings.ForSpecies("Oak");
        public BranchSettings birchBranches = BranchSettings.ForSpecies("Birch");
        public BranchSettings mapleBranches = BranchSettings.ForSpecies("Maple");
    }

    [Serializable]
    internal sealed class TreeRecipeJson
    {
        public string format = "terrainity.recipe";
        public int version = 1;
        public TreeRecipeSettings settings;
        public TreeFamilyProfile family;
        public TreeGradientJson barkGradient, foliageGradient;
        public string barkTextureGuid, barkTexturePath, foliageTextureGuid, foliageTexturePath;
        public int sibling;

        internal static TreeRecipeJson From(TreeRecipe recipe, int variant)
        {
            var copy = JsonUtility.FromJson<TreeRecipeSettings>(JsonUtility.ToJson(recipe));
            copy.customBranches = JsonUtility.FromJson<BranchSettings>(JsonUtility.ToJson(recipe.Branches));
            string barkPath = UnityEditor.AssetDatabase.GetAssetPath(recipe.barkTexture);
            string leafPath = UnityEditor.AssetDatabase.GetAssetPath(recipe.foliageTexture);
            return new TreeRecipeJson { settings = copy, family = recipe.Profile, sibling = variant,
                barkGradient = TreeGradientJson.From(recipe.barkGradient), foliageGradient = TreeGradientJson.From(recipe.foliageGradient),
                barkTexturePath = barkPath, barkTextureGuid = UnityEditor.AssetDatabase.AssetPathToGUID(barkPath),
                foliageTexturePath = leafPath, foliageTextureGuid = UnityEditor.AssetDatabase.AssetPathToGUID(leafPath) };
        }

        internal TreeRecipe Restore(out string warning)
        {
            if (format != "terrainity.recipe" || version != 1 || settings == null || family == null)
                throw new System.IO.InvalidDataException("Expected a Terrainity recipe JSON, version 1.");
            TreeJsonStorage.ValidateFamily(family);
            TreeJsonStorage.ValidateNumbers(settings);
            if (settings.height < 2 || settings.height > 18 || settings.trunkRadius < .06f || settings.trunkRadius > .7f || settings.crownWidth < 1 || settings.crownWidth > 9 || settings.variantCount < 1 || settings.variantCount > 12)
                throw new System.IO.InvalidDataException("Recipe dimensions or family size are outside the builder limits.");
            var result = JsonUtility.FromJson<TreeRecipe>(JsonUtility.ToJson(settings));
            result.useFamilyProfile = true;
            result.familyProfile = JsonUtility.FromJson<TreeFamilyProfile>(JsonUtility.ToJson(family));
            result.species = family.name;
            result.customBranches = result.customBranches ?? JsonUtility.FromJson<BranchSettings>(JsonUtility.ToJson(family.branches));
            result.barkGradient = barkGradient == null ? new TreeRecipe().barkGradient : barkGradient.ToGradient();
            result.foliageGradient = foliageGradient == null ? new TreeRecipe().foliageGradient : foliageGradient.ToGradient();
            warning = "";
            result.barkTexture = Texture(barkTextureGuid, barkTexturePath, ref warning);
            result.foliageTexture = Texture(foliageTextureGuid, foliageTexturePath, ref warning);
            return result;
        }

        static Texture2D Texture(string guid, string path, ref string warning)
        {
            string resolved = string.IsNullOrEmpty(guid) ? "" : UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(string.IsNullOrEmpty(resolved) ? path ?? "" : resolved);
            if (texture == null && (!string.IsNullOrEmpty(guid) || !string.IsNullOrEmpty(path))) warning += "Missing texture: " + path + ". Using built-in texture.\n";
            return texture;
        }
    }

    [Serializable]
    internal sealed class TreeFamilyJson
    {
        public string format = "terrainity.family";
        public int version = 1;
        public TreeFamilyProfile profile;
        public TreeRecipeJson defaults;
    }

    internal static class TreeJsonStorage
    {
        internal const string FamilyFolder = "Assets/Terrainity/Families";
        internal static readonly System.Collections.Generic.List<TreeFamilyJson> Families = new System.Collections.Generic.List<TreeFamilyJson>();
        internal static void ReloadFamilies()
        {
            Families.Clear();
            foreach (string species in new[] { "Pine", "Oak", "Birch", "Maple" })
                Families.Add(new TreeFamilyJson { profile = TreeFamilyProfile.Legacy(species) });
            if (!System.IO.Directory.Exists(FamilyFolder)) return;
            var paths = System.IO.Directory.GetFiles(FamilyFolder, "*.json", System.IO.SearchOption.AllDirectories);
            System.Array.Sort(paths, StringComparer.Ordinal);
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                try
                {
                    var doc = ReadFamily(path);
                    if (!ids.Add(doc.profile.id)) throw new System.IO.InvalidDataException("Duplicate family id: " + doc.profile.id);
                    int index = Families.FindIndex(f => f.profile.id == doc.profile.id);
                    if (index < 0) Families.Add(doc); else Families[index] = doc;
                }
                catch (Exception e) { Debug.LogWarning("[Terrainity] Skipped family " + path + ": " + e.Message); }
            }
        }
        internal static T Read<T>(string path) where T : class
        {
            if (new System.IO.FileInfo(path).Length > 1024 * 1024) throw new System.IO.InvalidDataException("JSON file exceeds 1 MB.");
            string json = System.IO.File.ReadAllText(path);
            // Require explicit version/type instead of accepting constructor defaults for unrelated JSON.
            if (!json.Contains("\"format\"") || !json.Contains("\"version\"")) throw new System.IO.InvalidDataException("Missing JSON format or version.");
            return JsonUtility.FromJson<T>(json) ?? throw new System.IO.InvalidDataException("Empty JSON document.");
        }
        internal static TreeFamilyJson ReadFamily(string path)
        {
            var doc = Read<TreeFamilyJson>(path);
            if (doc.format != "terrainity.family" || doc.version != 1 || doc.profile == null) throw new System.IO.InvalidDataException("Expected a Terrainity family JSON, version 1.");
            ValidateFamily(doc.profile);
            if (doc.defaults != null) doc.defaults.Restore(out _);
            return doc;
        }
        internal static void Write(string path, object document)
        {
            string temp = path + ".tmp";
            try
            {
                System.IO.File.WriteAllText(temp, JsonUtility.ToJson(document, true));
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                else System.IO.File.Move(temp, path);
            }
            finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
            if (System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath("Assets") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) UnityEditor.AssetDatabase.Refresh();
        }
        internal static void ValidateFamily(TreeFamilyProfile p)
        {
            if (string.IsNullOrWhiteSpace(p.id) || string.IsNullOrWhiteSpace(p.name) || p.branches == null) throw new System.IO.InvalidDataException("Family requires id, name and branches.");
            ValidateNumbers(p);
            if (p.crownEnd < 0 || p.crownEnd > 1 || !Enum.IsDefined(typeof(TreeFoliageForm), p.foliageForm))
                throw new System.IO.InvalidDataException("Unsupported crown endpoint or foliage form.");
            if (p.limbsPerWhorl < 1 || p.limbsPerWhorl > 12 || p.crownTipScale < .01f || p.crownTipScale > 2 || p.liftExponent < 1 || p.liftExponent > 8 || p.childLengthScale < .05f || p.childLengthScale > 1 || p.foliageAspect < .1f || p.foliageAspect > 4 || p.forkAngle < 0 || p.forkAngle > 180)
                throw new System.IO.InvalidDataException("Family growth values are outside supported ranges.");
        }
        internal static void ValidateNumbers(object data)
        {
            if (data == null || data is string || data is UnityEngine.Object || data is Gradient) return;
            foreach (var field in data.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                object value = field.GetValue(data);
                if (value is float f && (float.IsNaN(f) || float.IsInfinity(f) || Mathf.Abs(f) > 10000)) throw new System.IO.InvalidDataException("Invalid value for " + field.Name);
                if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType != typeof(string)) ValidateNumbers(value);
            }
        }
    }
}
