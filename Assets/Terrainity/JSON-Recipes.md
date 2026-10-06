# Tree recipes and family profiles (JSON v1)

Use **Save recipe** / **Load recipe** below Tree family to keep and restore a complete tree setup, including seed, sibling selection, geometry, card placement, material settings, gradients and an embedded family profile. Loading only replaces the current setup after validation succeeds. Save before loading or changing family if you want to keep the current work. File dialogs handle overwrite confirmation.

Texture images are not embedded. JSON stores Unity asset GUIDs and project-relative paths; resolution uses GUID first, then path. Move textures inside Unity to preserve GUIDs. When sharing between projects, copy the texture assets too. Missing textures produce a message and fall back to the built-in texture.

## Family workflow

**Save as family** captures the current tree as a reusable default and copies its family growth rules. The chosen filename becomes the display name and a lowercase, hyphenated id. Save into `Assets/Terrainity/Families` to include it in the dropdown, or use **Import family** for a JSON from elsewhere. Import requires a unique id and copies a validated file into the project without overwriting existing profiles.

Selecting a family applies its default recipe and branch settings. All 13 shipped families have a `defaults` block with the active builder settings. The branch reset button restores the selected family's `profile.branches`. Recipe files embed their family so they don't depend on the library remaining unchanged. The dropdown checks file paths, sizes and modification times when the builder is rebuilt and reuses unchanged parsed documents; switch tabs after editing or adding a file externally. Validate JSON Recipes forces a fresh read.

To make a new tree type without C# changes, duplicate a family JSON, change `profile.id` and `profile.name`, then tune the fields below. Keep `defaults.family` and `defaults.settings.customBranches` aligned with `profile`, or use **Save as family** in the builder. Unique ids identify families. Invalid files are skipped with a Console warning.

| Family field | Meaning / range |
| --- | --- |
| format / version | `terrainity.family` / `1` |
| profile.id / name / description | Stable unique id, dropdown name, branch tooltip |
| limbsPerWhorl | 1 gives spiral placement; 2–12 gives tiered groups |
| whorlRotation | Rotation in degrees between tiers |
| branchRotation | Rotation in degrees between spiral limbs |
| crownTipScale | Relative limb length at the crown top; 0.01–2 |
| lift | Upward or downward curvature contribution |
| liftExponent | Distribution of lift along a limb; 1–8 |
| forkAngle | Child angle from its parent tangent; 0–180 degrees |
| childLengthScale | Child length relative to parent; 0.05–1 |
| foliageAspect | Floating foliage card height multiplier; 0.1–4 |
| branches | Default limb counts, depth, spread, bend, angle, droop, thickness, taper and trunk attachment thickness |
| defaults | Optional complete `terrainity.recipe` v1 document, created by Save as family |

Branch generator safety limits still apply: 0–24 main limbs, 1–4 children and 0–3 extra generations. These are artistic growth parameters, not a botanical simulator.

## Recipe document

`format: terrainity.recipe`, `version: 1`, `settings` contains builder fields, `family` contains the growth profile, and `sibling` selects the preview variant. `barkGradient` and `foliageGradient` contain explicit `colors` (color/time), `alpha` (alpha/time), and `mode` fields. Texture GUID/path pairs are top-level fields. Embedded family rules are authoritative; the library is not consulted when loading a recipe. Format and version must be root fields; nested names cannot supply a missing header. Unknown versions and invalid numeric values, including values inside arrays and lists, are rejected. Tree and rock JSON files are limited to 1 MB.

For card foliage, `settings.foliageSizeMin` and `settings.foliageSize` set the smallest and largest per-card size (0.25–3). Each card receives a repeatable seeded value in that range. Older recipes without `foliageSizeMin` keep their original fixed card size. PalmFrond continues to use `foliageSize` as frond width.

`settings.crownTaper` (0–1) progressively shortens upper branches and scales their foliage toward the crown top. Zero preserves the original silhouette; at one, the top retains 25% of its untapered reach and card size. Older recipes default to zero.

`settings.rootBend` (0–1) adds independent seeded root curves, varying direction, strength and bend position, with a smaller secondary bend. Bending stays horizontal and retains each root's socket, tip and depth. Zero preserves the unbent paths. `settings.rootSharpness` blends those curves into angular bends; their sampled corners are retained through mesh simplification and LOD generation. Regenerate existing prefabs to apply the updated bend style.

`settings.foliageClusterDistance` ranges from 0 (clusters bunched at the terminal branch tip) to 1 (clusters span from tip to the start of that branch). Older recipes without the field default to 0.

`settings.foliageAligned` switches card foliage to rows along each terminal branch. `settings.layers` still sets the number of rows and `settings.foliageClusterDistance` controls how far they extend toward the branch base. `settings.foliageAlignSides` (1–3) divides each row evenly around the branch. Every other row gets a separate seeded world-Y offset on each side between `settings.foliageAlignOffsetMin` and `settings.foliageAlignOffsetMax` (metres, -0.5–0.5). `settings.foliageCardTaper` (0–1) shrinks cards from the first placed row toward the tip, down to 20% at maximum; cards closest to the trunk keep their chosen size even at partial cluster distance. These fields do not affect the original cluster layout when `foliageAligned` is false.

`settings.foliageCardToBranchSize` (0–1) scales each terminal branch's cards by its total trunk-to-tip path length, compared with the longest terminal branch path. Zero preserves chosen card sizes. At one, cards on the longest branch keep their size while shorter branches scale down, to a minimum of 20%. The path includes parent limbs and is measured before LOD pruning. All cards on one branch share this factor. It applies to aligned and cluster card foliage, and combines with Card size taper when aligned.

`settings.foliageBackfaceCulling` enables front-face-only foliage rendering. It defaults to false, preserving two-sided foliage in older recipes.

`settings.prunedFoliageCards` stores the individual foliage cards hidden with Prune Tree. Card IDs follow deterministic limb, cluster and side indices, so the same cards are omitted from the live preview, LODs and exported meshes. Reset prune clears this list.
Clicking a branch in Prune Tree hides the cards on that limb and all its child limbs by adding their IDs to the same list.

Use **Tools > Terrainity > Validate JSON Recipes** to check round-trips, gradients, custom families, deterministic branch geometry and texture references (when the project's sample texture is available). Exporting a recipe is separate from prefab/mesh export.

## Additional family forms

See [Family-References.md](Family-References.md) for the 13 reference species and tuning notes. `profile.foliageForm` selects Broadleaf (0, legacy default), Needles (1), or PalmFrond (2). `profile.crownEnd` sets the upper socket fraction; 0 preserves the legacy 0.9 endpoint. These optional fields also live in embedded recipe profiles. Older JSON without them keeps its original geometry. PalmFrond uses terminal branch curves as folded feather-frond ribbons, controlled by Frond width.
