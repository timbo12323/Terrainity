using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Terrainity.Editor
{
    // Preview camera, rendering, picking and temporary resource cleanup.
    internal sealed partial class TreePreview
    {
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

        internal void Zoom(float delta)
        {
            zoom = allowFarZoom
                ? Mathf.Clamp(zoom * Mathf.Pow(1.12f, delta), .23f, 32)
                : Mathf.Clamp(zoom + delta * .035f, .23f, 2);
        }

        internal float ScreenHeightFraction(Vector2 viewport)
        {
            if (viewport.x < 2 || viewport.y < 2) return 1;
            var viewBounds = lodSubmeshes > 0 ? lodReferenceBounds : bounds;
            return viewBounds.size.y / (2 * ViewDistance(viewport) * Mathf.Tan(renderer.camera.fieldOfView * Mathf.Deg2Rad * .5f));
        }

        float ViewDistance(Vector2 viewport)
        {
            float halfFov = renderer.camera.fieldOfView * Mathf.Deg2Rad * .5f;
            float effectiveFov = Mathf.Atan(Mathf.Tan(halfFov) * Mathf.Min(1, viewport.x / viewport.y));
            var viewBounds = lodSubmeshes > 0 ? lodReferenceBounds : bounds;
            return Mathf.Max(viewBounds.extents.magnitude, grassStudy ? .02f : 1) / Mathf.Sin(effectiveFov) * 1.08f * zoom;
        }

        internal void Draw(Rect rect)
        {
            if (rect.width < 2 || rect.height < 2 || Event.current.type != EventType.Repaint) return;
            if (materials[0] == null) { GUI.Label(rect, "No compatible preview shader found."); return; }
            AdvanceWindClock();
            foreach (var material in materials) ApplyWind(material, true);
            var camera = renderer.camera;
            var viewBounds = lodSubmeshes > 0 ? lodReferenceBounds : bounds;
            float radius = Mathf.Max(viewBounds.extents.magnitude, 1);
            float distance = ViewDistance(rect.size);
            renderer.BeginPreview(rect, GUIStyle.none);
            var orbit = Quaternion.Euler(pitch, yaw, 0);
            var target = viewBounds.center + panOffset;
            camera.transform.SetPositionAndRotation(target + orbit * new Vector3(0, 0, -distance), orbit);
            camera.nearClipPlane = .01f; camera.farClipPlane = distance + radius * 4 + panOffset.magnitude;
            if (lodSubmeshes > 0)
                for (int i = 0; i < lodSubmeshes; i++) renderer.DrawMesh(meshes[0], Matrix4x4.identity, materials[i], i);
            else for (int i = 0; i < meshes.Count; i++) renderer.DrawMesh(meshes[i], Matrix4x4.identity, materials[materialIndices[i]], 0);
            DrawGrid();
            DrawMeshOverlay();
            DrawSelectionHighlight();
            renderer.Render(true);
            GUI.DrawTexture(rect, renderer.EndPreview(), ScaleMode.StretchToFill, false);
            DrawCompass(rect);
        }

        internal Texture2D ReferenceSnapshot(int size = 512)
        {
            foreach (var material in materials) ApplyWind(material, false);
            var rect = new Rect(0, 0, size, size);
            float distance = ViewDistance(rect.size);
            renderer.BeginStaticPreview(rect);
            var orbit = Quaternion.Euler(10, 35, 0);
            var viewBounds = lodSubmeshes > 0 ? lodReferenceBounds : bounds;
            renderer.camera.transform.SetPositionAndRotation(viewBounds.center + orbit * new Vector3(0, 0, -distance), orbit);
            renderer.camera.nearClipPlane = .01f;
            renderer.camera.farClipPlane = distance + viewBounds.extents.magnitude * 4;
            if (lodSubmeshes > 0)
                for (int i = 0; i < lodSubmeshes; i++) renderer.DrawMesh(meshes[0], Matrix4x4.identity, materials[i], i);
            else for (int i = 0; i < meshes.Count; i++) renderer.DrawMesh(meshes[i], Matrix4x4.identity, materials[materialIndices[i]], 0);
            DrawGrid();
            DrawMeshOverlay(false);
            DrawSelectionHighlight(false);
            renderer.Render(true);
            return renderer.EndStaticPreview();
        }

        void DrawMeshOverlay(bool animated = true)
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
                    var weights = new List<Vector4>(); source.GetUVs(3, weights);
                    if (weights.Count == source.vertexCount) wire.SetUVs(3, weights);
                    var roots = new List<Vector4>(); source.GetUVs(4, roots);
                    if (roots.Count == source.vertexCount) wire.SetUVs(4, roots);
                    wire.SetIndices(indices, MeshTopology.Lines, 0);
                    wire.bounds = source.bounds;
                    wireMeshes.Add(wire);
                }
            ApplyWind(wireMaterial, animated);
            foreach (var mesh in wireMeshes) renderer.DrawMesh(mesh, Matrix4x4.identity, wireMaterial, 0);
        }

        void DrawSelectionHighlight(bool animated = true)
        {
            if (selectionWire == null || EditorApplication.timeSinceStartup >= selectionExpires) return;
            if (wireMaterial == null)
            {
                var shader = Shader.Find("Hidden/Terrainity/Preview Wireframe");
                if (shader == null) return;
                wireMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            ApplyWind(wireMaterial, animated);
            renderer.DrawMesh(selectionWire, Matrix4x4.identity, wireMaterial, 0);
        }

        internal TreePreviewPart PickTreePart(Vector2 position, Vector2 viewport)
        {
            var part = PickTreeItem(position, viewport, out _, out _, true);
            return part;
        }

        internal bool TryPickFoliageCard(Vector2 position, Vector2 viewport, out int cardId)
            => PickTreeItem(position, viewport, out cardId, out _, false) == TreePreviewPart.Foliage && cardId >= 0;

        internal TreePreviewPart PickPruneTarget(Vector2 position, Vector2 viewport, out int cardId, out int limbIndex)
            => PickTreeItem(position, viewport, out cardId, out limbIndex, false);

        TreePreviewPart PickTreeItem(Vector2 position, Vector2 viewport, out int cardId, out int limbIndex, bool highlight)
        {
            cardId = -1;
            limbIndex = -1;
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
                var pickBounds = mesh.bounds;
                bool animated = windEnabled && wind != null && wind.speed > 0;
                if (animated) pickBounds.Expand(3);
                if (!pickBounds.IntersectRay(ray)) continue;
                var vertices = mesh.vertices; var triangles = mesh.triangles; var uv = mesh.uv;
                if (animated)
                {
                    var weights = new List<Vector4>(); mesh.GetUVs(3, weights);
                    if (weights.Count == vertices.Length)
                        for (int i = 0; i < vertices.Length; i++) vertices[i] = WindPosition(vertices[i], weights[i]);
                }
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
                    int rangeIndex = FindPickRange(meshIndex, triangle);
                    if (rangeIndex < 0) continue;
                    var range = pickRanges[rangeIndex];
                    if (range.part == TreePreviewPart.Foliage && uv.Length == vertices.Length)
                    {
                        var texture = materials[1].mainTexture as Texture2D;
                        if (texture != null && texture.isReadable)
                        {
                            Vector2 coordinate = uv[a] * (1-u-v) + uv[b] * u + uv[c] * v;
                            if (texture.GetPixelBilinear(coordinate.x, coordinate.y).a < materials[1].GetFloat("_Cutoff")) continue;
                        }
                    }
                    nearest = distance; selected = range;
                }
            }
            if (selected.part == TreePreviewPart.None) return TreePreviewPart.None;
            cardId = selected.cardId;
            limbIndex = selected.limbIndex;
            if (highlight) Highlight(selected);
            return selected.part;
        }

        int FindPickRange(int mesh, int triangle)
        {
            // Combining preserves ranges ordered by mesh and triangle start.
            int low = 0, high = pickRanges.Count - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var range = pickRanges[middle];
                if (range.mesh < mesh || (range.mesh == mesh && triangle >= range.start + range.count)) low = middle + 1;
                else if (range.mesh > mesh || triangle < range.start) high = middle - 1;
                else return middle;
            }
            return -1;
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
            var weights = new List<Vector4>(); source.GetUVs(3, weights);
            if (weights.Count == source.vertexCount) selectionWire.SetUVs(3, weights);
            selectionWire.SetIndices(lines, MeshTopology.Lines, 0); selectionWire.bounds = source.bounds;
            selectionExpires = EditorApplication.timeSinceStartup + 1;
        }

        void ClearMeshes()
        {
            lodSubmeshes = 0;
            if (selectionWire != null) UnityEngine.Object.DestroyImmediate(selectionWire);
            selectionWire = null;
            foreach (var mesh in wireMeshes) UnityEngine.Object.DestroyImmediate(mesh);
            wireMeshes.Clear();
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            meshes.Clear(); materialIndices.Clear(); sourceParts.Clear(); pickRanges.Clear();
            sourceCardRanges.Clear(); sourceBranchLimbs.Clear();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ClearMeshes();
            branchCards.Clear(); childLimbs.Clear();
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
