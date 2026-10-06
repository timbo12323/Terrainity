using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    // Shared serializable fields preserve the flat v1 JSON schema and Unity Undo paths.
    [Serializable]
    internal class TreeRecipeSettings
    {
        public string assetName = "Woodland Pine";
        public string species = "Pine";
        public BranchSettings customBranches;
        public int seed = 1842;
        public float height = 7f;
        public float trunkRadius = .2f;
        public bool rootsEnabled = false;
        public int rootCount = 6;
        public int rootForks = 0;
        public int rootMinForkDepth = 1;
        public int rootMaxForkDepth = 1;
        public float rootBend = 0;
        public float rootSharpness = 0;
        public float rootSpread = 1.5f;
        public float rootThickness = .65f;
        public float rootTaper = .95f;
        public float rootAttachmentHeight = .4f;
        public float rootDepth = .25f;
        // Ground placement is baked into every mesh, including reduced LODs.
        public float floorHeight = 0;
        public float taper = .7f;
        public float lean = .12f;
        public float trunkBend = 0;
        public int trunkBends = 1;
        public float trunkSharpness = 0;
        public float trunkTwist = 0;
        public float crownWidth = 3.8f;
        public float crownTaper = 0;
        public float crownStart = .25f;
        // Legacy JSON stores three units per foliage cluster; keep this encoding compatible.
        public int layers = 6;
        public int trunkSides = 8;
        public int trunkSegments = 12;
        public int branchSides = 6;
        public int branchSegments = 4;
        public float barkSurfaceDetail = 0;
        public float barkDetailScale = 1;
        public int barkDetailResolution = 2;
        public bool simplifyWood = false;
        // Stored in metres; the builder presents this tolerance in millimetres.
        public float simplificationTolerance = .002f;
        public int foliageCards = 6;
        // IDs encode limb, cluster and card so pruning survives recipe saves and LOD reduction.
        public List<int> prunedFoliageCards = new List<int>();
        public int foliageSubdivisions = 1;
        public bool foliageBackfaceCulling = false;
        public float foliageBend = 0;
        public float foliageSize = 1;
        // Zero migrates older recipes to a fixed size equal to foliageSize.
        public float foliageSizeMin = 0;
        public float foliageWidthScale = 1;
        public float foliageHeightScale = 1;
        public float foliageSpread = .5f;
        public float foliageClusterDistance = 0;
        public bool foliageAligned = false;
        public int foliageAlignSides = 2;
        public float foliageAlignOffsetMin = -.1f;
        public float foliageAlignOffsetMax = .1f;
        public float foliageCardTaper = 0;
        public float foliageCardToBranchSize = 0;
        public float foliageCutoff = .5f;
        public float foliageFeathering = 0;
        public float foliageSoftness = 0;
        public bool foliageAlphaToCoverage = false;
        public bool foliageTransmissionEnabled = false;
        public float foliageTransmission = .5f;
        public float foliageOcclusion = .6f;
        public float barkOcclusion = .35f;
        public bool foliageStemAttached = false;
        public Vector2 foliageStemPivot = new Vector2(.5f, 0);
        public Vector3 foliageRotation = Vector3.zero;
        public float foliageFanAngle = 20;
        public float irregularity = .22f;
        public Color bark = new Color(.32f, .21f, .12f);
        public float barkTilingAround = 1;
        public float barkTilingLength = 1;
        public Color leaves = new Color(.27f, .45f, .20f);
        public float foliageRoughness = 1;
        public bool foliageHighlights = false;
        public bool foliageReflections = false;
        public float barkRoughness = .88f;
        public bool barkHighlights = true;
        public bool barkReflections = true;
        public bool barkGradientEnabled = false;
        public bool foliageGradientEnabled = false;
        public FoliageTintMode foliageTintMode = FoliageTintMode.PerCard;
        public int variantCount = 1;
        public float variation = .12f;
        public bool showFoliage = true;
        public BranchSettings pineBranches = BranchSettings.ForSpecies("Pine");
        public BranchSettings oakBranches = BranchSettings.ForSpecies("Oak");
        public BranchSettings birchBranches = BranchSettings.ForSpecies("Birch");
        public BranchSettings mapleBranches = BranchSettings.ForSpecies("Maple");
    }

}
