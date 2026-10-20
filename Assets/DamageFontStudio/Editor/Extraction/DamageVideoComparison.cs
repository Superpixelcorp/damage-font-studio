using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Actual shared DOTween playback rendered over the video, not a second motion evaluator.
    internal sealed class DamageVideoComparison : IDisposable
    {
        PreviewRenderUtility preview;
        DamageFontPlayback playback;
        DamageFontPreset preset;
        DamageFontSequencePlayback sequencePlayback;

        public void Build(DamageFontSequence sequence, string number = null, bool includeSourceCameraMotion = false)
        {
            Dispose(); preview = new PreviewRenderUtility();
            preview.camera.orthographic = true; preview.camera.orthographicSize = sequence.sourceHeight / 2f;
            preview.camera.transform.position = new Vector3(sequence.sourceWidth / 2f, -sequence.sourceHeight / 2f, -1000);
            preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 2000;
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = Color.clear;
            var parent = new GameObject("Recorded hit sequence") { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(parent);
            sequencePlayback = new DamageFontSequencePlayback(sequence, parent.transform, number, includeSourceCameraMotion);
            foreach (var text in parent.GetComponentsInChildren<TMP_Text>(true)) text.isOrthographic = true;
        }
        public void Build(DamageVideoSession session)
        {
            Dispose();
            preset = session.BuildPreset();
            var font = session.font ? session.font : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (!font) { Dispose(); throw new InvalidOperationException("Choose a TMP font or import TMP Essential Resources."); }
            preview = new PreviewRenderUtility();
            preview.camera.orthographic = true;
            preview.camera.orthographicSize = session.height * session.unitsPerPixel / 2;
            preview.camera.transform.position = new Vector3(session.width * session.unitsPerPixel / 2, -session.height * session.unitsPerPixel / 2, -1000);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.nearClipPlane = 0.1f;
            preview.camera.farClipPlane = 2000;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = Color.clear;
            var go = new GameObject("Extracted animation", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            preview.AddSingleGO(go);
            var text = go.AddComponent<TextMeshPro>();
            text.font = font; text.fontSize = preset.fontSize; text.isOrthographic = true;
            text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = false; text.text = string.IsNullOrEmpty(session.number) ? "123456" : session.number;
            text.rectTransform.sizeDelta = new Vector2(10000, 10000);
            text.ForceMeshUpdate();
            var first = session.At(session.startFrame);
            var bounds = DamageVideoSampleExporter.InkSize(text);
            // Appearance calibration is separate from motion: fit the chosen font to the initial box.
            go.transform.localScale = new Vector3(first.size.x * session.unitsPerPixel / Mathf.Max(1, bounds.x * preset.initialScale.x), first.size.y * session.unitsPerPixel / Mathf.Max(1, bounds.y * preset.initialScale.y), 1);
            playback = new DamageFontPlayback(go, text, true);
            playback.Prepare(preset, text.text, new Vector3(first.center.x, -first.center.y, 0) * session.unitsPerPixel, manual: true);
        }

        public Texture Render(Rect rect, float time)
        {
            if (preview == null) return null;
            playback?.Seek(time); sequencePlayback?.Seek(time);
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true);
            return preview.EndPreview();
        }
        public void Dispose()
        {
            playback?.Dispose(); playback = null;
            sequencePlayback?.Dispose(); sequencePlayback = null;
            preview?.Cleanup(); preview = null;
            if (preset) UnityEngine.Object.DestroyImmediate(preset);
            preset = null;
        }
    }
}
