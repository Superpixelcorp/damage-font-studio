using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageFontStudio
{
    // Captures authored state once; never changes shared materials or child transforms.
    internal sealed class DamageFontTarget
    {
        public readonly GameObject Root;
        readonly TMP_Text valueText;
        readonly TMP_Text[] texts;
        readonly Graphic[] graphics;
        readonly SpriteRenderer[] sprites;
        readonly Color[] textColors, graphicColors, spriteColors;
        readonly Vector3 basePosition, baseScale;
        readonly Quaternion baseRotation;
        readonly TMP_FontAsset font;
        readonly float fontSize;
        readonly Material fontMaterial;
        readonly FontStyles fontStyle;
        readonly string originalText;
        readonly bool overrideAppearance, legacy;
        bool animationOnly;

        public DamageFontTarget(GameObject root, TMP_Text selectedText, bool overrideAppearance, bool legacy = false)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            if (selectedText && selectedText.transform != root.transform && !selectedText.transform.IsChildOf(root.transform))
                throw new ArgumentException("The number text must belong to the animated root.");
            Root = root;
            this.overrideAppearance = overrideAppearance;
            this.legacy = legacy;
            texts = root.GetComponentsInChildren<TMP_Text>(true);
            valueText = selectedText ? selectedText : texts.FirstOrDefault();
            graphics = root.GetComponentsInChildren<Graphic>(true).Where(g => !(g is TMP_Text)).ToArray();
            sprites = root.GetComponentsInChildren<SpriteRenderer>(true);
            textColors = texts.Select(t => t.color).ToArray();
            graphicColors = graphics.Select(g => g.color).ToArray();
            spriteColors = sprites.Select(s => s.color).ToArray();
            basePosition = root.transform.localPosition;
            baseScale = root.transform.localScale;
            baseRotation = root.transform.localRotation;
            if (valueText) { font = valueText.font; fontSize = valueText.fontSize; originalText = valueText.text;
                fontMaterial = valueText.fontSharedMaterial; fontStyle = valueText.fontStyle; }
        }

        public void SetNumber(DamageFontPreset preset, string value)
        {
            animationOnly = preset.animationOnly;
            if (!valueText) return;
            valueText.text = value;
            if (overrideAppearance && !animationOnly)
            {
                valueText.font = preset.font ? preset.font : font;
                valueText.fontSize = preset.fontSize;
                if (preset.fontMaterial) valueText.fontSharedMaterial = preset.fontMaterial;
                if (preset.overrideFontStyle) valueText.fontStyle = preset.fontStyle;
            }
        }

        public void Apply(Vector3 position, Vector3 scale, float rotation, Color tint)
        {
            if (!Root) return;
            if (animationOnly) { tint.r = 1; tint.g = 1; tint.b = 1; }
            Root.transform.localPosition = position;
            Root.transform.localScale = legacy ? scale : Vector3.Scale(baseScale, scale);
            Root.transform.localRotation = (legacy ? Quaternion.identity : baseRotation) * Quaternion.Euler(0, 0, rotation);
            for (int i = 0; i < texts.Length; i++) if (texts[i]) texts[i].color = overrideAppearance && !animationOnly ? tint : textColors[i] * tint;
            for (int i = 0; i < graphics.Length; i++) if (graphics[i]) graphics[i].color = graphicColors[i] * tint;
            for (int i = 0; i < sprites.Length; i++) if (sprites[i]) sprites[i].color = spriteColors[i] * tint;
        }

        public void SetVisible(bool visible)
        {
            if (!Root) return;
            if (Root.activeSelf != visible) Root.SetActive(visible);
            if (visible) foreach (var text in texts) if (text && text.gameObject.activeInHierarchy) text.ForceMeshUpdate();
        }

        public void Reset()
        {
            if (!Root) return;
            Root.transform.localPosition = legacy ? Vector3.zero : basePosition;
            Root.transform.localScale = legacy ? Vector3.one : baseScale;
            Root.transform.localRotation = legacy ? Quaternion.identity : baseRotation;
            for (int i = 0; i < texts.Length; i++) if (texts[i]) texts[i].color = textColors[i];
            for (int i = 0; i < graphics.Length; i++) if (graphics[i]) graphics[i].color = graphicColors[i];
            for (int i = 0; i < sprites.Length; i++) if (sprites[i]) sprites[i].color = spriteColors[i];
            if (valueText) { valueText.text = legacy ? string.Empty : originalText; valueText.font = font; valueText.fontSize = fontSize;
                valueText.fontSharedMaterial = fontMaterial; valueText.fontStyle = fontStyle; }
            SetVisible(false);
        }
    }
}
