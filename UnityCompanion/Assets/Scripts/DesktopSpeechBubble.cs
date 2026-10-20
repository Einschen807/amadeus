using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace KurisuCompanion
{
    internal sealed class DesktopSpeechBubble : IDisposable
    {
        [Serializable] internal sealed class Reply
        {
            public string op, side, text, path;
            public bool visible;
            public int x, y, width, height, ownerLeft, ownerRight, ownerTop, ownerBottom;
            public int edgeTransparency, centerTransparency;
        }
        [Serializable] private sealed class Command { public string op, text, path; }
        private delegate bool EnumProc(IntPtr handle, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder name, int length);

        private readonly ConcurrentQueue<string> messages = new ConcurrentQueue<string>();
        private readonly SpeechBubble fallback;
        private Process process;
        private string currentText = "";
        private float hideAt = -1f;
        private bool failed;

        public bool Ready { get; private set; }
        public bool Visible => currentText.Length > 0;
        public Reply Probe { get; private set; }
        public int ProcessId => process == null ? 0 : process.Id;

        public DesktopSpeechBubble(SpeechBubble inAppFallback)
        {
            fallback = inAppFallback;
            if (Application.platform != RuntimePlatform.WindowsPlayer) { failed = true; return; }
            try
            {
                IntPtr owner = FindWindow();
                if (owner == IntPtr.Zero) throw new InvalidOperationException("找不到主窗口");
                string executable = Path.Combine(Application.streamingAssetsPath, "DesktopBubble", "DesktopBubble.exe");
                if (!File.Exists(executable)) throw new FileNotFoundException("缺少桌面气泡窗口", executable);
                process = new Process { StartInfo = new ProcessStartInfo(executable, owner.ToInt64().ToString())
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                } };
                process.OutputDataReceived += (sender, args) => { if (args.Data != null) messages.Enqueue(args.Data); };
                process.ErrorDataReceived += (sender, args) => { if (args.Data != null) UnityEngine.Debug.LogWarning("桌面气泡：" + args.Data); };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            }
            catch (Exception error) { Fail(error.Message); }
        }

        public void Show(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
            hideAt = -1f;
            currentText = text;
            if (Ready) { fallback.Hide(); Write("show", text); }
            else fallback.Show(text);
        }

        public void HideAfter(float seconds)
        {
            if (Visible) hideAt = Time.unscaledTime + seconds;
        }

        public void Hide()
        {
            hideAt = -1f;
            currentText = "";
            fallback.Hide();
            if (Ready) Write("hide");
        }

        public void Tick()
        {
            while (messages.TryDequeue(out string line))
            {
                Reply reply;
                try { reply = JsonUtility.FromJson<Reply>(line); }
                catch { continue; }
                if (reply == null) continue;
                if (reply.op == "ready")
                {
                    Ready = true;
                    if (Visible) { fallback.Hide(); Write("show", currentText); }
                }
                else if (reply.op == "probe") Probe = reply;
                else if (reply.op == "error") Fail(reply.text);
            }
            if (!failed && process != null && process.HasExited) Fail("桌面气泡进程已退出");
            if (hideAt >= 0f && Time.unscaledTime >= hideAt) Hide();
            fallback.Tick();
        }

        public void RequestProbe() { Probe = null; if (Ready) Write("probe"); }
        public void Capture(string path) { if (Ready) Write("capture", path: path); }

        private void Write(string op, string text = null, string path = null)
        {
            try
            {
                if (process == null || process.HasExited) return;
                process.StandardInput.WriteLine(JsonUtility.ToJson(new Command { op = op, text = text, path = path }));
                process.StandardInput.Flush();
            }
            catch (Exception error) { Fail(error.Message); }
        }

        private void Fail(string message)
        {
            if (failed) return;
            failed = true; Ready = false;
            UnityEngine.Debug.LogWarning("DESKTOP_BUBBLE_FALLBACK " + message);
            if (Visible) fallback.Show(currentText);
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

        public void Dispose()
        {
            if (process == null) return;
            try
            {
                Write("quit");
                if (!process.WaitForExit(1000)) process.Kill();
            }
            catch { }
            finally { process.Dispose(); process = null; }
        }
    }
}
