using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class TreeRecipe : TreeRecipeSettings
    {
        public TreeFamilyProfile familyProfile;
        public bool useFamilyProfile;
        [NonSerialized] TreeFamilyProfile legacyProfile;
        public TreeFamilyProfile Profile
        {
            get
            {
                if (useFamilyProfile && familyProfile != null) return familyProfile;
                // Geometry reads this repeatedly; rebuild only when the legacy species changes.
                if (legacyProfile == null || legacyProfile.name != (species ?? "Pine"))
                    legacyProfile = TreeFamilyProfile.Legacy(species);
                return legacyProfile;
            }
        }
        [NonSerialized] internal float barkDetailLodScale = 1;
        [NonSerialized] internal bool lodAggressiveWood;
        public TreeRecipe()
        {
            // New trees vary card size; omitted v1 JSON settings keep the legacy fixed-size default.
            foliageSizeMin = .8f;
        }

        public Texture2D foliageTexture;
        public Texture2D barkTexture;
        public Gradient barkGradient = TintGradient(new Color(.20f, .12f, .07f), new Color(.58f, .43f, .25f));
        public Gradient foliageGradient = TintGradient(new Color(.12f, .3f, .08f), new Color(.8f, .65f, .16f));

        static Gradient TintGradient(Color from, Color to)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return gradient;
        }

        internal float CrownTaperScale(float crownHeightFraction)
            => 1f - .75f * Mathf.Clamp01(crownTaper) * Mathf.Clamp01(crownHeightFraction);

        public BranchSettings Branches
        {
            get
            {
                if (useFamilyProfile && familyProfile != null) return customBranches ?? (customBranches = BranchSettings.ForSpecies(species));
                if (species == "Oak") return oakBranches ?? (oakBranches = BranchSettings.ForSpecies("Oak"));
                if (species == "Birch") return birchBranches ?? (birchBranches = BranchSettings.ForSpecies("Birch"));
                if (species == "Maple") return mapleBranches ?? (mapleBranches = BranchSettings.ForSpecies("Maple"));
                return pineBranches ?? (pineBranches = BranchSettings.ForSpecies("Pine"));
            }
        }

        public void ResetBranches()
        {
            if (useFamilyProfile && familyProfile != null) { customBranches = familyProfile.branches.Copy(); return; }
            if (species == "Oak") oakBranches = BranchSettings.ForSpecies(species);
            else if (species == "Birch") birchBranches = BranchSettings.ForSpecies(species);
            else if (species == "Maple") mapleBranches = BranchSettings.ForSpecies(species);
            else pineBranches = BranchSettings.ForSpecies(species);
        }

        internal TreeRecipe Copy()
        {
            // Detach mutable settings while retaining references to imported texture assets.
            var copy = (TreeRecipe)MemberwiseClone();
            copy.legacyProfile = null;
            copy.familyProfile = familyProfile?.Copy();
            copy.customBranches = customBranches?.Copy();
            copy.pineBranches = pineBranches?.Copy();
            copy.oakBranches = oakBranches?.Copy();
            copy.birchBranches = birchBranches?.Copy();
            copy.mapleBranches = mapleBranches?.Copy();
            copy.prunedFoliageCards = prunedFoliageCards == null ? new List<int>() : new List<int>(prunedFoliageCards);
            copy.barkGradient = CopyGradient(barkGradient);
            copy.foliageGradient = CopyGradient(foliageGradient);
            return copy;
        }

        static Gradient CopyGradient(Gradient source)
        {
            if (source == null) return null;
            var copy = new Gradient { mode = source.mode };
            copy.SetKeys(source.colorKeys, source.alphaKeys);
            return copy;
        }
    }

    internal enum FoliageTintMode { Height, PerCard, StemToTip }

    [Serializable]
    internal sealed class BranchSettings
    {
        public int limbs = 16;
        public int depth = 3;
        // -1 migrates existing recipes to a fixed range at their saved depth.
        public int minDepth = -1;
        public int subBranches = 2;
        public float spread = 1;
        public float bend = .25f;
        public float sharpness = 0;
        public float angle = 75;
        public float upperAngleBias = 20;
        public float droop = .4f;
        public float thickness = .4f;
        public float taper = .8f;
        public float attachmentThickness = 1;

        internal BranchSettings Copy() => (BranchSettings)MemberwiseClone();

        public static BranchSettings ForSpecies(string species)
        {
            if (species == "Oak") return new BranchSettings { limbs = 9, depth = 3, spread = 1.15f, bend = .55f, angle = 58, upperAngleBias = 24, droop = .65f, thickness = .65f };
            if (species == "Birch") return new BranchSettings { limbs = 13, depth = 3, spread = .7f, bend = .65f, angle = 42, upperAngleBias = 18, droop = .8f, thickness = .3f };
            if (species == "Maple") return new BranchSettings { limbs = 9, depth = 3, spread = .9f, bend = .45f, angle = 52, upperAngleBias = 23, droop = .5f, thickness = .55f };
            return new BranchSettings();
        }
    }
}

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class TreeFamilyProfile
    {
        public string id = "custom";
        public string name = "Custom";
        public string description = "Custom tree family";
        public int limbsPerWhorl = 1;
        public float whorlRotation = 31, branchRotation = 137.508f;
        public float crownTipScale = .55f, lift = .38f, liftExponent = 2;
        public float forkAngle = 38, childLengthScale = .62f, foliageAspect = 1;
        // Zero preserves the original crown endpoint for older JSON recipes.
        public float crownEnd = 0;
        public TreeFoliageForm foliageForm = TreeFoliageForm.Broadleaf;
        public BranchSettings branches = new BranchSettings();

        internal TreeFamilyProfile Copy()
        {
            var copy = (TreeFamilyProfile)MemberwiseClone();
            copy.branches = branches?.Copy();
            return copy;
        }

        internal static TreeFamilyProfile Legacy(string species)
        {
            species = species ?? "Pine";
            var p = new TreeFamilyProfile { id = species.ToLowerInvariant(), name = species, branches = BranchSettings.ForSpecies(species) };
            if (species == "Pine") { p.limbsPerWhorl = 4; p.crownTipScale = .18f; p.lift = .22f; p.forkAngle = 48; p.childLengthScale = .48f; p.foliageAspect = .65f; }
            if (species == "Birch") { p.lift = -.42f; p.liftExponent = 3; p.forkAngle = 27; p.childLengthScale = .55f; }
            if (species == "Maple") { p.lift = .23f; p.forkAngle = 65; p.childLengthScale = .45f; }
            return p;
        }
    }

    internal enum TreeFoliageForm { Broadleaf, Needles, PalmFrond }

}
