using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoFinalMotionVerification
    {
        const string Output = "Logs/FinalGameMotionChecks";
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static DamageVideoAutoJob job;
        static double deadline;
        static T Get<T>(DamageFontStudioWindow window, string name) => (T)typeof(DamageFontStudioWindow).GetField(name, Fields).GetValue(window);
        static void Set(DamageFontStudioWindow window, string name, object value) => typeof(DamageFontStudioWindow).GetField(name, Fields).SetValue(window, value);
        static GameObject Root(DamageFontPlayback player)
        {
            var target = typeof(DamageFontPlayback).GetField("target", Fields).GetValue(player);
            return (GameObject)target.GetType().GetField("Root").GetValue(target);
        }
        static void Check(bool value, string message) { if (!value) throw new Exception("Final game motion: " + message); }
        static DamageFontPreset Preset(float x, float duration)
        {
            var preset = ScriptableObject.CreateInstance<DamageFontPreset>(); preset.animationOnly = true;
            preset.initialScale = Vector3.one * 1.2f;
            preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = duration, targetVector = new Vector3(x, 60, 0), ease = Ease.Linear });
            preset.segments.Add(new DamageSegment { channel = DamageChannel.Scale, duration = duration, targetVector = Vector3.one, ease = Ease.Linear });
            preset.segments.Add(new DamageSegment { channel = DamageChannel.Opacity, duration = duration, targetValue = 0, ease = Ease.Linear });
            return preset;
        }
        public static void RunChecks()
        {
            Directory.CreateDirectory(Output);
            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/DamageFontStudio/Generated/FinalMotionValidation");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>(); sequence.sourceFps = 30;
            var presets = new[] { Preset(20, .5f), Preset(500, .9f), Preset(70, .6f) };
            try
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    AssetDatabase.CreateAsset(presets[i], folder + "/Candidate-" + i + ".asset");
                    sequence.events.Add(new DamageFontSequenceEvent { motionPreset = presets[i], time = i * .1f,
                        observedDisappearance = true, motionInferred = i == 1, confidence = i == 0 ? .7f : i == 1 ? .99f : .8f });
                }
                AssetDatabase.CreateAsset(sequence, folder + "/RecordedHits.asset");
                var final = DamageVideoFinalMotion.Ensure(sequence);
                Check(final.sourceHitIndex == 2 && final.segments[0].targetVector.x == 70, "Choose one observed source, not the inferred high-score hit or an average.");
                Check(final.initialScale == presets[2].initialScale, "Selecting an animation-only candidate must preserve its intentional initial animation scale.");
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(final));
                final.segments[0].targetVector.x = 91; EditorUtility.SetDirty(final); AssetDatabase.SaveAssetIfDirty(final);
                Check(DamageVideoFinalMotion.Ensure(sequence).segments[0].targetVector.x == 91, "Reopening results must not overwrite user edits to the final asset.");
                DamageVideoFinalMotion.Select(sequence, 0);
                Check(guid == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(final)) && final.segments[0].targetVector.x == 20,
                    "Changing the chosen source updates the same final file and preserves game references.");
                Verify(sequence, "fixture");
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
        public static void Verify(DamageFontSequence sequence, string label)
        {
            var final = DamageVideoFinalMotion.Ensure(sequence);
            Check(final.animationOnly && !final.font && !final.fontMaterial && final.Validate() == null, "Final game asset must contain valid motion without sample font/material dependencies.");
            Check(!AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(final)).Any(p => p.EndsWith(".prefab") || p.EndsWith(".mat")), "Only the final animation asset is needed, not an analysis sample prefab or material.");
            int baseline = DOTween.TotalActiveTweens(), decoders = Resources.FindObjectsOfTypeAll<VideoPlayer>().Length;
            var window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
            var source = new GameObject("Existing game final preview fixture", typeof(TextMeshPro));
            var text = source.GetComponent<TextMeshPro>(); text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            text.textWrappingMode = TextWrappingModes.NoWrap; text.rectTransform.sizeDelta = new Vector2(1200, 200);
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 44; text.fontStyle = FontStyles.Underline; text.color = new Color(.4f, .8f, .6f, .7f); text.text = "GAME";
            source.transform.localScale = new Vector3(2, .8f, 1); source.transform.localRotation = Quaternion.Euler(0, 0, 14);
            var reference = UnityEngine.Object.Instantiate(source);
            try
            {
                if (sequence.events[0].sample) Set(window, "previewSource", sequence.events[0].sample);
                window.UsePreset(final);
                Check(!Get<GameObject>(window, "previewSource"), "Opening the final game asset must not silently reuse an analysis sample prefab.");
                window.UsePreset(final, source);
                Check(Get<DamageFontPreset>(window, "preset") == final && Get<GameObject>(window, "previewSource") == source,
                    "The playback lab must use the exact saved final asset and the supplied game object.");
                Set(window, "hits", 3); Set(window, "interval", .13f); Set(window, "spawn", new Vector2(7, -13)); Set(window, "randomizeSpawn", false);
                using (var runtime = new DamageFontPlayback(reference, reference.GetComponent<TMP_Text>(), false))
                foreach (string number in new[] { "1", "9876543210" })
                foreach (var mode in new[] { DamageFontStudioWindow.PreviewMode.Single, DamageFontStudioWindow.PreviewMode.Series, DamageFontStudioWindow.PreviewMode.Burst })
                {
                    Set(window, "number", number); window.StartPreview(mode);
                    var players = Get<List<DamageFontPlayback>>(window, "players");
                    Check(players.Count == (mode == DamageFontStudioWindow.PreviewMode.Single ? 1 : mode == DamageFontStudioWindow.PreviewMode.Series ? 3 : 50), "Original single/series/burst authoring controls remain available.");
                    float length = Get<float>(window, "length");
                    foreach (float time in new[] { .05f, .15f, .3f, .07f, Mathf.Max(0, length - .01f) })
                    {
                        window.SeekPreview(time);
                        for (int i = 0; i < players.Count; i++)
                        {
                            float interval = mode == DamageFontStudioWindow.PreviewMode.Series ? .13f : 0;
                            runtime.Prepare(final, number, new Vector3(7, -13, -i * .01f), manual: true); runtime.Seek(time - i * interval);
                            var clone = Root(players[i]); var actualText = clone.GetComponent<TMP_Text>();
                            Check(Vector3.Distance(clone.transform.localPosition, reference.transform.localPosition) < .002f &&
                                Vector3.Distance(clone.transform.localScale, reference.transform.localScale) < .002f &&
                                Quaternion.Angle(clone.transform.localRotation, reference.transform.localRotation) < .002f && clone.activeSelf == reference.activeSelf,
                                "Playback-lab movement/scale/rotation/visibility must match the game runtime at the same age.");
                            Check(actualText.text == number && actualText.font == text.font && actualText.fontSharedMaterial == text.fontSharedMaterial && actualText.fontSize == 44 && actualText.fontStyle == text.fontStyle,
                                "The actual game font/material/style/base size survive digit changes in final preview.");
                            Check(Mathf.Abs(actualText.color.r - .4f) < .0001f && Mathf.Abs(actualText.color.g - .8f) < .0001f && Mathf.Abs(actualText.color.b - .6f) < .0001f &&
                                Mathf.Abs(actualText.color.a - reference.GetComponent<TMP_Text>().color.a) < .0001f,
                                "The game object's RGB remains authored and its fade matches game playback.");
                        }
                    }
                }
                window.StartPreview(DamageFontStudioWindow.PreviewMode.Series); window.SeekPreview(Mathf.Min(.15f, final.Duration * .4f));
                var rendered = (RenderTexture)typeof(DamageFontStudioWindow).GetMethod("RenderPreview", Fields).Invoke(window, new object[] { new Rect(0, 0, 800, 600) });
                var previous = RenderTexture.active; var image = new Texture2D(800, 600, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = rendered; image.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); image.Apply();
                    var pixels = image.GetPixels32(); var bg = pixels[0];
                    Check(pixels.Count(p => Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 30) > 100, "Final game preview actually renders visible text.");
                    File.WriteAllBytes(Output + "/" + label + ".png", image.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
                window.StopPreview();
                Check(text.text == "GAME" && text.fontSize == 44 && source.transform.localScale == new Vector3(2, .8f, 1), "Preview must leave the existing game object unchanged.");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); UnityEngine.Object.DestroyImmediate(reference); UnityEngine.Object.DestroyImmediate(source); }
            Check(DOTween.TotalActiveTweens() == baseline && Resources.FindObjectsOfTypeAll<VideoPlayer>().Length == decoders, "Final preview owns its tweens and uses no video decoder.");
            File.WriteAllText(Output + "/" + label + ".txt", "PASS: one explicit GameMotion.asset; same file tested and used by game runtime; existing game prefab appearance preserved; 1/9876543210; single/series/burst; reverse seek; render; no source mutation, tween leaks or video decoder.\nFile: " + AssetDatabase.GetAssetPath(final) + "\nSource: " + final.sourceHitName + " (hit " + (final.sourceHitIndex + 1) + ")\n");
        }
        public static void RunExistingAndReference()
        {
            try
            {
                Directory.CreateDirectory(Output); if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
                RunChecks(); DamageFontRecordedPlaybackVerification.Run(); DamageVideoRegionVerification.RunChecks();
                var args = Environment.GetCommandLineArgs(); int existing = Array.IndexOf(args, "-existingSequence"), video = Array.IndexOf(args, "-referenceVideo");
                if (existing >= 0)
                {
                    var sequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(args[existing + 1]);
                    Check(sequence, "Existing user analysis must load."); Verify(sequence, "current-result");
                }
                if (video < 0) throw new ArgumentException("Pass -referenceVideo.");
                deadline = EditorApplication.timeSinceStartup + 600; EditorApplication.update += Watchdog;
                job = new DamageVideoAutoJob(args[video + 1], 1, 1.8f, Completed, message => Finish(new Exception(message)),
                    lockHorizontalPosition: true, analysisRegion: new Rect(40, 65, 180, 225));
            }
            catch (Exception e) { Finish(e); }
        }
        public static void RunCurrentPreviewChecks()
        {
            try
            {
                if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
                var args = Environment.GetCommandLineArgs(); int existing = Array.IndexOf(args, "-existingSequence");
                if (existing < 0) throw new ArgumentException("Pass -existingSequence.");
                var sequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(args[existing + 1]);
                VerifyFallback(sequence);
                Verify(sequence, "current-result");
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        static void VerifyFallback(DamageFontSequence sequence)
        {
            Directory.CreateDirectory(Output);
            var window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
            try
            {
                window.UsePreset(DamageVideoFinalMotion.Get(sequence));
                Set(window, "previewSource", null); Set(window, "gamePreviewSource", null);
                Set(window, "number", "123456");
                window.StartPreview(DamageFontStudioWindow.PreviewMode.Single);
                window.SeekPreview(.15f);
                var root = Root(Get<List<DamageFontPlayback>>(window, "players")[0]);
                var text = root.GetComponent<TMP_Text>();
                Check(text.fontSize == 64 && text.color.r == 1 && text.text == "123456", "Fallback digits have an explicit readable size and neutral appearance.");
                var rendered = (RenderTexture)typeof(DamageFontStudioWindow).GetMethod("RenderPreview", Fields).Invoke(window, new object[] { new Rect(0, 0, 800, 350) });
                var previous = RenderTexture.active; var image = new Texture2D(800, 350, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = rendered; image.ReadPixels(new Rect(0, 0, 800, 350), 0, 0); image.Apply();
                    File.WriteAllBytes(Output + "/fallback.png", image.EncodeToPNG());
                    var pixels = image.GetPixels32(); var bg = pixels[0];
                    int changed = pixels.Count(p => Math.Abs(p.r - bg.r) + Math.Abs(p.g - bg.g) + Math.Abs(p.b - bg.b) > 30);
                    File.WriteAllText(Output + "/fallback.txt", $"pixels={changed}; active={root.activeSelf}; size={text.fontSize}; color={text.color}; font={text.font}; material={text.fontSharedMaterial}; position={root.transform.position}; scale={root.transform.localScale}; bounds={text.bounds}; chars={text.textInfo.characterCount}");
                    Check(changed > 100, "No-prefab final preview must render visible default digits. " + File.ReadAllText(Output + "/fallback.txt"));
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
                Set(window, "spawn", new Vector2(50000, -50000)); Set(window, "zoom", .01f);
                Set(window, "number", ""); Set(window, "randomizeSpawn", true); Set(window, "loop", false);
                window.PlayDefaultText();
                Check(Get<Vector2>(window, "spawn") == Vector2.zero && Get<float>(window, "zoom") == 1 && Get<bool>(window, "loop") && !Get<bool>(window, "randomizeSpawn"),
                    "Default-digit action resets off-screen viewing settings and starts repeating playback.");
                Check(Get<DamageFontPreset>(window, "preset") == DamageVideoFinalMotion.Get(sequence) && !Get<GameObject>(window, "previewSource"), "Fallback action keeps the exact final motion asset without any prefab.");
                window.Advance(DamageVideoFinalMotion.Get(sequence).Duration * 5 + .15f);
                Check(Get<bool>(window, "running") && Root(Get<List<DamageFontPlayback>>(window, "players")[0]).activeSelf, "Opening the short effect must not leave an empty ended preview.");
                window.StopPreview(); Set(window, "preset", null); window.StartPreview(DamageFontStudioWindow.PreviewMode.Single);
                Check(!string.IsNullOrEmpty(Get<string>(window, "previewError")), "Preview construction failures must be reported in the window, not silently ignored.");
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }
        static void Watchdog() { if (EditorApplication.timeSinceStartup > deadline) Finish(new TimeoutException("Final game motion checks timed out.")); }
        static void Completed(List<GameObject> samples, string folder)
        {
            try
            {
                var sequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder + "/RecordedHits.asset");
                Check(samples.Count > 0 && sequence, "Automatic extraction produces analysis hits.");
                Check(samples.All(p => AssetDatabase.GetAssetPath(p).StartsWith(folder + "/Analysis/")), "Per-hit analysis assets must be grouped below Analysis, not mixed with the final file.");
                Check(File.Exists(folder + "/Analysis/Analysis.json") && DamageVideoFinalMotion.Get(sequence), "Automatic extraction creates the one final game file and separate diagnostics.");
                Verify(sequence, "new-analysis"); DamageVideoExistingPrefabVerification.VerifySequence(sequence);
                File.WriteAllText(Output + "/reference.txt", "PASS: actual selected time + area; GameMotion.asset automatically selected from one measured hit; analysis details grouped; final game preview uses exact saved motion on an existing prefab.\nFolder: " + folder);
                Finish(null);
            }
            catch (Exception e) { Finish(e); }
        }
        static void Finish(Exception error)
        {
            job?.Dispose(); job = null; EditorApplication.update -= Watchdog;
            Directory.CreateDirectory(Output);
            if (error != null) { File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error); }
            else Debug.Log("Final game motion selection, preview and game runtime checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
