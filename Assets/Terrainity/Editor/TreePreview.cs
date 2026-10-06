using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    internal enum TreePreviewPart { None, Trunk, Roots, Branches, Foliage }

    // Isolated concept geometry: no scene objects, project assets or global Random state.
    internal sealed partial class TreePreview : IDisposable
    {
        readonly PreviewRenderUtility renderer = new PreviewRenderUtility();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<int> materialIndices = new List<int>();
        readonly List<Mesh> wireMeshes = new List<Mesh>();
        readonly List<TreePreviewPart> sourceParts = new List<TreePreviewPart>();
        readonly List<PickRange> pickRanges = new List<PickRange>();
        readonly Dictionary<Mesh, List<PickRange>> sourceCardRanges = new Dictionary<Mesh, List<PickRange>>();
        readonly Dictionary<Mesh, int> sourceBranchLimbs = new Dictionary<Mesh, int>();
        readonly Dictionary<int, List<int>> branchCards = new Dictionary<int, List<int>>();
        readonly HashSet<int> prunedCards = new HashSet<int>();
        readonly Dictionary<int, List<TreeBranchGrowth.Limb>> childLimbs = new Dictionary<int, List<TreeBranchGrowth.Limb>>();
        readonly Shader treeShader;
        bool disposed;
        TreePreviewPart currentPart;
        Mesh selectionWire;
        double selectionExpires;
        struct PickRange { internal int mesh, start, count, cardId, limbIndex; internal TreePreviewPart part; }
        Material wireMaterial;
        internal bool ShowMeshOverlay { get; set; }
        readonly Material[] materials = new Material[2];
        readonly List<Light> previewLights = new List<Light>();
        PreviewEnvironmentSettings environment;
        Mesh gridMesh;
        Material gridMaterial;
        Skybox cameraSkybox;
        float yaw = 35, pitch = 10, zoom = 1;
        bool allowFarZoom;
        int lodSubmeshes;
        Vector3 panOffset;
        Bounds bounds;
        Bounds lodReferenceBounds;
        System.Random random;
        Texture2D defaultFoliage, defaultBark;
        TreeFoliageForm defaultFoliageForm;
        readonly Texture2D[] tintRamps = new Texture2D[2];
        internal int WoodTriangles { get; private set; }
        internal int RemovedWoodTriangles { get; private set; }
        internal int RemovedWoodVertices { get; private set; }
        internal int FoliageTriangles { get; private set; }
        internal IReadOnlyList<Mesh> Meshes => meshes;
        internal Material MaterialForMesh(int index) => materials[materialIndices[index]];
        internal void AllowFarZoom(bool enabled) => allowFarZoom = enabled;

        internal TreePreview()
        {
            var shader = Shader.Find("Hidden/Terrainity/Tint Preview") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            treeShader = shader;
            if (shader != null)
                for (int i = 0; i < materials.Length; i++) materials[i] = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            renderer.camera.fieldOfView = 45;
            renderer.camera.clearFlags = CameraClearFlags.SolidColor;
            renderer.camera.backgroundColor = new Color(.10f, .14f, .12f);
            previewLights.AddRange(renderer.lights);
            SetLighting(new PreviewLightingSettings());
        }

        internal void SetLighting(PreviewLightingSettings settings)
        {
            settings.Validate();
            while (previewLights.Count < settings.lights.Count)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("Terrainity preview light", HideFlags.HideAndDontSave, typeof(Light));
                renderer.AddSingleGO(go);
                previewLights.Add(go.GetComponent<Light>());
            }
            for (int i = 0; i < previewLights.Count; i++)
            {
                var light = previewLights[i];
                bool active = i < settings.lights.Count;
                light.type = LightType.Directional;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                // PreviewRenderUtility re-enables its two built-in lights on every render.
                light.intensity = active ? settings.lights[i].intensity : 0;
                light.enabled = active;
                if (!active) continue;
                light.color = settings.lights[i].color;
                light.transform.rotation = Quaternion.Euler(45, settings.Position(i), 0);
            }
            renderer.ambientColor = Color.white * settings.ambient;
        }

        internal void SetEnvironment(PreviewEnvironmentSettings settings)
        {
            environment = settings;
            renderer.camera.backgroundColor = settings.background;
            renderer.camera.clearFlags = settings.useSkybox && settings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            if (cameraSkybox == null) cameraSkybox = renderer.camera.gameObject.AddComponent<Skybox>();
            cameraSkybox.material = settings.skybox;
            cameraSkybox.enabled = settings.useSkybox && settings.skybox != null;
        }

        void DrawGrid()
        {
            if (environment == null || !environment.showGrid) return;
            if (gridMesh == null)
            {
                var vertices = new List<Vector3>(); var colors = new List<Color>(); var indices = new List<int>();
                void Line(Vector3 a, Vector3 b, Color color)
                {
                    indices.Add(vertices.Count); vertices.Add(a); colors.Add(color);
                    indices.Add(vertices.Count); vertices.Add(b); colors.Add(color);
                }
                for (int i = -10; i <= 10; i++)
                {
                    var color = new Color(.6f, .65f, .65f, i % 5 == 0 ? .5f : .23f);
                    Line(new Vector3(i,0,-10), new Vector3(i,0,10), i == 0 ? new Color(.25f,.5f,1,.8f) : color);
                    Line(new Vector3(-10,0,i), new Vector3(10,0,i), i == 0 ? new Color(1,.3f,.3f,.8f) : color);
                }
                gridMesh = new Mesh { name = "Preview grid", hideFlags = HideFlags.HideAndDontSave };
                gridMesh.SetVertices(vertices); gridMesh.SetColors(colors); gridMesh.SetIndices(indices, MeshTopology.Lines, 0); gridMesh.RecalculateBounds();
                var shader = Shader.Find("Hidden/Terrainity/Preview Grid");
                if (shader != null) gridMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (gridMaterial == null) return;
            float spacing = Mathf.Pow(10, Mathf.Ceil(Mathf.Log10(Mathf.Max(bounds.size.magnitude / 4, .1f))));
            renderer.DrawMesh(gridMesh, Matrix4x4.TRS(new Vector3(0, -.005f, 0), Quaternion.identity, Vector3.one * spacing), gridMaterial, 0);
        }

        static readonly Vector3[] compassAxes = { Vector3.right, Vector3.up, Vector3.forward };
        static readonly Color[] compassColors = { new Color(1,.35f,.35f), new Color(.35f,1,.4f), new Color(.35f,.65f,1) };
        static readonly string[] compassNames = { "X", "Y", "Z" };
        readonly Vector3[] compassLine = new Vector3[2];
        GUIStyle compassStyle;

        void DrawCompass(Rect rect)
        {
            if (environment == null || !environment.showCompass || rect.width < 120 || rect.height < 120) return;
            Vector2 origin = new Vector2(rect.xMax - 55, rect.y + 55);
            var previousColor = Handles.color;
            Handles.BeginGUI();
            try
            {
                if (compassStyle == null) compassStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
                for (int i = 0; i < 3; i++)
                {
                    Vector3 direction = renderer.camera.transform.InverseTransformDirection(compassAxes[i]);
                    Vector2 end = origin + new Vector2(direction.x, -direction.y) * 30;
                    compassLine[0] = new Vector3(origin.x, origin.y);
                    compassLine[1] = new Vector3(end.x, end.y);
                    Handles.color = compassColors[i]; Handles.DrawAAPolyLine(3, compassLine);
                    compassStyle.normal.textColor = compassColors[i]; GUI.Label(new Rect(end.x - 10, end.y - 10, 20, 20), compassNames[i], compassStyle);
                }
            }
            finally { Handles.EndGUI(); Handles.color = previousColor; }
        }

        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        internal Vector3 ViewOrbit => new Vector3(yaw, pitch, zoom);
        internal Vector3 ViewPan => panOffset;
        internal void SetView(Vector3 orbit, Vector3 pan)
        {
            yaw = orbit.x; pitch = orbit.y; zoom = Mathf.Clamp(orbit.z, .23f, allowFarZoom ? 32 : 2); panOffset = pan;
        }
        internal void ResetView() { yaw = 35; pitch = 10; zoom = 1; panOffset = Vector3.zero; }

        RockRecipe previousRock;
        int previousRockVariant;
        internal void BuildRock(RockRecipe recipe, int variant)
        {
            if (!recipe.SameGeometry(previousRock) || previousRockVariant != variant || meshes.Count == 0)
            {
                var mesh = RockGenerator.Build(recipe, variant);
                ClearMeshes();
                meshes.Add(mesh); materialIndices.Add(0);
                bounds = mesh.bounds;
                WoodTriangles = TerrainityMeshUtility.TriangleCount(mesh); FoliageTriangles = 0;
            }
            if (previousRock == null)
            {
                if (materials[0] != null) UnityEngine.Object.DestroyImmediate(materials[0]);
                materials[0] = RockGenerator.Material(recipe);
            }
            else if (!recipe.SameMaterial(previousRock)) RockGenerator.ConfigureMaterial(materials[0], recipe);
            previousRock = recipe.Copy(); previousRockVariant = variant;
        }

        internal void Build(TreeRecipe recipe, int variant, int pruneLevel = 0)
        {
            RemovedWoodTriangles = 0;
            RemovedWoodVertices = 0;
            if (previousRock != null && materials[0] != null) materials[0].shader = treeShader;
            previousRock = null;
            ClearMeshes();
            if (materials[0] == null) return;
            prunedCards.Clear();
            if (recipe.prunedFoliageCards != null) prunedCards.UnionWith(recipe.prunedFoliageCards);
            materials[0].color = recipe.bark;
            materials[1].color = recipe.leaves;
            ConfigureBark(recipe);
            ConfigureFoliage(recipe);
            // Shared structural randomness keeps every sibling in the same family.
            var sibling = new System.Random(unchecked(recipe.seed + variant * 7919));
            float scale = variant == 0 ? 1 : 1 + ((float)sibling.NextDouble() * 2 - 1) * recipe.variation;
            float spread = variant == 0 ? 1 : 1 + ((float)sibling.NextDouble() * 2 - 1) * recipe.variation;
            random = new System.Random(recipe.seed);
            float height = recipe.height * scale;
            Vector3 tip = new Vector3(recipe.lean * height * .18f, height, 0);
            var trunk = new TreeTrunkGrowth(recipe, tip);
            var limbs = TreeBranchGrowth.Generate(recipe, trunk, spread, variant);
            float[] foliagePathStarts = null;
            float farthestFoliagePath = 1;
            if (recipe.showFoliage && recipe.Profile.foliageForm != TreeFoliageForm.PalmFrond
                && recipe.foliageCardToBranchSize > 0)
                foliagePathStarts = FoliagePathStarts(limbs, out farthestFoliagePath);
            float[] branchScales = null;
            if (pruneLevel > 0)
            {
                branchScales = TreeLodGenerator.BranchScaleFactors(limbs, recipe, pruneLevel);
                for (int i = 0; i < branchScales.Length; i++)
                {
                    if (branchScales[i] <= 0) { limbs[i].pruned = true; continue; }
                    if (branchScales[i] >= 1) continue;
                    // Keep the original foliage sockets while the supporting wood contracts.
                    limbs[i].foliagePoints = limbs[i].points;
                    var points = (Vector3[])limbs[i].points.Clone();
                    limbs[i].points = points;
                    for (int point = 1; point < points.Length; point++)
                        points[point] = Vector3.Lerp(points[0], points[point], branchScales[i]);
                    limbs[i].radius *= branchScales[i];
                }
            }
            limbs.AddRange(TreeRootGrowth.Generate(recipe, trunk, spread, variant, limbs.Count));
            childLimbs.Clear();
            foreach (var limb in limbs)
            {
                if (!childLimbs.TryGetValue(limb.parentIndex, out var children))
                    childLimbs.Add(limb.parentIndex, children = new List<TreeBranchGrowth.Limb>());
                children.Add(limb);
            }
            IndexBranchCards(limbs, recipe);
            currentPart = TreePreviewPart.Trunk;
            CurvedTrunk(recipe, trunk, limbs);
            for (int i = 0; i < limbs.Count; i++)
            {
                if (limbs[i].pruned) continue;
                currentPart = limbs[i].isRoot ? TreePreviewPart.Roots : TreePreviewPart.Branches;
                CurvedLimb(recipe, limbs, i);
            }
            // Distant leaves keep their original canopy sockets even when an interior
            // supporting twig shrinks or disappears from the wood mesh.
            if (recipe.showFoliage)
            {
                for (int limbIndex = 0; limbIndex < limbs.Count; limbIndex++)
                {
                    var limb = limbs[limbIndex];
                    if (!limb.terminal) continue;
                    currentPart = TreePreviewPart.Foliage;
                    var foliagePoints = limb.foliagePoints ?? limb.points;
                    if (recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond)
                    {
                        int frondId = limbIndex * 144;
                        if (!prunedCards.Contains(frondId)) PalmFrond(recipe, foliagePoints, frondId);
                        continue;
                    }
                    float foliageSize = recipe.crownWidth * spread * .12f * Mathf.Clamp(recipe.foliageSize, .25f, 3)
                        / (1 + limb.level * .25f);
                    if (foliagePathStarts != null)
                    {
                        // Compare whole branches, so every card on one branch gets
                        // the same scale regardless of its cluster position.
                        float branchLength = foliagePathStarts[limbIndex] + ArcLength(foliagePoints, 1);
                        foliageSize *= 1f - .8f * Mathf.Clamp01(recipe.foliageCardToBranchSize)
                            * (1f - Mathf.Clamp01(branchLength / farthestFoliagePath));
                    }
                    int clusters = Mathf.Clamp(Mathf.RoundToInt(recipe.layers / 3f), 1, 12);
                    for (int i = 0; i < clusters; i++)
                    {
                        int clusterSeed = unchecked(recipe.seed ^ limbIndex * 104729 ^ i * 7919);
                        random = new System.Random(clusterSeed);
                        float row = i / (float)Mathf.Max(1, clusters - 1);
                        float t = recipe.foliageAligned
                            ? 1f - Mathf.Clamp01(recipe.foliageClusterDistance) * (1f - row)
                            : 1f - Mathf.Clamp01(recipe.foliageClusterDistance) * row;
                        float segment = t * (foliagePoints.Length - 1);
                        int socket = Mathf.Min(Mathf.FloorToInt(segment), foliagePoints.Length - 2);
                        var center = Vector3.Lerp(foliagePoints[socket], foliagePoints[socket + 1], segment - socket);
                        var direction = (foliagePoints[socket + 1] - foliagePoints[socket]).normalized;
                        // Taper begins at the first placed row, even when distance
                        // keeps that row partway up the branch.
                        float rowSize = recipe.foliageAligned
                            ? foliageSize * (1f - .8f * Mathf.Clamp01(recipe.foliageCardTaper)
                                * (recipe.foliageClusterDistance > 0 && clusters > 1 ? row : 0))
                            : foliageSize;
                        if (recipe.crownTaper > 0)
                        {
                            float crownEnd = recipe.Profile.crownEnd > 0 ? recipe.Profile.crownEnd : .9f;
                            float crownHeight = Mathf.InverseLerp(recipe.crownStart,
                                Mathf.Max(recipe.crownStart + .001f, crownEnd), center.y / Mathf.Max(height, .001f));
                            rowSize *= recipe.CrownTaperScale(crownHeight);
                        }
                        LeafCards(recipe, center, direction, rowSize, clusterSeed,
                            limbIndex, i, recipe.foliageAligned);
                    }
                }
            }
            childLimbs.Clear(); // Socket indexing is needed only while emitting wood geometry.
            CombineByMaterial();
            WoodTriangles = FoliageTriangles = 0;
            for (int i = 0; i < meshes.Count; i++)
                if (materialIndices[i] == 0) WoodTriangles += (int)meshes[i].GetIndexCount(0) / 3;
                else FoliageTriangles += (int)meshes[i].GetIndexCount(0) / 3;
            bounds = meshes[0].bounds;
            foreach (var mesh in meshes) bounds.Encapsulate(mesh.bounds);
            ApplyTints(recipe);
            // The selected floor is the placement pivot, shared by preview, export and every LOD.
            float floor = Mathf.Clamp(recipe.floorHeight, -2, 2);
            if (!Mathf.Approximately(floor, 0))
            {
                foreach (var mesh in meshes)
                {
                    var vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i].y -= floor;
                    mesh.vertices = vertices; mesh.RecalculateBounds();
                }
                bounds = meshes[0].bounds;
                foreach (var mesh in meshes) bounds.Encapsulate(mesh.bounds);
            }
        }

        void IndexBranchCards(List<TreeBranchGrowth.Limb> limbs, TreeRecipe recipe)
        {
            branchCards.Clear();
            int clusters = Mathf.Clamp(Mathf.RoundToInt(recipe.layers / 3f), 1, 12);
            int cards = recipe.foliageAligned ? Mathf.Clamp(recipe.foliageAlignSides, 1, 3)
                : Mathf.Clamp(recipe.foliageCards, 2, 12);
            for (int terminal = 0; terminal < limbs.Count; terminal++)
            {
                if (!limbs[terminal].terminal || limbs[terminal].isRoot) continue;
                for (int ancestor = terminal; ancestor >= 0; ancestor = limbs[ancestor].parentIndex)
                {
                    if (!branchCards.TryGetValue(ancestor, out var ids))
                    {
                        ids = new List<int>();
                        branchCards.Add(ancestor, ids);
                    }
                    if (recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond) ids.Add(terminal * 144);
                    else for (int cluster = 0; cluster < clusters; cluster++)
                        for (int card = 0; card < cards; card++)
                            ids.Add((terminal * 12 + cluster) * 12 + card);
                }
            }
        }

        internal IReadOnlyList<int> CardsOnBranch(int limbIndex)
            => branchCards.TryGetValue(limbIndex, out var ids) ? ids : Array.Empty<int>();

        internal void BuildLod(TreeRecipe recipe, int variant, int level, TreeLodSettings settings)
        {
            Build(recipe, variant);
            var fullBounds = bounds;
            var lod = TreeLodGenerator.Build(recipe, variant, level, settings);
            ClearMeshes();
            meshes.Add(lod); materialIndices.Add(0);
            lodSubmeshes = lod.subMeshCount;
            lodReferenceBounds = fullBounds;
            bounds = lod.bounds;
            WoodTriangles = (int)lod.GetIndexCount(0) / 3;
            FoliageTriangles = lodSubmeshes > 1 ? (int)lod.GetIndexCount(1) / 3 : 0;
        }

        internal static float[] FoliagePathStarts(List<TreeBranchGrowth.Limb> limbs, out float farthestPath)
        {
            var starts = new float[limbs.Count];
            farthestPath = 0;
            for (int i = 0; i < limbs.Count; i++)
            {
                var limb = limbs[i];
                if (limb.parentIndex >= 0)
                {
                    var parent = limbs[limb.parentIndex];
                    starts[i] = starts[limb.parentIndex] + ArcLength(parent.points, limb.attachment);
                }
                if (limb.terminal && !limb.isRoot)
                    farthestPath = Mathf.Max(farthestPath, starts[i] + ArcLength(limb.points, 1));
            }
            farthestPath = Mathf.Max(farthestPath, .0001f);
            return starts;
        }

        void CombineByMaterial()
        {
            var combined = new List<Mesh>(); var indices = new List<int>();
            var combinedRanges = new List<PickRange>();
            for (int material = 0; material < materials.Length; material++)
            {
                var parts = new List<CombineInstance>();
                int triangleStart = 0;
                for (int i = 0; i < meshes.Count; i++)
                    if (materialIndices[i] == material)
                    {
                        int count = (int)meshes[i].GetIndexCount(0) / 3;
                        if (sourceCardRanges.TryGetValue(meshes[i], out var cards))
                            foreach (var card in cards)
                                combinedRanges.Add(new PickRange { mesh = combined.Count, start = triangleStart + card.start,
                                    count = card.count, part = TreePreviewPart.Foliage, cardId = card.cardId, limbIndex = -1 });
                        else combinedRanges.Add(new PickRange { mesh = combined.Count, start = triangleStart,
                            count = count, part = sourceParts[i], cardId = -1,
                            limbIndex = sourceBranchLimbs.TryGetValue(meshes[i], out int limbIndex) ? limbIndex : -1 });
                        triangleStart += count;
                        parts.Add(new CombineInstance { mesh = meshes[i], transform = Matrix4x4.identity });
                    }
                if (parts.Count == 0) continue;
                var mesh = new Mesh { name = "Terrainity combined preview", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), true, false);
                combined.Add(mesh); indices.Add(material);
            }
            ClearMeshes(); meshes.AddRange(combined); materialIndices.AddRange(indices); pickRanges.AddRange(combinedRanges);
        }

        Mesh AddMesh(List<Vector3> vertices, List<int> triangles, int material, List<Vector3> normals = null, List<Vector2> uv = null)
        {
            var mesh = new Mesh { name = "Terrainity concept preview", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            if (uv != null) mesh.SetUVs(0, uv);
            if (normals == null) mesh.RecalculateNormals(); else mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            meshes.Add(mesh); materialIndices.Add(material); sourceParts.Add(currentPart);
            return mesh;
        }

    }
}
