using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Small local font-template recognizer, used only to improve comparison text.
    // Low-confidence text remains explicitly unknown; it never controls event timing.
    internal static class DamageVideoDigitRecognizer
    {
        const int W = 20, H = 28;
        static List<float[]> digits;
        static List<float> aspects;
        static bool initialized;
        static void Initialize()
        {
            initialized = true; digits = new List<float[]>(); aspects = new List<float>();
            var font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (!font) return;
            var atlas = font.atlasTexture; Color32[] p;
            if (atlas.isReadable) p = atlas.GetPixels32();
            else
            {
                var previous = RenderTexture.active; var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                var copy = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
                try { Graphics.Blit(atlas, rt); RenderTexture.active = rt; copy.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0); copy.Apply(); p = copy.GetPixels32(); }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(copy); }
            }
            for (uint ch = '0'; ch <= '9'; ch++)
            {
                if (!font.characterLookupTable.TryGetValue(ch, out var c)) { digits.Clear(); return; }
                var rect = c.glyph.glyphRect;
                int left = rect.x + rect.width, right = rect.x, bottom = rect.y + rect.height, top = rect.y;
                for (int y = rect.y; y < rect.y + rect.height; y++) for (int x = rect.x; x < rect.x + rect.width; x++)
                    if (p[y * atlas.width + x].a > 127)
                    { left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y); }
                var pattern = new float[W * H];
                float height = top - bottom + 1, width = right - left + 1, lean = height * .18f;
                for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
                {
                    float yy = (y + .5f) / H;
                    float xx = (x + .5f) / W * (width + lean) - lean * (1 - yy);
                    int ax = Mathf.RoundToInt(left + xx), ay = Mathf.RoundToInt(top - yy * height);
                    if (ax >= left - 1 && ax <= right + 1 && ay >= bottom && ay <= top)
                        pattern[y * W + x] = p[ay * atlas.width + Mathf.Clamp(ax, 0, atlas.width - 1)].a > 116 ? 1 : 0;
                }
                digits.Add(pattern);
                aspects.Add((width + lean) / height);
            }
        }
        internal static string Read(Color32[] p, int w, int h, int x, int y, int width, int height, int family, out float confidence)
        {
            confidence = 0;
            if (!initialized) Initialize();
            if (digits.Count != 10 || width > height * 1.3f || height < 8) return "";
            var observed = new float[W * H];
            for (int yy = 0; yy < H; yy++) for (int xx = 0; xx < W; xx++)
            {
                int sx = Mathf.Clamp(x + (int)((xx + .5f) / W * width), 0, w - 1);
                int sy = Mathf.Clamp(y + (int)((yy + .5f) / H * height), 0, h - 1);
                var c = p[(h - 1 - sy) * w + sx];
                observed[yy * W + xx] = family == 1 ? (Mathf.Min(c.r, c.g, c.b) > 145 && Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b) < 70 ? 1 : 0) :
                    (c.r > 140 && c.b > 70 && c.r > c.g * 1.25f && c.b > c.g * 1.2f ? 1 : 0);
            }
            float best = -1; int digit = 0;
            for (int n = 0; n < 10; n++)
            {
                float cross = 0, sum = 0;
                for (int i = 0; i < observed.Length; i++) { cross += observed[i] * digits[n][i]; sum += observed[i] + digits[n][i]; }
                float score = 2 * cross / Mathf.Max(1, sum);
                score -= .3f * Mathf.Abs(Mathf.Log((width / (float)height) / aspects[n]));
                if (score > best) { best = score; digit = n; }
            }
            confidence = best; return best >= .55f ? digit.ToString() : "";
        }
    }
}
