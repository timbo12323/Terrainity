using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    public sealed partial class TerrainityWindow : EditorWindow
    {
        [SerializeField] TreeRecipe recipe = new TreeRecipe();
        [SerializeField] int currentTab;
        [SerializeField] string category = "Trees";
        [SerializeField] int variant;
        [SerializeField] bool exportTrunkCollider;
        [SerializeField] TreeLodSettings exportLods = new TreeLodSettings();
        [SerializeField] Terrain exportTerrain;
        [SerializeField] string lastExportFolder;
        [SerializeField] GameObject[] lastExportPrefabs = Array.Empty<GameObject>();
        [SerializeField] string exportStatus;
        [SerializeField] float previewHeight = 320;
        [SerializeField] float parameterWidth = 355;
        [SerializeField] PreviewEnvironmentSettings previewEnvironment = new PreviewEnvironmentSettings();
        [SerializeField] PreviewLightingSettings previewLighting = new PreviewLightingSettings();
        [SerializeField] bool showPreviewLighting = true;
        [SerializeField] PreviewWindSettings previewWind = new PreviewWindSettings();
        [SerializeField] bool showPreviewWind;
        double nextWindRepaint;
        [SerializeField] bool showMeshOverlay;
        [SerializeField] int previewMode;
        [SerializeField] bool footerShowsVertices;
        [SerializeField] bool pruneMode;
        Texture2D pruneCursor;
        bool pruneCursorActive;
        VisualElement homePage, builderPage, libraryPage, builderSlot, librarySlot;
        bool builderInteractionsBound, previewResizeBound;
        TreePreview preview;
        VisualElement previewElement;
        ScrollView parameterScroll;
        Foldout trunkControls, rootControls, branchControls, foliageControls;
        Label previewCaption;
        Label meshStats;
        Label footerDelta;
        int previousTreeTriangles, previousTreeVertices, previousMetricVariant, previousMetricMode, previousMetricLod;
        int triangleDelta, vertexDelta;
        bool hasPreviousTreeMetric;
        int previewStatsFlashVersion;
        Label lodPreviewLabel;
        Button livePreviewTab, lodPreviewTab;
        int previewLodLevel = 1;
        Label simplificationStats;
        readonly Dictionary<string, bool> sectionStates = new Dictionary<string, bool>();
        readonly List<Button> tabs = new List<Button>();
        const string GeneratedRoot = "Assets/TerrainityGenerated";

        [MenuItem("Tools/Terrainity/Foliage Studio")]
        public static void Open()
        {
            var window = GetWindow<TerrainityWindow>();
            window.titleContent = new GUIContent("Terrainity");
            window.minSize = new Vector2(780, 580);
            window.Show();
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed += RestoreUndoSettings;
            EditorApplication.update += UpdateWindPreview;
            if (PreviewPreferences.instance.Restore(out var lighting, out var environment, out bool visible))
            {
                previewLighting = lighting;
                previewEnvironment = environment;
                showPreviewLighting = visible;
            }
            else SavePreviewPreferences();
            if (PreviewPreferences.instance.RestoreWind(out var wind, out bool windVisible))
            {
                previewWind = wind;
                showPreviewWind = windVisible;
            }
        }

        void SavePreviewPreferences()
        {
            PreviewPreferences.instance.StoreWind(previewWind, showPreviewWind);
            PreviewPreferences.instance.Store(previewLighting, previewEnvironment, showPreviewLighting);
        }

        void UpdateWindPreview()
        {
            if (currentTab != 1 || (category != "Trees" && category != "Grass") || previewElement == null || preview == null || !preview.WindAnimating) return;
            double now = EditorApplication.timeSinceStartup;
            if (now < nextWindRepaint) return;
            nextWindRepaint = now + 1.0 / 30;
            previewElement.MarkDirtyRepaint();
            Repaint();
        }
        void OnDisable()
        {
            ClearPruneCursor();
            if (pruneCursor != null) DestroyImmediate(pruneCursor);
            pruneCursor = null;
            StopReleaseCheck();
            EndUndoDrag();
            Undo.undoRedoPerformed -= RestoreUndoSettings;
            EditorApplication.update -= UpdateWindPreview;
            CancelPreviewUpdate();
            SavePreviewPreferences();
            PreviewPreferences.instance.Flush();
            preview?.Dispose();
            preview = null;
            previewStatsFlashVersion++;
        }

        public void CreateGUI()
        {
            InstallUndoInput();
            rootVisualElement.Clear();
            tabs.Clear();
            builderInteractionsBound = previewResizeBound = false;
            var path = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            var directory = Path.GetDirectoryName(path).Replace('\\', '/');
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(directory + "/TerrainityWindow.uxml");
            var builderLayout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(directory + "/TerrainityBuilder.uxml");
            var libraryLayout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(directory + "/TerrainityLibrary.uxml");
            if (layout == null)
            {
                rootVisualElement.Add(new HelpBox("TerrainityWindow.uxml must stay beside TerrainityWindow.cs.", HelpBoxMessageType.Error));
                return;
            }
            if (builderLayout == null || libraryLayout == null)
            {
                rootVisualElement.Add(new HelpBox("TerrainityBuilder.uxml and TerrainityLibrary.uxml must stay beside TerrainityWindow.cs.", HelpBoxMessageType.Error));
                return;
            }
            layout.CloneTree(rootVisualElement);
            builderSlot = rootVisualElement.Q("builderSlot");
            librarySlot = rootVisualElement.Q("librarySlot");
            builderLayout.CloneTree(builderSlot);
            libraryLayout.CloneTree(librarySlot);
            homePage = rootVisualElement.Q("homePage");
            latestReleaseLabel = rootVisualElement.Q<Label>("latestRelease");
            CheckLatestRelease();
            builderPage = rootVisualElement.Q("builderPage");
            libraryPage = rootVisualElement.Q("libraryPage");
            var verticesToggle = rootVisualElement.Q<Toggle>("footerVertices");
            footerDelta = rootVisualElement.Q<Label>("footerDelta");
            verticesToggle.SetValueWithoutNotify(footerShowsVertices);
            verticesToggle.RegisterValueChangedCallback(e => { footerShowsVertices = e.newValue; UpdateFooterDelta(); });
            UpdateFooterDelta();
            rootVisualElement.Q<Button>("startTreeButton").clicked += () => ShowTab(1);
            string[] tabNames = { "homeTab", "builderTab", "libraryTab" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int index = i;
                var button = rootVisualElement.Q<Button>(tabNames[i]);
                button.clicked += () => ShowTab(index);
                tabs.Add(button);
            }
            ShowTab(currentTab);
        }

        void ShowTab(int index)
        {
            if (builderPage == null || libraryPage == null) return;
            CancelPreviewUpdate();
            currentTab = Mathf.Clamp(index, 0, 2);
            homePage.EnableInClassList("hidden", currentTab != 0);
            builderSlot.EnableInClassList("hidden", currentTab != 1);
            librarySlot.EnableInClassList("hidden", currentTab != 2);
            previewElement = null;
            previewStatsFlashVersion++;
            parameterScroll = null;
            trunkControls = rootControls = branchControls = foliageControls = null;
            meshStats = null;
            simplificationStats = null;
            preview?.Dispose();
            preview = null;
            for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("selected", i == currentTab);
            if (currentTab == 1) BuildBuilder();
            else if (currentTab == 2) BuildLibrary();
            rootVisualElement.Q("footerMetrics")?.EnableInClassList("hidden", currentTab != 1 || category != "Trees");
        }

        static VisualElement Box(VisualElement parent, string classes)
        {
            var box = new VisualElement();
            foreach (var c in classes.Split(' ')) box.AddToClassList(c);
            parent.Add(box);
            return box;
        }
        static Label Text(VisualElement parent, string text, string css = "copy")
        {
            var label = new Label(text);
            label.AddToClassList(css);
            parent.Add(label);
            return label;
        }
        static Button Action(VisualElement parent, string title, System.Action action, bool primary = false)
        {
            var button = new Button(action) { text = title };
            if (primary) button.AddToClassList("primary");
            parent.Add(button);
            return button;
        }

        SliderInt IntSlider(VisualElement parent, string title, int min, int max, int value, Action<int> set, string tooltip)
        {
            var slider = new SliderInt(title, min, max) { value = value, showInputField = true, tooltip = tooltip };
            slider.RegisterValueChangedCallback(e => { int next = Mathf.Clamp(e.newValue, min, max); slider.SetValueWithoutNotify(next); set(next); Changed(); });
            parent.Add(slider);
            return slider;
        }
        Slider Slider(VisualElement parent, string title, float min, float max, float value, Action<float> set, string tooltip = null)
        {
            var slider = new Slider(title, min, max) { value = value, showInputField = true, tooltip = tooltip };
            slider.RegisterValueChangedCallback(e =>
            {
                float next = Mathf.Clamp(e.newValue, min, max);
                slider.SetValueWithoutNotify(next); // Keep typed values consistent with the stored limits.
                set(next); Changed();
            }); parent.Add(slider);
            return slider;
        }
        void ColorControl(VisualElement parent, string title, Color value, Action<Color> set)
        {
            var field = new ColorField(title) { value = value, showAlpha = false, hdr = false };
            field.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); parent.Add(field);
        }
        void MaterialToggle(VisualElement parent, string title, bool value, Action<bool> set)
        {
            var toggle = new Toggle(title) { value = value };
            toggle.RegisterValueChangedCallback(e => { set(e.newValue); Changed(); }); parent.Add(toggle);
        }
        bool previewUpdatePending;
        double nextPreviewUpdate;
        double lastPreviewUpdate;

        void CancelPreviewUpdate()
        {
            previewUpdatePending = false;
            EditorApplication.update -= UpdatePendingPreview;
        }
        void Changed()
        {
            if (preview == null) return;
            if (preview.Meshes.Count == 0) { RebuildPreview(); return; }
            if (previewUpdatePending) return;
            previewUpdatePending = true;
            nextPreviewUpdate = Math.Max(EditorApplication.timeSinceStartup, lastPreviewUpdate + .12);
            EditorApplication.update += UpdatePendingPreview;
        }
        void UpdatePendingPreview()
        {
            if (EditorApplication.timeSinceStartup < nextPreviewUpdate) return;
            CancelPreviewUpdate();
            if (preview != null) RebuildPreview();
        }
        void RebuildPreview()
        {
            lastPreviewUpdate = EditorApplication.timeSinceStartup;
            if (category == "Rocks") { RockChanged(); return; }
            if (category == "Grass") { GrassChanged(); return; }
            if (previewMode == 1 && exportLods != null && exportLods.enabled)
                preview?.BuildLod(recipe, variant, previewLodLevel, exportLods);
            else preview?.Build(recipe, variant);
            if (previewCaption != null) previewCaption.text = recipe.species + " / Sibling " + (variant + 1) + " of " + recipe.variantCount;
            UpdateMeshStats("Wood");
            UpdatePreviewModeUI();
            RefreshLodLevel();
            previewElement?.MarkDirtyRepaint();
        }

        void UpdateMeshStats(string geometryLabel)
        {
            if (meshStats == null || preview == null) return;
            if (simplificationStats != null)
                simplificationStats.text = $"{preview.RemovedWoodTriangles:N0} tris\n{preview.RemovedWoodVertices:N0} verts removed";
            int vertices = preview.Meshes.Sum(mesh => mesh.vertexCount);
            meshStats.text = $"Triangle Count: {preview.WoodTriangles + preview.FoliageTriangles:N0}\n"
                + (category == "Grass" ? $"Grass: {preview.WoodTriangles:N0}\n" : $"{geometryLabel}: {preview.WoodTriangles:N0} • Foliage: {preview.FoliageTriangles:N0}\n")
                + $"Vertex Count: {vertices:N0}";
            if (category != "Trees") return;
            int triangles = preview.WoodTriangles + preview.FoliageTriangles;
            bool samePreview = hasPreviousTreeMetric && previousMetricVariant == variant
                && previousMetricMode == previewMode && previousMetricLod == (previewMode == 1 ? previewLodLevel : 0);
            triangleDelta = samePreview ? triangles - previousTreeTriangles : 0;
            vertexDelta = samePreview ? vertices - previousTreeVertices : 0;
            previousTreeTriangles = triangles;
            previousTreeVertices = vertices;
            previousMetricVariant = variant;
            previousMetricMode = previewMode;
            previousMetricLod = previewMode == 1 ? previewLodLevel : 0;
            hasPreviousTreeMetric = true;
            UpdateFooterDelta();
            FlashPreviewStats(footerShowsVertices ? vertexDelta : triangleDelta);
        }

        void UpdateFooterDelta()
        {
            if (footerDelta == null) return;
            int delta = footerShowsVertices ? vertexDelta : triangleDelta;
            footerDelta.text = (delta > 0 ? "+" : "") + $"{delta:N0} "
                + (footerShowsVertices ? "Vertices" : "Triangles");
            footerDelta.EnableInClassList("footer-delta-increase", delta > 0);
            footerDelta.EnableInClassList("footer-delta-decrease", delta < 0);
        }

        void FlashPreviewStats(int delta)
        {
            if (meshStats == null) return;
            var label = meshStats;
            int version = ++previewStatsFlashVersion;
            label.EnableInClassList("preview-stats-increase", delta > 0);
            label.EnableInClassList("preview-stats-decrease", delta < 0);
            if (delta == 0) return;
            label.schedule.Execute(() =>
            {
                if (version != previewStatsFlashVersion || label != meshStats) return;
                label.RemoveFromClassList("preview-stats-increase");
                label.RemoveFromClassList("preview-stats-decrease");
            }).ExecuteLater(650);
        }

    }
}
