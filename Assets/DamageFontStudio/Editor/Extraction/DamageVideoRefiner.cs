using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Track = DamageFontStudio.Editor.DamageVideoAutoAnalyzer.Track;
using Hit = DamageFontStudio.Editor.DamageVideoAutoAnalyzer.Hit;

namespace DamageFontStudio.Editor
{
    internal sealed class DamageVideoRefiner
    {
        internal sealed class Result
        {
            internal Track coarse;
            internal readonly List<Hit> hits = new List<Hit>();
            internal int firstMissingFrame = -1;
            internal float meanCorrelation;
            internal float[] template;
            internal int missingFrames;
            internal bool closed;
            internal int backwardMissing;
            internal bool birthClosed;
            internal bool inferred;
            internal Vector2 appearanceRatio = Vector2.one;
            internal int measuredFrames;
            internal bool regionTruncatedStart, regionTruncatedEnd;
        }
        readonly List<Result> results;
        readonly float[] scratch = new float[DamageVideoSignal.Columns * DamageVideoSignal.Rows];
        readonly int first, last;
        readonly IReadOnlyDictionary<int, Vector2> cameraOffsets;
        readonly Rect? analysisRegion;
        internal IEnumerable<string> Diagnostics => results.Select(r => $"REFINE coarse={r.coarse.First.frame}-{r.coarse.Last.frame} hits={r.hits.Count} range={(r.hits.Count > 0 ? r.hits[0].frame : -1)}-{(r.hits.Count > 0 ? r.hits.Last().frame : -1)} correlation={r.meanCorrelation:0.000} box={r.coarse.First.size} regionCut={r.regionTruncatedStart}/{r.regionTruncatedEnd}");
        Vector2 CameraAt(int frame) => cameraOffsets != null && cameraOffsets.TryGetValue(frame, out var value) ? value : Vector2.zero;
        internal DamageVideoRefiner(List<Track> tracks, int first, int last, IReadOnlyDictionary<int, Vector2> cameraOffsets = null, Rect? analysisRegion = null)
        {
            this.first = first; this.last = last;
            this.cameraOffsets = cameraOffsets;
            this.analysisRegion = analysisRegion;
            tracks = tracks.Where(t => t.hits.Count > 0 && t.hits.All(h => DamageVideoAnalysisRegion.Contains(analysisRegion, h.center, h.size))).ToList();
            var prototypes = tracks.SelectMany(t => t.hits).Where(h => h.text?.Length >= 2 && h.textConfidence > .65f).ToList();
            results = tracks.Select(t =>
            {
                float medianHeight = t.hits.Select(h => h.size.y).OrderBy(h => h).ElementAt(t.hits.Count / 2);
                var anchor = t.hits.Where(h => h.size.y < medianHeight * 1.25f).OrderByDescending(h => h.contrast).FirstOrDefault() ?? t.First;
                float bestPrototype = .85f;
                float anchorAspect = anchor.size.x / anchor.size.y;
                foreach (var candidate in prototypes.Where(h => !string.IsNullOrEmpty(anchor.text) && h.text == anchor.text && h.family == t.First.family && Mathf.Abs(Mathf.Log((h.size.x / h.size.y) / anchorAspect)) < .35f))
                {
                    float similarity = t.hits.Max(h => DamageVideoSignal.Compare(candidate.signal, h.signal, out _));
                    float quality = similarity * .7f + candidate.textConfidence * .3f;
                    if (quality > bestPrototype) { bestPrototype = quality; anchor = candidate; }
                }
                var styles = t.hits.Where(h => h.learned).ToList();
                var ratio = styles.Count == 0 ? Vector2.one : new Vector2(styles.Select(h => h.appearanceRatio.x).OrderBy(v => v).ElementAt(styles.Count / 2), styles.Select(h => h.appearanceRatio.y).OrderBy(v => v).ElementAt(styles.Count / 2));
                return new Result { coarse = t, template = anchor.maskSignal ?? anchor.signal, appearanceRatio = ratio };
            }).Where(r => r.template != null).ToList();
        }

        internal void AddFrame(Color32[] pixels, int w, int h, int frame)
        {
            foreach (var r in results)
            {
                if (r.closed || frame < r.coarse.First.frame - 3 || frame > r.coarse.Last.frame + 24) continue;
                var predicted = Predict(r.coarse, frame);
                if (frame > r.coarse.Last.frame && r.hits.Count > 1)
                {
                    var b = r.hits.Last(); var a = r.hits[Math.Max(0, r.hits.Count - 3)];
                    predicted.center = b.center + (b.center - a.center - CameraAt(b.frame) + CameraAt(a.frame)) * ((frame - b.frame) / (float)Math.Max(1, b.frame - a.frame)) + CameraAt(frame) - CameraAt(b.frame);
                    predicted.size = b.size;
                }
                if (!DamageVideoAnalysisRegion.Contains(analysisRegion, predicted.center, predicted.size))
                {
                    if (r.hits.Count > 0) { r.regionTruncatedEnd = true; r.closed = true; }
                    continue;
                }
                float best = -1, amplitude = 0, correlation = 0;
                Vector2 center = predicted.center, size = predicted.size;
                int yRadius = Mathf.Max(4, Mathf.RoundToInt(predicted.size.y * .3f));
                foreach (float factor in new[] { .6f, .75f, .9f, 1f, 1.15f, 1.4f })
                    for (int y = -yRadius; y <= yRadius; y += 2) for (int x = -4; x <= 4; x += 2)
                    {
                        var c = predicted.center + new Vector2(x, y); var s = predicted.size * factor;
                        if (!DamageVideoAnalysisRegion.Contains(analysisRegion, c, s)) continue;
                        DamageVideoSignal.Fill(pixels, w, h, c, s, r.coarse.First.family, scratch);
                        float score = DamageVideoSignal.Compare(r.template, scratch, out float alpha);
                        float penalized = score - (x * x + y * y) * .0007f;
                        if (penalized <= best) continue;
                        best = penalized; correlation = score; amplitude = alpha; center = c; size = s;
                    }
                // The absence of a convincing match is recorded, rather than fabricating a two-frame fade.
                bool visible = correlation >= .53f && amplitude >= .13f;
                if (visible)
                {
                    r.missingFrames = 0; r.firstMissingFrame = -1;
                    r.hits.Add(new Hit { frame = frame, family = r.coarse.First.family, center = center, size = size,
                        contrast = amplitude, color = r.coarse.First.color });
                    r.meanCorrelation += correlation;
                }
                else if (r.hits.Count > 0 && !DamageVideoAnalysisRegion.Contains(analysisRegion, predicted.center, predicted.size))
                { r.regionTruncatedEnd = true; r.closed = true; }
                else if (r.hits.Count > 0 && frame > r.coarse.Last.frame)
                {
                    if (r.firstMissingFrame < 0) r.firstMissingFrame = frame;
                    if (++r.missingFrames >= 2) r.closed = true;
                }
            }
        }

        internal void AddBackwardFrame(Color32[] pixels, int w, int h, int frame)
        {
            foreach (var r in results)
            {
                if (r.birthClosed || r.hits.Count < 2 || frame >= r.hits[0].frame || frame < r.coarse.First.frame - 24) continue;
                var b = r.hits[0]; var a = r.hits[Math.Min(2, r.hits.Count - 1)];
                var predicted = b.center + (b.center - a.center - CameraAt(b.frame) + CameraAt(a.frame)) * ((b.frame - frame) / (float)Math.Max(1, a.frame - b.frame)) + CameraAt(frame) - CameraAt(b.frame);
                if (!DamageVideoAnalysisRegion.Contains(analysisRegion, predicted, b.size))
                { r.regionTruncatedStart = true; r.birthClosed = true; continue; }
                float best = -1, alpha = 0, correlation = 0; Vector2 center = predicted, size = b.size;
                foreach (float factor in new[] { .9f, 1f, 1.15f, 1.4f, 1.7f })
                    for (int y = -6; y <= 6; y += 2) for (int x = -6; x <= 6; x += 2)
                    {
                        var c = predicted + new Vector2(x, y); var s = b.size * factor;
                        if (!DamageVideoAnalysisRegion.Contains(analysisRegion, c, s)) continue;
                        DamageVideoSignal.Fill(pixels, w, h, c, s, r.coarse.First.family, scratch);
                        float score = DamageVideoSignal.Compare(r.template, scratch, out float amplitude);
                        float penalized = score - (x * x + y * y) * .0007f;
                        if (penalized <= best) continue;
                        best = penalized; alpha = amplitude; correlation = score; center = c; size = s;
                    }
                if (correlation >= .58f && alpha >= .13f)
                {
                    r.backwardMissing = 0;
                    r.hits.Insert(0, new Hit { frame = frame, center = center, size = size, family = b.family, contrast = alpha, color = b.color });
                    r.meanCorrelation += correlation;
                }
                else if (!DamageVideoAnalysisRegion.Contains(analysisRegion, predicted, b.size))
                { r.regionTruncatedStart = true; r.birthClosed = true; }
                else if (++r.backwardMissing >= 2) r.birthClosed = true;
            }
        }

        Hit Predict(Track track, int frame)
        {
            var a = track.First; var b = track.Last;
            if (frame < a.frame) b = track.hits[Math.Min(2, track.hits.Count - 1)];
            else if (frame > b.frame) a = track.hits[Math.Max(0, track.hits.Count - 3)];
            else
                for (int i = 1; i < track.hits.Count; i++)
                    if (track.hits[i].frame >= frame) { a = track.hits[i - 1]; b = track.hits[i]; break; }
            float t = (frame - a.frame) / (float)Math.Max(1, b.frame - a.frame);
            return new Hit { center = Vector2.LerpUnclamped(a.center, b.center, t) + CameraAt(frame) - Vector2.LerpUnclamped(CameraAt(a.frame), CameraAt(b.frame), t), size = t < 0 ? a.size : t > 1 ? b.size : Vector2.Lerp(a.size, b.size, t) };
        }

        internal List<Result> Finish()
        {
            var accepted = new List<Result>();
            foreach (var r in results) r.meanCorrelation /= Math.Max(1, r.hits.Count);
            foreach (var r in results.OrderByDescending(r => r.hits.Count))
            {
                if (r.hits.Count < 5 || r.hits.Last().frame - r.hits[0].frame < 5) continue;
                bool identified = r.coarse.hits.Count(h => h.learned && h.textConfidence >= .85f) >= 2 || r.coarse.hits.Count(h => h.templateMatched) >= 5;
                var origin = r.hits[0].center - CameraAt(r.hits[0].frame);
                float movement = r.hits.Max(h => Vector2.Distance(h.center - CameraAt(h.frame), origin));
                if (r.meanCorrelation < (identified ? .56f : .62f) || movement < r.hits.Average(h => h.size.y) * 1.2f) continue;
                bool duplicate = accepted.Any(other =>
                {
                    var shared = r.hits.Where(a => other.hits.Any(b => b.frame == a.frame &&
                        Mathf.Abs(a.center.y - b.center.y) < Mathf.Min(a.size.y * r.appearanceRatio.y, b.size.y * other.appearanceRatio.y) * .3f &&
                        Mathf.Abs(a.center.x - b.center.x) < Mathf.Max(a.size.x * r.appearanceRatio.x, b.size.x * other.appearanceRatio.x) * .35f)).Count();
                    return shared > Math.Min(r.hits.Count, other.hits.Count) * .65f;
                });
                if (!duplicate)
                {
                    var first = r.hits[0]; float peak = r.hits.Max(h => h.contrast);
                    foreach (var hit in r.coarse.hits.Where(h => h.frame < first.frame && h.frame >= first.frame - 3 && h.learned && !h.templateMatched && h.textConfidence >= .8f).OrderByDescending(h => h.frame))
                        r.hits.Insert(0, new Hit { frame = hit.frame, family = hit.family, center = hit.center, size = hit.size, color = hit.color, contrast = peak });
                    r.measuredFrames = r.hits.Count; accepted.Add(r);
                }
            }
            RecoverOccludedBirths(accepted);
            // Separate detectors can follow different pieces of the same glyph row.
            // Compare births as well as their later paths: overlap can make those
            // paths diverge even though they began as the same hit.
            var unique = new List<Result>();
            foreach (var r in accepted.OrderByDescending(r => !r.inferred).ThenByDescending(r => r.meanCorrelation))
            {
                var birth = r.hits[0];
                bool duplicate = unique.Any(other =>
                {
                    var b = other.hits[0];
                    float height = Mathf.Max(birth.size.y * r.appearanceRatio.y, b.size.y * other.appearanceRatio.y);
                    var delta = birth.center - CameraAt(birth.frame) - b.center + CameraAt(b.frame);
                    return birth.family == b.family && Math.Abs(birth.frame - b.frame) <= 1 &&
                        Mathf.Abs(delta.y) < height * .65f && Mathf.Abs(delta.x) < height;
                });
                if (!duplicate) unique.Add(r);
            }
            return unique.OrderBy(r => r.hits[0].frame).ToList();
        }

        void RecoverOccludedBirths(List<Result> accepted)
        {
            var measured = accepted.ToArray();
            foreach (int family in new[] { 1, 2 })
            {
                var reliable = measured.Where(r => r.coarse.First.family == family && r.hits.Count >= (family == 2 ? 7 : 15) && r.meanCorrelation >= .68f).ToList();
                if (reliable.Count == 0) continue;
                var prototype = reliable.OrderByDescending(r => r.hits[0].size.y / r.hits.Skip(r.hits.Count / 3).Average(h => h.size.y) * r.meanCorrelation).First();
                var sizes = results.SelectMany(r => r.coarse.hits).Where(h => h.family == family && h.learned && !h.templateMatched && h.appearanceRatio.y < .95f).Select(h => h.size.y * h.appearanceRatio.y).OrderBy(v => v).ToArray();
                if (sizes.Length == 0) sizes = reliable.SelectMany(r => r.hits.Skip(r.hits.Count / 3).Take(r.hits.Count / 3).Select(h => h.size.y * r.appearanceRatio.y)).OrderBy(v => v).ToArray();
                float mature = sizes[sizes.Length / 2];
                foreach (var candidate in results.Where(r => !accepted.Contains(r) && r.coarse.First.family == family).OrderBy(r => r.coarse.First.frame))
                {
                    var birth = candidate.coarse.hits.FirstOrDefault(h => ((h.learned && h.textConfidence >= .85f) || (h.templateMatched && h.matchCorrelation >= .7f && candidate.coarse.hits.Count >= 2)) && h.text?.Length >= 2 && h.size.y * h.appearanceRatio.y > mature * 1.3f);
                    if (birth == null) continue;
                    int observations = results.SelectMany(r => r.coarse.hits).Where(h => h.text == birth.text && ((h.learned && h.textConfidence >= .8f) || (h.templateMatched && h.matchCorrelation >= .65f))).Select(h => h.frame).Distinct().Count();
                    if (observations < 3) continue;
                    Vector2 world = birth.center - CameraAt(birth.frame);
                    if (accepted.Any(r => Math.Abs(r.hits[0].frame - birth.frame) < 2 &&
                        Vector2.Distance(r.hits[0].center - CameraAt(r.hits[0].frame), world) < mature * 2)) continue;
                    // A clear large birth may be hidden immediately by the next hit.
                    // Continue it with a measured family profile, explicitly marked as
                    // inferred rather than treating occlusion as observed disappearance.
                    var initial = prototype.hits[0];
                    candidate.hits.Clear(); candidate.inferred = true; candidate.closed = false;
                    candidate.measuredFrames = candidate.coarse.hits.Count;
                    candidate.appearanceRatio = birth.appearanceRatio;
                    foreach (var observation in prototype.hits)
                    {
                        int f = birth.frame + observation.frame - initial.frame;
                        if (f > last) break;
                        var displacement = observation.center - initial.center - CameraAt(observation.frame) + CameraAt(initial.frame);
                        var center = birth.center + displacement + CameraAt(f) - CameraAt(birth.frame);
                        var size = Vector2.Scale(birth.size, new Vector2(observation.size.x / initial.size.x, observation.size.y / initial.size.y));
                        if (!DamageVideoAnalysisRegion.Contains(analysisRegion, center, size)) { candidate.regionTruncatedEnd = true; break; }
                        candidate.hits.Add(new Hit { frame = f, family = family, color = birth.color,
                            center = center, size = size,
                            contrast = observation.contrast });
                    }
                    if (candidate.hits.Count >= 5) accepted.Add(candidate);
                }
            }
        }

        internal DamageVideoSession MakeSession(Result result, string path, int width, int height, int frameCount, double fps, bool lockX, IReadOnlyDictionary<int, double> timestamps)
        {
            var session = ScriptableObject.CreateInstance<DamageVideoSession>();
            session.videoPath = path; session.width = width; session.height = height; session.frameCount = frameCount; session.fps = fps;
            session.autoAppearance = true; session.lockHorizontalPosition = lockX; session.sampleStep = 1;
            session.startFrame = result.hits[0].frame;
            var end = result.hits.Last();
            session.endFrame = Math.Min(frameCount - 1, end.frame + 1);
            session.observedEnd = result.closed && !result.regionTruncatedEnd;
            session.regionTruncatedStart = result.regionTruncatedStart; session.regionTruncatedEnd = result.regionTruncatedEnd;
            session.motionInferred = result.inferred;
            session.color = end.family == 1 ? Color.white : result.coarse.hits.OrderByDescending(h => h.contrast).First().color;
            if (end.family != 1)
            {
                float brightness = Mathf.Max(session.color.r, session.color.g, session.color.b);
                if (brightness > .01f) session.color = new Color(session.color.r / brightness, session.color.g / brightness, session.color.b / brightness, 1);
            }
            var amplitudes = result.hits.Select(h => h.contrast).OrderBy(v => v).ToArray();
            float peak = amplitudes[Math.Min(amplitudes.Length - 1, (int)(amplitudes.Length * .8f))];
            foreach (var hit in result.hits)
                session.Put(new DamageVideoSample { frame = hit.frame, center = hit.center, size = hit.size,
                    timestamp = timestamps.TryGetValue(hit.frame, out var stamp) ? stamp : -1,
                    opacity = Mathf.Clamp01(hit.contrast / Mathf.Max(.01f, peak)), confidence = result.meanCorrelation, manual = false });
            foreach (var sample in session.samples) sample.size = Vector2.Scale(sample.size, result.appearanceRatio);
            if (session.endFrame > end.frame)
                session.Put(new DamageVideoSample { frame = session.endFrame, timestamp = timestamps.TryGetValue(session.endFrame, out var finalStamp) ? finalStamp : -1,
                    center = end.center, size = Vector2.Scale(end.size, result.appearanceRatio), opacity = result.regionTruncatedEnd ? session.samples.Last().opacity : 0, confidence = result.meanCorrelation, manual = false });
            var words = result.coarse.hits.Where(h => h.learned && !string.IsNullOrEmpty(h.text) && h.textConfidence > .8f).ToList();
            if (words.Count == 0) words = result.coarse.hits.Where(h => !string.IsNullOrEmpty(h.text) && h.textConfidence > .6f).ToList();
            var recognized = words
                .GroupBy(h => h.text).OrderByDescending(g => g.Count() * g.Average(h => h.textConfidence)).FirstOrDefault();
            session.detectedNumber = recognized?.Key ?? "";
            session.numberConfidence = recognized != null ? recognized.Average(h => h.textConfidence) : 0;
            session.number = string.IsNullOrEmpty(session.detectedNumber) ? "189" : session.detectedNumber;
            return session;
        }
    }
}
