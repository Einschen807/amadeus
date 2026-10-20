using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace KurisuCompanion
{
    // The phone UI supplies drag/minimize/close controls for a fixed-size native window.
    internal sealed class PhoneWindowChrome : MonoBehaviour
    {
        public const int ClientWidth = 620, ClientHeight = 923;
        private const int GwlStyle = -16, GwlExStyle = -20;
        private const int WsCaption = 0x00C00000, WsThickFrame = 0x00040000, WsMaximizeBox = 0x00010000;
        private const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;
        private const uint WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 2, SwMinimize = 6;
        private IntPtr window;
        private float nextCheck;

        public bool Hidden { get; private set; }
        public bool Locked { get; private set; }
        public bool CompanionActive { get; set; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }
        private delegate bool EnumProc(IntPtr handle, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr handle, int index, int value);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll")] private static extern bool AdjustWindowRectEx(ref Rect rect, int style, bool menu, int extendedStyle);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);

        private void Update()
        {
            if (CompanionActive || Application.platform != RuntimePlatform.WindowsPlayer || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + .5f;
            HideTitleBar();
        }

        public void HideTitleBar()
        {
            if (CompanionActive) return;
            if (Application.platform != RuntimePlatform.WindowsPlayer) return;
            if (window == IntPtr.Zero) window = FindWindow();
            if (window == IntPtr.Zero) return;
            int style = GetWindowLong(window, GwlStyle);
            if (!GetWindowRect(window, out var outer) || !GetClientRect(window, out var client)) return;
            int newStyle = style & ~(WsCaption | WsThickFrame | WsMaximizeBox);
            bool styleChanged = newStyle != style;
            if (!styleChanged && client.Right == ClientWidth && client.Bottom == ClientHeight)
            {
                Hidden = Locked = true;
                return;
            }
            var bounds = new Rect { Right = ClientWidth, Bottom = ClientHeight };
            if (!AdjustWindowRectEx(ref bounds, newStyle, false, GetWindowLong(window, GwlExStyle))) return;
            if (styleChanged) SetWindowLong(window, GwlStyle, newStyle);
            SetWindowPos(window, IntPtr.Zero, outer.Left, outer.Top,
                bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            Hidden = (GetWindowLong(window, GwlStyle) & WsCaption) == 0;
            Locked = Hidden && (GetWindowLong(window, GwlStyle) & (WsThickFrame | WsMaximizeBox)) == 0 &&
                GetClientRect(window, out var actual) && actual.Right == ClientWidth && actual.Bottom == ClientHeight;
        }

        public void BeginDrag()
        {
            if (window == IntPtr.Zero) return;
            ReleaseCapture();
            SendMessage(window, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
        }

        public void Minimize()
        {
            if (window != IntPtr.Zero) ShowWindow(window, SwMinimize);
        }

        private static IntPtr FindWindow()
        {
            IntPtr found = IntPtr.Zero;
            uint current = (uint)Process.GetCurrentProcess().Id;
            EnumWindows((handle, state) =>
            {
                GetWindowThreadProcessId(handle, out uint owner);
                if (owner != current) return true;
                var name = new StringBuilder(128);
                GetClassName(handle, name, name.Capacity);
                if (name.ToString() != "UnityWndClass") return true;
                found = handle;
                return false;
            }, IntPtr.Zero);
            return found;
        }
    }
}
