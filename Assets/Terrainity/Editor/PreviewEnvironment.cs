using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class PreviewEnvironmentSettings
    {
        public Color background = new Color(.10f, .14f, .12f);
        public Material skybox;
        public bool useSkybox, showGrid = true, showCompass = true, expanded;
    }

    internal sealed class PreviewEnvironmentPanel : Foldout
    {
        internal PreviewEnvironmentPanel(PreviewEnvironmentSettings settings, Action changed)
        {
            text = "Environment"; value = settings.expanded;
            this.RegisterValueChangedCallback(e => { if (e.target == this) { settings.expanded = e.newValue; changed(); } });
            var color = new ColorField("Background") { value = settings.background, showAlpha = false, hdr = false };
            color.RegisterValueChangedCallback(e => { settings.background = e.newValue; changed(); }); Add(color);
            var sky = new Toggle("Use skybox") { value = settings.useSkybox }; Add(sky);
            var material = new ObjectField("Skybox") { objectType = typeof(Material), allowSceneObjects = false, value = settings.skybox,
                tooltip = "Assign a material using a Skybox shader. This affects the preview background only." };
            material.SetEnabled(settings.useSkybox); Add(material);
            sky.RegisterValueChangedCallback(e => { settings.useSkybox = e.newValue; material.SetEnabled(e.newValue); changed(); });
            material.RegisterValueChangedCallback(e => { settings.skybox = e.newValue as Material; changed(); });
            var grid = new Toggle("Show grid") { value = settings.showGrid };
            grid.RegisterValueChangedCallback(e => { settings.showGrid = e.newValue; changed(); }); Add(grid);
            var compass = new Toggle("XYZ compass") { value = settings.showCompass };
            compass.RegisterValueChangedCallback(e => { settings.showCompass = e.newValue; changed(); }); Add(compass);
            Add(new Label("X red / Y green / Z blue\nSkybox does not change ambient lighting.") { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10 } });
        }
    }
}
