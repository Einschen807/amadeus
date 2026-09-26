using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;

namespace KurisuCompanion
{
    [Serializable]
    internal sealed class AgentArguments
    {
        public string action;
        public string query;
        public string url;
        public string path;
        public string content;
        public string command;
        public string working_directory;
    }

    internal static class AgentTools
    {
        public static string WorkspacePath => Path.Combine(Application.persistentDataPath, "AgentWorkspace");
        public static string LastOpenedUrl { get; set; }

        public static IEnumerator Execute(string name, string arguments,
            Func<string, string, Action<bool>, IEnumerator> askApproval,
            Action<string> onResult, Action<string> onImage = null)
        {
            AgentArguments args;
            try { args = JsonUtility.FromJson<AgentArguments>(arguments) ?? new AgentArguments(); }
            catch { onResult("工具参数不是有效 JSON。"); yield break; }

            if (name != "computer_action") { onResult("未知工具：" + name); yield break; }
            switch ((args.action ?? "").Trim().ToLowerInvariant())
            {
                case "web_search":
                    yield return SearchWeb(args.query, onResult);
                    break;
                case "paper_search":
                    yield return SearchPapers(args.query, onResult);
                    break;
                case "capture_screen":
                    try
                    {
                        string image = ScreenCaptureService.CapturePrimaryScreen();
                        onImage?.Invoke(image);
                        onResult("已抓取当前主屏幕。图像只随本次请求发送给模型，不写入对话记录或截图文件。");
                    }
                    catch (Exception e) { onResult("屏幕抓取失败：" + e.Message); }
                    break;
                case "read_page":
                {
                    string pageUrl = string.IsNullOrWhiteSpace(args.url) ? LastOpenedUrl : args.url.Trim();
                    yield return ReadPage(pageUrl, askApproval, onResult);
                    break;
                }
                case "open":
                {
                    if (!string.IsNullOrWhiteSpace(args.url) || (!string.IsNullOrWhiteSpace(args.query) && string.IsNullOrWhiteSpace(args.path)))
                    {
                        string opened = OpenBrowser(args);
                        if (opened.StartsWith("已在默认浏览器打开：", StringComparison.Ordinal))
                        {
                            string page = null;
                            yield return ReadPage(LastOpenedUrl, askApproval, value => page = value);
                            onResult(opened + "\n\n" + page);
                        }
                        else onResult(opened);
                        break;
                    }
                    string target;
                    bool outside;
                    try { target = ResolvePath(args.path, out outside); }
                    catch (Exception e) { onResult(e.Message); yield break; }
                    if (!File.Exists(target) && !Directory.Exists(target)) { onResult("路径不存在：" + target); yield break; }
                    bool approved = false;
                    yield return askApproval("打开本机路径", "将由 Windows 默认程序打开：\n" + target,
                        value => approved = value);
                    if (!approved) { onResult("用户拒绝打开路径。"); yield break; }
                    OpenPath(target, onResult);
                    break;
                }
                case "list":
                {
                    string target;
                    bool outside;
                    try { target = ResolvePath(args.path, out outside); }
                    catch (Exception e) { onResult(e.Message); yield break; }
                    if (outside)
                    {
                        bool approved = false;
                        yield return askApproval("查看本机目录", target, value => approved = value);
                        if (!approved) { onResult("用户拒绝查看目录。"); yield break; }
                    }
                    ListFiles(target, onResult);
                    break;
                }
                case "read":
                {
                    string target;
                    bool outside;
                    try { target = ResolvePath(args.path, out outside); }
                    catch (Exception e) { onResult(e.Message); yield break; }
                    if (outside)
                    {
                        bool approved = false;
                        yield return askApproval("读取本机文件", target, value => approved = value);
                        if (!approved) { onResult("用户拒绝读取文件。"); yield break; }
                    }
                    ReadFile(target, onResult);
                    break;
                }
                case "write":
                {
                    string target;
                    bool outside;
                    try { target = ResolvePath(args.path, out outside); }
                    catch (Exception e) { onResult(e.Message); yield break; }
                    if ((args.content ?? "").Length > 100000) { onResult("单个文件内容超过 100000 字符限制。"); yield break; }
                    if (outside || File.Exists(target))
                    {
                        bool allowed = false;
                        yield return askApproval(File.Exists(target) ? "覆盖已有文件" : "写入本机文件",
                            target + "\n\n" + (args.content ?? ""), value => allowed = value);
                        if (!allowed) { onResult("用户拒绝写入文件。"); yield break; }
                    }
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        string temporary = target + ".tmp";
                        File.WriteAllText(temporary, args.content ?? "", new UTF8Encoding(false));
                        if (File.Exists(target)) File.Replace(temporary, target, null);
                        else File.Move(temporary, target);
                        onResult("已写入文件：" + target);
                    }
                    catch (Exception e) { onResult("写入失败：" + e.Message); }
                    break;
                }
                case "run":
                {
                    string command = (args.command ?? "").Trim();
                    if (command.Length == 0 || command.Length > 2000) { onResult("命令为空或超过 2000 字符。"); yield break; }
                    string workingDirectory;
                    try
                    {
                        bool outside;
                        workingDirectory = string.IsNullOrWhiteSpace(args.working_directory) ? WorkspacePath :
                            ResolvePath(args.working_directory, out outside);
                    }
                    catch (Exception e) { onResult(e.Message); yield break; }
                    if (!Directory.Exists(workingDirectory)) { onResult("工作目录不存在：" + workingDirectory); yield break; }
                    bool allowed = false;
                    yield return askApproval("运行 PowerShell 命令", "工作目录：" + workingDirectory + "\n\n" + command,
                        value => allowed = value);
                    if (!allowed) { onResult("用户拒绝运行命令。"); yield break; }
                    yield return RunCommand(command, workingDirectory, onResult);
                    break;
                }
                default:
                    onResult("未知电脑操作：" + args.action);
                    break;
            }
        }

        private static string ResolvePath(string path, out bool outside)
        {
            path = (path ?? "").Trim();
            string root = Path.GetFullPath(WorkspacePath);
            string target = path.Length == 0 ? root : Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            outside = !target.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                      !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (outside && !Path.IsPathRooted(path))
                throw new InvalidOperationException("相对路径不能跳出 Agent 工作目录；请使用明确的绝对路径。");
            return target;
        }

        private static IEnumerator SearchWeb(string query, Action<string> onResult)
        {
            if (string.IsNullOrWhiteSpace(query)) { onResult("搜索词为空。"); yield break; }
            string url = "https://www.bing.com/search?format=rss&q=" + Uri.EscapeDataString(query.Trim());
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 20;
                request.SetRequestHeader("User-Agent", "KurisuCompanion/0.2");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onResult("网页搜索失败：" + request.error); yield break; }
                try
                {
                    var document = new XmlDocument(); document.LoadXml(request.downloadHandler.text);
                    var items = document.SelectNodes("/rss/channel/item");
                    var result = new StringBuilder("网页搜索结果（摘要可能不完整，需打开原文核对）：\n");
                    int count = Math.Min(5, items.Count);
                    for (int i = 0; i < count; i++)
                    {
                        XmlNode item = items[i];
                        result.Append(i + 1).Append(". ").Append(item.SelectSingleNode("title")?.InnerText).Append("\n")
                            .Append(item.SelectSingleNode("link")?.InnerText).Append("\n")
                            .Append(item.SelectSingleNode("description")?.InnerText).Append("\n");
                    }
                    onResult(count == 0 ? "未找到网页结果。" : result.ToString());
                }
                catch (Exception e) { onResult("无法解析搜索结果：" + e.Message); }
            }
        }

        [Serializable] private sealed class PaperResponse { public PaperMessage message; }
        [Serializable] private sealed class PaperMessage { public Paper[] items; }
        [Serializable] private sealed class Paper
        {
            public string DOI;
            public string URL;
            public string[] title;
            public PaperAuthor[] author;
            public string @abstract;
        }
        [Serializable] private sealed class PaperAuthor { public string given; public string family; }

        private static IEnumerator SearchPapers(string query, Action<string> onResult)
        {
            if (string.IsNullOrWhiteSpace(query)) { onResult("论文搜索词为空。"); yield break; }
            string url = "https://api.crossref.org/works?query.bibliographic=" + Uri.EscapeDataString(query.Trim()) +
                         "&rows=5&select=DOI,title,author,URL,abstract";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 20;
                request.SetRequestHeader("User-Agent", "KurisuCompanion/0.2 (research assistant)");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { onResult("论文检索失败：" + request.error); yield break; }
                try
                {
                    var data = JsonUtility.FromJson<PaperResponse>(request.downloadHandler.text);
                    if (data?.message?.items == null || data.message.items.Length == 0) { onResult("未找到论文元数据。"); yield break; }
                    var result = new StringBuilder("Crossref 论文元数据（标题与 DOI 已检索到；摘要需核对原文）：\n");
                    int index = 0;
                    foreach (var paper in data.message.items)
                    {
                        index++;
                        result.Append(index).Append(". ").Append(paper.title != null && paper.title.Length > 0 ? paper.title[0] : "无标题");
                        if (paper.author != null && paper.author.Length > 0)
                            result.Append(" — ").Append(paper.author[0].given).Append(' ').Append(paper.author[0].family);
                        result.Append("\nDOI: ").Append(paper.DOI).Append("\n链接: ").Append(paper.URL).Append('\n');
                        if (!string.IsNullOrWhiteSpace(paper.@abstract))
                        {
                            string plain = Regex.Replace(paper.@abstract, "<[^>]+>", " ");
                            result.Append("摘要: ").Append(plain.Substring(0, Math.Min(700, plain.Length))).Append('\n');
                        }
                    }
                    onResult(result.ToString());
                }
                catch (Exception e) { onResult("无法解析论文元数据：" + e.Message); }
            }
        }

        private static string OpenBrowser(AgentArguments args)
        {
            string value = !string.IsNullOrWhiteSpace(args.url) ? args.url.Trim() :
                "https://www.bing.com/search?q=" + Uri.EscapeDataString((args.query ?? "").Trim());
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { return "只能打开 http 或 https 网页。"; }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
                LastOpenedUrl = uri.AbsoluteUri;
                return "已在默认浏览器打开：" + uri.AbsoluteUri;
            }
            catch (Exception e) { return "打开浏览器失败：" + e.Message; }
        }

        private static IEnumerator ReadPage(string url,
            Func<string, string, Action<bool>, IEnumerator> askApproval, Action<string> onResult)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            { onResult("没有可读取的网页 URL。"); yield break; }
            for (int redirect = 0; redirect < 5; redirect++)
            {
                if (IsLocalAddress(uri))
                {
                    bool approved = false;
                    yield return askApproval("读取本机或内网网页", uri.AbsoluteUri, value => approved = value);
                    if (!approved) { onResult("用户拒绝读取内网页面。"); yield break; }
                }
                using (var request = UnityWebRequest.Get(uri.AbsoluteUri))
                {
                    request.timeout = 25;
                    request.redirectLimit = 0;
                    request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
                    yield return request.SendWebRequest();
                    if (request.responseCode >= 300 && request.responseCode < 400)
                    {
                        string location = request.GetResponseHeader("Location");
                        if (!Uri.TryCreate(uri, location, out Uri destination) ||
                            (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps))
                        { onResult("网页重定向地址无效。"); yield break; }
                        uri = destination;
                        continue;
                    }
                    if (request.result != UnityWebRequest.Result.Success)
                    { onResult("网页正文读取失败：" + request.error + "。浏览器可能已打开，但模型尚未读到页面。"); yield break; }
                    string mediaType = request.GetResponseHeader("Content-Type") ?? "";
                    if (mediaType.Length > 0 && mediaType.IndexOf("html", StringComparison.OrdinalIgnoreCase) < 0 &&
                        mediaType.IndexOf("text/plain", StringComparison.OrdinalIgnoreCase) < 0)
                    { onResult("页面返回的不是可读取的 HTML 或纯文本（" + mediaType + "）。"); yield break; }
                    string html = request.downloadHandler.text;
                    if (html.Length > 2000000) html = html.Substring(0, 2000000);
                    string text = mediaType.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        html.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ? HtmlToText(html) : html;
                    if (text.Length > 16000) text = text.Substring(0, 16000) + "\n[页面内容已截断]";
                    onResult("网页：" + uri.AbsoluteUri + "\n正文：\n" +
                        (string.IsNullOrWhiteSpace(text) ? "未提取到正文；这个网站可能需要 JavaScript 登录或动态加载。" : text));
                    yield break;
                }
            }
            onResult("网页重定向次数过多。");
        }

        private static bool IsLocalAddress(Uri uri)
        {
            string host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || host.EndsWith(".local") || host.IndexOf('.') < 0) return true;
            if (!IPAddress.TryParse(host, out IPAddress ip)) return false;
            if (IPAddress.IsLoopback(ip)) return true;
            byte[] bytes = ip.GetAddressBytes();
            return bytes.Length == 4 && (bytes[0] == 10 || bytes[0] == 127 ||
                bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 ||
                bytes[0] == 169 && bytes[1] == 254);
        }

        private static string HtmlToText(string html)
        {
            string text = Regex.Replace(html, "<!--.*?-->|<script\\b[^>]*>.*?</script>|<style\\b[^>]*>.*?</style>|<noscript\\b[^>]*>.*?</noscript>",
                " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "</?(p|div|h[1-6]|li|br|article|section|tr|blockquote)\\b[^>]*>", "\n",
                RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, "[\\t \\u00a0]+", " ");
            return Regex.Replace(text, "\\n[\\s\\n]*\\n+", "\n").Trim();
        }

        private static void ListFiles(string directory, Action<string> onResult)
        {
            try
            {
                if (directory.Equals(WorkspacePath, StringComparison.OrdinalIgnoreCase)) Directory.CreateDirectory(directory);
                if (!Directory.Exists(directory)) { onResult("目录不存在：" + directory); return; }
                var files = Directory.GetFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly);
                var output = new StringBuilder("目录：" + directory + "\n");
                int count = Math.Min(files.Length, 100);
                for (int i = 0; i < count; i++) output.Append(Path.GetFileName(files[i]))
                    .Append(Directory.Exists(files[i]) ? " /" : "").Append('\n');
                if (files.Length > count) output.Append("其余 ").Append(files.Length - count).Append(" 个文件未列出。\n");
                onResult(output.ToString());
            }
            catch (Exception e) { onResult("列出文件失败：" + e.Message); }
        }

        private static void ReadFile(string target, Action<string> onResult)
        {
            try
            {
                if (!File.Exists(target)) { onResult("文件不存在：" + target); return; }
                var info = new FileInfo(target);
                if (info.Length > 100000) { onResult("文件超过读取上限 100 KB。"); return; }
                string content = File.ReadAllText(target);
                onResult("文件：" + target + "\n" + content.Substring(0, Math.Min(20000, content.Length)));
            }
            catch (Exception e) { onResult("读取文件失败：" + e.Message); }
        }

        private static void OpenPath(string target, Action<string> onResult)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
                onResult("已由 Windows 打开：" + target);
            }
            catch (Exception e) { onResult("打开路径失败：" + e.Message); }
        }

        private static IEnumerator RunCommand(string command, string workingDirectory, Action<string> onResult)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process process = null;
            Task<string> stdout = null, stderr = null;
            try
            {
                process = Process.Start(info);
                stdout = process.StandardOutput.ReadToEndAsync();
                stderr = process.StandardError.ReadToEndAsync();
            }
            catch (Exception e) { process?.Dispose(); onResult("启动命令失败：" + e.Message); yield break; }
            float start = Time.realtimeSinceStartup;
            while (!process.HasExited && Time.realtimeSinceStartup - start < 120f) yield return null;
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { /* Process may have exited concurrently. */ }
                process.Dispose();
                onResult("命令超过 120 秒，已停止。");
                yield break;
            }
            while (!stdout.IsCompleted || !stderr.IsCompleted) yield return null;
            try
            {
                string output = stdout.Result + stderr.Result;
                if (output.Length > 12000) output = output.Substring(0, 12000) + "\n[输出已截断]";
                onResult("退出码：" + process.ExitCode + "\n" + output);
            }
            catch (Exception e) { onResult("读取命令结果失败：" + e.Message); }
            finally { process.Dispose(); }
        }
    }
}
