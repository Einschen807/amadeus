using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    [Serializable]
    internal sealed class CompanionWindowSettings
    {
        public bool enabled;
        public int x = int.MinValue, y = int.MinValue;
    }

    internal sealed class CompanionWindow
    {
        // 布局尺寸（界面逻辑单位）。面板按 96 DPI 等比缩放，窗口实际像素 = 逻辑尺寸 × UiScale。
        public const int Width = 520, Height = 200;
        public static float UiScale => Screen.dpi > 0 ? Screen.dpi / 96f : 1f;
        public static int PixelWidth => Mathf.RoundToInt(Width * UiScale);
        public static int PixelHeight => Mathf.RoundToInt(Height * UiScale);
        private const uint NoActivate = 0x0010, ShowWindow = 0x0040;
        private const uint WmNcLButtonDown = 0x00A1;
        private static readonly IntPtr Topmost = new IntPtr(-1), NotTopmost = new IntPtr(-2);
        private readonly UIDocument document;
        private readonly PhoneWindowChrome chrome;
        private readonly EmotePortraitRenderer renderer;
        private readonly SpeechPipeline pipeline;
        private readonly Action requestReturn;
        private readonly List<KeyValuePair<VisualElement, StyleEnum<Visibility>>> hidden = new List<KeyValuePair<VisualElement, StyleEnum<Visibility>>>();
        private readonly VisualElement view;
        private readonly CompanionAvatar avatar;
        private readonly CompanionBubble bubble;
        private readonly CompanionFrame frame;
        private IntPtr window;
        private NativeRect normalBounds;
        private StyleColor normalBackground;
        private int lastFrame = -1;
        private float nextPositionCheck;
        public CompanionWindowSettings Settings { get; private set; }
        public bool Active { get; private set; }
        public bool Transparent { get; private set; }
        private static string SettingsPath => Path.Combine(Application.persistentDataPath, "companion-mode.json");

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int left, top, right, bottom; }
        private delegate bool EnumProc(IntPtr handle, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr handle, IntPtr region, bool redraw);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr handle, IntPtr region);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct Margins { public int left, right, top, bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr handle, ref Margins margins);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePolygonRgn(NativePoint[] points, int count, int fillMode);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        private const uint SpiGetWorkArea = 0x0030;
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, ref NativeRect area, uint flags);

        public CompanionWindow(UIDocument document, PhoneWindowChrome chrome, EmotePortraitRenderer renderer,
            SpeechPipeline pipeline, Action onReturn)
        {
            this.document = document; this.chrome = chrome; this.renderer = renderer;
            this.pipeline = pipeline; requestReturn = onReturn;
            try { Settings = JsonUtility.FromJson<CompanionWindowSettings>(File.ReadAllText(SettingsPath)); }
            catch { Settings = null; }
            Settings = Settings ?? new CompanionWindowSettings();
            view = new VisualElement { name = "companion-window" };
            view.style.position = Position.Absolute;
            view.style.left = view.style.right = view.style.top = view.style.bottom = 0;
            // 背景完全透明：半透明面板由 CompanionFrame 沿切角轮廓绘制，头像舱不透明。
            view.style.backgroundColor = Color.clear;
            view.style.overflow = Overflow.Hidden;
            frame = new CompanionFrame();
            view.Add(frame);
            avatar = new CompanionAvatar();
            avatar.style.left = CompanionFrame.AvatarLeft;
            avatar.style.top = CompanionFrame.AvatarTop;
            view.Add(avatar);
            var plate = AmadeusTheme.Caps("MAKISE KURISU", AmadeusTheme.Text);
            plate.style.position = Position.Absolute;
            plate.style.left = CompanionFrame.AvatarLeft + 6;
            plate.style.top = CompanionFrame.AvatarTop + CompanionAvatar.BoxHeight + 6;
            plate.style.fontSize = 8; plate.style.letterSpacing = 1.5f;
            view.Add(plate);
            bubble = new CompanionBubble();
            bubble.style.position = Position.Absolute;
            bubble.style.left = CompanionFrame.AvatarLeft + CompanionAvatar.BoxWidth + 16;
            bubble.style.right = 18;
            bubble.style.top = CompanionFrame.PanelTop + 8;
            bubble.style.bottom = 22;
            view.Add(bubble);
            var link = AmadeusTheme.Caps("AMADEUS // LINK", AmadeusTheme.Text);
            link.name = "companion-link";
            link.style.position = Position.Absolute;
            link.style.right = 38; link.style.top = CompanionFrame.PanelTop + 9;
            link.style.fontSize = 8;
            link.style.opacity = .55f;
            view.Add(link);
            var exit = new Button(requestReturn) { name = "companion-return", text = "↩", tooltip = "返回普通模式" };
            exit.style.position = Position.Absolute;
            exit.style.right = 12; exit.style.top = CompanionFrame.PanelTop + 6;
            exit.style.width = 18; exit.style.height = 17;
            exit.style.fontSize = 12;
            exit.style.opacity = .7f;
            exit.style.backgroundColor = Color.clear;
            exit.style.color = AmadeusTheme.Text;
            exit.style.borderTopWidth = exit.style.borderBottomWidth = exit.style.borderLeftWidth = exit.style.borderRightWidth = 0;
            exit.style.paddingTop = exit.style.paddingBottom = exit.style.paddingLeft = exit.style.paddingRight = 0;
            exit.RegisterCallback<PointerEnterEvent>(_ => exit.style.opacity = 1f);
            exit.RegisterCallback<PointerLeaveEvent>(_ => exit.style.opacity = .7f);
            view.Add(exit);
            view.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || exit.Contains(evt.target as VisualElement) || window == IntPtr.Zero) return;
                ReleaseCapture();
                SendMessage(window, WmNcLButtonDown, new IntPtr(2), IntPtr.Zero);
            });
        }

        public bool Enter()
        {
            if (Active) return true;
            var root = document.rootVisualElement;
            normalBackground = root.style.backgroundColor;
            root.style.backgroundColor = Color.clear;
            hidden.Clear();
            foreach (var child in root.Children())
            {
                hidden.Add(new KeyValuePair<VisualElement, StyleEnum<Visibility>>(child, child.style.visibility));
                child.style.visibility = Visibility.Hidden;
            }
            root.Add(view);
            chrome.CompanionActive = true;
            Active = true;
            if (Application.platform == RuntimePlatform.WindowsPlayer)
            {
                window = FindWindow();
                if (window == IntPtr.Zero) { Exit(); return false; }
                GetWindowRect(window, out normalBounds);
                int x = Settings.x == int.MinValue ? Math.Max(0, GetSystemMetrics(0) - PixelWidth - 24) : Settings.x;
                int y = Settings.y == int.MinValue ? Math.Max(0, GetSystemMetrics(1) - PixelHeight - 90) : Settings.y;
                // 保存的位置可能来自不同尺寸或缩放，夹回主屏幕工作区（不含任务栏）。
                var work = new NativeRect { right = GetSystemMetrics(0), bottom = GetSystemMetrics(1) };
                SystemParametersInfo(SpiGetWorkArea, 0, ref work, 0);
                x = Mathf.Clamp(x, work.left, Math.Max(work.left, work.right - PixelWidth));
                y = Mathf.Clamp(y, work.top, Math.Max(work.top, work.bottom - PixelHeight));
                if (!SetWindowPos(window, Topmost, x, y, PixelWidth, PixelHeight, NoActivate | ShowWindow))
                { Exit(); return false; }
                if (!ApplyWindowShape(window)) { Exit(); return false; }
                // 让 DWM 按像素 alpha 合成：面板半透明处露出桌面。失败时退化为不透明深色面板，不影响使用。
                var glass = new Margins { left = -1, right = -1, top = -1, bottom = -1 };
                Transparent = DwmExtendFrameIntoClientArea(window, ref glass) == 0;
            }
            Settings.enabled = true;
            Save();
            return true;
        }

        public void Exit()
        {
            if (!Active) return;
            SavePosition();
            Active = false;
            view.RemoveFromHierarchy();
            document.rootVisualElement.style.backgroundColor = normalBackground;
            foreach (var item in hidden) item.Key.style.visibility = item.Value;
            hidden.Clear();
            if (window != IntPtr.Zero)
            {
                var solid = new Margins();
                DwmExtendFrameIntoClientArea(window, ref solid);
                Transparent = false;
                SetWindowRgn(window, IntPtr.Zero, true);
                SetWindowPos(window, NotTopmost, normalBounds.left, normalBounds.top,
                    normalBounds.right - normalBounds.left, normalBounds.bottom - normalBounds.top,
                    NoActivate | ShowWindow);
                window = IntPtr.Zero;
            }
            chrome.CompanionActive = false;
            chrome.HideTitleBar();
            Settings.enabled = false;
            Save();
        }

        public void Tick()
        {
            if (!Active) return;
            bubble.SetAudioLevel(pipeline != null ? pipeline.OutputLevel : 0f, Time.unscaledTime);
            frame.Animate(Time.unscaledTime);
            if (renderer != null && renderer.Frames != lastFrame)
            {
                lastFrame = renderer.Frames;
                avatar.Show(renderer.CurrentTexture);
            }
            if (Time.unscaledTime >= nextPositionCheck)
            {
                nextPositionCheck = Time.unscaledTime + .5f;
                SavePosition();
            }
        }

        public void Show(string text) { bubble.Show(text); }
        public string LastText => bubble.Text;
        public bool NativeCornersClipped
        {
            get
            {
                if (window == IntPtr.Zero) return false;
                IntPtr region = CreateRectRgn(0, 0, 0, 0);
                if (region == IntPtr.Zero) return false;
                try
                {
                    return GetWindowRgn(window, region) > 1 &&
                        !PtInRegion(region, 0, 0) &&
                        !PtInRegion(region, PixelWidth - 1, 0) &&
                        !PtInRegion(region, 0, PixelHeight - 1) &&
                        !PtInRegion(region, PixelWidth - 1, PixelHeight - 1) &&
                        PtInRegion(region, PixelWidth / 2, PixelHeight / 2);
                }
                finally { DeleteObject(region); }
            }
        }
        public RectInt Bounds
        {
            get
            {
                if (window != IntPtr.Zero && GetWindowRect(window, out var rect))
                    return new RectInt(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                return new RectInt();
            }
        }

        public void SavePosition()
        {
            if (window == IntPtr.Zero || !GetWindowRect(window, out var rect)) return;
            if (Settings.x == rect.left && Settings.y == rect.top) return;
            Settings.x = rect.left; Settings.y = rect.top;
            Save();
        }

        private void Save()
        {
            try { Directory.CreateDirectory(Application.persistentDataPath); File.WriteAllText(SettingsPath, JsonUtility.ToJson(Settings, true)); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("COMPANION_SETTINGS: " + error.Message); }
        }

        // 切角轮廓：左上与右下大切角，其余两角小切角，与 CompanionFrame 的描边一致。
        private static bool ApplyWindowShape(IntPtr handle)
        {
            // 窗口区域 = 头像舱（含四角括号与铭牌）∪ 切角面板；区域外的点击会穿透到桌面。
            // 坐标按界面逻辑单位给出，再乘以 DPI 缩放换算成窗口像素。
            float c = CompanionFrame.Chamfer, s = 6, top = CompanionFrame.PanelTop;
            float bayLeft = CompanionFrame.AvatarLeft - 3, bayRight = CompanionFrame.AvatarLeft + CompanionAvatar.BoxWidth + 3;
            float bayTop = CompanionFrame.AvatarTop - 3;
            var outline = new[]
            {
                new Vector2(bayLeft, bayTop), new Vector2(bayRight, bayTop),
                new Vector2(bayRight, top), new Vector2(Width - s, top), new Vector2(Width, top + s),
                new Vector2(Width, Height - c), new Vector2(Width - c, Height),
                new Vector2(s, Height), new Vector2(0, Height - s),
                new Vector2(0, top + CompanionFrame.AvatarLeft), new Vector2(bayLeft, top + 3),
            };
            float scale = UiScale;
            var points = Array.ConvertAll(outline, p => new NativePoint { x = Mathf.RoundToInt(p.x * scale), y = Mathf.RoundToInt(p.y * scale) });
            IntPtr region = CreatePolygonRgn(points, points.Length, 1);
            if (region == IntPtr.Zero) return false;
            if (SetWindowRgn(handle, region, true) != 0) return true;
            DeleteObject(region); // Windows owns the region only after SetWindowRgn succeeds.
            return false;
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
