using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;
using Mode = DamageFontStudio.Editor.DamageFontStudioWindow.PreviewMode;

namespace DamageFontStudio.Editor
{
    // Exercises the same actions as the Studio buttons using an isolated preview window.
    public static class StudioPlaybackVerification
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly List<string> results = new List<string>();
        static int assertions, combinations;

        [MenuItem("Tools/Damage Font Studio/Run Studio Playback Checks")]
        public static void Run()
        {
            results.Clear(); assertions = combinations = 0;
            int baseline = DOTween.TotalActiveTweens();
            var presets = new[] {
                "Assets/DamageFontStudio/Presets/Critical.asset",
                "Assets/VideoDamagePreset.asset"
            };
            foreach (string path in presets)
            {
                var saved = AssetDatabase.LoadAssetAtPath<DamageFontPreset>(path);
                if (!saved) continue;
                string before = EditorJsonUtility.ToJson(saved);
                var preset = UnityEngine.Object.Instantiate(saved);
                try
                {
                    foreach (Mode mode in Enum.GetValues(typeof(Mode)))
                    foreach (bool repeat in new[] { false, true })
                    foreach (float speed in new[] { 0.1f, 1f, 3f })
                        CheckCombination(preset, mode, repeat, speed, baseline);
                    CheckTransitions(preset, baseline);
                    CheckSettings(preset, baseline);
                    CheckLimits(preset, baseline);
                    CheckObjectSource(preset, baseline);
                    CheckLifecycle(preset, baseline);
                    Check(EditorJsonUtility.ToJson(saved) == before, "Saved preset unchanged: " + path);
                    results.Add("PASS: " + path + " — all mode/repeat/speed combinations, transitions, setting edits, source preservation, lifecycle cleanup.");
                }
                finally { UnityEngine.Object.DestroyImmediate(preset); }
            }
            Check(combinations >= 18, "At least one preset tested");
            Check(DOTween.TotalActiveTweens() == baseline, "Global tween baseline restored");
            results.Add($"PASS: {combinations} mode/repeat/speed combinations; {assertions} assertions.");
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/StudioPlaybackChecks.txt", results);
            Debug.Log(string.Join("\n", results));
        }

        static DamageFontStudioWindow Window(DamageFontPreset preset)
        {
            var window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
            Set(window, "preset", preset);
            Set(window, "hits", 5); Set(window, "interval", 0.12f);
            Set(window, "number", "123456");
            return window;
        }

        static void CheckCombination(DamageFontPreset preset, Mode mode, bool repeat, float speed, int baseline)
        {
            combinations++;
            var window = Window(preset);
            try
            {
                Set(window, "loop", repeat); Set(window, "speed", speed);
                window.StartPreview(mode);
                AssertFrame(window, preset, 0, mode);
                window.Advance(0.1f);
                Near(Get<float>(window, "time"), 0.1f * speed, "Speed applied");
                float pausedAt = Get<float>(window, "time");
                var pausedPose = Poses(window);
                window.TogglePause();
                window.Advance(100);
                Near(Get<float>(window, "time"), pausedAt, "Pause freezes time");
                SamePose(pausedPose, Poses(window), "Pause freezes display");
                window.TogglePause();
                window.Advance(0.02f);
                Near(Get<float>(window, "time"), pausedAt + 0.02f * speed, "Resume keeps progress");

                float length = Get<float>(window, "length");
                foreach (float seek in new[] { length * 0.75f, 0.05f, length, 0, length * 0.35f })
                {
                    window.SeekPreview(seek);
                    AssertFrame(window, preset, seek, mode);
                    Check(!Get<bool>(window, "running"), "Seeking pauses playback");
                }
                var at35 = Poses(window);
                window.SeekPreview(length * 0.8f);
                window.SeekPreview(length * 0.35f);
                SamePose(at35, Poses(window), "Backward seek deterministic");
                window.SeekPreview(-10);
                Near(Get<float>(window,"time"), 0, "Seek clamps below zero");
                window.SeekPreview(length + 10);
                Near(Get<float>(window,"time"), length, "Seek clamps above end");
                AssertFrame(window, preset, length, mode);

                window.SeekPreview(length - 0.01f);
                window.TogglePause();
                window.Advance(0.03f / speed);
                if (repeat)
                {
                    Near(Get<float>(window, "time"), 0.02f, "Repeat wraps without drift");
                    Check(Get<bool>(window, "running"), "Repeat remains running");
                    AssertFrame(window, preset, 0.02f, mode);
                    window.Advance(length * 3 / speed);
                    Near(Get<float>(window, "time"), 0.02f, "Multiple repeat cycles wrap");
                }
                else
                {
                    AssertFrame(window, preset, length, mode);
                    Check(!Get<bool>(window, "running"), "Natural completion stops");
                    window.TogglePause();
                    AssertFrame(window, preset, 0, mode);
                    Check(Get<bool>(window, "running"), "Resume at end restarts immediately");
                }
                window.StopPreview();
                AssertStopped(window, baseline);
                window.TogglePause(); window.SeekPreview(100); window.Advance(100);
                AssertStopped(window, baseline);
                window.StartPreview(mode);
                AssertFrame(window, preset, 0, mode);
                Near(Get<float>(window, "interval"), 0.12f, "Interval setting preserved");
                Check(Get<int>(window, "hits") == 5 && Get<bool>(window, "loop") == repeat, "Hit count and repeat setting preserved");
                Near(Get<float>(window, "speed"), speed, "Speed setting preserved");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            Check(DOTween.TotalActiveTweens() == baseline, "Combination releases tweens");
        }

        static void CheckTransitions(DamageFontPreset preset, int baseline)
        {
            var window = Window(preset);
            try
            {
                // Every ordered mode pair, from running / paused / completed / looping states.
                foreach (Mode from in Enum.GetValues(typeof(Mode)))
                foreach (Mode to in Enum.GetValues(typeof(Mode)))
                for (int state = 0; state < 4; state++)
                {
                    Set(window, "loop", state == 3);
                    window.StartPreview(from);
                    if (state == 0) window.Advance(0.07f);
                    if (state == 1) window.SeekPreview(Get<float>(window, "length") * 0.5f);
                    if (state == 2) { window.Advance(Get<float>(window, "length") + 1); }
                    if (state == 3) window.Advance(Get<float>(window, "length") * 2 + 0.04f);
                    window.StartPreview(to);
                    AssertFrame(window, preset, 0, to);
                    window.SeekPreview(0.25f);
                    AssertFrame(window, preset, 0.25f, to);
                    Near(Get<float>(window, "interval"), 0.12f, "Transition keeps interval");
                    Check(Get<int>(window, "hits") == 5, "Transition keeps count");
                    Check(DOTween.TotalActiveSequences() >= 1, "Only current preview stays playable");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            Check(DOTween.TotalActiveTweens() == baseline, "All transitions cleaned");
        }

        static void CheckSettings(DamageFontPreset preset, int baseline)
        {
            var window = Window(preset);
            try
            {
                foreach (Mode mode in Enum.GetValues(typeof(Mode)))
                {
                    Set(window, "hits", 5); Set(window, "interval", 0.12f);
                    window.StartPreview(mode);
                    window.SeekPreview(0.06f);
                    Set(window, "hits", 8); Set(window, "interval", 0.2f);
                    Set(window, "number", "98765"); Set(window, "spawn", new Vector2(30, 40));
                    Set(window, "scatter", new Vector2(20, 10)); Set(window, "seed", 42);
                    Set(window, "randomizeSpawn", true);
                    window.RebuildPreview();
                    AssertFrame(window, preset, 0.06f, mode);
                    int expected = mode == Mode.Single ? 1 : mode == Mode.Series ? 8 : 50;
                    Check(Get<int>(window, "count") == expected, "Count edit follows current mode");
                    Check(!Get<bool>(window, "running"), "Editing pauses preview");
                    Check(Roots(window).All(go => go.GetComponentInChildren<TMP_Text>(true).text == "98765"), "Text edit updates all copies");
                    var pose = Poses(window);
                    window.RebuildPreview();
                    SamePose(pose, Poses(window), "Seed and spawn deterministic after rebuild");
                    var times = preset.segments.Select(s => new Vector2(s.start, s.duration)).ToArray();
                    foreach (var segment in preset.segments) { segment.start *= 1.1f; segment.duration *= 1.1f; }
                    window.RebuildPreview();
                    AssertFrame(window, preset, 0.06f, mode);
                    for (int i = 0; i < times.Length; i++) { preset.segments[i].start = times[i].x; preset.segments[i].duration = times[i].y; }
                    window.RebuildPreview();
                    SamePose(pose, Poses(window), "Restoring preset values restores preview");
                    // Zero interval is a supported deliberate setting, distinct from burst mode.
                    Set(window, "interval", 0f);
                    window.StartPreview(Mode.Series);
                    window.SeekPreview(0.06f);
                    Check(Roots(window).Count(go => go.activeSelf) == 8, "Explicit zero interval series");
                }
                window.StartPreview(Mode.Series);
                window.SeekPreview(Get<float>(window, "length"));
                Set(window, "hits", 1);
                window.RebuildPreview();
                Check(Get<float>(window, "time") <= Get<float>(window, "length"), "Shorter rebuild clamps time");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            Check(DOTween.TotalActiveTweens() == baseline, "Settings rebuild cleanup");
        }

        static void CheckObjectSource(DamageFontPreset preset, int baseline)
        {
            var source = new GameObject("Playback audit source", typeof(RectTransform));
            var child = new GameObject("Number", typeof(RectTransform));
            child.transform.SetParent(source.transform, false);
            var text = child.AddComponent<TextMeshPro>();
            text.font = preset.font ? preset.font : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            text.text = "ORIGINAL";
            source.transform.localScale = new Vector3(2, 3, 1);
            child.transform.localPosition = new Vector3(12, 20, 0);
            var binding = source.AddComponent<DamageFontObject>(); binding.numberText = text;
            var window = Window(preset);
            try
            {
                Set(window, "previewSource", source);
                foreach (Mode mode in Enum.GetValues(typeof(Mode)))
                {
                    window.StartPreview(mode);
                    window.SeekPreview(0.25f); window.SeekPreview(0.06f);
                    Check(text.text == "ORIGINAL" && source.activeSelf, "Original source display preserved");
                    Check(source.transform.localScale == new Vector3(2,3,1) && child.transform.localPosition == new Vector3(12,20,0), "Original source layout preserved");
                    window.StopPreview();
                    AssertStopped(window, baseline);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(source); }
        }

        static void CheckLimits(DamageFontPreset preset, int baseline)
        {
            var window = Window(preset);
            try
            {
                foreach (int hits in new[] { 1, 100 })
                foreach (float interval in new[] { 0, 0.03366512f, 0.33333334f, 1 })
                {
                    Set(window, "hits", hits); Set(window, "interval", interval);
                    window.StartPreview(Mode.Series);
                    window.SeekPreview(Get<float>(window, "length"));
                    AssertFrame(window, preset, Get<float>(window, "length"), Mode.Series);
                    window.TogglePause();
                    AssertFrame(window, preset, 0, Mode.Series);
                    window.StopPreview();
                    AssertStopped(window, baseline);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        static void CheckLifecycle(DamageFontPreset preset, int baseline)
        {
            var window = Window(preset);
            try
            {
                window.StartPreview(Mode.Burst);
                window.Advance(0.25f);
                typeof(DamageFontStudioWindow).GetMethod("PlayModeChanged", Fields).Invoke(window, new object[] { PlayModeStateChange.ExitingEditMode });
                AssertStopped(window, baseline);
                window.StartPreview(Mode.Series);
                window.SeekPreview(0.25f);
                var invalid = UnityEngine.Object.Instantiate(preset);
                try
                {
                    invalid.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = 100 });
                    Set(window, "preset", invalid); window.RebuildPreview();
                    AssertStopped(window, baseline);
                    Set(window, "preset", preset); window.StartPreview(Mode.Single);
                    AssertFrame(window, preset, 0, Mode.Single);
                }
                finally { UnityEngine.Object.DestroyImmediate(invalid); }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            Check(DOTween.TotalActiveTweens() == baseline, "Closing window releases previews");
        }

        static void AssertFrame(DamageFontStudioWindow window, DamageFontPreset preset, float time, Mode mode)
        {
            int count = mode == Mode.Burst ? 50 : mode == Mode.Series ? Get<int>(window, "hits") : 1;
            float interval = mode == Mode.Burst ? 0 : Get<float>(window, "interval");
            var roots = Roots(window);
            Check(roots.Length == count, "Instance count");
            Near(Get<float>(window, "length"), preset.Duration + (count - 1) * interval, "Timeline length");
            for (int i = 0; i < count; i++)
                Check(roots[i].activeSelf == (time < Get<float>(window, "length") && time - i * interval >= 0 && time - i * interval < preset.Duration), $"Visibility follows each hit start/end ({mode}, count={count}, interval={interval}, time={time}, hit={i})");
        }

        static GameObject[] Roots(DamageFontStudioWindow window)
            => Get<PreviewRenderUtility>(window, "preview").camera.scene.GetRootGameObjects()
                .Where(go => go.name.StartsWith("Damage preview ")).OrderBy(go => int.Parse(go.name.Substring("Damage preview ".Length))).ToArray();

        struct Pose { public bool visible; public Vector3 position, scale; public Quaternion rotation; public Color color; }
        static Pose[] Poses(DamageFontStudioWindow window) => Roots(window).Select(go => new Pose {
            visible = go.activeSelf, position = go.transform.localPosition, scale = go.transform.localScale,
            rotation = go.transform.localRotation, color = go.GetComponentInChildren<TMP_Text>(true).color
        }).ToArray();
        static void SamePose(Pose[] a, Pose[] b, string label)
        {
            Check(a.Length == b.Length, label);
            for (int i = 0; i < a.Length; i++)
                Check(a[i].visible == b[i].visible && Vector3.Distance(a[i].position,b[i].position) < 0.001f &&
                    Vector3.Distance(a[i].scale,b[i].scale) < 0.001f && Quaternion.Angle(a[i].rotation,b[i].rotation) < 0.01f &&
                    Vector4.Distance(a[i].color,b[i].color) < 0.001f, label);
        }
        static void AssertStopped(DamageFontStudioWindow window, int baseline)
        {
            Check(Get<int>(window,"count") == 0 && Get<PreviewRenderUtility>(window,"preview") == null && !Get<bool>(window,"running"), "Stop clears instances and scene");
            Near(Get<float>(window,"time"), 0, "Stop resets time");
            Near(Get<float>(window,"length"), 0, "Stop resets timeline");
            Check(DOTween.TotalActiveTweens() == baseline, "Stop clears tweens");
        }
        static T Get<T>(DamageFontStudioWindow window, string field) => (T)typeof(DamageFontStudioWindow).GetField(field,Fields).GetValue(window);
        static void Set(DamageFontStudioWindow window, string field, object value) => typeof(DamageFontStudioWindow).GetField(field,Fields).SetValue(window,value);
        static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual-expected) < 0.0001f, message + $" ({actual} vs {expected})");
        static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("Studio playback audit: " + message); }
    }
}
