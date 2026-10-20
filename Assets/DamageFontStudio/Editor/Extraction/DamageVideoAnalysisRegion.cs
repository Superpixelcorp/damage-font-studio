using System;
using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Source pixels, top-left origin. No crop-relative coordinates escape this class.
    internal sealed class DamageVideoAnalysisRegion
    {
        internal Rect Bounds { get; }
        internal DamageVideoAnalysisRegion(Rect requested, int width, int height)
        {
            if (!float.IsFinite(requested.x) || !float.IsFinite(requested.y) || !float.IsFinite(requested.width) || !float.IsFinite(requested.height) || requested.width <= 0 || requested.height <= 0)
                throw new ArgumentException("Select an analysis region at least 4 × 4 pixels inside the video.");
            int left = Mathf.Clamp(Mathf.CeilToInt(requested.xMin), 0, width);
            int top = Mathf.Clamp(Mathf.CeilToInt(requested.yMin), 0, height);
            int right = Mathf.Clamp(Mathf.FloorToInt(requested.xMax), 0, width);
            int bottom = Mathf.Clamp(Mathf.FloorToInt(requested.yMax), 0, height);
            if (right - left < 4 || bottom - top < 4)
                throw new ArgumentException("Select an analysis region at least 4 × 4 pixels inside the video.");
            Bounds = Rect.MinMaxRect(left, top, right, bottom);
        }
        internal static bool Contains(Rect? region, Vector2 center, Vector2 size)
            => !region.HasValue || (center.x - size.x / 2 > region.Value.xMin && center.y - size.y / 2 > region.Value.yMin &&
                center.x + size.x / 2 < region.Value.xMax && center.y + size.y / 2 < region.Value.yMax);

        internal Color32[] Apply(Color32[] source, int width, int height)
        {
            if (source.Length != width * height) throw new ArgumentException("Invalid frame pixels.");
            // Signal descriptors cache features by pixel-array identity. Each frame
            // needs a fresh array so changing the ROI image cannot reuse stale maps.
            var masked = new Color32[source.Length];
            for (int y = (int)Bounds.yMin; y < Bounds.yMax; y++)
            {
                int index = (height - 1 - y) * width + (int)Bounds.xMin;
                Array.Copy(source, index, masked, index, (int)Bounds.width);
            }
            return masked;
        }
        internal static Vector2 SourcePoint(Vector2 point, Rect preview, int width, int height)
            => new Vector2(Mathf.Clamp01((point.x - preview.x) / preview.width) * width,
                Mathf.Clamp01((point.y - preview.y) / preview.height) * height);
        internal static Rect DragRect(Vector2 a, Vector2 b)
            => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
}
