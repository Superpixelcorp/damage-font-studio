using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoRangeVerification
    {
        const string Output = "Logs/VideoRangeChecks";
        static DamageVideoAutoJob job;
        static double deadline;
        static void Check(bool value, string message) { if (!value) throw new Exception("Analysis range: " + message); }

        public static void RunChecks()
        {
            var range = DamageVideoAutoJob.SelectRange(91, f => f / 30d, 1f, 1.8f);
            Check(range.first == 30 && range.last == 54, "Float seconds must select frames 30–54 only; zoom beginning at 55 is outside.");
            range = DamageVideoAutoJob.SelectRange(91, f => f / 30d, 1.01, 1.79);
            Check(range.first == 31 && range.last == 53, "Unaligned boundaries must round inward.");
            double[] pts = { 0, .033, .066, .11, .15, .21 };
            range = DamageVideoAutoJob.SelectRange(pts.Length, f => pts[f], .04, .18);
            Check(range.first == 2 && range.last == 4, "Variable-frame-rate range uses presentation times.");
            range = DamageVideoAutoJob.SelectRange(pts.Length, f => pts[f], .04, 0);
            Check(range.first == 2 && range.last == 5, "End zero retains the documented whole-video convention.");
            var session = ScriptableObject.CreateInstance<DamageVideoSession>();
            DamageFontPreset preset = null;
            try
            {
                session.width = 528; session.height = 496; session.fps = 30; session.frameCount = 91;
                session.startFrame = 20; session.endFrame = 70; session.observedEnd = true;
                for (int f = 20; f <= 70; f++) session.Put(new DamageVideoSample {
                    frame = f, timestamp = f / 30d, center = new Vector2(f <= 54 ? 100 + f : -1000, 200 - f),
                    size = f <= 54 ? new Vector2(30, 20) : new Vector2(120, 80), opacity = .6f });
                Check(session.ClipToRange(30, 54), "A partial track can be clipped at both boundaries.");
                Check(session.samples.All(s => s.frame >= 30 && s.frame <= 54), "No out-of-range coordinates or zoom samples are retained.");
                Check(session.rangeTruncatedStart && session.rangeTruncatedEnd && !session.observedEnd, "Range clipping must not claim observed disappearance.");
                Check(Mathf.Abs(session.samples.Last().opacity - .6f) < .0001f, "Range end must not invent a fade.");
                preset = session.BuildPreset();
                Check(Mathf.Abs(preset.Duration - .8f) < .0001f, "Individual preset stops at the selected end.");
                Check(preset.segments.Where(s => s.channel == DamageChannel.Position).All(s => s.targetVector.x >= 0 && s.targetVector.x <= 24), "Out-of-range left jump cannot enter exported motion.");
                Check(preset.segments.Where(s => s.channel == DamageChannel.Scale).All(s => s.targetVector == Vector3.one), "Out-of-range zoom cannot enter exported scale.");
            }
            finally { if (preset) UnityEngine.Object.DestroyImmediate(preset); UnityEngine.Object.DestroyImmediate(session); }
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/model.txt", "PASS: exact/inward/VFR range selection; clipping at both ends; outside left jump/zoom excluded; no invented fade or observed disappearance; individual preset duration.\n");
        }

        // Batch entry point, using -referenceVideo <path>. Also callable in an open Editor.
        public static void RunReferenceChecks()
        {
            try
            {
                RunChecks(); DamageVideoCameraRemovalVerification.RunChecks(); DamageVideoExistingPrefabVerification.RunChecks(); DamageFontRecordedPlaybackVerification.Run();
                var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-referenceVideo");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Pass -referenceVideo <path>.");
                deadline = EditorApplication.timeSinceStartup + 600; EditorApplication.update += Watchdog;
                job = new DamageVideoAutoJob(args[index + 1], 1, 1.8f, Completed, message => Finish(new Exception(message)), lockHorizontalPosition: true);
            }
            catch (Exception e) { Finish(e); }
        }
        static void Watchdog() { if (EditorApplication.timeSinceStartup > deadline) Finish(new TimeoutException("Range extraction timed out.")); }
        static void Completed(List<GameObject> prefabs, string folder)
        {
            try
            {
                var sequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder + "/RecordedHits.asset");
                Check(sequence && prefabs.Count > 0, "The actual selected range must produce samples.");
                Check(sequence.sourceStartFrame == 30 && sequence.sourceEndFrame == 54 && Mathf.Abs(sequence.duration - .8f) < .00001f, "Actual source range is exactly 1.0–1.8 seconds.");
                Check(sequence.frames.All(f => f >= 30 && f <= 54), "Saved sequence timeline excludes context frames.");
                Check(sequence.cameraMotionRemoved, "Automatic extraction removes screen shake by default.");
                foreach (var hit in sequence.events)
                {
                    var binding = hit.sample.GetComponent<DamageFontObject>();
                    Check(hit.time >= 0 && hit.time + binding.preset.Duration <= sequence.duration + .00001f, "Every saved event ends within the selected range.");
                    var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(hit.sample)).Replace('\\', '/') + "/" + hit.sample.name + "-Session.asset");
                    Check(session && session.ValidateExport() == null && session.samples.All(s => s.frame >= 30 && s.frame <= 54), "Every saved sample excludes zoom frames 55 onward.");
                    Check(!session.rangeTruncatedEnd || !session.observedEnd, "Truncated actual hit must not claim observed disappearance.");
                    Check(session.removeCameraMotion, "Every generated session retains camera compensation for rebuilding presets.");
                }
                DamageFontRecordedPlaybackVerification.Verify(sequence);
                DamageVideoExistingPrefabVerification.VerifySequence(sequence);
                File.WriteAllText(Output + "/reference.txt", "PASS: actual source 1.0–1.8 seconds, frames 30–54; " + prefabs.Count + " samples; no frame 55 onward in saved sessions, event motion or timeline; background translation removed from exported presets; whole playback and authoring restoration passed.\nFolder: " + folder);
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        static void Finish(Exception error)
        {
            job?.Dispose(); job = null; EditorApplication.update -= Watchdog;
            Directory.CreateDirectory(Output);
            if (error != null) { File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error); }
            else Debug.Log("Strict video range extraction and RecordedHits playback checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
