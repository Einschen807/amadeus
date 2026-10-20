using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    // 构建后程序的 --smoke-* 自动检查。与正常运行路径分离，只在命令行参数要求时启动。
    public sealed partial class KurisuApp
    {
        private static bool HasArgument(string name) => Array.Exists(Environment.GetCommandLineArgs(), arg => arg == name);

        private void StartSmokeMode(bool bootSmoke)
        {
            if (bootSmoke) StartCoroutine(SmokeCapture());
            else if (HasArgument("--smoke-companion")) StartCoroutine(SmokeCompanion());
            else if (HasArgument("--smoke-companion-mic")) StartCoroutine(SmokeCompanionMic());
            else if (HasArgument("--smoke-companion-vision")) StartCoroutine(SmokeCompanionVision());
            else if (HasArgument("--smoke-fade")) StartCoroutine(SmokeFade());
            else if (HasArgument("--smoke-minimal-ui")) gameObject.AddComponent<MinimalUiChecks>();
            else if (HasArgument("--smoke-microphone")) gameObject.AddComponent<VoiceInputChecks>();
            else if (HasArgument("--smoke-speech")) gameObject.AddComponent<SpeechSmokeTests>();
            else if (HasArgument("--smoke-voice-live")) gameObject.AddComponent<SpeechLiveCheck>();
            else if (HasArgument("--smoke-emote")) StartCoroutine(SmokeEmote());
            else if (HasArgument("--smoke-portrait")) StartCoroutine(SmokeFixedPortrait());
            else if (HasArgument("--smoke-settings")) { OpenSettings(); StartCoroutine(SmokeCapture()); }
            else if (HasArgument("--smoke-ui-gallery")) StartCoroutine(SmokeUiGallery());
            else if (HasArgument("--smoke-eval")) gameObject.AddComponent<EvalRunner>();
            else if (HasArgument("--smoke-companion-preview")) StartCoroutine(SmokeCompanionPreview());
            else if (HasArgument("--smoke")) StartCoroutine(SmokeCapture());
        }

        // 依次截下各个界面状态，供视觉检查。记录页使用示例对话，不展示真实聊天内容。
        private IEnumerator SmokeUiGallery()
        {
            string folder = Path.Combine(Application.persistentDataPath, "ui-gallery");
            Directory.CreateDirectory(folder);
            yield return new WaitForSecondsRealtime(1.6f);
            yield return GalleryShot(folder, "01-main");
            notice.text = "设置已保存。";
            yield return GalleryShot(folder, "02-notice");
            notice.text = "";
            OpenSettings();
            yield return GalleryShot(folder, "03-settings-top");
            var scroll = settingsPanel.Q<ScrollView>();
            for (int page = 1; page <= 3 && scroll != null; page++)
            {
                scroll.scrollOffset = new Vector2(0, page * (scroll.contentViewport.layout.height - 40));
                yield return GalleryShot(folder, "03-settings-" + page);
            }
            settingsPanel.style.display = DisplayStyle.None;
            OpenHistory(new List<SavedTurn>
            {
                new SavedTurn { role = "user", text = "今天实验又失败了，有点累。", time = "2026-10-20 21:14" },
                new SavedTurn { role = "assistant", text = "先别急着下结论。失败在哪一步？把条件说清楚，我们一起排查。", time = "2026-10-20 21:14" },
                new SavedTurn { role = "user", text = "离心那步，样本好像污染了。", time = "2026-10-20 21:15" },
                new SavedTurn { role = "assistant", text = "那就先换一批对照组。还有，你今天睡够了吗？", time = "2026-10-20 21:15" },
            });
            yield return GalleryShot(folder, "04-history");
            historyPanel.style.display = DisplayStyle.None;
            StartCoroutine(AskApproval("允许运行命令？", "PowerShell\nGet-ChildItem D:\\Projects\\demo -Recurse\n\n工作目录：D:\\Projects\\demo", _ => { }));
            yield return GalleryShot(folder, "05-approval");
            approvalDecision = false;
            yield return null;
            // Update 每帧按真实拖放状态刷新遮罩，截图期间暂时摘下拖放监听。
            var drop = fileDrop; fileDrop = null;
            SetDropHover(true);
            yield return GalleryShot(folder, "06-drop");
            SetDropHover(false);
            fileDrop = drop;
            ShowVoiceProblem("语音服务未启动：没有找到 GPT-SoVITS 目录。");
            yield return GalleryShot(folder, "07-voice-problem");
            Debug.Log("UI_GALLERY_OK " + folder);
            yield return new WaitForSecondsRealtime(.3f);
            Application.Quit();
        }

        // 进入陪伴模式并停留一段时间，供外部对合成后的桌面截图，检查半透明效果；不观察屏幕、不调用接口。
        private IEnumerator SmokeCompanionPreview()
        {
            yield return new WaitForSecondsRealtime(1f);
            companionToggle.value = true;
            yield return new WaitForSecondsRealtime(.5f);
            companionManager.Display("你又在熬夜写代码？……算了，先把这个 bug 修完。我陪你看着。");
            Debug.Log("COMPANION_PREVIEW_READY transparent=" + companionManager.WindowTransparent);
            yield return new WaitForSecondsRealtime(12f);
            companionToggle.value = false;
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit();
        }

        private static IEnumerator GalleryShot(string folder, string name)
        {
            yield return null;
            yield return new WaitForSecondsRealtime(.35f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png"));
            yield return new WaitForSecondsRealtime(.25f);
        }

        private IEnumerator SmokeCapture()
        {
            yield return new WaitForSeconds(2f);
            yield return new WaitForEndOfFrame();
            var path = Path.Combine(Application.persistentDataPath, "smoke.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("KURISU_SMOKE_SCREENSHOT=" + path);
            yield return new WaitForSeconds(1f);
            Application.Quit();
        }

        private IEnumerator SmokeFade()
        {
            string folder = Path.Combine(Application.persistentDataPath, "fade-checks");
            Directory.CreateDirectory(folder);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "fade-start.png"));
            float startOpacity = portraitViewport.style.opacity.value;
            yield return new WaitForSecondsRealtime(.48f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "fade-mid.png"));
            float middleOpacity = portraitViewport.style.opacity.value;
            yield return new WaitForSecondsRealtime(.85f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "fade-end.png"));
            float endOpacity = portraitViewport.style.opacity.value;
            bool valid = startOpacity < .15f && middleOpacity > .2f && middleOpacity < .9f && endOpacity > .99f;
            Debug.Log(valid ? "AMADEUS_FADE_SMOKE_OK" :
                "AMADEUS_FADE_SMOKE_FAILED opacity=" + startOpacity + "," + middleOpacity + "," + endOpacity);
            Application.Quit(valid ? 0 : 1);
        }

        private IEnumerator SmokeCompanion()
        {
            string folder = Path.Combine(Application.persistentDataPath, "companion-checks");
            Directory.CreateDirectory(folder);
            if (!CompanionVoiceCommands.TryParse("你来陪我打游戏", out bool enterByVoice) || !enterByVoice ||
                !CompanionVoiceCommands.TryParse("退出陪伴模式", out bool leaveByVoice) || leaveByVoice ||
                CompanionVoiceCommands.TryParse("不要进入陪伴模式", out _))
            {
                Debug.LogError("COMPANION_SMOKE_FAILED: voice command recognition");
                Application.Quit(1); yield break;
            }
            try
            {
                byte[] red = { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 };
                string png = ScreenPngEncoder.EncodeDataUrl(new ScreenFrame(2, 2, red));
                var decoded = new Texture2D(2, 2);
                try
                {
                    if (!ImageConversion.LoadImage(decoded, Convert.FromBase64String(png.Substring("data:image/png;base64,".Length))) ||
                        decoded.width != 2 || decoded.height != 2 || decoded.GetPixel(0, 0).r < .8f)
                        throw new InvalidDataException("PNG 像素验证失败");
                }
                finally { Destroy(decoded); }
            }
            catch (Exception error)
            {
                Debug.LogError("COMPANION_SMOKE_FAILED: image encoding " + error.Message);
                Application.Quit(1); yield break;
            }
            companionToggle.value = true;
            yield return new WaitForSecondsRealtime(.8f);
            if (!companionManager.Active || !companionManager.WindowCornersClipped ||
                Screen.width != CompanionWindow.PixelWidth || Screen.height != CompanionWindow.PixelHeight ||
                companionManager.LocalScreenSamples < 1)
            {
                Debug.LogError("COMPANION_SMOKE_FAILED: window shape, mode, or local screen sampling");
                Application.Quit(1); yield break;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "companion.png"));
            yield return new WaitForSecondsRealtime(.5f);
            companionToggle.value = false;
            yield return new WaitForSecondsRealtime(.5f);
            if (companionManager.Active || companionManager.Observing ||
                Screen.width != PhoneWindowChrome.ClientWidth || Screen.height != PhoneWindowChrome.ClientHeight)
            {
                Debug.LogError("COMPANION_SMOKE_FAILED: normal window was not restored");
                Application.Quit(1); yield break;
            }
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "normal.png"));
            Debug.Log("COMPANION_SMOKE_OK");
            yield return new WaitForSecondsRealtime(.5f);
            Application.Quit();
        }

        private IEnumerator SmokeCompanionMic()
        {
            if (voiceMode)
            {
                Debug.LogError("COMPANION_MIC_SMOKE_FAILED: microphone was already on");
                Application.Quit(1); yield break;
            }
            companionManager.SetActive(true);
            yield return new WaitForSecondsRealtime(.25f);
            if (!companionManager.Active || !voiceMode || !voiceInput.Active)
            {
                Debug.LogError("COMPANION_MIC_SMOKE_FAILED: microphone did not activate on entry");
                Application.Quit(1); yield break;
            }
            companionManager.SetActive(false);
            yield return new WaitForSecondsRealtime(.25f);
            if (companionManager.Active || voiceMode || voiceInput.Active)
            {
                Debug.LogError("COMPANION_MIC_SMOKE_FAILED: original microphone state was not restored");
                Application.Quit(1); yield break;
            }
            Debug.Log("COMPANION_MIC_SMOKE_OK");
            Application.Quit();
        }

        private IEnumerator SmokeCompanionVision()
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Debug.LogError("COMPANION_VISION_SMOKE_FAILED: API Key 未配置");
                Application.Quit(1); yield break;
            }
            var visual = gameObject.AddComponent<ScreenVisionService>();
            ScreenInsight insight = null;
            visual.Configure(() => apiKey, () => "合成色块验证图；不要推断真实桌面活动。", value => insight = value);
            visual.Begin();
            const int width = 320, height = 180;
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    pixels[i] = (byte)(x < width / 2 ? 30 : 220);
                    pixels[i + 1] = 50;
                    pixels[i + 2] = (byte)(x < width / 2 ? 220 : 30);
                    pixels[i + 3] = 255;
                }
            visual.Submit(new ScreenFrame(width, height, pixels), true);
            float until = Time.unscaledTime + 40f;
            while (visual.FinishedCount == 0 && Time.unscaledTime < until) yield return null;
            bool success = insight != null && !string.IsNullOrWhiteSpace(insight.summary);
            visual.End();
            Debug.Log(success ? "COMPANION_VISION_SMOKE_OK summary=" + insight.summary :
                "COMPANION_VISION_SMOKE_FAILED: no structured summary");
            Application.Quit(success ? 0 : 1);
        }

        private IEnumerator SmokeEmote()
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (emoteRenderer.Frames < 2 && Time.realtimeSinceStartup < deadline) yield return null;
            if (!emoteRenderer.Ready || emoteRenderer.Frames < 2)
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: renderer did not produce frames");
                Application.Quit(1); yield break;
            }
            var folder = Path.Combine(Application.persistentDataPath, "emote-checks");
            Directory.CreateDirectory(folder);
            emoteRenderer.SaveFrame(Path.Combine(folder, "neutral-frame.png"));
            emoteRenderer.Present(new BilingualSentence { Chinese = "真拿你没办法，笨蛋。", Emotion = "teasing", Gesture = "nod", Complete = true });
            yield return new WaitForSecondsRealtime(1f);
            if (emoteRenderer.LastEmotion != "teasing" || !emoteRenderer.CueApplied || emoteRenderer.Frames < 4)
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: cue or frame update failed");
                Application.Quit(1); yield break;
            }
            string closedPath = Path.Combine(folder, "teasing-frame.png");
            emoteRenderer.SaveFrame(closedPath);
            speechPipeline.enabled = false;
            emoteRenderer.SetMouth(true);
            yield return new WaitForSecondsRealtime(.25f);
            string openPath = Path.Combine(folder, "mouth-open-frame.png");
            emoteRenderer.SaveFrame(openPath);
            emoteRenderer.SetMouth(false);
            speechPipeline.enabled = true;
            if (SameFrame(closedPath, openPath))
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: face_talk did not change the rendered frame");
                Application.Quit(1); yield break;
            }
            emoteRenderer.Present(new BilingualSentence { Emotion = "shy", Gesture = "blush", Complete = true });
            yield return new WaitForSecondsRealtime(.5f);
            string blushPath = Path.Combine(folder, "blush-frame.png");
            emoteRenderer.SaveFrame(blushPath);
            int framesBeforeReturn = emoteRenderer.Frames;
            emoteRenderer.Neutral();
            deadline = Time.realtimeSinceStartup + 3f;
            while ((!emoteRenderer.CueApplied || emoteRenderer.Frames < framesBeforeReturn + 2) &&
                   Time.realtimeSinceStartup < deadline) yield return null;
            if (!emoteRenderer.CueApplied || emoteRenderer.Frames < framesBeforeReturn + 2)
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: native neutral cue did not render new frames");
                Application.Quit(1); yield break;
            }
            string returningPath = Path.Combine(folder, "returning-frame.png");
            emoteRenderer.SaveFrame(returningPath);
            if (SameFrame(blushPath, returningPath))
                Debug.LogWarning("EMOTE_SMOKE: the early return frame still matches the held pose");
            yield return new WaitForSecondsRealtime(.6f);
            if (!emoteRenderer.CueApplied || emoteRenderer.LastEmotion != "neutral")
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: neutral reset was not applied");
                Application.Quit(1); yield break;
            }
            string restoredPath = Path.Combine(folder, "restored-frame.png");
            emoteRenderer.SaveFrame(restoredPath);
            if (!SameFrame(Path.Combine(folder, "neutral-frame.png"), restoredPath))
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: blush or pose remained after neutral reset");
                Application.Quit(1); yield break;
            }
            emoteRenderer.BlinkOnce();
            deadline = Time.realtimeSinceStartup + 3f;
            while (emoteRenderer.PaintedBlinkPhase != 2 && Time.realtimeSinceStartup < deadline) yield return null;
            if (emoteRenderer.PaintedBlinkPhase != 2)
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: controlled blink did not reach closed phase");
                Application.Quit(1); yield break;
            }
            string blinkPath = Path.Combine(folder, "blink-closed-frame.png");
            emoteRenderer.SaveFrame(blinkPath);
            if (SameFrame(restoredPath, blinkPath))
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: closed-eye frame did not change");
                Application.Quit(1); yield break;
            }
            yield return new WaitForSecondsRealtime(.6f);
            if (emoteRenderer.PaintedBlinkPhase != 0)
            {
                Debug.LogError("EMOTE_SMOKE_FAILED: blink did not reopen");
                Application.Quit(1); yield break;
            }
            Debug.Log("EMOTE_SMOKE_OK frames=" + emoteRenderer.Frames + " fps=" + emoteRenderer.RecentFps.ToString("F1") +
                " visibleSamples=" + emoteRenderer.VisiblePixelSamples);
            yield return new WaitForSecondsRealtime(.2f);
            Application.Quit();
        }

        private static bool SameFrame(string leftPath, string rightPath)
        {
            byte[] left = File.ReadAllBytes(leftPath), right = File.ReadAllBytes(rightPath);
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }

        private void PortraitTestEvent(EventType type, Vector2 point, int button = 0, int clicks = 1, VisualElement receiver = null)
        {
            var systemEvent = new Event { type = type, mousePosition = point, button = button, clickCount = clicks };
            EventBase evt;
            if (type == EventType.MouseDown) evt = PointerDownEvent.GetPooled(systemEvent);
            else if (type == EventType.MouseUp) evt = PointerUpEvent.GetPooled(systemEvent);
            else evt = PointerMoveEvent.GetPooled(systemEvent);
            using (evt) { evt.target = receiver ?? portraitViewport; (receiver ?? portraitViewport).SendEvent(evt); }
        }

        private IEnumerator SmokeFixedPortrait()
        {
            yield return new WaitForSeconds(1);
            Action<bool,string> check = (ok,message) => {
                if (!ok) { Debug.LogError("PORTRAIT_CHECK_FAILED: " + message); Application.Quit(1); throw new InvalidOperationException(message); }
                Debug.Log("PORTRAIT_CHECK_OK: " + message);
            };
            var expected = PortraitPose.ConfirmedDefault;
            var start = portraitViewport.worldBound.center;
            var original = portraitViewport.Offset;
            float zoom = portraitViewport.Zoom;
            check(Mathf.Abs(zoom - 1.2336781f) < .00001f, "confirmed 123.36781 percent scale is the built-in default");
            check(Vector2.Distance(original, new Vector2(expected.offsetX * portraitViewport.contentRect.width,
                expected.offsetY * portraitViewport.contentRect.height)) < .01f, "confirmed position is preserved");
            PortraitTestEvent(EventType.MouseDown, start);
            PortraitTestEvent(EventType.MouseDrag, start + new Vector2(90,-240));
            PortraitTestEvent(EventType.MouseUp, start + new Vector2(90,-240));
            yield return null;
            check(portraitViewport.Offset == original && !portraitViewport.HasPointerCapture(0), "left dragging cannot move portrait or capture pointer");
            foreach (var receiver in new VisualElement[] { portraitViewport, dialogue }) {
                foreach (float delta in new[] {-12f,12f}) {
                    using (var wheel = WheelEvent.GetPooled(new Event {type=EventType.ScrollWheel,mousePosition=start,delta=new Vector2(0,delta)})) {
                        wheel.target=receiver; receiver.SendEvent(wheel);
                    }
                }
            }
            yield return null;
            check(portraitViewport.Zoom == zoom && portraitViewport.Offset == original, "wheel over portrait and dialogue cannot change framing");
            PortraitTestEvent(EventType.MouseDown, start, 0, 2); yield return null;
            check(portraitViewport.Offset == original, "double click cannot change fixed framing");
            portraitViewport.SetMouth(true); check(portraitViewport.MouthOpen,"mouth animation still opens");
            portraitViewport.SetMouth(false); check(!portraitViewport.MouthOpen,"mouth animation still closes");
            var folder=Path.Combine(Application.persistentDataPath,"fixed-portrait-checks"); Directory.CreateDirectory(folder);
            yield return new WaitForEndOfFrame(); ScreenCapture.CaptureScreenshot(Path.Combine(folder,"default.png"));
            yield return new WaitForSeconds(.8f);
            check(Screen.width==PhoneWindowChrome.ClientWidth && Screen.height==PhoneWindowChrome.ClientHeight &&
                portraitViewport.Zoom==zoom && Vector2.Distance(portraitViewport.Offset,
                new Vector2(expected.offsetX*portraitViewport.contentRect.width,expected.offsetY*portraitViewport.contentRect.height))<.01f,
                "locked window size preserves confirmed framing");
            Debug.Log("FIXED_PORTRAIT_CHECKS_OK"); yield return new WaitForSeconds(.2f); Application.Quit();
        }
    }
}
