using UnityEngine;
using UnityEngine.UIElements;
using T = KurisuCompanion.AmadeusTheme;

namespace KurisuCompanion
{
    // 陪伴小窗的面板与机身装饰：半透明的通讯面板 + 从面板上沿探出的不透明头像舱。
    // 面板底色在这里绘制（而不是用 backgroundColor），这样能沿切角轮廓填充，切角外完全透明。
    internal sealed class CompanionFrame : VisualElement
    {
        public const float AvatarLeft = 14, AvatarTop = 8, PanelTop = 36, Chamfer = 14;
        public const float PanelAlpha = .58f;
        private float animationTime;
        private float nextRepaint;

        public CompanionFrame()
        {
            name = "companion-frame";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            generateVisualContent += Paint;
        }

        public void Animate(float now)
        {
            if (now < nextRepaint) return;
            nextRepaint = now + .08f;
            animationTime = now;
            MarkDirtyRepaint();
        }

        // 面板轮廓（不含头像舱），与 CompanionWindow 的窗口区域保持一致。
        public static Vector2[] PanelOutline(float w, float h) => new[]
        {
            new Vector2(AvatarLeft, PanelTop), new Vector2(w - 6, PanelTop), new Vector2(w, PanelTop + 6),
            new Vector2(w, h - Chamfer), new Vector2(w - Chamfer, h), new Vector2(6, h), new Vector2(0, h - 6),
            new Vector2(0, PanelTop + AvatarLeft),
        };

        private void Paint(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 200 || h < 100) return;
            var p = context.painter2D;
            p.lineWidth = 1f;
            float avatarRight = AvatarLeft + CompanionAvatar.BoxWidth, avatarBottom = AvatarTop + CompanionAvatar.BoxHeight;
            float textLeft = avatarRight + 14;

            // 半透明面板与轮廓描边。
            var outline = PanelOutline(w, h);
            Fill(p, new Color(T.Surface.r, T.Surface.g, T.Surface.b, PanelAlpha), outline);
            Polyline(p, new Color(T.Highlight.r, T.Highlight.g, T.Highlight.b, .42f), true, outline);

            // 面板上沿：头像舱右侧一条铁锈红顶带与高光线，左端斜切收口。
            Fill(p, new Color(T.Oxide.r, T.Oxide.g, T.Oxide.b, .9f),
                new Vector2(avatarRight, PanelTop), new Vector2(w - 6, PanelTop), new Vector2(w - 3, PanelTop + 3),
                new Vector2(avatarRight + 4, PanelTop + 3));
            Fill(p, T.Red, new Vector2(avatarRight + 8, PanelTop), new Vector2(avatarRight + 74, PanelTop),
                new Vector2(avatarRight + 70, PanelTop + 3), new Vector2(avatarRight + 12, PanelTop + 3));

            // 对话区域内缓慢移动的低亮度扫描线。
            float sweep = PanelTop + 14 + (h - PanelTop - 40) * (.5f + .5f * Mathf.Sin(animationTime * .42f));
            Line(p, textLeft, sweep, w - 14, sweep, new Color(T.Highlight.r, T.Highlight.g, T.Highlight.b, .06f));

            // 头像舱：细描边 + 外扩的四角括号 + 底部铭牌。
            // 本层在头像之下，描边向外偏 1px 才不会被头像盖住。
            Polyline(p, new Color(T.Highlight.r, T.Highlight.g, T.Highlight.b, .55f), true,
                new Vector2(AvatarLeft - 1, AvatarTop - 1), new Vector2(avatarRight + 1, AvatarTop - 1),
                new Vector2(avatarRight + 1, avatarBottom + 1), new Vector2(AvatarLeft - 1, avatarBottom + 1));
            const float arm = 10, gap = 3;
            p.strokeColor = T.Highlight; p.lineWidth = 1.6f;
            foreach (var (x, y, dx, dy) in new[]
            {
                (AvatarLeft - gap, AvatarTop - gap, 1, 1), (avatarRight + gap, AvatarTop - gap, -1, 1),
                (AvatarLeft - gap, avatarBottom + gap, 1, -1), (avatarRight + gap, avatarBottom + gap, -1, -1),
            })
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x + dx * arm, y)); p.LineTo(new Vector2(x, y)); p.LineTo(new Vector2(x, y + dy * arm));
                p.Stroke();
            }
            p.lineWidth = 1f;
            Fill(p, T.Red, new Vector2(AvatarLeft, avatarBottom + 6), new Vector2(avatarRight, avatarBottom + 6),
                new Vector2(avatarRight - 6, avatarBottom + 19), new Vector2(AvatarLeft, avatarBottom + 19));

            // 头像与对话之间的分隔线。
            Line(p, avatarRight + 7, PanelTop + 16, avatarRight + 7, h - 24, T.Line);

            // 底部：高光线与交替的红色锯齿，呼应主界面下巴。
            Line(p, textLeft, h - 15.5f, w - 22, h - 15.5f, new Color(T.Highlight.r, T.Highlight.g, T.Highlight.b, .5f));
            for (int i = 0; i < 18; i++)
            {
                float x = textLeft + 4 + i * 19;
                if (x > w - 40) break;
                Fill(p, i % 2 == 0 ? T.Red : T.Oxide,
                    new Vector2(x, h - 12), new Vector2(x + 12, h - 12), new Vector2(x + 9, h - 7), new Vector2(x - 3, h - 7));
            }

            // 右侧护轨。
            Line(p, w - 6, PanelTop + 18, w - 6, h - 26, T.Line);
            Fill(p, T.Red, new Vector2(w - 4, h * .5f), new Vector2(w - 1, h * .53f), new Vector2(w - 1, h * .66f), new Vector2(w - 4, h * .69f));
        }

        private static void Fill(Painter2D painter, Color color, params Vector2[] points)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            painter.ClosePath();
            painter.Fill();
        }

        private static void Polyline(Painter2D painter, Color color, bool closed, params Vector2[] points)
        {
            painter.strokeColor = color;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++) painter.LineTo(points[i]);
            if (closed) painter.ClosePath();
            painter.Stroke();
        }

        private static void Line(Painter2D painter, float x1, float y1, float x2, float y2, Color color)
        {
            painter.strokeColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x1, y1));
            painter.LineTo(new Vector2(x2, y2));
            painter.Stroke();
        }
    }
}
