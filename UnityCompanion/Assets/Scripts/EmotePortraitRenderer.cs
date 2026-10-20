using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using UnityEngine;

namespace KurisuCompanion
{
    // FreeMote.NET contains C++/CLI and cannot run inside Unity's Mono runtime.
    // A .NET Framework bridge renders E-mote offscreen and streams BGRA frames here.
    internal sealed class EmotePortraitRenderer : MonoBehaviour
    {
        private const int Width = 1120, Height = 2080;
        private readonly object gate = new object();
        private PortraitViewport viewport;
        private Texture2D texture;
        private Process host;
        private Thread worker;
        private NamedPipeClientStream pipe;
        private byte[] latestFrame, spareFrame;
        private readonly byte[] rowScratch = new byte[Width * 4];
        private string failure;
        private bool stopping, mouthOpen;
        private int cueVersion, appliedCueVersion = -1, blinkRequest, blinkPhase, latestFrameBlinkPhase;
        private string expression = "表情_普通", gesture = "";
        public Action<string> Problem;
        public bool Ready { get; private set; }
        public Texture2D CurrentTexture => Ready ? texture : null;
        public int Frames { get; private set; }
        public float RecentFps { get; private set; }
        private float fpsWindowStart;
        private int fpsWindowFrames;
        public int VisiblePixelSamples { get; private set; }
        public string LastEmotion { get; private set; } = "neutral";
        public bool CueApplied { get { lock (gate) return appliedCueVersion == cueVersion; } }
        public int BlinkPhase { get { lock (gate) return blinkPhase; } }
        public int PaintedBlinkPhase { get; private set; }

        public void Configure(PortraitViewport target)
        {
            viewport = target;
            if (Application.platform != RuntimePlatform.WindowsEditor &&
                Application.platform != RuntimePlatform.WindowsPlayer) return;
            try
            {
                string executable = Path.GetDirectoryName(Application.dataPath);
                string packaged = Path.Combine(executable, "Emote");
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string local = Path.Combine(project, "EmoteTest");
                string folder = File.Exists(Path.Combine(packaged, "EmoteBridge.exe")) ? packaged : local;
                string bridge = Path.Combine(folder, "EmoteBridge.exe");
                string model = folder == packaged ? Path.Combine(folder, "kurisu.psb") :
                    Path.Combine(folder, "cache", "kurisu0-crossed.psb");
                if (!File.Exists(bridge) || !File.Exists(model) ||
                    !File.Exists(Path.Combine(folder, "FreeMote.NET.dll")) ||
                    !File.Exists(Path.Combine(folder, "emotedriver.dll")))
                    throw new FileNotFoundException("缺少本地 E-mote 渲染器或模型，请运行 EmoteTest/build.ps1 和 prepare-models.ps1。");
                string pipeName = "amadeus-emote-" + Guid.NewGuid().ToString("N");
                texture = new Texture2D(Width, Height, TextureFormat.BGRA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                host = Process.Start(new ProcessStartInfo
                {
                    FileName = bridge,
                    Arguments = "--pipe " + pipeName + " --model \"" + model + "\"",
                    WorkingDirectory = folder,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (host == null) throw new InvalidOperationException("无法启动 E-mote 渲染进程。");
                worker = new Thread(() => StreamFrames(pipeName)) { IsBackground = true, Name = "E-mote frames" };
                worker.Start();
            }
            catch (Exception error) { Fail(error.Message); }
        }

        private void StreamFrames(string pipeName)
        {
            try
            {
                pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.None);
                pipe.Connect(10000);
                using (var input = new BinaryReader(pipe))
                using (var output = new BinaryWriter(pipe))
                {
                    while (!stopping)
                    {
                        int version, requestedBlink;
                        string face, motion;
                        bool mouth;
                        lock (gate)
                        {
                            version = cueVersion;
                            face = expression;
                            motion = gesture;
                            mouth = mouthOpen;
                            requestedBlink = blinkRequest;
                        }
                        output.Write(version);
                        output.Write(face);
                        output.Write(motion);
                        output.Write(mouth);
                        output.Write(requestedBlink);
                        output.Flush();
                        int appliedVersion = input.ReadInt32();
                        int activeBlinkPhase = input.ReadInt32();
                        int length = input.ReadInt32();
                        if (length != 0 && length != Width * Height * 4)
                            throw new InvalidDataException("E-mote 返回的画面大小异常。");
                        if (length > 0)
                        {
                            byte[] pixels;
                            lock (gate) { pixels = spareFrame; spareFrame = null; }
                            if (pixels == null || pixels.Length != length) pixels = new byte[length];
                            int received = 0;
                            while (received < length)
                            {
                                int count = input.Read(pixels, received, length - received);
                                if (count == 0) throw new EndOfStreamException("E-mote 画面中断。");
                                received += count;
                            }
                            lock (gate)
                            {
                                if (latestFrame != null) spareFrame = latestFrame;
                                latestFrame = pixels;
                                latestFrameBlinkPhase = activeBlinkPhase;
                                appliedCueVersion = appliedVersion;
                                blinkPhase = activeBlinkPhase;
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                if (!stopping) lock (gate) failure = error.Message;
            }
        }

        public void Present(BilingualSentence sentence)
        {
            var cue = EmoteCue.FromSentence(sentence);
            LastEmotion = cue.Emotion;
            lock (gate)
            {
                expression = cue.ExpressionTimeline;
                gesture = cue.GestureTimeline ?? "";
                cueVersion++;
            }
            UnityEngine.Debug.Log("EMOTE_CUE emotion=" + cue.Emotion + " gesture=" + cue.Gesture);
        }

        public void Neutral()
        {
            lock (gate)
                if (expression == "表情_普通" && string.IsNullOrEmpty(gesture)) return;
            Present(new BilingualSentence { Emotion = "neutral", Gesture = "none" });
        }

        public void SetMouth(bool open)
        {
            lock (gate) mouthOpen = open;
        }

        public void BlinkOnce()
        {
            lock (gate) blinkRequest++;
        }

        private void Update()
        {
            byte[] pixels;
            string error;
            int frameBlinkPhase;
            lock (gate)
            {
                pixels = latestFrame;
                latestFrame = null;
                frameBlinkPhase = latestFrameBlinkPhase;
                error = failure;
                failure = null;
            }
            if (error != null) { Fail(error); return; }
            if (pixels == null || texture == null) return;
            if (Frames == 0)
            {
                int visible = 0;
                for (int i = 0; i < pixels.Length; i += 16)
                    if (pixels[i + 3] > 32 && pixels[i] + pixels[i + 1] + pixels[i + 2] > 60) visible++;
                VisiblePixelSamples = visible;
                if (visible < 10000) { Fail("E-mote 渲染画面为空。"); return; }
            }
            // FreeMote returns top-first BGRA rows; Unity raw textures expect bottom-first rows.
            int stride = Width * 4;
            for (int y = 0; y < Height / 2; y++)
            {
                int top = y * stride, bottom = (Height - 1 - y) * stride;
                Buffer.BlockCopy(pixels, top, rowScratch, 0, stride);
                Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
                Buffer.BlockCopy(rowScratch, 0, pixels, bottom, stride);
            }
            texture.LoadRawTextureData(pixels);
            texture.Apply(false, false);
            lock (gate) if (spareFrame == null) spareFrame = pixels;
            PaintedBlinkPhase = frameBlinkPhase;
            if (Frames++ == 0) viewport.SetDynamicTexture(texture);
            else viewport.RefreshDynamicFrame();
            fpsWindowFrames++;
            float now = Time.realtimeSinceStartup;
            if (fpsWindowStart == 0f) fpsWindowStart = now;
            if (now - fpsWindowStart >= 1f)
            {
                RecentFps = fpsWindowFrames / (now - fpsWindowStart);
                fpsWindowStart = now;
                fpsWindowFrames = 0;
            }
            Ready = true;
        }

        public void SaveFrame(string path)
        {
            if (!Ready || texture == null) throw new InvalidOperationException("E-mote 画面尚未就绪。");
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        private void Fail(string message)
        {
            UnityEngine.Debug.LogWarning("EMOTE_FALLBACK " + message);
            Problem?.Invoke("动态立绘不可用，已切换静态立绘：" + message);
            DisposeRenderer();
        }

        private void DisposeRenderer()
        {
            stopping = true;
            Ready = false;
            viewport?.SetDynamicTexture(null);
            try { pipe?.Dispose(); } catch { }
            try { if (host != null && !host.HasExited) host.Kill(); } catch { }
            if (worker != null && worker.IsAlive) worker.Join(1000);
            host?.Dispose();
            host = null;
            if (texture != null) { Destroy(texture); texture = null; }
        }

        private void OnDestroy() { DisposeRenderer(); }
    }
}
