# Terrainity — Foliage Studio

Open **Tools → Terrainity → Foliage Studio**. Unity 6 editor tool with baked runtime tree assets. See [Terrain export](Terrain-Export.md) for generation and Paint Trees integration.

## Available now

- Welcome, Asset builder and Generated library tabs, built with UI Toolkit.
- Repeatable previews for 13 JSON tree families; seed, trunk proportions, canopy, roots, materials and sibling variation controls. Orbit by dragging and zoom by scrolling. See [family references](Family-References.md) for the reference species and visual limits.
- Species-specific branch rules with independently remembered settings. Controls progress from trunk and crown outline to branch structure, curvature, foliage and sibling variation. Upper limb lift changes the attachment angle along the trunk; downward weight bends long, thick limbs more strongly. These are artistic presets, not botanical simulations.
- Hide foliage to inspect the skeleton. Zero bend makes each limb straight; zero limbs leaves a bare trunk. Foliage follows branch endpoints. Preview rendering combines geometry into at most two meshes; branching is capped at 2,040 limbs (24 main limbs, four children per fork, three extra generations).
- Bend sharpness changes a smooth arc into an angular elbow without changing the main limb endpoint. It has no effect at zero bend. Each species remembers its own sharpness value.
- Trunk bend adds one to nine alternating arcs. Bend sharpness blends smooth curves into angular elbows without changing their peak displacement. Trunk twist rotates the bends and branch placement gradually from base to tip (−720° to 720°); combine it with bending for a corkscrew silhouette. Zero bend and twist retain a straight, leaning trunk. Branch sockets follow the curved trunk's position, local direction and taper. Base position and tip height stay fixed.
- Junction thickness uses the parent's tapered radius at the actual attachment. Child roots remain narrower than that radius; buried root rings and blended normals soften the visible join. These are connected-looking overlapping preview surfaces, **not welded/watertight topology**. Combining them into one render mesh does not remove internal faces; exported meshes retain the same overlapping junction topology.
- Seeded rock presets, surface controls, shared materials, mesh LODs and prefab export. Grass and bushes have descriptive placeholders.
- Library search and category filtering, scoped to prefabs under `Assets/TerrainityGenerated` with the `TerrainityGenerated` asset label. No unrelated project assets are listed.

## Parameter design

Start with height, crown start and crown width to establish the silhouette. Then set main limb count, forking levels and limb length for the branch budget and crown fill. Lower limb angle and upper limb lift set growth direction along the trunk. Curvature, downward weight and sharpness control the arc; thickness affects both the visible socket and the amount of droop. Natural irregularity and a fixed seed add controlled variation. Clusters and cards per cluster change canopy density without changing the branch skeleton; Cluster distance places the clusters along each terminal branch.

The controls follow the distinction between whole-tree shape and local branch rules in [Weber and Penn's procedural tree model](https://doi.org/10.1145/218380.218427) and Unity's [branch-group parameters](https://docs.unity3d.com/Manual/tree-Branches.html), which include distribution, growth scale and growth angle along a parent. Depth and density remain bounded because Unity's [tree performance guidance](https://docs.unity3d.com/6000.0/Documentation/Manual/terrain-Tree-Performance.html) recommends limiting branches and leaves and testing polygon cost on the target platform. These sources inform the controls, but the preview is a stylized model and has no claimed real-world species accuracy.

## Deliberately deferred

The preview uses temporary procedural meshes. Generate prefab family saves meshes, materials, textures, recipes and Terrain-ready prefabs, with optional trunk colliders. Optional automatic mesh LODs are assigned on export; see [Tree LODs](Tree-LOD.md). Exported trees respond to Wind Zones through the runtime wind controller. Distant impostors remain deferred. See [Terrain export](Terrain-Export.md).

Tree output: `Assets/TerrainityGenerated/Trees/{AssetName}/{Prefabs|Models|Recipes}`. New exports reuse matching materials and textures under `Assets/TerrainityGenerated/Shared/{Materials|Textures}` across families. Existing exports keep their original resources. The exporter labels its final prefabs `TerrainityGenerated` so they appear in this library.

Unity Terrain uses registered prefab prototypes. Random mesh-changing scripts on a tree prefab are not a reliable per-instance generation mechanism. Shape diversity needs a baked family of meshes/prefabs plus a Terrain painting integration that mixes those prototypes. Generate bakes each sibling separately. Add family to Terrain registers the resulting prototypes for native Paint Trees.

## Manual acceptance checks

1. Open all three tabs; resize down to the minimum window size and scroll the controls.
2. Change each tree parameter, orbit/zoom, switch species, and cycle siblings. Repeating a seed should reproduce the same geometry. Set variation to zero: siblings should match.
3. Switch to Grass, Rocks and Bushes and back. Tree settings persist across tabs and script reloads.
4. Confirm the library starts empty and no unrelated prefabs appear.
5. Close and reopen the window; inspect the Console for errors or temporary mesh leaks.
6. Run **Tools → Terrainity → Validate Family Library** to load all 13 defaults and check geometry round-trips and five siblings per family. Run **Validate Branch Growth** for deterministic branch and trunk checks. Inspect the Console for the PASS reports.
7. Hide foliage, adjust branch settings, then switch families and return. Each family's branch values should be preserved. Restore branch defaults should affect only the selected family.

## Mesh detail and foliage cards

Mesh detail has separate radial sides and longitudinal segments for the trunk and branches. Defaults are 8 sides / 12 segments for the trunk and 6 sides / 4 segments for branches. Lower them for fewer triangles; raise longitudinal segments for strong curvature or twist. Socket locations and sharp bend peaks retain extra rings, so the segment control is a base subdivision count, not a strict triangle budget. Growth stays independent of mesh detail. The preview displays actual wood, foliage and total triangle counts.

When **Generate LOD models** is enabled, the preview panel offers **Live preview** and **LOD preview** tabs. The live view retains its usual camera range. The LOD view renders the same reduced meshes used for export, supports farther zoom, and shows LOD1 or LOD2 in the upper-left corner as the projected tree height crosses the LOD2 screen-height setting. Turning off LOD generation returns to the live view.

Foliage uses overlapping, fixed leaf cards arranged around terminal branches, following the canopy construction shown in [Viktoriia Zavhorodnia's Stylized Tree Tutorial at 32:41](https://www.youtube.com/watch?v=yvKWDkKSWnQ&t=1961s). These planes do not turn toward the camera. Each URP card has four vertices and two triangles, alpha clipping and two-sided rendering by default. The Mesh detail option **Cull foliage backfaces** renders only the front side of cards and fronds; it may make foliage disappear from some viewing angles. Cluster normals give softer volume shading. The Standard fallback adds reverse faces only when culling is off. This is card-based canopy geometry, not a whole-tree distant impostor or automatic LOD system.

Use Clusters (1–12), Cards per cluster (2–12), Card size, Cluster spread and Alpha cutoff to tune fullness and cost. Cluster distance at 0 bunches clusters at each terminal branch tip; at 1 it spreads them from tip to branch start, without placing them along the main limb. Assign an RGBA leaf-spray texture with a transparent background; opaque images remain rectangular. A built-in temporary leaf spray is used when the texture field is empty. The foliage color tints that texture. Smaller/fewer cards reduce geometry and overlapping transparent pixels; triangle count alone is not a GPU performance measurement. The supplied spray is original placeholder artwork, not copied from the video.

Validate Branch Growth also checks mesh reduction across all four species, valid triangles, retained socket samples, foliage placement independent of mesh detail, card vertex counts, UVs, hidden foliage and bare trunks. Tree export preserves these meshes and foliage cards.
## Bark textures

Bark material includes a Bark texture slot plus Bark Texture U and Bark Texture V controls. An original procedural, seamless bark texture is supplied when the slot is empty. Assign a seamless color texture with Repeat wrapping to replace it; the Bark color multiplies its color (use white for the source colors). Import settings on assigned textures are not modified.

Trunk and branches have cylindrical UVs with duplicated seam vertices, length measured along the growth skeleton, and separate planar UVs on the end caps. Bends and twists carry the texture with the wood. Tiling does not add triangles or alter growth; UV seam/cap splits add vertices only. Integer repeats around the circumference best preserve a seamless join. Branch junctions remain overlapping surfaces, so bark patterns are not blended between parent and child. Normal/displacement maps are not provided. Bark textures and materials are included in tree export.
## Stem-attached needle bunches

In Foliage cards, assign a pine-bunch texture and click **Align bottom-centre stems**. This enables Stem attached, resets the rotation, and uses pivot (0.5, 0). The texture's upward direction follows the terminal branch. Each card's pivot remains on its branch socket while Bunch tilt, turn and roll rotate the card around it. Fan angle spreads the cards around the branch axis. Stem pivot X/Y can compensate for transparent margins or stems away from the centre (UV origin is bottom-left). Attached cards preserve the texture aspect ratio. Cluster spread applies only to the original floating-cluster mode; Clusters and Cluster distance control how many branch sockets receive bunches and where. Existing recipes retain the original mode until attachment is enabled.

## Branch fill and variable depth

Sub-branches per limb selects 1–4 children, filling from the tip inward: at 100%, 75%, 50%, then 25% of parent length. Existing sockets and child shapes remain stable when adding children. Each child follows the local direction and tapered radius at its socket; golden-angle spacing distributes children around the parent. The two-handle Forking level range selects inclusive minimum and maximum extra generations (0–3). Each main limb independently draws a seeded depth in that range; its descendants grow to that selected depth. Equal handles give fixed depth, and 0–0 disables forks. Existing recipes keep their previous fixed depth until the range is edited. Changing the seed changes the randomized depths. More children and levels multiply mesh and foliage cost; monitor the live triangle counters.
## Gradient tints

Preview navigation: left-drag to orbit, middle-drag or Shift + left-drag to pan, and scroll to zoom. Panning follows the camera's view plane and scales with zoom. Reset view restores the centered framing, orbit and zoom. View offsets persist while adjusting tree settings.

Bark material and Foliage material have independent gradient toggles. Each gradient replaces its solid tint and supports multiple color stops. Bark uses normalized tree height. Foliage placement offers Tree height, seeded Random per card (each leaf spray gets one color), and Stem to tip (bottom-to-top texture UV). For a card containing many leaves, Random per card colors the whole spray; it cannot identify individual painted leaves. Gradients multiply the source texture, so neutral textures give the clearest palette. Alpha stops are ignored to preserve leaf cutouts and opaque wood. Solid tints remain the default for saved recipes.

A dedicated preview shader samples 256-pixel color ramps, retaining intermediate color stops without adding mesh subdivisions. The preview combines into at most two meshes. Preview and runtime tree shaders share the material layout and foliage lighting in `Shaders/TreeLighting.hlsl`; runtime export retains gradients and adds wind animation.

## Builder organization

Controls follow Silhouette, Trunk shape, Branches (including bending), Foliage layout, Foliage orientation, Bark material, Foliage material, Mesh detail, and Family variations. Each section has a right-aligned ? icon with hover help; preview navigation and export help use the same convention. Material sections keep textures, tint/gradient controls and surface lighting together. Bark Texture U is circumferential repetition; Bark Texture V is repetition per metre. Section expansion is retained during UI rebuilds within the window session. The redundant Matte foliage preset button has been removed; all roughness and reflection controls remain.

Design references: Unity Foundations foldouts and tooltips (https://www.foundations.unity.com/components/foldout, https://www.foundations.unity.com/components/tooltip), Houdini parameter interfaces (https://www.sidefx.com/docs/houdini/ref/windows/edit_parameter_interface.html), and Odin Inspector grouping (https://odininspector.com/attributes/foldout-group-attribute). These support grouping related parameters, collapsible sections and contextual help. No additional UI plugin dependency was added.



