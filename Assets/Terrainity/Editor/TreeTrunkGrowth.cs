using UnityEngine;

namespace Terrainity.Editor
{
    internal sealed class TreeTrunkGrowth
    {
        internal const int Segments = 48;
        internal readonly Vector3[] points = new Vector3[Segments + 1];
        internal readonly Quaternion[] frames = new Quaternion[Segments + 1];

        internal TreeTrunkGrowth(TreeRecipe recipe, Vector3 tip)
        {
            float bend = Mathf.Clamp(recipe.trunkBend, -1, 1) * tip.y * .12f;
            int bends = Mathf.Clamp(recipe.trunkBends, 1, 9);
            float sharpness = Mathf.Clamp01(recipe.trunkSharpness);
            float twist = Mathf.Clamp(recipe.trunkTwist, -720, 720);
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                float smooth = Mathf.Sin(t * Mathf.PI * bends);
                float angular = Mathf.Asin(Mathf.Clamp(smooth, -1, 1)) * (2 / Mathf.PI);
                float offset = i == 0 || i == Segments ? 0 : Mathf.Lerp(smooth, angular, sharpness) * bend;
                points[i] = tip * t + Quaternion.AngleAxis(twist * t, Vector3.up) * Vector3.right * offset;
            }
            // Transport the frame along the curve, avoiding sudden flips in the tube's rings.
            Quaternion frame = Quaternion.FromToRotation(Vector3.up, Tangent(0));
            Vector3 previous = Tangent(0);
            for (int i = 0; i <= Segments; i++)
            {
                Vector3 tangent = Tangent(i);
                frame = Quaternion.FromToRotation(previous, tangent) * frame;
                frames[i] = frame * Quaternion.AngleAxis(twist * i / Segments, Vector3.up);
                previous = tangent;
            }
        }

        Vector3 Tangent(int ring) =>
            (points[Mathf.Min(ring + 1, Segments)] - points[Mathf.Max(ring - 1, 0)]).normalized;

        internal void Sample(float t, out Vector3 position, out Quaternion frame)
        {
            float ring = Mathf.Clamp01(t) * Segments;
            int lower = Mathf.Min(Mathf.FloorToInt(ring), Segments - 1);
            float fraction = ring - lower;
            position = Vector3.Lerp(points[lower], points[lower + 1], fraction);
            frame = Quaternion.Slerp(frames[lower], frames[lower + 1], fraction);
        }
    }
}
