using System;
using System.Collections.Generic;
using UnityEngine;

namespace Terrainity.Editor
{
    // Measure bend continuously from the trunk through every parent socket.
    internal sealed class TreeWindRig
    {
        readonly float[] pathStarts;
        readonly float[] trunkWeights;
        readonly float inversePathLength;
        readonly float inverseHeight;

        internal TreeWindRig(List<TreeBranchGrowth.Limb> limbs, float height)
        {
            inverseHeight = 1 / Mathf.Max(height, .0001f);
            pathStarts = new float[limbs.Count];
            trunkWeights = new float[limbs.Count];
            float longest = .0001f;
            for (int i = 0; i < limbs.Count; i++)
            {
                var limb = limbs[i];
                if (limb.parentIndex >= 0)
                {
                    pathStarts[i] = pathStarts[limb.parentIndex]
                        + TreePreview.ArcLength(limbs[limb.parentIndex].points, limb.attachment);
                    trunkWeights[i] = trunkWeights[limb.parentIndex];
                }
                else trunkWeights[i] = TrunkWeight(limb.points[0]);
                longest = Mathf.Max(longest, pathStarts[i] + TreePreview.ArcLength(limb.points, 1));
            }
            inversePathLength = 1 / longest;
        }

        internal float TrunkWeight(Vector3 position)
        {
            float height = Mathf.Clamp01(position.y * inverseHeight);
            return .12f * height * height;
        }

        internal float BranchWeight(int index, float distance)
            => trunkWeights[index] + .42f * Mathf.Clamp01((pathStarts[index] + distance) * inversePathLength);
    }

    internal sealed partial class TreePreview
    {
        TreeWindRig treeWindRig;

        static Vector3 PositionAtDistance(Vector3[] points, float distance)
        {
            for (int i = 1; i < points.Length; i++)
            {
                float length = Vector3.Distance(points[i - 1], points[i]);
                if (distance <= length)
                    return Vector3.Lerp(points[i - 1], points[i], distance / Mathf.Max(length, .0001f));
                distance -= length;
            }
            return points[points.Length - 1];
        }

        void SetWoodWind(Mesh mesh, Vector3[] points, int sides, int rings, Func<Vector3, float, float> weight)
        {
            int stride = sides + 1;
            var uv = mesh.uv;
            var data = new List<Vector4>(mesh.vertexCount);
            for (int ring = 0; ring < rings; ring++)
            {
                float distance = uv[ring * stride].y;
                var position = PositionAtDistance(points, distance);
                var wind = new Vector4(weight(position, distance), 0, 0, 0);
                for (int side = 0; side <= sides; side++) data.Add(wind);
            }
            // Caps have planar texture UVs. Copy their ring's wind rather than interpreting those UVs as length.
            for (int cap = 0; cap < 2; cap++)
            {
                var wind = data[cap == 0 ? 0 : (rings - 1) * stride];
                for (int side = 0; side <= sides; side++) data.Add(wind);
            }
            mesh.SetUVs(3, data);
        }
    }
}
