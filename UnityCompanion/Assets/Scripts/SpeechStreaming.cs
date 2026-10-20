using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KurisuCompanion
{
    // 把流式文本切成适合字幕与 TTS 的句子：优先在句末标点断开，过长时退到逗号等次级边界。
    internal sealed class SentenceBuffer
    {
        private const string Terminators = "。！？\n";
        private const string AsciiTerminators = "!?";
        private const string SoftBoundaries = "，、；：,;: \t";
        private const string TrailingClosers = "。！？!?…」』”\"";

        private readonly StringBuilder buffer = new StringBuilder();
        public readonly int Limit;

        public SentenceBuffer(int limit = 56) { Limit = Math.Max(8, limit); }

        public void Push(string delta, Action<string> emit, bool final = false)
        {
            buffer.Append(delta);
            while (buffer.Length > 0)
            {
                string text = buffer.ToString();
                int cut = FindTerminator(text);
                if (cut > Limit || (cut == 0 && text.Length >= Limit)) cut = FindSoftCut(text);
                if (cut == 0)
                {
                    if (!final) break;
                    cut = text.Length;
                }
                // 把紧跟的标点和引号并入本句，例如"……」"。
                while (cut < text.Length && cut < Limit && TrailingClosers.IndexOf(text[cut]) >= 0) cut++;
                string sentence = text.Substring(0, cut).Trim();
                buffer.Remove(0, cut);
                if (sentence.Length > 0) emit(sentence);
            }
        }

        // ASCII 句点不作为句末：小数、缩写和 URL 保持完整。
        private static int FindTerminator(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                bool url = InUrl(text, i);
                if (Terminators.IndexOf(text[i]) >= 0 || (!url && AsciiTerminators.IndexOf(text[i]) >= 0)) return i + 1;
                if (!url && text[i] == '…' && i + 1 < text.Length && text[i + 1] == '…') return i + 2;
            }
            return 0;
        }

        private int FindSoftCut(string text)
        {
            int boundary = 0;
            for (int i = 0; i < Math.Min(Limit, text.Length); i++)
                if (!InUrl(text, i) && SoftBoundaries.IndexOf(text[i]) >= 0) boundary = i + 1;
            int cut = boundary >= Limit / 2 ? boundary : Math.Min(Limit, text.Length);
            // 不把代理对（如表情符号）从中间切开。
            if (cut < text.Length && char.IsHighSurrogate(text[cut - 1])) cut--;
            return cut;
        }

        private static bool InUrl(string text, int index)
        {
            int start = -1;
            foreach (string prefix in new[] { "https://", "http://", "www." })
            {
                int found = text.LastIndexOf(prefix, index, StringComparison.OrdinalIgnoreCase);
                if (found > start) start = found;
            }
            if (start < 0) return false;
            for (int i = start; i < index; i++)
                if (char.IsWhiteSpace(text[i]) || "。！？，「」".IndexOf(text[i]) >= 0) return false;
            return true;
        }
    }

    // 接收 SSE 字节流。ReceiveData 可能在网络线程回调，因此只入队，解码与分行在主线程的 Drain 中完成。
    internal sealed class SseDownload : DownloadHandlerScript
    {
        private readonly Queue<byte[]> chunks = new Queue<byte[]>();
        private readonly object gate = new object();
        private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder pending = new StringBuilder();

        public SseDownload() : base(new byte[8192]) { }

        protected override bool ReceiveData(byte[] data, int length)
        {
            if (length > 0)
            {
                var copy = new byte[length];
                Buffer.BlockCopy(data, 0, copy, 0, length);
                lock (gate) chunks.Enqueue(copy);
            }
            return true;
        }

        public void Drain(Action<string> consume)
        {
            lock (gate)
            {
                while (chunks.Count > 0)
                {
                    byte[] bytes = chunks.Dequeue();
                    var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
                    // 有状态的 Decoder 能正确处理被拆在两个分块之间的多字节 UTF-8 字符。
                    int count = decoder.GetChars(bytes, 0, bytes.Length, chars, 0, false);
                    pending.Append(chars, 0, count);
                }
            }
            string text = pending.ToString();
            int start = 0, end;
            while ((end = text.IndexOf('\n', start)) >= 0)
            {
                string line = text.Substring(start, end - start).TrimEnd('\r');
                start = end + 1;
                if (line.StartsWith("data:")) consume(line.Substring(5).Trim());
            }
            pending.Clear();
            pending.Append(text, start, text.Length - start);
        }
    }

    // 读取 OpenAI 兼容的流式补全：逐段回调正文，并把分片到达的 tool_calls 拼回完整调用。
    internal static class AgentStream
    {
        public static IEnumerator Read(string endpoint, string key, string body, Func<bool> cancelled,
            Action<string> delta, Action<string> completed, Action<string> error)
        {
            using (var request = new UnityWebRequest(endpoint, "POST"))
            {
                var handler = new SseDownload();
                request.downloadHandler = handler;
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + key);
                request.timeout = 120;

                var answer = new StringBuilder();
                var calls = new SortedDictionary<int, Dictionary<string, object>>();
                string failure = null;
                bool done = false, firstToken = true;
                float started = Time.realtimeSinceStartup;

                Action<string> consume = payload =>
                {
                    if (payload == "[DONE]") { done = true; return; }
                    try
                    {
                        object parsed = RpcJson.Decode(payload);
                        if (RpcJson.Get(parsed, "error") != null)
                        {
                            failure = RpcJson.Str(RpcJson.Get(parsed, "error"), "message");
                            return;
                        }
                        var choices = RpcJson.Get(parsed, "choices") as List<object>;
                        if (choices == null || choices.Count == 0) return;
                        var change = RpcJson.Get(choices[0], "delta");
                        string content = RpcJson.Str(change, "content");
                        if (content.Length > 0)
                        {
                            if (firstToken)
                            {
                                firstToken = false;
                                Metrics.Sample("llm.first_token_seconds", Time.realtimeSinceStartup - started);
                            }
                            answer.Append(content);
                            delta?.Invoke(content);
                        }
                        if (RpcJson.Get(change, "tool_calls") is List<object> fragments)
                            foreach (var fragment in fragments) MergeToolCall(calls, fragment);
                    }
                    catch (Exception e) { failure = "流式消息解析失败：" + e.Message; }
                };

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancelled()) { request.Abort(); yield break; }
                    handler.Drain(consume);
                    yield return null;
                }
                handler.Drain(consume);
                if (cancelled()) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    error("聊天连接失败：HTTP " + request.responseCode + " " + request.error);
                    yield break;
                }
                if (failure != null || !done)
                {
                    error(failure ?? "聊天流提前结束，请重试。");
                    yield break;
                }
                // 拼成非流式响应的形状，调用方可以用同一套解析逻辑。
                completed(RpcJson.Encode(RpcJson.Obj("choices", new[]
                {
                    RpcJson.Obj("message", RpcJson.Obj("content", answer.ToString(),
                        "tool_calls", new List<Dictionary<string, object>>(calls.Values)))
                })));
            }
        }

        // 同一个工具调用的 id、name、arguments 会按 index 分多次到达，这里按序拼接。
        private static void MergeToolCall(SortedDictionary<int, Dictionary<string, object>> calls, object fragment)
        {
            int index = Convert.ToInt32(RpcJson.Get(fragment, "index"));
            if (!calls.TryGetValue(index, out var call))
            {
                call = RpcJson.Obj("id", "", "type", "function", "function", RpcJson.Obj("name", "", "arguments", ""));
                calls.Add(index, call);
            }
            if (RpcJson.Str(fragment, "id").Length > 0) call["id"] = RpcJson.Str(fragment, "id");
            var function = (Dictionary<string, object>)call["function"];
            var part = RpcJson.Get(fragment, "function");
            function["name"] = (string)function["name"] + RpcJson.Str(part, "name");
            function["arguments"] = (string)function["arguments"] + RpcJson.Str(part, "arguments");
        }
    }
}
