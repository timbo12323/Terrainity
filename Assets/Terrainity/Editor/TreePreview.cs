using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    internal enum TreePreviewPart { None, Trunk, Roots, Branches, Foliage }

    // Isolated concept geometry: no scene objects, project assets or global Random state.
    internal sealed class TreePreview : IDisposable
    {
        readonly PreviewRenderUtility renderer = new PreviewRenderUtility();
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<int> materialIndices = new List<int>();
        readonly List<Mesh> wireMeshes = new List<Mesh>();
        readonly List<TreePreviewPart> sourceParts = new List<TreePreviewPart>();
        readonly List<PickRange> pickRanges = new List<PickRange>();
        TreePreviewPart currentPart;
        Mesh selectionWire;
        double selectionExpires;
        struct PickRange { internal int mesh, start, count; internal TreePreviewPart part; }
        Material wireMaterial;
        internal bool ShowMeshOverlay { get; set; }
        readonly Material[] materials = new Material[2];
        readonly List<Light> previewLights = new List<Light>();
        PreviewEnvironmentSettings environment;
        Mesh gridMesh;
        Material gridMaterial;
        Skybox cameraSkybox;
        float yaw = 35, pitch = 10, zoom = 1;
        Vector3 panOffset;
        bool panning;
        int dragButton;
        Bounds bounds;
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

        internal TreePreview()
        {
            var shader = Shader.Find("Hidden/Terrainity/Tint Preview") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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

        void DrawCompass(Rect rect)
        {
            if (environment == null || !environment.showCompass || rect.width < 120 || rect.height < 120) return;
            Vector2 origin = new Vector2(rect.xMax - 55, rect.y + 55);
            var previousColor = Handles.color;
            Handles.BeginGUI();
            try
            {
                var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
                var colors = new[] { new Color(1,.35f,.35f), new Color(.35f,1,.4f), new Color(.35f,.65f,1) };
                var names = new[] { "X", "Y", "Z" };
                var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
                for (int i = 0; i < 3; i++)
                {
                    Vector3 direction = renderer.camera.transform.InverseTransformDirection(axes[i]);
                    Vector2 end = origin + new Vector2(direction.x, -direction.y) * 30;
                    Handles.color = colors[i]; Handles.DrawAAPolyLine(3, new Vector3(origin.x, origin.y), new Vector3(end.x, end.y));
                    style.normal.textColor = colors[i]; GUI.Label(new Rect(end.x - 10, end.y - 10, 20, 20), names[i], style);
                }
            }
            finally { Handles.EndGUI(); Handles.color = previousColor; }
        }

        float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
        internal Vector3 ViewOrbit => new Vector3(yaw, pitch, zoom);
        internal Vector3 ViewPan => panOffset;
        internal void SetView(Vector3 orbit, Vector3 pan)
        {
            yaw = orbit.x; pitch = orbit.y; zoom = Mathf.Clamp(orbit.z, .23f, 2); panOffset = pan;
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
                WoodTriangles = mesh.triangles.Length / 3; FoliageTriangles = 0;
            }
            if (previousRock == null)
            {
                if (materials[0] != null) UnityEngine.Object.DestroyImmediate(materials[0]);
                materials[0] = RockGenerator.Material(recipe);
            }
            else if (!recipe.SameMaterial(previousRock)) RockGenerator.ConfigureMaterial(materials[0], recipe);
            previousRock = recipe.Copy(); previousRockVariant = variant;
        }

        internal void Build(TreeRecipe recipe, int variant)
        {
            RemovedWoodTriangles = 0;
            RemovedWoodVertices = 0;
            previousRock = null;
            ClearMeshes();
            if (materials[0] == null) return;
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
            limbs.AddRange(TreeRootGrowth.Generate(recipe, trunk, spread, variant, limbs.Count));
            currentPart = TreePreviewPart.Trunk;
            CurvedTrunk(recipe, trunk, limbs);
            for (int i = 0; i < limbs.Count; i++)
            {
                currentPart = limbs[i].isRoot ? TreePreviewPart.Roots : TreePreviewPart.Branches;
                CurvedLimb(recipe, limbs, i);
            }
            // Foliage follows the branch skeleton, so spread and depth change the crown too.
            if (recipe.showFoliage)
            {
                for (int limbIndex = 0; limbIndex < limbs.Count; limbIndex++)
                {
                    var limb = limbs[limbIndex];
                    if (!limb.terminal) continue;
                    currentPart = TreePreviewPart.Foliage;
                    if (recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond)
                    {
                        PalmFrond(recipe, limb);
                        continue;
                    }
                    float foliageSize = recipe.crownWidth * spread * .12f * Mathf.Clamp(recipe.foliageSize, .25f, 3) / (1 + limb.level * .25f);
                    int clusters = Mathf.Clamp(Mathf.RoundToInt(recipe.layers / 3f), 1, 4);
                    for (int i = 0; i < clusters; i++)
                    {
                        int clusterSeed = unchecked(recipe.seed ^ limbIndex * 104729 ^ i * 7919);
                        random = new System.Random(clusterSeed);
                        int socket = limb.points.Length - 1 - i * 2;
                        var center = limb.points[socket];
                        var direction = (limb.points[Mathf.Min(socket + 1, limb.points.Length - 1)] - limb.points[Mathf.Max(socket - 1, 0)]).normalized;
                        LeafCards(recipe, center, direction, foliageSize, clusterSeed);
                    }
                }
            }
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
                var coordinates = new List<Vector2>(vertices.Length);
                var canopyNormals = new List<Vector3>(vertices.Length);
                bool foliage = materialIndices[meshIndex] == 1;
                var cardRandom = new System.Random(unchecked(recipe.seed ^ 73856093));
                float cardTint = 0;
                int tintGroupSize = recipe.Profile.foliageForm == TreeFoliageForm.PalmFrond
                    ? (TreeBranchGrowth.Segments - 1) * 3 : 4 * Mathf.Clamp(recipe.foliageSubdivisions, 1, 12);
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (i % tintGroupSize == 0) cardTint = (float)cardRandom.NextDouble();
                    float t = Mathf.InverseLerp(bounds.min.y, bounds.max.y, vertices[i].y);
                    if (foliage && recipe.foliageTintMode == FoliageTintMode.PerCard) t = cardTint;
                    if (foliage && recipe.foliageTintMode == FoliageTintMode.StemToTip) t = uv[i].y;
                    var relative = vertices[i] - canopy.center;
                    var canopyNormal = new Vector3(relative.x / (extent.x * extent.x), relative.y / (extent.y * extent.y), relative.z / (extent.z * extent.z));
                    canopyNormals.Add(canopyNormal.sqrMagnitude > .000001f ? canopyNormal.normalized : Vector3.up);
                    float radius = new Vector3(relative.x / extent.x, relative.y / extent.y, relative.z / extent.z).magnitude;
                    float interior = Mathf.Clamp01(1 - radius * .8f);
                    float occluded = interior * interior * .85f;
                    if (!foliage)
                        occluded = Mathf.Max(occluded, .65f * (1 - Mathf.SmoothStep(0, 1,
                            Mathf.Clamp01((vertices[i].y - bounds.min.y) / Mathf.Max(.1f, recipe.trunkRadius * 5)))));
                    coordinates.Add(new Vector2(t, occluded));
                }
                mesh.SetUVs(1, coordinates);
                if (foliage) mesh.SetUVs(2, canopyNormals);
            }
        }

        void CurvedTrunk(TreeRecipe recipe, TreeTrunkGrowth trunk, List<TreeBranchGrowth.Limb> limbs)
        {
            int sides = Mathf.Clamp(recipe.trunkSides, 3, 12);
            int segments = Mathf.Clamp(recipe.trunkSegments, 2, 48);
            TreeBarkDetail.Resolution(recipe, recipe.trunkRadius, true, ref sides, ref segments);
            var samples = RingSamples(segments, limbs, -1);
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
                    triangles.AddRange(new[] { a, a + stride, b, b, a + stride, b + stride });
                }
            AddCaps(vertices, normals, uv, triangles, sides, ringCount, trunk.points[0], trunk.points[TreeTrunkGrowth.Segments]);
            AddMesh(vertices, triangles, 0, normals, uv);
        }

        int SimplifyWood(TreeRecipe recipe, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            int sides, List<float> samples, List<TreeBranchGrowth.Limb> limbs, int parent)
        {
            if (!recipe.simplifyWood) return samples.Count;
            var protectedSamples = new List<float>();
            foreach (var limb in limbs) if (limb.parentIndex == parent) protectedSamples.Add(limb.attachment);
            if (parent == -1 && Mathf.Abs(recipe.trunkBend) > 0 && recipe.trunkSharpness > 0)
                for (int i = 0; i < Mathf.Clamp(recipe.trunkBends, 1, 9); i++)
                    protectedSamples.Add((i + .5f) / Mathf.Clamp(recipe.trunkBends, 1, 9));
            if (parent >= 0)
            {
                protectedSamples.Add(.5f);
                if (limbs[parent].level == 0 && !Mathf.Approximately(recipe.Branches.attachmentThickness, 1))
                    protectedSamples.AddRange(new[] { .1f, .2f, .35f });
            }
            int result = TreeMeshSimplifier.Simplify(vertices, normals, uv, sides, samples, protectedSamples,
                Mathf.Clamp(recipe.simplificationTolerance, .0001f, .02f));
            RemovedWoodTriangles += (samples.Count - result) * sides * 2;
            RemovedWoodVertices += (samples.Count - result) * (sides + 1);
            return result;
        }

        internal static void AddSample(List<float> samples, float t)
        {
            if (!samples.Exists(x => Mathf.Abs(x - t) < .00001f)) samples.Add(t);
        }

        internal static List<float> RingSamples(int segments, List<TreeBranchGrowth.Limb> limbs, int parent)
        {
            var samples = new List<float>();
            for (int i = 0; i <= segments; i++) samples.Add((float)i / segments);
            // Keep socket centers exact even when the rest of the curve is simplified.
            foreach (var limb in limbs) if (limb.parentIndex == parent) AddSample(samples, limb.attachment);
            samples.Sort();
            return samples;
        }

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
            var samples = RingSamples(segments, limbs, limbIndex);
            if (limb.level == 0 && !Mathf.Approximately(recipe.Branches.attachmentThickness, 1))
                foreach (float t in new[] { .1f, .2f, .35f }) AddSample(samples, t);
            if (limb.isRoot ? recipe.rootBend > 0 && recipe.rootSharpness > 0 : recipe.Branches.bend > 0 && recipe.Branches.sharpness > 0) AddSample(samples, .5f);
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
                    triangles.AddRange(new[] { a, a + stride, b, b, a + stride, b + stride });
                }
            AddCaps(vertices, normals, uv, triangles, sides, ringCount, points[0], points[points.Length - 1]);
            AddMesh(vertices, triangles, 0, normals, uv);
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
                    if (RootSafeRadius(points, j) >= requested[j]) continue;
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
                    triangles.AddRange(cap == 0 ? new[] { center, a, b } : new[] { center, b, a });
                }
            }
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

        static Texture2D CreateBarkTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Terrainity default bark", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * Mathf.PI * 2, v = (y + .5f) / size * Mathf.PI * 2;
                    // Integer-frequency waves tile in both directions and form long broken furrows.
                    float warp = .65f * Mathf.Sin(v) + .22f * Mathf.Sin(v * 3 + u * 2);
                    float furrow = Mathf.Pow(.5f + .5f * Mathf.Cos(u * 11 + warp), 12);
                    float grain = Mathf.Sin(u * 29 + Mathf.Sin(v * 2)) * .045f;
                    float shade = Mathf.Clamp01(.85f - furrow * .46f + grain + .08f * Mathf.Sin(u * 5 + warp));
                    pixels[y * size + x] = new Color(shade, shade, shade, 1);
                }
            texture.SetPixels(pixels); texture.Apply(true, true);
            return texture;
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
                        combinedRanges.Add(new PickRange { mesh = combined.Count, start = triangleStart, count = count, part = sourceParts[i] });
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
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
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

        // An original, temporary leaf spray so cards work before an artist supplies a texture.
        static Texture2D CreateLeafTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Terrainity default leaf spray", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + .5f) / size, (y + .5f) / size);
                    float coverage = 0;
                    for (int leaf = 0; leaf < 9; leaf++)
                    {
                        float side = leaf % 2 == 0 ? -1 : 1;
                        var center = leaf == 8 ? new Vector2(.5f, .83f) : new Vector2(.5f + side * .17f, .22f + (leaf / 2) * .16f);
                        float angle = leaf == 8 ? 0 : side * -.65f;
                        var d = p - center;
                        float u = d.x * Mathf.Cos(angle) - d.y * Mathf.Sin(angle);
                        float v = d.x * Mathf.Sin(angle) + d.y * Mathf.Cos(angle);
                        float edge = 1 - u * u / (.12f * .12f) - v * v / (.17f * .17f);
                        coverage = Mathf.Max(coverage, Mathf.Clamp01(edge * 14));
                    }
                    if (p.y > .08f && p.y < .87f)
                        coverage = Mathf.Max(coverage, Mathf.Clamp01((.013f - Mathf.Abs(p.x - .5f)) * size));
                    float shade = Mathf.Lerp(.7f, 1, p.y);
                    pixels[y * size + x] = new Color(shade, shade, shade, coverage);
                }
            texture.SetPixels(pixels); texture.Apply(true, false);
            return texture;
        }

        internal static Vector3 CardCorner(Vector3 socket, Vector3 right, Vector3 up, Vector2 uv, Vector2 pivot)
            => socket + right * (2 * (uv.x - pivot.x)) + up * (2 * (uv.y - pivot.y));

        // Original procedural feather spray; external artwork can replace it through Foliage texture.
        static Texture2D CreateNeedleTexture()
        {
            const int size = 256;
            const int width = 128;
            var texture = new Texture2D(width, size, TextureFormat.RGBA32, true)
            { name = "Terrainity needle spray", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[width * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < width; x++)
            {
                float u = (x + .5f) / width, v = (y + .5f) / size;
                float across = Mathf.Abs(u - .5f) * 2;
                float envelope = Mathf.Pow(Mathf.Max(0, Mathf.Sin(v * Mathf.PI)), .6f);
                float along = v - across * .065f;
                float row = Mathf.Repeat(along * 24, 1);
                float edge = Mathf.Min(row, 1 - row);
                // Keep enough alpha coverage for mipmaps at normal tree-preview distances.
                float coverage = Mathf.Clamp01((.44f * (1 - across * .2f) - edge) * 36);
                coverage *= Mathf.Clamp01((envelope - across) * 28) * (along > .025f && along < .975f ? 1 : 0);
                coverage = Mathf.Max(coverage, Mathf.Clamp01((.009f - Mathf.Abs(u - .5f)) * size));
                float shade = Mathf.Lerp(.68f, 1, v) - across * .08f;
                pixels[y * width + x] = new Color(shade, shade, shade, coverage);
            }
            texture.SetPixels(pixels); texture.Apply(true, false);
            return texture;
        }

        void PalmFrond(TreeRecipe recipe, TreeBranchGrowth.Limb limb)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            var points = limb.points;
            float length = 0;
            for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
            var axis = points[points.Length - 1] - points[0];
            var right = Vector3.Cross(axis, Vector3.up).normalized;
            if (right.sqrMagnitude < .01f) right = Vector3.right;
            // Bare petiole at the base, paired leaflets following the curved rachis.
            for (int i = 2; i < points.Length; i++)
            {
                float t = (i - 2f) / (points.Length - 3);
                float width = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .7f) * length * .14f * recipe.foliageSize;
                vertices.Add(points[i] - right * width);
                vertices.Add(points[i] + Vector3.up * width * .25f);
                vertices.Add(points[i] + right * width);
                uv.Add(new Vector2(0, t)); uv.Add(new Vector2(.5f, t)); uv.Add(new Vector2(1, t));
                if (i == 2) continue;
                int n = vertices.Count - 6;
                triangles.AddRange(new[] { n, n + 1, n + 3, n + 1, n + 4, n + 3,
                    n + 1, n + 2, n + 4, n + 2, n + 5, n + 4 });
            }
            var mesh = new Mesh { name = "Terrainity palm frond", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh); materialIndices.Add(1); sourceParts.Add(TreePreviewPart.Foliage);
        }

        void LeafCards(TreeRecipe recipe, Vector3 center, Vector3 branchDirection, float size, int clusterSeed)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var normals = new List<Vector3>(); var uv = new List<Vector2>();
            int cards = Mathf.Clamp(recipe.foliageCards, 2, 12);
            float phase = Range(0, 360);
            float maxSize = Mathf.Clamp(recipe.foliageSize, .25f, 3);
            float minSize = recipe.foliageSizeMin > 0 ? Mathf.Clamp(recipe.foliageSizeMin, .25f, maxSize) : maxSize;
            var sizeRandom = new System.Random(unchecked(clusterSeed ^ 0x5F3759DF));
            for (int i = 0; i < cards; i++)
            {
                float cardSize = minSize == maxSize ? size : size * Mathf.Lerp(minSize / maxSize, 1, (float)sizeRandom.NextDouble());
                // Golden-angle orientations spread cards around the volume for all viewing angles.
                float y = 1 - 2 * (i + .5f) / cards;
                float angle = (phase + i * 137.508f) * Mathf.Deg2Rad;
                float radius = Mathf.Sqrt(1 - y * y);
                var outward = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                var rotation = Quaternion.LookRotation(outward) * Quaternion.AngleAxis(Range(-35, 35), Vector3.forward);
                var offset = outward * cardSize * Mathf.Clamp01(recipe.foliageSpread);
                var pivot = new Vector2(.5f, .5f);
                if (recipe.foliageStemAttached)
                {
                    // Local up follows the branch. All orientation changes rotate around the stem socket.
                    var branchFrame = Quaternion.FromToRotation(Vector3.up, branchDirection);
                    rotation = branchFrame * Quaternion.AngleAxis(phase + i * 360f / cards, Vector3.up)
                        * Quaternion.AngleAxis(Mathf.Clamp(recipe.foliageFanAngle, 0, 85), Vector3.right)
                        * Quaternion.Euler(recipe.foliageRotation);
                    outward = rotation * Vector3.forward;
                    offset = Vector3.zero;
                    pivot = new Vector2(Mathf.Clamp01(recipe.foliageStemPivot.x), Mathf.Clamp01(recipe.foliageStemPivot.y));
                }
                else rotation *= Quaternion.Euler(recipe.foliageRotation);
                var right = rotation * Vector3.right * cardSize * Range(.8f, 1.2f);
                var up = rotation * Vector3.up * cardSize * recipe.Profile.foliageAspect;
                if (recipe.foliageStemAttached)
                {
                    var texture = recipe.foliageTexture != null ? recipe.foliageTexture : defaultFoliage;
                    float aspect = texture != null ? (float)texture.width / texture.height : 1;
                    right = rotation * Vector3.right * cardSize * aspect;
                    up = rotation * Vector3.up * cardSize;
                }
                right *= Mathf.Clamp(recipe.foliageWidthScale, .1f, 3);
                up *= Mathf.Clamp(recipe.foliageHeightScale, .1f, 3);
                int subdivisions = Mathf.Clamp(recipe.foliageSubdivisions, 1, 12);
                for (int segment = 0; segment < subdivisions; segment++)
                {
                    int first = vertices.Count;
                    float bottom = segment / (float)subdivisions, top = (segment + 1f) / subdivisions;
                    foreach (var coordinate in new[] { new Vector2(0, bottom), new Vector2(1, bottom), new Vector2(1, top), new Vector2(0, top) })
                    {
                        float bend = Mathf.Clamp(recipe.foliageBend, -1, 1);
                        var forward = rotation * Vector3.forward;
                        float along = coordinate.y - pivot.y;
                        var position = CardCorner(center + offset, right, up, coordinate, pivot)
                            + forward * (up.magnitude * 2 * bend * along * along);
                        vertices.Add(position); uv.Add(coordinate);
                        var corner = right * (2 * coordinate.x - 1) + up * (2 * coordinate.y - 1);
                        normals.Add((outward * cardSize + corner * .35f - up.normalized * (cardSize * 2 * bend * along)).normalized);
                    }
                    triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                    if (!materials[1].HasProperty("_Cull"))
                        triangles.AddRange(new[] { first + 2, first + 1, first, first + 3, first + 2, first });
                }
            }
            AddMesh(vertices, triangles, 1, normals, uv);
        }
        void AddMesh(List<Vector3> vertices, List<int> triangles, int material, List<Vector3> normals = null, List<Vector2> uv = null)
        {
            var mesh = new Mesh { name = "Terrainity concept preview", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            if (uv != null) mesh.SetUVs(0, uv);
            if (normals == null) mesh.RecalculateNormals(); else mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            meshes.Add(mesh); materialIndices.Add(material); sourceParts.Add(currentPart);
        }

        internal void Orbit(Vector2 delta)
        {
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch + delta.y, -65, 65);
        }

        internal void Pan(Vector2 delta, Vector2 viewport)
        {
            if (viewport.x < 2 || viewport.y < 2) return;
            float unitsPerPoint = 2 * ViewDistance(viewport) * Mathf.Tan(renderer.camera.fieldOfView * Mathf.Deg2Rad * .5f) / viewport.y;
            panOffset += Quaternion.Euler(pitch, yaw, 0) * new Vector3(-delta.x, delta.y, 0) * unitsPerPoint;
        }

        internal void Zoom(float delta) { zoom = Mathf.Clamp(zoom + delta * .035f, .23f, 2); }

        float ViewDistance(Vector2 viewport)
        {
            float halfFov = renderer.camera.fieldOfView * Mathf.Deg2Rad * .5f;
            float effectiveFov = Mathf.Atan(Mathf.Tan(halfFov) * Mathf.Min(1, viewport.x / viewport.y));
            return Mathf.Max(bounds.extents.magnitude, 1) / Mathf.Sin(effectiveFov) * 1.08f * zoom;
        }

        internal void Draw(Rect rect)
        {
            if (rect.width < 2 || rect.height < 2 || Event.current.type != EventType.Repaint) return;
            if (materials[0] == null) { GUI.Label(rect, "No compatible preview shader found."); return; }
            var camera = renderer.camera;
            float radius = Mathf.Max(bounds.extents.magnitude, 1);
            float distance = ViewDistance(rect.size);
            renderer.BeginPreview(rect, GUIStyle.none);
            var orbit = Quaternion.Euler(pitch, yaw, 0);
            var target = bounds.center + panOffset;
            camera.transform.SetPositionAndRotation(target + orbit * new Vector3(0, 0, -distance), orbit);
            camera.nearClipPlane = .01f; camera.farClipPlane = distance + radius * 4 + panOffset.magnitude;
            for (int i = 0; i < meshes.Count; i++) renderer.DrawMesh(meshes[i], Matrix4x4.identity, materials[materialIndices[i]], 0);
            DrawGrid();
            DrawMeshOverlay();
            DrawSelectionHighlight();
            renderer.Render(true);
            GUI.DrawTexture(rect, renderer.EndPreview(), ScaleMode.StretchToFill, false);
            DrawCompass(rect);
        }

        internal Texture2D ReferenceSnapshot(int size = 512)
        {
            var rect = new Rect(0, 0, size, size);
            float distance = ViewDistance(rect.size);
            renderer.BeginStaticPreview(rect);
            var orbit = Quaternion.Euler(10, 35, 0);
            renderer.camera.transform.SetPositionAndRotation(bounds.center + orbit * new Vector3(0, 0, -distance), orbit);
            renderer.camera.nearClipPlane = .01f;
            renderer.camera.farClipPlane = distance + bounds.extents.magnitude * 4;
            for (int i = 0; i < meshes.Count; i++) renderer.DrawMesh(meshes[i], Matrix4x4.identity, materials[materialIndices[i]], 0);
            DrawGrid();
            DrawMeshOverlay();
            DrawSelectionHighlight();
            renderer.Render(true);
            return renderer.EndStaticPreview();
        }

        void DrawMeshOverlay()
        {
            if (!ShowMeshOverlay) return;
            if (wireMaterial == null)
            {
                var shader = Shader.Find("Hidden/Terrainity/Preview Wireframe");
                if (shader == null) return;
                wireMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (wireMeshes.Count == 0)
                foreach (var source in meshes)
                {
                    var edges = new HashSet<ulong>();
                    var indices = new List<int>();
                    void Edge(int a, int b)
                    {
                        int low = Mathf.Min(a, b), high = Mathf.Max(a, b);
                        ulong key = ((ulong)(uint)low << 32) | (uint)high;
                        if (edges.Add(key)) { indices.Add(a); indices.Add(b); }
                    }
                    var triangles = source.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                    { Edge(triangles[i], triangles[i + 1]); Edge(triangles[i + 1], triangles[i + 2]); Edge(triangles[i + 2], triangles[i]); }
                    var wire = new Mesh { name = "Terrainity preview edges", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                    wire.vertices = source.vertices;
                    wire.SetIndices(indices, MeshTopology.Lines, 0);
                    wire.bounds = source.bounds;
                    wireMeshes.Add(wire);
                }
            foreach (var mesh in wireMeshes) renderer.DrawMesh(mesh, Matrix4x4.identity, wireMaterial, 0);
        }

        void DrawSelectionHighlight()
        {
            if (selectionWire == null || EditorApplication.timeSinceStartup >= selectionExpires) return;
            if (wireMaterial == null)
            {
                var shader = Shader.Find("Hidden/Terrainity/Preview Wireframe");
                if (shader == null) return;
                wireMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            renderer.DrawMesh(selectionWire, Matrix4x4.identity, wireMaterial, 0);
        }

        internal TreePreviewPart PickTreePart(Vector2 position, Vector2 viewport)
        {
            if (viewport.x < 2 || viewport.y < 2 || pickRanges.Count == 0) return TreePreviewPart.None;
            float halfHeight = Mathf.Tan(renderer.camera.fieldOfView * Mathf.Deg2Rad * .5f);
            Vector3 direction = renderer.camera.transform.rotation * new Vector3(
                (position.x / viewport.x * 2 - 1) * halfHeight * viewport.x / viewport.y,
                (1 - position.y / viewport.y * 2) * halfHeight, 1);
            var ray = new Ray(renderer.camera.transform.position, direction.normalized);
            float nearest = float.PositiveInfinity;
            PickRange selected = default;
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                var mesh = meshes[meshIndex];
                if (!mesh.bounds.IntersectRay(ray)) continue;
                var vertices = mesh.vertices; var triangles = mesh.triangles; var uv = mesh.uv;
                for (int triangle = 0; triangle < triangles.Length / 3; triangle++)
                {
                    int offset = triangle * 3, a = triangles[offset], b = triangles[offset + 1], c = triangles[offset + 2];
                    Vector3 e1 = vertices[b] - vertices[a], e2 = vertices[c] - vertices[a];
                    Vector3 p = Vector3.Cross(ray.direction, e2);
                    float determinant = Vector3.Dot(e1, p);
                    if (Mathf.Abs(determinant) < .0000001f) continue;
                    float inverse = 1 / determinant;
                    Vector3 t = ray.origin - vertices[a];
                    float u = Vector3.Dot(t, p) * inverse;
                    if (u < 0 || u > 1) continue;
                    Vector3 q = Vector3.Cross(t, e1);
                    float v = Vector3.Dot(ray.direction, q) * inverse;
                    if (v < 0 || u + v > 1) continue;
                    float distance = Vector3.Dot(e2, q) * inverse;
                    if (distance <= 0 || distance >= nearest) continue;
                    foreach (var range in pickRanges)
                    {
                        if (range.mesh != meshIndex || triangle < range.start || triangle >= range.start + range.count) continue;
                        if (range.part == TreePreviewPart.Foliage && uv.Length == vertices.Length)
                        {
                            var texture = materials[1].mainTexture as Texture2D;
                            if (texture != null && texture.isReadable)
                            {
                                Vector2 coordinate = uv[a] * (1-u-v) + uv[b] * u + uv[c] * v;
                                if (texture.GetPixelBilinear(coordinate.x, coordinate.y).a < materials[1].GetFloat("_Cutoff")) break;
                            }
                        }
                        nearest = distance; selected = range; break;
                    }
                }
            }
            if (selected.part == TreePreviewPart.None) return TreePreviewPart.None;
            Highlight(selected);
            return selected.part;
        }

        void Highlight(PickRange range)
        {
            if (selectionWire != null) UnityEngine.Object.DestroyImmediate(selectionWire);
            var source = meshes[range.mesh]; var triangles = source.triangles;
            var edges = new HashSet<ulong>(); var lines = new List<int>();
            void Edge(int a, int b)
            {
                int low = Mathf.Min(a,b), high = Mathf.Max(a,b);
                ulong key = ((ulong)(uint)low << 32) | (uint)high;
                if (edges.Add(key)) { lines.Add(a); lines.Add(b); }
            }
            for (int i = range.start * 3; i < (range.start + range.count) * 3; i += 3)
            { Edge(triangles[i], triangles[i+1]); Edge(triangles[i+1], triangles[i+2]); Edge(triangles[i+2], triangles[i]); }
            selectionWire = new Mesh { name = "Terrainity selected preview part", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            selectionWire.vertices = source.vertices;
            selectionWire.SetIndices(lines, MeshTopology.Lines, 0); selectionWire.bounds = source.bounds;
            selectionExpires = EditorApplication.timeSinceStartup + 1;
        }

        void ClearMeshes()
        {
            if (selectionWire != null) UnityEngine.Object.DestroyImmediate(selectionWire);
            selectionWire = null;
            foreach (var mesh in wireMeshes) UnityEngine.Object.DestroyImmediate(mesh);
            wireMeshes.Clear();
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            meshes.Clear(); materialIndices.Clear(); sourceParts.Clear(); pickRanges.Clear();
        }
        public void Dispose()
        {
            ClearMeshes();
            if (wireMaterial != null) UnityEngine.Object.DestroyImmediate(wireMaterial);
            if (gridMesh != null) UnityEngine.Object.DestroyImmediate(gridMesh);
            if (gridMaterial != null) UnityEngine.Object.DestroyImmediate(gridMaterial);
            foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (defaultFoliage != null) UnityEngine.Object.DestroyImmediate(defaultFoliage);
            if (defaultBark != null) UnityEngine.Object.DestroyImmediate(defaultBark);
            foreach (var ramp in tintRamps) if (ramp != null) UnityEngine.Object.DestroyImmediate(ramp);
            renderer.Cleanup();
        }
    }
}

