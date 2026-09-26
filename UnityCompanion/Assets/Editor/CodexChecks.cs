using System;
using System.Collections;
using System.IO;
using System.Threading;
using KurisuCompanion;
using UnityEngine;

public static class CodexChecks
{
    private static readonly string Python = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe");
    public static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex-integration"));
        string fixture = Path.Combine(root, "mock_server.py");
        var originalFactory = CodexService.CreateRpc;
        try
        {
            var obj = RpcJson.Obj("中文", "引号\"\\\n😀", "n", 123, "list", new object[] { true, null, "ja" });
            var decoded = RpcJson.Decode(RpcJson.Encode(obj));
            Check(RpcJson.Str(decoded, "中文") == "引号\"\\\n😀", "JSON Unicode and escaping");
            bool rejected = false; try { RpcJson.Decode("{\"x\":}"); } catch (FormatException) { rejected = true; }
            Check(rejected, "malformed JSON rejected");
            Check(CodexRouter.IsComplexRequest("帮我写一个小游戏并运行验证"), "coding delegates");
            Check(CodexRouter.IsComplexRequest("修复这个项目的编译报错"), "debugging delegates");
            Check(CodexRouter.IsComplexRequest("请用 Codex 继续上个任务"), "explicit delegation");
            Check(!CodexRouter.IsComplexRequest("帮我打开浏览器"), "simple operation stays local");
            Check(!CodexRouter.IsComplexRequest("如何写一个程序？"), "concept question stays chat");
            Check(!CodexRouter.IsComplexRequest("不要帮我修改代码"), "negative request stays chat");
            foreach (string scenario in new[] { "success", "deny", "failure", "disconnect", "cancel", "unauth" })
            {
                string testRoot = Path.Combine(root, "checks-" + scenario + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                CodexService.ResetForChecks(testRoot);
                CodexService.CreateRpc = (exe, cwd) => new CodexRpc(Python, root, "-X utf8 \"" + fixture + "\" " + scenario);
                CodexService.AskInput = (question, reply) => Answer(reply);
                int approvals = 0;
                Func<string, string, Action<bool>, IEnumerator> approve = (title, detail, reply) => Approve(reply, scenario != "deny" || title == "Codex 工作目录", () => approvals++);
                string output = null;
                Pump(CodexService.Run("Fixture task", Path.Combine(testRoot, "work"), false, "用户授权的测试任务", null, approve,
                    progress => { if (scenario == "cancel" && progress == "Codex 正在处理任务……") CodexService.Cancel(); }, value => output = value));
                Check(!CodexService.Busy, scenario + " releases busy state");
                Check(!string.IsNullOrEmpty(output), scenario + " returns a result");
                Debug.Log("CODEX_FIXTURE_RESULT=" + scenario + ": " + output);
                if (scenario == "success")
                {
                    Check(output.Contains("Codex 状态：完成") && output.Contains("FIXTURE_VERIFIED"), "success correlated to actual turn");
                    Check(!output.Contains("COMMENTARY_MUST_NOT_REPLACE_RESULT"), "final result separated from progress messages");
                    Check(approvals == 4, "directory, command, file and permission approvals");
                    Pump(CodexService.Run("Continue fixture", null, true, "继续上个任务", null, approve, p => { }, value => output = value));
                    Check(output.Contains("FIXTURE_VERIFIED") && CodexService.Records.Count == 2, "resume persisted thread");
                    CodexService.ResetForChecks(testRoot);
                    Check(CodexService.LastTask.threadId == "fixture-thread", "ledger survives reload");
                }
                if (scenario == "failure") Check(output.Contains("失败") && output.Contains("fixture model error"), "model failure preserved");
                if (scenario == "disconnect") Check(output.Contains("连接中断"), "disconnect not success");
                if (scenario == "cancel") Check(output.Contains("已停止"), "cancel waits for interrupted receipt");
                if (scenario == "deny") Check(output.Contains("拒绝"), "directory denial stops delegation");
                if (scenario == "unauth") Check(output.Contains("尚未登录") && CodexService.Records.Count == 0, "no run without login");
                Debug.Log("CODEX_CHECK_" + scenario.ToUpperInvariant() + "_OK");
            }
            Debug.Log("KURISU_CODEX_CHECKS_OK");
        }
        finally { CodexService.ResetForChecks(null); CodexService.CreateRpc = originalFactory; CodexService.AskInput = null; }
    }
    public static void CheckLocalConnection()
    {
        string result = null;
        try { Pump(CodexService.CheckConnection(value => result = value)); Debug.Log("CODEX_LOCAL_CONNECTION=" + result); }
        finally { CodexService.Shutdown(); }
    }
    public static void RunRoutingLive()
    {
        string apiKey = KeyVault.Read();
        if (string.IsNullOrWhiteSpace(apiKey)) { Debug.Log("CODEX_LIVE_ROUTE_SKIPPED_NO_DEEPSEEK_KEY"); return; }
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex-integration"));
        var originalFactory = CodexService.CreateRpc;
        try
        {
            CodexService.ResetForChecks(Path.Combine(root, "live-route-" + Guid.NewGuid().ToString("N")));
            CodexService.CreateRpc = (exe, cwd) => new CodexRpc(Python, root, "-X utf8 \"" + Path.Combine(root, "mock_server.py") + "\" success");
            CodexService.AskInput = (q, reply) => Answer(reply);
            string answer = null, error = null;
            Pump(AgentClient.Send(apiKey, RoleGuard.CoreRule, "", new System.Collections.Generic.List<ChatTurn>(),
                "帮我写一个 Python 加法程序，运行验证，并告诉我文件在哪里。请使用新的默认工作目录。", null,
                (title, detail, reply) => Approve(reply, true, () => { }), p => { }, value => answer = value, value => error = value));
            Check(error == null, "live DeepSeek routing error: " + error);
            Check(CodexService.Records.Count == 1 && CodexService.LastTask.status == "完成", "live DeepSeek chose Codex and got result");
            Check(!string.IsNullOrWhiteSpace(answer), "live character result returned");
            Debug.Log("CODEX_LIVE_ROUTE_OK (real DeepSeek, simulated Codex; no coding execution)");
        }
        finally { CodexService.ResetForChecks(null); CodexService.CreateRpc = originalFactory; CodexService.AskInput = null; }
    }
    public static void VerifyAndBuild()
    {
        Run();
        CheckLocalConnection();
        RunRoutingLive();
        RunCodexLive();
        ProjectSetup.BuildWindows();
    }
    public static void CheckAndBuild()
    {
        Run();
        ProjectSetup.BuildWindows();
    }
    public static void RunCodexLive()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.codex-integration/live-codex-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        string directory = Path.Combine(root, "work");
        try
        {
            CodexService.ResetForChecks(root);
            string result = null;
            string task = "这是 Amadeus 的最小接入验收。仅在当前工作目录创建 smoke.ps1，计算 2+3，并把结果 5 写入 result.txt；实际运行脚本并读回 result.txt 验证。不要安装依赖，不要读取其他项目或修改目录外文件。用中文简短报告运行结果。";
            Pump(CodexService.Run(task, directory, false, task, null,
                (title, details, reply) => Approve(reply, title == "Codex 工作目录" && details.Contains(directory), () => { }),
                progress => Debug.Log("CODEX_LIVE_PROGRESS=" + progress), value => result = value));
            Check(result != null && result.Contains("Codex 状态：完成"), "real Codex completion: " + result);
            Check(File.Exists(Path.Combine(directory, "smoke.ps1")), "real code artifact created");
            Check(File.ReadAllText(Path.Combine(directory, "result.txt")).Trim() == "5", "real script execution result");
            Debug.Log("CODEX_LIVE_EXECUTION_OK directory=" + directory);
        }
        finally { CodexService.ResetForChecks(null); }
    }
    private static IEnumerator Answer(Action<string> reply) { reply("A"); yield break; }
    private static IEnumerator Approve(Action<bool> reply, bool allow, Action count) { count(); reply(allow); yield break; }
    private static void Pump(IEnumerator iterator)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(180);
        try
        {
            while (iterator.MoveNext())
            {
                if (DateTime.UtcNow > end) throw new Exception("Codex check timeout");
                if (iterator.Current is IEnumerator nested) Pump(nested);
                else if (iterator.Current is AsyncOperation operation)
                {
                    while (!operation.isDone)
                    {
                        if (DateTime.UtcNow > end) throw new Exception("Codex async check timeout");
                        Thread.Sleep(10);
                    }
                }
                else Thread.Sleep(5);
            }
        }
        finally { (iterator as IDisposable)?.Dispose(); }
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Codex check failed: " + message); }
}
