using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    // Deterministic foliage cards and curved palm fronds.
    internal sealed partial class TreePreview
    {
        internal static Vector3 CardCorner(Vector3 socket, Vector3 right, Vector3 up, Vector2 uv, Vector2 pivot)
            => socket + right * (2 * (uv.x - pivot.x)) + up * (2 * (uv.y - pivot.y));

        void PalmFrond(TreeRecipe recipe, Vector3[] points, int cardId, int limbIndex)
        {
            var vertices = new List<Vector3>(); var uv = new List<Vector2>(); var triangles = new List<int>();
            var wind = new List<Vector4>();
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
                float weight = treeWindRig.BranchWeight(limbIndex, ArcLength(points, i / (float)(points.Length - 1)));
                float phase = Mathf.Repeat(cardId * .6180339f, 1);
                wind.Add(new Vector4(weight, t * t, phase, cardId + 1));
                wind.Add(new Vector4(weight, 0, phase, cardId + 1));
                wind.Add(new Vector4(weight, t * t, phase, cardId + 1));
                if (i == 2) continue;
                int n = vertices.Count - 6;
                TerrainityMeshUtility.AddTriangle(triangles, n, n + 1, n + 3);
                TerrainityMeshUtility.AddTriangle(triangles, n + 1, n + 4, n + 3);
                TerrainityMeshUtility.AddTriangle(triangles, n + 1, n + 2, n + 4);
                TerrainityMeshUtility.AddTriangle(triangles, n + 2, n + 5, n + 4);
                if (!recipe.foliageBackfaceCulling && !materials[1].HasProperty("_Cull"))
                {
                    TerrainityMeshUtility.AddTriangle(triangles, n + 3, n + 1, n);
                    TerrainityMeshUtility.AddTriangle(triangles, n + 3, n + 4, n + 1);
                    TerrainityMeshUtility.AddTriangle(triangles, n + 4, n + 2, n + 1);
                    TerrainityMeshUtility.AddTriangle(triangles, n + 4, n + 5, n + 2);
                }
            }
            var mesh = new Mesh { name = "Terrainity palm frond", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(3, wind);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            meshes.Add(mesh); materialIndices.Add(1); sourceParts.Add(TreePreviewPart.Foliage);
            sourceCardRanges[mesh] = new List<PickRange> { new PickRange { start = 0, count = triangles.Count / 3, cardId = cardId } };
        }

        void LeafCards(TreeRecipe recipe, Vector3 center, Vector3 branchDirection, float size, int clusterSeed,
            int limbIndex, int rowIndex, float socketWeight, bool aligned = false)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var wind = new List<Vector4>();
            var cardsForPicking = new List<PickRange>();
            int cards = aligned ? Mathf.Clamp(recipe.foliageAlignSides, 1, 3) : Mathf.Clamp(recipe.foliageCards, 2, 12);
            float phase = Range(0, 360);
            float maxSize = Mathf.Clamp(recipe.foliageSize, .25f, 3);
            float minSize = recipe.foliageSizeMin > 0 ? Mathf.Clamp(recipe.foliageSizeMin, .25f, maxSize) : maxSize;
            var sizeRandom = new System.Random(unchecked(clusterSeed ^ 0x5F3759DF));
            var offsetRandom = new System.Random(unchecked(clusterSeed ^ 0x3243F6A9));
            for (int i = 0; i < cards; i++)
            {
                int cardId = (limbIndex * 12 + rowIndex) * 12 + i;
                bool hidden = prunedCards.Contains(cardId);
                int firstTriangle = triangles.Count / 3;
                int firstVertex = vertices.Count;
                float cardSize = minSize == maxSize ? size : size * Mathf.Lerp(minSize / maxSize, 1, (float)sizeRandom.NextDouble());
                // Golden-angle orientations spread cards around the volume for all viewing angles.
                float y = 1 - 2 * (i + .5f) / cards;
                float angle = (phase + i * 137.508f) * Mathf.Deg2Rad;
                float radius = Mathf.Sqrt(1 - y * y);
                var outward = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                var rotation = Quaternion.LookRotation(outward) * Quaternion.AngleAxis(Range(-35, 35), Vector3.forward);
                var offset = outward * cardSize * Mathf.Clamp01(recipe.foliageSpread);
                var pivot = new Vector2(.5f, .5f);
                if (aligned)
                {
                    // The card's bottom centre sits on the stem. Its long axis points
                    // radially away from the branch; the sides divide a full turn evenly.
                    var axis = branchDirection.sqrMagnitude > .000001f ? branchDirection.normalized : Vector3.up;
                    var radial = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
                    if (radial.sqrMagnitude < .000001f) radial = Vector3.ProjectOnPlane(Vector3.forward, axis).normalized;
                    radial = Quaternion.AngleAxis(i * 360f / cards, axis) * radial;
                    rotation = Quaternion.LookRotation(Vector3.Cross(axis, radial), radial)
                        * Quaternion.Euler(recipe.foliageRotation);
                    float low = Mathf.Clamp(recipe.foliageAlignOffsetMin, -.5f, .5f);
                    float high = Mathf.Clamp(recipe.foliageAlignOffsetMax, low, .5f);
                    // Alternate along each side of the branch, so no side is
                    // permanently shifted relative to the other sides.
                    offset = rowIndex % 2 == 1
                        ? Vector3.up * Mathf.Lerp(low, high, (float)offsetRandom.NextDouble()) : Vector3.zero;
                    pivot = new Vector2(.5f, 0);
                }
                else if (recipe.foliageStemAttached)
                {
                    // Local up follows the branch. All orientation changes rotate around the stem socket.
                    var branchFrame = Quaternion.FromToRotation(Vector3.up, branchDirection);
                    rotation = branchFrame * Quaternion.AngleAxis(phase + i * 360f / cards, Vector3.up)
                        * Quaternion.AngleAxis(Mathf.Clamp(recipe.foliageFanAngle, 0, 85), Vector3.right)
                        * Quaternion.Euler(recipe.foliageRotation);
                    offset = Vector3.zero;
                    pivot = new Vector2(Mathf.Clamp01(recipe.foliageStemPivot.x), Mathf.Clamp01(recipe.foliageStemPivot.y));
                }
                else rotation *= Quaternion.Euler(recipe.foliageRotation);
                var right = rotation * Vector3.right * cardSize * Range(.8f, 1.2f);
                var up = rotation * Vector3.up * cardSize * recipe.Profile.foliageAspect;
                if (aligned || recipe.foliageStemAttached)
                {
                    var texture = recipe.foliageTexture != null ? recipe.foliageTexture : defaultFoliage;
                    float aspect = texture != null ? (float)texture.width / texture.height : 1;
                    right = rotation * Vector3.right * cardSize * aspect;
                    up = rotation * Vector3.up * cardSize;
                }
                right *= Mathf.Clamp(recipe.foliageWidthScale, .1f, 3);
                up *= Mathf.Clamp(recipe.foliageHeightScale, .1f, 3);
                // Opposing aligned cards follow the same world-space sweep.
                float bendSide = aligned && i > 0 ? -1f : 1f;
                int subdivisions = Mathf.Clamp(recipe.foliageSubdivisions, 1, 12);
                for (int segment = 0; segment < subdivisions; segment++)
                {
                    int first = vertices.Count;
                    float bottom = segment / (float)subdivisions, top = (segment + 1f) / subdivisions;
                    foreach (var coordinate in new[] { new Vector2(0, bottom), new Vector2(1, bottom), new Vector2(1, top), new Vector2(0, top) })
                    {
                        float bend = Mathf.Clamp(recipe.foliageBend, -1, 1);
                        float along = coordinate.y - pivot.y;
                        float arc = bend * 1.8f;
                        float arcAngle = arc * along;
                        float length = up.magnitude * 2;
                        float radialDistance = Mathf.Abs(arc) < .0001f ? length * along : length * Mathf.Sin(arcAngle) / arc;
                        float depth = Mathf.Abs(arc) < .0001f ? 0 : length * (1 - Mathf.Cos(arcAngle)) / arc;
                        var forward = rotation * Vector3.forward;
                        var position = CardCorner(center + offset, right, up, coordinate, pivot)
                            + up.normalized * (radialDistance - length * along) + forward * (depth * bendSide);
                        vertices.Add(position); uv.Add(coordinate);
                        float flutter = Mathf.Abs(coordinate.y - pivot.y) / Mathf.Max(pivot.y, 1 - pivot.y);
                        wind.Add(new Vector4(socketWeight, flutter * flutter, Mathf.Repeat(cardId * .6180339f, 1), cardId + 1));
                        var corner = right * (2 * coordinate.x - 1) + up * (2 * coordinate.y - 1);
                        var curvedNormal = forward * Mathf.Cos(arcAngle) - up.normalized * (Mathf.Sin(arcAngle) * bendSide);
                        normals.Add((curvedNormal * cardSize + corner * .35f).normalized);
                    }
                    TerrainityMeshUtility.AddTriangle(triangles, first, first + 1, first + 2);
                    TerrainityMeshUtility.AddTriangle(triangles, first, first + 2, first + 3);
                    if (!recipe.foliageBackfaceCulling && !materials[1].HasProperty("_Cull"))
                    {
                        TerrainityMeshUtility.AddTriangle(triangles, first + 2, first + 1, first);
                        TerrainityMeshUtility.AddTriangle(triangles, first + 3, first + 2, first);
                    }
                }
                if (hidden)
                {
                    triangles.RemoveRange(firstTriangle * 3, triangles.Count - firstTriangle * 3);
                    int addedVertices = vertices.Count - firstVertex;
                    vertices.RemoveRange(firstVertex, addedVertices);
                    normals.RemoveRange(firstVertex, addedVertices);
                    uv.RemoveRange(firstVertex, addedVertices);
                    wind.RemoveRange(firstVertex, addedVertices);
                }
                else cardsForPicking.Add(new PickRange { start = firstTriangle, count = triangles.Count / 3 - firstTriangle, cardId = cardId });
            }
            if (triangles.Count == 0) return;
            var cardMesh = AddMesh(vertices, triangles, 1, normals, uv);
            cardMesh.SetUVs(3, wind);
            sourceCardRanges[cardMesh] = cardsForPicking;
        }
    }
}
