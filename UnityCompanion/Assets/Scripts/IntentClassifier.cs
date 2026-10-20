using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KurisuCompanion
{
    // 只在规则路由拿不准（RouteDecision.Ambiguous）时调用的意图分类。
    // 输出极短、超时很紧：失败或超时一律回落为 Chat，让主请求的 tool_choice=auto 自行判断，不阻塞对话。
    internal static class IntentClassifier
    {
        private const string Endpoint = "https://api.deepseek.com/chat/completions";
        public const float TimeoutSeconds = 2.5f;
        private const string Rule =
            "你是桌面陪伴程序的意图分类器。判断用户这句话是否要求程序在电脑上实际执行操作。" +
            "computer：打开/搜索/读取网页或文件、列目录、查看屏幕、运行一条命令等单步或少量步骤的电脑操作。" +
            "codex：编写或修改程序、修复项目报错、需要多步执行并产出文件的复杂任务。" +
            "chat：闲聊、提问、讲述已发生的事、询问原理或方法，即使提到了文件、网页、程序等词。" +
            "只输出 json：{\"intent\":\"chat|computer|codex\"}";

        [Serializable] private sealed class Response { public Choice[] choices; }
        [Serializable] private sealed class Choice { public Message message; }
        [Serializable] private sealed class Message { public string content; }
        [Serializable] private sealed class Intent { public string intent; }

        public static IEnumerator Classify(string apiKey, string userText, Func<bool> cancelled, Action<RouteDecision> onResult)
        {
            float started = Time.realtimeSinceStartup;
            var json = new StringBuilder(1200);
            json.Append("{\"model\":\"deepseek-flash\",\"stream\":false,\"max_tokens\":20,\"temperature\":0,")
                .Append("\"thinking\":{\"type\":\"disabled\"},\"response_format\":{\"type\":\"json_object\"},\"messages\":[")
                .Append("{\"role\":\"system\",\"content\":");
            AgentClient.Quote(json, Rule);
            json.Append("},{\"role\":\"user\",\"content\":");
            AgentClient.Quote(json, userText.Length > 600 ? userText.Substring(0, 600) : userText);
            json.Append("}]}");
            RouteDecision decision = RouteDecision.Chat;
            using (var request = new UnityWebRequest(Endpoint, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json.ToString()));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancelled() || Time.realtimeSinceStartup - started > TimeoutSeconds) { request.Abort(); break; }
                    yield return null;
                }
                if (request.result == UnityWebRequest.Result.Success) decision = Parse(request.downloadHandler.text);
                else Metrics.Count("router.classifier_fallback");
            }
            Metrics.Sample("router.classifier_seconds", Time.realtimeSinceStartup - started);
            Metrics.Count("router.classifier_" + decision.ToString().ToLowerInvariant());
            onResult(decision);
        }

        internal static RouteDecision Parse(string responseText)
        {
            try
            {
                string content = JsonUtility.FromJson<Response>(responseText)?.choices?[0]?.message?.content;
                string intent = string.IsNullOrWhiteSpace(content) ? "" : JsonUtility.FromJson<Intent>(content)?.intent ?? "";
                switch (intent.Trim().ToLowerInvariant())
                {
                    case "computer": return RouteDecision.Computer;
                    case "codex": return RouteDecision.Codex;
                    default: return RouteDecision.Chat;
                }
            }
            catch { return RouteDecision.Chat; }
        }
    }
}
