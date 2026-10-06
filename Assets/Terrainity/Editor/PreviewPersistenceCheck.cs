using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class PreviewPersistenceCheck
    {
        [MenuItem("Tools/Terrainity/Validate Preview Persistence")]
        static void Run() => EditorApplication.delayCall += Validate;

        internal static void Validate()
        {
            TerrainityWindow window = null;
            try
            {
                if (!PreviewPreferences.instance.Restore(out var lights, out var environment, out bool visible)) throw new Exception("No saved preview preferences.");
                window = ScriptableObject.CreateInstance<TerrainityWindow>();
                var serialized = new SerializedObject(window);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var actualLights = (PreviewLightingSettings)typeof(TerrainityWindow).GetField("previewLighting", flags).GetValue(window);
                var actualEnvironment = (PreviewEnvironmentSettings)typeof(TerrainityWindow).GetField("previewEnvironment", flags).GetValue(window);
                if (JsonUtility.ToJson(actualLights) != JsonUtility.ToJson(lights)) throw new Exception("New window lost lighting settings.");
                if (JsonUtility.ToJson(actualEnvironment) != JsonUtility.ToJson(environment)) throw new Exception("New window lost environment settings.");
                if (serialized.FindProperty("showPreviewLighting").boolValue != visible) throw new Exception("Panel visibility was not restored.");
                string file = File.ReadAllText("UserSettings/TerrainityPreview.asset");
                if (environment.skybox != null && !file.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(environment.skybox)))) throw new Exception("Skybox asset GUID is missing from the saved preferences.");
                File.WriteAllText("Temp/terrainity-preview-persistence.txt", "PASS: fresh window restores lighting, environment and panel visibility; preferences saved on disk; persistent skybox reference checked when assigned.");
            }
            catch (Exception e) { File.WriteAllText("Temp/terrainity-preview-persistence.txt", "FAIL: " + e); }
            finally { if (window != null) UnityEngine.Object.DestroyImmediate(window); }
        }
    }
}
