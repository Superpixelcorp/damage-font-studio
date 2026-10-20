using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoSeriesVerification
    {
        const string Folder = "Assets/DamageFontStudio/Generated/MonsterHitReference";
        const string Output = "Logs/ReferenceSeriesChecks";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("Tools/Damage Font Studio/Restore Reference Sample Trajectories")]
        public static void FixReferenceSamples()
        {
            foreach (string name in new[] { "Light-1", "Light-2", "Pink-3" })
            {
                var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Folder + "/" + name + "-Session.asset");
                var preset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>(Folder + "/" + name + ".asset");
                if (!session || !preset) throw new InvalidOperationException("Missing reference sample: " + name);
                // Restore the X samples removed by the former interpretation of X lock.
                // Keep GUIDs, prefab bindings and other authored settings.
                session.lockHorizontalPosition = true;
                var measured = session.BuildPreset();
                try
                {
                    var targets = preset.segments.Where(s => s.channel == DamageChannel.Position).ToArray();
                    var positions = measured.segments.Where(s => s.channel == DamageChannel.Position).ToArray();
                    Check(targets.Length == positions.Length, "Reference segment layout must match its saved measurements");
                    for (int i = 0; i < targets.Length; i++) targets[i].targetVector.x = positions[i].targetVector.x;
                }
                finally { UnityEngine.Object.DestroyImmediate(measured); }
                EditorUtility.SetDirty(session); EditorUtility.SetDirty(preset);
                AssetDatabase.SaveAssetIfDirty(session); AssetDatabase.SaveAssetIfDirty(preset);
            }
        }

        public static void Run()
        {
            try
            {
                Directory.CreateDirectory(Output);
                if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
                FixReferenceSamples();
                int baseline = DOTween.TotalActiveTweens();
                foreach (string name in new[] { "Light-1", "Light-2", "Pink-3" }) CheckSample(name);
                Check(DOTween.TotalActiveTweens() == baseline, "No tween leaks after series playback");
                StudioPlaybackVerification.Run();
                DamageVideoVerification.RunModelChecks();
                File.WriteAllText(Output + "/checks.txt", "PASS: all three saved reference samples; 6-hit series at 60 Hz; arbitrary numbers 7/189/987654; common initial spawn X and Y with measured XY motion preserved afterward; stale scatter settings ignored by default; explicit randomization still works; reverse seek; repeat; source/prefab references preserved; Studio playback and manual extraction regressions.\n");
                Debug.Log("Reference hit-series checks passed.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Output); File.WriteAllText(Output + "/failure.txt", ex.ToString()); Debug.LogException(ex);
                if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
            }
        }
        static void CheckSample(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab");
            var binding = prefab.GetComponent<DamageFontObject>();
            var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Folder + "/" + name + "-Session.asset");
            Check(binding.preset == AssetDatabase.LoadAssetAtPath<DamageFontPreset>(Folder + "/" + name + ".asset"), "Existing prefab reference preserved");
            var exported = session.BuildPreset();
            try { Check(exported.segments.Where(s => s.channel == DamageChannel.Position).Select(s => s.targetVector.x)
                .SequenceEqual(session.samples.Skip(1).Select(s => (s.center.x - session.samples[0].center.x) * session.unitsPerPixel)), "Session re-export preserves measured X motion"); }
            finally { UnityEngine.Object.DestroyImmediate(exported); }
            var window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
            try
            {
                Set(window, "preset", binding.preset); Set(window, "previewSource", prefab); Set(window, "overrideTextAppearance", true);
                Set(window, "hits", 6); Set(window, "interval", .12f); Set(window, "zoom", 2f);
                Set(window, "spawn", new Vector2(0, -50));
                // Simulate the scatter serialized in older, already-open Studio windows.
                Set(window, "scatter", new Vector2(100, 25));
                Check(!Get<bool>(window, "randomizeSpawn"), "Random scatter is opt-in");
                foreach (string number in new[] { "7", "189", "987654" })
                {
                    Set(window, "number", number); window.StartPreview(DamageFontStudioWindow.PreviewMode.Series);
                    float length = Get<float>(window, "length");
                    for (float t = 0; t < length; t += 1f / 60)
                    { window.SeekPreview(t); AssertColumn(window); }
                    for (int i = 0; i < 6; i++)
                    {
                        window.SeekPreview(i * .12f);
                        var born = Roots(window)[i];
                        Check(Mathf.Abs(born.transform.localPosition.x) < .001f, "All hits share the same spawn X");
                        Check(Mathf.Abs(born.transform.localPosition.y + 50) < .001f, "All hits share the same spawn Y");
                    }
                    window.SeekPreview(length * .8f); window.SeekPreview(.25f); AssertColumn(window);
                    Set(window, "loop", true); window.TogglePause(); window.Advance(length + .1f); AssertColumn(window);
                    window.StopPreview();
                }
                Set(window, "number", "189"); Set(window, "randomizeSpawn", true);
                window.StartPreview(DamageFontStudioWindow.PreviewMode.Series);
                Check(Roots(window).Select(r => r.transform.localPosition.x).Distinct().Count() > 1, "Explicit randomization remains available");
                Set(window, "randomizeSpawn", false);
                window.StartPreview(DamageFontStudioWindow.PreviewMode.Series);
                window.SeekPreview(.3f); AssertColumn(window);
                if (name == "Light-1")
                {
                    Directory.CreateDirectory(Output + "/Frames");
                    Set(window, "light", true);
                    for (int f = 0; f < 40; f++)
                    {
                        window.SeekPreview(f / 30f); AssertColumn(window);
                        var image = (Texture)typeof(DamageFontStudioWindow).GetMethod("RenderPreview", Fields).Invoke(window, new object[] { new Rect(0, 0, 480, 480) });
                        Save(image, Output + "/Frames/" + f.ToString("D3") + ".png");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
        static void AssertColumn(DamageFontStudioWindow window)
        {
            var source = Get<GameObject>(window, "previewSource");
            var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Folder + "/" + source.name + "-Session.asset");
            var samples = session.samples.OrderBy(s => s.frame).ToArray(); var roots = Roots(window);
            for (int i = 0; i < roots.Length; i++)
            {
                if (!roots[i].activeSelf) continue;
                double time = session.TimeOf(samples[0]) + Get<float>(window, "time") - i * .12f;
                var a = samples[0]; var b = samples.Last();
                for (int k = 1; k < samples.Length; k++) if (session.TimeOf(samples[k]) >= time) { a = samples[k - 1]; b = samples[k]; break; }
                float t = (float)((time - session.TimeOf(a)) / Math.Max(.000001, session.TimeOf(b) - session.TimeOf(a)));
                var delta = (Vector2.Lerp(a.center, b.center, t) - samples[0].center) * session.unitsPerPixel;
                var expected = new Vector2(delta.x, -50 - delta.y);
                Check(Vector2.Distance(roots[i].transform.localPosition, expected) < .02f, "Each hit preserves measured XY after its common spawn");
            }
        }
        static GameObject[] Roots(DamageFontStudioWindow window)
            => Get<List<DamageFontPlayback>>(window, "players").Select(p =>
            {
                var target = typeof(DamageFontPlayback).GetField("target", Fields).GetValue(p);
                return (GameObject)target.GetType().GetField("Root").GetValue(target);
            }).ToArray();
        static void Save(Texture source, string path)
        {
            var previous = RenderTexture.active;
            var pixels = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = (RenderTexture)source; pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); }
        }
        static T Get<T>(DamageFontStudioWindow window, string field) => (T)typeof(DamageFontStudioWindow).GetField(field, Fields).GetValue(window);
        static void Set(DamageFontStudioWindow window, string field, object value) => typeof(DamageFontStudioWindow).GetField(field, Fields).SetValue(window, value);
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Reference series: " + message); }
    }
}
