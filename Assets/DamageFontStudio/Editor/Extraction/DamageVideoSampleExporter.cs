using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DamageFontStudio.Editor
{
    internal static class DamageVideoSampleExporter
    {
        internal static GameObject Save(DamageVideoSession session, string folder, string name, DamageVideoMotionProfile motion = null)
        {
            if (session.ValidateExport() != null) throw new InvalidOperationException(session.ValidateExport());
            if (!session.font) session.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (!session.font) throw new InvalidOperationException("Choose a TMP font or import TMP Essential Resources.");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject(name, typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(root, scene);
                var text = root.AddComponent<TextMeshPro>();
                text.font = session.font; text.fontSize = 100; text.fontStyle = FontStyles.Bold | FontStyles.Italic;
                text.enableAutoSizing = false;
                text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
                text.richText = false; text.isOrthographic = true; text.text = session.number;
                text.rectTransform.sizeDelta = new Vector2(2000, 300);
                text.ForceMeshUpdate();
                // textBounds includes the SDF sampling padding. Calibrating to it shrinks
                // the visible digits by about 30%; use glyph ink metrics instead.
                var ink = InkSize(text);
                session.calibratedFontSize = (motion != null ? motion.initialHeight : session.samples[0].size.y) * 100 / Mathf.Max(1, ink.y);
                session.calibratedWidthScale = motion != null ? 1 : session.samples[0].size.x * 100 / Mathf.Max(1, ink.x * session.calibratedFontSize);
                var material = new Material(session.font.material) { name = name + " Outline" };
                material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.08f, .04f, .06f, 1));
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, .18f);
                material.SetFloat(ShaderUtilities.ID_FaceDilate, .08f);
                material.EnableKeyword("OUTLINE_ON");
                string materialPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + "-Outline.mat");
                AssetDatabase.CreateAsset(material, materialPath); session.fontMaterial = material;
                var preset = session.BuildPreset(); preset.kind = session.color.g < .6f ? DamageKind.Critical : DamageKind.Normal;
                if (motion != null) { motion.Apply(preset); session.sharedMotion = true; session.sharedMotionDuration = motion.duration; }
                preset.name = name;
                AssetDatabase.CreateAsset(preset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset"));
                var motionPreset = MotionOnly(preset);
                motionPreset.name = name + "-Motion";
                AssetDatabase.CreateAsset(motionPreset, folder + "/" + motionPreset.name + ".asset");
                text.fontSize = preset.fontSize; text.fontSharedMaterial = material;
                text.color = Color.white; // The preset tint is applied exactly once by shared playback.
                var binding = root.AddComponent<DamageFontObject>();
                binding.preset = preset; binding.numberText = text; binding.overrideTextAppearance = true; binding.number = session.number;
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".prefab");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.CreateAsset(session, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + "-Session.asset"));
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // Game integration uses this asset on the game's existing damage prefab.
        // Glyph-fitting width and sample appearance are preview-only data.
        internal static DamageFontPreset MotionOnly(DamageFontPreset samplePreset)
        {
            var result = UnityEngine.Object.Instantiate(samplePreset);
            result.animationOnly = true;
            var appearanceScale = samplePreset.animationOnly ? Vector3.one : result.initialScale;
            result.font = null; result.fontMaterial = null;
            result.overrideFontStyle = false; result.fontStyle = FontStyles.Normal; result.fontSize = 64;
            result.initialColor = Color.white;
            result.segments.RemoveAll(s => s.channel == DamageChannel.Color);
            if (!samplePreset.animationOnly) result.initialScale = Vector3.one;
            foreach (var segment in result.segments)
                if (segment.channel == DamageChannel.Scale)
                    segment.targetVector = new Vector3(segment.targetVector.x / Mathf.Max(.000001f, appearanceScale.x),
                        segment.targetVector.y / Mathf.Max(.000001f, appearanceScale.y), segment.targetVector.z / Mathf.Max(.000001f, appearanceScale.z));
            return result;
        }

        internal static Vector2 InkSize(TMP_Text text)
        {
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int i = 0; i < text.textInfo.characterCount; i++)
            {
                var c = text.textInfo.characterInfo[i]; if (!c.isVisible || c.textElement == null) continue;
                var m = c.textElement.glyph.metrics;
                var top = new Vector2(c.origin + m.horizontalBearingX * c.scale, c.baseLine + m.horizontalBearingY * c.scale);
                float slope = (c.topLeft.x - c.bottomLeft.x) / Mathf.Max(.001f, c.topLeft.y - c.bottomLeft.y);
                var bottom = top + new Vector2(0, -m.height * c.scale);
                top.x += slope * (top.y - c.baseLine); bottom.x += slope * (bottom.y - c.baseLine);
                min = Vector2.Min(min, Vector2.Min(top, bottom));
                max = Vector2.Max(max, Vector2.Max(top, bottom) + new Vector2(m.width * c.scale, 0));
            }
            return float.IsFinite(min.x) ? max - min : (Vector2)text.textBounds.size;
        }
    }
}
