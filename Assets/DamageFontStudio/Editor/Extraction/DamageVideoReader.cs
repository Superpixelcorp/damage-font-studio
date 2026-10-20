using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace DamageFontStudio.Editor
{
    internal sealed class DamageVideoReader : IDisposable
    {
        Scene scene;
        GameObject root;
        VideoPlayer player;
        RenderTexture render;
        Texture2D pixels;
        readonly string sourcePath;
        const double SeekTimeout = 5, PrepareTimeout = 30;
        const int MaxSeekRecoveries = 2;
        Action<int> callback;
        int requested = -1;
        double deadline;
        double lastUpdate;
        int seekRecoveries;
        bool recovering;
        internal int RecoveryCount { get; private set; }
        bool frameArrived;
        int settleUpdates;
        double[] presentationTimes;
        public Texture Texture => render;
        public bool Ready => player && player.isPrepared && render;
        public bool Busy => requested >= 0 || (player && !player.isPrepared);
        public int CurrentFrame { get; private set; } = -1;
        public int FrameCount { get; private set; }
        public int Width => render ? render.width : 0;
        public int Height => render ? render.height : 0;
        public double Fps => player ? player.frameRate : 0;
        public double CurrentTime => presentationTimes != null && CurrentFrame >= 0 ? presentationTimes[CurrentFrame] : player ? player.time : 0;
        public bool HasPresentationTimes => presentationTimes != null;
        public double TimeAtFrame(int frame) => presentationTimes != null ? presentationTimes[Mathf.Clamp(frame, 0, FrameCount - 1)] : frame / Fps;
        public int FrameAtTime(double time)
        {
            if (presentationTimes == null) return Mathf.Clamp((int)(time * Fps), 0, FrameCount - 1);
            int index = Array.BinarySearch(presentationTimes, time);
            return Mathf.Clamp(index >= 0 ? index : ~index, 0, FrameCount - 1);
        }
        public event Action Prepared;
        public event Action<string> Error;

        public DamageVideoReader(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Video file not found. Relink the local file.", path);
            sourcePath = Path.GetFullPath(path);
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Damage video decoder") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            CreatePlayer();
            deadline = EditorApplication.timeSinceStartup + PrepareTimeout;
            lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
            player.Prepare();
        }

        void CreatePlayer()
        {
            player = root.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.source = VideoSource.Url;
            player.url = sourcePath;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.waitForFirstFrame = true;
            player.skipOnDrop = false;
            player.sendFrameReadyEvents = false;
            player.prepareCompleted += OnPrepared;
            player.errorReceived += OnError;
            player.seekCompleted += OnSeekCompleted;
            player.loopPointReached += OnEndReached;
        }

        void OnPrepared(VideoPlayer source)
        {
            if (source.width == 0 || source.height == 0 || source.width > 4096 || source.height > 4096 || source.frameRate <= 0 || source.frameCount < 2 || source.frameCount > int.MaxValue)
            { Fail("Unsupported video metadata. Use a constant-frame-rate MP4, up to 4096 px per side."); return; }
            if (source != player) return;
            if (recovering && (render.width != source.width || render.height != source.height || FrameCount != (int)source.frameCount))
            { Fail("Video metadata changed while recovering the decoder. Reopen the video."); return; }
            if (!render)
            {
                render = new RenderTexture((int)source.width, (int)source.height, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
                render.Create();
            }
            source.targetTexture = render;
            FrameCount = (int)source.frameCount;
            if (presentationTimes == null) presentationTimes = DamageVideoTiming.TryRead(source.url, FrameCount);
            if (recovering) { recovering = false; BeginSeek(); return; }
            Prepared?.Invoke();
        }

        public void Seek(int frame, Action<int> complete)
        {
            if (!Ready || Busy) throw new InvalidOperationException("Video decoder is not ready or is seeking.");
            frame = Mathf.Clamp(frame, 0, FrameCount - 1);
            if (CurrentFrame == frame) { complete?.Invoke(frame); return; }
            callback = complete;
            requested = frame;
            seekRecoveries = 0;
            BeginSeek();
        }

        void BeginSeek()
        {
            frameArrived = false;
            settleUpdates = 0;
            deadline = EditorApplication.timeSinceStartup + SeekTimeout;
            lastUpdate = EditorApplication.timeSinceStartup;
            player.Pause();
            // VideoPlayer.frame converts through its average frameRate on some backends.
            // For VFR MP4 this can decode a different presentation frame entirely.
            double timestamp = TimeAtFrame(requested);
            double interval = presentationTimes != null && requested + 1 < presentationTimes.Length ? presentationTimes[requested + 1] - timestamp : 1 / Fps;
            // Seek inside the sample interval. Exact boundaries can round down in
            // Media Foundation and return the preceding frame.
            player.time = timestamp + Math.Min(.001, interval * .1);
        }

        void OnSeekCompleted(VideoPlayer source)
        {
            if (source != player || recovering || requested < 0) return;
            source.Pause();
            if (requested == FrameCount - 1) { source.Play(); return; }
            frameArrived = true; settleUpdates = 0;
        }
        void OnEndReached(VideoPlayer source)
        {
            if (source != player || recovering || requested != FrameCount - 1) return;
            source.Pause(); frameArrived = true; settleUpdates = 0;
        }

        void Tick()
        {
            if (!player) return;
            double now = EditorApplication.timeSinceStartup;
            // Importing assets, model warm-up or a suspended Editor can block its
            // player loop. That interval is not time spent servicing a decode request.
            if (now - lastUpdate > 1) deadline += now - lastUpdate;
            lastUpdate = now;
            if (frameArrived)
            {
                if (settleUpdates++ < 2) { EditorApplication.QueuePlayerLoopUpdate(); return; }
                frameArrived = false;
                CurrentFrame = requested;
                requested = -1;
                var finished = callback; callback = null;
                finished?.Invoke(CurrentFrame);
            }
            else if (Busy && now > deadline)
            {
                string state = $"requested={requested}, last={CurrentFrame}, nativeFrame={player.frame}, nativeTime={player.time:0.000000}, prepared={player.isPrepared}, recovery={seekRecoveries}/{MaxSeekRecoveries}";
                if (requested >= 0 && seekRecoveries < MaxSeekRecoveries)
                {
                    Debug.Log("Damage video decoder stalled; reopening the decoder and retrying the same frame. " + state);
                    seekRecoveries++; RecoveryCount++; recovering = true; frameArrived = false; settleUpdates = 0;
                    ReleasePlayer(); CreatePlayer();
                    deadline = now + PrepareTimeout;
                    player.Prepare();
                }
                else
                {
                    string message = "Video decoder did not complete after automatic recovery. " + state;
                    Debug.LogWarning(message); Fail(message);
                }
            }
            if (Busy) EditorApplication.QueuePlayerLoopUpdate();
        }

        public Color32[] ReadPixels()
        {
            if (CurrentFrame < 0 || !render) throw new InvalidOperationException("No decoded frame.");
            if (!pixels) pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var previous = RenderTexture.active;
            try { RenderTexture.active = render; pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(); return pixels.GetPixels32(); }
            finally { RenderTexture.active = previous; }
        }

        void OnError(VideoPlayer source, string message) { if (source == player) Fail(message); }
        void Fail(string message)
        {
            var handler = Error;
            Dispose();
            handler?.Invoke(message);
        }
        public void Dispose()
        {
            EditorApplication.update -= Tick;
            callback = null; requested = -1;
            ReleasePlayer();
            if (root) UnityEngine.Object.DestroyImmediate(root);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            if (render) { render.Release(); UnityEngine.Object.DestroyImmediate(render); }
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            player = null; root = null; render = null; pixels = null; presentationTimes = null; scene = default;
        }

        void ReleasePlayer()
        {
            if (player)
            {
                player.prepareCompleted -= OnPrepared; player.errorReceived -= OnError;
                player.seekCompleted -= OnSeekCompleted;
                player.loopPointReached -= OnEndReached;
                player.Stop(); player.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(player); player = null;
            }
        }
    }
}
