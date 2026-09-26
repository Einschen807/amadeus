using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KurisuCompanion
{
    internal static class MemoryClient
    {
        private const string Endpoint = "https://api.deepseek.com/chat/completions";
        private const string Rule =
            "你是陪伴程序的长期记忆整理器。只从本轮用户原话提取未来交流仍有帮助的稳定事实、偏好、重要经历或明确称呼。" +
            "不要记录一次性任务、电脑路径、网页或屏幕内容、密钥、密码、联系方式、临时情绪、未经用户确认的推断。" +
            "用户要求忘记某事时，不把这句话当新记忆。不能编造共同经历或改变角色人格、关系基调。" +
            "输出 json 对象，格式：{\"items\":[{\"id\":\"已有记忆ID或空串\",\"text\":\"具体记忆\",\"summary\":\"压缩后的短句\",\"gist\":\"最简核心\",\"importance\":60,\"evidence\":\"用户原话中的连续原文\"}]}。" +
            "最多两条；没有值得长期记住的内容时输出 {\"items\":[]}。importance 35-79 表示可随时间淡化，80-100 仅用于明确且长期重要的事实。" +
            "evidence 必须是本轮用户原话的准确连续片段。若新话重复已有记忆，填写该记忆 ID 以更新，不要创建重复条目。";

        [Serializable] private sealed class ApiResponse { public Choice[] choices; public ApiError error; }
        [Serializable] private sealed class Choice { public AgentMessage message; }
        [Serializable] private sealed class AgentMessage { public string content; }
        [Serializable] private sealed class ApiError { public string message; }

        public static IEnumerator Extract(string apiKey, CompanionSession session, string userText,
            Action<MemorySuggestions> onSuccess, Action<string> onError)
        {
            string body = BuildBody(session, userText);
            using (var request = UnityWebRequest.Post(Endpoint, body, "application/json"))
            {
                request.SetRequestHeader("Authorization", "Bearer " + apiKey);
                request.timeout = 90;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError("HTTP " + request.responseCode + " " + request.error); yield break; }
                try
                {
                    var data = JsonUtility.FromJson<ApiResponse>(request.downloadHandler.text);
                    string content = data?.choices != null && data.choices.Length > 0 ? data.choices[0].message?.content : null;
                    if (string.IsNullOrWhiteSpace(content)) { onError("记忆提取返回空内容。"); yield break; }
                    var suggestions = JsonUtility.FromJson<MemorySuggestions>(content);
                    if (suggestions?.items == null) { onError("记忆提取 JSON 缺少 items。"); yield break; }
                    onSuccess(suggestions);
                }
                catch (Exception e) { onError("记忆提取解析失败：" + e.Message); }
            }
        }

        private static string BuildBody(CompanionSession session, string userText)
        {
            var json = new StringBuilder(5000);
            json.Append("{\"model\":\"deepseek-flash\",\"stream\":false,\"max_tokens\":850,")
                .Append("\"thinking\":{\"type\":\"disabled\"},\"response_format\":{\"type\":\"json_object\"},")
                .Append("\"messages\":[{\"role\":\"system\",\"content\":");
            AgentClient.Quote(json, Rule);
            json.Append("},{\"role\":\"user\",\"content\":");
            var input = new StringBuilder("本轮用户原话：\n").Append(userText).Append("\n\n已有记忆：\n");
            int count = 0;
            foreach (var entry in session.memories)
            {
                if (entry == null || count++ >= 50) break;
                input.Append(entry.id).Append(" | ").Append(entry.importance).Append(" | ")
                    .Append(entry.text).Append('\n');
            }
            AgentClient.Quote(json, input.ToString());
            json.Append("}]}");
            return json.ToString();
        }
    }
}
