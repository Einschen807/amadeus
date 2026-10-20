using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace KurisuCompanion
{
    internal sealed class CompanionManager : MonoBehaviour
    {
        private readonly CompanionContext context = new CompanionContext();
        private readonly CompanionDecisionService decision = new CompanionDecisionService();
        private CompanionWindow window;
        private ScreenObserver observer;
        private ScreenVisionService vision;
        private CompanionSpeechController speech;
        private ScreenInsight lastInsight;
        private ScreenInsight pendingMajorEvent;
        private float pendingMajorAt;
        private bool lastDesktopAvailable;
        private Func<bool> userBusy;
        private Action<bool> modeChanged;
        private bool localSmoke;
        public bool Active => window != null && window.Active;
        public bool ProactiveSpeaking => speech != null && speech.ProactiveActive;
        public bool ResumeOnStartup => window != null && window.Settings.enabled;
        public int LocalScreenSamples => observer != null ? observer.SampleCount : 0;
        public bool Observing => observer != null && observer.Active;
        public bool WindowCornersClipped => window != null && window.NativeCornersClipped;
        public bool WindowTransparent => window != null && window.Transparent;
        public int VisionAnalyses => vision != null ? vision.AnalysisCount : 0;
        public string ScreenContext => context.ForConversation();

        public void Configure(UIDocument document, PhoneWindowChrome chrome, EmotePortraitRenderer renderer,
            SpeechPipeline pipeline, Func<string> apiKey, Func<string> persona,
            Func<string> baseContext, Func<List<ChatTurn>> history,
            Func<bool> isUserBusy, Action<bool> onModeChanged)
        {
            userBusy = isUserBusy; modeChanged = onModeChanged;
            localSmoke = Array.Exists(Environment.GetCommandLineArgs(), arg =>
                arg == "--smoke-companion" || arg == "--smoke-companion-mic" || arg == "--smoke-companion-preview");
            window = new CompanionWindow(document, chrome, renderer, pipeline, () => SetActive(false));
            vision = gameObject.AddComponent<ScreenVisionService>();
            vision.Configure(apiKey, () => context.ForVision(), Observe);
            observer = gameObject.AddComponent<ScreenObserver>();
            observer.Configure(() => vision.Ready, () => window.Bounds, frame => vision.Submit(frame));
            speech = gameObject.AddComponent<CompanionSpeechController>();
            speech.Configure(pipeline, apiKey, persona, baseContext, history, context, text => window.Show(text));
        }

        public void SetActive(bool enabled)
        {
            if (window == null || enabled == Active) return;
            if (enabled)
            {
                if (!window.Enter()) { modeChanged?.Invoke(false); return; }
                decision.Begin();
                if (!localSmoke) vision.Begin();
                observer.Begin();
                window.Show("我在这里。你继续忙，我会留意发生了什么。");
                modeChanged?.Invoke(true);
            }
            else
            {
                observer.End();
                vision.End();
                speech.Cancel();
                context.Clear();
                decision.Clear();
                lastInsight = null;
                pendingMajorEvent = null;
                lastDesktopAvailable = false;
                window.Exit();
                modeChanged?.Invoke(false);
            }
        }

        public void Display(string text)
        {
            if (Active) window.Show(text);
        }

        public void UserSpoke()
        {
            decision.UserSpoke();
            pendingMajorEvent = null;
            speech?.Cancel();
        }

        public void RecordReply(string text) { if (Active) context.Comment(text); }

        // Voice requests wait briefly for a current scene summary before asking the main LLM.
        public IEnumerator RefreshForVoice()
        {
            if (!Active || !vision.Ready || !observer.DesktopAvailable ||
                (DateTime.UtcNow - context.LastObservationUtc).TotalSeconds < 4) yield break;
            int before = vision.FinishedCount;
            Task<ScreenFrame> capture = Task.Run(() => ScreenCaptureService.CaptureScaled(1280));
            float deadline = Time.unscaledTime + 3f;
            while (!capture.IsCompleted && Active && observer.DesktopAvailable && Time.unscaledTime < deadline)
                yield return null;
            if (!Active || !observer.DesktopAvailable || !capture.IsCompleted || capture.IsFaulted || capture.IsCanceled)
                yield break;
            if ((DateTime.UtcNow - context.LastObservationUtc).TotalSeconds < 4) yield break;
            ScreenMasker.HideWindow(capture.Result, window.Bounds,
                Screen.currentResolution.width, Screen.currentResolution.height);
            vision.Submit(capture.Result, true);
            while (Active && vision.FinishedCount == before && Time.unscaledTime < deadline)
                yield return null;
        }

        // Called only with a completed visual summary. Local change detection never calls it directly.
        public void Observe(ScreenInsight insight)
        {
            if (!Active || insight == null) return;
            lastInsight = insight;
            if (decision.IsNewMajorEvent(insight))
            {
                pendingMajorEvent = insight;
                pendingMajorAt = Time.unscaledTime;
            }
            context.Observe(insight);
        }

        private void Update()
        {
            if (!Active) return;
            bool desktopAvailable = observer.DesktopAvailable;
            vision.SetDesktopAvailable(desktopAvailable);
            if (lastDesktopAvailable && !desktopAvailable)
            {
                context.InvalidateScreen();
                lastInsight = null;
                pendingMajorEvent = null;
            }
            lastDesktopAvailable = desktopAvailable;
            bool canSpeak = desktopAvailable && !((userBusy?.Invoke() ?? true) || ProactiveSpeaking);
            if (pendingMajorEvent != null && Time.unscaledTime - pendingMajorAt > 30f)
                pendingMajorEvent = null;
            if (canSpeak && pendingMajorEvent != null && decision.CanSendMajor())
            {
                speech.Comment(pendingMajorEvent);
                pendingMajorEvent = null;
                decision.MajorSent();
            }
            else if (canSpeak && lastInsight != null &&
                     !string.IsNullOrWhiteSpace(lastInsight.summary) && decision.RoutineDue(context))
            {
                var dwell = new ScreenInsight { application = lastInsight.application,
                    activity = lastInsight.activity, scene = lastInsight.scene,
                    summary = lastInsight.summary, importance = lastInsight.importance,
                    events = Array.Empty<string>() };
                speech.Comment(dwell);
                decision.RoutineSent(context);
            }
            window.Tick();
        }
        private void OnDestroy() { if (Active) SetActive(false); else { observer?.End(); vision?.End(); } }
    }
}
