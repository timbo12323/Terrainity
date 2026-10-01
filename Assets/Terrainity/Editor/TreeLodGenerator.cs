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
        internal static Mesh Build(TreeRecipe original, int sibling, int level, Mesh fullFoliage, TreeLodSettings settings)
        {
            var recipe = TreeRecipeJson.From(original, sibling).Restore(out _);
            float resolution = level == 1 ? .7f : .4f;
            recipe.trunkSides = Mathf.Max(3, Mathf.FloorToInt(original.trunkSides * resolution));
            recipe.branchSides = Mathf.Max(3, Mathf.FloorToInt(original.branchSides * resolution));
            recipe.trunkSegments = Mathf.Max(2, Mathf.FloorToInt(original.trunkSegments * resolution));
            recipe.branchSegments = Mathf.Max(2, Mathf.FloorToInt(original.branchSegments * resolution));
            recipe.barkDetailResolution = Mathf.Max(1, original.barkDetailResolution / 2);
            recipe.barkDetailLodScale = resolution;
            recipe.barkSurfaceDetail = level == 1 ? original.barkSurfaceDetail * .65f : 0;
            recipe.showFoliage = true;
            using (var builder = new TreePreview())
            {
                builder.Build(recipe, sibling);
                Mesh foliage = fullFoliage == null ? null : ReduceFoliage(fullFoliage, original, level, settings);
                try
                {
                    var parts = new List<CombineInstance> { new CombineInstance { mesh = builder.Meshes[0] } };
                    if (foliage != null) parts.Add(new CombineInstance { mesh = foliage });
                    var mesh = new Mesh { name = "LOD" + level, indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(parts.ToArray(), false, false);
                    mesh.RecalculateBounds();
                    return mesh;
                }
                finally { if (foliage != null) UnityEngine.Object.DestroyImmediate(foliage); }
            }
        }

        internal static Mesh ReduceFoliage(Mesh source, TreeRecipe recipe, int level, TreeLodSettings settings)
        {
            var input = source.vertices; var inputNormals = source.normals;
            var inputUV = source.uv; var inputTint = source.uv2;
            var inputCanopy = new List<Vector3>(); source.GetUVs(2, inputCanopy);
            var canopy = new List<Vector3>();
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var tint = new List<Vector2>(); var indices = new List<int>();
            void Copy(int index, Vector3 position)
            { vertices.Add(position); normals.Add(inputNormals[index]); uv.Add(inputUV[index]); tint.Add(inputTint[index]); canopy.Add(inputCanopy.Count == input.Length ? inputCanopy[index] : inputNormals[index]); }
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
                        for (int col = 0; col < 3; col++) Copy(start + row * 3 + col, input[start + row * 3 + col]);
                        if (previous >= 0) indices.AddRange(new[] { previous, previous + 1, n, previous + 1, n + 1, n,
                            previous + 1, previous + 2, n + 1, previous + 2, n + 2, n + 1 });
                        previous = n;
                    }
                }
            }
            else
            {
                int cards = Mathf.Clamp(recipe.foliageCards, 2, 12);
                int farCount = Mathf.Clamp(Mathf.CeilToInt(cards * settings.foliageRetention), 2, cards);
                int keep = level == 1 ? Mathf.CeilToInt((cards + farCount) * .5f) : farCount;
                float growth = keep == cards ? 1 : 1 + settings.foliageCompensation * (level == 1 ? .5f : 1);
                // A deterministic nested ordering spreads surviving orientations through each cluster.
                var order = new List<int> { 0 };
                while (order.Count < cards)
                {
                    int best = -1; float score = -1;
                    for (int i = 0; i < cards; i++)
                    {
                        if (order.Contains(i)) continue;
                        float distance = float.MaxValue;
                        foreach (int chosen in order) distance = Mathf.Min(distance, Mathf.Min(Mathf.Abs(i - chosen), cards - Mathf.Abs(i - chosen)));
                        if (distance > score) { score = distance; best = i; }
                    }
                    order.Add(best);
                }
                int subdivisions = Mathf.Clamp(recipe.foliageSubdivisions, 1, 12);
                int cardStride = subdivisions * 4;
                for (int cluster = 0; cluster < input.Length; cluster += cards * cardStride)
                    for (int card = 0; card < cards; card++)
                    {
                        if (order.IndexOf(card) >= keep) continue;
                        int start = cluster + card * cardStride;
                        var pivot = recipe.foliageStemAttached ? recipe.foliageStemPivot : new Vector2(.5f, .5f);
                        float row = Mathf.Clamp01(pivot.y) * subdivisions;
                        int rowIndex = Mathf.Min((int)row, subdivisions - 1);
                        int rowStart = start + rowIndex * 4;
                        var anchor = Vector3.Lerp(Vector3.Lerp(input[rowStart], input[rowStart + 1], pivot.x),
                            Vector3.Lerp(input[rowStart + 3], input[rowStart + 2], pivot.x), row - rowIndex);
                        for (int segment = 0; segment < subdivisions; segment++)
                        {
                            int n = vertices.Count;
                            for (int corner = 0; corner < 4; corner++) Copy(start + segment * 4 + corner, anchor + (input[start + segment * 4 + corner] - anchor) * growth);
                            indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
                        }
                    }
            }
            var result = new Mesh { name = "LOD foliage", indexFormat = IndexFormat.UInt32 };
            result.SetVertices(vertices); result.SetNormals(normals); result.SetUVs(0, uv); result.SetUVs(1, tint);
            result.SetUVs(2, canopy);
            result.SetTriangles(indices, 0); result.RecalculateBounds();
            return result;
        }
    }
}
