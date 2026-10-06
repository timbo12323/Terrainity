using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeRootGrowth
    {
        internal static List<TreeBranchGrowth.Limb> Generate(TreeRecipe recipe, TreeTrunkGrowth trunk, float spread, int sibling, int parentOffset = 0)
        {
            var roots = new List<TreeBranchGrowth.Limb>();
            if (!recipe.rootsEnabled) return roots;
            int count = Mathf.Clamp(recipe.rootCount, 1, 12);
            float height = Mathf.Max(.1f, trunk.points[TreeTrunkGrowth.Segments].y);
            float attachment = Mathf.Clamp(recipe.rootAttachmentHeight / height, 0, .25f);
            trunk.Sample(attachment, out var center, out var frame);
            trunk.Sample(0, out var foot, out var baseFrame);
            Vector3 downward = -(frame * Vector3.up);
            float parentRadius = recipe.trunkRadius * (1 - Mathf.Clamp01(recipe.taper) * attachment);
            float radius = parentRadius * Mathf.Clamp(recipe.rootThickness, .1f, .9f);
            var rng = new System.Random(unchecked(recipe.seed ^ 15485863 ^ sibling * 7919));
            float phase = (float)rng.NextDouble() * Mathf.PI * 2;
            for (int i = 0; i < count; i++)
            {
                float angle = phase + i * Mathf.PI * 2 / count + ((float)rng.NextDouble() - .5f) * .3f;
                Vector3 radial = frame * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 outward = Vector3.ProjectOnPlane(radial, Vector3.up).normalized;
                if (outward.sqrMagnitude < .01f) outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 side = Vector3.Cross(Vector3.up, outward);
                float length = Mathf.Clamp(recipe.rootSpread, .2f, 5) * spread * Mathf.Lerp(.8f, 1.2f, (float)rng.NextDouble());
                float depth = Mathf.Clamp(recipe.rootDepth, 0, 2) * Mathf.Lerp(.8f, 1.2f, (float)rng.NextDouble());
                // Start inside the trunk wall and continue its downward tangent before fanning outward.
                Vector3 p0 = center + radial * Mathf.Max(0, parentRadius - radius * 1.15f);
                Vector3 end = foot + outward * length + Vector3.ProjectOnPlane(-(baseFrame * Vector3.up), Vector3.up) * length * .3f;
                end.y = foot.y - depth;
                float turn = Mathf.Clamp01(-Vector3.Dot(downward, (end - p0).normalized));
                float handle = Mathf.Min(Mathf.Max(parentRadius * 1.5f, Vector3.Distance(center, foot) * .8f), Vector3.Distance(p0, end) * .45f);
                Vector3 departure = Vector3.Slerp(downward, (end - p0).normalized, turn * .7f).normalized;
                Vector3 p1 = p0 + departure * handle * Mathf.Lerp(1, .45f, turn) + outward * length * .1f;
                Vector3 p2 = Vector3.Lerp(p1, end, .6f) + side * length * ((float)rng.NextDouble() - .5f) * .3f;
                p2.y = Mathf.Lerp(p1.y, end.y, .5f);
                var points = new Vector3[TreeBranchGrowth.Segments + 1];
                for (int j = 0; j < points.Length; j++)
                {
                    float t = j / (float)(points.Length - 1), u = 1 - t;
                    points[j] = u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * end;
                }
                var bendPeaks = Bend(recipe, points, unchecked(recipe.seed ^ sibling * 7919 ^ i * 104729));
                roots.Add(new TreeBranchGrowth.Limb {
                    points = points, radius = radius, parentRadius = parentRadius,
                    rootBendPeaks = bendPeaks,
                    parentDirection = downward, parentIndex = -1, attachment = attachment,
                    level = 1, terminal = false, isRoot = true
                });
            }
            var forkRandom = new System.Random(unchecked(recipe.seed ^ 32452843 ^ sibling * 7919));
            int maxDepth = Mathf.Clamp(recipe.rootMaxForkDepth, 0, 3);
            int minDepth = Mathf.Clamp(recipe.rootMinForkDepth, 0, maxDepth);
            var depths = new int[count];
            for (int i = 0; i < count; i++)
                depths[i] = new System.Random(unchecked(recipe.seed ^ (i + 1) * 49999)).Next(minDepth, maxDepth + 1);
            int firstParent = forkRandom.Next(count);
            for (int i = 0; i < Mathf.Clamp(recipe.rootForks, 0, 12); i++)
            {
                int parentIndex = (firstParent + i) % count;
                var parent = roots[parentIndex];
                float t = Mathf.Lerp(.38f, .72f, (float)forkRandom.NextDouble());
                float sample = t * TreeBranchGrowth.Segments;
                int segment = Mathf.Min((int)sample, TreeBranchGrowth.Segments - 1);
                Vector3 start = Vector3.Lerp(parent.points[segment], parent.points[segment + 1], sample - segment);
                Vector3 tangent = (parent.points[segment + 1] - parent.points[segment]).normalized;
                float sign = ((i / count + parentIndex) % 2 == 0) ? 1 : -1;
                Vector3 direction = Quaternion.AngleAxis(sign * Mathf.Lerp(30, 60, (float)forkRandom.NextDouble()), Vector3.up) * tangent;
                float length = Vector3.Distance(start, parent.points[TreeBranchGrowth.Segments]) * Mathf.Lerp(.65f, .95f, (float)forkRandom.NextDouble());
                Vector3 end = start + direction * length;
                end.y = Mathf.Min(start.y, parent.points[TreeBranchGrowth.Segments].y);
                Vector3 control1 = start + tangent * length * .3f;
                Vector3 control2 = Vector3.Lerp(start, end, .7f);
                var points = new Vector3[TreeBranchGrowth.Segments + 1];
                for (int j = 0; j < points.Length; j++)
                {
                    float s = j / (float)(points.Length - 1), u = 1 - s;
                    points[j] = u * u * u * start + 3 * u * u * s * control1 + 3 * u * s * s * control2 + s * s * s * end;
                }
                float socketRadius = TreeBranchGrowth.RadiusAt(parent.radius, t, recipe.rootTaper);
                // Preserve random draws even when this root has no forks, keeping later sockets stable.
                if (depths[parentIndex] == 0) continue;
                var bendPeaks = Bend(recipe, points, unchecked(recipe.seed ^ sibling * 7919 ^ (i + count) * 104729));
                roots.Add(new TreeBranchGrowth.Limb {
                    points = points, radius = socketRadius * .6f, parentRadius = socketRadius,
                    rootBendPeaks = bendPeaks,
                    parentDirection = tangent, parentIndex = parentOffset + parentIndex, attachment = t,
                    level = 2, terminal = false, isRoot = true
                });
            }
            int firstGenerationEnd = roots.Count;
            for (int i = count; i < firstGenerationEnd; i++)
                GrowForks(recipe, roots, i, parentOffset, depths[roots[i].parentIndex - parentOffset] - 1,
                    new System.Random(unchecked(recipe.seed ^ sibling * 7919 ^ i * 67867967)));
            return roots;
        }

        static Vector2 Bend(TreeRecipe recipe, Vector3[] points, int seed)
        {
            float amount = Mathf.Clamp01(recipe.rootBend);
            if (amount == 0) return Vector2.zero;
            Vector3 side = Vector3.Cross(Vector3.up, points[points.Length - 1] - points[0]).normalized;
            if (side.sqrMagnitude < .000001f) side = Vector3.right;
            // Mix related root seeds before sampling so neighboring roots do not follow a pattern.
            uint hash = unchecked((uint)seed);
            unchecked
            {
                hash ^= hash >> 16; hash *= 0x7feb352du;
                hash ^= hash >> 15; hash *= 0x846ca68bu;
                hash ^= hash >> 16;
            }
            var random = new System.Random((int)(hash & int.MaxValue));
            float Next(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
            float sign = random.Next(2) == 0 ? -1 : 1;
            float amplitude = Vector3.Distance(points[0], points[points.Length - 1]) * amount * .3f;
            float strength = Next(.4f, 1) * sign;
            float peak = Next(.3f, .78f);
            float secondaryStrength = Next(-.45f, .45f);
            float secondaryPeak = Next(.25f, .85f);
            float sharpness = Mathf.Clamp01(recipe.rootSharpness);
            for (int i = 1; i < points.Length - 1; i++)
            {
                float t = i / (float)(points.Length - 1);
                float socketFade = Mathf.SmoothStep(0, 1, t / .25f);
                float offset = strength * BendEnvelope(t, peak, sharpness)
                    + secondaryStrength * BendEnvelope(t, secondaryPeak, sharpness);
                // Horizontal variation preserves burial depth; endpoints and the socket fade stay fixed.
                points[i] += side * (amplitude * offset * socketFade);
            }
            return new Vector2(peak, secondaryPeak);
        }

        static float BendEnvelope(float t, float peak, float sharpness)
        {
            float along = t <= peak ? t / peak : (1 - t) / (1 - peak);
            float smooth = Mathf.Pow(Mathf.Sin(along * Mathf.PI * .5f), 2);
            return Mathf.Lerp(smooth, along, sharpness);
        }

        static void GrowForks(TreeRecipe recipe, List<TreeBranchGrowth.Limb> roots, int parentIndex, int parentOffset, int remaining, System.Random rng)
        {
            if (remaining <= 0) return;
            var parent = roots[parentIndex];
            for (int child = 0; child < 2; child++)
            {
                float t = (child == 0 ? .65f : .4f) + (float)rng.NextDouble() * .08f;
                float sample = t * TreeBranchGrowth.Segments;
                int segment = Mathf.Min((int)sample, TreeBranchGrowth.Segments - 1);
                Vector3 start = Vector3.Lerp(parent.points[segment], parent.points[segment + 1], sample - segment);
                Vector3 tangent = (parent.points[segment + 1] - parent.points[segment]).normalized;
                float angle = Mathf.Lerp(30, 60, (float)rng.NextDouble()) * (child == 0 ? 1 : -1);
                float length = Vector3.Distance(start, parent.points[TreeBranchGrowth.Segments]) * .85f;
                Vector3 end = start + Quaternion.AngleAxis(angle, Vector3.up) * tangent * length;
                end.y = Mathf.Min(start.y, parent.points[TreeBranchGrowth.Segments].y);
                var points = new Vector3[TreeBranchGrowth.Segments + 1];
                for (int j = 0; j < points.Length; j++)
                {
                    float s = j / (float)(points.Length - 1), u = 1 - s;
                    points[j] = u * u * u * start + 3 * u * u * s * (start + tangent * length * .3f)
                        + 3 * u * s * s * Vector3.Lerp(start, end, .7f) + s * s * s * end;
                }
                var bendPeaks = Bend(recipe, points, rng.Next());
                float radius = TreeBranchGrowth.RadiusAt(parent.radius, t, recipe.rootTaper);
                int index = roots.Count;
                roots.Add(new TreeBranchGrowth.Limb {
                    points = points, radius = radius * .6f, parentRadius = radius, parentDirection = tangent,
                    rootBendPeaks = bendPeaks,
                    parentIndex = parentIndex + parentOffset, attachment = t, level = parent.level + 1,
                    terminal = false, isRoot = true
                });
                GrowForks(recipe, roots, index, parentOffset, remaining - 1, new System.Random(rng.Next()));
            }
        }
    }
}
