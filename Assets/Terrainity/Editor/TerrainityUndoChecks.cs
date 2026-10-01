using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    internal static class TerrainityUndoChecks
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Get<T>(TerrainityWindow window, string field) => (T)typeof(TerrainityWindow).GetField(field, Flags).GetValue(window);
        static void Set(TerrainityWindow window, string field, object value) => typeof(TerrainityWindow).GetField(field, Flags).SetValue(window, value);

        [MenuItem("Tools/Terrainity/Validate Settings Undo")]
        static void Run()
        {
            TerrainityWindow window = null;
            var previousFocus = EditorWindow.focusedWindow;
            PreviewPreferences.instance.Restore(out var savedLights, out var savedEnvironment, out bool visible);
            try
            {
                window = ScriptableObject.CreateInstance<TerrainityWindow>();
                // Test a separate window and restore the saved preview preferences afterwards.
                void Check(string name, Action edit, Func<string> read)
                {
                    string before = read();
                    window.RecordSettingsUndo(); edit(); Undo.FlushUndoRecordObjects();
                    string after = read();
                    if (before == after) throw new Exception(name + ": test made no change");
                    Undo.PerformUndo();
                    if (read() != before) throw new Exception(name + ": undo did not restore settings");
                    Undo.PerformRedo();
                    if (read() != after) throw new Exception(name + ": redo did not restore settings");
                }
                Check("Tree recipe", () => Get<TreeRecipe>(window, "recipe").height += 2, () => JsonUtility.ToJson(Get<TreeRecipe>(window, "recipe")));
                Check("Tree gradient", () => Get<TreeRecipe>(window, "recipe").barkGradient.SetKeys(new[] { new GradientColorKey(Color.magenta, 0), new GradientColorKey(Color.cyan, 1) }, new[] { new GradientAlphaKey(1, 0) }), () => JsonUtility.ToJson(Get<TreeRecipe>(window, "recipe")));
                Check("Rock recipe", () => Get<RockRecipe>(window, "rockRecipe").ApplyPreset("Low-poly crystal"), () => JsonUtility.ToJson(Get<RockRecipe>(window, "rockRecipe")));
                Check("Light creation", () => { var lights = Get<PreviewLightingSettings>(window, "previewLighting"); lights.lights.Clear(); lights.independentPositions = true; lights.AddAt(144); }, () => JsonUtility.ToJson(Get<PreviewLightingSettings>(window, "previewLighting")));
                Check("Light deletion", () => Get<PreviewLightingSettings>(window, "previewLighting").lights.Clear(), () => JsonUtility.ToJson(Get<PreviewLightingSettings>(window, "previewLighting")));
                Check("Environment", () => { var env = Get<PreviewEnvironmentSettings>(window, "previewEnvironment"); env.background = Color.magenta; env.showGrid = !env.showGrid; env.skybox = null; env.useSkybox = false; }, () => JsonUtility.ToJson(Get<PreviewEnvironmentSettings>(window, "previewEnvironment")));
                Check("Export settings", () => Get<TreeLodSettings>(window, "exportLods").enabled = !Get<TreeLodSettings>(window, "exportLods").enabled, () => JsonUtility.ToJson(Get<TreeLodSettings>(window, "exportLods")));
                Check("Preview camera", () => Set(window, "previewOrbit", new Vector3(111, 22, .7f)), () => Get<Vector3>(window, "previewOrbit").ToString());
                Check("Panel size", () => Set(window, "parameterWidth", 444f), () => Get<float>(window, "parameterWidth").ToString());

                window.position = new Rect(100, 100, 300, 180);
                window.ShowPopup();
                typeof(TerrainityWindow).GetMethod("InstallUndoInput", Flags).Invoke(window, null);
                float beforeDrag = Get<TreeRecipe>(window, "recipe").height;
                using (var down = MouseDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 })) window.rootVisualElement.SendEvent(down);
                if (!Get<bool>(window, "undoDragging")) throw new Exception("Mouse down did not reach detached test root; panel=" + (window.rootVisualElement.panel != null));
                for (int i = 1; i <= 5; i++)
                {
                    using (var move = MouseMoveEvent.GetPooled(new Event { type = EventType.MouseMove })) window.rootVisualElement.SendEvent(move);
                    Get<TreeRecipe>(window, "recipe").height = beforeDrag + i;
                    Undo.FlushUndoRecordObjects();
                }
                typeof(TerrainityWindow).GetMethod("EndUndoDrag", Flags).Invoke(window, null);
                window.rootVisualElement.focusable = true; window.rootVisualElement.Focus();
                using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, modifiers = EventModifiers.Control }))
                { key.target = window.rootVisualElement; window.rootVisualElement.SendEvent(key); }
                if (Get<TreeRecipe>(window, "recipe").height != beforeDrag) throw new Exception("Grouped undo failed: expected " + beforeDrag + " got " + Get<TreeRecipe>(window, "recipe").height);
                window.rootVisualElement.Focus();
                using (var key = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Z, modifiers = EventModifiers.Control | EventModifiers.Shift }))
                { key.target = window.rootVisualElement; window.rootVisualElement.SendEvent(key); }
                if (Get<TreeRecipe>(window, "recipe").height != beforeDrag + 5) throw new Exception("Ctrl+Shift+Z / grouped redo failed");
                File.WriteAllText("Temp/terrainity-undo-result.txt", "PASS: tree/rock recipes, gradients, light creation/deletion, environment, export settings, camera and panel size undo/redo; Ctrl+Z and Ctrl+Shift+Z event handling; five drag updates collapsed into one undo step.");
            }
            catch (Exception e) { File.WriteAllText("Temp/terrainity-undo-result.txt", "FAIL: " + e); }
            finally
            {
                if (window != null)
                {
                    Set(window, "previewLighting", savedLights); Set(window, "previewEnvironment", savedEnvironment); Set(window, "showPreviewLighting", visible);
                    Undo.ClearUndo(window); window.Close();
                }
                if (previousFocus != null) previousFocus.Focus();
                if (savedLights != null && savedEnvironment != null) { PreviewPreferences.instance.Store(savedLights, savedEnvironment, visible); PreviewPreferences.instance.Flush(); }
            }
        }
    }
}
