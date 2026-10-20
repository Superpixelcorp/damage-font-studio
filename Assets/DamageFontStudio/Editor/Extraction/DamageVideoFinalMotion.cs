using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // One stable game-facing asset. Per-hit observations remain independent analysis data.
    internal static class DamageVideoFinalMotion
    {
        internal const string FileName = "GameMotion.asset";
        internal static string AssetPath(DamageFontSequence sequence)
        {
            string path = AssetDatabase.GetAssetPath(sequence);
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path).Replace('\\', '/') + "/" + FileName;
        }
        internal static DamageFontPreset Get(DamageFontSequence sequence)
        {
            string path = AssetPath(sequence);
            return path == null ? null : AssetDatabase.LoadAssetAtPath<DamageFontPreset>(path);
        }
        internal static DamageFontPreset Source(DamageFontSequenceEvent hit)
            => hit.motionPreset ? hit.motionPreset : hit.sample && hit.sample.TryGetComponent<DamageFontObject>(out var binding) ? binding.preset : null;

        internal static int BestSource(DamageFontSequence sequence)
            => Enumerable.Range(0, sequence.events.Count).Where(i => Source(sequence.events[i]) && Source(sequence.events[i]).Validate() == null)
                .OrderBy(i => sequence.events[i].motionInferred).ThenByDescending(i => sequence.events[i].observedDisappearance)
                .ThenByDescending(i => sequence.events[i].confidence).ThenByDescending(i => Source(sequence.events[i]).Duration).DefaultIfEmpty(-1).First();

        internal static DamageFontPreset Ensure(DamageFontSequence sequence)
            => Get(sequence) ? Get(sequence) : Select(sequence, BestSource(sequence));

        internal static DamageFontPreset Select(DamageFontSequence sequence, int index)
        {
            string path = AssetPath(sequence);
            if (path == null || index < 0 || index >= sequence.events.Count || !Source(sequence.events[index]))
                throw new InvalidOperationException("Save a valid recorded sequence before selecting its game motion.");
            var copy = DamageVideoSampleExporter.MotionOnly(Source(sequence.events[index]));
            try
            {
                var result = Get(sequence);
                if (!result) { result = ScriptableObject.CreateInstance<DamageFontPreset>(); AssetDatabase.CreateAsset(result, path); }
                else Undo.RecordObject(result, "Choose final game motion");
                EditorUtility.CopySerialized(copy, result);
                result.name = "GameMotion"; result.sourceHitIndex = index;
                result.sourceHitName = sequence.events[index].sample ? sequence.events[index].sample.name : "Hit " + (index + 1);
                EditorUtility.SetDirty(result); AssetDatabase.SaveAssetIfDirty(result);
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        internal static string Label(DamageFontSequence sequence, int index)
        {
            var hit = sequence.events[index]; var preset = Source(hit);
            string state = hit.motionInferred ? "Inferred" : hit.observedDisappearance ? "Observed end" : "Cut / unobserved end";
            string name = hit.sample ? hit.sample.name : "Hit " + (index + 1);
            return StudioLanguage.Format("Hit {0} · {1} · {2:0.000}s · {3:0.000}s long · {4}", index + 1, name,
                sequence.sourceStartTime + hit.time, preset ? preset.Duration : 0, StudioLanguage.Text(state));
        }
        internal static void Draw(DamageFontSequence sequence, ref bool showCandidates)
        {
            if (!sequence || sequence.events.Count == 0 || AssetPath(sequence) == null) return;
            GUILayout.Label(StudioLanguage.Text("Final game motion"), EditorStyles.boldLabel);
            var final = Get(sequence);
            if (!final)
            {
                if (GUILayout.Button(StudioLanguage.Text("Create and test GameMotion.asset")))
                { Ensure(sequence); DamageFontStudioWindow.OpenGameResult(sequence); GUIUtility.ExitGUI(); }
                return;
            }
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(StudioLanguage.Text("Apply this file to the game"), final, typeof(DamageFontPreset), false);
            EditorGUILayout.LabelField(StudioLanguage.Text("Preview this exact asset on your existing game prefab. Sample prefabs are analysis only."), EditorStyles.wordWrappedMiniLabel);
            if (final.sourceHitIndex >= 0 && final.sourceHitIndex < sequence.events.Count)
                EditorGUILayout.LabelField(Label(sequence, final.sourceHitIndex), EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button(StudioLanguage.Text("Test final game motion")))
            { DamageFontStudioWindow.OpenGameResult(sequence); GUIUtility.ExitGUI(); }
            showCandidates = EditorGUILayout.Foldout(showCandidates, StudioLanguage.Text("Analysis hits / choose another source"), true);
            if (!showCandidates) return;
            EditorGUILayout.HelpBox(StudioLanguage.Text("Light-01, 02... are separate tracked hits, not versions. Choosing a source copies that hit to GameMotion.asset without averaging."), MessageType.Info);
            for (int i = 0; i < sequence.events.Count; i++)
            {
                EditorGUILayout.LabelField(Label(sequence, i), EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(!Source(sequence.events[i])))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(StudioLanguage.Text("Test this observation")))
                    { DamageFontStudioWindow.OpenPreset(Source(sequence.events[i])); GUIUtility.ExitGUI(); }
                    if (GUILayout.Button(StudioLanguage.Text("Use as final game motion")))
                    { Select(sequence, i); DamageFontStudioWindow.OpenGameResult(sequence); GUIUtility.ExitGUI(); }
                }
            }
        }
        [OnOpenAsset]
        static bool OnOpenAsset(int instanceID, int line)
        {
            var preset = EditorUtility.InstanceIDToObject(instanceID) as DamageFontPreset;
            if (!preset || !preset.animationOnly || Path.GetFileName(AssetDatabase.GetAssetPath(preset)) != FileName) return false;
            DamageFontStudioWindow.OpenPreset(preset); return true;
        }
    }
}
