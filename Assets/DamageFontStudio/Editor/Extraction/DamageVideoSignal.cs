using System;
using System.Collections.Generic;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // A signed, color-selective high-pass template. Unlike thresholded bounding boxes,
    // its least-squares amplitude continues to measure a number as it fades.
    internal static class DamageVideoSignal
    {
        internal const int Columns = 24, Rows = 12;
        static Color32[] cachedPixels;
        static float[][] maps;
        static readonly int[] Radii = { 1, 2, 4, 8 };
        static int cachedWidth, cachedHeight;
        static void Prepare(Color32[] pixels, int w, int h)
        {
            if (ReferenceEquals(pixels, cachedPixels)) return;
            cachedPixels = pixels;
            if (maps == null || cachedWidth != w || cachedHeight != h)
            { maps = new float[8][]; for (int i = 0; i < maps.Length; i++) maps[i] = new float[w * h]; cachedWidth = w; cachedHeight = h; }
            var value = new float[w * h];
            for (int family = 0; family < 2; family++)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    var c = pixels[(h - 1 - i / w) * w + i % w];
                    value[i] = family == 0 ? Mathf.Min(c.r, c.g, c.b) / 255f : (c.r + c.b - 2f * c.g) / 510f;
                }
                for (int r = 0; r < Radii.Length; r++)
                    for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    {
                        int d = Radii[r], index = y * w + x;
                        maps[family * 4 + r][index] = value[index] - .25f * (value[y * w + Math.Max(0, x - d)] + value[y * w + Math.Min(w - 1, x + d)] +
                            value[Math.Max(0, y - d) * w + x] + value[Math.Min(h - 1, y + d) * w + x]);
                    }
            }
        }
        internal static float[][] FeatureMaps(Color32[] pixels, int w, int h) { Prepare(pixels, w, h); return maps; }
        internal static float[] Describe(Color32[] pixels, int w, int h, Vector2 center, Vector2 size, int family)
        {
            var values = new float[Columns * Rows]; Fill(pixels, w, h, center, size, family, values); return values;
        }
        internal static float[] DescribeMask(int[] labels, int w, int h, Vector2 center, Vector2 size, List<int> components)
        {
            var ids = new HashSet<int>(components); var values = new float[Columns * Rows];
            float Sample(float x, float y)
            {
                x = Mathf.Clamp(x, 0, w - 1.001f); y = Mathf.Clamp(y, 0, h - 1.001f);
                int xx = (int)x, yy = (int)y, at = yy * w + xx;
                return Mathf.Lerp(Mathf.Lerp(ids.Contains(labels[at]) ? 1 : 0, ids.Contains(labels[at + 1]) ? 1 : 0, x - xx),
                    Mathf.Lerp(ids.Contains(labels[at + w]) ? 1 : 0, ids.Contains(labels[at + w + 1]) ? 1 : 0, x - xx), y - yy);
            }
            float radius = Mathf.Max(1, size.y * .12f), sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                float x = center.x + ((i % Columns + .5f) / Columns - .5f) * size.x * 1.15f;
                float y = center.y + ((i / Columns + .5f) / Rows - .5f) * size.y * 1.3f;
                values[i] = Sample(x, y) - .25f * (Sample(x - radius, y) + Sample(x + radius, y) + Sample(x, y - radius) + Sample(x, y + radius));
                sum += values[i];
            }
            float mean = sum / values.Length; for (int i = 0; i < values.Length; i++) values[i] -= mean;
            return values;
        }
        internal static void Fill(Color32[] pixels, int w, int h, Vector2 center, Vector2 size, int family, float[] values)
        {
            Prepare(pixels, w, h);
            float radius = Mathf.Clamp(size.y * .12f, 1, 8), sum = 0;
            int level = radius < 2 ? 0 : radius < 4 ? 1 : 2;
            float blend = (radius - Radii[level]) / (Radii[level + 1] - Radii[level]);
            var a = maps[(family - 1) * 4 + level]; var b = maps[(family - 1) * 4 + level + 1];
            for (int i = 0; i < values.Length; i++)
            {
                float x = center.x + ((i % Columns + .5f) / Columns - .5f) * size.x * 1.15f;
                float y = center.y + ((i / Columns + .5f) / Rows - .5f) * size.y * 1.3f;
                float value = Mathf.Lerp(Read(a, w, h, x, y), Read(b, w, h, x, y), blend);
                values[i] = value; sum += value;
            }
            float mean = sum / values.Length; for (int i = 0; i < values.Length; i++) values[i] -= mean;
        }
        static float Read(float[] p, int w, int h, float x, float y)
        {
            x = Mathf.Clamp(x, 0, w - 1.001f); y = Mathf.Clamp(y, 0, h - 1.001f);
            int ix = (int)x, iy = (int)y; float tx = x - ix, ty = y - iy;
            int at = iy * w + ix;
            return Mathf.Lerp(Mathf.Lerp(p[at], p[at + 1], tx), Mathf.Lerp(p[at + w], p[at + w + 1], tx), ty);
        }
        internal static float Compare(float[] template, float[] values, out float amplitude)
        {
            double a = 0, b = 0, cross = 0, sumA = 0, sumB = 0, total = 0;
            for (int i = 0; i < template.Length; i++)
            {
                // Ignore empty template background, where unrelated attack flashes
                // otherwise dominate the normalization despite matching glyph strokes.
                float weight = Mathf.Clamp01((Mathf.Abs(template[i]) - .025f) * 20);
                total += weight; sumA += weight * template[i]; sumB += weight * values[i];
                a += weight * template[i] * template[i]; b += weight * values[i] * values[i]; cross += weight * template[i] * values[i];
            }
            if (total < 1) { amplitude = 0; return 0; }
            a = Math.Max(0, a - sumA * sumA / total); b = Math.Max(0, b - sumB * sumB / total); cross -= sumA * sumB / total;
            amplitude = a > .00001 ? Mathf.Max(0, (float)(cross / a)) : 0;
            return a * b > .0000001 ? (float)(cross / Math.Sqrt(a * b)) : 0;
        }
    }
}
