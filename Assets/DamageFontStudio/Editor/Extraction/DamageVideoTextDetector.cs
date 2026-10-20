using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Unity.InferenceEngine;
using System.IO;
using System.Linq;

namespace DamageFontStudio.Editor
{
    // Detection only: this model locates words. It does not identify the game's font or digits.
    // Kept in Editor so generated samples have no inference or video dependency.
    internal sealed class DamageVideoTextDetector : IDisposable
    {
        internal struct Region
        {
            internal Rect bounds, inkBounds;
            internal float coreHeight, confidence;
            internal string text; internal float textConfidence;
            internal int family; internal Color color; internal float[] mask;
        }
        readonly Worker worker;
        readonly Worker recognizer;
        readonly string[] characters;
        internal DamageVideoTextDetector()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/DamageFontStudio/Editor/Models/DamageTextDetector.onnx");
            if (!asset) throw new InvalidOperationException("Damage text detector model is missing.");
            worker = new Worker(ModelLoader.Load(asset), SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
            try
            {
                var recognitionAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/DamageFontStudio/Editor/Models/DamageTextRecognizer.onnx");
                if (!recognitionAsset) throw new InvalidOperationException("Damage text recognition model is missing.");
                recognizer = new Worker(ModelLoader.Load(recognitionAsset), SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU);
                characters = new[] { "" }.Concat(File.ReadAllLines("Assets/DamageFontStudio/Editor/Models/Characters.txt")).Concat(new[] { " " }).ToArray();
            }
            catch { worker.Dispose(); throw; }
        }
        internal List<Region> Detect(Color32[] pixels, int sw, int sh)
        {
            float resize = Mathf.Min(736f / Mathf.Min(sw, sh), 1536f / Mathf.Max(sw, sh));
            int inputWidth = Mathf.Max(32, Mathf.RoundToInt(sw * resize / 32) * 32);
            int inputHeight = Mathf.Max(32, Mathf.RoundToInt(sh * resize / 32) * 32);
            int n = inputWidth * inputHeight;
            var data = new float[3 * n];
            float[] means = { .485f, .456f, .406f }, deviations = { .229f, .224f, .225f };
            for (int y = 0; y < inputHeight; y++) for (int x = 0; x < inputWidth; x++)
            {
                float sx = Mathf.Clamp((x + .5f) * sw / inputWidth - .5f, 0, sw - 1.001f);
                float sy = Mathf.Clamp((y + .5f) * sh / inputHeight - .5f, 0, sh - 1.001f);
                int xx = (int)sx, yy = (int)sy;
                Color a = pixels[(sh - 1 - yy) * sw + xx], b = pixels[(sh - 1 - yy) * sw + xx + 1];
                Color c = pixels[(sh - 2 - yy) * sw + xx], d = pixels[(sh - 2 - yy) * sw + xx + 1];
                var color = Color.Lerp(Color.Lerp(a, b, sx - xx), Color.Lerp(c, d, sx - xx), sy - yy);
                int at = y * inputWidth + x;
                data[at] = (color.b - means[0]) / deviations[0];
                data[n + at] = (color.g - means[1]) / deviations[1];
                data[2 * n + at] = (color.r - means[2]) / deviations[2];
            }
            float[] scores; int w, h;
            using (var input = new Tensor<float>(new TensorShape(1, 3, inputHeight, inputWidth), data))
            {
                worker.Schedule(input);
                using (var output = ((Tensor<float>)worker.PeekOutput()).ReadbackAndClone())
                { w = output.shape[3]; h = output.shape[2]; scores = output.DownloadToArray(); }
            }
            var visited = new bool[scores.Length]; var queue = new int[scores.Length];
            var result = new List<Region>();
            for (int i = 0; i < scores.Length; i++)
            {
                if (visited[i] || scores[i] < .3f) continue;
                visited[i] = true; queue[0] = i; int head = 0, tail = 1, x0 = w, y0 = h, x1 = 0, y1 = 0; float sum = 0;
                while (head < tail)
                {
                    int at = queue[head++], x = at % w, y = at / w;
                    sum += scores[at]; x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        int k = yy * w + xx;
                        if (!visited[k] && scores[k] >= .3f) { visited[k] = true; queue[tail++] = k; }
                    }
                }
                float rw = (x1 - x0 + 1) * sw / (float)w, rh = (y1 - y0 + 1) * sh / (float)h;
                if (tail < 16 || sum / tail < .6f || rw < rh * 1.2f || rw > rh * 7 || rh < 5 || rh > sh * .12f) continue;
                float pad = 1.6f * rw * rh / (2 * (rw + rh));
                result.Add(new Region { bounds = new Rect(x0 * sw / (float)w - pad, y0 * sh / (float)h - pad, rw + 2 * pad, rh + 2 * pad),
                    coreHeight = rh, confidence = sum / tail });
            }
            // Large horizontal regions are the most useful unoccluded word observations.
            // Keep recognition separate from detection and preserve its uncertainty.
            foreach (int index in Enumerable.Range(0, result.Count).OrderByDescending(i => result[i].bounds.width * result[i].bounds.height).Take(16))
            {
                var region = result[index];
                ReadNumber(pixels, sw, sh, region.bounds, out region.text, out region.textConfidence);
                if (region.textConfidence >= .65f) MeasureInk(pixels, sw, sh, ref region);
                result[index] = region;
            }
            return result;
        }
        static void MeasureInk(Color32[] pixels, int sw, int sh, ref Region region)
        {
            var bounds = region.bounds;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(bounds.x)), y0 = Mathf.Max(0, Mathf.FloorToInt(bounds.y));
            int w = Mathf.Min(sw, Mathf.CeilToInt(bounds.xMax)) - x0, h = Mathf.Min(sh, Mathf.CeilToInt(bounds.yMax)) - y0;
            if (w < 3 || h < 3) return;
            var colors = new byte[w * h]; var labels = new int[w * h]; var queue = new int[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                var c = pixels[(sh - 1 - y - y0) * sw + x + x0];
                int max = Math.Max(c.r, Math.Max(c.g, c.b)), min = Math.Min(c.r, Math.Min(c.g, c.b));
                colors[y * w + x] = min > 135 && max - min < 80 ? (byte)1 :
                    c.r > 135 && c.b > 80 && c.r > c.g * 1.3f && c.b > c.g * 1.25f ? (byte)2 : (byte)0;
            }
            var components = new List<(int id, int family, int count, Rect box, Color color)>();
            for (int i = 0; i < colors.Length; i++)
            {
                int family = colors[i]; if (family == 0) continue;
                int head = 0, tail = 1, left = w, top = h, right = 0, bottom = 0; bool boundary = false; Color sum = Color.clear;
                queue[0] = i; colors[i] = 0;
                while (head < tail)
                {
                    int at = queue[head++], x = at % w, y = at / w;
                    labels[at] = i + 1; left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                    boundary |= x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    sum += (Color)pixels[(sh - 1 - y - y0) * sw + x + x0];
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        int k = yy * w + xx;
                        if (colors[k] == family) { colors[k] = 0; queue[tail++] = k; }
                    }
                }
                var box = new Rect(left, top, right - left + 1, bottom - top + 1);
                if (boundary || tail < 8 || box.height < region.coreHeight * .45f || box.height > region.coreHeight * 2.3f ||
                    box.width > box.height * 2.5f || Mathf.Abs(y0 + box.center.y - bounds.center.y) > region.coreHeight * .65f) continue;
                components.Add((i + 1, family, tail, box, sum / tail));
            }
            if (components.Count == 0) return;
            int selected = components.GroupBy(c => c.family).OrderByDescending(g => g.Sum(c => c.count)).First().Key;
            var chosen = components.Where(c => c.family == selected).ToList();
            chosen = chosen.OrderByDescending(c => c.count).Take(Math.Max(region.text.Length, 1)).ToList();
            if (chosen.Count >= 2)
            {
                float glyphHeight = chosen.Select(c => c.box.height).OrderBy(v => v).ElementAt(chosen.Count / 2);
                if (chosen.Max(c => c.box.yMax) - chosen.Min(c => c.box.yMax) > glyphHeight * .35f)
                { region.textConfidence *= .5f; return; }
            }
            // Require an observation of every digit. A partial glyph must not redefine
            // the word's aspect ratio when the neighboring digit is occluded.
            float minX = chosen.Min(c => c.box.x), minY = chosen.Min(c => c.box.y);
            float maxX = chosen.Max(c => c.box.xMax), maxY = chosen.Max(c => c.box.yMax);
            if (chosen.Count < region.text.Length && (maxX - minX) / (maxY - minY) < region.text.Length * .5f) return;
            region.inkBounds = new Rect(x0 + minX, y0 + minY, maxX - minX, maxY - minY);
            region.family = selected; region.color = Color.clear; int total = chosen.Sum(c => c.count);
            foreach (var c in chosen) region.color += c.color * (c.count / (float)total);
            region.mask = DamageVideoSignal.DescribeMask(labels, w, h, bounds.center - new Vector2(x0, y0),
                bounds.size, chosen.Select(c => c.id).ToList());
        }
        void ReadNumber(Color32[] pixels, int sw, int sh, Rect box, out string text, out float confidence)
        {
            const int h = 48, w = 320, n = h * w;
            int rw = Mathf.Clamp(Mathf.CeilToInt(h * box.width / box.height), 1, w);
            var data = new float[n * 3];
            for (int y = 0; y < h; y++) for (int x = 0; x < rw; x++)
            {
                float sx = Mathf.Clamp(box.x + (x + .5f) * box.width / rw - .5f, 0, sw - 1.001f);
                float sy = Mathf.Clamp(box.y + (y + .5f) * box.height / h - .5f, 0, sh - 1.001f);
                int xx = (int)sx, yy = (int)sy;
                Color a = pixels[(sh - 1 - yy) * sw + xx], b = pixels[(sh - 1 - yy) * sw + xx + 1];
                Color c = pixels[(sh - 2 - yy) * sw + xx], d = pixels[(sh - 2 - yy) * sw + xx + 1];
                var v = Color.Lerp(Color.Lerp(a, b, sx - xx), Color.Lerp(c, d, sx - xx), sy - yy);
                int at = y * w + x; data[at] = v.b * 2 - 1; data[n + at] = v.g * 2 - 1; data[2 * n + at] = v.r * 2 - 1;
            }
            text = ""; confidence = 0; int count = 0, previous = -1;
            using (var input = new Tensor<float>(new TensorShape(1, 3, h, w), data))
            {
                recognizer.Schedule(input);
                using (var output = ((Tensor<float>)recognizer.PeekOutput()).ReadbackAndClone())
                {
                    var values = output.DownloadToArray(); int categories = output.shape[2];
                    for (int t = 0; t < output.shape[1]; t++)
                    {
                        int best = 0, at = t * categories;
                        for (int j = 1; j < categories; j++) if (values[at + j] > values[at + best]) best = j;
                        if (best != 0 && best != previous && best < characters.Length)
                        { text += characters[best]; confidence += values[at + best]; count++; }
                        previous = best;
                    }
                }
            }
            confidence /= Math.Max(1, count);
            if (text.Length == 0 || text.Length > 10 || text.Any(c => c < '0' || c > '9')) { text = ""; confidence = 0; }
        }
        public void Dispose() { worker?.Dispose(); recognizer?.Dispose(); }
    }
}
