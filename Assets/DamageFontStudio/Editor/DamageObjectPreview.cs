using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageFontStudio.Editor
{
    internal static class DamageObjectPreview
    {
        internal static GameObject Clone(GameObject source, PreviewRenderUtility preview)
        {
            // Instantiate under an inactive preview-scene parent before enabling visual components.
            var container = new GameObject("Damage preview container", typeof(RectTransform));
            container.hideFlags = HideFlags.HideAndDontSave;
            container.SetActive(false);
            preview.AddSingleGO(container);
            var clone = Object.Instantiate(source, container.transform, false);
            if (clone.transform is RectTransform cloneRect && source.transform is RectTransform sourceRect)
            {
                // Detach the root's stretch anchors from its former canvas while retaining its size.
                cloneRect.anchorMin = cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
                cloneRect.sizeDelta = sourceRect.rect.size;
            }
            foreach (var transform in clone.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                transform.gameObject.layer = container.layer;
            }
            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true))
            {
                bool visual = behaviour is Canvas || behaviour is CanvasGroup || behaviour is UIBehaviour ||
                    behaviour.GetType().Namespace == "TMPro";
                if (!visual) behaviour.enabled = false;
            }
            foreach (var particle in clone.GetComponentsInChildren<ParticleSystem>(true))
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true)) text.isOrthographic = true;
            bool needsCanvas = clone.GetComponentsInChildren<Graphic>(true).Any(g => !(g is TextMeshPro));
            if (needsCanvas)
            {
                preview.camera.cameraType = CameraType.Game;
                var canvas = container.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = preview.camera;
                ((RectTransform)container.transform).sizeDelta = new Vector2(1200, 600);
            }
            foreach (var canvas in clone.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = preview.camera;
            }
            foreach (var scaler in clone.GetComponentsInChildren<CanvasScaler>(true)) scaler.enabled = false;
            clone.transform.localPosition = Vector3.zero;
            clone.SetActive(true);
            container.SetActive(true);
            Canvas.ForceUpdateCanvases();
            return clone;
        }
    }
}
