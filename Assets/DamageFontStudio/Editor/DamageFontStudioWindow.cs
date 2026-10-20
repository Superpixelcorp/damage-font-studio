using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public sealed partial class DamageFontStudioWindow : EditorWindow
    {
        [SerializeField] DamageFontPreset preset;
        [SerializeField] GameObject previewSource;
        [SerializeField] GameObject gamePreviewSource;
        [SerializeField] bool showFinalCandidates;
        [SerializeField] DamageFontSequence recordedSequence;
        [SerializeField] bool recordedPreviewMode;
        [SerializeField] string recordedNumber = "";
        [SerializeField] int recordedEditHit;
        [SerializeField] DamageFontPreset authoringPreset;
        [SerializeField] GameObject authoringSource;
        [SerializeField] bool authoringOverride;
        [SerializeField] bool overrideTextAppearance;
        [SerializeField] string number = "123456";
        [SerializeField] int hits = 5, seed = 17;
        [SerializeField] float interval = 0.12f, speed = 1;
        [SerializeField] Vector2 spawn, scatter = new Vector2(100, 25);
        [SerializeField] bool randomizeSpawn;
        [SerializeField] bool loop, light;
        [SerializeField] float zoom = 1;
        readonly List<DamageFontPlayback> players = new List<DamageFontPlayback>();
        PreviewRenderUtility preview;
        DamageFontSequencePlayback recordedPlayback;
        SerializedObject serializedPreset;
        Vector2 scroll;
        float time, length, playbackInterval;
        internal enum PreviewMode { Single, Series, Burst }
        PreviewMode mode;
        int count;
        bool running;
        double lastTime;
        string previewError;

        bool InvalidSourceBinding => previewSource && previewSource.TryGetComponent<DamageFontObject>(out var binding) && !binding.HasValidTextBinding;
        bool HasPreview => comparisonCells.Exists(cell => cell.Ready) || recordedPlayback != null || players.Count > 0;

        [MenuItem("Tools/Damage Font Studio/Open Studio")]
        public static void Open() { GetWindow<DamageFontStudioWindow>(StudioLanguage.Text("Damage Font Studio")); }

        internal static void OpenFor(DamageFontObject binding)
        {
            var window = GetWindow<DamageFontStudioWindow>(StudioLanguage.Text("Damage Font Studio"));
            window.SelectPlaybackTarget(false);
            window.Cleanup();
            window.recordedSequence = null;
            window.previewSource = binding.gameObject;
            if (binding.preset) window.preset = binding.preset;
            window.number = binding.number;
            window.overrideTextAppearance = binding.overrideTextAppearance;
            window.randomizeSpawn = false;
            window.serializedPreset = null;
            window.time = 0;
            window.Repaint();
        }

        public static void OpenSequence(DamageFontSequence sequence)
        {
            if (!sequence) return;
            var window = GetWindow<DamageFontStudioWindow>(StudioLanguage.Text("Damage Font Studio"));
            window.Cleanup(); window.recordedSequence = sequence; window.recordedNumber = "";
            window.SelectPlaybackTarget(true); window.recordedEditHit = 0; window.SelectRecordedHit(0);
            window.BuildRecorded(true); window.Focus();
        }

        public static void OpenPreset(DamageFontPreset selected)
        {
            if (!selected) return;
            var window = GetWindow<DamageFontStudioWindow>(StudioLanguage.Text("Damage Font Studio"));
            window.UsePreset(selected); window.OpenMotionPreview(); window.Focus();
        }
        public static void OpenGameResult(DamageFontSequence sequence)
        {
            if (!sequence) return;
            var selected = DamageVideoFinalMotion.Ensure(sequence);
            var window = GetWindow<DamageFontStudioWindow>(StudioLanguage.Text("Damage Font Studio"));
            window.recordedSequence = sequence; window.UsePreset(selected); window.OpenMotionPreview(); window.Focus();
        }
        void OpenMotionPreview()
        {
            if (preset.animationOnly && !previewSource) PlayDefaultText();
            else StartPreview(PreviewMode.Single);
        }

        internal void PlayDefaultText()
        {
            SelectPlaybackTarget(false);
            previewSource = null; gamePreviewSource = null; overrideTextAppearance = false;
            if (string.IsNullOrWhiteSpace(number)) number = "123456";
            spawn = Vector2.zero; randomizeSpawn = false; zoom = 1; light = false;
            loop = true;
            StartPreview(PreviewMode.Single);
        }
        static bool IsAnalysisSample(GameObject source)
            => source && AssetDatabase.GetAssetPath(source).StartsWith("Assets/DamageFontStudio/Generated/Video-", System.StringComparison.Ordinal);

        internal void UsePreset(DamageFontPreset selected, GameObject gameSource = null)
        {
            SelectPlaybackTarget(false); Cleanup();
            if (gameSource) gamePreviewSource = gameSource;
            else if (previewSource && !IsAnalysisSample(previewSource)) gamePreviewSource = previewSource;
            if (selected.animationOnly) { previewSource = gamePreviewSource; overrideTextAppearance = false; }
            preset = selected; serializedPreset = null; time = 0;
            Repaint();
        }

        void OnEnable()
        {
            minSize = new Vector2(980, 760);
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            Undo.undoRedoPerformed += PresetChanged;
            if (!preset) preset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>("Assets/DamageFontStudio/Presets/Normal.asset");
            lastTime = EditorApplication.timeSinceStartup;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            Undo.undoRedoPerformed -= PresetChanged;
            Cleanup();
        }

        void PlayModeChanged(PlayModeStateChange state) { Cleanup(); Repaint(); }
        void PresetChanged() { RebuildPreview(); Repaint(); }

        internal void StartPreview(PreviewMode next)
        {
            if (recordedPreviewMode) SelectPlaybackTarget(false);
            mode = next;
            if (comparisonMode) { BuildComparison(true); return; }
            Build(mode == PreviewMode.Burst ? 50 : mode == PreviewMode.Series ? hits : 1, true, mode == PreviewMode.Burst);
        }

        internal void SelectPlaybackTarget(bool useRecorded)
        {
            if (comparisonMode) { Cleanup(); comparisonMode = false; }
            if (recordedPreviewMode == useRecorded) return;
            Cleanup();
            if (useRecorded)
            {
                authoringPreset = preset; authoringSource = previewSource; authoringOverride = overrideTextAppearance;
                recordedPreviewMode = true; SelectRecordedHit(recordedEditHit);
            }
            else
            {
                recordedPreviewMode = false;
                preset = authoringPreset; previewSource = authoringSource; overrideTextAppearance = authoringOverride;
                serializedPreset = null;
            }
            Repaint();
        }

        void SelectRecordedHit(int index)
        {
            if (!recordedSequence || recordedSequence.events.Count == 0) return;
            recordedEditHit = Mathf.Clamp(index, 0, recordedSequence.events.Count - 1);
            var sample = recordedSequence.events[recordedEditHit].sample;
            if (!sample || !sample.TryGetComponent<DamageFontObject>(out var binding)) return;
            preset = binding.preset; previewSource = sample; overrideTextAppearance = binding.overrideTextAppearance;
            serializedPreset = null;
        }

        void DrawRecordedControls()
        {
            if (!recordedSequence) return;
            DamageVideoFinalMotion.Draw(recordedSequence, ref showFinalCandidates);
            EditorGUILayout.HelpBox(StudioLanguage.Text("RecordedHits is an analysis comparison: each hit uses its own measured motion. Test GameMotion.asset to see the exact game-facing effect."), MessageType.Info);
            EditorGUILayout.LabelField(StudioLanguage.Format("{0} recorded hits", recordedSequence.events.Count));
            EditorGUI.BeginChangeCheck();
            bool lockX = EditorGUILayout.Toggle(StudioLanguage.Text("Lock spawn X"), recordedSequence.lockSpawnX);
            float x = recordedSequence.spawnX;
            using (new EditorGUI.DisabledScope(!lockX)) x = EditorGUILayout.FloatField(StudioLanguage.Text("Spawn X"), x);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(recordedSequence, "Change sequence spawn X");
                recordedSequence.lockSpawnX = lockX;
                if (float.IsFinite(x)) recordedSequence.spawnX = x;
                EditorUtility.SetDirty(recordedSequence); RebuildPreview();
            }
            if (GUILayout.Button(StudioLanguage.Text("Compare recorded hits"))) DamageVideoExtractionWindow.OpenRecorded(recordedSequence);
            string error = ValidateRecorded();
            if (error != null) EditorGUILayout.HelpBox(StudioLanguage.Text(error), MessageType.Error);
        }

        internal void RebuildPreview()
        {
            if (comparisonMode && comparisonCells.Count > 0) BuildComparison(false);
            else if (recordedPlayback != null) BuildRecorded(false);
            else if (players.Count > 0)
                Build(mode == PreviewMode.Burst ? 50 : mode == PreviewMode.Series ? hits : 1, false, mode == PreviewMode.Burst);
        }

        internal void TogglePause()
        {
            if (!HasPreview) return;
            if (time >= length) time = 0;
            running = !running;
            lastTime = EditorApplication.timeSinceStartup;
            Sample();
            Repaint();
        }

        internal void SeekPreview(float targetTime)
        {
            if (!HasPreview) return;
            running = false;
            time = Mathf.Clamp(targetTime, 0, length);
            Sample();
            Repaint();
        }

        internal void StopPreview() { Cleanup(); time = 0; Repaint(); }

        void Cleanup()
        {
            running = false;
            foreach (var player in players) player.Dispose();
            players.Clear();
            foreach (var cell in comparisonCells) cell.Dispose();
            comparisonCells.Clear();
            recordedPlayback?.Dispose(); recordedPlayback = null;
            preview?.Cleanup();
            preview = null;
            count = 0;
            length = 0;
            playbackInterval = 0;
            time = 0;
            previewError = null;
        }

        void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = (float)(now - lastTime);
            lastTime = now;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            Advance(Mathf.Clamp(delta, 0, 0.1f));
        }

        internal void Advance(float delta)
        {
            if (!running || !HasPreview || length <= 0) return;
            time += Mathf.Max(0, delta) * speed;
            if (time >= length)
            {
                if (loop) time %= length;
                else { time = length; running = false; }
            }
            Sample();
            Repaint();
        }

        void Build(int amount, bool play, bool burst = false)
        {
            float previousTime = time;
            Cleanup();
            if (!preset) { previewError = "Choose a motion preset to preview."; return; }
            if (preset.Validate() is string error) { previewError = error; return; }
            if (InvalidSourceBinding) { previewError = "Number Text must belong to the selected object's hierarchy."; return; }
            var font = preset.font ? preset.font : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (!previewSource && !font) { previewError = "Choose a TMP font or import TMP Essential Resources."; return; }
            preview = new PreviewRenderUtility();
            preview.camera.orthographic = true;
            preview.camera.nearClipPlane = 0.1f;
            preview.camera.farClipPlane = 2000;
            preview.camera.transform.position = new Vector3(0, 0, -1000);
            preview.camera.transform.rotation = Quaternion.identity;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.allowHDR = false;
            count = Mathf.Clamp(amount, 1, 100);
            playbackInterval = burst ? 0 : interval;
            length = preset.Duration + (count - 1) * playbackInterval;
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                DamageFontPlayback player;
                if (previewSource)
                {
                    var clone = DamageObjectPreview.Clone(previewSource, preview);
                    var binding = clone.GetComponent<DamageFontObject>();
                    player = new DamageFontPlayback(clone, binding ? binding.numberText : null, overrideTextAppearance);
                }
                else
                {
                    var go = new GameObject("Damage preview " + i, typeof(RectTransform));
                    go.hideFlags = HideFlags.HideAndDontSave;
                    preview.AddSingleGO(go);
                    var text = go.AddComponent<TextMeshPro>();
                    text.font = font;
                    text.fontSize = preset.animationOnly ? 64 : preset.fontSize;
                    text.color = Color.white;
                    text.enableAutoSizing = false;
                    text.isOrthographic = true;
                    text.alignment = TextAlignmentOptions.Center;
                    text.textWrappingMode = TextWrappingModes.NoWrap;
                    text.richText = false;
                    text.rectTransform.sizeDelta = new Vector2(1200, 400);
                    player = new DamageFontPlayback(text);
                }
                Vector2 spread = randomizeSpawn ? scatter : Vector2.zero;
                Vector3 offset = new Vector3(spawn.x + ((float)random.NextDouble() * 2 - 1) * spread.x,
                    spawn.y + ((float)random.NextDouble() * 2 - 1) * spread.y, -i * 0.01f);
                player.Prepare(preset, number, offset, manual: true);
                players.Add(player);
            }
            time = play ? 0 : Mathf.Clamp(previousTime, 0, length);
            running = play;
            Sample();
            lastTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        void Sample()
        {
            if (comparisonMode) { foreach (var cell in comparisonCells) cell.Seek(time); return; }
            if (recordedPlayback != null) { recordedPlayback.Seek(time >= length ? length + .001f : time); return; }
            // Subtracting a large accumulated hit delay can round the last local
            // time just below Duration. The end of the whole timeline is final.
            for (int i = 0; i < players.Count; i++)
                players[i].Seek(time >= length ? preset.Duration : time - i * playbackInterval);
        }

        string ValidateRecorded()
        {
            if (!recordedSequence || recordedSequence.events.Count == 0) return "Choose a RecordedHits sequence with hit events.";
            if (recordedSequence.sourceWidth <= 0 || recordedSequence.sourceHeight <= 0) return "RecordedHits has invalid source dimensions.";
            foreach (var hit in recordedSequence.events)
            {
                if (!hit.sample || !hit.sample.TryGetComponent<DamageFontObject>(out var binding) || !binding.preset)
                    return "RecordedHits contains a missing sample or preset.";
                string error = binding.preset.Validate();
                if (error != null) return error;
            }
            return null;
        }

        void BuildRecorded(bool play)
        {
            if (!recordedPreviewMode) SelectPlaybackTarget(true);
            float previousTime = time; Cleanup();
            if (ValidateRecorded() != null) return;
            try
            {
                preview = new PreviewRenderUtility();
                preview.camera.orthographic = true;
                preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 2000;
                preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.allowHDR = false;
                preview.camera.transform.position = new Vector3(recordedSequence.sourceWidth * .5f, -recordedSequence.sourceHeight * .5f, -1000);
                preview.camera.transform.rotation = Quaternion.identity;
                var parent = new GameObject("RecordedHits playback lab") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(parent);
                recordedPlayback = new DamageFontSequencePlayback(recordedSequence, parent.transform, recordedNumber);
                foreach (var text in parent.GetComponentsInChildren<TMP_Text>(true)) text.isOrthographic = true;
                count = recordedSequence.events.Count;
                length = recordedSequence.duration;
                if (length <= 0) foreach (var hit in recordedSequence.events)
                    length = Mathf.Max(length, hit.time + hit.sample.GetComponent<DamageFontObject>().preset.Duration);
                time = play ? 0 : Mathf.Clamp(previousTime, 0, length);
                running = play; Sample(); lastTime = EditorApplication.timeSinceStartup;
            }
            catch (System.Exception ex) { Cleanup(); Debug.LogException(ex); }
            Repaint();
        }

        void OnGUI()
        {
            titleContent = new GUIContent(StudioLanguage.Text("Damage Font Studio"));
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(StudioLanguage.Text("DAMAGE FONT STUDIO"), EditorStyles.boldLabel);
                if (GUILayout.Button(StudioLanguage.Text("Extract from video"), EditorStyles.toolbarButton)) DamageVideoExtractionWindow.Open();
                GUILayout.FlexibleSpace();
                StudioLanguage.DrawSelector();
                GUILayout.Label(StudioLanguage.Text("Editor preview · DOTween + TextMeshPro"), EditorStyles.miniLabel);
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(StudioLanguage.Text("Exit Play mode to use the editor preview."), MessageType.Info);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(355))) DrawSettings();
                using (new EditorGUILayout.VerticalScope()) DrawPreview();
            }
        }

        void DrawSettings()
        {
            if (comparisonMode) DrawComparisonControls();
            if (recordedPreviewMode && recordedSequence && recordedSequence.events.Count > 0)
            {
                GUILayout.Label(StudioLanguage.Text("Edit recorded hit preset"), EditorStyles.boldLabel);
                var labels = new string[recordedSequence.events.Count];
                for (int i = 0; i < labels.Length; i++) labels[i] = $"{i + 1}. {recordedSequence.events[i].sample?.name ?? "—"}";
                int selected = EditorGUILayout.Popup(StudioLanguage.Text("Hit to edit"), Mathf.Clamp(recordedEditHit, 0, labels.Length - 1), labels);
                if (selected != recordedEditHit) SelectRecordedHit(selected);
            }
            EditorGUI.BeginChangeCheck();
            var next = (DamageFontPreset)EditorGUILayout.ObjectField(StudioLanguage.Text("Preset (.asset)"), preset, typeof(DamageFontPreset), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (comparisonMode) { preset = next; serializedPreset = null; RebuildPreview(); }
                else { SelectPlaybackTarget(false); Cleanup(); preset = next; serializedPreset = null; time = 0; }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(StudioLanguage.Text("New"))) SaveCopy(false);
                using (new EditorGUI.DisabledScope(!preset))
                {
                    if (GUILayout.Button(StudioLanguage.Text("Duplicate"))) SaveCopy(true);
                    if (GUILayout.Button(StudioLanguage.Text("Save"))) AssetDatabase.SaveAssetIfDirty(preset);
                    if (GUILayout.Button(StudioLanguage.Text("Locate"))) EditorGUIUtility.PingObject(preset);
                }
            }
            if (!preset)
            {
                EditorGUILayout.HelpBox(StudioLanguage.Text("Create a preset or select an existing .asset.\nTools > Damage Font Studio > Create Example Presets"), MessageType.Info);
                return;
            }
            if (preset.animationOnly && preset.name == "GameMotion")
                EditorGUILayout.HelpBox(StudioLanguage.Text("This is the final game asset. The playback lab uses this exact preset; assign your existing game prefab to verify its appearance."), MessageType.Info);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (serializedPreset == null || serializedPreset.targetObject != preset) serializedPreset = new SerializedObject(preset);
            serializedPreset.Update();
            EditorGUI.BeginChangeCheck();
            foreach (string field in new[] { "kind", "font", "fontMaterial", "overrideFontStyle", "fontStyle", "fontSize", "initialColor", "initialOpacity", "initialScale", "initialRotation", "segments" })
                StudioLanguage.Property(serializedPreset.FindProperty(field));
            if (EditorGUI.EndChangeCheck())
            {
                serializedPreset.ApplyModifiedProperties();
                RebuildPreview();
            }
            else serializedPreset.ApplyModifiedProperties();
            string error = preset.Validate();
            if (error != null) EditorGUILayout.HelpBox(StudioLanguage.Text(error), MessageType.Error);
            EditorGUILayout.HelpBox(StudioLanguage.Text("Position: preview pixels, relative to spawn.\nScale: absolute multiplier. Rotation: Z degrees.\nColor alpha × Opacity. Same-channel segments cannot overlap.\nEdits update the preview and support Undo. Save writes the asset."), MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void DrawPreview()
        {
            GUILayout.Label(StudioLanguage.Text("Playback lab"), EditorStyles.boldLabel);
            int currentTarget = comparisonMode ? 2 : recordedPreviewMode ? 1 : 0;
            int target = GUILayout.Toolbar(currentTarget, new[] { StudioLanguage.Text("Preset authoring / test"), StudioLanguage.Text("RecordedHits playback"), StudioLanguage.Text("Multiple preset comparison") });
            if (target != currentTarget)
            {
                if (target == 2) SetComparisonMode(true);
                else
                {
                    SetComparisonMode(false); SelectPlaybackTarget(target == 1);
                    if (recordedPreviewMode && ValidateRecorded() == null) BuildRecorded(false);
                }
            }
            if (!comparisonMode)
            {
                EditorGUI.BeginChangeCheck();
                var selectedSequence = (DamageFontSequence)EditorGUILayout.ObjectField(StudioLanguage.Text("Recorded hit sequence"), recordedSequence, typeof(DamageFontSequence), false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (recordedPreviewMode) Cleanup();
                    recordedSequence = selectedSequence; recordedEditHit = 0;
                    if (recordedPreviewMode && recordedSequence) { recordedNumber = ""; SelectRecordedHit(0); BuildRecorded(false); }
                    else if (!recordedSequence) SelectPlaybackTarget(false);
                }
            }
            if (recordedPreviewMode) DrawRecordedControls();
            EditorGUI.BeginChangeCheck();
            if (!recordedPreviewMode)
            {
                var source = (GameObject)EditorGUILayout.ObjectField(StudioLanguage.Text(preset && preset.animationOnly ? "Existing game damage prefab / object" : "Prefab / Scene Object"), previewSource, typeof(GameObject), true);
                if (source != previewSource) { previewSource = source; if (preset && preset.animationOnly) gamePreviewSource = source; }
                if (preset && preset.animationOnly && !previewSource)
                    EditorGUILayout.HelpBox(StudioLanguage.Text("No game prefab assigned: showing fallback text. Assign your existing prefab to verify the actual game appearance."), MessageType.Info);
                if (!comparisonMode && preset && GUILayout.Button(StudioLanguage.Text("Play with default digits (no prefab)")))
                {
                    PlayDefaultText(); GUIUtility.ExitGUI();
                }
                if (previewSource)
                {
                    string sourcePath = AssetDatabase.GetAssetPath(previewSource);
                    if (!string.IsNullOrEmpty(sourcePath))
                    {
                        string folder = System.IO.Path.GetDirectoryName(sourcePath).Replace('\\', '/');
                        var captured = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder + "/RecordedHits.asset");
                        if (!captured && folder.EndsWith("/Analysis", System.StringComparison.Ordinal))
                            captured = AssetDatabase.LoadAssetAtPath<DamageFontSequence>(folder.Substring(0, folder.Length - "/Analysis".Length) + "/RecordedHits.asset");
                        if (captured && GUILayout.Button(StudioLanguage.Text("Play RecordedHits in Studio"))) { OpenSequence(captured); GUIUtility.ExitGUI(); }
                    }
                    overrideTextAppearance = EditorGUILayout.Toggle(StudioLanguage.Text("Override text style"), overrideTextAppearance);
                    EditorGUILayout.HelpBox(StudioLanguage.Text("Preview uses a copy. Root motion applies to the whole hierarchy.\nOriginal scale, rotation and child layout are preserved. Color is a tint multiplier.\nTMP / UI Graphic / SpriteRenderer support color and opacity. Scripts and Animator are disabled in preview."), MessageType.None);
                    var binding = previewSource.GetComponent<DamageFontObject>();
                    if (InvalidSourceBinding) EditorGUILayout.HelpBox(StudioLanguage.Text("Number Text must belong to the selected object's hierarchy."), MessageType.Error);
                    if (binding && binding.preset && GUILayout.Button(StudioLanguage.Text("Use object's preset and text binding")))
                    {
                        preset = binding.preset;
                        number = binding.number;
                        overrideTextAppearance = binding.overrideTextAppearance;
                        serializedPreset = null;
                        GUI.changed = true;
                    }
                }
            }
            if (recordedPreviewMode)
            {
                recordedNumber = EditorGUILayout.TextField(StudioLanguage.Text("Display number / text"), recordedNumber);
                EditorGUILayout.LabelField(StudioLanguage.Text("Leave empty to use each hit's saved number."), EditorStyles.wordWrappedMiniLabel);
            }
            else number = EditorGUILayout.TextField(StudioLanguage.Text("Display number / text"), number);
            if (!recordedPreviewMode)
            {
                hits = EditorGUILayout.IntSlider(StudioLanguage.Text("Hit count"), hits, 1, 100);
                interval = EditorGUILayout.Slider(StudioLanguage.Text("Hit interval (seconds)"), interval, 0, 1);
                spawn = EditorGUILayout.Vector2Field(StudioLanguage.Text("Spawn position"), spawn);
                randomizeSpawn = EditorGUILayout.Toggle(StudioLanguage.Text("Randomize spawn positions"), randomizeSpawn);
                using (new EditorGUI.DisabledScope(!randomizeSpawn))
                {
                    scatter = EditorGUILayout.Vector2Field(StudioLanguage.Text("Random spread (+/-)"), scatter);
                    seed = EditorGUILayout.IntField(StudioLanguage.Text("Random seed"), seed);
                }
            }
            if (EditorGUI.EndChangeCheck()) RebuildPreview();
            using (new EditorGUILayout.HorizontalScope())
            {
                light = GUILayout.Toggle(light, StudioLanguage.Text("Light background"));
                loop = GUILayout.Toggle(loop, StudioLanguage.Text("Repeat"));
                GUILayout.Label(StudioLanguage.Text("Speed"), GUILayout.Width(40));
                speed = EditorGUILayout.Slider(speed, 0.1f, 3);
            }
            using (new EditorGUI.DisabledScope(comparisonMode ? !CanPlayComparison : recordedPreviewMode ? ValidateRecorded() != null : !preset || preset.Validate() != null || InvalidSourceBinding))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (recordedPreviewMode)
                {
                    if (GUILayout.Button(StudioLanguage.Text("Play full sequence"))) BuildRecorded(true);
                }
                else
                {
                    if (GUILayout.Button(StudioLanguage.Text("Single"))) StartPreview(PreviewMode.Single);
                    if (GUILayout.Button(StudioLanguage.Text("Hit series"))) StartPreview(PreviewMode.Series);
                    if (GUILayout.Button(StudioLanguage.Text("Burst × 50"))) StartPreview(PreviewMode.Burst);
                }
                using (new EditorGUI.DisabledScope(!HasPreview))
                {
                    if (GUILayout.Button(running ? StudioLanguage.Text("Pause") : StudioLanguage.Text("Resume")))
                        TogglePause();
                    if (GUILayout.Button(StudioLanguage.Text("Stop / clear"))) StopPreview();
                }
            }
            using (new EditorGUI.DisabledScope(!HasPreview))
            {
                EditorGUI.BeginChangeCheck();
                float seek = EditorGUILayout.Slider(StudioLanguage.Text("Time (seconds)"), time, 0, Mathf.Max(length, 0.01f));
                if (EditorGUI.EndChangeCheck()) SeekPreview(seek);
            }
            zoom = Mathf.Clamp(EditorGUILayout.FloatField(StudioLanguage.Text("Preview zoom"), zoom), 0.01f, 1000);
            if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(StudioLanguage.Text(previewError), MessageType.Error);
            else if (HasPreview && !running && time >= length)
                EditorGUILayout.HelpBox(StudioLanguage.Text("Playback finished. Press Single / Hit series again, or enable Repeat and Resume."), MessageType.Info);
            Rect rect = GUILayoutUtility.GetRect(300, 250, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
            {
                if (comparisonMode) DrawComparisonGrid(rect);
                else if (preview != null)
                {
                    GUI.DrawTexture(rect, RenderPreview(rect), ScaleMode.StretchToFill, false);
                }
                else EditorGUI.DrawRect(rect, light ? new Color(0.86f, 0.88f, 0.91f) : new Color(0.035f, 0.045f, 0.065f));
            }
            if (!comparisonMode)
                GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, 24), !HasPreview ? StudioLanguage.Text(recordedPreviewMode ? "Press Play full sequence to preview RecordedHits." : "Select a preset and press Single / Hit series") : StudioLanguage.Format("{0}  |  {1} instances  |  {2:0.00} / {3:0.00} s", recordedPreviewMode ? recordedSequence.name : StudioLanguage.EnumText(preset.kind.ToString()), count, time, length), EditorStyles.whiteMiniLabel);
            GUILayout.Label(StudioLanguage.Text("Fixed random seed keeps replay and scrubbing reproducible. Preview objects never enter your scene."), EditorStyles.miniLabel);
        }

        Texture RenderPreview(Rect rect)
        {
            preview.camera.backgroundColor = light ? new Color(0.86f, 0.88f, 0.91f) : new Color(0.035f, 0.045f, 0.065f);
            preview.camera.orthographicSize = recordedPlayback != null
                ? Mathf.Max(recordedSequence.sourceHeight * .5f, recordedSequence.sourceWidth * .5f / Mathf.Max(.01f, rect.width / rect.height)) * 1.05f / zoom
                : rect.height * 0.5f / zoom;
            Canvas.ForceUpdateCanvases();
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true);
            return preview.EndPreview();
        }

        internal void VerifyRendering(string path, bool bright, GameObject source = null)
        {
            preset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>("Assets/DamageFontStudio/Presets/Critical.asset");
            previewSource = source;
            light = bright;
            Build(5, false);
            time = 0.35f;
            Sample();
            var target = (RenderTexture)RenderPreview(new Rect(0, 0, 900, 500));
            var previous = RenderTexture.active;
            var image = new Texture2D(900, 500, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 900, 500), 0, 0);
                image.Apply();
                var pixels = image.GetPixels32();
                var background = pixels[0];
                int changed = 0;
                foreach (var p in pixels) if (Mathf.Abs(p.r - background.r) + Mathf.Abs(p.g - background.g) + Mathf.Abs(p.b - background.b) > 30) changed++;
                if (changed < 200) throw new System.Exception("Preview render is empty: " + changed);
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; DestroyImmediate(image); Cleanup(); }
        }

        void SaveCopy(bool duplicate)
        {
            string path = EditorUtility.SaveFilePanelInProject(duplicate ? StudioLanguage.Text("Duplicate preset") : StudioLanguage.Text("New preset"), duplicate ? preset.name + " Copy" : "DamagePreset", "asset", StudioLanguage.Text("Choose a preset path"));
            if (string.IsNullOrEmpty(path)) return;
            var asset = duplicate ? Instantiate(preset) : CreateInstance<DamageFontPreset>();
            if (!duplicate) asset.SetDefaults(DamageKind.Normal);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Cleanup();
            SelectPlaybackTarget(false);
            preset = asset;
            serializedPreset = null;
        }

        [MenuItem("Tools/Damage Font Studio/Create Example Presets")]
        public static void CreateExamples()
        {
            const string folder = "Assets/DamageFontStudio/Presets";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/DamageFontStudio", "Presets");
            foreach (DamageKind kind in System.Enum.GetValues(typeof(DamageKind)))
            {
                string path = folder + "/" + kind + ".asset";
                if (AssetDatabase.LoadAssetAtPath<DamageFontPreset>(path)) continue;
                var asset = CreateInstance<DamageFontPreset>();
                asset.SetDefaults(kind);
                asset.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                AssetDatabase.CreateAsset(asset, path);
            }
            const string arcPath = folder + "/FallingArc.asset";
            if (!AssetDatabase.LoadAssetAtPath<DamageFontPreset>(arcPath))
            {
                var asset = CreateInstance<DamageFontPreset>();
                asset.SetDefaults(DamageKind.Critical);
                asset.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                var movement = asset.segments[0];
                movement.positionPath = DamagePositionPath.Parabola;
                movement.arcHeight = 140;
                movement.targetVector = new Vector3(160, -120, 0);
                movement.duration = 1.1f;
                movement.ease = DG.Tweening.Ease.Linear;
                asset.segments[3].start = 0.8f;
                asset.segments[3].duration = 0.3f;
                AssetDatabase.CreateAsset(asset, arcPath);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
