using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    // Recipe files, family imports and tree export actions.
    public sealed partial class TerrainityWindow
    {
        void GenerateTreeFamily()
        {
            try
            {
                var result = TreeExporter.Generate(recipe, exportTrunkCollider, lodSettings: exportLods);
                lastExportFolder = result.folder; lastExportPrefabs = result.prefabs;
                exportStatus = $"Generated {result.prefabs.Length} prefabs in {result.folder}.";
                EditorGUIUtility.PingObject(result.prefabs[0]);
            }
            catch (OperationCanceledException) { exportStatus = "Generation cancelled. No partial export was kept."; }
            catch (Exception e) { exportStatus = "Generation failed: " + e.Message; Debug.LogException(e); }
            ShowTab(1);
        }

        void ExportTreePackage()
        {
            string path = EditorUtility.SaveFilePanel("Export tree family", "", Path.GetFileName(lastExportFolder) + ".unitypackage", "unitypackage");
            if (string.IsNullOrEmpty(path)) return;
            try { TreeExporter.ExportPackage(lastExportFolder, path); exportStatus = "Unity package saved to " + path; ShowTab(1); }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot export package", e.Message, "OK"); }
        }

        void SaveRecipeJson()
        {
            string path = EditorUtility.SaveFilePanel("Save tree recipe", "", recipe.assetName + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { TreeJsonStorage.Write(path, TreeRecipeJson.From(recipe, variant)); }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot save recipe", e.Message, "OK"); }
        }

        void LoadRecipeJson()
        {
            string path = EditorUtility.OpenFilePanel("Load tree recipe", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var doc = TreeJsonStorage.Read<TreeRecipeJson>(path);
                var loaded = doc.Restore(out var warning);
                RecordSettingsUndo();
                recipe = loaded; variant = Mathf.Clamp(doc.sibling, 0, recipe.variantCount - 1); ShowTab(1);
                if (!string.IsNullOrEmpty(warning)) EditorUtility.DisplayDialog("Recipe loaded", warning, "OK");
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot load recipe", e.Message, "OK"); }
        }

        void SaveFamilyJson()
        {
            Directory.CreateDirectory(TreeJsonStorage.FamilyFolder);
            string path = EditorUtility.SaveFilePanel("Save tree family — filename becomes the family name", TreeJsonStorage.FamilyFolder, "New family.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var profile = recipe.Profile.Copy();
                profile.name = Path.GetFileNameWithoutExtension(path);
                profile.id = profile.name.ToLowerInvariant().Replace(' ', '-');
                profile.branches = recipe.Branches.Copy();
                var defaults = TreeRecipeJson.From(recipe, 0); defaults.family = profile;
                TreeJsonStorage.Write(path, new TreeFamilyJson { profile = profile, defaults = defaults });
                TreeJsonStorage.ReloadFamilies(); ShowTab(1);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot save family", e.Message, "OK"); }
        }

        void ImportFamilyJson()
        {
            string path = EditorUtility.OpenFilePanel("Import tree family", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var doc = TreeJsonStorage.ReadFamily(path);
                TreeJsonStorage.ReloadFamilies();
                if (TreeJsonStorage.Families.Any(f => string.Equals(f.profile.id, doc.profile.id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("A family with id '" + doc.profile.id + "' already exists. Give the imported JSON a unique profile.id.");
                Directory.CreateDirectory(TreeJsonStorage.FamilyFolder);
                string filename = string.Concat(doc.profile.id.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
                string destination = AssetDatabase.GenerateUniqueAssetPath(TreeJsonStorage.FamilyFolder + "/" + filename + ".json");
                TreeJsonStorage.Write(destination, doc); ShowTab(1);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot import family", e.Message, "OK"); }
        }

    }
}
