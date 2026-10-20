using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    [Serializable]
    public sealed class DamageVideoSample
    {
        public int frame;
        public double timestamp = -1;
        // Source pixels, top-left origin. Angle is clockwise in the video.
        public Vector2 center;
        public Vector2 cameraOffset;
        public Vector2 size;
        public float angle;
        [Range(0, 1)] public float opacity = 1;
        public float confidence = 1;
        public bool manual = true;
        public DamageVideoSample Copy() => (DamageVideoSample)MemberwiseClone();
    }

    [CreateAssetMenu(menuName = "Damage Font Studio/Video Extraction Session")]
    public sealed class DamageVideoSession : ScriptableObject
    {
        public string videoPath;
        public int width, height, frameCount;
        public double fps;
        public int startFrame, endFrame;
        public bool limitAnalysisRegion;
        public Rect analysisRegion;
        public bool regionTruncatedStart, regionTruncatedEnd;
        [Min(1)] public int sampleStep = 2;
        [Range(4, 128)] public int searchRadius = 40;
        [Range(0, 1)] public float minimumConfidence = 0.65f;
        public string number = "123456";
        public TMP_FontAsset font;
        public Material fontMaterial;
        public bool autoAppearance;
        [Tooltip("Use a common spawn X when exporting a hit sequence. Motion after spawning is preserved.")]
        public bool lockHorizontalPosition;
        public bool observedEnd;
        public bool removeCameraMotion;
        public bool rangeTruncatedStart, rangeTruncatedEnd;
        public bool motionInferred;
        public bool sharedMotion;
        public float sharedMotionDuration;
        public string detectedNumber;
        public float numberConfidence;
        public float calibratedFontSize;
        public float calibratedWidthScale = 1;
        public Color color = Color.yellow;
        [Min(0.001f)] public float unitsPerPixel = 1;
        public List<DamageVideoSample> samples = new List<DamageVideoSample>();

        public void Put(DamageVideoSample sample)
        {
            samples.RemoveAll(s => s.frame == sample.frame);
            samples.Add(sample);
            samples.Sort((a, b) => a.frame.CompareTo(b.frame));
        }

        public DamageVideoSample At(int frame)
        {
            var ordered = samples.OrderBy(s => s.frame).ToArray();
            if (ordered.Length == 0) return null;
            if (frame <= ordered[0].frame) { var first = ordered[0].Copy(); first.frame = frame; return first; }
            for (int i = 1; i < ordered.Length; i++)
            {
                if (frame > ordered[i].frame) continue;
                var a = ordered[i - 1]; var b = ordered[i];
                float t = (float)(frame - a.frame) / (b.frame - a.frame);
                return new DamageVideoSample { frame = frame, center = Vector2.Lerp(a.center, b.center, t),
                    cameraOffset = Vector2.Lerp(a.cameraOffset, b.cameraOffset, t),
                    size = Vector2.Lerp(a.size, b.size, t), angle = Mathf.Lerp(a.angle, b.angle, t),
                    timestamp = a.timestamp >= 0 && b.timestamp >= 0 ? a.timestamp + (b.timestamp - a.timestamp) * t : -1,
                    opacity = Mathf.Lerp(a.opacity, b.opacity, t), confidence = Mathf.Min(a.confidence, b.confidence), manual = false };
            }
            var last = ordered[ordered.Length - 1].Copy(); last.frame = frame; return last;
        }

        internal bool ClipToRange(int first, int last, IReadOnlyDictionary<int, double> timestamps = null)
        {
            int clippedFirst = Math.Max(first, startFrame), clippedLast = Math.Min(last, endFrame);
            if (clippedLast <= clippedFirst || samples.Count < 2) return false;
            var a = At(clippedFirst); var b = At(clippedLast);
            if (timestamps != null)
            {
                if (timestamps.TryGetValue(clippedFirst, out var start)) a.timestamp = start;
                if (timestamps.TryGetValue(clippedLast, out var end)) b.timestamp = end;
            }
            rangeTruncatedStart |= clippedFirst > startFrame;
            rangeTruncatedEnd |= clippedLast < endFrame;
            if (rangeTruncatedEnd) observedEnd = false;
            startFrame = clippedFirst; endFrame = clippedLast;
            samples.RemoveAll(s => s.frame < startFrame || s.frame > endFrame);
            Put(a); Put(b);
            return true;
        }

        public string ValidateExport()
        {
            if (fps <= 0 || double.IsNaN(fps) || double.IsInfinity(fps) || width <= 0 || height <= 0) return "Load a video first.";
            if (startFrame < 0 || endFrame <= startFrame || endFrame >= frameCount) return "Select a valid start/end frame range.";
            if (!float.IsFinite(unitsPerPixel) || unitsPerPixel <= 0) return "Units per pixel must be positive.";
            var selected = samples.Where(s => s.frame >= startFrame && s.frame <= endFrame).OrderBy(s => s.frame).ToArray();
            if (selected.Length < 2 || selected[0].frame != startFrame || selected[selected.Length - 1].frame != endFrame)
                return "Add samples at both Start and End (at least two samples).";
            if (selected.Length > 600) return "Use at most 600 samples per preset; shorten the range or increase sample spacing.";
            if (selected.Select(s => s.frame).Distinct().Count() != selected.Length) return "Duplicate sample frames.";
            foreach (var s in selected)
                if (!float.IsFinite(s.center.x) || !float.IsFinite(s.center.y) || !float.IsFinite(s.size.x) || !float.IsFinite(s.size.y) ||
                    !float.IsFinite(s.angle) || !float.IsFinite(s.opacity) || s.opacity < 0 || s.opacity > 1 || s.size.x < 2 || s.size.y < 2)
                    return "Samples need finite positions/angles, size >= 2 and opacity 0..1.";
            for (int i = 1; i < selected.Length; i++)
                if (!double.IsFinite(TimeOf(selected[i])) || TimeOf(selected[i]) <= TimeOf(selected[i - 1]))
                    return "Sample timestamps must increase.";
            return null;
        }

        // Standard segments: exported assets have no dependency on the video or editor session.
        public DamageFontPreset BuildPreset()
        {
            string error = ValidateExport();
            if (error != null) throw new InvalidOperationException(error);
            var result = CreateInstance<DamageFontPreset>();
            var list = samples.Where(s => s.frame >= startFrame && s.frame <= endFrame).OrderBy(s => s.frame).ToArray();
            var first = list[0];
            result.font = font;
            result.fontMaterial = fontMaterial;
            result.overrideFontStyle = autoAppearance;
            result.fontStyle = autoAppearance ? FontStyles.Bold | FontStyles.Italic : FontStyles.Normal;
            result.fontSize = (autoAppearance ? (calibratedFontSize > 0 ? calibratedFontSize : first.size.y * 1.4f) : first.size.y) * unitsPerPixel;
            result.initialColor = color;
            result.initialRotation = -first.angle;
            result.initialOpacity = first.opacity;
            if (autoAppearance) result.initialScale = new Vector3(calibratedWidthScale, 1, 1);
            for (int i = 1; i < list.Length; i++)
            {
                var a = list[i - 1]; var b = list[i];
                float start = (float)(TimeOf(a) - TimeOf(first));
                float end = (float)(TimeOf(b) - TimeOf(first));
                Vector2 delta = b.center - first.center;
                if (removeCameraMotion) delta -= b.cameraOffset - first.cameraOffset;
                result.segments.Add(Segment(DamageChannel.Position, start, end, new Vector3(delta.x, -delta.y, 0) * unitsPerPixel));
                result.segments.Add(Segment(DamageChannel.Scale, start, end, new Vector3(b.size.x / first.size.x * result.initialScale.x, b.size.y / first.size.y, 1)));
                result.segments.Add(Segment(DamageChannel.Rotation, start, end, Vector3.zero, -b.angle));
                result.segments.Add(Segment(DamageChannel.Opacity, start, end, Vector3.zero, b.opacity));
            }
            return result;
        }

        public double TimeOf(DamageVideoSample sample) => sample.timestamp >= 0 ? sample.timestamp : sample.frame / fps;

        static DamageSegment Segment(DamageChannel channel, float start, float end, Vector3 vector, float value = 0)
            => new DamageSegment { channel = channel, start = start, duration = end - start, targetVector = vector, targetValue = value, ease = Ease.Linear };
    }
}
