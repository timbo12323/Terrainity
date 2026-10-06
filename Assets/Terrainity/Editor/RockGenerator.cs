using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    internal enum RockUvMode { Box, Spherical }

    [Serializable]
    internal sealed class RockRecipe
    {
        public string kind = "TerrainityRock";
        public int version = 1;
        public string assetName = "Woodland Boulder";
        public string preset = "Natural boulder";
        public int seed = 12345, subdivisions = 3, variantCount = 1;
        public float width = 2, height = 1.6f, depth = 1.8f;
        public float irregularity = .28f, angularity = .25f, detail = .06f, detailScale = 6, baseFlattening = .18f;
        public float variation = .18f, roughness = .85f, textureScale = 1, textureScaleV = 1, normalStrength = 1, projectionBlend = 4;
        public RockUvMode uvMode = RockUvMode.Box;
        public bool flatShading, generateLods = true, collider = true;
        public bool specularHighlights = true, environmentReflections = true, crossFade = true;
        public float lod1Start = .35f, lod2Start = .12f, cull = .01f;
        public Color tint = new Color(.48f, .46f, .42f);
        public string textureGuid = "", normalGuid = "";
        internal static readonly string[] Presets = { "Natural boulder", "River pebble", "Layered slab", "Jagged crag", "Stylized boulder", "Low-poly crystal" };
        internal void ApplyPreset(string name)
        {
            preset = name; width = 2; height = 1.6f; depth = 1.8f;
            irregularity = .28f; angularity = .25f; detail = .06f; detailScale = 6; baseFlattening = .18f; subdivisions = 3; flatShading = false;
            tint = new Color(.48f, .46f, .42f); roughness = .85f;
            if (name == "River pebble") { height = .9f; irregularity = .12f; angularity = 0; detail = .015f; baseFlattening = .08f; }
            if (name == "Layered slab") { width = 3; height = .65f; depth = 2; angularity = .8f; detail = .035f; baseFlattening = .3f; }
            if (name == "Jagged crag") { height = 3.6f; width = 1.6f; depth = 1.4f; irregularity = .5f; angularity = .85f; detail = .1f; }
            if (name == "Stylized boulder") { subdivisions = 1; flatShading = true; angularity = .55f; detail = 0; tint = new Color(.47f, .53f, .58f); }
            if (name == "Low-poly crystal") { subdivisions = 1; height = 3; width = 1.2f; depth = 1.1f; flatShading = true; irregularity = .12f; angularity = .9f; detail = 0; roughness = .35f; tint = new Color(.32f, .56f, .58f); }
        }
        // All fields are value types or immutable strings; texture assets are referenced by GUID.
        internal RockRecipe Copy() => (RockRecipe)MemberwiseClone();
        // Appearance changes deliberately stay outside the geometry cache key.
        internal bool SameGeometry(RockRecipe other) => other != null &&
            seed == other.seed && subdivisions == other.subdivisions && width == other.width && height == other.height && depth == other.depth &&
            irregularity == other.irregularity && angularity == other.angularity && detail == other.detail && detailScale == other.detailScale &&
            baseFlattening == other.baseFlattening && variation == other.variation && flatShading == other.flatShading;
        internal bool SameMaterial(RockRecipe other) => other != null && tint == other.tint && roughness == other.roughness &&
            specularHighlights == other.specularHighlights && environmentReflections == other.environmentReflections &&
            textureGuid == other.textureGuid && normalGuid == other.normalGuid && textureScale == other.textureScale && textureScaleV == other.textureScaleV && normalStrength == other.normalStrength &&
            projectionBlend == other.projectionBlend && uvMode == other.uvMode;
        internal void Validate()
        {
            if (kind != "TerrainityRock" || version != 1) throw new InvalidDataException("This is not a supported Terrainity rock recipe.");
            TreeJsonStorage.ValidateNumbers(tint);
            foreach (float value in new[] { width, height, depth, irregularity, angularity, detail, detailScale, baseFlattening, variation, roughness, textureScale, textureScaleV, normalStrength, projectionBlend, lod1Start, lod2Start, cull })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Rock settings must contain finite numbers.");
            width = Mathf.Clamp(width, .1f, 30); height = Mathf.Clamp(height, .1f, 30); depth = Mathf.Clamp(depth, .1f, 30);
            subdivisions = Mathf.Clamp(subdivisions, 1, 5); variantCount = Mathf.Clamp(variantCount, 1, 12);
            irregularity = Mathf.Clamp(irregularity, 0, .7f); angularity = Mathf.Clamp01(angularity); detail = Mathf.Clamp(detail, 0, .2f);
            detailScale = Mathf.Clamp(detailScale, 1, 15); baseFlattening = Mathf.Clamp(baseFlattening, 0, .65f);
            variation = Mathf.Clamp(variation, 0, .6f); roughness = Mathf.Clamp01(roughness);
            textureScale = Mathf.Clamp(textureScale, .1f, 20);
            textureScaleV = Mathf.Clamp(textureScaleV <= 0 ? textureScale : textureScaleV, .1f, 20);
            projectionBlend = Mathf.Clamp(projectionBlend <= 0 ? 4 : projectionBlend, 1, 16);
            if (!Enum.IsDefined(typeof(RockUvMode), uvMode)) uvMode = RockUvMode.Box;
            normalStrength = Mathf.Clamp(normalStrength, 0, 3);
            lod1Start = Mathf.Clamp(lod1Start, .21f, .8f); lod2Start = Mathf.Clamp(lod2Start, .04f, .2f); cull = Mathf.Clamp(cull, .001f, .03f);
        }
        internal static RockRecipe Read(string path)
        {
            var json = TreeJsonStorage.ReadText(path);
            var header = JsonUtility.FromJson<TreeJsonStorage.DocumentHeader>(json);
            if (header.kind != "TerrainityRock" || header.version != 1) throw new InvalidDataException("Choose a Terrainity rock recipe JSON file, version 1.");
            var result = new RockRecipe();
            JsonUtility.FromJsonOverwrite(json, result);
            if (!json.Contains("\"uvMode\"")) result.uvMode = RockUvMode.Spherical;
            if (!json.Contains("\"textureScaleV\"")) result.textureScaleV = result.textureScale;
            result.Validate(); return result;
        }
        internal Texture2D Texture(bool normal = false) => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(normal ? normalGuid : textureGuid));
    }

    internal static class RockGenerator
    {
        internal static Mesh Build(RockRecipe source, int sibling, int lod = 0)
        {
            var r = source.Copy(); r.Validate();
            int subdivisions = Mathf.Max(0, r.subdivisions - lod);
            var random = new System.Random(unchecked(r.seed + sibling * 7919));
            float Next() => (float)random.NextDouble();
            var offset = new Vector3(Next(), Next(), Next()) * 100;
            var scale = Vector3.one;
            if (sibling > 0) scale = new Vector3(1 + (Next() * 2 - 1) * r.variation, 1 + (Next() * 2 - 1) * r.variation, 1 + (Next() * 2 - 1) * r.variation);
            var planes = new Vector3[10]; var distances = new float[10];
            for (int i = 0; i < planes.Length; i++) { planes[i] = new Vector3(Next() * 2 - 1, Next() * 2 - 1, Next() * 2 - 1).normalized; distances[i] = .7f + Next() * .25f; }
            var topology = GetTopology(subdivisions);
            var vertices = new List<Vector3>(topology.vertices);
            var triangles = topology.triangles;
            var uv = new List<Vector2>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 n = vertices[i];
                vertices[i] = Deform(n, r, offset, scale, planes, distances);
                uv.Add(new Vector2(Mathf.Atan2(n.z, n.x) / (2 * Mathf.PI) + .5f, Mathf.Asin(n.y) / Mathf.PI + .5f));
            }
            // All LODs use the full-resolution base height to keep their pivot aligned.
            float minY = float.PositiveInfinity;
            foreach (var v in vertices) minY = Mathf.Min(minY, v.y);
            if (lod > 0)
            {
                // Sample the full-resolution surface without building its mesh, UVs or tangents.
                var full = GetTopology(r.subdivisions);
                float fullMinY = float.PositiveInfinity;
                foreach (var point in full.vertices)
                    fullMinY = Mathf.Min(fullMinY, Deform(point, r, offset, scale, planes, distances).y);
                var first = Deform(full.vertices[0], r, offset, scale, planes, distances);
                first -= new Vector3(0, fullMinY, 0);
                // Keep the original subtraction order so old seeded LOD pivots match exactly.
                minY = vertices[0].y - first.y;
            }
            // Center the horizontal footprint and place the lowest point at the pivot.
            for (int i = 0; i < vertices.Count; i++) vertices[i] -= new Vector3(0, minY, 0);
            var mesh = new Mesh { name = "Rock", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            if (!r.flatShading) mesh.RecalculateNormals();
            var smoothNormals = r.flatShading ? Array.Empty<Vector3>() : mesh.normals;
            var outVertices = r.flatShading ? new List<Vector3>() : new List<Vector3>(vertices);
            var outUv = r.flatShading ? new List<Vector2>() : new List<Vector2>(uv);
            var outNormals = r.flatShading ? new List<Vector3>() : new List<Vector3>(smoothNormals);
            var indices = new List<int>(triangles.Count);
            var uvCopies = new Dictionary<int, int>();
            // UV0 remains usable with ordinary materials; the box material blends projections per pixel.
            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i+1], c = triangles[i+2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                float low = Mathf.Min(uv[a].x, Mathf.Min(uv[b].x, uv[c].x));
                float high = Mathf.Max(uv[a].x, Mathf.Max(uv[b].x, uv[c].x));
                for (int j = 0; j < 3; j++)
                {
                    int index = triangles[i+j]; Vector2 coord = uv[index];
                    bool shifted = high - low > .5f && coord.x < .5f;
                    if (shifted) coord.x += 1;
                    if (r.flatShading)
                    {
                        indices.Add(outVertices.Count); outVertices.Add(vertices[index]); outUv.Add(coord); outNormals.Add(normal);
                    }
                    else if (shifted)
                    {
                        if (!uvCopies.TryGetValue(index, out int copy))
                        {
                            copy = outVertices.Count; uvCopies.Add(index, copy);
                            outVertices.Add(vertices[index]); outUv.Add(coord); outNormals.Add(smoothNormals[index]);
                        }
                        indices.Add(copy);
                    }
                    else indices.Add(index);
                }
            }
            mesh.Clear(); mesh.SetVertices(outVertices); mesh.SetTriangles(indices,0); mesh.SetUVs(0,outUv); mesh.SetNormals(outNormals); mesh.RecalculateTangents(); mesh.RecalculateBounds();
            return mesh;
        }
        sealed class SphereTopology
        {
            internal readonly List<Vector3> vertices;
            internal readonly List<int> triangles;
            internal SphereTopology(List<Vector3> vertices, List<int> triangles)
            { this.vertices = vertices; this.triangles = triangles; }
        }

        // Bounded to six resolutions and read on the Editor thread; never deform cached vertices.
        static readonly SphereTopology[] sphereTopologies = new SphereTopology[6];
        static SphereTopology GetTopology(int subdivisions)
        {
            if (sphereTopologies[subdivisions] != null) return sphereTopologies[subdivisions];
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var vertices = new List<Vector3> { new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1) };
            for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
            var triangles = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int level = 0; level < subdivisions; level++)
            {
                var edges = new Dictionary<long, int>(); var next = new List<int>();
                int Mid(int a, int b)
                {
                    long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    if (edges.TryGetValue(key, out int found)) return found;
                    int index = vertices.Count; vertices.Add((vertices[a] + vertices[b]).normalized); edges.Add(key, index); return index;
                }
                for (int i = 0; i < triangles.Count; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2], ab = Mid(a,b), bc = Mid(b,c), ca = Mid(c,a);
                    TerrainityMeshUtility.AddTriangle(next, a, ab, ca);
                    TerrainityMeshUtility.AddTriangle(next, b, bc, ab);
                    TerrainityMeshUtility.AddTriangle(next, c, ca, bc);
                    TerrainityMeshUtility.AddTriangle(next, ab, bc, ca);
                }
                triangles = next;
            }
            return sphereTopologies[subdivisions] = new SphereTopology(vertices, triangles);
        }

        static Vector3 Deform(Vector3 n, RockRecipe r, Vector3 offset, Vector3 scale, Vector3[] planes, float[] distances)
        {
            float radius = 1 + r.irregularity * Noise(n * 1.8f + offset) + r.detail * Noise(n * r.detailScale + offset * 2);
            float clipped = radius;
            for (int j = 0; j < planes.Length; j++) { float dot = Vector3.Dot(n, planes[j]); if (dot > .01f) clipped = Mathf.Min(clipped, distances[j] / dot); }
            Vector3 v = n * Mathf.Lerp(radius, clipped, r.angularity);
            float floor = -1 + r.baseFlattening;
            // Compress, rather than collapse, the underside so triangles remain valid.
            if (v.y < floor) v.y = floor + (v.y - floor) * .08f;
            return Vector3.Scale(v, Vector3.Scale(new Vector3(r.width, r.height, r.depth) * .5f, scale));
        }

        static float Noise(Vector3 p) => (Mathf.PerlinNoise(p.x,p.y) + Mathf.PerlinNoise(p.y,p.z) + Mathf.PerlinNoise(p.z,p.x)) * (2f/3) - 1;
        static Shader ShaderFor(RockRecipe recipe) => recipe.uvMode == RockUvMode.Box
            ? Shader.Find("Terrainity/Rock Triplanar")
            : Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        internal static Material Material(RockRecipe r)
        {
            var shader = ShaderFor(r);
            if (shader == null) throw new InvalidOperationException("The Terrainity rock projection shader is unavailable.");
            var material = new Material(shader) { name = "Rock", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
            ConfigureMaterial(material, r);
            return material;
        }
        internal static void ConfigureMaterial(Material material, RockRecipe r)
        {
            var shader = ShaderFor(r);
            if (shader == null) throw new InvalidOperationException("The Terrainity rock projection shader is unavailable.");
            if (material.shader != shader) material.shader = shader;
            string map = material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            material.SetColor(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", r.tint);
            material.SetTexture(map, r.Texture()); material.SetTextureScale(map, new Vector2(r.textureScale, r.textureScaleV));
            TreePreview.ConfigureSurface(material, r.roughness, r.specularHighlights, r.environmentReflections);
            var normal = r.Texture(true);
            material.SetTexture("_BumpMap", normal); material.SetFloat("_BumpScale", r.normalStrength);
            material.SetTextureScale("_BumpMap", new Vector2(r.textureScale, r.textureScaleV));
            if (material.HasProperty("_BlendSharpness")) material.SetFloat("_BlendSharpness", r.projectionBlend);
            if (normal != null) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            // Match URP's import-time normalization before hashing shared materials.
            if (material.shader.name == "Universal Render Pipeline/Lit")
                BaseShaderGUI.SetMaterialKeywords(material, UnityEditor.Rendering.Universal.ShaderGUI.LitGUI.SetMaterialKeywords);
        }
    }
}
