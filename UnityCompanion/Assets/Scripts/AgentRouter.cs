using System;

namespace KurisuCompanion
{
    // 三级路由：规则能确定的直接决定（零延迟）；规则拿不准时交给轻量意图分类（IntentClassifier）；
    // 生成后若模型仍声称"无法访问设备"，AgentClient 会再强制调用一次工具。
    internal enum RouteDecision { Chat, Computer, Codex, Ambiguous }

    internal static class AgentRouter
    {
        public static RouteDecision Decide(string text)
        {
            text = (text ?? "").Trim();
            if (CodexRouter.IsComplexRequest(text)) return RouteDecision.Codex;
            if (ShouldUseAgent(text)) return RouteDecision.Computer;
            // 提到电脑对象（文件、网页、屏幕……）却没有命中明确句式，例如"为什么这个网页打不开，帮我看看"。
            return MentionsComputerTarget(text) && !IsNarrative(text) ? RouteDecision.Ambiguous : RouteDecision.Chat;
        }

        // 用户在讲述已经发生的事，而不是下达指令。
        private static bool IsNarrative(string text)
        {
            if (IsDirectRequest(text)) return false;
            return text.Contains("我刚") || text.Contains("我昨天") || text.Contains("我之前") || text.Contains("我已经") ||
                   text.Contains("今天我") || text.EndsWith("了", StringComparison.Ordinal) || text.EndsWith("了。", StringComparison.Ordinal);
        }

        private static readonly string[] Verbs =
        {
            "打开浏览器", "打开文件", "打开目录", "打开文件夹", "打开网页", "打开网站",
            "查找论文", "搜索论文", "检索论文", "帮我搜索", "帮我查找", "帮我查", "帮我找", "上网查", "查一下网页",
            "创建文件", "生成文件", "写个程序", "做个程序", "做一个程序", "帮我制作",
            "运行程序", "运行命令", "执行命令", "帮我运行", "帮我执行", "读取文件", "查看文件",
            "修改文件", "编辑文件", "列出文件", "查看目录", "打开桌面", "搜索网页", "帮我打开", "帮我写",
            "替我打开", "替我查", "替我搜", "打开一下", "搜一下", "查一下论文", "查一下资料",
            "看看我的屏幕", "看看电脑屏幕", "看一下屏幕", "看一眼屏幕", "截一下屏", "分析我的屏幕",
            "读取页面", "阅读网页", "读一下页面", "读一下网页", "看看刚才打开的页面", "看看这个页面"
        };

        public static bool ShouldUseAgent(string text)
        {
            text = (text ?? "").Trim();
            if (text.Contains("如何") || text.Contains("怎么") || text.Contains("教程") || text.Contains("为什么"))
                return false;
            foreach (string verb in Verbs)
                if (text.IndexOf(verb, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string[] leadingVerbs = { "打开", "搜索", "查找", "检索", "运行", "执行", "创建", "制作", "读取", "列出", "修改", "帮我查", "帮我搜" };
            foreach (string verb in leadingVerbs)
                if (text.StartsWith(verb, StringComparison.OrdinalIgnoreCase)) return true;
            if (MentionsComputerTarget(text) && IsDirectRequest(text) &&
                (text.Contains("打开") || text.Contains("搜索") || text.Contains("查找") || text.Contains("运行") ||
                 text.Contains("创建") || text.Contains("制作") || text.Contains("读取") || text.Contains("查看") ||
                 text.Contains("看看") || text.Contains("读一下") || text.Contains("阅读") || text.Contains("截屏")))
                return true;
            return false;
        }

        public static bool IsDirectRequest(string text)
        {
            text = (text ?? "").Trim();
            return text.StartsWith("请", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("帮我", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("替我", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("你能", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("你看看", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("能不能", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("麻烦", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("可以帮我", StringComparison.OrdinalIgnoreCase);
        }

        public static bool MentionsComputerTarget(string text)
        {
            text = text ?? "";
            string[] targets = { "浏览器", "网页", "页面", "网站", "论文", "文件", "文件夹", "目录", "桌面", "屏幕", "画面",
                "程序", "命令", "电脑", "设备", "搜索", "资料", ".pdf", ".txt", ".docx", "http" };
            foreach (string target in targets)
                if (text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static bool LooksLikeNoAccessReply(string text)
        {
            text = text ?? "";
            string[] denials = { "没有连接你的设备", "没有连接你的电脑", "不能访问你的设备", "无法访问你的设备",
                "不能操作你的电脑", "无法操作你的电脑", "打不开你的浏览器", "无法打开你的浏览器",
                "没有能力打开", "无法直接打开", "不能直接打开" };
            foreach (string denial in denials)
                if (text.IndexOf(denial, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
