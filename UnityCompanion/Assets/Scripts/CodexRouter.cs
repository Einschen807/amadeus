using System;

namespace KurisuCompanion
{
    internal static class CodexRouter
    {
        // Only force unmistakable execution requests. The LLM can delegate other multi-step tasks via its tool schema.
        public static bool IsComplexRequest(string text)
        {
            string t = (text ?? "").Trim().ToLowerInvariant();
            if (t.Contains("不要") || t.Contains("不用") || t.Contains("先别") || t.Contains("不需要") || t.Contains("仅解释") || t.Contains("只解释")) return false;
            if (t.Contains("如何") || t.Contains("怎么") || t.Contains("原理") || t.Contains("是什么") || t.Contains("能否") || t.Contains("能不能") || t.Contains("可以吗") || t.Contains("是否")) return false;
            bool request = AgentRouter.IsDirectRequest(t) || Starts(t, "写", "做", "创建", "制作", "开发", "实现", "修改", "修复", "重构", "排查", "调试", "用codex", "用 codex", "让codex", "让 codex", "请codex", "请 codex", "build ", "fix ", "implement ");
            if (!request) return false;
            if (t.Contains("codex")) return true;
            bool action = Contains(t, "写", "做", "创建", "制作", "开发", "实现", "修改", "修复", "重构", "排查", "调试", "build", "fix", "implement");
            bool target = Contains(t, "程序", "代码", "项目", "网站", "网页应用", "小游戏", "游戏", "脚本", "插件", "编译", "bug", "报错", "自动化", "工具", "app", "application");
            return action && target;
        }
        private static bool Contains(string t, params string[] words) { foreach (var w in words) if (t.Contains(w)) return true; return false; }
        private static bool Starts(string t, params string[] words) { foreach (var w in words) if (t.StartsWith(w, StringComparison.Ordinal)) return true; return false; }
    }
}
