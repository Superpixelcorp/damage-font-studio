using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    [CustomEditor(typeof(DamageFontSequence))]
    public sealed class DamageFontSequenceEditor : UnityEditor.Editor
    {
        bool showFinalCandidates;
        public override void OnInspectorGUI()
        {
            StudioLanguage.DrawSelector();
            DamageVideoFinalMotion.Draw((DamageFontSequence)target, ref showFinalCandidates);
            if (GUILayout.Button(StudioLanguage.Text("Play RecordedHits in Studio"))) DamageFontStudioWindow.OpenSequence((DamageFontSequence)target);
            if (GUILayout.Button(StudioLanguage.Text("Compare recorded hits"))) DamageVideoExtractionWindow.OpenRecorded((DamageFontSequence)target);
            DrawDefaultInspector();
        }
    }
}
