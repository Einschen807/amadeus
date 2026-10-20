using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    // Fixed user-confirmed framing. Only window resizing and mouth animation change rendering.
    internal sealed class PortraitViewport : VisualElement
    {
        private readonly Image portrait;
        private readonly Texture2D closedMouth, openMouth;
        private Texture2D dynamicTexture;
        private readonly float aspect;
        private readonly PortraitPose pose = PortraitPose.ConfirmedDefault;
        private Vector2 offset;
        public bool MouthOpen { get; private set; }
        public float Zoom => pose.zoom;
        public Vector2 Offset => offset;

        public PortraitViewport(Texture2D texture)
        {
            closedMouth = texture;
            openMouth = Resources.Load<Texture2D>("kurisu-mouth-open");
            name = "portrait-viewport";
            aspect = texture != null ? (float)texture.height / texture.width : 1f;
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            style.overflow = Overflow.Hidden;
            // The imported static texture is resized to a different aspect ratio by Unity.
            // Fill the same fixed rectangle for both textures so the E-mote handoff cannot shift the portrait.
            portrait = new Image { image = texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            portrait.style.position = Position.Absolute;
            Add(portrait);
            RegisterCallback<GeometryChangedEvent>(evt => {
                if (evt.newRect.size != evt.oldRect.size) LayoutPortrait();
            });
        }

        public void SetMouth(bool open)
        {
            MouthOpen = open;
            if (dynamicTexture == null) portrait.image = open && openMouth != null ? openMouth : closedMouth;
        }

        public void SetDynamicTexture(Texture2D texture)
        {
            dynamicTexture = texture;
            portrait.image = texture != null ? texture : MouthOpen && openMouth != null ? openMouth : closedMouth;
            portrait.MarkDirtyRepaint();
        }

        public void RefreshDynamicFrame()
        {
            if (dynamicTexture != null) portrait.MarkDirtyRepaint();
        }

        private void LayoutPortrait()
        {
            float width = contentRect.width, height = contentRect.height;
            if (width <= 0 || height <= 0 || float.IsNaN(width) || float.IsNaN(height)) return;
            float portraitWidth = width * 1.65f * pose.zoom;
            offset = new Vector2(width * pose.offsetX, height * pose.offsetY);
            portrait.style.width = portraitWidth;
            portrait.style.height = portraitWidth * aspect;
            portrait.style.left = (width - portraitWidth) * .5f + offset.x;
            portrait.style.top = 8f + offset.y;
        }
    }
}
