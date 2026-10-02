# Tree family references and defaults

Each of the 13 family files contains a complete `terrainity.recipe` default. Selecting a family applies its embedded settings; saved recipes retain their own copy. Common names can cover several species, so the reference species below make the intended form explicit. Dimensions are landscape-scale builder inputs, not maximum mature botanical measurements. Height measures the trunk; generated branches and foliage can extend beyond the crown-width input.

The botanical sources guided silhouette, branch angle, crown density, bark and foliage color, and the newly exposed root, taper, surface detail, translucency, and card-shading controls. Numeric values are artistic translations into the builder's supported ranges. The built-in broadleaf and needle masks are generic, so they cannot reproduce species-specific leaf margins, bark scars, cones, flowers, or seeds. No reference photographs are copied into the project.

| Family | Reference type and defining traits | Default tuning |
| --- | --- | --- |
| [Aspen](https://landscapeplants.oregonstate.edu/plants/populus-tremuloides) | Quaking aspen; long pale trunk, narrow rounded upper crown, thin fluttering leaves | High crown start, fine branch bases, light bark, small soft leaf cards, restrained roots |
| [Birch](https://landscapeplants.oregonstate.edu/plants/betula-pendula) | European silver birch; white trunk and fine pendulous shoots | Open high crown, narrow drooping foliage, slender branches and low bark relief |
| [Cedar](https://landscapeplants.oregonstate.edu/plants/cedrus-deodara) | Deodar cedar; broad pyramidal tiers and drooping leaders | Wide whorls, hanging needle sprays, substantial branch sockets and gray-green tint |
| [Elm](https://landscapeplants.oregonstate.edu/plants/ulmus-americana) | American elm; ascending scaffold limbs arch into a vase | Steep main limbs, long upper reach, thicker attachments and elevated foliage |
| [Fir](https://landscapeplants.oregonstate.edu/plants/abies-concolor) | White fir; conical, lower branches horizontal, upper branches rise | Narrow cone, low crown start, pale blue-green needles and little branch bend |
| [Maple](https://landscapeplants.oregonstate.edu/plants/acer-saccharum) | Sugar maple; ascending branches and dense oval crown | Moderate fork angles, fuller broadleaf clusters, plated gray bark and rounded canopy |
| [Oak](https://landscapeplants.oregonstate.edu/plants/quercus-robur) | English oak; massive short trunk, heavy spreading limbs and rounded crown | Wider lower limbs, thick branch bases, broad crown, coarse bark and broad roots |
| [Palm](https://ask.ifas.ufl.edu/publication/ST439) | Canary Island date palm; single thick trunk and terminal feather crown | Unbranched trunk, folded frond form and no exposed lateral roots |
| [Pine](https://landscapeplants.oregonstate.edu/plants/pinus-sylvestris) | Scots pine; open high crown, spreading limbs and orange upper bark | Raised irregular crown, needle form, open sprays and bark gradient |
| [Poplar](https://landscapeplants.oregonstate.edu/plants/populus-nigra-italica) | Lombardy poplar; very narrow, dense column | Upright short limbs, tight forks, small leaf cards and narrow roots |
| [Spruce](https://landscapeplants.oregonstate.edu/plants/picea-abies) | Norway spruce; tapered cone and drooping secondary branches | Near-horizontal whorls, hanging needle sprays and dense interior shade |
| [Sycamore](https://landscapeplants.oregonstate.edu/plants/platanus-occidentalis) | American sycamore; massive trunk, wide crown and pale exfoliating bark | Broad limbs, large leaf clusters, heavy roots and pale bark gradient |
| [Willow](https://landscapeplants.oregonstate.edu/plants/salix-babylonica) | Weeping willow; rounded crown and long pendulous twigs | Strong downward curvature, long narrow leaf cards, dark furrowed bark and spreading roots |

## Complete defaults

Every family now specifies all active builder fields in `TreeRecipeSettings`, including roots, trunk bend, branch taper and attachment thickness, foliage subdivisions and bend, material softness, feathering, transmission, occlusion, bark detail, and family variation. The four legacy branch fields used only by profile-free recipes are omitted. `profile.branches`, embedded `defaults.family.branches`, and `defaults.settings.customBranches` use the same values. The three former profile-only families (Maple, Oak, Pine) now have recipe defaults too. Texture paths remain empty so the built-in masks continue to work without external assets.

The values are intended to give a recognizable tree on first selection. They are not a botanical growth simulation. In particular, the palm generator caps visible fronds at 24, while a healthy mature Canary Island date palm can carry far more; its trunk also lacks the species' diamond leaf-base scars. The generic broadleaf mask cannot reproduce the exact lobes of oak, maple, or sycamore, and the bark shader does not create birch scars or true sycamore exfoliation. Species-specific texture artwork can be assigned in the builder when closer detail is needed.

## Validation

Run **Tools > Terrainity > Validate Family Library** after editing a family. It loads the JSON through Unity, checks recipe round-trips, and renders five seeded siblings for each family. The earlier preview measurements were taken before these updated defaults and are no longer representative.
