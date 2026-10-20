using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    // 全程序共用的视觉语言：黑底、铁锈红机身、橙红高光线、青绿信号色，与手机外壳和电路背景保持一致。
    internal static class AmadeusTheme
    {
        public static readonly Color Void = new Color(.02f, .016f, .016f);
        public static readonly Color Surface = new Color(.045f, .032f, .03f, .975f);
        public static readonly Color Raised = new Color(.085f, .055f, .05f);
        public static readonly Color FieldFill = new Color(.03f, .022f, .02f);
        public static readonly Color Iron = PhoneFrameOverlay.Iron;
        public static readonly Color Oxide = PhoneFrameOverlay.Oxide;
        public static readonly Color Red = new Color(.56f, .13f, .09f);
        public static readonly Color Highlight = new Color(.86f, .36f, .22f);
        public static readonly Color Line = new Color(.86f, .36f, .22f, .28f);
        public static readonly Color Signal = new Color(.43f, .86f, .70f);
        public static readonly Color Text = new Color(.95f, .92f, .89f);
        public static readonly Color Muted = new Color(.60f, .55f, .52f);

        // 覆盖在手机屏幕区域内的全屏面板（设置、记录、审批），四周露出机身外框。
        public static VisualElement Sheet(string name, string title, string code, Action close)
        {
            var sheet = new VisualElement { name = name };
            sheet.style.position = Position.Absolute;
            sheet.style.left = sheet.style.right = 12;
            sheet.style.top = 18; sheet.style.bottom = 11;
            sheet.style.backgroundColor = Surface;
            sheet.style.paddingLeft = sheet.style.paddingRight = 22;
            sheet.style.paddingTop = 14; sheet.style.paddingBottom = 14;
            sheet.style.display = DisplayStyle.None;

            var header = new VisualElement { name = name + "-header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.FlexEnd;
            header.style.flexShrink = 0;
            header.style.paddingBottom = 10;
            sheet.Add(header);
            var titles = new VisualElement();
            titles.style.flexGrow = 1;
            header.Add(titles);
            titles.Add(Caps("AMADEUS  //  " + code, Highlight));
            var heading = TextLabel(title, 22, Text, true);
            heading.name = name + "-title";
            heading.style.marginTop = 3;
            titles.Add(heading);
            if (close != null)
            {
                var closeButton = Ghost("×", close);
                closeButton.name = name + "-close";
                closeButton.tooltip = "关闭";
                closeButton.style.width = 34; closeButton.style.height = 30;
                closeButton.style.fontSize = 18;
                closeButton.style.marginRight = 0;
                header.Add(closeButton);
            }
            sheet.Add(Rule(true));
            return sheet;
        }

        // 分区标题："01  对话 ··········· CONVERSATION"
        public static VisualElement Section(string index, string title, string english)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 22; row.style.marginBottom = 10;
            var number = TextLabel(index, 11, Highlight, true);
            number.style.width = 24;
            row.Add(number);
            row.Add(TextLabel(title, 15, Text, true));
            var line = new VisualElement();
            line.style.flexGrow = 1; line.style.height = 1;
            line.style.marginLeft = line.style.marginRight = 10;
            line.style.backgroundColor = Line;
            row.Add(line);
            row.Add(Caps(english, Muted));
            return row;
        }

        // 分隔线；accent 为真时左端带一段红色标签，呼应机身顶部的斜角红板。
        public static VisualElement Rule(bool accent = false)
        {
            var rule = new VisualElement();
            rule.style.height = 1; rule.style.flexShrink = 0;
            rule.style.backgroundColor = Line;
            if (accent)
            {
                var tab = new VisualElement();
                tab.style.position = Position.Absolute;
                tab.style.left = 0; tab.style.top = -1;
                tab.style.width = 46; tab.style.height = 3;
                tab.style.backgroundColor = Highlight;
                rule.Add(tab);
            }
            return rule;
        }

        public static Label TextLabel(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = label.style.marginRight = 0;
            label.style.paddingLeft = label.style.paddingRight = 0;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        // 小号大写英文标识，字距拉开，作为装饰性副标题。
        public static Label Caps(string text, Color color)
        {
            var label = TextLabel(text, 10, color);
            label.style.letterSpacing = 2;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            return label;
        }

        public static Button Primary(string text, Action action)
        {
            var button = Ghost(text, action);
            button.style.backgroundColor = Red;
            SetBorder(button, Highlight);
            Hover(button, Red, Highlight);
            return button;
        }

        public static Button Ghost(string text, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.height = 32;
            button.style.paddingLeft = button.style.paddingRight = 14;
            button.style.paddingTop = button.style.paddingBottom = 0;
            button.style.marginLeft = 0; button.style.marginRight = 8;
            button.style.marginTop = button.style.marginBottom = 0;
            button.style.fontSize = 13;
            button.style.color = Text;
            button.style.backgroundColor = Color.clear;
            SetRadius(button, 1);
            SetBorder(button, Line);
            Hover(button, Color.clear, new Color(Highlight.r, Highlight.g, Highlight.b, .14f));
            return button;
        }

        public static VisualElement Row(float marginTop = 10)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = marginTop;
            return row;
        }

        // 标签在上、输入框在下的字段；placeholder 承担原来的说明文字。
        public static TextField Field(VisualElement parent, string label, string value, string placeholder = null, bool password = false)
        {
            var caption = TextLabel(label, 12, Muted);
            caption.style.marginTop = 10; caption.style.marginBottom = 4;
            parent.Add(caption);
            var field = new TextField { value = value ?? "", isPasswordField = password };
            field.style.marginLeft = field.style.marginRight = 0;
            field.style.marginTop = field.style.marginBottom = 0;
            if (!string.IsNullOrEmpty(placeholder))
            {
                field.textEdition.placeholder = placeholder;
                field.textEdition.hidePlaceholderOnFocus = true;
            }
            var input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = FieldFill;
                // 内联颜色会盖过占位文字的默认灰色，所以空值时手动改用弱化色。
                void Tint(string text) => input.style.color = string.IsNullOrEmpty(text) ? Muted : Text;
                Tint(field.value);
                field.RegisterValueChangedCallback(evt => Tint(evt.newValue));
                input.style.fontSize = 13;
                input.style.minHeight = 32;
                input.style.paddingLeft = input.style.paddingRight = 10;
                SetRadius(input, 1);
                SetBorder(input, Line);
                input.style.borderBottomColor = new Color(Highlight.r, Highlight.g, Highlight.b, .55f);
                field.RegisterCallback<FocusInEvent>(_ => SetBorder(input, Highlight));
                field.RegisterCallback<FocusOutEvent>(_ =>
                {
                    SetBorder(input, Line);
                    input.style.borderBottomColor = new Color(Highlight.r, Highlight.g, Highlight.b, .55f);
                });
            }
            parent.Add(field);
            return field;
        }

        // 一行开关：左侧文字，右侧滑块。替换默认勾选框，开启时滑块移到右侧并点亮。
        public static Toggle Switch(string label, bool value)
        {
            var toggle = new Toggle(label) { value = value };
            toggle.style.marginLeft = toggle.style.marginRight = 0;
            toggle.style.marginTop = 6;
            toggle.style.minHeight = 30;
            toggle.style.alignItems = Align.Center;
            toggle.labelElement.style.color = Text;
            toggle.labelElement.style.fontSize = 14;
            toggle.labelElement.style.flexGrow = 1;
            toggle.labelElement.style.minWidth = 0;
            toggle.labelElement.style.marginLeft = 0;
            toggle.style.paddingRight = 44;
            var check = toggle.Q(className: "unity-toggle__checkmark");
            if (check != null) check.style.display = DisplayStyle.None;

            // 绝对定位在行尾，不依赖 Toggle 内部输入框的布局。
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.position = Position.Absolute;
            track.style.right = 0; track.style.top = 6;
            track.style.width = 36; track.style.height = 18;
            track.style.backgroundColor = FieldFill;
            SetRadius(track, 1);
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.style.position = Position.Absolute;
            knob.style.top = 3; knob.style.width = 12; knob.style.height = 10;
            track.Add(knob);
            toggle.hierarchy.Add(track);
            bool painted = !value;
            void Paint()
            {
                if (painted == toggle.value) return;
                painted = toggle.value;
                knob.style.left = painted ? 20 : 3;
                knob.style.backgroundColor = painted ? Highlight : Muted;
                SetBorder(track, painted ? Highlight : Line);
            }
            Paint();
            // SetValueWithoutNotify 不触发事件，因此轮询同步外观，开销可以忽略。
            track.schedule.Execute(Paint).Every(100);
            return toggle;
        }

        // 细长的橙红滚动条，隐藏两端箭头。
        public static ScrollView Scroll()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            var scroller = scroll.verticalScroller;
            scroller.style.width = 5;
            scroller.style.borderLeftWidth = 0;
            scroller.style.backgroundColor = Color.clear;
            scroller.lowButton.style.display = DisplayStyle.None;
            scroller.highButton.style.display = DisplayStyle.None;
            var tracker = scroller.Q(className: "unity-base-slider__tracker");
            if (tracker != null) { tracker.style.backgroundColor = new Color(1, 1, 1, .04f); SetBorder(tracker, Color.clear); }
            var dragger = scroller.Q(className: "unity-base-slider__dragger");
            if (dragger != null) { dragger.style.backgroundColor = Line; SetBorder(dragger, Color.clear); SetRadius(dragger, 2); }
            scroll.contentContainer.style.paddingRight = 10;
            return scroll;
        }

        // 带左侧色条的内容块：记录条目、代码块、提示。
        public static VisualElement Card(Color bar)
        {
            var card = new VisualElement();
            card.style.backgroundColor = Raised;
            card.style.borderLeftWidth = 2;
            card.style.borderLeftColor = bar;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.paddingTop = 9; card.style.paddingBottom = 10;
            return card;
        }

        public static void SetBorder(VisualElement element, Color color)
        {
            element.style.borderTopWidth = element.style.borderRightWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = 1;
            element.style.borderTopColor = element.style.borderRightColor = element.style.borderBottomColor = element.style.borderLeftColor = color;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }

        private static void Hover(Button button, Color normal, Color hover)
        {
            button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = hover);
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = normal);
        }
    }
}
