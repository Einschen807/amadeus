using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace KurisuCompanion
{
    internal sealed class CodexRpc : IDisposable
    {
        private readonly Process process;
        private readonly ConcurrentDictionary<string, object> replies = new ConcurrentDictionary<string, object>();
        private readonly ConcurrentDictionary<string, bool> pending = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentQueue<object> events = new ConcurrentQueue<object>();
        private readonly object writeGate = new object();
        private int sequence;
        private volatile bool disposed;
        public string Error { get; private set; }
        public bool Alive => !disposed && !process.HasExited && Error == null;

        public CodexRpc(string executable, string workingDirectory, string arguments = "app-server --listen stdio://")
        {
            var info = new ProcessStartInfo
            {
                FileName = executable, Arguments = arguments, WorkingDirectory = workingDirectory,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            process = Process.Start(info) ?? throw new IOException("无法启动 Codex。");
            Task.Run(async () =>
            {
                try
                {
                    string line;
                    while (!disposed && (line = await process.StandardOutput.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        object message = RpcJson.Decode(line);
                        if (RpcJson.Get(message, "method") != null)
                        {
                            string method = RpcJson.Str(message, "method");
                            // Final items contain the complete text. Unused token/output deltas must not build up during approvals.
                            if (RpcJson.Get(message, "id") != null || !method.EndsWith("Delta", StringComparison.Ordinal) && !method.EndsWith("/delta", StringComparison.Ordinal)) events.Enqueue(message);
                        }
                        else
                        {
                            string key = RpcJson.Encode(RpcJson.Get(message, "id"));
                            if (pending.ContainsKey(key)) replies[key] = message;
                        }
                    }
                    if (!disposed) Error = "Codex 连接已关闭。";
                }
                catch (Exception e) { if (!disposed) Error = "Codex 通信失败：" + e.Message; }
            });
            // Drain diagnostics to prevent a full stderr pipe blocking the server. Never log credentials.
            Task.Run(async () => { try { while (!disposed && await process.StandardError.ReadLineAsync() != null) { } } catch { } });
        }

        public bool TryEvent(out object message) => events.TryDequeue(out message);
        public bool Write(object message)
        {
            try
            {
                lock (writeGate) { process.StandardInput.WriteLine(RpcJson.Encode(message)); process.StandardInput.Flush(); }
                return true;
            }
            catch (Exception e) { Error = "无法向 Codex 发送消息：" + e.Message; return false; }
        }
        public IEnumerator Request(string method, object parameters, Action<object> success, Action<string> failure, double timeout = 60)
        {
            string id = "amadeus-" + (++sequence);
            string key = RpcJson.Encode(id); pending[key] = true;
            try
            {
                if (!Write(RpcJson.Obj("id", id, "method", method, "params", parameters))) { failure(Error); yield break; }
                var deadline = DateTime.UtcNow.AddSeconds(timeout);
                while (true)
                {
                    if (replies.TryRemove(key, out object response))
                    {
                        object error = RpcJson.Get(response, "error");
                        if (error != null) failure(method + "：" + RpcJson.Str(error, "message"));
                        else success(RpcJson.Get(response, "result"));
                        yield break;
                    }
                    if (!Alive) { failure(Error ?? "Codex 进程已退出。"); yield break; }
                    if (DateTime.UtcNow > deadline) { failure("Codex 请求超时：" + method); yield break; }
                    yield return null;
                }
            }
            finally { pending.TryRemove(key, out _); replies.TryRemove(key, out _); }
        }
        public void Respond(object request, object result) => Write(RpcJson.Obj("id", RpcJson.Get(request, "id"), "result", result));
        public void RejectUnknown(object request) => Write(RpcJson.Obj("id", RpcJson.Get(request, "id"), "error",
            RpcJson.Obj("code", -32601, "message", "Amadeus does not support this request; no permission granted.")));
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { process.StandardInput.Close(); } catch { }
            Task.Run(() =>
            {
                try { if (!process.WaitForExit(2000)) process.Kill(); } catch { }
                finally { process.Dispose(); }
            });
        }
    }
}
