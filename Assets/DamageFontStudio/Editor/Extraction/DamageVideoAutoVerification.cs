using System;
using System.Collections.Generic;
using System.IO;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public static class DamageVideoAutoVerification
    {
        const string Output = "Logs/AutoVideoChecks";
        static DamageVideoAutoJob job;
        static double deadline;

        [MenuItem("Tools/Damage Font Studio/Run Automatic Extraction Checks")]
        public static void RunModelChecks()
        {
            Directory.CreateDirectory(Output);
            var analyzer = new DamageVideoAutoAnalyzer(0, 40, 1, 30);
            var blank = new DamageVideoAutoAnalyzer(0, 40, 1, 30);
            for (int f = 0; f <= 40; f++)
            {
                var pixels = new Color32[320 * 240];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(110, 60, 30, 255);
                void Rect(int x, int y, int w, int h, Color32 color)
                { for (int yy = y; yy < y + h; yy++) for (int xx = x; xx < x + w; xx++) pixels[(239 - yy) * 320 + xx] = color; }
                void Number(int x, int y)
                {
                    Rect(x - 2, y - 2, 27, 18, new Color32(10, 10, 10, 255));
                    Rect(x, y, 7, 14, new Color32(255, 255, 255, 255));
                    Rect(x + 10, y, 12, 14, new Color32(255, 255, 255, 255));
                }
                Number(230, 35); // Persistent HUD text must not become a sample.
                if (f >= 6 && f <= 25) Number(95, 165 - (f - 6) * 3);
                analyzer.AddFrame(pixels, 320, 240, f);
                blank.AddFrame(new Color32[320 * 240], 320, 240, f);
            }
            var tracks = analyzer.Finish();
            Check(tracks.Count == 1, "Detect one complete moving number and reject static HUD");
            Check(tracks[0].First.frame == 6 && tracks[0].Last.frame == 25, "Automatic birth/death boundaries");
            Check(blank.Finish().Count == 0, "Blank input produces no invented preset");
            var session = analyzer.MakeSession(tracks[0], "synthetic", 320, 240, 41);
            var preset = session.BuildPreset();
            try
            {
                Check(session.ValidateExport() == null && preset.Validate() == null, "Generated preset validates");
                Check(session.samples[session.samples.Count - 1].opacity == 0, "Estimated tail fades to zero");
                Check(preset.overrideFontStyle, "Generated samples carry editable text style");
            }
            finally { UnityEngine.Object.DestroyImmediate(session); UnityEngine.Object.DestroyImmediate(preset); }
            File.WriteAllText(Output + "/model.txt", "PASS: automatic detection, track association, static HUD and blank rejection, birth/death, valid export, disappearance tail.\n");
        }

        // Run on an isolated validation checkout with -referenceVideo <local path>.
        public static void RunReferenceChecks()
        {
            try
            {
                RunModelChecks();
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-referenceVideo");
                if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Pass -referenceVideo <path>.");
                deadline = EditorApplication.timeSinceStartup + 600;
                EditorApplication.update += Watchdog;
                job = new DamageVideoAutoJob(args[index + 1], 2, 4, VerifySamples, message => Finish(new Exception(message)), lockHorizontalPosition: true);
            }
            catch (Exception ex) { Finish(ex); }
        }
        static void Watchdog() { if (EditorApplication.timeSinceStartup > deadline) Finish(new TimeoutException("Reference analysis timed out.")); }
        static void VerifySamples(List<GameObject> prefabs, string folder)
        {
            try
            {
                job = null;
                Check(prefabs.Count > 0, "Actual video yields samples without manual boxes");
                foreach (var prefab in prefabs) CheckPrefab(prefab);
                DamageVideoVerification.RunModelChecks();
                File.WriteAllText(Output + "/reference.txt", "PASS: actual 2–4s video decoded/analyzed; " + prefabs.Count +
                    " saved presets/materials/prefabs; arbitrary numbers 7/189/987654; identical motion across values; source-sample motion; style restoration; reverse seek; no tween leaks; manual extraction regression.\nFolder: " + folder);
                Finish(null);
            }
            catch (Exception ex) { Finish(ex); }
        }
        static void CheckPrefab(GameObject prefab)
        {
            int baseline = DOTween.TotalActiveTweens();
            var binding = prefab.GetComponent<DamageFontObject>();
            var preset = binding.preset;
            Check(preset && preset.Validate() == null && preset.fontMaterial, "Saved preset and material are linked");
            var session = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)) + "/" + prefab.name + "-Session.asset");
            Check(session, "Analysis session is retained");
            var preview = new PreviewRenderUtility();
            GameObject go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(prefab);
                preview.AddSingleGO(go);
                var text = go.GetComponent<TMP_Text>();
                text.fontStyle = FontStyles.Normal;
                text.fontSharedMaterial = text.font.material;
                var originalMaterial = text.fontSharedMaterial; var originalStyle = text.fontStyle;
                preview.camera.orthographic = true; preview.camera.orthographicSize = 70;
                preview.camera.transform.position = new Vector3(0, 45, -1000);
                preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 2000;
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.55f, .58f, .65f);
                using (var player = new DamageFontPlayback(go, text, true))
                {
                    Vector3 position = Vector3.zero, scale = Vector3.zero;
                    string[] numbers = { "7", "189", "987654" };
                    for (int i = 0; i < numbers.Length; i++)
                    {
                        player.Prepare(preset, numbers[i], Vector3.zero, manual: true);
                        float time = Mathf.Min(.2f, preset.Duration * .4f); player.Seek(time);
                        Check(text.text == numbers[i], "Arbitrary value remains text");
                        Check(text.fontSharedMaterial == preset.fontMaterial && text.fontStyle == preset.fontStyle, "Preset appearance applied");
                        if (i == 0) { position = go.transform.localPosition; scale = go.transform.localScale; }
                        else Check(Vector3.Distance(position, go.transform.localPosition) < .001f && Vector3.Distance(scale, go.transform.localScale) < .001f, "Number-independent animation");
                        var sampled = session.At(session.startFrame + Mathf.RoundToInt(time * (float)session.fps));
                        // Use an exact sampled frame to verify the exported movement, including screen Y inversion.
                        float frameTime = (float)(session.TimeOf(sampled) - session.TimeOf(session.samples[0]));
                        player.Seek(frameTime);
                        Vector2 delta = sampled.center - session.samples[0].center;
                        if (session.removeCameraMotion) delta -= sampled.cameraOffset - session.samples[0].cameraOffset;
                        Check(Vector3.Distance(go.transform.localPosition, new Vector3(delta.x, -delta.y, 0)) < .02f, "Measured trajectory reproduced");
                        player.Seek(time);
                        text.ForceMeshUpdate();
                        preview.BeginPreview(new Rect(0, 0, 640, 320), GUIStyle.none); preview.Render(true);
                        SaveImage(preview.EndPreview(), Output + "/" + prefab.name + "-" + numbers[i] + ".png");
                        player.Seek(preset.Duration); Check(!go.activeSelf, "Completes hidden"); player.Seek(time); Check(go.activeSelf, "Reverse seek restores display");
                        if (prefab.name == "Light-1")
                        {
                            string frames = Output + "/Animation-" + numbers[i]; Directory.CreateDirectory(frames);
                            for (int f = 0; f < 22; f++)
                            {
                                player.Seek(f / 30f); text.ForceMeshUpdate();
                                preview.BeginPreview(new Rect(0, 0, 480, 360), GUIStyle.none); preview.Render(true);
                                SaveImage(preview.EndPreview(), frames + "/" + f.ToString("D3") + ".png", false);
                            }
                        }
                    }
                    player.Cancel();
                    Check(text.fontSharedMaterial == originalMaterial && text.fontStyle == originalStyle, "Original style restored on release");
                }
            }
            finally { preview.Cleanup(); }
            Check(DOTween.TotalActiveTweens() == baseline, "No owned tween leaks");
        }
        static void SaveImage(Texture source, string path, bool requireVisible = true)
        {
            var previous = RenderTexture.active; var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = (RenderTexture)source; texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); texture.Apply();
                int visible = 0; foreach (var p in texture.GetPixels32()) if (p.r > 170 || p.g > 170 || p.b > 170) visible++;
                if (requireVisible) Check(visible > 10, "Sample visibly renders"); File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(texture); }
        }
        static void Finish(Exception error)
        {
            EditorApplication.update -= Watchdog; job?.Dispose(); job = null;
            if (error != null) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/failure.txt", error.ToString()); Debug.LogException(error); }
            else Debug.Log("Automatic video extraction checks passed.");
            if (Application.isBatchMode) EditorApplication.Exit(error == null ? 0 : 1);
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception("Automatic extraction check: " + message); }
    }
}
