using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    // Artistic species rules, not a botanical growth simulation. Each limb has its
    // own random stream so changing density or foliage does not reshuffle its neighbors.
    internal static class TreeBranchGrowth
    {
        internal const int Segments = 12;
        internal static float RadiusAt(float rootRadius, float t, float taper = .8f)
            => rootRadius * (1 - Mathf.Clamp(taper, 0, .95f) * Mathf.Clamp01(t));
        internal static float AttachmentRadiusAt(float rootRadius, float parentRadius, float t, float thickness, float taper = .8f)
        {
            float blend = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .35f));
            float scale = Mathf.Lerp(1, Mathf.Clamp(thickness, .5f, 2.5f), blend);
            return Mathf.Min(RadiusAt(rootRadius, t, taper) * scale, parentRadius * .95f);
        }
        internal sealed class Limb
        {
            internal Vector3[] points;
            internal Vector3[] foliagePoints;
            internal float radius;
            internal int level;
            internal bool terminal;
            internal bool pruned;
            internal bool isRoot;
            internal float parentRadius;
            internal Vector3 parentDirection;
            internal int parentIndex;
            internal float attachment;
            internal Vector2 rootBendPeaks;
        }

        internal static List<Limb> Generate(TreeRecipe recipe, Vector3 trunkTip, float spreadScale, int variant)
            => Generate(recipe, new TreeTrunkGrowth(recipe, trunkTip), spreadScale, variant);

        internal static List<Limb> Generate(TreeRecipe recipe, TreeTrunkGrowth trunk, float spreadScale, int variant)
        {
            var result = new List<Limb>();
            var settings = recipe.Branches;
            int count = Mathf.Clamp(settings.limbs, 0, 24);
            int depth = Mathf.Clamp(settings.depth, 0, 3);
            int minDepth = settings.minDepth < 0 ? depth : Mathf.Clamp(settings.minDepth, 0, depth);
            var profile = recipe.Profile;
            int whorl = Mathf.Clamp(profile.limbsPerWhorl, 1, 12);
            bool tiered = whorl > 1;
            for (int i = 0; i < count; i++)
            {
                var rng = new System.Random(unchecked(recipe.seed * 397 ^ (i + 1) * 7919));
                // A separate stream makes depth repeatable without moving the main limbs.
                int branchDepth = new System.Random(unchecked(recipe.seed ^ (i + 1) * 104729)).Next(minDepth, depth + 1);
                float heightFraction = tiered
                    ? (float)(i / whorl) / Mathf.Max(1, (count - 1) / whorl)
                    : (i + .5f) / Mathf.Max(1, count);
                float y = Mathf.Lerp(recipe.crownStart, profile.crownEnd > 0 ? profile.crownEnd : .9f, heightFraction);
                trunk.Sample(y, out var origin, out var trunkFrame);
                float azimuth = tiered ? (i % whorl) * (360f / whorl) + (i / whorl) * profile.whorlRotation : i * profile.branchRotation;
                azimuth += Sample(rng, -18, 18) * recipe.irregularity;
                var sibling = new System.Random(unchecked(recipe.seed ^ variant * 104729 ^ (i + 1) * 8191));
                float variation = variant == 0 ? 0 : recipe.variation;
                azimuth += Sample(sibling, -20, 20) * variation;
                float angle = Mathf.Clamp(settings.angle - Mathf.Clamp(settings.upperAngleBias, 0, 45) * heightFraction
                    + Sample(rng, -12, 12) * recipe.irregularity, 5, 115) * Mathf.Deg2Rad;
                var radial = Quaternion.Euler(0, azimuth, 0) * Vector3.forward;
                var direction = trunkFrame * (radial * Mathf.Sin(angle) + Vector3.up * Mathf.Cos(angle)).normalized;
                float envelope = Mathf.Lerp(1, profile.crownTipScale, heightFraction);
                float length = recipe.crownWidth * .5f * Mathf.Clamp(settings.spread, .1f, 2) * spreadScale
                    * envelope * recipe.CrownTaperScale(heightFraction);
                length *= 1 + Sample(rng, -.25f, .25f) * recipe.irregularity + Sample(sibling, -.6f, .6f) * variation;
                float parentRadius = recipe.trunkRadius * (1 - Mathf.Clamp01(recipe.taper) * y);
                float radius = Mathf.Min(parentRadius * Mathf.Clamp(settings.thickness, .1f, .9f), length * .16f);
                Grow(result, recipe, origin, direction, length, radius, 0, branchDepth, rng, profile,
                    parentRadius, trunkFrame * Vector3.up, -1, y);
            }
            return result;
        }

        static void Grow(List<Limb> result, TreeRecipe recipe, Vector3 start, Vector3 direction,
            float length, float radius, int level, int depth, System.Random rng, TreeFamilyProfile profile,
            float parentRadius, Vector3 parentDirection, int parentIndex, float attachmentFraction)
        {
            const int segments = Segments;
            var points = new Vector3[segments + 1];
            var side = Vector3.Cross(direction, Vector3.up).normalized;
            float bend = Mathf.Clamp01(recipe.Branches.bend);
            float sharpness = Mathf.Clamp01(recipe.Branches.sharpness);
            // Relative reach and socket thickness put most of the weight on long, substantial limbs.
            float reach = Mathf.Clamp01(length / Mathf.Max(recipe.crownWidth * .5f, .00001f));
            float girth = Mathf.Clamp01(radius / Mathf.Max(parentRadius * .9f, .00001f));
            float weightedDroop = Mathf.Clamp01(recipe.Branches.droop) * reach * girth;
            float sway = Sample(rng, -.32f, .32f);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float smoothLift = Mathf.Pow(t, profile.liftExponent);
                float elbow = Mathf.Max(0, (t - .5f) * 2);
                float lift = (profile.lift - weightedDroop)
                    * Mathf.Lerp(smoothLift, elbow, sharpness);
                float sideBend = Mathf.Lerp(Mathf.Sin(t * Mathf.PI), 1 - Mathf.Abs(t * 2 - 1), sharpness);
                points[i] = start + direction * (length * t) + bend * length *
                    (Vector3.up * lift + side * (sideBend * sway));
            }
            int index = result.Count;
            result.Add(new Limb { points = points, radius = radius, level = level, terminal = level == depth,
                parentRadius = parentRadius, parentDirection = parentDirection, parentIndex = parentIndex, attachment = attachmentFraction });
            if (level == depth) return;
            // Fill from tip toward the root without moving existing sockets as density increases.
            int childCount = Mathf.Clamp(recipe.Branches.subBranches, 1, 4);
            int childSeed = rng.Next();
            for (int child = 0; child < childCount; child++)
            {
                var childRandom = new System.Random(unchecked(childSeed ^ (child + 1) * 104729));
                int attachment = segments - child * 3;
                var tangent = (points[Mathf.Min(attachment + 1, segments)] - points[attachment - 1]).normalized;
                var forkFrame = Quaternion.FromToRotation(Vector3.up, tangent);
                float fork = profile.forkAngle + Sample(childRandom, -9, 9) * recipe.irregularity;
                float azimuth = child * 137.508f * Mathf.Deg2Rad;
                float angle = fork * Mathf.Deg2Rad;
                var heading = forkFrame * new Vector3(Mathf.Cos(azimuth) * Mathf.Sin(angle),
                    Mathf.Cos(angle), Mathf.Sin(azimuth) * Mathf.Sin(angle));
                float childLength = length * profile.childLengthScale;
                float localRadius = RadiusAt(radius, (float)attachment / segments, recipe.Branches.taper);
                if (level == 0)
                    localRadius = AttachmentRadiusAt(radius, parentRadius, (float)attachment / segments, recipe.Branches.attachmentThickness, recipe.Branches.taper);
                Grow(result, recipe, points[attachment], heading, childLength,
                    Mathf.Min(localRadius * .75f, childLength * .2f), level + 1, depth, childRandom, profile,
                    localRadius, tangent, index, (float)attachment / segments);
            }
        }

        static float Sample(System.Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    }
}
