using System;
using System.IO;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class StudioVerification
    {
        [MenuItem("Tools/Damage Font Studio/Run Playback Checks")]
        public static void Run()
        {
            GameObject go = null;
            DamageFontPreset preset = null;
            DamageFontPlayback player = null;
            int baseline = DOTween.TotalActiveTweens();
            try
            {
                DamageFontStudioWindow.CreateExamples();
                preset = ScriptableObject.CreateInstance<DamageFontPreset>();
                preset.SetDefaults(DamageKind.Critical);
                preset.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                Check(preset.font, "TMP sample font exists");
                go = new GameObject("Playback checks", typeof(RectTransform));
                go.hideFlags = HideFlags.HideAndDontSave;
                var text = go.AddComponent<TextMeshPro>();
                text.font = preset.font;
                player = new DamageFontPlayback(text);
                player.Prepare(preset, "123456", Vector3.zero, manual: true);
                player.Seek(0.3f);
                var position = go.transform.localPosition;
                var scale = go.transform.localScale;
                Check(position.y > 0, "Position tween runs");
                player.Seek(0.8f);
                Check(text.color.a < 1, "Opacity tween runs");
                player.Seek(0.3f);
                Check(Vector3.Distance(position, go.transform.localPosition) < 0.001f, "Backward seek position is deterministic");
                Check(Vector3.Distance(scale, go.transform.localScale) < 0.001f, "Backward seek scale is deterministic");
                player.Seek(-0.1f);
                Check(!go.activeSelf, "Future hits stay hidden");
                player.Seek(0);
                Check(go.activeSelf && go.transform.localPosition == Vector3.zero, "Seek to zero restores initial state");
                int completed = 0;
                for (int i = 0; i < 100; i++)
                {
                    player.Prepare(preset, "42", Vector3.one, () => completed++, true);
                    player.Seek(preset.Duration);
                    Check(!go.activeSelf, "Completed instance hidden");
                    player.Cancel();
                    Check(!player.IsActive && text.text == "", "Cancel releases sequence and text");
                }
                Check(completed == 100, "One completion per playback");
                player.Prepare(preset, "99", Vector3.zero, () => completed++, true);
                player.Cancel();
                Check(completed == 100, "Cancellation does not fire completion");
                const string roundtripPath = "Assets/DamageFontStudio/Presets/VerificationRoundTrip.asset";
                string tempPath = AssetDatabase.GenerateUniqueAssetPath(roundtripPath);
                var saved = UnityEngine.Object.Instantiate(preset);
                AssetDatabase.CreateAsset(saved, tempPath);
                AssetDatabase.SaveAssets();
                try
                {
                    AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceUpdate);
                    var loaded = AssetDatabase.LoadAssetAtPath<DamageFontPreset>(tempPath);
                    player.Prepare(loaded, "123456", Vector3.zero, manual: true);
                    player.Seek(0.3f);
                    Check(Vector3.Distance(position, go.transform.localPosition) < 0.001f, "Saved preset reproduces playback");
                    player.Cancel();
                }
                finally { AssetDatabase.DeleteAsset(tempPath); }
                var arcPreset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>("Assets/DamageFontStudio/Presets/FallingArc.asset");
                Check(arcPreset && arcPreset.segments[0].positionPath == DamagePositionPath.Parabola, "Parabola example saved and loaded");
                preset.segments.Clear();
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = 1, targetVector = new Vector3(160, -120, 0), positionPath = DamagePositionPath.Parabola, arcHeight = 140, ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, start = 1, duration = 1, targetVector = new Vector3(-100, -180, 0), positionPath = DamagePositionPath.Parabola, arcHeight = 50, ease = Ease.Linear });
                var arcOrigin = new Vector3(12, 34, 0);
                player.Prepare(preset, "42", arcOrigin, manual: true);
                player.Seek(0.5f);
                Check(Vector3.Distance(go.transform.localPosition, arcOrigin + new Vector3(80, 80, 0)) < 0.01f, "Parabola rises above spawn");
                player.Seek(0.75f);
                Check(go.transform.localPosition.y < arcOrigin.y + 80, "Parabola descends");
                player.Seek(1);
                Check(Vector3.Distance(go.transform.localPosition, arcOrigin + new Vector3(160, -120, 0)) < 0.01f, "Parabola lands at target");
                player.Seek(1.5f);
                Check(Vector3.Distance(go.transform.localPosition, arcOrigin + new Vector3(30, -100, 0)) < 0.01f, "Chained arc starts from previous target");
                player.Seek(0.5f);
                Check(Vector3.Distance(go.transform.localPosition, arcOrigin + new Vector3(80, 80, 0)) < 0.01f, "Parabola backward seeking");
                player.Seek(0);
                Check(Vector3.Distance(go.transform.localPosition, arcOrigin) < 0.01f, "Parabola restarts at spawn");
                player.Cancel();
                preset.segments.Clear();
                preset.initialColor = Color.white;
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Rotation, duration = 1, targetValue = 90, ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Color, duration = 1, targetColor = Color.red, ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = 1, targetVector = new Vector3(100, 0, 0), useCurve = true, curve = AnimationCurve.Linear(0, 0, 1, 1) });
                player.Prepare(preset, "42", Vector3.zero, manual: true);
                player.Seek(0.5f);
                Check(Mathf.Abs(go.transform.localEulerAngles.z - 45) < 0.01f, "Rotation channel");
                Check(Mathf.Abs(text.color.g - 0.5f) < 0.01f, "Color channel");
                Check(Mathf.Abs(go.transform.localPosition.x - 50) < 0.01f, "Custom curve");
                player.Cancel();
                player.Prepare(preset, "42", Vector3.zero, () => player.Cancel(), true);
                player.Seek(1);
                Check(!player.IsActive && !go.activeSelf, "Completion callback can safely return to pool");
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Position });
                Check(preset.Validate() != null, "Overlapping same-channel segments are rejected");
                Check(DOTween.TotalActiveTweens() == baseline, $"No owned tweens remain (before={baseline}, after={DOTween.TotalActiveTweens()}, sequences={DOTween.TotalActiveSequences()}, tweeners={DOTween.TotalActiveTweeners()})");
                Directory.CreateDirectory("Logs");
                StudioObjectVerification.Run();
                var window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
                try
                {
                    window.VerifyRendering("Logs/FontStudioPreview-dark.png", false);
                    window.VerifyRendering("Logs/FontStudioPreview-light.png", true);
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
                Check(DOTween.TotalActiveTweens() == baseline, "Preview rebuild and cleanup release all sequences");
                File.WriteAllText("Logs/FontStudioVerification.txt", "PASS: TMP font, movement, scale, opacity, rotation, color, custom curve, parabola rise/fall/landing/chaining/backward seek/restart and saved example, backward seek, initial state, delayed hits, 100 reuses, completion, cancellation, callback pool return, asset round-trip, overlap validation, tween cleanup, dark/light preview rendering and cleanup.\n");
                File.AppendAllText("Logs/FontStudioVerification.txt", "PASS: scene object and prefab previews, root animation, authored scale/rotation/layout preservation, TMP binding, UI/sprite opacity, 20 reuses, source preservation, cleanup.\n");
                Debug.Log("Damage Font Studio: all playback checks passed.");
            }
            finally
            {
                player?.Dispose();
                if (go) UnityEngine.Object.DestroyImmediate(go);
                if (preset) UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("Damage Font Studio check failed: " + label);
        }
    }

    [InitializeOnLoad]
    static class StudioFirstRun
    {
        static StudioFirstRun()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
                DamageFontStudioWindow.CreateExamples();
            };
        }
    }
}
