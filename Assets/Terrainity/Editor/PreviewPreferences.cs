using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    [FilePath("UserSettings/TerrainityPreview.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class PreviewPreferences : ScriptableSingleton<PreviewPreferences>
    {
        [SerializeField] bool hasSavedSettings;
        [SerializeField] PreviewLightingSettings lighting;
        [SerializeField] PreviewEnvironmentSettings environment;
        [SerializeField] bool panelVisible = true;
        bool pendingSave;
        double saveAfter;

        internal bool Restore(out PreviewLightingSettings savedLighting, out PreviewEnvironmentSettings savedEnvironment, out bool visible)
        {
            savedLighting = lighting == null ? null : Copy(lighting);
            savedEnvironment = environment == null ? null : Copy(environment);
            visible = panelVisible;
            if (!hasSavedSettings || savedLighting == null || savedEnvironment == null) return false;
            savedLighting.Validate();
            return true;
        }

        internal void Store(PreviewLightingSettings currentLighting, PreviewEnvironmentSettings currentEnvironment, bool visible)
        {
            if (currentLighting == null || currentEnvironment == null) return;
            lighting = Copy(currentLighting);
            environment = Copy(currentEnvironment);
            panelVisible = visible;
            hasSavedSettings = true;
            pendingSave = true;
            saveAfter = EditorApplication.timeSinceStartup + .5;
            EditorApplication.update -= SaveWhenIdle;
            EditorApplication.update += SaveWhenIdle;
        }

        void SaveWhenIdle()
        {
            if (EditorApplication.timeSinceStartup >= saveAfter) Flush();
        }

        internal void Flush()
        {
            EditorApplication.update -= SaveWhenIdle;
            if (!pendingSave) return;
            pendingSave = false;
            Save(true);
        }

        static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    }
}
