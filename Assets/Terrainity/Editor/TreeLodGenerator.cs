using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    [Serializable]
    internal sealed class TreeLodSettings
    {
        public bool enabled = true;
        public bool crossFade = true;
        public float lod1Start = .35f;
        public float lod2Start = .12f;
        public float cull = .01f;
        public float foliageRetention = .5f;
        public float foliageCompensation = .15f;
        internal void Validate()
        {
            foreach (float v in new[] { lod1Start, lod2Start, cull, foliageRetention, foliageCompensation })
                if (!float.IsFinite(v)) throw new ArgumentException("LOD settings must be finite.");
            if (!(lod1Start > lod2Start && lod2Start > cull && cull > 0 && lod1Start < 1)
                || foliageRetention < .35f || foliageRetention > 1 || foliageCompensation < 0 || foliageCompensation > .25f)
                throw new ArgumentException("LOD thresholds must descend: LOD1 > LOD2 > cull. Foliage retention must be 35–100%, compensation 0–25%.");
        }
    }

    internal static class TreeLodGenerator
    {
        // Rank terminal child branches within height bands. The lowest-ranked branches
        // collapse at LOD1 and disappear at LOD2; main limbs and roots always survive.
        internal static float[] BranchScaleFactors(List<TreeBranchGrowth.Limb> limbs, TreeRecipe recipe, int level)
        {
            var scales = new float[limbs.Count];
            for (int i = 0; i < scales.Length; i++) scales[i] = 1;
            if (level <= 0) return scales;
            var eligible = new List<int>();
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int index = 0; index < limbs.Count; index++)
            {
                var limb = limbs[index];
                if (limb.terminal && limb.parentIndex >= 0 && !limb.isRoot)
                {
                    float y = limb.points[limb.points.Length - 1].y;
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    eligible.Add(index);
                }
            }
            if (eligible.Count < 3) return scales;
            const int bands = 8;
            var radii = new float[bands];
            var lengths = new float[bands];
            int Band(int index) => Mathf.Clamp(Mathf.FloorToInt((limbs[index].points[TreeBranchGrowth.Segments].y - minY)
                / Mathf.Max(maxY - minY, .0001f) * bands), 0, bands - 1);
            float Radius(int index)
            {
                var tip = limbs[index].points[TreeBranchGrowth.Segments];
                return new Vector2(tip.x - recipe.lean * tip.y * .18f, tip.z).magnitude;
            }
            float Length(int index)
            {
                var points = limbs[index].points;
                return Vector3.Distance(points[0], points[points.Length - 1]);
            }
            foreach (int index in eligible)
            {
                int band = Band(index);
                radii[band] = Mathf.Max(radii[band], Radius(index));
                lengths[band] = Mathf.Max(lengths[band], Length(index));
            }
            var ranking = new List<(int index, float score)>();
            foreach (int index in eligible)
            {
                int band = Band(index);
                float outerRadius = 0, longest = 0;
                for (int neighbor = Mathf.Max(0, band - 1); neighbor <= Mathf.Min(bands - 1, band + 1); neighbor++)
                { outerRadius = Mathf.Max(outerRadius, radii[neighbor]); longest = Mathf.Max(longest, lengths[neighbor]); }
                uint hash = unchecked((uint)(recipe.seed * 73856093 ^ (index + 1) * 19349663));
                hash ^= hash >> 16; hash *= 2246822519u; hash ^= hash >> 13;
                float jitter = (hash & 0xffff) / 65535f;
                ranking.Add((index, .72f * Radius(index) / Mathf.Max(.001f, outerRadius)
                    + .23f * Length(index) / Mathf.Max(.001f, longest) + .05f * jitter));
            }
            ranking.Sort((a, b) => { int order = a.score.CompareTo(b.score); return order != 0 ? order : a.index.CompareTo(b.index); });
            int culled = Mathf.FloorToInt(ranking.Count * (level == 1 ? .10f : .30f));
            int affected = Mathf.FloorToInt(ranking.Count * (level == 1 ? .35f : .50f));
            for (int rank = 0; rank < affected; rank++)
                scales[ranking[rank].index] = rank < culled ? 0 : level == 1 ? .55f : .40f;
            return scales;
        }

        internal static Mesh Build(TreeRecipe original, int sibling, int level, TreeLodSettings settings)
        {
            // The caller already owns loaded textures; avoid JSON and asset lookups for each LOD.
            var recipe = original.Copy();
            float resolution = level == 1 ? .7f : .4f;
            recipe.trunkSides = Mathf.Max(3, Mathf.FloorToInt(original.trunkSides * resolution));
            recipe.branchSides = Mathf.Max(3, Mathf.FloorToInt(original.branchSides * resolution));
            recipe.trunkSegments = Mathf.Max(2, Mathf.FloorToInt(original.trunkSegments * resolution));
            recipe.branchSegments = Mathf.Max(2, Mathf.FloorToInt(original.branchSegments * resolution));
            recipe.barkDetailResolution = Mathf.Max(1, original.barkDetailResolution / 2);
            recipe.barkDetailLodScale = resolution;
            recipe.barkSurfaceDetail = level == 1 ? original.barkSurfaceDetail * .65f : 0;
            if (level == 2)
            {
                recipe.simplifyWood = true;
                recipe.lodAggressiveWood = true;
                // Half a percent of height limits the outline change at LOD2 while
                // letting straight stretches collapse to far fewer tube rings.
                recipe.simplificationTolerance = Mathf.Max(.02f, original.height * .005f);
            }
            recipe.showFoliage = true;
            using (var builder = new TreePreview())
            {
                builder.Build(recipe, sibling, level);
                Mesh foliage = builder.Meshes.Count > 1 ? ReduceFoliage(builder.Meshes[1], original, level, settings) : null;
                try
                {
                    var parts = new List<CombineInstance> { new CombineInstance { mesh = builder.Meshes[0] } };
                    if (foliage != null) parts.Add(new CombineInstance { mesh = foliage });
                    var mesh = new Mesh { name = "LOD" + level, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(parts.ToArray(), false, false);
                    mesh.RecalculateBounds();
                    mesh.bounds = TreeExporter.WindBounds(mesh.bounds, recipe.height);
                    return mesh;
                }
                finally { if (foliage != null) UnityEngine.Object.DestroyImmediate(foliage); }
            }
        }

        internal static Mesh ReduceFoliage(Mesh source, TreeRecipe recipe, int level, TreeLodSettings settings)
        {
            var input = source.vertices; var inputNormals = source.normals;
            var inputUV = source.uv; var inputTint = source.uv2;
            var canopyCenter = source.bounds.center;
            float canopyRadius = Mathf.Max(source.bounds.extents.x, source.bounds.extents.z, .001f);
            var inputCanopy = new List<Vector3>(); source.GetUVs(2, inputCanopy);
            var inputWind = new List<Vector4>(); source.GetUVs(3, inputWind);
            var canopy = new List<Vector3>();
            var wind = new List<Vector4>();
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var tint = new List<Vector2>(); var indices = new List<int>();
            void Copy(int index, Vector3 position)
            { vertices.Add(position); normals.Add(inputNormals[index]); uv.Add(inputUV[index]); tint.Add(inputTint[index]);
                canopy.Add(inputCanopy.Count == input.Length ? inputCanopy[index] : inputNormals[index]);
                wind.Add(inputWind.Count == input.Length ? inputWind[index] : Vector4.zero); }
            if (recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond)
            {
                int rows = TreeBranchGrowth.Segments - 1, stride = rows * 3;
                int step = level == 1 ? 2 : 3;
                for (int start = 0; start < input.Length; start += stride)
                {
                    int previous = -1;
                    for (int row = 0; row < rows; row++)
                    {
                        if (row != 0 && row != rows - 1 && row != rows / 2 && row % step != 0) continue;
                        int n = vertices.Count;
                        int spineIndex = start + row * 3 + 1;
                        var spine = input[spineIndex];
                        float edge = Mathf.Clamp01(new Vector2(spine.x - canopyCenter.x, spine.z - canopyCenter.z).magnitude / canopyRadius);
                        float growth = 1 + settings.foliageCompensation * (level == 1 ? .5f : 1) * (1 + edge);
                        for (int col = 0; col < 3; col++)
                        {
                            int index = start + row * 3 + col;
                            Copy(index, spine + (input[index] - spine) * growth);
                        }
                        if (previous >= 0)
                        {
                            TerrainityMeshUtility.AddTriangle(indices, previous, previous + 1, n);
                            TerrainityMeshUtility.AddTriangle(indices, previous + 1, n + 1, n);
                            TerrainityMeshUtility.AddTriangle(indices, previous + 1, previous + 2, n + 1);
                            TerrainityMeshUtility.AddTriangle(indices, previous + 2, n + 2, n + 1);
                        }
                        previous = n;
                    }
                }
            }
            else
            {
                int cards = recipe.foliageAligned ? Mathf.Clamp(recipe.foliageAlignSides, 1, 3) : Mathf.Clamp(recipe.foliageCards, 2, 12);
                int subdivisions = Mathf.Clamp(recipe.foliageSubdivisions, 1, 12);
                int cardStride = subdivisions * 4;
                for (int cluster = 0; cluster < input.Length;)
                {
                    int group = inputWind.Count == input.Length
                        ? Mathf.Max(0, Mathf.RoundToInt(inputWind[cluster].w) - 1) / 12
                        : cluster / (cards * cardStride);
                    int groupEnd = cluster + cardStride;
                    while (groupEnd < input.Length && (inputWind.Count == input.Length
                        ? Mathf.Max(0, Mathf.RoundToInt(inputWind[groupEnd].w) - 1) / 12
                        : groupEnd / (cards * cardStride)) == group) groupEnd += cardStride;
                    int visibleCards = (groupEnd - cluster) / cardStride;
                    int farVisible = Mathf.Clamp(Mathf.CeilToInt(visibleCards * settings.foliageRetention),
                        Mathf.Min(visibleCards, 2), visibleCards);
                    int keepVisible = level == 1 ? Mathf.CeilToInt((visibleCards + farVisible) * .5f) : farVisible;
                    var ranking = new List<(int card, float score, float edge, float facing)>();
                    for (int card = 0; card < visibleCards; card++)
                    {
                        int start = cluster + card * cardStride;
                        var center = (input[start] + input[start + 1] + input[start + 2] + input[start + 3]) * .25f;
                        var radial = new Vector3(center.x - canopyCenter.x, 0, center.z - canopyCenter.z);
                        float edge = Mathf.Clamp01(radial.magnitude / canopyRadius);
                        float facing = radial.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(inputNormals[start], radial.normalized)) : .5f;
                        uint hash = unchecked((uint)(recipe.seed * 73856093 ^ cluster * 19349663 ^ (card + 1) * 83492791));
                        hash ^= hash >> 16; hash *= 2246822519u; hash ^= hash >> 13;
                        ranking.Add((card, .65f * edge + .30f * facing + .05f * ((hash & 0xffff) / 65535f), edge, facing));
                    }
                    ranking.Sort((a, b) => { int order = b.score.CompareTo(a.score); return order != 0 ? order : a.card.CompareTo(b.card); });
                    for (int rank = 0; rank < keepVisible; rank++)
                    {
                        var choice = ranking[rank];
                        int start = cluster + choice.card * cardStride;
                        // Cards that survive LOD1 but vanish at LOD2 contract onto their
                        // stem/card spine before their geometry is removed at the farther level.
                        // Compensate for lost card area, with a stronger bias at the canopy edge.
                        // The default .15 setting replaces roughly the area removed at LOD2.
                        float coverageScale = Mathf.Sqrt((float)visibleCards / keepVisible);
                        float growth = level == 1 && rank >= farVisible ? .55f
                            : 1 + (coverageScale - 1) * (settings.foliageCompensation / .15f)
                                * Mathf.Lerp(.7f, 1f, choice.edge);
                        growth = Mathf.Min(growth, 1.7f);
                        var pivot = recipe.foliageAligned ? new Vector2(.5f, 0)
                            : recipe.foliageStemAttached ? recipe.foliageStemPivot : new Vector2(.5f, .5f);
                        float row = Mathf.Clamp01(pivot.y) * subdivisions;
                        int rowIndex = Mathf.Min((int)row, subdivisions - 1);
                        int rowStart = start + rowIndex * 4;
                        var anchor = Vector3.Lerp(Vector3.Lerp(input[rowStart], input[rowStart + 1], pivot.x),
                            Vector3.Lerp(input[rowStart + 3], input[rowStart + 2], pivot.x), row - rowIndex);
                        for (int segment = 0; segment < subdivisions; segment++)
                        {
                            int n = vertices.Count;
                            for (int corner = 0; corner < 4; corner++) Copy(start + segment * 4 + corner, anchor + (input[start + segment * 4 + corner] - anchor) * growth);
                            TerrainityMeshUtility.AddTriangle(indices, n, n + 1, n + 2);
                            TerrainityMeshUtility.AddTriangle(indices, n, n + 2, n + 3);
                        }
                    }
                    cluster = groupEnd;
                }
            }
            var result = new Mesh { name = "LOD foliage", indexFormat = IndexFormat.UInt32 };
            result.SetVertices(vertices); result.SetNormals(normals); result.SetUVs(0, uv); result.SetUVs(1, tint);
            result.SetUVs(2, canopy);
            result.SetUVs(3, wind);
            result.SetTriangles(indices, 0); result.RecalculateBounds();
            return result;
        }
    }
}
