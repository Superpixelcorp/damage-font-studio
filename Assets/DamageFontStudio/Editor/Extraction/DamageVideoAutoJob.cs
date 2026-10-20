using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    internal sealed class DamageVideoAutoJob : IDisposable
    {
        [Serializable] sealed class Summary
        {
            public string name; public int firstFrame, endFrame; public double onset, duration;
            public float correlation; public bool observedDisappearance;
            public bool motionInferred;
            public string estimatedText; public float textConfidence;
            public int missingFrames;
            public bool sharedMotion;
        }
        [Serializable] sealed class Report
        {
            public string video; public int width, height; public double fps, startTime, endTime;
            public bool lockHorizontalPosition, cameraMotionRemoved; public Summary[] samples; public string[] limitations;
            public bool limitAnalysisRegion; public Rect analysisRegion;
            public string gameMotion; public int initialGameMotionSourceIndex;
            public string timeBasis;
            public MotionSummary[] motionProfiles;
        }
        [Serializable] sealed class MotionSummary { public int family, sourceTracks; public float duration, horizontalPixels, risePixels, peakScale, initialHeight; }
        DamageVideoReader reader;
        DamageVideoAutoAnalyzer analyzer;
        DamageVideoRefiner refiner;
        DamageVideoTextDetector textDetector;
        DamageVideoWordSearch wordSearch;
        DamageVideoAutoAnalyzer recovered;
        List<DamageVideoAutoAnalyzer.Track> candidates;
        string[] wordBankInfo = Array.Empty<string>();
        readonly Dictionary<int, double> timestamps = new Dictionary<int, double>();
        readonly string path;
        readonly float startSeconds, endSeconds;
        readonly Action<List<GameObject>, string> completed;
        readonly Action<string> failed;
        readonly Action changed;
        readonly bool lockHorizontalPosition;
        readonly bool removeCameraMotion;
        readonly Rect? requestedRegion;
        DamageVideoAnalysisRegion region;
        int next = -1, first, last, rangeFirst, rangeLast, phase;
        bool disposed;
        internal float Progress { get; private set; }
        internal string OutputFolder { get; private set; }
        internal IEnumerable<string> DiagnosticTracks => (analyzer?.Diagnostics ?? Array.Empty<string>()).Concat(wordBankInfo).Concat(recovered == null ? Array.Empty<string>() : recovered.Diagnostics.Select(s => "GPU " + s)).Concat(refiner?.Diagnostics ?? Array.Empty<string>());
        internal string Stage => phase == 0 ? "Detecting hit events" : phase == 1 ? "Finding overlapping repeated numbers" : phase == 2 ? "Measuring motion and fade" : "Recovering hit onset";

        internal DamageVideoAutoJob(string path, float startSeconds, float endSeconds,
            Action<List<GameObject>, string> completed, Action<string> failed, Action changed = null, bool lockHorizontalPosition = false, bool removeCameraMotion = true, Rect? analysisRegion = null)
        {
            this.path = path; this.startSeconds = startSeconds; this.endSeconds = endSeconds;
            this.completed = completed; this.failed = failed; this.changed = changed; this.lockHorizontalPosition = lockHorizontalPosition;
            this.removeCameraMotion = removeCameraMotion;
            requestedRegion = analysisRegion;
            if (!float.IsFinite(startSeconds) || !float.IsFinite(endSeconds) || startSeconds < 0 || endSeconds < 0 || (endSeconds > 0 && endSeconds <= startSeconds))
                throw new ArgumentException("Choose a valid analysis range (end 0 means the entire video).");
            reader = new DamageVideoReader(path); reader.Prepared += Prepared; reader.Error += Fail;
            EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }
        void PlayModeChanged(PlayModeStateChange _) => Dispose();
        void Prepared()
        {
            // The user's range is a hard boundary for detection, learning and export.
            (first, last) = SelectRange(reader.FrameCount, reader.TimeAtFrame, startSeconds, endSeconds);
            if (first < 0 || last <= first || reader.TimeAtFrame(last) - reader.TimeAtFrame(first) < .3 - .000001)
            { Fail("Choose at least 0.3 seconds for analysis."); return; }
            rangeFirst = first; rangeLast = last;
            try
            {
                if (requestedRegion.HasValue) region = new DamageVideoAnalysisRegion(requestedRegion.Value, reader.Width, reader.Height);
                textDetector = new DamageVideoTextDetector();
                analyzer = new DamageVideoAutoAnalyzer(first, last, 1, reader.Fps, region?.Bounds) { textDetector = textDetector }; next = first;
            }
            catch (Exception e) { Fail(e.Message); }
        }
        internal static (int first, int last) SelectRange(int count, Func<int, double> timeAtFrame, double start, double end)
        {
            int first = -1, last = -1;
            for (int f = 0; f < count; f++)
            {
                double time = timeAtFrame(f);
                if (time < start - .000001 || (end > 0 && time > end + .000001)) continue;
                if (first < 0) first = f;
                last = f;
            }
            return (first, last);
        }
        void Tick()
        {
            if (disposed || reader == null || !reader.Ready || reader.Busy || next < 0) return;
            int frame = next; next = -1;
            try
            {
                reader.Seek(frame, decoded =>
                {
                    if (disposed) return;
                    try
                    {
                        var pixels = reader.ReadPixels();
                        var input = region != null ? region.Apply(pixels, reader.Width, reader.Height) : pixels;
                        if (phase == 0)
                        {
                            timestamps[decoded] = reader.CurrentTime;
                            analyzer.AddFrame(input, reader.Width, reader.Height, decoded, pixels);
                        }
                        else if (phase == 1) recovered.AddRecoveredFrame(wordSearch.Find(input, decoded), decoded, analyzer.CameraOffsets[decoded]);
                        else if (phase == 2) refiner.AddFrame(input, reader.Width, reader.Height, decoded);
                        else refiner.AddBackwardFrame(input, reader.Width, reader.Height, decoded);
                        Progress = (phase + (phase == 3 ? last - decoded : decoded - first) / (float)Math.Max(1, last - first)) / 4f; changed?.Invoke();
                        if (phase == 3 && decoded > first) next = decoded - 1;
                        else if (phase < 3 && decoded < last) next = decoded + 1;
                        else if (phase == 0)
                        {
                            candidates = analyzer.Finish(true);
                            if (candidates.Count == 0) { Fail(NoTracksMessage); return; }
                            wordSearch = new DamageVideoWordSearch(analyzer.WordObservations.Where(h => h.frame >= rangeFirst && h.frame <= rangeLast), reader.Width, reader.Height, region?.Bounds);
                            wordBankInfo = wordSearch.Diagnostics.ToArray();
                            if (wordSearch.Ready) { recovered = new DamageVideoAutoAnalyzer(first, last, 1, reader.Fps, region?.Bounds); phase = 1; }
                            else { refiner = new DamageVideoRefiner(candidates, first, last, analyzer.CameraOffsets, region?.Bounds); phase = 2; }
                            next = first;
                        }
                        else if (phase == 1)
                        {
                            candidates.AddRange(recovered.Finish(true));
                            refiner = new DamageVideoRefiner(candidates, first, last, analyzer.CameraOffsets, region?.Bounds); phase = 2; next = first;
                            wordSearch.Dispose(); wordSearch = null;
                        }
                        else if (phase == 2) { phase = 3; next = last; }
                        else SaveResults();
                    }
                    catch (Exception ex) { Debug.LogException(ex); Fail(ex.Message); }
                });
            }
            catch (Exception ex) { Fail(ex.Message); }
        }
        string NoTracksMessage => region != null ? "No tracks found inside the selected area. Include the full motion path or choose a clearer range." : "No complete floating-number tracks found. Try a clearer range or use manual extraction.";
        void SaveResults()
        {
            var results = refiner.Finish().Where(r => r.hits.Last().frame > rangeFirst && r.hits[0].frame < rangeLast).ToList();
            if (results.Count == 0) { Fail(NoTracksMessage); return; }
            var motionProfiles = DamageVideoMotionProfile.Learn(results, timestamps, analyzer.CameraOffsets);
            if (!AssetDatabase.IsValidFolder("Assets/DamageFontStudio/Generated")) AssetDatabase.CreateFolder("Assets/DamageFontStudio", "Generated");
            OutputFolder = AssetDatabase.GenerateUniqueAssetPath("Assets/DamageFontStudio/Generated/Video-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            var prefabs = new List<GameObject>(); var summaries = new List<Summary>();
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>();
            sequence.name = "RecordedHits"; sequence.sourceWidth = reader.Width; sequence.sourceHeight = reader.Height;
            sequence.sourceFps = reader.Fps; sequence.sourceStartFrame = rangeFirst; sequence.sourceEndFrame = rangeLast;
            sequence.sourceStartTime = timestamps[rangeFirst];
            sequence.duration = (float)(timestamps[rangeLast] - sequence.sourceStartTime);
            sequence.lockSpawnX = lockHorizontalPosition;
            sequence.cameraMotionRemoved = removeCameraMotion;
            var spawnPositions = results.Select(r => r.hits[0].center.x).OrderBy(x => x).ToArray();
            sequence.spawnX = spawnPositions[spawnPositions.Length / 2];
            foreach (var pair in timestamps.OrderBy(p => p.Key)) { sequence.frames.Add(pair.Key); sequence.times.Add((float)(pair.Value - sequence.sourceStartTime)); }
            int index = 0;
            string analysisFolder = OutputFolder + "/Analysis";
            foreach (var result in results)
            {
                var session = refiner.MakeSession(result, path, reader.Width, reader.Height, reader.FrameCount, reader.Fps, lockHorizontalPosition, timestamps);
                if (!session.ClipToRange(rangeFirst, rangeLast, timestamps)) { UnityEngine.Object.DestroyImmediate(session); continue; }
                session.limitAnalysisRegion = region != null;
                session.analysisRegion = region?.Bounds ?? new Rect(0, 0, reader.Width, reader.Height);
                session.removeCameraMotion = removeCameraMotion;
                foreach (var sample in session.samples)
                    sample.cameraOffset = analyzer.CameraOffsets.TryGetValue(sample.frame, out var offset) ? offset : Vector2.zero;
                motionProfiles.TryGetValue(result.coarse.First.family, out var motion);
                string name = (result.coarse.First.family == 1 ? "Light" : "Pink") + "-" + (++index).ToString("D2");
                GameObject prefab;
                // Observed tracks retain their own XY, scale and alpha samples.
                // A shared profile may only complete an explicitly inferred track.
                var completion = region == null && removeCameraMotion && result.inferred && !session.rangeTruncatedStart && !session.rangeTruncatedEnd && motion != null &&
                    motion.duration <= session.TimeOf(session.samples.Last()) - session.TimeOf(session.samples[0]) + .000001 ? motion : null;
                try { prefab = DamageVideoSampleExporter.Save(session, analysisFolder, name, completion); prefabs.Add(prefab); }
                catch { if (!EditorUtility.IsPersistent(session)) UnityEngine.Object.DestroyImmediate(session); throw; }
                var initial = session.samples[0];
                float onset = (float)(session.TimeOf(initial) - sequence.sourceStartTime);
                var sourceX = new AnimationCurve();
                var sourceY = new AnimationCurve();
                if (removeCameraMotion || completion != null)
                {
                    var cameraOrigin = analyzer.CameraOffsets[session.startFrame];
                    float length = prefab.GetComponent<DamageFontObject>().preset.Duration;
                    foreach (var pair in timestamps.Where(p => p.Key >= session.startFrame && p.Key <= session.endFrame && p.Value <= session.TimeOf(initial) + length + .000001))
                    {
                        float t = (float)(pair.Value - session.TimeOf(initial)); var camera = analyzer.CameraOffsets[pair.Key] - cameraOrigin;
                        sourceX.AddKey(t, camera.x); sourceY.AddKey(t, -camera.y);
                    }
                }
                foreach (var curve in new[] { sourceX, sourceY }) for (int k = 0; k < curve.length; k++)
                { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear); }
                sequence.events.Add(new DamageFontSequenceEvent { time = onset, position = new Vector3(initial.center.x, -initial.center.y, 0), sample = prefab,
                    motionPreset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>(analysisFolder + "/" + name + "-Motion.asset"),
                    sourceCameraOffset = removeCameraMotion ? new Vector3(initial.cameraOffset.x, -initial.cameraOffset.y, 0) : Vector3.zero,
                    observedText = session.detectedNumber, textConfidence = session.numberConfidence,
                    observedDisappearance = session.observedEnd, motionInferred = result.inferred, missingFrames = Math.Max(0, session.endFrame - session.startFrame - result.measuredFrames),
                    confidence = result.meanCorrelation, firstObservedFrame = session.startFrame, lastObservedFrame = session.endFrame,
                    sourceHorizontalMotion = sourceX, sourceVerticalMotion = sourceY });
                summaries.Add(new Summary { name = name, firstFrame = session.startFrame, endFrame = session.endFrame, onset = onset,
                    duration = prefab.GetComponent<DamageFontObject>().preset.Duration, correlation = result.meanCorrelation, observedDisappearance = session.observedEnd, motionInferred = result.inferred,
                    estimatedText = session.detectedNumber, textConfidence = session.numberConfidence, sharedMotion = completion != null });
                summaries.Last().missingFrames = Math.Max(0, session.endFrame - session.startFrame - result.measuredFrames);
            }
            AssetDatabase.CreateAsset(sequence, OutputFolder + "/RecordedHits.asset"); AssetDatabase.SaveAssetIfDirty(sequence);
            var final = DamageVideoFinalMotion.Ensure(sequence);
            File.WriteAllText(analysisFolder + "/Analysis.json", JsonUtility.ToJson(new Report
            {
                video = path, width = reader.Width, height = reader.Height, fps = reader.Fps, startTime = sequence.sourceStartTime,
                endTime = sequence.sourceStartTime + sequence.duration, lockHorizontalPosition = lockHorizontalPosition, cameraMotionRemoved = removeCameraMotion, samples = summaries.ToArray(),
                limitAnalysisRegion = region != null, analysisRegion = region?.Bounds ?? new Rect(0, 0, reader.Width, reader.Height),
                gameMotion = DamageVideoFinalMotion.FileName, initialGameMotionSourceIndex = final.sourceHitIndex,
                timeBasis = reader.HasPresentationTimes ? "MP4 presentation timestamps" : "Decoder clock fallback (timing precision not guaranteed)",
                motionProfiles = motionProfiles.Values.Select(p => new MotionSummary { family = p.family, sourceTracks = p.observations, duration = p.duration, horizontalPixels = p.horizontal.Last(), risePixels = p.rise.Last(), peakScale = p.scale.Max(), initialHeight = p.initialHeight }).ToArray(),
                limitations = new[] { "Event times use decoded video timestamps. Each observed hit exports its own measured XY, scale and opacity; upward, downward, lateral and turning motion are preserved without a prescribed path shape.",
                    "Detection, motion learning, sessions and playback are restricted to the selected range. A hit still visible at the end is cut at that boundary; its disappearance is not claimed as observed.",
                    "When limitAnalysisRegion is enabled, outside text pixels are masked during detection, repeated-word recovery and refinement. Candidate boxes and inferred continuations must remain entirely within that fixed source-pixel region. Full-frame background pixels are used only to estimate camera translation.",
                    "When cameraMotionRemoved is enabled, estimated background translation is subtracted from each exported trajectory. Raw observations and camera offsets remain in the session; original-screen comparison can reapply camera motion. Rotation, zoom and parallax are not stabilized.",
                    "Signed template regression estimates opacity against a changing background; occlusion remains uncertain.",
                    "Lock horizontal position means a common initial spawn X only. Relative X motion remains in the preset. Original event positions are retained in RecordedHits and can be used by turning lockSpawnX off.",
                    "Clear birth observations hidden by subsequent hits may use a measured family motion profile; motionInferred marks these completions and their disappearance is not claimed as observed.",
                    "Local PP-OCR word detection/recognition and glyph templates estimate digits with separate confidence; unknown text falls back to 189. Font appearance is approximated, not recovered.",
                    "Low-contrast/occluded hits can be missed; correlation measures template agreement, not correctness probability." }
            }, true));
            AssetDatabase.Refresh(); var folder = OutputFolder; Dispose(); completed?.Invoke(prefabs, folder);
        }
        void Fail(string error) { if (disposed) return; Dispose(); failed?.Invoke(error); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.playModeStateChanged -= PlayModeChanged; reader?.Dispose(); reader = null;
            textDetector?.Dispose(); textDetector = null;
            wordSearch?.Dispose(); wordSearch = null;
        }
    }
}
