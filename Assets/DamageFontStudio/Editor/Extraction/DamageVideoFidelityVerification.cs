using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DG.Tweening;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoFidelityVerification
    {
        const string Output = "Logs/VideoFidelityChecks";
        static DamageVideoAutoJob job;
        static DamageVideoReader reader;
        static DamageVideoComparison comparison;
        static DamageFontSequence sequence;
        static string source;
        static int next = -1, baseline;
        static double deadline;
        public static void RunCoreChecks()
        {
            try
            {
                CheckSignal(); CheckBirthSeparation(); CheckCameraMotion(); CheckWordSearch(); CheckMotionProfile(); DamageVideoTrajectoryVerification.RunChecks();
                DamageVideoRangeVerification.RunChecks(); DamageVideoCameraRemovalVerification.RunChecks();
                DamageVideoExistingPrefabVerification.RunChecks();
                DamageVideoRegionVerification.RunChecks();
                DamageVideoVerification.RunModelChecks(); StudioPlaybackVerification.Run();
                Debug.Log("Extraction core regression checks passed.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogException(e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }
        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output);
                if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
                CheckSignal(); CheckBirthSeparation(); CheckCameraMotion(); CheckWordSearch(); CheckMotionProfile(); DamageVideoTrajectoryVerification.RunChecks();
                var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-referenceVideo");
                if (index < 0) throw new ArgumentException("Pass -referenceVideo.");
                source = args[index + 1]; deadline = EditorApplication.timeSinceStartup + 600;
                EditorApplication.update += Tick; baseline = DOTween.TotalActiveTweens();
                job = new DamageVideoAutoJob(source, 2, 4, AnalyzeDone, message => Finish(new Exception(message)), lockHorizontalPosition: false);
            }
            catch (Exception ex) { Finish(ex); }
        }
        static void CheckSignal()
        {
            var opaque = new Color32[160 * 120]; var faded = new Color32[opaque.Length];
            for (int y = 0; y < 120; y++) for (int x = 0; x < 160; x++)
            {
                float v = x > 62 && x < 96 && y > 46 && y < 74 && ((x / 5 + y / 7) % 2 == 0) ? .9f : .1f;
                opaque[y * 160 + x] = new Color(v, v, v, 1);
                float a = .35f * v + .4f; faded[y * 160 + x] = new Color(a, a, a, 1);
            }
            var signal = DamageVideoSignal.Describe(opaque, 160, 120, new Vector2(80, 60), new Vector2(38, 32), 1);
            var after = DamageVideoSignal.Describe(faded, 160, 120, new Vector2(80, 60), new Vector2(38, 32), 1);
            float correlation = DamageVideoSignal.Compare(signal, after, out float alpha);
            Check(correlation > .99f && Mathf.Abs(alpha - .35f) < .01f, "Photometric fade recovers planted alpha despite a changing background offset");
        }
        static void CheckBirthSeparation()
        {
            var analyzer = new DamageVideoAutoAnalyzer(0, 40, 1, 30);
            for (int f = 0; f <= 40; f++)
            {
                var pixels = Enumerable.Repeat(new Color32(100, 50, 25, 255), 320 * 240).ToArray();
                void Rect(int x, int y, int w, int h, Color32 color)
                { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) pixels[(239 - yy) * 320 + xx] = color; }
                void Number(int start, int end)
                {
                    if (f < start || f > end) return; int y = 175 - (f - start) * 3;
                    Rect(90, y - 2, 27, 18, new Color32(10, 10, 10, 255));
                    Rect(92, y, 7, 14, new Color32(255, 255, 255, 255)); Rect(102, y, 12, 14, new Color32(255, 255, 255, 255));
                }
                Number(6, 25); Number(12, 31); analyzer.AddFrame(pixels, 320, 240, f);
            }
            var tracks = analyzer.Finish();
            Check(tracks.Count == 2 && tracks.Any(t => t.First.frame == 6) && tracks.Any(t => t.First.frame == 12), "Identical values form two distinct hit events with planted cadence");
        }
        static void CheckCameraMotion()
        {
            const int w = 320, h = 240;
            var random = new System.Random(918); var a = new Color32[w * h]; var b = new Color32[a.Length];
            var cells = new byte[w / 4 * (h / 4)]; random.NextBytes(cells);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            { byte v = cells[y / 4 * (w / 4) + x / 4]; a[(h - 1 - y) * w + x] = new Color32(v, v, v, 255); }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                b[(h - 1 - y) * w + x] = x > 8 && y > 4 ? a[(h - 1 - (y - 4)) * w + x - 8] : new Color32(0, 0, 0, 255);
            var camera = new DamageVideoCameraMotion(); camera.Add(a, w, h); var offset = camera.Add(b, w, h);
            Check(Vector2.Distance(offset, new Vector2(8, 4)) < 1, "Recovers independently planted background translation");
        }
        static void CheckWordSearch()
        {
            if (!SystemInfo.supportsComputeShaders) return;
            const int w = 240, h = 220;
            var pixels = Enumerable.Repeat(new Color32(60, 50, 40, 255), w * h).ToArray();
            void Rectangle(int x, int y, int width, int height, Color32 color)
            { for (int yy = y; yy < y + height; yy++) for (int xx = x; xx < x + width; xx++) pixels[(h - 1 - yy) * w + xx] = color; }
            foreach (int y in new[] { 110, 160 })
            {
                Rectangle(68, y - 12, 44, 24, new Color32(5, 5, 5, 255));
                Rectangle(73, y - 9, 8, 18, new Color32(255, 255, 255, 255));
                Rectangle(88, y - 9, 18, 18, new Color32(255, 255, 255, 255));
                Rectangle(93, y - 5, 7, 10, new Color32(5, 5, 5, 255));
            }
            var examples = new List<DamageVideoAutoAnalyzer.Hit>();
            foreach (int f in new[] { 1, 2, 3 }) examples.Add(new DamageVideoAutoAnalyzer.Hit { frame = f, family = 1, text = "12", textConfidence = .99f,
                center = new Vector2(90, f == 1 ? 110 : 160), size = new Vector2(40, 20), color = Color.white,
                signal = DamageVideoSignal.Describe(pixels, w, h, new Vector2(90, 160), new Vector2(40, 20), 1) });
            using (var search = new DamageVideoWordSearch(examples, w, h))
            {
                var hits = search.Find(pixels, 4);
                Check(hits.Any(hit => Vector2.Distance(hit.center, new Vector2(90, 110)) < 4) && hits.Any(hit => Vector2.Distance(hit.center, new Vector2(90, 160)) < 4),
                    "GPU word search finds two independently planted repeated words");
            }
        }
        static void CheckMotionProfile()
        {
            var stamps = new Dictionary<int, double>(); var camera = new Dictionary<int, Vector2>();
            for (int f = 0; f <= 100; f++) { stamps[f] = f / 30d; camera[f] = new Vector2(f * .7f, f * .4f); }
            var tracks = new List<DamageVideoRefiner.Result>();
            for (int n = 0; n < 3; n++)
            {
                var coarse = new DamageVideoAutoAnalyzer.Track();
                var result = new DamageVideoRefiner.Result { coarse = coarse, meanCorrelation = .95f, closed = true };
                for (int k = 0; k <= 18; k++)
                {
                    int f = 5 + n * 25 + k;
                    float size = k == 0 ? 30 : k == 1 ? 24 : 20;
                    var hit = new DamageVideoAutoAnalyzer.Hit { frame = f, family = 1, size = new Vector2((n + 2) * size, size),
                        center = new Vector2(n * 80 + k * 2, 200 - k * 4) + camera[f], contrast = k < 12 ? 1 : 1 - (k - 12) * .13f };
                    coarse.hits.Add(hit); result.hits.Add(hit);
                }
                tracks.Add(result);
            }
            var profile = DamageVideoMotionProfile.Learn(tracks, stamps, camera)[1];
            Check(profile.observations == 3 && Mathf.Abs(profile.duration - 19 / 30f) < .0001f, "Shared animation learns independently planted lifetime across different word widths");
            Check(Mathf.Abs(profile.rise.Last() - 72) < .01f && Mathf.Abs(profile.scale[3] - 2 / 3f) < .001f,
                "Shared animation separates camera motion and recovers planted rise and relative scale");
            Check(profile.alpha[0] == 1 && profile.alpha.Last() == 0 && profile.alpha[profile.alpha.Count - 2] < .3f, "Shared animation recovers planted fade and end");
            var preset = ScriptableObject.CreateInstance<DamageFontPreset>();
            try
            {
                profile.Apply(preset);
                Check(Mathf.Abs(preset.Duration - profile.duration) < .0001f && Mathf.Abs(profile.horizontal.Last() - 36) < .001f,
                    "Completion profile retains measured horizontal motion and duration");
            }
            finally { UnityEngine.Object.DestroyImmediate(preset); }
        }
        static void AnalyzeDone(List<GameObject> prefabs, string folder)
        {
            try
            {
                File.WriteAllLines(Output + "/coarse-tracks.txt", job.DiagnosticTracks);
                job = null; sequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder + "/RecordedHits.asset");
                Check(sequence && sequence.events.Count > 3, "Exports hit events, not just two representatives per appearance");
                var parent = new GameObject("Replacement number regression");
                var reference = new GameObject("Reference number regression");
                try
                {
                    using (var replacement = new DamageFontSequencePlayback(sequence, parent.transform, "9876543210"))
                    using (var original = new DamageFontSequencePlayback(sequence, reference.transform, "1"))
                    {
                        foreach (float time in new[] { 0f, .03f, .45f, .15f, .8f, 1.7f, 2f })
                        {
                            replacement.Seek(time); original.Seek(time);
                            for (int i = 0; i < parent.transform.childCount; i++)
                            {
                                var a = parent.transform.GetChild(i); var b = reference.transform.GetChild(i);
                                Check(a.localPosition == b.localPosition && a.localScale == b.localScale && a.localRotation == b.localRotation && a.gameObject.activeSelf == b.gameObject.activeSelf,
                                    "Changing digit count preserves every hit's position, scale, rotation and visibility");
                                Check(a.GetComponentInChildren<TMPro.TMP_Text>(true).color == b.GetComponentInChildren<TMPro.TMP_Text>(true).color,
                                    "Changing digit count preserves opacity and tint");
                            }
                        }
                        Check(parent.GetComponentsInChildren<TMPro.TMP_Text>(true).All(t => t.text == "9876543210"), "Every hit accepts replacement digits including 0-9 after backward/forward seeking");
                    }
                    Check(parent.transform.childCount == 0, "Replacement sequence releases owned objects");
                }
                finally { UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(reference); }
                var summary = new List<string> { "Folder: " + folder, $"Range: {sequence.sourceStartTime:0.000}–{sequence.sourceStartTime + sequence.duration:0.000}s", "Source interval = decoded timestamp; correlation is a fit score, not truth probability." };
                foreach (var e in sequence.events)
                {
                    var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(e.sample)).Replace('\\', '/') + "/" + e.sample.name + "-Session.asset");
                    var preset = e.sample.GetComponent<DamageFontObject>().preset;
                    Check(preset.Validate() == null, "Refined preset validates");
                    Check(Mathf.Abs(e.time - sequence.TimeAtFrame(session.startFrame)) < .0001f, "Event onset uses decoded timestamp");
                    double length = session.TimeOf(session.samples.Last()) - session.TimeOf(session.samples[0]);
                    Check(Mathf.Abs(preset.Duration - (session.sharedMotion ? session.sharedMotionDuration : (float)length)) < .0001f, "Duration uses the learned shared motion profile or individual measured timestamps");
                    summary.Add($"{e.sample.name}: start={e.time:0.000}s duration={preset.Duration:0.000}s rise={session.samples[0].center.y - session.samples.Last().center.y:0.0}px scale={session.samples.Last().size.y / session.samples[0].size.y:0.000} inferred={session.motionInferred} observedEnd={session.observedEnd} corr={e.confidence:0.000}");
                }
                var delays = sequence.events.Zip(sequence.events.Skip(1), (a, b) => b.time - a.time).Where(t => t > .005f).ToArray();
                Check(delays.Length > 1 && delays.Max() - delays.Min() > .01f, "Keeps irregular observed cadence");
                File.WriteAllLines(Output + "/measurements.txt", summary);
                File.WriteAllText(Output + "/folder.txt", folder);
                comparison = new DamageVideoComparison(); comparison.Build(sequence, "123");
                reader = new DamageVideoReader(source); reader.Error += message => Finish(new Exception(message));
                reader.Prepared += () => { Check(reader.HasPresentationTimes, "Reads MP4 presentation time tables instead of average-FPS clock"); next = sequence.sourceStartFrame; };
            }
            catch (Exception ex) { Finish(ex); }
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Fidelity verification timed out.")); return; }
            if (reader == null || !reader.Ready || reader.Busy || next < 0) return;
            int frame = next; next = -1;
            reader.Seek(frame, actual =>
            {
                try
                {
                    Directory.CreateDirectory(Output + "/Source"); Directory.CreateDirectory(Output + "/Reconstruction");
                    Save(reader.Texture, Output + "/Source/" + actual.ToString("D4") + ".png");
                    float time = sequence.TimeAtFrame(actual);
                    Save(comparison.Render(new Rect(0, 0, sequence.sourceWidth, sequence.sourceHeight), time), Output + "/Reconstruction/" + actual.ToString("D4") + ".png");
                    if (actual < sequence.sourceEndFrame) next = actual + 1;
                    else
                    {
                        // Seek backwards and forwards in the complete sequence; verify ownership cleanup.
                        comparison.Render(new Rect(0, 0, 528, 496), .2f); comparison.Dispose(); comparison = null;
                        Check(DOTween.TotalActiveTweens() == baseline, "Sequence releases all owned tweens");
                        DamageVideoVerification.RunModelChecks(); StudioPlaybackVerification.Run();
                        File.WriteAllText(Output + "/checks.txt", "PASS: independent planted-alpha, camera translation, repeated-word GPU search and shared animation profile regressions; actual decoded video timestamps; generic right/left arcs, zigzag and lateral motion; lifetime, relative scale and fade; common spawn X with subsequent XY motion preserved; observed tracks retain individual curves; same-frame source/reconstruction renders with all values set to 123; digit-count changes from 1 to 9876543210 preserve every hit's position, scale, rotation, tint, alpha and visibility during forward/backward seeking; cleanup; existing manual and Studio regressions.\nReal-video correlation is not an independent accuracy measurement. Font appearance and recognized digits are excluded from animation evaluation. Visual motion discrepancies and missed detections must be inspected in the paired render.\n");
                        Finish(null);
                    }
                }
                catch (Exception ex) { Finish(ex); }
            });
        }
        static void Save(Texture sourceTexture, string path)
        {
            var previous = RenderTexture.active; var pixels = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = (RenderTexture)sourceTexture; pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); }
        }
        static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; next = -1; job?.Dispose(); job = null; reader?.Dispose(); reader = null; comparison?.Dispose(); comparison = null;
            if (error != null) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error); }
            else Debug.Log("Extraction pipeline regression checks passed; paired renders still require visual fidelity inspection.");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Fidelity check: " + message); }
    }
}
