using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class PreviewLightSettings
    {
        public Color color = Color.white;
        public float intensity = 1.3f;
        public float azimuth;
    }

    [Serializable]
    internal sealed class PreviewLightingSettings
    {
        public float rotation = 35;
        public bool independentPositions;
        public float ambient = .25f;
        public List<PreviewLightSettings> lights = new List<PreviewLightSettings> { new PreviewLightSettings() };
        internal void Validate()
        {
            rotation = Mathf.Repeat(rotation, 360);
            ambient = Mathf.Clamp01(ambient);
            if (lights == null) lights = new List<PreviewLightSettings>();
            if (lights.Count == 0 && !independentPositions) lights.Add(new PreviewLightSettings());
            if (lights.Count > 8) lights.RemoveRange(8, lights.Count - 8);
            for (int i = 0; i < lights.Count; i++)
            {
                if (lights[i] == null) lights[i] = new PreviewLightSettings();
                if (!independentPositions) lights[i].azimuth = i * 360f / lights.Count;
                lights[i].azimuth = Mathf.Repeat(lights[i].azimuth, 360);
                lights[i].intensity = Mathf.Clamp(lights[i].intensity, 0, 5);
            }
            independentPositions = true;
        }
        internal float Position(int index) => Mathf.Repeat(rotation + lights[index].azimuth, 360);
        internal void SetPosition(int index, float degrees) => lights[index].azimuth = Mathf.Repeat(degrees - rotation, 360);
        internal int AddAt(float position, int copyFrom = -1)
        {
            Validate();
            if (lights.Count >= 8) return -1;
            var light = new PreviewLightSettings();
            if (copyFrom >= 0 && copyFrom < lights.Count)
            {
                light.color = lights[copyFrom].color;
                light.intensity = lights[copyFrom].intensity;
            }
            lights.Add(light);
            SetPosition(lights.Count - 1, position);
            return lights.Count - 1;
        }
        internal void AddLight()
        {
            Validate();
            if (lights.Count >= 8) return;
            if (lights.Count == 0) { AddAt(rotation); return; }
            var angles = new List<float>();
            foreach (var light in lights) angles.Add(light.azimuth);
            angles.Sort();
            float largest = -1, position = 0;
            for (int i = 0; i < angles.Count; i++)
            {
                float end = i + 1 < angles.Count ? angles[i + 1] : angles[0] + 360;
                if (end - angles[i] > largest) { largest = end - angles[i]; position = angles[i] + largest * .5f; }
            }
            lights.Add(new PreviewLightSettings { azimuth = Mathf.Repeat(position, 360) });
        }
    }

    internal sealed class PreviewLightingPanel : ScrollView
    {
        internal PreviewLightingPanel(PreviewLightingSettings settings, Action changed)
        {
            AddToClassList("lighting-panel");
            settings.Validate();
            Add(new Label("PREVIEW LIGHTING") { name = "lighting-title" });
            var dialHeading = new VisualElement();
            dialHeading.style.flexDirection = FlexDirection.Row;
            dialHeading.style.alignItems = Align.Center;
            var dialTitle = new Label("Light positions");
            dialTitle.style.flexGrow = 1;
            dialHeading.Add(dialTitle);
            var help = new Label("?") { tooltip = LightRotationDial.ControlsTooltip };
            help.AddToClassList("header-help");
            dialHeading.Add(help);
            Add(dialHeading);
            var dial = new LightRotationDial(settings);
            Add(dial);
            var angle = new Slider("Rotate all", 0, 360) { value = settings.rotation, showInputField = true };
            Add(angle);
            var positions = new List<Slider>();
            void Refresh()
            {
                for (int i = 0; i < positions.Count; i++) positions[i].SetValueWithoutNotify(settings.Position(i));
                dial.MarkDirtyRepaint(); changed();
            }
            angle.RegisterValueChangedCallback(e => { settings.rotation = Mathf.Repeat(e.newValue, 360); Refresh(); });
            dial.rotated = (index, value) => { settings.SetPosition(index, value); Refresh(); };
            var entries = new VisualElement(); Add(entries);
            void Rebuild()
            {
                entries.Clear(); positions.Clear();
                dial.selected = settings.lights.Count == 0 ? -1 : Mathf.Clamp(dial.selected, 0, settings.lights.Count - 1);
                for (int i = 0; i < settings.lights.Count; i++)
                {
                    int index = i;
                    var item = settings.lights[i];
                    var fold = new Foldout { text = "Light " + (i + 1), value = true };
                    entries.Add(fold);
                    fold.Add(new Button(() => { dial.selected = index; dial.Focus(); dial.MarkDirtyRepaint(); }) { text = "Select on dial" });
                    var position = new Slider("Position", 0, 360) { value = settings.Position(index), showInputField = true };
                    positions.Add(position); fold.Add(position);
                    position.RegisterValueChangedCallback(e => { settings.SetPosition(index, e.newValue); dial.selected = index; Refresh(); });
                    var color = new ColorField("Color") { value = item.color, showAlpha = false, hdr = false };
                    color.RegisterValueChangedCallback(e => { item.color = e.newValue; Refresh(); }); fold.Add(color);
                    var intensity = new Slider("Intensity", 0, 5) { value = item.intensity, showInputField = true };
                    intensity.RegisterValueChangedCallback(e => { item.intensity = Mathf.Clamp(e.newValue, 0, 5); Refresh(); }); fold.Add(intensity);
                    fold.Add(new Button(() => { settings.lights.RemoveAt(index); Rebuild(); Refresh(); }) { text = "Remove light" });
                }

            }
            dial.collectionChanged = () => { Rebuild(); Refresh(); };
            Rebuild();
            var ambient = new Slider("Ambient", 0, 1) { value = settings.ambient, showInputField = true, tooltip = "Soft background illumination, independent of the directional lights." };
            ambient.RegisterValueChangedCallback(e => { settings.ambient = Mathf.Clamp01(e.newValue); Refresh(); }); Add(ambient);
        }
    }

    internal sealed class LightRotationDial : VisualElement
    {
        internal const string ControlsTooltip = "Lighting dial controls\n\n"
            + "Click an empty spot on the ring: add a light (maximum 8).\n"
            + "Drag a numbered light: move it independently.\n"
            + "Shift-click a light: duplicate its color and intensity, then drag the copy.\n"
            + "Shift-click empty ring space: copy the selected light there.\n"
            + "Ctrl-click a light: delete it.\n"
            + "Left / Right arrow keys while the dial is focused: move the selected light by 5 degrees.\n\n"
            + "Use Select on dial to choose overlapping lights. Rotate all turns the whole lighting setup.\n"
            + "Lights point downward at 45 degrees. These settings affect the preview only.";
        readonly PreviewLightingSettings settings;
        internal Action<int, float> rotated;
        internal Action collectionChanged;
        internal int selected;
        bool dragging;
        internal LightRotationDial(PreviewLightingSettings settings)
        {
            this.settings = settings;
            AddToClassList("light-rotation-dial");
            focusable = true;
            tooltip = ControlsTooltip;
            generateVisualContent += Draw;
            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0) return;
                float distance = Vector2.Distance(e.localMousePosition, Center);
                if (Mathf.Abs(distance - Radius) > 20) return;
                int hit = -1;
                if (selected >= 0 && selected < settings.lights.Count && Vector2.Distance(e.localMousePosition, Marker(selected)) < 15) hit = selected;
                else for (int i = settings.lights.Count - 1; i >= 0; i--)
                    if (Vector2.Distance(e.localMousePosition, Marker(i)) < 15) { hit = i; break; }
                if (e.ctrlKey)
                {
                    if (hit >= 0)
                    {
                        settings.lights.RemoveAt(hit);
                        selected = settings.lights.Count == 0 ? -1 : Mathf.Min(hit, settings.lights.Count - 1);
                        collectionChanged?.Invoke();
                    }
                    e.StopPropagation(); return;
                }
                if (hit < 0 || e.shiftKey)
                {
                    int copy = e.shiftKey ? (hit >= 0 ? hit : selected) : -1;
                    Vector2 delta = e.localMousePosition - Center;
                    int added = settings.AddAt(Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg, copy);
                    if (added < 0) { e.StopPropagation(); return; }
                    selected = added;
                    collectionChanged?.Invoke();
                }
                else selected = hit;
                dragging = true; Focus(); this.CaptureMouse(); Rotate(e.localMousePosition); e.StopPropagation();
            });
            RegisterCallback<MouseMoveEvent>(e => { if (!dragging) return; Rotate(e.localMousePosition); e.StopPropagation(); });
            RegisterCallback<MouseUpEvent>(e => { if (e.button != 0 || !dragging) return; dragging = false; this.ReleaseMouse(); e.StopPropagation(); });
            RegisterCallback<MouseCaptureOutEvent>(_ => dragging = false);
            RegisterCallback<KeyDownEvent>(e =>
            {
                if (selected < 0 || selected >= settings.lights.Count) return;
                if (e.keyCode != KeyCode.LeftArrow && e.keyCode != KeyCode.RightArrow) return;
                rotated?.Invoke(selected, Mathf.Repeat(settings.Position(selected) + (e.keyCode == KeyCode.LeftArrow ? -5 : 5), 360)); e.StopPropagation();
            });
        }
        Vector2 Center => contentRect.center;
        float Radius => Mathf.Max(1, Mathf.Min(contentRect.width, contentRect.height) * .5f - 23);
        Vector2 Marker(int index)
        {
            float angle = settings.Position(index) * Mathf.Deg2Rad;
            return Center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * Radius;
        }
        void Rotate(Vector2 position)
        {
            if (selected < 0 || selected >= settings.lights.Count) return;
            Vector2 delta = position - Center;
            if (delta.sqrMagnitude < 100) return;
            rotated?.Invoke(selected, Mathf.Repeat(Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg, 360));
        }
        void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1) return;
            var p = context.painter2D;
            p.lineWidth = 2; p.strokeColor = new Color(.3f, .65f, .8f);
            p.BeginPath(); p.Arc(Center, Radius, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.fillColor = new Color(.22f, .28f, .25f);
            p.BeginPath(); p.Arc(Center, Radius * .5f, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
            context.DrawText("TOP", Center - new Vector2(12, 7), 10, Color.gray, null);
            for (int i = 0; i < settings.lights.Count; i++)
            {
                var point = Marker(i);
                p.lineWidth = i == selected ? 4 : 2;
                p.fillColor = settings.lights[i].color; p.strokeColor = new Color(.3f, .65f, .8f);
                p.BeginPath(); p.Arc(point, 9, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke();
                var color = settings.lights[i].color.grayscale > .5f ? Color.black : Color.white;
                context.DrawText((i + 1).ToString(), point - new Vector2(3, 7), 11, color, null);
            }
        }
    }
}

