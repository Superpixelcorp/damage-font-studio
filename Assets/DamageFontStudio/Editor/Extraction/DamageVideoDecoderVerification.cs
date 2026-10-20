using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoDecoderVerification
    {
        static readonly int[] Frames = Enumerable.Range(0, 46).Concat(new[] { 20, 21, 20, 21, 59, 60, 61, 71, 92, 119, 120, 150, 350, 650, 1000, 1305 }).ToArray();
        const string Output = "Logs/DecoderChecks";
        static DamageVideoReader reader;
        static int next = -1;
        static double deadline;
        static bool injectLostSeek, injected;
        static int preparedCount, callbacks;
        static readonly List<string> results = new List<string>();
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-referenceVideo");
            RunForVideo(args[at + 1], Array.IndexOf(args, "-injectLostSeek") >= 0);
        }
        public static void RunForVideo(string path, bool simulateLostSeek = false)
        {
            try
            {
                if (reader != null) throw new InvalidOperationException("Decoder verification is already running.");
                results.Clear(); Directory.CreateDirectory(Output);
                if (File.Exists(Output + "/failure.txt")) File.Delete(Output + "/failure.txt");
                injectLostSeek = simulateLostSeek; injected = false; callbacks = preparedCount = 0; next = -1;
                reader = new DamageVideoReader(path); deadline = EditorApplication.timeSinceStartup + 120;
                reader.Prepared += () => { preparedCount++; next = 0; }; reader.Error += message => Finish(new Exception(message));
                EditorApplication.update += Tick;
            }
            catch (Exception ex) { Finish(ex); }
        }
        static void Tick()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(new TimeoutException("Paused seek did not complete.")); return; }
            if (reader == null || !reader.Ready || reader.Busy || next < 0) return;
            int index = next; next = -1;
            if (injectLostSeek && !injected && Frames[index] == 21)
            {
                // Reproduce a dropped completion notification without substituting
                // a fake decoded frame. Recovery must decode and verify the real pixels.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var player = (VideoPlayer)typeof(DamageVideoReader).GetField("player", flags).GetValue(reader);
                var handler = (VideoPlayer.EventHandler)Delegate.CreateDelegate(typeof(VideoPlayer.EventHandler), reader,
                    typeof(DamageVideoReader).GetMethod("OnSeekCompleted", flags));
                player.seekCompleted -= handler; injected = true;
            }
            reader.Seek(Frames[index], actual =>
            {
                try
                {
                    if (++callbacks != index + 1) throw new Exception("A seek callback was delivered more than once.");
                    var pixels = reader.ReadPixels(); double actualError = double.PositiveInfinity, bestError = double.PositiveInfinity; int bestFrame = -1;
                    for (int f = Math.Max(0, actual - 1); f <= Math.Min(reader.FrameCount - 1, actual + 1); f++)
                    {
                        var truth = new Texture2D(2, 2);
                        try
                        {
                            truth.LoadImage(File.ReadAllBytes("Logs/DecoderTruth/" + f.ToString("D4") + ".png"));
                            var expected = truth.GetPixels32(); double error = 0;
                            for (int p = 0; p < pixels.Length; p++)
                            { var a = pixels[p]; var b = expected[p]; error += (a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b); }
                            error /= pixels.Length * 3;
                            if (f == actual) actualError = error;
                            if (error < bestError) { bestError = error; bestFrame = f; }
                        }
                        finally { UnityEngine.Object.DestroyImmediate(truth); }
                    }
                    results.Add($"requested={actual}, bestMatch={bestFrame}, MSE={actualError:0.000}, time={reader.CurrentTime:0.000000}");
                    if (actualError > 160 || actualError > bestError * 1.1 + 20) throw new Exception("Decoded pixels do not match the requested independent OpenCV frame: " + results[results.Count - 1]);
                    if (index + 1 < Frames.Length) next = index + 1; else Finish(null);
                }
                catch (Exception ex) { Finish(ex); }
            });
        }
        static void Finish(Exception error)
        {
            if (error == null && (preparedCount != 1 || callbacks != Frames.Length || (injectLostSeek && (!injected || reader.RecoveryCount < 1))))
                error = new Exception("Decoder recovery callback or retry count mismatch.");
            results.Add($"Prepared callbacks={preparedCount}; seek callbacks={callbacks}; automatic recoveries={reader?.RecoveryCount}; injected lost seek={injected}");
            EditorApplication.update -= Tick; reader?.Dispose(); reader = null;
            Directory.CreateDirectory(Output); File.WriteAllLines(Output + "/frames.txt", results);
            if (error != null) { File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error); }
            else Debug.Log("Independent decoder pixel checks passed.");
            if (error == null && Array.IndexOf(Environment.GetCommandLineArgs(), "-thenFidelity") >= 0)
            { DamageVideoFidelityVerification.Run(); return; }
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
    }
}
