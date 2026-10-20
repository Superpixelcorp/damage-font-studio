using System;
using TMPro;
using UnityEngine;

namespace DamageFontStudio
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Damage Font Studio/Damage Font Object")]
    public sealed class DamageFontObject : MonoBehaviour
    {
        public DamageFontPreset preset;
        [Tooltip("Optional. If empty, the first TMP text under this root displays the number.")]
        public TMP_Text numberText;
        public bool overrideTextAppearance;
        public bool playOnEnable;
        public string number = "123456";
        DamageFontPlayback playback;
        Vector3 origin;
        bool preparing;

        public bool HasValidTextBinding => !numberText || numberText.transform == transform || numberText.transform.IsChildOf(transform);

        public void Play(string value, Action completed = null)
        {
            if (!Application.isPlaying || preparing) return;
            if (!preset || preset.Validate() != null) { Debug.LogError("Assign a valid Damage Font Preset.", this); return; }
            if (!HasValidTextBinding) { Debug.LogError("Number Text must be a child of this animated root.", this); return; }
            preparing = true;
            try
            {
                if (playback == null) playback = new DamageFontPlayback(gameObject, numberText, overrideTextAppearance);
                // Capture the current spawn point before Prepare clears the previous animation.
                if (!playback.IsActive) origin = transform.localPosition;
                playback.Prepare(preset, value, origin, completed);
                playback.Play();
            }
            finally { preparing = false; }
        }

        public void Play() { Play(number); }
        public void Cancel() { playback?.Cancel(); }
        void OnEnable() { if (playOnEnable && !preparing) Play(); }
        void OnDisable() { if (!preparing) Cancel(); }
        void OnDestroy() { playback?.Dispose(); playback = null; }
    }
}
