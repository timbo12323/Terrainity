using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    // Simplify generated tubes before emitting triangles; keep their UV seam and sockets.
    internal static class TreeMeshSimplifier
    {
        internal static int Simplify(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            int sides, List<float> samples, List<float> protectedSamples, float tolerance, float normalTolerance = .015f)
        {
            int stride = sides + 1, count = samples.Count;
            if (count <= 2) return count;
            float limit = Mathf.Max(.00001f, tolerance);
            var keep = new bool[count]; keep[0] = keep[count - 1] = true;
            foreach (float t in protectedSamples)
                for (int i = 0; i < count; i++)
                    if (Mathf.Abs(samples[i] - t) < .00001f) keep[i] = true;

            void Reduce(int first, int last)
            {
                if (last - first <= 1) return;
                int split = -1; float worst = 1;
                float start = uv[first * stride].y, end = uv[last * stride].y;
                for (int ring = first + 1; ring < last; ring++)
                {
                    float t = Mathf.InverseLerp(start, end, uv[ring * stride].y);
                    for (int side = 0; side < sides; side++)
                    {
                        int a = first * stride + side, b = last * stride + side, v = ring * stride + side;
                        float error = Vector3.Distance(vertices[v], Vector3.Lerp(vertices[a], vertices[b], t)) / limit;
                        // Preserve shading transitions as well as vertex positions.
                        var interpolatedNormal = Vector3.Lerp(normals[a], normals[b], t).normalized;
                        error = Mathf.Max(error, (1 - Vector3.Dot(normals[v], interpolatedNormal)) / normalTolerance);
                        if (error > worst) { worst = error; split = ring; }
                    }
                }
                if (split < 0) return;
                keep[split] = true; Reduce(first, split); Reduce(split, last);
            }
            int previous = 0;
            for (int i = 1; i < count; i++)
                if (keep[i]) { Reduce(previous, i); previous = i; }

            int output = 0;
            for (int ring = 0; ring < count; ring++)
            {
                if (!keep[ring]) continue;
                for (int side = 0; side <= sides; side++)
                {
                    int source = ring * stride + side, target = output * stride + side;
                    vertices[target] = vertices[source]; normals[target] = normals[source]; uv[target] = uv[source];
                }
                output++;
            }
            vertices.RemoveRange(output * stride, vertices.Count - output * stride);
            normals.RemoveRange(output * stride, normals.Count - output * stride);
            uv.RemoveRange(output * stride, uv.Count - output * stride);
            return output;
        }
    }
}
