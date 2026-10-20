using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KurisuCompanion
{
    // The normal bilingual stream and SpeechPipeline still own segmentation, TTS and playback.
    internal sealed class CompanionSpeechController : MonoBehaviour
    {
        private SpeechPipeline pipeline;
        private Func<string> apiKey, persona, baseContext;
        private Func<List<ChatTurn>> history;
        private Action<string> show;
        private CompanionContext context;
        private Coroutine request;
        private int generation;
        private bool ownsPlayback;
        public bool ProactiveActive => request != null || ownsPlayback;

        public void Configure(SpeechPipeline speech, Func<string> key, Func<string> role,
            Func<string> conversationContext, Func<List<ChatTurn>> turns,
            CompanionContext state, Action<string> display)
        {
            pipeline = speech; apiKey = key; persona = role; baseContext = conversationContext;
            history = turns; context = state; show = display;
        }

        public void Comment(ScreenInsight insight)
        {
            if (ProactiveActive || string.IsNullOrWhiteSpace(apiKey?.Invoke())) return;
            int version = ++generation;
            request = StartCoroutine(Generate(insight, version));
        }

        private IEnumerator Generate(ScreenInsight insight, int version)
        {
            var sentences = new List<BilingualSentence>();
            string answer = null, error = null;
            string prompt = "根据当前活动，像坐在旁边陪伴用户的牧濑红莉栖一样自然说一句简短的话，中文尽量不超过三十字。" +
                            "不要描述截图方位，不要重复近期台词或换词重述同一判断；若没有新内容就回复空字符串。不要编造没看到的细节。场景：" + insight.summary +
                            "。新事件：" + string.Join("；", insight.events ?? Array.Empty<string>()) + "。";
            string extra = baseContext() + "\n" + context.ForConversation() +
                           "\n近期已说过的话：" + context.RecentCommentsForPrompt() +
                           "\n[主动发言]本次没有用户提问，不执行电脑工具，不替用户操作，只说一句。[/主动发言]";
            var recent = history();
            var shortHistory = recent.GetRange(Math.Max(0, recent.Count - 6), Math.Min(6, recent.Count));
            yield return AgentClient.Send(apiKey(), persona(), extra, shortHistory,
                prompt, null, RejectApproval, _ => { }, value => answer = value, value => error = value,
                null, () => version != generation,
                sentence =>
                {
                    if (version != generation) return;
                    sentences.Add(sentence);
                }, false);
            request = null;
            if (version != generation) yield break;
            if (error != null)
            {
                Debug.LogWarning("COMPANION_COMMENT: " + error);
                yield break;
            }
            string spoken = !string.IsNullOrWhiteSpace(answer) ? answer :
                string.Join("", sentences.ConvertAll(item => item.Chinese));
            if (string.IsNullOrWhiteSpace(spoken) || context.IsRepeatedComment(spoken)) yield break;
            ownsPlayback = true;
            pipeline.Begin(apiKey());
            if (sentences.Count == 0) pipeline.Push(spoken);
            else foreach (var sentence in sentences) pipeline.PushBilingual(sentence);
            pipeline.Complete();
            context.Comment(spoken);
        }

        private static IEnumerator RejectApproval(string title, string details, Action<bool> decision)
        { decision(false); yield break; }

        public void Cancel()
        {
            if (!ProactiveActive) return;
            generation++;
            if (request != null) StopCoroutine(request);
            request = null;
            if (ownsPlayback) pipeline?.Cancel();
            ownsPlayback = false;
        }

        private void Update()
        {
            if (request == null && ownsPlayback && (pipeline == null || !pipeline.Busy)) ownsPlayback = false;
        }

        private void OnDestroy() { Cancel(); }
    }
}
