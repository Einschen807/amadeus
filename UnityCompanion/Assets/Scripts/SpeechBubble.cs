using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    internal sealed class SpeechBubble : VisualElement
    {
        private readonly Label caption;
        private float hideAt = -1f;

        public Label Caption => caption;
        public bool Visible => style.display.value == DisplayStyle.Flex;

        public SpeechBubble()
        {
            name = "speech-bubble";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 24;
            style.right = 70;
            style.bottom = 122;
            style.minHeight = 104;
            style.paddingLeft = 18;
            style.paddingRight = 48;
            style.paddingTop = 15;
            style.paddingBottom = 17;
            style.display = DisplayStyle.None;
            generateVisualContent += Paint;

            caption = new Label("") { name = "sentence-dialogue", pickingMode = PickingMode.Ignore };
            caption.style.fontSize = 18;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = new Color(.08f, .09f, .09f);
            caption.style.whiteSpace = WhiteSpace.Normal;
            Add(caption);
        }

        public void Show(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
            hideAt = -1f;
            caption.text = text;
            style.display = DisplayStyle.Flex;
        }

        public void HideAfter(float seconds)
        {
            if (Visible) hideAt = Time.unscaledTime + seconds;
        }

        public void Tick()
        {
            if (hideAt >= 0f && Time.unscaledTime >= hideAt) Hide();
        }

        public void Hide()
        {
            hideAt = -1f;
            caption.text = "";
            style.display = DisplayStyle.None;
        }

        private void Paint(MeshGenerationContext context)
        {
            float width = contentRect.width, height = contentRect.height;
            if (width < 100 || height < 50) return;
            float body = width - 28;
            var p = context.painter2D;
            p.fillColor = new Color(.97f, .985f, .98f, .90f);
            p.BeginPath();
            p.MoveTo(new Vector2(9, 0));
            p.LineTo(new Vector2(body - 9, 0));
            p.LineTo(new Vector2(body, 9));
            p.LineTo(new Vector2(body, height * .43f));
            p.LineTo(new Vector2(width, height * .50f));
            p.LineTo(new Vector2(body, height * .62f));
            p.LineTo(new Vector2(body, height - 9));
            p.LineTo(new Vector2(body - 9, height));
            p.LineTo(new Vector2(9, height));
            p.LineTo(new Vector2(0, height - 9));
            p.LineTo(new Vector2(0, 9));
            p.ClosePath(); p.Fill();

            // Pale circles and construction lines sit under the text like the original UI motif.
            var ink = new Color(.27f, .44f, .45f, .16f);
            Circle(p, new Vector2(77, height - 18), 42, ink);
            Circle(p, new Vector2(77, height - 18), 24, ink);
            Circle(p, new Vector2(body * .52f, height - 10), 55, ink);
            Circle(p, new Vector2(body * .52f, height - 10), 29, ink);
            Circle(p, new Vector2(body - 35, height - 25), 25, ink);
            Line(p, new Vector2(35, height - 46), new Vector2(112, height - 2), ink);
            Line(p, new Vector2(28, height - 9), new Vector2(116, height - 44), ink);
            Line(p, new Vector2(body * .52f - 52, height - 11),
                new Vector2(body * .52f + 48, height - 42), ink);
            Line(p, new Vector2(body * .52f - 28, height - 52),
                new Vector2(body * .52f + 31, height - 1), ink);
        }

        private static void Circle(Painter2D p, Vector2 center, float radius, Color color)
        {
            p.strokeColor = color; p.lineWidth = .8f;
            p.BeginPath(); p.Arc(center, radius, 0, 360); p.Stroke();
        }

        private static void Line(Painter2D p, Vector2 from, Vector2 to, Color color)
        {
            p.strokeColor = color; p.lineWidth = .8f;
            p.BeginPath(); p.MoveTo(from); p.LineTo(to); p.Stroke();
        }
    }
}
