using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    [CustomPropertyDrawer(typeof(DamageSegment))]
    public sealed class DamageSegmentDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (property.isExpanded)
                foreach (var field in VisibleFields(property))
                    height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(field, true);
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var channel = property.FindPropertyRelative("channel");
            string title = $"{StudioLanguage.EnumText(channel.enumNames[channel.enumValueIndex])} · {property.FindPropertyRelative("start").floatValue:0.00}s + {property.FindPropertyRelative("duration").floatValue:0.00}s";
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, title, true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (var field in VisibleFields(property)) Draw(ref row, field);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        static void Draw(ref Rect row, SerializedProperty property)
        {
            // Vector3 uses two lines in a narrow Inspector. Reserve its actual height
            // both here and in GetPropertyHeight so the next field/array element clears it.
            row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
            row.height = EditorGUI.GetPropertyHeight(property, true);
            StudioLanguage.Property(row, property);
        }

        static IEnumerable<SerializedProperty> VisibleFields(SerializedProperty property)
        {
            yield return property.FindPropertyRelative("channel");
            yield return property.FindPropertyRelative("start");
            yield return property.FindPropertyRelative("duration");
            var kind = (DamageChannel)property.FindPropertyRelative("channel").enumValueIndex;
            string target = kind == DamageChannel.Position || kind == DamageChannel.Scale ? "targetVector" : kind == DamageChannel.Color ? "targetColor" : "targetValue";
            yield return property.FindPropertyRelative(target);
            if (kind == DamageChannel.Position)
            {
                yield return property.FindPropertyRelative("positionPath");
                if ((DamagePositionPath)property.FindPropertyRelative("positionPath").enumValueIndex == DamagePositionPath.Parabola)
                    yield return property.FindPropertyRelative("arcHeight");
            }
            yield return property.FindPropertyRelative("useCurve");
            yield return property.FindPropertyRelative(property.FindPropertyRelative("useCurve").boolValue ? "curve" : "ease");
        }
    }
}
