using System;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Background translation, used for search prediction and optional removal
    // of shared screen shake from the exported effect trajectory.
    internal sealed class DamageVideoCameraMotion
    {
        float[] previous;
        int oldWidth, oldHeight;
        internal Vector2 Add(Color32[] pixels, int sw, int sh)
        {
            const int stride = 4;
            int w = sw / stride, h = sh / stride;
            if (w < 24 || h < 24) return Vector2.zero;
            var gray = new float[w * h]; var feature = new float[gray.Length];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                var c = pixels[(sh - 1 - y * stride) * sw + x * stride];
                gray[y * w + x] = (c.r + c.g + c.b) / 765f;
            }
            for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
            { int at = y * w + x; feature[at] = gray[at] - .25f * (gray[at - 1] + gray[at + 1] + gray[at - w] + gray[at + w]); }
            Vector2 motion = Vector2.zero;
            if (previous != null && oldWidth == w && oldHeight == h)
            {
                float best = float.PositiveInfinity, zero = float.PositiveInfinity; int bestX = 0, bestY = 0;
                for (int dy = -12; dy <= 12; dy++) for (int dx = -20; dx <= 20; dx++)
                {
                    float sum = 0; int count = 0;
                    for (int y = 14; y < h - 14; y += 3) for (int x = 22; x < w - 22; x += 3)
                    {
                        int at = y * w + x, before = (y - dy) * w + x - dx;
                        if (Mathf.Abs(previous[before]) < .025f && Mathf.Abs(feature[at]) < .025f) continue;
                        sum += Mathf.Min(.15f, Mathf.Abs(feature[at] - previous[before])); count++;
                    }
                    if (count < 100) continue;
                    float error = sum / count + (Math.Abs(dx) + Math.Abs(dy)) * .00008f;
                    if (dx == 0 && dy == 0) zero = error;
                    if (error < best) { best = error; bestX = dx; bestY = dy; }
                }
                if (best < zero * .75f) motion = new Vector2(bestX * stride, bestY * stride);
            }
            previous = feature; oldWidth = w; oldHeight = h; return motion;
        }
    }
}
