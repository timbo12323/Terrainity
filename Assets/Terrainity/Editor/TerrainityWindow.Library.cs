using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    // Generated prefab browsing and library actions.
    public sealed partial class TerrainityWindow
    {
        void BuildLibrary()
        {
            var toolbar = libraryPage.Q("libraryToolbar");
            toolbar.Clear();
            var search = new ToolbarSearchField(); search.AddToClassList("grow"); toolbar.Add(search);
            var filter = new PopupField<string>(new List<string> { "All types", "Trees", "Grass", "Rocks", "Bushes" }, 0); toolbar.Add(filter);
            var list = libraryPage.Q("libraryList");
            string[] paths = Array.Empty<string>();
            void ReloadPaths()
            {
                paths = AssetDatabase.IsValidFolder(GeneratedRoot)
                    ? AssetDatabase.FindAssets("t:Prefab l:TerrainityGenerated", new[] { GeneratedRoot })
                        .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
            }
            void Refresh()
            {
                list.Clear();
                // Typing filters the current snapshot instead of rescanning AssetDatabase per key.
                int shown = 0;
                foreach (var path in paths)
                {
                    if (filter.value != "All types" && !path.StartsWith(GeneratedRoot + "/" + filter.value + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Path.GetFileNameWithoutExtension(path).IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    shown++;
                    var row = Box(list, "library-item");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var field = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = false, value = prefab };
                    field.AddToClassList("grow");
                    field.RegisterValueChangedCallback(_ => field.SetValueWithoutNotify(prefab)); row.Add(field);
                    Action(row, "Locate", () => { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); });
                    Action(row, "Delete prefab", () =>
                    {
                        if (DeleteLibraryPrefab(prefab)) { ReloadPaths(); Refresh(); }
                    }).tooltip = "Move this prefab to the Recycle Bin. Shared generated resources are kept.";
                }
                if (shown == 0)
                {
                    var empty = Box(list, "card empty");
                    Text(empty, paths.Length == 0 ? "Your forest starts here." : "No matching prefabs.", "section-title");
                    Text(empty, paths.Length == 0 ? "Generate assets in the tree, grass or rock builder. Your saved prefabs will appear here for scene placement or Terrain painting." : "Try another search or asset type.");
                    Action(empty, "Open tree builder", () => { category = "Trees"; ShowTab(1); }, true);
                }
            }
            search.RegisterValueChangedCallback(_ => Refresh()); filter.RegisterValueChangedCallback(_ => Refresh());
            Action(toolbar, "Refresh", () => { ReloadPaths(); Refresh(); });
            ReloadPaths(); Refresh();
        }

        bool DeleteLibraryPrefab(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null || !path.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal)
                || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.GetLabels(prefab).Contains("TerrainityGenerated"))
            {
                EditorUtility.DisplayDialog("Cannot delete prefab", "This entry is no longer a generated Terrainity prefab. Refresh the library and try again.", "OK");
                return false;
            }
            if (!EditorUtility.DisplayDialog("Delete generated prefab?",
                $"Move {prefab.name} to the Recycle Bin?\n\n{path}\n\nOnly this prefab is removed. Meshes, materials, textures, recipes and sibling prefabs are kept.\n\nTerrains or scenes using this prefab will lose its asset reference. Remove it from their tree palettes first if needed. This cannot be undone with Unity Undo; restore the prefab and its .meta file from the Recycle Bin to recover it.",
                "Delete prefab", "Cancel")) return false;
            try
            {
                if (!AssetDatabase.MoveAssetToTrash(path))
                {
                    EditorUtility.DisplayDialog("Cannot delete prefab", "Unity could not move the prefab to the Recycle Bin. Check whether the asset is read-only or locked.", "OK");
                    return false;
                }
                lastExportPrefabs = (lastExportPrefabs ?? Array.Empty<GameObject>()).Where(p => p != null && p != prefab).ToArray();
                lastRockPrefabs = (lastRockPrefabs ?? Array.Empty<GameObject>()).Where(p => p != null && p != prefab).ToArray();
                exportStatus = "Deleted " + Path.GetFileNameWithoutExtension(path) + ". Shared generated resources were kept.";
                return true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Cannot delete prefab", e.Message, "OK");
                return false;
            }
        }
    }
}
