using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace KurisuCompanion
{
    // Samples locally at 2 FPS. A single newest candidate is kept; no screenshot is queued.
    internal sealed class ScreenObserver : MonoBehaviour
    {
        private static readonly Guid DisplayStateGuid = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);
        [DllImport("Powrprof.dll")] private static extern uint PowerSettingRegisterNotification(
            ref Guid settingGuid, uint flags, IntPtr recipient, out IntPtr registration);
        [DllImport("Powrprof.dll")] private static extern uint PowerSettingUnregisterNotification(IntPtr registration);
        [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder buffer, int bytes, out int needed);

        private Func<bool> canAnalyze;
        private Func<RectInt> windowBounds;
        private Action<ScreenFrame> candidate;
        private Task<ScreenFrame> capture;
        private CancellationTokenSource cancellation;
        private PowerCallback powerCallback;
        private IntPtr powerRegistration;
        private volatile int displayState = 1;
        private ScreenFrame previous;
        private bool largeCapture, changed, desktopAvailable;
        private int primaryWidth, primaryHeight;
        private float nextSample, nextCandidate, nextDesktopCheck, nextWarning;
        public bool Active { get; private set; }
        public bool DesktopAvailable => Active && desktopAvailable;
        public int SampleCount { get; private set; }

        public void Configure(Func<bool> ready, Func<RectInt> ownWindow, Action<ScreenFrame> onCandidate)
        {
            canAnalyze = ready; windowBounds = ownWindow; candidate = onCandidate;
        }

        public void Begin()
        {
            End();
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
            Active = true;
            RegisterDisplayPower();
            SampleCount = 0;
            cancellation = new CancellationTokenSource();
            primaryWidth = Screen.currentResolution.width;
            primaryHeight = Screen.currentResolution.height;
            nextSample = nextCandidate = nextDesktopCheck = Time.unscaledTime;
        }

        public void End()
        {
            Active = false;
            if (powerRegistration != IntPtr.Zero)
            {
                PowerSettingUnregisterNotification(powerRegistration);
                powerRegistration = IntPtr.Zero;
            }
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            capture = null;
            previous = null;
            desktopAvailable = false;
            changed = largeCapture = false;
        }

        private void Update()
        {
            if (!Active) return;
            if (Time.unscaledTime >= nextDesktopCheck)
            {
                nextDesktopCheck = Time.unscaledTime + .5f;
                desktopAvailable = displayState != 0 && CanObserveDesktop();
                if (!desktopAvailable) { previous = null; changed = false; }
            }
            if (!desktopAvailable) return;
            if (capture != null && capture.IsCompleted)
            {
                Task<ScreenFrame> done = capture;
                bool wasLarge = largeCapture;
                capture = null;
                if (done.IsFaulted)
                {
                    if (Time.unscaledTime >= nextWarning)
                    {
                        Debug.LogWarning("COMPANION_CAPTURE: " + done.Exception?.GetBaseException().Message);
                        nextWarning = Time.unscaledTime + 30f;
                    }
                }
                else if (!done.IsCanceled && done.Result != null)
                {
                    if (wasLarge)
                    {
                        changed = false;
                        nextCandidate = Time.unscaledTime + 2.5f;
                        ScreenMasker.HideWindow(done.Result, windowBounds?.Invoke() ?? new RectInt(),
                            primaryWidth, primaryHeight);
                        candidate?.Invoke(done.Result);
                    }
                    else
                    {
                        ScreenFrame frame = done.Result;
                        SampleCount++;
                        if (previous == null || ScreenChangeDetector.MeanDifference(previous, frame,
                                windowBounds?.Invoke() ?? new RectInt(), primaryWidth, primaryHeight) >= 7.5f)
                            changed = true;
                        previous = frame;
                    }
                }
            }
            if (capture != null) return;
            if (changed && Time.unscaledTime >= nextCandidate && (canAnalyze?.Invoke() ?? false))
            {
                StartCapture(1280, true);
                return;
            }
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + .5f;
                StartCapture(320, false);
            }
        }

        private void StartCapture(int longSide, bool large)
        {
            largeCapture = large;
            CancellationToken token = cancellation.Token;
            capture = Task.Run(() => token.IsCancellationRequested ? null : ScreenCaptureService.CaptureScaled(longSide), token);
        }

        private static bool CanObserveDesktop()
        {
            IntPtr desktop = OpenInputDesktop(0, false, 0x0001);
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(128);
                return GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out _) &&
                       string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseDesktop(desktop); }
        }

        private void RegisterDisplayPower()
        {
            displayState = 1;
            powerCallback = OnPowerChanged;
            IntPtr parameters = Marshal.AllocHGlobal(IntPtr.Size * 2);
            try
            {
                Marshal.WriteIntPtr(parameters, 0, Marshal.GetFunctionPointerForDelegate(powerCallback));
                Marshal.WriteIntPtr(parameters, IntPtr.Size, IntPtr.Zero);
                Guid guid = DisplayStateGuid;
                if (PowerSettingRegisterNotification(ref guid, 2, parameters, out powerRegistration) != 0)
                    powerRegistration = IntPtr.Zero;
            }
            catch (Exception error) { Debug.LogWarning("COMPANION_POWER: " + error.Message); }
            finally { Marshal.FreeHGlobal(parameters); }
        }

        private uint OnPowerChanged(IntPtr context, uint type, IntPtr setting)
        {
            if (type == 0x8013 && setting != IntPtr.Zero && Marshal.ReadInt32(setting, 16) >= 4)
                displayState = Marshal.ReadInt32(setting, 20);
            return 0;
        }

        private void OnDestroy() { End(); }
    }

    internal static class ScreenChangeDetector
    {
        public static float MeanDifference(ScreenFrame first, ScreenFrame second, RectInt excluded,
            int primaryWidth, int primaryHeight)
        {
            if (first.Width != second.Width || first.Height != second.Height) return 255f;
            int width = second.Width, height = second.Height;
            int left = primaryWidth > 0 ? Mathf.Clamp(excluded.x * width / primaryWidth, 0, width) : 0;
            int right = primaryWidth > 0 ? Mathf.Clamp(excluded.xMax * width / primaryWidth, 0, width) : 0;
            int top = primaryHeight > 0 ? Mathf.Clamp(excluded.y * height / primaryHeight, 0, height) : 0;
            int bottom = primaryHeight > 0 ? Mathf.Clamp(excluded.yMax * height / primaryHeight, 0, height) : 0;
            long sum = 0, count = 0;
            for (int y = 0; y < height; y += 2)
            {
                int screenY = height - y - 1;
                for (int x = 0; x < width; x += 2)
                {
                    if (x >= left && x < right && screenY >= top && screenY < bottom) continue;
                    int index = (y * width + x) * 4;
                    sum += Math.Abs(first.Bgra[index] - second.Bgra[index]);
                    sum += Math.Abs(first.Bgra[index + 1] - second.Bgra[index + 1]);
                    sum += Math.Abs(first.Bgra[index + 2] - second.Bgra[index + 2]);
                    count += 3;
                }
            }
            return count == 0 ? 0 : sum / (float)count;
        }
    }

    internal static class ScreenMasker
    {
        public static void HideWindow(ScreenFrame frame, RectInt excluded, int primaryWidth, int primaryHeight)
        {
            if (frame == null || primaryWidth < 1 || primaryHeight < 1 || excluded.width < 1 || excluded.height < 1) return;
            int left = Mathf.Clamp(excluded.x * frame.Width / primaryWidth, 0, frame.Width);
            int right = Mathf.Clamp(excluded.xMax * frame.Width / primaryWidth, 0, frame.Width);
            int top = Mathf.Clamp(excluded.y * frame.Height / primaryHeight, 0, frame.Height);
            int bottom = Mathf.Clamp(excluded.yMax * frame.Height / primaryHeight, 0, frame.Height);
            for (int topY = top; topY < bottom; topY++)
            {
                int row = (frame.Height - topY - 1) * frame.Width * 4;
                for (int x = left; x < right; x++)
                {
                    int index = row + x * 4;
                    frame.Bgra[index] = frame.Bgra[index + 1] = frame.Bgra[index + 2] = 0;
                }
            }
        }
    }
}
