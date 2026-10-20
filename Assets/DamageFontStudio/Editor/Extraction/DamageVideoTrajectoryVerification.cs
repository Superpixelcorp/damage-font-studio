using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Independent moving-pixel fixtures and analytic paths, unrelated to the sample video.
    public static class DamageVideoTrajectoryVerification
    {
        static void Check(bool value, string message) { if (!value) throw new Exception("Trajectory regression: " + message); }
        public static void RunChecks()
        {
            CheckMeasuredPaths(); CheckCurvedTracking();
            Directory.CreateDirectory("Logs/TrajectoryChecks");
            File.WriteAllText("Logs/TrajectoryChecks/checks.txt", "PASS: independent right/left arcs, zigzag and lateral trajectories; measured XY/scale/alpha replay and reverse seeking; spawn X lock changes only the initial coordinate; repeated hits continue horizontal motion; downward-moving pixel tracking through and beyond the apex. No source-video-specific path parameters.\n");
            Debug.Log("Generic XY trajectory and spawn-only X lock checks passed.");
        }
        static void CheckMeasuredPaths()
        {
            Func<int, Vector2>[] paths = {
                f => new Vector2(f * 2, f * 9 - .35f * f * f),
                f => new Vector2(-f * 3, f * 7 - .3f * f * f),
                f => new Vector2(Mathf.Sin(f * .3f) * 35, Mathf.Sin(f * .15f) * 40),
                f => new Vector2(f * f * .12f, 0)
            };
            foreach (var path in paths)
            {
                var session = ScriptableObject.CreateInstance<DamageVideoSession>();
                session.width = 400; session.height = 300; session.fps = 30; session.frameCount = 31; session.endFrame = 30;
                session.lockHorizontalPosition = true;
                for (int f = 0; f <= 30; f++)
                {
                    var offset = path(f); float size = 1 + .2f * Mathf.Sin(Mathf.PI * f / 30);
                    session.Put(new DamageVideoSample { frame = f, timestamp = 12 + f / 30d,
                        center = new Vector2(120 + offset.x, 160 - offset.y), size = new Vector2(30, 20) * size,
                        opacity = f < 18 ? 1 : (30 - f) / 12f });
                }
                DamageFontPreset preset = null, unlocked = null;
                var root = new GameObject("Measured path fixture", typeof(SpriteRenderer));
                try
                {
                    preset = session.BuildPreset(); session.lockHorizontalPosition = false; unlocked = session.BuildPreset();
                    Check(preset.segments.Where(s => s.channel == DamageChannel.Position).Select(s => s.targetVector)
                        .SequenceEqual(unlocked.segments.Where(s => s.channel == DamageChannel.Position).Select(s => s.targetVector)),
                        "Spawn lock must not erase or modify exported X/Y samples.");
                    using (var player = new DamageFontPlayback(root))
                    {
                        foreach (string number in new[] { "1", "9876543210" })
                        {
                            player.Prepare(preset, number, new Vector3(40, -30, 0), manual: true);
                            foreach (int f in Enumerable.Range(0, 31).Concat(new[] { 17, 5, 29 }))
                            {
                                player.Seek(f / 30f); var offset = path(f);
                                Check(Vector3.Distance(root.transform.localPosition, new Vector3(40 + offset.x, -30 + offset.y, 0)) < .003f,
                                    "Replay must reproduce measured XY including direction changes.");
                                float expectedSize = 1 + .2f * Mathf.Sin(Mathf.PI * f / 30);
                                Check(Mathf.Abs(root.transform.localScale.x - expectedSize) < .001f && Mathf.Abs(root.transform.localScale.y - expectedSize) < .001f,
                                    "Replay must reproduce measured scale.");
                                Check(Mathf.Abs(root.GetComponent<SpriteRenderer>().color.a - (f < 18 ? 1 : (30 - f) / 12f)) < .001f,
                                    "Replay must reproduce measured fade through descent.");
                            }
                        }
                    }
                    CheckSpawnLock(preset);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(session);
                    if (preset) UnityEngine.Object.DestroyImmediate(preset);
                    if (unlocked) UnityEngine.Object.DestroyImmediate(unlocked);
                }
            }
        }
        static void CheckSpawnLock(DamageFontPreset preset)
        {
            var sequence = ScriptableObject.CreateInstance<DamageFontSequence>();
            var sample = new GameObject("Spawn fixture", typeof(SpriteRenderer), typeof(DamageFontObject));
            var parent = new GameObject("Spawn fixture parent");
            sample.GetComponent<DamageFontObject>().preset = preset;
            sequence.lockSpawnX = true; sequence.spawnX = 17;
            sequence.events.Add(new DamageFontSequenceEvent { sample = sample, position = new Vector3(20, 40, 0), time = 0 });
            sequence.events.Add(new DamageFontSequenceEvent { sample = sample, position = new Vector3(80, 70, 0), time = .2f });
            try
            {
                using (var player = new DamageFontSequencePlayback(sequence, parent.transform, "123"))
                {
                    player.Seek(0); Check(Mathf.Abs(parent.transform.GetChild(0).localPosition.x - 17) < .001f, "First hit must start at the fixed X.");
                    player.Seek(.2f); Check(Mathf.Abs(parent.transform.GetChild(1).localPosition.x - 17) < .001f, "Later hit must start at the same fixed X.");
                    player.Seek(.4f);
                    Check(Mathf.Abs(parent.transform.GetChild(0).localPosition.x - 17) > 1, "X must remain animated after spawning.");
                }
                sequence.lockSpawnX = false;
                using (var player = new DamageFontSequencePlayback(sequence, parent.transform))
                {
                    player.Seek(0); Check(Mathf.Abs(parent.transform.GetChild(0).localPosition.x - 20) < .001f, "Unlocked first origin is preserved.");
                    player.Seek(.2f); Check(Mathf.Abs(parent.transform.GetChild(1).localPosition.x - 80) < .001f, "Unlocked second origin is preserved.");
                }
            }
            finally
            { UnityEngine.Object.DestroyImmediate(parent); UnityEngine.Object.DestroyImmediate(sample); UnityEngine.Object.DestroyImmediate(sequence); }
        }
        static void CheckCurvedTracking()
        {
            const int w = 320, h = 300;
            var images = new List<Color32[]>(); var coarse = new DamageVideoAutoAnalyzer.Track();
            var analyzer = new DamageVideoAutoAnalyzer(0, 45, 1, 30);
            for (int f = 0; f <= 45; f++)
            {
                var pixels = Enumerable.Repeat(new Color32(45, 45, 45, 255), w * h).ToArray();
                var hits = new List<DamageVideoAutoAnalyzer.Hit>();
                if (f >= 4 && f <= 36)
                {
                    int k = f - 4, x = Mathf.RoundToInt(50 + k * 2.5f), y = Mathf.RoundToInt(190 - k * 9 + .35f * k * k);
                    float alpha = k < 20 ? 1 : 1 - (k - 20) * .06f;
                    void Rect(int xx, int yy, int width, int height, float value)
                    {
                        byte v = (byte)Mathf.RoundToInt(Mathf.Lerp(45, value, alpha));
                        for (int by = yy; by < yy + height; by++) for (int bx = xx; bx < xx + width; bx++)
                            pixels[(h - 1 - by) * w + bx] = new Color32(v, v, v, 255);
                    }
                    Rect(x - 14, y - 10, 28, 20, 5);
                    Rect(x - 10, y - 7, 6, 14, 245); Rect(x + 1, y - 7, 10, 14, 245); Rect(x + 4, y - 3, 4, 6, 5);
                    var hit = new DamageVideoAutoAnalyzer.Hit { frame = f, family = 1, center = new Vector2(x, y), size = new Vector2(28, 20),
                        learned = true, text = "12", textConfidence = .99f, color = Color.white, contrast = alpha,
                        signal = DamageVideoSignal.Describe(pixels, w, h, new Vector2(x, y), new Vector2(28, 20), 1) };
                    hits.Add(hit); if (k % 4 == 0) coarse.hits.Add(hit);
                }
                analyzer.AddRecoveredFrame(hits, f, Vector2.zero); images.Add(pixels);
            }
            Check(analyzer.Finish().Count == 1, "Association must keep one continuous curved track through the descending half.");
            var refiner = new DamageVideoRefiner(new List<DamageVideoAutoAnalyzer.Track> { coarse }, 0, 45);
            for (int f = 0; f <= 45; f++) refiner.AddFrame(images[f], w, h, f);
            for (int f = 45; f >= 0; f--) refiner.AddBackwardFrame(images[f], w, h, f);
            var result = refiner.Finish().Single();
            Check(result.hits.Last().frame >= 35 && result.hits.Last().center.y > result.hits.Min(p => p.center.y) + 90,
                "Pixel refinement must preserve the descent instead of truncating at the apex.");
            foreach (var hit in result.hits)
            {
                int k = hit.frame - 4; var expected = new Vector2(Mathf.RoundToInt(50 + k * 2.5f), Mathf.RoundToInt(190 - k * 9 + .35f * k * k));
                Check(Vector2.Distance(hit.center, expected) < 4, "Refined XY must agree with independently planted moving pixels.");
            }
        }
    }
}
