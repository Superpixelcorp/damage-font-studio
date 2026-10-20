using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace DamageFontStudio
{
    public enum DamageKind { Normal, Critical, Incoming, Skill }
    public enum DamageChannel { Position, Scale, Rotation, Opacity, Color }
    public enum DamagePositionPath { Direct, Parabola }

    [Serializable]
    public sealed class DamageSegment
    {
        public DamageChannel channel;
        [Min(0)] public float start;
        [Min(0.001f)] public float duration = 0.3f;
        public Vector3 targetVector = Vector3.one;
        public DamagePositionPath positionPath;
        [Min(0), Tooltip("Vertical lift above the straight start-to-end line at the path midpoint.")]
        public float arcHeight = 120;
        public float targetValue = 1;
        public Color targetColor = Color.white;
        public Ease ease = Ease.OutQuad;
        public bool useCurve;
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
    }

    [CreateAssetMenu(menuName = "Damage Font Studio/Preset")]
    public sealed class DamageFontPreset : ScriptableObject
    {
        public DamageKind kind;
        [Tooltip("Apply animation while preserving the game's authored font, material, style and RGB colors.")]
        public bool animationOnly;
        [HideInInspector] public int sourceHitIndex = -1;
        [HideInInspector] public string sourceHitName;
        public TMP_FontAsset font;
        public Material fontMaterial;
        public bool overrideFontStyle;
        public FontStyles fontStyle;
        [Min(1)] public float fontSize = 64;
        public Color initialColor = Color.white;
        [Range(0, 1)] public float initialOpacity = 1;
        public Vector3 initialScale = Vector3.one;
        public float initialRotation;
        public List<DamageSegment> segments = new List<DamageSegment>();

        public float Duration
        {
            get
            {
                float end = 0.01f;
                foreach (var s in segments) if (s != null) end = Mathf.Max(end, s.start + s.duration);
                return end;
            }
        }

        public string Validate()
        {
            foreach (var s in segments)
            {
                if (s == null || !float.IsFinite(s.start) || !float.IsFinite(s.duration) || s.start < 0 || s.duration <= 0)
                    return "Start must be >= 0 and duration must be > 0 (finite values).";
                if (s.useCurve && (s.curve == null || s.curve.length < 2)) return "A custom curve needs at least two keys.";
                if (s.channel == DamageChannel.Position && s.positionPath == DamagePositionPath.Parabola &&
                    (!float.IsFinite(s.arcHeight) || s.arcHeight < 0)) return "Arc Height must be a finite value >= 0.";
            }
            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                    if (segments[i].channel == segments[j].channel &&
                        OverlapsBeyondRounding(segments[i], segments[j]))
                        return "Segments on the same channel must not overlap.";
            return null;
        }

        static bool OverlapsBeyondRounding(DamageSegment a, DamageSegment b)
        {
            // Frame times and (end - start) are stored as floats. Reconstructing an end
            // can land a few float rounding units past the next start, particularly
            // with non-integer video frame rates and different evaluation precision.
            double aEnd = (double)a.start + a.duration;
            double bEnd = (double)b.start + b.duration;
            double overlap = Math.Min(aEnd, bEnd) - Math.Max(a.start, b.start);
            if (overlap <= 0) return false;
            const double floatPrecision = 1.1920928955078125e-7;
            double tolerance = Math.Min(
                Math.Max(Math.Abs(aEnd), Math.Abs(bEnd)) * 2 * floatPrecision,
                Math.Min(a.duration, b.duration) * 0.0001);
            return overlap > tolerance;
        }

        public void SetDefaults(DamageKind damageKind)
        {
            kind = damageKind;
            initialColor = kind == DamageKind.Critical ? new Color(1, 0.7f, 0.15f) :
                kind == DamageKind.Incoming ? new Color(1, 0.25f, 0.25f) :
                kind == DamageKind.Skill ? new Color(0.3f, 0.8f, 1) : Color.white;
            fontSize = kind == DamageKind.Critical ? 82 : 64;
            segments = new List<DamageSegment>
            {
                new DamageSegment { channel = DamageChannel.Position, duration = 0.9f, targetVector = new Vector3(0, 150, 0), ease = Ease.OutCubic },
                new DamageSegment { channel = DamageChannel.Scale, duration = 0.12f, targetVector = Vector3.one * 1.35f, ease = Ease.OutBack },
                new DamageSegment { channel = DamageChannel.Scale, start = 0.12f, duration = 0.2f, targetVector = Vector3.one },
                new DamageSegment { channel = DamageChannel.Opacity, start = 0.5f, duration = 0.4f, targetValue = 0, ease = Ease.InQuad }
            };
        }
    }
}
