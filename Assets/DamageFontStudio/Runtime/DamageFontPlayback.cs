using System;
using System.Linq;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace DamageFontStudio
{
    /// <summary>One owned sequence per instance. Preview and game code share this implementation.</summary>
    public sealed class DamageFontPlayback : IDisposable
    {
        readonly DamageFontTarget target;
        bool completionSent;
        Sequence sequence;
        Vector3 origin, position, scale;
        float rotation, opacity;
        Color color;
        DamageFontPreset preset;
        public bool IsActive => sequence != null && sequence.IsActive();

        public DamageFontPlayback(TMP_Text target)
        {
            if (!target) throw new ArgumentNullException(nameof(target));
            this.target = new DamageFontTarget(target.gameObject, target, true, true);
        }

        public DamageFontPlayback(GameObject root, TMP_Text numberText = null, bool overrideTextAppearance = false)
        {
            target = new DamageFontTarget(root, numberText, overrideTextAppearance);
        }

        public void Prepare(DamageFontPreset definition, string value, Vector3 spawnPosition, Action completed = null, bool manual = false)
        {
            Cancel();
            if (!definition) throw new ArgumentNullException(nameof(definition));
            string error = definition.Validate();
            if (error != null) throw new ArgumentException(error);
            preset = definition;
            origin = spawnPosition;
            target.SetNumber(preset, value);
            completionSent = false;
            ResetState();
            target.SetVisible(true);
            sequence = DOTween.Sequence().SetId(this).SetAutoKill(false).Pause();
            sequence.SetUpdate(manual ? UpdateType.Manual : UpdateType.Normal);
            sequence.AppendInterval(preset.Duration);
            Vector3 lastPosition = Vector3.zero, lastScale = preset.initialScale;
            float lastRotation = preset.initialRotation, lastOpacity = preset.initialOpacity;
            Color lastColor = preset.initialColor;
            foreach (var s in preset.segments.OrderBy(s => s.start))
            {
                // Explicit start values make seeking independent of previous playback history.
                Vector3 fromVector = s.channel == DamageChannel.Position ? lastPosition : lastScale;
                float fromValue = s.channel == DamageChannel.Rotation ? lastRotation : lastOpacity;
                Color fromColor = lastColor;
                var channel = s.channel;
                Vector3 toVector = s.targetVector;
                float toValue = s.targetValue;
                Color toColor = s.targetColor;
                bool parabola = s.positionPath == DamagePositionPath.Parabola;
                float arcHeight = s.arcHeight;
                Tween tween = DOTween.To(() => 0f, progress =>
                {
                    switch (channel)
                    {
                        case DamageChannel.Position:
                            position = Vector3.LerpUnclamped(fromVector, toVector, progress);
                            if (parabola) position.y += 4 * arcHeight * progress * (1 - progress);
                            break;
                        case DamageChannel.Scale: scale = Vector3.LerpUnclamped(fromVector, toVector, progress); break;
                        case DamageChannel.Rotation: rotation = Mathf.LerpUnclamped(fromValue, toValue, progress); break;
                        case DamageChannel.Opacity: opacity = Mathf.LerpUnclamped(fromValue, toValue, progress); break;
                        case DamageChannel.Color: color = Color.LerpUnclamped(fromColor, toColor, progress); break;
                    }
                    Apply();
                }, 1f, s.duration);
                if (s.useCurve) tween.SetEase(s.curve); else tween.SetEase(s.ease);
                sequence.Insert(s.start, tween);
                switch (s.channel)
                {
                    case DamageChannel.Position: lastPosition = s.targetVector; break;
                    case DamageChannel.Scale: lastScale = s.targetVector; break;
                    case DamageChannel.Rotation: lastRotation = s.targetValue; break;
                    case DamageChannel.Opacity: lastOpacity = s.targetValue; break;
                    case DamageChannel.Color: lastColor = s.targetColor; break;
                }
            }
            sequence.OnComplete(() =>
            {
                target.SetVisible(false);
                if (completionSent) return;
                completionSent = true;
                completed?.Invoke();
            });
            sequence.ForceInit();
        }

        void ResetState()
        {
            position = Vector3.zero;
            scale = preset.initialScale;
            rotation = preset.initialRotation;
            color = preset.initialColor;
            opacity = preset.initialOpacity;
            Apply();
        }

        void Apply()
        {
            var tint = color;
            tint.a *= Mathf.Clamp01(opacity);
            target.Apply(origin + position, scale, rotation, tint);
        }

        public void Play() { sequence?.Play(); }
        public void Pause() { sequence?.Pause(); }
        public void Seek(float time)
        {
            if (!IsActive) return;
            sequence.Goto(0, false);
            ResetState();
            var owned = sequence;
            sequence.Goto(Mathf.Clamp(time, 0, preset.Duration), false);
            if (sequence != owned) return; // Completion may return or re-prepare a pooled view.
            target.SetVisible(time >= 0 && time < preset.Duration);
        }

        public void Cancel()
        {
            // The instance Kill extension skips cleanup before DOTween runtime initialization
            // (edit mode). The owner-filtered API also despawns edit-mode sequences.
            if (sequence != null) DOTween.Kill(this, false);
            sequence = null;
            target.Reset();
        }
        public void Dispose() { Cancel(); }
    }
}
