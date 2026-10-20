using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace KurisuCompanion
{
    internal sealed class ScreenFrame
    {
        public readonly int Width, Height;
        public readonly byte[] Bgra;
        public ScreenFrame(int width, int height, byte[] bgra) { Width = width; Height = height; Bgra = bgra; }
    }

    internal static class ScreenCaptureService
    {
        private const int Srccopy = 0x00CC0020;
        private const int CaptureBlt = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint size;
            public int width;
            public int height;
            public ushort planes;
            public ushort bitCount;
            public uint compression;
            public uint imageSize;
            public int xPixelsPerMeter;
            public int yPixelsPerMeter;
            public uint colorsUsed;
            public uint colorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo { public BitmapInfoHeader header; public uint color0; }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr destination, int dx, int dy, int width, int height,
            IntPtr source, int sx, int sy, int operation);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool StretchBlt(IntPtr destination, int dx, int dy, int width, int height,
            IntPtr source, int sx, int sy, int sourceWidth, int sourceHeight, int operation);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap,
            uint firstLine, uint lineCount, byte[] pixels, ref BitmapInfo info, uint usage);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);

        // Only Win32 calls and managed arrays: safe to run on a worker thread.
        public static ScreenFrame CaptureScaled(int maxLongSide)
        {
            int sourceWidth = GetSystemMetrics(0), sourceHeight = GetSystemMetrics(1);
            if (sourceWidth <= 0 || sourceHeight <= 0 || (long)sourceWidth * sourceHeight > 33000000)
                throw new InvalidOperationException("主屏幕尺寸异常，无法抓取。");
            float scale = Math.Min(1f, maxLongSide / (float)Math.Max(sourceWidth, sourceHeight));
            int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                screen = GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) throw new InvalidOperationException("无法读取当前桌面。");
                memory = CreateCompatibleDC(screen);
                bitmap = CreateCompatibleBitmap(screen, width, height);
                if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
                    throw new InvalidOperationException("无法创建缩略屏幕图像。");
                old = SelectObject(memory, bitmap);
                SetStretchBltMode(memory, 4); // HALFTONE
                if (!StretchBlt(memory, 0, 0, width, height, screen, 0, 0, sourceWidth, sourceHeight, Srccopy | CaptureBlt))
                    throw new InvalidOperationException("Windows 无法缩小屏幕画面。");
                var info = new BitmapInfo { header = new BitmapInfoHeader
                {
                    size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)), width = width, height = height,
                    planes = 1, bitCount = 32, compression = 0
                } };
                byte[] pixels = new byte[width * height * 4];
                if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0) == 0)
                    throw new InvalidOperationException("无法读取缩略屏幕像素。");
                return new ScreenFrame(width, height, pixels);
            }
            finally
            {
                if (old != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, old);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }

        public static string CapturePrimaryScreen()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer &&
                Application.platform != RuntimePlatform.WindowsEditor)
                throw new PlatformNotSupportedException("当前只支持 Windows 屏幕抓取。");
            int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
            if (width <= 0 || height <= 0 || (long)width * height > 33000000)
                throw new InvalidOperationException("主屏幕尺寸异常，无法抓取。");
            IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
            Texture2D texture = null;
            try
            {
                screen = GetDC(IntPtr.Zero);
                memory = CreateCompatibleDC(screen);
                bitmap = CreateCompatibleBitmap(screen, width, height);
                if (screen == IntPtr.Zero || memory == IntPtr.Zero || bitmap == IntPtr.Zero)
                    throw new InvalidOperationException("无法创建设备截图。");
                old = SelectObject(memory, bitmap);
                if (!BitBlt(memory, 0, 0, width, height, screen, 0, 0, Srccopy | CaptureBlt))
                    throw new InvalidOperationException("Windows 无法复制主屏幕画面。");
                var info = new BitmapInfo { header = new BitmapInfoHeader
                {
                    size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)), width = width, height = height,
                    planes = 1, bitCount = 32, compression = 0
                } };
                byte[] pixels = new byte[width * height * 4];
                if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref info, 0) == 0)
                    throw new InvalidOperationException("无法读取屏幕像素。");
                texture = new Texture2D(width, height, TextureFormat.BGRA32, false);
                texture.LoadRawTextureData(pixels);
                texture.Apply();
                return ImageAttachment.PrepareTexture(texture, 2048);
            }
            finally
            {
                if (texture != null) UnityEngine.Object.Destroy(texture);
                if (old != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, old);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
