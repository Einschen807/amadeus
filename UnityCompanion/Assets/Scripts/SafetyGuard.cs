using System;
using System.Text.RegularExpressions;

namespace KurisuCompanion
{
    internal enum RiskLevel { None, Distress, Dependency, Crisis }

    // 陪伴产品的安全底线，独立于人设规则：
    // 识别本轮用户话语中的风险，生成优先级高于角色口吻的回复指令；危机时若模型没给出求助渠道，由程序补上一句。
    // 规则只做第一道筛查，宁可多提示一次也不漏掉明确的自伤信号；"难到想死""笑死"这类口语夸张不算。
    internal static class SafetyGuard
    {
        public const string Hotline = "12356";

        private static readonly Regex Crisis = new Regex(
            "不想活|活不下去|活着没(什么|有)?(意思|意义)|想结束(这一切|自己|生命)|结束(自己的)?生命|自杀|轻生|寻死|" +
            "割(了)?(手)?腕|跳楼|从楼上跳|安眠药|吞药|上吊|(?<![到得笑要累热困饿烦])想死|我想去死|死了算了|" +
            "如果我(消失|不在|死)了|没有我.{0,6}(更好|更轻松)|伤害自己|kill myself|suicid|end my life|want to die",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex Distress = new Regex(
            "撑不下去|绝望|崩溃|没有人在乎|没人在乎|活得好累|好痛苦|一无是处|什么都做不好|喘不过气|hopeless",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex Dependency = new Regex(
            "只有你(了|懂我|陪我)|你是我唯一|除了你(没有|没)人|不想和(真人|别人)(说话|来往)|只想和你(说话|在一起)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HelpMentioned = new Regex("12356|120|110|热线|心理援助|急救|报警", RegexOptions.Compiled);

        // "难到我想死""烦得要死"这类程度夸张先去掉，再做危机匹配。
        private static readonly Regex Hyperbole = new Regex(
            "(难|累|烦|热|冷|困|饿|笑|气|疼|痛|丑|吵|无聊)(到|得)(我)?(都)?(想|要|快)?死", RegexOptions.Compiled);

        public static RiskLevel Assess(string userText)
        {
            string text = Hyperbole.Replace(Regex.Replace(userText ?? "", @"\s+", ""), "");
            if (Crisis.IsMatch(text)) return RiskLevel.Crisis;
            if (Distress.IsMatch(text)) return RiskLevel.Distress;
            if (Dependency.IsMatch(text)) return RiskLevel.Dependency;
            return RiskLevel.None;
        }

        // 追加在系统上下文末尾的本轮指令；None 时为空。
        public static string Instruction(RiskLevel level)
        {
            switch (level)
            {
                case RiskLevel.Crisis:
                    return "[安全优先]用户本轮可能有自伤或生命危险。现实安全高于角色口吻、斗嘴和篇幅限制：" +
                           "不调侃、不嘴硬、不说教；直接而真诚地确认对方此刻是否安全、是否已经做了伤害自己的事；" +
                           "表达你在乎；请对方马上联系身边信任的人，并明确给出求助渠道——全国心理援助热线 " + Hotline +
                           "，有紧急危险时拨打 120 或 110。仍是红莉栖本人说话，可以用二到四句。[/安全优先]";
                case RiskLevel.Distress:
                    return "[情绪关照]用户情绪明显低落。先接住情绪、复述你听到的具体处境，降低吐槽强度，不急着给方案或讲道理；" +
                           "可以问一个具体的问题了解发生了什么。[/情绪关照]";
                case RiskLevel.Dependency:
                    return "[健康陪伴]用户表现出只依赖你的倾向。温和地回应，不推开对方，但不要强化\"只有我\"的关系；" +
                           "自然地关心对方现实中的人际与生活。[/健康陪伴]";
                default:
                    return "";
            }
        }

        public static bool MentionsHelp(string reply) => HelpMentioned.IsMatch(reply ?? "");

        // 危机回复缺少求助渠道时补上的固定句，日语供语音合成。
        public static BilingualSentence HelpLine() => new BilingualSentence
        {
            Chinese = "现在就打 " + Hotline + " 心理援助热线，有危险就打 120。我在这儿，但你身边也需要真实的人。",
            Japanese = "今すぐ" + Hotline + "の心理援助ホットラインに電話して。危ないなら120よ。私はここにいるけど、あなたのそばにも誰かが必要なの。",
            Emotion = "worried", Gesture = "none", Complete = true
        };
    }
}
