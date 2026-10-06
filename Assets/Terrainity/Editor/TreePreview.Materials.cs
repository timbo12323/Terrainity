using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    // Material settings, gradients, occlusion and runtime wind vertex channels.
    internal sealed partial class TreePreview
    {
        void ApplyTints(TreeRecipe recipe)
        {
            // Store occluded fraction beside the gradient coordinate. Existing meshes
            // have zero here, so old exports remain unoccluded with the new shader.
            Bounds canopy = bounds;
            for (int m = 0; m < meshes.Count; m++)
                if (materialIndices[m] == 1) { canopy = meshes[m].bounds; break; }
            var extent = canopy.extents;
            extent = new Vector3(Mathf.Max(.1f, extent.x), Mathf.Max(.1f, extent.y), Mathf.Max(.1f, extent.z));
            for (int material = 0; material < 2; material++)
            {
                bool enabled = material == 0 ? recipe.barkGradientEnabled : recipe.foliageGradientEnabled;
                Gradient gradient = material == 0 ? recipe.barkGradient : recipe.foliageGradient;
                var tint = enabled ? Color.white : material == 0 ? recipe.bark : recipe.leaves;
                materials[material].color = tint;
                if (!materials[material].HasProperty("_TintRamp")) continue;
                if (tintRamps[material] == null)
                    tintRamps[material] = new Texture2D(256, 1, TextureFormat.RGBA32, false)
                    {
                        name = "Terrainity tint gradient", hideFlags = HideFlags.HideAndDontSave,
                        wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                    };
                var pixels = new Color[256];
                for (int x = 0; x < pixels.Length; x++)
                {
                    pixels[x] = enabled && gradient != null ? gradient.Evaluate(x / 255f) : Color.white;
                    pixels[x].a = 1; // Tint alpha must not punch holes in bark or change leaf cutouts.
                }
                tintRamps[material].SetPixels(pixels); tintRamps[material].Apply(false, false);
                tintRamps[material].filterMode = gradient != null && gradient.mode == GradientMode.Fixed ? FilterMode.Point : FilterMode.Bilinear;
                materials[material].SetTexture("_TintRamp", tintRamps[material]);
            }
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var mesh = meshes[meshIndex];
                var vertices = mesh.vertices; var uv = mesh.uv;
                bool foliage = materialIndices[meshIndex] == 1;
                var coordinates = new List<Vector2>(vertices.Length);
                var canopyNormals = foliage ? new List<Vector3>(vertices.Length) : null;
                var wind = new List<Vector4>(vertices.Length);
                var cardRandom = new System.Random(unchecked(recipe.seed ^ 73856093));
                float cardTint = 0;
                int tintGroupSize = recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond
                    ? (TreeBranchGrowth.Segments - 1) * 3 : 4 * Mathf.Clamp(recipe.foliageSubdivisions, 1, 12);
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (foliage && i % tintGroupSize == 0) cardTint = (float)cardRandom.NextDouble();
                    float heightFraction = Mathf.InverseLerp(bounds.min.y, bounds.max.y, vertices[i].y);
                    float t = heightFraction;
                    if (foliage && recipe.foliageTintMode == FoliageTintMode.PerCard) t = cardTint;
                    if (foliage && recipe.foliageTintMode == FoliageTintMode.StemToTip) t = uv[i].y;
                    var relative = vertices[i] - canopy.center;
                    if (foliage)
                    {
                        var canopyNormal = new Vector3(relative.x / (extent.x * extent.x), relative.y / (extent.y * extent.y), relative.z / (extent.z * extent.z));
                        canopyNormals.Add(canopyNormal.sqrMagnitude > .000001f ? canopyNormal.normalized : Vector3.up);
                    }
                    float radius = new Vector3(relative.x / extent.x, relative.y / extent.y, relative.z / extent.z).magnitude;
                    float interior = Mathf.Clamp01(1 - radius * .8f);
                    float occluded = interior * interior * .85f;
                    if (!foliage)
                        occluded = Mathf.Max(occluded, .65f * (1 - Mathf.SmoothStep(0, 1,
                            Mathf.Clamp01((vertices[i].y - bounds.min.y) / Mathf.Max(.1f, recipe.trunkRadius * 5)))));
                    coordinates.Add(new Vector2(t, occluded));
                    float heightWeight = Mathf.Pow(heightFraction, 2);
                    wind.Add(new Vector4(foliage ? .12f * heightWeight + .45f : .12f * heightWeight,
                        foliage ? 1 : 0, 0, 0));
                }
                var meshTriangles = mesh.triangles;
                for (int rangeIndex = 0; rangeIndex < pickRanges.Count; rangeIndex++)
                {
                    var range = pickRanges[rangeIndex];
                    if (range.mesh != meshIndex) continue;
                    if (range.part == TreePreviewPart.Branches)
                    {
                        float minDistance = float.MaxValue, maxDistance = float.MinValue;
                        for (int t = range.start * 3; t < (range.start + range.count) * 3; t++)
                        { minDistance = Mathf.Min(minDistance, uv[meshTriangles[t]].y); maxDistance = Mathf.Max(maxDistance, uv[meshTriangles[t]].y); }
                        for (int t = range.start * 3; t < (range.start + range.count) * 3; t++)
                        {
                            int index = meshTriangles[t];
                            var data = wind[index];
                            data.x = .12f * Mathf.Pow(Mathf.InverseLerp(bounds.min.y, bounds.max.y, vertices[index].y), 2)
                                + .42f * Mathf.InverseLerp(minDistance, maxDistance, uv[index].y);
                            wind[index] = data;
                        }
                    }
                    else if (range.part == TreePreviewPart.Roots)
                        for (int t = range.start * 3; t < (range.start + range.count) * 3; t++) wind[meshTriangles[t]] = Vector4.zero;
                    else if (range.part == TreePreviewPart.Foliage)
                        for (int t = range.start * 3; t < (range.start + range.count) * 3; t++)
                        {
                            int index = meshTriangles[t];
                            var data = wind[index];
                            data.z = Mathf.Repeat(range.cardId * .6180339f, 1);
                            data.w = range.cardId + 1;
                            wind[index] = data;
                        }
                }
                mesh.SetUVs(1, coordinates);
                if (foliage) mesh.SetUVs(2, canopyNormals);
                mesh.SetUVs(3, wind);
            }
        }

        void ConfigureFoliage(TreeRecipe recipe)
        {
            if (defaultFoliage == null || defaultFoliageForm != recipe.Profile.foliageForm)
            {
                if (defaultFoliage != null) UnityEngine.Object.DestroyImmediate(defaultFoliage);
                defaultFoliageForm = recipe.Profile.foliageForm;
                defaultFoliage = defaultFoliageForm == TreeFoliageForm.Broadleaf ? CreateLeafTexture() : CreateNeedleTexture();
            }
            var mat = materials[1];
            var texture = recipe.foliageTexture != null ? recipe.foliageTexture : defaultFoliage;
            mat.mainTexture = texture;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", recipe.leaves);
            mat.SetFloat("_Cutoff", Mathf.Clamp(recipe.foliageCutoff, .05f, .95f));
            mat.SetFloat("_Feathering", Mathf.Clamp(recipe.foliageFeathering, 0, .5f));
            mat.SetFloat("_CanopySoftness", Mathf.Clamp01(recipe.foliageSoftness));
            mat.SetFloat("_AlphaToMask", recipe.foliageAlphaToCoverage ? 1 : 0);
            mat.SetFloat("_OcclusionStrength", Mathf.Clamp01(recipe.foliageOcclusion));
            mat.SetFloat("_Transmission", recipe.foliageTransmissionEnabled ? Mathf.Clamp01(recipe.foliageTransmission) : 0);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)(recipe.foliageBackfaceCulling ? CullMode.Back : CullMode.Off));
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1);
            if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 1);
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.SetFloat("_ZWrite", 1);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            ConfigureSurface(mat, recipe.foliageRoughness, recipe.foliageHighlights, recipe.foliageReflections);
        }

        internal static void ConfigureSurface(Material mat, float roughness, bool highlights, bool reflections)
        {
            float smoothness = 1 - Mathf.Clamp01(roughness);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", highlights ? 1 : 0);
            if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", reflections ? 1 : 0);
            if (mat.HasProperty("_GlossyReflections")) mat.SetFloat("_GlossyReflections", reflections ? 1 : 0);
            SetKeyword(mat, "_SPECULARHIGHLIGHTS_OFF", !highlights);
            SetKeyword(mat, "_ENVIRONMENTREFLECTIONS_OFF", !reflections);
            SetKeyword(mat, "_GLOSSYREFLECTIONS_OFF", !reflections);
        }

        static void SetKeyword(Material mat, string keyword, bool enabled)
        {
            if (enabled) mat.EnableKeyword(keyword); else mat.DisableKeyword(keyword);
        }

        void ConfigureBark(TreeRecipe recipe)
        {
            if (defaultBark == null) defaultBark = CreateBarkTexture();
            var mat = materials[0];
            var texture = recipe.barkTexture != null ? recipe.barkTexture : defaultBark;
            var tiling = new Vector2(Mathf.Clamp(recipe.barkTilingAround, 1, 12), Mathf.Clamp(recipe.barkTilingLength, .1f, 8));
            mat.mainTexture = texture;
            mat.mainTextureScale = tiling;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", texture);
                mat.SetTextureScale("_BaseMap", tiling);
                mat.SetColor("_BaseColor", recipe.bark);
            }
            mat.SetFloat("_OcclusionStrength", Mathf.Clamp01(recipe.barkOcclusion));
            ConfigureSurface(mat, recipe.barkRoughness, recipe.barkHighlights, recipe.barkReflections);
        }

    }
}
