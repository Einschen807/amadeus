using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    internal sealed class CompanionBubble : VisualElement
    {
        private readonly Label caption;
        private readonly CompanionWaveform waveform;
        public string Text => caption.text;

        public CompanionBubble()
        {
            name = "companion-bubble";
            pickingMode = PickingMode.Ignore;
            style.justifyContent = Justify.FlexStart;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = Color.clear;
            // 面板半透明后桌面内容会透出来，文字加一层暗色投影保证可读。
            var shadow = new TextShadow { offset = new Vector2(0, 1), blurRadius = 3, color = new Color(0, 0, 0, .85f) };
            var speakerRow = new VisualElement { name = "companion-speaker-row" };
            speakerRow.style.flexDirection = FlexDirection.Row;
            speakerRow.style.alignItems = Align.Center;
            speakerRow.style.marginBottom = 8;
            var speaker = new Label("牧濑 红莉栖") { name = "companion-speaker" };
            speaker.style.color = AmadeusTheme.Highlight;
            speaker.style.fontSize = 11;
            speaker.style.unityFontStyleAndWeight = FontStyle.Bold;
            speaker.style.letterSpacing = 1;
            speaker.style.textShadow = shadow;
            speakerRow.Add(speaker);
            waveform = new CompanionWaveform();
            speakerRow.Add(waveform);
            Add(speakerRow);
            caption = new Label("我在这里。") { name = "companion-caption" };
            caption.style.color = AmadeusTheme.Text;
            caption.style.fontSize = 15;
            caption.style.textShadow = shadow;
            caption.style.marginLeft = caption.style.paddingLeft = 0;
            caption.style.whiteSpace = WhiteSpace.Normal;
            caption.style.unityTextAlign = TextAnchor.UpperLeft;
            caption.style.flexShrink = 1;
            Add(caption);
        }

        public void Show(string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) caption.text = text.Trim();
        }

        public void SetAudioLevel(float level, float now) { waveform.SetLevel(level, now); }
    }

    // The bars are driven by the RMS of the PCM clip currently playing through SpeechPipeline.
    internal sealed class CompanionWaveform : VisualElement
    {
        private float level;
        private float phase;
        private float lastTime;

        public CompanionWaveform()
        {
            name = "companion-audio-waveform";
            pickingMode = PickingMode.Ignore;
            style.width = 82;
            style.height = 16;
            style.marginLeft = 9;
            generateVisualContent += Paint;
        }

        public void SetLevel(float rms, float now)
        {
            float target = Mathf.Clamp01(rms * 7f);
            float elapsed = lastTime > 0f ? Mathf.Clamp(now - lastTime, 0f, .1f) : .016f;
            lastTime = now;
            level = Mathf.Lerp(level, target,
                1f - Mathf.Exp(-elapsed / (target > level ? .055f : .17f)));
            phase = now;
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext context)
        {
            var p = context.painter2D;
            float center = contentRect.height * .5f;
            p.lineWidth = 1.5f;
            for (int i = 0; i < 17; i++)
            {
                float x = 2f + i * 4.6f;
                float shape = .35f + .65f * Mathf.Abs(Mathf.Sin(i * 1.21f + phase * 13f));
                float envelope = .45f + .55f * Mathf.Sin((i + .5f) / 17f * Mathf.PI);
                float halfHeight = .75f + level * 6.1f * shape * envelope;
                var signal = AmadeusTheme.Signal;
                p.strokeColor = new Color(signal.r, signal.g, signal.b, .35f + level * .55f);
                p.BeginPath();
                p.MoveTo(new Vector2(x, center - halfHeight));
                p.LineTo(new Vector2(x, center + halfHeight));
                p.Stroke();
            }
        }
    }
}
