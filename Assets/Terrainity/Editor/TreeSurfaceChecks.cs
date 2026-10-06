using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Terrainity.Editor
{
    internal static class TreeSurfaceChecks
    {
        [MenuItem("Tools/Terrainity/Validate Tree Surface")]
        static void Run()
        {
            try
            {
                var recipe = new TreeRecipe { layers = 3, foliageFeathering = .25f, foliageOcclusion = .8f, barkOcclusion = .7f, foliageTransmissionEnabled = true, foliageTransmission = .75f };
                var restored = TreeRecipeJson.From(recipe, 0).Restore(out _);
                Require(restored.foliageFeathering == .25f && restored.foliageOcclusion == .8f && restored.barkOcclusion == .7f, "Recipe round-trip");
                Require(restored.foliageTransmissionEnabled && restored.foliageTransmission == .75f, "Transmission recipe round-trip");
                var shader = Shader.Find("Terrainity/Tree");
                Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Runtime shader compile");
                Require(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/Terrainity/Tint Preview")), "Preview shader compile");
                CheckCoverage(shader);
                Require(File.Exists("Assets/Terrainity/Shaders/TreeSurface.hlsl"), "Shader include exists (explicitly included in package export)");
                Require(File.Exists("Assets/Terrainity/Shaders/TreeWind.hlsl")
                    && File.Exists("Assets/Terrainity/Runtime/TreeWindController.cs"), "Wind runtime dependencies exist");
                using (var preview = new TreePreview())
                {
                    var lighting = new PreviewLightingSettings();
                    lighting.ambient = 1;
                    foreach (var light in lighting.lights) light.intensity = 0;
                    preview.SetLighting(lighting);
                    recipe.foliageOcclusion = recipe.barkOcclusion = 0; recipe.foliageFeathering = 0;
                    preview.Build(recipe, 0);
                    var baseline = preview.ReferenceSnapshot(512);
                    recipe.foliageOcclusion = recipe.barkOcclusion = 1;
                    preview.Build(recipe, 0);
                    var occluded = preview.ReferenceSnapshot(512);
                    var hardPixels = occluded.GetPixels32();
                    try
                    {
                        float brightnessA = baseline.GetPixels().Sum(c => c.r + c.g + c.b);
                        float brightnessB = occluded.GetPixels().Sum(c => c.r + c.g + c.b);
                        Require(brightnessB < brightnessA - 1, "AO visibly reduces ambient lighting");
                        File.WriteAllBytes("Temp/tree-surface-before.png", baseline.EncodeToPNG());
                        File.WriteAllBytes("Temp/tree-surface-ao.png", occluded.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(baseline); UnityEngine.Object.DestroyImmediate(occluded); }
                    recipe.foliageFeathering = .5f;
                    preview.Build(recipe, 0);
                    foreach (var mesh in preview.Meshes)
                    {
                        Require(mesh.uv2.Length == mesh.vertexCount && mesh.uv2.All(v => v.y >= 0 && v.y <= 1), "AO channel bounds");
                        Require(mesh.uv2.Any(v => v.y > .01f), "AO channel populated");
                    }
                    var foliage = preview.Meshes[1];
                    var windWeights = new System.Collections.Generic.List<Vector4>();
                    foliage.GetUVs(3, windWeights);
                    Require(windWeights.Count == foliage.vertexCount && windWeights.Any(v => v.x > 0 && v.y > 0 && v.w > 0),
                        "Foliage wind weights and card IDs");
                    var reduced = TreeLodGenerator.ReduceFoliage(foliage, recipe, 2, new TreeLodSettings());
                    try
                    {
                        Require(reduced.uv2.Any(v => v.y > .01f), "LOD preserves AO");
                        var reducedWind = new System.Collections.Generic.List<Vector4>();
                        reduced.GetUVs(3, reducedWind);
                        Require(reducedWind.Count == reduced.vertexCount && reducedWind.Any(v => v.w > 0),
                            "LOD preserves wind weights and card IDs");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(reduced); }
                    recipe.prunedFoliageCards.Add(0);
                    preview.Build(recipe, 0);
                    var prunedLod = TreeLodGenerator.ReduceFoliage(preview.Meshes[1], recipe, 2, new TreeLodSettings());
                    try { Require(prunedLod.vertexCount > 0, "LOD supports pruned foliage groups"); }
                    finally { UnityEngine.Object.DestroyImmediate(prunedLod); recipe.prunedFoliageCards.Clear(); }
                    preview.Build(recipe, 0);
                    for (int i = 0; i < preview.Meshes.Count; i++)
                    {
                        var exported = new Material(shader);
                        try
                        {
                            exported.CopyPropertiesFromMaterial(preview.MaterialForMesh(i));
                            Require(exported.GetFloat("_OcclusionStrength") == 1, "Export AO material copy");
                            if (i == 1) Require(exported.GetFloat("_Feathering") == .5f, "Export feather material copy");
                            Require(exported.GetFloat("_Transmission") == (i == 1 ? .75f : 0), "Foliage-only transmission export");
                            for (int pass = 0; pass < exported.passCount; pass++) Require(exported.SetPass(pass), "Export shader pass " + pass);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(exported); }
                    }
                    var feather = preview.ReferenceSnapshot(512);
                    try
                    {
                        Require(!feather.GetPixels32().SequenceEqual(hardPixels), "Feathering changes rendered edges");
                        File.WriteAllBytes("Temp/tree-surface-feather.png", feather.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(feather); }
                    lighting.rotation = 215; lighting.ambient = .05f;
                    foreach (var light in lighting.lights) light.intensity = 1.3f;
                    preview.SetLighting(lighting);
                    recipe.foliageFeathering = 0; recipe.foliageTransmissionEnabled = false;
                    preview.Build(recipe, 0);
                    var opaque = preview.ReferenceSnapshot(512);
                    recipe.foliageTransmissionEnabled = true; recipe.foliageTransmission = 1;
                    preview.Build(recipe, 0);
                    var transmitted = preview.ReferenceSnapshot(512);
                    try
                    {
                        Require(transmitted.GetPixels().Sum(c => c.r + c.g + c.b) > opaque.GetPixels().Sum(c => c.r + c.g + c.b) + 1, "Backlight visibly transmits through foliage");
                        File.WriteAllBytes("Temp/tree-transmission-off.png", opaque.EncodeToPNG());
                        File.WriteAllBytes("Temp/tree-transmission-on.png", transmitted.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(opaque); UnityEngine.Object.DestroyImmediate(transmitted); }
                    recipe.foliageSoftness = 0;
                    preview.Build(recipe, 0);
                    var crisp = preview.ReferenceSnapshot(512);
                    var positions = preview.Meshes[1].vertices;
                    recipe.foliageSoftness = .8f; recipe.foliageAlphaToCoverage = true;
                    preview.Build(recipe, 0);
                    var soft = preview.ReferenceSnapshot(512);
                    try
                    {
                        Require(!crisp.GetPixels32().SequenceEqual(soft.GetPixels32()), "Canopy softness changes lighting");
                        Require(positions.SequenceEqual(preview.Meshes[1].vertices), "Softness preserves geometry");
                        File.WriteAllBytes("Temp/tree-canopy-soft.png", soft.EncodeToPNG());
                        File.WriteAllBytes("Temp/tree-canopy-original.png", crisp.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.DestroyImmediate(crisp); UnityEngine.Object.DestroyImmediate(soft); }
                    var canopy = new System.Collections.Generic.List<Vector3>();
                    preview.Meshes[1].GetUVs(2, canopy);
                    Require(canopy.Count == positions.Length && canopy.All(n=>Mathf.Abs(n.magnitude-1)<.001f), "Unit canopy shading normals");
                    var softLod = TreeLodGenerator.ReduceFoliage(preview.Meshes[1], recipe, 2, new TreeLodSettings());
                    try { var reducedCanopy = new System.Collections.Generic.List<Vector3>(); softLod.GetUVs(2, reducedCanopy); Require(reducedCanopy.Count == softLod.vertexCount && reducedCanopy.All(n=>Mathf.Abs(n.magnitude-1)<.001f), "LOD canopy normals"); }
                    finally { UnityEngine.Object.DestroyImmediate(softLod); }
                    var softRecipe = TreeRecipeJson.From(recipe, 0).Restore(out _);
                    Require(softRecipe.foliageSoftness == .8f && softRecipe.foliageAlphaToCoverage, "Softness and coverage JSON");
                    var exportedSoft = new Material(shader);
                    try
                    {
                        exportedSoft.CopyPropertiesFromMaterial(preview.MaterialForMesh(1));
                        Require(exportedSoft.GetFloat("_CanopySoftness") == .8f && exportedSoft.GetFloat("_AlphaToMask") == 1, "Softness and coverage export");
                        for (int pass = 0; pass < exportedSoft.passCount; pass++) Require(exportedSoft.SetPass(pass), "Soft runtime shader pass");
                        Require(preview.MaterialForMesh(0).GetFloat("_AlphaToMask") == 0 && preview.MaterialForMesh(0).GetFloat("_CanopySoftness") == 0, "Bark unaffected");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(exportedSoft); }
                }
                File.WriteAllText("Temp/tree-surface-checks.txt", "PASS: 4x MSAA partial coverage and dither fallback, canopy softness visible without changing geometry, unit canopy normals and LOD preservation, JSON and material export including coverage, shader passes, unchanged bark, AO/feathering/transmission regression checks.");
            }
            catch (Exception exception)
            { File.WriteAllText("Temp/tree-surface-checks.txt", "FAIL: " + exception); Debug.LogException(exception); }
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }

        static void CheckCoverage(Shader shader)
        {
            var material = new Material(shader);
            var texture = new Texture2D(64, 1, TextureFormat.RGBA32, false, true);
            var mesh = new Mesh();
            var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var oldTarget = RenderTexture.active;
            float oldAvailability = Shader.GetGlobalFloat("_AlphaToMaskAvailable");
            try
            {
                texture.SetPixels(Enumerable.Range(0,64).Select(i=>new Color(1,1,1,i/63f)).ToArray()); texture.Apply();
                mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
                mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.triangles = new[] { 0,1,2,0,2,3 };
                material.SetTexture("_BaseMap", texture); material.SetFloat("_AlphaClip", 1); material.SetFloat("_AlphaToMask", 1);
                material.SetFloat("_Cutoff", .5f); material.SetFloat("_Feathering", .5f); material.SetFloat("_Cull", 0);
                target.Create(); Require(target.antiAliasing == 4, "4x MSAA target");
                Color32[] Render(bool available)
                {
                    var command = new UnityEngine.Rendering.CommandBuffer();
                    try
                    {
                        command.SetRenderTarget(target); command.ClearRenderTarget(true, true, Color.magenta);
                        command.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,-1,1), true));
                        command.SetGlobalFloat("_AlphaToMaskAvailable", available ? 1 : 0);
                        command.DrawMesh(mesh, Matrix4x4.identity, material, 0, material.FindPass("ForwardLit"));
                        Graphics.ExecuteCommandBuffer(command);
                        RenderTexture.active = target; readback.ReadPixels(new Rect(0,0,64,64),0,0); readback.Apply();
                        return readback.GetPixels32();
                    }
                    finally { command.Release(); }
                }
                var coverage = Render(true); var fallback = Render(false);
                Require(!coverage.SequenceEqual(fallback), "MSAA coverage differs from dither fallback");
                Require(coverage.Any(c=>c.a > 0 && c.a < 250), "MSAA resolves partial edge coverage");
            }
            finally
            {
                Shader.SetGlobalFloat("_AlphaToMaskAvailable", oldAvailability); RenderTexture.active = oldTarget;
                target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
