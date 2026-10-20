using System;
using System.IO;
using System.Linq;
using DG.Tweening;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoCameraRemovalVerification
    {
        static void Check(bool value, string message) { if (!value) throw new Exception("Camera removal: " + message); }
        public static void RunChecks()
        {
            int baseline = DOTween.TotalActiveTweens();
            var session = ScriptableObject.CreateInstance<DamageVideoSession>();
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>();
            DamageFontPreset preset = null, raw = null;
            var sample = new GameObject("Camera removal fixture", typeof(SpriteRenderer), typeof(DamageFontObject));
            var stableRoot = new GameObject("Stabilized fixture"); var sourceRoot = new GameObject("Source fixture");
            Vector2 Path(int f) => new Vector2(2 * f, -8 * f + .3f * f * f);
            Vector2 Camera(int f) => new Vector2(12 + (f % 3 - 1) * 4, -8 + (f % 5 - 2) * 2);
            var birth = new Vector2(120, 180);
            try
            {
                session.width = 528; session.height = 496; session.fps = 30; session.frameCount = 31;
                session.startFrame = 0; session.endFrame = 30; session.removeCameraMotion = true;
                for (int f = 0; f <= 30; f++) session.Put(new DamageVideoSample {
                    frame = f, timestamp = f / 30d, center = birth + Path(f) + Camera(f), cameraOffset = Camera(f),
                    size = new Vector2(30, 20) * (1 + .2f * Mathf.Sin(f * .1f)), opacity = f < 18 ? 1 : (30 - f) / 12f });
                preset = session.BuildPreset(); session.removeCameraMotion = false; raw = session.BuildPreset();
                var binding = sample.GetComponent<DamageFontObject>(); binding.preset = preset;
                sequence.duration = 1; sequence.cameraMotionRemoved = true;
                var initial = session.samples[0];
                var hit = new DamageFontSequenceEvent { sample = sample,
                    position = new Vector3(initial.center.x, -initial.center.y, 0),
                    sourceCameraOffset = new Vector3(Camera(0).x, -Camera(0).y, 0),
                    sourceHorizontalMotion = new AnimationCurve(), sourceVerticalMotion = new AnimationCurve() };
                for (int f = 0; f <= 30; f++)
                {
                    hit.sourceHorizontalMotion.AddKey(f / 30f, Camera(f).x - Camera(0).x);
                    hit.sourceVerticalMotion.AddKey(f / 30f, -Camera(f).y + Camera(0).y);
                }
                sequence.events.Add(hit);
                using (var stable = new DamageFontSequencePlayback(sequence, stableRoot.transform, "9876543210"))
                using (var source = new DamageFontSequencePlayback(sequence, sourceRoot.transform, "1", includeSourceCameraMotion: true))
                {
                    foreach (int f in Enumerable.Range(0, 30).Concat(new[] { 15, 3, 27 }))
                    {
                        float time = f / 30f; stable.Seek(time); source.Seek(time);
                        Vector2 p = birth + Path(f); Vector2 observed = p + Camera(f);
                        var a = stableRoot.transform.GetChild(0); var b = sourceRoot.transform.GetChild(0);
                        Check(Vector3.Distance(a.localPosition, new Vector3(p.x, -p.y, 0)) < .003f, "Game sequence preserves the planted arc while removing alternating camera shake.");
                        Check(Vector3.Distance(b.localPosition, new Vector3(observed.x, -observed.y, 0)) < .003f, "Optional source comparison restores the measured screen coordinates.");
                        Check(a.localScale == b.localScale && Mathf.Abs(a.GetComponent<SpriteRenderer>().color.a - b.GetComponent<SpriteRenderer>().color.a) < .0001f, "Camera compensation must preserve scale and fade.");
                    }
                }
                sequence.lockSpawnX = true; sequence.spawnX = 17;
                using (var stable = new DamageFontSequencePlayback(sequence, stableRoot.transform))
                {
                    stable.Seek(.4f);
                    Check(Mathf.Abs(stableRoot.transform.GetChild(0).localPosition.x - (17 + Path(12).x)) < .003f, "Spawn X lock still preserves the effect's own horizontal movement after camera removal.");
                }
                using (var player = new DamageFontPlayback(sample))
                {
                    player.Prepare(preset, "123", Vector3.zero, manual: true); player.Seek(.4f);
                    Check(Vector3.Distance(sample.transform.localPosition, new Vector3(Path(12).x, -Path(12).y, 0)) < .003f, "Standalone prefab uses the same stabilized preset.");
                    player.Prepare(raw, "123", Vector3.zero, manual: true); player.Seek(.4f);
                    var delta = Path(12) + Camera(12) - Camera(0);
                    Check(Vector3.Distance(sample.transform.localPosition, new Vector3(delta.x, -delta.y, 0)) < .003f, "Turning correction off retains raw observations for re-export.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stableRoot); UnityEngine.Object.DestroyImmediate(sourceRoot);
                UnityEngine.Object.DestroyImmediate(sample); UnityEngine.Object.DestroyImmediate(session); UnityEngine.Object.DestroyImmediate(sequence);
                if (preset) UnityEngine.Object.DestroyImmediate(preset); if (raw) UnityEngine.Object.DestroyImmediate(raw);
            }
            Check(DOTween.TotalActiveTweens() == baseline, "All owned tweens are released.");
            Directory.CreateDirectory("Logs/CameraRemovalChecks");
            File.WriteAllText("Logs/CameraRemovalChecks/model.txt", "PASS: independently planted alternating X/Y camera shake separated from arc motion; standalone preset and sequence agree; original-screen comparison restores camera motion; spawn-only X lock; scale/fade/number independence; reverse seek; raw observations preserved; owned tween cleanup.\n");
        }
    }
}
