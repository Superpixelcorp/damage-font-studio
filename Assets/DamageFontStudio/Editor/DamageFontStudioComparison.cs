using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    public sealed partial class DamageFontStudioWindow
    {
        [SerializeField] bool comparisonMode;
        [SerializeField] List<DamageFontPreset> comparisonPresets = new List<DamageFontPreset>();
        [SerializeField] int comparisonColumns;
        [SerializeField] Vector2 comparisonViewSize = new Vector2(800, 500);
        Vector2 comparisonScroll;
        readonly List<DamageComparisonPreview> comparisonCells = new List<DamageComparisonPreview>();

        bool CanPlayComparison => !InvalidSourceBinding && comparisonPresets.Exists(p => p && p.Validate() == null);

        internal void SetComparisonMode(bool enabled)
        {
            if (comparisonMode == enabled) return;
            if (enabled) SelectPlaybackTarget(false);
            Cleanup(); comparisonMode = enabled;
            Repaint();
        }

        internal void BuildComparison(bool play)
        {
            float previousTime = time;
            Cleanup();
            int amount = mode == PreviewMode.Burst ? 50 : mode == PreviewMode.Series ? Mathf.Clamp(hits,1,100) : 1;
            float spacing = mode == PreviewMode.Burst ? 0 : interval;
            foreach (var selected in comparisonPresets)
            {
                var cell = new DamageComparisonPreview(selected, previewSource, overrideTextAppearance, number,
                    amount, spacing, spawn, randomizeSpawn ? scatter : Vector2.zero, seed);
                comparisonCells.Add(cell);
                if (cell.Ready) length = Mathf.Max(length, cell.Duration);
            }
            if (HasPreview)
            {
                count = amount; playbackInterval = spacing;
                time = play ? 0 : Mathf.Clamp(previousTime, 0, length);
                running = play; Sample(); lastTime = EditorApplication.timeSinceStartup;
            }
            Repaint();
        }

        void DrawComparisonControls()
        {
            GUILayout.Label(StudioLanguage.Text("Comparison presets"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(StudioLanguage.Text("Add presets below or drag several preset assets here. All tiles share playback controls; shorter effects wait until the longest finishes."), MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(StudioLanguage.Text("Add preset slot")))
                { Undo.RecordObject(this, "Add comparison slot"); comparisonPresets.Add(null); RebuildPreview(); }
                using (new EditorGUI.DisabledScope(!preset))
                    if (GUILayout.Button(StudioLanguage.Text("Add current preset")))
                    { Undo.RecordObject(this, "Add comparison preset"); comparisonPresets.Add(preset); RebuildPreview(); }
            }
            comparisonScroll = EditorGUILayout.BeginScrollView(comparisonScroll, GUILayout.Height(Mathf.Clamp(comparisonPresets.Count * 23 + 8, 38, 145)));
            int remove = -1, move = -1, direction = 0;
            for (int i = 0; i < comparisonPresets.Count; i++)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label((i+1).ToString(), GUILayout.Width(24));
                var selected = (DamageFontPreset)EditorGUILayout.ObjectField(comparisonPresets[i], typeof(DamageFontPreset), false);
                if (selected != comparisonPresets[i])
                { Undo.RecordObject(this,"Change comparison preset"); comparisonPresets[i] = selected; RebuildPreview(); }
                using (new EditorGUI.DisabledScope(!selected))
                    if (GUILayout.Button(StudioLanguage.Text("Edit"), GUILayout.Width(42))) { preset = selected; serializedPreset = null; }
                using (new EditorGUI.DisabledScope(i == 0))
                    if (GUILayout.Button("↑", GUILayout.Width(24))) { move = i; direction = -1; }
                using (new EditorGUI.DisabledScope(i == comparisonPresets.Count-1))
                    if (GUILayout.Button("↓", GUILayout.Width(24))) { move = i; direction = 1; }
                if (GUILayout.Button("×", GUILayout.Width(24))) remove = i;
            }
            EditorGUILayout.EndScrollView();
            Rect dropArea = GUILayoutUtility.GetLastRect();
            var e = Event.current;
            if (dropArea.Contains(e.mousePosition) && (e.type == EventType.DragUpdated || e.type == EventType.DragPerform))
            {
                bool accepts = System.Array.Exists(DragAndDrop.objectReferences, item => item is DamageFontPreset);
                if (accepts)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag(); Undo.RecordObject(this,"Drop comparison presets");
                        foreach (var item in DragAndDrop.objectReferences) if (item is DamageFontPreset selected) comparisonPresets.Add(selected);
                        RebuildPreview();
                    }
                    e.Use();
                }
            }
            if (remove >= 0 || move >= 0)
            {
                Undo.RecordObject(this, "Reorder comparison presets");
                if (remove >= 0) comparisonPresets.RemoveAt(remove);
                else { var item = comparisonPresets[move]; comparisonPresets.RemoveAt(move); comparisonPresets.Insert(move+direction,item); }
                RebuildPreview(); GUIUtility.ExitGUI();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                comparisonColumns = EditorGUILayout.Popup(StudioLanguage.Text("Grid columns"), comparisonColumns,
                    new[] { StudioLanguage.Text("Automatic"), "1", "2", "3", "4", "5", "6" });
                EditorGUILayout.LabelField(StudioLanguage.Format("{0} presets", comparisonPresets.Count), GUILayout.Width(100));
            }
            comparisonViewSize = EditorGUILayout.Vector2Field(StudioLanguage.Text("Comparison view size"), comparisonViewSize);
            comparisonViewSize.x = Mathf.Max(10, comparisonViewSize.x); comparisonViewSize.y = Mathf.Max(10, comparisonViewSize.y);
        }

        internal static Rect[] ComparisonRects(Rect area, int count, int columns)
        {
            if (count <= 0 || area.width <= 0 || area.height <= 0) return new Rect[0];
            columns = columns <= 0 ? Mathf.RoundToInt(Mathf.Sqrt(count * area.width / area.height / 1.6f)) : columns;
            columns = Mathf.Clamp(columns, 1, count);
            int rows = Mathf.CeilToInt((float)count / columns);
            float gap = Mathf.Min(6, Mathf.Min(area.width/columns,area.height/rows) * .1f);
            float width = (area.width - (columns-1)*gap) / columns;
            float height = (area.height - (rows-1)*gap) / rows;
            var result = new Rect[count];
            for (int i = 0; i < count; i++)
                result[i] = new Rect(area.x+(i%columns)*(width+gap), area.y+(i/columns)*(height+gap), width, height);
            return result;
        }

        void DrawComparisonGrid(Rect area)
        {
            EditorGUI.DrawRect(area, new Color(.035f,.045f,.065f));
            var rects = ComparisonRects(area, comparisonPresets.Count, comparisonColumns);
            for (int i = 0; i < rects.Length; i++)
            {
                Rect tile = rects[i];
                float header = Mathf.Min(24, tile.height * .25f);
                Rect content = new Rect(tile.x, tile.y + header, tile.width, tile.height - header);
                EditorGUI.DrawRect(tile, new Color(.17f,.18f,.2f));
                string title = comparisonPresets[i] ? comparisonPresets[i].name : StudioLanguage.Text("Empty slot");
                GUI.Label(new Rect(tile.x+4,tile.y,Mathf.Max(0,tile.width-8),header), new GUIContent($"{i+1}. {title}", title), EditorStyles.whiteMiniLabel);
                var cell = i < comparisonCells.Count ? comparisonCells[i] : null;
                if (cell != null && cell.Ready && content.width >= 1 && content.height >= 1)
                {
                    GUI.DrawTexture(content, cell.Render(content, light, zoom, comparisonViewSize), ScaleMode.StretchToFill, false);
                    string timing = StudioLanguage.Format("{0:0.00} / {1:0.00} s", Mathf.Min(time,cell.Duration), cell.Duration);
                    GUI.Label(new Rect(content.x+5,content.y+4,Mathf.Max(0,content.width-10),18), timing,
                        light ? EditorStyles.miniLabel : EditorStyles.whiteMiniLabel);
                }
                else
                {
                    string error = cell?.Error;
                    if (error == null && comparisonPresets[i]) error = comparisonPresets[i].Validate();
                    if (error == null && InvalidSourceBinding) error = "Number Text must belong to the selected object's hierarchy.";
                    EditorGUI.HelpBox(content, StudioLanguage.Text(error ?? "Select presets and press Single / Hit series."), MessageType.None);
                }
            }
            if (rects.Length == 0)
                GUI.Label(new Rect(area.x+10,area.y+10,Mathf.Max(0,area.width-20),50), StudioLanguage.Text("Add presets to compare them in separate tiles."), EditorStyles.whiteMiniLabel);
        }
    }
}
