using System;
using System.IO;
using UnityEngine;

namespace KurisuCompanion
{
    internal static class ImageAttachment
    {
        private const int MaxInputBytes = 25 * 1024 * 1024;
        private const int MaxLongSide = 1280;

        public static string Prepare(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxInputBytes) throw new InvalidOperationException("图片超过 25 MiB，请先压缩。");
            byte[] bytes = File.ReadAllBytes(path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, false))
                    throw new InvalidOperationException("无法解码图片，请选用 PNG 或 JPEG。");
                return PrepareTexture(texture, MaxLongSide);
            }
            finally { UnityEngine.Object.Destroy(texture); }
        }

        public static string PrepareTexture(Texture2D texture, int maxLongSide)
        {
            Texture2D resized = null;
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                int longSide = Math.Max(texture.width, texture.height);
                int width = Math.Max(1, Mathf.RoundToInt(texture.width * Mathf.Min(1f, maxLongSide / (float)longSide)));
                int height = Math.Max(1, Mathf.RoundToInt(texture.height * Mathf.Min(1f, maxLongSide / (float)longSide)));
                temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                resized = new Texture2D(width, height, TextureFormat.RGB24, false);
                resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resized.Apply();
                byte[] jpeg = resized.EncodeToJPG(82);
                return "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (resized != null) UnityEngine.Object.Destroy(resized);
            }
        }
    }
}
