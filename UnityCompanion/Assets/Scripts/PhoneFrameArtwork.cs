using System;
using UnityEngine;

namespace KurisuCompanion
{
    // Rasterizes the normal UI's frame geometry once for the startup IMGUI layer.
    internal static class PhoneFrameArtwork
    {
        public static Texture2D Create(int width, int height)
        {
            var p = new Raster(width, height);
            Color iron = PhoneFrameOverlay.Iron, oxide = PhoneFrameOverlay.Oxide;
            Color red = PhoneFrameOverlay.Red, highlight = PhoneFrameOverlay.Highlight;
            p.Rect(0, 0, 12, height, iron);
            p.Rect(width - 12, 0, 12, height, iron);
            p.Rect(2, 0, 5, height, oxide);
            p.Rect(width - 7, 0, 5, height, oxide);
            p.Line(10, 18, 10, height - 13, highlight, 1);
            p.Line(width - 10, 18, width - 10, height - 13, highlight, 1);
            p.Rect(0, 0, width, 17, oxide);
            p.Rect(0, 0, width, 3, red);
            p.Quad(new Vector2(10, 3), new Vector2(82, 3),
                new Vector2(67, 16), new Vector2(10, 16), red);
            p.Quad(new Vector2(width - 82, 3), new Vector2(width - 10, 3),
                new Vector2(width - 10, 16), new Vector2(width - 67, 16), red);
            p.Quad(new Vector2(92, 3), new Vector2(106, 3),
                new Vector2(119, 16), new Vector2(105, 16), highlight);
            p.Quad(new Vector2(width - 106, 3), new Vector2(width - 92, 3),
                new Vector2(width - 105, 16), new Vector2(width - 119, 16), highlight);
            p.Rect(width * .5f - 42, 2, 84, 11, iron);
            p.Rect(width * .5f - 26, 5, 52, 3, new Color(.14f, .13f, .13f));
            p.Line(12, 17, width - 12, 17, highlight, .8f);
            float chin = height - 98;
            p.Rect(0, chin, width, 98, oxide);
            p.Rect(0, chin, width, 4, iron);
            p.Line(0, chin + 4, width, chin + 4, highlight, 1.3f);
            p.Quad(new Vector2(12, chin + 8), new Vector2(width * .34f, chin + 8),
                new Vector2(width * .26f, height - 11), new Vector2(12, height - 11), red);
            p.Quad(new Vector2(width * .66f, chin + 8), new Vector2(width - 12, chin + 8),
                new Vector2(width - 12, height - 11), new Vector2(width * .74f, height - 11), red);
            p.Quad(new Vector2(width * .40f, chin + 7), new Vector2(width * .60f, chin + 7),
                new Vector2(width * .66f, height - 11), new Vector2(width * .34f, height - 11), iron);
            p.Ring(width * .5f, chin + 50, 19, highlight, 1.5f);
            p.Ring(width * .5f, chin + 50, 14, iron, 1.3f);
            p.Rect(0, height - 11, width, 11, iron);
            p.Rect(0, height - 11, width, 2, highlight);
            p.Rect(0, height - 3, width, 3, red);
            p.Ring(26, 27, 3.4f, highlight, .9f);
            p.Ring(width - 26, 27, 3.4f, highlight, .9f);
            p.Rect(width * .5f - 24, height - 7, 48, 2.5f,
                new Color(.61f, .24f, .16f, .8f));
            return p.Finish();
        }

        private sealed class Raster
        {
            private readonly int width, height;
            private readonly Color32[] pixels;
            public Raster(int width, int height)
            {
                this.width = width; this.height = height;
                pixels = new Color32[checked(width * height)];
            }

            public void Rect(float x, float y, float w, float h, Color color)
            {
                for (int row = Math.Max(0, Mathf.FloorToInt(y)); row < Math.Min(height, Mathf.CeilToInt(y + h)); row++)
                    for (int col = Math.Max(0, Mathf.FloorToInt(x)); col < Math.Min(width, Mathf.CeilToInt(x + w)); col++)
                        Blend(col, row, color);
            }

            public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
            {
                Vector2[] points = { a, b, c, d };
                int left = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x, d.x)));
                int right = Mathf.Min(width, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x, d.x)));
                int top = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y, d.y)));
                int bottom = Mathf.Min(height, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y, d.y)));
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                    {
                        Vector2 point = new Vector2(x + .5f, y + .5f);
                        bool positive = false, negative = false;
                        for (int i = 0; i < 4; i++)
                        {
                            Vector2 edge = points[(i + 1) % 4] - points[i];
                            Vector2 offset = point - points[i];
                            float cross = edge.x * offset.y - edge.y * offset.x;
                            positive |= cross > 0; negative |= cross < 0;
                        }
                        if (!(positive && negative)) Blend(x, y, color);
                    }
            }

            public void Line(float x1, float y1, float x2, float y2, Color color, float size)
            {
                float radius = Mathf.Max(.5f, size * .5f);
                int left = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(x1, x2) - radius));
                int right = Mathf.Min(width, Mathf.CeilToInt(Mathf.Max(x1, x2) + radius));
                int top = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(y1, y2) - radius));
                int bottom = Mathf.Min(height, Mathf.CeilToInt(Mathf.Max(y1, y2) + radius));
                Vector2 a = new Vector2(x1, y1), direction = new Vector2(x2 - x1, y2 - y1);
                float lengthSquared = direction.sqrMagnitude;
                for (int y = top; y < bottom; y++)
                    for (int x = left; x < right; x++)
                    {
                        Vector2 point = new Vector2(x + .5f, y + .5f);
                        float t = lengthSquared > 0 ? Mathf.Clamp01(Vector2.Dot(point - a, direction) / lengthSquared) : 0;
                        if ((point - (a + direction * t)).sqrMagnitude <= radius * radius) Blend(x, y, color);
                    }
            }

            public void Ring(float x, float y, float radius, Color color, float size)
            {
                float half = Mathf.Max(.5f, size * .5f), outer = radius + half, inner = radius - half;
                for (int row = Math.Max(0, Mathf.FloorToInt(y - outer)); row < Math.Min(height, Mathf.CeilToInt(y + outer)); row++)
                    for (int col = Math.Max(0, Mathf.FloorToInt(x - outer)); col < Math.Min(width, Mathf.CeilToInt(x + outer)); col++)
                    {
                        float distance = new Vector2(col + .5f - x, row + .5f - y).magnitude;
                        if (distance >= inner && distance <= outer) Blend(col, row, color);
                    }
            }

            private void Blend(int x, int y, Color foreground)
            {
                int index = (height - y - 1) * width + x;
                Color background = pixels[index];
                float alpha = foreground.a + background.a * (1f - foreground.a);
                if (alpha <= 0) return;
                Color mixed = (foreground * foreground.a + background * (background.a * (1f - foreground.a))) / alpha;
                mixed.a = alpha;
                pixels[index] = mixed;
            }

            public Texture2D Finish()
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return texture;
            }
        }
    }
}
