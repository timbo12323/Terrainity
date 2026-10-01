using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    public sealed partial class TerrainityWindow
    {
        [SerializeField] RockRecipe rockRecipe = new RockRecipe();
        [SerializeField] string lastRockFolder, rockStatus;
        [SerializeField] GameObject[] lastRockPrefabs = Array.Empty<GameObject>();

        void BuildRockBuilder(VisualElement split, ScrollView controls)
        {
            if (rockRecipe == null) rockRecipe = new RockRecipe();
            rockRecipe.Validate(); variant = Mathf.Clamp(variant, 0, rockRecipe.variantCount - 1);
            var name = new TextField("Asset name") { value = rockRecipe.assetName };
            name.RegisterValueChangedCallback(e => rockRecipe.assetName = e.newValue); controls.Add(name);
            var names = new List<string>(RockRecipe.Presets);
            var preset = new PopupField<string>("Shape preset", names, Math.Max(0, names.IndexOf(rockRecipe.preset)));
            preset.tooltip = "Applies starting shape, color, roughness and topology settings. Your seed, textures, family size and export settings are retained.";
            preset.RegisterValueChangedCallback(e => { rockRecipe.ApplyPreset(e.newValue); ShowTab(1); }); controls.Add(preset);
            var files = Box(controls, "row");
            Action(files, "Save recipe", () =>
            {
                string path = EditorUtility.SaveFilePanel("Save rock recipe", "", rockRecipe.assetName + ".json", "json");
                if (string.IsNullOrEmpty(path)) return;
                try { File.WriteAllText(path, JsonUtility.ToJson(rockRecipe, true)); }
                catch (Exception e) { EditorUtility.DisplayDialog("Cannot save rock recipe", e.Message, "OK"); }
            });
            Action(files, "Load recipe", () =>
            {
                string path = EditorUtility.OpenFilePanel("Load rock recipe", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                try { var loaded = RockRecipe.Read(path); RecordSettingsUndo(); rockRecipe = loaded; variant = 0; ShowTab(1); }
                catch (Exception e) { EditorUtility.DisplayDialog("Cannot load rock recipe", e.Message, "OK"); }
            });
            var seed = new IntegerField("Seed") { value = rockRecipe.seed };
            seed.RegisterValueChangedCallback(e => { rockRecipe.seed = e.newValue; Changed(); }); controls.Add(seed);
            Action(controls, "New random seed", () => seed.value = Guid.NewGuid().GetHashCode() & int.MaxValue, true);
            var shape = Fold(controls, "Silhouette");
            AddTip(shape, "Natural and stylized presets share the same shape controls. Dimensions describe the starting envelope; irregularity and sibling variation alter the final bounds. The pivot sits at the base for placement.");
            Slider(shape, "Width (m)", .1f, 30, rockRecipe.width, x => rockRecipe.width = x);
            Slider(shape, "Height (m)", .1f, 30, rockRecipe.height, x => rockRecipe.height = x);
            Slider(shape, "Depth (m)", .1f, 30, rockRecipe.depth, x => rockRecipe.depth = x);
            Slider(shape, "Irregularity", 0, .7f, rockRecipe.irregularity, x => rockRecipe.irregularity = x);
            Slider(shape, "Angularity", 0, 1, rockRecipe.angularity, x => rockRecipe.angularity = x);
            Slider(shape, "Flatten base", 0, .65f, rockRecipe.baseFlattening, x => rockRecipe.baseFlattening = x);
            var material = Fold(controls, "Rock material");
            AddTip(material, "Seamless box blend samples the base and normal textures from three directions and blends them across the rock, avoiding per-triangle projection seams. UV0 remains available for other materials. Spherical UV uses the mesh's UV0 and preserves older recipes. Import normal textures as Normal map in Unity.");
            ColorControl(material, "Tint", rockRecipe.tint, x => rockRecipe.tint = x);
            Slider(material, "Roughness", 0, 1, rockRecipe.roughness, x => rockRecipe.roughness = x);
            MaterialToggle(material, "Specular highlights", rockRecipe.specularHighlights, x => rockRecipe.specularHighlights = x);
            MaterialToggle(material, "Environment reflections", rockRecipe.environmentReflections, x => rockRecipe.environmentReflections = x);
            void TextureField(string label, bool normal)
            {
                var field = new ObjectField(label) { objectType = typeof(Texture2D), allowSceneObjects = false, value = rockRecipe.Texture(normal) };
                field.RegisterValueChangedCallback(e =>
                {
                    string guid = e.newValue == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.newValue));
                    if (normal) rockRecipe.normalGuid = guid; else rockRecipe.textureGuid = guid;
                    Changed();
                }); material.Add(field);
            }
            TextureField("Base texture", false); TextureField("Normal map", true);
            var uvModes = new List<string> { "Seamless box blend", "Spherical UV (legacy)" };
            var uvMode = new PopupField<string>("UV mapping", uvModes, (int)rockRecipe.uvMode)
            { tooltip = "Seamless box blend mixes three texture projections in the material. Spherical UV uses the mesh UVs and can stretch around the poles." };
            uvMode.RegisterValueChangedCallback(e => { rockRecipe.uvMode = (RockUvMode)uvMode.index; Changed(); }); material.Add(uvMode);
            Slider(material, "Rock Texture U", .1f, 20, rockRecipe.textureScale, x => rockRecipe.textureScale = x);
            Slider(material, "Rock Texture V", .1f, 20, rockRecipe.textureScaleV, x => rockRecipe.textureScaleV = x);
            var blend = new Slider("Projection blend", 1, 16)
            {
                value = rockRecipe.projectionBlend,
                showInputField = true,
                tooltip = "Box projection only. Lower values blend more softly between projection directions; higher values keep each direction more distinct."
            };
            blend.SetEnabled(rockRecipe.uvMode == RockUvMode.Box);
            blend.RegisterValueChangedCallback(e => { rockRecipe.projectionBlend = Mathf.Clamp(e.newValue, 1, 16); Changed(); });
            material.Add(blend);
            uvMode.RegisterValueChangedCallback(e => blend.SetEnabled(uvMode.index == (int)RockUvMode.Box));
            Slider(material, "Normal strength", 0, 3, rockRecipe.normalStrength, x => rockRecipe.normalStrength = x);
            var detail = Fold(controls, "Mesh detail");
            AddTip(detail, "Surface detail deforms geometry. Increase subdivisions for smaller features. Flat shading gives each face a hard normal for a low-poly look. Smooth shading shares vertices except at the texture seam. Lower LODs sample the same shape at fewer vertices.");
            Slider(detail, "Surface detail", 0, .2f, rockRecipe.detail, x => rockRecipe.detail = x);
            Slider(detail, "Detail scale", 1, 15, rockRecipe.detailScale, x => rockRecipe.detailScale = x);
            IntSlider(detail, "Subdivisions", 1, 5, rockRecipe.subdivisions, x => rockRecipe.subdivisions = x, "80 / 320 / 1,280 / 5,120 / 20,480 triangles. Each step multiplies triangle count by four.");
            MaterialToggle(detail, "Flat shading", rockRecipe.flatShading, x => rockRecipe.flatShading = x);
            var family = Fold(controls, "Family variations");
            AddTip(family, "Each seed produces a repeatable set of siblings with different surface shapes. Size variation controls their proportions.");
            IntSlider(family, "Family size", 1, 12, rockRecipe.variantCount, x => { rockRecipe.variantCount = x; variant = Mathf.Min(variant, x - 1); }, "Number of exported sibling prefabs.");
            Slider(family, "Shape variation", 0, .6f, rockRecipe.variation, x => rockRecipe.variation = x);
            Action(controls, "Reset rock settings", () => { rockRecipe = new RockRecipe(); variant = 0; ShowTab(1); });
            var panel = BuildPreviewPanel(split);
            var export = Box(panel, "card");
            var heading = Box(export, "row"); Text(heading, "Generate rock family", "section-title");
            AddHeaderHelp(heading, "Saves meshes, prefab siblings and a JSON recipe to Rocks. Matching materials and textures are shared. Drag prefabs into your scene. LODs use the same seed and progressively fewer subdivisions.");
            var lodToggle = new Toggle("Generate LOD models") { value = rockRecipe.generateLods };
            export.Add(lodToggle);
            var lodControls = new Foldout { text = "LOD settings", value = false };
            export.Add(lodControls); lodControls.SetEnabled(rockRecipe.generateLods);
            lodToggle.RegisterValueChangedCallback(e => { rockRecipe.generateLods = e.newValue; lodControls.SetEnabled(e.newValue); });
            var fadeToggle = new Toggle("Dithered transitions") { value = rockRecipe.crossFade, tooltip = "Uses a 15% transition band. URP also needs LOD Cross Fade enabled in its pipeline asset." };
            fadeToggle.RegisterValueChangedCallback(e => rockRecipe.crossFade = e.newValue); lodControls.Add(fadeToggle);
            Slider(lodControls, "LOD1 screen height", .21f, .8f, rockRecipe.lod1Start, x => rockRecipe.lod1Start = x);
            Slider(lodControls, "LOD2 screen height", .04f, .2f, rockRecipe.lod2Start, x => rockRecipe.lod2Start = x);
            Slider(lodControls, "Cull screen height", .001f, .03f, rockRecipe.cull, x => rockRecipe.cull = x);
            Text(lodControls, "Screen height is a fraction: 0.35 = 35%. Rocks with two LODs use the cull height for their last level. Triangle counts are saved in LOD-Report.csv.", "small");
            MaterialToggle(export, "Add mesh collider", rockRecipe.collider, x => rockRecipe.collider = x);
            Text(export, "Mesh colliders are intended for static scenery.", "small");
            Action(export, "Generate prefab family", () =>
            {
                try
                {
                    var result = RockExporter.Generate(rockRecipe);
                    lastRockFolder = result.folder; lastRockPrefabs = result.prefabs;
                    rockStatus = $"Generated {result.prefabs.Length} rocks in {result.folder}.";
                    EditorGUIUtility.PingObject(result.prefabs[0]);
                }
                catch (OperationCanceledException) { rockStatus = "Generation cancelled. No partial export was kept."; }
                catch (Exception e) { rockStatus = "Generation failed: " + e.Message; Debug.LogException(e); }
                ShowTab(1);
            }, true);
            if (!string.IsNullOrEmpty(rockStatus)) Text(export, rockStatus, "small");
            if (!string.IsNullOrEmpty(lastRockFolder) && AssetDatabase.IsValidFolder(lastRockFolder))
            {
                var actions = Box(export, "row");
                Action(actions, "Locate resources", () => { var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(lastRockFolder); Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); });
                Action(actions, "Export Unity package", () =>
                {
                    string path = EditorUtility.SaveFilePanel("Export rock family", "", Path.GetFileName(lastRockFolder) + ".unitypackage", "unitypackage");
                    if (string.IsNullOrEmpty(path)) return;
                    try { TreeExporter.ExportPackage(lastRockFolder, path); }
                    catch (Exception e) { EditorUtility.DisplayDialog("Cannot export rocks", e.Message, "OK"); }
                });
            }
            Changed();
        }
        void RockChanged()
        {
            preview?.BuildRock(rockRecipe, variant);
            if (previewCaption != null) previewCaption.text = rockRecipe.preset + " / Sibling " + (variant + 1) + " of " + rockRecipe.variantCount;
            if (meshStats != null && preview != null) meshStats.text = $"Rock: {preview.WoodTriangles:N0} triangles • {preview.Meshes[0].vertexCount:N0} vertices";
            previewElement?.MarkDirtyRepaint(); Repaint();
        }
    }
}
