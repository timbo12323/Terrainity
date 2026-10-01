# Willow family

Select **Willow (willow)** in Tree family. This is a weeping-willow interpretation, supplied entirely through `Families/Willow.json`. It includes the default recipe, material settings and gradient, and supports normal recipe saving and family import/export.

## Reference comparison

Compared against Oregon State University's [summer photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/sabab0652A.jpg), [spring photograph](https://landscapeplants.oregonstate.edu/sites/plantid7/files/plantimage/sabab0133A.jpg), and [plant description](https://landscapeplants.oregonstate.edu/plants/salix-babylonica). The summer image shows dense hanging curtains and a broad crown; the spring image reveals the supporting arches and exposed trunk. These informed the following changes from standard recipe/Birch defaults.

| Parameter | Existing default | Willow | Purpose |
| --- | --- | --- | --- |
| Height / crown width input | 7 / 3.8 m | 9 / 7 m | Broader mature silhouette; child branches extend beyond the width input |
| Crown start | 0.25 | 0.65 | Leave clearance for descending shoots |
| Trunk radius | 0.2 m | 0.45 m | Stronger visible supporting trunk |
| Main limbs / children | Birch: 13 / 2 | 14 / 3 | Fill the hanging crown |
| Fork generations | Birch: fixed 3 | Random 2–3 | Break up uniform curtains |
| Main branch angle | Birch: 42° | 65° | Spread limbs outward |
| Bend / sharpness | Birch: 0.65 / 0 | 0.75 / 0 | Smooth arches |
| Fork angle | Birch: 27° | 18° | Keep fine branches in narrower hanging sprays |
| Family lift / child length | Birch: -0.42 / 0.55 | -0.38 / 0.55 | Downward tips without excessive cumulative drop |
| Clusters per terminal / cards per cluster | 2 / 6 | 4 / 3 | Distribute foliage along shoots |
| Card attachment / fan | Floating / 20° | Stem attached / 8° | Follow branch direction closely |

Matte foliage uses a muted yellow-green gradient; bark uses gray-brown tint. The preview still uses the existing procedural foliage and bark textures. Its leaves are broader and its crown more open than the photographs; a dedicated narrow-leaf willow spray texture is the next visual improvement. No reference photographs are bundled as textures.

## Verification

Default seed 1842, five siblings at 0.08 variation: branch points remain at least 0.758 m above ground, with 1,615 of 1,620 terminal tips descending. All five foliage meshes remain above ground. Combined branch measurements reach about 9.88 m tall and 11.82 m in radial diameter. First sibling: 43,770 wood triangles and 7,776 foliage triangles. These checks cover the shipped defaults, not every possible slider combination.

Run **Tools > Terrainity > Validate Willow Defaults** to repeat geometry/clearance checks and the existing JSON checks. Lower branch segments or fork depth for a cheaper mesh.
