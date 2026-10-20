using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public sealed class DamageVideoExtractionWindow : EditorWindow
    {
        [SerializeField] DamageVideoSession session;
        DamageVideoReader reader;
        DamageVideoTracker tracker;
        DamageVideoComparison comparison;
        DamageVideoAutoJob autoJob;
        [SerializeField] bool autoLockHorizontalPosition = true;
        [SerializeField] bool autoRemoveCameraMotion = true;
        [SerializeField] bool compareSourceCameraMotion;
        [SerializeField] GameObject[] autoSamples = Array.Empty<GameObject>();
        [SerializeField] DamageFontSequence recordedSequence;
        [SerializeField] string sequenceNumber = "";
        [SerializeField] bool showManual, showAutoSamples;
        [SerializeField] bool showFinalCandidates;
        string autoFolder;
        DamageVideoSample lastTracked;
        int currentFrame, pendingTrack = -1, wantedFrame = -1;
        int revision, comparedRevision = -1;
        bool tracking, playing, selectArea;
        bool dragging;
        bool selectAnalysisArea, draggingAnalysisArea;
        int analysisAreaControl;
        Vector2 analysisAreaStart, analysisAreaEnd;
        Vector2 dragStart, dragEnd, scroll;
        string status = "Open a local video to begin.";
        object[] statusArgs = Array.Empty<object>();
        bool error;
        int compareMode;
        float overlayAlpha = 0.65f, playbackSpeed = 1;
        double playOriginTime, playOriginClock;
        float playClockSpeed = 1;
        int generation;
        bool CanEdit => !recordedSequence && autoJob == null && reader != null && reader.Ready && reader.CurrentFrame >= 0 && !reader.Busy && wantedFrame < 0 && !tracking && !playing;
        bool CanSetAnalysisRange => autoJob == null && reader != null && reader.Ready && reader.CurrentFrame >= 0 && !reader.Busy && wantedFrame < 0 && !tracking && !playing;

        public static void OpenRecorded(DamageFontSequence sequence)
        {
            if (!sequence || sequence.events.Count == 0) return;
            var window = GetWindow<DamageVideoExtractionWindow>(StudioLanguage.Text("Video Extraction"));
            var source = sequence.events[0].sample;
            string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)).Replace('\\', '/');
            var original = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(folder + "/" + source.name + "-Session.asset");
            if (!original) return;
            window.Release();
            if (window.session && !EditorUtility.IsPersistent(window.session)) DestroyImmediate(window.session);
            window.session = Instantiate(original); window.recordedSequence = sequence;
            window.sequenceNumber = "";
            window.session.startFrame = sequence.sourceStartFrame; window.session.endFrame = sequence.sourceEndFrame;
            window.compareMode = 3; window.revision++;
            window.LoadVideo(window.session.videoPath, true);
        }

        [MenuItem("Tools/Damage Font Studio/Extract From Video")]
        public static void Open() => GetWindow<DamageVideoExtractionWindow>(StudioLanguage.Text("Video Extraction"));

        void OnEnable()
        {
            minSize = new Vector2(1050, 730);
            if (!session) session = CreateInstance<DamageVideoSession>();
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.playModeStateChanged += OnPlayMode;
            Undo.undoRedoPerformed += OnUndo;
            status = "Open video, or load a session and press Reopen video.";
        }
        void OnDisable()
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Release;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            Undo.undoRedoPerformed -= OnUndo;
            Release();
            if (session && !EditorUtility.IsPersistent(session)) DestroyImmediate(session);
        }
        void OnPlayMode(PlayModeStateChange state) { Release(); }
        void OnUndo() { tracking = playing = selectAnalysisArea = draggingAnalysisArea = false; pendingTrack = -1; revision++; Repaint(); }
        void Release()
        {
            generation++; tracking = playing = dragging = false; pendingTrack = wantedFrame = -1;
            selectAnalysisArea = draggingAnalysisArea = false;
            autoJob?.Dispose(); autoJob = null;
            reader?.Dispose(); reader = null;
            comparison?.Dispose(); comparison = null; comparedRevision = -1;
        }
        void Changed(string undo)
        {
            Undo.RegisterCompleteObjectUndo(session, undo);
            EditorUtility.SetDirty(session);
            revision++;
        }
        void Message(string message, bool failed = false, params object[] args) { status = message; statusArgs = args; error = failed; Repaint(); }

        void LoadVideo(string path, bool keepSession)
        {
            if (!keepSession && session && (session.samples.Count > 0 || EditorUtility.IsPersistent(session)) &&
                !EditorUtility.DisplayDialog(StudioLanguage.Text("New extraction"), StudioLanguage.Text("Create a new session? Save the current session first if you need its corrections."), StudioLanguage.Text("New session"), StudioLanguage.Text("Cancel"))) return;
            Release();
            if (!keepSession)
            {
                recordedSequence = null; compareMode = 0; selectArea = false;
                if (session && !EditorUtility.IsPersistent(session)) DestroyImmediate(session);
                session = CreateInstance<DamageVideoSession>();
                session.videoPath = path;
                currentFrame = 0; revision++;
            }
            Message("Preparing video...");
            try
            {
                reader = new DamageVideoReader(path);
                reader.Error += message => { tracking = playing = false; pendingTrack = -1; Message(message, true); };
                reader.Prepared += () =>
                {
                    if (keepSession && session.width > 0 && (session.width != reader.Width || session.height != reader.Height || session.frameCount != reader.FrameCount || Math.Abs(session.fps - reader.Fps) > 0.01))
                    { Release(); Message("Video metadata differs from the saved session. Open as a new video instead.", true); return; }
                    Changed("Load video metadata");
                    session.videoPath = path;
                    session.width = reader.Width; session.height = reader.Height; session.frameCount = reader.FrameCount; session.fps = reader.Fps;
                    if (!keepSession) session.endFrame = reader.FrameCount - 1;
                    wantedFrame = session.startFrame;
                    Message(recordedSequence ? "Recorded hits ready. Play the range to compare with the source." : "Video ready. Preview it, choose the analysis range, then press Start analysis → create samples.");
                };
            }
            catch (Exception ex) { Message(ex.Message, true); }
        }

        void Seek(int frame)
        {
            if (reader == null || !reader.Ready) return;
            wantedFrame = Mathf.Clamp(frame, 0, reader.FrameCount - 1);
        }

        void Tick()
        {
            if (reader == null || !reader.Ready || reader.Busy) return;
            if (tracking && pendingTrack >= 0)
            {
                int frame = pendingTrack; pendingTrack = -1;
                int token = generation;
                reader.Seek(frame, decoded =>
                {
                    if (token != generation) return;
                    currentFrame = decoded;
                    if (!tracking) { Repaint(); return; }
                    var pixels = reader.ReadPixels();
                    var anchor = session.samples.FirstOrDefault(s => s.frame == decoded && s.manual);
                    DamageVideoSample sample;
                    if (anchor != null)
                    {
                        sample = anchor.Copy(); tracker.Anchor(pixels, session.width, session.height, sample);
                    }
                    else
                    {
                        sample = tracker.Track(pixels, session.width, session.height, lastTracked, decoded, session.searchRadius);
                        sample.timestamp = reader.CurrentTime;
                        session.Put(sample); EditorUtility.SetDirty(session); revision++;
                    }
                    lastTracked = sample;
                    if (sample.confidence < session.minimumConfidence && !sample.manual)
                    {
                        tracking = false;
                        Message("Low confidence ({0:0.00}) at frame {1}. Correct the box and Continue tracking.", true, sample.confidence, decoded);
                    }
                    else if (decoded >= session.endFrame)
                    { tracking = false; Message("Tracking finished. Review samples, correct drift, then export the preset."); }
                    else QueueNextTrack(decoded);
                    Repaint();
                });
            }
            else if (wantedFrame >= 0)
            {
                int frame = wantedFrame; wantedFrame = -1;
                reader.Seek(frame, decoded => { currentFrame = decoded; Repaint(); });
            }
            else if (playing)
            {
                double now = EditorApplication.timeSinceStartup;
                if (playClockSpeed != playbackSpeed)
                { playOriginTime += (now - playOriginClock) * playClockSpeed; playOriginClock = now; playClockSpeed = playbackSpeed; }
                double time = playOriginTime + (now - playOriginClock) * playClockSpeed;
                int frame = Mathf.Min(session.endFrame, reader.FrameAtTime(time));
                if (frame > currentFrame) Seek(frame);
                if (time >= reader.TimeAtFrame(session.endFrame)) playing = false;
            }
        }

        void QueueNextTrack(int frame)
        {
            int next = Mathf.Min(session.endFrame, frame + session.sampleStep);
            var anchor = session.samples.Where(s => s.manual && s.frame > frame && s.frame <= next).OrderBy(s => s.frame).FirstOrDefault();
            pendingTrack = anchor != null ? anchor.frame : next;
        }
        void StartTracking()
        {
            if ((session.endFrame - session.startFrame) / session.sampleStep + session.samples.Count(s => s.manual) > 600)
            { Message("Too many samples. Shorten the range or increase sample spacing (maximum 600).", true); return; }
            var anchor = session.samples.FirstOrDefault(s => s.frame == currentFrame);
            if (anchor == null || currentFrame < session.startFrame || currentFrame >= session.endFrame)
            { Message("Add/correct a sample at the current frame inside the range first.", true); return; }
            Changed("Track video range");
            anchor.manual = true; anchor.confidence = 1;
            tracker = new DamageVideoTracker();
            tracker.Anchor(reader.ReadPixels(), session.width, session.height, anchor);
            lastTracked = anchor.Copy();
            tracking = true; playing = false; wantedFrame = -1;
            QueueNextTrack(currentFrame);
            Message("Tracking position and uniform scale. Rotation and opacity remain manual. Stop is safe.");
        }

        void OnGUI()
        {
            titleContent = new GUIContent(StudioLanguage.Text("Video Extraction"));
            if (!session) return;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(StudioLanguage.Text("VIDEO → DAMAGE PRESET"), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                StudioLanguage.DrawSelector();
                if (GUILayout.Button(StudioLanguage.Text("Open Studio"), EditorStyles.toolbarButton)) DamageFontStudioWindow.Open();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorGUILayout.HelpBox(StudioLanguage.Text("Exit Play mode to extract video animation."), MessageType.Info); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(335))) DrawControls();
                using (new EditorGUILayout.VerticalScope()) DrawVideo();
            }
        }

        void DrawControls()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawAutomatic();
            if (autoJob != null) { EditorGUILayout.EndScrollView(); return; }
            showManual = EditorGUILayout.Foldout(showManual, StudioLanguage.Text("Manual extraction / corrections"), true);
            if (!showManual) { EditorGUILayout.EndScrollView(); return; }
            GUILayout.Label(StudioLanguage.Text("1. Video / extraction session"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(StudioLanguage.Text("Open video...")))
                {
                    string path = EditorUtility.OpenFilePanel(StudioLanguage.Text("Open local video (MP4 recommended)"), "", "");
                    if (!string.IsNullOrEmpty(path)) LoadVideo(path, false);
                }
                if (GUILayout.Button(StudioLanguage.Text("Reopen / relink")))
                {
                    string path = session.videoPath;
                    if (!File.Exists(path)) path = EditorUtility.OpenFilePanel(StudioLanguage.Text("Relink video"), "", "");
                    if (!string.IsNullOrEmpty(path)) LoadVideo(path, true);
                }
            }
            var next = (DamageVideoSession)EditorGUILayout.ObjectField(StudioLanguage.Text("Session"), EditorUtility.IsPersistent(session) ? session : null, typeof(DamageVideoSession), false);
            if (next && next != session)
            {
                recordedSequence = null;
                Release(); if (!EditorUtility.IsPersistent(session)) DestroyImmediate(session);
                session = next; revision++; Message("Session loaded. Press Reopen / relink to load its video.");
            }
            if (GUILayout.Button(StudioLanguage.Text("Save session..."))) SaveSession();
            EditorGUILayout.LabelField(Path.GetFileName(session.videoPath), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(StudioLanguage.Format("{0} × {1}  |  {2:0.###} fps  |  {3} frames", session.width, session.height, session.fps, session.frameCount));

            using (new EditorGUI.DisabledScope(!CanEdit))
            {
                GUILayout.Space(8); GUILayout.Label(StudioLanguage.Text("2. Range and tracking"), EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                int start = EditorGUILayout.IntField(StudioLanguage.Text("Start frame"), session.startFrame);
                int end = EditorGUILayout.IntField(StudioLanguage.Text("End frame (disappear)"), session.endFrame);
                if (EditorGUI.EndChangeCheck())
                { Changed("Change extraction range"); session.startFrame = Mathf.Clamp(start, 0, Math.Max(0, session.frameCount - 2)); session.endFrame = Mathf.Clamp(end, session.startFrame + 1, Math.Max(1, session.frameCount - 1)); }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(StudioLanguage.Text("Set Start here")) && currentFrame < session.endFrame) { Changed("Set start"); session.startFrame = currentFrame; }
                    if (GUILayout.Button(StudioLanguage.Text("Set End here")) && currentFrame > session.startFrame) { Changed("Set end"); session.endFrame = currentFrame; }
                }
                EditorGUI.BeginChangeCheck();
                int step = EditorGUILayout.IntSlider(StudioLanguage.Text("Sample every N frames"), session.sampleStep, 1, 12);
                int radius = EditorGUILayout.IntSlider(StudioLanguage.Text("Search radius (px)"), session.searchRadius, 4, 128);
                float threshold = EditorGUILayout.Slider(StudioLanguage.Text("Stop below confidence"), session.minimumConfidence, 0, 1);
                if (EditorGUI.EndChangeCheck()) { Changed("Tracking settings"); session.sampleStep = step; session.searchRadius = radius; session.minimumConfidence = threshold; }
                selectArea = GUILayout.Toggle(selectArea, StudioLanguage.Text("Draw / replace number region"), "Button");
                if (GUILayout.Button(StudioLanguage.Text("Track forward from current frame"))) StartTracking();
            }
            if (tracking && GUILayout.Button(StudioLanguage.Text("Stop tracking (keep results)"))) { tracking = false; pendingTrack = -1; Message("Stopped. Completed samples kept."); }
            if (!recordedSequence) DrawSample();
            GUILayout.Space(8); GUILayout.Label(StudioLanguage.Text("4. Compare / export"), EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(tracking))
            {
                EditorGUI.BeginChangeCheck();
                string number = EditorGUILayout.TextField(StudioLanguage.Text("Number text"), session.number);
                var font = (TMP_FontAsset)EditorGUILayout.ObjectField(StudioLanguage.Text("TMP font"), session.font, typeof(TMP_FontAsset), false);
                Color color = EditorGUILayout.ColorField(StudioLanguage.Text("Comparison / preset color"), session.color);
                float units = EditorGUILayout.FloatField(StudioLanguage.Text("Units per source pixel"), session.unitsPerPixel);
                if (EditorGUI.EndChangeCheck()) { Changed("Comparison style"); session.number = number; session.font = font; session.color = color; session.unitsPerPixel = Mathf.Max(0.001f, units); }
                string invalid = session.ValidateExport();
                if (invalid != null) EditorGUILayout.HelpBox(StudioLanguage.Text(invalid), MessageType.Info);
                using (new EditorGUI.DisabledScope(invalid != null))
                    if (GUILayout.Button(StudioLanguage.Text("Save animation preset (.asset)..."))) Export();
            }
            EditorGUILayout.HelpBox(StudioLanguage.Text("Local matching estimates position + uniform scale. Rotation / opacity are manual. Camera movement is included; this is screen-space reconstruction, not recovery of the original settings. Save session to keep corrections."), MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void DrawAutomatic()
        {
            GUILayout.Label(StudioLanguage.Text("Automatic sample creation"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(StudioLanguage.Text("Load a video, preview it and choose Start / End. Start analysis then detects the effects and creates editable presets + sample prefabs."), MessageType.Info);
            using (new EditorGUI.DisabledScope(autoJob != null || tracking))
            {
                if (GUILayout.Button(StudioLanguage.Text("Open video...")))
                {
                    string path = EditorUtility.OpenFilePanel(StudioLanguage.Text("Open local video (MP4 recommended)"), "", "");
                    if (!string.IsNullOrEmpty(path)) LoadVideo(path, false);
                }
            }
            if (!string.IsNullOrEmpty(session.videoPath))
                EditorGUILayout.LabelField(Path.GetFileName(session.videoPath), EditorStyles.wordWrappedMiniLabel);
            bool ready = reader != null && reader.Ready;
            using (new EditorGUI.DisabledScope(!CanSetAnalysisRange))
            {
                EditorGUI.BeginChangeCheck();
                double start = EditorGUILayout.DoubleField(StudioLanguage.Text("Analyze from (seconds)"), ready ? reader.TimeAtFrame(session.startFrame) : 0);
                double end = EditorGUILayout.DoubleField(StudioLanguage.Text("Analyze to (seconds)"), ready ? reader.TimeAtFrame(session.endFrame) : 0);
                if (EditorGUI.EndChangeCheck() && double.IsFinite(start) && double.IsFinite(end))
                    SetAnalysisRange(reader.FrameAtTime(start), reader.FrameAtTime(end));
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(currentFrame >= session.frameCount - 1))
                        if (GUILayout.Button(StudioLanguage.Text("Set Start here"))) SetAnalysisRange(currentFrame, Math.Max(currentFrame + 1, session.endFrame));
                    using (new EditorGUI.DisabledScope(currentFrame <= 0))
                        if (GUILayout.Button(StudioLanguage.Text("Set End here"))) SetAnalysisRange(Math.Min(session.startFrame, currentFrame - 1), currentFrame);
                }
                autoLockHorizontalPosition = EditorGUILayout.Toggle(StudioLanguage.Text("Lock horizontal position"), autoLockHorizontalPosition);
                EditorGUILayout.LabelField(StudioLanguage.Text("All hits start at the same X. Horizontal motion after spawning is preserved."), EditorStyles.wordWrappedMiniLabel);
                autoRemoveCameraMotion = EditorGUILayout.Toggle(StudioLanguage.Text("Remove screen shake"), autoRemoveCameraMotion);
                DrawAnalysisRegionControls();
            }
            double duration = ready ? reader.TimeAtFrame(session.endFrame) - reader.TimeAtFrame(session.startFrame) : 0;
            if (ready) EditorGUILayout.LabelField(StudioLanguage.Format("Selected range: {0:0.000}s · frames {1}–{2}", duration, session.startFrame, session.endFrame), EditorStyles.wordWrappedMiniLabel);
            bool canAnalyze = autoJob == null && ready && !reader.Busy && wantedFrame < 0 && !tracking && !selectAnalysisArea && !draggingAnalysisArea && duration >= .3 - .0001;
            using (new EditorGUI.DisabledScope(!canAnalyze))
                if (GUILayout.Button(StudioLanguage.Text("Start analysis → create samples"))) StartAnalysis();
            if (ready && duration < .3 - .0001) EditorGUILayout.HelpBox(StudioLanguage.Text("Choose at least 0.3 seconds for analysis."), MessageType.Info);
            if (autoJob != null)
            {
                Rect progress = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(progress, autoJob.Progress, StudioLanguage.Text(autoJob.Stage) + $" · {autoJob.Progress:P0}");
                if (GUILayout.Button(StudioLanguage.Text("Cancel analysis"))) CancelAnalysis();
            }
            using (new EditorGUI.DisabledScope(autoJob != null))
            {
                var nextSequence = (DamageFontSequence)EditorGUILayout.ObjectField(StudioLanguage.Text("Recorded hit sequence"), recordedSequence, typeof(DamageFontSequence), false);
                if (nextSequence != recordedSequence)
                { if (nextSequence) OpenRecorded(nextSequence); else { recordedSequence = null; revision++; } }
                if (recordedSequence)
                {
                    DamageVideoFinalMotion.Draw(recordedSequence, ref showFinalCandidates);
                    if (recordedSequence.cameraMotionRemoved)
                    {
                        EditorGUI.BeginChangeCheck();
                        compareSourceCameraMotion = EditorGUILayout.Toggle(StudioLanguage.Text("Include camera motion for source comparison"), compareSourceCameraMotion);
                        if (EditorGUI.EndChangeCheck()) revision++;
                    }
                    EditorGUI.BeginChangeCheck();
                    bool lockX = EditorGUILayout.Toggle(StudioLanguage.Text("Lock spawn X"), recordedSequence.lockSpawnX);
                    float spawnX = recordedSequence.spawnX;
                    using (new EditorGUI.DisabledScope(!lockX)) spawnX = EditorGUILayout.FloatField(StudioLanguage.Text("Spawn X"), spawnX);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(recordedSequence, "Change sequence spawn X");
                        recordedSequence.lockSpawnX = lockX;
                        if (float.IsFinite(spawnX)) recordedSequence.spawnX = spawnX;
                        EditorUtility.SetDirty(recordedSequence); revision++;
                    }
                    int uncertain = recordedSequence.events.Count(e => string.IsNullOrEmpty(e.observedText));
                    EditorGUILayout.LabelField(StudioLanguage.Format("{0} recorded hits · {1} unread numbers", recordedSequence.events.Count, uncertain), EditorStyles.wordWrappedMiniLabel);
                    int inferred = recordedSequence.events.Count(e => e.motionInferred);
                    if (inferred > 0) EditorGUILayout.LabelField(StudioLanguage.Format("{0} occluded hits use a measured family profile", inferred), EditorStyles.wordWrappedMiniLabel);
                    EditorGUI.BeginChangeCheck();
                    sequenceNumber = EditorGUILayout.TextField(StudioLanguage.Text("Display number / text"), sequenceNumber);
                    if (EditorGUI.EndChangeCheck()) { revision++; Repaint(); }
                    if (GUILayout.Button(StudioLanguage.Text("Play RecordedHits in Studio"))) DamageFontStudioWindow.OpenSequence(recordedSequence);
                    if (GUILayout.Button(StudioLanguage.Text("Compare recorded hits"))) OpenRecorded(recordedSequence);
                    if (GUILayout.Button(StudioLanguage.Text("Reopen / relink")))
                    {
                        string path = session.videoPath;
                        if (!File.Exists(path)) path = EditorUtility.OpenFilePanel(StudioLanguage.Text("Relink video"), "", "");
                        if (!string.IsNullOrEmpty(path)) LoadVideo(path, true);
                    }
                }
                if (autoSamples.Length > 0) showAutoSamples = EditorGUILayout.Foldout(showAutoSamples, StudioLanguage.Text("Analysis samples (advanced)"), true);
                if (showAutoSamples)
                foreach (var sample in autoSamples)
                {
                    if (!sample) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(StudioLanguage.Text("Preview sample") + " · " + sample.name))
                            DamageFontStudioWindow.OpenFor(sample.GetComponent<DamageFontObject>());
                        if (GUILayout.Button(StudioLanguage.Text("Compare source"), GUILayout.Width(85)))
                        {
                            string path = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sample)).Replace('\\', '/') + "/" + sample.name + "-Session.asset";
                            var extracted = AssetDatabase.LoadAssetAtPath<DamageVideoSession>(path);
                            if (extracted)
                            {
                                recordedSequence = null;
                                Release(); if (session && !EditorUtility.IsPersistent(session)) DestroyImmediate(session);
                                session = extracted; compareMode = 2; revision++; LoadVideo(session.videoPath, true);
                            }
                        }
                    }
                }
                if (!string.IsNullOrEmpty(autoFolder)) EditorGUILayout.LabelField(autoFolder, EditorStyles.wordWrappedMiniLabel);
            }
            GUILayout.Space(12);
        }

        void DrawAnalysisRegionControls()
        {
            EditorGUI.BeginChangeCheck();
            bool limited = EditorGUILayout.Toggle(StudioLanguage.Text("Limit analysis area"), session.limitAnalysisRegion);
            if (EditorGUI.EndChangeCheck())
            {
                Changed("Change analysis area mode"); session.limitAnalysisRegion = limited;
                if (limited && (session.analysisRegion.width < 4 || session.analysisRegion.height < 4))
                    session.analysisRegion = new Rect(0, 0, session.width, session.height);
                selectAnalysisArea = limited; draggingAnalysisArea = false; selectArea = false;
                if (limited) { playing = false; compareMode = 0; Message("Drag the source preview to select the analysis area. Include the whole motion path."); }
            }
            if (!session.limitAnalysisRegion) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(StudioLanguage.Text(selectAnalysisArea ? "Cancel area selection" : "Select analysis area")))
                {
                    selectAnalysisArea = !selectAnalysisArea; draggingAnalysisArea = false; selectArea = false; playing = false;
                    if (selectAnalysisArea) { compareMode = 0; Message("Drag the source preview to select the analysis area. Include the whole motion path."); }
                }
                if (GUILayout.Button(StudioLanguage.Text("Use full frame")))
                {
                    Changed("Clear analysis area"); session.limitAnalysisRegion = false;
                    session.analysisRegion = new Rect(0, 0, session.width, session.height);
                    selectAnalysisArea = draggingAnalysisArea = false;
                }
            }
            EditorGUI.BeginChangeCheck();
            var next = EditorGUILayout.RectField(StudioLanguage.Text("Analysis area (source pixels)"), session.analysisRegion);
            if (EditorGUI.EndChangeCheck()) SetAnalysisRegion(next);
            EditorGUILayout.LabelField(StudioLanguage.Text("Only complete number boxes inside this area are analyzed. Include the full flight path; motion outside the area is excluded."), EditorStyles.wordWrappedMiniLabel);
        }

        internal bool SetAnalysisRegion(Rect requested)
        {
            try
            {
                var area = new DamageVideoAnalysisRegion(requested, session.width, session.height);
                Changed("Set analysis area"); session.limitAnalysisRegion = true; session.analysisRegion = area.Bounds;
                selectAnalysisArea = draggingAnalysisArea = false; playing = false;
                Message("Analysis area selected. Set the time range, then start analysis.");
                return true;
            }
            catch (ArgumentException ex) { Message(ex.Message, true); return false; }
        }

        void SetAnalysisRange(int start, int end)
        {
            Changed("Change extraction range"); playing = false;
            session.startFrame = Mathf.Clamp(start, 0, Math.Max(0, session.frameCount - 2));
            session.endFrame = Mathf.Clamp(end, session.startFrame + 1, Math.Max(1, session.frameCount - 1));
        }

        void StartAnalysis()
        {
            if (autoJob != null || reader == null || !reader.Ready || reader.Busy || wantedFrame >= 0 || tracking) return;
            string path = session.videoPath;
            double start = reader.TimeAtFrame(session.startFrame), end = reader.TimeAtFrame(session.endFrame);
            if (end - start < .3 - .0001) { Message("Choose at least 0.3 seconds for analysis.", true); return; }
            if (selectAnalysisArea || draggingAnalysisArea) return;
            Rect? analysisRegion = null;
            if (session.limitAnalysisRegion)
            {
                try { analysisRegion = new DamageVideoAnalysisRegion(session.analysisRegion, session.width, session.height).Bounds; }
                catch (ArgumentException ex) { Message(ex.Message, true); return; }
            }
            recordedSequence = null; compareMode = 0;
            Release();
            try
            {
                autoJob = new DamageVideoAutoJob(path, (float)start, (float)end, (samples, folder) =>
                {
                    autoJob = null; autoSamples = samples.ToArray(); autoFolder = folder;
                    recordedSequence = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder + "/RecordedHits.asset");
                    if (recordedSequence)
                    {
                        OpenRecorded(recordedSequence);
                        var final = DamageVideoFinalMotion.Ensure(recordedSequence);
                        Selection.activeObject = final; EditorGUIUtility.PingObject(final);
                        DamageFontStudioWindow.OpenGameResult(recordedSequence);
                    }
                }, message => { autoJob = null; LoadVideo(path, true); Message(message, true); }, Repaint, autoLockHorizontalPosition, autoRemoveCameraMotion, analysisRegion);
                Message("Analyzing video. You can cancel without changing existing presets.");
            }
            catch (Exception ex) { autoJob = null; LoadVideo(path, true); Message(ex.Message, true); }
        }

        void CancelAnalysis()
        {
            if (autoJob == null) return;
            autoJob.Dispose(); autoJob = null;
            LoadVideo(session.videoPath, true);
            Message("Analysis cancelled. The selected range is kept.");
        }

        void DrawSample()
        {
            GUILayout.Space(8); GUILayout.Label(StudioLanguage.Text("3. Correct current frame"), EditorStyles.boldLabel);
            var actual = session.samples.FirstOrDefault(s => s.frame == currentFrame);
            var sample = actual ?? session.At(currentFrame);
            using (new EditorGUI.DisabledScope(!CanEdit || sample == null))
            {
                if (sample != null)
                {
                    EditorGUILayout.LabelField(actual == null ? StudioLanguage.Text("Interpolated (not yet a sample)") : actual.manual ? StudioLanguage.Text("Manual anchor") : StudioLanguage.Format("Auto · confidence {0:0.00}", actual.confidence));
                    EditorGUI.BeginChangeCheck();
                    var center = EditorGUILayout.Vector2Field(StudioLanguage.Text("Center (source pixels, top-left)"), sample.center);
                    var size = EditorGUILayout.Vector2Field(StudioLanguage.Text("Unrotated box size"), sample.size);
                    float angle = EditorGUILayout.FloatField(StudioLanguage.Text("Rotation clockwise (degrees)"), sample.angle);
                    float opacity = EditorGUILayout.Slider(StudioLanguage.Text("Opacity (manual)"), sample.opacity, 0, 1);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Changed("Correct tracked sample");
                        var corrected = sample.Copy(); corrected.frame = currentFrame;
                        corrected.timestamp = reader.CurrentTime;
                        corrected.center = center; corrected.size = new Vector2(Mathf.Max(2, size.x), Mathf.Max(2, size.y));
                        corrected.angle = angle; corrected.opacity = opacity; corrected.manual = true; corrected.confidence = 1;
                        session.Put(corrected);
                    }
                    if (GUILayout.Button(StudioLanguage.Text("Pin interpolated frame as manual anchor")))
                    { Changed("Pin sample"); var pin = sample.Copy(); pin.frame = currentFrame; pin.timestamp = reader.CurrentTime; pin.manual = true; pin.confidence = 1; session.Put(pin); }
                }
                if (actual != null && GUILayout.Button(StudioLanguage.Text("Delete current sample"))) { Changed("Delete sample"); session.samples.Remove(actual); }
            }
            using (new EditorGUI.DisabledScope(!CanEdit))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(StudioLanguage.Text("Previous sample"))) Seek(session.samples.Where(s => s.frame < currentFrame).Select(s => s.frame).DefaultIfEmpty(session.startFrame).Max());
                if (GUILayout.Button(StudioLanguage.Text("Next sample"))) Seek(session.samples.Where(s => s.frame > currentFrame).Select(s => s.frame).DefaultIfEmpty(session.endFrame).Min());
            }
            EditorGUILayout.LabelField(StudioLanguage.Format("{0} samples · {1} low confidence", session.samples.Count, session.samples.Count(s => !s.manual && s.confidence < session.minimumConfidence)));
        }

        void DrawVideo()
        {
            EditorGUILayout.HelpBox(statusArgs.Length == 0 ? StudioLanguage.Text(status) : StudioLanguage.Format(status, statusArgs), error ? MessageType.Warning : MessageType.Info);
            using (new EditorGUI.DisabledScope(reader == null || !reader.Ready || tracking))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("|<", GUILayout.Width(40))) { playing = false; Seek(session.startFrame); }
                    if (GUILayout.Button("<", GUILayout.Width(30))) { playing = false; Seek(currentFrame - 1); }
                    if (GUILayout.Button(playing ? StudioLanguage.Text("Pause") : StudioLanguage.Text("Play range"), GUILayout.Width(90)))
                    {
                        playing = !playing;
                        if (playing)
                        {
                            bool restart = currentFrame < session.startFrame || currentFrame >= session.endFrame;
                            int start = restart ? session.startFrame : currentFrame;
                            if (restart) Seek(start);
                            playOriginTime = reader.TimeAtFrame(start); playOriginClock = EditorApplication.timeSinceStartup; playClockSpeed = playbackSpeed;
                        }
                    }
                    if (GUILayout.Button(">", GUILayout.Width(30))) { playing = false; Seek(currentFrame + 1); }
                    playbackSpeed = EditorGUILayout.Slider(playbackSpeed, 0.1f, 2);
                }
                EditorGUI.BeginChangeCheck();
                int frame = EditorGUILayout.IntSlider(StudioLanguage.Text("Frame"), wantedFrame >= 0 ? wantedFrame : currentFrame, 0, Math.Max(1, session.frameCount - 1));
                if (EditorGUI.EndChangeCheck()) { playing = false; Seek(frame); }
            }
            EditorGUILayout.LabelField(StudioLanguage.Format("Decoded frame {0}  ·  {1:0.000}s", currentFrame, reader != null && reader.CurrentFrame >= 0 ? reader.CurrentTime : 0) + (reader != null && reader.Busy ? StudioLanguage.Text("  (seeking...)") : ""));
            compareMode = GUILayout.Toolbar(compareMode, new[] { StudioLanguage.Text("Original + track"), StudioLanguage.Text("Overlay preset"), StudioLanguage.Text("Side by side"), StudioLanguage.Text("Source / reconstruction") });
            if (compareMode == 1 || compareMode == 2) overlayAlpha = EditorGUILayout.Slider(StudioLanguage.Text("Overlay blend"), overlayAlpha, 0, 1);
            Rect available = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(available, new Color(0.07f, 0.08f, 0.1f));
            if (reader == null || !reader.Ready || reader.CurrentFrame < 0) return;
            Rect sourceArea = available;
            if (compareMode >= 2) sourceArea.width = (available.width - 8) / 2;
            Rect videoRect = Fit(sourceArea);
            GUI.DrawTexture(videoRect, reader.Texture, ScaleMode.StretchToFill, false);
            if (compareMode != 0 && (recordedSequence || session.ValidateExport() == null) && Event.current.type == EventType.Repaint)
            {
                try
                {
                    if (comparison == null || comparedRevision != revision)
                    {
                        comparison?.Dispose(); comparison = new DamageVideoComparison();
                        if (recordedSequence) comparison.Build(recordedSequence, sequenceNumber, compareSourceCameraMotion); else comparison.Build(session);
                        comparedRevision = revision;
                    }
                    Rect targetRect = videoRect;
                    if (compareMode >= 2)
                    {
                        sourceArea.x += sourceArea.width + 8; targetRect = Fit(sourceArea);
                        if (compareMode == 2) GUI.DrawTexture(targetRect, reader.Texture, ScaleMode.StretchToFill, false);
                        else EditorGUI.DrawRect(targetRect, new Color(.2f, .22f, .27f));
                    }
                    var oldColor = GUI.color;
                    GUI.color = new Color(1, 1, 1, compareMode == 3 ? 1 : overlayAlpha);
                    float compareTime = recordedSequence ? (float)(reader.CurrentTime - recordedSequence.sourceStartTime) : (float)(reader.CurrentTime - session.TimeOf(session.At(session.startFrame)));
                    try { GUI.DrawTexture(targetRect, comparison.Render(targetRect, compareTime), ScaleMode.StretchToFill, true); }
                    finally { GUI.color = oldColor; }
                }
                catch (Exception ex) { compareMode = 0; Message(ex.Message, true); }
            }
            DrawBox(videoRect);
            DrawAnalysisRegion(videoRect);
            HandleAnalysisRegionSelection(videoRect);
            HandleSelection(videoRect);
            if (selectAnalysisArea || session.limitAnalysisRegion)
                GUILayout.Label(StudioLanguage.Text("Only complete number boxes inside this area are analyzed. Include the full flight path; motion outside the area is excluded."), EditorStyles.wordWrappedMiniLabel);
            else if (!recordedSequence) GUILayout.Label(StudioLanguage.Text("Drag to select a number. Coordinates use original video pixels. Cyan = manual; yellow = automatic/interpolated.\nComparison fits the selected font to the first box; exported scale remains relative to your font/prefab."), EditorStyles.wordWrappedMiniLabel);
        }

        Rect Fit(Rect area)
        {
            float scale = Mathf.Min(area.width / session.width, area.height / session.height);
            var size = new Vector2(session.width, session.height) * scale;
            return new Rect(area.center - size / 2, size);
        }
        void DrawAnalysisRegion(Rect previewRect)
        {
            if (!session.limitAnalysisRegion && !draggingAnalysisArea) return;
            var source = draggingAnalysisArea ? DamageVideoAnalysisRegion.DragRect(analysisAreaStart, analysisAreaEnd) : session.analysisRegion;
            var area = new Rect(previewRect.x + source.x / session.width * previewRect.width, previewRect.y + source.y / session.height * previewRect.height,
                source.width / session.width * previewRect.width, source.height / session.height * previewRect.height);
            var dim = new Color(0, 0, 0, .4f);
            EditorGUI.DrawRect(Rect.MinMaxRect(previewRect.xMin, previewRect.yMin, previewRect.xMax, area.yMin), dim);
            EditorGUI.DrawRect(Rect.MinMaxRect(previewRect.xMin, area.yMax, previewRect.xMax, previewRect.yMax), dim);
            EditorGUI.DrawRect(Rect.MinMaxRect(previewRect.xMin, area.yMin, area.xMin, area.yMax), dim);
            EditorGUI.DrawRect(Rect.MinMaxRect(area.xMax, area.yMin, previewRect.xMax, area.yMax), dim);
            Handles.BeginGUI();
            Handles.DrawSolidRectangleWithOutline(area, new Color(0, 1, .3f, .06f), new Color(.2f, 1, .4f));
            Handles.EndGUI();
            GUI.Label(new Rect(area.x + 3, area.y + 3, Mathf.Max(150, area.width), 20), StudioLanguage.Text("Analysis area"), EditorStyles.whiteMiniLabel);
        }
        void HandleAnalysisRegionSelection(Rect previewRect)
        {
            if (!draggingAnalysisArea && analysisAreaControl != 0 && GUIUtility.hotControl == analysisAreaControl) GUIUtility.hotControl = 0;
            int control = GUIUtility.GetControlID("DamageVideoAnalysisRegion".GetHashCode(), FocusType.Passive, previewRect);
            if (!selectAnalysisArea || !CanSetAnalysisRange) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            { selectAnalysisArea = draggingAnalysisArea = false; if (GUIUtility.hotControl == analysisAreaControl) GUIUtility.hotControl = 0; e.Use(); Repaint(); return; }
            if (e.type == EventType.MouseDown && e.button == 0 && previewRect.Contains(e.mousePosition))
            {
                analysisAreaControl = control; GUIUtility.hotControl = control;
                draggingAnalysisArea = true;
                analysisAreaStart = analysisAreaEnd = DamageVideoAnalysisRegion.SourcePoint(e.mousePosition, previewRect, session.width, session.height);
                e.Use(); Repaint();
            }
            else if (draggingAnalysisArea && GUIUtility.hotControl == analysisAreaControl && (e.type == EventType.MouseDrag || e.type == EventType.MouseUp))
            {
                analysisAreaEnd = DamageVideoAnalysisRegion.SourcePoint(e.mousePosition, previewRect, session.width, session.height);
                if (e.type == EventType.MouseUp)
                {
                    GUIUtility.hotControl = 0; draggingAnalysisArea = false;
                    SetAnalysisRegion(DamageVideoAnalysisRegion.DragRect(analysisAreaStart, analysisAreaEnd));
                }
                e.Use(); Repaint();
            }
        }
        void DrawBox(Rect rect)
        {
            if (recordedSequence) return;
            var s = session.samples.FirstOrDefault(sample => sample.frame == currentFrame) ?? session.At(currentFrame);
            if (s == null) return;
            float scale = rect.width / session.width;
            var center = rect.position + s.center * scale;
            Vector3[] points = new Vector3[5];
            var rotation = Quaternion.Euler(0, 0, s.angle);
            var corners = new[] { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) };
            for (int i = 0; i < 4; i++) points[i] = (Vector3)center + rotation * (Vector3)(Vector2.Scale(corners[i], s.size) * (scale / 2));
            points[4] = points[0];
            Handles.BeginGUI(); Handles.color = s.manual ? Color.cyan : Color.yellow; Handles.DrawAAPolyLine(2, points); Handles.EndGUI();
        }
        void HandleSelection(Rect rect)
        {
            if (!selectArea || !CanEdit) return;
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            { dragging = true; dragStart = dragEnd = e.mousePosition; e.Use(); }
            if (dragging && e.type == EventType.MouseDrag)
            { dragEnd = new Vector2(Mathf.Clamp(e.mousePosition.x, rect.xMin, rect.xMax), Mathf.Clamp(e.mousePosition.y, rect.yMin, rect.yMax)); e.Use(); Repaint(); }
            if (dragging)
            {
                var selection = Rect.MinMaxRect(Mathf.Min(dragStart.x, dragEnd.x), Mathf.Min(dragStart.y, dragEnd.y), Mathf.Max(dragStart.x, dragEnd.x), Mathf.Max(dragStart.y, dragEnd.y));
                EditorGUI.DrawRect(selection, new Color(0, 1, 1, 0.2f));
                if (e.type == EventType.MouseUp)
                {
                    dragging = false; e.Use();
                    if (selection.width >= 3 && selection.height >= 3)
                    {
                        float scale = session.width / rect.width;
                        Changed("Select number region");
                        var old = session.At(currentFrame);
                        session.Put(new DamageVideoSample { frame = currentFrame, timestamp = reader.CurrentTime, center = (selection.center - rect.position) * scale, size = selection.size * scale, opacity = old?.opacity ?? 1 });
                        selectArea = false; Message("Manual anchor saved. Track forward, or correct additional frames.");
                    }
                }
            }
        }
        void SaveSession()
        {
            if (!EditorUtility.IsPersistent(session))
            {
                string path = EditorUtility.SaveFilePanelInProject(StudioLanguage.Text("Save extraction session"), "VideoExtraction", "asset", StudioLanguage.Text("Stores local video path, range and corrections (not the video)."));
                if (string.IsNullOrEmpty(path)) return;
                AssetDatabase.CreateAsset(session, path);
            }
            AssetDatabase.SaveAssetIfDirty(session);
            Message("Extraction session saved. The video remains at its original local path.");
        }
        void Export()
        {
            string path = EditorUtility.SaveFilePanelInProject(StudioLanguage.Text("Save animation preset"), "VideoDamagePreset", "asset", StudioLanguage.Text("Creates a normal runtime preset. Keep the session separately to resume tracking."));
            if (string.IsNullOrEmpty(path)) return;
            var preset = session.BuildPreset();
            AssetDatabase.CreateAsset(preset, path);
            AssetDatabase.SaveAssetIfDirty(preset);
            EditorGUIUtility.PingObject(preset);
            Message("Preset saved. Open Studio and assign it to your font or prefab. Save session separately to retain corrections.");
        }
    }
}
