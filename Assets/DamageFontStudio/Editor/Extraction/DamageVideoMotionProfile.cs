using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DG.Tweening;
using Hit = DamageFontStudio.Editor.DamageVideoAutoAnalyzer.Hit;
using Result = DamageFontStudio.Editor.DamageVideoRefiner.Result;

namespace DamageFontStudio.Editor
{
    // Fit animation independently of glyph identity and width. All hits of the same
    // measured style reuse these channels, including when the displayed number changes.
    internal sealed class DamageVideoMotionProfile
    {
        internal int family, observations;
        internal float duration, initialHeight;
        internal readonly List<float> times = new List<float>(), horizontal = new List<float>(), rise = new List<float>(), scale = new List<float>(), alpha = new List<float>();
        static float Quantile(IEnumerable<float> values, float p)
        { var a = values.OrderBy(v => v).ToArray(); return a.Length == 0 ? 0 : a[Mathf.Clamp(Mathf.RoundToInt((a.Length - 1) * p), 0, a.Length - 1)]; }
        internal static Dictionary<int, DamageVideoMotionProfile> Learn(List<Result> results, IReadOnlyDictionary<int, double> stamps, IReadOnlyDictionary<int, Vector2> camera)
        {
            var profiles = new Dictionary<int, DamageVideoMotionProfile>();
            double Time(Hit h) => stamps[h.frame];
            Vector2 Camera(int f) => camera.TryGetValue(f, out var v) ? v : Vector2.zero;
            float Span(Result r) => (float)(Time(r.hits.Last()) - Time(r.hits[0]));
            foreach (int family in new[] { 1, 2 })
            {
                var candidates = results.Where(r => !r.inferred && r.coarse.First.family == family && r.hits.Count >= 8 && Span(r) >= .25f && r.meanCorrelation >= .63f).ToList();
                if (candidates.Count == 0) continue;
                float Pop(Result r) => r.hits.Take(3).Max(h => h.size.y) / Mathf.Max(1, Quantile(r.hits.Skip(r.hits.Count / 3).Select(h => h.size.y), .5f));
                var full = candidates.Where(r => Pop(r) >= 1.15f && r.hits.Last().contrast < Quantile(r.hits.Select(h => h.contrast), .8f) * .6f).ToList();
                if (full.Count < 2) full = candidates.Where(r => Span(r) >= Quantile(candidates.Select(Span), .65f)).ToList();
                full = full.OrderByDescending(r => Span(r) * r.meanCorrelation).Take(6).ToList();
                var profile = new DamageVideoMotionProfile { family = family, observations = full.Count,
                    duration = Quantile(full.Select(r => Span(r) + (float)(stamps[Math.Min(stamps.Keys.Max(), r.hits.Last().frame + 1)] - Time(r.hits.Last()))), .5f),
                    initialHeight = Quantile(full.Select(r => r.hits[0].size.y * r.appearanceRatio.y), .5f) };
                float step = Quantile(stamps.OrderBy(s => s.Key).Zip(stamps.OrderBy(s => s.Key).Skip(1), (a, b) => (float)(b.Value - a.Value)), .5f);
                int count = Mathf.Clamp(Mathf.CeilToInt(profile.duration / Mathf.Max(.005f, step)), 2, 120);
                for (int i = 0; i <= count; i++)
                {
                    float age = profile.duration * i / count;
                    var xValues = new List<float>(); var yValues = new List<float>(); var scales = new List<float>(); var opacities = new List<float>();
                    foreach (var r in full)
                    {
                        var first = r.hits[0]; double t = Time(first) + age;
                        float peak = Quantile(r.hits.Select(h => h.contrast), .8f);
                        if (t > Time(r.hits.Last()))
                        {
                            var end = r.hits.Last();
                            xValues.Add(end.center.x - Camera(end.frame).x - first.center.x + Camera(first.frame).x);
                            yValues.Add(first.center.y - Camera(first.frame).y - end.center.y + Camera(end.frame).y);
                            scales.Add(end.size.y / first.size.y);
                            if (r.closed) opacities.Add(0);
                            continue;
                        }
                        var a = first; var b = r.hits.Last();
                        for (int k = 1; k < r.hits.Count; k++) if (Time(r.hits[k]) >= t) { a = r.hits[k - 1]; b = r.hits[k]; break; }
                        float fraction = (float)((t - Time(a)) / Math.Max(.00001, Time(b) - Time(a)));
                        float worldY = Mathf.Lerp(a.center.y - Camera(a.frame).y, b.center.y - Camera(b.frame).y, fraction);
                        float worldX = Mathf.Lerp(a.center.x - Camera(a.frame).x, b.center.x - Camera(b.frame).x, fraction);
                        xValues.Add(worldX - first.center.x + Camera(first.frame).x);
                        yValues.Add(first.center.y - Camera(first.frame).y - worldY);
                        scales.Add(Mathf.Lerp(a.size.y, b.size.y, fraction) / first.size.y);
                        opacities.Add(Mathf.Clamp01(Mathf.Lerp(a.contrast, b.contrast, fraction) / Mathf.Max(.01f, peak)));
                    }
                    profile.times.Add(age);
                    profile.horizontal.Add(xValues.Count > 0 ? Quantile(xValues, .5f) : profile.horizontal.LastOrDefault());
                    profile.rise.Add(yValues.Count > 0 ? Quantile(yValues, .5f) : profile.rise.LastOrDefault());
                    profile.scale.Add(scales.Count > 0 ? Quantile(scales, .5f) : profile.scale.LastOrDefault());
                    profile.alpha.Add(i == count ? 0 : Quantile(opacities, .8f));
                }
                profile.horizontal[0] = 0; profile.rise[0] = 0; profile.scale[0] = 1;
                profiles.Add(family, profile);
            }
            return profiles;
        }
        internal void Apply(DamageFontPreset preset)
        {
            preset.initialScale = Vector3.one; preset.initialRotation = 0; preset.initialOpacity = alpha[0]; preset.segments.Clear();
            for (int i = 1; i < times.Count; i++)
            {
                float start = times[i - 1], length = times[i] - start;
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, start = start, duration = length, targetVector = new Vector3(horizontal[i], rise[i], 0), ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Scale, start = start, duration = length, targetVector = new Vector3(scale[i], scale[i], 1), ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Opacity, start = start, duration = length, targetValue = alpha[i], ease = Ease.Linear });
            }
        }
    }
}
