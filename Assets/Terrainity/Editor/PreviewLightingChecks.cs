using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class PreviewLightingChecks
    {
        [MenuItem("Tools/Terrainity/Validate Preview Lighting")]
        static void Run()
        {
            const string folder = "TerrainityReports/PreviewLighting";
            Directory.CreateDirectory(folder);
            try
            {
                var gestures = new PreviewLightingSettings(); gestures.Validate();
                gestures.lights[0].color = Color.cyan; gestures.lights[0].intensity = 2.5f;
                int copyIndex = gestures.AddAt(123, 0);
                if (Mathf.Abs(gestures.Position(copyIndex) - 123) > .001f || gestures.lights[copyIndex].color != Color.cyan || gestures.lights[copyIndex].intensity != 2.5f) throw new Exception("Light duplication lost properties or position.");
                gestures.lights[copyIndex].intensity = .5f;
                if (gestures.lights[0].intensity != 2.5f) throw new Exception("Duplicate shares mutable settings.");
                while (gestures.lights.Count < 8) gestures.AddAt(200);
                if (gestures.AddAt(240) != -1 || gestures.lights.Count != 8) throw new Exception("Light limit failed.");
                gestures.lights.Clear(); gestures.Validate();
                var empty = JsonUtility.FromJson<PreviewLightingSettings>(JsonUtility.ToJson(gestures)); empty.Validate();
                if (empty.lights.Count != 0) throw new Exception("Deleted final light returned after restore.");
                if (empty.AddAt(90) != 0 || Mathf.Abs(empty.Position(0) - 90) > .001f) throw new Exception("Adding to an empty rig failed.");
                var legacy = new PreviewLightingSettings { rotation = 30 };
                legacy.lights.Add(new PreviewLightSettings()); legacy.lights.Add(new PreviewLightSettings());
                legacy.Validate();
                if (legacy.Position(1) != 150 || legacy.Position(2) != 270) throw new Exception("Legacy light positions changed.");
                legacy.SetPosition(1, 220); legacy.Validate();
                if (legacy.Position(0) != 30 || legacy.Position(1) != 220 || legacy.Position(2) != 270) throw new Exception("Moving a light moved its neighbors.");
                legacy.AddLight();
                if (legacy.Position(0) != 30 || legacy.Position(1) != 220 || legacy.Position(2) != 270) throw new Exception("Adding a light moved existing lights.");
                legacy.lights.RemoveAt(1); legacy.Validate();
                if (legacy.Position(1) != 270) throw new Exception("Removing a light moved its neighbor.");
                var saved = JsonUtility.FromJson<PreviewLightingSettings>(JsonUtility.ToJson(legacy)); saved.Validate();
                for (int i = 0; i < legacy.lights.Count; i++) if (saved.Position(i) != legacy.Position(i)) throw new Exception("Independent positions did not persist.");
                var before = Resources.FindObjectsOfTypeAll<Light>().ToArray();
                using (var preview = new TreePreview())
                {
                    preview.Build(new TreeRecipe { showFoliage = false }, 0);
                    int triangles = preview.WoodTriangles;
                    var settings = new PreviewLightingSettings { ambient = 0 };
                    Color32[] Capture(string name)
                    {
                        preview.SetLighting(settings);
                        var texture = preview.ReferenceSnapshot(256);
                        File.WriteAllBytes(folder + "/" + name + ".png", texture.EncodeToPNG());
                        var pixels = texture.GetPixels32();
                        UnityEngine.Object.DestroyImmediate(texture);
                        return pixels;
                    }
                    var original = Capture("one-light");
                    settings.SetPosition(0, settings.Position(0) + 180);
                    Different(original, Capture("rotated"), "rotation");
                    settings.lights[0].color = Color.red;
                    var red = Capture("red");
                    settings.lights[0].color = Color.blue;
                    Different(red, Capture("blue"), "color");
                    settings.lights[0].intensity = 0;
                    var dark = Capture("off");
                    settings.lights.Add(new PreviewLightSettings { intensity = 0 });
                    settings.lights.Add(new PreviewLightSettings { intensity = 2 });
                    Different(dark, Capture("third-light-only"), "additional light");
                    settings.lights.RemoveRange(1, 2);
                    var removed = Capture("removed");
                    if (Difference(dark, removed) > .001) throw new Exception("Removed light still contributes.");
                    if (preview.WoodTriangles != triangles) throw new Exception("Lighting changed geometry.");
                    var environment = new PreviewEnvironmentSettings { background = Color.blue, showGrid = false };
                    preview.SetEnvironment(environment);
                    var blueBackground = Capture("environment-blue");
                    environment.background = Color.red; preview.SetEnvironment(environment);
                    var redBackground = Capture("environment-red");
                    Different(blueBackground, redBackground, "background color");
                    environment.showGrid = true; preview.SetEnvironment(environment);
                    Different(redBackground, Capture("environment-grid"), "grid");
                    var sceneSkybox = RenderSettings.skybox;
                    var skyShader = Shader.Find("Skybox/Procedural");
                    if (skyShader == null) throw new Exception("Skybox shader unavailable for validation.");
                    var skyMaterial = new Material(skyShader);
                    try
                    {
                        environment.showGrid = false; environment.useSkybox = true; environment.skybox = skyMaterial;
                        preview.SetEnvironment(environment);
                        var skyPixels = Capture("environment-skybox");
                        Different(redBackground, skyPixels, "skybox");
                        if (RenderSettings.skybox != sceneSkybox) throw new Exception("Preview modified scene skybox.");
                        environment.skybox = null; preview.SetEnvironment(environment);
                        if (Difference(redBackground, Capture("environment-fallback")) > .001) throw new Exception("Missing skybox did not fall back to background.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(skyMaterial); }
                    var restored = JsonUtility.FromJson<PreviewLightingSettings>(JsonUtility.ToJson(settings));
                    if (restored.lights.Count != 1 || restored.rotation != settings.rotation) throw new Exception("Settings did not round-trip.");
                }
                var leaked = Resources.FindObjectsOfTypeAll<Light>().Where(x => !before.Contains(x)).ToArray();
                if (leaked.Length != 0) throw new Exception("Preview lights leaked after disposal.");
                File.WriteAllText(folder + "/validation.txt", "PASS: light add/copy properties and independence, eight-light limit, empty rig persistence and recreation, background color, grid toggle, skybox, missing-skybox fallback, scene isolation, independent positions, legacy migration, add/remove stability, rotation, color, intensity, third light contribution, removal, unchanged geometry, settings persistence and light cleanup.");
            }
            catch (Exception e) { File.WriteAllText(folder + "/validation.txt", "FAIL: " + e); Debug.LogException(e); }
        }
        static double Difference(Color32[] a, Color32[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
            return sum / (a.Length * 3);
        }
        static void Different(Color32[] a, Color32[] b, string feature)
        {
            if (Difference(a, b) < .02) throw new Exception(feature + " did not change the rendered preview.");
        }
    }
}

