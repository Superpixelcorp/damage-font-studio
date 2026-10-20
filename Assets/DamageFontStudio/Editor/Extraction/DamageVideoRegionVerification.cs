using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Hit = DamageFontStudio.Editor.DamageVideoAutoAnalyzer.Hit;
using Track = DamageFontStudio.Editor.DamageVideoAutoAnalyzer.Track;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoRegionVerification
    {
        const string Output = "Logs/AnalysisRegionChecks";
        static DamageVideoAutoJob job;
        static double deadline;
        static readonly Rect ReferenceRegion = new Rect(40, 65, 180, 225);
        static void Check(bool value, string message) { if (!value) throw new Exception("Analysis area: " + message); }
        public static void RunChecks()
        {
            CheckCoordinatesAndSettings(); CheckMaskedDetectionAndTracking(); CheckBoundaryExit(); CheckWordSearch();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/model.txt", "PASS: reverse drag, scaled/letterboxed preview coordinates, source-pixel masking, invalid/outside/boundary boxes rejected; outside text excluded from detection, repeated-word recovery and forward/backward refinement; absolute coordinates retained; analysis area persisted independently of manual anchors and time range.\n");
        }
        static void CheckCoordinatesAndSettings()
        {
            var preview = new Rect(130, 75, 264, 248);
            var a = DamageVideoAnalysisRegion.SourcePoint(new Vector2(330, 225), preview, 528, 496);
            var b = DamageVideoAnalysisRegion.SourcePoint(new Vector2(180, 125), preview, 528, 496);
            Check(DamageVideoAnalysisRegion.DragRect(a, b) == new Rect(100, 100, 300, 200), "Dragging in either direction must map to original video pixels.");
            Check(DamageVideoAnalysisRegion.SourcePoint(new Vector2(-1, 900), preview, 528, 496) == new Vector2(0, 496), "Dragging past the preview must clamp to video bounds.");
            var clipped = new DamageVideoAnalysisRegion(new Rect(-5, -4, 30, 40), 100, 80);
            Check(clipped.Bounds == new Rect(0, 0, 25, 36), "Regions are clipped to the actual image, without shifting source coordinates.");
            bool rejected = false;
            try { new DamageVideoAnalysisRegion(new Rect(200, 200, 30, 40), 100, 80); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "An entirely outside rectangle must not silently turn into full-frame analysis.");
            var window = ScriptableObject.CreateInstance<DamageVideoExtractionWindow>();
            var field = typeof(DamageVideoExtractionWindow).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic);
            var session = (DamageVideoSession)field.GetValue(window);
            try
            {
                session.width = 528; session.height = 496; session.startFrame = 30; session.endFrame = 54;
                Check(window.SetAnalysisRegion(new Rect(40, 60, 180, 230)), "A valid automatic area can be selected before starting analysis.");
                Check(session.limitAnalysisRegion && session.analysisRegion == new Rect(40, 60, 180, 230) && session.samples.Count == 0,
                    "Automatic area selection must not create a manual glyph anchor.");
                Check(session.startFrame == 30 && session.endFrame == 54, "Changing the area must preserve the selected time range.");
                Check(!window.SetAnalysisRegion(new Rect(1, 2, 0, 0)) && session.analysisRegion == new Rect(40, 60, 180, 230),
                    "Invalid area edits preserve the previous valid selection.");
                var restored = ScriptableObject.CreateInstance<DamageVideoSession>();
                try
                {
                    JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(session), restored);
                    Check(restored.limitAnalysisRegion && restored.analysisRegion == session.analysisRegion, "Saved sessions retain the area configuration.");
                }
                finally { UnityEngine.Object.DestroyImmediate(restored); }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
        static void CheckMaskedDetectionAndTracking()
        {
            const int w = 320, h = 300;
            var area = new DamageVideoAnalysisRegion(new Rect(40, 60, 110, 210), w, h);
            var all = new DamageVideoAutoAnalyzer(0, 40, 1, 30);
            var selected = new DamageVideoAutoAnalyzer(0, 40, 1, 30, area.Bounds);
            var images = new List<Color32[]>();
            var inside = new Track(); var outside = new Track();
            for (int f = 0; f <= 40; f++)
            {
                var pixels = Enumerable.Repeat(new Color32(110, 60, 30, 255), w * h).ToArray();
                void Rect(int x, int y, int width, int height, Color32 color)
                { for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) pixels[(h - 1 - yy) * w + xx] = color; }
                void Number(int x, int y)
                {
                    Rect(x - 2, y - 2, 27, 18, new Color32(10, 10, 10, 255));
                    Rect(x, y, 7, 14, new Color32(255, 255, 255, 255)); Rect(x + 10, y, 12, 14, new Color32(255, 255, 255, 255));
                }
                if (f >= 6 && f <= 25)
                {
                    int y = 215 - (f - 6) * 3;
                    Number(90, y); Number(230, y - 25);
                    if (f % 3 == 0)
                    foreach (var item in new[] { (inside, 101f, y + 7f), (outside, 241f, y - 18f) })
                        item.Item1.hits.Add(new Hit { frame = f, family = 1, center = new Vector2(item.Item2, item.Item3), size = new Vector2(27, 18),
                            color = Color.white, contrast = 1, signal = DamageVideoSignal.Describe(pixels, w, h, new Vector2(item.Item2, item.Item3), new Vector2(27, 18), 1) });
                }
                all.AddFrame(pixels, w, h, f);
                var masked = area.Apply(pixels, w, h);
                Check(masked[(h - 1 - 100) * w + 250].r == 0 && pixels[(h - 1 - 100) * w + 250].r == 110, "Masking removes outside pixels without modifying the source/background image.");
                selected.AddFrame(masked, w, h, f, pixels);
                images.Add((Color32[])masked.Clone());
            }
            Check(all.Finish().Count == 2, "The independent fixture contains two different simultaneous damage locations.");
            var tracks = selected.Finish();
            Check(tracks.Count == 1 && tracks[0].hits.All(hit => DamageVideoAnalysisRegion.Contains(area.Bounds, hit.center, hit.size)), "Only the selected location enters coarse detection and learning.");
            Check(tracks[0].First.center.x > 90, "Detections remain in full-video coordinates, not crop-local coordinates.");
            var firstSignal = DamageVideoSignal.Describe(area.Apply(images[6], w, h), w, h, new Vector2(101, 222), new Vector2(27, 18), 1);
            var nextSignal = DamageVideoSignal.Describe(area.Apply(images[10], w, h), w, h, new Vector2(101, 222), new Vector2(27, 18), 1);
            Check(firstSignal.Zip(nextSignal, (a, b) => Mathf.Abs(a - b)).Sum() > 1, "Masked successive frames must update signal maps instead of reusing stale image features.");
            var refiner = new DamageVideoRefiner(new List<Track> { inside, outside }, 0, 40, analysisRegion: area.Bounds);
            for (int f = 0; f <= 40; f++) refiner.AddFrame(images[f], w, h, f);
            for (int f = 40; f >= 0; f--) refiner.AddBackwardFrame(images[f], w, h, f);
            var results = refiner.Finish();
            Check(results.Count == 1 && results.All(r => r.hits.All(hit => DamageVideoAnalysisRegion.Contains(area.Bounds, hit.center, hit.size))), "Forward/backward refinement cannot attach an outside effect or use its template.");
            Check(!DamageVideoAnalysisRegion.Contains(area.Bounds, new Vector2(45, 100), new Vector2(10, 10)), "Boxes touching the crop boundary are rejected to avoid learning cut glyph fragments.");
        }
        static void CheckWordSearch()
        {
            if (!SystemInfo.supportsComputeShaders) return;
            const int w = 240, h = 220;
            var pixels = Enumerable.Repeat(new Color32(60, 50, 40, 255), w * h).ToArray();
            void Rect(int x, int y, int width, int height, Color32 color)
            { for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) pixels[(h - 1 - yy) * w + xx] = color; }
            foreach (int y in new[] { 110, 160 })
            {
                Rect(68, y - 12, 44, 24, new Color32(5, 5, 5, 255));
                Rect(73, y - 9, 8, 18, new Color32(255, 255, 255, 255)); Rect(88, y - 9, 18, 18, new Color32(255, 255, 255, 255));
                Rect(93, y - 5, 7, 10, new Color32(5, 5, 5, 255));
            }
            var examples = Enumerable.Range(1, 3).Select(f => new Hit { frame = f, family = 1, text = "12", textConfidence = .99f,
                center = new Vector2(90, 110), size = new Vector2(40, 20), color = Color.white,
                signal = DamageVideoSignal.Describe(pixels, w, h, new Vector2(90, 110), new Vector2(40, 20), 1) }).ToList();
            var area = new Rect(55, 90, 70, 45);
            using (var search = new DamageVideoWordSearch(examples, w, h, area))
            {
                var hits = search.Find(pixels, 4);
                Check(hits.Count > 0 && hits.All(hit => DamageVideoAnalysisRegion.Contains(area, hit.center, hit.size)), "GPU re-detection must ignore the identical word outside the selected area even on an unmasked input.");
                Check(hits.Any(hit => Vector2.Distance(hit.center, new Vector2(90, 110)) < 4), "The selected repeated word remains detectable.");
            }
        }
        static void CheckBoundaryExit()
        {
            const int w = 320, h = 300;
            var area = new DamageVideoAnalysisRegion(new Rect(40, 80, 120, 180), w, h);
            var coarse = new Track(); var frames = new List<Color32[]>(); var timestamps = new Dictionary<int, double>();
            for (int f = 0; f <= 40; f++)
            {
                var pixels = Enumerable.Repeat(new Color32(110, 60, 30, 255), w * h).ToArray();
                if (f >= 6 && f <= 35)
                {
                    int cx = 90 + (f - 6) * 4, cy = 220 - (f - 6) * 3;
                    void Rect(int x, int y, int width, int height, Color32 color)
                    { for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) pixels[(h - 1 - yy) * w + xx] = color; }
                    Rect(cx - 13, cy - 9, 27, 18, new Color32(10, 10, 10, 255));
                    Rect(cx - 11, cy - 7, 7, 14, new Color32(255, 255, 255, 255)); Rect(cx - 1, cy - 7, 12, 14, new Color32(255, 255, 255, 255));
                    if (f >= 6 && f <= 15 && f % 3 == 0)
                        coarse.hits.Add(new Hit { frame = f, family = 1, center = new Vector2(cx, cy), size = new Vector2(27, 18), color = Color.white, contrast = 1,
                            signal = DamageVideoSignal.Describe(pixels, w, h, new Vector2(cx, cy), new Vector2(27, 18), 1) });
                }
                frames.Add(area.Apply(pixels, w, h)); timestamps[f] = f / 30d;
            }
            var refiner = new DamageVideoRefiner(new List<Track> { coarse }, 0, 40, analysisRegion: area.Bounds);
            for (int f = 0; f <= 40; f++) refiner.AddFrame(frames[f], w, h, f);
            for (int f = 40; f >= 0; f--) refiner.AddBackwardFrame(frames[f], w, h, f);
            var results = refiner.Finish();
            Check(results.Count == 1 && results[0].regionTruncatedEnd && results[0].hits.Max(hit => hit.frame) <= 20,
                "A tracked word leaving the area must stop instead of shrinking to follow a cut glyph outside the boundary.");
            var session = refiner.MakeSession(results[0], "boundary fixture", w, h, 41, 30, false, timestamps);
            try
            {
                Check(!session.observedEnd && session.regionTruncatedEnd && session.samples.Last().opacity > 0,
                    "An area-boundary cutoff must not be exported as an observed fade.");
            }
            finally { UnityEngine.Object.DestroyImmediate(session); }
        }
        public static void RunReferenceChecks()
        {
            try
            {
                Directory.CreateDirectory(Output); if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
                RunChecks(); DamageVideoRangeVerification.RunChecks(); DamageVideoCameraRemovalVerification.RunChecks();
                DamageVideoTrajectoryVerification.RunChecks();
                DamageVideoExistingPrefabVerification.RunChecks(); DamageFontRecordedPlaybackVerification.Run();
                var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-referenceVideo");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Pass -referenceVideo <path>.");
                deadline = EditorApplication.timeSinceStartup + 600; EditorApplication.update += Watchdog;
                job = new DamageVideoAutoJob(args[index + 1], 1, 1.8f, Completed, message => Finish(new Exception(message)), lockHorizontalPosition: true, analysisRegion: ReferenceRegion);
            }
            catch (Exception e) { Finish(e); }
        }
        static void Watchdog() { if (EditorApplication.timeSinceStartup > deadline) Finish(new TimeoutException("Analysis area verification timed out.")); }
        static void Completed(List<GameObject> samples, string folder)
        {
            try
            {
                var sequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder + "/RecordedHits.asset");
                Check(sequence && samples.Count > 0 && sequence.sourceStartFrame == 30 && sequence.sourceEndFrame == 54, "Actual video honors both time and area selection.");
                foreach (var hit in sequence.events)
                {
                    var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(hit.sample)).Replace('\\', '/') + "/" + hit.sample.name + "-Session.asset");
                    Check(session && session.limitAnalysisRegion && session.analysisRegion == ReferenceRegion, "Exported sessions retain the selected analysis area.");
                    Check(session.samples.All(s => s.frame >= 30 && s.frame <= 54 && DamageVideoAnalysisRegion.Contains(ReferenceRegion, s.center, s.size)), "No samples outside either boundary enter the saved motion assets.");
                    Check(hit.time + hit.motionPreset.Duration <= sequence.duration + .00001f, "Selected area does not extend the selected time range.");
                }
                DamageVideoExistingPrefabVerification.VerifySequence(sequence); DamageFontRecordedPlaybackVerification.Verify(sequence);
                File.WriteAllText(Output + "/reference.txt", "PASS: actual source 1.0–1.8 seconds; fixed analysis area x40 y65 w180 h225; " + samples.Count + " samples; all saved measurements within selected time and area; motion-only assets and existing game prefab appearance preserved.\nValidation folder: " + folder);
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        static void Finish(Exception error)
        {
            if (job != null) { Directory.CreateDirectory(Output); File.WriteAllLines(Output + "/diagnostics.txt", job.DiagnosticTracks); }
            job?.Dispose(); job = null; EditorApplication.update -= Watchdog;
            Directory.CreateDirectory(Output);
            if (error != null) { File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error); }
            else Debug.Log("Analysis area selection and extraction checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
