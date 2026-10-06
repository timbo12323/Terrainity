# Rock builder

Open Terrainity's Asset builder and choose **Rocks**. Pick a starting preset: Natural boulder, River pebble, Layered slab, Jagged crag, Stylized boulder, or Low-poly crystal. Presets change shape, tint, roughness and topology while retaining the seed, assigned textures, family size and export options.

Width, height and depth describe the starting envelope in metres. Irregularity changes the outline; angularity introduces planar cuts; surface detail adds smaller variations. Flatten base compresses the underside without collapsing faces. Subdivisions control resolution (80 to 20,480 triangles). Flat shading creates hard triangle normals for a low-poly style.

The preview supports orbit, pan, zoom, vertical resizing, a resizable parameter column and the shared lighting panel. Next sibling cycles through repeatable seeded variations. Size variation changes sibling proportions. Lighting is preview-only.

The material uses opaque URP Lit, with tint, roughness, optional base texture and normal map, texture tiling and normal strength. Import normal maps using Unity's Normal map texture type. Spherical UVs include a seam and can stretch near the poles. Assigned textures are referenced without duplication. Matching exported materials are reused under `Assets/TerrainityGenerated/Shared`.

Save/load recipe uses versioned JSON with texture asset GUIDs. Texture references resolve when the corresponding assets are present in the project; JSON alone does not embed image data.

Generate prefab family exports to `Assets/TerrainityGenerated/Rocks/[name]` with Models, Prefabs and Recipes folders. Rocks appear in the generated library and use its existing asset deletion action. Drag a prefab into a scene. Export Unity package includes asset dependencies.

Optional LODs resample the same seeded surface at fewer subdivisions. Each step divides triangle count by four. Low-poly presets have two levels; higher resolutions have three. Pivots align across levels. The transitions switch at 35% and 12% screen height, culling at 1%; inspect transitions for strong surface detail. Cross-fading is not enabled. Optional non-convex MeshColliders use the full mesh and are intended for static scenery, not dynamic Rigidbody objects.

`Tools > Terrainity > Validate Rocks` checks geometry, deterministic generation, sibling variation, LOD reduction/alignment, JSON round-trip, prefab exports, shared materials and scene isolation. It removes its temporary exported test assets and writes results/images under `TerrainityReports/Rocks`.
