using System;
using System.Collections.Generic;
using UnityEngine;

namespace DamageFontStudio
{
    [Serializable]
    public sealed class DamageFontSequenceEvent
    {
        public float time;
        public Vector3 position;
        public Vector3 sourceCameraOffset;
        public GameObject sample;
        [Tooltip("Animation-only preset for applying this hit to the game's existing damage prefab.")]
        public DamageFontPreset motionPreset;
        public string observedText;
        public float textConfidence;
        public bool observedDisappearance;
        public bool motionInferred;
        public int missingFrames;
        public float confidence;
        public int firstObservedFrame, lastObservedFrame;
        public AnimationCurve sourceHorizontalMotion = AnimationCurve.Linear(0, 0, 1, 0);
        public AnimationCurve sourceVerticalMotion = AnimationCurve.Linear(0, 0, 1, 0);
    }

    // Runtime data only. Delays are measured per hit, not averaged to one interval.
    [CreateAssetMenu(menuName = "Damage Font Studio/Extracted Hit Sequence")]
    public sealed class DamageFontSequence : ScriptableObject
    {
        public int sourceWidth, sourceHeight, sourceStartFrame, sourceEndFrame;
        public double sourceFps;
        public double sourceStartTime;
        public List<int> frames = new List<int>();
        public List<float> times = new List<float>();
        public float duration;
        public bool cameraMotionRemoved;
        [Tooltip("Use the same initial X for every hit without changing its animated trajectory.")]
        public bool lockSpawnX;
        public float spawnX;
        public List<DamageFontSequenceEvent> events = new List<DamageFontSequenceEvent>();

        public float TimeAtFrame(int frame)
        {
            int index = frames.BinarySearch(frame);
            return index >= 0 ? times[index] : (float)(frame / sourceFps - sourceStartTime);
        }
    }
}
