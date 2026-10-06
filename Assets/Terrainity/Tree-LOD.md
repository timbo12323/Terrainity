# Tree LODs: design, controls and review

A Level of Detail (LOD) is another mesh of the same tree. Unity renders the detailed version nearby and switches to simpler versions when the tree occupies less of the screen. This reduces vertex and triangle work; texture sampling, overlapping alpha-cutout foliage, shadows and draw calls still matter. A lower triangle count alone is not a measured frame-rate improvement.

## Using the exporter

Enable **Generate LOD models** under Destination / Paint Trees. **LOD settings** exposes:

| Control | Default | Meaning |
| --- | --- | --- |
| LOD1 screen height | 0.35 | Leave full-detail LOD0 below 35% of screen height |
| LOD2 screen height | 0.12 | Use the simplest mesh below 12% |
| Cull screen height | 0.01 | Stop rendering below 1% |
| Far foliage retained | 0.50 | Retain roughly half the cards per cluster in LOD2, with at least two |
| Leaf size compensation | 0.15 | Scale surviving cards to replace lost visible area; 0 disables growth |
| Dithered transitions | On | Fade between levels across 15% of each LOD interval |

These are starting values, not universal optimal distances. Camera field of view, object scale, Unity Quality LOD Bias, Terrain tree distance and resolution affect the visible result. The export has three meshes per sibling, one root LODGroup, the full-detail renderer on the root, and child renderers for LOD1/LOD2. All levels use the same materials and textures. Colliders stay on the root and do not change with visual LOD.

`Models` contains the baked meshes. `Recipes/LODSettings.json` records the export controls separately from the tree recipe. `LOD-Report.csv` records actual triangle counts. Existing prefabs are not rewritten; generate a new export to get LODs. Disabling generation retains the previous single-mesh export behavior.

## How reduction works

**Wood:** regenerate from the same seed and growth skeleton, with approximately 70% and 40% of the radial/longitudinal resolution. Rank terminal child branches by their radial position and length within canopy-height bands, with a small seeded tie-breaker. At LOD1, roughly the least exposed 10% disappear and the next branches through 35% shrink toward their parent sockets. At LOD2, roughly the least exposed 30% disappear and the next branches through 50% shrink farther. Counts round down, and very small canopies may keep every branch. The subsets are nested: a branch never reappears at a farther LOD. Main limbs and roots remain. LOD2 also simplifies trunk and branch rings within a positional tolerance of 0.5% of tree height and a relaxed normal tolerance. It retains main limb attachment rings and intentional trunk bends; terminal twig sockets and small collar rings may collapse. Minimums are three sides and two segments. These are resolution factors, not triangle targets: protected rings and minimums limit reduction.

**Broadleaf and needle cards:** keep foliage clusters at their original canopy sockets when interior supporting twigs shrink or disappear. This preserves crown coverage at a distance. In each cluster, rank cards by outward position and facing, with a small seeded tie-breaker; the hidden, inward-facing cards disappear first. Cards that remain at LOD1 but disappear at LOD2 contract toward their stem or card spine. LOD1 and LOD2 keep nested subsets. Copy the original normals, texture coordinates and gradient coordinates so surviving leaves do not change colors. Size compensation grows surviving cards toward the area lost to card reduction, with more growth at the canopy edge. Cards scale around their attachment pivot. At the default setting, a cluster reduced from eight to four cards grows its outer surviving cards by about 41%. Very sparse two-card clusters keep both cards. Compensation increases overdraw and can expand the outline slightly; reduce it for delicate silhouettes.

When **Align Foliage** is enabled, each row's one to three sides becomes the reduction group. LOD2 keeps both sides of a two-side row and at least two cards of a three-side row; a single-side row remains intact.

**Palm:** keep the original frond silhouette when interior supporting wood is pruned, then retain each frond's attachment, tip and middle row while reducing the number of ribbon rows. Exposed outer leaflets widen around the frond spine according to Leaf size compensation.

Generic edge-collapse decimation is useful for solid meshes, but can destroy individual two-triangle leaf cards. This exporter uses its own procedural structure instead. The ranking is baked and view independent: it favors the outer canopy but does not measure occlusion from every camera angle or create SpeedTree morph data. Branch junctions remain overlapping rather than welded.

## Transitions and validation

The runtime shader supports `LOD_FADE_CROSSFADE` in URP color, shadow, depth and normal passes. The Built-in fallback includes dither support too, but validation is in this project's URP setup. Enable **LOD Cross Fade** in the URP asset; this project already has it enabled. Cross-fading renders two meshes in transition and has a cost. It is optional. SpeedTree fade mode is not used because these meshes do not contain SpeedTree's vertex-morph data.

Review a generated prefab using Unity's LODGroup Inspector preview slider, then paint it on a test Terrain and move the game camera toward and away from it. Check several angles, backlighting, shadows and your actual target resolution. Look for canopy thinning, branch jumps, foliage shimmer and abrupt color changes. Tune screen thresholds before making aggressive geometry cuts. Profile a forest on the target hardware; compare GPU time and overdraw as well as triangle counts.

Run **Tools > Terrainity > Validate Tree LODs** for representative Birch, Spruce, Willow and Palm exports, two siblings each, mesh validity, reduction, assignment, shared materials, persisted settings and runtime shader checks. The check writes front/side reference images and counts into `Temp`.

## Further improvements

For very large forests, a baked multi-angle whole-tree impostor is a useful additional distant LOD. It requires a texture atlas, view selection, lighting/normals and shadow handling; a flat screenshot on a quad will not match well from all directions. It is not generated here. Wind weights and per-card phase are retained through LOD generation so the same Wind Zones move every mesh level.

Foliage mipmaps and alpha coverage deserve separate treatment: distant alpha-tested leaves can disappear even when the geometry is retained. Test texture alpha coverage at the material cutoff. Normal-map/baked lighting consistency and shadow-distance tuning can matter as much as geometric detail.

## Research references

- [Unity LODGroup](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LODGroup.html): screen-relative thresholds, renderer assignment and preview controls.
- [Unity LOD transitions](https://docs.unity.com/en-us/engine/6000.3/manual/analysis/graphics-performance-profiling/lod/lod-group/lod-transitions-lod-group): URP cross-fade prerequisite, fade widths, shader keyword and differences from SpeedTree morphing.
- [Unity Terrain tree LOD](https://docs.unity3d.com/6000.0/Documentation/Manual/terrain-Tree-LOD.html): LODGroup trees and legacy Tree Editor billboards use different paths.
- [SpeedTree card generator](https://docs9.speedtree.com/modeler/doku.php?id=card_generator): increasing leaf size at lower resolution can offset reduced leaf counts; canopy-oriented normals improve cluster shading.
- [SpeedTree 8 introduction](https://docs8.speedtree.com/modeler/doku.php?id=st8intro): silhouette contribution guides its LOD system. Our conservative structure-preserving implementation is inspired by that aim, not an implementation of SpeedTree's algorithm.
- [Epic automatic LOD generation](https://dev.epicgames.com/documentation/unreal-engine/setting-up-automatic-lod-generation?application_version=4.27): quadratic simplification ranks edge collapses by visual error. Useful background for solid-mesh decimation, not a leaf-card preservation guarantee.
