# Tree recipes and family profiles (JSON v1)

Use **Save recipe** / **Load recipe** below Tree family to keep and restore a complete tree setup, including seed, sibling selection, geometry, card placement, material settings, gradients and an embedded family profile. Loading only replaces the current setup after validation succeeds. Save before loading or changing family if you want to keep the current work. File dialogs handle overwrite confirmation.

Texture images are not embedded. JSON stores Unity asset GUIDs and project-relative paths; resolution uses GUID first, then path. Move textures inside Unity to preserve GUIDs. When sharing between projects, copy the texture assets too. Missing textures produce a message and fall back to the built-in texture.

## Family workflow

**Save as family** captures the current tree as a reusable default and copies its family growth rules. The chosen filename becomes the display name and a lowercase, hyphenated id. Save into `Assets/Terrainity/Families` to include it in the dropdown, or use **Import family** for a JSON from elsewhere. Import requires a unique id and copies a validated file into the project without overwriting existing profiles.

Selecting a family applies its default recipe and branch settings. All 13 shipped families have a `defaults` block with the active builder settings. The branch reset button restores the selected family's `profile.branches`. Recipe files embed their family so they don't depend on the library remaining unchanged. The dropdown scans family files whenever the builder is rebuilt; switch tabs after editing or adding a file externally.

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

`format: terrainity.recipe`, `version: 1`, `settings` contains builder fields, `family` contains the growth profile, and `sibling` selects the preview variant. `barkGradient` and `foliageGradient` contain explicit `colors` (color/time), `alpha` (alpha/time), and `mode` fields. Texture GUID/path pairs are top-level fields. Embedded family rules are authoritative; the library is not consulted when loading a recipe. Unknown format versions are rejected. JSON files are limited to 1 MB.

For card foliage, `settings.foliageSizeMin` and `settings.foliageSize` set the smallest and largest per-card size (0.25–3). Each card receives a repeatable seeded value in that range. Older recipes without `foliageSizeMin` keep their original fixed card size. PalmFrond continues to use `foliageSize` as frond width.

Use **Tools > Terrainity > Validate JSON Recipes** to check round-trips, gradients, custom families, deterministic branch geometry and texture references (when the project's sample texture is available). Exporting a recipe is separate from prefab/mesh export.

## Additional family forms

See [Family-References.md](Family-References.md) for the 13 reference species and tuning notes. `profile.foliageForm` selects Broadleaf (0, legacy default), Needles (1), or PalmFrond (2). `profile.crownEnd` sets the upper socket fraction; 0 preserves the legacy 0.9 endpoint. These optional fields also live in embedded recipe profiles. Older JSON without them keeps its original geometry. PalmFrond uses terminal branch curves as folded feather-frond ribbons, controlled by Frond width.
