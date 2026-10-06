using UnityEngine;

namespace Terrainity.Editor
{
    // Original procedural bark, broadleaf and needle masks owned by the preview.
    internal sealed partial class TreePreview
    {
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

    }
}
