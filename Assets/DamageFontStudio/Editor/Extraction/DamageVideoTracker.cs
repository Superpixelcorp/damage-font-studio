using UnityEngine;

namespace DamageFontStudio.Editor
{
    // Local template matching; confidence is correlation, not a probability of correctness.
    // Rotation and opacity stay manual to avoid treating background changes as animation.
    internal sealed class DamageVideoTracker
    {
        const int Columns = 24, Rows = 12;
        readonly float[] template = new float[Columns * Rows];
        double templateVariance;

        internal void Anchor(Color32[] pixels, int width, int height, DamageVideoSample sample)
        {
            double sum = 0, square = 0;
            for (int i = 0; i < template.Length; i++)
            {
                float v = Read(pixels, width, height, Point(sample, i));
                template[i] = v; sum += v; square += v * v;
            }
            float mean = (float)(sum / template.Length);
            for (int i = 0; i < template.Length; i++) template[i] -= mean;
            templateVariance = square - sum * sum / template.Length;
        }

        internal DamageVideoSample Track(Color32[] pixels, int width, int height, DamageVideoSample previous, int frame, int radius)
        {
            var best = previous.Copy(); best.frame = frame; best.manual = false; best.confidence = 0;
            if (templateVariance < 0.0001) return best;
            float score = -2;
            int stride = Mathf.Max(2, radius / 10);
            var candidate = previous.Copy();
            foreach (float factor in new[] { 0.85f, 0.925f, 1f, 1.075f, 1.15f })
            {
                candidate.size = previous.size * factor;
                for (int y = -radius; y <= radius; y += stride)
                    for (int x = -radius; x <= radius; x += stride)
                    {
                        candidate.center = previous.center + new Vector2(x, y);
                        Consider(pixels, width, height, candidate, best, ref score);
                    }
            }
            Vector2 center = best.center; Vector2 size = best.size;
            foreach (float factor in new[] { 0.975f, 1f, 1.025f })
            {
                candidate.size = size * factor;
                for (int y = -stride; y <= stride; y++)
                    for (int x = -stride; x <= stride; x++)
                    {
                        candidate.center = center + new Vector2(x, y);
                        Consider(pixels, width, height, candidate, best, ref score);
                    }
            }
            best.confidence = Mathf.Clamp01(score);
            return best;
        }

        void Consider(Color32[] pixels, int width, int height, DamageVideoSample candidate, DamageVideoSample best, ref float bestScore)
        {
            double sum = 0, square = 0, cross = 0;
            for (int i = 0; i < template.Length; i++)
            {
                var point = Point(candidate, i);
                if (point.x < 0 || point.y < 0 || point.x >= width || point.y >= height) return;
                float v = Read(pixels, width, height, point);
                sum += v; square += v * v; cross += template[i] * v;
            }
            double variance = square - sum * sum / template.Length;
            if (variance < 0.0001) return;
            float score = (float)(cross / System.Math.Sqrt(templateVariance * variance));
            if (score <= bestScore) return;
            bestScore = score; best.center = candidate.center; best.size = candidate.size;
        }

        static Vector2 Point(DamageVideoSample sample, int i)
        {
            float x = ((i % Columns + 0.5f) / Columns - 0.5f) * sample.size.x;
            float y = ((i / Columns + 0.5f) / Rows - 0.5f) * sample.size.y;
            float angle = sample.angle * Mathf.Deg2Rad;
            return sample.center + new Vector2(x * Mathf.Cos(angle) - y * Mathf.Sin(angle), x * Mathf.Sin(angle) + y * Mathf.Cos(angle));
        }

        // Unity ReadPixels is bottom-up; session coordinates are top-down.
        static float Read(Color32[] pixels, int width, int height, Vector2 point)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(point.x), 0, width - 1);
            int y = height - 1 - Mathf.Clamp(Mathf.RoundToInt(point.y), 0, height - 1);
            var p = pixels[y * width + x];
            return (p.r * 0.299f + p.g * 0.587f + p.b * 0.114f) / 255f;
        }
    }
}
