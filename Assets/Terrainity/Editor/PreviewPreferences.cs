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
        [SerializeField] PreviewWindSettings wind;
        [SerializeField] bool windVisible;
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

        internal bool RestoreWind(out PreviewWindSettings savedWind, out bool visible)
        {
            savedWind = wind == null ? null : Copy(wind);
            visible = windVisible;
            if (savedWind == null) return false;
            savedWind.Validate();
            return true;
        }

        internal void StoreWind(PreviewWindSettings currentWind, bool visible)
        {
            if (currentWind == null) return;
            wind = Copy(currentWind);
            windVisible = visible;
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
