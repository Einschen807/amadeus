using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    // Decorative hardware sits over the stage without taking layout space or pointer input.
    internal sealed class PhoneFrameOverlay : VisualElement
    {
        internal static readonly Color Iron = new Color(.055f, .045f, .045f, .97f);
        internal static readonly Color Oxide = new Color(.30f, .075f, .055f, .95f);
        internal static readonly Color Red = new Color(.48f, .11f, .075f, .97f);
        internal static readonly Color Highlight = new Color(.78f, .29f, .16f, .76f);

        public PhoneFrameOverlay()
        {
            name = "phone-frame-overlay";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            generateVisualContent += Paint;
        }

        private void Paint(MeshGenerationContext context)
        {
            float width = contentRect.width, height = contentRect.height;
            if (width < 180 || height < 220) return;
            var p = context.painter2D;

            // Narrow side rails preserve the portrait and the dialogue layout.
            FillRect(p, 0, 0, 12, height, Iron);
            FillRect(p, width - 12, 0, 12, height, Iron);
            FillRect(p, 2, 0, 5, height, Oxide);
            FillRect(p, width - 7, 0, 5, height, Oxide);
            Line(p, 10, 18, 10, height - 13, Highlight, 1);
            Line(p, width - 10, 18, width - 10, height - 13, Highlight, 1);

            // Red upper housing, angular corner plates, and a recessed receiver.
            FillRect(p, 0, 0, width, 17, Oxide);
            FillRect(p, 0, 0, width, 3, Red);
            FillQuad(p, new Vector2(10, 3), new Vector2(82, 3),
                new Vector2(67, 16), new Vector2(10, 16), Red);
            FillQuad(p, new Vector2(width - 82, 3), new Vector2(width - 10, 3),
                new Vector2(width - 10, 16), new Vector2(width - 67, 16), Red);
            FillQuad(p, new Vector2(92, 3), new Vector2(106, 3),
                new Vector2(119, 16), new Vector2(105, 16), Highlight);
            FillQuad(p, new Vector2(width - 106, 3), new Vector2(width - 92, 3),
                new Vector2(width - 105, 16), new Vector2(width - 119, 16), Highlight);
            FillRect(p, width * .5f - 42, 2, 84, 11, Iron);
            FillRect(p, width * .5f - 26, 5, 52, 3, new Color(.14f, .13f, .13f, 1));
            Line(p, 12, 17, width - 12, 17, Highlight, .8f);

            // A complete lower shell covers the portrait edge rather than leaving a cutout.
            float chin = height - 98;
            FillRect(p, 0, chin, width, 98, Oxide);
            FillRect(p, 0, chin, width, 4, Iron);
            Line(p, 0, chin + 4, width, chin + 4, Highlight, 1.3f);
            FillQuad(p, new Vector2(12, chin + 8), new Vector2(width * .34f, chin + 8),
                new Vector2(width * .26f, height - 11), new Vector2(12, height - 11), Red);
            FillQuad(p, new Vector2(width * .66f, chin + 8), new Vector2(width - 12, chin + 8),
                new Vector2(width - 12, height - 11), new Vector2(width * .74f, height - 11), Red);
            FillQuad(p, new Vector2(width * .40f, chin + 7), new Vector2(width * .60f, chin + 7),
                new Vector2(width * .66f, height - 11), new Vector2(width * .34f, height - 11), Iron);
            Ring(p, width * .5f, chin + 50, 19f, Highlight, 1.5f);
            Ring(p, width * .5f, chin + 50, 14f, Iron, 1.3f);
            FillRect(p, 0, height - 11, width, 11, Iron);
            FillRect(p, 0, height - 11, width, 2, Highlight);
            FillRect(p, 0, height - 3, width, 3, Red);

            // Small hardware details echo the reference without covering controls.
            Ring(p, 26, 27, 3.4f, Highlight, .9f);
            Ring(p, width - 26, 27, 3.4f, Highlight, .9f);
            FillRect(p, width * .5f - 24, height - 7, 48, 2.5f,
                new Color(.61f, .24f, .16f, .8f));
        }

        private static void FillRect(Painter2D p, float x, float y, float width, float height, Color color)
        {
            FillQuad(p, new Vector2(x, y), new Vector2(x + width, y),
                new Vector2(x + width, y + height), new Vector2(x, y + height), color);
        }

        private static void FillQuad(Painter2D p, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.MoveTo(a); p.LineTo(b); p.LineTo(c); p.LineTo(d);
            p.ClosePath(); p.Fill();
        }

        private static void Line(Painter2D p, float x1, float y1, float x2, float y2, Color color, float size)
        {
            p.strokeColor = color;
            p.lineWidth = size;
            p.BeginPath(); p.MoveTo(new Vector2(x1, y1)); p.LineTo(new Vector2(x2, y2)); p.Stroke();
        }

        private static void Ring(Painter2D p, float x, float y, float radius, Color color, float size)
        {
            p.strokeColor = color;
            p.lineWidth = size;
            p.BeginPath(); p.Arc(new Vector2(x, y), radius, 0, 360); p.Stroke();
        }
    }
}
