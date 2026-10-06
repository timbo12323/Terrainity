using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
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
            if (g == null) return null;
            // Unity returns a new array for each key accessor.
            var colorKeys = g.colorKeys;
            var alphaKeys = g.alphaKeys;
            var result = new TreeGradientJson { colors = new ColorStop[colorKeys.Length], alpha = new AlphaStop[alphaKeys.Length], mode = g.mode };
            for (int i = 0; i < result.colors.Length; i++) result.colors[i] = new ColorStop { color = colorKeys[i].color, time = colorKeys[i].time };
            for (int i = 0; i < result.alpha.Length; i++) result.alpha[i] = new AlphaStop { alpha = alphaKeys[i].alpha, time = alphaKeys[i].time };
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
            copy.customBranches = recipe.Branches.Copy();
            string barkPath = UnityEditor.AssetDatabase.GetAssetPath(recipe.barkTexture);
            string leafPath = UnityEditor.AssetDatabase.GetAssetPath(recipe.foliageTexture);
            return new TreeRecipeJson { settings = copy, family = recipe.Profile.Copy(), sibling = variant,
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
            if (result.foliageSizeMin <= 0) result.foliageSizeMin = result.foliageSize;
            result.useFamilyProfile = true;
            result.familyProfile = family.Copy();
            result.species = family.name;
            result.customBranches = result.customBranches ?? family.branches.Copy();
            if (barkGradient != null) result.barkGradient = barkGradient.ToGradient();
            if (foliageGradient != null) result.foliageGradient = foliageGradient.ToGradient();
            warning = "";
            result.barkTexture = Texture(barkTextureGuid, barkTexturePath, ref warning);
            result.foliageTexture = Texture(foliageTextureGuid, foliageTexturePath, ref warning);
            return result;
        }

        static Texture2D Texture(string guid, string path, ref string warning)
        {
            string resolved = string.IsNullOrEmpty(guid) ? "" : UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var texture = string.IsNullOrEmpty(resolved) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(resolved);
            if (texture == null && !string.IsNullOrEmpty(path)) texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
        static readonly Dictionary<string, (long modified, long length, TreeFamilyJson document)> familyCache
            = new Dictionary<string, (long, long, TreeFamilyJson)>(StringComparer.Ordinal);
        static readonly Dictionary<Type, System.Reflection.FieldInfo[]> numericFields
            = new Dictionary<Type, System.Reflection.FieldInfo[]>();

        [Serializable]
        internal struct DocumentHeader
        {
            public string format, kind;
            public int version;
        }

        internal static void ReloadFamilies(bool force = false)
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
                    // Reopening builder controls should not deserialize unchanged family files.
                    var info = new System.IO.FileInfo(path);
                    if (!familyCache.TryGetValue(path, out var cached) || force || cached.modified != info.LastWriteTimeUtc.Ticks || cached.length != info.Length)
                    {
                        cached = (info.LastWriteTimeUtc.Ticks, info.Length, ReadFamily(path));
                        familyCache[path] = cached;
                    }
                    var doc = cached.document;
                    if (!ids.Add(doc.profile.id)) throw new System.IO.InvalidDataException("Duplicate family id: " + doc.profile.id);
                    int index = Families.FindIndex(f => string.Equals(f.profile.id, doc.profile.id, StringComparison.OrdinalIgnoreCase));
                    if (index < 0) Families.Add(doc); else Families[index] = doc;
                }
                catch (Exception e) { Debug.LogWarning("[Terrainity] Skipped family " + path + ": " + e.Message); }
            }
            var activePaths = new HashSet<string>(paths, StringComparer.Ordinal);
            foreach (string path in new List<string>(familyCache.Keys))
                if (!activePaths.Contains(path)) familyCache.Remove(path);
        }
        internal static string ReadText(string path)
        {
            if (new System.IO.FileInfo(path).Length > 1024 * 1024) throw new System.IO.InvalidDataException("JSON file exceeds 1 MB.");
            return System.IO.File.ReadAllText(path);
        }
        internal static T Read<T>(string path) where T : class
        {
            string json = ReadText(path);
            // Root fields must supply the envelope; nested field names cannot stand in for it.
            var header = JsonUtility.FromJson<DocumentHeader>(json);
            if (string.IsNullOrEmpty(header.format) || header.version != 1)
                throw new System.IO.InvalidDataException("Missing or unsupported JSON format or version.");
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
        internal static void Write(string path, object document, bool refreshAssets = true)
        {
            string temp = path + ".tmp";
            try
            {
                System.IO.File.WriteAllText(temp, JsonUtility.ToJson(document, true));
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                else System.IO.File.Move(temp, path);
            }
            finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
            if (refreshAssets && System.IO.Path.GetFullPath(path).StartsWith(System.IO.Path.GetFullPath("Assets") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) UnityEditor.AssetDatabase.Refresh();
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
            => ValidateNumbers(data, 0);

        static void ValidateNumbers(object data, int depth)
        {
            if (data == null || data is string || data is UnityEngine.Object || data is Gradient) return;
            if (depth > 64) throw new System.IO.InvalidDataException("JSON settings are nested too deeply.");
            if (data is float number)
            {
                if (!float.IsFinite(number) || Mathf.Abs(number) > 10000)
                    throw new System.IO.InvalidDataException("Invalid numeric setting.");
                return;
            }
            var type = data.GetType();
            if (type.IsPrimitive || type.IsEnum) return;
            // Reflection alone sees collection metadata, not the elements that need checking.
            if (data is System.Collections.IEnumerable items)
            {
                foreach (var item in items) ValidateNumbers(item, depth + 1);
                return;
            }
            if (!numericFields.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                numericFields.Add(type, fields);
            }
            foreach (var field in fields)
                if (!Attribute.IsDefined(field, typeof(NonSerializedAttribute))) ValidateNumbers(field.GetValue(data), depth + 1);
        }
    }
}
