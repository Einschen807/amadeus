using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using KurisuCompanion;
using UnityEngine;

public static class AgentChecks
{
    [Serializable] private sealed class RequestShape
    {
        public string model;
        public string tool_choice;
        public ToolShape[] tools;
        public MessageShape[] messages;
    }
    [Serializable] private sealed class ToolShape { public FunctionShape function; }
    [Serializable] private sealed class FunctionShape { public string name; }
    [Serializable] private sealed class MessageShape { public string role; }
    [Serializable] private sealed class PathAction { public string action; public string path; }

    public static void Run()
    {
        Assembly runtime = typeof(KurisuApp).Assembly;
        Type client = runtime.GetType("KurisuCompanion.AgentClient", true);
        Type chatTurn = runtime.GetType("KurisuCompanion.ChatTurn", true);
        Type exchange = client.GetNestedType("Exchange", BindingFlags.NonPublic);
        object history = Activator.CreateInstance(typeof(List<>).MakeGenericType(chatTurn));
        object exchanges = Activator.CreateInstance(typeof(List<>).MakeGenericType(exchange));
        MethodInfo bodyMethod = client.GetMethod("BuildBody", BindingFlags.NonPublic | BindingFlags.Static);
        // 参数：finalOnly, requireTool, allowTools, preferCodex, characterRetry, turnRule
        string body = (string)bodyMethod.Invoke(null, new object[] { "角色设定", "确认的状态", history, "打开桌面的文件", null, exchanges, false, true, true, false, false, null });
        var shape = JsonUtility.FromJson<RequestShape>(body);
        Check(shape != null && shape.model == "deepseek-flash" && shape.tool_choice == "required", "required action tool");
        Check(shape.tools != null && shape.tools.Length == 2 && shape.tools[0].function.name == "computer_action" && shape.tools[1].function.name == "delegate_codex", "computer and Codex tools");
        Check(shape.messages != null && shape.messages.Length >= 3 && shape.messages[shape.messages.Length - 1].role == "user", "request messages");
        string chatBody = (string)bodyMethod.Invoke(null, new object[] { "角色设定", "", history, "今天过得怎么样？", null, exchanges, false, false, true, false, false, null });
        Check(JsonUtility.FromJson<RequestShape>(chatBody).tool_choice == "auto", "ordinary chat may choose tool");
        string codexBody = (string)bodyMethod.Invoke(null, new object[] { "角色设定", "", history, "这个项目编译不过", null, exchanges, false, true, true, true, false, null });
        Check(codexBody.Contains("\"tool_choice\":{\"type\":\"function\",\"function\":{\"name\":\"delegate_codex\"}}"), "classifier codex intent forces delegation");
        string retryBody = (string)bodyMethod.Invoke(null, new object[] { "角色设定", "", history, "别演了", null, exchanges, false, false, false, false, true, null });
        Check(retryBody.Contains("刚才的候选回复脱离了牧濑红莉栖的声音") && !retryBody.Contains("\"tools\""), "persona retry adds retry rule");
        Type router = runtime.GetType("KurisuCompanion.AgentRouter", true);
        MethodInfo shouldUseAgent = router.GetMethod("ShouldUseAgent", BindingFlags.Public | BindingFlags.Static);
        Check((bool)shouldUseAgent.Invoke(null, new object[] { "帮我打开桌面上的论文文件" }), "automatic computer routing");
        Check((bool)shouldUseAgent.Invoke(null, new object[] { "你能帮我打开浏览器吗" }), "natural computer routing");
        Check((bool)shouldUseAgent.Invoke(null, new object[] { "你看看我的电脑屏幕里面有什么" }), "screen request routing");
        Check((bool)shouldUseAgent.Invoke(null, new object[] { "帮我读一下刚才打开的页面" }), "page reading routing");
        Check(!(bool)shouldUseAgent.Invoke(null, new object[] { "今天过得怎么样？" }), "ordinary chat routing");
        Check(!(bool)shouldUseAgent.Invoke(null, new object[] { "我刚刚打开了一个文件" }), "narrative does not force action");

        Type tools = runtime.GetType("KurisuCompanion.AgentTools", true);
        MethodInfo execute = tools.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static);
        string workspace = (string)tools.GetProperty("WorkspacePath", BindingFlags.Public | BindingFlags.Static).GetValue(null);
        string relative = "agent-check-" + Guid.NewGuid().ToString("N") + ".txt";
        string absolute = Path.Combine(workspace, relative);
        try
        {
            string written = Execute(execute, "{\"action\":\"write\",\"path\":\"" + relative + "\",\"content\":\"AGENT_FILE_OK\"}");
            Check(written.Contains("已写入文件") && File.Exists(absolute), "write file");
            Check(Execute(execute, "{\"action\":\"read\",\"path\":\"" + relative + "\"}").Contains("AGENT_FILE_OK"), "read file");
            Check(Execute(execute, "{\"action\":\"list\"}").Contains(relative), "list files");
            Check(Execute(execute, "{\"action\":\"run\",\"command\":\"Write-Output AGENT_COMMAND_OK\"}").Contains("AGENT_COMMAND_OK"), "run command");
            string outside = JsonUtility.ToJson(new PathAction { action = "open", path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe") });
            Check(Execute(execute, outside, false).Contains("用户拒绝打开路径"), "external file approval");
            Check(Execute(execute, "{\"action\":\"web_search\",\"query\":\"memory neuroscience\"}").Contains("http"), "web search");
            Check(Execute(execute, "{\"action\":\"paper_search\",\"query\":\"memory neuroscience\"}").Contains("DOI:"), "paper search");
            Check(Execute(execute, "{\"action\":\"read_page\",\"url\":\"https://example.com\"}").Contains("Example Domain"), "read web page");
            Debug.Log("KURISU_AGENT_CHECKS_OK");
        }
        finally { if (File.Exists(absolute)) File.Delete(absolute); }
    }

    public static void RunLive()
    {
        Assembly runtime = typeof(KurisuApp).Assembly;
        Type keyVault = runtime.GetType("KurisuCompanion.KeyVault", true);
        string apiKey = (string)keyVault.GetMethod("Read", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.Log("KURISU_AGENT_LIVE_SKIPPED_NO_KEY");
            return;
        }

        Type client = runtime.GetType("KurisuCompanion.AgentClient", true);
        Type chatTurn = runtime.GetType("KurisuCompanion.ChatTurn", true);
        object history = Activator.CreateInstance(typeof(List<>).MakeGenericType(chatTurn));
        bool calledTool = false;
        string answer = null, error = null;
        Func<string, string, Action<bool>, IEnumerator> approval = (title, details, decide) => Approve(decide, false);
        IEnumerator request = (IEnumerator)client.GetMethod("Send", BindingFlags.Public | BindingFlags.Static).Invoke(null,
            new object[] { apiKey, "你是牧濑红莉栖。用中文回答。", "", history,
                "搜索记忆神经科学相关论文，给出一个 DOI。", null, approval,
                (Action<string>)(value => { if (value != null && value.Contains("正在执行：paper_search")) calledTool = true; }),
                (Action<string>)(value => answer = value), (Action<string>)(value => error = value), null, null, null });
        Pump(request);
        Check(error == null, "live API request: " + error);
        Check(calledTool, "live paper_search tool call");
        Check(!string.IsNullOrWhiteSpace(answer), "live final answer");
        Debug.Log("KURISU_AGENT_LIVE_OK");
    }

    private static string Execute(MethodInfo method, string arguments, bool allow = true)
    {
        string result = null;
        Func<string, string, Action<bool>, IEnumerator> approval = (title, details, decide) => Approve(decide, allow);
        IEnumerator operation = (IEnumerator)method.Invoke(null, new object[]
        {
            "computer_action", arguments, approval, (Action<string>)(value => result = value), null
        });
        Pump(operation);
        return result ?? "";
    }

    private static IEnumerator Approve(Action<bool> decide, bool allow)
    {
        decide(allow);
        yield break;
    }

    private static void Pump(IEnumerator iterator)
    {
        while (iterator.MoveNext())
        {
            if (iterator.Current is IEnumerator nested) Pump(nested);
            else if (iterator.Current is AsyncOperation operation)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                while (!operation.isDone)
                {
                    if (DateTime.UtcNow > deadline) throw new Exception("Agent tool operation timed out");
                    Thread.Sleep(20);
                }
            }
            else Thread.Sleep(20);
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Agent check failed: " + name);
    }
}
