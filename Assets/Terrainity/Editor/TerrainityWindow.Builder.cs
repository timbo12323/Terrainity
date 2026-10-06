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
    // Tree parameter controls and export settings.
    public sealed partial class TerrainityWindow
    {
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
                    next.familyProfile = selected.profile.Copy();
                    next.species = selected.profile.name;
                    next.customBranches = selected.profile.branches.Copy();
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
            Slider(silhouette, "Crown taper", 0, 1, recipe.crownTaper, x => recipe.crownTaper = x,
                "Narrows branch reach and foliage toward the top of the crown. Zero preserves the current silhouette.");
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
            Slider(roots, "Root bend", 0, 1, recipe.rootBend, x => recipe.rootBend = x,
                "Each root gets its own seeded bend direction, strength and position, with a smaller secondary curve. Zero keeps the original root paths; sockets, tips and depth stay fixed.");
            Slider(roots, "Root bend sharpness", 0, 1, recipe.rootSharpness, x => recipe.rootSharpness = x,
                "Blends smooth root curves into angular bends at their independently seeded locations.");
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
            var leafSurface = Fold(foliageSection, "Material");
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
                var alignFoliage = new Toggle("Align Foliage") { value = recipe.foliageAligned };
                alignFoliage.tooltip = "Place cards in evenly spaced rows around each terminal branch, from base toward tip.";
                alignFoliage.RegisterValueChangedCallback(e => { recipe.foliageAligned = e.newValue; ShowTab(1); });
                crown.Add(alignFoliage);
                IntSlider(crown, "Clusters", 1, 12, Mathf.Clamp(Mathf.RoundToInt(recipe.layers / 3f), 1, 12), x => recipe.layers = x * 3, "Number of card clusters on each terminal branch.");
                Slider(crown, "Cluster distance", 0, 1, recipe.foliageClusterDistance, x => recipe.foliageClusterDistance = x, "0 bunches positions at the tip; 1 spaces them evenly from the branch base to its tip.");
                if (recipe.foliageAligned)
                {
                    IntSlider(crown, "Sides around branch", 1, 3, Mathf.Clamp(recipe.foliageAlignSides, 1, 3), x => recipe.foliageAlignSides = x,
                        "One card on each selected side at every position; two sides oppose each other, three are 120° apart.");
                    float offsetMin = Mathf.Clamp(recipe.foliageAlignOffsetMin, -.5f, .5f);
                    float offsetMax = Mathf.Clamp(recipe.foliageAlignOffsetMax, offsetMin, .5f);
                    var offsetLabel = Text(crown, $"Alternate Y offset: {offsetMin:0.##}–{offsetMax:0.##} m", "small");
                    var offsetRange = new MinMaxSlider("Alternate Y offset (m)", offsetMin, offsetMax, -.5f, .5f);
                    offsetRange.tooltip = "Every other position along each side gets its own seeded vertical offset in this range; equal values give a fixed offset.";
                    offsetRange.RegisterValueChangedCallback(e => {
                        recipe.foliageAlignOffsetMin = Mathf.Clamp(e.newValue.x, -.5f, .5f);
                        recipe.foliageAlignOffsetMax = Mathf.Clamp(e.newValue.y, recipe.foliageAlignOffsetMin, .5f);
                        offsetLabel.text = $"Alternate Y offset: {recipe.foliageAlignOffsetMin:0.##}–{recipe.foliageAlignOffsetMax:0.##} m";
                        Changed();
                    });
                    crown.Add(offsetRange);
                }
                else IntSlider(crown, "Cards per cluster", 2, 12, recipe.foliageCards, x => recipe.foliageCards = x, "Number of double-sided textured cards in each cluster.");
                IntSlider(crown, "Foliage subdivisions", 1, 12, recipe.foliageSubdivisions, x => recipe.foliageSubdivisions = x, "Lengthwise segments per card. One is a single quad; increase for smoother card bends. Each segment adds two front-face triangles.");
                Slider(crown, "Card bend", -1, 1, recipe.foliageBend, x => recipe.foliageBend = x,
                    "Curves the card out of its plane along a rounded arc, like fabric draped over a pipe. Positive and negative values bend to opposite sides.");
                var cardSizeControls = Fold(crown, "Card size");
                float cardSizeMax = Mathf.Clamp(recipe.foliageSize, .25f, 3);
                float cardSizeMin = recipe.foliageSizeMin > 0
                    ? Mathf.Clamp(recipe.foliageSizeMin, .25f, cardSizeMax) : cardSizeMax;
                var cardSizeLabel = Text(cardSizeControls, $"Card size: {cardSizeMin:0.##}–{cardSizeMax:0.##}", "small");
                var cardSizeRange = new MinMaxSlider("Size range", cardSizeMin, cardSizeMax, .25f, 3);
                cardSizeRange.tooltip = "Drag either handle to set the smallest and largest card size. Each card gets a seeded size in this range; equal values keep all cards the same size.";
                cardSizeRange.RegisterValueChangedCallback(e => {
                    recipe.foliageSizeMin = Mathf.Clamp(e.newValue.x, .25f, 3);
                    recipe.foliageSize = Mathf.Clamp(e.newValue.y, recipe.foliageSizeMin, 3);
                    cardSizeLabel.text = $"Card size: {recipe.foliageSizeMin:0.##}–{recipe.foliageSize:0.##}";
                    Changed();
                });
                cardSizeControls.Add(cardSizeRange);
                Slider(cardSizeControls, "Card to branch size", 0, 1, recipe.foliageCardToBranchSize, x => recipe.foliageCardToBranchSize = x,
                    "0 keeps chosen card sizes. At 1, cards on the longest trunk-to-tip branch keep their size; cards on shorter branches become smaller. All cards on one branch receive the same scale.");
                if (recipe.foliageAligned)
                    Slider(cardSizeControls, "Card size taper", 0, 1, recipe.foliageCardTaper, x => recipe.foliageCardTaper = x,
                        "0 keeps the chosen card sizes; 1 shrinks cards toward the tip to 20%. The card nearest the trunk keeps its chosen size.");
                Slider(cardSizeControls, "Card width scale", .1f, 3, recipe.foliageWidthScale, x => recipe.foliageWidthScale = x);
                Slider(cardSizeControls, "Card height scale", .1f, 3, recipe.foliageHeightScale, x => recipe.foliageHeightScale = x);
                if (!recipe.foliageAligned)
                {
                    Slider(crown, "Cluster spread", 0, 1, recipe.foliageSpread, x => recipe.foliageSpread = x);
                    AddTip(attachment, "Rotate cards with tilt, turn and roll. Stem attached pins the texture pivot to its branch. Bottom-centre stems use X 0.5, Y 0. Fan angle spreads attached bunches. Cluster spread only applies to floating cards.");
                    var stemAttached = new Toggle("Stem attached") { value = recipe.foliageStemAttached };
                    stemAttached.RegisterValueChangedCallback(e => { recipe.foliageStemAttached = e.newValue; ShowTab(1); }); attachment.Add(stemAttached);
                }
                else AddTip(attachment, "Aligned cards attach at their bottom centre and extend out from the branch. Tilt, turn and roll adjust their orientation.");
                Slider(attachment, "Bunch tilt (°)", -180, 180, recipe.foliageRotation.x, x => recipe.foliageRotation.x = x);
                Slider(attachment, "Bunch turn (°)", -180, 180, recipe.foliageRotation.y, x => recipe.foliageRotation.y = x);
                Slider(attachment, "Bunch roll (°)", -180, 180, recipe.foliageRotation.z, x => recipe.foliageRotation.z = x);
                if (!recipe.foliageAligned && recipe.foliageStemAttached)
                {
                    Slider(attachment, "Fan angle (°)", 0, 85, recipe.foliageFanAngle, x => recipe.foliageFanAngle = x);
                    Slider(attachment, "Stem pivot X", 0, 1, recipe.foliageStemPivot.x, x => recipe.foliageStemPivot.x = x);
                    Slider(attachment, "Stem pivot Y", 0, 1, recipe.foliageStemPivot.y, x => recipe.foliageStemPivot.y = x);
                }
                if (!recipe.foliageAligned)
                    Action(attachment, "Align bottom-centre stems", () => {
                        recipe.foliageStemAttached = true; recipe.foliageStemPivot = new Vector2(.5f, 0);
                        recipe.foliageRotation = Vector3.zero; recipe.foliageFanAngle = 20; ShowTab(1);
                    });
                AddTip(crown, recipe.foliageAligned
                    ? "Aligned cards attach along each terminal branch. Cluster distance spreads their positions; side count distributes cards around the stem. Card size taper shrinks cards toward the tip. Subdivisions and card bend still shape each card. Palm fronds use their own geometry."
                    : "Overlapping cards form a canopy from every angle. Assign a leaf spray with transparent background, or leave empty for the built-in spray. Cards keep their orientation as you orbit. Subdivisions add lengthwise segments; use two or more to see the rounded, out-of-plane card bend. The stem pivot (or card centre) stays fixed. More subdivisions increase triangle count. Palm fronds use their own geometry.");
            }
            var leafTexture = new ObjectField("Leaf texture (RGBA)") { objectType = typeof(Texture2D), allowSceneObjects = false, value = recipe.foliageTexture };
            leafTexture.RegisterValueChangedCallback(e => { recipe.foliageTexture = e.newValue as Texture2D; Changed(); }); leafSurface.Add(leafTexture);
            var foliageCulling = new Toggle("Cull foliage backfaces") { value = recipe.foliageBackfaceCulling,
                tooltip = "Render only the front of foliage cards and fronds. This can reduce overdraw, but leaves may disappear when viewed from behind." };
            foliageCulling.RegisterValueChangedCallback(e => { recipe.foliageBackfaceCulling = e.newValue; Changed(); });
            leafSurface.Add(foliageCulling);
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
            var surfaceDetail = Slider(detail, "Surface detail", 0, 1, recipe.barkSurfaceDetail, x => recipe.barkSurfaceDetail = x);
            var detailScale = Slider(detail, "Detail scale", .25f, 4, recipe.barkDetailScale, x => recipe.barkDetailScale = x);
            var detailResolution = IntSlider(detail, "Detail resolution", 1, 4, recipe.barkDetailResolution, x => recipe.barkDetailResolution = x, "Adds samples to the shared base subdivisions on both trunk and branches. Higher values cost more triangles. Only active when surface detail is above zero.");
            detailScale.SetEnabled(recipe.barkSurfaceDetail > 0);
            detailResolution.SetEnabled(recipe.barkSurfaceDetail > 0);
            surfaceDetail.RegisterValueChangedCallback(e =>
            {
                bool enabled = e.newValue > 0;
                detailScale.SetEnabled(enabled);
                detailResolution.SetEnabled(enabled);
            });
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
            AddHeaderHelp(exportHeading, "Generate saves sibling meshes, prefabs and a recipe in a new folder. Matching materials and textures are shared. Optional LODs preserve the growth skeleton and simplify wood and foliage. Hiding preview foliage does not remove it from exports. Exported trees respond to Wind Zones at runtime. Distant whole-tree billboards are not generated.");
            if (exportLods == null) exportLods = new TreeLodSettings();
            var lodToggle = new Toggle("Generate LOD models") { value = exportLods.enabled, tooltip = "Exports the full tree plus two reduced meshes, assigned to its LODGroup. Materials and textures are shared across all levels." };
            export.Add(lodToggle);
            var lodControls = new Foldout { text = "LOD settings", value = false };
            export.Add(lodControls); lodControls.SetEnabled(exportLods.enabled);
            lodToggle.RegisterValueChangedCallback(e =>
            {
                exportLods.enabled = e.newValue;
                lodControls.SetEnabled(e.newValue);
                if (!e.newValue && previewMode == 1) SetPreviewMode(0);
                UpdatePreviewModeUI();
            });
            var fadeToggle = new Toggle("Dithered transitions") { value = exportLods.crossFade, tooltip = "Uses a 15% transition band. URP also needs LOD Cross Fade enabled in its pipeline asset. Both meshes render during transitions." };
            fadeToggle.RegisterValueChangedCallback(e => exportLods.crossFade = e.newValue); lodControls.Add(fadeToggle);
            void LodSlider(string label, float min, float max, float value, Action<float> set)
            {
                var control = new Slider(label, min, max) { value = value, showInputField = true };
                control.RegisterValueChangedCallback(e =>
                {
                    float v = Mathf.Clamp(e.newValue, min, max);
                    control.SetValueWithoutNotify(v); set(v);
                    if (previewMode == 1) { Changed(); RefreshLodLevel(); }
                });
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

    }
}
