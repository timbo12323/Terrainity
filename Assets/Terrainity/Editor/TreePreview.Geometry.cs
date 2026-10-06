using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    // Tube tessellation, protected sockets, caps and curvature-safe radii.
    internal sealed partial class TreePreview
    {
        void CurvedTrunk(TreeRecipe recipe, TreeTrunkGrowth trunk, List<TreeBranchGrowth.Limb> limbs)
        {
            int sides = Mathf.Clamp(recipe.trunkSides, 3, 12);
            int segments = Mathf.Clamp(recipe.trunkSegments, 2, 48);
            TreeBarkDetail.Resolution(recipe, recipe.trunkRadius, true, ref sides, ref segments);
            var samples = ChildRingSamples(segments, -1);
            if (Mathf.Abs(recipe.trunkBend) > 0 && recipe.trunkSharpness > 0)
                for (int i = 0; i < Mathf.Clamp(recipe.trunkBends, 1, 9); i++)
                    AddSample(samples, (i + .5f) / Mathf.Clamp(recipe.trunkBends, 1, 9));
            samples.Sort();
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            int stride = sides + 1;
            for (int ring = 0; ring < samples.Count; ring++)
            {
                float t = samples[ring];
                trunk.Sample(t, out var position, out var frame);
                float radius = recipe.trunkRadius * (1 - Mathf.Clamp01(recipe.taper) * t);
                float distance = ArcLength(trunk.points, t);
                for (int side = 0; side <= sides; side++)
                {
                    float angle = (side % sides) * Mathf.PI * 2 / sides;
                    var radial = frame * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    float relief = TreeBarkDetail.Relief(recipe, angle, distance, radius, 1);
                    vertices.Add(position + radial * (radius + relief));
                    normals.Add(radial);
                    uv.Add(new Vector2((float)side / sides, distance));
                }
            }
            if (TreeBarkDetail.Enabled(recipe)) TreeBarkDetail.SurfaceNormals(vertices, normals, sides, samples.Count, false);
            int ringCount = SimplifyWood(recipe, vertices, normals, uv, sides, samples, limbs, -1);
            for (int ring = 0; ring < ringCount - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * stride + side, b = a + 1;
                    TerrainityMeshUtility.AddTriangle(triangles, a, a + stride, b);
                    TerrainityMeshUtility.AddTriangle(triangles, b, a + stride, b + stride);
                }
            AddCaps(vertices, normals, uv, triangles, sides, ringCount, trunk.points[0], trunk.points[TreeTrunkGrowth.Segments]);
            AddMesh(vertices, triangles, 0, normals, uv);
        }

        int SimplifyWood(TreeRecipe recipe, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            int sides, List<float> samples, List<TreeBranchGrowth.Limb> limbs, int parent)
        {
            if (!recipe.simplifyWood) return samples.Count;
            var protectedSamples = new List<float>();
            foreach (var limb in ChildrenOf(parent))
                if (!limb.pruned && limb.parentIndex == parent && (!recipe.lodAggressiveWood || !limb.terminal))
                    protectedSamples.Add(limb.attachment);
            if (parent == -1 && Mathf.Abs(recipe.trunkBend) > 0 && recipe.trunkSharpness > 0)
                for (int i = 0; i < Mathf.Clamp(recipe.trunkBends, 1, 9); i++)
                    protectedSamples.Add((i + .5f) / Mathf.Clamp(recipe.trunkBends, 1, 9));
            if (parent >= 0)
            {
                if (limbs[parent].isRoot && recipe.rootBend > 0 && recipe.rootSharpness > 0)
                    AddRootBendSamples(protectedSamples, limbs[parent].rootBendPeaks);
                if (!recipe.lodAggressiveWood) protectedSamples.Add(.5f);
                if (!recipe.lodAggressiveWood && limbs[parent].level == 0 && !Mathf.Approximately(recipe.Branches.attachmentThickness, 1))
                    protectedSamples.AddRange(new[] { .1f, .2f, .35f });
            }
            int result = TreeMeshSimplifier.Simplify(vertices, normals, uv, sides, samples, protectedSamples,
                recipe.lodAggressiveWood ? recipe.simplificationTolerance : Mathf.Clamp(recipe.simplificationTolerance, .0001f, .02f),
                recipe.lodAggressiveWood ? .12f : .015f);
            RemovedWoodTriangles += (samples.Count - result) * sides * 2;
            RemovedWoodVertices += (samples.Count - result) * (sides + 1);
            return result;
        }

        internal static void AddSample(List<float> samples, float t)
        {
            if (!samples.Exists(x => Mathf.Abs(x - t) < .00001f)) samples.Add(t);
        }

        internal static List<float> RingSamples(int segments, IEnumerable<TreeBranchGrowth.Limb> limbs, int parent)
        {
            var samples = new List<float>(segments + 1);
            for (int i = 0; i <= segments; i++) samples.Add((float)i / segments);
            // Keep socket centers exact even when the rest of the curve is simplified.
            foreach (var limb in limbs) if (!limb.pruned && limb.parentIndex == parent) AddSample(samples, limb.attachment);
            samples.Sort();
            return samples;
        }

        IEnumerable<TreeBranchGrowth.Limb> ChildrenOf(int parent)
            => childLimbs.TryGetValue(parent, out var children) ? children : Array.Empty<TreeBranchGrowth.Limb>();

        List<float> ChildRingSamples(int segments, int parent)
            // Index sockets once instead of scanning every limb for every generated tube.
            => RingSamples(segments, ChildrenOf(parent), parent);

        static Vector3 SampleLimb(Vector3[] points, float t)
        {
            float index = Mathf.Clamp01(t) * (points.Length - 1);
            int lo = Mathf.FloorToInt(index);
            return Vector3.Lerp(points[lo], points[Mathf.Min(lo + 1, points.Length - 1)], index - lo);
        }

        void CurvedLimb(TreeRecipe recipe, List<TreeBranchGrowth.Limb> limbs, int limbIndex)
        {
            var limb = limbs[limbIndex];
            int sides = Mathf.Clamp(recipe.branchSides, 3, 12);
            int segments = Mathf.Clamp(recipe.branchSegments, 2, 48);
            TreeBarkDetail.Resolution(recipe, limb.radius, false, ref sides, ref segments);
            var samples = ChildRingSamples(segments, limbIndex);
            if (limb.level == 0 && !Mathf.Approximately(recipe.Branches.attachmentThickness, 1))
                foreach (float t in new[] { .1f, .2f, .35f }) AddSample(samples, t);
            if (limb.isRoot)
            {
                if (recipe.rootBend > 0 && recipe.rootSharpness > 0) AddRootBendSamples(samples, limb.rootBendPeaks);
            }
            else if (recipe.Branches.bend > 0 && recipe.Branches.sharpness > 0) AddSample(samples, .5f);
            samples.Sort();
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            int stride = sides + 1;
            var points = samples.ConvertAll(t => SampleLimb(limb.points, t)).ToArray();
            var rootRadii = RootRadiusProfile(limb.points, limb.radius,
                limb.isRoot ? recipe.rootTaper : recipe.Branches.taper,
                limb.level == 0 ? limb.parentRadius : 0, recipe.Branches.attachmentThickness);
            float travelled = 0;
            var parentAxis = limb.parentDirection.normalized;
            Quaternion rootFrame = Quaternion.identity;
            Vector3 previousRootAxis = Vector3.up;
            for (int ring = 0; ring < points.Length; ring++)
            {
                if (ring > 0) travelled += Vector3.Distance(points[ring - 1], points[ring]);
                float collar = Mathf.SmoothStep(0, 1, travelled / Mathf.Max(limb.parentRadius * 2.5f, .00001f));
                // Geometry follows the limb; only shading blends toward the parent.
                // Rotating socket rings toward the trunk can fold a downward limb inside out.
                float rootSample = samples[ring] * (limb.points.Length - 1);
                int rootLo = Mathf.Min((int)rootSample, limb.points.Length - 2);
                var axis = Vector3.Slerp(RootAxis(limb.points, rootLo), RootAxis(limb.points, rootLo + 1), rootSample - rootLo).normalized;
                rootFrame = Quaternion.FromToRotation(previousRootAxis, axis) * rootFrame;
                previousRootAxis = axis;
                var rotation = rootFrame;
                float radius = Mathf.Lerp(rootRadii[rootLo], rootRadii[rootLo + 1], rootSample - rootLo);
                float distance = ArcLength(limb.points, samples[ring]);
                for (int side = 0; side <= sides; side++)
                {
                    float angle = (side % sides) * Mathf.PI * 2 / sides;
                    var radial = rotation * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    float relief = TreeBarkDetail.Relief(recipe, angle, distance, radius, collar);
                    var position = points[ring] + radial * (radius + relief);
                    vertices.Add(position);
                    uv.Add(new Vector2((float)side / sides, distance));
                    var parentRadial = Vector3.ProjectOnPlane(position - points[0], parentAxis).normalized;
                    if (parentRadial.sqrMagnitude < .001f) parentRadial = radial;
                    normals.Add(Vector3.Slerp(parentRadial, radial, collar).normalized);
                }
            }
            if (TreeBarkDetail.Enabled(recipe)) TreeBarkDetail.SurfaceNormals(vertices, normals, sides, points.Length, true);
            int ringCount = SimplifyWood(recipe, vertices, normals, uv, sides, samples, limbs, limbIndex);
            for (int ring = 0; ring < ringCount - 1; ring++)
                for (int side = 0; side < sides; side++)
                {
                    int a = ring * stride + side, b = a + 1;
                    TerrainityMeshUtility.AddTriangle(triangles, a, a + stride, b);
                    TerrainityMeshUtility.AddTriangle(triangles, b, a + stride, b + stride);
                }
            AddCaps(vertices, normals, uv, triangles, sides, ringCount, points[0], points[points.Length - 1]);
            var limbMesh = AddMesh(vertices, triangles, 0, normals, uv);
            if (!limb.isRoot) sourceBranchLimbs[limbMesh] = limbIndex;
        }

        static void AddRootBendSamples(List<float> samples, Vector2 peaks)
        {
            // A peak lies between skeleton points; retain both enclosing rings at every LOD.
            void AddPeak(float peak)
            {
                if (peak <= 0 || peak >= 1) return;
                float ring = peak * TreeBranchGrowth.Segments;
                AddSample(samples, Mathf.Floor(ring) / TreeBranchGrowth.Segments);
                AddSample(samples, Mathf.Ceil(ring) / TreeBranchGrowth.Segments);
            }
            AddPeak(peaks.x); AddPeak(peaks.y);
        }

        internal static Vector3 RootAxis(Vector3[] points, int ring)
        {
            Vector3 incoming = ring > 0 ? (points[ring] - points[ring - 1]).normalized : Vector3.zero;
            Vector3 outgoing = ring + 1 < points.Length ? (points[ring + 1] - points[ring]).normalized : Vector3.zero;
            Vector3 bisector = incoming + outgoing;
            return bisector.sqrMagnitude > .000001f ? bisector.normalized : (outgoing.sqrMagnitude > 0 ? outgoing : incoming);
        }

        internal static float[] RootRadiusProfile(Vector3[] points, float baseRadius, float taper, float parentRadius = 0, float attachmentThickness = 1)
        {
            var radii = new float[points.Length];
            var requested = new float[points.Length];
            var distances = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);
                float t = i / (float)(points.Length - 1);
                requested[i] = parentRadius > 0
                    ? TreeBranchGrowth.AttachmentRadiusAt(baseRadius, parentRadius, t, attachmentThickness, taper)
                    : TreeBranchGrowth.RadiusAt(baseRadius, t, taper);
                radii[i] = Mathf.Min(requested[i], RootSafeRadius(points, i));
            }
            // Spread curvature corrections over physical distance, using the stable skeleton
            // rather than mesh rings: extra subdivisions must not create a tighter pinch.
            var smooth = (float[])radii.Clone();
            float width = Mathf.Max(.001f, baseRadius);
            for (int i = 0; i < points.Length; i++)
                for (int j = 0; j < points.Length; j++)
                {
                    if (radii[j] >= requested[j]) continue;
                    float distance = Mathf.Abs(distances[i] - distances[j]);
                    float transition = .6f * (Mathf.Sqrt(distance * distance + width * width) - width);
                    smooth[i] = Mathf.Min(smooth[i], radii[j] + transition);
                }
            return smooth;
        }

        internal static float RootSafeRadius(Vector3[] points, int ring)
        {
            float limit = float.PositiveInfinity;
            Vector3 axis = RootAxis(points, ring);
            for (int neighbor = Mathf.Max(0, ring - 1); neighbor <= Mathf.Min(points.Length - 1, ring + 1); neighbor++)
            {
                if (neighbor == ring) continue;
                Vector3 edge = points[neighbor] - points[ring];
                float sine = Vector3.Cross(axis, edge.normalized).magnitude;
                if (sine > .00001f) limit = Mathf.Min(limit, edge.magnitude * .3f / sine);
            }
            return limit;
        }

        // Measure the original skeleton so changing mesh detail does not slide the texture.
        internal static float ArcLength(Vector3[] points, float t)
        {
            float index = Mathf.Clamp01(t) * (points.Length - 1);
            int last = Mathf.FloorToInt(index);
            float length = 0;
            for (int i = 1; i <= last; i++) length += Vector3.Distance(points[i - 1], points[i]);
            if (last < points.Length - 1) length += Vector3.Distance(points[last], points[last + 1]) * (index - last);
            return length;
        }

        static void AddCaps(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            List<int> triangles, int sides, int rings, Vector3 bottom, Vector3 top)
        {
            // Separate cap vertices keep their planar UVs and normals off the cylinder seam.
            for (int cap = 0; cap < 2; cap++)
            {
                int source = cap == 0 ? 0 : (rings - 1) * (sides + 1);
                // Normalize edges first: tiny, densely sampled twig caps otherwise
                // produce a cross product below Unity's normalization threshold.
                var normal = Vector3.Cross((vertices[source + 1] - vertices[source]).normalized,
                    (vertices[source + 2] - vertices[source]).normalized).normalized;
                if (cap == 1) normal = -normal;
                int center = vertices.Count;
                vertices.Add(cap == 0 ? bottom : top); normals.Add(normal); uv.Add(new Vector2(.5f, .5f));
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2 / sides;
                    vertices.Add(vertices[source + side]); normals.Add(normal);
                    uv.Add(new Vector2(.5f + .5f * Mathf.Cos(angle), .5f + .5f * Mathf.Sin(angle)));
                }
                for (int side = 0; side < sides; side++)
                {
                    int a = center + 1 + side, b = center + 1 + (side + 1) % sides;
                    TerrainityMeshUtility.AddTriangle(triangles, center, cap == 0 ? a : b, cap == 0 ? b : a);
                }
            }
        }

    }
}
