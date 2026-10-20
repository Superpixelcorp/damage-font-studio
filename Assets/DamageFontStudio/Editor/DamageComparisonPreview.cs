using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Each tile owns a separate preview scene and render target: effects cannot
    // spill into another tile, even when a preset travels outside its viewport.
    internal sealed class DamageComparisonPreview : IDisposable
    {
        readonly List<DamageFontPlayback> players = new List<DamageFontPlayback>();
        readonly List<GameObject> roots = new List<GameObject>();
        readonly float interval, effectDuration;
        PreviewRenderUtility preview;
        public readonly DamageFontPreset Preset;
        public string Error { get; private set; }
        public float Duration { get; private set; }
        public bool Ready => preview != null && players.Count > 0;
        internal IReadOnlyList<GameObject> Roots => roots;

        public DamageComparisonPreview(DamageFontPreset preset, GameObject source, bool overrideAppearance,
            string number, int count, float interval, Vector2 spawn, Vector2 spread, int seed)
        {
            Preset = preset;
            this.interval = interval;
            if (!preset) { Error = "Choose a motion preset to preview."; return; }
            Error = preset.Validate();
            if (Error != null) return;
            if (source && source.TryGetComponent<DamageFontObject>(out var sourceBinding) && !sourceBinding.HasValidTextBinding)
            { Error = "Number Text must belong to the selected object's hierarchy."; return; }
            var font = preset.font ? preset.font : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (!source && !font) { Error = "Choose a TMP font or import TMP Essential Resources."; return; }
            effectDuration = preset.Duration;
            Duration = effectDuration + (count - 1) * interval;
            try
            {
                preview = new PreviewRenderUtility();
                preview.camera.orthographic = true;
                preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 2000;
                preview.camera.transform.position = new Vector3(0, 0, -1000);
                preview.camera.transform.rotation = Quaternion.identity;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.allowHDR = false;
                var random = new System.Random(seed);
                for (int i = 0; i < count; i++)
                {
                    GameObject go;
                    DamageFontPlayback player;
                    if (source)
                    {
                        go = DamageObjectPreview.Clone(source, preview);
                        var binding = go.GetComponent<DamageFontObject>();
                        player = new DamageFontPlayback(go, binding ? binding.numberText : null, overrideAppearance);
                    }
                    else
                    {
                        go = new GameObject("Comparison damage " + i, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
                        preview.AddSingleGO(go);
                        var text = go.AddComponent<TextMeshPro>();
                        text.font = font; text.fontSize = preset.animationOnly ? 64 : preset.fontSize;
                        text.color = Color.white; text.enableAutoSizing = false;
                        text.isOrthographic = true; text.alignment = TextAlignmentOptions.Center;
                        text.textWrappingMode = TextWrappingModes.NoWrap; text.richText = false;
                        text.rectTransform.sizeDelta = new Vector2(1200, 400);
                        player = new DamageFontPlayback(text);
                    }
                    roots.Add(go); players.Add(player);
                    Vector3 position = new Vector3(spawn.x + ((float)random.NextDouble() * 2 - 1) * spread.x,
                        spawn.y + ((float)random.NextDouble() * 2 - 1) * spread.y, -i * .01f);
                    player.Prepare(preset, number, position, manual: true);
                }
            }
            catch (Exception ex) { Dispose(); Error = ex.Message; }
        }

        public void Seek(float time)
        {
            if (!Ready) return;
            for (int i = 0; i < players.Count; i++)
                players[i].Seek(time >= Duration ? effectDuration : time - i * interval);
        }

        public Texture Render(Rect rect, bool light, float zoom, Vector2 viewSize)
        {
            if (!Ready || rect.width < 1 || rect.height < 1) return null;
            preview.camera.backgroundColor = light ? new Color(.86f,.88f,.91f) : new Color(.035f,.045f,.065f);
            float aspect = rect.width / rect.height;
            preview.camera.orthographicSize = Mathf.Max(viewSize.y * .5f, viewSize.x * .5f / aspect) / Mathf.Max(.01f, zoom);
            Canvas.ForceUpdateCanvases();
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true);
            return preview.EndPreview();
        }

        public void Dispose()
        {
            foreach (var player in players) player.Dispose();
            players.Clear(); roots.Clear();
            preview?.Cleanup(); preview = null;
        }
    }
}
