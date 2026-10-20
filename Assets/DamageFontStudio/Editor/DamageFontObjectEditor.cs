using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    [CustomEditor(typeof(DamageFontObject))]
    public sealed class DamageFontObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            StudioLanguage.DrawSelector();
            serializedObject.Update();
            foreach (string field in new[] { "preset", "numberText", "overrideTextAppearance", "playOnEnable", "number" })
                StudioLanguage.Property(serializedObject.FindProperty(field));
            serializedObject.ApplyModifiedProperties();
            var binding = (DamageFontObject)target;
            if (!binding.HasValidTextBinding) EditorGUILayout.HelpBox(StudioLanguage.Text("Number Text must be a child of this animated root."), MessageType.Error);
            if (GUILayout.Button(StudioLanguage.Text("Preview in Damage Font Studio"))) DamageFontStudioWindow.OpenFor(binding);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || EditorUtility.IsPersistent(binding)))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(StudioLanguage.Text("Play"))) binding.Play();
                if (GUILayout.Button(StudioLanguage.Text("Cancel"))) binding.Cancel();
            }
        }
    }
}
