using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeBarkDetail
    {
        internal static bool Enabled(TreeRecipe recipe) => recipe.barkSurfaceDetail > .0001f;

        internal static void Resolution(TreeRecipe recipe, float radius, bool trunk, ref int sides, ref int segments)
        {
            if (!Enabled(recipe)) return;
            float weight = Mathf.Lerp(.15f, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.015f, .12f, radius)));
            int quality = Mathf.Clamp(recipe.barkDetailResolution, 1, 4);
            float lod = Mathf.Clamp(recipe.barkDetailLodScale, .1f, 1);
            sides += Mathf.RoundToInt((trunk ? 12 : 8) * quality * lod * weight);
            segments += Mathf.RoundToInt((trunk ? 24 : 8) * quality * lod * weight);
        }

        internal static float Relief(TreeRecipe recipe, float angle, float distance, float radius, float socketFade)
        {
            if (!Enabled(recipe)) return 0;
            float scale = Mathf.Clamp(recipe.barkDetailScale, .25f, 4);
            int grooves = Mathf.Clamp(Mathf.RoundToInt(8 / scale), 3, 24);
            float seed = (recipe.seed % 8191) * .017f;
            float along = distance / scale;
            float warp = .65f * Mathf.Sin(along * 1.7f + seed) + .25f * Mathf.Sin(along * 4.3f + angle * 2);
            float ridge = Mathf.Sin(angle * grooves + warp + seed);
            float grain = Mathf.Sin(angle * grooves * 2 + along * 2.1f + seed) * .2f;
            float knots = Mathf.Sin(angle * 3 + along * 3.5f + seed) * Mathf.Sin(along * 1.3f) * .18f;
            float twigFade = Mathf.Lerp(.3f, 1, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.008f, .06f, radius)));
            return radius * .16f * Mathf.Clamp01(recipe.barkSurfaceDetail) * twigFade * socketFade * (ridge + grain + knots);
        }

        internal static void SurfaceNormals(List<Vector3> vertices, List<Vector3> normals, int sides, int rings, bool limb)
        {
            int stride = sides + 1;
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side <= sides; side++)
                {
                    int s = side % sides, index = ring * stride + side;
                    var around = vertices[ring * stride + (s + 1) % sides] - vertices[ring * stride + (s + sides - 1) % sides];
                    var along = vertices[Mathf.Min(ring + 1, rings - 1) * stride + s] - vertices[Mathf.Max(0, ring - 1) * stride + s];
                    var normal = Vector3.Cross(along, around).normalized;
                    if (normal.sqrMagnitude < .5f) continue;
                    if (Vector3.Dot(normal, normals[index]) < 0) normal = -normal;
                    // Preserve the existing buried socket's blended normal.
                    if (limb && ring == 0) continue;
                    normals[index] = normal;
                }
        }
    }
}
