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
        [SerializeField] GrassRecipe grassRecipe = new GrassRecipe();
        [SerializeField] GrassStudy grassStudy = GrassStudy.Blade;
        [SerializeField] string grassStatus, lastGrassFolder;
        [SerializeField] GameObject lastGrassPrefab;

        GrassRecipe GrassExportRecipe()
        {
            var result = grassRecipe.Copy();
            if (previewWind != null)
            {
                result.windWaveSize = previewWind.grassWaveSize; result.windWaveSpeed = previewWind.grassWaveSpeed;
                result.windWaveBreakup = previewWind.grassWaveBreakup; result.windBladeVariation = previewWind.grassBladeVariation;
            }
            return result;
        }

        void BuildGrassBuilder(VisualElement controls)
        {
            if (grassRecipe == null) grassRecipe = new GrassRecipe();
            grassRecipe.Validate();
            var name = new TextField("Asset name") { value = grassRecipe.assetName };
            name.RegisterValueChangedCallback(e => grassRecipe.assetName = e.newValue); controls.Add(name);
            var styles = new List<string>(GrassRecipe.Styles);
            var style = new PopupField<string>("Blade style",styles,styles.IndexOf(grassRecipe.style))
            { tooltip = "Starting silhouettes: short lawn, curved meadow, broad stylized and narrow dry grass. Applies blade shape and gradient; keeps clump layout, seed and mesh quality." };
            style.RegisterValueChangedCallback(e => { grassRecipe.ApplyStyle(e.newValue); ShowTab(1); }); controls.Add(style);
            var files = Box(controls,"row");
            Action(files,"Save recipe",() =>
            {
                string path = EditorUtility.SaveFilePanel("Save grass recipe","",grassRecipe.assetName+".json","json");
                if (string.IsNullOrEmpty(path)) return;
                try { TreeJsonStorage.Write(path,GrassRecipeJson.From(GrassExportRecipe())); }
                catch (Exception e) { EditorUtility.DisplayDialog("Cannot save grass",e.Message,"OK"); }
            });
            Action(files,"Load recipe",() =>
            {
                string path = EditorUtility.OpenFilePanel("Load grass recipe","","json");
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    var loaded = GrassRecipeJson.Read(path); RecordSettingsUndo(); grassRecipe = loaded;
                    if (previewWind == null) previewWind = new PreviewWindSettings();
                    previewWind.grassWaveSize = loaded.windWaveSize; previewWind.grassWaveSpeed = loaded.windWaveSpeed;
                    previewWind.grassWaveBreakup = loaded.windWaveBreakup; previewWind.grassBladeVariation = loaded.windBladeVariation;
                    SavePreviewPreferences(); ShowTab(1);
                }
                catch (Exception e) { EditorUtility.DisplayDialog("Cannot load grass",e.Message,"OK"); }
            });
            var seed = new IntegerField("Seed") { value = grassRecipe.seed };
            seed.RegisterValueChangedCallback(e => { grassRecipe.seed = e.newValue; Changed(); }); controls.Add(seed);
            Action(controls,"New random seed",() => seed.value = Guid.NewGuid().GetHashCode() & int.MaxValue);

            var silhouette = Fold(controls,"Blade silhouette");
            AddTip(silhouette,"Width is the starting blade width. Root and tip width are fractions of it. Belly fullness broadens the middle; belly position moves the widest region. Higher taper keeps width farther up the blade before narrowing. All styles remain fully editable.");
            Slider(silhouette,"Height (m)",.03f,3,grassRecipe.height,x => grassRecipe.height = x);
            Slider(silhouette,"Width (m)",.002f,.3f,grassRecipe.width,x => grassRecipe.width = x);
            Slider(silhouette,"Root width",.05f,1,grassRecipe.rootWidth,x => grassRecipe.rootWidth = x);
            Slider(silhouette,"Tip width",0,1,grassRecipe.tipWidth,x => grassRecipe.tipWidth = x);
            Slider(silhouette,"Taper",.2f,5,grassRecipe.taper,x => grassRecipe.taper = x);
            Slider(silhouette,"Belly fullness",0,1,grassRecipe.belly,x => grassRecipe.belly = x);
            Slider(silhouette,"Belly position",.05f,.95f,grassRecipe.bellyPosition,x => grassRecipe.bellyPosition = x);
            var shape = Fold(controls,"Blade curvature and depth");
            AddTip(shape,"Lean tilts the whole blade. Bend adds forward or backward curvature after Bend start. Twist turns the cross-section from root to tip. Center fold makes a V-shaped cross-section. Thickness 0 creates a two-sided ribbon; positive thickness creates front, back and capped edge geometry. Height is vertical rise, not curved blade length.");
            Slider(shape,"Lean (degrees)",-60,60,grassRecipe.lean,x => grassRecipe.lean = x);
            Slider(shape,"Bend",-1.5f,1.5f,grassRecipe.bend,x => grassRecipe.bend = x);
            Slider(shape,"Bend start",0,.9f,grassRecipe.bendStart,x => grassRecipe.bendStart = x);
            Slider(shape,"Twist (degrees)",-180,180,grassRecipe.twist,x => grassRecipe.twist = x);
            Slider(shape,"Center fold (degrees)",0,75,grassRecipe.fold,x => grassRecipe.fold = x);
            Slider(shape,"Thickness (m)",0,.02f,grassRecipe.thickness,x => grassRecipe.thickness = x);

            var color = Fold(controls,"Grass material");
            AddTip(color,"One shared gradient follows each blade from root (left) to tip (right), including bent and twisted blades. Supports multiple color stops. Alpha is ignored. Patch color variation will be added later.");
            var gradient = new GradientField("Root → tip") { value = grassRecipe.gradient };
            gradient.RegisterValueChangedCallback(e => { grassRecipe.gradient = e.newValue; Changed(); }); color.Add(gradient);
            MaterialToggle(color,"Translucency",grassRecipe.translucency,x => grassRecipe.translucency = x);
            Slider(color,"Light transmission",0,1,grassRecipe.transmission,x => grassRecipe.transmission = x);
            AddTip(color,"Translucency simulates backlighting through thin grass blades. Enable it, then adjust Light transmission and place a preview light behind the blades to inspect the effect.");
            MaterialToggle(color,"Alpha-to-coverage (MSAA)",grassRecipe.alphaToCoverage,x => grassRecipe.alphaToCoverage = x);
            Slider(color,"Edge softness",0,.25f,grassRecipe.edgeSoftness,x => grassRecipe.edgeSoftness = x);
            AddTip(color,"Alpha-to-coverage smooths ribbon edges on a multisampled render target. Enable 2x/4x/8x MSAA in the URP asset to use it at runtime. Edge softness controls a width fraction along ribbon edges; without MSAA it uses dithered coverage. Solid blades keep their capped sides. Project anti-aliasing settings are retained.");
            Slider(color,"Ambient occlusion",0,1,grassRecipe.ambientOcclusion,x => grassRecipe.ambientOcclusion = x);
            AddTip(color,"Approximate occlusion darkens ambient light near blade roots and fades toward the tips. The strength is saved with the material.");
            Slider(color,"Roughness",0,1,grassRecipe.roughness,x => grassRecipe.roughness = x);
            MaterialToggle(color,"Specular highlights",grassRecipe.specularHighlights,x => grassRecipe.specularHighlights = x);
            MaterialToggle(color,"Environment reflections",grassRecipe.environmentReflections,x => grassRecipe.environmentReflections = x);
            var layout = Fold(controls,"Clump layout");
            AddTip(layout,"Blade count fills the footprint. Center gap reserves an empty inner radius (fraction of clump radius). Placement irregularity blends even spiral spacing into random placement. Shape variation changes dimensions and orientation, while retaining the shared gradient.");
            IntSlider(layout,"Blade count",1,128,grassRecipe.bladeCount,x => grassRecipe.bladeCount = x,"Number of blades in one exported clump.");
            Slider(layout,"Clump radius (m)",.01f,1,grassRecipe.radius,x => grassRecipe.radius = x);
            Slider(layout,"Center gap",0,1,grassRecipe.gap,x => grassRecipe.gap = x);
            Slider(layout,"Placement irregularity",0,1,grassRecipe.irregularity,x => grassRecipe.irregularity = x);
            Slider(layout,"Height variation",0,.8f,grassRecipe.heightVariation,x => grassRecipe.heightVariation = x);
            Slider(layout,"Width variation",0,.8f,grassRecipe.widthVariation,x => grassRecipe.widthVariation = x);
            Slider(layout,"Direction variation",0,1,grassRecipe.directionVariation,x => grassRecipe.directionVariation = x);
            var quality = Fold(controls,"Mesh quality");
            AddTip(quality,"Length subdivisions resolve taper, belly and bends. Width subdivisions resolve the center fold. Folded blades use an even width count of at least two to retain the central ridge. Changing quality keeps seeded placement and proportions stable. Mesh overlay shows actual triangles. Thick blades require additional faces.");
            IntSlider(quality,"Length subdivisions",1,24,grassRecipe.lengthSegments,x => grassRecipe.lengthSegments = x,"Segments along each blade.");
            IntSlider(quality,"Width subdivisions",1,8,grassRecipe.widthSegments,x => grassRecipe.widthSegments = x,"Segments across each blade; center fold rounds up to an even number.");
            var patch = Fold(controls,"Repeated patch study");
            AddTip(patch,"Preview only. Repeats seeded clumps on a square grid to inspect visible repetition and gaps. Spacing controls overlap; position jitter breaks up rows. All blades use the same gradient. Export always saves one clump.");
            IntSlider(patch,"Clumps per side",1,5,grassRecipe.patchCount,x => grassRecipe.patchCount = x,"A 3 × 3 study shows nine clumps.");
            Slider(patch,"Clump spacing (m)",.02f,2,grassRecipe.patchSpacing,x => grassRecipe.patchSpacing = x);
            Slider(patch,"Position jitter",0,1,grassRecipe.patchJitter,x => grassRecipe.patchJitter = x);
            Action(controls,"Reset grass settings",() => { grassRecipe = new GrassRecipe(); grassStudy = GrassStudy.Blade; ShowTab(1); });
            var panel = BuildPreviewPanel();
            var studies = new List<string> { "Blade", "Clump", "Repeated patch" };
            var study = new PopupField<string>("Shape study",studies,(int)grassStudy);
            study.RegisterValueChangedCallback(e => { grassStudy = (GrassStudy)study.index; Changed(); });
            builderPage.Q("previewOptions").Add(study);
            var export = Box(panel,"card");
            Text(export,"Generate grass clump","section-title");
            Text(export,"Saves one clump with its material and wave settings. In Play mode, grass responds to active Wind Zones, including Terrain details. Automatic LODs are deferred.","small");
            Action(export,"Generate clump prefab",() =>
            {
                try
                {
                    lastGrassPrefab = GrassExporter.Generate(GrassExportRecipe(),out lastGrassFolder);
                    grassStatus = "Generated " + lastGrassFolder;
                    EditorGUIUtility.PingObject(lastGrassPrefab);
                }
                catch (Exception e) { grassStatus = "Generation failed: " + e.Message; Debug.LogException(e); }
                ShowTab(1);
            },true);
            if (!string.IsNullOrEmpty(grassStatus)) Text(export,grassStatus,"small");
            if (lastGrassPrefab != null)
            {
                Action(export,"Locate prefab",() => { Selection.activeObject = lastGrassPrefab; EditorGUIUtility.PingObject(lastGrassPrefab); });
                var target = new ObjectField("Target Terrain") { objectType = typeof(Terrain), allowSceneObjects = true, value = exportTerrain };
                target.RegisterValueChangedCallback(e => exportTerrain = e.newValue as Terrain); export.Add(target);
                Action(export,"Add clump to Terrain details",() =>
                {
                    try { grassStatus = GrassExporter.AddToTerrain(exportTerrain,lastGrassPrefab) ? "Added to Paint Details. Select the Terrain to paint grass." : "This clump is already in the detail palette."; ShowTab(1); }
                    catch (Exception e) { EditorUtility.DisplayDialog("Cannot add grass",e.Message,"OK"); }
                });
            }
            Changed();
        }
        void GrassChanged()
        {
            grassRecipe.Validate(); preview?.BuildGrass(grassRecipe,grassStudy);
            if (previewCaption != null) previewCaption.text = grassRecipe.style + " / " + (grassStudy == GrassStudy.RepeatedPatch ? "Repeated patch" : grassStudy.ToString());
            UpdateMeshStats("Grass"); previewElement?.MarkDirtyRepaint(); Repaint();
        }
    }
}
