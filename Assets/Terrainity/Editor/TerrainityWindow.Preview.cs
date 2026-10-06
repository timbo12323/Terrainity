using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Terrainity.Editor
{
    // Preview layout, navigation and pruning gestures.
    public sealed partial class TerrainityWindow
    {
        VisualElement BuildPreviewPanel()
        {
            if (exportLods == null) exportLods = new TreeLodSettings();
            if (category != "Trees" || !exportLods.enabled) previewMode = 0;
            var content = builderPage.Q("builderPreviewContent");
            content.RemoveFromClassList("hidden");
            var placeholder = builderPage.Q("builderPlaceholder");
            placeholder.AddToClassList("hidden");
            placeholder.Clear();
            var help = builderPage.Q("previewHelp");
            help.Clear();
            AddHeaderHelp(help, "Left-drag to orbit. Middle-drag or Shift + left-drag to pan. Scroll to zoom. Reset view recenters the camera. " + (category == "Trees" ? "Shift-click a tree part to highlight it and open its controls." : "Use the mesh overlay to inspect the generated rock topology."));
            var previewTabs = builderPage.Q("previewTabs");
            previewTabs.Clear();
            livePreviewTab = new Button(() => SetPreviewMode(0)) { text = "Live preview" };
            lodPreviewTab = new Button(() => SetPreviewMode(1)) { text = "LOD preview" };
            previewTabs.Add(livePreviewTab);
            previewTabs.Add(lodPreviewTab);
            previewCaption = builderPage.Q<Label>("previewCaption");
            var viewOptions = builderPage.Q("previewOptions");
            viewOptions.Clear();
            if (category == "Trees")
            {
            var foliage = new Toggle("Show foliage") { value = recipe.showFoliage };
            foliage.AddToClassList("preview-option");
            foliage.tooltip = "Hide foliage to inspect limb placement, forks and curvature.";
            foliage.RegisterValueChangedCallback(e => { recipe.showFoliage = e.newValue; Changed(); }); viewOptions.Add(foliage);
            }
            preview = new TreePreview();
            preview.AllowFarZoom(previewMode == 1);
            preview.ShowMeshOverlay = showMeshOverlay;
            var meshOverlay = new Toggle("Mesh overlay") { value = showMeshOverlay, tooltip = "Overlay triangle edges on the shaded preview, including the current surface detail and simplification. Hide foliage to inspect the wood. Preview only; not exported." };
            meshOverlay.AddToClassList("preview-option");
            meshOverlay.RegisterValueChangedCallback(e => { showMeshOverlay = e.newValue; preview.ShowMeshOverlay = e.newValue; previewElement?.MarkDirtyRepaint(); Repaint(); });
            viewOptions.Add(meshOverlay);
            preview.SetView(previewMode == 1 ? lodPreviewOrbit : previewOrbit,
                previewMode == 1 ? lodPreviewPan : previewPan);
            if (previewLighting == null) previewLighting = new PreviewLightingSettings();
            preview.SetLighting(previewLighting);
            if (previewEnvironment == null) previewEnvironment = new PreviewEnvironmentSettings();
            preview.SetEnvironment(previewEnvironment);
            var previewStage = builderPage.Q("previewStage");
            var previewHost = builderPage.Q("previewImageHost");
            previewHost.Clear();
            previewElement = new VisualElement { tooltip = category == "Trees"
                ? "Shift-click a trunk, root, branch, or foliage card to highlight it and jump to its controls. Shift-drag still pans the view."
                : "Drag to orbit, Shift-drag to pan, and scroll to zoom. Use Mesh overlay to inspect the rock." };
            previewElement.style.flexGrow = 1;
            previewElement.RegisterCallback<GeometryChangedEvent>(_ => RefreshLodLevel());
            previewHost.Add(previewElement);
            var previewImage = new IMGUIContainer(() => preview?.Draw(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))))
            { pickingMode = PickingMode.Ignore };
            previewImage.style.flexGrow = 1;
            previewElement.Add(previewImage);
            meshStats = new Label { name = "previewStats", pickingMode = PickingMode.Ignore };
            meshStats.AddToClassList("preview-stats");
            previewHost.Add(meshStats);
            lodPreviewLabel = new Label { pickingMode = PickingMode.Ignore };
            lodPreviewLabel.AddToClassList("preview-lod-label");
            previewHost.Add(lodPreviewLabel);
            if (category == "Trees") AddFloorHeightOverlay(previewHost);
            if (category == "Trees")
            {
                var resetHost = new VisualElement { pickingMode = PickingMode.Ignore };
                resetHost.AddToClassList("reset-prune-host");
                var resetPrune = new Button(() =>
                {
                    if (recipe.prunedFoliageCards == null || recipe.prunedFoliageCards.Count == 0) return;
                    RecordSettingsUndo();
                    recipe.prunedFoliageCards.Clear();
                    Changed();
                }) { text = "Reset prune", tooltip = "Restore every foliage card hidden with Prune Tree." };
                resetPrune.AddToClassList("reset-prune");
                resetPrune.EnableInClassList("hidden", !pruneMode);
                resetHost.Add(resetPrune);
                previewHost.Add(resetHost);
            }
            UpdatePruneModeUI();
            UpdatePreviewModeUI();
            ConfigurePreviewInput(previewElement, previewImage);
            for (int i = previewStage.childCount - 1; i >= 1; i--) previewStage.RemoveAt(i);
            var lightingPanel = new PreviewLightingPanel(previewLighting, () => { SavePreviewPreferences(); preview.SetLighting(previewLighting); previewImage.MarkDirtyRepaint(); Repaint(); });
            lightingPanel.Add(new PreviewEnvironmentPanel(previewEnvironment, () => { SavePreviewPreferences(); preview.SetEnvironment(previewEnvironment); previewImage.MarkDirtyRepaint(); Repaint(); }));
            previewStage.Add(lightingPanel);
            lightingPanel.style.display = showPreviewLighting ? DisplayStyle.Flex : DisplayStyle.None;
            previewHeight = Mathf.Clamp(previewHeight, 180, 1600);
            previewStage.style.height = previewHeight;
            var resizeHandle = builderPage.Q<Label>("previewResizeHandle");
            if (!previewResizeBound)
            {
            previewResizeBound = true;
            bool resizing = false;
            float resizeStartY = 0, resizeStartHeight = 0;
            resizeHandle.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount == 2)
                {
                    previewHeight = 320; previewStage.style.height = previewHeight;
                }
                else
                {
                    resizing = true; resizeStartY = e.mousePosition.y; resizeStartHeight = previewHeight;
                    resizeHandle.CaptureMouse();
                }
                e.StopPropagation();
            });
            resizeHandle.RegisterCallback<MouseMoveEvent>(e =>
            {
                if (!resizing) return;
                previewHeight = Mathf.Clamp(resizeStartHeight + e.mousePosition.y - resizeStartY, 180, 1600);
                previewStage.style.height = previewHeight;
                previewElement?.MarkDirtyRepaint(); Repaint(); e.StopPropagation();
            });
            resizeHandle.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button != 0 || !resizing) return;
                resizing = false; resizeHandle.ReleaseMouse(); e.StopPropagation();
            });
            resizeHandle.RegisterCallback<MouseCaptureOutEvent>(_ => resizing = false);
            }

            var viewButtons = builderPage.Q("previewButtons");
            viewButtons.Clear();
            var lightingToggle = new Toggle("Lighting") { value = showPreviewLighting };
            lightingToggle.AddToClassList("preview-option");
            lightingToggle.RegisterValueChangedCallback(e => { showPreviewLighting = e.newValue; SavePreviewPreferences(); lightingPanel.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None; });
            viewOptions.Add(lightingToggle);
            Action(viewButtons, "Next sibling", () => { variant = (variant + 1) % (category == "Rocks" ? rockRecipe.variantCount : recipe.variantCount); Changed(); });
            Action(viewButtons, "Reset view", () => { preview.ResetView(); CapturePreviewView(); RefreshLodLevel(); previewElement.MarkDirtyRepaint(); });
            var exportHost = builderPage.Q("previewExport");
            exportHost.Clear();
            return exportHost;
        }

        void UpdatePreviewModeUI()
        {
            bool available = category == "Trees" && exportLods != null && exportLods.enabled;
            builderPage?.Q("previewTabs")?.EnableInClassList("hidden", !available);
            livePreviewTab?.EnableInClassList("selected", previewMode == 0);
            lodPreviewTab?.EnableInClassList("selected", previewMode == 1);
            var heading = builderPage?.Q<Label>("previewHeading");
            if (heading != null) heading.text = previewMode == 1 ? "LOD SHAPE STUDY" : "LIVE SHAPE STUDY";
            if (lodPreviewLabel != null)
            {
                lodPreviewLabel.text = "LOD" + previewLodLevel;
                lodPreviewLabel.EnableInClassList("hidden", previewMode != 1);
            }
            if (previewElement != null && category == "Trees")
                previewElement.tooltip = previewMode == 1
                    ? "Drag to orbit, Shift-drag to pan, and scroll farther out to switch from LOD1 to LOD2."
                    : pruneMode ? "Click a foliage card to hide it, or a branch to hide foliage on that branch and its offshoots. Drag to orbit, Shift-drag to pan, and scroll to zoom."
                    : "Shift-click a trunk, root, branch, or foliage card to highlight it and jump to its controls. Shift-drag still pans the view.";
            var help = builderPage?.Q("previewHelp")?.Q<Label>(className: "header-help");
            if (help != null && category == "Trees")
                help.tooltip = previewMode == 1
                    ? "Left-drag to orbit. Middle-drag or Shift + left-drag to pan. Scroll to zoom and switch LOD levels. Reset view recenters the camera."
                    : pruneMode ? "Click a card to prune it or a branch to prune its foliage and offshoot foliage. Left-drag to orbit. Middle-drag or Shift + left-drag to pan. Reset prune restores all hidden cards."
                    : "Left-drag to orbit. Middle-drag or Shift + left-drag to pan. Scroll to zoom. Reset view recenters the camera. Shift-click a tree part to highlight it and open its controls.";
        }

        void SetPreviewMode(int mode)
        {
            mode = mode == 1 && category == "Trees" && exportLods != null && exportLods.enabled ? 1 : 0;
            if (mode == 1 && pruneMode)
            {
                pruneMode = false;
                builderPage?.Q<Toggle>(className: "prune-tree-toggle")?.SetValueWithoutNotify(false);
                UpdatePruneModeUI();
            }
            if (previewMode == mode) return;
            CapturePreviewView();
            previewMode = mode;
            preview?.AllowFarZoom(mode == 1);
            preview?.SetView(mode == 1 ? lodPreviewOrbit : previewOrbit,
                mode == 1 ? lodPreviewPan : previewPan);
            UpdatePreviewModeUI();
            RebuildPreview();
            RefreshLodLevel();
        }

        void RefreshLodLevel()
        {
            if (previewMode != 1 || preview == null || preview.Meshes.Count == 0 || previewElement == null) return;
            int level = preview.ScreenHeightFraction(previewElement.contentRect.size) <= exportLods.lod2Start ? 2 : 1;
            if (level == previewLodLevel) return;
            previewLodLevel = level;
            UpdatePreviewModeUI();
            Changed();
        }

        void AddFloorHeightOverlay(VisualElement previewHost)
        {
            var overlay = new VisualElement { tooltip = "Floor height sets the placement ground line. Positive values bury more of the tree; negative values expose more. The offset is baked into exported meshes and LODs." };
            overlay.AddToClassList("floor-height-overlay");
            var pruneRow = new VisualElement { tooltip = "Click a foliage card to hide it, or click a branch to hide all foliage on that branch and its offshoots." };
            pruneRow.AddToClassList("prune-tree-row");
            var prune = new Toggle { value = pruneMode };
            prune.AddToClassList("prune-tree-toggle");
            prune.RegisterValueChangedCallback(e => SetPruneMode(e.newValue));
            var pruneLabel = new Label("Prune Tree");
            pruneLabel.AddToClassList("prune-tree-label");
            pruneLabel.RegisterCallback<ClickEvent>(_ => prune.value = !prune.value);
            pruneRow.Add(prune);
            pruneRow.Add(pruneLabel);
            overlay.Add(pruneRow);
            var floorRow = new VisualElement();
            floorRow.AddToClassList("floor-height-row");
            var track = new VisualElement();
            track.AddToClassList("floor-height-track");
            var slider = new SliderInt(-20, 20, SliderDirection.Vertical, 1f) { value = Mathf.RoundToInt(recipe.floorHeight * 10f) };
            slider.AddToClassList("floor-height-slider");
            var labels = new VisualElement();
            labels.AddToClassList("floor-height-labels");
            labels.Add(new Label("FLOOR"));
            var value = new Label($"{recipe.floorHeight:0.00} m");
            labels.Add(value);
            slider.RegisterValueChangedCallback(e =>
            {
                float height = e.newValue / 10f;
                recipe.floorHeight = height;
                value.text = $"{height:0.0} m";
                Changed();
            });
            track.Add(slider);
            floorRow.Add(track);
            floorRow.Add(labels);
            overlay.Add(floorRow);
            previewHost.Add(overlay);
        }

        void SetPruneMode(bool enabled)
        {
            if (enabled && previewMode != 0) SetPreviewMode(0);
            pruneMode = enabled;
            UpdatePruneModeUI();
        }

        void UpdatePruneModeUI()
        {
            bool active = pruneMode && category == "Trees" && previewMode == 0;
            var controls = builderPage?.Q("builderControls");
            var divider = builderPage?.Q("parameterDivider");
            if (controls != null) controls.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            if (divider != null) divider.style.display = active ? DisplayStyle.None : DisplayStyle.Flex;
            builderPage?.Q("previewImageHost")?.Q<Button>(className: "reset-prune")?.EnableInClassList("hidden", !active);
            if (!active) ClearPruneCursor();
            UpdatePreviewModeUI();
        }

        void ShowPruneCursor()
        {
            if (!pruneMode || category != "Trees" || previewMode != 0) return;
            if (pruneCursor == null)
            {
                pruneCursor = new Texture2D(32, 32, TextureFormat.RGBA32, false)
                    { name = "Terrainity prune scissors cursor", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                var pixels = new Color32[32 * 32];
                void Dot(int x, int y, Color32 color)
                {
                    if (x >= 0 && x < 32 && y >= 0 && y < 32) pixels[y * 32 + x] = color;
                }
                void Line(int x0, int y0, int x1, int y1, Color32 color)
                {
                    int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = steps == 0 ? 0 : (float)i / steps;
                        int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                        Dot(x, y, color); Dot(x + 1, y, color);
                    }
                }
                var blade = new Color32(239, 245, 240, 255);
                var grip = new Color32(113, 204, 145, 255);
                Line(3, 28, 19, 12, blade); Line(3, 12, 19, 20, blade);
                Dot(16, 17, new Color32(35, 45, 39, 255));
                for (int ring = 0; ring < 2; ring++)
                {
                    int cy = ring == 0 ? 8 : 23;
                    for (int a = 0; a < 36; a++)
                    {
                        float angle = a * Mathf.PI * 2 / 36;
                        Dot(24 + Mathf.RoundToInt(Mathf.Cos(angle) * 4), cy + Mathf.RoundToInt(Mathf.Sin(angle) * 4), grip);
                    }
                    Line(18, ring == 0 ? 14 : 19, 21, cy, grip);
                }
                pruneCursor.SetPixels32(pixels); pruneCursor.Apply(false, false);
            }
            if (previewElement != null)
            {
                var cursor = new UnityEngine.UIElements.Cursor { texture = pruneCursor, hotspot = new Vector2(3, 3) };
                previewElement.style.cursor = cursor;
                var image = previewElement.Q<IMGUIContainer>();
                if (image != null) image.style.cursor = cursor;
            }
            pruneCursorActive = true;
        }

        void ClearPruneCursor()
        {
            if (!pruneCursorActive) return;
            if (previewElement != null)
            {
                previewElement.style.cursor = StyleKeyword.Null;
                var image = previewElement.Q<IMGUIContainer>();
                if (image != null) image.style.cursor = StyleKeyword.Null;
            }
            pruneCursorActive = false;
        }

        Foldout Fold(VisualElement parent, string title)
        {
            var section = Box(parent, "builder-section");
            var fold = new Foldout { text = title, value = !sectionStates.TryGetValue(title, out var open) || open };
            fold.AddToClassList("builder-foldout");
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) sectionStates[title] = e.newValue; });
            section.Add(fold);
            var help = new Label("?") { tooltip = title, pickingMode = PickingMode.Position };
            help.AddToClassList("section-help"); section.Add(help); fold.userData = help;
            return fold;
        }

        static void AddTip(Foldout section, string tip)
        {
            var help = (Label)section.userData;
            help.tooltip += "\n\n" + tip;
        }

        static void AddHeaderHelp(VisualElement header, string tip)
        {
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);
            var help = new Label("?") { tooltip = tip };
            help.AddToClassList("header-help"); header.Add(help);
        }

        void ConfigurePreviewInput(VisualElement viewport, IMGUIContainer image)
        {
            int dragButton = -1;
            bool pan = false;
            bool picking = false;
            bool pruning = false;
            bool dragged = false;
            Vector2 previous = Vector2.zero;
            Vector2 pressPosition = Vector2.zero;
            void RefreshView() { CapturePreviewView(); image.MarkDirtyRepaint(); Repaint(); }
            viewport.RegisterCallback<MouseEnterEvent>(_ => ShowPruneCursor());
            viewport.RegisterCallback<MouseLeaveEvent>(_ => ClearPruneCursor());
            viewport.RegisterCallback<MouseDownEvent>(e =>
            {
                if (dragButton != -1 || (e.button != 0 && e.button != 2)) return;
                dragButton = e.button; pan = e.button == 2 || e.shiftKey; previous = pressPosition = e.mousePosition;
                picking = e.button == 0 && e.shiftKey && category == "Trees" && previewMode == 0;
                pruning = e.button == 0 && !e.shiftKey && pruneMode && category == "Trees" && previewMode == 0;
                dragged = false;
                viewport.CaptureMouse(); e.StopPropagation();
            });
            viewport.RegisterCallback<MouseMoveEvent>(e =>
            {
                if (dragButton == -1 || !viewport.HasMouseCapture()) return;
                var delta = e.mousePosition - previous; previous = e.mousePosition;
                if ((e.mousePosition - pressPosition).sqrMagnitude > 9) dragged = true;
                if (pan) preview?.Pan(delta, viewport.contentRect.size); else preview?.Orbit(delta);
                RefreshView(); e.StopPropagation();
            });
            viewport.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button != dragButton) return;
                if (pruning && !dragged && preview != null)
                {
                    var target = preview.PickPruneTarget(viewport.WorldToLocal(e.mousePosition), viewport.contentRect.size,
                        out int cardId, out int limbIndex);
                    IReadOnlyList<int> cards = target == TreePreviewPart.Branches && limbIndex >= 0
                        ? preview.CardsOnBranch(limbIndex) : null;
                    if (target == TreePreviewPart.Foliage && cardId >= 0 || cards != null && cards.Count > 0)
                    {
                        RecordSettingsUndo();
                        if (recipe.prunedFoliageCards == null) recipe.prunedFoliageCards = new List<int>();
                        if (cards != null)
                        {
                            var existing = new HashSet<int>(recipe.prunedFoliageCards);
                            foreach (int id in cards) if (existing.Add(id)) recipe.prunedFoliageCards.Add(id);
                        }
                        else if (!recipe.prunedFoliageCards.Contains(cardId)) recipe.prunedFoliageCards.Add(cardId);
                        RebuildPreview();
                    }
                }
                if (picking && !dragged && preview != null)
                {
                    var part = preview.PickTreePart(viewport.WorldToLocal(e.mousePosition), viewport.contentRect.size);
                    if (part != TreePreviewPart.None)
                    {
                        NavigateToTreePart(part);
                        image.MarkDirtyRepaint(); Repaint();
                        viewport.schedule.Execute(() => { image.MarkDirtyRepaint(); Repaint(); }).ExecuteLater(1050);
                    }
                }
                picking = false;
                pruning = false;
                dragButton = -1; viewport.ReleaseMouse(); e.StopPropagation();
            });
            viewport.RegisterCallback<MouseCaptureOutEvent>(e => { dragButton = -1; picking = false; pruning = false; });
            viewport.RegisterCallback<WheelEvent>(e =>
            {
                preview?.Zoom(e.delta.y); RefreshLodLevel(); RefreshView(); e.StopPropagation();
            });
        }

        void NavigateToTreePart(TreePreviewPart part)
        {
            Foldout target = part == TreePreviewPart.Trunk ? trunkControls
                : part == TreePreviewPart.Roots ? rootControls
                : part == TreePreviewPart.Branches ? branchControls
                : part == TreePreviewPart.Foliage ? foliageControls : null;
            if (target == null || parameterScroll == null) return;
            target.value = true;
            var header = target.Q<VisualElement>(className: "unity-foldout__toggle") ?? target;
            var scroll = parameterScroll;
            scroll.schedule.Execute(() =>
            {
                if (scroll.panel == null || header.panel != scroll.panel) return;
                // ScrollTo only makes the heading visible, often leaving it at the bottom.
                // Place the chosen section at the top so its controls are immediately in view.
                float position = scroll.scrollOffset.y + header.worldBound.y - scroll.contentViewport.worldBound.y - 12;
                scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, Mathf.Max(0, position));
            }).ExecuteLater(20);
        }
    }
}
