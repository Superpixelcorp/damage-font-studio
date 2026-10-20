using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Hit = DamageFontStudio.Editor.DamageVideoAutoAnalyzer.Hit;

namespace DamageFontStudio.Editor
{
    // Reuse words actually observed in this video to find repeated overlapping hits.
    // No source-specific values, coordinates or timings are encoded here.
    internal sealed class DamageVideoWordSearch : IDisposable
    {
        readonly List<Hit> words;
        readonly ComputeShader shader;
        ComputeBuffer features, template, scores;
        readonly int width, height, gridW, gridH, originX, originY;
        const int Step = 2;
        readonly Vector2[] values;
        readonly Rect? analysisRegion;
        internal bool Ready => shader && words.Count > 0;
        internal IEnumerable<string> Diagnostics => words.Select(w => $"WORD {w.family} {w.text} size={w.size} mask={w.maskSignal != null} source={w.frame}");
        internal DamageVideoWordSearch(IEnumerable<Hit> observations, int w, int h, Rect? analysisRegion = null)
        {
            width = w; height = h;
            this.analysisRegion = analysisRegion;
            observations = observations.Where(o => DamageVideoAnalysisRegion.Contains(analysisRegion, o.center, o.size));
            var groups = observations.GroupBy(o => (o.family, o.text)).Where(g => g.Select(o => o.frame).Distinct().Count() >= 2 || g.Any(o => o.textConfidence >= .98f))
                .OrderByDescending(g => g.Count()).Take(12).ToList();
            words = groups.Select(g =>
            {
                var median = g.OrderBy(o => o.size.y).ElementAt(g.Count() / 2);
                return g.Where(o => o.size.y < median.size.y * 1.5f).OrderByDescending(o => o.maskSignal != null ? 2 + o.textConfidence : o.textConfidence).First();
            }).ToList();
            if (!SystemInfo.supportsComputeShaders || words.Count == 0) return;
            var asset = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/DamageFontStudio/Editor/Models/DamageWordSearch.compute");
            if (!asset) throw new InvalidOperationException("Missing word search shader.");
            shader = UnityEngine.Object.Instantiate(asset); shader.hideFlags = HideFlags.HideAndDontSave;
            var all = groups.SelectMany(g => g).ToList();
            float margin = all.Max(o => o.size.y) * 1.5f;
            originX = Mathf.Max(analysisRegion.HasValue ? Mathf.CeilToInt(analysisRegion.Value.xMin) : 0, Mathf.FloorToInt(all.Min(o => o.center.x) - margin));
            originY = Mathf.Max(analysisRegion.HasValue ? Mathf.CeilToInt(analysisRegion.Value.yMin) : 0, Mathf.FloorToInt(all.Min(o => o.center.y) - margin));
            int right = analysisRegion.HasValue ? Mathf.FloorToInt(analysisRegion.Value.xMax) : w;
            int bottom = analysisRegion.HasValue ? Mathf.FloorToInt(analysisRegion.Value.yMax) : h;
            gridW = Math.Max(1, (Mathf.Min(right, Mathf.CeilToInt(all.Max(o => o.center.x) + margin)) - originX) / Step);
            gridH = Math.Max(1, (Mathf.Min(bottom, Mathf.CeilToInt(all.Max(o => o.center.y) + margin)) - originY) / Step);
            features = new ComputeBuffer(w * h * 8, sizeof(float)); template = new ComputeBuffer(288, sizeof(float));
            scores = new ComputeBuffer(gridW * gridH, sizeof(float) * 2); values = new Vector2[gridW * gridH];
            shader.SetInt("Width", w); shader.SetInt("Height", h); shader.SetInt("GridWidth", gridW); shader.SetInt("GridHeight", gridH);
            shader.SetInt("OriginX", originX); shader.SetInt("OriginY", originY); shader.SetFloat("Step", Step);
            shader.SetBuffer(0, "Features", features); shader.SetBuffer(0, "Template", template); shader.SetBuffer(0, "Scores", scores);
        }
        internal List<Hit> Find(Color32[] pixels, int frame)
        {
            var hits = new List<(float score, Hit hit)>(); if (!Ready) return new List<Hit>();
            var maps = DamageVideoSignal.FeatureMaps(pixels, width, height);
            for (int i = 0; i < maps.Length; i++) features.SetData(maps[i], 0, i * width * height, width * height);
            foreach (var word in words)
            {
                template.SetData(word.maskSignal ?? word.signal); shader.SetInt("Family", word.family);
                foreach (float factor in new[] { .6f, .7f, .8f, .9f, 1f, 1.15f, 1.3f, 1.5f, 1.7f, 2f })
                {
                    var size = word.size * factor; shader.SetFloat("BoxWidth", size.x); shader.SetFloat("BoxHeight", size.y);
                    shader.Dispatch(0, (gridW + 7) / 8, (gridH + 7) / 8, 1); scores.GetData(values);
                    for (int y = 1; y < gridH - 1; y++) for (int x = 1; x < gridW - 1; x++)
                    {
                        int at = y * gridW + x; var value = values[at];
                        if (!DamageVideoAnalysisRegion.Contains(analysisRegion, new Vector2(originX + x * Step, originY + y * Step), size)) continue;
                        if (value.x < .6f || value.y < .15f || value.x < values[at - 1].x || value.x < values[at + 1].x || value.x < values[at - gridW].x || value.x < values[at + gridW].x) continue;
                        hits.Add((value.x, new Hit { frame = frame, family = word.family, text = word.text, textConfidence = word.textConfidence,
                            center = new Vector2(originX + x * Step, originY + y * Step), size = size, color = word.color, contrast = value.y,
                            appearanceRatio = word.appearanceRatio, learned = true, templateMatched = true, matchCorrelation = value.x, maskSignal = word.maskSignal ?? word.signal }));
                    }
                }
            }
            var selected = new List<Hit>();
            foreach (var pair in hits.OrderByDescending(p => p.score))
            {
                var hit = pair.hit;
                var ink = Vector2.Scale(hit.size, hit.appearanceRatio); var box = new Rect(hit.center - ink / 2, ink);
                bool contained = false;
                foreach (var other in selected.ToArray())
                {
                    if (other.family != hit.family || other.text == hit.text || !(other.text.Contains(hit.text) || hit.text.Contains(other.text))) continue;
                    var otherInk = Vector2.Scale(other.size, other.appearanceRatio); var otherBox = new Rect(other.center - otherInk / 2, otherInk);
                    float overlap = Mathf.Max(0, Math.Min(box.xMax, otherBox.xMax) - Math.Max(box.x, otherBox.x)) * Mathf.Max(0, Math.Min(box.yMax, otherBox.yMax) - Math.Max(box.y, otherBox.y));
                    if (overlap < Math.Min(box.width * box.height, otherBox.width * otherBox.height) * .65f) continue;
                    if (hit.text.Length > other.text.Length) selected.Remove(other); else contained = true;
                }
                if (contained) continue;
                var sameRow = selected.FirstOrDefault(other => other.family == hit.family &&
                    Mathf.Abs(hit.center.y - other.center.y) < Math.Min(hit.size.y * hit.appearanceRatio.y, other.size.y * other.appearanceRatio.y) * .25f &&
                    Mathf.Abs(hit.center.x - other.center.x) < Math.Max(hit.size.x * hit.appearanceRatio.x, other.size.x * other.appearanceRatio.x) * .35f);
                if (sameRow != null) { if (hit.size.x <= sameRow.size.x) continue; selected.Remove(sameRow); }
                if (selected.Any(other => other.family == hit.family && Vector2.Distance(hit.center, other.center) < Math.Min(hit.size.y * hit.appearanceRatio.y, other.size.y * other.appearanceRatio.y) * .35f)) continue;
                hit.signal = DamageVideoSignal.Describe(pixels, width, height, hit.center, hit.size, hit.family);
                selected.Add(hit); if (selected.Count >= 48) break;
            }
            return selected;
        }
        public void Dispose()
        { features?.Dispose(); template?.Dispose(); scores?.Dispose(); if (shader) UnityEngine.Object.DestroyImmediate(shader); }
    }
}
