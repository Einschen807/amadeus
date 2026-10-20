using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    // 与普通界面共用同一张实时 E-mote 画面，裁出头肩部分。
    // 裁剪按显示框的宽高比计算，避免拉伸；底色不透明，保证头像在半透明小窗里始终完整清晰。
    internal sealed class CompanionAvatar : VisualElement
    {
        public const float BoxWidth = 120, BoxHeight = 142;
        // 立绘的视觉宽高比（E-mote 帧 1120×2080，与静态立绘原图一致），与纹理导入后的像素尺寸无关。
        private const float PortraitAspect = 2080f / 1120f;
        private const float CropX = .335f, CropY = .006f, CropWidth = .38f;
        private readonly Image image;
        private readonly Texture2D fallback;
        private Texture2D displayed;

        public CompanionAvatar()
        {
            name = "companion-avatar";
            style.position = Position.Absolute;
            style.width = BoxWidth;
            style.height = BoxHeight;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(.07f, .05f, .048f, 1f);
            fallback = Resources.Load<Texture2D>("kurisu");
            image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.style.width = Length.Percent(100);
            image.style.height = Length.Percent(100);
            Add(image);
            Show(fallback);
        }

        public void Show(Texture2D texture)
        {
            texture = texture != null ? texture : fallback;
            if (texture == null) return;
            if (displayed != texture)
            {
                displayed = texture;
                image.image = texture;
                // 归一化裁剪高度 = 裁剪宽度 ÷（显示框宽高比 × 立绘高宽比），保证显示比例不变形。
                float cropHeight = CropWidth / (BoxWidth / BoxHeight * PortraitAspect);
                // Image.sourceRect 以纹理左上角为原点，单位为像素。
                image.sourceRect = new Rect(texture.width * CropX, texture.height * CropY,
                    texture.width * CropWidth, texture.height * cropHeight);
            }
            image.MarkDirtyRepaint();
        }
    }
}
