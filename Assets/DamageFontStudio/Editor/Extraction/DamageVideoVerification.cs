using System;
using System.IO;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoVerification
    {
        static DamageVideoReader reader;
        static DamageVideoSession videoSession;
        static DamageVideoTracker tracker;
        static DamageVideoSample previous;
        static int nextFrame = -1, phase;
        static double deadline;
        const string Output = "Logs/VideoExtractionChecks";

        [MenuItem("Tools/Damage Font Studio/Run Video Extraction Checks")]
        public static void RunModelChecks()
        {
            Directory.CreateDirectory(Output);
            CheckFrameBoundaryRounding();
            int baseline = DOTween.TotalActiveTweens();
            var session = ScriptableObject.CreateInstance<DamageVideoSession>();
            DamageFontPreset preset = null;
            GameObject go = null;
            string assetPath = null, sessionPath = null;
            try
            {
                session.width = 640; session.height = 360; session.frameCount = 120; session.fps = 30;
                session.startFrame = 10; session.endFrame = 40; session.unitsPerPixel = 0.5f;
                Check(session.ValidateExport() != null, "Missing endpoints rejected");
                session.Put(Sample(10, new Vector2(100, 150), new Vector2(80, 40)));
                var middle = Sample(25, new Vector2(140, 100), new Vector2(120, 60)); middle.angle = 30; middle.opacity = 0.6f;
                session.Put(middle);
                var last = Sample(40, new Vector2(180, 180), new Vector2(40, 20)); last.angle = -10; last.opacity = 0;
                session.Put(last);
                Check(session.ValidateExport() == null, "Session valid");
                Check(session.At(20).center == new Vector2(126.66667f, 116.66667f) || Vector2.Distance(session.At(20).center, new Vector2(126.66667f, 116.66667f)) < 0.01f, "Interpolation");
                sessionPath = AssetDatabase.GenerateUniqueAssetPath("Assets/DamageFontStudio/VideoSessionCheck.asset");
                AssetDatabase.CreateAsset(session, sessionPath); AssetDatabase.SaveAssetIfDirty(session);
                AssetDatabase.ImportAsset(sessionPath, ImportAssetOptions.ForceUpdate);
                var loadedSession = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(sessionPath);
                Check(loadedSession.samples.Count == 3 && loadedSession.samples[1].manual, "Session corrections persist");
                preset = loadedSession.BuildPreset();
                Check(preset.Validate() == null, "Exported preset validates");
                Check(Mathf.Abs(preset.Duration - 1) < 0.001f, "Trimmed duration");
                assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/DamageFontStudio/VideoPresetCheck.asset");
                AssetDatabase.CreateAsset(preset, assetPath); AssetDatabase.SaveAssetIfDirty(preset);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                preset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>(assetPath);
                go = new GameObject("Extracted preset test", typeof(RectTransform));
                go.hideFlags = HideFlags.HideAndDontSave;
                var text = go.AddComponent<TextMeshPro>();
                text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                using (var playback = new DamageFontPlayback(text))
                {
                    playback.Prepare(preset, "123", Vector3.zero, manual: true);
                    playback.Seek(0.5f);
                    Check(Vector3.Distance(go.transform.localPosition, new Vector3(20,25,0)) < 0.001f, "Pixel conversion and Y inversion");
                    Check(Vector3.Distance(go.transform.localScale, new Vector3(1.5f,1.5f,1)) < 0.001f, "Scale extraction");
                    Check(Mathf.Abs(Mathf.DeltaAngle(go.transform.localEulerAngles.z, -30)) < 0.001f, "Rotation extraction");
                    Check(Mathf.Abs(text.color.a - 0.6f) < 0.001f, "Manual opacity extraction");
                    playback.Seek(0.9f); playback.Seek(0.5f);
                    Check(Vector3.Distance(go.transform.localPosition, new Vector3(20,25,0)) < 0.001f, "Reverse seek deterministic");
                }
                Check(DOTween.TotalActiveTweens() == baseline, "Preset cleanup");
                session.endFrame = 41;
                Check(session.ValidateExport() != null, "Range changes require endpoint correction");
                var flat = new Color32[640 * 360];
                var matcher = new DamageVideoTracker();
                matcher.Anchor(flat, 640, 360, middle);
                Check(matcher.Track(flat, 640, 360, middle, 30, 40).confidence == 0, "Flat region low-confidence rejection");
                File.WriteAllText(Output + "/model-checks.txt", "PASS: session serialization, range validation, preset export/reload, timing, XY conversion, scale/rotation/opacity, reverse seek, cleanup, flat-region rejection.\n");
                Debug.Log("Video extraction model checks passed.");
            }
            finally
            {
                if (go) UnityEngine.Object.DestroyImmediate(go);
                if (assetPath != null) AssetDatabase.DeleteAsset(assetPath); else if (preset) UnityEngine.Object.DestroyImmediate(preset);
                if (sessionPath != null) AssetDatabase.DeleteAsset(sessionPath); else UnityEngine.Object.DestroyImmediate(session);
            }
        }

        static void CheckFrameBoundaryRounding()
        {
            var definition = ScriptableObject.CreateInstance<DamageFontPreset>();
            var session = ScriptableObject.CreateInstance<DamageVideoSession>();
            DamageFontPreset extracted = null;
            try
            {
                // Regression: user video at 29.70435333251953 fps. Reconstructing
                // this boundary in double precision overlaps by ~1.49e-8 seconds.
                definition.segments.Add(new DamageSegment { start = 0.0673302f, duration = 0.7742973f });
                definition.segments.Add(new DamageSegment { start = 0.8416275f, duration = 0.03366512f });
                Check(definition.Validate() == null, "Rounded consecutive video boundaries accepted");
                definition.segments[1].start -= 0.0001f;
                Check(definition.Validate() != null, "Actual small overlap still rejected");
                definition.segments[1].start = definition.segments[0].start;
                Check(definition.Validate() != null, "Duplicate interval rejected");

                session.width = 640; session.height = 360; session.fps = 29.70435333251953;
                session.frameCount = 1230; session.startFrame = 30; session.endFrame = 1229;
                for (int frame = session.startFrame; frame <= session.endFrame; frame += 2)
                    session.Put(Sample(frame, new Vector2(frame, 100), new Vector2(80, 40)));
                // At most 600 samples; use the final existing sample as the endpoint.
                session.endFrame = session.samples[session.samples.Count - 1].frame;
                extracted = session.BuildPreset();
                Check(extracted.Validate() == null, "600 samples at fractional fps validate");

                // Validate the existing user export without changing or saving it.
                var saved = AssetDatabase.LoadAssetAtPath<DamageFontPreset>("Assets/VideoDamagePreset.asset");
                if (saved)
                {
                    Check(saved.Validate() == null, "Existing VideoDamagePreset validates without re-export");
                    var go = new GameObject("Saved video preset regression");
                    try
                    {
                        using (var playback = new DamageFontPlayback(go))
                        {
                            playback.Prepare(saved, "123", Vector3.zero, manual: true);
                            playback.Seek(saved.Duration * 0.5f);
                            Check(go.activeSelf, "Saved video preset can play");
                            playback.Seek(saved.Duration);
                            Check(!go.activeSelf, "Saved video preset completes");
                        }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
                Debug.Log("Video frame-boundary rounding regression checks passed.");
            }
            finally
            {
                if (extracted) UnityEngine.Object.DestroyImmediate(extracted);
                UnityEngine.Object.DestroyImmediate(session);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        // Async verification entry point for a generated 640x360, 30fps, 31-frame fixture.
        public static void RunFixtureChecks()
        {
            try
            {
                RunModelChecks();
                videoSession = ScriptableObject.CreateInstance<DamageVideoSession>();
                reader = new DamageVideoReader(Path.GetFullPath(Output + "/tracking-fixture.mp4"));
                reader.Error += message => Finish(new Exception(message));
                reader.Prepared += () =>
                {
                    Debug.Log($"Video fixture prepared: {reader.Width}x{reader.Height}, {reader.Fps} fps, {reader.FrameCount} frames");
                    videoSession.width = reader.Width; videoSession.height = reader.Height;
                    videoSession.frameCount = reader.FrameCount; videoSession.fps = reader.Fps;
                    videoSession.startFrame = 0; videoSession.endFrame = 30;
                    tracker = new DamageVideoTracker(); nextFrame = 0; phase = 0;
                };
                deadline = EditorApplication.timeSinceStartup + 120;
                EditorApplication.update += FixtureTick;
            }
            catch (Exception ex) { Finish(ex); }
        }

        static void FixtureTick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(new Exception("Fixture test timeout")); return; }
            if (reader == null || !reader.Ready || reader.Busy || nextFrame < 0) return;
            int frame = nextFrame; nextFrame = -1;
            try
            {
                reader.Seek(frame, decoded =>
                {
                    try
                    {
                        Check(decoded == frame, "Exact video seek");
                        Debug.Log("Decoded fixture frame " + decoded);
                        if (phase == 1)
                        {
                            var p = reader.ReadPixels();
                            Check(p.Length == 640 * 360, "Backward seek pixels");
                            File.WriteAllText(Output + "/video-checks.txt", "PASS: MP4 preparation, exact forward/backward seeks, frame readback, automatic tracking with known displacement/scale, generated preset, overlay render.\n");
                            Finish(null); return;
                        }
                        var pixels = reader.ReadPixels();
                        if (frame == 0)
                        {
                            previous = Sample(0, new Vector2(120,140), new Vector2(110,46));
                            tracker.Anchor(pixels, 640, 360, previous);
                        }
                        else
                        {
                            previous = tracker.Track(pixels, 640, 360, previous, frame, 40);
                            Vector2 expected = new Vector2(120 + frame * 3, 140 - frame * 2 + 0.06f * frame * frame);
                            Check(previous.confidence > 0.65f, "Video tracking confidence at " + frame + ": " + previous.confidence);
                            Check(Vector2.Distance(previous.center, expected) < 6, "Video tracking center at " + frame + ": " + previous.center);
                            Check(Mathf.Abs(previous.size.x / 110 - (1 + frame * 0.01f)) < 0.15f, "Video tracking scale");
                        }
                        videoSession.Put(previous.Copy());
                        if (frame == 30)
                        {
                            Check(videoSession.ValidateExport() == null, "Tracked session exports");
                            using (var comparison = new DamageVideoComparison())
                            {
                                comparison.Build(videoSession);
                                var texture = (RenderTexture)comparison.Render(new Rect(0,0,640,360), 0.5f);
                                SaveRender(texture, Output + "/comparison.png");
                            }
                            phase = 1; nextFrame = 0;
                        }
                        else nextFrame = Math.Min(30, frame + 3);
                    }
                    catch (Exception ex) { Finish(ex); }
                });
            }
            catch (Exception ex) { Finish(ex); }
        }
        static void SaveRender(RenderTexture render, string path)
        {
            var image = new Texture2D(render.width, render.height, TextureFormat.RGBA32, false);
            var prior = RenderTexture.active;
            try
            {
                RenderTexture.active = render; image.ReadPixels(new Rect(0,0,render.width,render.height), 0,0); image.Apply();
                int visible = 0; foreach (var p in image.GetPixels32()) if (p.a > 10 && (p.r > 50 || p.g > 50)) visible++;
                Check(visible > 50, "Actual DOTween comparison renders text");
                Check(image.GetPixels32()[0].a == 0, "Overlay background transparent");
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = prior; UnityEngine.Object.DestroyImmediate(image); }
        }
        static void Finish(Exception error)
        {
            EditorApplication.update -= FixtureTick;
            reader?.Dispose(); reader = null;
            if (videoSession) UnityEngine.Object.DestroyImmediate(videoSession);
            videoSession = null;
            if (error != null) Debug.LogException(error); else Debug.Log("Video extraction fixture checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
        static DamageVideoSample Sample(int frame, Vector2 center, Vector2 size) => new DamageVideoSample { frame = frame, center = center, size = size };
        static void Check(bool value, string message) { if (!value) throw new Exception("Video extraction check: " + message); }
    }
}
