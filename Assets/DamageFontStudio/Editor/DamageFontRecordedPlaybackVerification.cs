using System;
using System.Collections;
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
    public static class DamageFontRecordedPlaybackVerification
    {
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Get<T>(DamageFontStudioWindow w, string name) => (T)typeof(DamageFontStudioWindow).GetField(name, Fields).GetValue(w);
        static void Set(DamageFontStudioWindow w, string name, object value) => typeof(DamageFontStudioWindow).GetField(name, Fields).SetValue(w, value);
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Recorded playback: " + message); }
        static Transform Root(DamageFontStudioWindow w)
        {
            var player = Get<DamageFontSequencePlayback>(w, "recordedPlayback");
            var objects = (IList)typeof(DamageFontSequencePlayback).GetField("objects", Fields).GetValue(player);
            return ((GameObject)objects[0]).transform.parent;
        }
        public static void Run()
        {
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>();
            var preset = ScriptableObject.CreateInstance<DamageFontPreset>();
            var sample = new GameObject("Recorded playback fixture", typeof(TextMeshPro), typeof(DamageFontObject));
            var text = sample.GetComponent<TextMeshPro>();
            text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF"); text.fontSize = 30;
            text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
            var binding = sample.GetComponent<DamageFontObject>(); binding.preset = preset; binding.numberText = text;
            preset.font = text.font; preset.fontSize = 30;
            preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = 1, targetVector = new Vector3(100, 40, 0), ease = Ease.Linear });
            preset.segments.Add(new DamageSegment { channel = DamageChannel.Opacity, start = .6f, duration = .4f, targetValue = 0, ease = Ease.Linear });
            sequence.sourceWidth = 528; sequence.sourceHeight = 496; sequence.duration = .8f;
            sequence.lockSpawnX = true; sequence.spawnX = 100;
            foreach (var entry in new[] { (-.1f, "19"), (0f, "1274"), (.13f, "7"), (.37f, "987") })
                sequence.events.Add(new DamageFontSequenceEvent { time = entry.Item1, observedText = entry.Item2, sample = sample, position = new Vector3(180, -200, 0) });
            try { Verify(sequence, true); StudioPlaybackVerification.Run(); }
            finally { UnityEngine.Object.DestroyImmediate(sample); UnityEngine.Object.DestroyImmediate(preset); UnityEngine.Object.DestroyImmediate(sequence); }
            Debug.Log("RecordedHits playback lab checks passed.");
        }
        public static void Verify(DamageFontSequence sequence, bool fixture = false)
        {
            int baseline = DOTween.TotalActiveTweens(), decoders = Resources.FindObjectsOfTypeAll<VideoPlayer>().Length;
            var w = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
            try
            {
                var originalPreset = Get<DamageFontPreset>(w, "preset");
                var originalSource = Get<GameObject>(w, "previewSource");
                Set(w, "number", "AUTHORING"); Set(w, "hits", 7); Set(w, "interval", .23f);
                Set(w, "recordedSequence", sequence); Set(w, "recordedNumber", "");
                typeof(DamageFontStudioWindow).GetMethod("BuildRecorded", Fields).Invoke(w, new object[] { true });
                Check(Get<DamageFontSequencePlayback>(w, "recordedPlayback") != null, "Whole sequence must create a shared sequence player.");
                Check(Get<int>(w, "count") == sequence.events.Count, "All recorded events must be present.");
                float end = sequence.duration > 0 ? sequence.duration : sequence.events.Max(e => e.time + e.sample.GetComponent<DamageFontObject>().preset.Duration);
                Check(Mathf.Abs(Get<float>(w, "length") - end) < .0001f, "Timeline must stop at the selected range, even when a hit has a longer tail.");
                Check(Resources.FindObjectsOfTypeAll<VideoPlayer>().Length == decoders, "Playback lab must not create a video decoder.");
                if (fixture)
                {
                    w.SeekPreview(0); Check(Root(w).GetChild(0).gameObject.activeSelf, "A hit born before the source range must already be alive at time zero.");
                    Check(!Root(w).GetChild(2).gameObject.activeSelf, "Later hits must not appear early.");
                    w.SeekPreview(.13f); Check(Root(w).GetChild(2).gameObject.activeSelf && Mathf.Abs(Root(w).GetChild(2).localPosition.x - 100) < .001f, "Recorded onset and common spawn X must be preserved.");
                    w.SeekPreview(.2f); Check(Mathf.Abs(Root(w).GetChild(2).localPosition.x - 107) < .001f, "Per-hit motion must continue after its recorded onset.");
                    Check(Root(w).GetComponentsInChildren<TMP_Text>(true).Select(t => t.text).SequenceEqual(new[] { "19", "1274", "7", "987" }), "Saved per-hit numbers must remain distinct.");
                }
                w.SeekPreview(end * .4f); w.TogglePause(); Set(w, "speed", 2f); w.Advance(.05f);
                float advanced = end * .4f + .1f;
                Check(Mathf.Abs(Get<float>(w, "time") - advanced) < .0001f, "Speed control must advance the full sequence.");
                w.TogglePause(); w.Advance(.2f); Check(Mathf.Abs(Get<float>(w, "time") - advanced) < .0001f, "Pause must stop the full sequence.");
                Set(w, "recordedNumber", "9876543210"); w.RebuildPreview();
                Check(Root(w).GetComponentsInChildren<TMP_Text>(true).All(t => t.text == "9876543210"), "Number override must update every event.");
                w.SeekPreview(end); Check(Root(w).Cast<Transform>().All(t => !t.gameObject.activeSelf), "The entire sequence must finish hidden, including float-rounding boundaries.");
                w.SeekPreview(.1f); Check(Root(w).Cast<Transform>().Any(t => t.gameObject.activeSelf), "Backward seeking must restore visible events.");
                Set(w, "loop", true); Set(w, "speed", 1f); w.SeekPreview(end - .05f); w.TogglePause(); w.Advance(.15f);
                Check(Mathf.Abs(Get<float>(w, "time") - .1f) < .0001f, "Repeat must restart the whole recorded sequence.");
                Set(w, "recordedNumber", ""); w.RebuildPreview(); w.SeekPreview(Mathf.Min(.25f, end * .4f));
                Directory.CreateDirectory("Logs/RecordedPlaybackChecks");
                var rendered = (RenderTexture)typeof(DamageFontStudioWindow).GetMethod("RenderPreview", Fields).Invoke(w, new object[] { new Rect(0, 0, 800, 600) });
                var previous = RenderTexture.active; var pixels = new Texture2D(800, 600, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = rendered; pixels.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); pixels.Apply();
                    var colors = pixels.GetPixels32(); var bg = colors[0];
                    Check(colors.Count(c => Math.Abs(c.r - bg.r) + Math.Abs(c.g - bg.g) + Math.Abs(c.b - bg.b) > 30) > 100, "The full sequence must render visible samples in the playback lab.");
                    File.WriteAllBytes("Logs/RecordedPlaybackChecks/" + (fixture ? "fixture" : "generated") + ".png", pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(pixels); }
                w.StopPreview(); Check(Get<DamageFontSequencePlayback>(w, "recordedPlayback") == null, "Stop must release the sequence player.");
                w.SelectPlaybackTarget(false);
                Check(Get<DamageFontPreset>(w, "preset") == originalPreset && Get<GameObject>(w, "previewSource") == originalSource,
                    "Returning to authoring must restore its preset and object selection.");
                Check(Get<string>(w, "number") == "AUTHORING" && Get<int>(w, "hits") == 7 && Mathf.Abs(Get<float>(w, "interval") - .23f) < .0001f,
                    "Recorded playback must not overwrite authoring number, hit count or interval.");
                Check(Get<DamageFontSequence>(w, "recordedSequence") == sequence, "Authoring must retain the sequence for switching back.");
                w.StartPreview(DamageFontStudioWindow.PreviewMode.Series);
                Check(Get<int>(w, "count") == 7, "Authoring hit-series controls must still work after recorded playback.");
                w.SelectPlaybackTarget(true);
                Check(Get<DamageFontPreset>(w, "preset") == sequence.events[0].sample.GetComponent<DamageFontObject>().preset,
                    "Recorded playback must expose the selected hit's preset to the existing editing controls.");
            }
            finally { UnityEngine.Object.DestroyImmediate(w); }
            Check(DOTween.TotalActiveTweens() == baseline && Resources.FindObjectsOfTypeAll<VideoPlayer>().Length == decoders, "Playback must release owned tweens and create no video decoder.");
            File.WriteAllText("Logs/RecordedPlaybackChecks/" + (fixture ? "fixture-checks" : "generated-checks") + ".txt", "PASS: RecordedHits within selected range; recorded cadence and per-hit samples; tails cut at range end; speed/pause/repeat/reverse seek; number override; visible render; authoring controls restored; owned tween cleanup; no video decoder.\n");
        }
    }
}
