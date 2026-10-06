# Exporting trees to Unity Terrain

1. In Asset builder, choose the tree recipe, mesh detail and sibling count.
2. Choose **Generate LOD models** (enabled by default), optionally enable **Add trunk collider**, then click **Generate prefab family**. Expand **LOD settings** to tune screen-size thresholds and foliage retention.
3. The exporter creates a new folder under `Assets/TerrainityGenerated/Trees`. Each sibling gets a mesh and prefab; the folder also contains the JSON recipe. Matching materials and textures are reused across exports in `Assets/TerrainityGenerated/Shared/Materials` and `Shared/Textures`. Existing exports are never overwritten.
4. Choose **Target Terrain**, then **Add family to Terrain**. This appends the prefabs to its Paint Trees palette, preserves existing prototypes and planted trees, and supports Undo. TerrainData shared by several Terrain objects shares this palette too.
5. Use Terrain's **Paint Trees** tool to plant them. Alternatively, choose **Edit Trees > Add Tree** and select a generated prefab manually.

**Locate resources** selects the output folder. The Generated library lists exported prefabs. **Export Unity package** saves the family and runtime shader for importing into another project. Install a compatible Universal Render Pipeline package in the destination project (this project uses URP 17.6 / Unity 6000.6); package files themselves are not bundled.

## What is saved

- Prefabs: one grounded, identity-transform prefab per sibling, with a root LODGroup.
- Models: baked meshes, with separate bark and foliage submeshes and gradient coordinates. LOD export adds LOD1 and LOD2 meshes and assigns them automatically.
- Shared/Materials: runtime Terrainity/Tree materials, including roughness/specular settings, cutouts and tint gradients. Matching settings and texture references reuse an existing material; different settings create another material.
- Shared/Textures: content-matched copies of bark, foliage and gradient textures, including generated defaults. Identical pixels, color space, mipmaps and sampling settings reuse one texture even when material settings differ. Source import settings are left alone.
- Recipes: a JSON recipe referencing the exported textures.
- Optional collider: a capsule approximating the lower trunk; check the fit on strongly bent trees.

The editor preview's **Show foliage** toggle is an inspection aid. Exports include foliage according to the recipe's branch/card settings.

## Shared resource behavior

New exports use this layout:

```text
TerrainityGenerated/
  Trees/[Asset name]/Prefabs, Models, Recipes
  Shared/Materials
  Shared/Textures
```

Editing a shared material or texture affects every prefab referencing it. Future exports compare the current contents rather than trusting filenames, so edited resources are not silently overwritten. Duplicate a material and assign it to a prefab when you want an independent manual variation. Package export follows references and includes the shared dependencies needed by that family.

Existing exports retain their original folder layout and references; they are not automatically migrated or deleted. Generate them again to use the shared layout. Failed or cancelled exports remove only the shared assets created by that attempt. Library deletion keeps shared resources.

## Deleting library entries

Use **Delete prefab** beside a Generated library entry, then confirm the displayed asset path. The prefab and its metadata move to the Recycle Bin; shared meshes, materials, textures, recipes and other siblings remain. The library refreshes after deletion. References from existing Terrains or scenes will be missing, so remove the prefab from their palettes before deleting it. Unity Undo cannot restore this action; restore both the prefab and its `.meta` file from the Recycle Bin to recover its references.

## Current limits

Exports optionally include three mesh LODs with dithered transitions. See [Tree LODs](Tree-LOD.md) for the research, controls, reduction method and review workflow. The Terrainity/Tree shader animates branches and foliage from active Unity Wind Zones at runtime; the package includes TreeWindController, which bridges Wind Zone settings to the shader. Directional zones affect the scene and up to four spherical zones contribute within their radii. Whole-tree billboards are not generated. Use mesh detail and branch/card density controls to tune the budget before generating large forests. Junctions retain overlapping surfaces.

The runtime shader has URP lighting, cutout shadow/depth passes, instancing and Terrain tree tint support. A Built-in fallback is supplied; validation was performed in this project's URP configuration. HDRP is not supported.

Run **Tools > Terrainity > Validate Tree Export** to exercise persistence, texture pixels, recipes, export packages and Terrain palette preservation on temporary test assets.

Reference: [Unity Terrain trees](https://docs.unity3d.com/6000.0/Documentation/Manual/terrain-Trees.html).


