using System;
using System.Collections.Generic;
using UnityEngine;

namespace DamageFontStudio
{
    // Uses the same per-number runtime playback in the comparison viewer and in games.
    public sealed class DamageFontSequencePlayback : IDisposable
    {
        readonly List<DamageFontPlayback> players = new List<DamageFontPlayback>();
        readonly List<GameObject> objects = new List<GameObject>();
        readonly DamageFontSequence sequence;
        readonly bool includeSourceCameraMotion;

        public DamageFontSequencePlayback(DamageFontSequence sequence, Transform parent, string number = null, bool includeSourceCameraMotion = false, GameObject targetPrefab = null)
        {
            this.sequence = sequence ? sequence : throw new ArgumentNullException(nameof(sequence));
            this.includeSourceCameraMotion = includeSourceCameraMotion;
            try
            {
                foreach (var hit in sequence.events)
                {
                    var source = targetPrefab ? targetPrefab : hit.sample;
                    if (!source) throw new ArgumentException("Assign the game's damage prefab or a sequence preview sample.");
                    var root = UnityEngine.Object.Instantiate(source, parent, false); objects.Add(root);
                    var binding = root.GetComponent<DamageFontObject>();
                    var preset = targetPrefab ? hit.motionPreset : binding ? binding.preset : null;
                    if (!preset) throw new ArgumentException(targetPrefab ? "Missing animation-only hit preset. Re-export the sequence." : "Missing sequence preset.");
                    if (binding) { binding.playOnEnable = false; binding.enabled = false; }
                    var player = new DamageFontPlayback(root, binding ? binding.numberText : null, !targetPrefab); players.Add(player);
                    var spawn = hit.position;
                    if (sequence.cameraMotionRemoved && !includeSourceCameraMotion) spawn -= hit.sourceCameraOffset;
                    if (sequence.lockSpawnX) spawn.x = sequence.spawnX;
                    player.Prepare(preset, string.IsNullOrEmpty(number) ? (string.IsNullOrEmpty(hit.observedText) ? (binding ? binding.number : "123") : hit.observedText) : number, spawn, manual: true);
                }
                Seek(0);
            }
            catch { Dispose(); throw; }
        }
        public void Seek(float time)
        {
            for (int i = 0; i < players.Count; i++)
            {
                // Old assets can contain longer per-hit tails. Never replay them
                // outside the sequence's selected source interval.
                bool outside = time < 0 || (sequence.duration > 0 && time >= sequence.duration);
                float localTime = time - sequence.events[i].time;
                players[i].Seek(outside ? -1 : localTime);
                if (sequence.cameraMotionRemoved && !includeSourceCameraMotion) continue;
                var curve = sequence.events[i].sourceHorizontalMotion;
                if (curve != null && objects[i]) objects[i].transform.localPosition += Vector3.right * curve.Evaluate(Mathf.Max(0, localTime));
                var vertical = sequence.events[i].sourceVerticalMotion;
                if (vertical != null && objects[i]) objects[i].transform.localPosition += Vector3.up * vertical.Evaluate(Mathf.Max(0, localTime));
            }
        }
        public void Dispose()
        {
            foreach (var player in players) player.Dispose(); players.Clear();
            foreach (var root in objects)
                if (root) { if (Application.isPlaying) UnityEngine.Object.Destroy(root); else UnityEngine.Object.DestroyImmediate(root); }
            objects.Clear();
        }
    }
}
