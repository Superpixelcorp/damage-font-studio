using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    [CustomEditor(typeof(DamageFontPreset)), CanEditMultipleObjects]
    public sealed class DamageFontPresetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            StudioLanguage.DrawSelector();
            if (((DamageFontPreset)target).animationOnly)
                EditorGUILayout.HelpBox(StudioLanguage.Text("Animation only: preserves the game's font, material, style and RGB colors."), MessageType.Info);
            if (GUILayout.Button(StudioLanguage.Text("Test this exact preset")))
                DamageFontStudioWindow.OpenPreset((DamageFontPreset)target);
            if (((DamageFontPreset)target).name == "GameMotion")
                EditorGUILayout.HelpBox(StudioLanguage.Text("This is the final game asset. The playback lab uses this exact preset; assign your existing game prefab to verify its appearance."), MessageType.Info);
            serializedObject.Update();
            foreach (string field in new[] { "kind", "font", "fontMaterial", "overrideFontStyle", "fontStyle", "fontSize", "initialColor", "initialOpacity", "initialScale", "initialRotation", "segments" })
                StudioLanguage.Property(serializedObject.FindProperty(field));
            serializedObject.ApplyModifiedProperties();
            var error = ((DamageFontPreset)target).Validate();
            if (error != null) EditorGUILayout.HelpBox(StudioLanguage.Text(error), MessageType.Error);
        }
    }
}
