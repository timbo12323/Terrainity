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
        [SerializeField] bool showMeshOverlay;
        VisualElement homePage, builderPage, libraryPage, builderSlot, librarySlot;
        bool builderInteractionsBound, previewResizeBound;
        TreePreview preview;
        VisualElement previewElement;
        ScrollView parameterScroll;
        Foldout trunkControls, rootControls, branchControls, foliageControls;
        Label previewCaption;
        Label meshStats;
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
            if (PreviewPreferences.instance.Restore(out var lighting, out var environment, out bool visible))
            {
                previewLighting = lighting;
                previewEnvironment = environment;
                showPreviewLighting = visible;
            }
            else SavePreviewPreferences();
        }

        void SavePreviewPreferences() => PreviewPreferences.instance.Store(previewLighting, previewEnvironment, showPreviewLighting);
        void OnDisable()
        {
            StopReleaseCheck();
            EndUndoDrag();
            Undo.undoRedoPerformed -= RestoreUndoSettings;
            CancelPreviewUpdate();
            SavePreviewPreferences();
            PreviewPreferences.instance.Flush();
            preview?.Dispose();
            preview = null;
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
            parameterScroll = null;
            trunkControls = rootControls = branchControls = foliageControls = null;
            meshStats = null;
            simplificationStats = null;
            preview?.Dispose();
            preview = null;
            for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("selected", i == currentTab);
            if (currentTab == 1) BuildBuilder();
            else if (currentTab == 2) BuildLibrary();
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

        void BuildBuilder()
        {
            var split = builderPage.Q("builderSplit");
            var controlsScroll = builderPage.Q<ScrollView>("builderControls");
            var controls = builderPage.Q("builderFields");
            controls.Clear();
            parameterScroll = controlsScroll;
            var divider = builderPage.Q("parameterDivider");
            float AvailableWidth() => Mathf.Max(310, Mathf.Min(800, split.contentRect.width - 328));
            void ApplyParameterWidth() => controlsScroll.style.width = Mathf.Clamp(parameterWidth, 310, AvailableWidth());
            controlsScroll.style.width = Mathf.Clamp(parameterWidth, 310, 800);
            if (!builderInteractionsBound)
            {
            builderInteractionsBound = true;
            bool resizingParameters = false;
            float startX = 0, startWidth = 0;
            split.RegisterCallback<GeometryChangedEvent>(_ => ApplyParameterWidth());
            divider.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount == 2) { parameterWidth = 355; ApplyParameterWidth(); }
                else
                {
                    resizingParameters = true; startX = e.mousePosition.x; startWidth = controlsScroll.resolvedStyle.width;
                    divider.CaptureMouse();
                }
                e.StopPropagation();
            });
            divider.RegisterCallback<MouseMoveEvent>(e =>
            {
                if (!resizingParameters) return;
                parameterWidth = Mathf.Clamp(startWidth + e.mousePosition.x - startX, 310, AvailableWidth());
                ApplyParameterWidth(); previewElement?.MarkDirtyRepaint(); Repaint(); e.StopPropagation();
            });
            divider.RegisterCallback<MouseUpEvent>(e =>
            {
                if (e.button != 0 || !resizingParameters) return;
                resizingParameters = false; divider.ReleaseMouse(); e.StopPropagation();
            });
            divider.RegisterCallback<MouseCaptureOutEvent>(_ => resizingParameters = false);
            }
            var type = new PopupField<string>("Asset type", new List<string> { "Trees", "Grass", "Rocks", "Bushes" }, category);
            type.RegisterValueChangedCallback(e => { category = e.newValue; variant = 0; ShowTab(1); }); controls.Add(type);
            if (category == "Rocks") { BuildRockBuilder(controls); return; }
            if (category != "Trees")
            {
                Text(controls, category + " • Coming next", "section-title");
                Text(controls, category == "Grass" ? "Planned controls: blade shape, height, clump density, bend and color." : category == "Rocks" ? "Planned controls: silhouette, size, surface roughness, erosion and material." : "Planned controls: branching, spread, leaf shape, density and color.");
                Text(controls, "Terrain destination: " + (category == "Grass" ? "Paint Details" : "Paint Trees / detail meshes"), "small");
                var placeholder = builderPage.Q("builderPlaceholder");
                builderPage.Q("builderPreviewContent").AddToClassList("hidden");
                placeholder.RemoveFromClassList("hidden");
                placeholder.Clear();
                Text(placeholder, category, "title"); Text(placeholder, "A new kind of wilderness is on its way.");
                Action(placeholder, "Back to trees", () => { category = "Trees"; ShowTab(1); });
                return;
            }
            var name = new TextField("Asset name") { value = recipe.assetName };
            name.RegisterValueChangedCallback(e => recipe.assetName = e.newValue); controls.Add(name);
            TreeJsonStorage.ReloadFamilies();
            var families = new List<TreeFamilyJson>(TreeJsonStorage.Families);
            if (recipe.useFamilyProfile && recipe.familyProfile != null && !families.Any(f => f.profile.id == recipe.Profile.id))
                families.Add(new TreeFamilyJson { profile = recipe.Profile });
            var familyNames = families.Select(f => f.profile.name + " (" + f.profile.id + ")").ToList();
            int familyIndex = Mathf.Max(0, families.FindIndex(f => f.profile.id == recipe.Profile.id));
            var species = new PopupField<string>("Tree family", familyNames, familyIndex);
            species.RegisterValueChangedCallback(e =>
            {
                var selected = families[species.index];
                try
                {
                    string warning = "";
                    var next = selected.defaults == null ? new TreeRecipe() : selected.defaults.Restore(out warning);
                    next.useFamilyProfile = true;
                    next.familyProfile = JsonUtility.FromJson<TreeFamilyProfile>(JsonUtility.ToJson(selected.profile));
                    next.species = selected.profile.name;
                    next.customBranches = JsonUtility.FromJson<BranchSettings>(JsonUtility.ToJson(selected.profile.branches));
                    next.assetName = "Woodland " + selected.profile.name;
                    recipe = next; variant = 0; ShowTab(1);
                    if (!string.IsNullOrEmpty(warning)) EditorUtility.DisplayDialog("Family loaded", warning, "OK");
                }
                catch (Exception ex) { EditorUtility.DisplayDialog("Cannot apply family", ex.Message, "OK"); }
            }); controls.Add(species);
            species.tooltip = "Selecting a family applies its default tree settings. Save your recipe first to keep the current setup.";
            var recipeFiles = Box(controls, "row");
            Action(recipeFiles, "Save recipe", SaveRecipeJson);
            Action(recipeFiles, "Load recipe", LoadRecipeJson);
            var familyFiles = Box(controls, "row");
            Action(familyFiles, "Save as family", SaveFamilyJson);
            Action(familyFiles, "Import family", ImportFamilyJson);
            var seed = new IntegerField("Seed") { value = recipe.seed };
            seed.RegisterValueChangedCallback(e => { recipe.seed = e.newValue; Changed(); }); controls.Add(seed);
            Action(controls, "New random seed", () => { recipe.seed = Guid.NewGuid().GetHashCode() & int.MaxValue; seed.SetValueWithoutNotify(recipe.seed); variant = 0; Changed(); }, true);
            var silhouette = Fold(controls, "Silhouette");
            Slider(silhouette, "Height (m)", 2, 18, recipe.height, x => recipe.height = x);
            Slider(silhouette, "Crown width (m)", 1, 9, recipe.crownWidth, x => recipe.crownWidth = x);
            Slider(silhouette, "Crown start fraction", .12f, .98f, recipe.crownStart, x => recipe.crownStart = x);
            var trunk = Fold(controls, "Trunk shape");
            trunkControls = trunk;
            Slider(trunk, "Trunk radius (m)", .06f, .7f, recipe.trunkRadius, x => recipe.trunkRadius = x);
            Slider(trunk, "Taper", .1f, .95f, recipe.taper, x => recipe.taper = x);
            Slider(trunk, "Lean", -1, 1, recipe.lean, x => recipe.lean = x);
            Slider(trunk, "Trunk bend", -1, 1, recipe.trunkBend, x => recipe.trunkBend = x);
            IntSlider(trunk, "Bend count", 1, 9, recipe.trunkBends, x => recipe.trunkBends = x,
                "One creates a broad arc; higher counts create more alternating bends, up to nine. Trunk bend controls their strength and direction.");
            Slider(trunk, "Bend sharpness", 0, 1, recipe.trunkSharpness, x => recipe.trunkSharpness = x);
            Slider(trunk, "Trunk twist (°)", -720, 720, recipe.trunkTwist, x => recipe.trunkTwist = x);
            AddTip(trunk, "Negative lean and trunk bend reverse their direction. Sharpness changes smooth bends into elbows. Twist rotates bends and branches from base to tip, up to two turns in either direction; use nonzero Trunk bend for a corkscrew shape.");
            AddTip(silhouette, "Set the overall outline first. Crown start is the fraction of trunk height below the first limbs.");
            var roots = Fold(controls, "Roots");
            rootControls = roots;
            MaterialToggle(roots, "Generate roots", recipe.rootsEnabled, x => recipe.rootsEnabled = x);
            IntSlider(roots, "Root count", 1, 12, recipe.rootCount, x => recipe.rootCount = x, "Roots distributed around the lower trunk.");
            IntSlider(roots, "Root forks", 0, 12, recipe.rootForks, x => recipe.rootForks = x, "First-generation offshoots distributed across the main roots. Zero keeps roots unforked. The level range controls further generations; roots assigned zero levels have no offshoots.");
            int rootMaxDepth = Mathf.Clamp(recipe.rootMaxForkDepth, 0, 3);
            int rootMinDepth = Mathf.Clamp(recipe.rootMinForkDepth, 0, rootMaxDepth);
            var rootDepthLabel = Text(roots, $"Forking levels: {rootMinDepth}–{rootMaxDepth}", "small");
            var rootDepthRange = new MinMaxSlider("Forking level range", rootMinDepth, rootMaxDepth, 0, 3);
            rootDepthRange.tooltip = "Each main root receives a seeded random number of fork generations between these inclusive limits. Equal limits give a fixed depth. Each further generation adds two smaller offshoots; up to 84 offshoots at maximum settings.";
            rootDepthRange.RegisterValueChangedCallback(e => {
                int low = Mathf.Clamp(Mathf.RoundToInt(e.newValue.x), 0, 3);
                int high = Mathf.Clamp(Mathf.RoundToInt(e.newValue.y), low, 3);
                recipe.rootMinForkDepth = low; recipe.rootMaxForkDepth = high;
                rootDepthRange.SetValueWithoutNotify(new Vector2(low, high)); rootDepthLabel.text = $"Forking levels: {low}–{high}"; Changed();
            });
            roots.Add(rootDepthRange);
            Slider(roots, "Root bend", 0, 1, recipe.rootBend, x => recipe.rootBend = x);
            Slider(roots, "Root bend sharpness", 0, 1, recipe.rootSharpness, x => recipe.rootSharpness = x);
            Slider(roots, "Root spread (m)", .2f, 5, recipe.rootSpread, x => recipe.rootSpread = x);
            Slider(roots, "Root thickness", .1f, .9f, recipe.rootThickness, x => recipe.rootThickness = x);
            Slider(roots, "Root taper", 0, .95f, recipe.rootTaper, x => recipe.rootTaper = x);
            Slider(roots, "Attachment height (m)", 0, 1.5f, recipe.rootAttachmentHeight, x => recipe.rootAttachmentHeight = x);
            Slider(roots, "Root depth (m)", 0, 2, recipe.rootDepth, x => recipe.rootDepth = x);
            AddTip(roots, "Roots follow the lower trunk's orientation, then fan outward and down. Attachment height is above the original trunk base (limited to the lowest quarter); depth is below that base. Roots share bark, detail and simplification settings. They do not conform to terrain slopes; the optional trunk collider does not cover roots.");
            var branches = Fold(controls, "Branches / " + recipe.species);
            branchControls = branches;
            AddTip(branches, recipe.Profile.description);
            var branchSettings = recipe.Branches;
            IntSlider(branches, "Main limbs", 0, 24, branchSettings.limbs, x => branchSettings.limbs = x,
                "Number of limbs attached directly to the trunk. Zero leaves a bare trunk.");
            IntSlider(branches, "Sub-branches per limb", 1, 4, branchSettings.subBranches, x => branchSettings.subBranches = x,
                "Adds children from tip toward the root: 100%, 75%, 50%, then 25% along each parent. Set forking levels to 0–0 for no sub-branches.");
            int maxDepth = Mathf.Clamp(branchSettings.depth, 0, 3);
            int minDepth = branchSettings.minDepth < 0 ? maxDepth : Mathf.Clamp(branchSettings.minDepth, 0, maxDepth);
            var depthLabel = Text(branches, $"Forking levels: {minDepth}–{maxDepth}", "small");
            var depthRange = new MinMaxSlider("Forking level range", minDepth, maxDepth, 0, 3);
            depthRange.tooltip = "Drag either handle. Each main limb receives a seeded random number of extra generations between these inclusive limits. Equal limits give a fixed depth.";
            depthRange.RegisterValueChangedCallback(e => {
                int low = Mathf.Clamp(Mathf.RoundToInt(e.newValue.x), 0, 3);
                int high = Mathf.Clamp(Mathf.RoundToInt(e.newValue.y), low, 3);
                branchSettings.minDepth = low; branchSettings.depth = high;
                depthRange.SetValueWithoutNotify(new Vector2(low, high)); depthLabel.text = $"Forking levels: {low}–{high}"; Changed();
            });
            branches.Add(depthRange);
            AddTip(branches, "One child starts at the tip. Additional children fill inward toward the trunk without moving the existing ones. The range randomizes depth per main limb using the seed. Four children and three levels can generate up to 2,040 limbs.");
            Slider(branches, "Limb length", .1f, 2, branchSettings.spread, x => branchSettings.spread = x);
            Slider(branches, "Lower limb angle (°)", 5, 115, branchSettings.angle, x => branchSettings.angle = x);
            Slider(branches, "Upper limb lift (°)", 0, 45, branchSettings.upperAngleBias, x => branchSettings.upperAngleBias = x);
            Slider(branches, "Limb thickness", .1f, .9f, branchSettings.thickness, x => branchSettings.thickness = x);
            Slider(branches, "Branch taper", 0, .95f, branchSettings.taper, x => branchSettings.taper = x);
            AddTip(branches, "Branch taper controls how much each limb narrows toward its tip. Lower values make thicker tips: 0 = constant thickness, 0.5 = half the base radius at the tip, 0.8 = the original taper. Applies to main limbs and sub-branches; child thickness follows the parent at its attachment.");
            Slider(branches, "Trunk attachment thickness", .5f, 2.5f, branchSettings.attachmentThickness, x => branchSettings.attachmentThickness = x);
            AddTip(branches, "Trunk attachment thickness scales the base of main limbs: 1 = original, 0.5 = thinner, 2.5 = thicker. It blends into the normal taper over the first 35% of the limb. The base is limited to 95% of the trunk radius at its join to avoid protruding sockets. Limb thickness still controls the overall branch thickness.");
            AddTip(branches, "Limb length scales reach within the crown. 90° is horizontal; upper limb lift reduces the angle toward the tree top.");
            var character = branches;
            Text(branches, "Bending", "parameter-subheading");
            Slider(character, "Curvature", 0, 1, branchSettings.bend, x => branchSettings.bend = x);
            Slider(character, "Downward weight", 0, 1, branchSettings.droop, x => branchSettings.droop = x);
            Slider(character, "Curve sharpness", 0, 1, branchSettings.sharpness, x => branchSettings.sharpness = x);
            Slider(character, "Natural irregularity", 0, .6f, recipe.irregularity, x => recipe.irregularity = x);
            AddTip(character, "Longer, thicker limbs carry more downward weight. Curvature at zero keeps every limb straight. Sharpness changes the bend profile without moving its endpoint.");
            Action(character, "Restore " + recipe.species + " branch defaults", () => { recipe.ResetBranches(); ShowTab(1); });
            var foliageSection = Fold(controls, "Foliage");
            foliageControls = foliageSection;
            var crown = Fold(foliageSection, "Layout");
            var attachment = Fold(foliageSection, "Orientation");
            if (recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond)
            {
                Slider(crown, "Frond width", .25f, 3, recipe.foliageSize, x => recipe.foliageSize = x);
                AddTip(crown, "Curved feather fronds follow the terminal branch skeleton. Main limbs set frond count; branch angles and bending shape the crown. Leaf texture maps from the bare stem end to the frond tip.");
                Text(attachment, "Palm fronds follow the angle and curvature of their supporting branches.", "small");
                AddTip(attachment, "Palm fronds follow the orientation of their supporting branches. Adjust limb angles and curvature in Branches.");
            }
            else
            {
                IntSlider(crown, "Clusters per tip", 1, 4, Mathf.Clamp(Mathf.RoundToInt(recipe.layers / 3f), 1, 4), x => recipe.layers = x * 3, "Overlapping card clusters along each terminal branch.");
                IntSlider(crown, "Cards per cluster", 2, 12, recipe.foliageCards, x => recipe.foliageCards = x, "Number of double-sided textured cards in each cluster.");
                IntSlider(crown, "Foliage subdivisions", 1, 12, recipe.foliageSubdivisions, x => recipe.foliageSubdivisions = x, "Lengthwise segments per card. One is a single quad; increase for smoother card bends. Each segment adds two front-face triangles.");
                Slider(crown, "Card bend", -1, 1, recipe.foliageBend, x => recipe.foliageBend = x);
                float cardSizeMax = Mathf.Clamp(recipe.foliageSize, .25f, 3);
                float cardSizeMin = recipe.foliageSizeMin > 0
                    ? Mathf.Clamp(recipe.foliageSizeMin, .25f, cardSizeMax) : cardSizeMax;
                var cardSizeLabel = Text(crown, $"Card size: {cardSizeMin:0.##}–{cardSizeMax:0.##}", "small");
                var cardSizeRange = new MinMaxSlider("Card size", cardSizeMin, cardSizeMax, .25f, 3);
                cardSizeRange.tooltip = "Drag either handle to set the smallest and largest card size. Each card gets a seeded size in this range; equal values keep all cards the same size.";
                cardSizeRange.RegisterValueChangedCallback(e => {
                    recipe.foliageSizeMin = Mathf.Clamp(e.newValue.x, .25f, 3);
                    recipe.foliageSize = Mathf.Clamp(e.newValue.y, recipe.foliageSizeMin, 3);
                    cardSizeLabel.text = $"Card size: {recipe.foliageSizeMin:0.##}–{recipe.foliageSize:0.##}";
                    Changed();
                });
                crown.Add(cardSizeRange);
                Slider(crown, "Card width scale", .1f, 3, recipe.foliageWidthScale, x => recipe.foliageWidthScale = x);
                Slider(crown, "Card height scale", .1f, 3, recipe.foliageHeightScale, x => recipe.foliageHeightScale = x);
                Slider(crown, "Cluster spread", 0, 1, recipe.foliageSpread, x => recipe.foliageSpread = x);
                AddTip(attachment, "Rotate cards with tilt, turn and roll. Stem attached pins the texture pivot to its branch. Bottom-centre stems use X 0.5, Y 0. Fan angle spreads attached bunches. Cluster spread only applies to floating cards.");
                var stemAttached = new Toggle("Stem attached") { value = recipe.foliageStemAttached };
                stemAttached.RegisterValueChangedCallback(e => { recipe.foliageStemAttached = e.newValue; ShowTab(1); }); attachment.Add(stemAttached);
                Slider(attachment, "Bunch tilt (°)", -180, 180, recipe.foliageRotation.x, x => recipe.foliageRotation.x = x);
                Slider(attachment, "Bunch turn (°)", -180, 180, recipe.foliageRotation.y, x => recipe.foliageRotation.y = x);
                Slider(attachment, "Bunch roll (°)", -180, 180, recipe.foliageRotation.z, x => recipe.foliageRotation.z = x);
                if (recipe.foliageStemAttached)
                {
                    Slider(attachment, "Fan angle (°)", 0, 85, recipe.foliageFanAngle, x => recipe.foliageFanAngle = x);
                    Slider(attachment, "Stem pivot X", 0, 1, recipe.foliageStemPivot.x, x => recipe.foliageStemPivot.x = x);
                    Slider(attachment, "Stem pivot Y", 0, 1, recipe.foliageStemPivot.y, x => recipe.foliageStemPivot.y = x);
                }
                Action(attachment, "Align bottom-centre stems", () => {
                    recipe.foliageStemAttached = true; recipe.foliageStemPivot = new Vector2(.5f, 0);
                    recipe.foliageRotation = Vector3.zero; recipe.foliageFanAngle = 20; ShowTab(1);
                });
                AddTip(crown, "Overlapping cards form a canopy from every angle. Assign a leaf spray with transparent background, or leave empty for the built-in spray. Cards keep their orientation as you orbit. Subdivisions add lengthwise segments; use two or more to see curvature. Card bend curves around the stem pivot (or card centre); negative values reverse it. More subdivisions increase triangle count. Palm fronds use their own geometry.");
            }
            var leafSurface = Fold(foliageSection, "Material");
            var leafTexture = new ObjectField("Leaf texture (RGBA)") { objectType = typeof(Texture2D), allowSceneObjects = false, value = recipe.foliageTexture };
            leafTexture.RegisterValueChangedCallback(e => { recipe.foliageTexture = e.newValue as Texture2D; Changed(); }); leafSurface.Add(leafTexture);
            Slider(leafSurface, "Alpha cutoff", .05f, .95f, recipe.foliageCutoff, x => recipe.foliageCutoff = x);
            Slider(leafSurface, "Cutoff feathering", 0, .5f, recipe.foliageFeathering, x => recipe.foliageFeathering = x);
            Slider(leafSurface, "Canopy shading softness", 0, 1, recipe.foliageSoftness, x => recipe.foliageSoftness = x);
            MaterialToggle(leafSurface, "Alpha-to-coverage (MSAA)", recipe.foliageAlphaToCoverage, x => recipe.foliageAlphaToCoverage = x);
            AddTip(leafSurface, "Canopy shading softness blends card lighting into the rounded canopy. Alpha-to-coverage smooths leaf edges in URP when the render target uses MSAA (typically Forward/Forward+ with 2x/4x/8x MSAA). Without MSAA, and in the built-in pipeline, the existing cutoff/feathering is used. This does not change project anti-aliasing settings. Both options export with the material.");
            Slider(leafSurface, "Ambient occlusion", 0, 1, recipe.foliageOcclusion, x => recipe.foliageOcclusion = x);
            AddTip(leafSurface, "Feathering softens the alpha cutoff using dithered coverage. 0 keeps a hard cutout. It works best with a soft alpha texture; dithering can look grainy without anti-aliasing. Ambient occlusion approximates the reduced ambient light inside the canopy. Both settings export with the tree.");
            ColorControl(leafSurface, "Foliage", recipe.leaves, x => recipe.leaves = x);
            var foliageGradientToggle = new Toggle("Use foliage gradient") { value = recipe.foliageGradientEnabled };
            foliageGradientToggle.RegisterValueChangedCallback(e => { recipe.foliageGradientEnabled = e.newValue; Changed(); }); leafSurface.Add(foliageGradientToggle);
            var foliageGradient = new GradientField("Foliage gradient") { value = recipe.foliageGradient };
            foliageGradient.RegisterValueChangedCallback(e => { recipe.foliageGradient = e.newValue; Changed(); }); leafSurface.Add(foliageGradient);
            var tintMode = new PopupField<string>("Gradient placement", new List<string> { "Tree height", "Random per card", "Stem to tip" }, (int)recipe.foliageTintMode);
            tintMode.RegisterValueChangedCallback(e => { recipe.foliageTintMode = (FoliageTintMode)tintMode.index; Changed(); }); leafSurface.Add(tintMode);
            AddTip(leafSurface, "A gradient replaces the solid tint. Blend by tree height, select seeded colors per card, or blend from texture stem to tip. Tints multiply the texture; gradient alpha is ignored.");
            MaterialToggle(leafSurface, "Leaf translucency", recipe.foliageTransmissionEnabled, x => recipe.foliageTransmissionEnabled = x);
            Slider(leafSurface, "Light transmission", 0, 1, recipe.foliageTransmission, x => recipe.foliageTransmission = x);
            AddTip(leafSurface, "Leaf translucency simulates light shining through thin leaves. Light transmission: 0 = off, 1 = strongest backlighting. Turn a preview light behind the tree to see it. This is a real-time transmission approximation, not ray tracing or transparency; leaf cutouts and shadow casting remain intact.");
            Slider(leafSurface, "Foliage roughness", 0, 1, recipe.foliageRoughness, x => recipe.foliageRoughness = x);
            MaterialToggle(leafSurface, "Foliage specular highlights", recipe.foliageHighlights, x => recipe.foliageHighlights = x);
            MaterialToggle(leafSurface, "Foliage environment reflections", recipe.foliageReflections, x => recipe.foliageReflections = x);
            AddTip(leafSurface, "Roughness: 0 = glossy, 1 = rough. Highlights control shine from lights; reflections control shine from the environment. Leaves default to matte. Enable either for waxy or wet foliage, then lower roughness. Diffuse lighting and gradient tints remain active.");
            var surface = Fold(controls, "Bark material");
            var barkTexture = new ObjectField("Bark texture") { objectType = typeof(Texture2D), allowSceneObjects = false, value = recipe.barkTexture };
            barkTexture.RegisterValueChangedCallback(e => { recipe.barkTexture = e.newValue as Texture2D; Changed(); }); surface.Add(barkTexture);
            Slider(surface, "Bark Texture U", 1, 12, recipe.barkTilingAround, x => recipe.barkTilingAround = x);
            Slider(surface, "Bark Texture V", .1f, 8, recipe.barkTilingLength, x => recipe.barkTilingLength = x);
            ColorControl(surface, "Bark", recipe.bark, x => recipe.bark = x);
            var barkGradientToggle = new Toggle("Use bark gradient") { value = recipe.barkGradientEnabled };
            barkGradientToggle.RegisterValueChangedCallback(e => { recipe.barkGradientEnabled = e.newValue; Changed(); }); surface.Add(barkGradientToggle);
            var barkGradient = new GradientField("Bark gradient") { value = recipe.barkGradient };
            barkGradient.RegisterValueChangedCallback(e => { recipe.barkGradient = e.newValue; Changed(); }); surface.Add(barkGradient);
            AddTip(surface, "Bark follows the trunk and branches. Leave the texture empty for built-in bark. For custom seamless textures, use Repeat wrapping in their import settings. Bark color tints the texture; white preserves its original colors.");
            Slider(surface, "Ambient occlusion", 0, 1, recipe.barkOcclusion, x => recipe.barkOcclusion = x);
            AddTip(surface, "Ambient occlusion approximates shading inside the canopy and near the trunk base. 0 disables it; it affects ambient light, not direct lights.");
            Slider(surface, "Bark roughness", 0, 1, recipe.barkRoughness, x => recipe.barkRoughness = x);
            MaterialToggle(surface, "Bark specular highlights", recipe.barkHighlights, x => recipe.barkHighlights = x);
            MaterialToggle(surface, "Bark environment reflections", recipe.barkReflections, x => recipe.barkReflections = x);
            AddTip(surface, "Bark Texture U repeats around the trunk; Bark Texture V repeats per metre along it. A bark gradient replaces the solid tint from tree base to top; alpha is ignored. Roughness: 0 = glossy, 1 = rough. Specular highlights control shine from lights; environment reflections control reflected surroundings.");
            var detail = Fold(controls, "Mesh detail");
            var simplificationRow = Box(detail, "row simplification-row");
            var simplificationToggle = new Toggle("Live wood simplification") { value = recipe.simplifyWood };
            simplificationToggle.AddToClassList("simplification-toggle");
            simplificationToggle.RegisterValueChangedCallback(e => { recipe.simplifyWood = e.newValue; Changed(); });
            simplificationRow.Add(simplificationToggle);
            simplificationStats = new Label("0 tris\n0 verts removed") {
                tooltip = "Triangles and vertices removed from the current preview wood mesh. Foliage is unchanged."
            };
            simplificationStats.AddToClassList("simplification-stats");
            simplificationRow.Add(simplificationStats);
            Slider(detail, "Simplification tolerance (mm)", .1f, 20, recipe.simplificationTolerance * 1000, x => recipe.simplificationTolerance = x / 1000);
            AddTip(detail, "Live simplification removes redundant lengthwise rings on the trunk and branches while keeping sockets, endpoints, UV seams, and shading transitions. Higher tolerance allows more shape change and can remove more triangles. Dense bark detail may limit reduction. Foliage cards are left intact. The simplified mesh is used in previews, exports, and LODs.");
            IntSlider(detail, "Radial subdivisions", 3, 12, recipe.branchSides, x => { recipe.trunkSides = recipe.branchSides = x; }, "Changes the number of sides around both trunk and branches together.");
            IntSlider(detail, "Length subdivisions", 2, 48, recipe.branchSegments, x => { recipe.trunkSegments = recipe.branchSegments = x; }, "Changes lengthwise subdivisions of both trunk and branches together. Socket and bend rings are retained.");
            Slider(detail, "Surface detail", 0, 1, recipe.barkSurfaceDetail, x => recipe.barkSurfaceDetail = x);
            Slider(detail, "Detail scale", .25f, 4, recipe.barkDetailScale, x => recipe.barkDetailScale = x);
            IntSlider(detail, "Detail resolution", 1, 4, recipe.barkDetailResolution, x => recipe.barkDetailResolution = x, "Adds samples to the shared base subdivisions on both trunk and branches. Higher values cost more triangles. Only active when surface detail is above zero.");
            AddTip(detail, "Radial and length subdivisions adjust the trunk and branches together. Surface detail adds grooves and ridges to both with one strength control; 0 disables it. Larger scale makes broader ridges. Detail resolution adds geometry, so the base subdivision controls remain effective. Relief scales with branch thickness and fades at joins. LOD1 reduces relief and resolution; LOD2 keeps the smooth base mesh. Existing recipes retain their saved subdivisions until you adjust the shared controls.");
            var family = Fold(controls, "Family variations");
            var count = new SliderInt("Family size", 1, 12) { value = recipe.variantCount, showInputField = true };
            count.RegisterValueChangedCallback(e => { recipe.variantCount = e.newValue; variant = Mathf.Min(variant, recipe.variantCount - 1); Changed(); }); family.Add(count);
            Slider(family, "Shape variation", 0, .6f, recipe.variation, x => recipe.variation = x);
            AddTip(family, "Preview sibling shapes from the same recipe. The native Terrain brush will need a separate variant-painting integration to mix these automatically.");
            Action(controls, "Reset tree settings", () => { recipe = new TreeRecipe(); variant = 0; ShowTab(1); });
            var panel = BuildPreviewPanel();
            var export = Box(panel, "card");
            var exportHeading = Box(export, "row");
            Text(exportHeading, "Destination / Paint Trees", "section-title");
            AddHeaderHelp(exportHeading, "Generate saves sibling meshes, prefabs and a recipe in a new folder. Matching materials and textures are shared. Optional LODs preserve the growth skeleton and simplify wood and foliage. Hiding preview foliage does not remove it from exports. Wind and distant whole-tree billboards are not generated.");
            if (exportLods == null) exportLods = new TreeLodSettings();
            var lodToggle = new Toggle("Generate LOD models") { value = exportLods.enabled, tooltip = "Exports the full tree plus two reduced meshes, assigned to its LODGroup. Materials and textures are shared across all levels." };
            export.Add(lodToggle);
            var lodControls = new Foldout { text = "LOD settings", value = false };
            export.Add(lodControls); lodControls.SetEnabled(exportLods.enabled);
            lodToggle.RegisterValueChangedCallback(e => { exportLods.enabled = e.newValue; lodControls.SetEnabled(e.newValue); });
            var fadeToggle = new Toggle("Dithered transitions") { value = exportLods.crossFade, tooltip = "Uses a 15% transition band. URP also needs LOD Cross Fade enabled in its pipeline asset. Both meshes render during transitions." };
            fadeToggle.RegisterValueChangedCallback(e => exportLods.crossFade = e.newValue); lodControls.Add(fadeToggle);
            void LodSlider(string label, float min, float max, float value, Action<float> set)
            {
                var control = new Slider(label, min, max) { value = value, showInputField = true };
                control.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); control.SetValueWithoutNotify(v); set(v); });
                lodControls.Add(control);
            }
            LodSlider("LOD1 screen height", .21f, .8f, exportLods.lod1Start, v => exportLods.lod1Start = v);
            LodSlider("LOD2 screen height", .04f, .2f, exportLods.lod2Start, v => exportLods.lod2Start = v);
            LodSlider("Cull screen height", .001f, .03f, exportLods.cull, v => exportLods.cull = v);
            LodSlider("Far foliage retained", .35f, 1, exportLods.foliageRetention, v => exportLods.foliageRetention = v);
            LodSlider("Leaf size compensation", 0, .25f, exportLods.foliageCompensation, v => exportLods.foliageCompensation = v);
            Text(lodControls, "Screen height is a fraction: 0.35 = 35%. Palm LODs simplify curved fronds instead of removing them. Actual triangle counts are saved in LOD-Report.csv.", "small");
            var collision = new Toggle("Add trunk collider") { value = exportTrunkCollider, tooltip = "Adds an approximate capsule around the lower trunk. Review it for strongly bent trees." };
            collision.RegisterValueChangedCallback(e => exportTrunkCollider = e.newValue); export.Add(collision);
            Action(export, "Generate prefab family", GenerateTreeFamily, true);
            if (!string.IsNullOrEmpty(exportStatus)) Text(export, exportStatus, "small");
            if (!string.IsNullOrEmpty(lastExportFolder) && AssetDatabase.IsValidFolder(lastExportFolder))
            {
                Text(export, "Last export: " + Path.GetFileName(lastExportFolder), "section-title");
                var outputActions = Box(export, "row");
                Action(outputActions, "Locate resources", () => { var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(lastExportFolder); Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); });
                Action(outputActions, "Export Unity package", ExportTreePackage);
                if (exportTerrain == null && Selection.activeGameObject != null) exportTerrain = Selection.activeGameObject.GetComponent<Terrain>();
                var terrainField = new ObjectField("Target Terrain") { objectType = typeof(Terrain), allowSceneObjects = true, value = exportTerrain };
                terrainField.RegisterValueChangedCallback(e => exportTerrain = e.newValue as Terrain); export.Add(terrainField);
                Action(export, "Add family to Terrain", () =>
                {
                    try
                    {
                        int added = TreeExporter.AddToTerrain(exportTerrain, lastExportPrefabs);
                        exportStatus = added == 0 ? "This family is already in the Terrain’s tree palette." : $"Added {added} tree prototypes. Select the Terrain and use Paint Trees to plant them. Undo restores the previous palette.";
                        ShowTab(1);
                    }
                    catch (Exception e) { EditorUtility.DisplayDialog("Cannot add trees", e.Message, "OK"); }
                });
                Text(export, "Adds brush choices without planting trees. Existing trees are preserved.", "small");
            }
            Changed();
        }

        VisualElement BuildPreviewPanel()
        {
            var content = builderPage.Q("builderPreviewContent");
            content.RemoveFromClassList("hidden");
            var placeholder = builderPage.Q("builderPlaceholder");
            placeholder.AddToClassList("hidden");
            placeholder.Clear();
            var help = builderPage.Q("previewHelp");
            help.Clear();
            AddHeaderHelp(help, "Left-drag to orbit. Middle-drag or Shift + left-drag to pan. Scroll to zoom. Reset view recenters the camera. " + (category == "Trees" ? "Shift-click a tree part to highlight it and open its controls." : "Use the mesh overlay to inspect the generated rock topology."));
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
            preview.ShowMeshOverlay = showMeshOverlay;
            var meshOverlay = new Toggle("Mesh overlay") { value = showMeshOverlay, tooltip = "Overlay triangle edges on the shaded preview, including the current surface detail and simplification. Hide foliage to inspect the wood. Preview only; not exported." };
            meshOverlay.AddToClassList("preview-option");
            meshOverlay.RegisterValueChangedCallback(e => { showMeshOverlay = e.newValue; preview.ShowMeshOverlay = e.newValue; previewElement?.MarkDirtyRepaint(); Repaint(); });
            viewOptions.Add(meshOverlay);
            preview.SetView(previewOrbit, previewPan);
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
            previewHost.Add(previewElement);
            var previewImage = new IMGUIContainer(() => preview?.Draw(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))))
            { pickingMode = PickingMode.Ignore };
            previewImage.style.flexGrow = 1;
            previewElement.Add(previewImage);
            meshStats = new Label { name = "previewStats", pickingMode = PickingMode.Ignore };
            meshStats.AddToClassList("preview-stats");
            previewHost.Add(meshStats);
            if (category == "Trees") AddFloorHeightOverlay(previewHost);
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
            Action(viewButtons, "Reset view", () => { preview.ResetView(); CapturePreviewView(); previewElement.MarkDirtyRepaint(); });
            var exportHost = builderPage.Q("previewExport");
            exportHost.Clear();
            return exportHost;
        }

        void AddFloorHeightOverlay(VisualElement previewHost)
        {
            var overlay = new VisualElement { tooltip = "Floor height sets the placement ground line. Positive values bury more of the tree; negative values expose more. The offset is baked into exported meshes and LODs." };
            overlay.AddToClassList("floor-height-overlay");
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
            overlay.Add(track);
            overlay.Add(labels);
            previewHost.Add(overlay);
        }

        void GenerateTreeFamily()
        {
            try
            {
                var result = TreeExporter.Generate(recipe, exportTrunkCollider, lodSettings: exportLods);
                lastExportFolder = result.folder; lastExportPrefabs = result.prefabs;
                exportStatus = $"Generated {result.prefabs.Length} prefabs in {result.folder}.";
                EditorGUIUtility.PingObject(result.prefabs[0]);
            }
            catch (OperationCanceledException) { exportStatus = "Generation cancelled. No partial export was kept."; }
            catch (Exception e) { exportStatus = "Generation failed: " + e.Message; Debug.LogException(e); }
            ShowTab(1);
        }

        void ExportTreePackage()
        {
            string path = EditorUtility.SaveFilePanel("Export tree family", "", Path.GetFileName(lastExportFolder) + ".unitypackage", "unitypackage");
            if (string.IsNullOrEmpty(path)) return;
            try { TreeExporter.ExportPackage(lastExportFolder, path); exportStatus = "Unity package saved to " + path; ShowTab(1); }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot export package", e.Message, "OK"); }
        }

        void SaveRecipeJson()
        {
            string path = EditorUtility.SaveFilePanel("Save tree recipe", "", recipe.assetName + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { TreeJsonStorage.Write(path, TreeRecipeJson.From(recipe, variant)); }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot save recipe", e.Message, "OK"); }
        }

        void LoadRecipeJson()
        {
            string path = EditorUtility.OpenFilePanel("Load tree recipe", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var doc = TreeJsonStorage.Read<TreeRecipeJson>(path);
                var loaded = doc.Restore(out var warning);
                RecordSettingsUndo();
                recipe = loaded; variant = Mathf.Clamp(doc.sibling, 0, recipe.variantCount - 1); ShowTab(1);
                if (!string.IsNullOrEmpty(warning)) EditorUtility.DisplayDialog("Recipe loaded", warning, "OK");
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot load recipe", e.Message, "OK"); }
        }

        void SaveFamilyJson()
        {
            Directory.CreateDirectory(TreeJsonStorage.FamilyFolder);
            string path = EditorUtility.SaveFilePanel("Save tree family — filename becomes the family name", TreeJsonStorage.FamilyFolder, "New family.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var profile = JsonUtility.FromJson<TreeFamilyProfile>(JsonUtility.ToJson(recipe.Profile));
                profile.name = Path.GetFileNameWithoutExtension(path);
                profile.id = profile.name.ToLowerInvariant().Replace(' ', '-');
                profile.branches = JsonUtility.FromJson<BranchSettings>(JsonUtility.ToJson(recipe.Branches));
                var defaults = TreeRecipeJson.From(recipe, 0); defaults.family = profile;
                TreeJsonStorage.Write(path, new TreeFamilyJson { profile = profile, defaults = defaults });
                TreeJsonStorage.ReloadFamilies(); ShowTab(1);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot save family", e.Message, "OK"); }
        }

        void ImportFamilyJson()
        {
            string path = EditorUtility.OpenFilePanel("Import tree family", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var doc = TreeJsonStorage.ReadFamily(path);
                if (TreeJsonStorage.Families.Any(f => f.profile.id == doc.profile.id))
                    throw new InvalidDataException("A family with id '" + doc.profile.id + "' already exists. Give the imported JSON a unique profile.id.");
                Directory.CreateDirectory(TreeJsonStorage.FamilyFolder);
                string filename = string.Concat(doc.profile.id.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
                string destination = AssetDatabase.GenerateUniqueAssetPath(TreeJsonStorage.FamilyFolder + "/" + filename + ".json");
                TreeJsonStorage.Write(destination, doc); ShowTab(1);
            }
            catch (Exception e) { EditorUtility.DisplayDialog("Cannot import family", e.Message, "OK"); }
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
            bool dragged = false;
            Vector2 previous = Vector2.zero;
            Vector2 pressPosition = Vector2.zero;
            void RefreshView() { CapturePreviewView(); image.MarkDirtyRepaint(); Repaint(); }
            viewport.RegisterCallback<MouseDownEvent>(e =>
            {
                if (dragButton != -1 || (e.button != 0 && e.button != 2)) return;
                dragButton = e.button; pan = e.button == 2 || e.shiftKey; previous = pressPosition = e.mousePosition;
                picking = e.button == 0 && e.shiftKey && category == "Trees";
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
                dragButton = -1; viewport.ReleaseMouse(); e.StopPropagation();
            });
            viewport.RegisterCallback<MouseCaptureOutEvent>(e => { dragButton = -1; picking = false; });
            viewport.RegisterCallback<WheelEvent>(e =>
            {
                preview?.Zoom(e.delta.y); RefreshView(); e.StopPropagation();
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
        void IntSlider(VisualElement parent, string title, int min, int max, int value, Action<int> set, string tooltip)
        {
            var slider = new SliderInt(title, min, max) { value = value, showInputField = true, tooltip = tooltip };
            slider.RegisterValueChangedCallback(e => { int next = Mathf.Clamp(e.newValue, min, max); slider.SetValueWithoutNotify(next); set(next); Changed(); });
            parent.Add(slider);
        }
        void Slider(VisualElement parent, string title, float min, float max, float value, Action<float> set)
        {
            var slider = new Slider(title, min, max) { value = value, showInputField = true };
            slider.RegisterValueChangedCallback(e => { set(Mathf.Clamp(e.newValue, min, max)); Changed(); }); parent.Add(slider);
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
            preview?.Build(recipe, variant);
            if (previewCaption != null) previewCaption.text = recipe.species + " / Sibling " + (variant + 1) + " of " + recipe.variantCount;
            UpdateMeshStats("Wood");
            previewElement?.MarkDirtyRepaint();
        }

        void UpdateMeshStats(string geometryLabel)
        {
            if (meshStats == null || preview == null) return;
            if (simplificationStats != null)
                simplificationStats.text = $"{preview.RemovedWoodTriangles:N0} tris\n{preview.RemovedWoodVertices:N0} verts removed";
            int vertices = preview.Meshes.Sum(mesh => mesh.vertexCount);
            meshStats.text = $"Triangle Count: {preview.WoodTriangles + preview.FoliageTriangles:N0}\n"
                + $"{geometryLabel}: {preview.WoodTriangles:N0} • Foliage: {preview.FoliageTriangles:N0}\n"
                + $"Vertex Count: {vertices:N0}";
        }

        void BuildLibrary()
        {
            var toolbar = libraryPage.Q("libraryToolbar");
            toolbar.Clear();
            var search = new ToolbarSearchField(); search.AddToClassList("grow"); toolbar.Add(search);
            var filter = new PopupField<string>(new List<string> { "All types", "Trees", "Grass", "Rocks", "Bushes" }, 0); toolbar.Add(filter);
            var list = libraryPage.Q("libraryList");
            void Refresh()
            {
                list.Clear();
                var paths = AssetDatabase.IsValidFolder(GeneratedRoot)
                    ? AssetDatabase.FindAssets("t:Prefab l:TerrainityGenerated", new[] { GeneratedRoot }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToArray()
                    : Array.Empty<string>();
                int shown = 0;
                foreach (var path in paths)
                {
                    if (filter.value != "All types" && !path.StartsWith(GeneratedRoot + "/" + filter.value + "/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Path.GetFileNameWithoutExtension(path).IndexOf(search.value ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    shown++;
                    var row = Box(list, "library-item");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var field = new ObjectField { objectType = typeof(GameObject), allowSceneObjects = false, value = prefab };
                    field.AddToClassList("grow");
                    field.RegisterValueChangedCallback(_ => field.SetValueWithoutNotify(prefab)); row.Add(field);
                    Action(row, "Locate", () => { Selection.activeObject = prefab; EditorGUIUtility.PingObject(prefab); });
                    Action(row, "Delete prefab", () =>
                    {
                        if (DeleteLibraryPrefab(prefab)) Refresh();
                    }).tooltip = "Move this prefab to the Recycle Bin. Shared generated resources are kept.";
                }
                if (shown == 0)
                {
                    var empty = Box(list, "card empty");
                    Text(empty, paths.Length == 0 ? "Your forest starts here." : "No matching prefabs.", "section-title");
                    Text(empty, paths.Length == 0 ? "Use Generate prefab family in the tree or rock builder. Your saved siblings will appear here, ready for Terrain’s Add Tree dialog." : "Try another search or asset type.");
                    Action(empty, "Open tree builder", () => { category = "Trees"; ShowTab(1); }, true);
                }
            }
            search.RegisterValueChangedCallback(_ => Refresh()); filter.RegisterValueChangedCallback(_ => Refresh());
            Action(toolbar, "Refresh", Refresh); Refresh();
        }

        bool DeleteLibraryPrefab(GameObject prefab)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null || !path.StartsWith(GeneratedRoot + "/", StringComparison.Ordinal)
                || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.GetLabels(prefab).Contains("TerrainityGenerated"))
            {
                EditorUtility.DisplayDialog("Cannot delete prefab", "This entry is no longer a generated Terrainity prefab. Refresh the library and try again.", "OK");
                return false;
            }
            if (!EditorUtility.DisplayDialog("Delete generated prefab?",
                $"Move {prefab.name} to the Recycle Bin?\n\n{path}\n\nOnly this prefab is removed. Meshes, materials, textures, recipes and sibling prefabs are kept.\n\nTerrains or scenes using this prefab will lose its asset reference. Remove it from their tree palettes first if needed. This cannot be undone with Unity Undo; restore the prefab and its .meta file from the Recycle Bin to recover it.",
                "Delete prefab", "Cancel")) return false;
            try
            {
                if (!AssetDatabase.MoveAssetToTrash(path))
                {
                    EditorUtility.DisplayDialog("Cannot delete prefab", "Unity could not move the prefab to the Recycle Bin. Check whether the asset is read-only or locked.", "OK");
                    return false;
                }
                lastExportPrefabs = (lastExportPrefabs ?? Array.Empty<GameObject>()).Where(p => p != null && p != prefab).ToArray();
                exportStatus = "Deleted " + Path.GetFileNameWithoutExtension(path) + ". Shared generated resources were kept.";
                return true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Cannot delete prefab", e.Message, "OK");
                return false;
            }
        }
    }
}

