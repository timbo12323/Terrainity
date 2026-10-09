using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class PreviewWindSettings
    {
        public float speed = 1;
        public float chaos = 1;
        public float direction = 45;
        public float gustStrength = .5f;
        public float gustFrequency = .2f;
        public float sway = 1;
        public float flutter = 1;
        public float playbackSpeed = 1;
        public bool paused;
        public float grassWaveSize = .6f;
        public float grassWaveSpeed = .45f;
        public float grassWaveBreakup = .65f;
        public float grassBladeVariation = .25f;

        internal void Validate()
        {
            speed = Clamp(speed, 0, 5, 1);
            chaos = Clamp(chaos, 0, 5, 1);
            direction = Clamp(direction, 0, 360, 45);
            gustStrength = Clamp(gustStrength, 0, 3, .5f);
            gustFrequency = Clamp(gustFrequency, 0, 1, .2f);
            sway = Clamp(sway, 0, 2, 1);
            flutter = Clamp(flutter, 0, 2, 1);
            playbackSpeed = Clamp(playbackSpeed, .1f, 3, 1);
            grassWaveSize = Clamp(grassWaveSize, .05f, 5, .6f);
            grassWaveSpeed = Clamp(grassWaveSpeed, 0, 2, .45f);
            grassWaveBreakup = Clamp(grassWaveBreakup, 0, 1, .65f);
            grassBladeVariation = Clamp(grassBladeVariation, 0, .8f, .25f);
        }

        static float Clamp(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        internal Vector4 Direction
        {
            get
            {
                float angle = direction * Mathf.Deg2Rad;
                return new Vector4(Mathf.Sin(angle), 0, Mathf.Cos(angle), speed);
            }
        }
    }

    internal sealed class PreviewWindPanel : VisualElement
    {
        internal PreviewWindPanel(PreviewWindSettings settings, Action changed, Action restart, bool grass = false)
        {
            name = "previewWindPanel";
            AddToClassList("wind-panel");
            settings.Validate();
            Add(new Label("PREVIEW WIND") { name = "wind-title" });
            AddSlider("Wind speed", 0, 5, () => settings.speed, v => settings.speed = v,
                "Wind force, matching Wind Zone Main. Zero gives still air.");
            AddSlider("Wind chaos", 0, 5, () => settings.chaos, v => settings.chaos = v,
                grass ? "Adds small sideways swirls and blade-tip flutter on top of the travelling waves." : "Turbulence, matching Wind Zone Turbulence. Adds irregular leaf motion.");
            AddSlider("Direction", 0, 360, () => settings.direction, v => settings.direction = v,
                "Direction the wind blows toward: 0° = +Z, 90° = +X, 180° = -Z, 270° = -X.");
            if (grass)
            {
                var waves = new Foldout { text = "Travelling waves", value = true };
                Add(waves);
                AddSlider("Wave size (m)", .05f, 5, () => settings.grassWaveSize, v => settings.grassWaveSize = v,
                    "Distance between gust fronts. Smaller values show several waves within a patch; larger values bend broad areas together.", waves);
                AddSlider("Wave travel (m/s)", 0, 2, () => settings.grassWaveSpeed, v => settings.grassWaveSpeed = v,
                    "Moves gust fronts in the wind direction. Gust frequency scales the travel rate. Zero holds the large gust field in place; chaos can still flutter tips.", waves);
                AddSlider("Wave breakup", 0, 1, () => settings.grassWaveBreakup, v => settings.grassWaveBreakup = v,
                    "Smooth spatial noise warps and breaks up the gust fronts. Zero produces regular travelling bands; higher values make them irregular.", waves);
                AddSlider("Blade response variation", 0, .8f, () => settings.grassBladeVariation, v => settings.grassBladeVariation = v,
                    "Seeded differences in bending strength keep neighbouring blades from responding identically. The main gusts stay spatially coherent.", waves);
            }
            var motion = new Foldout { text = "Gusts & motion", value = false };
            Add(motion);
            AddSlider("Gust strength", 0, 3, () => settings.gustStrength, v => settings.gustStrength = v,
                "Extra pressure from gusts, matching Wind Zone Pulse Magnitude.", motion);
            AddSlider("Gust frequency", 0, 1, () => settings.gustFrequency, v => settings.gustFrequency = v,
                "How quickly sway and gusts cycle, matching Wind Zone Pulse Frequency.", motion);
            AddSlider("Sway", 0, 2, () => settings.sway, v => settings.sway = v,
                grass ? "Multiply grass bending. Blade roots remain anchored; bends grow toward the tips." : "Multiply trunk, branch and canopy bending. Roots stay anchored. 1 matches runtime weights.", motion);
            AddSlider(grass ? "Tip flutter" : "Leaf flutter", 0, 2, () => settings.flutter, v => settings.flutter = v,
                grass ? "Multiply small, independently phased blade-tip motion from chaos." : "Multiply fine leaf motion from chaos. 1 matches runtime weights.", motion);
            AddSlider("Playback speed", .1f, 3, () => settings.playbackSpeed, v => settings.playbackSpeed = v,
                "Animation time multiplier. Slow playback helps inspect movement.", motion);
            var pause = new Toggle("Pause animation") { value = settings.paused };
            pause.RegisterValueChangedCallback(e => { settings.paused = e.newValue; changed(); });
            Add(pause);
            Add(new Button(restart) { text = "Restart animation", tooltip = "Replay the wind from time zero using the current settings." });

            void AddSlider(string label, float min, float max, Func<float> get, Action<float> set, string tip, VisualElement parent = null)
            {
                var slider = new Slider(label, min, max) { value = get(), showInputField = true, tooltip = tip };
                slider.RegisterValueChangedCallback(e =>
                {
                    set(e.newValue);
                    settings.Validate();
                    slider.SetValueWithoutNotify(get());
                    changed();
                });
                (parent ?? this).Add(slider);
            }
        }
    }

    internal sealed partial class TreePreview
    {
        static readonly int WindDirectionId = Shader.PropertyToID("_PreviewWindDirection");
        static readonly int WindMotionId = Shader.PropertyToID("_PreviewWindMotion");
        static readonly int WindTimeId = Shader.PropertyToID("_PreviewWindTime");
        static readonly int WindWeightsId = Shader.PropertyToID("_PreviewWindWeights");
        static readonly int GrassWaveId = Shader.PropertyToID("_PreviewGrassWindWave");
        PreviewWindSettings wind;
        bool windEnabled;
        float windTime;
        double windUpdatedAt;

        internal bool WindAnimating => windEnabled && wind != null && !wind.paused && wind.speed > 0;

        internal void SetWind(PreviewWindSettings settings, bool enabled)
        {
            AdvanceWindClock();
            settings.Validate();
            wind = settings;
            windEnabled = enabled;
            windUpdatedAt = EditorApplication.timeSinceStartup;
        }

        internal void RestartWind()
        {
            windTime = 0;
            windUpdatedAt = EditorApplication.timeSinceStartup;
        }

        void AdvanceWindClock()
        {
            double now = EditorApplication.timeSinceStartup;
            if (WindAnimating && windUpdatedAt > 0)
                windTime += (float)Math.Min(now - windUpdatedAt, .1) * wind.playbackSpeed;
            windUpdatedAt = now;
        }

        void ApplyWind(Material material, bool animated)
        {
            if (material == null || !material.HasProperty(WindDirectionId)) return;
            if (material.HasProperty("_GrassPreview")) material.SetFloat("_GrassPreview",1);
            bool active = animated && windEnabled && wind != null;
            material.SetVector(WindDirectionId, active ? wind.Direction : Vector4.zero);
            material.SetVector(WindMotionId, active ? new Vector4(wind.chaos, wind.gustStrength, wind.gustFrequency, 0) : Vector4.zero);
            material.SetFloat(WindTimeId, windTime);
            material.SetVector(WindWeightsId, active ? new Vector4(wind.sway, wind.flutter, grassStudy ? 1 : 0, 0) : Vector4.zero);
            if (material.HasProperty(GrassWaveId)) material.SetVector(GrassWaveId, wind == null ? new Vector4(.6f,.45f,.65f,.25f)
                : new Vector4(wind.grassWaveSize,wind.grassWaveSpeed,wind.grassWaveBreakup,wind.grassBladeVariation));
        }

        Vector3 WindPosition(Vector3 position, Vector4 weights)
        {
            if (!windEnabled || wind == null || wind.speed <= .0001f || weights.x <= .0001f) return position;
            float phase = 0; // Preview meshes use an identity object transform, matching the shader's tree origin.
            var direction = (Vector3)wind.Direction;
            float slow = Mathf.Sin(windTime * (1.1f + wind.gustFrequency * 6) + phase);
            float gust = Mathf.Sin(windTime * (2.3f + wind.gustFrequency * 9) + phase * 1.7f);
            float pressure = Mathf.Clamp(wind.speed * (.55f + .45f * slow) + wind.gustStrength * .25f * gust, -3, 3);
            float flutter = weights.y * wind.flutter * wind.chaos * .028f
                * Mathf.Sin(windTime * 8.7f + phase * 2.1f + position.y * 1.7f + weights.z * 6.28318f);
            return position + direction * pressure * (.28f * weights.x * wind.sway)
                + (direction + new Vector3(0, .35f, 0)) * flutter;
        }
    }
}
