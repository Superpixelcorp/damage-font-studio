using System;
using DG.Tweening;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace DamageFontStudio.Editor
{
    internal static class StudioObjectVerification
    {
        internal static void Run()
        {
            int baseline = DOTween.TotalActiveTweens();
            var root = new GameObject("Object binding check", typeof(RectTransform));
            root.hideFlags = HideFlags.HideAndDontSave;
            var preset = ScriptableObject.CreateInstance<DamageFontPreset>();
            DamageFontPlayback player = null;
            DamageFontStudioWindow window = null;
            string prefabPath = null;
            try
            {
                root.transform.localPosition = new Vector3(10, 20, 0);
                ((RectTransform)root.transform).anchorMin = Vector2.zero;
                ((RectTransform)root.transform).anchorMax = Vector2.one;
                ((RectTransform)root.transform).sizeDelta = new Vector2(260, 90);
                root.transform.localScale = new Vector3(1.2f, 0.8f, 1);
                root.transform.localRotation = Quaternion.Euler(0, 0, 12);
                var authoredPosition = root.transform.localPosition;
                var authoredScale = root.transform.localScale;
                var authoredRotation = root.transform.localRotation;
                var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
                background.transform.SetParent(root.transform, false);
                var graphic = background.GetComponent<Image>();
                graphic.rectTransform.sizeDelta = new Vector2(260, 90);
                graphic.color = new Color(0.2f, 0.3f, 0.5f, 0.8f);
                var graphicColor = graphic.color;
                var value = new GameObject("Number", typeof(RectTransform));
                value.transform.SetParent(root.transform, false);
                var text = value.AddComponent<TextMeshProUGUI>();
                text.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
                text.fontSize = 54;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.rectTransform.sizeDelta = new Vector2(260, 90);
                text.rectTransform.localPosition = new Vector3(8, 3, 0);
                text.text = "original";
                var childPosition = text.transform.localPosition;
                var binding = root.AddComponent<DamageFontObject>();
                binding.numberText = text;
                binding.preset = AssetDatabase.LoadAssetAtPath<DamageFontPreset>("Assets/DamageFontStudio/Presets/FallingArc.asset");
                binding.playOnEnable = true; // Preview must disable behaviour before activation.
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Position, duration = 1, targetVector = new Vector3(100, -80, 0), positionPath = DamagePositionPath.Parabola, arcHeight = 100, ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Opacity, duration = 1, targetValue = 0, ease = Ease.Linear });
                preset.segments.Add(new DamageSegment { channel = DamageChannel.Scale, duration = 1, targetVector = Vector3.one * 2, ease = Ease.Linear });
                player = new DamageFontPlayback(root, text);
                player.Prepare(preset, "789", authoredPosition, manual: true);
                player.Seek(0.5f);
                Check(Vector3.Distance(root.transform.localPosition, authoredPosition + new Vector3(50, 60, 0)) < 0.01f, "Root follows parabola");
                Check(Vector3.Distance(root.transform.localScale, authoredScale * 1.5f) < 0.01f, "Scale multiplies authored root scale");
                Check(Quaternion.Angle(root.transform.localRotation, authoredRotation) < 0.01f, "Authored rotation preserved");
                Check(text.transform.localPosition == childPosition, "Child layout unchanged");
                Check(text.text == "789" && text.fontSize == 54, "Number binding preserves font style");
                Check(Mathf.Abs(graphic.color.a - 0.4f) < 0.01f && Mathf.Abs(text.color.a - 0.5f) < 0.01f, "Hierarchy opacity applied once");
                player.Cancel();
                Check(!root.activeSelf && text.text == "original" && graphic.color == graphicColor, "Cancel restores authored visuals and hides root");
                Check(root.transform.localPosition == authoredPosition && root.transform.localScale == authoredScale, "Cancel restores root pose");
                for (int i = 0; i < 20; i++)
                {
                    player.Prepare(preset, "456", authoredPosition, manual: true);
                    player.Seek(0.5f);
                    Check(Mathf.Abs(graphic.color.a - 0.4f) < 0.01f, "Reuse does not accumulate opacity");
                    player.Cancel();
                }
                player.Dispose();
                player = null;
                root.SetActive(true);
                var preview = new PreviewRenderUtility();
                try
                {
                    var clone = DamageObjectPreview.Clone(root, preview);
                    Check(Vector2.Distance(((RectTransform)clone.transform).rect.size, ((RectTransform)root.transform).rect.size) < 0.01f, "Stretched UI root retains authored dimensions");
                    Check(!clone.GetComponent<DamageFontObject>().enabled, "Clone scripts disabled");
                }
                finally { preview.Cleanup(); }
                window = ScriptableObject.CreateInstance<DamageFontStudioWindow>();
                window.VerifyRendering("Logs/FontStudioPreview-scene-object.png", false, root);
                Check(root.activeSelf && text.text == "original" && root.transform.localPosition == authoredPosition, "Scene preview never mutates source");
                Check(binding.enabled && binding.playOnEnable, "Source binding untouched");
                prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/DamageFontStudio/Presets/ObjectVerification.prefab");
                root.hideFlags = HideFlags.None;
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                root.hideFlags = HideFlags.HideAndDontSave;
                Check(prefab, "Prefab saved");
                window.VerifyRendering("Logs/FontStudioPreview-prefab.png", true, prefab);
                Check(prefab.GetComponent<DamageFontObject>().numberText.text == "original", "Prefab source unmodified");

                // A renderer-only object has no text dependency.
                var spriteRoot = new GameObject("Sprite-only check", typeof(SpriteRenderer));
                spriteRoot.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    var sprite = spriteRoot.GetComponent<SpriteRenderer>();
                    sprite.color = new Color(0.5f, 1, 0.3f, 0.8f);
                    using (var spritePlayer = new DamageFontPlayback(spriteRoot))
                    {
                        spritePlayer.Prepare(preset, "unused", Vector3.zero, manual: true);
                        spritePlayer.Seek(0.5f);
                        Check(Mathf.Abs(sprite.color.a - 0.4f) < 0.01f, "Sprite opacity supported without TMP");
                        Check(spriteRoot.transform.localPosition.x > 0, "Object without text moves");
                    }
                    Check(Mathf.Abs(sprite.color.a - 0.8f) < 0.01f, "Sprite color restored");
                }
                finally { UnityEngine.Object.DestroyImmediate(spriteRoot); }
                Check(DOTween.TotalActiveTweens() == baseline, "Object previews release sequences");
                Debug.Log("Damage Font Studio: object/prefab binding checks passed.");
            }
            finally
            {
                player?.Dispose();
                if (window) UnityEngine.Object.DestroyImmediate(window);
                if (prefabPath != null) AssetDatabase.DeleteAsset(prefabPath);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new Exception("Object binding check failed: " + label);
        }
    }
}
