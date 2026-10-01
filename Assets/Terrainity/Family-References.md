# Tree family references and tuning

The library now contains 13 families. Birch is updated; Spruce, Cedar, Sycamore, Elm, Fir, Poplar, Aspen and Palm are added. Select a family to apply its complete JSON default recipe. Existing saved recipes retain their embedded profile.

## Scope

Common names cover many species. These are the explicit reference types chosen for this library. Defaults are artistic landscape-scale representations within the builder limits, not maximum mature botanical dimensions. Crown width is a growth input: child branches and cards extend beyond it. The Height control measures the trunk, not the final canopy.

## Parameter comparison

Standard recipe baseline: 7 m trunk height, 3.8 m crown-width input, 0.2 m trunk radius and 0.25 crown start.

| Family | Trunk height / crown input (m) | Trunk radius (m) | Crown start | Main limbs / children | Fork depth | Primary angle / upper lift | Bend / downward weight |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Birch | 12 / 5 | 0.22 | 0.4 | 13 / 3 | 2–3 | 48° / 18° | 0.6 / 0.3 |
| Spruce | 12 / 5.2 | 0.28 | 0.3 | 24 / 3 | 3–3 | 88° / 22° | 0.35 / 0.22 |
| Cedar | 12 / 7 | 0.4 | 0.42 | 21 / 3 | 2–3 | 82° / 18° | 0.45 / 0.25 |
| Sycamore | 10 / 8 | 0.6 | 0.35 | 10 / 3 | 2–3 | 60° / 18° | 0.5 / 0.35 |
| Elm | 10 / 7 | 0.5 | 0.32 | 9 / 3 | 3–3 | 28° / 0° | 0.55 / 0.2 |
| Fir | 12 / 5 | 0.28 | 0.23 | 24 / 3 | 3–3 | 88° / 32° | 0.18 / 0.18 |
| Poplar | 14 / 3.5 | 0.26 | 0.2 | 20 / 3 | 2–3 | 16° / 6° | 0.2 / 0.08 |
| Aspen | 12 / 4.5 | 0.21 | 0.48 | 11 / 3 | 2–3 | 45° / 12° | 0.3 / 0.18 |
| Palm | 9 / 8 | 0.48 | 0.93 | 24 / 1 | 0–0 | 95° / 45° | 0.6 / 0.12 |

## Image observations and sources

### Birch — European silver birch

Pale slender trunk and pendulous fine twigs. Increased crown height and child density from the old Birch preset; retained smooth downward curvature and narrow forks.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/betula-pendula) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/bepe339A.jpg)

### Spruce — Norway spruce

Pyramidal outline with drooping secondary shoots. Four limbs per whorl, a strongly tapering crown, negative lift and dark needle sprays distinguish it from Pine.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/picea-abies) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/piab977.jpg)

### Cedar — Deodar cedar

Broad spreading tiers and softly drooping shoots. Wider crown, three limbs per tier, longer child branches and muted gray-green foliage distinguish it from Spruce.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/cedrus-deodara) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/cede0967A.jpg)

### Sycamore — American sycamore

Large supporting trunk, broad irregular crown and broad lobed leaves. Thick limbs, wider forks and larger foliage clusters produce a robust silhouette.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/platanus-occidentalis) · [Inspected reference photograph](https://www.portland.gov/sites/default/files/2020-06/015.jpg?auto=false)

### Elm — American elm

Ascending scaffold limbs spread into a vase. Steeper primary limbs and longer upper limbs lift and widen the canopy; gentle downward curvature softens the ends.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/ulmus-americana) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/ulam846B.jpg)

### Fir — White fir

Regular conical form with ascending upper branches and pale blue-green needles. Smaller bend, shorter children and a narrower crown tip distinguish it from Spruce.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/abies-concolor) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/abco9984.jpg)

### Poplar — Lombardy poplar

Extremely narrow upright crown. Steep primary angles, 14-degree forks and a small crown width keep the branch network close to the trunk.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/populus-nigra-italica) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/ponii680B.jpg)

### Aspen — Quaking aspen

Long pale trunk and a relatively open elevated crown. Higher crown start, smaller limbs and moderate upward lift distinguish it from Birch.

[Botanical description](https://landscapeplants.oregonstate.edu/plants/populus-tremuloides) · [Inspected reference photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/potre931.jpg)

### Palm — Canary Island date palm

Terminal crown of long feather fronds on an unbranched trunk. No recursive forks; 24 curved, folded ribbons surround the top of a lightly tapered trunk.

[Botanical description](https://ask.ifas.ufl.edu/publication/ST439) · [Inspected reference photograph](https://ask.ifas.ufl.edu/image/ST439/11642562/16748718/16748718-2048.webp)

## Preview comparison and limitations

Every family was rendered and visually compared with its reference. The conifers have distinguishable tapered crowns; Poplar is a column, Elm spreads above upright limbs, and Aspen exposes more trunk. Palm uses actual curved ribbons instead of broadleaf clusters. Compared with photographs, these remain simplified silhouettes: generic broadleaf shapes, procedural bark, fewer palm fronds, and evenly distributed branching. Birch/Aspen bark lacks species-specific scars; Sycamore lacks exfoliating patchwork. Dedicated artwork can replace the procedural textures through the existing material fields. No reference photographs are copied into project assets.

The built-in needle spray has a 1:2 texture aspect ratio and tapered alpha outline. Palm uses the same feather pattern along curved ribbons. Its Foliage layout shows Frond width; frond count and curvature are controlled through Branches. Card-only controls are hidden for this form.

## Validation

Run **Tools > Terrainity > Validate Family Library**. It loads all nine defaults, checks JSON geometry round-trips, renders five siblings per family and checks finite vertices and foliage ground clearance. It writes reference previews and a measurement report to Temp. The initial seed is 1842; clearance is verified for those five shipped siblings, not arbitrary seeds or slider combinations. Existing JSON recipe checks run as part of this validation.

Measurements from the verified defaults (height and width are maxima across five siblings; triangle counts are sibling 1):

```text
Birch: foliageMin=3.35, height=12.92, width=7.63, wood=32750, foliage=7128
Spruce: foliageMin=0.63, height=12.11, width=8.95, wood=70780, foliage=15552
Cedar: foliageMin=0.50, height=12.77, width=11.49, wood=46170, foliage=10152
Sycamore: foliageMin=2.86, height=14.74, width=13.21, wood=29840, foliage=6480
Elm: foliageMin=4.02, height=14.91, width=11.20, wood=26890, foliage=5832
Fir: foliageMin=0.64, height=12.08, width=8.21, wood=70780, foliage=15552
Poplar: foliageMin=4.24, height=15.05, width=2.47, wood=45480, foliage=9936
Aspen: foliageMin=6.43, height=13.77, width=5.97, wood=30810, foliage=6696
Palm: foliageMin=6.51, height=10.60, width=8.36, wood=2500, foliage=960
```
