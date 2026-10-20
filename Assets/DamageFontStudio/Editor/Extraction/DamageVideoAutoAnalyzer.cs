using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Local, heuristic detector for light/pink, dark-outlined floating numbers.
    // Digit templates provide an uncertain text estimate, never the source font or hidden glyphs.
    internal sealed class DamageVideoAutoAnalyzer
    {
        internal sealed class Hit
        {
            internal int frame, family;
            internal Vector2 center, size;
            internal float contrast;
            internal Color color;
            internal float[] signal;
            internal float[] maskSignal;
            internal string text;
            internal float textConfidence;
            internal bool learned;
            internal bool templateMatched;
            internal float matchCorrelation;
            internal Vector2 appearanceRatio = Vector2.one;
        }
        internal sealed class Track
        {
            internal readonly List<Hit> hits = new List<Hit>();
            internal float score;
            internal Hit First => hits[0];
            internal Hit Last => hits[hits.Count - 1];
            internal string Label(double fps) => $"{(First.family == 1 ? StudioLanguage.Text("Light numbers") : StudioLanguage.Text("Pink numbers"))} · {First.frame / fps:0.00}–{Last.frame / fps:0.00}s";
        }
        struct Blob
        {
            internal int x, y, w, h, count, family;
            internal Color color;
            internal float contrast;
            internal string text;
            internal float textConfidence;
            internal List<int> components;
            internal Vector2 Center => new Vector2(x + w * .5f, y + h * .5f);
        }
        readonly List<Track> tracks = new List<Track>();
        int sourceHeight;
        internal IEnumerable<Hit> WordObservations => tracks.SelectMany(t => t.hits.Where(h => h.text?.Length >= 2 &&
            ((h.learned && h.textConfidence >= .8f) || (!h.learned && h.textConfidence >= .6f && t.hits.Count >= 3 && h.size.y >= sourceHeight * .024f))));
        internal IEnumerable<string> Diagnostics => tracks.Where(t => t.hits.Count >= 2).Select(t => $"{t.First.frame}-{t.Last.frame} n={t.hits.Count} family={t.First.family} xy={t.First.center} size={t.First.size} rise={t.First.center.y-t.Last.center.y:0.0} text={string.Join("/", t.hits.Select(h=>h.text).Distinct())}");
        readonly int firstFrame, lastFrame, step;
        readonly double fps;
        readonly Rect? analysisRegion;
        internal DamageVideoTextDetector textDetector;
        readonly DamageVideoCameraMotion camera = new DamageVideoCameraMotion();
        internal readonly Dictionary<int, Vector2> CameraOffsets = new Dictionary<int, Vector2>();
        Vector2 cameraOffset;
        Vector2 CameraAt(int frame) => CameraOffsets.TryGetValue(frame, out var value) ? value : Vector2.zero;
        int[] integral, queue, labels;
        byte[] mask;
        internal DamageVideoAutoAnalyzer(int firstFrame, int lastFrame, int step, double fps, Rect? analysisRegion = null)
        { this.firstFrame = firstFrame; this.lastFrame = lastFrame; this.step = step; this.fps = fps; this.analysisRegion = analysisRegion; }

        internal void AddFrame(Color32[] pixels, int width, int height, int frame, Color32[] backgroundPixels = null)
        {
            if (pixels == null || pixels.Length != width * height) throw new ArgumentException("Invalid frame pixels.");
            sourceHeight = height;
            cameraOffset += camera.Add(backgroundPixels ?? pixels, width, height); CameraOffsets[frame] = cameraOffset;
            var found = Detect(pixels, width, height, frame);
            Associate(found, frame);
        }
        internal void AddRecoveredFrame(List<Hit> found, int frame, Vector2 offset)
        { cameraOffset = offset; CameraOffsets[frame] = offset; Associate(found, frame); }
        void Associate(List<Hit> found, int frame)
        {
            found = found.Where(h => DamageVideoAnalysisRegion.Contains(analysisRegion, h.center, h.size)).ToList();
            var pairs = new List<(float cost, Track track, int index)>();
            foreach (var t in tracks)
            {
                int gap = frame - t.Last.frame;
                if (gap <= 0 || gap > step * 3) continue;
                Vector2 velocity = Vector2.zero;
                if (t.hits.Count > 1)
                {
                    var a = t.hits[t.hits.Count - 2];
                    velocity = (t.Last.center - a.center - CameraAt(t.Last.frame) + CameraAt(a.frame)) / (t.Last.frame - a.frame);
                }
                for (int i = 0; i < found.Count; i++)
                {
                    var h = found[i];
                    if (h.family != t.First.family) continue;
                    var word = t.hits.LastOrDefault(a => a.learned && a.textConfidence > .85f);
                    float ratio = h.size.y / t.Last.size.y;
                    // Reject a discontinuous birth below an existing row, while
                    // allowing continuous downward motion after an apex.
                    float down = h.center.y - t.Last.center.y - (cameraOffset.y - CameraAt(t.Last.frame).y);
                    if (down > Mathf.Max(3, t.Last.size.y * .35f) * gap &&
                        down - velocity.y * gap > Mathf.Max(4, t.Last.size.y * .5f) * gap) continue;
                    if (frame - t.First.frame > fps * .12 && ratio > 1.3f) continue;
                    float aspect = h.size.x / h.size.y;
                    float firstAspect = t.First.size.x / t.First.size.y;
                    // Compare with the birth aspect too: prevents a 189 track silently switching to 18.
                    if (ratio < .55f || ratio > 1.8f || Mathf.Abs(Mathf.Log(aspect / firstAspect)) > .30f) continue;
                    var delta = h.center - t.Last.center - velocity * gap - cameraOffset + CameraAt(t.Last.frame);
                    float allowance = Mathf.Max(10, t.Last.size.y * .7f) * gap;
                    if (delta.magnitude > allowance) continue;
                    float cost = delta.magnitude + Mathf.Abs(Mathf.Log(ratio)) * t.Last.size.y +
                        Mathf.Abs(Mathf.Log(aspect / firstAspect)) * t.Last.size.y;
                    if (h.learned && h.textConfidence > .85f && word != null && word.text != h.text) cost += t.Last.size.y * .2f;
                    if (t.Last.signal != null && h.signal != null)
                    {
                        float similarity = DamageVideoSignal.Compare(t.Last.signal, h.signal, out _);
                        if (similarity < .05f) continue;
                        cost += (1 - similarity) * t.Last.size.y;
                    }
                    pairs.Add((cost, t, i));
                }
            }
            var usedTracks = new HashSet<Track>(); var usedHits = new HashSet<int>();
            foreach (var pair in pairs.OrderBy(p => p.cost))
            {
                if (usedTracks.Contains(pair.track) || usedHits.Contains(pair.index)) continue;
                usedTracks.Add(pair.track); usedHits.Add(pair.index); pair.track.hits.Add(found[pair.index]);
            }
            for (int i = 0; i < found.Count; i++)
                if (!usedHits.Contains(i)) { var t = new Track(); t.hits.Add(found[i]); tracks.Add(t); }
        }

        internal List<Track> Finish(bool all = false)
        {
            var result = new List<Track>();
            foreach (var t in tracks)
            {
                float duration = (float)((t.Last.frame - t.First.frame) / fps);
                var origin = t.First.center - CameraAt(t.First.frame);
                float movement = t.hits.Max(h => Vector2.Distance(h.center - CameraAt(h.frame), origin));
                float height = t.hits.Average(h => h.size.y);
                bool clearWord = all && t.hits.Any(h => h.learned && h.textConfidence > .85f && h.text?.Length >= 2);
                // Reject static HUD text, flashes, short fragments and scan-boundary truncations.
                if (t.hits.Count < (clearWord ? 1 : all ? 2 : 5) || duration < (clearWord ? 0 : all ? .03f : .22f) || duration > 2 || (!clearWord && movement < height * (all ? .2f : 1)) ||
                    t.First.frame <= firstFrame + step || t.Last.frame >= lastFrame - step) continue;
                float density = t.hits.Count * step / (float)(t.Last.frame - t.First.frame + step);
                float aspect = t.First.size.x / t.First.size.y;
                float shapeError = t.hits.Average(h => Mathf.Abs(Mathf.Log((h.size.x / h.size.y) / aspect)));
                t.score = density * Mathf.Clamp01(duration / .65f) * Mathf.Clamp01(1 - shapeError * 3);
                if (clearWord) t.score = Mathf.Max(.1f, t.score);
                if (t.score > (all ? .025f : .15f)) result.Add(t);
            }
            return result.OrderByDescending(t => t.score).Take(all ? 256 : 12).ToList();
        }

        internal DamageVideoSession MakeSession(Track track, string path, int width, int height, int frames)
        {
            var session = ScriptableObject.CreateInstance<DamageVideoSession>();
            session.videoPath = path; session.width = width; session.height = height; session.frameCount = frames; session.fps = fps;
            session.startFrame = track.First.frame; session.endFrame = Mathf.Min(frames - 1, track.Last.frame + step * 2);
            session.sampleStep = step; session.autoAppearance = true;
            session.number = "189"; // Editable preview value, not an OCR result.
            var strongest = track.hits.OrderByDescending(h => h.contrast).First().color;
            float brightest = Mathf.Max(strongest.r, strongest.g, strongest.b);
            session.color = track.First.family == 1 ? Color.white : new Color(strongest.r / brightest, strongest.g / brightest, strongest.b / brightest, 1);
            float peak = track.hits.Max(h => h.contrast);
            foreach (var h in track.hits)
            {
                // Contrast against the dark outline is an alpha proxy, not recovered source alpha.
                float opacity = Mathf.Clamp01(h.contrast / Mathf.Max(.05f, peak));
                session.Put(new DamageVideoSample { frame = h.frame, center = h.center, size = h.size,
                    opacity = opacity, confidence = track.score, manual = false });
            }
            var tail = session.samples[session.samples.Count - 1].Copy();
            var before = session.samples[Mathf.Max(0, session.samples.Count - 3)];
            Vector2 velocity = (tail.center - before.center) / Mathf.Max(1, tail.frame - before.frame);
            tail.center += velocity * (session.endFrame - tail.frame); tail.frame = session.endFrame; tail.opacity = 0;
            session.Put(tail);
            return session;
        }

        List<Hit> Detect(Color32[] source, int sourceW, int sourceH, int frame)
        {
            int stride = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(sourceW, sourceH) / 640f));
            int w = sourceW / stride, h = sourceH / stride, length = w * h;
            if (mask == null || mask.Length != length)
            { mask = new byte[length]; queue = new int[length]; labels = new int[length]; integral = new int[(w + 1) * (h + 1)]; }
            Array.Clear(mask, 0, length); Array.Clear(integral, 0, integral.Length);
            Array.Clear(labels, 0, length);
            Color32 Read(int x, int y) => source[(sourceH - 1 - y * stride) * sourceW + x * stride];
            for (int y = 0; y < h; y++)
            {
                int row = 0;
                for (int x = 0; x < w; x++)
                {
                    var c = Read(x, y); int max = Math.Max(c.r, Math.Max(c.g, c.b));
                    row += max < 110 ? 1 : 0;
                    integral[(y + 1) * (w + 1) + x + 1] = integral[y * (w + 1) + x + 1] + row;
                }
            }
            int radius = Mathf.Max(2, Mathf.RoundToInt(h * .008f));
            int Dark(int x0, int y0, int x1, int y1) => integral[y1 * (w + 1) + x1] - integral[y0 * (w + 1) + x1] - integral[y1 * (w + 1) + x0] + integral[y0 * (w + 1) + x0];
            for (int y = radius; y < h - radius; y++)
                for (int x = radius; x < w - radius; x++)
                {
                    var c = Read(x, y); int max = Math.Max(c.r, Math.Max(c.g, c.b)), min = Math.Min(c.r, Math.Min(c.g, c.b));
                    byte family = min > 150 && max - min < 60 ? (byte)1 :
                        c.r > 150 && c.b > 80 && c.r > c.g * 1.3f && c.b > c.g * 1.25f ? (byte)2 : (byte)0;
                    if (family == 0) continue;
                    int x0 = x - radius, y0 = y - radius, x1 = x + radius + 1, y1 = y + radius + 1;
                    // An outlined glyph has dark pixels on opposing sides of its strokes.
                    // A bright spell touching a single dark edge is not a glyph fill.
                    bool enclosed = (Dark(x0, y0, x, y1) > 0 && Dark(x + 1, y0, x1, y1) > 0) ||
                        (Dark(x0, y0, x1, y) > 0 && Dark(x0, y + 1, x1, y1) > 0);
                    if (enclosed) mask[y * w + x] = family;
                }
            var blobs = new List<Blob>();
            for (int i = 0; i < length; i++)
            {
                byte family = mask[i]; if (family == 0) continue;
                int head = 0, tail = 1; queue[0] = i; mask[i] = 0;
                int minX = w, minY = h, maxX = 0, maxY = 0; Color sum = Color.clear;
                while (head < tail)
                {
                    int p = queue[head++], x = p % w, y = p / w;
                    labels[p] = i + 1;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    sum += (Color)Read(x, y);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        int n = yy * w + xx;
                        if (mask[n] == family) { mask[n] = 0; queue[tail++] = n; }
                    }
                }
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (bh < Mathf.Max(6, h * .014f) || bh > h * .12f || bw < 2 || bw > bh * 3 ||
                    tail < 6 || tail < bw * bh * .13f || minX < 1 || maxX > w - 2 || minY < h * .04f || maxY > h * .95f) continue;
                Color color = sum / tail;
                float darkLevel = 1;
                for (int yy = Math.Max(0, minY - 1); yy <= Math.Min(h - 1, maxY + 1); yy++)
                    for (int xx = Math.Max(0, minX - 1); xx <= Math.Min(w - 1, maxX + 1); xx++)
                    {
                        var edge = Read(xx, yy);
                        darkLevel = Mathf.Min(darkLevel, Mathf.Max(edge.r, edge.g, edge.b) / 255f);
                    }
                float textScore = 0;
                string text = stride == 1 ? DamageVideoDigitRecognizer.Read(source, sourceW, sourceH, minX, minY, bw, bh, family, out textScore) : "";
                blobs.Add(new Blob { x = minX, y = minY, w = bw, h = bh, count = tail, family = family, text = text, textConfidence = textScore, components = new List<int> { i + 1 },
                    color = color, contrast = Mathf.Max(0, Mathf.Max(color.r, color.g, color.b) - darkLevel) });
            }
            var groups = new List<Blob>();
            foreach (var b in blobs.OrderBy(b => b.x))
            {
                int best = -1;
                for (int j = 0; j < groups.Count; j++)
                {
                    var g = groups[j];
                    if (g.family == b.family && Mathf.Abs(g.Center.y - b.Center.y) < Mathf.Min(g.h, b.h) * .35f &&
                        b.x - (g.x + g.w) < Mathf.Max(g.h, b.h) * .4f && Mathf.Max(g.h, b.h) < Mathf.Min(g.h, b.h) * 1.5f) best = j;
                }
                if (best < 0) groups.Add(b);
                else
                {
                    var g = groups[best]; int x = Math.Min(g.x, b.x), y = Math.Min(g.y, b.y);
                    g.w = Math.Max(g.x + g.w, b.x + b.w) - x; g.h = Math.Max(g.y + g.h, b.y + b.h) - y; g.x = x; g.y = y;
                    g.color = (g.color * g.count + b.color * b.count) / (g.count + b.count); g.count += b.count;
                    g.contrast = Mathf.Max(g.contrast, b.contrast); groups[best] = g;
                    g.text = string.IsNullOrEmpty(g.text) || string.IsNullOrEmpty(b.text) ? "" : g.text + b.text;
                    g.textConfidence = Mathf.Min(g.textConfidence, b.textConfidence); groups[best] = g;
                    g.components.AddRange(b.components); groups[best] = g;
                }
            }
            var learnedHits = new List<Hit>();
            if (textDetector != null)
            {
                foreach (var region in textDetector.Detect(source, sourceW, sourceH))
                {
                    var box = region.bounds;
                    // Learned word regions join strokes that the outline threshold splits.
                    // Keep the measured glyph bounds, never the model's padded rectangle.
                    var inside = blobs.Where(b => box.Contains(b.Center * stride) &&
                        b.h * stride >= region.coreHeight * .4f && b.h * stride <= region.coreHeight * 2 &&
                        Mathf.Abs(b.Center.y * stride - box.center.y) < region.coreHeight * .65f).ToList();
                    if (region.textConfidence < .65f || string.IsNullOrEmpty(region.text) || (region.family == 0 && inside.Count == 0)) continue;
                    int family = region.family != 0 ? region.family : inside.GroupBy(b => b.family).OrderByDescending(g => g.Sum(b => b.count)).First().Key;
                    inside = inside.Where(b => b.family == family).OrderBy(b => b.x).ToList();
                    Color color = region.color;
                    if (region.family == 0)
                    { color = Color.clear; int count = inside.Sum(b => b.count); foreach (var b in inside) color += b.color * (b.count / (float)count); }
                    float pad = (box.height - region.coreHeight) / 2;
                    var appearance = region.inkBounds.width > 0 ? region.inkBounds.size : new Vector2(box.width - pad, box.height - pad);
                    // A detector rectangle contains the whole outlined word, including
                    // strokes lost by the color threshold under a bright attack flash.
                    var hit = new Hit { frame = frame, family = family, center = box.center, size = box.size,
                        color = color, contrast = Mathf.Max(color.r, color.g, color.b), text = region.text,
                        textConfidence = region.textConfidence, learned = true, maskSignal = region.mask,
                        appearanceRatio = new Vector2(appearance.x / box.width, appearance.y / box.height) };
                    hit.signal = DamageVideoSignal.Describe(source, sourceW, sourceH, hit.center, hit.size, family);
                    groups.RemoveAll(b => b.family == family && box.Contains(b.Center * stride));
                    learnedHits.Add(hit);
                }
            }
            return groups.Where(g => g.w > g.h * .65f && g.w < g.h * 5).Select(g =>
            {
                var hit = new Hit { frame = frame, family = g.family, center = g.Center * stride, size = new Vector2(g.w, g.h) * stride, color = g.color, contrast = g.contrast, text = g.text, textConfidence = g.textConfidence };
                hit.signal = DamageVideoSignal.Describe(source, sourceW, sourceH, hit.center, hit.size, hit.family);
                hit.maskSignal = DamageVideoSignal.DescribeMask(labels, w, h, g.Center, new Vector2(g.w, g.h), g.components); return hit;
            }).Concat(learnedHits).ToList();
        }
    }
}
