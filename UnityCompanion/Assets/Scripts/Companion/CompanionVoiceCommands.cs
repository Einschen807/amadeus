using System.Text;

namespace KurisuCompanion
{
    internal static class CompanionVoiceCommands
    {
        // Returns true only for direct mode commands or the common "come keep me company" requests.
        public static bool TryParse(string text, out bool enable)
        {
            enable = false;
            var builder = new StringBuilder();
            foreach (char c in text ?? "") if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c)) builder.Append(c);
            string value = builder.ToString();
            if (value.Length > 60) return false;
            if (Has(value, "退出陪伴模式", "关闭陪伴模式", "结束陪伴模式", "回到普通模式", "返回普通模式", "停止陪伴模式"))
                return true;
            if (Has(value, "不要进入陪伴模式", "别进入陪伴模式", "别陪我", "不用陪我")) return false;
            if (Has(value, "进入陪伴模式", "开启陪伴模式", "打开陪伴模式", "切换到陪伴模式",
                "陪我打游戏", "陪我玩游戏", "陪我工作", "陪我写代码", "陪我学习", "陪我看视频", "陪我看电影", "陪我看动画"))
            { enable = true; return true; }
            return false;
        }

        private static bool Has(string text, params string[] phrases)
        {
            foreach (string phrase in phrases) if (text.Contains(phrase)) return true;
            return false;
        }
    }
}
